<script lang="ts">
  import type { JsonValue } from './api';
  import { combatPrepReconciliationReason, type UiState } from './controller';
  import { object } from './model';
  import { combatAtShare, combatExactZero, combatMetric, combatObserved, combatOs, combatPrepDraftError, combatPrepReady, combatPrepStartReason, combatResourceCost, combatRows, combatStats, combatSupportBudget, combatSupportMetrics, combatSupportUsage, combatText, combatTime, type CombatPrepDraft } from './combat-prep';
  import ActivityTiming from './ActivityTiming.svelte';
  import { parseActivityTiming } from './activity-timing';

  export let state: UiState;
  export let onedit: (key: keyof CombatPrepDraft, value: string) => void;
  export let onselect: (id: string) => void;
  export let onpreview: () => Promise<void>;
  export let onstart: () => Promise<void>;
  export let oncancel: () => Promise<void>;
  $: preview = state.combatPrepPreview;
  $: candidates = combatRows(preview?.candidates);
  $: runtime = object(state.snapshot?.runtime.combatPrep);
  $: planProgress = object(state.snapshot?.runtime.planProgress);
  $: reported = runtime.schemaVersion === 1;
  $: automatic = runtime.owner === 'scheduled';
  $: cleanupRecoveryRequired = runtime.cleanupRecoveryRequired === true || planProgress.cleanupRecoveryRequired === true || object(planProgress.combatPrep).cleanupRecoveryRequired === true;
  $: recoveryReason = combatPrepReconciliationReason(planProgress, runtime, object(state.snapshot?.runtime.resourceAllocation));
  $: cleanupPending = reported && runtime.cleanupPending === true;
  $: active = runtime.active === true || cleanupPending;
  $: actual = object(runtime.actual);
  $: wandoosMagicZero = combatExactZero(actual.wandoosMagicAllocation);
  $: connected = combatPrepReady(state.snapshot, state.runtimeDir, 'previewCombatPrep', state.now);
  $: activityTiming = connected && !cleanupRecoveryRequired ? parseActivityTiming(runtime) : null;
  $: validation = combatPrepDraftError(state.combatPrepDraft);
  $: startReason = combatPrepStartReason(preview, state.combatPrepSelectedId, state.combatPrepDraft, state.now);
  $: canCancel = reported && !cleanupRecoveryRequired && active && typeof runtime.runId === 'string' && !!runtime.runId.trim() && runtime.runId.length <= 128 && runtime.runId !== state.combatPrepUncertainRunId && combatPrepReady(state.snapshot, state.runtimeDir, 'cancelCombatPrep', state.now);
  const fields = [['durationSeconds', 'Total budget (seconds)', 1, 86400, '1'], ['backgroundEnergyPercent', 'Background energy (%)', 0, 100, 'any'], ['backgroundMagicPercent', 'Background magic (%)', 0, 100, 'any']] as const;
  const observedFields = [
    ['energyAtLevel', 'Energy AT level (AT-3)'], ['magicAtLevel', 'Magic AT level (AT-4)'],
    ['energyAtAllocation', 'Energy to energy AT (AT-3)'], ['magicAtAllocation', 'Energy to magic AT (AT-4)'],
    ['wandoosEnergyLevel', 'Wandoos energy level'], ['wandoosMagicLevel', 'Wandoos magic level'],
    ['wandoosEnergyAllocation', 'Wandoos energy allocation'], ['wandoosMagicAllocation', 'Wandoos magic allocation'],
    ['gold', 'Observed gold'], ['augmentIndex', 'Augment index (zero-based)'],
    ['augmentLevel', 'Augment level'], ['upgradeLevel', 'Upgrade level'],
    ['augmentEnergyAllocation', 'Energy to augment'], ['upgradeEnergyAllocation', 'Energy to upgrade']
  ] as const;
</script>

