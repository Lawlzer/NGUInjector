<script lang="ts">
  import { onDestroy, tick } from 'svelte';
  import type { JsonObject, JsonValue, PlanStageAction } from './api';
  import { equal, longTarget, object, reorderStages } from './model';
  import { stageFocuses } from './options';
  import { changeStageTiming, editDurationText, editFlexWeight, fixedSeconds, formatStageDuration, formatTime, scheduleTiming, type StageTimingKind } from './stage-duration';
  import { applyStagePreset, automaticStageLabel, stageLabel, stagePresets, weightedStage } from './stage-presets';
  import GlobalPresetSelect from './GlobalPresetSelect.svelte';
  import { globalPresetRows, presetSupportsAtTargets } from './presets';
  import { matchingPlanProgress } from './stage-bar';
  import { parsePlanProgress, planProgressDuration, stageHistoryDetails } from './plan-progress';
  import { combatPrepReconciliationReason } from './controller';
  import ActivityTiming from './ActivityTiming.svelte';
  import { activityDuration, matchingStageActivityTiming } from './activity-timing';
  import PriorityPicker from './PriorityPicker.svelte';
  import { energyPriorityOptions, magicPriorityOptions } from './priority-options';

  export let plan: JsonObject;
  export let presetLibrary: JsonObject | null = null;
  export let name = '';
  export let progress: JsonObject = {};
  export let connected = false;
  export let dirty = false;
  export let namespace = '';
  export let disabled = false;
  export let stageActionReason = 'Manual stage controls are unavailable.';
  export let onstageaction: ((action: PlanStageAction, stageIndex: number, expectedEntry: string) => Promise<void>) | undefined = undefined;
  export let onchange: (plan: JsonObject) => void;
  export let ondirty: (dirty: boolean) => void = () => {};
  const ids = new WeakMap<JsonObject, number>();
  let identities: { row: JsonObject; id: number }[] = [];
  let identityNamespace: string | undefined;
  let drafts: Record<number, { text: string; dirty: boolean; ready?: boolean; error?: string }> = {};
  const durationTimers = new Map<number, ReturnType<typeof setTimeout>>();
  let restoringDuration: number | null = null;
  let restoreVersion = 0;
  let serial = 0;
  let error = '';
  let announcement = '';
  let list: HTMLDivElement;
  let drag: { row: JsonObject; plan: JsonObject; namespace: string; pointer: number } | null = null;
  let press: { row: JsonObject; plan: JsonObject; namespace: string; pointer: number; x: number; y: number } | null = null;
  let suppressClick = false;
  let suppressedPointer: number | null = null;
  let insertion: number | null = null;
  let selected: number | null = null;
  let scrollFrame: number | undefined;
  let touchX: number | null = null;
  $: durationScope = JSON.stringify([namespace, name, plan.Name ?? name]);
  $: rows = reconcile(plan, durationScope);
  $: if (!disabled && restoringDuration === null) {
    for (const [key, draft] of Object.entries(drafts)) if (draft.ready) commitDuration(Number(key));
  }
  $: if (drag && (drag.plan !== plan || drag.namespace !== namespace || disabled)) cancel();
  $: if (press && (press.plan !== plan || press.namespace !== namespace || disabled)) cancel();
  $: timing = readTiming(plan);
  $: durationDraftDirty = Object.values(drafts).some(draft => draft.dirty);
  $: ondirty(durationDraftDirty);
  $: live = parsePlanProgress(progress, connected);
  $: combatPrep = object(progress.combatPrep);
  $: cleanupPending = connected && combatPrep.schemaVersion === 1 && combatPrep.cleanupPending === true;
  $: cleanupRecoveryRequired = connected && (progress.cleanupRecoveryRequired === true || combatPrep.cleanupRecoveryRequired === true);
  $: recoveryReason = combatPrepReconciliationReason(progress);
  $: mapped = matchingPlanProgress(plan, name, progress, connected, dirty || durationDraftDirty);
  $: selectedStageIndex = rows.findIndex(row => id(row) === selected);
  $: skipToSelected = selectedStageIndex >= 0 && selectedStageIndex !== mapped?.activeStageIndex;
  $: activityTiming = cleanupRecoveryRequired ? null : matchingStageActivityTiming(combatPrep, mapped);
  $: timingInPanel = !!activityTiming && mapped?.activeStageIndex != null && selected === id(rows[mapped.activeStageIndex]);
  $: livePercent = mapped?.stageProgress === null || !mapped ? null : Math.round(mapped.stageProgress * 100);
  $: manualReason = disabled ? 'Another action is pending.' : cleanupPending || connected && combatPrep.active === true ? 'Combat prep still owns equipment and resources; normal stage allocation is blocked.' : cleanupRecoveryRequired ? recoveryReason : stageActionReason || (!onstageaction || !mapped || mapped.status !== 'running' || !mapped.stageEntry || mapped.activeStageIndex === null && (mapped.scheduleComplete !== true || (progress.activeStageIndex !== null && progress.activeStageIndex !== -1) || mapped.stages.some(stage => stage.state === 'unknown')) ? 'Wait for matching live stage telemetry with no pending edits.' : '');
  $: currentStageReason = manualReason || (mapped?.activeStageIndex == null ? 'No current stage is selected. Set a stage as current first.' : '');
  onDestroy(() => { cancel(); durationTimers.forEach(clearTimeout); durationTimers.clear(); });

  function id(row: JsonObject): number {
    if (!ids.has(row)) ids.set(row, ++serial);
    return ids.get(row)!;
  }
  function canonical(row: JsonObject): JsonObject {
    const defaults: JsonObject = { Name: '', ExecutionMode: 0, AdventureDynamicWeight: 0, Percent: 100, DurationSeconds: 0, DurationUnit: '%', CompletionMode: 0, EnergyFocus: 0, MagicFocus: 0, GearGoal: -1, FightOnEntry: false, EnergyPriorities: [], MagicPriorities: [], BlackBarLookaheadSeconds: 5 };
    return {
      ...row,
      ...Object.fromEntries(Object.entries(defaults).map(([key, fallback]) => [key, row[key] ?? fallback])),
      TargetAdvancedTrainingPower: canonicalTarget(row.TargetAdvancedTrainingPower),
      TargetAdvancedTrainingToughness: canonicalTarget(row.TargetAdvancedTrainingToughness)
    };
  }
  function canonicalTarget(value: JsonValue | undefined): string {
    try { return BigInt(longTarget(value ?? '0', 'AT target')).toString(); }
    catch { return String(value ?? '0'); }
  }
  function reconcile(value: JsonObject, scope: string): JsonObject[] {
    const focused = typeof document === 'undefined' ? null : document.activeElement;
    const focusedKey = typeof HTMLInputElement !== 'undefined' && focused instanceof HTMLInputElement && focused.hasAttribute('data-duration-key') && list?.contains(focused)
      ? Number(focused.closest('[data-stage]')?.getAttribute('data-stage')) : null;
    const nextRows = Array.isArray(value.Stages) ? value.Stages.map(object) : [];
    const sameScope = identityNamespace === scope;
    const previous = sameScope ? identities : [];
    const used = new Set<number>();
    const next = nextRows.map(row => {
      const known = ids.get(row);
      const key = sameScope && known && !used.has(known) ? known : 0;
      if (key) used.add(key);
      return { row, id: key };
    });
    // An unchanged ordered sequence is safe even when it contains duplicate presets.
    // Known references still take precedence when the user actually moved a row.
    if (previous.length === next.length && next.every((entry, i) => (!entry.id || entry.id === previous[i].id) && equal(canonical(entry.row), canonical(previous[i].row)))) {
      next.forEach((entry, i) => { entry.id = previous[i].id; used.add(entry.id); });
    }
    // Changed sequences require unique matches, never a positional guess.
    for (const compare of [equal, (a: JsonObject, b: JsonObject) => equal(canonical(a), canonical(b))]) {
      for (const entry of next.filter(entry => !entry.id)) {
        const matches = previous.filter(old => !used.has(old.id) && compare(old.row, entry.row));
        if (matches.length !== 1 || next.filter(candidate => !candidate.id && compare(matches[0].row, candidate.row)).length !== 1) continue;
        entry.id = matches[0].id;
        used.add(entry.id);
      }
    }
    for (const entry of next) {
      if (!entry.id) entry.id = ++serial;
      ids.set(entry.row, entry.id);
    }
    const retained = new Set(next.map(entry => entry.id));
    for (const key of durationTimers.keys()) if (!retained.has(key)) {
      clearTimeout(durationTimers.get(key));
      durationTimers.delete(key);
    }
    if (selected !== null && (!sameScope || !retained.has(selected))) selected = null;
    if (Object.entries(drafts).some(([key, draft]) => draft.dirty && !retained.has(Number(key)))) {
      error = sameScope ? 'Duration draft cancelled: its stage could not be identified safely after readback.' : 'Duration draft cancelled because the plan changed.';
    }
    drafts = Object.fromEntries(Object.entries(drafts).filter(([key]) => retained.has(Number(key))));
    identities = next;
    identityNamespace = scope;
    const version = ++restoreVersion;
    restoringDuration = focusedKey !== null && retained.has(focusedKey) ? focusedKey : null;
    if (restoringDuration !== null) void tick().then(() => {
      if (version !== restoreVersion || identityNamespace !== scope) return;
      // Moving keyed DOM nodes can blur an input in Chromium. That is not a user commit.
      if (document.activeElement === focused || document.activeElement === document.body) {
        list?.querySelector<HTMLInputElement>(`[data-stage="${focusedKey}"] input[data-duration-key]`)?.focus({ preventScroll: true });
      }
      restoringDuration = null;
    });
    return nextRows;
  }
  function readTiming(value: JsonObject): string {
    try { scheduleTiming(value); return ''; } catch (err) { return (err as Error).message; }
  }
  function amount(value: JsonObject, index: number): string {
    try { return formatStageDuration(value, index); } catch { return ''; }
  }
  function estimatedDuration(value: JsonObject, index: number): string {
    try { return formatTime(scheduleTiming(value).seconds[index] ?? 0); } catch { return 'Unknown'; }
  }
  function editorAmount(value: JsonObject, index: number): string {
    const row = (value.Stages as JsonObject[])[index];
    return row.CompletionMode !== 1 && !fixedSeconds(row) ? String(row.Percent ?? 100) : amount(value, index);
  }
  function segmentWeight(index: number): number {
    try {
      const timing = scheduleTiming(plan);
      return Math.max(1, (timing.seconds[index] ?? timing.total / rows.length) / Math.max(1, timing.total) * 100);
    } catch { return 1; }
  }
  function addStage(): void {
    if (!globalPresetRows(presetLibrary).some(row => row.Id === plan.ActivePresetId)) { error = 'Choose an active saved global preset in Misc > Gear settings before adding an inherited stage.'; return; }
    error = '';
    const row = weightedStage({ Percent: 10, DurationSeconds: 300, DurationUnit: 'min' });
    const key = id(row);
    onchange({ ...plan, Stages: [...rows, row] });
    selected = key;
  }
  function selectStage(key: number): void {
    if (suppressClick) return;
    selected = selected === key ? null : key;
  }
  function stageAction(action: PlanStageAction, stageIndex: number | null): void {
    if (manualReason || !mapped?.stageEntry || stageIndex === null) return;
    void onstageaction?.(action, stageIndex, mapped.stageEntry);
  }
  async function segmentKey(event: KeyboardEvent, row: JsonObject): Promise<void> {
    if (event.altKey) { keyboard(event, row); return; }
    const index = rows.indexOf(row);
    const target = event.key === 'ArrowRight' ? index + 1 : event.key === 'ArrowLeft' ? index - 1 : event.key === 'Home' ? 0 : event.key === 'End' ? rows.length - 1 : -1;
    if (target < 0 || target >= rows.length) return;
    event.preventDefault();
    selected = id(rows[target]);
    await tick();
    list?.querySelector<HTMLButtonElement>(`[data-select="${selected}"]`)?.focus();
  }
  const value = (event: Event) => (event.currentTarget as HTMLInputElement).value;
  const numeric = (event: Event): JsonValue => value(event).trim() && Number.isFinite(Number(value(event))) ? Number(value(event)) : value(event);
  function emit(next: JsonObject): void {
    if (disabled) return;
    error = '';
    // A field edit replaces its object, but must not replace the row's DOM identity.
    const nextRows = (next.Stages as JsonObject[]);
    if (nextRows.length === rows.length) nextRows.forEach((row, index) => {
      if (!ids.has(row) && !rows.includes(row)) {
        const key = id(rows[index]);
        ids.set(row, key);
        identities = identities.map(entry => entry.id === key ? { row, id: key } : entry);
      }
    });
    onchange(next);
  }
  function patch(row: JsonObject, changes: JsonObject): void {
    const index = rows.indexOf(row);
    if (index < 0) return;
    emit({ ...plan, Stages: rows.map(item => item === row ? { ...item, ...changes } : item) });
  }
  function beginDuration(key: number): void {
    const index = rows.findIndex(row => id(row) === key);
    if (index < 0 || identityNamespace !== durationScope) return;
    if (!drafts[key]) drafts = { ...drafts, [key]: { text: editorAmount(plan, index), dirty: false } };
  }
  function selectPreset(row: JsonObject, presetId: string): void {
    const preset = globalPresetRows(presetLibrary).find(preset => preset.Id === (presetId === '@active' ? plan.ActivePresetId : presetId));
    if (row.CompletionMode === 1 && !presetSupportsAtTargets(preset)) { error = 'AT targets require a saved global preset with Advanced Training weight and positive toughness/power ratios. Choose a compatible preset first.'; return; }
    patch(row, { PresetId: presetId, ...(preset ? { Name: String(preset.Name) } : { Name: 'Plan active preset' }) });
  }
  function selectMode(row: JsonObject, event: Event): void {
    if (row.CompletionMode === 1 && value(event) === '1') {
      error = 'Choose Time or % of remaining before using the automatic total-budget mode.';
      (event.currentTarget as HTMLSelectElement).value = String(row.ExecutionMode ?? 0);
      return;
    }
    patch(row, { ExecutionMode: numeric(event) });
  }
  function selectCompletion(row: JsonObject, event: Event): void {
    try {
      const selected = value(event);
      const mode = selected === 'target' ? 1 : 0;
      if (mode === 1 && row.PresetId && !presetSupportsAtTargets(globalPresetRows(presetLibrary).find(preset => preset.Id === (row.PresetId === '@active' ? plan.ActivePresetId : row.PresetId)))) throw new Error('AT targets require a saved global preset with Advanced Training weight and positive toughness/power ratios. Choose a compatible preset first.');
      const next = changeStageTiming(plan, rows.indexOf(row), selected as StageTimingKind);
      if (mode === 1 && !row.PresetId) {
        // P/T completion must allocate the training that can satisfy it.
        next.Stages = (next.Stages as JsonObject[]).map((item, index) => index === rows.indexOf(row) ? applyStagePreset(item, stagePresets.findIndex(preset => preset.label === 'AT Adventure')) : item);
      }
      discardDuration(id(row));
      emit(next);
    } catch (err) {
      error = (err as Error).message;
      (event.currentTarget as HTMLSelectElement).value = row.CompletionMode === 1 ? 'target' : fixedSeconds(row) ? 'fixed' : 'flexible';
    }
  }
  function discardDuration(key: number): void {
    clearTimeout(durationTimers.get(key));
    durationTimers.delete(key);
    const { [key]: _, ...rest } = drafts;
    drafts = rest;
  }
  function durationPlan(key: number, text: string): JsonObject {
    const index = rows.findIndex(row => id(row) === key);
    if (index < 0) throw new Error('Stage no longer exists.');
    if (/\.\s*(?:%|s|m|min|h)?$/i.test(text)) throw new Error('Finish the decimal value before saving.');
    return !fixedSeconds(rows[index]) ? editFlexWeight(plan, index, text) : editDurationText(plan, index, text);
  }
  function changeDuration(key: number, text: string): void {
    if (disabled || identityNamespace !== durationScope || !identities.some(entry => entry.id === key)) return;
    clearTimeout(durationTimers.get(key));
    durationTimers.delete(key);
    const draft = { text, dirty: true, ready: false, error: '' };
    drafts = { ...drafts, [key]: draft };
    try {
      durationPlan(key, text);
      const scope = durationScope;
      durationTimers.set(key, setTimeout(() => {
        durationTimers.delete(key);
        if (scope === durationScope && identityNamespace === scope && drafts[key] === draft) {
          drafts = { ...drafts, [key]: { ...draft, ready: true } };
        }
      }, 350));
    } catch (err) { drafts = { ...drafts, [key]: { ...draft, error: (err as Error).message } }; }
  }
  function commitDuration(key: number): void {
    if (restoringDuration === key) return;
    clearTimeout(durationTimers.get(key));
    durationTimers.delete(key);
    const draft = drafts[key];
    if (!draft?.dirty) { discardDuration(key); return; }
    if (identityNamespace !== durationScope || !identities.some(entry => entry.id === key)) { discardDuration(key); return; }
    if (disabled) { drafts = { ...drafts, [key]: { ...draft, ready: true } }; return; }
    try {
      const next = durationPlan(key, draft.text);
      emit(next);
      discardDuration(key);
    } catch (err) { drafts = { ...drafts, [key]: { ...draft, ready: false, error: (err as Error).message } }; }
  }
  function durationKey(event: KeyboardEvent, key: number): void {
    if (event.key === 'Enter') { event.preventDefault(); commitDuration(key); }
    if (event.key === 'Escape') { event.preventDefault(); discardDuration(key); error = ''; }
  }
  async function blurDuration(event: FocusEvent): Promise<void> {
    const input = event.currentTarget as HTMLInputElement;
    const key = Number(input.dataset.durationKey), scope = durationScope;
    if (restoringDuration === key) return;
    // DOM removal blurs before teardown; keyed moves may also restore this input's focus.
    await tick();
    if (input.isConnected && document.activeElement !== input && scope === durationScope) commitDuration(key);
  }
  async function move(row: JsonObject, to: number): Promise<void> {
    const from = rows.indexOf(row);
    if (disabled || from < 0 || to < 0 || to >= rows.length || from === to) return;
    const key = id(row);
    emit(reorderStages(plan, from, to));
    announcement = `${stageLabel(row)} moved to stage ${to + 1}.`;
    await tick();
    list?.querySelector<HTMLButtonElement>(`[data-select="${key}"]`)?.focus({ preventScroll: true });
  }
  function keyboard(event: KeyboardEvent, row: JsonObject): void {
    if (event.key === 'Escape') { cancel(); return; }
    if (!event.altKey || !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return;
    event.preventDefault();
    void move(row, rows.indexOf(row) + (event.key === 'ArrowUp' || event.key === 'ArrowLeft' ? -1 : 1));
  }
  function cancel(): void {
    // Browsers may emit a click immediately after a completed pointer drag.
    if (drag) {
      suppressClick = true;
      suppressedPointer = drag.pointer;
    }
    drag = null;
    press = null;
    insertion = null;
    touchX = null;
    if (scrollFrame !== undefined) cancelAnimationFrame(scrollFrame);
    scrollFrame = undefined;
  }
  function clearClickLater(): void {
    setTimeout(() => { if (suppressedPointer === null) suppressClick = false; }, 0);
  }
  function locate(x: number): void {
    const elements = Array.from(list.querySelectorAll<HTMLElement>('.bar-segment'));
    const found = elements.findIndex(element => x < element.getBoundingClientRect().left + element.getBoundingClientRect().width / 2);
    insertion = found < 0 ? rows.length : found;
  }
  function drop(): void {
    const active = drag;
    const boundary = insertion;
    cancel();
    if (!active || active.plan !== plan || active.namespace !== namespace || boundary === null) return;
    const from = rows.indexOf(active.row);
    void move(active.row, boundary > from ? boundary - 1 : boundary);
  }
  function pointerStart(event: PointerEvent, row: JsonObject): void {
    if (disabled || drag || press || (event.pointerType === 'mouse' && event.button !== 0)) return;
    suppressClick = false;
    suppressedPointer = null;
    (event.currentTarget as HTMLButtonElement).setPointerCapture(event.pointerId);
    press = { row, plan, namespace, pointer: event.pointerId, x: event.clientX, y: event.clientY };
  }
  function pointerMove(event: PointerEvent): void {
    if (press?.pointer === event.pointerId) {
      const dx = Math.abs(event.clientX - press.x);
      const dy = Math.abs(event.clientY - press.y);
      if (Math.max(dx, dy) < 7) return;
      if (dy > dx) { cancel(); return; }
      drag = { row: press.row, plan: press.plan, namespace: press.namespace, pointer: press.pointer };
      press = null;
      suppressClick = true;
      suppressedPointer = event.pointerId;
    }
    if (drag?.pointer !== event.pointerId) return;
    event.preventDefault();
    touchX = event.clientX;
    locate(touchX);
    if (scrollFrame === undefined) scrollFrame = requestAnimationFrame(scrollBar);
  }
  function scrollBar(): void {
    scrollFrame = undefined;
    if (!drag || touchX === null) return;
    const bar = list.querySelector<HTMLElement>('.bar-scroll');
    if (bar) {
      const bounds = bar.getBoundingClientRect();
      const direction = touchX < bounds.left + 40 ? -1 : touchX > bounds.right - 40 ? 1 : 0;
      if (direction) {
        bar.scrollLeft += direction * 12;
        locate(touchX);
        scrollFrame = requestAnimationFrame(scrollBar);
      }
    }
  }
  function pointerEnd(event: PointerEvent): void {
    if (press?.pointer === event.pointerId) { press = null; return; }
    if (drag?.pointer === event.pointerId) {
      locate(event.clientX);
      drop();
    }
    if (suppressedPointer === event.pointerId) {
      suppressedPointer = null;
      clearClickLater();
    }
  }
  function pointerCancel(event: PointerEvent): void {
    if (press?.pointer !== event.pointerId && drag?.pointer !== event.pointerId && suppressedPointer !== event.pointerId) return;
    cancel();
    suppressedPointer = null;
    clearClickLater();
  }
</script>

<svelte:window on:keydown={event => { if (event.key === 'Escape') cancel(); }} on:blur={cancel} />

{#if error || timing}<p class="notice error" role="alert">{error || timing}</p>{/if}
<div class="stage-workspace" bind:this={list}>
  <div class="bar-toolbar">
    <h3>Stages</h3>
    <button type="button" class="ghost" {disabled} on:click={addStage}>Add stage</button>
  </div>
  <div class="bar-scroll">
    <ol class="stage-bar" aria-label="Plan stages">
      {#each rows as row, index (id(row))}
        {@const key = id(row)}
        {@const state = mapped?.stages[index]?.state}
         {@const history = stageHistoryDetails(mapped?.stages[index] ?? null, row.ExecutionMode === 1 ? 'elapsed' : row.CompletionMode === 1 ? 'target' : fixedSeconds(row) ? 'fixed' : 'flexible')}
        <li class="bar-segment" data-stage={key} class:selected={selected === key} class:current={state === 'current' && mapped?.status === 'running'} class:completed={state === 'completed'} class:dragging={drag?.row === row} class:insert-before={insertion === index} class:insert-after={insertion === rows.length && index === rows.length - 1} style:flex-grow={segmentWeight(index)} aria-current={state === 'current' ? 'step' : undefined}>
          <button type="button" class="stage-select" data-select={key} draggable={false} aria-label={`Edit stage ${index + 1}: ${stageLabel(row)}`} aria-expanded={selected === key} aria-controls={`stage-panel-${key}`} aria-describedby={`stage-history-${key}`} aria-keyshortcuts="Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown" title={`${history}\nClick to edit; drag left or right to reorder. Keyboard: Alt+Left / Alt+Right.`}
            on:click={() => selectStage(key)} on:keydown={event => segmentKey(event, row)}
            on:pointerdown={event => pointerStart(event, row)} on:pointermove={pointerMove} on:pointerup={pointerEnd} on:pointercancel={pointerCancel} on:lostpointercapture={pointerCancel}>
              <span class="segment-top"><span>{index + 1}</span>{#if plan.StageLoopStartIndex === index}<span class="loop" aria-label="Loop start">Repeat</span>{/if}{#if state === 'current'}<span>{cleanupRecoveryRequired ? 'Reconciling' : cleanupPending ? 'Cleanup pending' : mapped?.status === 'running' ? mapped.scheduleComplete === true ? 'Sustaining' : 'Current' : mapped?.status}</span>{:else if state === 'completed'}<span>Completed</span>{:else if state === 'skipped'}<span>Skipped</span>{/if}</span>
            <strong>{stageLabel(row)}</strong>
             <span class="segment-duration" title={row.ExecutionMode === 1 ? 'Total elapsed budget including AT, planning and setup' : row.CompletionMode === 1 ? 'Mandatory AT targets' : fixedSeconds(row) ? 'Fixed duration' : 'Estimated flexible share, not live time remaining'}>{row.CompletionMode === 1 ? 'AT targets' : fixedSeconds(row) ? `${amount(plan, index)}${row.ExecutionMode === 1 ? ' total budget' : ' fixed'}` : `${estimatedDuration(plan, index)}${row.ExecutionMode === 1 ? ' flexible budget' : ' flexible'}`}{drafts[key]?.dirty ? ' *' : ''}</span>
             {#if row.CompletionMode !== 1 && !fixedSeconds(row)}<span class="segment-share">{amount(plan, index)} of remaining</span>{/if}
            <span class="runtime-fill" aria-hidden="true" style:width={`${state === 'completed' ? 100 : state === 'current' ? livePercent ?? 0 : 0}%`}></span>
          </button>
          <span id={`stage-history-${key}`} class="sr-only">{history}</span>
        </li>
      {/each}
    </ol>
  </div>
  <div class="runtime-controls">
  {#if cleanupRecoveryRequired}
    <span class="live-note" role="status">{recoveryReason}</span>
  {:else if cleanupPending}
    <span class="live-note" role="status">Cleanup pending: combat prep still owns equipment and resources.</span>
  {:else if mapped && mapped.activeStageIndex !== null}
    <div class="live-position">
      <span>{mapped.status === 'running' ? mapped.scheduleComplete === true ? 'Sustaining' : 'Current' : mapped.status}: {mapped.activeStageIndex + 1}. {mapped.stages[mapped.activeStageIndex].name}</span>
       <span title={mapped.stages[mapped.activeStageIndex].automatic ? 'Total elapsed budget used, including AT, planning and setup' : mapped.stages[mapped.activeStageIndex].timingKind === 'fixed' ? 'Applied training time in this stage' : 'Elapsed time in this stage'}>{livePercent === null ? '' : `${livePercent}%`}{mapped.stageElapsedSeconds !== null ? `${livePercent === null ? '' : ' / '}${planProgressDuration(mapped.stageElapsedSeconds)}` : ''}</span>
      <div class="live-meter" role="progressbar" aria-label={`Stage ${mapped.activeStageIndex + 1} progress`} aria-valuemin="0" aria-valuemax="100" aria-valuenow={livePercent ?? undefined} aria-valuetext={mapped.stages[mapped.activeStageIndex].presetBlocker || `${mapped.status}: ${mapped.stages[mapped.activeStageIndex].name}; ${livePercent === null ? 'progress not reported' : `${livePercent}% complete`}`}><span style:width={`${livePercent ?? 0}%`}></span></div>
      {#if mapped.stages[mapped.activeStageIndex].presetBlocker}<span class="live-note preset-blocker" role="status">{mapped.stages[mapped.activeStageIndex].presetBlocker}</span>{/if}
      {#if activityTiming?.current}<span class="activity-now">Now: {activityTiming.current.kind} / {activityDuration(activityTiming.current.elapsedSeconds)}</span>{/if}
    </div>
  {:else if mapped?.status === 'disabled'}
    <span class="live-note">Stages disabled</span>
  {:else if mapped?.scheduleComplete === true}
    <span class="live-note">Schedule complete</span>
  {:else if !live.showWaitingPlanProgress}
    <span class="live-note" title={live.planName !== name ? `Running ${live.planName}` : 'Live stages differ from the editor or edits are pending.'}>{live.planName !== name ? 'Editing another plan' : 'Syncing live progress'}</span>
  {:else}<span class="live-note">{connected ? 'Waiting for live progress' : 'Offline'}</span>{/if}
  <div class="stage-actions" aria-label="Manual stage actions" title={manualReason || undefined}>
    {#if skipToSelected}
    <button type="button" class="ghost" disabled={!!manualReason} title={manualReason || `Continue from stage ${selectedStageIndex + 1} immediately. Earlier unfinished work is skipped; this stage and later stages restart.`} on:click={() => stageAction('set', selectedStageIndex)}>Skip to selected stage</button>
    {:else}
    <button type="button" class="ghost" disabled={!!currentStageReason} title={currentStageReason || 'Restart the reported current stage immediately, resetting its current-entry progress.'} on:click={() => stageAction('restart', mapped?.activeStageIndex ?? null)}>Restart current stage</button>
    {/if}
  </div>
  </div>
  {#if activityTiming && !timingInPanel}<ActivityTiming timing={activityTiming} showNow={false} />{/if}
  {#if mapped && (mapped.status === 'paused' || mapped.status === 'interrupted') && mapped.reason}<p class="live-note" role="status">{mapped.reason}</p>{/if}

  {#each rows as row, index (id(row))}
    {@const key = id(row)}
    {@const automatic = row.ExecutionMode === 1}
    {@const flex = row.CompletionMode !== 1 && !fixedSeconds(row)}
    {@const alreadyCurrent = mapped?.activeStageIndex === index && mapped?.stages[index]?.state === 'current'}
    <section id={`stage-panel-${key}`} class="stage-panel" data-stage={key} aria-label={`Stage ${index + 1} settings`} hidden={selected !== key} style:display={selected === key ? undefined : 'none'}>
      <div class="panel-heading"><div><p class="eyebrow">Stage {index + 1}</p><h4>{stageLabel(row)}</h4></div><button type="button" class="ghost" aria-label="Close stage settings" on:click={() => { selected = null; list.querySelector<HTMLButtonElement>(`[data-select="${key}"]`)?.focus(); }}>Close</button></div>
      <div class="panel-tools">
        <button type="button" class="ghost" disabled={!!manualReason || alreadyCurrent} title={manualReason || (alreadyCurrent ? 'This stage is already current; use Restart current stage to start it again.' : 'Make this stage current immediately. Jumping forward bypasses earlier required work.')} on:click={() => stageAction('set', index)}>Set as current stage</button>
         <details class="stage-history"><summary>Runtime history</summary><p aria-label="Stage runtime history">{stageHistoryDetails(mapped?.stages[index] ?? null, automatic ? 'elapsed' : row.CompletionMode === 1 ? 'target' : fixedSeconds(row) ? 'fixed' : 'flexible')}</p></details>
      </div>
      {#if automatic}<ActivityTiming timing={alreadyCurrent ? activityTiming : null} showNow={false} />{/if}
      <div class="stage-fields">
        <label>Stage mode<select aria-label="Stage mode" value={row.ExecutionMode === undefined ? 0 : row.ExecutionMode} on:change={event => selectMode(row, event)}><option value={0}>Standard</option><option value={1}>{automaticStageLabel}</option>{#if row.ExecutionMode !== undefined && row.ExecutionMode !== 0 && row.ExecutionMode !== 1}<option value={row.ExecutionMode} disabled>Unknown execution mode</option>{/if}</select></label>
        {#if !automatic}
        <GlobalPresetSelect library={presetLibrary} label="Focus preset" value={String(row.PresetId ?? '')} inherit activePresetId={String(plan.ActivePresetId ?? '')} legacy={!row.PresetId} {disabled} onchange={id => selectPreset(row, id)} />
        {/if}
        <label>Finish stage by<select value={row.CompletionMode === 1 ? 'target' : fixedSeconds(row) ? 'fixed' : 'flexible'} on:change={event => selectCompletion(row, event)}><option value="fixed">Time</option><option value="flexible">% of remaining</option>{#if !automatic || row.CompletionMode === 1}<option value="target" disabled={automatic}>Advanced training targets</option>{/if}</select><small class="weight-share">{automatic ? 'Time and remaining shares are total elapsed budgets, including AT, planning and setup.' : row.CompletionMode === 1 ? 'Finish when both Advanced Training targets are reached; not a timed stage.' : flex ? 'Share of the time budget left after fixed-time stages, divided by relative weights.' : 'Counts applied training time; preparation and allocation waits do not count.'}</small></label>
        {#if row.CompletionMode !== 1}
          <label>{flex ? 'Flexible weight' : automatic ? 'Total budget' : 'Duration'}<input aria-label={flex ? 'Flexible weight' : automatic ? 'Total budget' : 'Duration'} title={flex ? 'Relative share of flexible time; weights do not need to total 100.' : automatic ? 'Total elapsed budget including AT, planning and setup, e.g. 25m or 2h.' : 'Training time, e.g. 25m or 2h.'} data-duration-key={key} placeholder={flex ? '35' : '25m or 2h'} type="text" spellcheck="false" value={drafts[key]?.text ?? editorAmount(plan, index)} aria-invalid={!!drafts[key]?.error} aria-describedby={drafts[key]?.error ? `duration-error-${key}` : undefined} on:focus={event => beginDuration(Number(event.currentTarget.dataset.durationKey))} on:input={event => changeDuration(Number(event.currentTarget.dataset.durationKey), value(event))} on:blur={blurDuration} on:keydown={event => durationKey(event, Number(event.currentTarget.dataset.durationKey))} />{#if drafts[key]?.error}<small id={`duration-error-${key}`} class="notice error" role="alert">{drafts[key].error}</small>{/if}{#if flex}<small class="weight-share">{amount(plan, index)} of remaining time{automatic ? ' (total elapsed budget)' : ''}. Relative weight, not a literal percentage; weights do not need to total 100.</small>{/if}</label>
        {:else}
          <label>AT power target<input aria-label="AT power target" inputmode="numeric" value={String(row.TargetAdvancedTrainingPower ?? '0')} on:input={event => patch(row, { TargetAdvancedTrainingPower: value(event) })} /></label>
          <label>AT toughness target<input aria-label="AT toughness target" inputmode="numeric" value={String(row.TargetAdvancedTrainingToughness ?? '0')} on:input={event => patch(row, { TargetAdvancedTrainingToughness: value(event) })} /></label>
        {/if}
        {#if automatic}
          <label>Adventure weight<input aria-label="Adventure weight" type="number" inputmode="decimal" min="0" max="1000000" step="any" value={row.AdventureDynamicWeight === undefined ? 0 : row.AdventureDynamicWeight} {disabled} on:input={event => patch(row, { AdventureDynamicWeight: numeric(event) })} /></label>
        {/if}
      </div>
      {#if automatic}
        <p class="live-note" title="Results are not guaranteed optimal.">Preparation counts toward the budget. Maximum 24 hours per automatic stage.</p>
      {:else if !row.PresetId}
      <p class="live-note">Choosing a preset replaces this stage's allocation and gear.</p>
      <div class="resource-fields">
        <fieldset><legend>Energy</legend><label>Energy focus<select aria-label="Energy focus" value={row.EnergyFocus ?? 0} on:change={event => patch(row, { EnergyFocus: numeric(event) })}>{#each stageFocuses as option}<option value={option.value}>{option.label}</option>{/each}</select></label><PriorityPicker label="Energy priorities" value={row.EnergyPriorities} options={energyPriorityOptions} defaultLabel="Use focus defaults" helper="Ordered first to last; unavailable or capped targets pass resources to the next priority. Empty uses focus defaults." {disabled} onchange={priorities => patch(row, { EnergyPriorities: priorities })} /></fieldset>
        <fieldset><legend>Magic</legend><label>Magic focus<select aria-label="Magic focus" value={row.MagicFocus ?? 0} on:change={event => patch(row, { MagicFocus: numeric(event) })}>{#each stageFocuses as option}<option value={option.value}>{option.label}</option>{/each}</select></label><PriorityPicker label="Magic priorities" value={row.MagicPriorities} options={magicPriorityOptions} defaultLabel="Use focus defaults" helper="Ordered first to last; unavailable or capped targets pass resources to the next priority. Empty uses focus defaults." {disabled} onchange={priorities => patch(row, { MagicPriorities: priorities })} /></fieldset>
      </div>
      {/if}
      <div class="stage-fields">
        <label class="check" title={automatic ? 'Fight prep and boss push once before fresh planning. Entry preparation consumes the total elapsed budget.' : 'Fight prep and boss push once, then stage gear. No power fruits or digger upgrades.'}><input aria-label="Fight on entry" type="checkbox" checked={row.FightOnEntry === true} on:change={event => patch(row, { FightOnEntry: event.currentTarget.checked })} />Fight on entry</label>
      </div>
      <div class="panel-actions">
        <button type="button" class="ghost" class:active={plan.StageLoopStartIndex === index} aria-pressed={plan.StageLoopStartIndex === index} on:click={() => onchange({ ...plan, StageLoopStartIndex: plan.StageLoopStartIndex === index ? -1 : index })}>Repeat from here</button>
        <button type="button" class="danger" disabled={rows.length <= 1} on:click={() => emit(reorderStages(plan, rows.indexOf(row), null))}>Remove stage</button>
      </div>
    </section>
  {/each}
</div>
<span class="sr-only" aria-live="polite">{announcement}</span>

<style>
  .stage-workspace { min-width: 0; }
  .bar-toolbar { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: .6rem; margin-bottom: .75rem; }
  .bar-toolbar button { padding: .5rem .75rem; }
  .bar-scroll { overflow-x: auto; padding: 4px 2px 8px; }
  .stage-bar { display: flex; gap: 3px; list-style: none; margin: 0; padding: 0; min-width: 100%; }
  .bar-segment { position: relative; flex-basis: 0; flex-shrink: 0; min-width: 7rem; border: 1px solid #48515b; background: #22272e; }
  .bar-segment:first-child { border-radius: 5px 0 0 5px; }
  .bar-segment:last-child { border-radius: 0 5px 5px 0; }
  .stage-select { display: flex; flex-direction: column; align-items: stretch; gap: .4rem; width: 100%; height: 100%; min-height: 98px; padding: .6rem; border: 0; border-radius: 0; background: transparent; text-align: left; cursor: grab; touch-action: pan-y; user-select: none; }
  .dragging .stage-select { cursor: grabbing; }
  .segment-top { display: flex; flex-wrap: wrap; justify-content: space-between; gap: .3rem; font: .65rem Consolas, monospace; color: #a6afb9; }
  .stage-select strong { font-size: .82rem; line-height: 1.35; overflow-wrap: anywhere; }
  .segment-duration { font: .74rem Consolas, monospace; color: #bbc7d5; }
  .segment-share { font-size: .7rem; color: var(--muted); }
  .selected { border-color: #d5e1ee; background: #303a45; box-shadow: inset 0 -3px #d5e1ee; }
  .current:not(.selected) { border-color: #9caec1; }
  .current .segment-top { color: #e3edf8; }
  .completed:not(.selected) { background: #293039; }
  .runtime-fill { position: absolute; bottom: 0; left: 0; height: 3px; background: #8ca9c6; pointer-events: none; }
  .runtime-controls { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem 1rem; margin-top: .4rem; }
  .live-position { display: flex; flex: 1 1 18rem; flex-wrap: wrap; justify-content: space-between; gap: .4rem 1rem; font-size: .78rem; min-width: 0; }
  .live-position > span:last-of-type { color: var(--muted); }
  .live-position > .activity-now { flex-basis: 100%; color: #d5e1ee; }
  .preset-blocker { flex-basis: 100%; overflow-wrap: anywhere; }
  .live-meter { flex-basis: 100%; height: 3px; background: #30363f; }
  .live-meter > span { display: block; height: 100%; background: #a6b7ca; }
  .live-note { color: var(--muted); font-size: .76rem; }
  .stage-actions { display: flex; flex-wrap: wrap; gap: .4rem; margin-left: auto; }
  .stage-actions button, .panel-tools button { padding: .4rem .55rem; font-size: .76rem; }
  .panel-tools { display: flex; flex-wrap: wrap; align-items: start; gap: .5rem 1rem; margin-top: .65rem; }
  .stage-history { margin: 0; color: var(--muted); font-size: .76rem; }
  .stage-history p { white-space: pre-line; line-height: 1.6; }
  .check { display: flex; align-items: center; gap: .5rem; }
  .stage-panel { margin-top: .8rem; padding: 1.15rem; border: 1px solid #697a8e; border-top: 3px solid #d5e1ee; border-radius: 0 0 5px 5px; background: #20262d; min-width: 0; }
  .panel-heading { display: flex; justify-content: space-between; align-items: start; gap: 1rem; }
  .panel-heading .eyebrow { margin-bottom: .3rem; }
  h4 { margin: 0; font-size: 1rem; }
  .panel-heading button { padding: .4rem .65rem; font-size: .76rem; }
  .stage-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 1rem; margin-top: 1rem; }
  .weight-share { display: block; margin-top: .35rem; color: var(--muted); font-size: .76rem; }
  .resource-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1rem; margin-top: 1rem; }
  fieldset { min-width: 0; margin: 0; padding: .85rem; border: 1px solid #3d4855; border-radius: 3px; }
  legend { padding: 0 .35rem; font: .75rem Consolas, monospace; color: #b7c7d9; }
  .panel-actions { display: flex; flex-wrap: wrap; gap: .45rem; margin-top: 1.15rem; padding-top: 1rem; border-top: 1px solid #3d4855; }
  .panel-actions button { font-size: .78rem; }
  .dragging { opacity: .5; }
  .insert-before { box-shadow: -3px 0 #c2d4e8; }
  .insert-after { box-shadow: 3px 0 #c2d4e8; }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  @media (max-width: 740px) {
    .stage-panel { padding: .85rem; }
    .resource-fields, .stage-fields { grid-template-columns: minmax(0, 1fr); }
    .panel-actions button { flex: 1 1 auto; }
  }
</style>
