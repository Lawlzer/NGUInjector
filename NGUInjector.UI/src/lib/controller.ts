import { get, writable } from 'svelte/store';
import { ask } from '@tauri-apps/plugin-dialog';
import * as api from './api';
import { assertPlanMetadata, canCommand, diffObject, equal, freshEdits, freshPlan, heartbeat, mergePatch, object, pendingPlanEdits, planDeletionReason, pollResult, validateAugmentArgs, validateName, validatePlan, validateSettings } from './model';
import type { JsonObject, JsonValue, NguiState } from './api';
import { commandLabels } from './options';

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
  lastResult: JsonObject | null;
  augmentResult: JsonObject | null;
  pendingQuickGoal: string | null;
  logName: string;
  logText: string;
}

export interface EditSession {
  commit(value: JsonValue): Promise<boolean>;
  cancel(): void;
}

export function createController(backend = api, confirm = (message: string) => ask(message, { title: 'NGU Injector', kind: 'warning' }), initialDir = '') {
  const store = writable<UiState>({ snapshot: null, runtimeDir: initialDir, patch: {}, planName: '', plan: null, planBase: null, planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: 0, busy: false, editing: false, refreshing: false, now: Date.now(), status: 'Ready.', error: '', saveError: '', lastResult: null, augmentResult: null, pendingQuickGoal: null, logName: 'inject.log', logText: '' });
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
  const saveErrors = { settings: '', plan: '' };
  const current = () => get(store);
  const update = (patch: Partial<UiState>) => store.update(value => ({ ...value, ...patch }));
  const message = (err: unknown) => err instanceof Error ? err.message : String(err);
  const dirty = () => Object.keys(current().patch).length > 0 || current().planDirty || current().planDraftDirty || current().advancedDirty;

  function withAcknowledgedFlags(snapshot: NguiState): NguiState {
    if (!acknowledgedFlags) return snapshot;
    const nextRevision = api.runtimeRevision(snapshot.runtime);
    const newer = nextRevision !== null && acknowledgedFlags.revision !== null ? nextRevision > acknowledgedFlags.revision : Date.parse(String(snapshot.runtime.updatedAt)) > acknowledgedFlags.at;
    if (!snapshot.connected || !equal(snapshot.runtime.startedAt, acknowledgedFlags.session) || newer) {
      acknowledgedFlags = null;
      return snapshot;
    }
    const flags: JsonObject = {};
    for (const [key, value] of Object.entries(acknowledgedFlags.patch)) flags[key.slice(1)] = value;
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
    for (const key of ['_customPlanFile', '_useCustomRebirthPlan', '_autoRebirth']) if (key in patch) flags[key] = patch[key];
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
      for (const key of ['_customPlanFile', '_useCustomRebirthPlan', '_autoRebirth']) if (key in previous.settings) flags[key] = previous.settings[key];
      next = { ...next, runtime: previous.runtime, settings: { ...next.settings, ...flags } };
    }
    if (!sameSession) { acknowledgedRuntimeAt = null; acknowledgedRuntimeRevision = null; }
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

  async function perform(action: () => Promise<void>, editing = false): Promise<void> {
    if (current().busy) return;
    update({ busy: true, editing, error: '' });
    try { await action(); } catch (err) { update({ error: message(err), status: '' }); }
    finally {
      update({ busy: false, editing: false });
      if (autosaveSettings || autosavePlan) scheduleAutosave();
    }
  }

  function scheduleAutosave(delay = 350): void {
    clearTimeout(autosaveTimer);
    autosaveTimer = setTimeout(() => { void flushAutosave(); }, delay);
  }

  async function flushAutosave(): Promise<void> {
    clearTimeout(autosaveTimer);
    if (current().busy || current().advancedDirty) return;
    // Invalid intermediate input stays local until corrected. Failed writes are not retried in a loop.
    if (autosaveSettings) {
      autosaveSettings = false;
      try {
        validateSettings(current().patch, current().snapshot?.settings ?? {});
        await saveSettings();
        saveErrors.settings = current().error || (Object.keys(current().patch).length && !autosaveSettings ? 'Settings were not saved.' : '');
      } catch (err) { saveErrors.settings = message(err); }
      update({ saveError: Object.values(saveErrors).filter(Boolean).join(' ') });
    }
    if (autosavePlan) {
      autosavePlan = false;
      try {
        if (current().plan) validatePlan(current().plan!);
        if (current().planDirty) {
          await saveActivePlan();
          saveErrors.plan = current().error || (current().planDirty && !autosavePlan ? 'Plan changes were not saved.' : '');
        } else saveErrors.plan = '';
      } catch (err) { saveErrors.plan = message(err); }
      update({ saveError: Object.values(saveErrors).filter(Boolean).join(' ') });
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
    await flushAutosave();
  }

  async function refresh(quiet = false, force = false, loadInitialPlan = true): Promise<void> {
    if (quiet && !force && current().refreshing) return;
    const request = ++refreshId;
    const context = epoch;
    const dir = current().runtimeDir;
    update({ refreshing: true, ...(quiet ? {} : { error: '' }) });
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
          update({ error: retryError, snapshot: current().snapshot ? { ...current().snapshot!, connected: false } : null });
          await new Promise(resolve => setTimeout(resolve, 5000));
          if (context !== epoch || request !== refreshId) return;
        }
      }
      if (context !== epoch || request !== refreshId) return;
      if (retryError && current().error === retryError) update({ error: '' });
      // Disk snapshots never become the edit buffer. Pending patches always overlay the latest snapshot.
      publishSnapshot(next);
      update({ runtimeDir: next.runtime_dir });
      backend.storeRuntimeDir(next.runtime_dir);
      // A native plan read may wait on Unity. Keep heartbeat polling free while that read is pending.
      update({ refreshing: false });
      if (loadInitialPlan && !current().planName && next.plans.length && !current().busy) {
        const configured = String(next.settings._customPlanFile ?? '');
        await loadPlan(next.plans.includes(configured) ? configured : next.plans[0]);
      }
    } catch (err) {
      if (context === epoch && request === refreshId) update({ error: message(err), snapshot: current().snapshot ? { ...current().snapshot!, connected: false } : null });
    } finally {
      if (request === refreshId) update({ refreshing: false });
    }
  }

  function setSetting(key: string, value: JsonValue): void {
    if (!(key in current().patch)) {
      delete settingBases[key];
      if (current().snapshot?.settings[key] !== undefined) settingBases = { ...settingBases, ...api.cloneObject({ [key]: current().snapshot!.settings[key] }) };
    }
    keyRevisions[key] = ++revision;
    update({ patch: { ...current().patch, [key]: value } });
  }

  function setPlan(plan: JsonObject): void {
    if (current().planBase) assertPlanMetadata(current().planBase!, plan);
    for (const key of new Set([...Object.keys(current().plan ?? {}), ...Object.keys(plan)])) {
      if (!equal(current().plan?.[key], plan[key])) planKeyRevisions[key] = ++revision;
    }
    update({ plan, planDirty: !equal(plan, current().planBase) });
  }

  function beginEdit(key: string, planEdit: boolean): EditSession {
    const before = current();
    const session = before.snapshot?.runtime.startedAt;
    const field = (source: JsonObject | null) => api.cloneObject(source && Object.prototype.hasOwnProperty.call(source, key) ? { [key]: source[key] } : {});
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
      const sent = api.cloneObject({ [key]: value });
      setSetting(key, sent[key]);
      const sentRevision = keyRevisions[key];
      const sentBase = api.cloneObject(settingBases);
      validateSettings(sent, snapshot.settings);
      let context = ++epoch;
      const read = async () => {
        const next = await backend.getState(runtimeDir);
        if (context !== epoch || runtimeDir !== current().runtimeDir || next.runtime_dir !== runtimeDir || !equal(snapshot.runtime.startedAt, next.runtime.startedAt) || !equal(snapshot.runtime.startedAt, current().snapshot?.runtime.startedAt)) throw new Error('Settings read was superseded by a runtime-folder or injector-session change. Edits retained.');
        return next;
      };
      const fresh = await read();
      if (!equal(sentBase[key], fresh.settings[key]) && !equal(sent[key], fresh.settings[key])) throw new Error(`Source changed at ${key}. Local edits are retained; reload before saving.`);
      // Check every pending field, not just the one being saved, before advancing its baseline.
      for (const [pendingKey, pendingValue] of Object.entries(current().patch)) {
        if (!equal(settingBases[pendingKey], fresh.settings[pendingKey]) && !equal(pendingValue, fresh.settings[pendingKey])) throw new Error(`Source changed at ${pendingKey}. Local edits are retained; reload before saving.`);
      }
      validateSettings(sent, fresh.settings);
      const result = await backend.saveSettingsPatch(runtimeDir, sent);
      if (result.success !== true) throw new Error('Settings save did not report success. Edits retained.');
      context = ++epoch;
      const completedAt = object(result.result).completedAt;
      const completed = typeof completedAt === 'string' ? Date.parse(completedAt) : NaN;
      const hasBarrier = result.mode === 'connected' && Number.isFinite(completed) && typeof snapshot.runtime.startedAt === 'string' && !!snapshot.runtime.startedAt;
      let saved!: NguiState;
      for (let attempt = 0; attempt < 4; ++attempt) {
        saved = await read();
        if (!(key in saved.settings)) throw new Error('Saved setting is missing from the normalized read. Edits retained.');
        validateSettings({ [key]: saved.settings[key] }, fresh.settings);
        // Connected normalization needs a same-session heartbeat at/after completion. Raw offline
        // writes and older responses without that proof must read back the exact submitted value.
        const acknowledged = hasBarrier ? heartbeat(saved).connected && Date.parse(String(saved.runtime.updatedAt)) >= completed : equal(saved.settings[key], sent[key]);
        if (acknowledged) break;
        if (attempt === 3) throw new Error('Settings read-back did not acknowledge the saved value in time. Outcome may be unknown; edits retained.');
        await new Promise(resolve => setTimeout(resolve, 400));
      }
      const pending = api.cloneObject(current().patch);
      const base = { ...settingBases, [key]: saved.settings[key] };
      if (sentRevision === keyRevisions[key]) delete pending[key];
      for (const [pendingKey, pendingValue] of Object.entries(pending)) {
        if (!equal(base[pendingKey], saved.settings[pendingKey]) && !equal(pendingValue, saved.settings[pendingKey])) throw new Error(`Source changed at ${pendingKey}. Local edits are retained; reload before saving.`);
      }
      ++epoch;
      settingBases = api.cloneObject(saved.settings);
      update({ snapshot: withAcknowledgedFlags(saved), patch: pending, now: Date.now(), status: 'Setting saved.' });
      acknowledgeSettings({ [key]: saved.settings[key] }, object(result.result).completedAt, snapshot.runtime.startedAt);
      if (!Object.keys(pending).length) {
        saveErrors.settings = '';
        update({ saveError: saveErrors.plan });
      }
      success = true;
    }, true);
    return success;
  }

  function checkWeightWrite(base: JsonObject, draft: JsonObject): void {
    if (!['EnergyNguWeights', 'MagicNguWeights'].some(key => !equal(base[key], draft[key]))) return;
    if (Number(draft.Version) < 16 || !(Number(current().snapshot?.runtime.planVersion) >= 16)
      || !canCommand(current().snapshot, 'readPlan') || !canCommand(current().snapshot, 'savePlan')) {
      throw new Error('NGU weight changes require a version 16 plan and a connected injector advertising planVersion 16 or newer. Update, reconnect, and reload. Edits retained.');
    }
  }

  async function savePlanField(key: string, value: JsonValue): Promise<boolean> {
    if (current().busy) { update({ error: 'Another action is busy. Edits retained; retry when it finishes.' }); return false; }
    let success = false;
    await perform(async () => {
      const { runtimeDir, planName, plan, planBase } = current();
      if (!runtimeDir || !planName || !plan || !planBase) throw new Error('Select and load a plan before saving a field.');
      if (['Name', 'Version', 'RebirthHistory', api.planRevisionField].includes(key)) throw new Error(`${key} is backend-owned and read-only.`);
      const target = api.cloneObject({ [key]: value });
      const session = current().snapshot?.runtime.startedAt;
      // This explicit inline edit only authorizes its own value, never the rest of the draft.
      setPlan({ ...plan, ...target });
      const sentRevision = planKeyRevisions[key] = ++revision;
      const sent = { ...api.cloneObject(planBase), ...target };
      assertPlanMetadata(planBase, sent);
      validatePlan(sent);
      checkWeightWrite(planBase, sent);
      ++epoch;
      const fresh = await readCurrentPlan(runtimeDir, planName);
      if (!equal(planBase[key], fresh[key]) && !equal(sent[key], fresh[key])) throw new Error(`Source changed at ${key}. Local edits are retained; reload before saving.`);
      freshPlan(planBase, current().plan!, fresh);
      const payload = freshPlan(planBase, sent, fresh);
      validatePlan(payload);
      checkWeightWrite(planBase, payload);
      const result = await backend.savePlan(runtimeDir, planName, payload);
      if (result.success !== true) throw new Error('Plan save did not report success. Edits retained.');
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
        update({ saveError: saveErrors.settings });
      }
      success = true;
    }, true);
    return success;
  }

  async function saveSettings(): Promise<void> {
    await perform(async () => {
      const { runtimeDir, patch, snapshot } = current();
      if (!runtimeDir || !Object.keys(patch).length) return;
      const sent = api.cloneObject(patch);
      const sentRevisions = { ...keyRevisions };
      validateSettings(sent, snapshot?.settings ?? {});
      if (sent._autoRebirth === true && snapshot?.settings._autoRebirth !== true && !await confirm('Enable auto-rebirth? The active plan may rebirth as soon as its conditions are met.')) return;
      ++epoch;
      const result = await backend.saveSettingsPatch(runtimeDir, sent);
      if (result.success !== true) throw new Error('Settings save did not report success. Edits retained.');
      ++epoch;
      const pending = { ...current().patch };
      for (const key of Object.keys(sent)) if (sentRevisions[key] === keyRevisions[key]) delete pending[key];
      settingBases = mergePatch(settingBases, sent);
      // Publish the acknowledged baseline immediately so a later edit cannot use the pre-save values.
      update({ patch: pending, snapshot: current().snapshot ? { ...current().snapshot!, settings: mergePatch(current().snapshot!.settings, sent) } : null, status: 'Settings saved.' });
      consumeRoot(object(result.result), snapshot?.runtime.startedAt);
      void refresh(true, true, false);
    }, true);
  }

  async function readCurrentPlan(dir: string, name: string): Promise<JsonObject> {
    const context = epoch;
    const connected = heartbeat(current().snapshot).connected;
    const session = current().snapshot?.runtime.startedAt;
    if (connected && !canCommand(current().snapshot, 'readPlan')) throw new Error('Normalized plan reads require a running protocol v3 bridge advertising readPlan. Update or reconnect the injector.');
    const plan = await backend.readPlan(dir, name);
    if (context !== epoch || dir !== current().runtimeDir) throw new Error('Plan read was superseded by a runtime-folder change. Refresh and retry.');
    if (connected && (!canCommand(current().snapshot, 'readPlan') || !equal(session, current().snapshot?.runtime.startedAt))) throw new Error('Injector session changed or became unready while reading the plan. Previous editor retained; refresh and retry.');
    return plan;
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
      update({ saveError: saveErrors.settings });
      update({ planName: name, plan, planBase: api.cloneObject(plan), planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: current().editorVersion + 1, status: `Loaded plan '${name}'.` });
    });
  }

  async function saveActivePlan(): Promise<void> {
    await perform(async () => {
      const { runtimeDir, planName, plan, planBase } = current();
      if (!plan || !planBase || !planName) return;
      if (current().advancedDirty) throw new Error('Apply or discard the advanced JSON buffer first.');
      const sent = api.cloneObject(plan);
      assertPlanMetadata(planBase, sent);
      validatePlan(sent);
      checkWeightWrite(planBase, sent);
      if (sent.AutoRebirthEnabled === true && planBase.AutoRebirthEnabled !== true && !await confirm('Enable rebirth for this plan? If running, it may rebirth immediately.')) return;
      ++epoch;
      const fresh = await readCurrentPlan(runtimeDir, planName);
      const payload = freshPlan(planBase, sent, fresh);
      validatePlan(payload);
      checkWeightWrite(planBase, payload);
      const result = await backend.savePlan(runtimeDir, planName, payload);
      if (result.success !== true) throw new Error('Plan save did not report success. Edits retained.');
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
    if (autosaveSettings || autosavePlan) await flushAutosave();
    await perform(async () => {
      if (!dir.trim() || dir === current().runtimeDir) return;
      if (dirty() && !await confirm('Discard unsaved changes and switch data folder?')) return;
      ++epoch;
      ++refreshId;
      ++logId;
      update({ refreshing: false });
      const patchRevision = revision;
      const previousPlan = current().plan;
      // Validate/read the new folder before replacing the current session. Failure retains every edit.
      const next = await backend.getState(dir.trim());
      if (revision !== patchRevision || previousPlan !== current().plan) throw new Error('Edits were made while switching folders. Current folder and new edits retained; retry the switch.');
      keyRevisions = {};
      settingBases = {};
      acknowledgedFlags = null;
      acknowledgedRuntimeAt = null;
      autosaveSettings = autosavePlan = false;
      saveErrors.settings = saveErrors.plan = '';
      update({ saveError: '' });
      acknowledgedRuntimeRevision = null;
      update({ snapshot: next, runtimeDir: next.runtime_dir, patch: {}, plan: null, planBase: null, planName: '', planDirty: false, planDraftDirty: false, advancedDirty: false, editorVersion: current().editorVersion + 1, lastResult: null, augmentResult: null, pendingQuickGoal: null, logText: '', now: Date.now(), status: 'Runtime folder changed.' });
      backend.storeRuntimeDir(next.runtime_dir);
    });
    if (!current().planName) await refresh(true);
  }

  async function runCommand(command: string, args: JsonObject = {}): Promise<void> {
    await perform(async () => {
      if (!canCommand(current().snapshot, command)) throw new Error(`Disconnected, stale heartbeat, or unsupported command: ${command}.`);
      const augmentCommand = command === 'previewAugments' || command === 'allocateAugments';
      if (augmentCommand) validateAugmentArgs(args);
      const warnings: Record<string, string> = {
        manualRebirth: 'Rebirth now? This resets the current run.',
        unload: 'Disconnect and stop automation?',
        probeCommands: 'Run the commands in probe.txt? They may change the game.',
        reloadSettings: 'Reload saved settings? Your unsaved edits will remain.',
        optimizeGear: 'Equip this loadout?',
        allocateAugments: 'Allocate energy to the best augment and upgrade?'
      };
      if (warnings[command] && !await confirm(warnings[command])) return;
      if (!canCommand(current().snapshot, command)) throw new Error('Heartbeat expired while confirming. Refresh before retrying.');
      const dir = current().runtimeDir;
      const session = current().snapshot?.runtime.startedAt;
      const context = ++epoch;
      const sameRequest = () => context === epoch && dir === current().runtimeDir && equal(session, current().snapshot?.runtime.startedAt);
      const assertRequest = () => { if (!sameRequest()) throw new Error('Injector session changed or request was superseded while applying the action. Refresh before retrying.'); };
      update({ status: `${commandLabels[command] ?? 'Action'}: working...`, lastResult: null, pendingQuickGoal: command === 'focusGoal' && typeof args.goal === 'string' ? args.goal : null, ...(augmentCommand ? { augmentResult: null } : {}) });
      let watching = true;
      let timer: ReturnType<typeof setTimeout> | undefined;
      const watchRuntime = () => {
        timer = setTimeout(async () => {
          const request = refreshId;
          try {
            const next = await backend.getState(dir);
            if (watching && sameRequest() && request === refreshId && next.runtime_dir === dir && equal(session, next.runtime.startedAt) && heartbeat(next).connected) publishSnapshot(next);
          } catch { /* Result polling owns action errors; a transient status read can retry. */ }
          finally { if (watching && sameRequest()) watchRuntime(); }
        }, 200);
      };
      watchRuntime();
      try {
        const id = await backend.enqueueCommand(dir, command, args);
        assertRequest();
        const result = await pollResult(id, command, async () => {
          assertRequest();
          const value = await backend.readCommandResult(dir, id);
          assertRequest();
          return value;
        }, 12000, 50);
        assertRequest();
        ++epoch;
        if (command === 'focusGoal') acknowledgeSettings({ _autoRebirth: false, _useCustomRebirthPlan: false }, result.completedAt, session, api.commandRuntime(result));
        consumeRoot(result, session);
        update({ status: `${commandLabels[command] ?? 'Action'} completed.`, lastResult: result, ...(augmentCommand ? { augmentResult: result } : {}) });
        void refresh(true, true, false);
      } finally {
        watching = false;
        clearTimeout(timer);
        update({ pendingQuickGoal: null });
      }
    });
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
      const result = await backend.saveSettingsPatch(dir, patch);
      if (result.success !== true) throw new Error('Plan action did not report success.');
      if (dir !== current().runtimeDir || !equal(session, current().snapshot?.runtime.startedAt)) throw new Error('Injector session changed during the plan action. Refresh before retrying.');
      ++epoch;
      acknowledgeSettings(patch, object(result.result).completedAt, session, api.commandRuntime(object(result.result)));
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
      if (dirty() && !await confirm('Create a new plan and discard unsaved plan edits?')) return;
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
      update({ saveError: saveErrors.settings });
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
      update({ saveError: saveErrors.settings });
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

  return { subscribe: store.subscribe, current, refresh, setSetting, setPlan, editSetting, editPlan, flushAutosave, retryAutosave, saveSettings, saveSettingValue, savePlanField, beginSettingEdit, beginPlanEdit, loadPlan, saveActivePlan, changeRuntimeDir, runCommand, planAction, createPlan, deletePlan, marker, loadLog,
    dispose: () => clearTimeout(autosaveTimer),
    tick: () => update({ now: Date.now() }),
    planDraftDirty: (value: boolean) => {
      if (current().planDraftDirty !== value) update({ planDraftDirty: value });
    },
    advancedDirty: (value: boolean) => {
      update({ advancedDirty: value });
      if (!value && (autosaveSettings || autosavePlan)) scheduleAutosave();
    },
    error: (err: unknown) => update({ error: message(err) }),
    discardSettings: () => { autosaveSettings = false; saveErrors.settings = ''; keyRevisions = {}; settingBases = {}; update({ patch: {}, saveError: saveErrors.plan }); },
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
