import { afterEach, describe, expect, it, vi } from 'vitest';
import * as api from '../src/lib/api';
import { createController } from '../src/lib/controller';
import type { JsonObject, JsonValue, NguiState } from '../src/lib/api';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
const snapshot = (settings: JsonObject = {}, dir = 'A:/runtime'): NguiState => ({ runtime_dir: dir, marker_enabled: true, connected: true, protocol: {}, settings, runtime: { externalUiEnabled: true, status: 'running', protocolVersion: 3, updatedAt: new Date().toISOString(), supportedCommands: ['readPlan', 'savePlan', 'focusGoal', 'manualRebirth', 'applySettingsPatch', 'previewGear', 'previewAugments', 'allocateAugments', 'createPlan', 'deletePlan'] }, profiles: [], plans: [], logs: [], command_results: [] });
const plan = (): JsonObject => ({ Version: 14, __externalUiRevision: 'a'.repeat(64), Name: 'plan', Stages: [{ Name: 'one', Percent: 100, GearGoal: -1 }], StageLoopStartIndex: -1, StageDurationSeconds: 1800, Future: 1, RebirthHistory: [{ TimestampLocal: 'old' }] });
function setup(initial = snapshot()) {
  const backend = { ...api,
    getState: vi.fn(async () => initial), storeRuntimeDir: vi.fn(),
    saveSettingsPatch: vi.fn(async (_dir: string, _patch: JsonObject) => ({ success: true, mode: 'connected' } as JsonObject)),
    readPlan: vi.fn(async (_dir: string, _name: string) => plan()),
    savePlan: vi.fn(async (_dir: string, _name: string, _plan: JsonObject) => ({ success: true, mode: 'connected' } as JsonObject)),
    enqueueCommand: vi.fn(async (_dir: string, _command: string, _args?: JsonObject) => 'command-id'),
    readCommandResult: vi.fn(async (_dir: string, _id: string): Promise<JsonObject | null> => ({ id: 'command-id', command: 'focusGoal', success: true, message: 'Applied' })),
    createPlan: vi.fn(async (_dir: string, _name: string, _source?: JsonObject) => 'created'),
    deletePlan: vi.fn(async (_dir: string, _name: string): Promise<JsonObject> => ({ success: true, mode: 'connected', result: { data: { name: _name, selectedPlan: 'default' } } })),
    tailLog: vi.fn(async (_dir: string, _name: string) => 'log')
  };
  const confirm = vi.fn(async (_message: string) => true);
  const ui = createController(backend, confirm, initial.runtime_dir);
  return { ui, backend, confirm };
}

describe('automatic saving', () => {
  afterEach(() => vi.useRealTimers());

  it('saves a setting toggle without an explicit save action', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostEnabled: true }));
    await ui.refresh();
    backend.saveSettingsPatch.mockImplementation(async (_dir, patch) => {
      backend.getState.mockResolvedValue(snapshot(patch));
      return { success: true };
    });
    ui.editSetting('_customAutoBoostEnabled', false);
    await vi.advanceTimersByTimeAsync(0);
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { _customAutoBoostEnabled: false });
    expect(ui.current().patch).toEqual({});
    expect(ui.current().snapshot!.settings._customAutoBoostEnabled).toBe(false);
    ui.dispose();
  });

  it('debounces numeric input and submits only its final value', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostCubePowerRatio: 2 }));
    await ui.refresh();
    ui.editSetting('_customAutoBoostCubePowerRatio', 3, 350);
    await vi.advanceTimersByTimeAsync(200);
    ui.editSetting('_customAutoBoostCubePowerRatio', 4, 350);
    await vi.advanceTimersByTimeAsync(349);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { _customAutoBoostCubePowerRatio: 4 });
    expect(ui.current().patch).toEqual({});
    ui.dispose();
  });

  it('retains invalid numeric input locally until corrected', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostCubePowerRatio: 2 }));
    await ui.refresh();
    ui.editSetting('_customAutoBoostCubePowerRatio', '', 350);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual({ _customAutoBoostCubePowerRatio: '' });
    expect(ui.current().saveError).toContain('_customAutoBoostCubePowerRatio');
    ui.editSetting('_customAutoBoostCubePowerRatio', 3, 350);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { _customAutoBoostCubePowerRatio: 3 });
    expect(ui.current().error).toBe('');
    ui.dispose();
  });

  it('queues edits made during a settings write instead of losing them', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostEnabled: true, _customAutoBoostCubePowerRatio: 2 }));
    await ui.refresh();
    const saving = deferred<JsonObject>();
    backend.saveSettingsPatch.mockReturnValueOnce(saving.promise);
    ui.editSetting('_customAutoBoostEnabled', false);
    await vi.advanceTimersByTimeAsync(0);
    ui.editSetting('_customAutoBoostEnabled', true);
    ui.editSetting('_customAutoBoostCubePowerRatio', 4, 350);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(1);
    saving.resolve({ success: true });
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.saveSettingsPatch.mock.calls.map(call => call[1])).toEqual([
      { _customAutoBoostEnabled: false },
      { _customAutoBoostEnabled: true, _customAutoBoostCubePowerRatio: 4 }
    ]);
    expect(ui.current().patch).toEqual({});
    ui.dispose();
  });

  it('keeps failed writes pending without an automatic retry loop', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostEnabled: true }));
    await ui.refresh();
    backend.saveSettingsPatch.mockRejectedValueOnce(new Error('Disk full'));
    ui.editSetting('_customAutoBoostEnabled', false);
    await vi.advanceTimersByTimeAsync(10000);
    expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(1);
    expect(ui.current().patch).toEqual({ _customAutoBoostEnabled: false });
    expect(ui.current().error).toBe('Disk full');
    await ui.retryAutosave();
    expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(2);
    expect(ui.current().patch).toEqual({});
    expect(ui.current().error).toBe('');
    ui.dispose();
  });

  it('saves plan edits automatically and reads normalized values back', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    let stored = plan();
    backend.readPlan.mockImplementation(async () => api.cloneObject(stored));
    backend.savePlan.mockImplementation(async (_dir, _name, payload) => {
      stored = { ...payload, __externalUiRevision: 'b'.repeat(64) };
      return { success: true };
    });
    await ui.refresh();
    await ui.loadPlan('plan');
    ui.editPlan({ ...ui.current().plan!, TimeTriggerSeconds: 7200 });
    await vi.advanceTimersByTimeAsync(349);
    expect(backend.savePlan).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    expect(stored.TimeTriggerSeconds).toBe(7200);
    expect(ui.current().plan).toEqual(stored);
    expect(ui.current().planDirty).toBe(false);
    ui.dispose();
  });

  it('saves a reversion made during an in-flight plan write', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    let stored = plan();
    const saving = deferred<JsonObject>();
    backend.readPlan.mockImplementation(async () => api.cloneObject(stored));
    backend.savePlan.mockImplementation(async (_dir, _name, payload) => {
      if (backend.savePlan.mock.calls.length === 1) await saving.promise;
      stored = api.cloneObject(payload);
      return { success: true };
    });
    await ui.refresh();
    await ui.loadPlan('plan');
    const original = api.cloneObject(ui.current().plan!);
    ui.editPlan({ ...original, StageDurationSeconds: 3600 });
    await vi.advanceTimersByTimeAsync(350);
    ui.editPlan(original);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    saving.resolve({ success: true });
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePlan).toHaveBeenCalledTimes(2);
    expect(stored.StageDurationSeconds).toBe(1800);
    expect(ui.current().planDirty).toBe(false);
    ui.dispose();
  });

  it('autosaves committed settings and plan edits while a local timing draft is dirty', async () => {
    vi.useFakeTimers();
    const { ui, backend, confirm } = setup(snapshot({ _customAutoBoostEnabled: true }));
    let stored = plan();
    backend.readPlan.mockImplementation(async () => api.cloneObject(stored));
    backend.savePlan.mockImplementation(async (_dir, _name, payload) => {
      stored = { ...payload, __externalUiRevision: 'b'.repeat(64) };
      return { success: true };
    });
    await ui.refresh();
    await ui.loadPlan('plan');
    ui.planDraftDirty(true);
    ui.editSetting('_customAutoBoostEnabled', false);
    ui.editPlan({ ...stored, TimeTriggerSeconds: 7200 });
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { _customAutoBoostEnabled: false });
    expect(backend.savePlan).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'plan', { ...plan(), TimeTriggerSeconds: 7200 });
    expect(ui.current().patch).toEqual({});
    expect(ui.current().plan).toEqual(stored);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().planDraftDirty).toBe(true);
    expect(ui.current().saveError).toBe('');
    expect(confirm).not.toHaveBeenCalled();
    ui.planDraftDirty(false);
    expect(ui.current().planDraftDirty).toBe(false);
    ui.dispose();
  });

  it('keeps a failed settings save visible after the queued plan save succeeds', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostEnabled: true }));
    let stored = plan();
    backend.readPlan.mockImplementation(async () => api.cloneObject(stored));
    backend.savePlan.mockImplementation(async (_dir, _name, payload) => {
      stored = api.cloneObject(payload);
      return { success: true };
    });
    await ui.refresh();
    await ui.loadPlan('plan');
    backend.saveSettingsPatch.mockRejectedValueOnce(new Error('Settings rejected'));
    ui.editSetting('_customAutoBoostEnabled', false);
    ui.editPlan({ ...stored, TimeTriggerSeconds: 7200 });
    await vi.advanceTimersByTimeAsync(1000);
    expect(stored.TimeTriggerSeconds).toBe(7200);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().patch).toEqual({ _customAutoBoostEnabled: false });
    expect(ui.current().saveError).toBe('Settings rejected');
    expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(1);
    await ui.retryAutosave();
    expect(ui.current().patch).toEqual({});
    expect(ui.current().saveError).toBe('');
    ui.dispose();
  });

  it('does not copy a settings failure into a reverted clean plan', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostEnabled: true }));
    await ui.refresh();
    await ui.loadPlan('plan');
    const original = api.cloneObject(ui.current().plan!);
    backend.saveSettingsPatch.mockRejectedValueOnce(new Error('Settings rejected'));
    ui.editSetting('_customAutoBoostEnabled', false);
    ui.editPlan({ ...original, TimeTriggerSeconds: 7200 });
    ui.editPlan(original);
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().saveError).toBe('Settings rejected');
    await ui.retryAutosave();
    expect(ui.current().patch).toEqual({});
    expect(ui.current().saveError).toBe('');
    ui.dispose();
  });

  it('clears an autosave failure after the pending setting is saved inline', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customIronPillSaveSeconds: 900 }));
    await ui.refresh();
    backend.saveSettingsPatch.mockRejectedValueOnce(new Error('Settings rejected'));
    ui.editSetting('_customIronPillSaveSeconds', 1200);
    await vi.advanceTimersByTimeAsync(0);
    expect(ui.current().saveError).toBe('Settings rejected');
    backend.saveSettingsPatch.mockImplementation(async (_dir, patch) => {
      backend.getState.mockResolvedValue(snapshot(patch));
      return { success: true };
    });
    expect(await ui.saveSettingValue('_customIronPillSaveSeconds', 1200)).toBe(true);
    expect(ui.current().patch).toEqual({});
    expect(ui.current().saveError).toBe('');
    ui.dispose();
  });

  it('clears an autosave failure after the pending plan field is saved inline', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    let stored = plan();
    backend.readPlan.mockImplementation(async () => api.cloneObject(stored));
    backend.savePlan.mockRejectedValueOnce(new Error('Plan rejected'));
    await ui.refresh();
    await ui.loadPlan('plan');
    ui.editPlan({ ...stored, TimeTriggerSeconds: 7200 });
    await vi.advanceTimersByTimeAsync(350);
    expect(ui.current().saveError).toBe('Plan rejected');
    backend.savePlan.mockImplementation(async (_dir, _name, payload) => {
      stored = api.cloneObject(payload);
      return { success: true };
    });
    expect(await ui.savePlanField('TimeTriggerSeconds', 7200)).toBe(true);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().saveError).toBe('');
    ui.dispose();
  });

  it('saves a valid plan even when settings input is invalid', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ _customAutoBoostCubePowerRatio: 2 }));
    let stored = plan();
    backend.readPlan.mockImplementation(async () => api.cloneObject(stored));
    backend.savePlan.mockImplementation(async (_dir, _name, payload) => {
      stored = api.cloneObject(payload);
      return { success: true };
    });
    await ui.refresh();
    await ui.loadPlan('plan');
    ui.editSetting('_customAutoBoostCubePowerRatio', '', 350);
    ui.editPlan({ ...stored, TimeTriggerSeconds: 7200 });
    await vi.advanceTimersByTimeAsync(350);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(stored.TimeTriggerSeconds).toBe(7200);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().saveError).toContain('_customAutoBoostCubePowerRatio');
    ui.dispose();
  });

  it('reports a declined rebirth confirmation as unsaved rather than saving forever', async () => {
    vi.useFakeTimers();
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    confirm.mockResolvedValue(false);
    ui.editPlan({ ...ui.current().plan!, AutoRebirthEnabled: true });
    await vi.advanceTimersByTimeAsync(1000);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().saveError).toBe('Plan changes were not saved.');
    ui.dispose();
  });

  it('does not save the replacement editor after discarding blocked advanced edits', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    ui.editPlan({ ...ui.current().plan!, TimeTriggerSeconds: 7200 });
    ui.advancedDirty(true);
    await ui.loadPlan('other');
    await vi.advanceTimersByTimeAsync(1000);
    expect(ui.current().planName).toBe('other');
    expect(ui.current().planDirty).toBe(false);
    expect(backend.savePlan).not.toHaveBeenCalled();
    ui.dispose();
  });

  it('flushes pending plan edits before selecting another plan', async () => {
    vi.useFakeTimers();
    const { ui, backend, confirm } = setup();
    backend.savePlan.mockImplementation(async (_dir, _name, payload) => {
      backend.readPlan.mockResolvedValue(payload);
      return { success: true };
    });
    await ui.refresh();
    await ui.loadPlan('plan');
    ui.editPlan({ ...ui.current().plan!, TimeTriggerSeconds: 7200 });
    await ui.loadPlan('other');
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    expect(backend.savePlan.mock.calls[0][1]).toBe('plan');
    expect(backend.savePlan.mock.calls[0][2].TimeTriggerSeconds).toBe(7200);
    expect(ui.current().planName).toBe('other');
    expect(confirm).not.toHaveBeenCalled();
    ui.dispose();
  });
});

