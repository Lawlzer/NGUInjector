<script lang="ts">
  import type { JsonObject } from './api';
  import { object } from './model';
  import { combatPrepReconciliationReason } from './controller';
  import { parsePlanProgress, planProgressDuration, stageHistoryDetails, type PlanProgressStage } from './plan-progress';

  // data is runtime.planProgress; connected is the fresh heartbeat result.
  export let data: JsonObject;
  export let connected: boolean;
  export let onedit: ((stageIndex: number) => void) | undefined = undefined;

  $: view = parsePlanProgress(data, connected);
  $: combatPrep = object(data.combatPrep);
  $: cleanupPending = connected && combatPrep.schemaVersion === 1 && combatPrep.cleanupPending === true;
  $: cleanupRecoveryRequired = connected && (data.cleanupRecoveryRequired === true || combatPrep.cleanupRecoveryRequired === true);
  $: cleanupBlocked = cleanupPending || cleanupRecoveryRequired;
  $: recoveryReason = combatPrepReconciliationReason(data);
  $: active = view.activeStageIndex === null ? null : view.stages[view.activeStageIndex];
  $: statusLabel = cleanupRecoveryRequired ? 'Reconciling' : cleanupPending ? 'Cleanup pending' : view.status === 'running' ? 'Running' : view.status === 'paused' ? 'Paused' : view.status === 'interrupted' ? 'Interrupted' : view.status === 'disabled' ? 'Disabled' : 'Waiting';
  $: percent = view.stageProgress === null ? null : Math.round(view.stageProgress * 100);
  $: progressText = cleanupRecoveryRequired ? recoveryReason : cleanupPending ? 'Cleanup pending: combat prep still owns equipment and resources.' : active?.presetBlocker || `${statusLabel}${active ? `: ${active.name}` : ''}; ${percent === null ? 'progress not reported' : `${percent}% complete`}`;

  function stageLabel(stage: PlanProgressStage): string {
    return stage.state === 'current' ? cleanupBlocked ? statusLabel : view.status === 'running' ? view.scheduleComplete === true ? 'Sustaining' : 'Current' : statusLabel : stage.state === 'completed' ? 'Completed' : stage.state === 'skipped' ? 'Skipped' : stage.state === 'pending' ? 'Pending' : 'Not recorded';
  }
</script>

