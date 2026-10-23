import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonObject, type NguiState } from '../src/lib/api';
import { heartbeat } from '../src/lib/model';
import { parsePlanProgress, planProgressDuration, stageHistoryDetails } from '../src/lib/plan-progress';
import { stagePresets } from '../src/lib/stage-presets';
import PlanProgress from '../src/lib/PlanProgress.svelte';

function snapshot(patch: JsonObject = {}): JsonObject {
  const active = typeof patch.activeStageIndex === 'number' ? patch.activeStageIndex : 2;
  return {
    available: true, status: 'running', planName: 'Active eight-part plan', planVersion: 17,
    schedulerMode: 'stages', activeStageIndex: 2, stageProgress: .25,
    elapsedSeconds: 675, stageElapsedSeconds: 75, cycle: 0, repeating: true, loopStartIndex: 5,
    stages: Array.from({ length: 8 }, (_, index) => ({
      index, state: index === active ? 'current' : active >= 0 && index < active ? 'completed' : 'pending', appliedSeconds: index === active ? 75 : index < active ? 300 : 0, elapsedSeconds: index === active ? 100 : index < active ? 350 : 0, visits: index <= active ? 1 : 0,
      name: `Part ${index + 1}`, durationSeconds: 300, configuredDurationSeconds: 300, flexWeight: 100, timingKind: 'fixed', completionMode: 0,
      energyFocus: 1, magicFocus: 1, gearGoal: 5,
      configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['TM'], magicPriorities: ['TM']
    })),
    ...patch
  };
}

describe('blood-goal priority display', () => {
  const message = 'Blood goal catch-up';
  function casual(patch: JsonObject = {}): JsonObject {
    const data = snapshot({ bloodGoalsPrioritized: true, ...patch });
    const rows = data.stages as JsonObject[];
    rows[2] = { ...rows[2], energyFocus: 5, magicFocus: 6, gearGoal: 2, configuredMagicPriorities: ['NGU', 'RIT'], magicPriorities: ['RIT', 'NGU'] };
    return data;
  }
  it('shows active ordinary catch-up alongside the effective magic order', () => {
    const data = casual();
    const before = stringifyJson(data);
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    expect(body).toContain(message);
    expect(body).toContain('title="Spaghetti / Counterfeit Gold goals; energy and stage gear unchanged"');
    expect(body).not.toContain('Blood Magic is catching up');
    expect(body).toContain('Magic NGU / RIT > NGU');
    expect(stringifyJson(data)).toBe(before);
  });
  it.each<JsonObject>([
    { bloodGoalsPrioritized: false }, { bloodGoalsPrioritized: 'true' },
    { bloodReservedForIronPill: true }, { status: 'paused' },
    { status: 'interrupted' }, { status: 'disabled' }, { available: false }
  ])('does not claim catch-up when it is inactive or superseded: %j', patch => {
    const { body } = render(PlanProgress, { props: { data: casual(patch), connected: true } });
    expect(body).not.toContain(message);
  });
  it('hides stale catch-up while disconnected', () => {
    const { body } = render(PlanProgress, { props: { data: casual(), connected: false } });
    expect(body).not.toContain(message);
    expect(body).toContain('Waiting for plan progress');
  });
});

describe('combat cleanup ownership display', () => {
  it.each(['root', 'nested'])('shows reconciliation from %s without an active run or a finished schedule', source => {
    const reason = 'Checkpoint write failed: access denied. Retrying automatically.';
    const data = snapshot({ scheduleComplete: true, stageProgress: 1, reason, ...(source === 'root' ? { cleanupRecoveryRequired: true } : {}), combatPrep: { schemaVersion: 1, active: false, cleanupPending: false, ...(source === 'nested' ? { cleanupRecoveryRequired: true, cleanupRecoveryReason: reason } : {}) } });
    const before = stringifyJson(data);
    const body = render(PlanProgress, { props: { data, connected: true } }).body;
    expect(body).toContain(`Reconciling: ${reason}`);
    expect(body).toContain(`aria-valuetext="Reconciling: ${reason}"`);
    for (const text of ['Schedule complete', 'Sustaining', '100% complete', 'still owns equipment and resources', 'acknowledge', 'Restart current stage', 'Set as current stage', 'Skip to selected stage']) expect(body).not.toContain(text);
    expect(stringifyJson(data)).toBe(before);
    const stale = render(PlanProgress, { props: { data, connected: false } }).body;
    expect(stale).not.toContain('Reconciling');
  });
  it.each([false, true])('keeps recovery explicit even with schedule complete %s', scheduleComplete => {
    const data = snapshot({ scheduleComplete, stageProgress: 1, bloodGoalsPrioritized: true, combatPrep: { schemaVersion: 1, active: true, cleanupPending: true, status: 'failed', owner: 'scheduled' } });
    const before = stringifyJson(data);
    const body = render(PlanProgress, { props: { data, connected: true } }).body;
    expect(body).toContain('Cleanup pending: combat prep still owns equipment and resources.');
    expect(body).not.toContain('Schedule complete');
    expect(body).not.toContain('Sustaining');
    expect(body).not.toContain('100% complete');
    expect(body).not.toContain('Blood goal catch-up');
    expect(stringifyJson(data)).toBe(before);
  });
  it('does not claim live cleanup ownership from stale telemetry', () => {
    const data = snapshot({ combatPrep: { schemaVersion: 1, active: true, cleanupPending: true, status: 'failed' } });
    const body = render(PlanProgress, { props: { data, connected: false } }).body;
    expect(body).not.toContain('Cleanup pending');
    expect(body).toContain('Waiting for plan progress');
  });
});