describe('locked runtime refresh', () => {
  const locked = 'Failed to read C:\\Users\\Kevin\\Desktop\\NGUInjector\\runtime.json: The process cannot access the file because it is being used by another process. (os error 32)';
  afterEach(() => vi.useRealTimers());

  it('retries after exactly five seconds and clears the transient error on recovery', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    backend.getState.mockRejectedValueOnce(locked);
    const refreshing = ui.refresh();
    await vi.advanceTimersByTimeAsync(0);
    expect(ui.current().error).toBe(`${locked} Retrying in 5 seconds.`);
    expect(ui.current().refreshing).toBe(true);
    await vi.advanceTimersByTimeAsync(4999);
    await ui.refresh(true);
    expect(backend.getState).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    await refreshing;
    expect(backend.getState).toHaveBeenCalledTimes(2);
    expect(ui.current().snapshot?.connected).toBe(true);
    expect(ui.current().error).toBe('');
    expect(ui.current().refreshing).toBe(false);
  });

  it('keeps retrying locked reads without losing unsaved edits', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh();
    ui.setSetting('enabled', false);
    backend.getState.mockRejectedValueOnce(new Error(locked)).mockRejectedValueOnce(new Error(locked));
    const refreshing = ui.refresh(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(ui.current().snapshot?.connected).toBe(false);
    await vi.advanceTimersByTimeAsync(5000);
    expect(backend.getState).toHaveBeenCalledTimes(3);
    expect(ui.current().error).toBe(`${locked} Retrying in 5 seconds.`);
    await vi.advanceTimersByTimeAsync(5000);
    await refreshing;
    expect(backend.getState).toHaveBeenCalledTimes(4);
    expect(ui.current().patch).toEqual({ enabled: false });
    expect(ui.current().error).toBe('');
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
  });

  it('abandons a pending retry when the runtime folder changes', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    backend.getState.mockRejectedValueOnce(locked);
    const refreshing = ui.refresh();
    await vi.advanceTimersByTimeAsync(0);
    backend.getState.mockResolvedValue(snapshot({}, 'B:/runtime'));
    await ui.changeRuntimeDir('B:/runtime');
    const calls = backend.getState.mock.calls.length;
    await vi.advanceTimersByTimeAsync(5000);
    await refreshing;
    expect(backend.getState).toHaveBeenCalledTimes(calls);
    expect(ui.current().runtimeDir).toBe('B:/runtime');
    expect(ui.current().error).toBe('');
  });

  it('does not clear an unrelated action error when the read recovers', async () => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    backend.getState.mockRejectedValueOnce(locked);
    const refreshing = ui.refresh();
    await vi.advanceTimersByTimeAsync(0);
    ui.error('Action failed');
    await vi.advanceTimersByTimeAsync(5000);
    await refreshing;
    expect(ui.current().snapshot?.connected).toBe(true);
    expect(ui.current().error).toBe('Action failed');
  });

  it.each([
    'Failed to read C:\\runtime\\runtime.json: Access is denied. (os error 5)',
    'Failed to read C:\\runtime\\settings.json: File is in use. (os error 32)',
    'Failed to parse C:\\runtime\\runtime.json: invalid JSON; file was not modified'
  ])('does not retry unrelated errors: %s', async error => {
    vi.useFakeTimers();
    const { ui, backend } = setup();
    backend.getState.mockRejectedValueOnce(error);
    await ui.refresh();
    await vi.advanceTimersByTimeAsync(5000);
    expect(backend.getState).toHaveBeenCalledTimes(1);
    expect(ui.current().error).toBe(error);
    expect(ui.current().refreshing).toBe(false);
  });
});

describe('pending settings', () => {
  it('refreshes Iron Pill history without overwriting threshold edits or submitting history', async () => {
    const history = [{ CastAtUtc: '2026-10-14T12:00:00Z', WaitSeconds: 900, Worth: 1e8, RebirthSeconds: 7200 }];
    const { ui, backend } = setup(snapshot({ _customIronPillSaveSeconds: 900, _customIronPillHistory: [] }));
    await ui.refresh();
    ui.setSetting('_customIronPillSaveSeconds', 1200);
    backend.getState.mockResolvedValue(snapshot({ _customIronPillSaveSeconds: 900, _customIronPillHistory: history }));
    await ui.refresh(true);
    expect(ui.current().snapshot!.settings._customIronPillHistory).toEqual(history);
    expect(ui.current().patch).toEqual({ _customIronPillSaveSeconds: 1200 });
    await ui.saveSettings();
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { _customIronPillSaveSeconds: 1200 });
  });
  it('rebases untouched raw history onto the latest backend history', async () => {
    const base = { _customIronPillMinWorth: 0, _customIronPillHistory: [] };
    const history = [{ CastAtUtc: '2026-10-14T12:00:00Z' }];
    const { ui, backend } = setup(snapshot(base));
    await ui.refresh();
    backend.getState.mockResolvedValue(snapshot({ ...base, _customIronPillHistory: history }));
    await ui.refresh(true);
    ui.applyRawSettings({ ...base, _customIronPillMinWorth: 1e8 }, base);
    expect(ui.current().patch).toEqual({ _customIronPillMinWorth: 1e8 });
    expect(ui.current().snapshot!.settings._customIronPillHistory).toEqual(history);
  });
  it('rejects raw edits to history before applying any settings', async () => {
    const base = { _customIronPillMinWorth: 0, _customIronPillHistory: [] };
    const { ui } = setup(snapshot(base));
    await ui.refresh();
    expect(() => ui.applyRawSettings({ ...base, _customIronPillMinWorth: 5, _customIronPillHistory: [{ Worth: 10 }] }, base)).toThrow('backend-owned');
    expect(ui.current().patch).toEqual({});
  });
  it('blocks direct history patches before calling the save backend', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    ui.setSetting('_customIronPillHistory', []);
    await ui.saveSettings();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('backend-owned');
  });
  it('does not mutate the disk snapshot when a field is edited', async () => {
    const { ui } = setup(snapshot({ enabled: true, Future: 42 }));
    await ui.refresh();
    ui.setSetting('enabled', false);
    expect(ui.current().snapshot!.settings).toEqual({ enabled: true, Future: 42 });
    expect(ui.current().patch).toEqual({ enabled: false });
  });
  it('refreshes the baseline without clobbering pending edits', async () => {
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh();
    ui.setSetting('enabled', false);
    backend.getState.mockResolvedValue(snapshot({ enabled: true, Future: 43 }));
    await ui.refresh(true);
    expect(ui.current().patch).toEqual({ enabled: false });
    expect(ui.current().snapshot!.settings.Future).toBe(43);
  });
  it('sends only captured pending keys and retains later edits', async () => {
    const { ui, backend } = setup(snapshot({ enabled: true, ratio: 1, Future: 42 }));
    await ui.refresh();
    ui.setSetting('enabled', false);
    const saving = deferred<JsonObject>();
    backend.saveSettingsPatch.mockReturnValueOnce(saving.promise);
    const pending = ui.saveSettings();
    ui.setSetting('enabled', true);
    ui.setSetting('ratio', 2);
    saving.resolve({ success: true, mode: 'connected' });
    backend.getState.mockResolvedValue(snapshot({ enabled: false, ratio: 1, Future: 42 }));
    await pending;
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { enabled: false });
    expect(ui.current().patch).toEqual({ enabled: true, ratio: 2 });
  });
  it('keeps an away-and-back edit made during the save pending', async () => {
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh();
    ui.setSetting('enabled', false);
    const saving = deferred<JsonObject>();
    backend.saveSettingsPatch.mockReturnValueOnce(saving.promise);
    const pending = ui.saveSettings();
    ui.setSetting('enabled', true);
    ui.setSetting('enabled', false);
    saving.resolve({ success: true });
    await pending;
    expect(ui.current().patch).toEqual({ enabled: false });
  });
  it('retains all pending edits when a save fails', async () => {
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh();
    ui.setSetting('enabled', false);
    backend.saveSettingsPatch.mockRejectedValueOnce(new Error('Rejected'));
    await ui.saveSettings();
    expect(ui.current().patch).toEqual({ enabled: false });
    expect(ui.current().error).toBe('Rejected');
    expect(ui.current().busy).toBe(false);
  });
  it('does not clear the write busy flag when quiet refresh completes', async () => {
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh();
    ui.setSetting('enabled', false);
    const saving = deferred<JsonObject>();
    backend.saveSettingsPatch.mockReturnValueOnce(saving.promise);
    const pending = ui.saveSettings();
    await ui.refresh(true);
    expect(ui.current().busy).toBe(true);
    saving.resolve({ success: true });
    await pending;
    expect(ui.current().busy).toBe(false);
  });
  it('does not publish a refresh started before the acknowledged save', async () => {
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh();
    const stale = deferred<NguiState>();
    backend.getState.mockReturnValueOnce(stale.promise);
    const refreshing = ui.refresh(true);
    ui.setSetting('enabled', false);
    backend.getState.mockResolvedValue(snapshot({ enabled: false }));
    await ui.saveSettings();
    stale.resolve(snapshot({ enabled: true }));
    await refreshing;
    expect(ui.current().snapshot!.settings.enabled).toBe(false);
    expect(ui.current().patch).toEqual({});
  });
});

describe('local plan timing draft safety', () => {
  it.each([false, true])('guards plan loading with only a local draft when discard is %s', async accepted => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    const before = ui.current();
    expect(before.planDraftDirty).toBe(false);
    ui.planDraftDirty(true);
    confirm.mockResolvedValueOnce(accepted);
    const other = { ...plan(), Name: 'other' };
    backend.readPlan.mockResolvedValueOnce(other);
    await ui.loadPlan('other');
    expect(confirm).toHaveBeenCalledExactlyOnceWith('Discard unsaved plan / advanced JSON edits and load another plan?');
    expect(backend.readPlan).toHaveBeenCalledTimes(accepted ? 2 : 1);
    expect(ui.current().planName).toBe(accepted ? 'other' : 'plan');
    expect(ui.current().plan).toBe(accepted ? other : before.plan);
    expect(ui.current().planBase).toEqual(accepted ? other : before.planBase);
    expect(ui.current().editorVersion).toBe(before.editorVersion + Number(accepted));
    expect(ui.current().planDraftDirty).toBe(!accepted);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().advancedDirty).toBe(false);
    expect(backend.savePlan).not.toHaveBeenCalled();
  });

  it('retains the local draft flag when an accepted plan load fails', async () => {
    const { ui, backend, confirm } = setup();
    await ui.loadPlan('plan');
    const before = ui.current();
    ui.planDraftDirty(true);
    backend.readPlan.mockRejectedValueOnce(new Error('Unreadable'));
    await ui.loadPlan('other');
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(ui.current().plan).toBe(before.plan);
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().editorVersion).toBe(before.editorVersion);
    expect(ui.current().planDraftDirty).toBe(true);
    expect(ui.current().error).toBe('Unreadable');
  });

  it.each([false, true])('guards folder switching with only a local draft when discard is %s', async accepted => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    const before = ui.current();
    ui.planDraftDirty(true);
    confirm.mockResolvedValueOnce(accepted);
    backend.getState.mockResolvedValue(snapshot({}, 'B:/other'));
    await ui.changeRuntimeDir('B:/other');
    expect(confirm).toHaveBeenCalledExactlyOnceWith('Discard unsaved changes and switch data folder?');
    expect(backend.getState).toHaveBeenCalledTimes(accepted ? 3 : 1);
    expect(ui.current().runtimeDir).toBe(accepted ? 'B:/other' : 'A:/runtime');
    expect(ui.current().plan).toBe(accepted ? null : before.plan);
    expect(ui.current().planName).toBe(accepted ? '' : 'plan');
    expect(ui.current().planDraftDirty).toBe(!accepted);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().advancedDirty).toBe(false);
    if (accepted) expect(backend.storeRuntimeDir).toHaveBeenCalledWith('B:/other');
    else expect(backend.storeRuntimeDir).not.toHaveBeenCalledWith('B:/other');
  });

  it.each([false, true])('guards plan creation with only a local draft when discard is %s', async accepted => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    const before = ui.current();
    ui.planDraftDirty(true);
    confirm.mockResolvedValueOnce(accepted);
    await ui.createPlan('new', false);
    expect(confirm).toHaveBeenCalledExactlyOnceWith('Create a new plan and discard unsaved plan edits?');
    expect(backend.createPlan).toHaveBeenCalledTimes(Number(accepted));
    expect(ui.current().planName).toBe(accepted ? 'created' : 'plan');
    expect(ui.current().editorVersion).toBe(before.editorVersion + Number(accepted));
    expect(ui.current().planDraftDirty).toBe(!accepted);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().advancedDirty).toBe(false);
    if (accepted) expect(backend.createPlan).toHaveBeenCalledWith('A:/runtime', 'new', undefined);
    else expect(ui.current().plan).toBe(before.plan);
  });

  it.each([
    ['activate', undefined], ['run', undefined], ['run', false], ['run', true]
  ] as const)('blocks %s with auto-rebirth=%s while only a local timing draft is dirty', async (action, autoRebirth) => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    const before = ui.current();
    ui.planDraftDirty(true);
    await ui.planAction(action, autoRebirth);
    expect(backend.readPlan).toHaveBeenCalledTimes(1);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().plan).toBe(before.plan);
    expect(ui.current().planBase).toBe(before.planBase);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().advancedDirty).toBe(false);
    expect(ui.current().planDraftDirty).toBe(true);
    expect(ui.current().error).toContain('Save or discard plan');
    expect(confirm).not.toHaveBeenCalled();
  });

  it.each([false, true])('blocks starting with auto-rebirth=%s when a local timing draft appears during the plan read', async autoRebirth => {
    const { ui, backend } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    const reading = deferred<JsonObject>();
    backend.readPlan.mockReturnValueOnce(reading.promise);
    const starting = ui.planAction('run', autoRebirth);
    expect(backend.readPlan).toHaveBeenCalledTimes(2);
    expect(ui.current().busy).toBe(true);
    ui.planDraftDirty(true);
    const fresh = { ...plan(), AutoRebirthEnabled: true, Future: 2 };
    reading.resolve(fresh);
    await starting;
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().plan).toEqual(fresh);
    expect(ui.current().planBase).toEqual(fresh);
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().advancedDirty).toBe(false);
    expect(ui.current().planDraftDirty).toBe(true);
    expect(ui.current().error).toContain('edited while starting');
    expect(ui.current().busy).toBe(false);
  });

  it('allows stopping while only a local timing draft is dirty', async () => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    await ui.loadPlan('plan');
    ui.planDraftDirty(true);
    await ui.planAction('stop');
    expect(backend.saveSettingsPatch).toHaveBeenCalledExactlyOnceWith('A:/runtime', { _useCustomRebirthPlan: false, _autoRebirth: false });
    expect(backend.readPlan).toHaveBeenCalledTimes(1);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().planDraftDirty).toBe(true);
    expect(ui.current().error).toBe('');
    expect(confirm).not.toHaveBeenCalled();
  });
});

