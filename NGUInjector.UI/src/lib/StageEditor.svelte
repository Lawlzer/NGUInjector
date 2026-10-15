<script lang="ts">
  import { onDestroy, tick } from 'svelte';
  import type { JsonObject, JsonValue } from './api';
  import { equal, longTarget, object, reorderStages } from './model';
  import { stageFocuses } from './options';
  import { changeStageCompletion, changeTimingMode, editDurationText, formatStageDuration, scheduleTiming, timingMode } from './stage-duration';
  import { applyStagePreset, stageLabel, stagePresetIndex, stagePresets } from './stage-presets';
  import { matchingPlanProgress } from './stage-bar';
  import { parsePlanProgress, planProgressDuration } from './plan-progress';

  export let plan: JsonObject;
  export let name = '';
  export let progress: JsonObject = {};
  export let connected = false;
  export let dirty = false;
  export let namespace = '';
  export let disabled = false;
  export let onchange: (plan: JsonObject) => void;
  export let ondirty: (dirty: boolean) => void = () => {};
  const ids = new WeakMap<JsonObject, number>();
  let identities: { row: JsonObject; id: number }[] = [];
  let identityNamespace: string | undefined;
  let drafts: Record<number, { text: string; dirty: boolean }> = {};
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
  $: rows = reconcile(plan, namespace);
  $: if (drag && (drag.plan !== plan || drag.namespace !== namespace || disabled)) cancel();
  $: if (press && (press.plan !== plan || press.namespace !== namespace || disabled)) cancel();
  $: timing = readTiming(plan);
  $: durationDraftDirty = Object.values(drafts).some(draft => draft.dirty);
  $: ondirty(durationDraftDirty);
  $: live = parsePlanProgress(progress, connected);
  $: mapped = matchingPlanProgress(plan, name, progress, connected, dirty || durationDraftDirty);
  $: livePercent = mapped?.stageProgress === null || !mapped ? null : Math.round(mapped.stageProgress * 100);
  onDestroy(cancel);

  function id(row: JsonObject): number {
    if (!ids.has(row)) ids.set(row, ++serial);
    return ids.get(row)!;
  }
  function canonical(row: JsonObject): JsonObject {
    const defaults: JsonObject = { Name: '', Percent: 100, DurationSeconds: 0, DurationUnit: '%', CompletionMode: 0, EnergyFocus: 0, MagicFocus: 0, GearGoal: -1, FightOnEntry: false, EnergyPriorities: [], MagicPriorities: [], BlackBarLookaheadSeconds: 5 };
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
    const focusedKey = typeof HTMLInputElement !== 'undefined' && focused instanceof HTMLInputElement && focused.getAttribute('aria-label') === 'Duration' && list?.contains(focused)
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
        list?.querySelector<HTMLInputElement>(`[data-stage="${focusedKey}"] input[aria-label="Duration"]`)?.focus({ preventScroll: true });
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
  function segmentWeight(index: number): number {
    try {
      const timing = scheduleTiming(plan);
      return Math.max(1, (timing.seconds[index] ?? timing.total / rows.length) / Math.max(1, timing.total) * 100);
    } catch { return 1; }
  }
  function addStage(): void {
    const timed = timingMode(plan) === 'time';
    const row = applyStagePreset({ Percent: 10, DurationSeconds: timed ? 300 : 0, DurationUnit: timed ? 'min' : '%', CompletionMode: 0, TargetAdvancedTrainingPower: '0', TargetAdvancedTrainingToughness: '0', BlackBarLookaheadSeconds: 5 }, 0);
    const key = id(row);
    onchange({ ...plan, Stages: [...rows, row] });
    selected = key;
  }
  function selectStage(key: number): void {
    if (suppressClick) return;
    selected = selected === key ? null : key;
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
      if (!ids.has(row) && !rows.includes(row)) ids.set(row, id(rows[index]));
    });
    onchange(next);
  }
  function patch(row: JsonObject, changes: JsonObject): void {
    const index = rows.indexOf(row);
    if (index < 0) return;
    emit({ ...plan, Stages: rows.map(item => item === row ? { ...item, ...changes } : item) });
  }
  function selectTiming(event: Event): void {
    try {
      if (Object.values(drafts).some(draft => draft.dirty)) throw new Error('Finish or cancel duration edits before changing timing mode.');
      emit(changeTimingMode(plan, value(event) as 'time' | '%'));
    } catch (err) {
      error = (err as Error).message;
      (event.currentTarget as HTMLSelectElement).value = timingMode(plan);
    }
  }
  function beginDuration(key: number): void {
    const index = rows.findIndex(row => id(row) === key);
    if (index < 0 || identityNamespace !== namespace) return;
    if (!drafts[key]) drafts = { ...drafts, [key]: { text: amount(plan, index), dirty: false } };
  }
  function selectPreset(row: JsonObject, event: Event): void {
    const preset = Number(value(event));
    if (row.CompletionMode === 1 && stagePresets[preset]?.label !== 'AT Adventure') {
      error = 'AT power/toughness targets require AT Adventure. Change "Finish stage by" to Duration before choosing another preset.';
      (event.currentTarget as HTMLSelectElement).value = String(stagePresetIndex(row));
      return;
    }
    patch(row, applyStagePreset(row, preset));
  }
  function selectCompletion(row: JsonObject, event: Event): void {
    try {
      const mode = Number(value(event));
      const next = changeStageCompletion(plan, rows.indexOf(row), mode);
      if (mode === 1) {
        // P/T completion must allocate the training that can satisfy it.
        next.Stages = (next.Stages as JsonObject[]).map((item, index) => index === rows.indexOf(row) ? applyStagePreset(item, stagePresets.findIndex(preset => preset.label === 'AT Adventure')) : item);
        discardDuration(id(row));
      }
      emit(next);
    } catch (err) {
      error = (err as Error).message;
      (event.currentTarget as HTMLSelectElement).value = String(row.CompletionMode ?? 0);
    }
  }
  function discardDuration(key: number): void {
    const { [key]: _, ...rest } = drafts;
    drafts = rest;
  }
  function commitDuration(key: number): void {
    if (restoringDuration === key) return;
    const draft = drafts[key];
    if (!draft?.dirty) { discardDuration(key); return; }
    const index = rows.findIndex(row => id(row) === key);
    if (index < 0 || identityNamespace !== namespace || disabled) return;
    try {
      const next = editDurationText(plan, index, draft.text);
      emit(next);
      discardDuration(key);
    } catch (err) { error = (err as Error).message; }
  }
  function durationKey(event: KeyboardEvent, key: number): void {
    if (event.key === 'Enter') { event.preventDefault(); commitDuration(key); }
    if (event.key === 'Escape') { event.preventDefault(); discardDuration(key); error = ''; }
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
    <label>Stage timing<select aria-label="Stage timing" value={timingMode(plan)} on:change={selectTiming} {disabled}><option value="time" disabled={Number(plan.Version) < 15}>Time (m / h)</option><option value="%">Percent (%)</option>{#if timingMode(plan) === 'mixed'}<option value="mixed" disabled>Mixed</option>{/if}</select></label>
    <button type="button" class="ghost" {disabled} on:click={addStage}>Add stage</button>
  </div>
  <div class="bar-scroll">
    <ol class="stage-bar" aria-label="Plan stages">
      {#each rows as row, index (id(row))}
        {@const key = id(row)}
        {@const state = mapped?.stages[index]?.state}
        <li class="bar-segment" data-stage={key} class:selected={selected === key} class:current={state === 'current' && mapped?.status === 'running'} class:completed={state === 'completed'} class:dragging={drag?.row === row} class:insert-before={insertion === index} class:insert-after={insertion === rows.length && index === rows.length - 1} style:flex-grow={segmentWeight(index)} aria-current={state === 'current' ? 'step' : undefined}>
          <button type="button" class="stage-select" data-select={key} draggable={false} aria-label={`Edit stage ${index + 1}: ${stageLabel(row)}`} aria-expanded={selected === key} aria-controls={`stage-panel-${key}`} aria-keyshortcuts="Alt+ArrowLeft Alt+ArrowRight Alt+ArrowUp Alt+ArrowDown" title="Click to edit; drag left or right to reorder. Keyboard: Alt+Left / Alt+Right."
            on:click={() => selectStage(key)} on:keydown={event => segmentKey(event, row)}
            on:pointerdown={event => pointerStart(event, row)} on:pointermove={pointerMove} on:pointerup={pointerEnd} on:pointercancel={pointerCancel} on:lostpointercapture={pointerCancel}>
            <span class="segment-top"><span>{index + 1}</span>{#if plan.StageLoopStartIndex === index}<span class="loop" aria-label="Loop start">Repeat</span>{/if}{#if state === 'current'}<span>{mapped?.status === 'running' ? 'Current' : mapped?.status}</span>{/if}</span>
            <strong>{stageLabel(row)}</strong>
            <span class="segment-duration">{row.CompletionMode === 1 ? 'AT targets' : amount(plan, index)}{drafts[key]?.dirty ? ' *' : ''}</span>
            <span class="runtime-fill" aria-hidden="true" style:width={`${state === 'completed' ? 100 : state === 'current' ? livePercent ?? 0 : 0}%`}></span>
          </button>
        </li>
      {/each}
    </ol>
  </div>
  {#if mapped && mapped.activeStageIndex !== null}
    <div class="live-position">
      <span>{mapped.status === 'running' ? 'Current' : mapped.status}: {mapped.activeStageIndex + 1}. {mapped.stages[mapped.activeStageIndex].name}</span>
      <span>{livePercent === null ? 'Progress not reported' : `${livePercent}%`}{mapped.stageElapsedSeconds !== null ? ` / ${planProgressDuration(mapped.stageElapsedSeconds)} in stage` : ''}</span>
      <div class="live-meter" role="progressbar" aria-label={`Stage ${mapped.activeStageIndex + 1} progress`} aria-valuemin="0" aria-valuemax="100" aria-valuenow={livePercent ?? undefined} aria-valuetext={`${mapped.status}: ${mapped.stages[mapped.activeStageIndex].name}; ${livePercent === null ? 'progress not reported' : `${livePercent}% complete`}`}><span style:width={`${livePercent ?? 0}%`}></span></div>
    </div>
  {:else if !live.showWaitingPlanProgress}
    <p class="live-note">Runtime: {live.planName} / {live.status}. {live.planName !== name ? `Editing ${name}, not the running plan.` : 'Live position is separate while edits are pending or the runtime schedule differs.'}</p>
  {:else}<p class="live-note">{connected ? 'Waiting for runtime progress. The plan is still editable.' : 'Offline editing. Live progress appears when connected.'}</p>{/if}
  {#if live.reason && !live.showWaitingPlanProgress}<p class="live-note">{live.reason}</p>{/if}
  {#if selected === null}<p class="selection-hint">Click a section to edit it. Drag it left or right to reorder.</p>{/if}

  {#each rows as row, index (id(row))}
    {@const key = id(row)}
    <section id={`stage-panel-${key}`} class="stage-panel" data-stage={key} aria-label={`Stage ${index + 1} settings`} hidden={selected !== key} style:display={selected === key ? undefined : 'none'}>
      <div class="panel-heading"><div><p class="eyebrow">Stage {index + 1}</p><h4>{stageLabel(row)}</h4></div><button type="button" class="ghost" aria-label="Close stage settings" on:click={() => { selected = null; list.querySelector<HTMLButtonElement>(`[data-select="${key}"]`)?.focus(); }}>Close</button></div>
      <div class="stage-fields">
        <label>Focus preset<select class="preset" aria-label="Focus preset" title="Sets focuses, gear, and priority orders; keeps timing, targets, and Fight on entry." value={stagePresetIndex(row)} on:change={event => selectPreset(row, event)}><option value={-1} disabled>Custom</option>{#each stagePresets as preset, i}<option value={i}>{preset.label}</option>{/each}</select></label>
        <label>Finish stage by<select value={row.CompletionMode ?? 0} on:change={event => selectCompletion(row, event)}><option value={0}>Duration</option><option value={1}>AT power/toughness targets</option></select></label>
        {#if row.CompletionMode !== 1}
          <label>Duration<input aria-label="Duration" data-duration-key={key} placeholder="25m, 2h, or 30%" type="text" spellcheck="false" value={drafts[key]?.text ?? amount(plan, index)} on:focus={event => beginDuration(Number(event.currentTarget.dataset.durationKey))} on:input={event => { const key = Number(event.currentTarget.dataset.durationKey); if (identities.some(entry => entry.id === key)) drafts = { ...drafts, [key]: { text: value(event), dirty: true } }; }} on:blur={event => commitDuration(Number(event.currentTarget.dataset.durationKey))} on:keydown={event => durationKey(event, Number(event.currentTarget.dataset.durationKey))} /></label>
        {:else}
          <label>AT power target<input aria-label="AT power target" inputmode="numeric" value={String(row.TargetAdvancedTrainingPower ?? '0')} on:input={event => patch(row, { TargetAdvancedTrainingPower: value(event) })} /></label>
          <label>AT toughness target<input aria-label="AT toughness target" inputmode="numeric" value={String(row.TargetAdvancedTrainingToughness ?? '0')} on:input={event => patch(row, { TargetAdvancedTrainingToughness: value(event) })} /></label>
        {/if}
      </div>
      <p class="muted">Focuses and gear identify the base preset. Energy and magic priority orders are editable without changing its name; selecting a preset resets both orders.</p>
      <div class="resource-fields">
        <fieldset><legend>Energy</legend><label>Energy focus<select aria-label="Energy focus" value={row.EnergyFocus ?? 0} on:change={event => patch(row, { EnergyFocus: numeric(event) })}>{#each stageFocuses as option}<option value={option.value}>{option.label}</option>{/each}</select></label><label>Energy priorities<input aria-label="Energy priorities" placeholder="Focus defaults" title="Ordered tokens: WAN, TM, NGU, NGU-0..8, AUG(-0..13), AT(-0..4). Blank uses focus defaults." spellcheck="false" value={Array.isArray(row.EnergyPriorities) ? row.EnergyPriorities.join(', ') : ''} on:input={event => patch(row, { EnergyPriorities: value(event).trim() ? value(event).split(',').map(token => token.trim()) : [] })} /></label></fieldset>
        <fieldset><legend>Magic</legend><label>Magic focus<select aria-label="Magic focus" value={row.MagicFocus ?? 0} on:change={event => patch(row, { MagicFocus: numeric(event) })}>{#each stageFocuses as option}<option value={option.value}>{option.label}</option>{/each}</select></label><label>Magic priorities<input aria-label="Magic priorities" placeholder="Focus defaults" title="Ordered tokens: WAN, TM, NGU, NGU-0..6, RIT(-0..7). Blank uses focus defaults." spellcheck="false" value={Array.isArray(row.MagicPriorities) ? row.MagicPriorities.join(', ') : ''} on:input={event => patch(row, { MagicPriorities: value(event).trim() ? value(event).split(',').map(token => token.trim()) : [] })} /></label></fieldset>
      </div>
      <div class="stage-fields">
        <label class="check"><input aria-label="Fight on entry" type="checkbox" checked={row.FightOnEntry === true} on:change={event => patch(row, { FightOnEntry: event.currentTarget.checked })} />Fight on entry<small>Fight prep and boss push once, then stage gear. No power fruits or digger upgrades.</small></label>
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
  .stage-workspace { min-width: 0; margin-top: .8rem; }
  .bar-toolbar { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: .6rem; margin-bottom: .75rem; }
  .bar-toolbar label { display: flex; align-items: center; gap: .65rem; font-size: .78rem; }
  .bar-toolbar select { width: auto; padding: .4rem .6rem; }
  .bar-toolbar button { padding: .5rem .75rem; }
  .bar-scroll { overflow-x: auto; padding: 4px 2px 8px; }
  .stage-bar { display: flex; gap: 3px; list-style: none; margin: 0; padding: 0; min-width: 100%; }
  .bar-segment { position: relative; flex-basis: 0; flex-shrink: 0; min-width: 7rem; border: 1px solid #48515b; background: #22272e; }
  .bar-segment:first-child { border-radius: 5px 0 0 5px; }
  .bar-segment:last-child { border-radius: 0 5px 5px 0; }
  .stage-select { display: flex; flex-direction: column; align-items: stretch; gap: .45rem; width: 100%; height: 100%; min-height: 124px; padding: .7rem .65rem; border: 0; border-radius: 0; background: transparent; text-align: left; cursor: grab; touch-action: pan-y; user-select: none; }
  .dragging .stage-select { cursor: grabbing; }
  .segment-top { display: flex; flex-wrap: wrap; justify-content: space-between; gap: .3rem; font: .65rem Consolas, monospace; color: #a6afb9; }
  .stage-select strong { font-size: .82rem; line-height: 1.35; overflow-wrap: anywhere; }
  .segment-duration { font: .74rem Consolas, monospace; color: #bbc7d5; }
  .selected { border-color: #d5e1ee; background: #303a45; box-shadow: inset 0 -3px #d5e1ee; }
  .current:not(.selected) { border-color: #9caec1; }
  .current .segment-top { color: #e3edf8; }
  .completed:not(.selected) { background: #293039; }
  .runtime-fill { position: absolute; bottom: 0; left: 0; height: 3px; background: #8ca9c6; pointer-events: none; }
  .live-position { display: flex; flex-wrap: wrap; justify-content: space-between; gap: .45rem 1rem; padding: .65rem 0; font-size: .78rem; }
  .live-position > span:last-of-type { color: var(--muted); }
  .live-meter { flex-basis: 100%; height: 3px; background: #30363f; }
  .live-meter > span { display: block; height: 100%; background: #a6b7ca; }
  .live-note, .selection-hint { color: var(--muted); font-size: .76rem; margin-top: .45rem; }
  .selection-hint { padding: .7rem 0 .2rem; }
  .stage-panel { margin-top: .8rem; padding: 1.15rem; border: 1px solid #697a8e; border-top: 3px solid #d5e1ee; border-radius: 0 0 5px 5px; background: #20262d; min-width: 0; }
  .panel-heading { display: flex; justify-content: space-between; align-items: start; gap: 1rem; }
  .panel-heading .eyebrow { margin-bottom: .3rem; }
  h4 { margin: 0; font-size: 1rem; }
  .panel-heading button { padding: .4rem .65rem; font-size: .76rem; }
  .stage-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 1rem; margin-top: 1rem; }
  .resource-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1rem; margin-top: 1rem; }
  fieldset { min-width: 0; margin: 0; padding: .85rem; border: 1px solid #3d4855; border-radius: 3px; }
  legend { padding: 0 .35rem; font: .75rem Consolas, monospace; color: #b7c7d9; }
  fieldset label + label { margin-top: .8rem; }
  .panel-actions { display: flex; flex-wrap: wrap; gap: .45rem; margin-top: 1.15rem; padding-top: 1rem; border-top: 1px solid #3d4855; }
  .panel-actions button { font-size: .78rem; }
  .dragging { opacity: .5; }
  .insert-before { box-shadow: -3px 0 #c2d4e8; }
  .insert-after { box-shadow: 3px 0 #c2d4e8; }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  @media (max-width: 740px) {
    .stage-panel { padding: .85rem; }
    .resource-fields, .stage-fields { grid-template-columns: minmax(0, 1fr); }
    .stage-select { min-height: 140px; }
    .panel-actions button { flex: 1 1 auto; }
  }
</style>