describe('scheduled preset blockers', () => {
  function blockedProgress(fields: Record<string, unknown> = {}): JsonObject {
    const data = snapshot({ activePresetId: 'ngu' });
    Object.assign((data.stages as JsonObject[])[2], { presetId: '@active', resolvedPresetId: 'ngu', presetName: 'Energy NGU', presetStatus: 'failed', presetReason: 'Use Preview + Apply first.', ...fields });
    return data;
  }

  it.each(['failed', 'blocked', 'waiting'])('shows the active preset %s reason beside progress', presetStatus => {
    const data = blockedProgress({ presetStatus, presetReason: '  Use Preview + Apply first.  ' });
    const before = stringifyJson(data);
    const expected = `Preset ${presetStatus}: Use Preview + Apply first.`;
    expect(parsePlanProgress(data, true).stages[2].presetBlocker).toBe(expected);
    const body = render(PlanProgress, { props: { data, connected: true } }).body;
    expect(body).toContain(`role="status">${expected}</p>`);
    expect(body).toContain(`aria-valuetext="${expected}"`);
    expect(stringifyJson(data)).toBe(before);
  });

  it.each([undefined, '', '   ', null, 42])('keeps a reported failure meaningful without inventing a reason: %j', presetReason => {
    const data = blockedProgress({ presetReason });
    expect(parsePlanProgress(data, true).stages[2].presetBlocker).toBe('Preset failed.');
    expect(render(PlanProgress, { props: { data, connected: true } }).body).toContain('role="status">Preset failed.</p>');
  });

  it.each(['applied', 'pending', 'not-used', 'displaced', 'FAILED', '', null, 42])('does not turn routine or malformed status %j into a blocker', presetStatus => {
    const data = blockedProgress({ presetStatus });
    expect(parsePlanProgress(data, true).stages[2].presetBlocker).toBe('');
    expect(render(PlanProgress, { props: { data, connected: true } }).body).not.toContain('Use Preview + Apply first.');
  });

  it.each(['other stage', 'paused', 'interrupted', 'disabled', 'inline', 'automatic', 'unknown position', 'cleanup'])('hides an unattributable or superseded preset failure: %s', scenario => {
    const data = blockedProgress();
    const row = (data.stages as JsonObject[])[2];
    if (scenario === 'other stage') { data.activeStageIndex = 3; row.state = 'completed'; (data.stages as JsonObject[])[3].state = 'current'; }
    if (['paused', 'interrupted', 'disabled'].includes(scenario)) data.status = scenario;
    if (scenario === 'inline') { row.presetId = ''; row.resolvedPresetId = ''; row.presetName = ''; }
    if (scenario === 'automatic') row.executionMode = 1;
    if (scenario === 'unknown position') row.state = 'unknown';
    if (scenario === 'cleanup') data.combatPrep = { schemaVersion: 1, cleanupPending: true };
    expect(render(PlanProgress, { props: { data, connected: true } }).body).not.toContain('Use Preview + Apply first.');
    expect(render(PlanProgress, { props: { data, connected: false } }).body).not.toContain('Use Preview + Apply first.');
  });
});

