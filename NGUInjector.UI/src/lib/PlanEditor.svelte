<script lang="ts">
  import { onDestroy, tick } from 'svelte';
  import { ask } from '@tauri-apps/plugin-dialog';
  import type { JsonObject, JsonValue, PlanStageAction } from './api';
  import { object, validatePlan, INT_MAX } from './model';
  import RebirthConditions from './RebirthConditions.svelte';
  import StageEditor from './StageEditor.svelte';
  import { fixedSeconds, formatTime, parseDurationText, scheduleTiming } from './stage-duration';
  import { stageLabel, weightedStage } from './stage-presets';
  import { globalPresetRows, presetSupportsAtTargets } from './presets';

  export let plan: JsonObject;
  export let presetLibrary: JsonObject | null = null;
  export let name: string;
  export let dirty: boolean;
  export let progress: JsonObject = {};
  export let connected = false;
  export let disabled = false;
  export let stageActionReason = 'Manual stage controls are unavailable.';
  export let onstageaction: ((action: PlanStageAction, stageIndex: number, expectedEntry: string) => Promise<void>) | undefined = undefined;
  export let activePlanName = '';
  export let namespace = '';
  export let onchange: (plan: JsonObject) => void;
  export let ondirty: (dirty: boolean) => void = () => {};
  let actionError = '';
  let scheduleDraft: string | null = null;
  let scheduleError = '';
  let scheduleReady = false;
  let scheduleTimer: ReturnType<typeof setTimeout> | undefined;
  let scheduleScope = '';
  let stageDraftDirty = false;
  let rebirthDraftDirty = false;
  $: rows = Array.isArray(plan.Stages) ? plan.Stages.map(object) : [];
  $: if (scheduleScope !== JSON.stringify([namespace, name, plan.Name ?? name])) {
    scheduleScope = JSON.stringify([namespace, name, plan.Name ?? name]);
    cancelSchedule();
    actionError = '';
  }
  $: if (scheduleReady && !disabled) saveSchedule();
  $: validation = planError(plan);
  $: budget = readBudget(plan);
  $: localDraftDirty = stageDraftDirty || rebirthDraftDirty || scheduleDraft !== null;
  $: ondirty(localDraftDirty);
  onDestroy(() => clearTimeout(scheduleTimer));

  function planError(value: JsonObject): string {
    try { validatePlan(value); return ''; } catch (err) { return String((err as Error).message); }
  }
  function readBudget(value: JsonObject) {
    try {
      const timing = scheduleTiming(value);
      return { ...timing, fixedTotal: (value.Stages as JsonObject[]).reduce((sum, row) => sum + (row.CompletionMode === 1 ? 0 : fixedSeconds(row)), 0) };
    } catch { return null; }
  }
  const value = (event: Event) => (event.currentTarget as HTMLInputElement).value;
  function numericInput(event: Event): JsonValue {
    const text = value(event);
    return text.trim() && Number.isFinite(Number(text)) ? Number(text) : text;
  }
  function set(key: string, value: JsonValue): void { onchange({ ...plan, [key]: value }); }
  function scheduleSeconds(text: string): number {
    if (/\.\s*(?:%|s|m|min|h)?$/i.test(text)) throw new Error('Finish the decimal value before saving.');
    const { amount, unit } = parseDurationText(text, 'min');
    const rawSeconds = amount * (unit === 'h' ? 3600 : unit === 'min' ? 60 : 1);
    const seconds = Math.round(rawSeconds);
    if (unit === '%' || !Number.isInteger(seconds) || Math.abs(seconds - rawSeconds) > Math.max(1, Math.abs(rawSeconds)) * Number.EPSILON * 8 || seconds < 0 || seconds > INT_MAX) throw new Error('Time budget must be a non-negative whole-second time, such as 0s, 30m, or 2h.');
    scheduleTiming({ ...plan, StageDurationSeconds: seconds });
    return seconds;
  }
  function cancelSchedule(): void {
    clearTimeout(scheduleTimer);
    scheduleReady = false;
    scheduleDraft = null;
    scheduleError = '';
  }
  function changeSchedule(text: string): void {
    if (disabled) return;
    clearTimeout(scheduleTimer);
    scheduleReady = false;
    scheduleDraft = text;
    scheduleError = '';
    try {
      scheduleSeconds(text);
      const scope = scheduleScope;
      scheduleTimer = setTimeout(() => { if (scope === scheduleScope) scheduleReady = true; }, 350);
    } catch (err) { scheduleError = (err as Error).message; }
  }
  function saveSchedule(): void {
    if (scheduleDraft === null) return;
    clearTimeout(scheduleTimer);
    if (disabled) { scheduleReady = true; return; }
    scheduleReady = false;
    try {
      const seconds = scheduleSeconds(scheduleDraft);
      set('StageDurationSeconds', seconds);
      cancelSchedule();
    } catch (err) { scheduleError = (err as Error).message; }
  }
  async function blurSchedule(event: FocusEvent): Promise<void> {
    const input = event.currentTarget as HTMLInputElement, scope = scheduleScope;
    await tick();
    if (input.isConnected && document.activeElement !== input && scope === scheduleScope) saveSchedule();
  }
  async function useTemplate(advancedTraining: boolean): Promise<void> {
    const before = plan;
    actionError = '';
    try {
      if (!globalPresetRows(presetLibrary).some(row => row.Id === plan.ActivePresetId)) throw new Error('Choose an active saved global preset in Misc > Gear settings before applying a weighted template.');
      if (!await ask('Replace all stages? Current stage settings will be replaced and saved automatically.', { title: 'Replace stages', kind: 'warning' })) return;
      if (plan !== before || disabled) return;
      const training = advancedTraining ? globalPresetRows(presetLibrary).find(presetSupportsAtTargets) : undefined;
      if (advancedTraining && !training) throw new Error('Add and save a global preset with Advanced Training weight and positive toughness/power ratios before applying this template.');
      const common = weightedStage();
      const next = advancedTraining ? [
        weightedStage({ ...common, Name: String(training!.Name), PresetId: String(training!.Id), Percent: 100, CompletionMode: 1, TargetAdvancedTrainingPower: '6000', TargetAdvancedTrainingToughness: '6000' }),
        weightedStage({ ...common, Percent: 100 })
      ] : [
        300, 180, 180, 60, 180, 300, 300, 300
      ].map(seconds => weightedStage({ ...common, Percent: seconds / 18, DurationSeconds: seconds, DurationUnit: 'min' }));
      const template = { ...plan, Stages: next, StageSchedulerEnabled: true, StageDurationSeconds: advancedTraining ? plan.StageDurationSeconds ?? 3600 : 1800, StageLoopStartIndex: advancedTraining ? -1 : 5 };
      if (advancedTraining && scheduleTiming(template).total === 0) {
        throw new Error('AT Adventure to NGU requires a positive time budget. Enable a positive time trigger or set a positive fallback time budget before applying this template.');
      }
      cancelSchedule();
      onchange(template);
    } catch (err) { actionError = (err as Error).message; }
  }