describe('plan saves and selection', () => {
  it.each(['default', '24hr', '1h-3m-rebirth', 'infinite'])('quick-selects %s without activating or starting rebirth', async name => {
    const { ui, backend } = setup();
    await ui.refresh();
    await ui.loadPlan(name);
    expect(ui.current().planName).toBe(name);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
  });
  it('guards unsaved selection without changing the selected name on cancellation', async () => {
    const { ui, backend, confirm } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    confirm.mockResolvedValueOnce(false);
    await ui.loadPlan('other');
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().planDirty).toBe(true);
    expect(backend.readPlan).toHaveBeenCalledTimes(1);
  });
  it('keeps the old plan if reading another plan fails', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    backend.readPlan.mockRejectedValueOnce(new Error('Unreadable'));
    await ui.loadPlan('other');
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().plan!.Name).toBe('plan');
  });
  it('merges against fresh disk fields and reads normalized results back', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    const fresh = { ...plan(), Future: 2, RebirthHistory: [{ TimestampLocal: 'new' }] };
    backend.readPlan.mockResolvedValueOnce(fresh).mockResolvedValueOnce({ ...fresh, StageDurationSeconds: 3600 });
    await ui.saveActivePlan();
    expect(backend.savePlan).toHaveBeenCalledWith('A:/runtime', 'plan', { ...fresh, StageDurationSeconds: 3600 });
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().plan!.RebirthHistory).toEqual([{ TimestampLocal: 'new' }]);
  });
  it('rebases plan edits made while saving onto the returned snapshot', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    const saving = deferred<JsonObject>();
    backend.savePlan.mockReturnValueOnce(saving.promise);
    backend.readPlan.mockResolvedValueOnce({ ...plan(), __externalUiRevision: 'b'.repeat(64) }).mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 3600, Future: 2, __externalUiRevision: 'c'.repeat(64) });
    const pending = ui.saveActivePlan();
    await Promise.resolve();
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 7200 });
    saving.resolve({ success: true });
    await pending;
    expect(ui.current().plan!.StageDurationSeconds).toBe(7200);
    expect(ui.current().plan!.Future).toBe(2);
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().plan!.__externalUiRevision).toBe('c'.repeat(64));
    expect(ui.current().planBase!.__externalUiRevision).toBe('c'.repeat(64));
  });
  it('saves with the fresh CAS token while retaining the old immutable editor token until reload', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    const saving = deferred<JsonObject>();
    backend.savePlan.mockReturnValueOnce(saving.promise);
    backend.readPlan.mockResolvedValueOnce({ ...plan(), __externalUiRevision: 'b'.repeat(64) }).mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 3600, __externalUiRevision: 'c'.repeat(64) });
    const pending = ui.saveActivePlan();
    await vi.waitFor(() => expect(backend.savePlan).toHaveBeenCalledTimes(1));
    expect(backend.savePlan).toHaveBeenCalledWith('A:/runtime', 'plan', { ...plan(), StageDurationSeconds: 3600, __externalUiRevision: 'b'.repeat(64) });
    expect(ui.current().plan!.__externalUiRevision).toBe('a'.repeat(64));
    saving.resolve({ success: true });
    await pending;
    expect(ui.current().plan!.__externalUiRevision).toBe('c'.repeat(64));
    expect(ui.current().planDirty).toBe(false);
  });
  it('retains edits and the original metadata on a backend CAS conflict', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    backend.readPlan.mockResolvedValueOnce({ ...plan(), __externalUiRevision: 'b'.repeat(64) });
    backend.savePlan.mockRejectedValueOnce(new Error('Plan revision conflict: changed after reading'));
    await ui.saveActivePlan();
    expect(ui.current().error).toContain('revision conflict');
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().plan!.__externalUiRevision).toBe('a'.repeat(64));
    expect(ui.current().planDirty).toBe(true);
  });
  it('rejects transport-token changes from programmatic editor updates', async () => {
    const { ui } = setup();
    await ui.loadPlan('plan');
    expect(() => ui.setPlan({ ...ui.current().plan!, __externalUiRevision: 'b'.repeat(64) })).toThrow('backend-owned');
    expect(ui.current().plan!.__externalUiRevision).toBe('a'.repeat(64));
    expect(ui.current().planDirty).toBe(false);
  });
  it('rejects overlapping external plan edits without writing', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    backend.readPlan.mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 7200 });
    await ui.saveActivePlan();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().error).toContain('Source changed');
  });
});

describe('native read latency and fresh heartbeat snapshots', () => {
  it('does not publish a post-save reload from an injector session that changed while reading', async () => {
    const initial = snapshot(); initial.runtime.startedAt = 'old';
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    const reading = deferred<JsonObject>();
    backend.readPlan.mockResolvedValueOnce({ ...plan(), __externalUiRevision: 'b'.repeat(64) }).mockReturnValueOnce(reading.promise);
    const saving = ui.saveActivePlan();
    await vi.waitFor(() => expect(backend.readPlan).toHaveBeenCalledTimes(3));
    const next = snapshot(); next.runtime.startedAt = 'new';
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    reading.resolve({ ...plan(), StageDurationSeconds: 7200, __externalUiRevision: 'c'.repeat(64) });
    await saving;
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().plan!.__externalUiRevision).toBe('a'.repeat(64));
    expect(ui.current().error).toContain('session changed');
  });
  it('keeps polling snapshots while refresh waits for the initial normalized plan read', async () => {
    const initial = snapshot({ _customPlanFile: 'plan' }); initial.plans = ['plan'];
    const { ui, backend } = setup(initial);
    const reading = deferred<JsonObject>();
    backend.readPlan.mockReturnValueOnce(reading.promise);
    const refresh = ui.refresh();
    await Promise.resolve();
    expect(ui.current().busy).toBe(true);
    expect(ui.current().refreshing).toBe(false);
    const next = { ...initial, runtime: { ...initial.runtime, customStatus: 'Latest heartbeat' } };
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    expect(backend.getState).toHaveBeenCalledTimes(2);
    expect(backend.readPlan).toHaveBeenCalledTimes(1);
    expect(ui.current().snapshot!.runtime.customStatus).toBe('Latest heartbeat');
    reading.resolve(plan());
    await refresh;
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().snapshot!.runtime.customStatus).toBe('Latest heartbeat');
    expect(ui.current().busy).toBe(false);
  });
  it.each(['starting', 'failed'])('rejects a remote plan that completes after runtime becomes %s', async status => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    const reading = deferred<JsonObject>();
    backend.readPlan.mockReturnValueOnce(reading.promise);
    const loading = ui.loadPlan('other');
    const next = snapshot(); next.runtime.status = status;
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    reading.resolve({ ...plan(), Name: 'other', __externalUiRevision: 'b'.repeat(64) });
    await loading;
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().plan!.__externalUiRevision).toBe('a'.repeat(64));
    expect(ui.current().error).toContain('became unready');
  });
  it('rejects a late result from a previous injector session', async () => {
    const initial = snapshot(); initial.runtime.startedAt = 'old';
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('plan');
    const reading = deferred<JsonObject>();
    backend.readPlan.mockReturnValueOnce(reading.promise);
    const loading = ui.loadPlan('other');
    const next = snapshot(); next.runtime.startedAt = 'new';
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    reading.resolve({ ...plan(), Name: 'other' });
    await loading;
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().error).toContain('session changed');
  });
  it('retains edits made while a native read is underway', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    const reading = deferred<JsonObject>();
    backend.readPlan.mockReturnValueOnce(reading.promise);
    const loading = ui.loadPlan('other');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    reading.resolve({ ...plan(), Name: 'other' });
    await loading;
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().planDirty).toBe(true);
  });
  it('requires protocol v3 readPlan capability before a connected native read', async () => {
    const initial = snapshot(); initial.runtime.protocolVersion = 2;
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('plan');
    expect(backend.readPlan).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('protocol v3');
  });
  it.each(['starting', 'failed'])('does not enqueue a live action while runtime is %s', async status => {
    const initial = snapshot(); initial.runtime.status = status;
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('Disconnected');
  });
});

describe('connected plan creation and deletion', () => {
  it('waits for connected plan creation before selecting the new plan for editing', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    const created = deferred<string>();
    backend.createPlan.mockReturnValueOnce(created.promise);
    const pending = ui.createPlan('new', false);
    expect(ui.current().busy).toBe(true);
    expect(ui.current().status).toBe('Creating plan...');
    expect(ui.current().planName).toBe('');
    expect(backend.createPlan).toHaveBeenCalledWith('A:/runtime', 'new', undefined);
    created.resolve('created');
    await pending;
    expect(ui.current().planName).toBe('created');
    expect(ui.current().busy).toBe(false);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });
  it('copies configuration and unknown fields with empty runtime history', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    const original = ui.current().plan!;
    await ui.createPlan('copy', true);
    const expected: JsonObject = { ...original, RebirthHistory: [] };
    delete expected.__externalUiRevision;
    expect(backend.createPlan).toHaveBeenCalledWith('A:/runtime', 'copy', expected);
    expect(original.RebirthHistory).toEqual([{ TimestampLocal: 'old' }]);
    expect(original.Future).toBe(1);
  });
  it('requires connected creation support without falling back to a direct file write', async () => {
    const initial = snapshot();
    initial.runtime.supportedCommands = ['applySettingsPatch'];
    const { ui, backend } = setup(initial);
    await ui.refresh();
    await ui.createPlan('new', false);
    expect(backend.createPlan).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('does not support plan creation');
  });
  it('rechecks creation support after the discard confirmation', async () => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    ui.setSetting('pending', true);
    confirm.mockImplementationOnce(async () => {
      const next = snapshot(); next.runtime.supportedCommands = [];
      backend.getState.mockResolvedValue(next);
      await ui.refresh(true);
      return true;
    });
    await ui.createPlan('new', false);
    expect(backend.createPlan).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual({ pending: true });
    expect(ui.current().error).toContain('creation is unsupported');
  });
  it('still allows offline plan creation', async () => {
    const { ui, backend } = setup({ ...snapshot(), connected: false });
    await ui.refresh();
    await ui.createPlan('new', false);
    expect(backend.createPlan).toHaveBeenCalledWith('A:/runtime', 'new', undefined);
    expect(ui.current().planName).toBe('created');
  });
  it('keeps local edits when the connected creation fails', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    ui.setPlan({ ...plan(), StageDurationSeconds: 3600 });
    ui.planDraftDirty(true);
    backend.createPlan.mockRejectedValueOnce(new Error('Creation rejected'));
    await ui.createPlan('copy', true);
    expect(ui.current().planName).toBe('plan');
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().planDraftDirty).toBe(true);
  });
  it.each(['default', '24hr', '1h-3m-rebirth', 'infinite'])('blocks deletion of builtin %s before confirmation', async name => {
    const { ui, backend, confirm } = setup();
    await ui.refresh(); await ui.loadPlan(name);
    await ui.deletePlan();
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.deletePlan).not.toHaveBeenCalled();
    expect(ui.current().planName).toBe(name);
    expect(ui.current().error).toContain('Built-in plans');
  });
  it('blocks deletion of the active running plan even with pending stop edits', async () => {
    const initial = snapshot();
    initial.plans = ['default', 'custom', 'other'];
    initial.runtime.settings = { customPlanFile: 'custom', useCustomRebirthPlan: true };
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('custom');
    ui.setSetting('_useCustomRebirthPlan', false);
    await ui.deletePlan();
    expect(backend.deletePlan).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('active running plan');
    expect(ui.current().patch).toEqual({ _useCustomRebirthPlan: false });
  });
  it('waits for connected deletion and refreshes the backend fallback selection', async () => {
    const initial = snapshot({ _customPlanFile: 'custom' });
    initial.plans = ['default', 'custom'];
    initial.runtime.settings = { customPlanFile: 'custom', useCustomRebirthPlan: false };
    const { ui, backend } = setup(initial);
    await ui.refresh();
    ui.planDraftDirty(true);
    const deleted = deferred<JsonObject>();
    backend.deletePlan.mockReturnValueOnce(deleted.promise);
    const pending = ui.deletePlan();
    await Promise.resolve();
    expect(ui.current().planName).toBe('custom');
    expect(ui.current().planDraftDirty).toBe(true);
    expect(ui.current().busy).toBe(true);
    const next = snapshot({ _customPlanFile: 'default' });
    next.plans = ['default']; next.runtime.settings = { customPlanFile: 'default', useCustomRebirthPlan: false };
    backend.getState.mockResolvedValue(next);
    deleted.resolve({ success: true, mode: 'connected', result: { data: { name: 'custom', selectedPlan: 'default' } } });
    await pending;
    expect(backend.deletePlan).toHaveBeenCalledWith('A:/runtime', 'custom');
    expect(ui.current().planName).toBe('');
    expect(ui.current().planDraftDirty).toBe(false);
    expect(ui.current().snapshot!.settings._customPlanFile).toBe('default');
    expect(ui.current().busy).toBe(false);
  });
  it('requires connected deletion support', async () => {
    const initial = snapshot(); initial.plans = ['default', 'custom']; initial.runtime.supportedCommands = ['readPlan'];
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('custom');
    expect(ui.current().planName).toBe('custom');
    expect(ui.current().error).toBe('');
    await ui.deletePlan();
    expect(backend.deletePlan).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('does not support plan deletion');
  });
  it('allows offline deletion without connected command support', async () => {
    const initial = { ...snapshot(), connected: false }; initial.plans = ['default', 'custom']; initial.runtime.supportedCommands = [];
    const { ui, backend } = setup(initial);
    backend.deletePlan.mockResolvedValueOnce({ success: true, mode: 'offline', data: { name: 'custom', selectedPlan: 'default' } });
    await ui.refresh(); await ui.loadPlan('custom');
    await ui.deletePlan();
    expect(backend.deletePlan).toHaveBeenCalledWith('A:/runtime', 'custom');
    expect(ui.current().planName).toBe('');
  });
  it('rechecks running-plan protection after confirmation', async () => {
    const initial = snapshot(); initial.plans = ['default', 'custom'];
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh(); await ui.loadPlan('custom');
    confirm.mockImplementationOnce(async () => {
      const next = { ...initial, runtime: { ...initial.runtime, settings: { customPlanFile: 'custom', useCustomRebirthPlan: true } } };
      backend.getState.mockResolvedValue(next);
      await ui.refresh(true);
      return true;
    });
    await ui.deletePlan();
    expect(backend.deletePlan).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('active running plan');
  });
  it('keeps edits when deletion fails', async () => {
    const initial = snapshot(); initial.plans = ['default', 'custom'];
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('custom');
    ui.setPlan({ ...plan(), StageDurationSeconds: 3600 });
    ui.planDraftDirty(true);
    backend.deletePlan.mockRejectedValueOnce(new Error('Deletion rejected'));
    await ui.deletePlan();
    expect(ui.current().planName).toBe('custom');
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().planDraftDirty).toBe(true);
    expect(ui.current().error).toBe('Deletion rejected');
  });
  it('does not discard the editor for an unsuccessful deletion response', async () => {
    const initial = snapshot(); initial.plans = ['default', 'custom'];
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('custom');
    backend.deletePlan.mockResolvedValueOnce({ success: false, mode: 'connected' });
    await ui.deletePlan();
    expect(ui.current().planName).toBe('custom');
    expect(ui.current().error).toContain('did not report success');
  });
  it('rejects an invalid fallback selection instead of inventing a successful state', async () => {
    const initial = snapshot(); initial.plans = ['default', 'custom'];
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('custom');
    backend.deletePlan.mockResolvedValueOnce({ success: true, mode: 'connected', result: { data: {} } });
    await ui.deletePlan();
    expect(ui.current().error).toContain('selected-plan result is invalid');
    expect(ui.current().planName).toBe('custom');
  });
});