describe('runtime plan progression parser', () => {
  it.each(['ngu', '@active'])('uses the runtime weighted name for binding %s despite an ignored Time Machine tuple', presetId => {
    const data = snapshot({ activePresetId: 'ngu' });
    const row = (data.stages as JsonObject[])[2];
    Object.assign(row, { name: 'Old Time Machine descriptor', presetId, resolvedPresetId: 'ngu', presetName: 'Energy NGU' });
    const before = stringifyJson(data);
    const view = parsePlanProgress(data, true);
    expect(view.stages[2]).toMatchObject({ name: 'Energy NGU', energy: 'Global weighted preset', magic: 'Global weighted preset', gear: 'Global weighted preset', state: 'current', timingKind: 'fixed', durationSeconds: 300 });
    const body = render(PlanProgress, { props: { data, connected: true } }).body;
    expect(body).toContain('aria-valuetext="Running: Energy NGU; 25% complete"');
    expect(body).not.toContain('Running: Time Machine;');
    expect(body).not.toContain('Old Time Machine descriptor');
    expect(stringifyJson(data)).toBe(before);
    expect(parsePlanProgress(data, false).stages).toEqual([]);
  });
  it('labels a newly inherited stage without any legacy tuple solely from runtime presetName', () => {
    const data = snapshot({ activePresetId: 'ngu' });
    const rows = data.stages as JsonObject[];
    rows[2] = { index: 2, state: 'current', completionMode: 0, presetId: '@active', resolvedPresetId: 'ngu', presetName: 'Captured NGU policy' };
    expect(parsePlanProgress(data, true).stages[2]).toMatchObject({ name: 'Captured NGU policy', energy: 'Global weighted preset', magic: 'Global weighted preset', gear: 'Global weighted preset' });
    data.activePresetId = 'different_id';
    expect(parsePlanProgress(data, true).stages[2].name).toBe('Captured NGU policy');
  });
  it.each([undefined, '', null, 123])('does not fall back to ignored legacy labels when weighted presetName is %j', presetName => {
    const data = snapshot();
    const row = (data.stages as JsonObject[])[2];
    Object.assign(row, { presetId: '@active', resolvedPresetId: 'ngu' });
    if (presetName !== undefined) row.presetName = presetName;
    expect(parsePlanProgress(data, true).stages[2]).toMatchObject({ name: 'Global preset (name unavailable)', energy: 'Global weighted preset', magic: 'Global weighted preset', gear: 'Global weighted preset' });
  });
  it('retains legacy allocation labels when the new runtime explicitly reports an inline stage', () => {
    const data = snapshot({ activePresetId: 'ngu' });
    Object.assign((data.stages as JsonObject[])[2], { presetId: '', resolvedPresetId: '', presetName: '' });
    expect(parsePlanProgress(data, true).stages[2]).toMatchObject({ name: 'Time Machine', energy: 'Time Machine / TM', magic: 'Time Machine / TM', gear: 'Time Machine' });
  });
  it('keeps automatic execution authoritative over a preserved weighted reference', () => {
    const data = snapshot();
    Object.assign((data.stages as JsonObject[])[2], { executionMode: 1, presetId: '@active', resolvedPresetId: 'ngu', presetName: 'Energy NGU' });
    expect(parsePlanProgress(data, true).stages[2]).toMatchObject({ name: 'Augments + Wandoos (automatic, includes AT)', energy: 'Automatic AT / augments / Wandoos', magic: 'Automatic Wandoos', gear: 'Automatic phase gear' });
  });
  it('projects the actual runtime position with compact allocations', () => {
    const view = parsePlanProgress(snapshot(), true);
    expect(view.showWaitingPlanProgress).toBe(false);
    expect(view.planName).toBe('Active eight-part plan');
    expect(view.planVersion).toBe('17');
    expect(view.activeStageIndex).toBe(2);
    expect(view.stageProgress).toBe(.25);
    expect(view.stages.map(stage => stage.state)).toEqual(['completed', 'completed', 'current', 'pending', 'pending', 'pending', 'pending', 'pending']);
    expect(view.stages[2]).toMatchObject({ index: 2, name: 'Time Machine', durationSeconds: 300, completionMode: 'Time', energy: 'Time Machine / TM', magic: 'Time Machine / TM', gear: 'Time Machine' });
  });

  it.each(stagePresets)('labels $label from configured allocations without mutating runtime data', preset => {
    const data = snapshot();
    const rows = data.stages as JsonObject[];
    rows[0] = {
      ...rows[0], energyFocus: preset.EnergyFocus, magicFocus: preset.MagicFocus, gearGoal: preset.GearGoal,
      configuredEnergyPriorities: [...preset.EnergyPriorities], configuredMagicPriorities: [...preset.MagicPriorities],
      energyPriorities: ['TM'], magicPriorities: ['RIT-0']
    };
    const before = stringifyJson(data);
    for (const row of rows) {
      for (const key of ['configuredEnergyPriorities', 'configuredMagicPriorities', 'energyPriorities', 'magicPriorities']) Object.freeze(row[key]);
      Object.freeze(row);
    }
    Object.freeze(rows);
    Object.freeze(data);
    const stage = parsePlanProgress(data, true).stages[0];
    expect(stage.name).toBe(preset.label);
    expect(stage.energy).toMatch(/ \/ TM$/);
    expect(stage.magic).toMatch(/ \/ RIT-0$/);
    expect(stringifyJson(data)).toBe(before);
  });

  it.each<JsonObject>([
    { energyFocus: 0 }, { magicFocus: 4 }, { gearGoal: -1 }
  ])('labels unknown focus/gear tuples as Custom despite a preset name: %j', patch => {
    const data = snapshot();
    const rows = data.stages as JsonObject[];
    rows[0] = { ...rows[0], name: 'Time Machine', ...patch };
    expect(parsePlanProgress(data, true).stages[0].name).toBe('Custom');
  });

  it.each(stagePresets)('keeps $label identity when configured priority orders are customized', preset => {
    const data = snapshot();
    const rows = data.stages as JsonObject[];
    const row = {
      ...rows[0], name: preset.label, energyFocus: preset.EnergyFocus, magicFocus: preset.MagicFocus, gearGoal: preset.GearGoal,
      configuredEnergyPriorities: [...preset.EnergyPriorities], configuredMagicPriorities: [...preset.MagicPriorities],
      energyPriorities: [...preset.EnergyPriorities], magicPriorities: [...preset.MagicPriorities]
    };
    for (const key of ['configuredEnergyPriorities', 'configuredMagicPriorities'] as const) {
      for (const priorities of [[], row[key].slice(1), [...row[key], 'TM'], [...row[key]].reverse()]) {
        rows[0] = { ...row, [key]: priorities };
        expect(parsePlanProgress(data, true).stages[0].name).toBe(preset.label);
      }
    }
  });

  it('does not let effective priorities spoof an unknown focus/gear tuple', () => {
    const data = snapshot();
    const rows = data.stages as JsonObject[];
    rows[0] = { ...rows[0], name: 'Time Machine', gearGoal: -1, configuredEnergyPriorities: ['AT'], energyPriorities: ['TM'], magicPriorities: ['TM'] };
    expect(parsePlanProgress(data, true).stages[0]).toMatchObject({ name: 'Custom', energy: 'Time Machine / TM', magic: 'Time Machine / TM' });
  });

  it.each<JsonObject>([
    {}, { name: 'Wandoos' }, { name: '' }, { name: 123 }
  ])('ignores legacy names when displaying the configured preset: %j', legacy => {
    const data = snapshot();
    const rows = data.stages as JsonObject[];
    const { name: _name, ...row } = rows[0];
    rows[0] = { ...row, ...legacy };
    const before = stringifyJson(data);
    expect(parsePlanProgress(data, true).stages[0].name).toBe('Time Machine');
    expect(stringifyJson(data)).toBe(before);
  });

  it.each([
    ['configuredEnergyPriorities'], ['configuredMagicPriorities'], ['configuredEnergyPriorities', 'configuredMagicPriorities']
  ])('keeps the base preset label when configured priorities are absent: %j', (...keys) => {
    const data = snapshot();
    const row = (data.stages as JsonObject[])[0];
    row.name = 'Time Machine';
    for (const key of keys) delete row[key];
    expect(parsePlanProgress(data, true).stages[0]).toMatchObject({ name: 'Time Machine', energy: 'Time Machine / TM', magic: 'Time Machine / TM' });
  });

  it.each([
    ['absent', {}], ['null', null], ['array', []], ['invalid availability', { available: 'true' }],
    ['unknown status', { status: 'complete' }], ['no stages', { stages: [] }],
    ['partial stages', { stages: [{ index: 0 }, null] }],
    ['renumbered stages', { stages: [{ index: 1 }] }],
    ['fractional stage index', { activeStageIndex: .5 }], ['out of range stage index', { activeStageIndex: 8 }],
    ['missing running position', { activeStageIndex: null }], ['negative running position', { activeStageIndex: -1 }],
    ['unknown scheduler', { schedulerMode: 'guess' }], ['invalid loop boundary', { loopStartIndex: 8 }]
  ])('waits safely for %s data', (label, patch) => {
    const data = label === 'absent' || patch === null || Array.isArray(patch) ? patch : snapshot(patch as JsonObject);
    const view = parsePlanProgress(data as JsonObject, true);
    expect(view.showWaitingPlanProgress).toBe(true);
    expect(view.activeStageIndex).toBeNull();
    expect(view.stageProgress).toBeNull();
    expect(view.stages).toEqual([]);
  });

  it('clears all live position and plan identity when freshness is lost', () => {
    const view = parsePlanProgress(snapshot(), false);
    expect(view).toMatchObject({ showWaitingPlanProgress: true, status: 'unavailable', activeStageIndex: null, stageProgress: null, planName: '', stages: [] });
    expect(view.reason).toBe('Connect to see fresh runtime plan progression.');
  });

  it('uses the existing heartbeat gate rather than a raw connected flag', () => {
    const now = Date.parse('2026-10-14T12:00:00Z');
    const state: NguiState = {
      connected: true, runtime: { updatedAt: new Date(now).toISOString(), externalUiEnabled: true, status: 'running' },
      runtime_dir: '', marker_enabled: true, protocol: {}, settings: {}, profiles: [], plans: [], logs: [], command_results: []
    };
    expect(parsePlanProgress(snapshot(), heartbeat(state, now).connected).activeStageIndex).toBe(2);
    expect(parsePlanProgress(snapshot(), heartbeat(state, now + 5001).connected).showWaitingPlanProgress).toBe(true);
  });

  it.each(['paused', 'interrupted'])('retains the reported %s position without inferring advancement', status => {
    const view = parsePlanProgress(snapshot({ status, elapsedSeconds: 999999, stageElapsedSeconds: 999999, reason: 'Quick: Time Machine overrides stage allocations.' }), true);
    expect(view.status).toBe(status);
    expect(view.activeStageIndex).toBe(2);
    expect(view.stageProgress).toBe(.25);
    expect(view.stages[2].state).toBe('current');
    expect(view.stages[3].state).toBe('pending');
    expect(view.reason).toBe('Quick: Time Machine overrides stage allocations.');
  });

  it.each(['paused', 'interrupted'])('does not invent a %s position when the runtime reports none', status => {
    const view = parsePlanProgress(snapshot({ status, activeStageIndex: -1 }), true);
    expect(view.showWaitingPlanProgress).toBe(false);
    expect(view.activeStageIndex).toBeNull();
    expect(view.stageProgress).toBeNull();
    expect(view.stages.map(stage => stage.state)).toEqual(Array(8).fill('pending'));
  });

  it('does not advance a mandatory AT stage based on elapsed time', () => {
    const data = snapshot({ activeStageIndex: 0, stageProgress: null, elapsedSeconds: 999999, stageElapsedSeconds: 999999 });
    (data.stages as JsonObject[])[0] = { ...(data.stages as JsonObject[])[0], completionMode: 1, targetAdvancedTrainingPower: '9223372036854775806', targetAdvancedTrainingToughness: '6000' };
    const view = parsePlanProgress(data, true);
    expect(view.schedulerMode).toBe('stages');
    expect(view.activeStageIndex).toBe(0);
    expect(view.stageProgress).toBeNull();
    expect(view.stages[0]).toMatchObject({ state: 'current', completionMode: 'AT targets', targets: 'P 9223372036854775806 / T 6000' });
    expect(view.stages.slice(1).map(stage => stage.state)).toEqual(Array(7).fill('pending'));
  });

  it('uses actual stage progress when supplied, never a duration ratio', () => {
    const view = parsePlanProgress(snapshot({ stageProgress: .6, stageElapsedSeconds: 999999 }), true);
    expect(view.stageProgress).toBe(.6);
    expect(view.activeStageIndex).toBe(2);
  });

  it('leaves a 100% stage current until the backend changes the position', () => {
    const view = parsePlanProgress(snapshot({ stageProgress: 1, stageElapsedSeconds: 300 }), true);
    expect(view.stages[2].state).toBe('current');
    expect(view.stages[3].state).toBe('pending');
  });

  it('retains explicitly completed intro stages and the reported loop tail on later cycles', () => {
    const view = parsePlanProgress(snapshot({ activeStageIndex: 5, stageProgress: 0, cycle: 4 }), true);
    expect(view.cycle).toBe(4);
    expect(view.loopStartIndex).toBe(5);
    expect(view.stages.map(stage => stage.state)).toEqual(['completed', 'completed', 'completed', 'completed', 'completed', 'current', 'pending', 'pending']);
    expect(view.stages.filter(stage => stage.loopStart).map(stage => stage.index)).toEqual([5]);
  });

  it('supports a loop at the first stage without keeping previous-cycle completion', () => {
    const view = parsePlanProgress(snapshot({ activeStageIndex: 0, loopStartIndex: 0, cycle: 10 }), true);
    expect(view.stages.map(stage => stage.state)).toEqual(['current', 'pending', 'pending', 'pending', 'pending', 'pending', 'pending', 'pending']);
    expect(view.stages[0].loopStart).toBe(true);
  });

  it('does not mark a nonrepeating stage as a loop boundary', () => {
    expect(parsePlanProgress(snapshot({ repeating: false, loopStartIndex: -1 }), true).stages.map(stage => stage.loopStart)).toEqual(Array(8).fill(false));
  });

  it('keeps disabled stages neutral even if a position is present', () => {
    const view = parsePlanProgress(snapshot({ status: 'disabled', reason: 'Stage scheduler is disabled.' }), true);
    expect(view.status).toBe('disabled');
    expect(view.activeStageIndex).toBeNull();
    expect(view.stageProgress).toBeNull();
    expect(view.stages.map(stage => stage.state)).toEqual(Array(8).fill('unknown'));
  });

  it.each(['disabled', 'unavailable'])('shows the backend reason for %s snapshots without stages', status => {
    const view = parsePlanProgress({ available: false, status, reason: 'No active custom plan.' }, true);
    expect(view.showWaitingPlanProgress).toBe(true);
    expect(view.status).toBe(status);
    expect(view.reason).toBe('No active custom plan.');
  });

  it.each([-1, 1.01, Infinity, NaN, '0.5', null])('does not fabricate a percentage from invalid progress %s', stageProgress => {
    const view = parsePlanProgress(snapshot({ stageProgress }), true);
    expect(view.stageProgress).toBeNull();
    expect(view.activeStageIndex).toBe(2);
  });

  it('preserves exact lossless AT targets without mutating the runtime object', () => {
    const data = snapshot();
    (data.stages as JsonObject[])[0] = { index: 0, completionMode: 'advancedTraining', targetAdvancedTrainingPower: parseJson('9223372036854775806'), targetAdvancedTrainingToughness: '6000' };
    const before = stringifyJson(data);
    expect(parsePlanProgress(data, true).stages[0].targets).toBe('P 9223372036854775806 / T 6000');
    expect(stringifyJson(data)).toBe(before);
  });

  it('shows unknown targets rather than invalid or already-rounded values', () => {
    const data = snapshot();
    (data.stages as JsonObject[])[0] = { index: 0, completionMode: 1, targetAdvancedTrainingPower: {}, targetAdvancedTrainingToughness: 9007199254740992 };
    expect(parsePlanProgress(data, true).stages[0].targets).toBe('P ? / T ?');
  });

  it('falls back visibly for invalid optional metadata, without losing the actual position', () => {
    const data = snapshot({ elapsedSeconds: -1, stageElapsedSeconds: 'oops', cycle: -2 });
    (data.stages as JsonObject[])[2] = { index: 2, state: 'current', name: '', durationSeconds: -1, energyFocus: {}, magicPriorities: [null, 'RIT-0'], gearGoal: 99 };
    const view = parsePlanProgress(data, true);
    expect(view).toMatchObject({ activeStageIndex: 2, elapsedSeconds: null, stageElapsedSeconds: null, cycle: null });
    expect(view.stages[2]).toMatchObject({ name: 'Custom', durationSeconds: null, energy: 'Not reported', magic: 'RIT-0', gear: 'Not reported', completionMode: 'Completion not reported' });
  });
});

