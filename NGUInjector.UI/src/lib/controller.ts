import { get, writable } from 'svelte/store';
import { ask } from '@tauri-apps/plugin-dialog';
import * as api from './api';
import { assertPlanMetadata, canCommand, diffObject, equal, freshEdits, freshPlan, heartbeat, mergePatch, object, pendingPlanEdits, planDeletionReason, pollResult, validateAugmentArgs, validateCombatPrepArgs, validateMaxItopodPushArgs, validateName, validatePlan, validateSettings } from './model';
import type { JsonObject, JsonValue, NguiState } from './api';
import { commandLabels } from './options';
import { matchingPlanProgress } from './stage-bar';
import { combatPrepArgs, combatPrepCommands, combatPrepDefaults, combatPrepReady, combatPrepStartReason, readCombatPrepPreview, type CombatPrepDraft } from './combat-prep';
import { dynamicPresetReady, resourceLanePrioritiesReady, presetsReady, previewMatches, readPresetPreview, resultRows, validatePresetArgs, type PresetPreview } from './presets';
import { globalPresetArgs, globalPresetRows, globalPresetsReady, globalPreviewMatches, normalizePresetLibrary, rebasePresetLibrary, validateGlobalLibrary, validateGlobalPresetArgs } from './presets';

export interface UiState {
  snapshot: NguiState | null;
  runtimeDir: string;
  patch: JsonObject;
  planName: string;
  plan: JsonObject | null;
  planBase: JsonObject | null;
  planDirty: boolean;
  planDraftDirty: boolean;
  advancedDirty: boolean;
  editorVersion: number;
  busy: boolean;
  editing: boolean;
  refreshing: boolean;
  now: number;
  status: string;
  error: string;
  saveError: string;
  pollError: string;
  commandErrors: JsonObject[];
  lastResult: JsonObject | null;
  augmentResult: JsonObject | null;
  loadoutPresetPreview: PresetPreview | null;
  loadoutPresetSelectedId: string;
  loadoutPresetCandidateKey: string;
  presetLibrary: JsonObject | null;
  presetLibraryBase: JsonObject | null;
  presetLibraryDirty: boolean;
  presetLibraryLoading: boolean;
  combatPrepDraft: CombatPrepDraft;
  combatPrepPreview: JsonObject | null;
  combatPrepSelectedId: string;
  combatPrepUncertainRunId: string;
  pendingQuickGoal: string | null;
  logName: string;
  logText: string;
}

export interface EditSession {
  commit(value: JsonValue): Promise<boolean>;
  cancel(): void;
}

export function combatPrepReconciliationReason(progress: JsonObject, combatPrep = object(progress.combatPrep), resources: JsonObject = {}): string {
  const reason = [resources.state === 'Failed' || resources.state === 'Waiting' || resources.state === 'NeedsUser' ? resources.reason : '', progress.cleanupRecoveryReason, combatPrep.cleanupRecoveryReason, object(progress.combatPrep).cleanupRecoveryReason, progress.cleanupRecoveryRequired === true ? progress.reason : '', combatPrep.reason]
    .find(value => typeof value === 'string' && value.trim());
  return reason ? `Reconciling: ${String(reason).trim()}` : 'Reconciling.';
}

export function planStageOverrideReason(state: UiState, now = state.now): string {
  if (state.busy) return 'Another action is pending.';
  if (state.planDirty || state.planDraftDirty || state.advancedDirty || Object.keys(state.patch).length) return 'Finish or discard pending settings, timing, and JSON edits first.';
  const { snapshot, plan, planBase, planName } = state;
  if (!snapshot || !state.runtimeDir || snapshot.runtime_dir !== state.runtimeDir || !canCommand(snapshot, 'setPlanStage', now)) return 'Connect to an injector supporting manual stage actions.';
  if (typeof snapshot.runtime.startedAt !== 'string' || !snapshot.runtime.startedAt.trim()) return 'Wait for a fresh injector session.';
  if (snapshot.runtime.stageOverridesV1 !== true) return 'The injector does not advertise manual stage support.';
  if (!plan || !planBase || !equal(plan, planBase)) return 'Load the saved plan before changing stages.';
  if (plan.StageSchedulerEnabled !== true) return 'Enable and save stages before changing the current stage.';
  const liveSettings = object(snapshot.runtime.settings);
  if ((liveSettings.useCustomRebirthPlan ?? snapshot.settings._useCustomRebirthPlan) !== true ||
      String(liveSettings.customPlanFile ?? snapshot.settings._customPlanFile ?? '').toLowerCase() !== planName.toLowerCase()) return 'Only the active running plan can change stages.';
  const progress = object(snapshot.runtime.planProgress);
  const combat = object(snapshot.runtime.combatPrep);
  const stageCombat = object(progress.combatPrep);
  if (combat.active === true || combat.cleanupPending === true || stageCombat.active === true || stageCombat.cleanupPending === true) return 'Combat prep still owns equipment and resources; wait for the active run or cleanup to finish.';
  if (progress.cleanupRecoveryRequired === true || combat.cleanupRecoveryRequired === true || stageCombat.cleanupRecoveryRequired === true) return combatPrepReconciliationReason(progress, combat, object(snapshot.runtime.resourceAllocation));
  const view = matchingPlanProgress(plan, planName, progress, heartbeat(snapshot, now).connected, false);
  if (!view || view.status !== 'running') return 'Wait for matching live stage telemetry from the running plan.';
  if (view.activeStageIndex === null) {
    if (view.scheduleComplete !== true || (progress.activeStageIndex !== null && progress.activeStageIndex !== -1) || view.stages.some(stage => stage.state === 'unknown')) return 'Wait for matching live stage telemetry from the running plan.';
  } else {
    const activeState = view.stages[view.activeStageIndex].state;
    if (activeState !== 'current' && !(view.scheduleComplete === true && (activeState === 'completed' || activeState === 'skipped'))) return 'Wait for matching live stage telemetry from the running plan.';
  }
  try { api.requirePlanRevision(planBase); } catch { return 'Reload the saved plan revision before changing stages.'; }
  if (progress.planRevision !== planBase[api.planRevisionField] || !view.stageEntry) return 'Wait for the saved revision and current stage-entry token.';
  return '';
}

