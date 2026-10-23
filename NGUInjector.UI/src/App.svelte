<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { open } from '@tauri-apps/plugin-dialog';
  import * as api from './lib/api';
  import type { JsonObject, JsonValue } from './lib/api';
  import { createController, planStageOverrideReason, type EditSession } from './lib/controller';
  import { assertPlanMetadata, builtInPlans, canCommand, diffObject, equal, fightNames, freshPlan, gearNames, heartbeat, mergePatch, object, planDeletionReason, pretty, settingsBounds, validatePlan, validateSettings } from './lib/model';
  import { adventureModes, commandLabels, fightScoreModes, gearGoals, manualCombatLoadouts, zones } from './lib/options';
  import PlanEditor from './lib/PlanEditor.svelte';
  import PlanGear from './lib/PlanGear.svelte';
  import JsonEditor from './lib/JsonEditor.svelte';
  import BloodGoals from './lib/BloodGoals.svelte';
  import AugmentTools from './lib/AugmentTools.svelte';
  import IronPillHistory from './lib/IronPillHistory.svelte';
  import RebirthHistory from './lib/RebirthHistory.svelte';
  import IronPillRules from './lib/IronPillRules.svelte';
  import InlineNumber from './lib/InlineNumber.svelte';
  import NguAllocation from './lib/NguAllocation.svelte';
  import NguWeights from './lib/NguWeights.svelte';
  import AutoExp from './lib/AutoExp.svelte';
  import CombatPrep from './lib/CombatPrep.svelte';
  import Presets from './lib/Presets.svelte';
  import AdventureRules from './lib/AdventureRules.svelte';
  import ManualTitanFights from './lib/ManualTitanFights.svelte';
  import ManualAdventureBonuses from './lib/ManualAdventureBonuses.svelte';
  import ZoneReference from './lib/ZoneReference.svelte';
  import EquipmentBoosts from './lib/EquipmentBoosts.svelte';
  import ExecutionReview from './lib/ExecutionReview.svelte';
  import SaveSafety from './lib/SaveSafety.svelte';
  import InjectorErrors from './lib/InjectorErrors.svelte';

  const ui = createController({ ...api, async savePlan(runtimeDir: string, name: string, plan: JsonObject): Promise<JsonObject> {
    // Recheck queued autosaves and retries at the transport boundary as well as at edit time.
    checkBloodWrite(plan);
    return api.savePlan(runtimeDir, name, plan);
  } }, undefined, api.getStoredRuntimeDir() ?? '');
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
  let localDrafts: Record<string, boolean> = {};

  function setLocalDraftDirty(editor: string, dirty: boolean, identity: string): void {
    const state = ui.current();
    // Ignore late notifications from editors in an already discarded context.
    if (identity !== `${state.runtimeDir}/${state.planName}/${state.editorVersion}`) return;
    localDrafts = { ...localDrafts, [editor]: dirty };
    ui.planDraftDirty(Object.values(localDrafts).some(Boolean));
  }

  const tabs = ['dashboard', 'plan', 'presets', 'adventure', 'misc', 'logs', 'raw'];
  const tabLabels: Record<string, string> = { dashboard: 'Dashboard', plan: 'Plan & Rebirth', presets: 'Presets', adventure: 'Adventure', misc: 'Misc', logs: 'Logs', raw: 'Advanced' };
  const toggleGroups = [
    { title: 'Adventure automation', fields: [
      ['_customAdventureEnabled', 'Adventure goal'], ['_customAdventureGoldPush', 'Adventure gold push'],
    ] },
    { title: 'Inventory & Boosts', fields: [['_customAutoBoostEnabled', 'Auto boost']] },
    { title: 'Automation', fields: [
      ['_customAutoMoneyPitEnabled', 'Auto money pit']
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
    ] }
  ];
  const actions = ['manualFightPrep', 'manualRebirth', 'quickSave'];
  const utilities = ['safeProbe', 'precacheLoadouts', 'resetRebirthPrep', 'reloadSettings', 'status', 'unload', 'probeCommands'];
  const focusLabels: Record<string, string> = { AugmentsBloodMagic: 'Augments + Blood', TimeMachine: 'Time Machine', EnergyNgu: 'Energy NGU', MagicNgu: 'Magic NGU', Ngu: 'All NGU', AdvancedTraining: 'Advanced Training', ManualTitans: 'Manual Titans', Fight: 'Fight / Boss Push' };
  const bloodFields = ['AutoBloodSpellsEnabled', 'BloodSpaghettiGoalPercent', 'BloodCounterfeitGoalPercent', 'IronPillSaveSeconds', 'IronPillMinWorth'];

  $: settings = mergePatch($ui.snapshot?.settings ?? {}, $ui.patch);
  $: runtime = $ui.snapshot?.runtime ?? {};
  $: runtimeSettings = object(runtime.settings);
  $: maxItopodPush = object(runtime.maxItopodPush);
  $: maxItopodPushActive = maxItopodPush.active === true;
  $: manualPushMode = maxItopodPushActive || ((settings._customAdventureEnabled === true || settings._manualAdventureEnabled === true) && settings._customAdventureMode === 3);
  $: manualRuleEnabled = Array.isArray(settings._customAdventureRules) && settings._customAdventureRules.some(rule => object(rule).Enabled === true && object(rule).Mode === 4);
  $: manualAdventureOptions = settings._manualAdventureEnabled === true || manualRuleEnabled;
  $: sharedManualCombatOptions = manualAdventureOptions || settings._manualTitanEnabled === true;
  $: bloodGoalsPending = $ui.plan
    ? [...bloodFields, 'AutoIronPillEnabled'].some(key => !equal($ui.plan?.[key], $ui.planBase?.[key]))
    : ['_customBloodSpaghettiGoalPercent', '_customBloodCounterfeitGoalPercent', '_customAutoBloodSpellsEnabled', '_customIronPillSaveSeconds', '_customIronPillMinWorth'].some(key => key in $ui.patch);
  $: bloodValues = {
    AutoBloodSpellsEnabled: $ui.plan?.AutoBloodSpellsEnabled ?? settings._customAutoBloodSpellsEnabled ?? false,
    BloodSpaghettiGoalPercent: $ui.plan?.BloodSpaghettiGoalPercent ?? settings._customBloodSpaghettiGoalPercent ?? 30,
    BloodCounterfeitGoalPercent: $ui.plan?.BloodCounterfeitGoalPercent ?? settings._customBloodCounterfeitGoalPercent ?? 400,
    IronPillSaveSeconds: $ui.plan?.IronPillSaveSeconds ?? settings._customIronPillSaveSeconds ?? 900,
    IronPillMinWorth: $ui.plan?.IronPillMinWorth ?? settings._customIronPillMinWorth ?? 0
  };
  $: bloodWritable = $ui.plan
    ? !$ui.busy && !!$ui.snapshot && (!$ui.snapshot.connected || runtime.planBloodMagicVersion === 1 && allowed('readPlan', $ui.snapshot, $ui.now) && allowed('savePlan', $ui.snapshot, $ui.now))
    : canEditSettings;
  $: bloodTelemetry = object(runtime.bloodGoals);
  $: bloodPlanRunning = (runtimeSettings.useCustomRebirthPlan ?? $ui.snapshot?.settings._useCustomRebirthPlan) === true;
  $: bloodRuntimeName = typeof bloodTelemetry.planName === 'string' ? bloodTelemetry.planName : bloodPlanRunning ? String(runtimeSettings.customPlanFile ?? activePlanName) : '';
  $: bloodTelemetryMatches = $ui.plan
    ? bloodPlanRunning && bloodTelemetry.planName === $ui.planName
    : !bloodPlanRunning && !bloodTelemetry.planName;
  $: bloodTelemetryReason = $ui.plan ? 'Live status is not for the editing plan.' : 'Live status is for a running plan.';
  $: ironTelemetry = object(runtime.ironPill);
  $: ironTelemetryMatches = bloodTelemetryMatches && (!$ui.plan || ironTelemetry.planName === $ui.planName)
    && (ironTelemetry.saveSeconds === undefined || equal(ironTelemetry.saveSeconds, bloodValues.IronPillSaveSeconds))
    && (ironTelemetry.minWorth === undefined || equal(ironTelemetry.minWorth, bloodValues.IronPillMinWorth));
  $: connection = heartbeat($ui.snapshot, $ui.now);
  $: pending = Object.keys($ui.patch).length;
  $: presetLibraryPending = $ui.presetLibraryDirty;
  $: deletionReason = planDeletionReason($ui.snapshot, $ui.planName, $ui.now, $ui.patch);
  $: activePlanName = String($ui.snapshot?.settings._customPlanFile ?? runtimeSettings.customPlanFile ?? 'None');
  $: planRunning = ($ui.snapshot?.settings._useCustomRebirthPlan ?? runtimeSettings.useCustomRebirthPlan) === true;
  $: selectedIsRunning = planRunning && activePlanName === $ui.planName;
  $: canRunPlan = !!$ui.planName && !$ui.busy && !$ui.planDirty && !$ui.planDraftDirty && !$ui.advancedDirty && allowed('applySettingsPatch', $ui.snapshot, $ui.now) && allowed('readPlan', $ui.snapshot, $ui.now);
  $: canEditSettings = !$ui.busy && !!$ui.snapshot && (!$ui.snapshot.connected || allowed('applySettingsPatch', $ui.snapshot, $ui.now));
  $: stageActionReason = planStageOverrideReason($ui, $ui.now);
  $: currentFocus = String(runtime.activeFocusGoal ?? '');
  $: globalPreset = object(runtime.loadoutPreset);
  $: appliedQuick = typeof runtime.appliedFocusGoal === 'string' ? runtime.appliedFocusGoal : runtime.focusStatus === 'failed' ? '' : currentFocus;
  $: pendingQuick = $ui.pendingQuickGoal ?? (runtime.focusStatus === 'pending' ? currentFocus : null);
  $: currentZone = zones.find(zone => zone.value === Number(object(runtime.character).adventureZone))?.label ?? '';
  $: if (appliedPath !== $ui.runtimeDir) { appliedPath = $ui.runtimeDir; pathDraft = appliedPath; }
  $: if (rawIdentity !== `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`) {
    rawIdentity = `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`;
    localDrafts = {};
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
      if (Object.keys(ui.current().patch).length || ui.current().planDirty || ui.current().planDraftDirty || ui.current().advancedDirty || ui.current().presetLibraryDirty || ui.current().busy) event.preventDefault();
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
  function applyRawPlan(draft: JsonObject, base: JsonObject): void {
    assertPlanMetadata(base, draft);
    const merged = freshPlan(base, draft, ui.current().plan!);
    validatePlan(merged);
    checkBloodWrite(merged);
    ui.editPlan(merged);
  }
  function checkBloodWrite(draft: JsonObject): void {
    const state = ui.current();
    if (![...bloodFields, 'AutoIronPillEnabled'].some(key => !equal(state.planBase?.[key], draft[key]))) return;
    if (state.snapshot?.connected && (state.snapshot.runtime.planBloodMagicVersion !== 1 || !canCommand(state.snapshot, 'readPlan') || !canCommand(state.snapshot, 'savePlan'))) {
      throw new Error('Plan Blood Magic edits require an updated injector advertising planBloodMagicVersion: 1. Update and reconnect, or edit offline.');
    }
  }
  function beginBloodEdit(key: string, legacyKey: string): EditSession {
    if (!ui.current().plan) return ui.beginSettingEdit(legacyKey);
    const session = ui.beginPlanEdit(key);
    return { cancel: session.cancel, async commit(next) {
      try { checkBloodWrite({ ...ui.current().plan, [key]: next }); }
      catch (err) { ui.error(err); return false; }
      return session.commit(next);
    } };
  }
  function editBloodToggle(key: string, legacyKey: string, event: Event): void {
    const checked = (event.currentTarget as HTMLInputElement).checked;
    if (!ui.current().plan) { ui.editSetting(legacyKey, checked); return; }
    editBloodPlan({ ...ui.current().plan, [key]: checked });
  }
  function editBloodPlan(plan: JsonObject): void {
    try { checkBloodWrite(plan); ui.editPlan(plan); }
    catch (err) { ui.error(err); }
  }
  function validateRawPlan(draft: JsonObject, base: JsonObject): void { assertPlanMetadata(base, draft); validatePlan(draft); checkBloodWrite(draft); }
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
      {#if (pending || $ui.planDirty || presetLibraryPending) && ($ui.saveError || $ui.error)}<span class="muted">Changes not saved</span>{/if}
      {#if ($ui.saveError || $ui.error) && (pending || $ui.planDirty || presetLibraryPending)}<button type="button" disabled={$ui.busy} on:click={ui.retryAutosave}>Retry saving</button>{/if}
      <button type="button" class="ghost" disabled={$ui.busy || $ui.refreshing} on:click={() => ui.refresh()}>Refresh</button>
    </div>
  </header>
  <SaveSafety data={runtime.saveSafety} connected={connection.connected} />
  <InjectorErrors state={$ui} connected={connection.connected} onlogs={async () => { activeTab = 'logs'; void ui.loadLog(); await tick(); document.querySelector<HTMLSelectElement>('[aria-label="Log file"]')?.focus(); }} onreview={async () => { await tick(); document.getElementById('execution-review')?.focus(); }} />
  <p class="status-line" role="status">{$ui.busy ? 'Working...' : ''}</p>
  <nav class="tabs" aria-label="Primary">
    {#each tabs as tab}
      <button type="button" class:active={activeTab === tab} aria-current={activeTab === tab ? 'page' : undefined} on:click={() => { activeTab = tab; if (tab === 'logs') void ui.loadLog(); if (tab === 'misc') void ui.refresh(true); }}>{tabLabels[tab]}</button>
    {/each}
  </nav>

  <div id="execution-review" tabindex="-1">
  {#key `${$ui.runtimeDir}/${runtime.startedAt ?? ''}`}
    <ExecutionReview review={object(runtime.executionReview)} lastResult={$ui.lastResult} busy={$ui.busy} canPreview={allowed('previewExecutionRebind')} canApply={allowed('applyExecutionRebind')} canRestart={allowed('previewExecutionRestart')} oncommand={ui.runCommand} />
  {/key}
  </div>

  <section class="global-library" hidden={activeTab !== 'presets' && activeTab !== 'dashboard'} style:display={activeTab === 'presets' || activeTab === 'dashboard' ? undefined : 'none'}>
    <Presets state={$ui} compact={activeTab === 'dashboard'} onmanage={() => activeTab = 'presets'} onchange={ui.setPresetLibrary} onselect={ui.selectLoadoutPreset} oncandidate={ui.selectLoadoutPresetCandidate} onpreview={ui.previewLoadoutPreset} onapply={ui.applyLoadoutPreset} onequip={ui.equipGlobalPreset} onstop={() => ui.runCommand('stopFocus')} onreload={ui.loadPresetLibrary} ondiscard={ui.discardPresetLibrary} />
  </section>

  {#if activeTab === 'dashboard' || activeTab === 'plan'}
    <section class="grid dashboard-grid">
      <article class="card span-2 rebirth-controls" class:running={connection.connected && selectedIsRunning} class:stopped={connection.connected && !selectedIsRunning} aria-label="Plan controls" aria-describedby="plan-execution-status">
        <span id="plan-execution-status" hidden>{!connection.connected ? 'Disconnected' : selectedIsRunning ? 'Selected plan is running' : 'Selected plan is stopped'}</span>
        <div class="card-header"><h2>Plan & Rebirth</h2>
          <button type="button" class="primary" aria-pressed={selectedIsRunning} disabled={selectedIsRunning ? $ui.busy || !allowed('applySettingsPatch', $ui.snapshot, $ui.now) : !canRunPlan} on:click={() => ui.planAction(selectedIsRunning ? 'stop' : 'run', $ui.planBase?.AutoRebirthEnabled === true)}>{selectedIsRunning ? 'Disable plan' : 'Enable plan'}</button>
        </div>
        <label>Plan
          <select value={$ui.planName} disabled={$ui.busy} on:change={selectPlan}>
            <option value="" disabled>Select plan</option>
            {#each $ui.snapshot?.plans ?? [] as plan}<option value={plan}>{plan}</option>{/each}
          </select>
        </label>
        {#if activeTab === 'dashboard'}<button type="button" class="ghost" on:click={() => activeTab = 'plan'}>Edit plan</button>{/if}
        {#if $ui.planDirty || $ui.planDraftDirty || $ui.advancedDirty}<p class="muted">Finish or discard pending edits before starting.</p>{/if}
      </article>
      {#if activeTab === 'dashboard'}
      <article class="card current-state" aria-label="Now">
        <div class="card-header"><h2>Now</h2><span class="muted" title="Time since rebirth">{seconds(object(runtime.character).rebirthSeconds)}</span></div>
        <dl class="stats">
          {#if activePlanName !== $ui.planName && planRunning}<div><dt>Running plan</dt><dd>{activePlanName}</dd></div>{/if}
          <div><dt>Quick</dt><dd>{globalPreset.active === true ? String(globalPreset.appliedName ?? globalPreset.name ?? globalPreset.id) : focusLabels[appliedQuick] ?? (appliedQuick || 'None')}{#if globalPreset.active === true && globalPreset.savedChangesRequireReequip === true}<small>Saved changes require re-equip</small>{/if}</dd></div>
          <div><dt>Zone</dt><dd>{currentZone || 'None'}</dd></div>
        </dl>
        <details><summary>Activity details</summary><p class="muted">{String(runtime.customStatus ?? 'Waiting for the game.')}</p></details>
        <div class="action-buttons" aria-label="Actions">
          {#each ['Fight', 'ManualTitans'] as goal}<button type="button" aria-label={focusLabels[goal]} class:active={appliedQuick === goal} class:pending={pendingQuick === goal} aria-pressed={appliedQuick === goal} aria-busy={pendingQuick === goal} disabled={$ui.busy || !allowed('focusGoal', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('focusGoal', { goal, mode: 'toggle' })}>{focusLabels[goal]}{#if pendingQuick === goal}<span class="pending-label">{appliedQuick === goal ? 'Updating...' : 'Applying...'}</span>{/if}</button>{/each}
          <button type="button" class="ghost" disabled={$ui.busy || !allowed('stopFocus', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('stopFocus')}>Clear quick</button>
          {#each actions as command}
            <button type="button" class:danger={command === 'manualRebirth'} disabled={$ui.busy || !allowed(command, $ui.snapshot, $ui.now)} on:click={() => ui.runCommand(command)}>{commandLabels[command]}</button>
          {/each}
        </div>
      </article>
      {/if}
    </section>
  {/if}

  <!-- Keep one editor mounted so tab changes retain duration drafts. -->
  <section id="plan-editor" class="dashboard-plan-editor" aria-label="Plan editor" hidden={activeTab !== 'plan'} style:display={activeTab === 'plan' ? undefined : 'none'}>
    {#if $ui.plan}
      {#key `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`}
      {@const identity = `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`}
      <PlanEditor plan={$ui.plan} presetLibrary={$ui.presetLibraryBase} name={$ui.planName} {activePlanName} progress={object(runtime.planProgress)} connected={connection.connected} dirty={$ui.planDirty} {stageActionReason} onstageaction={ui.setPlanStage} namespace={`${$ui.runtimeDir}/${$ui.editorVersion}`} disabled={$ui.busy && !$ui.editing} onchange={ui.editPlan} ondirty={dirty => setLocalDraftDirty('plan', dirty, identity)} />
      {/key}
    {:else}<article class="card empty-state">Select or create a plan.</article>{/if}
  </section>

    <section class="grid dashboard-support" aria-label="Blood Magic and Iron Pill" hidden={activeTab !== 'dashboard'} style:display={activeTab === 'dashboard' ? undefined : 'none'}>
      {#key `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`}
      {@const identity = `${$ui.runtimeDir}/${$ui.planName}/${$ui.editorVersion}`}
      <article class="card span-all" aria-label="Blood Magic"><h2>Blood Magic</h2>
        <p class="muted">{$ui.plan ? 'Plan:' : 'Editing:'} <strong>{$ui.plan ? $ui.planName : 'Global defaults'}</strong>{#if connection.connected && !bloodTelemetryMatches}{' / Running: '}<strong>{bloodRuntimeName || 'Quick'}</strong>{/if}</p>
        {#if $ui.plan}
          {#if $ui.snapshot?.connected && runtime.planBloodMagicVersion !== 1}<p class="notice" role="status">Update and reconnect the injector to edit plan Blood Magic.</p>{/if}
          <button type="button" class="ghost" disabled={!bloodWritable || bloodFields.every(key => $ui.plan?.[key] == null)} on:click={() => editBloodPlan({ ...$ui.plan, ...Object.fromEntries(bloodFields.map(key => [key, null])) })}>Use global defaults</button>
        {/if}
        <label class="toggle-row"><input type="checkbox" checked={bloodValues.AutoBloodSpellsEnabled === true} disabled={!bloodWritable} on:change={event => editBloodToggle('AutoBloodSpellsEnabled', '_customAutoBloodSpellsEnabled', event)} /><span><strong>Auto blood spells</strong></span></label>
        <BloodGoals data={bloodTelemetryMatches ? bloodTelemetry : { available: false, reason: bloodTelemetryReason }} connected={connection.connected} pending={bloodGoalsPending || bloodTelemetry.autoBloodSpellsEnabled !== undefined && !equal(bloodTelemetry.autoBloodSpellsEnabled, bloodValues.AutoBloodSpellsEnabled)} savedGoals={{ spaghetti: bloodValues.BloodSpaghettiGoalPercent, counterfeit: bloodValues.BloodCounterfeitGoalPercent }} editable={bloodWritable} beginGoalEdit={goal => beginBloodEdit(goal === 'spaghetti' ? 'BloodSpaghettiGoalPercent' : 'BloodCounterfeitGoalPercent', goal === 'spaghetti' ? '_customBloodSpaghettiGoalPercent' : '_customBloodCounterfeitGoalPercent')} ondirty={dirty => setLocalDraftDirty('blood', dirty, identity)} />
        <h3>Iron Pill</h3>
        {#if $ui.plan}<label class="toggle-row"><input type="checkbox" checked={($ui.plan.AutoIronPillEnabled ?? true) === true} disabled={!bloodWritable} on:change={event => editBloodToggle('AutoIronPillEnabled', '', event)} /><span><strong>Auto Iron Pill</strong></span></label>{/if}
        <div class="dashboard-fields">
          <div class="dashboard-field"><span>Saving time</span><InlineNumber label="Iron Pill saving time (s)" value={bloodValues.IronPillSaveSeconds} display={seconds(bloodValues.IronPillSaveSeconds)} unit="s" min={0} max={604800} disabled={!bloodWritable} onbegin={() => beginBloodEdit('IronPillSaveSeconds', '_customIronPillSaveSeconds')} ondirty={dirty => setLocalDraftDirty('pillTime', dirty, identity)} /></div>
          <div class="dashboard-field"><span>Minimum worth</span><InlineNumber label="Iron Pill minimum worth" value={bloodValues.IronPillMinWorth} min={0} max={1e8} integer={false} disabled={!bloodWritable} onbegin={() => beginBloodEdit('IronPillMinWorth', '_customIronPillMinWorth')} ondirty={dirty => setLocalDraftDirty('pillWorth', dirty, identity)} /></div>
        </div>
        <IronPillRules data={ironTelemetryMatches && !bloodGoalsPending ? ironTelemetry : { reason: bloodTelemetryMatches ? 'Updating live status...' : bloodTelemetryReason }} connected={connection.connected} />
      </article>
      {/key}
    </section>

  <section class="grid tools-grid" aria-label="NGU" hidden={activeTab !== 'misc'} style:display={activeTab === 'misc' ? undefined : 'none'}>
    <NguWeights {settings} disabled={$ui.busy && !$ui.editing} weightsWritable={canEditSettings && runtime.globalNguWeightsVersion === 1} onedit={ui.editSetting} />
    <article class="card span-all" aria-label="Observed NGU allocation"><h2>Live NGU allocation</h2><NguAllocation data={object(runtime.nguAllocation)} connected={connection.connected} /></article>
    <EquipmentBoosts {settings} telemetry={object(runtime.equipmentBoosts)} connected={connection.connected} disabled={!canEditSettings} onedit={ui.editSetting} />
  </section>

  {#if activeTab === 'adventure' || activeTab === 'misc'}
    <section class="grid tools-grid" inert={$ui.busy && !$ui.editing}>
      {#each toggleGroups.filter(group => activeTab === 'adventure' ? ['Adventure automation', 'Manual Adventure', 'Inventory & Boosts'].includes(group.title) : ['Automation', 'Resources'].includes(group.title)) as group}
        <article class="card" aria-label={group.title}><h2>{group.title}</h2>
          <div class="toggle-list">
            {#each group.fields as [key, label]}
              {#if group.title !== 'Manual Adventure' || key === '_manualAdventureEnabled' || (sharedManualCombatOptions && (!['_manualAdventureBossOnly', '_manualAdventureAllowFallback'].includes(key) || manualAdventureOptions && !manualPushMode))}
                <label class="toggle-row"><input type="checkbox" checked={settings[key] === true} on:change={event => ui.editSetting(key, (event.currentTarget as HTMLInputElement).checked)} /><span><strong>{label}</strong></span></label>
              {/if}
            {/each}
          </div>
          {#if group.title === 'Automation'}
            <label>Money pit gold threshold<input type="number" min={settingsBounds._moneyPitThreshold[0]} max={settingsBounds._moneyPitThreshold[1]} step="any" value={String(settings._moneyPitThreshold ?? 0)} on:input={event => ui.editSetting('_moneyPitThreshold', numericInput(event), 350)} /></label>
          {/if}
          {#if group.title === 'Adventure automation'}
            <div class="form-grid">
              <label>Adventure mode<select value={settings._customAdventureMode === 3 ? 0 : settings._customAdventureMode ?? 0} on:change={event => ui.editSetting('_customAdventureMode', numericInput(event))}>{#each adventureModes.filter(mode => mode.value !== 3) as mode}<option value={mode.value}>{mode.label}</option>{/each}</select></label>
              {#if settings._customAdventureMode === 1}<label>Farm zone<select value={settings._customAdventureZone ?? 0} on:change={event => ui.editSetting('_customAdventureZone', numericInput(event))}>{#each zones as zone}<option value={zone.value}>{zone.label}</option>{/each}</select></label>{/if}
            </div>
            <AdventureRules {settings} telemetry={object(runtime.adventureRules)} connected={connection.connected} pending={'_customAdventureRules' in $ui.patch} onedit={ui.editSetting} />
          {/if}
          {#if group.title === 'Manual Adventure' && sharedManualCombatOptions}<p class="muted">Combat options also apply to manual farm rules and titan fights.</p>{/if}
          {#if group.title === 'Manual Adventure' && manualAdventureOptions}
            <label>Manual adventure loadout<select value={settings._manualAdventureLoadout ?? -1} on:change={event => ui.editSetting('_manualAdventureLoadout', numericInput(event))}>{#each manualCombatLoadouts as loadout}<option value={loadout.value}>{loadout.label}</option>{/each}</select></label>
          {/if}
          {#if group.title === 'Manual Adventure' && settings._manualAdventureEnabled === true}
            <div class="form-grid"><label>Manual adventure zone<select value={settings._manualAdventureZone ?? 0} on:change={event => ui.editSetting('_manualAdventureZone', numericInput(event))}>{#each zones as zone}<option value={zone.value}>{zone.label}</option>{/each}</select></label></div>
            {#if manualPushMode}<p class="muted">Push uses ITOPOD; the selected zone resumes afterward.</p>{/if}
          {/if}
          {#if group.title === 'Manual Adventure'}
            <button type="button" class:active={settings._customAdventureMode === 3 || maxItopodPushActive} aria-pressed={settings._customAdventureMode === 3 || maxItopodPushActive} disabled={maxItopodPushActive && settings._customAdventureMode !== 3 && !allowed('cancelMaxItopodPush')} on:click={() => maxItopodPushActive && settings._customAdventureMode !== 3 ? ui.runCommand('cancelMaxItopodPush') : ui.editSetting('_customAdventureMode', settings._customAdventureMode === 3 ? 0 : 3)}>Push ITOPOD</button>
            {#if settings._customAdventureMode === 3}
              <label>Max ITOPOD death limit<input type="number" min="1" max="1000" step="1" aria-describedby="max-itopod-deaths-help" value={String(settings._customAdventureMaxItopodDeaths ?? 3)} on:input={event => ui.editSetting('_customAdventureMaxItopodDeaths', numericInput(event), 350)} /></label>
              <p class="muted" id="max-itopod-deaths-help">After the death limit, returns to the manual zone or Optimal ITOPOD. Limit changes apply to the next push.</p>
            {/if}
            {#if settings._customAdventureMode === 3 || maxItopodPushActive}
              <p class="muted" role="status">{!connection.connected ? 'Disconnected' : maxItopodPushActive ? `Active: ${maxItopodPush.remainingDeaths ?? 'Unknown'} deaths remaining / Floor ${maxItopodPush.floor ?? 'Unknown'}` : maxItopodPush.active === false ? 'Idle' : 'Max ITOPOD push status not reported.'}</p>
            {/if}
          {/if}
          {#if group.title === 'Inventory & Boosts' && settings._customAutoBoostEnabled === true}<div class="form-grid"><label>Cube Power:Toughness ratio<input type="number" min={settingsBounds._customAutoBoostCubePowerRatio[0]} max={settingsBounds._customAutoBoostCubePowerRatio[1]} step="any" value={String(settings._customAutoBoostCubePowerRatio ?? 2)} on:input={event => ui.editSetting('_customAutoBoostCubePowerRatio', numericInput(event), 350)} /></label></div>{/if}
          {#if group.title === 'Manual Adventure'}<ManualAdventureBonuses {settings} onedit={ui.editSetting} />{/if}
        </article>
      {/each}
      {#if activeTab === 'adventure'}
        <ManualTitanFights {settings} telemetry={object(runtime.manualTitan)} connected={connection.connected} onedit={ui.editSetting} />
      {/if}
      {#if activeTab === 'misc'}
      {#if $ui.plan}<PlanGear plan={$ui.plan} presetLibrary={$ui.presetLibraryBase} disabled={$ui.busy && !$ui.editing} onchange={ui.editPlan} />{/if}
      <AutoExp {settings} onedit={ui.editSetting} />
      {/if}
    </section>
  {/if}

  <section hidden={activeTab !== 'misc'} style:display={activeTab === 'misc' ? undefined : 'none'} class="reference-tools">
    <ZoneReference />
  </section>

  {#if activeTab === 'logs'}
    <section class="logs-layout">
      <article class="card log-sidebar"><h2>Logs</h2><select aria-label="Log file" value={$ui.logName} on:change={event => ui.loadLog(value(event))}>{#each $ui.snapshot?.logs ?? [] as log}<option value={log.name}>{log.name}</option>{/each}</select><button type="button" on:click={() => ui.loadLog()}>Refresh log</button></article>
      <article class="card log-output"><div class="card-header"><h2>{$ui.logName}</h2></div><pre>{$ui.logText || 'No entries.'}</pre></article>
      <article class="card span-all" aria-label="Rebirth history"><div class="card-header"><h2>Rebirth history</h2><span class="muted">{$ui.planName || 'No plan selected'}</span></div><RebirthHistory history={$ui.plan?.RebirthHistory} /></article>
      <article class="card span-all" aria-label="Iron Pill history"><div class="card-header"><h2>Iron Pill</h2><span class="muted">All plans</span></div><IronPillHistory history={$ui.snapshot?.settings._customIronPillHistory} /></article>
    </section>
  {/if}

  <!-- Keep advanced buffers mounted across tab changes so navigation cannot silently lose JSON edits. -->
  <section class="grid raw-grid" hidden={activeTab !== 'raw'} style:display={activeTab === 'raw' ? undefined : 'none'} inert={$ui.busy && !$ui.editing}>
      {#if $ui.plan}
      <details class="advanced-plan span-all"><summary>Advanced plan JSON</summary>
        <JsonEditor bind:this={rawPlan} title="Plan" value={$ui.plan} validate={validateRawPlan} onapply={applyRawPlan} ondirty={dirty => rawPlanDirty = dirty} disabled={$ui.busy} guidance="Name, history, version, and revision are read-only." />
      </details>
      {/if}
      <details class="card plan-management span-all">
        <summary>Plan library</summary>
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
      <CombatPrep state={$ui} onedit={ui.editCombatPrep} onselect={ui.selectCombatPrep} onpreview={ui.previewCombatPrep} onstart={ui.startCombatPrep} oncancel={ui.cancelCombatPrep} />
      <article class="card"><details><summary>Data & Setup</summary>
        <label for="runtime-dir">Data folder</label><div class="runtime-row">
          <input id="runtime-dir" bind:value={pathDraft} spellcheck="false" disabled={$ui.busy} />
          <button type="button" disabled={$ui.busy} on:click={async () => { await ui.changeRuntimeDir(pathDraft); pathDraft = ui.current().runtimeDir; }}>Use folder</button>
          <button type="button" disabled={$ui.busy} on:click={chooseFolder}>Browse</button>
        </div>
        <div class="button-row" title="Applies after restarting the game."><button type="button" disabled={$ui.busy || !$ui.runtimeDir} on:click={() => ui.marker(true)}>Use this app</button></div>
      </details></article>
      <article class="card"><h2>Manual inventory</h2><div class="form-grid">
        <label>Goal<select bind:value={gearGoal}>{#each metadata('gearGoals', gearNames, runtime) as goal}<option value={goal}>{gearGoals[gearNames.indexOf(goal)]?.label ?? goal}</option>{/each}</select></label>
        <label>Fight scoring<select bind:value={fightMode}>{#each metadata('fightScoreModes', fightNames, runtime) as mode}<option value={mode}>{fightScoreModes[fightNames.indexOf(mode)]?.label ?? mode}</option>{/each}</select></label>
      </div><div class="button-row"><button type="button" disabled={$ui.busy || !allowed('previewGear', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('previewGear', { goal: gearGoal, fightScoreMode: fightMode })}>Preview gear</button><button type="button" disabled={$ui.busy || !allowed('optimizeGear', $ui.snapshot, $ui.now)} on:click={() => ui.runCommand('optimizeGear', { goal: gearGoal, fightScoreMode: fightMode })}>Equip gear</button></div>
      {#if ($ui.lastResult?.command === 'previewGear' || $ui.lastResult?.command === 'optimizeGear') && $ui.lastResult?.data}<details open><summary>Result</summary><pre>{String($ui.lastResult.message ?? '')}</pre></details>{/if}</article>
      <AugmentTools canPreview={allowed('previewAugments', $ui.snapshot, $ui.now)} canAllocate={allowed('allocateAugments', $ui.snapshot, $ui.now)} busy={$ui.busy} savedEstimate={$ui.snapshot?.settings._customAugmentsBloodEstimateSeconds} estimatePending={'_customAugmentsBloodEstimateSeconds' in $ui.patch} result={$ui.augmentResult} oncommand={ui.runCommand} />
      <article class="card span-all"><h2>Utilities</h2><div class="command-grid">{#each utilities as command}<button type="button" class:danger={command === 'unload' || command === 'probeCommands'} disabled={$ui.busy || !allowed(command, $ui.snapshot, $ui.now)} on:click={() => ui.runCommand(command)}>{commandLabels[command]}</button>{/each}</div></article>
      <article class="card span-all"><details><summary>Recent actions</summary><div class="result-list">{#each $ui.snapshot?.command_results ?? [] as result}<div class="result" class:ok={result.success}><strong>{commandLabels[result.command] ?? result.command}</strong><span>{result.success ? 'Done' : result.message}</span><details><summary>Details</summary><p>{result.message}</p><small>{result.completed_at}</small></details></div>{:else}<p class="muted">No actions yet.</p>{/each}</div></details></article>
    <JsonEditor bind:this={rawSettings} title="Settings" value={settings} validate={validateRawSettings} onapply={ui.applyRawSettings} ondirty={dirty => rawSettingsDirty = dirty} disabled={$ui.busy} guidance="Leave unrecognized fields unchanged." />
    <article class="card span-2"><details><summary>Game data</summary><pre>{pretty(runtime)}</pre></details></article>
  </section>
</main>
