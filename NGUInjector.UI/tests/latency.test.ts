import { afterEach, describe, expect, it, vi } from 'vitest';
import * as api from '../src/lib/api';
import { createController } from '../src/lib/controller';
import type { JsonObject, NguiState } from '../src/lib/api';

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(yes => { resolve = yes; });
  return { promise, resolve };
}

async function setup() {
  vi.useFakeTimers();
  const initial: NguiState = { runtime_dir: 'A:/runtime', marker_enabled: true, connected: true, protocol: {}, settings: { ratio: 1 }, runtime: { startedAt: 'session', updatedAt: new Date().toISOString(), externalUiEnabled: true, status: 'running', protocolVersion: 3, supportedCommands: ['focusGoal'], activeFocusGoal: 'Wandoos' }, profiles: [], plans: [], logs: [], command_results: [] };
  const backend = { ...api, getState: vi.fn(async () => initial), storeRuntimeDir: vi.fn(), enqueueCommand: vi.fn(async () => 'id'), readCommandResult: vi.fn(async (): Promise<JsonObject | null> => null) };
  const ui = createController(backend, vi.fn(async () => true), initial.runtime_dir);
  await ui.refresh();
  return { ui, backend, initial };
}

afterEach(() => vi.useRealTimers());

function compactRuntime(initial: NguiState, patch: JsonObject = {}): JsonObject {
  return { startedAt: initial.runtime.startedAt, externalUiEnabled: true, protocolVersion: 3, planVersion: 15, updatedAt: initial.runtime.updatedAt, status: 'running', customStatus: 'Quick applied', activeFocusGoal: 'TimeMachine', appliedFocusGoal: 'TimeMachine', focusStatus: 'applied', settings: { useCustomRebirthPlan: false, autoRebirth: false }, ...patch };
}

