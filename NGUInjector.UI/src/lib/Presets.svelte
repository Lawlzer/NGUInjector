<script lang="ts">
  import { tick } from 'svelte';
  import definitions from '../../../NGUInjector/Data/PresetDefinitions.json';
  import { isObject, type JsonObject, type JsonValue } from './api';
  import type { UiState } from './controller';
  import { canCommand, equal, object } from './model';
  import { presetZones } from './options';
  import PriorityPicker from './PriorityPicker.svelte';
  import { beardNames, beardOptions, defaultPresetBeards, diggerNames, dynamicPresetReady, focusEfficiency, newPreset, normalizePreset, objectiveKeys, objectiveLabels, globalPresetArgs, presetBeards, presetDiggerWeights, presetFields, presetMetric, presetNumberInput, globalPresetRows, globalPresetsReady, globalPreviewMatches, relativeProduction, resultRows, setPresetBeard, validateGlobalLibrary } from './presets';
  import { proposedResourcePriorities, resourceLaneOptions, resourceLanePrioritiesReady } from './presets';

  export let state: UiState;
  export let onchange: (library: JsonObject) => void;
  export let onselect: (id: string) => void;
  export let oncandidate: (key: string) => void;
  export let onpreview: () => Promise<void>;
  export let onapply: () => Promise<void>;
  export let onequip: (id: string) => Promise<void>;
  export let onstop: () => Promise<void>;
  export let onreload: () => Promise<void>;
  export let ondiscard: () => Promise<void>;
  export let compact = false;
  export let onmanage: () => void = () => {};

  let detailsOpen = false;
  let setup: HTMLElement;
  let goalPresetId = '';
  let retainedGoals: number[] = [];
  let orderAnnouncement = '';
  const removedControls = ['MagicBloodPercent', 'Zone', 'SwapSeconds'] as const;
  const goalOptions = [...objectiveLabels.map((label, index) => ({ label, index })), { label: 'Dynamic adventure', index: objectiveKeys.length }];
  $: rows = globalPresetRows(state.presetLibrary);
  $: selected = rows.find(row => row.Id === state.loadoutPresetSelectedId);
  $: if (String(selected?.Id ?? '') !== goalPresetId) {
    goalPresetId = String(selected?.Id ?? '');
    retainedGoals = [];
    orderAnnouncement = '';
  }
  $: goals = goalOptions.filter(({ index }) => retainedGoals.includes(index) || (index === objectiveKeys.length ? selected?.AdventureDynamicWeight !== undefined && selected.AdventureDynamicWeight !== 0 : Array.isArray(selected?.Weights) && selected.Weights[index] !== undefined && selected.Weights[index] !== 0));
  $: availableGoals = goalOptions.filter(({ index }) => ![3, 4, 5].includes(index) && !goals.some(goal => goal.index === index));
  $: dynamic = Number(selected?.AdventureDynamicWeight) > 0;
  $: reviewedLanes = selected?.ResourcePrioritiesReviewed === true;
  $: laneReady = resourceLanePrioritiesReady(state.snapshot, selected);
  $: trainingEnabled = Array.isArray(selected?.Weights) && Number(selected.Weights[objectiveKeys.indexOf('advancedTraining')]) > 0;
  $: trainingInvalid = !Array.isArray(selected?.AdvancedTrainingWeights) || selected.AdvancedTrainingWeights.length !== 5 || selected.AdvancedTrainingWeights.some(weight => typeof weight !== 'number' || !Number.isInteger(weight) || weight < 0 || weight > 2147483647);
  $: beards = selected ? presetBeards(selected) : null;
  $: orderedBeards = beardOptions.map(beard => {
    const priority = beards?.enabled.indexOf(beard.id) ?? -1;
    const mode = priority >= 0 ? 'enabled' : beards?.rng.includes(beard.id) ? 'rng' : 'off';
    return { ...beard, priority, mode, order: priority >= 0 ? priority : (mode === 'rng' ? 1 : 2) * beardOptions.length + beard.id };
  }).sort((a, b) => a.order - b.order);
  $: diggerWeights = selected ? presetDiggerWeights(selected, matching ? result?.effectiveWeights : undefined) : [];
  $: customDiggers = Array.isArray(selected?.DiggerWeights) && selected.DiggerWeights.length > 0;
  $: savedOverrides = selected ? removedControls.filter(key => selected[key] !== undefined && !equal(selected[key], definitions.defaults[key])) : [];
  $: validation = libraryError(state.presetLibrary);
  $: args = selected ? tryArgs(String(selected.Id), state.presetLibraryBase) : null;
  $: matching = globalPreviewMatches(state.loadoutPresetPreview, args, state.runtimeDir, state.snapshot?.runtime.startedAt, saved);
  $: result = state.loadoutPresetPreview?.result;
  $: candidates = resultRows(result?.candidates).filter(row => row.key !== result?.baselineKey);
  $: specialists = resultRows(result?.specialists);
  $: baselineMetrics = resultRows(resultRows(result?.candidates).find(row => row.key === result?.baselineKey)?.metrics);
  $: sequence = matching && isObject(result?.sequence) ? result.sequence : null;
  $: sequenceMetrics = resultRows(sequence?.metrics);
  $: previewWeights = result?.effectiveWeights ?? (Number(state.loadoutPresetPreview?.preset?.AdventureDynamicWeight) > 0 ? undefined : state.loadoutPresetPreview?.preset?.Weights);
  $: saved = !state.presetLibraryDirty && equal(state.presetLibrary, state.presetLibraryBase);
  $: lastApplied = object(state.snapshot?.runtime.loadoutPreset);
  $: activeQuickId = lastApplied.active === true && lastApplied.owner === 'dashboard' ? lastApplied.id : null;
  $: appliedEnergyRatioChanged = ratioChanged(lastApplied.energyNguWeights, state.snapshot?.settings._energyNguWeights, 9);
  $: appliedMagicRatioChanged = ratioChanged(lastApplied.magicNguWeights, state.snapshot?.settings._magicNguWeights, 7);
  $: appliedChanged = lastApplied.savedChangesRequireReequip === true || appliedEnergyRatioChanged === true || appliedMagicRatioChanged === true || !!selected && lastApplied.id === selected.Id && !equal(knownFields(object(lastApplied.preset)), knownFields(selected));
  $: appliedRatiosUnknown = !!lastApplied.id && (appliedEnergyRatioChanged === null || appliedMagicRatioChanged === null);
  $: dynamicReady = dynamicPresetReady(state.snapshot, selected);
  $: previewReady = dynamicReady && laneReady && globalPresetsReady(state.snapshot, 'previewGlobalPreset', state.now);
  $: applyReady = dynamicReady && laneReady && globalPresetsReady(state.snapshot, 'applyGlobalPreset', state.now);
  $: equipReady = globalPresetsReady(state.snapshot, 'equipGlobalPreset', state.now);
  $: stopReady = canCommand(state.snapshot, 'stopFocus', state.now);
  $: editDisabled = state.busy && !state.editing;

  function knownFields(row: JsonObject): JsonObject { const normalized = normalizePreset(row); return Object.fromEntries(presetFields.map(key => [key, normalized[key]])); }
  function ratioChanged(saved: JsonValue | undefined, current: JsonValue | undefined, count: number): boolean | null {
    return [saved, current].every(value => Array.isArray(value) && value.length === count && value.every(weight => typeof weight === 'number' && Number.isInteger(weight) && weight >= 0 && weight <= 2147483647)) ? !equal(saved, current) : null;
  }
  function libraryError(value: JsonObject | null): string { if (!value) return ''; try { validateGlobalLibrary(value); return ''; } catch (error) { return String((error as Error).message); } }
  function tryArgs(id: string, library: JsonObject | null): JsonObject | null { try { return globalPresetArgs(id, library); } catch { return null; } }
  const text = (event: Event) => (event.currentTarget as HTMLInputElement).value;
  function numberInput(event: Event): JsonValue {
    return presetNumberInput(text(event));
  }
  function replace(next: JsonObject[]): void { if (state.presetLibrary) onchange({ ...state.presetLibrary, Presets: next }); }
  function edit(key: string, value: JsonValue): void { replace(rows.map(row => row === selected ? { ...row, [key]: value } : row)); }
  function reviewLanes(): void {
    if (selected) replace(rows.map(row => row === selected ? { ...row, EnergyPriorities: proposedResourcePriorities(row, true), MagicPriorities: proposedResourcePriorities(row, false), ResourcePrioritiesReviewed: true } : row));
  }
  function add(source?: JsonObject): void {
    if (rows.length >= 1024) return;
    const row = newPreset(source);
    replace([...rows, row]); onselect(String(row.Id));
  }
  function remove(): void { replace(rows.filter(row => row !== selected)); onselect(''); }
  function goalWeight(index: number, value: JsonValue): void {
    if (!retainedGoals.includes(index)) retainedGoals = [...retainedGoals, index];
    if (index === objectiveKeys.length) { edit('AdventureDynamicWeight', value); return; }
    const weights = Array.isArray(selected?.Weights) ? [...selected.Weights] : Array(objectiveKeys.length).fill(0);
    weights[index] = value; edit('Weights', weights);
  }
  async function addGoal(event: Event): Promise<void> {
    const value = text(event);
    if (!value) return;
    const index = Number(value);
    goalWeight(index, 1);
    (event.currentTarget as HTMLSelectElement).value = '';
    await tick();
    setup?.querySelector<HTMLInputElement>(`[data-goal-index="${index}"]`)?.focus();
  }
  async function removeGoal(index: number): Promise<void> {
    const next = goals.find(goal => goal.index > index) ?? goals.find(goal => goal.index !== index);
    goalWeight(index, 0);
    retainedGoals = retainedGoals.filter(value => value !== index);
    await tick();
    (next ? setup?.querySelector<HTMLInputElement>(`[data-goal-index="${next.index}"]`) : setup?.querySelector<HTMLSelectElement>('[aria-label="Add goal"]'))?.focus();
  }
  function diggerWeight(event: Event, index: number): void {
    const weights = [...diggerWeights];
    weights[index] = numberInput(event); edit('DiggerWeights', weights);
  }
  async function beardMode(id: number, mode: string): Promise<void> {
    if (selected) replace(rows.map(row => row === selected ? setPresetBeard(row, id, mode) : row));
    await tick();
    setup?.querySelector<HTMLSelectElement>(`[data-beard-id="${id}"] select`)?.focus();
  }
  function defaultBeards(): void {
    if (selected) replace(rows.map(row => row === selected ? { ...row, ...defaultPresetBeards() } : row));
  }
  async function moveBeard(index: number, direction: number): Promise<void> {
    if (!selected || !beards) return;
    const enabled = [...beards.enabled];
    [enabled[index], enabled[index + direction]] = [enabled[index + direction], enabled[index]];
    replace(rows.map(row => row === selected ? { ...row, BeardPriority: enabled, BeardRotation: beards.rng } : row));
    const id = enabled[index + direction];
    orderAnnouncement = `${beardOptions.find(beard => beard.id === id)?.name}, priority ${index + direction + 1}`;
    await tick();
    const controls = setup?.querySelector(`[data-beard-id="${id}"]`);
    (controls?.querySelector<HTMLButtonElement>(`button[data-direction="${direction}"]:not(:disabled)`) ?? controls?.querySelector<HTMLButtonElement>('button:not(:disabled)'))?.focus();
  }
  function resetSavedOverrides(): void {
    replace(rows.map(row => row === selected ? { ...row, ...Object.fromEntries(removedControls.map(key => [key, definitions.defaults[key]])) } : row));
  }
  function trainingWeight(event: Event, index: number): void {
    const weights = Array.isArray(selected?.AdvancedTrainingWeights) ? [...selected.AdvancedTrainingWeights] : [1, 1, 0, 0, 0];
    weights[index] = numberInput(event); edit('AdvancedTrainingWeights', weights);
  }
  function ignored(metric: JsonObject): boolean { return Array.isArray(previewWeights) && Number(previewWeights[objectiveKeys.indexOf(String(metric.key))]) === 0; }
  function output(metric: JsonObject | undefined): string {
    if (!metric) return 'Unknown';
    if (ignored(metric) && (metric.value === 0 || metric.value === null || metric.value === undefined)) return 'n/a';
    return metric.quality === 'unavailable' ? 'Unknown' : presetMetric(metric.value);
  }
  function comparison(metric: JsonObject): string {
    if (!matching) return 'Unknown';
    const value = relativeProduction(metric, baselineMetrics.find(row => row.key === metric.key));
    return value === 'Unknown' && ignored(metric) ? 'n/a' : value;
  }
  function efficiency(metric: JsonObject): string {
    if (!matching) return 'Unknown';
    const value = focusEfficiency(metric, specialists);
    return value === 'Unknown' && ignored(metric) ? 'n/a' : value;
  }
  function notes(value: JsonValue | undefined): string[] { return Array.isArray(value) ? value.filter((row): row is string => typeof row === 'string') : []; }
  function listText(value: JsonValue | undefined): string { return Array.isArray(value) ? value.join(', ') : String(value ?? ''); }
  function canApply(row: JsonObject): boolean { return !state.busy && saved && !validation && matching && applyReady && row.feasible === true && typeof row.score === 'number' && Number.isFinite(row.score); }
  function apply(row: JsonObject): void { if (canApply(row)) { oncandidate(String(row.key)); void onapply(); } }
  function candidateName(key: JsonValue | undefined): string {
    return String([...candidates, ...specialists].find(row => row.key === key)?.label ?? (key === 'current' ? 'Current gear' : key ?? 'Unknown'));
  }
  function gearSlot(value: JsonValue | undefined): string {
    if (typeof value !== 'number') return String(value ?? 'Unknown slot');
    return ({ '-1': 'Head', '-2': 'Chest', '-3': 'Legs', '-4': 'Boots', '-5': 'Weapon', '-6': 'Offhand' } as Record<number, string>)[value] ?? (value >= 10000 ? `Accessory ${value - 9999}` : `Slot ${value}`);
  }
  function canUsePreset(id: string): boolean { const row = rows.find(row => row.Id === id); return !state.busy && (activeQuickId === id ? stopReady : saved && !validation && equipReady && dynamicPresetReady(state.snapshot, row) && resourceLanePrioritiesReady(state.snapshot, row)); }
  function usePreset(id: string): void {
    if (!canUsePreset(id)) return;
    if (activeQuickId === id) { void onstop(); return; }
    onselect(id);
    detailsOpen = true;
    void onequip(id);
  }