describe('augment command handling', () => {
  const metrics: JsonObject = { augmentIndex: 2, usesUpgrade: true, energyToAugment: 100, energyToUpgrade: 200, seconds: 120, expectedSeconds: 120, valuePerSecond: 10, expectedValue: 1200, projectedGoldCost: 1000, projectedGoldBudget: 2000, goldPerSecond: 50, energyPower: 3, augmentSpeedMultiplier: 4, projectedWork: 108000, progressFraction: 1 };
  it('previews the live saved estimate without sending pending settings', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    ui.setSetting('_customAugmentsBloodEstimateSeconds', 600);
    backend.readCommandResult.mockResolvedValueOnce({ id: 'command-id', command: 'previewAugments', success: true, message: 'Best augment', data: metrics });
    await ui.runCommand('previewAugments');
    expect(backend.enqueueCommand).toHaveBeenCalledWith('A:/runtime', 'previewAugments', {});
    expect(ui.current().augmentResult!.data).toEqual(metrics);
    expect(ui.current().patch).toEqual({ _customAugmentsBloodEstimateSeconds: 600 });
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });
  it('passes a positive override without persisting it as a setting', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    backend.readCommandResult.mockResolvedValueOnce({ id: 'command-id', command: 'previewAugments', success: true, data: metrics });
    await ui.runCommand('previewAugments', { expectedSeconds: 600 });
    expect(backend.enqueueCommand).toHaveBeenCalledWith('A:/runtime', 'previewAugments', { expectedSeconds: 600 });
    expect(ui.current().patch).toEqual({});
  });
  it.each([0, -1, 1.5, Infinity, 2147483648, '300', null])('does not enqueue an invalid estimate: %s', async expectedSeconds => {
    const { ui, backend } = setup();
    await ui.refresh();
    await ui.runCommand('previewAugments', { expectedSeconds });
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('expectedSeconds');
  });
  it('requires an explicit confirmation before allocation', async () => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    confirm.mockResolvedValueOnce(false);
    await ui.runCommand('allocateAugments', { expectedSeconds: 300 });
    expect(confirm).toHaveBeenCalledWith('Allocate energy to the best augment and upgrade?');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
  });
  it('awaits allocation completion before publishing optimizer metrics', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    const completed = deferred<JsonObject | null>();
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const pending = ui.runCommand('allocateAugments', { expectedSeconds: 300 });
    await Promise.resolve(); await Promise.resolve();
    expect(ui.current().status).toBe('Allocate augment: working...');
    expect(ui.current().augmentResult).toBe(null);
    expect(ui.current().busy).toBe(true);
    completed.resolve({ id: 'command-id', command: 'allocateAugments', success: true, message: 'Allocated', data: metrics });
    await pending;
    expect(ui.current().status).toBe('Allocate augment completed.');
    expect(ui.current().augmentResult!.data).toEqual(metrics);
    expect(ui.current().busy).toBe(false);
  });
  it('retains augment metrics when an unrelated command completes', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    const result = { id: 'command-id', command: 'previewAugments', success: true, data: metrics };
    backend.readCommandResult.mockResolvedValueOnce(result);
    await ui.runCommand('previewAugments');
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().augmentResult).toEqual(result);
    expect(ui.current().lastResult!.command).toBe('focusGoal');
  });
  it('clears prior augment metrics on a failed new attempt', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    backend.readCommandResult.mockResolvedValueOnce({ id: 'command-id', command: 'previewAugments', success: true, data: metrics });
    await ui.runCommand('previewAugments');
    backend.readCommandResult.mockResolvedValueOnce({ id: 'command-id', command: 'allocateAugments', success: false, message: 'No affordable unlocked augment target found.' });
    await ui.runCommand('allocateAugments');
    expect(ui.current().augmentResult).toBe(null);
    expect(ui.current().error).toBe('No affordable unlocked augment target found.');
  });
  it.each(['previewAugments', 'allocateAugments'])('blocks disconnected %s actions', async command => {
    const { ui, backend } = setup({ ...snapshot(), connected: false });
    await ui.refresh();
    await ui.runCommand(command);
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('Disconnected');
  });
  it('blocks an unadvertised preview command', async () => {
    const initial = snapshot(); initial.runtime.supportedCommands = ['focusGoal'];
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.runCommand('previewAugments');
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('unsupported command');
  });
});

describe('runtime folder and live command safety', () => {
  it('retains the old folder and edits when the new path fails', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    ui.setSetting('enabled', false);
    ui.planDraftDirty(true);
    backend.getState.mockRejectedValueOnce(new Error('Bad path'));
    await ui.changeRuntimeDir('B:/other');
    expect(ui.current().runtimeDir).toBe('A:/runtime');
    expect(ui.current().patch).toEqual({ enabled: false });
    expect(ui.current().planDraftDirty).toBe(true);
    expect(backend.storeRuntimeDir).not.toHaveBeenCalledWith('B:/other');
  });
  it('rejects stale refresh responses after a path switch', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    const stale = deferred<NguiState>();
    backend.getState.mockReturnValueOnce(stale.promise);
    const refreshing = ui.refresh(true);
    backend.getState.mockResolvedValue(snapshot({}, 'B:/other'));
    await ui.changeRuntimeDir('B:/other');
    stale.resolve(snapshot({ wrong: true }));
    await refreshing;
    expect(ui.current().runtimeDir).toBe('B:/other');
    expect(ui.current().snapshot!.settings).toEqual({});
  });
  it('blocks a disconnected focus command before enqueueing', async () => {
    const { ui, backend } = setup({ ...snapshot(), connected: false });
    await ui.refresh(); await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('Disconnected');
  });
  it('awaits successful commands before reporting completion and refreshing active focus', async () => {
    const { ui, backend } = setup();
    await ui.refresh();
    const result = deferred<JsonObject | null>();
    backend.readCommandResult.mockReturnValueOnce(result.promise);
    const pending = ui.runCommand('focusGoal', { goal: 'TimeMachine', mode: 'toggle' });
    await Promise.resolve();
    expect(ui.current().status).toBe('Quick: working...');
    expect(ui.current().busy).toBe(true);
    const fresh = snapshot(); fresh.runtime.activeFocusGoal = 'TimeMachine';
    backend.getState.mockResolvedValue(fresh);
    result.resolve({ id: 'command-id', command: 'focusGoal', success: true, message: 'Applied' });
    await pending;
    expect(ui.current().status).toBe('Quick completed.');
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    expect(ui.current().busy).toBe(false);
  });
  it('does not enqueue rebirth after its confirmation is cancelled', async () => {
    const { ui, backend, confirm } = setup();
    await ui.refresh();
    confirm.mockResolvedValueOnce(false);
    await ui.runCommand('manualRebirth');
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Rebirth now?'));
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
  });
  it('refuses to run a dirty plan without touching pending settings', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    ui.setPlan({ ...plan(), StageDurationSeconds: 3600 });
    ui.setSetting('other', true);
    await ui.planAction('run');
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual({ other: true });
    expect(ui.current().error).toContain('Save or discard plan');
  });
  it('activates a different running rebirth plan without confirmation', async () => {
    const initial = snapshot();
    initial.runtime.settings = { customPlanFile: 'other', useCustomRebirthPlan: true, autoRebirth: true };
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh(); await ui.loadPlan('24hr');
    confirm.mockResolvedValueOnce(false);
    await ui.planAction('activate');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { _customPlanFile: '24hr' });
  });
});

describe('dashboard plan controls', () => {
  it('starts with auto-rebirth off without rewriting the plan', async () => {
    const { ui, backend, confirm } = setup(snapshot({ _autoRebirth: true }));
    await ui.refresh(); await ui.loadPlan('plan');
    const fresh = { ...plan(), TimeTriggerSeconds: 7200, __externalUiRevision: 'b'.repeat(64) };
    backend.readPlan.mockResolvedValueOnce(fresh);
    await ui.planAction('run', false);
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { _customPlanFile: 'plan', _useCustomRebirthPlan: true, _autoRebirth: false });
    expect(ui.current().plan).toEqual(fresh);
    expect(ui.current().planBase).toEqual(fresh);
  });
  it('enables both plan rebirth and the global switch without confirmation', async () => {
    const { ui, backend, confirm } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    const fresh = { ...plan(), AutoRebirthEnabled: false, Future: 42, __externalUiRevision: 'b'.repeat(64) };
    backend.readPlan.mockResolvedValueOnce(fresh).mockResolvedValueOnce({ ...fresh, AutoRebirthEnabled: true, __externalUiRevision: 'c'.repeat(64) });
    await ui.planAction('run', true);
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.savePlan).toHaveBeenCalledWith('A:/runtime', 'plan', { ...fresh, AutoRebirthEnabled: true });
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { _customPlanFile: 'plan', _useCustomRebirthPlan: true, _autoRebirth: true });
    expect(ui.current().plan?.__externalUiRevision).toBe('c'.repeat(64));
  });
  it('does not rewrite a plan which already allows rebirth', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    const fresh = { ...plan(), AutoRebirthEnabled: true, TimeTriggerSeconds: 7200, __externalUiRevision: 'b'.repeat(64) };
    backend.readPlan.mockResolvedValue(fresh);
    await ui.planAction('run', true);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', expect.objectContaining({ _autoRebirth: true }));
    expect(ui.current().plan).toEqual(fresh);
    expect(ui.current().planBase).toEqual(fresh);
  });
  it('does not start the plan when normalized read-back did not enable auto-rebirth', async () => {
    const { ui, backend, confirm } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    confirm.mockResolvedValueOnce(false);
    await ui.planAction('run', true);
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('did not enable rebirth');
  });
  it('does not enable automation when the plan save conflicts', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    backend.savePlan.mockRejectedValueOnce(new Error('Plan changed. Reload before saving.'));
    await ui.planAction('run', true);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('Plan changed');
  });
  it('preserves pending settings instead of replacing their run switches', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('plan');
    ui.setSetting('_autoRebirth', false);
    await ui.planAction('run', true);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual({ _autoRebirth: false });
  });
});

