import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonObject, type NguiState } from '../src/lib/api';
import { heartbeat } from '../src/lib/model';
import { parsePlanProgress, planProgressDuration } from '../src/lib/plan-progress';
import { stagePresets } from '../src/lib/stage-presets';
import PlanProgress from '../src/lib/PlanProgress.svelte';

function snapshot(patch: JsonObject = {}): JsonObject {
  return {
    available: true, status: 'running', planName: 'Active eight-part plan', planVersion: 14,
    schedulerMode: 'time', activeStageIndex: 2, stageProgress: .25,
    elapsedSeconds: 675, stageElapsedSeconds: 75, cycle: 0, repeating: true, loopStartIndex: 5,
    stages: Array.from({ length: 8 }, (_, index) => ({
      index, name: `Part ${index + 1}`, durationSeconds: 300, completionMode: 0,
      energyFocus: 1, magicFocus: 1, gearGoal: 5,
      configuredEnergyPriorities: [], configuredMagicPriorities: [], energyPriorities: ['TM'], magicPriorities: ['TM']
    })),
    ...patch
  };
}

describe('runtime plan progression parser', () => {
  it('projects the actual runtime position with compact allocations', () => {
    const view = parsePlanProgress(snapshot(), true);
    expect(view.showWaitingPlanProgress).toBe(false);
    expect(view.planName).toBe('Active eight-part plan');
    expect(view.planVersion).toBe('14');
    expect(view.activeStageIndex).toBe(2);
    expect(view.stageProgress).toBe(.25);
    expect(view.stages.map(stage => stage.state)).toEqual(['completed', 'completed', 'current', 'future', 'future', 'future', 'future', 'future']);
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
    expect(view.stages[3].state).toBe('future');
    expect(view.reason).toBe('Quick: Time Machine overrides stage allocations.');
  });

  it.each(['paused', 'interrupted'])('does not invent a %s position when the runtime reports none', status => {
    const view = parsePlanProgress(snapshot({ status, activeStageIndex: -1 }), true);
    expect(view.showWaitingPlanProgress).toBe(false);
    expect(view.activeStageIndex).toBeNull();
    expect(view.stageProgress).toBeNull();
    expect(view.stages.map(stage => stage.state)).toEqual(Array(8).fill('unknown'));
  });

  it('does not advance a sequential AT stage based on elapsed time', () => {
    const data = snapshot({ schedulerMode: 'sequential', activeStageIndex: 0, stageProgress: null, elapsedSeconds: 999999, stageElapsedSeconds: 999999 });
    (data.stages as JsonObject[])[0] = { ...(data.stages as JsonObject[])[0], completionMode: 1, targetAdvancedTrainingPower: '9223372036854775806', targetAdvancedTrainingToughness: '6000' };
    const view = parsePlanProgress(data, true);
    expect(view.schedulerMode).toBe('sequential');
    expect(view.activeStageIndex).toBe(0);
    expect(view.stageProgress).toBeNull();
    expect(view.stages[0]).toMatchObject({ state: 'current', completionMode: 'AT targets', targets: 'P 9223372036854775806 / T 6000' });
    expect(view.stages.slice(1).map(stage => stage.state)).toEqual(Array(7).fill('future'));
  });

  it('uses actual sequential progress when supplied, never a duration ratio', () => {
    const view = parsePlanProgress(snapshot({ schedulerMode: 'sequential', stageProgress: .6, stageElapsedSeconds: 999999 }), true);
    expect(view.stageProgress).toBe(.6);
    expect(view.activeStageIndex).toBe(2);
  });

  it('leaves a 100% stage current until the backend changes the position', () => {
    const view = parsePlanProgress(snapshot({ stageProgress: 1, stageElapsedSeconds: 300 }), true);
    expect(view.stages[2].state).toBe('current');
    expect(view.stages[3].state).toBe('future');
  });

  it('retains completed intro stages and resets the loop tail on later cycles', () => {
    const view = parsePlanProgress(snapshot({ activeStageIndex: 5, stageProgress: 0, cycle: 4 }), true);
    expect(view.cycle).toBe(4);
    expect(view.loopStartIndex).toBe(5);
    expect(view.stages.map(stage => stage.state)).toEqual(['completed', 'completed', 'completed', 'completed', 'completed', 'current', 'future', 'future']);
    expect(view.stages.filter(stage => stage.loopStart).map(stage => stage.index)).toEqual([5]);
  });

  it('supports a loop at the first stage without keeping previous-cycle completion', () => {
    const view = parsePlanProgress(snapshot({ activeStageIndex: 0, loopStartIndex: 0, cycle: 10 }), true);
    expect(view.stages.map(stage => stage.state)).toEqual(['current', 'future', 'future', 'future', 'future', 'future', 'future', 'future']);
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
    (data.stages as JsonObject[])[2] = { index: 2, name: '', durationSeconds: -1, energyFocus: {}, magicPriorities: [null, 'RIT-0'], gearGoal: 99 };
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

describe('plan progression component', () => {
  it('renders all eight stages, allocations, loop boundary and an accessible current indicator', () => {
    const { body } = render(PlanProgress, { props: { data: snapshot(), connected: true } });
    expect(body.match(/class="stage-name/g)).toHaveLength(8);
    expect(body.match(/aria-current="step"/g)).toHaveLength(1);
    expect(body).toContain('Completed');
    expect(body).toContain('Upcoming / Loop start');
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

  it('omits determinate progress for sequential stages whose progress is unknown', () => {
    const { body } = render(PlanProgress, { props: { data: snapshot({ schedulerMode: 'sequential', stageProgress: null }), connected: true } });
    expect(body).toContain('Sequential');
    expect(body).toContain('role="progressbar"');
    expect(body).not.toContain('aria-valuenow');
    expect(body).toContain('aria-valuetext="Running: Time Machine; progress not reported"');
  });

  it.each(['paused', 'interrupted'])('labels %s actual state rather than presenting it as running', status => {
    const { body } = render(PlanProgress, { props: { data: snapshot({ status, reason: 'Quick override active' }), connected: true } });
    expect(body).toContain(status === 'paused' ? 'Paused: 3. Time Machine' : 'Interrupted: 3. Time Machine');
    expect(body).toContain('Quick override active');
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

  it('only offers stage editor navigation when the callback is provided', () => {
    const props = { data: snapshot(), connected: true };
    expect(render(PlanProgress, { props }).body).not.toContain('<button');
    const { body } = render(PlanProgress, { props: { ...props, onedit: () => {} } });
    expect(body.match(/aria-label="Edit stage /g)).toHaveLength(8);
    expect(body).toContain('aria-label="Edit stage 3: Time Machine"');
    expect(body).toContain('11m 15s into this rebirth');
  });
});