</script>

<section class="presets" class:compact aria-label="Loadout presets">
  <article class="card library" aria-label={compact ? 'Quick' : 'Global preset library'}>
    <div class="card-header"><h2>{compact ? 'Quick' : 'Global presets'}</h2>{#if compact}<button type="button" class="ghost" on:click={onmanage}>Manage presets</button>{/if}</div>
    {#if compact}
      <p class="muted">Quick actions pause the plan and auto-rebirth.</p>
      <div class="quick-presets" aria-label="Global quick presets">{#each rows as row}<button type="button" class="quick-preset" class:active={activeQuickId === row.Id} aria-label={`Use ${String(row.Name)}`} aria-pressed={activeQuickId === row.Id} aria-expanded={detailsOpen && selected?.Id === row.Id} aria-controls="quick-preset-details" title={activeQuickId === row.Id ? 'Stop this Quick preset. Does not resume the plan or restore previous gear.' : 'Apply this preset and open its details. Replaces the running plan. Click again to stop.'} disabled={!canUsePreset(String(row.Id))} on:click={() => usePreset(String(row.Id))}><strong>{String(row.Name)}</strong></button>{/each}</div>
      {#if state.presetLibrary}<div class="library-row quick-browse"><label>Inspect preset<select aria-label="Inspect preset" value={state.loadoutPresetSelectedId} disabled={state.busy} on:change={event => { onselect(text(event)); detailsOpen = true; }}><option value="">Select a preset</option>{#each rows as row}<option value={String(row.Id)}>{String(row.Name || '(Unnamed preset)')}</option>{/each}</select></label>{#if !saved}<button type="button" class="ghost" disabled={state.busy} on:click={ondiscard}>Discard library edits</button>{/if}</div>{/if}
    {:else}
    <div class="library-row">
      {#if state.presetLibrary}
        <label class="preset-picker">Preset<select aria-label="Browse preset" value={state.loadoutPresetSelectedId} disabled={state.busy} on:change={event => onselect(text(event))}><option value="">Select a preset</option>{#each rows as row}<option value={String(row.Id)}>{String(row.Name || '(Unnamed preset)')}</option>{/each}</select></label>
        <div class="library-actions">
          <button type="button" disabled={state.busy || rows.length >= 1024} on:click={() => add()}>Add preset</button>
          <button type="button" disabled={state.busy || !selected || rows.length >= 1024} on:click={() => selected && add({ ...selected, Name: `${String(selected.Name).slice(0, 94)} copy` })}>Clone preset</button>
          <button type="button" class="ghost danger" disabled={state.busy || !selected} on:click={remove}>Delete preset</button>
          {#if !saved}<button type="button" class="ghost" disabled={state.busy} on:click={ondiscard}>Discard library edits</button>{/if}
        </div>
      {/if}
      <button type="button" class="ghost" disabled={state.busy || state.presetLibraryLoading} on:click={onreload}>{state.presetLibraryLoading ? 'Loading library...' : 'Reload library'}</button>
    </div>
    {/if}
    {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
    {#if state.presetLibrary && !saved && !validation}<p class="notice" role="status">Preset changes pending.</p>{/if}
  </article>

  <div id="quick-preset-details" hidden={compact && !detailsOpen} style:display={compact && !detailsOpen ? 'none' : undefined}>
  {#if compact}<div class="details-header"><h2>{String(selected?.Name ?? 'Preset details')}</h2><button type="button" class="ghost" aria-label="Close preset details" on:click={() => detailsOpen = false}>Close</button></div>{/if}
  <div class="workspace">
    {#if selected || !state.presetLibrary}
    <article class="card setup" aria-label="Preset settings" bind:this={setup}>
      {#if selected}
        <h2>Setup</h2>
        <div class="preset-header">
          <label>Preset name<input value={String(selected.Name ?? '')} disabled={editDisabled} maxlength="100" on:input={event => edit('Name', text(event))} /></label>
          <label class="toggle-row"><input type="checkbox" aria-label="Auto-update preset" checked={selected.AutoUpdate !== false} disabled={editDisabled} on:change={event => edit('AutoUpdate', event.currentTarget.checked)} /><span>Auto-update on equip</span></label>
        </div>
        <section class="goals-editor" aria-label="Preset goals">
          <h3>Goals</h3>
          <table class="editor-table goal-table">
            <caption class="sr-only">Goal weights</caption>
            <thead><tr><th scope="col">Goal</th><th scope="col">Weight</th><th scope="col"><span class="sr-only">Actions</span></th></tr></thead>
            <tbody>{#each goals as goal (goal.index)}<tr>
              <th scope="row">{goal.label}</th>
              <td><input aria-label={`${goal.label} weight`} data-goal-index={goal.index} inputmode="decimal" value={String(goal.index === objectiveKeys.length ? selected.AdventureDynamicWeight ?? '' : Array.isArray(selected.Weights) ? selected.Weights[goal.index] ?? '' : '')} disabled={editDisabled} on:input={event => goalWeight(goal.index, numberInput(event))} /></td>
              <td><button type="button" class="ghost" aria-label={`Remove ${goal.label} goal`} disabled={editDisabled} on:click={() => removeGoal(goal.index)}>Remove</button></td>
            </tr>{/each}</tbody>
          </table>
          {#if availableGoals.length}<select class="add-goal" aria-label="Add goal" value="" disabled={editDisabled} on:change={addGoal}><option value="">+ Add goal</option>{#each availableGoals as goal}<option value={String(goal.index)}>{goal.label}</option>{/each}</select>{/if}
          {#if trainingEnabled}
            <section class="training-editor" aria-label="Advanced Training ratios">
              <h4>Advanced Training ratios</h4>
              <table class="editor-table"><caption class="sr-only">Advanced Training ratios</caption><thead><tr><th scope="col">Training</th><th scope="col">Ratio</th></tr></thead><tbody>
                {#each ['Toughness', 'Power', 'Block', 'Wandoos energy', 'Wandoos magic'] as label, i}<tr><th scope="row">{label}</th><td><input aria-label={`Preset AT ${label} ratio`} inputmode="numeric" value={String(Array.isArray(selected.AdvancedTrainingWeights) ? selected.AdvancedTrainingWeights[i] : '')} disabled={editDisabled} on:input={event => trainingWeight(event, i)} /></td></tr>{/each}
              </tbody></table>
            </section>
          {:else if trainingInvalid}<button type="button" class="ghost reset-training" disabled={editDisabled} on:click={() => edit('AdvancedTrainingWeights', [...definitions.defaults.AdvancedTrainingWeights])}>Reset AT ratios</button>{/if}
        </section>
        <section class="lane-editor" aria-label="Resource funding">
          <div class="support-heading"><h3>{reviewedLanes ? 'Resource lane order' : 'Proposed lane order'}</h3>{#if !reviewedLanes}<span class="muted">Legacy proportional allocation</span>{/if}</div>
          <p class="muted">Goal weights score shared gear. Each lane funds to useful cap in order.</p>
          <div class="support-grid">
            {#each [true, false] as energy}
              {@const key = energy ? 'EnergyPriorities' : 'MagicPriorities'}
              <PriorityPicker label={energy ? 'Energy funding' : 'Magic funding'} value={reviewedLanes ? selected[key] : proposedResourcePriorities(selected, energy)} options={resourceLaneOptions(selected, energy)} defaultLabel="No activity" helper="" disabled={editDisabled || !reviewedLanes} onchange={value => edit(key, value)} />
            {/each}
          </div>
          {#if !reviewedLanes}<button type="button" disabled={editDisabled || !!validation} on:click={reviewLanes}>Review &amp; enable lane order</button>{/if}
        </section>
        <div class="support-heading support-mode"><h3>Beards &amp; diggers</h3><select aria-label="Support mode" value={String(selected.SupportMode)} disabled={editDisabled} on:change={event => edit('SupportMode', text(event))}><option value="current">Keep current</option><option value="planned">Optimize support</option></select></div>
        {#if selected.SupportMode === 'planned' && beards}
          <div class="support-grid">
          <section class="support-editor" aria-label="Beard settings">
            <div class="support-heading"><h3>Beards</h3><button type="button" class="ghost" disabled={editDisabled} on:click={defaultBeards}>Use default beards</button></div>
            <table class="editor-table beard-table"><caption class="sr-only">Beard modes and priority</caption><thead><tr><th scope="col">Beard</th><th scope="col">Mode</th><th scope="col">Order</th></tr></thead><tbody>
              {#each orderedBeards as beard, i (beard.id)}
                <tr data-beard-id={beard.id} class:group-start={i > 0 && orderedBeards[i - 1].mode !== beard.mode}>
                  <th scope="row">{beard.name}</th><td><select aria-label={`${beard.name} beard`} value={beard.mode} disabled={editDisabled} on:change={event => beardMode(beard.id, text(event))}><option value="enabled">Preferred</option><option value="rng">Rotate</option><option value="off">Off</option></select></td>
                  <td>{#if beard.priority >= 0}<div class="beard-order"><span>{beard.priority + 1}</span><button type="button" class="ghost" data-direction="-1" aria-label={`Move ${beard.name} beard up`} disabled={editDisabled || beard.priority === 0} on:click={() => moveBeard(beard.priority, -1)}>Up</button><button type="button" class="ghost" data-direction="1" aria-label={`Move ${beard.name} beard down`} disabled={editDisabled || beard.priority === beards.enabled.length - 1} on:click={() => moveBeard(beard.priority, 1)}>Down</button></div>{:else}<span class="muted">-</span>{/if}</td>
                </tr>
              {/each}
            </tbody></table>
            <span class="sr-only" role="status" aria-live="polite">{orderAnnouncement}</span>
          </section>
          <section class="support-editor" aria-label="Digger settings">
            <div class="support-heading"><h3>Diggers</h3></div>
            <div class="digger-controls">
              <label>Budget (% gold/sec)<input aria-label="Digger budget (% gold)" inputmode="numeric" value={String(selected.DiggerBudgetPercent ?? '')} disabled={editDisabled} on:input={event => edit('DiggerBudgetPercent', numberInput(event))} /></label>
              <label>Digger weights<select aria-label="Digger weights" value={customDiggers ? 'custom' : 'automatic'} disabled={editDisabled} on:change={event => edit('DiggerWeights', text(event) === 'custom' ? [...diggerWeights] : [])}><option value="automatic">Automatic</option><option value="custom" disabled={!customDiggers && (!!validation || diggerWeights.length !== 12)}>Custom</option></select></label>
            </div>
            {#if dynamic && !customDiggers && diggerWeights.length !== 12}<p class="muted">Preview the saved preset to resolve adventure goals before choosing Custom digger weights.</p>{/if}
            {#if customDiggers}<table class="editor-table"><caption class="sr-only">Custom digger weights</caption><thead><tr><th scope="col">Digger</th><th scope="col">Weight</th></tr></thead><tbody>{#each diggerNames as name, i}<tr><th scope="row">{name}</th><td><input aria-label={`${name} digger weight`} inputmode="decimal" value={String(diggerWeights[i] ?? '')} disabled={editDisabled} on:input={event => diggerWeight(event, i)} /></td></tr>{/each}</tbody></table>{/if}
          </section>
          </div>
        {/if}
        <details class="constraints"><summary>Minimum adventure stats{#if Number(selected.MinimumPower) > 0 || Number(selected.MinimumToughness) > 0}<span class="muted"> / Set</span>{/if}</summary>
          <div class="editor-fields">
            <label>Minimum power<input inputmode="decimal" value={String(selected.MinimumPower ?? '')} disabled={editDisabled} on:input={event => edit('MinimumPower', numberInput(event))} /></label>
            <label>Minimum toughness<input inputmode="decimal" value={String(selected.MinimumToughness ?? '')} disabled={editDisabled} on:input={event => edit('MinimumToughness', numberInput(event))} /></label>
          </div>
        </details>
        {#if savedOverrides.length}<div class="saved-overrides" aria-label="Saved overrides"><strong>Saved overrides</strong><span>{#each savedOverrides as key, i}{i ? ' / ' : ''}{key === 'MagicBloodPercent' ? `Blood magic: ${String(selected[key])}%` : key === 'SwapSeconds' ? `Swap cost: ${String(selected[key])}s` : `Zone: ${presetZones.find(zone => zone.value === selected[key])?.label ?? String(selected[key])}`}{/each}</span><button type="button" class="ghost" disabled={editDisabled} on:click={resetSavedOverrides}>Reset to automatic</button></div>{/if}
        <div class="preview-action"><button type="button" class="primary" disabled={state.busy || !saved || !!validation || !args || !previewReady} on:click={onpreview}>{state.busy ? 'Working...' : 'Preview preset'}</button><button type="button" title="Pauses the plan and auto-rebirth." disabled={state.busy || !saved || !!validation || !args || !equipReady || !dynamicReady || !laneReady} on:click={() => selected && onequip(String(selected.Id))}>Equip preset</button></div>
        {#if !previewReady}<p class="muted" role="status">Preview unavailable. Connect to a compatible injector.</p>{/if}
        {#if !dynamicReady}<p class="notice" role="status">Update the injector to use Dynamic adventure.</p>{/if}
        {#if !laneReady}<p class="notice" role="status">Update the injector to use reviewed lane order.</p>{/if}
      {:else}<h2>Presets unavailable</h2><p class="muted">Connect to the injector or reload the library.</p>{/if}
    </article>
    {/if}

  <div class="results">
    {#if result}
    <article class="card" aria-label="Preset comparison">
      <div class="card-header"><h2>Results</h2><span class="muted">{presetMetric(result.horizonSeconds, 's')} projection</span></div>
      {#if result.adventureTask}<p class="muted">Resolved adventure task: {String(result.adventureTask)}</p>{/if}
      {#if !matching}<p class="notice" role="status">Changed since preview. Preview again to compare or apply.</p>{/if}
      {#if result.lazyItopodShifterActive === true}<p class="notice">Lazy ITOPOD controls the floor; it may change with your stats.</p>{/if}
      <div class="candidate-list" class:stale={!matching}>{#each candidates as row, i}{@render candidateCard(row, i === 0)}{:else}<p class="muted">No recommended setup available. Preview again.</p>{/each}</div>
    </article>
    {/if}

    {#if lastApplied.id}
      <article class="card applied" aria-label="Last applied preset">
        <div class="card-header"><h2>Last applied</h2><span class="pill">{String(lastApplied.status ?? 'Unknown status')}</span></div>
        <p><strong>{String(lastApplied.appliedName ?? lastApplied.name ?? lastApplied.id)}</strong></p>
        {#if lastApplied.adventureTask}<p class="muted">Resolved adventure task: {String(lastApplied.adventureTask)}</p>{/if}
        {#if isObject(lastApplied.rotation)}<p class="muted">Current beards: {beardNames(lastApplied.rotation.beards) || 'None'}{lastApplied.rotation.observed === true ? '' : ' (not currently owned)'}</p>{/if}
        {#if appliedChanged}<p class="notice" role="status">Preset changed. Re-equip or preview and reapply.</p>{/if}
        {#if appliedRatiosUnknown}<p class="muted">Applied NGU ratios unavailable.</p>{/if}
      </article>
    {/if}
  </div>
  </div>

  </div>
</section>

{#snippet productionTable(metrics: JsonObject[], withSequence: boolean)}
  <div class="metric-scroll"><table>
    <caption class="sr-only">Projected production</caption>
    <thead><tr><th scope="col">Goal</th><th scope="col">Output</th><th scope="col" title="Compared with current gear under the preset's allocation and support settings">vs current gear</th><th scope="col" title="Percentage of the best specialist production found">% of full focus</th>{#if withSequence}<th scope="col">Two-phase<small>estimate only</small></th>{/if}</tr></thead>
    <tbody>{#each metrics as metric}<tr><th scope="row">{String(metric.label ?? metric.key)}{#if output(metric) !== 'n/a'}<small title={String(metric.reason ?? '')}>{String(metric.quality ?? 'unavailable')}{metric.reason ? `: ${metric.reason}` : ''}</small>{/if}</th><td>{output(metric)}{#if output(metric) !== 'n/a'}{' '}<small class="unit">{String(metric.unit ?? '')}</small>{/if}</td><td>{comparison(metric)}</td><td>{efficiency(metric)}</td>{#if withSequence}{@const phaseMetric = sequenceMetrics.find(row => row.key === metric.key)}<td>{output(phaseMetric)}{#if phaseMetric && output(phaseMetric) !== 'n/a'}{' '}<small class="unit">{String(phaseMetric.unit ?? metric.unit ?? '')}</small>{/if}</td>{/if}</tr>{/each}</tbody>
  </table></div>
{/snippet}

{#snippet candidateCard(row: JsonObject, first: boolean)}
  {@const support = object(row.support)}
  <section class="candidate" aria-label={String(row.label ?? row.key)}>
    <div class="candidate-header">
      <div><h3>{String(row.label ?? row.key)}</h3><p class="muted">Score {matching ? presetMetric(row.score) : 'Unknown'}{#if row.feasible !== true} / Unavailable{/if}</p></div>
      <button type="button" class="primary" aria-label={`Apply ${String(row.label ?? row.key)}`} disabled={!canApply(row)} on:click={() => apply(row)}>Apply setup</button>
    </div>
    {#if row.feasible !== true}<p class="notice">This setup cannot be applied. {notes(row.warnings).join(' ')}</p>{:else if typeof row.score !== 'number' || !Number.isFinite(row.score)}<p class="notice">Score unavailable. Resolve unknown goals and preview again.</p>{/if}
    {@render productionTable(resultRows(row.metrics), first && !!sequence)}
    {#if first && sequence}<div class="sequence-summary"><strong>Two-phase estimate</strong><span>{candidateName(sequence.firstKey)} {presetMetric(sequence.firstSeconds, 's')}, then {candidateName(sequence.secondKey)} {presetMetric(sequence.secondSeconds, 's')}</span><small>Score {presetMetric(sequence.score)} / {presetMetric(sequence.improvementPercent, '%')} improvement / preview only</small></div>{/if}
    <details><summary>Allocation &amp; route</summary>
      <dl class="allocation-list"><div><dt>Energy NGU</dt><dd>{presetMetric(row.energyAllocated)}</dd></div><div><dt>Magic NGU</dt><dd>{presetMetric(row.magicNguAllocated)}</dd></div><div><dt>Blood magic</dt><dd>{presetMetric(row.bloodAllocated)} / {presetMetric(row.magicBloodPercent, '%')}</dd></div><div><dt>Blood ritual</dt><dd>{row.bloodRitual === null ? 'n/a' : presetMetric(row.bloodRitual)}</dd></div></dl>
      <p class="muted">Zone: {typeof row.zone === 'number' ? presetZones.find(zone => zone.value === row.zone)?.label ?? presetMetric(row.zone) : 'None'} / ITOPOD floor: {typeof row.itopodFloor === 'number' ? presetMetric(row.itopodFloor) : 'None'}{#if typeof row.expectedHits === 'number'} / {presetMetric(row.expectedHits)} hits{/if}</p>
    </details>
    <details><summary>Gear <span class="muted">/ {resultRows(row.items).length} items</span></summary>
      <ul class="gear-list">{#each resultRows(row.items) as item}<li><span class="muted">{gearSlot(item.slot)}</span><strong title={`ID ${presetMetric(item.id)} / source slot ${String(item.sourceSlot)} / factor ${presetMetric(item.factor)}`}>{String(item.name)}</strong><span>Lv {presetMetric(item.level)}</span></li>{:else}<li>Item details unavailable; IDs: {listText(row.itemIds) || 'Unknown'}</li>{/each}</ul>
    </details>
    <details><summary>Beards &amp; diggers <span class="muted">/ {String(support.mode ?? 'Unknown')}</span></summary>
      <p>Beards: {beardNames(support.beards) || 'None'}</p>
      <dl class="allocation-list"><div><dt>Gold budget</dt><dd>{presetMetric(support.goldBudget)}</dd></div><div><dt>Gold drain</dt><dd>{presetMetric(support.goldDrain)}</dd></div><div><dt>Gold remaining</dt><dd>{presetMetric(support.goldRemaining)}</dd></div></dl>
      <div class="metric-scroll"><table class="digger-table"><caption class="sr-only">Digger allocation</caption><thead><tr><th scope="col">Digger</th><th scope="col">Weight</th><th scope="col">Level</th><th scope="col">Gold drain</th></tr></thead><tbody>{#each resultRows(support.diggers) as digger}<tr><th scope="row">{String(digger.name)}</th><td>{support.mode === 'current' ? 'n/a' : Array.isArray(support.diggerWeights) && typeof digger.id === 'number' ? presetMetric(support.diggerWeights[digger.id]) : 'Unknown'}</td><td>{presetMetric(digger.level)}</td><td>{presetMetric(digger.drain)}</td></tr>{:else}<tr><td colspan="4">No diggers</td></tr>{/each}</tbody></table></div>
    </details>
  </section>
{/snippet}

<style>
  .presets { display: grid; gap: 1rem; min-width: 0; }
  .quick-presets { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 1fr)); gap: .6rem; margin-top: .75rem; }
  .quick-preset { text-align: left; padding: .8rem; }
  .quick-browse { margin-top: .75rem; }
  .details-header { display: flex; align-items: center; justify-content: space-between; gap: .75rem; margin-bottom: .75rem; }
  .presets :is(p, label, section, article, details, h3) { min-width: 0; overflow-wrap: anywhere; }
  .library-row { display: flex; align-items: end; flex-wrap: wrap; gap: .75rem; }
  .library-row label { flex: 1 1 150px; }
  .library-row .preset-picker { flex: 2 1 220px; }
  .library-actions { display: flex; flex-wrap: wrap; gap: .4rem; }
  .library-actions button { padding: .55rem .65rem; font-size: .78rem; }
  .library > .notice { margin-top: .75rem; }
  .workspace { display: grid; grid-template-columns: minmax(0, 1fr); gap: 1rem; }
  .results { display: grid; gap: 1rem; min-width: 0; }
  .preset-header { display: flex; align-items: end; flex-wrap: wrap; gap: 1rem 2rem; margin: .8rem 0 1.5rem; }
  .preset-header > label:first-child { flex: 1 1 220px; max-width: 32rem; }
  .preset-header .toggle-row { padding-bottom: .65rem; }
  .goals-editor { max-width: 44rem; }
  .lane-editor { margin-top: 1.5rem; }
  .lane-editor > p { font-size: .8rem; }
  .lane-editor > button { margin-top: .75rem; }
  .editor-fields, .digger-controls { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .75rem; margin: .8rem 0 1rem; }
  .editor-table { margin-top: .5rem; }
  .editor-table th, .editor-table td { vertical-align: middle; padding: .45rem .4rem; }
  .editor-table tbody th { width: 100%; }
  .editor-table input { width: 6rem; padding: .45rem; text-align: right; font-variant-numeric: tabular-nums; }
  .editor-table select { width: 6.5rem; padding: .45rem .3rem; }
  .editor-table button { padding: .35rem .4rem; font-size: .72rem; }
  .add-goal { width: auto; max-width: 100%; margin-top: .7rem; }
  .training-editor { margin-top: 1.25rem; padding-left: 1rem; border-left: 2px solid var(--line); }
  .training-editor h4 { margin: 0; font-size: .8rem; font-weight: 500; }
  .reset-training { margin-top: .75rem; }
  .support-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.5rem; }
  .candidate-header { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: .5rem; }
  .support-mode { border-top: 1px solid var(--line); padding-top: 1rem; margin-top: 1.5rem; }
  .support-mode select { width: auto; max-width: 100%; }
  .support-editor { margin-bottom: 1.25rem; }
  .support-heading { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: .4rem; margin-bottom: .6rem; }
  .support-heading button, .beard-order button { padding: .35rem .45rem; font-size: .72rem; }
  .beard-table .group-start > * { border-top: 2px solid var(--line); }
  .beard-order { display: flex; align-items: center; gap: .2rem; }
  .beard-order > span { color: var(--muted); min-width: 1rem; }
  .constraints { border-top: 1px solid var(--line); padding-top: 1rem; margin-top: .5rem; }
  .constraints .editor-fields { max-width: 32rem; }
  .saved-overrides { display: flex; align-items: center; flex-wrap: wrap; gap: .5rem 1rem; margin-top: 1rem; font-size: .75rem; }
  .saved-overrides span { color: var(--muted); }
  .saved-overrides button { padding: .4rem; font-size: .75rem; }
  .preview-action { display: flex; flex-wrap: wrap; gap: .5rem; margin-top: 1.25rem; }
  .candidate-list { display: grid; gap: 1rem; }
  .candidate + .candidate { border-top: 1px solid var(--line); padding-top: 1rem; }
  .candidate-header .muted { margin-top: .2rem; }
  .candidate > .notice { margin-top: .75rem; }
  .candidate details { margin-top: .5rem; }
  .applied .notice { margin-top: .5rem; }
  .allocation-list { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .6rem; margin: .7rem 0; font-size: .8rem; }
  dt { color: var(--muted); }
  dd { margin: .15rem 0 0; font-variant-numeric: tabular-nums; }
  .gear-list { list-style: none; padding: 0; margin: .5rem 0; font-size: .8rem; }
  .gear-list li { display: flex; align-items: baseline; flex-wrap: wrap; gap: .4rem .75rem; padding: .45rem 0; border-bottom: 1px solid var(--line); }
  .gear-list strong { flex: 1; min-width: 100px; font-weight: 500; }
  .gear-list li > span:last-child { margin-left: auto; }
  .sequence-summary { display: grid; gap: .3rem; margin: .8rem 0; padding: .7rem; border-left: 2px solid var(--line); background: var(--input); font-size: .8rem; }
  .stale { opacity: .65; }
  .metric-scroll { max-width: 100%; overflow-x: auto; margin-top: .8rem; }
  table { border-collapse: collapse; width: 100%; font-size: .8rem; font-variant-numeric: tabular-nums; }
  th, td { text-align: right; vertical-align: top; padding: .65rem .4rem; border-bottom: 1px solid var(--line); }
  thead th { color: var(--muted); font-size: .7rem; font-weight: 500; }
  th:first-child { text-align: left; padding-left: 0; }
  th:last-child, td:last-child { padding-right: 0; }
  tbody th { font-weight: 500; }
  td { white-space: nowrap; }
  th small { display: block; font-size: .68rem; }
  .unit { display: block; font-size: .7rem; }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  @media (max-width: 1000px) { .support-grid { grid-template-columns: minmax(0, 1fr); gap: .5rem; } }
  @media (max-width: 600px) { .editor-fields { grid-template-columns: minmax(0, 1fr); } .library-row label { flex-basis: 100%; } .library-actions { width: 100%; } .library-actions button { flex: 1 1 auto; } table { font-size: .72rem; } thead th { font-size: .65rem; } th, td { padding: .6rem .25rem; } .editor-table th, .editor-table td { padding: .45rem .2rem; } .editor-table input { width: 5rem; } .editor-table select { width: 5.75rem; } .beard-order { gap: .1rem; } .beard-order button { padding: .35rem .25rem; } .candidate-header button { padding: .55rem .7rem; } .preview-action button { flex: 1 1 auto; } }
</style>