describe('direct settings saves', () => {
  it('checks the captured target against live state even when a newer local edit matches the remote value', async () => {
    const { ui, backend } = setup(snapshot({ ratio: 1 }));
    await ui.refresh();
    const reading = deferred<NguiState>();
    backend.getState.mockReturnValueOnce(reading.promise);
    const saving = ui.saveSettingValue('ratio', 2);
    ui.setSetting('ratio', 3);
    reading.resolve(snapshot({ ratio: 3 }));
    expect(await saving).toBe(false);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual({ ratio: 3 });
    expect(ui.current().error).toContain('Source changed at ratio');
  });
  it('retains an away-and-back target when the backend normalizes its value', async () => {
    const initial = snapshot({ ratio: 1 }); initial.runtime.startedAt = 'session';
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const write = deferred<JsonObject>();
    backend.saveSettingsPatch.mockReturnValueOnce(write.promise);
    const saving = ui.saveSettingValue('ratio', 2);
    await vi.waitFor(() => expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(1));
    ui.setSetting('ratio', 3);
    ui.setSetting('ratio', 2);
    const completedAt = new Date().toISOString();
    const saved = snapshot({ ratio: 2.5 }); saved.runtime.startedAt = 'session'; saved.runtime.updatedAt = completedAt;
    backend.getState.mockResolvedValueOnce(saved);
    write.resolve({ success: true, mode: 'connected', result: { completedAt } });
    expect(await saving).toBe(true);
    expect(ui.current().patch).toEqual({ ratio: 2 });
    expect(ui.current().snapshot!.settings.ratio).toBe(2.5);
  });
  it('saves only its field and publishes normalized state without submitting other drafts', async () => {
    const initial = snapshot({ _customIronPillSaveSeconds: 900, _customIronPillMinWorth: 0, Future: 1, _customIronPillHistory: [] });
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh();
    ui.setSetting('_customIronPillMinWorth', 100);
    const fresh = snapshot({ ...initial.settings, Future: api.parseJson('9007199254740993'), _customIronPillHistory: [{ Worth: 12 }] });
    const saved = snapshot({ ...fresh.settings, _customIronPillSaveSeconds: 1200 });
    backend.getState.mockResolvedValueOnce(fresh).mockResolvedValueOnce(saved);
    expect(await ui.saveSettingValue('_customIronPillSaveSeconds', 1200)).toBe(true);
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { _customIronPillSaveSeconds: 1200 });
    expect(ui.current().patch).toEqual({ _customIronPillMinWorth: 100 });
    expect(ui.current().snapshot!.settings).toEqual(saved.settings);
    expect(confirm).not.toHaveBeenCalled();
  });
  it('waits for validated read-back before clearing its target or returning true', async () => {
    const initial = snapshot({ _customIronPillSaveSeconds: 900 });
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const reading = deferred<NguiState>();
    backend.getState.mockResolvedValueOnce(initial).mockReturnValueOnce(reading.promise);
    const saving = ui.saveSettingValue('_customIronPillSaveSeconds', 1200);
    await vi.waitFor(() => expect(backend.getState).toHaveBeenCalledTimes(3));
    expect(ui.current().busy).toBe(true);
    expect(ui.current().patch).toEqual({ _customIronPillSaveSeconds: 1200 });
    reading.resolve(snapshot({ _customIronPillSaveSeconds: 1200 }));
    expect(await saving).toBe(true);
    expect(ui.current().patch).toEqual({});
  });
  it('preserves away-and-back target edits and other edits made while saving', async () => {
    const initial = snapshot({ enabled: true, ratio: 1 });
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const write = deferred<JsonObject>();
    backend.saveSettingsPatch.mockReturnValueOnce(write.promise);
    const saving = ui.saveSettingValue('enabled', false);
    await vi.waitFor(() => expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(1));
    ui.setSetting('enabled', true);
    ui.setSetting('enabled', false);
    ui.setSetting('ratio', 2);
    backend.getState.mockResolvedValue(snapshot({ enabled: false, ratio: 1 }));
    write.resolve({ success: true });
    expect(await saving).toBe(true);
    expect(ui.current().patch).toEqual({ enabled: false, ratio: 2 });
  });
  it.each(['target', 'other', 'same-as-base'])('rejects remote conflicts in %s pending fields even after polling', async conflict => {
    const initial = snapshot({ ratio: 1, other: 1 });
    const { ui, backend } = setup(initial);
    await ui.refresh();
    ui.setSetting(conflict === 'target' ? 'ratio' : 'other', conflict === 'same-as-base' ? 1 : 2);
    const remote = snapshot({ ratio: conflict === 'target' ? 3 : 1, other: conflict === 'target' ? 1 : 3 });
    backend.getState.mockResolvedValue(remote);
    await ui.refresh(true);
    expect(await ui.saveSettingValue('ratio', 2)).toBe(false);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().patch).toEqual(conflict === 'target' ? { ratio: 2 } : { ratio: 2, other: conflict === 'same-as-base' ? 1 : 2 });
    expect(ui.current().error).toContain('Source changed');
  });
  it('does not advance the baseline when a different pending field conflicts after saving', async () => {
    const initial = snapshot({ ratio: 1, other: 1 });
    const { ui, backend } = setup(initial);
    await ui.refresh();
    ui.setSetting('other', 2);
    backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(snapshot({ ratio: 2, other: 3 }));
    expect(await ui.saveSettingValue('ratio', 2)).toBe(false);
    expect(ui.current().patch).toEqual({ other: 2, ratio: 2 });
    expect(ui.current().snapshot!.settings).toEqual(initial.settings);
    expect(ui.current().error).toContain('other');
  });
  it.each(['reject', 'unsuccessful', 'read-failure', 'invalid-read', 'missing-read', 'session-change'])('returns false and retains edits for %s', async failure => {
    const initial = snapshot({ _customIronPillSaveSeconds: 900 });
    const { ui, backend } = setup(initial);
    await ui.refresh();
    if (failure === 'reject') backend.saveSettingsPatch.mockRejectedValueOnce(new Error('Rejected'));
    if (failure === 'unsuccessful') backend.saveSettingsPatch.mockResolvedValueOnce({ success: false });
    if (failure === 'read-failure') backend.getState.mockResolvedValueOnce(initial).mockRejectedValueOnce(new Error('Read failed'));
    if (failure === 'invalid-read') backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(snapshot({ _customIronPillSaveSeconds: -1 }));
    if (failure === 'missing-read') backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(snapshot());
    if (failure === 'session-change') {
      const next = snapshot({ _customIronPillSaveSeconds: 1200 }); next.runtime.startedAt = 'new-session';
      backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(next);
    }
    expect(await ui.saveSettingValue('_customIronPillSaveSeconds', 1200)).toBe(false);
    expect(ui.current().patch).toEqual({ _customIronPillSaveSeconds: 1200 });
    expect(ui.current().snapshot!.settings._customIronPillSaveSeconds).toBe(900);
    expect(ui.current().error.length).toBeGreaterThan(0);
    expect(ui.current().busy).toBe(false);
  });
  it.each<[string, JsonValue]>([['_customIronPillHistory', []], ['_customIronPillSaveSeconds', -1], ['_customIronPillMinWorth', 100000001], ['_customAutoBoostCubePowerRatio', 1.5], ['enabled', 'false'], ['_manualAdventureZone', 44]])('validates %s before writing', async (key, value) => {
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh();
    expect(await ui.saveSettingValue(key, value)).toBe(false);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().patch[key]).toEqual(value);
    expect(ui.current().error).toContain(key);
  });
  it('allows an explicit inline auto-rebirth setting without a popup but retains bulk confirmation', async () => {
    const initial = snapshot({ _autoRebirth: false });
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh();
    backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(snapshot({ _autoRebirth: true }));
    expect(await ui.saveSettingValue('_autoRebirth', true)).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
    const bulk = setup(initial);
    await bulk.ui.refresh();
    bulk.ui.setSetting('_autoRebirth', true);
    bulk.confirm.mockResolvedValueOnce(false);
    await bulk.ui.saveSettings();
    expect(bulk.confirm).toHaveBeenCalledWith(expect.stringContaining('Enable auto-rebirth?'));
    expect(bulk.backend.saveSettingsPatch).not.toHaveBeenCalled();
  });
});

describe('direct plan field saves', () => {
  it('saves the selected plan with a fresh revision while preserving drafts, history, and exact future numbers', async () => {
    const initial = snapshot({ _customPlanFile: 'active' });
    initial.runtime.settings = { customPlanFile: 'active', useCustomRebirthPlan: true };
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh(); await ui.loadPlan('selected');
    ui.setPlan({ ...ui.current().plan!, TimeTriggerSeconds: 600 });
    ui.advancedDirty(true);
    const fresh = { ...plan(), Future: api.parseJson('9007199254740993'), RebirthHistory: [{ TimestampLocal: 'new' }], __externalUiRevision: 'b'.repeat(64) };
    const saved = { ...fresh, StageDurationSeconds: 3600, __externalUiRevision: 'c'.repeat(64) };
    backend.readPlan.mockResolvedValueOnce(fresh).mockResolvedValueOnce(saved);
    expect(await ui.savePlanField('StageDurationSeconds', 3600)).toBe(true);
    expect(backend.savePlan).toHaveBeenCalledWith('A:/runtime', 'selected', { ...fresh, StageDurationSeconds: 3600 });
    expect(backend.readPlan.mock.calls.every(call => call[1] === 'selected')).toBe(true);
    expect(ui.current().plan).toEqual({ ...saved, TimeTriggerSeconds: 600 });
    expect(ui.current().planBase).toEqual(saved);
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().advancedDirty).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
    expect(api.stringifyJson(ui.current().plan!.Future)).toBe('9007199254740993');
  });
  it('returns true only after the normalized plan read completes', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    const reading = deferred<JsonObject>();
    backend.readPlan.mockResolvedValueOnce(plan()).mockReturnValueOnce(reading.promise);
    const saving = ui.savePlanField('StageDurationSeconds', 3600);
    await vi.waitFor(() => expect(backend.readPlan).toHaveBeenCalledTimes(3));
    expect(ui.current().planBase!.StageDurationSeconds).toBe(1800);
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().busy).toBe(true);
    reading.resolve({ ...plan(), StageDurationSeconds: 3600, __externalUiRevision: 'b'.repeat(64) });
    expect(await saving).toBe(true);
    expect(ui.current().planDirty).toBe(false);
  });
  it('retains target and other field edits made while saving', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    const write = deferred<JsonObject>();
    backend.savePlan.mockReturnValueOnce(write.promise);
    backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 3600, Future: 2, __externalUiRevision: 'b'.repeat(64) });
    const saving = ui.savePlanField('StageDurationSeconds', 3600);
    await vi.waitFor(() => expect(backend.savePlan).toHaveBeenCalledTimes(1));
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 7200, TimeTriggerSeconds: 600 });
    write.resolve({ success: true });
    expect(await saving).toBe(true);
    expect(ui.current().plan!.StageDurationSeconds).toBe(7200);
    expect(ui.current().plan!.TimeTriggerSeconds).toBe(600);
    expect(ui.current().plan!.Future).toBe(2);
    expect(ui.current().planBase!.StageDurationSeconds).toBe(3600);
  });
  it('retains an away-and-back target edit across normalization', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    const write = deferred<JsonObject>();
    backend.savePlan.mockReturnValueOnce(write.promise);
    backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 3600, __externalUiRevision: 'b'.repeat(64) });
    const saving = ui.savePlanField('StageDurationSeconds', 3600);
    await vi.waitFor(() => expect(backend.savePlan).toHaveBeenCalledTimes(1));
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 7200 });
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    write.resolve({ success: true });
    expect(await saving).toBe(true);
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().planDirty).toBe(true);
  });
  it.each(['target', 'other', 'unchanged-target'])('rejects remote %s conflicts before writing', async conflict => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    if (conflict === 'other') ui.setPlan({ ...ui.current().plan!, Future: 2 });
    backend.readPlan.mockResolvedValueOnce({ ...plan(), StageDurationSeconds: conflict === 'other' ? 1800 : 7200, Future: conflict === 'other' ? 3 : 1 });
    expect(await ui.savePlanField('StageDurationSeconds', conflict === 'unchanged-target' ? 1800 : 3600)).toBe(false);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().plan!.StageDurationSeconds).toBe(conflict === 'unchanged-target' ? 1800 : 3600);
    expect(ui.current().planBase!.StageDurationSeconds).toBe(1800);
    expect(ui.current().error).toContain('Source changed');
  });
  it('detects conflicting pending fields after save without silently advancing their baseline', async () => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, Future: 2 });
    backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 3600, Future: 3 });
    expect(await ui.savePlanField('StageDurationSeconds', 3600)).toBe(false);
    expect(ui.current().plan!.Future).toBe(2);
    expect(ui.current().planBase!.Future).toBe(1);
    expect(ui.current().error).toContain('Future');
  });
  it.each(['cas-conflict', 'unsuccessful', 'read-failure', 'invalid-read', 'missing-read'])('retains the editor and reports false for %s', async failure => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    const base = api.cloneObject(ui.current().planBase!);
    if (failure === 'cas-conflict') backend.savePlan.mockRejectedValueOnce(new Error('Plan revision conflict'));
    if (failure === 'unsuccessful') backend.savePlan.mockResolvedValueOnce({ success: false });
    if (failure === 'read-failure') backend.readPlan.mockResolvedValueOnce(plan()).mockRejectedValueOnce(new Error('Read failed'));
    if (failure === 'invalid-read') backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce({ ...plan(), __externalUiRevision: 'bad' });
    if (failure === 'missing-read') {
      const saved = plan(); delete saved.StageDurationSeconds;
      backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce(saved);
    }
    expect(await ui.savePlanField('StageDurationSeconds', 3600)).toBe(false);
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().planBase).toEqual(base);
    expect(ui.current().planDirty).toBe(true);
    expect(ui.current().error.length).toBeGreaterThan(0);
    expect(ui.current().busy).toBe(false);
  });
  it.each<[string, JsonValue]>([['Name', 'plan'], ['Version', 14], ['RebirthHistory', []], ['__externalUiRevision', 'a'.repeat(64)], ['StageDurationSeconds', 0], ['TimeTriggerEnabled', 'true'], ['TriggerMode', 2], ['NumberClimbMultiplierTarget', 0], ['Stages', []]])('rejects metadata or invalid %s edits before writing', async (key, value) => {
    const { ui, backend } = setup();
    await ui.loadPlan('plan');
    expect(await ui.savePlanField(key, value)).toBe(false);
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().error.length).toBeGreaterThan(0);
  });
  it('only enables the explicitly edited inline rebirth flag without saving other fields', async () => {
    const { ui, backend, confirm } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, TimeTriggerSeconds: 600 });
    backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce({ ...plan(), AutoRebirthEnabled: true, __externalUiRevision: 'b'.repeat(64) });
    expect(await ui.savePlanField('AutoRebirthEnabled', true)).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.savePlan).toHaveBeenCalledWith('A:/runtime', 'plan', { ...plan(), AutoRebirthEnabled: true });
    expect(ui.current().plan!.TimeTriggerSeconds).toBe(600);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
  });
  it('retains the advanced/bulk rebirth confirmation', async () => {
    const { ui, backend, confirm } = setup();
    await ui.loadPlan('plan');
    ui.setPlan({ ...ui.current().plan!, AutoRebirthEnabled: true });
    confirm.mockResolvedValueOnce(false);
    await ui.saveActivePlan();
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Enable rebirth for this plan?'));
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().planDirty).toBe(true);
  });
  it('returns false for both direct APIs while another action is busy without submitting or replacing edits', async () => {
    const { ui, backend } = setup(snapshot({ enabled: true }));
    await ui.refresh(); await ui.loadPlan('plan');
    const write = deferred<JsonObject>();
    backend.savePlan.mockReturnValueOnce(write.promise);
    backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 3600 });
    const saving = ui.savePlanField('StageDurationSeconds', 3600);
    await vi.waitFor(() => expect(backend.savePlan).toHaveBeenCalledTimes(1));
    ui.setSetting('enabled', false);
    expect(await ui.saveSettingValue('enabled', true)).toBe(false);
    expect(await ui.savePlanField('StageDurationSeconds', 7200)).toBe(false);
    expect(ui.current().patch).toEqual({ enabled: false });
    expect(ui.current().plan!.StageDurationSeconds).toBe(3600);
    expect(ui.current().error).toContain('busy');
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    write.resolve({ success: true });
    expect(await saving).toBe(true);
  });
});