describe('compact duration labels', () => {
  it.each([[0, '0s'], [30, '30s'], [300, '5m'], [3661.9, '1h 1m 1s'], [null, 'Duration unknown'], [-1, 'Duration unknown']])('formats %s as %s', (seconds, label) => {
    expect(planProgressDuration(seconds as number | null)).toBe(label);
  });
});

describe('explicit stage history', () => {
  it('uses reported states on both sides of the current stage, including a backward override', () => {
    const data = snapshot({ activeStageIndex: 1, stageEntry: 'entry-back', lastStageOverride: 'Set stage 2 as current', lastStageOverrideAt: 675 });
    const rows = data.stages as JsonObject[];
    rows[0].state = 'skipped';
    rows[4].state = 'completed';
    rows[6].state = 'skipped';
    const view = parsePlanProgress(data, true);
    expect(view).toMatchObject({ stageEntry: 'entry-back', lastStageOverride: 'Set stage 2 as current', lastStageOverrideAt: 675 });
    expect(view.stages.map(row => row.state)).toEqual(['skipped', 'current', 'pending', 'pending', 'completed', 'pending', 'skipped', 'pending']);
    expect(render(PlanProgress, { props: { data, connected: true } }).body).not.toContain('Set stage 2 as current');
  });

  it('never infers completion or current state from position when row states are missing or contradictory', () => {
    const data = snapshot();
    (data.stages as JsonObject[]).forEach(row => { delete row.state; });
    let view = parsePlanProgress(data, true);
    expect(view.activeStageIndex).toBeNull();
    expect(view.stageProgress).toBeNull();
    expect(view.stages.map(row => row.state)).toEqual(Array(8).fill('unknown'));
    (data.stages as JsonObject[])[0].state = 'current';
    view = parsePlanProgress(data, true);
    expect(view.stages[0].state).toBe('unknown');
    expect(view.activeStageIndex).toBeNull();
  });

  it('keeps cumulative allocation time distinct from selected wall time and stage credit', () => {
    const data = snapshot({ stageElapsedSeconds: 30 });
    Object.assign((data.stages as JsonObject[])[2], { appliedSeconds: 90, elapsedSeconds: 480, visits: 3 });
    const stage = parsePlanProgress(data, true).stages[2];
    expect(stage).toMatchObject({ appliedSeconds: 90, elapsedSeconds: 480, visits: 3 });
    expect(stageHistoryDetails(stage)).toBe('Fixed duration\nActual applied: 1m 30s\nElapsed selected (including offline): 8m\nVisits: 3\nState: Current');
    expect(parsePlanProgress(data, true).stageElapsedSeconds).toBe(30);
  });

  it.each<JsonObject>([
    {}, { appliedSeconds: -1, elapsedSeconds: '10', visits: 1.5, state: 'passed' },
    { appliedSeconds: null, elapsedSeconds: null, visits: -1, state: 'future' }
  ])('shows absent or malformed history as Not recorded, not zero: %j', patch => {
    const data = snapshot();
    (data.stages as JsonObject[])[0] = { index: 0, timingKind: 'flexible', ...patch };
    const stage = parsePlanProgress(data, true).stages[0];
    expect(stage).toMatchObject({ appliedSeconds: null, elapsedSeconds: null, visits: null, state: 'unknown' });
    expect(stageHistoryDetails(stage)).toBe('Flexible share\nActual applied: Not recorded\nElapsed selected (including offline): Not recorded\nVisits: Not recorded\nState: Not recorded');
  });

  it('preserves genuinely recorded zero counters on skipped stages and clears stale history when disconnected', () => {
    const data = snapshot();
    Object.assign((data.stages as JsonObject[])[0], { timingKind: 'target', state: 'skipped', appliedSeconds: 0, elapsedSeconds: 0, visits: 0 });
    expect(stageHistoryDetails(parsePlanProgress(data, true).stages[0])).toBe('AT targets / Mandatory\nActual applied: 0s\nElapsed selected (including offline): 0s\nVisits: 0\nState: Skipped');
    expect(parsePlanProgress(data, false).stages).toEqual([]);
    expect(stageHistoryDetails(null, 'fixed')).toContain('Actual applied: Not recorded');
  });
});