<section class="plan-progress" aria-label="Plan progression">
  <header>
    <div class="identity">
      <p class="eyebrow">Plan progression</p>
      <h2>{view.showWaitingPlanProgress ? 'Waiting for plan progress' : view.planName}</h2>
      {#if !view.showWaitingPlanProgress && (view.cycle !== null && view.cycle > 0 || view.repeating)}
        <p class="context">{view.cycle !== null && view.cycle > 0 ? `Repeat ${view.cycle}` : ''}{view.repeating ? `${view.cycle !== null && view.cycle > 0 ? ' / ' : ''}Repeat from ${view.loopStartIndex! + 1}` : ''}</p>
      {/if}
    </div>
    <span class="status" class:running={view.status === 'running' && !view.showWaitingPlanProgress && !cleanupBlocked}>{!cleanupBlocked && view.status === 'running' && view.scheduleComplete === true ? active ? 'Sustaining' : 'Schedule complete' : statusLabel}</span>
  </header>

  {#if cleanupRecoveryRequired}
    <p class="reason" role="status">{recoveryReason}</p>
  {/if}
  {#if cleanupPending}<p class="reason" role="status">Cleanup pending: combat prep still owns equipment and resources.</p>{/if}
  {#if view.showWaitingPlanProgress}
    <p class="reason" role="status">{view.reason}</p>
  {:else}
    {#if !cleanupRecoveryRequired && view.status !== 'running' && view.reason}<p class="reason" role="status">{view.reason}</p>{/if}
    {#if !cleanupBlocked && view.status === 'running' && data.bloodGoalsPrioritized === true && data.bloodReservedForIronPill !== true}
      <p class="reason" role="status" title="Spaghetti / Counterfeit Gold goals; energy and stage gear unchanged">Blood goal catch-up</p>
    {/if}
    <ol class="stages" style={`--stage-count: ${view.stages.length}`} aria-label="Active runtime plan stages">
      {#each view.stages as stage (stage.index)}
        <li class:completed={stage.state === 'completed'} class:current={stage.state === 'current' && view.status === 'running' && !cleanupBlocked} class:held={stage.state === 'current' && (view.status !== 'running' || cleanupBlocked)} class:loop-start={stage.loopStart} aria-current={stage.state === 'current' ? 'step' : undefined}>
          <div class="rail" aria-hidden="true"><span class="fill" style:width={`${stage.state === 'completed' ? 100 : stage.state === 'current' ? percent ?? 0 : 0}%`}></span><span class="point">{stage.index + 1}</span></div>
          <div class="stage-copy">
            <div class="stage-meta">
              <p class="stage-state">{stageLabel(stage)}{stage.loopStart ? ' / Loop start' : ''}</p>
               {#if stage.timingKind}<span class="timing-badge">{stage.automatic ? 'Total elapsed budget' : stage.timingKind === 'fixed' ? 'Fixed duration' : stage.timingKind === 'flexible' ? 'Flexible share' : 'AT targets'}</span>{/if}
            </div>
            {#if onedit}
              <button type="button" class="stage-name" aria-label={`Edit stage ${stage.index + 1}: ${stage.name}`} title={stageHistoryDetails(stage)} aria-describedby={`runtime-history-${stage.index}`} on:click={() => onedit?.(stage.index)}>{stage.name}</button>
            {:else}<h3 class="stage-name" title={stageHistoryDetails(stage)}>{stage.name}</h3>{/if}
            {#if stage.completionMode !== 'AT targets'}<p class="duration" title={stage.timingKind === 'flexible' ? `Assigned estimate${stage.flexWeight !== null ? ` / weight ${stage.flexWeight}` : ''}` : undefined}>{planProgressDuration(stage.durationSeconds)}{stage.timingKind === 'flexible' ? ' est.' : ''}{#if stage.timingKind === 'fixed' && stage.configuredDurationSeconds !== null && stage.configuredDurationSeconds !== stage.durationSeconds}{` / configured ${planProgressDuration(stage.configuredDurationSeconds)}`}{/if}</p>{/if}
            {#if stage.energy === 'Global weighted preset' && stage.magic === stage.energy && stage.gear === stage.energy}
              <p class="allocation">Energy, magic &amp; gear: preset settings</p>
            {:else}
              <p class="allocation"><abbr title="Energy">E</abbr> <span>{stage.energy}</span></p>
              <p class="allocation"><abbr title="Magic">M</abbr> <span>{stage.magic}</span></p>
              <p class="gear">Gear: {stage.gear}</p>
            {/if}
            {#if stage.targets}<p class="targets">AT: {stage.targets}</p>{/if}
            <details class="history"><summary>Runtime history</summary><p id={`runtime-history-${stage.index}`}>{stageHistoryDetails(stage)}</p></details>
          </div>
        </li>
      {/each}
    </ol>
    {#if active}
      <div class="current-summary">
         <p><strong>{statusLabel === 'Running' ? view.scheduleComplete === true ? 'Sustaining' : 'Current' : statusLabel}: {active.index + 1}. {active.name}</strong>{#if percent !== null || view.stageElapsedSeconds !== null}<span>{percent !== null ? `${percent}%` : ''}{percent !== null && view.stageElapsedSeconds !== null ? ' / ' : ''}{view.stageElapsedSeconds !== null ? `${planProgressDuration(view.stageElapsedSeconds)} ${active.automatic ? 'elapsed budget used' : active.timingKind === 'fixed' ? 'credited in stage' : active.timingKind === 'flexible' ? 'wall time in stage' : 'in stage'}` : ''}</span>{/if}</p>
        <div class="stage-progress" role="progressbar" aria-label={`Stage ${active.index + 1} progress`} aria-valuemin="0" aria-valuemax="100" aria-valuenow={percent ?? undefined} aria-valuetext={progressText}><span style:width={`${percent ?? 0}%`}></span></div>
        {#if !cleanupBlocked && active.presetBlocker}<p class="reason" role="status">{active.presetBlocker}</p>{/if}
      </div>
    {/if}
    {#if view.elapsedSeconds !== null}<footer>{planProgressDuration(view.elapsedSeconds)} into this rebirth</footer>{/if}
  {/if}
</section>

<style>
  .plan-progress { grid-column: 1 / -1; min-width: 0; max-width: 100%; padding: 1.15rem; border: 1px solid var(--line, #363c44); border-radius: 5px; background: var(--paper, #191c20); color: #e4e7ed; }
  header { display: flex; justify-content: space-between; align-items: start; gap: .75rem; }
  .identity { min-width: 0; }
  .eyebrow { margin: 0 0 .2rem; color: var(--muted, #a6afb9); font-size: .68rem; letter-spacing: .1em; text-transform: uppercase; }
  h2 { margin: 0; font-size: 1.05rem; }
  .context, footer { color: var(--muted, #a6afb9); font-size: .75rem; }
  .context { margin: .3rem 0 0; }
  .status { flex: 0 0 auto; padding: .2rem .5rem; border: 1px solid var(--line, #363c44); border-radius: 3px; font-size: .72rem; }
  .status.running { color: #101113; background: var(--accent, #d5dbe3); }
  .reason { margin: .65rem 0 0; color: var(--muted, #a6afb9); font-size: .82rem; }
  .stages { display: grid; grid-template-columns: repeat(var(--stage-count), minmax(0, 1fr)); list-style: none; margin: 1rem 0 0; padding: 0; }
  li { min-width: 0; padding-top: .7rem; }
  .rail { position: relative; height: 2px; background: var(--line, #363c44); }
  .fill { display: block; height: 100%; background: #77818e; }
  .point { position: absolute; left: .5rem; top: 50%; display: grid; place-items: center; width: 1.4rem; height: 1.4rem; transform: translateY(-50%); border: 1px solid #65707d; border-radius: 50%; background: var(--paper, #191c20); color: #b8c1cc; font: .68rem Consolas, monospace; }
  .completed .point { background: #414953; color: #e4e7ed; }
  .current .point { border-color: var(--accent, #d5dbe3); background: var(--accent, #d5dbe3); color: #101113; box-shadow: 0 0 0 3px #191c20, 0 0 0 4px #8995a3; }
  .current .fill { background: var(--accent, #d5dbe3); }
  .held .point { border-style: dashed; color: #e4e7ed; }
  .stage-copy { min-width: 0; margin-top: 1rem; padding: .35rem .5rem .5rem; border-left: 2px solid transparent; }
  .current .stage-copy { background: #242a31; border-left-color: var(--accent, #d5dbe3); }
  .held .stage-copy { border-left-color: #87919e; border-left-style: dashed; }
  .loop-start .rail::before { position: absolute; top: -.5rem; bottom: -.5rem; left: 0; border-left: 1px dashed #a6afb9; content: ''; }
  .stage-meta { display: flex; flex-wrap: wrap; align-items: center; gap: .25rem .4rem; margin-bottom: .35rem; }
  .stage-state { margin: 0; color: #a6afb9; font-size: .65rem; line-height: 1.4; }
  .current .stage-state { color: #e4e7ed; }
  .stage-name { display: block; width: 100%; margin: 0; padding: 0; border: 0; border-radius: 0; color: #e4e7ed; background: transparent; font: 600 .8rem/1.4 "Segoe UI", system-ui, sans-serif; text-align: left; }
  button.stage-name { cursor: pointer; text-decoration: underline dotted #87919e; text-underline-offset: .2em; }
  button.stage-name:hover { color: white; background: #323941; }
  .duration { margin: .35rem 0; color: #c5cdd6; font-size: .72rem; line-height: 1.45; }
  .timing-badge { display: inline-block; padding: .15rem .35rem; border: 1px solid #65707d; border-radius: 3px; color: #c5cdd6; font-size: .65rem; }
  .allocation { display: flex; gap: .35rem; margin: .2rem 0; color: #b9c3cf; font-size: .74rem; line-height: 1.45; }
  abbr { flex: 0 0 .7rem; color: #e4e7ed; font-family: Consolas, monospace; text-decoration: none; }
  .gear, .targets { margin: .3rem 0 0; color: #a6afb9; font-size: .7rem; line-height: 1.45; }
  .history { margin-top: .4rem; font-size: .72rem; color: var(--muted, #a6afb9); }
  .history p { white-space: pre-line; line-height: 1.6; }
  .current-summary { margin-top: .7rem; padding-top: .7rem; border-top: 1px solid var(--line, #363c44); }
  .current-summary p { display: flex; flex-wrap: wrap; justify-content: space-between; gap: .3rem .7rem; margin: 0 0 .45rem; font-size: .78rem; }
  .current-summary p span { color: #a6afb9; }
  .stage-progress { height: 3px; background: var(--line, #363c44); }
  .stage-progress > span { display: block; height: 100%; background: var(--accent, #d5dbe3); }
  footer { margin-top: .7rem; line-height: 1.5; }
  p, h2, .stage-name, span, footer { overflow-wrap: anywhere; }
  @media (max-width: 900px) { .stages { grid-template-columns: repeat(4, minmax(0, 1fr)); row-gap: .7rem; } }
  @media (max-width: 550px) { .plan-progress { padding: .85rem; } .stages { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