{#snippet supportDetails(value: JsonValue | undefined, observed: boolean)}
  {@const support = object(value)}
  <dl class="stats support-stats">
    <div><dt>Support policy</dt><dd>{support.policy === 'current' ? 'Current support' : support.policy === 'phase' ? 'Dedicated phase support' : 'Unavailable'}</dd></div>
    <div><dt>Beards</dt><dd>
      {#if Array.isArray(support.beards) && !support.beards.length}None
      {:else if combatRows(support.beards).length}<ul class="support-list">{#each combatRows(support.beards) as beard}<li>{combatText(beard.name)}</li>{/each}</ul>
      {:else}Unavailable{/if}
    </dd></div>
    <div><dt>Diggers</dt><dd>
      {#if Array.isArray(support.diggers) && !support.diggers.length}None
      {:else if combatRows(support.diggers).length}<ul class="support-list">{#each combatRows(support.diggers) as digger}<li>{combatText(digger.name)} / Level {combatText(digger.level)} / Drain {combatMetric(digger.drain)} gold / second</li>{/each}</ul>
      {:else}Unavailable{/if}
    </dd></div>
    <div><dt>{observed ? 'Share of gross GPS in use' : 'GPS budget limit'}</dt><dd>{observed ? combatSupportUsage(support.drainPerSecond, support.grossGoldPerSecond) : combatSupportBudget(support.budgetFraction)}</dd></div>
    {#each combatSupportMetrics as [key, label]}<div><dt>{label}</dt><dd>{combatMetric(support[key])}</dd></div>{/each}
  </dl>
{/snippet}

<article class="card span-all combat-prep" aria-label="Combat prep planner">
  <div class="card-header"><h2>Auto Augments + Wandoos</h2><span class="pill" class:on={connected}>{!connected ? 'Unavailable' : cleanupRecoveryRequired ? 'Reconciling' : cleanupPending ? 'Cleanup pending' : active ? 'Run active' : 'Planner ready'}</span></div>
  <p class="muted">Budget includes preparation.</p>
  <div class="planner-inputs">
    {#each fields as [key, label, min, max, step]}
      <label>{label}<input type="number" {min} {max} {step} value={state.combatPrepDraft[key]} disabled={state.busy} on:input={event => onedit(key, (event.currentTarget as HTMLInputElement).value)} /></label>
    {/each}
  </div>
  <div class="button-row budget-shortcuts" role="group" aria-label="Total budget shortcuts">
    {#each [['15m', '900'], ['1h', '3600'], ['2h', '7200'], ['24h', '86400']] as [label, seconds]}
      <button type="button" disabled={state.busy} on:click={() => onedit('durationSeconds', seconds)}>{label}</button>
    {/each}
  </div>
  {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
  {#if !connected}<p class="muted">Connect to an up-to-date injector to use the planner.</p>{/if}
  <div class="button-row">
    <button type="button" disabled={state.busy || !connected || !!validation} on:click={onpreview}>{preview ? 'Refresh preview' : 'Preview'}</button>
    <button type="button" class="primary" title={cleanupRecoveryRequired ? recoveryReason : cleanupPending ? 'Waiting for equipment and resource cleanup.' : startReason || undefined} disabled={state.busy || !!startReason || active || cleanupRecoveryRequired || !combatPrepReady(state.snapshot, state.runtimeDir, 'startCombatPrep', state.now)} on:click={onstart}>Start selected</button>
    <button type="button" class="danger" title={cleanupRecoveryRequired ? recoveryReason : undefined} disabled={state.busy || !canCancel} on:click={oncancel}>Cancel active run</button>
  </div>
  {#if startReason}<p class="muted" role="status">{startReason}</p>{/if}

  {#if preview}
    <h3>Alternatives</h3>
    <p class="muted">Generated {combatText(preview.generatedAt)}.</p>
    <dl class="stats compact-stats" aria-label="Preview baseline">{#each combatStats as [key, label]}<div><dt>Baseline {label.toLowerCase()}</dt><dd>{combatMetric(object(preview.baseline)[key])}</dd></div>{/each}</dl>
    <div class="candidates">
      {#each candidates as candidate}
        <section class="candidate" class:selected={candidate.id === state.combatPrepSelectedId} aria-label={combatText(candidate.label)}>
          <label class="candidate-choice"><input type="radio" name="combat-prep-candidate" value={String(candidate.id)} checked={candidate.id === state.combatPrepSelectedId} disabled={state.busy || candidate.available !== true} on:change={() => onselect(String(candidate.id))} /><strong>{combatText(candidate.label)}</strong>{#if candidate.id === preview.recommendedCandidateId}<span class="pill">Recommended</span>{/if}</label>
          {#if candidate.available !== true}<p class="muted">Unavailable{candidate.reason ? `: ${combatText(candidate.reason)}` : ''}</p>
          {:else if candidate.reason}<p class="muted">{combatText(candidate.reason)}</p>{/if}
          <dl class="stats compact-stats">
            <div><dt>OS</dt><dd>{combatOs(candidate.os)}</dd></div>
            <div><dt>Augments</dt><dd>{combatTime(candidate.augmentSeconds)}</dd></div>
            <div><dt>AT preparation</dt><dd>{combatTime(candidate.atSeconds)}</dd></div>
            <div><dt>Wandoos</dt><dd>{combatTime(candidate.wandoosSeconds)}</dd></div>
            <div><dt>Background / casual</dt><dd>{combatTime(candidate.backgroundSeconds)}</dd></div>
            <div><dt>AT energy share (AT-3 / AT-4)</dt><dd>{combatAtShare(candidate.atEnergyShare)}</dd></div>
            <div><dt>Augment index (zero-based)</dt><dd>{combatMetric(candidate.augmentIndex)}</dd></div>
            <div><dt>Gain factor</dt><dd>{combatMetric(candidate.gainFactor)}</dd></div>
            <div><dt>Energy allocation opportunity cost</dt><dd>{combatResourceCost(candidate, 'energy')}</dd></div>
            <div><dt>Magic allocation opportunity cost</dt><dd>{combatResourceCost(candidate, 'magic')}</dd></div>
            {#each combatStats as [key, label]}<div><dt>Projected {label.toLowerCase()}</dt><dd>{combatMetric(object(candidate.projected)[key])}</dd></div>{/each}
          </dl>
          <details><summary>Phases, gear, support &amp; level targets</summary>
            {#each combatRows(candidate.phases) as phase}
              <section class="phase" aria-label={combatText(phase.label)}>
                <h4>{combatText(phase.label)} / {combatTime(phase.seconds)}</h4>
                <dl class="stats compact-stats">
                  <div><dt>Phase kind</dt><dd>{combatText(phase.kind)}</dd></div>
                  <div><dt>Gear goal</dt><dd>{typeof phase.gearGoal === 'string' ? combatText(phase.gearGoal) : combatMetric(phase.gearGoal)}</dd></div>
                  <div><dt>Owned gear</dt><dd>{Array.isArray(phase.gearNames) && phase.gearNames.length ? phase.gearNames.map(String).join(', ') : 'Unavailable'}</dd></div>
                  <div><dt>Energy allocation</dt><dd>{combatText(phase.energyAllocation)}</dd></div>
                  <div><dt>Magic allocation</dt><dd>{combatText(phase.magicAllocation)}</dd></div>
                  <div><dt>AT energy target</dt><dd>{combatText(phase.atEnergyTarget)}</dd></div>
                  <div><dt>AT magic target</dt><dd>{combatText(phase.atMagicTarget)}</dd></div>
                </dl>
                <section aria-label="Planned phase support">
                  <h4>Planned phase support</h4>
                  {@render supportDetails(phase.support, false)}
                </section>
              </section>
            {:else}<p class="muted">Phase details unavailable.</p>{/each}
            <dl class="stats compact-stats">
              {#each [['energyAtLevel', 'Energy AT level'], ['magicAtLevel', 'Magic AT level'], ['wandoosEnergyLevel', 'Wandoos energy level'], ['wandoosMagicLevel', 'Wandoos magic level']] as [key, label]}<div><dt>Projected {label}</dt><dd>{combatText(object(candidate.projected)[key])}</dd></div>{/each}
              <div><dt>Projected gold remaining</dt><dd>{combatMetric(object(candidate.projected).goldRemaining)}</dd></div>
              <div><dt>Energy full-focus equivalent (Wandoos dump gain)</dt><dd>{combatTime(candidate.energyFocusEquivalentSeconds)}</dd></div>
              <div><dt>Magic full-focus equivalent (Wandoos dump gain)</dt><dd>{combatTime(candidate.magicFocusEquivalentSeconds)}</dd></div>
            </dl>
          </details>
        </section>
      {/each}
    </div>
    <details><summary>Model assumptions &amp; comparison</summary>
      <p class="muted">Wandoos magic waits for required AT targets. Policies without an AT gate, including Wish 190, skip this check. Estimates use owned gear and current beard/digger levels, comparing current and phase-specific support.</p>
      <p class="muted">Full-focus equivalents measure Wandoos gains; allocation opportunity costs measure resources used in reference seconds. Compare energy and magic separately; do not sum them. Unavailable values are not zero.</p>
      <ul>{#each Array.isArray(preview.assumptions) ? preview.assumptions : [] as assumption}<li>{String(assumption)}</li>{:else}<li>Assumptions unavailable.</li>{/each}</ul>
    </details>
  {/if}

  <section class="runtime" aria-label="Combat prep runtime">
    <h3>Run progress</h3>
    <p role="status">{!connected ? 'Disconnected; run status unavailable.' : cleanupRecoveryRequired ? recoveryReason : reported ? `Status: ${combatText(runtime.status)} / ${combatText(runtime.candidateLabel)} / Phase: ${combatText(runtime.phase)}` : 'Run status unavailable.'}</p>
    {#if reported}
      {#if cleanupPending}<p class="notice" role="status">Cleanup pending: combat prep still owns equipment and resources.</p>{/if}
      {#if !cleanupRecoveryRequired && typeof runtime.reason === 'string' && runtime.reason.trim()}<p class="muted">{runtime.reason}</p>{/if}
      <ActivityTiming timing={activityTiming} />
      <dl class="stats compact-stats">
        {#if automatic || runtime.owner === 'manual'}<div><dt>Run mode</dt><dd>{automatic ? 'Automatic plan stage' : 'Explicit planner run'}</dd></div>{/if}
        {#if typeof runtime.stageIndex === 'number' && Number.isSafeInteger(runtime.stageIndex) && runtime.stageIndex >= 0}<div><dt>Plan stage</dt><dd>{runtime.stageIndex + 1}</dd></div>{/if}
        {#if automatic && typeof runtime.planName === 'string' && runtime.planName.trim()}<div><dt>Plan</dt><dd>{runtime.planName}</dd></div>{/if}
        {#if automatic && runtime.deadlineRebirthSeconds !== undefined}<div><dt>Fixed deadline (rebirth elapsed)</dt><dd>{combatTime(runtime.deadlineRebirthSeconds)}</dd></div>{/if}
        <div><dt>Total budget</dt><dd>{combatTime(runtime.durationSeconds)}</dd></div>
        <div><dt>Elapsed</dt><dd>{combatTime(runtime.elapsedSeconds)}</dd></div>
        <div><dt>Remaining</dt><dd>{combatTime(runtime.remainingSeconds)}</dd></div>
        <div><dt>Phase elapsed</dt><dd>{combatTime(runtime.phaseElapsedSeconds)}</dd></div>
        <div><dt>Phase remaining</dt><dd>{combatTime(runtime.phaseRemainingSeconds)}</dd></div>
        <div><dt>AT targets verified</dt><dd>{runtime.atGateRequired === false ? 'Not applicable (no AT gate)' : runtime.atTargetsReached === true ? 'Yes' : runtime.atTargetsReached === false ? 'No' : 'Unavailable'}</dd></div>
        {#each combatStats as [key, label]}<div><dt>Actual {label.toLowerCase()}</dt><dd>{combatMetric(object(runtime.actual)[key])}</dd></div><div><dt>Run projected {label.toLowerCase()}</dt><dd>{combatMetric(object(runtime.projected)[key])}</dd></div>{/each}
      </dl>
      <div class="support-comparison" role="group" aria-label="Current phase support comparison">
        {#each [['plannedSupport', 'Planned current-phase support'], ['actualSupport', 'Actual observed support']] as [key, label]}
          <section class="support-panel" aria-label={label}>
            <h4>{label}</h4>
            {@render supportDetails(runtime[key], key === 'actualSupport')}
          </section>
        {/each}
      </div>
      <dl class="stats compact-stats" aria-label="Setup status">
        <div><dt>Support status</dt><dd>{combatText(runtime.supportStatus)}</dd></div>
        {#if typeof runtime.blockedReason === 'string' && runtime.blockedReason.trim()}
          <div><dt>{runtime.blockedDomain ? `${combatText(runtime.blockedDomain)} blocked` : 'Blocked'}</dt><dd>{runtime.blockedReason}</dd></div>
        {:else if typeof runtime.reason === 'string' && runtime.reason.trim()}
          <div><dt>Reason</dt><dd>{runtime.reason}</dd></div>
        {/if}
      </dl>
      <details aria-label="Observed training details"><summary>Observed levels, allocations &amp; rates</summary>
        <p class="muted">Both AT-3 (energy AT) and AT-4 (magic AT) consume energy, not magic.</p>
        {#if runtime.phase === 'AT' && runtime.atGateRequired !== false}
          {#if wandoosMagicZero === true}<p role="status">Wandoos magic held at zero</p>
          {:else if wandoosMagicZero === false}<p class="notice error" role="alert">Wandoos magic is not zero during AT.</p>
          {:else}<p class="notice" role="alert">Cannot verify Wandoos magic is zero during AT: allocation unavailable.</p>{/if}
        {/if}
        <dl class="stats compact-stats" aria-label="Observed training readings">
          {#each observedFields as [key, label]}<div><dt>{label}</dt><dd>{combatObserved(actual[key])}</dd></div>{/each}
          <div><dt>Wandoos energy levels / second</dt><dd>{combatMetric(actual.energyWandoosLevelsPerSecond)}</dd></div>
          <div><dt>Wandoos magic levels / second</dt><dd>{combatMetric(actual.magicWandoosLevelsPerSecond)}</dd></div>
        </dl>
      </details>
      {#if Array.isArray(runtime.warnings) && runtime.warnings.length}<ul aria-label="Runtime warnings">{#each runtime.warnings as warning}<li>{String(warning)}</li>{/each}</ul>{/if}
    {/if}
    {#if active && state.combatPrepUncertainRunId === runtime.runId}<p class="muted">Cancellation submitted. Waiting for confirmation.</p>{/if}
  </section>
</article>

<style>
  .combat-prep, .candidate, .runtime { min-width: 0; overflow-wrap: anywhere; }
  .planner-inputs { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; }
  .candidates { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 28rem), 1fr)); gap: 1rem; }
  .candidate { border: 1px solid var(--line); border-radius: 5px; padding: 1rem; }
  .candidate.selected { border-color: var(--accent, #6bbaa3); }
  .candidate-choice { display: flex; flex-wrap: wrap; align-items: center; gap: .65rem; }
  .candidate-choice input { width: auto; }
  .phase, .runtime { border-top: 1px solid var(--line); margin-top: 1rem; padding-top: .75rem; }
  .budget-shortcuts button { padding: .35rem .65rem; }
  .support-comparison { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1rem; margin-top: 1rem; }
  .support-panel { min-width: 0; border: 1px solid var(--line); border-radius: 5px; padding: .75rem; }
  .support-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .support-stats > div:nth-child(2), .support-stats > div:nth-child(3) { grid-column: 1 / -1; }
  .support-list { padding-left: 1.2rem; margin: 0; }
  dd { overflow-wrap: anywhere; min-width: 0; }
  @media (max-width: 600px) { .planner-inputs, .support-comparison { grid-template-columns: 1fr; } }
</style>
