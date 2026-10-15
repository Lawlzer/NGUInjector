<script lang="ts">
  import { onMount } from 'svelte';
  import { open } from '@tauri-apps/plugin-dialog';
  import { getStoredRuntimeDir, type JsonObject, type JsonValue } from './lib/api';
  import { createController } from './lib/controller';
  import { assertPlanMetadata, builtInPlans, canCommand, diffObject, fightNames, focusNames, freshPlan, gearNames, heartbeat, mergePatch, object, planDeletionReason, pretty, settingsBounds, validatePlan, validateSettings } from './lib/model';
  import { adventureModes, commandLabels, fightScoreModes, gearGoals, zones } from './lib/options';
  import PlanEditor from './lib/PlanEditor.svelte';
  import JsonEditor from './lib/JsonEditor.svelte';
  import BloodGoals from './lib/BloodGoals.svelte';
  import AugmentTools from './lib/AugmentTools.svelte';
  import IronPillHistory from './lib/IronPillHistory.svelte';
  import IronPillRules from './lib/IronPillRules.svelte';
  import InlineNumber from './lib/InlineNumber.svelte';
  import NguAllocation from './lib/NguAllocation.svelte';
  import NguWeights from './lib/NguWeights.svelte';

  const ui = createController(undefined, undefined, getStoredRuntimeDir() ?? '');
  let activeTab = 'dashboard';
  let pathDraft = $ui.runtimeDir;
  let appliedPath = $ui.runtimeDir;
  let newPlanName = '';
  let gearGoal = 'Fight';
  let fightMode = 'SqrtPowerToughness';
  let rawSettings: { reset: () => void } | undefined;
  let rawPlan: { reset: () => void } | undefined;
  let rawSettingsDirty = false;
  let rawPlanDirty = false;
  let rawIdentity = '';

  const tabs = ['dashboard', 'adventure', 'misc', 'logs', 'raw'];
  const tabLabels: Record<string, string> = { dashboard: 'Dashboard', adventure: 'Adventure', misc: 'Misc', logs: 'Logs', raw: 'Advanced' };
  const toggleGroups = [
    { title: 'Adventure automation', fields: [
      ['_customAdventureEnabled', 'Adventure goal'], ['_customAdventureGoldPush', 'Adventure gold push'],
    ] },
    { title: 'Inventory & Boosts', fields: [['_customAutoBoostEnabled', 'Auto boost']] },
    { title: 'Automation', fields: [
      ['_customAutoMoneyPitEnabled', 'Auto money pit'],
      ['_customAutoBloodSpellsEnabled', 'Auto blood spells']
    ] },
    { title: 'Manual Adventure', fields: [
      ['_manualAdventureEnabled', 'Manual adventure'], ['_manualAdventureBossOnly', 'Bosses only'],
      ['_manualAdventureRecoverHealth', 'Recover health'], ['_manualAdventurePrecastBuffs', 'Precast buffs'],
      ['_manualAdventureFastCombat', 'Fast combat'], ['_manualAdventureBeastMode', 'Beast mode'],
      ['_manualAdventureAllowFallback', 'Allow fallback']
    ] },
    { title: 'Resources', fields: [
      ['_manageEnergy', 'Energy'], ['_manageMagic', 'Magic'], ['_manageGear', 'Gear'],
      ['_manageDiggers', 'Diggers'], ['_manageYggdrasil', 'Yggdrasil'], ['_disableOverlay', 'Hide game overlay']
    ] },
    { title: 'Legacy options', fields: [
      ['_globalEnabled', 'Legacy global automation'], ['_combatEnabled', 'Legacy combat automation'],
      ['_activateFruits', 'Activate fruits'], ['_debugAllocation', 'Allocation logging']
    ] }
  ];
  const numericFields = [
    ['_customAugmentsBloodEstimateSeconds', 'Augments + Blood duration (s)'],
    ['_customBloodSpaghettiGoalPercent', 'Spaghetti goal (%)'],
    ['_customBloodCounterfeitGoalPercent', 'Counterfeit goal (%)'],
    ['_moneyPitThreshold', 'Money pit gold reserve']
  ];
  const actions = ['manualFightPrep', 'manualRebirth', 'quickSave'];
  const utilities = ['safeProbe', 'precacheLoadouts', 'resetRebirthPrep', 'reloadSettings', 'status', 'unload', 'probeCommands'];
  const focusLabels: Record<string, string> = { AugmentsBloodMagic: 'Augments + Blood', TimeMachine: 'Time Machine', EnergyNgu: 'Energy NGU', MagicNgu: 'Magic NGU', Ngu: 'All NGU', AdvancedTraining: 'Advanced Training', ManualTitans: 'Manual Titans', Fight: 'Fight / Boss Push' };

  $: settings = mergePatch($ui.snapshot?.settings ?? {}, $ui.patch);
  $: runtime = $ui.snapshot?.runtime ?? {};
  $: runtimeSettings = object(runtime.settings);
  $: bloodGoalsPending = ['_customBloodSpaghettiGoalPercent', '_customBloodCounterfeitGoalPercent', '_customAutoBloodSpellsEnabled', '_customIronPillSaveSeconds', '_customIronPillMinWorth'].some(key => key in $ui.patch);
  $: connection = heartbeat($ui.snapshot, $ui.now);
  $: pending = Object.keys($ui.patch).length;
  $: deletionReason = planDeletionReason($ui.snapshot, $ui.planName, $ui.now, $ui.patch);
  $: activePlanName = String($ui.snapshot?.settings._customPlanFile ?? runtimeSettings.customPlanFile ?? 'None');
  $: planRunning = ($ui.snapshot?.settings._useCustomRebirthPlan ?? runtimeSettings.useCustomRebirthPlan) === true;
  $: liveAutoRebirth = ($ui.snapshot?.settings._autoRebirth ?? runtimeSettings.autoRebirth) === true;
  $: selectedIsRunning = planRunning && activePlanName === $ui.planName;
  $: selectedAutoRebirth = selectedIsRunning && liveAutoRebirth && $ui.planBase?.AutoRebirthEnabled === true;
  $: canRunPlan = !!$ui.planName && !$ui.busy && !$ui.planDirty && !$ui.planDraftDirty && !$ui.advancedDirty && allowed('applySettingsPatch', $ui.snapshot, $ui.now) && allowed('readPlan', $ui.snapshot, $ui.now);
  $: canEditSettings = !$ui.busy && allowed('applySettingsPatch', $ui.snapshot, $ui.now);
  $: currentFocus = String(runtime.activeFocusGoal ?? '');
  $: appliedQuick = typeof runtime.appliedFocusGoal === 'string' ? runtime.appliedFocusGoal : runtime.focusStatus === 'failed' ? '' : currentFocus;
  $: pendingQuick = $ui.pendingQuickGoal ?? (runtime.focusStatus === 'pending' ? currentFocus : null);
  $: currentZone = zones.find(zone => zone.value === Number(object(runtime.character).adventureZone))?.label ?? '';
  $: if (appliedPath !== $ui.runtimeDir) { appliedPath = $ui.runtimeDir; pathDraft = appliedPath; }
  $: if (rawIdentity !== `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`) {
    rawIdentity = `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`;
    rawSettings?.reset(); rawPlan?.reset();
    rawSettingsDirty = false; rawPlanDirty = false;
  }
  $: ui.advancedDirty(rawSettingsDirty || rawPlanDirty);

  onMount(() => {
    void ui.refresh();
    const timer = window.setInterval(() => {
      void ui.refresh(true);
      if (activeTab === 'logs') void ui.loadLog();
    }, 500);
    const clock = window.setInterval(ui.tick, 500);
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (Object.keys(ui.current().patch).length || ui.current().planDirty || ui.current().planDraftDirty || ui.current().advancedDirty || ui.current().busy) event.preventDefault();
    };
    window.addEventListener('beforeunload', beforeUnload);
    return () => { window.clearInterval(timer); window.clearInterval(clock); window.removeEventListener('beforeunload', beforeUnload); ui.dispose(); };
  });

  const value = (event: Event) => (event.currentTarget as HTMLInputElement | HTMLSelectElement).value;
  function numericInput(event: Event): JsonValue {
    const text = value(event);
    return text.trim() && Number.isFinite(Number(text)) ? Number(text) : text;
  }
  function allowed(command: string, snapshot = $ui.snapshot, now = $ui.now): boolean { return canCommand(snapshot, command, now); }
  function metadata(key: string, fallback: string[], data: JsonObject): string[] {
    const names = data[key];
    return Array.isArray(names) ? names.filter((item): item is string => typeof item === 'string' && fallback.includes(item)) : fallback;
  }
  async function chooseFolder(): Promise<void> {
    try {
      const path = await open({ directory: true, multiple: false, title: 'Select NGU Injector data folder' });
      if (typeof path === 'string') { await ui.changeRuntimeDir(path); pathDraft = ui.current().runtimeDir; }
    } catch (err) { ui.error(err); }
  }
  async function selectPlan(event: Event): Promise<void> {
    const select = event.currentTarget as HTMLSelectElement;
    await ui.loadPlan(select.value);
    select.value = ui.current().planName;
  }
  function scrollToEditor(id = 'plan-editor'): void {
    const target = document.getElementById(id);
    const disclosure = target?.closest('details');
    if (disclosure) disclosure.open = true;
    target?.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }
  function applyRawPlan(draft: JsonObject, base: JsonObject): void {
    assertPlanMetadata(base, draft);
    const merged = freshPlan(base, draft, ui.current().plan!);
    validatePlan(merged);
    ui.editPlan(merged);
  }
  function validateRawPlan(draft: JsonObject, base: JsonObject): void { assertPlanMetadata(base, draft); validatePlan(draft); }
  function validateRawSettings(draft: JsonObject, base: JsonObject): void { validateSettings(diffObject(base, draft), base); }
  function seconds(value: JsonValue | undefined): string {
    if (typeof value !== 'number' || !Number.isFinite(value)) return 'unknown';
    const total = Math.floor(Math.max(0, value));
    return [Math.floor(total / 3600) ? `${Math.floor(total / 3600)}h` : '', Math.floor(total % 3600 / 60) ? `${Math.floor(total % 3600 / 60)}m` : '', total % 60 ? `${total % 60}s` : ''].filter(Boolean).join(' ') || '0s';
  }