describe('acknowledged plan action state', () => {
  it('publishes action flags immediately even when the accepted heartbeat clock is ahead', async () => {
    const initial = snapshot({ _useCustomRebirthPlan: true, _autoRebirth: true });
    initial.runtime.updatedAt = new Date(Date.now() + 1500).toISOString();
    initial.runtime.settings = { useCustomRebirthPlan: true, autoRebirth: true };
    const { ui } = setup(initial);
    await ui.refresh();
    await ui.planAction('stop');
    expect(ui.current().snapshot!.settings).toEqual({ _useCustomRebirthPlan: false, _autoRebirth: false });
    expect(ui.current().snapshot!.runtime.settings).toEqual({ useCustomRebirthPlan: false, autoRebirth: false });
  });
  it.each([true, false])('publishes run flags with auto=%s before refresh finishes and retains them through a lagging heartbeat', async auto => {
    const initial = snapshot({ _customPlanFile: 'old', _useCustomRebirthPlan: false, _autoRebirth: !auto });
    initial.runtime.settings = { customPlanFile: 'old', useCustomRebirthPlan: false, autoRebirth: !auto };
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh(); await ui.loadPlan('selected');
    backend.readPlan.mockResolvedValue({ ...plan(), AutoRebirthEnabled: true });
    const reading = deferred<NguiState>();
    backend.getState.mockReturnValueOnce(reading.promise);
    const action = ui.planAction('run', auto);
    await vi.waitFor(() => expect(backend.getState).toHaveBeenCalledTimes(2));
    const expected = { _customPlanFile: 'selected', _useCustomRebirthPlan: true, _autoRebirth: auto };
    expect(ui.current().snapshot!.settings).toEqual(expected);
    expect(ui.current().snapshot!.runtime.settings).toEqual({ customPlanFile: 'selected', useCustomRebirthPlan: true, autoRebirth: auto });
    reading.resolve(initial);
    await action;
    expect(ui.current().snapshot!.settings).toEqual(expected);
    expect(ui.current().snapshot!.runtime.settings).toEqual({ customPlanFile: 'selected', useCustomRebirthPlan: true, autoRebirth: auto });
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.enqueueCommand).not.toHaveBeenCalled();
    const fresh = snapshot({ _customPlanFile: 'remote', _useCustomRebirthPlan: false, _autoRebirth: false });
    fresh.runtime.updatedAt = new Date(Date.now() + 1000).toISOString();
    fresh.runtime.settings = { customPlanFile: 'remote', useCustomRebirthPlan: false, autoRebirth: false };
    backend.getState.mockResolvedValueOnce(fresh);
    await ui.refresh(true);
    expect(ui.current().snapshot!.settings).toEqual(fresh.settings);
    expect(ui.current().snapshot!.runtime.settings).toEqual(fresh.runtime.settings);
  });
  it('publishes acknowledged stop flags without clearing unrelated pending settings', async () => {
    const initial = snapshot({ _customPlanFile: 'active', _useCustomRebirthPlan: true, _autoRebirth: true });
    initial.runtime.settings = { customPlanFile: 'active', useCustomRebirthPlan: true, autoRebirth: true };
    const { ui, backend } = setup(initial);
    await ui.refresh();
    ui.setSetting('other', true);
    await ui.planAction('stop');
    expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { _useCustomRebirthPlan: false, _autoRebirth: false });
    expect(ui.current().snapshot!.settings).toEqual({ _customPlanFile: 'active', _useCustomRebirthPlan: false, _autoRebirth: false });
    expect(ui.current().snapshot!.runtime.settings).toEqual({ customPlanFile: 'active', useCustomRebirthPlan: false, autoRebirth: false });
    expect(ui.current().patch).toEqual({ other: true });
  });
  it('does not publish unacknowledged flags on a failed action', async () => {
    const initial = snapshot({ _customPlanFile: 'old', _useCustomRebirthPlan: false });
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('plan');
    backend.saveSettingsPatch.mockResolvedValueOnce({ success: false });
    await ui.planAction('run', false);
    expect(ui.current().snapshot!.settings).toEqual(initial.settings);
    expect(ui.current().error).toContain('did not report success');
  });
  it('retains edits made during the normalized run read rather than starting or replacing them', async () => {
    const { ui, backend } = setup();
    await ui.refresh(); await ui.loadPlan('selected');
    const reading = deferred<JsonObject>();
    backend.readPlan.mockReturnValueOnce(reading.promise);
    const action = ui.planAction('run', false);
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 7200 });
    reading.resolve({ ...plan(), Future: 2 });
    await action;
    expect(ui.current().plan!.StageDurationSeconds).toBe(7200);
    expect(ui.current().plan!.Future).toBe(2);
    expect(ui.current().planDirty).toBe(true);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(ui.current().error).toContain('edited while starting');
  });
});

describe.each(['settings', 'plan'] as const)('%s inline edit sessions', kind => {
  const key = kind === 'settings' ? '_customIronPillSaveSeconds' : 'StageDurationSeconds';
  const original = kind === 'settings' ? 900 : 1800;
  async function editor() {
    const context = setup(snapshot({ [key]: original }));
    await context.ui.refresh();
    if (kind === 'plan') await context.ui.loadPlan('selected');
    const { ui, backend } = context;
    return { ...context,
      begin: () => kind === 'settings' ? ui.beginSettingEdit(key) : ui.beginPlanEdit(key),
      set: (value: JsonValue) => kind === 'settings' ? ui.setSetting(key, value) : ui.setPlan({ ...ui.current().plan!, [key]: value }),
      draft: () => kind === 'settings' ? ui.current().patch[key] : ui.current().plan![key],
      write: kind === 'settings' ? backend.saveSettingsPatch : backend.savePlan,
      bulkSave: () => kind === 'settings' ? ui.saveSettings() : ui.saveActivePlan()
    };
  }
  it.each(['invalid', 'rejected', 'unsuccessful', 'read-failure'])('removes a cancelled %s attempt so a later bulk save cannot submit it', async failure => {
    const { ui, backend, begin, draft, write, bulkSave } = await editor();
    const edit = begin();
    if (failure === 'rejected') write.mockRejectedValueOnce(new Error('Rejected'));
    if (failure === 'unsuccessful') write.mockResolvedValueOnce({ success: false });
    if (failure === 'read-failure') {
      if (kind === 'settings') backend.getState.mockResolvedValueOnce(snapshot({ [key]: original })).mockRejectedValueOnce(new Error('Read failed'));
      else backend.readPlan.mockResolvedValueOnce(plan()).mockRejectedValueOnce(new Error('Read failed'));
    }
    const attempted = failure === 'invalid' ? -1 : 3600;
    expect(await edit.commit(attempted)).toBe(false);
    expect(draft()).toBe(attempted);
    const reported = ui.current().error;
    edit.cancel();
    expect(draft()).toBe(kind === 'settings' ? undefined : original);
    expect(ui.current().error).toBe(reported);
    if (kind === 'settings') ui.setSetting('other', true);
    else ui.setPlan({ ...ui.current().plan!, TimeTriggerSeconds: 600 });
    write.mockClear();
    await bulkSave();
    expect(write).toHaveBeenCalledTimes(1);
    if (kind === 'settings') expect(backend.saveSettingsPatch).toHaveBeenCalledWith('A:/runtime', { other: true });
    else expect(backend.savePlan).toHaveBeenCalledWith('A:/runtime', 'selected', { ...plan(), TimeTriggerSeconds: 600 });
  });
  it('restores the previous pending target and its original conflict baseline', async () => {
    const { ui, backend, begin, set, draft, write } = await editor();
    set(2400);
    const edit = begin();
    if (kind === 'settings') backend.getState.mockResolvedValue(snapshot({ [key]: 2700 }));
    else backend.readPlan.mockResolvedValue({ ...plan(), [key]: 2700 });
    expect(await edit.commit(3600)).toBe(false);
    edit.cancel();
    expect(draft()).toBe(2400);
    expect(write).not.toHaveBeenCalled();
    const retry = kind === 'settings' ? ui.saveSettingValue(key, 2400) : ui.savePlanField(key, 2400);
    expect(await retry).toBe(false);
    expect(ui.current().error).toContain('Source changed');
    expect(write).not.toHaveBeenCalled();
  });
  it('preserves unrelated drafts and exact numeric tokens when rolling back its target', async () => {
    const { ui, begin, write, draft } = await editor();
    const edit = begin();
    const exact = api.parseJson('9007199254740993');
    write.mockRejectedValueOnce(new Error('Rejected'));
    expect(await edit.commit(3600)).toBe(false);
    if (kind === 'settings') { ui.setSetting('Future', exact); ui.setSetting('other', true); }
    else { ui.setPlan({ ...ui.current().plan!, Future: exact, TimeTriggerSeconds: 600 }); ui.advancedDirty(true); }
    edit.cancel();
    const data = kind === 'settings' ? ui.current().patch : ui.current().plan!;
    expect(api.stringifyJson(data.Future)).toBe('9007199254740993');
    expect(data[kind === 'settings' ? 'other' : 'TimeTriggerSeconds']).toBe(kind === 'settings' ? true : 600);
    expect(draft()).toBe(kind === 'settings' ? undefined : original);
    if (kind === 'plan') expect(ui.current().advancedDirty).toBe(true);
  });
  it.each(['newer', 'away-and-back'])('cannot roll back a %s edit made while its commit was pending', async change => {
    const { begin, set, draft, write } = await editor();
    const saving = deferred<JsonObject>();
    write.mockReturnValueOnce(saving.promise);
    const edit = begin();
    const result = edit.commit(3600);
    await vi.waitFor(() => expect(write).toHaveBeenCalledTimes(1));
    set(7200);
    if (change === 'away-and-back') set(3600);
    saving.reject(new Error('Rejected'));
    expect(await result).toBe(false);
    edit.cancel();
    expect(draft()).toBe(change === 'newer' ? 7200 : 3600);
  });
  it('captures ownership synchronously even when validation rejects without an await', async () => {
    const { begin, set, draft } = await editor();
    const edit = begin();
    const result = edit.commit(-1);
    set(7200);
    expect(await result).toBe(false);
    edit.cancel();
    expect(draft()).toBe(7200);
  });
  it('can retry a failed commit but cancel still restores the draft from begin', async () => {
    const { begin, set, draft, write } = await editor();
    set(2400);
    const edit = begin();
    write.mockRejectedValue(new Error('Rejected'));
    expect(await edit.commit(3600)).toBe(false);
    expect(await edit.commit(4800)).toBe(false);
    edit.cancel();
    expect(draft()).toBe(2400);
    expect(await edit.commit(7200)).toBe(false);
    expect(draft()).toBe(2400);
  });
  it('does not use cancel as a rollback after a successful commit', async () => {
    const { ui, backend, begin, draft, write } = await editor();
    const edit = begin();
    if (kind === 'settings') backend.getState.mockResolvedValueOnce(snapshot({ [key]: original })).mockResolvedValueOnce(snapshot({ [key]: 3600 }));
    else backend.readPlan.mockResolvedValueOnce(plan()).mockResolvedValueOnce({ ...plan(), [key]: 3600, __externalUiRevision: 'b'.repeat(64) });
    expect(await edit.commit(3600)).toBe(true);
    const acknowledged = api.cloneObject(kind === 'settings' ? ui.current().snapshot!.settings : ui.current().planBase!);
    edit.cancel();
    expect(draft()).toBe(kind === 'settings' ? undefined : 3600);
    expect(kind === 'settings' ? ui.current().snapshot!.settings : ui.current().planBase).toEqual(acknowledged);
    expect(write).toHaveBeenCalledTimes(1);
  });
  it('does not claim another action\'s revision on a busy commit', async () => {
    const { begin, set, draft, write, bulkSave } = await editor();
    set(2400);
    const saving = deferred<JsonObject>();
    write.mockReturnValueOnce(saving.promise);
    const bulk = bulkSave();
    await vi.waitFor(() => expect(write).toHaveBeenCalledTimes(1));
    const edit = begin();
    expect(await edit.commit(3600)).toBe(false);
    edit.cancel();
    expect(draft()).toBe(2400);
    saving.resolve({ success: true });
    await bulk;
  });
  it('does not roll back an attempt belonging to a previous injector session', async () => {
    const { ui, backend, begin, set, draft, write } = await editor();
    set(2400);
    const edit = begin();
    write.mockRejectedValueOnce(new Error('Rejected'));
    expect(await edit.commit(3600)).toBe(false);
    const next = snapshot({ [key]: original }); next.runtime.startedAt = 'new-session';
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    edit.cancel();
    expect(draft()).toBe(3600);
    expect(await edit.commit(4800)).toBe(false);
    expect(draft()).toBe(3600);
  });
  it('does not edit a different runtime folder when an old session is cancelled', async () => {
    const { ui, backend, begin, set, write } = await editor();
    set(2400);
    const edit = begin();
    write.mockRejectedValueOnce(new Error('Rejected'));
    expect(await edit.commit(3600)).toBe(false);
    const next = snapshot({ [key]: 4800 }, 'B:/runtime');
    backend.getState.mockResolvedValue(next);
    await ui.changeRuntimeDir('B:/runtime');
    if (kind === 'plan') { backend.readPlan.mockResolvedValueOnce({ ...plan(), [key]: 4800 }); await ui.loadPlan('selected'); }
    const before = kind === 'settings' ? api.cloneObject(ui.current().patch) : api.cloneObject(ui.current().plan!);
    edit.cancel();
    expect(kind === 'settings' ? ui.current().patch : ui.current().plan).toEqual(before);
    expect(ui.current().runtimeDir).toBe('B:/runtime');
    expect(await edit.commit(7200)).toBe(false);
  });
  it.each([true, false])('defers in-flight cancel until the write outcome is known (success=%s)', async success => {
    const { ui, backend, begin, draft, write } = await editor();
    const saving = deferred<JsonObject>();
    write.mockReturnValueOnce(saving.promise);
    const edit = begin();
    const result = edit.commit(3600);
    await vi.waitFor(() => expect(write).toHaveBeenCalledTimes(1));
    edit.cancel();
    expect(draft()).toBe(3600);
    if (kind === 'settings') backend.getState.mockResolvedValueOnce(snapshot({ [key]: 3600 }));
    else backend.readPlan.mockResolvedValueOnce({ ...plan(), [key]: 3600 });
    saving.resolve({ success });
    expect(await result).toBe(success);
    expect(draft()).toBe(kind === 'settings' ? undefined : success ? 3600 : original);
    expect(write).toHaveBeenCalledTimes(1);
    if (success) expect((kind === 'settings' ? ui.current().snapshot!.settings : ui.current().planBase!)[key]).toBe(3600);
  });
});