describe('mandatory runtime work', () => {
  function mandatorySnapshot(patch: JsonObject = {}): JsonObject {
    const data = snapshot({ protectedWorkComplete: false, protectedSecondsRemaining: 180, timingReason: 'Fixed tail reserved.', scheduleComplete: false, ...patch });
    (data.stages as JsonObject[]).forEach((row, i) => Object.assign(row, {
      timingKind: i === 0 ? 'target' : i === 2 ? 'fixed' : 'flexible',
      configuredDurationSeconds: i === 2 ? 300 : 0, flexWeight: 35,
      completionMode: i === 0 ? 1 : 0, durationSeconds: i === 0 ? 0 : i === 2 ? 300 : 0,
      ...(i === 1 && Number(data.activeStageIndex) > 1 ? { state: 'skipped', appliedSeconds: 0, elapsedSeconds: 0, visits: 0 } : {}),
      ...(i === 0 ? { targetAdvancedTrainingPower: '6000', targetAdvancedTrainingToughness: '6000' } : {})
    }));
    return data;
  }

  it('reports credited elapsed and dynamic duration metadata by default', () => {
    const data = mandatorySnapshot({ stageElapsedSeconds: 75 });
    const before = stringifyJson(data);
    const view = parsePlanProgress(data, true);
    expect(view).toMatchObject({ showWaitingPlanProgress: false, schedulerMode: 'stages', activeStageIndex: 2, stageElapsedSeconds: 75, protectedWorkComplete: false, protectedSecondsRemaining: 180, timingReason: 'Fixed tail reserved.', scheduleComplete: false });
    expect(view.stages[1]).toMatchObject({ timingKind: 'flexible', durationSeconds: 0, configuredDurationSeconds: 0, flexWeight: 35, state: 'skipped' });
    expect(view.stages[2]).toMatchObject({ timingKind: 'fixed', configuredDurationSeconds: 300, state: 'current' });
    expect(view.stages[0]).toMatchObject({ timingKind: 'target', completionMode: 'AT targets' });
    expect(stringifyJson(data)).toBe(before);
  });

  it('never claims earlier flexible stages were trained based on position alone', () => {
    const { body } = render(PlanProgress, { props: { data: mandatorySnapshot(), connected: true } });
    expect(body).toContain('Skipped');
    expect(body).toContain('State: Skipped');
    expect(body).toContain('Fixed duration');
    expect(body).toContain('Flexible share');
    expect(body).toContain('AT targets / Mandatory');
    expect(body).not.toContain('Auto-rebirth blocked');
    expect(body).not.toContain('AT targets must also finish');
    expect(body).not.toContain('Fixed tail reserved.');
    expect(body).toContain('1m 15s credited in stage');
  });

  it('distinguishes schedule completion and sustaining from 100% current-stage progress', () => {
    const data = mandatorySnapshot({ stageProgress: 1, protectedWorkComplete: true });
    expect(parsePlanProgress(data, true).scheduleComplete).toBe(false);
    expect(render(PlanProgress, { props: { data, connected: true } }).body).not.toContain('Sustaining:');
    data.scheduleComplete = true;
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    expect(body).not.toContain('sustaining the reported allocation');
    expect(body).toContain('Sustaining: 3. Time Machine');
    expect(body).not.toContain('Mandatory work complete.');
    expect(body).not.toContain('Auto-rebirth blocked');
  });

  it('accepts an explicitly complete schedule without inventing a runtime stage position', () => {
    const data = mandatorySnapshot({ activeStageIndex: -1, scheduleComplete: true });
    expect(parsePlanProgress(data, true)).toMatchObject({ showWaitingPlanProgress: false, activeStageIndex: null, stageProgress: null, scheduleComplete: true });
    expect(parsePlanProgress(data, true).stages.every(stage => stage.state === 'pending')).toBe(true);
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    expect(body).toContain('Schedule complete</span>');
    expect(body).not.toContain('role="progressbar"');
    delete data.scheduleComplete;
    expect(parsePlanProgress(data, true).showWaitingPlanProgress).toBe(true);
  });

  it.each(['completed', 'skipped'])('retains a finished holding index with explicit %s history but no current marker', state => {
    const data = snapshot({ activeStageIndex: 7, scheduleComplete: true, stageProgress: null });
    (data.stages as JsonObject[])[7].state = state;
    const view = parsePlanProgress(data, true);
    expect(view).toMatchObject({ activeStageIndex: 7, scheduleComplete: true, stageProgress: null });
    expect(view.stages[7].state).toBe(state);
    expect(view.stages.filter(row => row.state === 'current')).toEqual([]);
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    expect(body).toContain('Sustaining: 8. Time Machine');
    expect(body).not.toContain('aria-current="step"');
  });

  it.each([
    ['completed', false], ['skipped', false], ['pending', true], ['unknown', true]
  ])('does not expose a holding index for state %s and scheduleComplete=%s', (state, scheduleComplete) => {
    const data = snapshot({ activeStageIndex: 7, scheduleComplete });
    (data.stages as JsonObject[])[7].state = state;
    expect(parsePlanProgress(data, true).activeStageIndex).toBeNull();
  });

  it('does not coerce missing or invalid optional credit fields', () => {
    const data = mandatorySnapshot({ protectedWorkComplete: 'false', protectedSecondsRemaining: -1, timingReason: {}, scheduleComplete: 'true' });
    (data.stages as JsonObject[])[2].timingKind = 'protected';
    expect(parsePlanProgress(data, true)).toMatchObject({ activeStageIndex: 2, protectedWorkComplete: null, protectedSecondsRemaining: null, timingReason: '', scheduleComplete: null });
    expect(parsePlanProgress(data, true).stages[2].timingKind).toBeNull();
    expect(parsePlanProgress(data, false)).toMatchObject({ showWaitingPlanProgress: true, protectedWorkComplete: null, protectedSecondsRemaining: null, timingReason: '', scheduleComplete: null });
  });

  it('keeps paused credited time and flexible wall time distinct', () => {
    const data = mandatorySnapshot({ status: 'paused', activeStageIndex: 1, stageElapsedSeconds: 42 });
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    expect(body).not.toContain('Fixed credited time is paused; flexible timing uses wall time.');
    expect(body).toContain('42s wall time in stage');
    expect(body).not.toContain('Running');
  });
});