</script>

<main class="shell">
  <header class="app-header">
    <h1>NGU Injector</h1>
    <div class="header-controls">
      <span class="pill" class:on={connection.connected}>{connection.connected ? 'Connected' : 'Disconnected'}</span>
      {#if pending || $ui.planDirty}<span class="muted">{$ui.saveError || $ui.error ? 'Changes not saved' : 'Saving changes...'}</span>{/if}
      {#if ($ui.saveError || $ui.error) && (pending || $ui.planDirty)}<button type="button" disabled={$ui.busy || $ui.advancedDirty} on:click={ui.retryAutosave}>Retry saving</button>{/if}
      <button type="button" class="ghost" disabled={$ui.busy || $ui.refreshing} on:click={() => ui.refresh()}>Refresh</button>
    </div>
  </header>
  {#if $ui.saveError || $ui.error}<aside class="notice error" role="alert">{$ui.saveError || $ui.error}</aside>{/if}
  <p class="status-line" role="status">{$ui.busy ? $ui.status || 'Working...' : $ui.status === 'Ready.' || $ui.status.startsWith('Loaded plan') ? '' : $ui.status}</p>
  <nav class="tabs" aria-label="Primary">
    {#each tabs as tab}
      <button type="button" class:active={activeTab === tab} aria-current={activeTab === tab ? 'page' : undefined} on:click={() => { activeTab = tab; if (tab === 'logs') void ui.loadLog(); }}>{tabLabels[tab]}</button>
    {/each}
  </nav>

  {#if activeTab === 'dashboard'}
    <section class="grid dashboard-grid">
      <article class="card span-all" aria-label="Quick">
        <h2>Quick</h2><p class="muted">Pauses the plan and auto-rebirth.</p>
        <div class="command-grid">
          {#each metadata('focusGoals', focusNames, runtime) as goal}
            <button type="button" aria-label={focusLabels[goal] ?? goal} class:active={appliedQuick === goal} class:pending={pendingQuick === goal} aria-pressed={appliedQuick === goal} aria-busy={pendingQuick === goal} disabled={$ui.busy || !allowed('focusGoal', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('focusGoal', { goal, mode: 'toggle' })}>{focusLabels[goal] ?? goal}{#if pendingQuick === goal}<span class="pending-label">{appliedQuick === goal ? 'Updating...' : 'Applying...'}</span>{/if}</button>
          {/each}
          <button type="button" class="ghost" disabled={$ui.busy || !allowed('stopFocus', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('stopFocus')}>Clear quick</button>
        </div>
        <div class="quick-duration">Augments + Blood duration: <InlineNumber label="Augments + Blood duration (s)" value={settings._customAugmentsBloodEstimateSeconds ?? 300} display={seconds(settings._customAugmentsBloodEstimateSeconds ?? 300)} unit="s" min={1} max={100000} disabled={!canEditSettings} onbegin={() => ui.beginSettingEdit('_customAugmentsBloodEstimateSeconds')} /></div>
        <NguAllocation data={object(runtime.nguAllocation)} {activePlanName} connected={connection.connected} />
        <button type="button" class="ghost" on:click={() => scrollToEditor('ngu-weights')}>Edit NGU weights</button>
      </article>
      <article class="card span-all dashboard-actions" aria-label="Actions">
        <h2>Actions</h2>
        <div class="action-buttons">
          {#each actions as command}
            <button type="button" class:danger={command === 'manualRebirth'} disabled={$ui.busy || !allowed(command, $ui.snapshot, $ui.now)} on:click={() => ui.runCommand(command)}>{commandLabels[command]}</button>
          {/each}
        </div>
      </article>
      <article class="card span-2 rebirth-controls" aria-label="Plan controls">
        <div class="card-header"><h2>Plan & Rebirth</h2><span class="pill" class:on={connection.connected && selectedIsRunning}>{!connection.connected ? 'Disconnected' : selectedIsRunning ? 'Running' : 'Stopped'}</span></div>
        <label>Plan
          <select value={$ui.planName} disabled={$ui.busy} on:change={selectPlan}>
            <option value="" disabled>Select plan</option>
            {#each $ui.snapshot?.plans ?? [] as plan}<option value={plan}>{plan}</option>{/each}
          </select>
        </label>
        <div class="button-row plan-live-toggle">
          <button type="button" class="primary" aria-pressed={selectedIsRunning} disabled={selectedIsRunning ? $ui.busy || !allowed('applySettingsPatch', $ui.snapshot, $ui.now) : !canRunPlan} on:click={() => ui.planAction(selectedIsRunning ? 'stop' : 'run', selectedAutoRebirth)}>{selectedIsRunning ? 'Disable plan' : 'Enable plan'}</button>
          <button type="button" class:active={selectedAutoRebirth} aria-pressed={selectedAutoRebirth} disabled={!canRunPlan || !allowed('savePlan', $ui.snapshot, $ui.now)} on:click={() => ui.planAction('run', !selectedAutoRebirth)}>Auto-rebirth: {selectedAutoRebirth ? 'On' : 'Off'}</button>
          <button type="button" class="ghost" on:click={() => scrollToEditor()}>Edit plan</button>
        </div>
        {#if $ui.planDirty || $ui.planDraftDirty || $ui.advancedDirty}<p class="muted">Finish timing edits and wait for changes to save, or apply advanced JSON edits before starting.</p>{/if}
      </article>
      <article class="card current-state">
        <div class="card-header"><h2>Now</h2><span class="muted">{seconds(object(runtime.character).rebirthSeconds)} into this rebirth</span></div>
        <dl class="stats">
          <div><dt>Plan</dt><dd>{activePlanName} {planRunning ? '(running)' : '(stopped)'}</dd></div>
          <div><dt>Quick</dt><dd>{focusLabels[appliedQuick] ?? (appliedQuick || 'None')}</dd></div>
          <div><dt>Zone</dt><dd>{currentZone || 'None'}</dd></div>
        </dl>
        <div class="meter" aria-label="automation loop progress"><div style={`width: ${Math.max(0, Math.min(100, Number(runtime.progressPercent) || 0))}%`}></div></div>
        <details><summary>Activity details</summary><p class="muted">{String(runtime.customStatus ?? 'Waiting for the game.')}</p></details>
      </article>
      <article class="card span-2"><h2>Blood Magic</h2><BloodGoals data={object(runtime.bloodGoals)} connected={connection.connected} pending={bloodGoalsPending} savedGoals={{ spaghetti: settings._customBloodSpaghettiGoalPercent ?? 30, counterfeit: settings._customBloodCounterfeitGoalPercent ?? 400 }} editable={canEditSettings} beginGoalEdit={goal => ui.beginSettingEdit(goal === 'spaghetti' ? '_customBloodSpaghettiGoalPercent' : '_customBloodCounterfeitGoalPercent')} /></article>
      <article class="card"><h2>Iron Pill</h2>
        <div class="dashboard-fields">
          <div class="dashboard-field"><span>Saving time</span><InlineNumber label="Iron Pill saving time (s)" value={settings._customIronPillSaveSeconds ?? 900} display={seconds(settings._customIronPillSaveSeconds ?? 900)} unit="s" min={0} max={604800} disabled={!canEditSettings} onbegin={() => ui.beginSettingEdit('_customIronPillSaveSeconds')} /></div>
          <div class="dashboard-field"><span>Minimum worth</span><InlineNumber label="Iron Pill minimum worth" value={settings._customIronPillMinWorth ?? 0} min={0} max={1e8} integer={false} disabled={!canEditSettings} onbegin={() => ui.beginSettingEdit('_customIronPillMinWorth')} /></div>
        </div>
        <IronPillRules data={object(runtime.ironPill)} connected={connection.connected} /><details><summary>Recent casts</summary><IronPillHistory history={$ui.snapshot?.settings._customIronPillHistory} /></details></article>
    </section>
  {/if}

  <!-- Keep one editor mounted so tab changes retain duration and advanced JSON drafts. -->
  <section id="plan-editor" class="dashboard-plan-editor" aria-label="Plan editor" hidden={activeTab !== 'dashboard'} style:display={activeTab === 'dashboard' ? undefined : 'none'}>
    {#if $ui.plan}
      <PlanEditor plan={$ui.plan} name={$ui.planName} {activePlanName} progress={object(runtime.planProgress)} connected={connection.connected} dirty={$ui.planDirty} namespace={`${$ui.runtimeDir}/${$ui.editorVersion}`} disabled={$ui.busy && !$ui.editing} onchange={ui.editPlan} ondirty={ui.planDraftDirty} />
      <details class="plan-disclosure"><summary>NGU weights</summary>
        <NguWeights plan={$ui.plan} name={$ui.planName} dirty={$ui.planDirty} {activePlanName} disabled={$ui.busy && !$ui.editing} weightsWritable={Number(runtime.planVersion) >= 16 && allowed('readPlan', $ui.snapshot, $ui.now) && allowed('savePlan', $ui.snapshot, $ui.now)} onchange={ui.editPlan} />
      </details>
      <details class="advanced-plan"><summary>Advanced plan JSON</summary>
        <JsonEditor bind:this={rawPlan} title="Plan" value={$ui.plan} validate={validateRawPlan} onapply={applyRawPlan} ondirty={dirty => rawPlanDirty = dirty} disabled={$ui.busy} guidance="Name, history, version, and revision are read-only." />
      </details>
    {:else}<article class="card empty-state">Select or create a plan.</article>{/if}
    <details class="card plan-management">
      <summary>Plan library</summary>
      <h2>Plans</h2><p class="muted">Select, create, or copy a plan. Changes save automatically.</p>
      <div class="plan-management-fields">
        <label>Plan to edit<select value={$ui.planName} disabled={$ui.busy} on:change={selectPlan}><option value="" disabled>Select plan</option>{#each $ui.snapshot?.plans ?? [] as plan}<option value={plan}>{plan}</option>{/each}</select></label>
        <label>New plan name<input bind:value={newPlanName} placeholder="My plan" disabled={$ui.busy} /></label>
        <div class="button-row">
          <button type="button" disabled={$ui.busy || !$ui.snapshot || (connection.connected && !allowed('createPlan', $ui.snapshot, $ui.now)) || !newPlanName} on:click={() => ui.createPlan(newPlanName, false)}>Create</button>
          <button type="button" class="ghost" disabled={$ui.busy || !$ui.snapshot || (connection.connected && !allowed('createPlan', $ui.snapshot, $ui.now)) || !$ui.plan || !newPlanName} on:click={() => ui.createPlan(newPlanName, true)}>Copy selected</button>
        </div>
      </div>
      <div class="button-row" aria-label="Built-in plan shortcuts">{#each builtInPlans as plan}<button type="button" class="ghost" disabled={$ui.busy || !$ui.snapshot?.plans.includes(plan.name)} on:click={() => ui.loadPlan(plan.name)}>{plan.label}</button>{/each}</div>
      <details><summary>Delete plan</summary><button type="button" class="danger" disabled={$ui.busy || !!deletionReason} title={deletionReason || 'Delete permanently'} on:click={ui.deletePlan}>Delete</button>{#if deletionReason}<p class="muted">{deletionReason}</p>{/if}</details>
    </details>
  </section>

  {#if activeTab === 'adventure' || activeTab === 'misc'}
    <section class="grid tools-grid" inert={$ui.busy && !$ui.editing}>
      <article class="card span-all"><h2>{tabLabels[activeTab]}</h2><p class="muted">Changes save automatically.</p></article>
      {#if activeTab === 'adventure'}
        <article class="card"><h2>Adventure zones</h2><div class="form-grid two">
          <label>Adventure mode<select value={settings._customAdventureMode ?? 0} on:change={event => ui.editSetting('_customAdventureMode', numericInput(event))}>{#each adventureModes as mode}<option value={mode.value}>{mode.label}</option>{/each}</select></label>
          {#if settings._customAdventureMode === 1}<label>Farm zone<select value={settings._customAdventureZone ?? 0} on:change={event => ui.editSetting('_customAdventureZone', numericInput(event))}>{#each zones as zone}<option value={zone.value}>{zone.label}</option>{/each}</select></label>{/if}
          <label>Manual adventure zone<select value={settings._manualAdventureZone ?? 0} on:change={event => ui.editSetting('_manualAdventureZone', numericInput(event))}>{#each zones as zone}<option value={zone.value}>{zone.label}</option>{/each}</select></label>
        </div></article>
      {/if}
      {#each toggleGroups.filter(group => activeTab === 'adventure' ? ['Adventure automation', 'Manual Adventure', 'Inventory & Boosts'].includes(group.title) : ['Automation', 'Resources'].includes(group.title)) as group}<article class="card"><h2>{group.title}</h2><div class="toggle-list">{#each group.fields as [key, label]}<label class="toggle-row"><input type="checkbox" checked={settings[key] === true} on:change={event => ui.editSetting(key, (event.currentTarget as HTMLInputElement).checked)} /><span><strong>{label}</strong></span></label>{/each}</div>
        {#if group.title === 'Inventory & Boosts'}<label>Cube Power:Toughness ratio<input type="number" min={settingsBounds._customAutoBoostCubePowerRatio[0]} max={settingsBounds._customAutoBoostCubePowerRatio[1]} step="any" value={String(settings._customAutoBoostCubePowerRatio ?? 2)} on:input={event => ui.editSetting('_customAutoBoostCubePowerRatio', numericInput(event), 350)} /></label>{/if}
      </article>{/each}
      {#if activeTab === 'misc'}
      <article class="card"><h2>Targets</h2><div class="form-grid two">{#each numericFields as [key, label]}<label>{label}<input type="number" min={settingsBounds[key][0]} max={settingsBounds[key][1]} step={settingsBounds[key][2] === false ? 'any' : '1'} value={String(settings[key] ?? '')} on:input={event => ui.editSetting(key, numericInput(event), 350)} /></label>{/each}</div></article>
      <article class="card"><h2>Iron Pill</h2><div class="form-grid two">
        <label>Saving time (s)<input aria-label="Saving time (s)" aria-describedby="pill-time-help" type="number" min="0" max="604800" step="1" value={String(settings._customIronPillSaveSeconds ?? 900)} on:input={event => ui.editSetting('_customIronPillSaveSeconds', numericInput(event), 350)} /><small id="pill-time-help">0 disables the time target.</small></label>
        <label>Minimum worth<input aria-label="Minimum worth" aria-describedby="pill-worth-help" type="number" min="0" max="1e8" step="any" value={String(settings._customIronPillMinWorth ?? 0)} on:input={event => ui.editSetting('_customIronPillMinWorth', numericInput(event), 350)} /><small id="pill-worth-help">0 disables the worth target. Max 100M.</small></label>
      </div><IronPillRules data={object(runtime.ironPill)} connected={connection.connected} /></article>
      <article class="card"><details><summary>Data & Setup</summary>
        <label for="runtime-dir">Data folder</label><div class="runtime-row">
          <input id="runtime-dir" bind:value={pathDraft} spellcheck="false" disabled={$ui.busy} />
          <button type="button" disabled={$ui.busy} on:click={async () => { await ui.changeRuntimeDir(pathDraft); pathDraft = ui.current().runtimeDir; }}>Use folder</button>
          <button type="button" disabled={$ui.busy} on:click={chooseFolder}>Browse</button>
        </div>
        <label>Allocation profile<select value={settings._allocationFile ?? ''} on:change={event => ui.editSetting('_allocationFile', value(event))}>{#each $ui.snapshot?.profiles ?? [] as file}<option value={file}>{file}</option>{/each}</select></label>
        <p class="muted">Window preference applies after restarting the game.</p>
        <div class="button-row"><button type="button" disabled={$ui.busy || !$ui.runtimeDir} on:click={() => ui.marker(true)}>Use this app</button><button type="button" class="ghost" disabled={$ui.busy || !$ui.runtimeDir} on:click={() => ui.marker(false)}>Use old window</button></div>
      </details></article>
      <article class="card"><details><summary>Legacy options</summary><div class="toggle-list">{#each toggleGroups.find(group => group.title === 'Legacy options')?.fields ?? [] as [key, label]}<label class="toggle-row"><input type="checkbox" checked={settings[key] === true} on:change={event => ui.editSetting(key, (event.currentTarget as HTMLInputElement).checked)} /><span><strong>{label}</strong></span></label>{/each}</div></details></article>
      <article class="card"><h2>Manual inventory</h2><p class="muted">Select a gear goal to preview or equip a loadout manually.</p><div class="form-grid">
        <label>Goal<select bind:value={gearGoal}>{#each metadata('gearGoals', gearNames, runtime) as goal}<option value={goal}>{gearGoals[gearNames.indexOf(goal)]?.label ?? goal}</option>{/each}</select></label>
        <label>Fight scoring<select bind:value={fightMode}>{#each metadata('fightScoreModes', fightNames, runtime) as mode}<option value={mode}>{fightScoreModes[fightNames.indexOf(mode)]?.label ?? mode}</option>{/each}</select></label>
      </div><div class="button-row"><button type="button" disabled={$ui.busy || !allowed('previewGear', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('previewGear', { goal: gearGoal, fightScoreMode: fightMode })}>Preview gear</button><button type="button" disabled={$ui.busy || !allowed('optimizeGear', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('optimizeGear', { goal: gearGoal, fightScoreMode: fightMode })}>Equip gear</button></div>
      {#if ($ui.lastResult?.command === 'previewGear' || $ui.lastResult?.command === 'optimizeGear') && $ui.lastResult?.data}<details open><summary>Result</summary><pre>{String($ui.lastResult.message ?? '')}</pre></details>{/if}</article>
      <AugmentTools canPreview={allowed('previewAugments', $ui.snapshot, $ui.now)} canAllocate={allowed('allocateAugments', $ui.snapshot, $ui.now)} busy={$ui.busy} savedEstimate={$ui.snapshot?.settings._customAugmentsBloodEstimateSeconds} estimatePending={'_customAugmentsBloodEstimateSeconds' in $ui.patch} result={$ui.augmentResult} oncommand={ui.runCommand} />
      <article class="card span-all"><h2>Utilities</h2><div class="command-grid">{#each utilities as command}<button type="button" class:danger={command === 'unload' || command === 'probeCommands'} disabled={$ui.busy || !allowed(command, $ui.snapshot, $ui.now)} on:click={() => ui.runCommand(command)}>{commandLabels[command]}</button>{/each}</div></article>
      <article class="card span-all"><details><summary>Recent actions</summary><div class="result-list">{#each $ui.snapshot?.command_results ?? [] as result}<div class="result" class:ok={result.success}><strong>{commandLabels[result.command] ?? result.command}</strong><span>{result.success ? 'Done' : result.message}</span><details><summary>Details</summary><p>{result.message}</p><small>{result.completed_at}</small></details></div>{:else}<p class="muted">No actions yet.</p>{/each}</div></details></article>
      {/if}
    </section>
  {/if}

  {#if activeTab === 'logs'}
    <section class="logs-layout"><article class="card log-sidebar"><h2>Logs</h2><select aria-label="Log file" value={$ui.logName} on:change={event => ui.loadLog(value(event))}>{#each $ui.snapshot?.logs ?? [] as log}<option value={log.name}>{log.name}</option>{/each}</select><button type="button" on:click={() => ui.loadLog()}>Refresh log</button></article><article class="card log-output"><div class="card-header"><h2>{$ui.logName}</h2></div><pre>{$ui.logText || 'No entries.'}</pre></article></section>
  {/if}

  <!-- Keep advanced buffers mounted across tab changes so navigation cannot silently lose JSON edits. -->
  <section class="grid raw-grid" hidden={activeTab !== 'raw'} style:display={activeTab === 'raw' ? undefined : 'none'}>
    <JsonEditor bind:this={rawSettings} title="Settings" value={settings} validate={validateRawSettings} onapply={ui.applyRawSettings} ondirty={dirty => rawSettingsDirty = dirty} disabled={$ui.busy} guidance="Leave unrecognized fields unchanged." />
    <article class="card span-2"><details><summary>Game data</summary><pre>{pretty(runtime)}</pre></details></article>
  </section>
</main>