describe('plan edit identity', () => {
  it.each(['other', 'selected'])('does not roll back after loading %s, including reloading the same named plan', async name => {
    const { ui, backend } = setup();
    await ui.loadPlan('selected');
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 2400 });
    const edit = ui.beginPlanEdit('StageDurationSeconds');
    backend.savePlan.mockRejectedValueOnce(new Error('Rejected'));
    expect(await edit.commit(3600)).toBe(false);
    backend.readPlan.mockResolvedValueOnce({ ...plan(), StageDurationSeconds: 4800, __externalUiRevision: 'b'.repeat(64) });
    await ui.loadPlan(name);
    edit.cancel();
    expect(ui.current().planName).toBe(name);
    expect(ui.current().plan!.StageDurationSeconds).toBe(4800);
    expect(ui.current().planBase!.StageDurationSeconds).toBe(4800);
    expect(await edit.commit(7200)).toBe(false);
  });
});

describe('settings acknowledgement barrier', () => {
  it('allows explicit cancellation of an unacknowledged write without a later bulk save resubmitting it', async () => {
    vi.useFakeTimers();
    try {
      const initial = snapshot({ _customIronPillSaveSeconds: 900 }); initial.runtime.startedAt = 'session';
      const { ui, backend } = setup(initial);
      await ui.refresh();
      const edit = ui.beginSettingEdit('_customIronPillSaveSeconds');
      backend.saveSettingsPatch.mockResolvedValueOnce({ success: true, mode: 'connected', result: { completedAt: new Date(Date.now() + 1).toISOString() } });
      const saving = edit.commit(1200);
      await vi.runAllTimersAsync();
      expect(await saving).toBe(false);
      expect(ui.current().patch).toEqual({ _customIronPillSaveSeconds: 1200 });
      edit.cancel();
      ui.setSetting('other', true);
      await ui.saveSettings();
      expect(backend.saveSettingsPatch.mock.calls.map(call => call[1])).toEqual([{ _customIronPillSaveSeconds: 1200 }, { other: true }]);
    } finally { vi.useRealTimers(); }
  });
  it.each(['missing', 'invalid', 'numeric', 'no-session', 'stale', 'offline'])('does not accept a different read-back value with %s completion proof', async proof => {
    vi.useFakeTimers();
    try {
      const completedAt = new Date().toISOString();
      const initial = snapshot({ _customIronPillSaveSeconds: 900 });
      initial.runtime.startedAt = proof === 'no-session' ? '' : 'session';
      initial.runtime.updatedAt = new Date(Date.now() - 100).toISOString();
      const { ui, backend } = setup(initial);
      await ui.refresh();
      const saved = snapshot({ _customIronPillSaveSeconds: 900 });
      saved.runtime.startedAt = initial.runtime.startedAt;
      saved.runtime.updatedAt = proof === 'stale' ? initial.runtime.updatedAt : completedAt;
      backend.getState.mockResolvedValue(saved);
      const result: JsonObject = { success: true, mode: proof === 'offline' ? 'offline' : 'connected', result: { completedAt: proof === 'invalid' ? 'bad-date' : proof === 'numeric' ? 1 : completedAt } };
      if (proof === 'missing') delete result.result;
      backend.saveSettingsPatch.mockResolvedValueOnce(result);
      const saving = ui.saveSettingValue('_customIronPillSaveSeconds', 1200);
      await vi.runAllTimersAsync();
      expect(await saving).toBe(false);
      expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(1);
      expect(backend.getState).toHaveBeenCalledTimes(6);
      expect(ui.current().patch).toEqual({ _customIronPillSaveSeconds: 1200 });
      expect(ui.current().snapshot!.settings._customIronPillSaveSeconds).toBe(900);
      expect(ui.current().error).toContain('did not acknowledge');
      expect(ui.current().busy).toBe(false);
    } finally { vi.useRealTimers(); }
  });
  it('retains the target through stale reads and accepts normalization only after the same-session completion barrier', async () => {
    vi.useFakeTimers();
    try {
      const completedAt = new Date().toISOString();
      const initial = snapshot({ _customIronPillSaveSeconds: 900 });
      initial.runtime.startedAt = 'session'; initial.runtime.updatedAt = new Date(Date.now() - 100).toISOString();
      const { ui, backend } = setup(initial);
      await ui.refresh();
      const saved = snapshot({ _customIronPillSaveSeconds: 1100 });
      saved.runtime.startedAt = 'session'; saved.runtime.updatedAt = new Date(Date.now() + 1).toISOString();
      backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(initial).mockResolvedValueOnce(initial).mockResolvedValueOnce(saved);
      backend.saveSettingsPatch.mockResolvedValueOnce({ success: true, mode: 'connected', result: { completedAt } });
      const saving = ui.saveSettingValue('_customIronPillSaveSeconds', 1200);
      await vi.advanceTimersByTimeAsync(400);
      expect(ui.current().patch).toEqual({ _customIronPillSaveSeconds: 1200 });
      expect(ui.current().busy).toBe(true);
      expect(ui.current().snapshot!.settings._customIronPillSaveSeconds).toBe(900);
      await vi.runAllTimersAsync();
      expect(await saving).toBe(true);
      expect(ui.current().patch).toEqual({});
      expect(ui.current().snapshot!.settings._customIronPillSaveSeconds).toBe(1100);
      expect(backend.saveSettingsPatch).toHaveBeenCalledTimes(1);
    } finally { vi.useRealTimers(); }
  });
  it.each(['missing', 'offline', 'no-session'])('requires exact equality without normalization proof (%s)', async proof => {
    const initial = snapshot({ ratio: 1 });
    if (proof !== 'no-session') initial.runtime.startedAt = 'session';
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const saved = snapshot({ ratio: 2 }); saved.runtime.startedAt = initial.runtime.startedAt ?? '';
    if (!initial.runtime.startedAt) delete saved.runtime.startedAt;
    backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(saved);
    backend.saveSettingsPatch.mockResolvedValueOnce({ success: true, mode: proof === 'offline' ? 'offline' : 'connected' });
    expect(await ui.saveSettingValue('ratio', 2)).toBe(true);
    expect(ui.current().patch).toEqual({});
    expect(ui.current().snapshot!.settings.ratio).toBe(2);
  });
  it('does not accept a matching value before an available connected barrier is crossed', async () => {
    vi.useFakeTimers();
    try {
      const completedAt = new Date().toISOString();
      const initial = snapshot({ ratio: 1 }); initial.runtime.startedAt = 'session';
      const { ui, backend } = setup(initial);
      await ui.refresh();
      const stale = snapshot({ ratio: 2 }); stale.runtime.startedAt = 'session'; stale.runtime.updatedAt = new Date(Date.now() - 100).toISOString();
      backend.getState.mockResolvedValueOnce(initial).mockResolvedValue(stale);
      backend.saveSettingsPatch.mockResolvedValueOnce({ success: true, mode: 'connected', result: { completedAt } });
      const saving = ui.saveSettingValue('ratio', 2);
      await vi.runAllTimersAsync();
      expect(await saving).toBe(false);
      expect(ui.current().patch).toEqual({ ratio: 2 });
    } finally { vi.useRealTimers(); }
  });
  it('rejects normalization from a different injector session', async () => {
    const initial = snapshot({ ratio: 1 }); initial.runtime.startedAt = 'session';
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const completedAt = new Date().toISOString();
    const next = snapshot({ ratio: 2.5 }); next.runtime.startedAt = 'new'; next.runtime.updatedAt = completedAt;
    backend.getState.mockResolvedValueOnce(initial).mockResolvedValueOnce(next);
    backend.saveSettingsPatch.mockResolvedValueOnce({ success: true, mode: 'connected', result: { completedAt } });
    expect(await ui.saveSettingValue('ratio', 2)).toBe(false);
    expect(ui.current().patch).toEqual({ ratio: 2 });
    expect(ui.current().error).toContain('session change');
  });
});

describe('Quick supersedes acknowledged plan flags', () => {
  it.each(['apply', 'toggle'])('acknowledges Quick %s pausing after an auto-enabled run despite a stale heartbeat', async mode => {
    const initial = snapshot({ _customPlanFile: 'old', _useCustomRebirthPlan: false, _autoRebirth: false });
    initial.runtime.startedAt = 'session';
    initial.runtime.settings = { customPlanFile: 'old', useCustomRebirthPlan: false, autoRebirth: false };
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('selected');
    backend.readPlan.mockResolvedValue({ ...plan(), AutoRebirthEnabled: true });
    const runCompletedAt = new Date(Date.now() + 10).toISOString();
    backend.saveSettingsPatch.mockResolvedValueOnce({ success: true, mode: 'connected', result: { completedAt: runCompletedAt } });
    await ui.planAction('run', true);
    expect(ui.current().snapshot!.settings._useCustomRebirthPlan).toBe(true);
    const completedAt = new Date(Date.now() + 20).toISOString();
    const completed = deferred<JsonObject | null>();
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const reading = deferred<NguiState>();
    backend.getState.mockReturnValueOnce(reading.promise);
    const quick = ui.runCommand('focusGoal', { goal: 'TimeMachine', mode });
    await vi.waitFor(() => expect(backend.readCommandResult).toHaveBeenCalledTimes(1));
    ui.setSetting('other', true);
    completed.resolve({ id: 'command-id', command: 'focusGoal', success: true, completedAt });
    await vi.waitFor(() => expect(backend.getState).toHaveBeenCalledTimes(3));
    expect(ui.current().snapshot!.settings).toEqual({ _customPlanFile: 'selected', _useCustomRebirthPlan: false, _autoRebirth: false });
    expect(ui.current().snapshot!.runtime.settings).toEqual({ customPlanFile: 'selected', useCustomRebirthPlan: false, autoRebirth: false });
    const stopped = { ...initial, settings: { _customPlanFile: 'selected', _useCustomRebirthPlan: false, _autoRebirth: false }, runtime: { ...initial.runtime, activeFocusGoal: 'TimeMachine' } };
    reading.resolve(stopped);
    await quick;
    expect(ui.current().snapshot!.settings).toEqual(stopped.settings);
    expect(ui.current().snapshot!.runtime.settings).toEqual({ customPlanFile: 'selected', useCustomRebirthPlan: false, autoRebirth: false });
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    expect(ui.current().patch).toEqual({ other: true });
    // A snapshot newer than the run but older than Quick must not release Quick's acknowledgement.
    const between = { ...stopped, runtime: { ...initial.runtime, updatedAt: new Date(Date.parse(runCompletedAt) + 1).toISOString() } };
    backend.getState.mockResolvedValueOnce(between);
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.settings).toEqual({ customPlanFile: 'selected', useCustomRebirthPlan: false, autoRebirth: false });
    expect(ui.current().status).toBe('Quick completed.');
  });
  it('does not infer a pause from a failed Quick result', async () => {
    const initial = snapshot({ _useCustomRebirthPlan: true, _autoRebirth: true });
    initial.runtime.settings = { useCustomRebirthPlan: true, autoRebirth: true };
    const { ui, backend } = setup(initial);
    await ui.refresh();
    backend.readCommandResult.mockResolvedValueOnce({ id: 'command-id', command: 'focusGoal', success: false, message: 'Failed', completedAt: new Date().toISOString() });
    await ui.runCommand('focusGoal', { goal: 'TimeMachine', mode: 'apply' });
    expect(ui.current().snapshot!.settings).toEqual(initial.settings);
    expect(ui.current().snapshot!.runtime.settings).toEqual(initial.runtime.settings);
    expect(ui.current().error).toBe('Failed');
  });
  it('does not publish a Quick acknowledgement into a new injector session', async () => {
    const initial = snapshot({ _useCustomRebirthPlan: true, _autoRebirth: true }); initial.runtime.startedAt = 'old';
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const completed = deferred<JsonObject | null>();
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const quick = ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    await vi.waitFor(() => expect(backend.readCommandResult).toHaveBeenCalledTimes(1));
    const next = snapshot({ _useCustomRebirthPlan: true, _autoRebirth: true }); next.runtime.startedAt = 'new';
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    completed.resolve({ id: 'command-id', command: 'focusGoal', success: true, completedAt: new Date().toISOString() });
    await quick;
    expect(ui.current().snapshot!.settings).toEqual(next.settings);
    expect(ui.current().error).toContain('session changed');
  });
});