describe('plan progression component', () => {
  it.each<JsonObject>([
    { protectedWorkComplete: false, protectedSecondsRemaining: 0, timingReason: 'Waiting for mandatory AT targets' },
    { protectedWorkComplete: false, protectedSecondsRemaining: 180, timingReason: 'Restored saved progress; offline time is not protected training time.' },
    { protectedWorkComplete: true, scheduleComplete: true, timingReason: 'Schedule complete.' }
  ])('omits routine telemetry entirely rather than relocating it to titles: %j', patch => {
    const data = snapshot({ ...patch, reason: patch.timingReason ?? '', lastStageOverride: 'Manual override at 2026-10-15T12:34:56Z', lastStageOverrideAt: 123456 });
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    for (const noise of [patch.timingReason, 'Auto-rebirth blocked', 'fixed time remaining', 'AT targets must also finish', 'Mandatory work complete.', 'This does not mean skipped', 'Last manual action', '2026-10-15T12:34:56Z', '123456']) {
      expect(body).not.toContain(noise);
    }
    expect(body).not.toMatch(/class="reason\b/);
    expect(body.match(/<details class="history\b/g)).toHaveLength(8);
    expect(body).not.toMatch(/<details[^>]*\bopen\b/);
  });

  it.each([300, 600, 0, null])('shows fixed configured duration only when known and different: %s', configuredDurationSeconds => {
    const data = snapshot();
    (data.stages as JsonObject[])[2].configuredDurationSeconds = configuredDurationSeconds;
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    expect(body.match(/class="duration\b/g)).toHaveLength(8);
    if (configuredDurationSeconds !== null && configuredDurationSeconds !== 300) {
      expect(body).toContain(` / configured ${planProgressDuration(configuredDurationSeconds)}`);
    } else {
      expect(body).not.toContain('configured ');
    }
  });

  it.each([35, null])('keeps flexible duration estimates compact with weight %s in the title', flexWeight => {
    const data = snapshot();
    Object.assign((data.stages as JsonObject[])[2], { timingKind: 'flexible', durationSeconds: 120, flexWeight });
    const { body } = render(PlanProgress, { props: { data, connected: true } });
    expect(body).toContain(`title="Assigned estimate${flexWeight !== null ? ` / weight ${flexWeight}` : ''}">2m est.`);
    expect(body.match(/class="duration\b/g)).toHaveLength(8);
    expect(body).not.toContain('configured ');
  });

  it('renders all eight stages, allocations, loop boundary and an accessible current indicator', () => {
    const { body } = render(PlanProgress, { props: { data: snapshot(), connected: true } });
    expect(body.match(/class="stage-name/g)).toHaveLength(8);
    expect(body.match(/aria-current="step"/g)).toHaveLength(1);
    expect(body).toContain('Completed');
    expect(body).toContain('Pending / Loop start');
    expect(body).toContain('Repeat from 6');
    expect(body).toContain('5m');
    expect(body).toContain('Time Machine / TM');
    expect(body).toContain('Gear: Time Machine');
    expect(body).not.toContain('Part 3');
    expect(body).toMatch(/role="progressbar"[^>]*aria-valuenow="25"[^>]*aria-valuetext="Running: Time Machine; 25% complete"/);
  });

  it('displays effective custom allocations and keep-gear without exposing the legacy stage name', () => {
    const data = snapshot();
    const rows = data.stages as JsonObject[];
    rows[2] = { ...rows[2], name: 'Wandoos', magicFocus: 4, gearGoal: -1, magicPriorities: ['RIT-0'] };
    const { body } = render(PlanProgress, { props: { data, connected: true, onedit: () => {} } });
    expect(parsePlanProgress(data, true).stages[2]).toMatchObject({ name: 'Custom', energy: 'Time Machine / TM', magic: 'Blood Magic / RIT-0', gear: 'Keep gear' });
    expect(body).toContain('Blood Magic / RIT-0');
    expect(body).toContain('Gear: Keep gear');
    expect(body).toContain('aria-label="Edit stage 3: Custom"');
    expect(body).toContain('aria-valuetext="Running: Custom; 25% complete"');
    expect(body).not.toContain('Wandoos');
  });

  it.each([75, null])('keeps unknown progress accessible without a duplicate visible explanation (elapsed %s)', stageElapsedSeconds => {
    const { body } = render(PlanProgress, { props: { data: snapshot({ stageProgress: null, stageElapsedSeconds }), connected: true } });
    expect(body).toMatch(/<ol\b[^>]*aria-label="Active runtime plan stages"/);
    expect(body).toContain('role="progressbar"');
    expect(body).toContain('aria-label="Stage 3 progress"');
    expect(body).not.toContain('aria-valuenow');
    expect(body).toContain('aria-valuetext="Running: Time Machine; progress not reported"');
    expect(body.match(/progress not reported/gi)).toHaveLength(1);
    expect(body).not.toContain('> / ');
  });

  it.each(['paused', 'interrupted'])('labels %s actual state rather than presenting it as running', status => {
    const { body } = render(PlanProgress, { props: { data: snapshot({ status, reason: 'Quick override active', timingReason: 'Restored saved progress; offline time is not protected training time.' }), connected: true } });
    expect(body).toContain(status === 'paused' ? 'Paused: 3. Time Machine' : 'Interrupted: 3. Time Machine');
    expect(body).toContain('Quick override active');
    expect(body).not.toContain('Restored saved progress');
    expect(body).not.toContain('Running');
    expect(body).not.toMatch(/<li\b[^>]*class="[^"]*\bcurrent\b/);
  });

  it('does not expose stale current markers or editor links after disconnect', () => {
    const { body } = render(PlanProgress, { props: { data: snapshot(), connected: false, onedit: () => {} } });
    expect(body).toContain('Waiting for plan progress');
    expect(body).not.toContain('aria-current');
    expect(body).not.toContain('role="progressbar"');
    expect(body).not.toContain('Active eight-part plan');
    expect(body).not.toContain('<button');
  });

  it.each<JsonObject>([
    { planVersion: 14 }, { planVersion: 15 }, { planVersion: 16 }, { planVersion: 18 },
    { schedulerMode: 'time' }, { schedulerMode: 'sequential' }, { schedulerMode: 'protected' }
  ])('rejects unsupported telemetry without current highlighting: %j', patch => {
    const data = snapshot(patch);
    expect(parsePlanProgress(data, true).showWaitingPlanProgress).toBe(true);
    const { body } = render(PlanProgress, { props: { data, connected: true, onedit: () => {} } });
    expect(body).toContain('Waiting for plan progress');
    expect(body).not.toContain('aria-current');
    expect(body).not.toContain('role="progressbar"');
    expect(body).not.toContain('<button');
  });

  it('only offers stage editor navigation when the callback is provided', () => {
    const props = { data: snapshot(), connected: true };
    expect(render(PlanProgress, { props }).body).not.toContain('<button');
    const { body } = render(PlanProgress, { props: { ...props, onedit: () => {} } });
    expect(body.match(/aria-label="Edit stage /g)).toHaveLength(8);
    expect(body).toContain('aria-label="Edit stage 3: Time Machine"');
    expect(body).toContain('11m 15s into this rebirth');
  });
});
