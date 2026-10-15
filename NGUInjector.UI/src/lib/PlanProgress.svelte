<script lang="ts">
  import type { JsonObject } from './api';
  import { parsePlanProgress, planProgressDuration, type PlanProgressStage } from './plan-progress';

  // data is runtime.planProgress; connected is the fresh heartbeat result.
  export let data: JsonObject;
  export let connected: boolean;
  export let onedit: ((stageIndex: number) => void) | undefined = undefined;

  $: view = parsePlanProgress(data, connected);
  $: active = view.activeStageIndex === null ? null : view.stages[view.activeStageIndex];
  $: statusLabel = view.status === 'running' ? 'Running' : view.status === 'paused' ? 'Paused' : view.status === 'interrupted' ? 'Interrupted' : view.status === 'disabled' ? 'Disabled' : 'Waiting';
  $: percent = view.stageProgress === null ? null : Math.round(view.stageProgress * 100);
  $: progressText = `${statusLabel}${active ? `: ${active.name}` : ''}; ${percent === null ? 'progress not reported' : `${percent}% complete`}`;

  function stageLabel(stage: PlanProgressStage): string {
    return stage.state === 'current' ? view.status === 'running' ? 'Current' : statusLabel : stage.state === 'completed' ? 'Completed' : stage.state === 'future' ? 'Upcoming' : 'Not active';
  }
</script>

<section class="plan-progress" aria-label="Plan progression">
  <header>
    <div class="identity">
      <p class="eyebrow">Plan progression</p>
      <h2>{view.showWaitingPlanProgress ? 'Waiting for plan progress' : view.planName}</h2>
      {#if !view.showWaitingPlanProgress}
        <p class="context">{view.schedulerMode === 'sequential' ? 'Sequential' : 'Time schedule'}{view.cycle !== null && view.cycle > 0 ? ` / Repeat ${view.cycle}` : ''}{view.repeating ? ` / Repeat from ${view.loopStartIndex! + 1}` : ''}</p>
      {/if}
    </div>
    <span class="status" class:running={view.status === 'running' && !view.showWaitingPlanProgress}>{statusLabel}</span>
  </header>

  {#if view.showWaitingPlanProgress}
    <p class="reason" role="status">{view.reason}</p>
  {:else}
    {#if view.reason}<p class="reason" role="status">{view.reason}</p>{/if}
    <ol class="stages" style={`--stage-count: ${view.stages.length}`} aria-label="Active runtime plan stages">
      {#each view.stages as stage (stage.index)}
        <li class:completed={stage.state === 'completed'} class:current={stage.state === 'current' && view.status === 'running'} class:held={stage.state === 'current' && view.status !== 'running'} class:loop-start={stage.loopStart} aria-current={stage.state === 'current' ? 'step' : undefined}>
          <div class="rail" aria-hidden="true"><span class="fill" style:width={`${stage.state === 'completed' ? 100 : stage.state === 'current' ? percent ?? 0 : 0}%`}></span><span class="point">{stage.index + 1}</span></div>
          <div class="stage-copy">
            <p class="stage-state">{stageLabel(stage)}{stage.loopStart ? ' / Loop start' : ''}</p>
            {#if onedit}
              <button type="button" class="stage-name" aria-label={`Edit stage ${stage.index + 1}: ${stage.name}`} on:click={() => onedit?.(stage.index)}>{stage.name}</button>
            {:else}<h3 class="stage-name">{stage.name}</h3>{/if}
            <p class="duration">{stage.completionMode === 'AT targets' ? 'AT targets' : planProgressDuration(stage.durationSeconds)}</p>
            <p class="allocation"><abbr title="Energy">E</abbr> <span>{stage.energy}</span></p>
            <p class="allocation"><abbr title="Magic">M</abbr> <span>{stage.magic}</span></p>
            <p class="gear">Gear: {stage.gear}</p>
            {#if stage.targets}<p class="targets">AT: {stage.targets}</p>{/if}
          </div>
        </li>
      {/each}
    </ol>
    {#if active}
      <div class="current-summary">
        <p><strong>{statusLabel === 'Running' ? 'Current' : statusLabel}: {active.index + 1}. {active.name}</strong><span>{percent === null ? 'Progress not reported' : `${percent}%`}{view.stageElapsedSeconds !== null ? ` / ${planProgressDuration(view.stageElapsedSeconds)} in stage` : ''}</span></p>
        <div class="stage-progress" role="progressbar" aria-label={`Stage ${active.index + 1} progress`} aria-valuemin="0" aria-valuemax="100" aria-valuenow={percent ?? undefined} aria-valuetext={progressText}><span style:width={`${percent ?? 0}%`}></span></div>
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
  .stage-state { margin: 0 0 .35rem; color: #a6afb9; font-size: .65rem; line-height: 1.4; }
  .current .stage-state { color: #e4e7ed; }
  .stage-name { display: block; width: 100%; margin: 0; padding: 0; border: 0; border-radius: 0; color: #e4e7ed; background: transparent; font: 600 .8rem/1.4 "Segoe UI", system-ui, sans-serif; text-align: left; }
  button.stage-name { cursor: pointer; text-decoration: underline dotted #87919e; text-underline-offset: .2em; }
  button.stage-name:hover { color: white; background: #323941; }
  .duration { margin: .35rem 0; color: #c5cdd6; font-size: .72rem; line-height: 1.45; }
  .allocation { display: flex; gap: .35rem; margin: .2rem 0; color: #b9c3cf; font-size: .74rem; line-height: 1.45; }
  abbr { flex: 0 0 .7rem; color: #e4e7ed; font-family: Consolas, monospace; text-decoration: none; }
  .gear, .targets { margin: .3rem 0 0; color: #a6afb9; font-size: .7rem; line-height: 1.45; }
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