describe('action latency acknowledgements', () => {
  it('marks Quick pending before enqueue resolves without changing the applied goal', async () => {
    const initial = snapshot(); initial.runtime.activeFocusGoal = 'Wandoos';
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh();
    const queued = deferred<string>();
    backend.enqueueCommand.mockReturnValueOnce(queued.promise);
    const action = ui.runCommand('focusGoal', { goal: 'TimeMachine', mode: 'toggle' });
    expect(ui.current().pendingQuickGoal).toBe('TimeMachine');
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Wandoos');
    expect(ui.current().status).toBe('Quick: working...');
    expect(confirm).not.toHaveBeenCalled();
    queued.resolve('command-id');
    await action;
    expect(ui.current().pendingQuickGoal).toBe(null);
    // A success without runtime proof must never infer the clicked goal as applied.
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Wandoos');
  });

  it.each(['enqueue', 'failure', 'mismatch'])('clears pending on %s failure while retaining the old applied goal', async failure => {
    const initial = snapshot(); initial.runtime.activeFocusGoal = 'Wandoos';
    const { ui, backend } = setup(initial);
    await ui.refresh();
    if (failure === 'enqueue') backend.enqueueCommand.mockRejectedValueOnce(new Error('Queue failed'));
    else backend.readCommandResult.mockResolvedValueOnce({ id: failure === 'mismatch' ? 'other' : 'command-id', command: 'focusGoal', success: false, message: 'Failed', data: { runtime: { ...initial.runtime, activeFocusGoal: 'TimeMachine' } } });
    const action = ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().pendingQuickGoal).toBe('TimeMachine');
    await action;
    expect(ui.current().pendingQuickGoal).toBe(null);
    expect(ui.current().busy).toBe(false);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Wandoos');
    expect(ui.current().error).toBe(failure === 'enqueue' ? 'Queue failed' : failure === 'mismatch' ? 'Command result does not match the request.' : 'Failed');
  });

  it('publishes result runtime immediately without waiting for a background GET or loading a plan', async () => {
    const initial = snapshot(); initial.runtime.startedAt = 'session'; initial.runtime.activeFocusGoal = 'Wandoos';
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const reading = deferred<NguiState>();
    backend.getState.mockReturnValueOnce(reading.promise);
    const runtime = { ...initial.runtime, activeFocusGoal: 'TimeMachine', settings: { useCustomRebirthPlan: false, autoRebirth: false } };
    const result = { id: 'command-id', command: 'focusGoal', success: true, completedAt: initial.runtime.updatedAt, data: { runtime } };
    backend.readCommandResult.mockResolvedValueOnce(result);
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().busy).toBe(false);
    expect(ui.current().refreshing).toBe(true);
    expect(ui.current().pendingQuickGoal).toBe(null);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    expect(ui.current().lastResult).toEqual(result);
    reading.resolve({ ...initial, plans: ['default'] });
    await vi.waitFor(() => expect(ui.current().refreshing).toBe(false));
    expect(backend.readPlan).not.toHaveBeenCalled();
  });

  it.each(['previewGear', 'previewAugments'])('retains %s metrics while consuming additive runtime', async command => {
    const initial = snapshot(); initial.runtime.startedAt = 'session';
    const { ui, backend } = setup(initial);
    await ui.refresh();
    const data = { expectedValue: 1200, items: [1, 2], runtime: { ...initial.runtime, customStatus: 'Acknowledged live state' } };
    backend.readCommandResult.mockResolvedValueOnce({ id: 'command-id', command, success: true, data });
    await ui.runCommand(command);
    expect(ui.current().lastResult!.data).toEqual(data);
    expect(ui.current().snapshot!.runtime.customStatus).toBe('Acknowledged live state');
    if (command === 'previewAugments') expect(ui.current().augmentResult!.data).toEqual(data);
  });

  it.each(['settings', 'stop', 'activate'] as const)('releases %s busy state at acknowledgement without waiting for background state', async action => {
    const initial = snapshot({ ratio: 1 }); initial.runtime.startedAt = 'session';
    const { ui, backend, confirm } = setup(initial);
    await ui.refresh(); await ui.loadPlan('selected');
    ui.setSetting('ratio', 2);
    const reading = deferred<NguiState>();
    backend.getState.mockReturnValueOnce(reading.promise);
    const runtime = { ...initial.runtime, customStatus: 'Action acknowledged', settings: { customPlanFile: 'selected', useCustomRebirthPlan: false, autoRebirth: false } };
    backend.saveSettingsPatch.mockResolvedValueOnce({ success: true, mode: 'connected', result: { completedAt: initial.runtime.updatedAt, data: { runtime } } });
    if (action === 'settings') await ui.saveSettings();
    else await ui.planAction(action);
    expect(ui.current().busy).toBe(false);
    expect(ui.current().refreshing).toBe(true);
    expect(ui.current().snapshot!.runtime.customStatus).toBe('Action acknowledged');
    expect(ui.current().planName).toBe('selected');
    expect(confirm).not.toHaveBeenCalled();
    expect(backend.readPlan).toHaveBeenCalledTimes(1);
    reading.resolve(initial);
    await vi.waitFor(() => expect(ui.current().refreshing).toBe(false));
  });

  it('keeps all editor identities and unsaved buffers when consuming action runtime', async () => {
    const initial = snapshot({ ratio: 1 }); initial.runtime.startedAt = 'session';
    const { ui, backend } = setup(initial);
    await ui.refresh(); await ui.loadPlan('selected');
    ui.setSetting('ratio', 2);
    ui.setPlan({ ...ui.current().plan!, StageDurationSeconds: 3600 });
    ui.advancedDirty(true);
    const before = ui.current();
    const data = { runtime: { ...initial.runtime, activeFocusGoal: 'TimeMachine' } };
    backend.readCommandResult.mockResolvedValueOnce({ id: 'command-id', command: 'focusGoal', success: true, data });
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    expect(ui.current().patch).toEqual({ ratio: 2 });
    expect(ui.current().plan).toBe(before.plan);
    expect(ui.current().planBase).toBe(before.planBase);
    expect(ui.current().editorVersion).toBe(before.editorVersion);
    expect(ui.current().planName).toBe('selected');
    expect(ui.current().advancedDirty).toBe(true);
    expect(ui.current().planDirty).toBe(true);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    expect(backend.readPlan).toHaveBeenCalledTimes(1);
  });
});

describe('NGU weights use whole-plan autosave and editor identity', () => {
  afterEach(() => vi.useRealTimers());
  async function editor(version: JsonValue = 16) {
    const initial = snapshot({ _customPlanFile: 'active', _useCustomRebirthPlan: true });
    initial.runtime.planVersion = version;
    const context = setup(initial);
    let disk: JsonObject = { ...plan(), Version: 16, EnergyNguWeights: [0, 0, 0, 0, 1, 0, 1, 0, 0], MagicNguWeights: [1, 0, 0, 0, 0, 0, 0] };
    context.backend.readPlan.mockImplementation(async () => api.cloneObject(disk));
    context.backend.savePlan.mockImplementation(async (_dir, _name, payload) => { disk = { ...payload, __externalUiRevision: 'b'.repeat(64) }; return { success: true }; });
    await context.ui.refresh(); await context.ui.loadPlan('selected');
    return { ...context, initial };
  }

  it('debounces array edits, saves the whole selected plan, and never selects the active plan', async () => {
    vi.useFakeTimers();
    const { ui, backend, initial } = await editor();
    const before = api.stringifyJson(ui.current().plan);
    await ui.refresh(true); await vi.advanceTimersByTimeAsync(500);
    expect(api.stringifyJson(ui.current().plan)).toBe(before);
    expect(backend.savePlan).not.toHaveBeenCalled();
    const weights = [0, 0, 0, 0, 2, 0, 1, 0, 0];
    ui.editPlan({ ...ui.current().plan!, EnergyNguWeights: weights, TimeTriggerSeconds: 600 });
    await vi.advanceTimersByTimeAsync(349);
    expect(backend.savePlan).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.savePlan).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'selected', expect.objectContaining({ Version: 16, EnergyNguWeights: weights, MagicNguWeights: [1, 0, 0, 0, 0, 0, 0], TimeTriggerSeconds: 600, StageDurationSeconds: 1800 }));
    expect(ui.current().planDirty).toBe(false);
    expect(ui.current().planName).toBe('selected');
    expect(ui.current().snapshot!.settings).toEqual(initial.settings);
    expect(backend.readPlan.mock.calls.every(call => call[1] === 'selected')).toBe(true);
    expect(backend.saveSettingsPatch).not.toHaveBeenCalled();
    ui.dispose();
  });

  it('retains conflicting array edits without writing or replaying on polls', async () => {
    vi.useFakeTimers();
    const { ui, backend } = await editor();
    const local = [0, 0, 0, 0, 3, 0, 1, 0, 0];
    ui.editPlan({ ...ui.current().plan!, EnergyNguWeights: local });
    backend.readPlan.mockResolvedValueOnce({ ...ui.current().planBase!, EnergyNguWeights: [1, 0, 0, 0, 0, 0, 0, 0, 0] });
    await vi.advanceTimersByTimeAsync(350);
    expect(ui.current().saveError).toContain('Source changed at EnergyNguWeights');
    expect(ui.current().plan!.EnergyNguWeights).toEqual(local);
    await ui.refresh(true); await vi.advanceTimersByTimeAsync(1000);
    expect(backend.savePlan).not.toHaveBeenCalled();
    ui.dispose();
  });

  it.each(['EnergyNguWeights', 'MagicNguWeights'])('rejects post-commit concurrent changes to the pending %s array', async key => {
    vi.useFakeTimers();
    const { ui, backend } = await editor();
    const committedWeights = (ui.current().plan![key] as number[]).map(() => 2);
    const localWeights = committedWeights.map(() => 3);
    const externalWeights = committedWeights.map(() => 4);
    const fresh = { ...ui.current().planBase!, Future: 2, __externalUiRevision: 'b'.repeat(64) };
    const commit = deferred<JsonObject>();
    const readback = deferred<JsonObject>();
    backend.savePlan.mockReturnValueOnce(commit.promise);
    backend.readPlan.mockResolvedValueOnce(fresh).mockReturnValueOnce(readback.promise);
    ui.editPlan({ ...ui.current().plan!, [key]: committedWeights });
    const saving = ui.flushAutosave();
    await vi.advanceTimersByTimeAsync(0);
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    const payload = backend.savePlan.mock.calls[0][2];
    const expectedBase = { ...payload, __externalUiRevision: 'a'.repeat(64) };
    ui.editPlan({ ...ui.current().plan!, [key]: localWeights });
    commit.resolve({ success: true });
    await vi.advanceTimersByTimeAsync(0);
    expect(ui.current().planBase).toEqual(expectedBase);
    const external = { ...payload, [key]: externalWeights, __externalUiRevision: 'c'.repeat(64) };
    backend.readPlan.mockResolvedValue(external);
    readback.resolve(external);
    await saving;
    expect(ui.current().saveError).toContain(`Source changed at ${key}`);
    expect(ui.current().planBase).toEqual(expectedBase);
    expect(ui.current().plan).toEqual({ ...expectedBase, [key]: localWeights });
    expect(ui.current().planDirty).toBe(true);
    await vi.advanceTimersByTimeAsync(1000);
    await ui.retryAutosave();
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    expect(ui.current().plan![key]).toEqual(localWeights);
    expect(ui.current().planBase![key]).toEqual(committedWeights);
    ui.dispose();
  });

  it('rebases pending weights over normalized stages and nonoverlapping external edits', async () => {
    vi.useFakeTimers();
    const { ui, backend } = await editor();
    const committedWeights = [0, 0, 0, 0, 2, 0, 1, 0, 0];
    const localWeights = [0, 0, 0, 0, 3, 0, 1, 0, 0];
    const fresh = { ...ui.current().planBase!, Future: 2, __externalUiRevision: 'b'.repeat(64) };
    const readback = deferred<JsonObject>();
    backend.readPlan.mockResolvedValueOnce(fresh).mockReturnValueOnce(readback.promise);
    ui.editPlan({ ...ui.current().plan!, EnergyNguWeights: committedWeights });
    const saving = ui.flushAutosave();
    await vi.advanceTimersByTimeAsync(0);
    const payload = backend.savePlan.mock.calls[0][2];
    expect(payload.Future).toBe(2);
    ui.editPlan({ ...ui.current().plan!, EnergyNguWeights: localWeights });
    const normalized = { ...payload, MagicNguWeights: [2, 0, 0, 0, 0, 0, 0],
      Stages: (payload.Stages as JsonObject[]).map(stage => ({ ...stage, DurationSeconds: 0, DurationUnit: '%' })),
      RebirthHistory: [{ TimestampLocal: 'new' }], __externalUiRevision: 'c'.repeat(64) };
    readback.resolve(normalized);
    await saving;
    expect(ui.current().error).toBe('');
    expect(ui.current().saveError).toBe('');
    expect(ui.current().planBase).toEqual(normalized);
    expect(ui.current().plan).toEqual({ ...normalized, EnergyNguWeights: localWeights });
    expect(ui.current().planDirty).toBe(true);
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    ui.dispose();
  });

  it.each([15, null])('blocks weights when runtime planVersion is %s, retaining the draft', async version => {
    const { ui, backend } = await editor(version);
    ui.editPlan({ ...ui.current().plan!, MagicNguWeights: [2, 0, 0, 0, 0, 0, 0] });
    await ui.flushAutosave();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().saveError).toContain('planVersion 16');
    expect(ui.current().planDirty).toBe(true);
    ui.dispose();
  });

  it.each(['', -1, .5, 2147483648])('keeps invalid weight %s local until corrected', async invalid => {
    const { ui, backend } = await editor();
    ui.editPlan({ ...ui.current().plan!, MagicNguWeights: [invalid, 0, 0, 0, 0, 0, 0] });
    await ui.flushAutosave();
    expect(backend.savePlan).not.toHaveBeenCalled();
    expect(ui.current().saveError).toContain('MagicNguWeights[0]');
    ui.editPlan({ ...ui.current().plan!, MagicNguWeights: [0, 0, 0, 0, 0, 0, 0] });
    await ui.flushAutosave();
    expect(backend.savePlan).toHaveBeenCalledTimes(1);
    ui.dispose();
  });

  it('preserves legacy version, fields, and fixed durations while editing unrelated settings', async () => {
    const { ui, backend } = setup();
    const legacy = { ...plan(), Version: 15, NguGoal: 1, Stages: [{ Name: 'Fixed', Percent: 100, DurationSeconds: 120, DurationUnit: 's' }] };
    backend.readPlan.mockResolvedValue(legacy);
    await ui.refresh(); await ui.loadPlan('legacy');
    ui.editPlan({ ...ui.current().plan!, TimeTriggerSeconds: 600 });
    await ui.flushAutosave();
    expect(backend.savePlan).toHaveBeenCalledExactlyOnceWith('A:/runtime', 'legacy', { ...legacy, TimeTriggerSeconds: 600 });
    ui.dispose();
  });
});