describe('bounded action latency', () => {
  it('polls results at 50 ms without extending the 12 second budget', async () => {
    const { ui, backend } = await setup();
    const action = ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    await vi.advanceTimersByTimeAsync(0);
    expect(backend.readCommandResult).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(49);
    expect(backend.readCommandResult).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(backend.readCommandResult).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(12000);
    await action;
    expect(ui.current().error).toContain('timed out');
    expect(ui.current().pendingQuickGoal).toBe(null);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Wandoos');
    expect(ui.current().busy).toBe(false);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('shows runtime-confirmed Quick within 200 ms while the result is still in flight', async () => {
    const { ui, backend, initial } = await setup();
    ui.setSetting('ratio', 2);
    ui.advancedDirty(true);
    const completed = deferred<JsonObject | null>();
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const action = ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().pendingQuickGoal).toBe('TimeMachine');
    await vi.advanceTimersByTimeAsync(199);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Wandoos');
    const next = { ...initial, runtime: { ...initial.runtime, updatedAt: new Date().toISOString(), activeFocusGoal: 'TimeMachine' } };
    backend.getState.mockResolvedValue(next);
    await vi.advanceTimersByTimeAsync(1);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    expect(ui.current().busy).toBe(true);
    expect(ui.current().lastResult).toBe(null);
    expect(ui.current().patch).toEqual({ ratio: 2 });
    expect(ui.current().advancedDirty).toBe(true);
    completed.resolve({ id: 'id', command: 'focusGoal', success: true });
    await action;
    expect(ui.current().pendingQuickGoal).toBe(null);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('ignores a late status read after result runtime has already been acknowledged', async () => {
    const { ui, backend, initial } = await setup();
    const reading = deferred<NguiState>();
    const completed = deferred<JsonObject | null>();
    backend.getState.mockReturnValueOnce(reading.promise);
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const action = ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    await vi.advanceTimersByTimeAsync(200);
    const runtime = compactRuntime(initial, { updatedAt: new Date().toISOString() });
    completed.resolve({ id: 'id', command: 'focusGoal', success: true, data: { runtime } });
    await action;
    reading.resolve({ ...initial, runtime: { ...runtime, updatedAt: new Date(Date.now() + 1).toISOString(), activeFocusGoal: 'Adventure' } });
    await vi.advanceTimersByTimeAsync(0);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    expect(vi.getTimerCount()).toBe(0);
  });

  it('ignores old-session status and results after a regular refresh replaces the session', async () => {
    const { ui, backend, initial } = await setup();
    const reading = deferred<NguiState>();
    const completed = deferred<JsonObject | null>();
    backend.getState.mockReturnValueOnce(reading.promise);
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const action = ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    await vi.advanceTimersByTimeAsync(200);
    const next = { ...initial, runtime: { ...initial.runtime, startedAt: 'new-session', updatedAt: new Date().toISOString(), activeFocusGoal: 'Adventure' } };
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    expect(ui.current().pendingQuickGoal).toBe(null);
    reading.resolve({ ...initial, runtime: { ...initial.runtime, updatedAt: new Date().toISOString(), activeFocusGoal: 'TimeMachine' } });
    completed.resolve({ id: 'id', command: 'focusGoal', success: true, data: { runtime: initial.runtime } });
    await action;
    expect(ui.current().snapshot!.runtime).toEqual(next.runtime);
    expect(ui.current().lastResult).toBe(null);
    expect(ui.current().error).toContain('session changed');
    expect(vi.getTimerCount()).toBe(0);
  });

  it.each(['wrong-session', 'no-session', 'stale', 'older', 'future', 'invalid-time', 'missing-time', 'old-protocol', 'invalid-protocol', 'disabled'])('does not apply %s compact runtime from a successful result', async proof => {
    const { ui, backend, initial } = await setup();
    const runtime = compactRuntime(initial);
    if (proof === 'wrong-session') runtime.startedAt = 'other';
    if (proof === 'no-session') delete runtime.startedAt;
    if (proof === 'stale') runtime.updatedAt = new Date(Date.now() - 6000).toISOString();
    if (proof === 'older') runtime.updatedAt = new Date(Date.now() - 1).toISOString();
    if (proof === 'future') runtime.updatedAt = new Date(Date.now() + 3000).toISOString();
    if (proof === 'invalid-time') runtime.updatedAt = 'invalid';
    if (proof === 'missing-time') delete runtime.updatedAt;
    if (proof === 'old-protocol') runtime.protocolVersion = 2;
    if (proof === 'invalid-protocol') runtime.protocolVersion = 'invalid';
    if (proof === 'disabled') runtime.externalUiEnabled = false;
    backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'focusGoal', success: true, data: { runtime } });
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Wandoos');
    expect(ui.current().pendingQuickGoal).toBe(null);
    expect(ui.current().error).toBe('');
  });

  it.each(['', 'Adventure'])('uses the actual result goal %s rather than inferring the clicked goal', async activeFocusGoal => {
    const { ui, backend, initial } = await setup();
    const runtime = compactRuntime(initial, { activeFocusGoal });
    backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'focusGoal', success: true, data: { runtime } });
    await ui.runCommand('focusGoal', { goal: 'TimeMachine', mode: 'toggle' });
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe(activeFocusGoal);
    expect(ui.current().snapshot!.runtime.settings).toEqual({ autoRebirth: false, useCustomRebirthPlan: false });
    expect(ui.current().pendingQuickGoal).toBe(null);
  });

  it('retains acknowledged runtime through equal/older snapshots but accepts a newer heartbeat', async () => {
    const { ui, backend, initial } = await setup();
    const runtime = compactRuntime(initial);
    backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'focusGoal', success: true, data: { runtime } });
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    backend.getState.mockResolvedValueOnce({ ...initial, runtime: { ...initial.runtime, updatedAt: new Date(Date.now() - 1).toISOString() } });
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('TimeMachine');
    await vi.advanceTimersByTimeAsync(1);
    const next = { ...initial, runtime: { ...initial.runtime, updatedAt: new Date().toISOString(), activeFocusGoal: 'Adventure' } };
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Adventure');
  });

  it('merges real compact runtime over the latest session metadata and cached blood goals', async () => {
    const { ui, backend, initial } = await setup();
    const completed = deferred<JsonObject | null>();
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const action = ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    const latest = { ...initial, runtime: { ...initial.runtime, focusGoals: ['TimeMachine', 'Wandoos'], planNames: ['active'], planRevisionField: '__externalUiRevision', blood: { spaghettiGoalPercent: 321, ironPill: { reason: 'Cached estimate', worth: 100 } }, settings: { customPlanFile: 'active', customAutoBloodSpellsEnabled: true } } };
    backend.getState.mockResolvedValue(latest);
    await ui.refresh(true);
    const runtime = compactRuntime(initial, { nguAllocation: { goal: 1, energyTargets: [{ index: 4, allocated: '1000', bufferedCap: '2000' }] } });
    completed.resolve({ id: 'id', command: 'focusGoal', success: true, data: { expectedValue: 1200, runtime } });
    await action;
    expect(ui.current().snapshot!.runtime).toMatchObject({ ...latest.runtime, ...runtime, settings: { ...latest.runtime.settings, ...(runtime.settings as JsonObject) } });
    expect(ui.current().snapshot!.runtime.supportedCommands).toEqual(['focusGoal']);
    expect(ui.current().snapshot!.runtime.blood).toEqual(latest.runtime.blood);
    expect(ui.current().lastResult!.data).toEqual({ expectedValue: 1200, runtime });
  });
});