</script>

<div class="plan-main" inert={disabled}>
  <RebirthConditions {plan} {disabled} {onchange} namespace={`${namespace}/${name}`} ondirty={dirty => rebirthDraftDirty = dirty} />
  <article class="card schedule-card">
    {#if activePlanName && activePlanName !== name}<p class="muted">Editing {name}; {activePlanName} is active.</p>{/if}
    {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
    {#if actionError}<p class="notice error" role="alert">{actionError}</p>{/if}
    {#if budget}
      {#if budget.fixedTotal > budget.total}<p class="notice" role="status">Fixed stages exceed budget by {formatTime(budget.fixedTotal - budget.total)}. Increase the budget or shorten fixed stages.</p>{:else if budget.remaining === 0 && rows.some(row => row.CompletionMode !== 1 && !fixedSeconds(row))}<p class="notice" role="status">No time for flexible stages. Increase the budget or shorten fixed stages.</p>{/if}
    {/if}
    <StageEditor {plan} {presetLibrary} {name} {progress} {connected} dirty={dirty || localDraftDirty} {disabled} {stageActionReason} {onstageaction} {onchange} ondirty={dirty => stageDraftDirty = dirty} namespace={`${namespace}/${name}/${String(plan.Name ?? name)}`} />
    <label class="toggle-row compact" title="Activate affordable fruits during plan or adventure automation when this plan's rebirth time or schedule is at least 1 hour."><input type="checkbox" aria-label="Activate fruits" checked={plan.AutoActivateFruits === true} on:change={event => set('AutoActivateFruits', (event.currentTarget as HTMLInputElement).checked)} /><span><strong>Activate fruits</strong><small>For rebirths of at least 1 hour</small></span></label>
    <details class="schedule-settings"><summary>Schedule &amp; templates</summary>
      <div class="form-grid three">
        <label class="toggle-row compact"><input type="checkbox" checked={plan.StageSchedulerEnabled === true} on:change={event => set('StageSchedulerEnabled', (event.currentTarget as HTMLInputElement).checked)} /><span><strong>Enable stages</strong></span></label>
        <label title="Used when no positive rebirth time trigger is enabled.">Fallback time budget<input aria-label="Fallback time budget" value={scheduleDraft ?? formatTime(Number(plan.StageDurationSeconds ?? 1800))} aria-invalid={!!scheduleError} aria-describedby={scheduleError ? 'schedule-duration-error' : undefined} on:input={event => changeSchedule(value(event))} on:blur={blurSchedule} on:keydown={event => { if (event.key === 'Enter') { event.preventDefault(); saveSchedule(); } if (event.key === 'Escape') { event.preventDefault(); cancelSchedule(); } }} />{#if scheduleError}<small id="schedule-duration-error" class="notice error" role="alert">{scheduleError}</small>{/if}</label>
        <label title="Follows this stage when reordered.">Repeat from<select value={plan.StageLoopStartIndex ?? -1} on:change={event => set('StageLoopStartIndex', numericInput(event))}><option value={-1}>Do not repeat</option>{#each rows as row, index}<option value={index}>{index + 1}: {stageLabel(row)}</option>{/each}</select></label>
      </div>
      <h3 class="templates-heading">Templates</h3>
      <p class="muted">Templates use the active preset. AT targets also require a saved preset with Advanced Training weight and power/toughness ratios enabled.</p>
      <div class="button-row"><button type="button" class="ghost" on:click={() => useTemplate(true)}>AT targets 6k/6k then active preset</button><button type="button" class="ghost" on:click={() => useTemplate(false)}>30m weighted cycle</button></div>
    </details>
  </article>
</div>

<style>
  .plan-main { align-items: start; }
  .schedule-card { grid-column: 1 / -1; }
  .schedule-settings { margin-top: .75rem; padding-top: .4rem; border-top: 1px solid var(--line); }
  .templates-heading { margin-top: .8rem; }
  @media (max-width: 740px) {
    .plan-main { grid-template-columns: minmax(0, 1fr); }
  }
</style>
