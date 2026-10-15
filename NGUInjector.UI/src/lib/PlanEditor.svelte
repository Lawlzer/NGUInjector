<script lang="ts">
  import { ask } from '@tauri-apps/plugin-dialog';
  import type { JsonObject, JsonValue } from './api';
  import { object, pretty, validatePlan, INT_MAX } from './model';
  import { fightScoreModes, gearGoals, triggerModes } from './options';
  import StageEditor from './StageEditor.svelte';
  import { formatTime, parseDurationText, scheduleTiming } from './stage-duration';
  import { applyStagePreset, stageLabel } from './stage-presets';

  export let plan: JsonObject;
  export let name: string;
  export let dirty: boolean;
  export let progress: JsonObject = {};
  export let connected = false;
  export let disabled = false;
  export let activePlanName = '';
  export let namespace = '';
  export let onchange: (plan: JsonObject) => void;
  export let ondirty: (dirty: boolean) => void = () => {};
  let actionError = '';
  let scheduleDraft: string | null = null;
  let scheduleScope = '';
  let stageDraftDirty = false;

  const toggles = [
    ['AutoRebirthEnabled', 'Auto-rebirth for this plan'], ['TimeTriggerEnabled', 'Time trigger'],
    ['NumberTriggerEnabled', 'NUMBER trigger'], ['NumberClimbEnabled', 'NUMBER climb'],
    ['AutoActivateFruits', 'Auto-activate fruits'],
    ['PrepRequireMaxedFruit', 'Require maxed prep fruit']
  ];
  const numbers = [
    { key: 'TimeTriggerSeconds', label: 'Rebirth after (s)', min: 0, step: '1' },
    { key: 'NumberMultiplierTarget', label: 'NUMBER target multiplier', min: 0, step: 'any' },
    { key: 'NumberClimbMinSeconds', label: 'Climb minimum (s)', min: 0, step: '1' },
    { key: 'NumberClimbMultiplierTarget', label: 'Climb target multiplier', min: 1, step: 'any' }
  ];
  const selects = [
    { key: 'TriggerMode', label: 'Rebirth when', options: triggerModes }
  ];
  $: rows = Array.isArray(plan.Stages) ? plan.Stages.map(object) : [];
  $: if (scheduleScope !== `${namespace}/${name}/${String(plan.Name ?? name)}`) {
    scheduleScope = `${namespace}/${name}/${String(plan.Name ?? name)}`;
    scheduleDraft = null;
    actionError = '';
  }
  $: presets = Array.isArray(plan.GoalPresets) && plan.GoalPresets.length ? plan.GoalPresets.map(object) : [{ Name: 'Default', GearGoal: plan.GearGoal ?? 0 }];
  $: presetIndex = Number(plan.ActiveGoalPresetIndex ?? 0);
  $: validation = planError(plan);
  $: usesTimeShares = rows.some(row => row.CompletionMode !== 1 && (Number(plan.Version) < 15 || Number(row.DurationSeconds ?? 0) === 0));
  $: localDraftDirty = stageDraftDirty || scheduleDraft !== null;
  $: ondirty(localDraftDirty);

  function planError(value: JsonObject): string {
    try { validatePlan(value); return ''; } catch (err) { return String((err as Error).message); }
  }
  const value = (event: Event) => (event.currentTarget as HTMLInputElement).value;
  function numericInput(event: Event): JsonValue {
    const text = value(event);
    return text.trim() && Number.isFinite(Number(text)) ? Number(text) : text;
  }
  function set(key: string, value: JsonValue): void { onchange({ ...plan, [key]: value }); }
  function selectPreset(index: number): void {
    onchange({ ...plan, GoalPresets: presets, ActiveGoalPresetIndex: index, GearGoal: presets[index].GearGoal });
  }
  function editPreset(index: number, key: string, value: JsonValue): void {
    const next = presets.map((preset, i) => i === index ? { ...preset, [key]: value } : preset);
    onchange({ ...plan, GoalPresets: next, GearGoal: next[presetIndex]?.GearGoal ?? plan.GearGoal });
  }
  function removePreset(index: number): void {
    if (presets.length <= 1) return;
    const next = presets.filter((_, i) => i !== index);
    const active = index < presetIndex ? presetIndex - 1 : Math.min(presetIndex, next.length - 1);
    onchange({ ...plan, GoalPresets: next, ActiveGoalPresetIndex: active, GearGoal: next[active].GearGoal });
  }
  function duration(seconds: JsonValue | undefined): string {
    if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) return 'Unknown';
    const total = Math.floor(seconds);
    return `${Math.floor(total / 3600)}h ${Math.floor(total % 3600 / 60)}m ${total % 60}s`;
  }
  function saveSchedule(): void {
    if (scheduleDraft === null) return;
    try {
      const { amount, unit } = parseDurationText(scheduleDraft, 'min');
      const seconds = amount * (unit === 'h' ? 3600 : unit === 'min' ? 60 : 1);
      if (unit === '%' || !Number.isInteger(seconds) || seconds < 1 || seconds > INT_MAX) throw new Error('Schedule length must be a positive whole-second time, such as 30m or 2h.');
      scheduleTiming({ ...plan, StageDurationSeconds: seconds });
      set('StageDurationSeconds', seconds);
      scheduleDraft = null;
      actionError = '';
    } catch (err) { actionError = (err as Error).message; }
  }
  function scheduleLength(value: JsonObject): string {
    try { return formatTime(scheduleTiming(value).total); } catch { return 'Check durations'; }
  }
  async function useTemplate(advancedTraining: boolean): Promise<void> {
    const before = plan;
    actionError = '';
    try {
      if (!await ask('Replace all stages? Current stage settings will be replaced and saved automatically.', { title: 'Replace stages', kind: 'warning' })) return;
      if (plan !== before || disabled) return;
      const common = { DurationSeconds: 0, DurationUnit: '%', CompletionMode: 0, TargetAdvancedTrainingPower: '0', TargetAdvancedTrainingToughness: '0', BlackBarLookaheadSeconds: 5 };
      const next = advancedTraining ? [
        applyStagePreset({ ...common, Percent: 100, CompletionMode: 1, TargetAdvancedTrainingPower: '6000', TargetAdvancedTrainingToughness: '6000' }, 6),
        applyStagePreset({ ...common, Percent: 100 }, 3)
      ] : [
        [0, 300], [1, 180], [0, 180], [1, 60], [2, 180], [0, 300], [1, 300], [2, 300]
      ].map(([preset, seconds]) => applyStagePreset({ ...common, Percent: seconds / 18, DurationSeconds: Number(plan.Version) >= 15 ? seconds : 0, DurationUnit: Number(plan.Version) >= 15 ? 'min' : '%' }, preset));
      const template = { ...plan, Stages: next, StageSchedulerEnabled: true, StageDurationSeconds: advancedTraining ? plan.StageDurationSeconds ?? 3600 : 1800, StageLoopStartIndex: advancedTraining ? -1 : 5 };
      if (advancedTraining && Number(plan.Version) >= 15) {
        const sustain = next[1];
        template.Stages = [next[0], { ...sustain, DurationSeconds: Number(template.StageDurationSeconds), DurationUnit: 'min' }];
      }
      scheduleDraft = null;
      onchange(template);
    } catch (err) { actionError = (err as Error).message; }
  }