describe('runtime publication revisions', () => {
  async function versioned() {
    const context = await setup();
    const initial: NguiState = { ...context.initial, settings: { _customPlanFile: 'active', _useCustomRebirthPlan: true, _autoRebirth: true }, runtime: { ...context.initial.runtime, supportedCommands: ['focusGoal', 'status'], runtimeRevision: 5, appliedFocusGoal: 'Wandoos', focusStatus: 'applied', nguAllocation: { goal: 0 }, settings: { customPlanFile: 'active', useCustomRebirthPlan: true, autoRebirth: true } } };
    context.backend.getState.mockResolvedValue(initial);
    await context.ui.refresh(true);
    return { ...context, initial };
  }

  async function acknowledged() {
    const context = await versioned();
    const runtime = compactRuntime(context.initial, { runtimeRevision: 6, activeFocusGoal: 'EnergyNgu', appliedFocusGoal: '', focusStatus: 'waiting', nguAllocation: { goal: 0 }, settings: { customPlanFile: 'active', useCustomRebirthPlan: false, autoRebirth: false } });
    context.backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'focusGoal', success: true, completedAt: runtime.updatedAt, data: { runtime } });
    await context.ui.runCommand('focusGoal', { goal: 'EnergyNgu' });
    return context;
  }

  it('accepts higher-revision applied Quick and NGU state in the same JavaScript millisecond', async () => {
    const { ui, backend, initial } = await acknowledged();
    expect(ui.current().snapshot!.runtime.appliedFocusGoal).toBe('');
    expect(ui.current().snapshot!.runtime.focusStatus).toBe('waiting');
    expect((ui.current().snapshot!.runtime.nguAllocation as JsonObject).goal).toBe(0);
    const next = { ...ui.current().snapshot!, runtime: { ...ui.current().snapshot!.runtime, runtimeRevision: 7, appliedFocusGoal: 'EnergyNgu', focusStatus: 'applied', nguAllocation: { goal: 1 } } };
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.updatedAt).toBe(initial.runtime.updatedAt);
    expect(ui.current().snapshot!.runtime.runtimeRevision).toBe(7);
    expect(ui.current().snapshot!.runtime.appliedFocusGoal).toBe('EnergyNgu');
    expect(ui.current().snapshot!.runtime.focusStatus).toBe('applied');
    expect((ui.current().snapshot!.runtime.nguAllocation as JsonObject).goal).toBe(1);
  });

  it.each([5, 6])('rejects revision %s disk regressions even with a newer timestamp while keeping false pause flags', async runtimeRevision => {
    const { ui, backend, initial } = await acknowledged();
    const actual = ui.current().snapshot!.runtime;
    backend.getState.mockResolvedValueOnce({ ...initial, runtime: { ...initial.runtime, runtimeRevision, updatedAt: new Date(Date.now() + 100).toISOString(), appliedFocusGoal: 'Fight', nguAllocation: { goal: 1 } } });
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime).toEqual(actual);
    expect(ui.current().snapshot!.settings._useCustomRebirthPlan).toBe(false);
    expect(ui.current().snapshot!.settings._autoRebirth).toBe(false);
  });

  it('releases acknowledged false flags on higher-revision actual flags despite equal timestamps', async () => {
    const { ui, backend, initial } = await acknowledged();
    backend.getState.mockResolvedValueOnce({ ...initial, runtime: { ...initial.runtime, runtimeRevision: 7 } });
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.runtimeRevision).toBe(7);
    expect(ui.current().snapshot!.settings._useCustomRebirthPlan).toBe(true);
    expect(ui.current().snapshot!.settings._autoRebirth).toBe(true);
    expect(ui.current().snapshot!.runtime.settings).toEqual(initial.runtime.settings);
  });

  it('releases acknowledged true flags when a later same-millisecond publication reports false', async () => {
    const { ui, backend, initial } = await versioned();
    const runtime = compactRuntime(initial, { runtimeRevision: 6, settings: initial.runtime.settings });
    backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'status', success: true, completedAt: runtime.updatedAt, data: { runtime } });
    await ui.runCommand('status');
    expect(ui.current().snapshot!.settings._autoRebirth).toBe(true);
    expect(ui.current().snapshot!.settings._useCustomRebirthPlan).toBe(true);
    const next = { ...initial, settings: { ...initial.settings, _autoRebirth: false, _useCustomRebirthPlan: false }, runtime: { ...initial.runtime, runtimeRevision: 7, settings: { customPlanFile: 'active', autoRebirth: false, useCustomRebirthPlan: false } } };
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    expect(ui.current().snapshot!.settings._autoRebirth).toBe(false);
    expect(ui.current().snapshot!.settings._useCustomRebirthPlan).toBe(false);
    expect(ui.current().snapshot!.runtime.settings).toEqual(next.runtime.settings);
  });

  it.each([6, 7])('ignores revision %s results arriving after already-published revision 7', async runtimeRevision => {
    const { ui, backend, initial } = await versioned();
    const completed = deferred<JsonObject | null>();
    backend.readCommandResult.mockReturnValueOnce(completed.promise);
    const action = ui.runCommand('focusGoal', { goal: 'EnergyNgu' });
    const next = { ...initial, runtime: { ...initial.runtime, runtimeRevision: 7, activeFocusGoal: 'EnergyNgu', appliedFocusGoal: 'EnergyNgu', nguAllocation: { goal: 1 } } };
    backend.getState.mockResolvedValue(next);
    await ui.refresh(true);
    const runtime = compactRuntime(initial, { runtimeRevision, nguAllocation: { goal: 0 } });
    completed.resolve({ id: 'id', command: 'focusGoal', success: true, completedAt: runtime.updatedAt, data: { runtime } });
    await action;
    expect(ui.current().snapshot!.runtime).toEqual(next.runtime);
    expect(ui.current().snapshot!.settings).toEqual(initial.settings);
    expect(ui.current().pendingQuickGoal).toBe(null);
    expect(ui.current().busy).toBe(false);
  });

  it('uses revision ordering across small clock corrections while still requiring a fresh heartbeat', async () => {
    const { ui, backend, initial } = await versioned();
    const runtime = compactRuntime(initial, { runtimeRevision: 6, updatedAt: new Date(Date.now() - 1).toISOString(), nguAllocation: { goal: 1 } });
    backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'focusGoal', success: true, completedAt: runtime.updatedAt, data: { runtime } });
    await ui.runCommand('focusGoal', { goal: 'EnergyNgu' });
    expect(ui.current().snapshot!.runtime.runtimeRevision).toBe(6);
    expect((ui.current().snapshot!.runtime.nguAllocation as JsonObject).goal).toBe(1);
  });

  it('accepts a low revision in a new session and resets the acknowledgement watermark', async () => {
    const { ui, backend, initial } = await acknowledged();
    const next = { ...initial, runtime: { ...initial.runtime, startedAt: 'new-session', runtimeRevision: 0, updatedAt: new Date(Date.now() - 1000).toISOString() } };
    backend.getState.mockResolvedValueOnce(next);
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime).toEqual(next.runtime);
    expect(ui.current().snapshot!.settings).toEqual(initial.settings);
    backend.getState.mockResolvedValueOnce({ ...next, runtime: { ...next.runtime, runtimeRevision: 1, nguAllocation: { goal: 1 } } });
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.runtimeRevision).toBe(1);
    expect((ui.current().snapshot!.runtime.nguAllocation as JsonObject).goal).toBe(1);
  });

  it('publishes source disconnection even when the disk revision is older', async () => {
    const { ui, backend, initial } = await acknowledged();
    const actual = ui.current().snapshot!.runtime;
    backend.getState.mockResolvedValueOnce({ ...initial, connected: false });
    await ui.refresh(true);
    expect(ui.current().snapshot!.connected).toBe(false);
    expect(ui.current().snapshot!.runtime).toEqual(actual);
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(backend.enqueueCommand).toHaveBeenCalledTimes(1);
    expect(ui.current().error).toContain('Disconnected');
  });

  it.each([undefined, null, '7', 1.5, -1, Number.MAX_SAFE_INTEGER + 1])('falls back to timestamps for an absent/invalid revision %s', async runtimeRevision => {
    const { ui, backend, initial } = await acknowledged();
    const runtime: JsonObject = { ...initial.runtime, activeFocusGoal: 'Adventure', appliedFocusGoal: 'Adventure', nguAllocation: { goal: 1 } };
    if (runtimeRevision === undefined) delete runtime.runtimeRevision;
    else runtime.runtimeRevision = runtimeRevision;
    backend.getState.mockResolvedValueOnce({ ...initial, runtime });
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('EnergyNgu');
    backend.getState.mockResolvedValueOnce({ ...initial, runtime: { ...runtime, updatedAt: new Date(Date.now() + 1).toISOString() } });
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Adventure');
    expect((ui.current().snapshot!.runtime.nguAllocation as JsonObject).goal).toBe(1);
  });

  it('does not infer a revision for a compact legacy result from current metadata', async () => {
    const { ui, backend, initial } = await versioned();
    const runtime = compactRuntime(initial, { nguAllocation: { goal: 1 } });
    delete runtime.protocolVersion;
    backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'focusGoal', success: true, data: { runtime } });
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().snapshot!.runtime.runtimeRevision).toBe(undefined);
    expect(ui.current().snapshot!.runtime.protocolVersion).toBe(3);
    expect(ui.current().snapshot!.runtime.supportedCommands).toEqual(initial.runtime.supportedCommands);
    expect((ui.current().snapshot!.runtime.nguAllocation as JsonObject).goal).toBe(1);
    backend.getState.mockResolvedValueOnce({ ...initial, runtime: { ...initial.runtime, runtimeRevision: 1, updatedAt: new Date(Date.now() + 1).toISOString() } });
    await ui.refresh(true);
    expect(ui.current().snapshot!.runtime.runtimeRevision).toBe(1);
  });

  it('requires an explicit same-session result even when its revision is higher', async () => {
    const { ui, backend, initial } = await versioned();
    const runtime = compactRuntime(initial, { runtimeRevision: 100, nguAllocation: { goal: 1 } });
    delete runtime.startedAt;
    backend.readCommandResult.mockResolvedValueOnce({ id: 'id', command: 'focusGoal', success: true, data: { runtime } });
    await ui.runCommand('focusGoal', { goal: 'TimeMachine' });
    expect(ui.current().snapshot!.runtime.runtimeRevision).toBe(5);
    expect(ui.current().snapshot!.runtime.activeFocusGoal).toBe('Wandoos');
    expect((ui.current().snapshot!.runtime.nguAllocation as JsonObject).goal).toBe(0);
  });
});

describe('runtime revision validation', () => {
  it.each([0, 1, Number.MAX_SAFE_INTEGER])('accepts safe nonnegative integer %s', runtimeRevision => {
    expect(api.runtimeRevision({ runtimeRevision })).toBe(runtimeRevision);
  });
  it.each([null, '1', -1, 1.5, Infinity, NaN, Number.MAX_SAFE_INTEGER + 1, api.parseJson('9007199254740993')])('does not coerce invalid revision %s', runtimeRevision => {
    expect(api.runtimeRevision({ runtimeRevision })).toBe(null);
  });
});