export function createController(backend = api, confirm = (message: string) => ask(message, { title: 'NGU Injector', kind: 'warning' }), initialDir = '') {
  const store = writable<UiState>({ snapshot: null, runtimeDir: initialDir, patch: {}, planName: '', plan: null, planBase: null, planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: 0, busy: false, editing: false, refreshing: false, now: Date.now(), status: 'Ready.', error: '', saveError: '', pollError: '', commandErrors: [], lastResult: null, augmentResult: null, loadoutPresetPreview: null, loadoutPresetSelectedId: '', loadoutPresetCandidateKey: '', presetLibrary: null, presetLibraryBase: null, presetLibraryDirty: false, presetLibraryLoading: false, combatPrepDraft: combatPrepDefaults(), combatPrepPreview: null, combatPrepSelectedId: '', combatPrepUncertainRunId: '', pendingQuickGoal: null, logName: 'inject.log', logText: '' });
  let libraryRevision = 0;
  let libraryContext = 0;
  let libraryAttempted = false;
  const libraryKeyRevisions = new Map<string, number>();
  let combatPrepRevision = 0;
  let nguWeightsRevision = 0;
  const nguWeightKeys = ['_energyNguWeights', '_magicNguWeights'];
  let epoch = 0;
  let refreshId = 0;
  let logId = 0;
  let revision = 0;
  let keyRevisions: Record<string, number> = {};
  let settingBases: JsonObject = {};
  const planKeyRevisions: Record<string, number> = {};
  let acknowledgedFlags: { patch: JsonObject; at: number; revision: number | null; session: JsonValue | undefined } | null = null;
  let acknowledgedRuntimeAt: number | null = null;
  let acknowledgedRuntimeRevision: number | null = null;
  let autosaveTimer: ReturnType<typeof setTimeout> | undefined;
  let autosaveSettings = false;
  let autosavePlan = false;
  let autosaveLibrary = false;
  const saveErrors = { settings: '', plan: '', library: '' };
  const saveErrorText = () => Object.values(saveErrors).filter(Boolean).join(' ');
  const current = () => get(store);
  const update = (patch: Partial<UiState>) => store.update(value => {
    if (patch.snapshot) {
      const progress = object(patch.snapshot.runtime.planProgress);
      const combat = object(patch.snapshot.runtime.combatPrep);
      const resources = object(patch.snapshot.runtime.resourceAllocation);
      // Progress-only views need the current backend I/O blocker, not just the historical marker reason.
      if ((progress.cleanupRecoveryRequired === true || combat.cleanupRecoveryRequired === true || object(progress.combatPrep).cleanupRecoveryRequired === true)
        && ['Failed', 'Waiting', 'NeedsUser'].includes(String(resources.state)) && typeof resources.reason === 'string' && resources.reason.trim()) {
        patch = { ...patch, snapshot: { ...patch.snapshot, runtime: { ...patch.snapshot.runtime, planProgress: { ...progress, cleanupRecoveryReason: resources.reason } } } };
      }
    }
    if (patch.snapshot && nguWeightKeys.some(key => !equal(value.snapshot?.settings[key], patch.snapshot?.settings[key]))) {
      ++nguWeightsRevision;
      ++combatPrepRevision;
      patch = { ...patch, loadoutPresetPreview: null, loadoutPresetCandidateKey: '', combatPrepPreview: null, combatPrepSelectedId: '' };
    }
    return { ...value, ...patch };
  });
  const message = (err: unknown) => err instanceof Error ? err.message : String(err);
  const dirty = () => Object.keys(current().patch).length > 0 || current().planDirty || current().planDraftDirty || current().advancedDirty || current().presetLibraryDirty;

  function setPresetLibrary(library: JsonObject): void {
    library = api.stripNguOverrides(library);
    const base = current().presetLibraryBase;
    if (!base || library.Version !== base.Version || library.__externalUiRevision !== base.__externalUiRevision) throw new Error('Library version and revision are backend-owned. Reload before editing.');
    const before = new Map(globalPresetRows(current().presetLibrary).map(row => [String(row.Id), row]));
    const after = new Map(globalPresetRows(library).map(row => [String(row.Id), row]));
    const at = ++libraryRevision;
    for (const id of new Set([...before.keys(), ...after.keys()])) {
      const a = before.get(id), b = after.get(id);
      if (!a || !b) libraryKeyRevisions.set(id, at);
      for (const key of new Set([...Object.keys(a ?? {}), ...Object.keys(b ?? {})])) if (!equal(a?.[key], b?.[key])) libraryKeyRevisions.set(`${id}/${key}`, at);
    }
    update({ presetLibrary: library, presetLibraryDirty: !equal(library, base) });
    autosaveLibrary = current().presetLibraryDirty;
    scheduleAutosave();
  }

  async function loadPresetLibrary(): Promise<void> {
    if (current().presetLibraryLoading) return;
    const dir = current().runtimeDir, context = libraryContext, at = libraryRevision;
    if (!dir) return;
    libraryAttempted = true;
    const base = current().presetLibraryBase;
    update({ presetLibraryLoading: true });
    try {
      const fresh = normalizePresetLibrary(await backend.readPresetLibrary(dir));
      if (context !== libraryContext || dir !== current().runtimeDir) return;
      if (base !== current().presetLibraryBase) throw new Error('Library changed while reloading. Newer edits retained; reload again.');
      const draft = current().presetLibrary;
      if (!base && at !== libraryRevision) throw new Error('Library was edited while loading. Edits retained.');
      const rebased = base && draft ? rebasePresetLibrary(base, draft, fresh) : fresh;
      update({ presetLibrary: rebased, presetLibraryBase: api.cloneObject(fresh), presetLibraryDirty: !equal(rebased, fresh) });
    } catch (err) { if (context === libraryContext) update({ error: message(err) }); }
    finally { if (context === libraryContext) update({ presetLibraryLoading: false }); }
  }

  async function discardPresetLibrary(): Promise<void> {
    await perform(async () => {
      const dir = current().runtimeDir, context = libraryContext, at = libraryRevision;
      if (current().presetLibraryDirty && !await confirm('Discard unsaved global preset library edits and reload? Plan edits will be kept.')) return;
      const fresh = normalizePresetLibrary(await backend.readPresetLibrary(dir));
      if (dir !== current().runtimeDir || context !== libraryContext || at !== libraryRevision) throw new Error('Library was edited while reloading. Edits retained.');
      autosaveLibrary = false;
      saveErrors.library = '';
      update({ saveError: saveErrorText() });
      update({ presetLibrary: fresh, presetLibraryBase: api.cloneObject(fresh), presetLibraryDirty: false, status: 'Global preset library reloaded.' });
    });
  }

  async function savePresetLibrary(): Promise<boolean> {
    let success = false;
    await perform(async () => {
      autosaveLibrary = false;
      const { runtimeDir: dir, presetLibrary: draft, presetLibraryBase: base } = current();
      if (!dir || !draft || !base) throw new Error('Load the global preset library before saving.');
      if (equal(draft, base)) {
        update({ status: 'Global preset library is already saved.' });
        success = true;
        return;
      }
      const context = libraryContext, at = libraryRevision;
      const sent = api.cloneObject(draft);
      validateGlobalLibrary(sent);
      const sameFolder = () => { if (context !== libraryContext || dir !== current().runtimeDir) throw new Error('Preset library folder changed. Edits retained.'); };
      const fresh = normalizePresetLibrary(await backend.readPresetLibrary(dir));
      sameFolder();
      const payload = normalizePresetLibrary(rebasePresetLibrary(base, sent, fresh));
      validateGlobalLibrary(payload);
      const result = await backend.savePresetLibrary(dir, payload);
      sameFolder();
      if (result.success !== true) throw new Error('Preset library save did not report success. Edits retained.');
      const saved = normalizePresetLibrary(await backend.readPresetLibrary(dir));
      sameFolder();
      // Readback must acknowledge every submitted definition, not merely a successful write envelope.
      if (!equal(saved.Presets, payload.Presets)) throw new Error('Preset library readback did not acknowledge the saved definitions. Outcome may be unknown; edits retained. Reload before retrying.');
      const pending = new Map(globalPresetRows(current().presetLibrary).map(row => [String(row.Id), row]));
      const rows = new Map(globalPresetRows(saved).map(row => [String(row.Id), row]));
      for (const id of new Set([...rows.keys(), ...pending.keys(), ...globalPresetRows(sent).map(row => String(row.Id))])) {
        const row = pending.get(id);
        if ((libraryKeyRevisions.get(id) ?? 0) > at) { if (row) rows.set(id, row); else rows.delete(id); continue; }
        if (!row || !rows.has(id)) continue;
        const next = { ...rows.get(id)! };
        for (const key of new Set([...Object.keys(next), ...Object.keys(row)])) if ((libraryKeyRevisions.get(`${id}/${key}`) ?? 0) > at) {
          if (key in row) next[key] = row[key]; else delete next[key];
        }
        rows.set(id, next);
      }
      const rebased = { ...saved, Presets: [...rows.values()] };
      // A newer edit can equal the old baseline while the submitted write is in flight.
      autosaveLibrary = !equal(saved, rebased);
      saveErrors.library = '';
      update({ presetLibraryBase: api.cloneObject(saved), presetLibrary: rebased, presetLibraryDirty: !equal(saved, rebased), status: 'Global preset library saved.' });
      update({ saveError: saveErrorText() });
      success = true;
    }, true, 'library');
    return success;
  }

  function withAcknowledgedFlags(snapshot: NguiState): NguiState {
    if (!acknowledgedFlags) return snapshot;
    const nextRevision = api.runtimeRevision(snapshot.runtime);
    const newer = nextRevision !== null && acknowledgedFlags.revision !== null ? nextRevision > acknowledgedFlags.revision : Date.parse(String(snapshot.runtime.updatedAt)) > acknowledgedFlags.at;
    if (!snapshot.connected || !equal(snapshot.runtime.startedAt, acknowledgedFlags.session) || newer) {
      acknowledgedFlags = null;
      return snapshot;
    }
    const flags: JsonObject = {};
    for (const [key, value] of Object.entries(acknowledgedFlags.patch)) if (!nguWeightKeys.includes(key)) flags[key.slice(1)] = value;
    return { ...snapshot, settings: mergePatch(snapshot.settings, acknowledgedFlags.patch), runtime: { ...snapshot.runtime, settings: mergePatch(object(snapshot.runtime.settings), flags) } };
  }

  function acknowledgeSettings(patch: JsonObject, completedAt?: JsonValue, session = current().snapshot?.runtime.startedAt, proof?: JsonObject | null): void {
    const snapshot = current().snapshot;
    if (!snapshot || !equal(session, snapshot.runtime.startedAt)) return;
    const completed = typeof completedAt === 'string' ? Date.parse(completedAt) : NaN;
    const proofRevision = proof && equal(proof.startedAt, session) ? api.runtimeRevision(proof) : null;
    const currentRevision = api.runtimeRevision(snapshot.runtime);
    const ordered = proofRevision !== null && currentRevision !== null;
    if (ordered ? currentRevision > proofRevision : Number.isFinite(completed) && Date.parse(String(snapshot.runtime.updatedAt)) > completed) return;
    const flags: JsonObject = {};
    for (const key of ['_customPlanFile', '_useCustomRebirthPlan', '_autoRebirth', ...nguWeightKeys]) if (key in patch) flags[key] = patch[key];
    // A duplicate result cannot contradict actual flags already published at that revision.
    const live = object(snapshot.runtime.settings);
    if (ordered && currentRevision === proofRevision && Object.entries(flags).some(([key, value]) => key.slice(1) in live && !equal(live[key.slice(1)], value))) return;
    if (Object.keys(flags).length) acknowledgedFlags = { patch: { ...acknowledgedFlags?.patch, ...flags }, at: Number.isFinite(completed) ? completed : Math.max(Date.now(), Date.parse(String(snapshot.runtime.updatedAt)) || 0), revision: proofRevision, session };
    update({ snapshot: withAcknowledgedFlags({ ...snapshot, settings: mergePatch(snapshot.settings, patch) }) });
  }

  function publishSnapshot(next: NguiState): void {
    const previous = current().snapshot;
    const sameSession = previous && previous.runtime_dir === next.runtime_dir && equal(previous.runtime.startedAt, next.runtime.startedAt);
    // Revisions resolve publications within the same millisecond; legacy bridges use the clock.
    const at = Date.parse(String(next.runtime.updatedAt));
    const nextRevision = api.runtimeRevision(next.runtime);
    const previousRevision = previous && api.runtimeRevision(previous.runtime);
    if (sameSession && (nextRevision !== null && previousRevision !== null
      ? nextRevision <= Math.max(previousRevision, acknowledgedRuntimeRevision ?? previousRevision)
      : at < Date.parse(String(previous.runtime.updatedAt)) || (acknowledgedRuntimeAt !== null && at <= acknowledgedRuntimeAt))) {
      const flags: JsonObject = {};
      for (const key of ['_customPlanFile', '_useCustomRebirthPlan', '_autoRebirth', ...nguWeightKeys]) if (key in previous.settings) flags[key] = previous.settings[key];
      next = { ...next, runtime: previous.runtime, settings: { ...next.settings, ...flags } };
    }
    if (!sameSession) { acknowledgedRuntimeAt = null; acknowledgedRuntimeRevision = null; invalidateCombatPrep(); update({ combatPrepUncertainRunId: '', loadoutPresetPreview: null, loadoutPresetCandidateKey: '' }); }
    update({ snapshot: withAcknowledgedFlags(next), now: Date.now(), ...(!sameSession ? { pendingQuickGoal: null } : {}) });
  }

  function consumeRoot(result: JsonObject, session: JsonValue | undefined): void {
    const snapshot = current().snapshot;
    const runtime = api.commandRuntime(result);
    if (!snapshot || !runtime || typeof session !== 'string' || !session || !equal(session, snapshot.runtime.startedAt) || !equal(session, runtime.startedAt)) return;
    const next = { ...snapshot, runtime: mergePatch(snapshot.runtime, runtime) };
    const nextRevision = api.runtimeRevision(runtime);
    const previousRevision = api.runtimeRevision(snapshot.runtime);
    if (typeof runtime.updatedAt !== 'string' || !heartbeat(next).connected || !(Number(next.runtime.protocolVersion) >= 3)) return;
    if (nextRevision !== null && previousRevision !== null
      ? nextRevision <= Math.max(previousRevision, acknowledgedRuntimeRevision ?? previousRevision)
      : Date.parse(runtime.updatedAt) < Date.parse(String(snapshot.runtime.updatedAt))) return;
    // A compact legacy response must not inherit a revision it did not publish.
    if (nextRevision === null) delete next.runtime.runtimeRevision;
    acknowledgedRuntimeAt = Date.parse(String(runtime.updatedAt));
    acknowledgedRuntimeRevision = nextRevision;
    update({ snapshot: next, now: Date.now() });
    const flags: JsonObject = {};
    const settings = object(runtime.settings);
    for (const key of ['customPlanFile', 'useCustomRebirthPlan', 'autoRebirth']) {
      if (typeof settings[key] === (key === 'customPlanFile' ? 'string' : 'boolean')) flags[`_${key}`] = settings[key];
    }
    acknowledgeSettings(flags, result.completedAt, session, runtime);
  }

  async function perform(action: () => Promise<void>, editing = false, write?: 'settings' | 'library', command?: string): Promise<void> {
    if (current().busy) return;
    update({ busy: true, editing, error: '' });
    try { await action(); } catch (err) {
      if (write === 'settings') autosaveSettings = false;
      if (write === 'library') autosaveLibrary = false;
      update({ error: message(err), status: '', ...(command ? { commandErrors: [...current().commandErrors.filter(result => result.command !== command), { command, success: false, message: message(err) }].slice(-16) } : {}) });
    }
    finally {
      update({ busy: false, editing: false });
      if (autosaveSettings || autosavePlan || autosaveLibrary) scheduleAutosave();
    }
  }

  function scheduleAutosave(delay = 350): void {
    clearTimeout(autosaveTimer);
    autosaveTimer = setTimeout(() => { void flushAutosave(); }, delay);
  }

  async function flushAutosave(): Promise<void> {
    clearTimeout(autosaveTimer);
    if (current().busy) return;
    // Invalid intermediate input stays local until corrected. Failed writes are not retried in a loop.
    if (autosaveSettings && !current().advancedDirty) {
      autosaveSettings = false;
      try {
        validateSettings(current().patch, current().snapshot?.settings ?? {});
        await saveSettings();
        saveErrors.settings = current().error || (Object.keys(current().patch).length && !autosaveSettings ? 'Settings were not saved.' : '');
      } catch (err) { saveErrors.settings = message(err); }
      update({ saveError: saveErrorText() });
    }
    if (autosavePlan && !current().advancedDirty) {
      autosavePlan = false;
      try {
        if (current().plan) validatePlan(current().plan!);
        if (current().planDirty) {
          await saveActivePlan();
          saveErrors.plan = current().error || (current().planDirty && !autosavePlan ? 'Plan changes were not saved.' : '');
        } else saveErrors.plan = '';
      } catch (err) { saveErrors.plan = message(err); }
      update({ saveError: saveErrorText() });
    }
    if (autosaveLibrary) {
      autosaveLibrary = false;
      try {
        if (current().presetLibraryDirty) {
          validateGlobalLibrary(current().presetLibrary!);
          await savePresetLibrary();
          saveErrors.library = current().error || (current().presetLibraryDirty && !autosaveLibrary ? 'Preset changes were not saved.' : '');
        } else saveErrors.library = '';
      } catch (err) { saveErrors.library = message(err); }
      update({ saveError: saveErrorText() });
    }
  }

  function editSetting(key: string, value: JsonValue, delay = 0): void {
    setSetting(key, value);
    autosaveSettings = true;
    scheduleAutosave(delay);
  }

  function editPlan(plan: JsonObject): void {
    setPlan(plan);
    autosavePlan = true;
    scheduleAutosave();
  }

  async function retryAutosave(): Promise<void> {
    autosaveSettings = Object.keys(current().patch).length > 0;
    autosavePlan = current().planDirty;
    autosaveLibrary = current().presetLibraryDirty;
    await flushAutosave();
  }

  async function refresh(quiet = false, force = false, loadInitialPlan = true): Promise<void> {
    if (quiet && !force && current().refreshing) return;
    const request = ++refreshId;
    const context = epoch;
    const dir = current().runtimeDir;
    update({ refreshing: true });
    try {
      let next: NguiState;
      let retryError = '';
      for (;;) {
        try {
          next = await backend.getState(dir || undefined);
          break;
        } catch (err) {
          if (context !== epoch || request !== refreshId) return;
          if (!/^Failed to read .*[/\\]runtime\.json: .*\(os error 32\)$/i.test(message(err))) throw err;
          retryError = `${message(err)} Retrying in 5 seconds.`;
          update({ error: retryError, pollError: retryError, snapshot: current().snapshot ? { ...current().snapshot!, connected: false } : null });
          await new Promise(resolve => setTimeout(resolve, 5000));
          if (context !== epoch || request !== refreshId) return;
        }
      }
      if (context !== epoch || request !== refreshId) return;
      if (current().snapshot && next.runtime_dir !== current().runtimeDir) throw new Error('State read returned another runtime folder. Current editors retained; switch folders explicitly.');
      update({ ...(current().pollError && current().error === current().pollError ? { error: '' } : {}), pollError: '' });
      // Disk snapshots never become the edit buffer. Pending patches always overlay the latest snapshot.
      publishSnapshot(next);
      update({ runtimeDir: next.runtime_dir });
      backend.storeRuntimeDir(next.runtime_dir);
      // A native plan read may wait on Unity. Keep heartbeat polling free while that read is pending.
      update({ refreshing: false });
      if (!libraryAttempted && !current().presetLibrary && !current().presetLibraryLoading && (next.runtime.globalPresetsVersion === 2 || next.protocol.globalPresetsVersion === 2)) await loadPresetLibrary();
      if (loadInitialPlan && !current().planName && next.plans.length && !current().busy) {
        const configured = String(next.settings._customPlanFile ?? '');
        await loadPlan(next.plans.includes(configured) ? configured : next.plans[0]);
      }
    } catch (err) {
      if (context === epoch && request === refreshId) update({ error: message(err), pollError: message(err), snapshot: current().snapshot ? { ...current().snapshot!, connected: false } : null });
    } finally {
      if (request === refreshId) update({ refreshing: false });
    }
  }

  // Retain native numeric drafts and exact lossless tokens without coercing either.
  function cloneSettings(value: JsonObject): JsonObject {
    const clone = (item: JsonValue): JsonValue => Array.isArray(item) ? item.map(clone) : api.isObject(item) ? cloneSettings(item) : item;
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, clone(item)]));
  }

  function setSetting(key: string, value: JsonValue): void {
    if (!(key in current().patch)) {
      delete settingBases[key];
      if (current().snapshot?.settings[key] !== undefined) settingBases = { ...settingBases, ...cloneSettings({ [key]: current().snapshot!.settings[key] }) };
    }
    keyRevisions[key] = ++revision;
    update({ patch: { ...current().patch, [key]: value } });
  }

  function setPlan(plan: JsonObject): void {
    plan = api.stripNguOverrides(plan);
    if (current().planBase) assertPlanMetadata(current().planBase!, plan);
    for (const key of new Set([...Object.keys(current().plan ?? {}), ...Object.keys(plan)])) {
      if (!equal(current().plan?.[key], plan[key])) planKeyRevisions[key] = ++revision;
    }
    update({ plan, planDirty: !equal(plan, current().planBase) });
  }

  function beginEdit(key: string, planEdit: boolean): EditSession {
    const before = current();
    const session = before.snapshot?.runtime.startedAt;
    const field = (source: JsonObject | null) => (planEdit ? api.cloneObject : cloneSettings)(source && Object.prototype.hasOwnProperty.call(source, key) ? { [key]: source[key] } : {});
    const priorDraft = field(planEdit ? before.plan : before.patch);
    const priorBase = field(planEdit ? before.planBase : settingBases);
    const wasPending = planEdit ? !equal(priorDraft, priorBase) : Object.prototype.hasOwnProperty.call(priorDraft, key);
    const keyRevision = () => (planEdit ? planKeyRevisions : keyRevisions)[key];
    const sameEditor = () => before.runtimeDir === current().runtimeDir && equal(session, current().snapshot?.runtime.startedAt)
      && (!planEdit || (before.planName === current().planName && before.editorVersion === current().editorVersion));
    let ownRevision: number | undefined;
    let closed = false;
    let committing = false;
    const rollback = () => {
      if (ownRevision === undefined || keyRevision() !== ownRevision || !sameEditor()) return;
      if (planEdit) {
        if (!current().plan || !current().planBase) return;
        let planBase = api.cloneObject(current().planBase!);
        const plan = api.cloneObject(current().plan!);
        if (wasPending) { delete planBase[key]; planBase = { ...planBase, ...priorBase }; }
        delete plan[key];
        const restored = { ...plan, ...(wasPending ? priorDraft : field(planBase)) };
        planKeyRevisions[key] = ++revision;
        update({ plan: restored, planBase, planDirty: !equal(restored, planBase) });
      } else {
        const patch = { ...current().patch };
        delete patch[key];
        delete settingBases[key];
        settingBases = { ...settingBases, ...priorBase };
        keyRevisions[key] = ++revision;
        update({ patch: { ...patch, ...(wasPending ? priorDraft : {}) } });
      }
    };
    return {
      async commit(value) {
        if (closed || !sameEditor()) { update({ error: 'Edit session is closed or belongs to another editor. Edits retained.' }); return false; }
        if (committing) { update({ error: 'This edit is still saving. Edits retained.' }); return false; }
        committing = true;
        const previousRevision = keyRevision();
        const saving = planEdit ? savePlanField(key, value) : saveSettingValue(key, value);
        // Both helpers stage synchronously. Claim only the revision produced by this attempt.
        if (keyRevision() !== previousRevision) ownRevision = keyRevision();
        const success = await saving;
        committing = false;
        if (success) closed = true;
        else if (closed) rollback();
        return success;
      },
      cancel() {
        if (closed) return;
        closed = true;
        // An in-flight write cannot be undone; defer local rollback until its outcome is known.
        if (!committing) rollback();
      }
    };
  }

  function beginSettingEdit(key: string): EditSession { return beginEdit(key, false); }
  function beginPlanEdit(key: string): EditSession { return beginEdit(key, true); }

  async function saveSettingValue(key: string, value: JsonValue): Promise<boolean> {
    if (current().busy) { update({ error: 'Another action is busy. Edits retained; retry when it finishes.' }); return false; }
    let success = false;
    await perform(async () => {
      const { runtimeDir, snapshot } = current();
      if (!runtimeDir || !snapshot) throw new Error('Load settings before saving a value.');
      const connected = heartbeat(snapshot).connected;
      const sent = cloneSettings({ [key]: value });
      setSetting(key, sent[key]);
      const sentRevision = keyRevisions[key];
      const sentBase = cloneSettings(settingBases);
      validateSettings(sent, snapshot.settings);
      checkGlobalWeightWrite(sent);
      const context = ++epoch;
      const read = async () => {
        const next = await backend.getState(runtimeDir);
        if (context !== epoch || runtimeDir !== current().runtimeDir || next.runtime_dir !== runtimeDir || !equal(snapshot.runtime.startedAt, next.runtime.startedAt) || !equal(snapshot.runtime.startedAt, current().snapshot?.runtime.startedAt)) throw new Error('Settings read was superseded by a runtime-folder or injector-session change. Edits retained.');
        return next;
      };
      const fresh = await read();
      if (connected && !canCommand(fresh, 'applySettingsPatch')) throw new Error('Injector became stale or unready while reading settings. Edits retained.');
      if (!equal(sentBase[key], fresh.settings[key]) && !equal(sent[key], fresh.settings[key])) throw new Error(`Source changed at ${key}. Local edits are retained; reload before saving.`);
      // Check every pending field, not just the one being saved, before advancing its baseline.
      for (const [pendingKey, pendingValue] of Object.entries(current().patch)) {
        if (!equal(settingBases[pendingKey], fresh.settings[pendingKey]) && !equal(pendingValue, fresh.settings[pendingKey])) throw new Error(`Source changed at ${pendingKey}. Local edits are retained; reload before saving.`);
      }
      validateSettings(sent, fresh.settings);
      await checkPlanExecutionPatch(sent);
      if (context !== epoch || runtimeDir !== current().runtimeDir || !equal(snapshot.runtime.startedAt, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed before saving settings. Edits retained.');
      api.requireSettingsRevision(fresh.settings_revision);
      const result = await backend.saveSettingsPatch(runtimeDir, sent, fresh.settings_revision);
      if (result.success !== true) throw new Error('Settings save did not report success. Edits retained.');
      if (context !== epoch || runtimeDir !== current().runtimeDir || !equal(snapshot.runtime.startedAt, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed while saving settings. Outcome may be unknown; edits retained.');
      const committed = api.committedSettings(result, Object.keys(sent));
      const saved = { ...current().snapshot!, settings: committed.settings, settings_revision: committed.revision };
      validateSettings({ [key]: saved.settings[key] }, fresh.settings);
      const pending = cloneSettings(current().patch);
      const base = { ...settingBases, [key]: saved.settings[key] };
      if (sentRevision === keyRevisions[key]) delete pending[key];
      for (const [pendingKey, pendingValue] of Object.entries(pending)) {
        if (!equal(base[pendingKey], saved.settings[pendingKey]) && !equal(pendingValue, saved.settings[pendingKey])) throw new Error(`Source changed at ${pendingKey}. Local edits are retained; reload before saving.`);
      }
      ++epoch;
      settingBases = base;
      update({ snapshot: withAcknowledgedFlags(saved), patch: pending, now: Date.now(), status: 'Setting saved.' });
      acknowledgeSettings({ [key]: saved.settings[key] }, object(result.result).completedAt, snapshot.runtime.startedAt, api.commandRuntime(object(result.result)));
      consumeRoot(object(result.result), snapshot.runtime.startedAt);
      if (!Object.keys(pending).length) {
        saveErrors.settings = '';
        update({ saveError: saveErrorText() });
      }
      success = true;
    }, true, 'settings');
    return success;
  }

  function checkGlobalWeightWrite(patch: JsonObject): void {
    if (nguWeightKeys.some(key => key in patch) && current().snapshot?.runtime.globalNguWeightsVersion !== 1) {
      throw new Error('Global NGU weights require an injector advertising globalNguWeightsVersion: 1. Update and reconnect. Edits retained.');
    }
  }

  async function checkPlanResourceLanes(plan: JsonObject, command: string): Promise<void> {
    const state = current();
    const ids = new Set<string>();
    if (typeof plan.ActivePresetId === 'string' && plan.ActivePresetId) ids.add(plan.ActivePresetId);
    for (const row of Array.isArray(plan.Stages) ? plan.Stages.filter(api.isObject) : []) {
      if (typeof row.PresetId === 'string' && row.PresetId && row.PresetId !== '@active') ids.add(row.PresetId);
    }
    if (!ids.size) return;
    const session = state.snapshot?.runtime.startedAt;
    const library = normalizePresetLibrary(await backend.readPresetLibrary(state.runtimeDir));
    if (state.runtimeDir !== current().runtimeDir || !equal(session, current().snapshot?.runtime.startedAt) || !canCommand(current().snapshot, command)) throw new Error('Injector session changed while checking preset lane order. Refresh before retrying.');
    for (const preset of globalPresetRows(library).filter(row => ids.has(String(row.Id)))) {
      if (!resourceLanePrioritiesReady(current().snapshot, preset)) throw new Error('Reviewed lane order requires runtime.resourceLanePrioritiesVersion === 1. Update the injector before running this plan.');
      if (preset.ResourcePrioritiesReviewed === true && (typeof session !== 'string' || !session.trim() || current().snapshot?.runtime.globalPresetsVersion !== 2 || current().snapshot?.runtime.globalNguWeightsVersion !== 1)) throw new Error('Reviewed lane order requires a fresh identified injector session with global preset and NGU support.');
    }
  }

  async function checkPlanExecutionPatch(patch: JsonObject): Promise<void> {
    const state = current();
    if (!heartbeat(state.snapshot).connected || !('_customPlanFile' in patch) && patch._useCustomRebirthPlan !== true && patch._autoRebirth !== true) return;
    const name = patch._customPlanFile ?? object(state.snapshot?.runtime.settings).customPlanFile ?? state.snapshot?.settings._customPlanFile;
    if (typeof name !== 'string' || !name) return;
    const plan = await readCurrentPlan(state.runtimeDir, name);
    validatePlan(plan);
    await checkPlanResourceLanes(plan, 'applySettingsPatch');
  }

  async function savePlanField(key: string, value: JsonValue): Promise<boolean> {
    if (current().busy) { update({ error: 'Another action is busy. Edits retained; retry when it finishes.' }); return false; }
    let success = false;
    await perform(async () => {
      const { runtimeDir, planName, plan, planBase } = current();
      if (!runtimeDir || !planName || !plan || !planBase) throw new Error('Select and load a plan before saving a field.');
      if (['Name', 'Version', 'RebirthHistory', api.planRevisionField].includes(key)) throw new Error(`${key} is backend-owned and read-only.`);
      if (['NguGoal', 'EnergyNguWeights', 'MagicNguWeights'].includes(key)) throw new Error('NGU weights are global settings, not plan fields.');
      const target = api.stripNguOverrides(api.cloneObject({ [key]: value }));
      const session = current().snapshot?.runtime.startedAt;
      // This explicit inline edit only authorizes its own value, never the rest of the draft.
      setPlan({ ...plan, ...target });
      const sentRevision = planKeyRevisions[key] = ++revision;
      const sent = { ...api.cloneObject(planBase), ...target };
      assertPlanMetadata(planBase, sent);
      validatePlan(sent);
      ++epoch;
      const fresh = await readCurrentPlan(runtimeDir, planName);
      if (!equal(planBase[key], fresh[key]) && !equal(sent[key], fresh[key])) throw new Error(`Source changed at ${key}. Local edits are retained; reload before saving.`);
      freshPlan(planBase, current().plan!, fresh);
      const payload = freshPlan(planBase, sent, fresh);
      validatePlan(payload);
      try {
        const result = await backend.savePlan(runtimeDir, planName, payload);
        consumeRoot(object(result.result), session);
        if (result.success !== true) throw new Error('Plan save did not report success. Edits retained.');
      } catch (err) {
        await refresh(true, true, false);
        throw err;
      }
      ++epoch;
      if (!equal(session, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed while saving the plan. Edits retained; reload before retrying.');
      const saved = await readCurrentPlan(runtimeDir, planName);
      validatePlan(saved);
      if (!(key in saved)) throw new Error('Saved field is missing from the normalized plan. Edits retained.');
      const changed = sentRevision !== planKeyRevisions[key];
      const base = { ...planBase, [key]: payload[key] };
      const draft = changed ? current().plan! : { ...current().plan!, [key]: payload[key] };
      let rebased = freshPlan(base, draft, saved);
      // Revisions preserve even an away-and-back target edit across backend normalization.
      if (changed) rebased = mergePatch(rebased, { [key]: current().plan![key] });
      update({ planBase: api.cloneObject(saved), plan: rebased, planDirty: changed || !equal(saved, rebased), status: `Saved '${planName}' field.` });
      if (!current().planDirty) {
        saveErrors.plan = '';
        update({ saveError: saveErrorText() });
      }
      success = true;
    }, true);
    return success;
  }

  async function saveSettings(): Promise<void> {
    await perform(async () => {
      const { runtimeDir, patch, snapshot } = current();
      if (!runtimeDir || !Object.keys(patch).length) return;
      const connected = heartbeat(snapshot).connected;
      const sent = cloneSettings(patch);
      const sentBase = cloneSettings(settingBases);
      const sentRevisions = { ...keyRevisions };
      validateSettings(sent, snapshot?.settings ?? {});
      checkGlobalWeightWrite(sent);
      if (sent._autoRebirth === true && snapshot?.settings._autoRebirth !== true && !await confirm('Enable auto-rebirth? The active plan may rebirth as soon as its conditions are met.')) return;
      const context = ++epoch;
      await checkPlanExecutionPatch(sent);
      if (context !== epoch || runtimeDir !== current().runtimeDir || !equal(snapshot?.runtime.startedAt, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed before saving settings. Edits retained; refresh before retrying.');
      const fresh = await backend.getState(runtimeDir);
      if (context !== epoch || runtimeDir !== current().runtimeDir || fresh.runtime_dir !== runtimeDir || !equal(snapshot?.runtime.startedAt, fresh.runtime.startedAt) || !equal(snapshot?.runtime.startedAt, current().snapshot?.runtime.startedAt)) throw new Error('Settings read was superseded by a runtime-folder or injector-session change. Edits retained.');
      const latest = fresh.settings;
      if (connected && !canCommand(fresh, 'applySettingsPatch')) throw new Error('Injector became stale or unready while reading settings. Edits retained.');
      for (const [key, value] of Object.entries(sent)) {
        if (!equal(sentBase[key], latest[key]) && !equal(value, latest[key])) throw new Error(`Source changed at ${key}. Local edits are retained; reload before saving.`);
      }
      validateSettings(sent, latest);
      api.requireSettingsRevision(fresh.settings_revision);
      const result = await backend.saveSettingsPatch(runtimeDir, sent, fresh.settings_revision);
      if (result.success !== true) throw new Error('Settings save did not report success. Edits retained.');
      if (context !== epoch || runtimeDir !== current().runtimeDir || !equal(snapshot?.runtime.startedAt, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed while saving settings. Outcome may be unknown; edits retained.');
      const committed = api.committedSettings(result, Object.keys(sent));
      validateSettings(Object.fromEntries(Object.keys(sent).map(key => [key, committed.settings[key]])), fresh.settings);
      ++epoch;
      const pending = { ...current().patch };
      for (const key of Object.keys(sent)) if (sentRevisions[key] === keyRevisions[key]) delete pending[key];
      settingBases = mergePatch(settingBases, Object.fromEntries(Object.keys(sent).map(key => [key, committed.settings[key]])));
      // Publish the acknowledged baseline immediately so a later edit cannot use the pre-save values.
      update({ patch: pending, snapshot: current().snapshot ? { ...current().snapshot!, settings: committed.settings, settings_revision: committed.revision } : null, status: 'Settings saved.' });
      const weights = Object.fromEntries(nguWeightKeys.filter(key => key in sent).map(key => [key, committed.settings[key]]));
      if (Object.keys(weights).length) acknowledgeSettings(weights, object(result.result).completedAt, snapshot?.runtime.startedAt, api.commandRuntime(object(result.result)));
      consumeRoot(object(result.result), snapshot?.runtime.startedAt);
    }, true, 'settings');
  }

  async function readCurrentPlan(dir: string, name: string): Promise<JsonObject> {
    const context = epoch;
    const connected = heartbeat(current().snapshot).connected;
    const session = current().snapshot?.runtime.startedAt;
    if (connected && !canCommand(current().snapshot, 'readPlan')) throw new Error('Normalized plan reads require a running protocol v3 bridge advertising readPlan. Update or reconnect the injector.');
    const plan = await backend.readPlan(dir, name);
    if (context !== epoch || dir !== current().runtimeDir) throw new Error('Plan read was superseded by a runtime-folder change. Refresh and retry.');
    if (connected && (!canCommand(current().snapshot, 'readPlan') || !equal(session, current().snapshot?.runtime.startedAt))) throw new Error('Injector session changed or became unready while reading the plan. Previous editor retained; refresh and retry.');
    return api.stripNguOverrides(plan);
  }

  async function loadPlan(name: string): Promise<void> {
    if (autosaveSettings || autosavePlan) await flushAutosave();
    await perform(async () => {
      if (!name || !current().runtimeDir) return;
      if ((current().planDirty || current().planDraftDirty || current().advancedDirty) && !await confirm('Discard unsaved plan / advanced JSON edits and load another plan?')) return;
      const previous = current().plan;
      const plan = await readCurrentPlan(current().runtimeDir, name);
      if (current().plan !== previous) throw new Error('Plan was edited while loading. New edits retained; retry loading.');
      autosavePlan = false;
      saveErrors.plan = '';
      update({ saveError: saveErrorText() });
      update({ planName: name, plan, planBase: api.cloneObject(plan), planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: current().editorVersion + 1, status: `Loaded plan '${name}'.` });
    });
  }

  async function saveActivePlan(): Promise<void> {
    await perform(async () => {
      const { runtimeDir, planName, plan, planBase, snapshot } = current();
      if (!plan || !planBase || !planName) return;
      if (current().advancedDirty) throw new Error('Apply or discard the advanced JSON buffer first.');
      const sent = api.stripNguOverrides(api.cloneObject(plan));
      assertPlanMetadata(planBase, sent);
      validatePlan(sent);
      if (sent.AutoRebirthEnabled === true && planBase.AutoRebirthEnabled !== true && !await confirm('Enable rebirth for this plan? If running, it may rebirth immediately.')) return;
      ++epoch;
      const fresh = await readCurrentPlan(runtimeDir, planName);
      const payload = freshPlan(planBase, sent, fresh);
      validatePlan(payload);
      try {
        const result = await backend.savePlan(runtimeDir, planName, payload);
        consumeRoot(object(result.result), snapshot?.runtime.startedAt);
        if (result.success !== true) throw new Error('Plan save did not report success. Edits retained.');
      } catch (err) {
        await refresh(true, true, false);
        throw err;
      }
      ++epoch;
      // Advance committed fields, but retain editor metadata until validated read-back.
      const committed = mergePatch(sent, pendingPlanEdits(sent, payload));
      const draft = mergePatch(committed, pendingPlanEdits(sent, current().plan!));
      update({ planBase: api.cloneObject(committed), plan: draft, planDirty: !equal(committed, draft), status: `Saved '${planName}'.` });
      const saved = await readCurrentPlan(runtimeDir, planName);
      const rebased = freshPlan(committed, current().plan!, saved);
      update({ planBase: api.cloneObject(saved), plan: rebased, planDirty: !equal(saved, rebased), status: `Saved plan '${planName}'.` });
      await refresh(true, true);
    }, true);
  }

  async function changeRuntimeDir(dir: string): Promise<void> {
    if (autosaveSettings || autosavePlan || autosaveLibrary) await flushAutosave();
    await perform(async () => {
      if (!dir.trim() || dir === current().runtimeDir) return;
      if (dirty() && !await confirm('Discard unsaved changes and switch data folder?')) return;
      ++epoch;
      ++refreshId;
      ++logId;
      update({ refreshing: false });
      const patchRevision = revision;
      const previousPlan = current().plan;
      const previousLibraryRevision = libraryRevision;
      // Validate/read the new folder before replacing the current session. Failure retains every edit.
      const next = await backend.getState(dir.trim());
      if (revision !== patchRevision || previousPlan !== current().plan || previousLibraryRevision !== libraryRevision) throw new Error('Edits were made while switching folders. Current folder and new edits retained; retry the switch.');
      ++libraryContext;
      libraryAttempted = false;
      libraryKeyRevisions.clear();
      update({ presetLibrary: null, presetLibraryBase: null, presetLibraryDirty: false, presetLibraryLoading: false });
      keyRevisions = {};
      settingBases = {};
      acknowledgedFlags = null;
      acknowledgedRuntimeAt = null;
      autosaveSettings = autosavePlan = autosaveLibrary = false;
      saveErrors.settings = saveErrors.plan = saveErrors.library = '';
      update({ saveError: '', pollError: '', commandErrors: [], error: '' });
      acknowledgedRuntimeRevision = null;
      invalidateCombatPrep();
      update({ loadoutPresetPreview: null, loadoutPresetSelectedId: '', loadoutPresetCandidateKey: '' });
      update({ combatPrepUncertainRunId: '' });
      update({ snapshot: next, runtimeDir: next.runtime_dir, patch: {}, plan: null, planBase: null, planName: '', planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: current().editorVersion + 1, lastResult: null, augmentResult: null, pendingQuickGoal: null, logText: '', now: Date.now(), status: 'Runtime folder changed.' });
      backend.storeRuntimeDir(next.runtime_dir);
    });
    if (!current().planName) await refresh(true);
  }

  async function runCommand(command: string, args: JsonObject = {}): Promise<void> {
    await perform(async () => {
      if (!canCommand(current().snapshot, command)) throw new Error(`Disconnected, stale heartbeat, or unsupported command: ${command}.`);
      const before = current();
      const dir = before.runtimeDir;
      const session = before.snapshot?.runtime.startedAt;
      const context = ++epoch;
      const sameRequest = () => context === epoch && dir === current().runtimeDir && current().snapshot?.runtime_dir === dir && equal(session, current().snapshot?.runtime.startedAt);
      const assertRequest = () => { if (!sameRequest()) throw new Error('Injector session changed or request was superseded while applying the action. Refresh before retrying.'); };
      const weightsRevision = nguWeightsRevision;
      const presetCommand = command === 'previewLoadoutPreset' || command === 'applyLoadoutPreset';
      const globalCommand = ['previewGlobalPreset', 'applyGlobalPreset', 'equipGlobalPreset'].includes(command);
      if (globalCommand) assertGlobalPreset(command, args);
      if (command === 'previewGlobalPreset') update({ loadoutPresetPreview: null, loadoutPresetCandidateKey: '' });
      if (presetCommand) assertLoadoutPreset(command, args);
      if (command === 'previewLoadoutPreset') update({ loadoutPresetPreview: null, loadoutPresetCandidateKey: '' });
      const combatCommand = combatPrepCommands.includes(command);
      if (combatCommand) assertCombatPrep(command, args);
      if (command === 'previewCombatPrep') invalidateCombatPrep();
      const combatRevision = combatPrepRevision;
      if (command === 'setPlanStage') assertStageOverride(args);
      const augmentCommand = command === 'previewAugments' || command === 'allocateAugments';
      if (augmentCommand) validateAugmentArgs(args);
      if (command === 'startMaxItopodPush' || command === 'cancelMaxItopodPush') validateMaxItopodPushArgs(command, args);
      const warnings: Record<string, string> = {
        manualRebirth: 'Rebirth now? This resets the current run.',
        unload: 'Disconnect and stop automation?',
        probeCommands: 'Run the commands in probe.txt? They may change the game.',
        reloadSettings: 'Reload saved settings? Your unsaved edits will remain.',
        optimizeGear: 'Equip this loadout?',
        allocateAugments: 'Allocate energy to the best augment and upgrade?'
      };
      if (warnings[command] && !await confirm(warnings[command])) return;
      assertRequest();
      if (!canCommand(current().snapshot, command)) throw new Error('Heartbeat expired before submitting the action. Refresh before retrying.');
      if (globalCommand) assertGlobalPreset(command, args);
      if (presetCommand) {
        assertLoadoutPreset(command, args);
      }
      if (command === 'setPlanStage') {
        if (before.editorVersion !== current().editorVersion) throw new Error('Plan editor changed before submitting the action. Retry from fresh telemetry.');
        assertStageOverride(args);
        await checkPlanResourceLanes(current().planBase!, command);
        assertRequest();
        assertStageOverride(args);
      }
      if (command === 'applySettingsPatch') await checkPlanExecutionPatch(object(args.patch));
      assertRequest();
      if (!canCommand(current().snapshot, command)) throw new Error('Heartbeat expired before submitting the action. Refresh before retrying.');
      const label = command === 'setPlanStage' ? 'Manual stage action' : commandLabels[command] ?? 'Action';
      update({ status: `${label}: working...`, lastResult: null, pendingQuickGoal: command === 'focusGoal' && typeof args.goal === 'string' ? args.goal : null, ...(augmentCommand ? { augmentResult: null } : {}) });
      let watching = true;
      let timer: ReturnType<typeof setTimeout> | undefined;
      const watchRuntime = () => {
        timer = setTimeout(async () => {
          const request = refreshId;
          try {
            const next = await backend.getState(dir);
            if (watching && sameRequest() && request === refreshId && next.runtime_dir === dir && equal(session, next.runtime.startedAt) && heartbeat(next).connected) publishSnapshot(next);
          } catch (err) {
            if (watching && sameRequest() && request === refreshId) update({ pollError: message(err), snapshot: current().snapshot ? { ...current().snapshot!, connected: false } : null });
          }
          finally { if (watching && sameRequest()) watchRuntime(); }
        }, 200);
      };
      watchRuntime();
      let rejected = false;
      let submitted = false;
      try {
        // Spend the preview locally before IPC: even a rejected transport can have queued it.
        if (command === 'startCombatPrep') invalidateCombatPrep();
        if (command === 'applyLoadoutPreset' || command === 'applyGlobalPreset') update({ loadoutPresetPreview: null, loadoutPresetCandidateKey: '' });
        if (command === 'cancelCombatPrep') update({ combatPrepUncertainRunId: String(args.runId) });
        submitted = true;
        const id = await backend.enqueueCommand(dir, command, args);
        assertRequest();
        const result = await pollResult(id, command, async () => {
          assertRequest();
          const value = await backend.readCommandResult(dir, id);
          assertRequest();
          if (value?.id === id && value.command === command && value.success === false) rejected = true;
          return value;
        }, globalCommand ? 60000 : 12000, 50);
        assertRequest();
        ++epoch;
        if (command === 'focusGoal') acknowledgeSettings({ _autoRebirth: false, _useCustomRebirthPlan: false }, result.completedAt, session, api.commandRuntime(result));
        consumeRoot(result, session);
        if (command === 'previewLoadoutPreset' && weightsRevision === nguWeightsRevision) {
          const preview = readPresetPreview(result, args);
          if (preview.startedAt !== undefined && preview.startedAt !== session) throw new Error('Preview belongs to another injector session.');
          update({ loadoutPresetPreview: { result: preview, args: api.cloneObject(args), runtimeDir: dir, session: String(session) } });
        }
        if (command === 'previewGlobalPreset' && weightsRevision === nguWeightsRevision) {
          const definition = globalPresetRows(before.presetLibraryBase).find(row => row.Id === args.presetId)!;
          const preview = readPresetPreview(result, args, definition);
          if (preview.startedAt !== undefined && preview.startedAt !== session) throw new Error('Preview belongs to another injector session.');
          if (preview.presetName !== definition.Name) throw new Error('Preview does not match the committed global definition.');
          update({ loadoutPresetPreview: { result: preview, args: api.cloneObject(args), preset: api.cloneObject(definition), runtimeDir: dir, session: String(session) } });
        }
        if (command === 'previewCombatPrep' && combatRevision === combatPrepRevision) {
          const preview = readCombatPrepPreview(result, args, current().snapshot?.protocol);
          update({ combatPrepPreview: preview, combatPrepSelectedId: String(preview.recommendedCandidateId) });
        }
        update({ status: `${label} completed.`, lastResult: result, commandErrors: current().commandErrors.filter(failure => failure.command !== command), ...(augmentCommand ? { augmentResult: result } : {}) });
        void refresh(true, true, false);
      } catch (err) {
        if (rejected && command === 'cancelCombatPrep') update({ combatPrepUncertainRunId: '' });
        if ((command === 'previewExecutionRebind' || command === 'previewExecutionRestart') && !rejected && message(err).startsWith(`${command} timed out.`)) throw new Error('Review preview timed out. Request a fresh review.');
        if (command === 'applyExecutionRebind' && submitted && !rejected) {
          const reason = message(err).startsWith(`${command} timed out.`) ? 'Selection approval timed out.' : message(err);
          throw new Error(`${reason} Outcome may be unknown. Inspect fresh runtime status and request a fresh review before approving again; this approval will not be replayed.`);
        }
        if (combatCommand && command !== 'previewCombatPrep' && !rejected) throw new Error(`${message(err)} Outcome may be unknown. Inspect fresh runtime status; this action will not be replayed.`);
        if ((command === 'applyLoadoutPreset' || command === 'applyGlobalPreset' || command === 'equipGlobalPreset') && !rejected) throw new Error(`${message(err)} Outcome may be unknown. Inspect Last applied and refresh preview before retrying; this action will not be replayed.`);
        throw err;
      } finally {
        watching = false;
        clearTimeout(timer);
        update({ pendingQuickGoal: null });
      }
    }, false, undefined, command);
  }

  function invalidateCombatPrep(): void {
    ++combatPrepRevision;
    update({ combatPrepPreview: null, combatPrepSelectedId: '' });
  }

  function assertLoadoutPreset(command: string, args: JsonObject): void {
    validatePresetArgs(command, args);
    const state = current();
    if (!presetsReady(state.snapshot, command) || state.snapshot?.runtime_dir !== state.runtimeDir) throw new Error('Connect to a fresh injector advertising loadoutPresetsVersion 2, globalNguWeightsVersion 1, and preset commands.');
    if (!dynamicPresetReady(state.snapshot, object(args.preset))) throw new Error('Dynamic adventure requires runtime.adventureDynamicPresetsVersion === 1.');
    if (!resourceLanePrioritiesReady(state.snapshot, object(args.preset))) throw new Error('Reviewed lane order requires runtime.resourceLanePrioritiesVersion === 1.');
    const submitted = { ...args }; delete submitted.candidateKey; delete submitted.previewId;
    if (command === 'applyLoadoutPreset') {
      if (!previewMatches(state.loadoutPresetPreview, submitted, state.runtimeDir, state.snapshot?.runtime.startedAt)) throw new Error('No matching latest preview. Refresh preview before applying.');
      if (args.previewId !== state.loadoutPresetPreview!.result.previewId) throw new Error('Preview token does not match the latest preview. Refresh preview before applying.');
      const candidate = resultRows(state.loadoutPresetPreview!.result.candidates).find(row => row.key === args.candidateKey);
      if (!candidate || candidate.feasible !== true || args.candidateKey !== state.loadoutPresetCandidateKey) throw new Error('Select a feasible candidate from the latest preview.');
      if (typeof candidate.score !== 'number' || !Number.isFinite(candidate.score)) throw new Error('Select a candidate with a finite score from the latest preview.');
    }
  }

  async function previewLoadoutPreset(): Promise<void> {
    try { await runCommand('previewGlobalPreset', globalPresetArgs(current().loadoutPresetSelectedId, current().presetLibraryBase)); } catch (err) { update({ error: message(err) }); }
  }

  async function applyLoadoutPreset(): Promise<void> {
    try { await runCommand('applyGlobalPreset', { ...globalPresetArgs(current().loadoutPresetSelectedId, current().presetLibraryBase), candidateKey: current().loadoutPresetCandidateKey, previewId: current().loadoutPresetPreview?.result.previewId ?? '' }); } catch (err) { update({ error: message(err) }); }
  }

  async function equipGlobalPreset(id: string): Promise<void> {
    try { await runCommand('equipGlobalPreset', globalPresetArgs(id, current().presetLibraryBase)); } catch (err) { update({ error: message(err) }); }
  }

  function assertGlobalPreset(command: string, args: JsonObject): void {
    validateGlobalPresetArgs(command, args);
    const state = current();
    if (!globalPresetsReady(state.snapshot, command) || state.snapshot?.runtime_dir !== state.runtimeDir) throw new Error('Connect to a fresh injector advertising globalPresetsVersion 2, globalNguWeightsVersion 1, and global preset commands.');
    if (!state.presetLibrary || state.presetLibraryDirty || !equal(state.presetLibrary, state.presetLibraryBase)) throw new Error('Wait for preset changes to save before previewing or equipping. Edits never apply automatically.');
    const submitted = { ...args }; delete submitted.candidateKey; delete submitted.previewId;
    if (!equal(submitted, globalPresetArgs(String(args.presetId), state.presetLibraryBase))) throw new Error('Global library revision changed. Refresh preview.');
    if (!dynamicPresetReady(state.snapshot, globalPresetRows(state.presetLibraryBase).find(row => row.Id === args.presetId))) throw new Error('Dynamic adventure requires runtime.adventureDynamicPresetsVersion === 1.');
    if (!resourceLanePrioritiesReady(state.snapshot, globalPresetRows(state.presetLibraryBase).find(row => row.Id === args.presetId))) throw new Error('Reviewed lane order requires runtime.resourceLanePrioritiesVersion === 1.');
    if (command === 'applyGlobalPreset') {
      if (args.presetId !== state.loadoutPresetSelectedId || !globalPreviewMatches(state.loadoutPresetPreview, submitted, state.runtimeDir, state.snapshot?.runtime.startedAt, !state.presetLibraryDirty)) throw new Error('No matching latest preview. Refresh preview before applying.');
      if (args.previewId !== state.loadoutPresetPreview!.result.previewId) throw new Error('Preview token does not match the latest preview.');
      const candidate = resultRows(state.loadoutPresetPreview!.result.candidates).find(row => row.key === args.candidateKey);
      if (!candidate || candidate.feasible !== true || args.candidateKey !== state.loadoutPresetCandidateKey) throw new Error('Select a feasible candidate from the latest preview.');
      if (typeof candidate.score !== 'number' || !Number.isFinite(candidate.score)) throw new Error('Select a candidate with a finite score from the latest preview.');
    }
  }

  function assertCombatPrep(command: string, args: JsonObject): void {
    validateCombatPrepArgs(command, args);
    const state = current();
    if (!combatPrepReady(state.snapshot, state.runtimeDir, command)) throw new Error('Wait for a fresh injector session advertising combat prep commands.');
    if (command === 'previewCombatPrep' && !equal(args, combatPrepArgs(state.combatPrepDraft))) throw new Error('Preview arguments do not match the local options.');
    const runtime = object(state.snapshot?.runtime.combatPrep);
    const progress = object(state.snapshot?.runtime.planProgress);
    if (command !== 'previewCombatPrep' && (runtime.cleanupRecoveryRequired === true || progress.cleanupRecoveryRequired === true || object(progress.combatPrep).cleanupRecoveryRequired === true)) throw new Error(combatPrepReconciliationReason(progress, runtime, object(state.snapshot?.runtime.resourceAllocation)));
    if (command === 'startCombatPrep') {
      const reason = combatPrepStartReason(state.combatPrepPreview, state.combatPrepSelectedId, state.combatPrepDraft);
      if (reason) throw new Error(reason);
      if (runtime.cleanupPending === true) throw new Error('Combat prep still owns equipment and resources; cleanup is pending.');
      if (runtime.active === true) throw new Error('Cancel the active combat prep run before starting another.');
      if (args.previewId !== state.combatPrepPreview?.previewId || args.candidateId !== state.combatPrepSelectedId) throw new Error('Preview token or selected candidate changed. Refresh preview.');
    }
    if (command === 'cancelCombatPrep') {
      if (runtime.schemaVersion !== 1 || runtime.active !== true || args.runId !== runtime.runId) throw new Error('Cancel requires the active run ID from fresh runtime telemetry.');
      if (state.combatPrepUncertainRunId === args.runId) throw new Error('Cancellation already submitted for this run. Inspect runtime status; it will not be replayed.');
    }
  }

  function editCombatPrep(key: keyof CombatPrepDraft, value: string): void {
    if (current().combatPrepDraft[key] === value) return;
    invalidateCombatPrep();
    update({ combatPrepDraft: { ...current().combatPrepDraft, [key]: value } });
  }

  async function previewCombatPrep(): Promise<void> {
    try { await runCommand('previewCombatPrep', combatPrepArgs(current().combatPrepDraft)); } catch (err) { update({ error: message(err) }); }
  }
  async function startCombatPrep(): Promise<void> {
    const state = current();
    await runCommand('startCombatPrep', { previewId: state.combatPrepPreview?.previewId ?? '', candidateId: state.combatPrepSelectedId });
  }
  async function cancelCombatPrep(): Promise<void> {
    await runCommand('cancelCombatPrep', { runId: object(current().snapshot?.runtime.combatPrep).runId ?? '' });
  }

  function assertStageOverride(args: JsonObject): void {
    const state = current();
    const reason = planStageOverrideReason({ ...state, busy: false }, Date.now());
    if (reason) throw new Error(reason);
    const progress = object(state.snapshot!.runtime.planProgress);
    if (args.name !== state.planName || args.expectedRevision !== state.planBase![api.planRevisionField] || args.expectedEntry !== progress.stageEntry) throw new Error('Saved revision or stage entry changed. Retry from fresh telemetry.');
    if (!['skip', 'set', 'restart'].includes(String(args.action)) || typeof args.stageIndex !== 'number' || !Number.isSafeInteger(args.stageIndex) || args.stageIndex < 0 || args.stageIndex >= (progress.stages as JsonObject[]).length) throw new Error('Choose a valid manual stage action and stage index.');
    if (args.action !== 'set') {
      if (progress.activeStageIndex === null || progress.activeStageIndex === -1) throw new Error('No current stage is selected. Set a stage as current first.');
      if (args.stageIndex !== progress.activeStageIndex) throw new Error('The current stage changed. Retry from fresh telemetry.');
    }
  }

  async function setPlanStage(action: api.PlanStageAction, stageIndex: number, expectedEntry: string): Promise<void> {
    const state = current();
    const reason = planStageOverrideReason(state, Date.now());
    if (reason) { update({ error: reason }); return; }
    // Manual navigation never flushes or saves unrelated drafts.
    await runCommand('setPlanStage', { name: state.planName, expectedRevision: state.planBase![api.planRevisionField], expectedEntry, action, stageIndex });
  }

  async function planAction(action: 'activate' | 'run' | 'stop', autoRebirth?: boolean): Promise<void> {
    await perform(async () => {
      if (!canCommand(current().snapshot, 'applySettingsPatch')) throw new Error('Connect to the injector before changing the active plan.');
      if (action !== 'stop' && (current().planDirty || current().planDraftDirty || current().advancedDirty)) throw new Error('Save or discard plan / JSON edits before activating or running the plan.');
      if (!current().planName && action !== 'stop') return;
      if (action === 'run' && !canCommand(current().snapshot, 'readPlan')) throw new Error('Reconnect the game before starting this plan.');
      if (action === 'run' && autoRebirth === true && (!canCommand(current().snapshot, 'readPlan') || !canCommand(current().snapshot, 'savePlan'))) throw new Error('Update or reconnect the injector before enabling auto-rebirth.');
      const patch: JsonObject = action === 'stop' ? { _useCustomRebirthPlan: false, _autoRebirth: false } : { _customPlanFile: current().planName };
      if (action === 'run') patch._useCustomRebirthPlan = true;
      if (action === 'run' && autoRebirth !== undefined) patch._autoRebirth = autoRebirth;
      if (action !== 'stop' && Object.keys(patch).some(key => key in current().patch)) throw new Error('Save or discard pending settings for the active plan/run switches first.');
      const dir = current().runtimeDir;
      const session = current().snapshot?.runtime.startedAt;
      ++epoch;
      if (action === 'run') {
        const base = api.cloneObject(current().planBase!);
        let plan = await readCurrentPlan(current().runtimeDir, current().planName);
        validatePlan(plan);
        freshPlan(base, current().plan!, plan);
        await checkPlanResourceLanes(plan, 'applySettingsPatch');
        if (autoRebirth === true && plan.AutoRebirthEnabled !== true) {
          const saved = await backend.savePlan(current().runtimeDir, current().planName, { ...plan, AutoRebirthEnabled: true });
          if (saved.success !== true) throw new Error('Could not enable rebirth for this plan. The plan was not started.');
          plan = await readCurrentPlan(current().runtimeDir, current().planName);
          validatePlan(plan);
          if (plan.AutoRebirthEnabled !== true) throw new Error('Normalized plan did not enable rebirth. The plan was not started.');
        }
        const rebased = freshPlan(base, current().plan!, plan);
        update({ plan: rebased, planBase: api.cloneObject(plan), planDirty: !equal(plan, rebased) });
        if (current().planDirty || current().planDraftDirty || current().advancedDirty) throw new Error('Plan was edited while starting. New edits retained; save or discard before retrying.');
      }
      if (!canCommand(current().snapshot, 'applySettingsPatch')) throw new Error('Connection lost. The plan was not started.');
      await checkPlanExecutionPatch(patch);
      if (dir !== current().runtimeDir || !equal(session, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed before the plan action. Refresh before retrying.');
      if (action !== 'stop' && (current().planDirty || current().planDraftDirty || current().advancedDirty)) throw new Error('Plan was edited while starting. Save or discard edits before retrying.');
      const fresh = await backend.getState(dir);
      if (fresh.runtime_dir !== dir || dir !== current().runtimeDir || !equal(session, fresh.runtime.startedAt) || !equal(session, current().snapshot?.runtime.startedAt)
          || !canCommand(fresh, 'applySettingsPatch') || !canCommand(current().snapshot, 'applySettingsPatch')) throw new Error('Injector session changed or became unready before the plan action. Refresh before retrying.');
      api.requireSettingsRevision(fresh.settings_revision);
      const result = await backend.saveSettingsPatch(dir, patch, fresh.settings_revision);
      if (result.success !== true) throw new Error('Plan action did not report success.');
      if (dir !== current().runtimeDir || !equal(session, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed during the plan action. Refresh before retrying.');
      const committed = api.committedSettings(result, Object.keys(patch));
      ++epoch;
      update({ snapshot: { ...current().snapshot!, settings: committed.settings, settings_revision: committed.revision } });
      acknowledgeSettings(Object.fromEntries(Object.keys(patch).map(key => [key, committed.settings[key]])), object(result.result).completedAt, session, api.commandRuntime(object(result.result)));
      consumeRoot(object(result.result), session);
      update({ status: action === 'stop' ? 'Plan stopped. Auto-rebirth off.' : action === 'run' ? `Running '${current().planName}'.` : `Selected '${current().planName}'.` });
      void refresh(true, true, false);
    });
  }

  async function createPlan(name: string, copy: boolean): Promise<void> {
    await perform(async () => {
      const connected = heartbeat(current().snapshot).connected;
      if (connected && !canCommand(current().snapshot, 'createPlan')) throw new Error('The connected injector does not support plan creation. Refresh or update it first.');
      validateName(name);
      if ((current().planDirty || current().planDraftDirty || current().advancedDirty || Object.keys(current().patch).length) && !await confirm('Create a new plan and discard unsaved plan edits?')) return;
      if ((connected || heartbeat(current().snapshot).connected) && !canCommand(current().snapshot, 'createPlan')) throw new Error('Connection expired or plan creation is unsupported. Refresh before retrying.');
      const source = copy && current().plan ? api.cloneObject(current().plan!) : undefined;
      if (copy && !source) throw new Error('Select a plan to copy first.');
      if (source) { validatePlan(source); source.RebirthHistory = []; delete source[api.planRevisionField]; }
      ++epoch;
      update({ status: copy ? 'Copying plan...' : 'Creating plan...' });
      const created = await backend.createPlan(current().runtimeDir, name, source);
      ++epoch;
      const plan = await readCurrentPlan(current().runtimeDir, created);
      autosavePlan = false;
      saveErrors.plan = '';
      update({ saveError: saveErrorText() });
      update({ planName: created, plan, planBase: api.cloneObject(plan), planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: current().editorVersion + 1, status: `Created '${created}'.` });
      await refresh(true, true);
    });
  }

  async function deletePlan(): Promise<void> {
    await perform(async () => {
      const connected = heartbeat(current().snapshot).connected;
      let reason = planDeletionReason(current().snapshot, current().planName, Date.now(), current().patch);
      if (reason) throw new Error(reason);
      if (!await confirm(`Delete '${current().planName}' permanently, including unsaved edits?`)) return;
      reason = planDeletionReason(current().snapshot, current().planName, Date.now(), current().patch);
      if (reason) throw new Error(reason);
      if (connected && !canCommand(current().snapshot, 'deletePlan')) throw new Error('Connection expired while confirming deletion. Refresh before retrying.');
      ++epoch;
      update({ status: 'Deleting plan...' });
      const result = await backend.deletePlan(current().runtimeDir, current().planName);
      if (result.success !== true) throw new Error('Plan deletion did not report success. Current editor retained.');
      ++epoch;
      const data = object(result.mode === 'connected' ? object(result.result).data : result.data);
      if (typeof data.selectedPlan !== 'string' || !data.selectedPlan) throw new Error('Deletion completed, but the selected-plan result is invalid. Refresh before retrying.');
      const snapshot = current().snapshot;
      autosavePlan = false;
      saveErrors.plan = '';
      update({ saveError: saveErrorText() });
      update({ snapshot: snapshot ? { ...snapshot, settings: { ...snapshot.settings, _customPlanFile: data.selectedPlan } } : null, planName: '', plan: null, planBase: null, planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: current().editorVersion + 1, status: 'Plan deleted.' });
      await refresh(true, true);
    });
  }

  async function marker(enabled: boolean): Promise<void> {
    await perform(async () => {
      await backend.setExternalUiEnabled(current().runtimeDir, enabled);
      update({ status: 'Window preference saved. Restart the game to apply.' });
      await refresh(true, true);
    });
  }

  async function loadLog(name = current().logName): Promise<void> {
    const request = ++logId;
    const context = epoch;
    update({ logName: name });
    try {
      const text = await backend.tailLog(current().runtimeDir, name, 350);
      if (context === epoch && request === logId) update({ logText: text });
    } catch (err) { if (context === epoch && request === logId) update({ error: message(err) }); }
  }

  return { subscribe: store.subscribe, current, refresh, setSetting, setPlan, editSetting, editPlan, flushAutosave, retryAutosave, saveSettings, saveSettingValue, savePlanField, beginSettingEdit, beginPlanEdit, loadPlan, saveActivePlan, changeRuntimeDir, runCommand, setPlanStage, planAction, createPlan, deletePlan, marker, loadLog,
    previewLoadoutPreset, applyLoadoutPreset, equipGlobalPreset, setPresetLibrary, loadPresetLibrary, savePresetLibrary, discardPresetLibrary,
    selectLoadoutPreset: (id: string) => update({ loadoutPresetSelectedId: id, loadoutPresetCandidateKey: '' }),
    selectLoadoutPresetCandidate: (key: string) => update({ loadoutPresetCandidateKey: key }),
    editCombatPrep, previewCombatPrep, startCombatPrep, cancelCombatPrep,
    selectCombatPrep: (id: string) => update({ combatPrepSelectedId: id }),
    dispose: () => clearTimeout(autosaveTimer),
    tick: () => update({ now: Date.now() }),
    planDraftDirty: (value: boolean) => {
      if (current().planDraftDirty !== value) update({ planDraftDirty: value });
    },
    advancedDirty: (value: boolean) => {
      update({ advancedDirty: value });
      if (!value && (autosaveSettings || autosavePlan || autosaveLibrary)) scheduleAutosave();
    },
    error: (err: unknown) => update({ error: message(err) }),
    discardSettings: () => { autosaveSettings = false; saveErrors.settings = ''; keyRevisions = {}; settingBases = {}; update({ patch: {}, saveError: saveErrorText() }); },
    applyRawSettings: (draft: JsonObject, base: JsonObject) => {
      validateSettings(diffObject(base, draft), base);
      const latest = mergePatch(current().snapshot?.settings ?? {}, current().patch);
      const merged = freshEdits(base, draft, latest);
      const patch = diffObject(latest, merged);
      validateSettings(patch, latest);
      for (const [key, value] of Object.entries(patch)) editSetting(key, value);
    }
  };
}