</script>

<div class="plan-main" inert={disabled}>
  <article class="card">
    <div class="card-header"><div><p class="eyebrow">Editing plan</p><h2>{name}{dirty ? ' *' : ''}</h2></div></div>
    {#if activePlanName}<p class="muted">Active plan: {activePlanName}. {activePlanName === name ? 'Editing this active plan.' : `Edits apply to ${name}, not automatically to the active plan.`}</p>{/if}
    {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
    {#if actionError}<p class="notice error" role="alert">{actionError}</p>{/if}
    <div class="stages-heading">
      <h3>Stages</h3>
      <p class="muted">Select a section of the bar to edit it.</p>
    </div>
    <StageEditor {plan} {name} {progress} {connected} dirty={dirty || localDraftDirty} {disabled} {onchange} ondirty={dirty => stageDraftDirty = dirty} namespace={`${namespace}/${name}/${String(plan.Name ?? name)}`} />
    <details class="schedule-settings"><summary>Schedule &amp; templates</summary>
      <div class="form-grid three">
        <label class="toggle-row compact"><input type="checkbox" checked={plan.StageSchedulerEnabled === true} on:change={event => set('StageSchedulerEnabled', (event.currentTarget as HTMLInputElement).checked)} /><span><strong>Enable stages</strong></span></label>
        {#if usesTimeShares}<label>Schedule length<input aria-label="Schedule length" value={scheduleDraft ?? formatTime(Number(plan.StageDurationSeconds ?? 1800))} on:input={event => scheduleDraft = value(event)} on:blur={saveSchedule} on:keydown={event => { if (event.key === 'Enter') saveSchedule(); if (event.key === 'Escape') { scheduleDraft = null; actionError = ''; } }} /></label>{:else}<div class="dashboard-field"><span>Total time</span><strong>{scheduleLength(plan)}</strong></div>{/if}
        <label>Repeat from<select value={plan.StageLoopStartIndex ?? -1} on:change={event => set('StageLoopStartIndex', numericInput(event))}><option value={-1}>Do not repeat</option>{#each rows as row, index}<option value={index}>{index + 1}: {stageLabel(row)}</option>{/each}</select></label>
      </div>
      <h3 class="templates-heading">Templates and timing</h3>
      <div class="button-row"><button type="button" class="ghost" on:click={() => useTemplate(true)}>AT Adventure 6k/6k to NGU</button><button type="button" class="ghost" on:click={() => useTemplate(false)}>30m NUMBER cycle</button></div>
      <p class="muted">Time uses exact stage lengths, such as 25m or 2h. Percent splits the schedule length. Existing mixed plans split only the time left after fixed stages. AT targets are untimed.</p>
      <p class="muted">Without AT targets, timing follows the rebirth clock. With AT targets, stages run in sequence from plan activation. Without a repeat, the last stage stays active; rebirth triggers are separate.</p>
      <p class="muted">Repeat follows its stage when moved. Removing that stage repeats from its successor, or stops repeating if there is none.</p>
      {#if Number(plan.Version) < 15}<p class="muted">Fixed durations require a version 15 plan. Connect and reload to migrate; this plan still uses weighted % shares.</p>{/if}
    </details>
  </article>

  <details class="card settings-card">
    <summary>Gear settings</summary>
    <div class="settings-body">
      <h3>Gear</h3>
      <div class="form-grid two">
        <label>Fight scoring<select value={plan.FightScoreMode ?? 0} on:change={event => set('FightScoreMode', numericInput(event))}>{#each fightScoreModes as option}<option value={option.value}>{option.label}</option>{/each}</select></label>
      </div>
      <label>Active preset<select value={presetIndex} on:change={event => selectPreset(Number(value(event)))}>{#each presets as preset, index}<option value={index}>{String(preset.Name ?? `Preset ${index + 1}`)}</option>{/each}</select></label>
      {#each presets as preset, index}
        <div class="form-grid two">
          <label>Preset {index + 1} name<input value={String(preset.Name ?? '')} on:input={event => editPreset(index, 'Name', value(event))} /></label>
          <label>Gear goal<select value={preset.GearGoal ?? 0} on:change={event => editPreset(index, 'GearGoal', numericInput(event))}>{#each gearGoals as option}<option value={option.value}>{option.label}</option>{/each}</select></label>
        </div>
        <button type="button" class="danger" aria-label={`Remove preset ${index + 1}`} disabled={presets.length <= 1} on:click={() => removePreset(index)}>Remove preset</button>
      {/each}
      <button type="button" class="ghost" on:click={() => set('GoalPresets', [...presets, { Name: `Goal ${presets.length + 1}`, GearGoal: 0 }])}>Add preset</button>
    </div>
  </details>

  <details class="card settings-card">
    <summary>Rebirth settings</summary>
    <div class="settings-body">
      <h3>Rebirth</h3>
      <div class="form-grid three">
        {#each toggles as [key, label]}
          <label class="toggle-row compact"><input type="checkbox" checked={plan[key] === true} on:change={event => set(key, (event.currentTarget as HTMLInputElement).checked)} /><span><strong>{label}</strong></span></label>
        {/each}
        {#each numbers as field}
          <label>{field.label}<input type="number" min={field.min} max={field.step === '1' ? INT_MAX : undefined} step={field.step} value={String(plan[field.key] ?? '')} on:input={event => set(field.key, numericInput(event))} /></label>
        {/each}
        {#each selects as field}
          <label>{field.label}<select value={plan[field.key] ?? 0} on:change={event => set(field.key, numericInput(event))}>{#each field.options as option}<option value={option.value}>{option.label}</option>{/each}</select></label>
        {/each}
      </div>
    </div>
  </details>

  <details class="card settings-card"><summary>Rebirth history</summary><div class="settings-body history-list"><h3>Rebirth history</h3>
    {#each (Array.isArray(plan.RebirthHistory) ? plan.RebirthHistory : []) as entry}
      {@const record = object(entry)}
      <article>
        <h4>{String(record.TimestampLocal || 'Unknown time')} (local)</h4>
        <p>{String(record.Reason || 'Rebirth')} / {duration(record.RebirthSeconds)} / Boss {String(record.BossId ?? '?')}</p>
        <p class="muted">NUMBER {String(record.NumberBefore ?? '?')} to {String(record.NumberAfter ?? '?')} / {String(record.NumberMultiplier ?? '?')}x</p>
        <details><summary>Details</summary><pre>{pretty(entry)}</pre></details>
      </article>
    {:else}<p class="muted">No rebirths yet.</p>{/each}
  </div></details>
</div>

<style>
  .stages-heading { margin-top: 1.2rem; }
  .stages-heading p { margin-top: .25rem; }
  .schedule-settings { margin-top: 1.2rem; padding-top: .6rem; border-top: 1px solid var(--line); }
  .templates-heading { margin-top: 1.2rem; }
  .settings-card { margin-top: 0; padding: 0; }
  .settings-card > summary { padding: .85rem 1.3rem; }
  .settings-body { padding: 0 1.3rem 1.3rem; }
  .settings-body > :global(h3) { margin-bottom: .8rem; }
  @media (max-width: 740px) {
    .settings-card > summary { padding: .85rem 1rem; }
    .settings-body { padding: 0 1rem 1rem; }
  }
</style>
