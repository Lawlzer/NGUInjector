import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import PlanEditor from '../src/lib/PlanEditor.svelte';
import PlanGear from '../src/lib/PlanGear.svelte';
import StageEditor from '../src/lib/StageEditor.svelte';
import AugmentTools from '../src/lib/AugmentTools.svelte';
import JsonEditor from '../src/lib/JsonEditor.svelte';
import { applyStagePreset } from '../src/lib/stage-presets';

const plan: JsonObject = {
  Name: 'test', Version: 17, __externalUiRevision: 'a'.repeat(64),
  StageDurationSeconds: 1800,
  GoalPresets: [{ Name: 'Default', GearGoal: 9 }], ActiveGoalPresetIndex: 0, GearGoal: 9,
  Stages: [{ Name: 'Training', Percent: 100, CompletionMode: 0, TargetAdvancedTrainingPower: '9223372036854775806', TargetAdvancedTrainingToughness: '6000', EnergyPriorities: ['AT'], MagicPriorities: ['RIT'], BlackBarLookaheadSeconds: 5 }]
};

function renderPlan(value = plan): string {
  return render(PlanEditor, { props: { plan: value, name: 'test', dirty: false, onchange: () => {} } }).body;
}

const augmentProps = { canPreview: true, canAllocate: true, busy: false, savedEstimate: 300, estimatePending: false, result: null, oncommand: async () => {} };

describe('plan editing display', () => {
  it('omits redundant headings and instructions but identifies a different selected plan', () => {
    const props = { plan, name: 'test', dirty: false, onchange: () => {} };
    const { body } = render(PlanEditor, { props: { ...props, activePlanName: 'test' } });
    expect(body).not.toContain('Editing plan');
    expect(body).not.toContain('Active plan:');
    expect(body).not.toContain('Select a section');
    expect(body).not.toContain('Click a section');
    expect(body).not.toMatch(/<h[23][^>]*>test<\/h[23]>/);
    expect(body.match(/<h3[^>]*>Stages<\/h3>/g)).toHaveLength(1);
    expect(render(PlanEditor, { props: { ...props, activePlanName: 'other' } }).body).toContain('Editing test; other is active.');
  });
  it.each(['fixed', 'flexible', 'target'] as const)('shows one timing selector and the appropriate inputs for %s stages', kind => {
    const source = { ...plan, Stages: [{ ...(plan.Stages as JsonObject[])[0], CompletionMode: kind === 'target' ? 1 : 0, DurationSeconds: kind === 'fixed' ? 300 : 0, DurationUnit: kind === 'fixed' ? 's' : '%' }] };
    const before = stringifyJson(source);
    const body = renderPlan(source);
    const selectors = body.match(/<label>Finish stage by<select[^>]*>[\s\S]*?<\/select>/g);
    expect(selectors).toHaveLength(1);
    expect(selectors![0].match(/<option value="[^"]*"[^>]*>[^<]*<\/option>/g)).toEqual([
      `<option value="fixed"${kind === 'fixed' ? ' selected=""' : ''}>Time</option>`,
      `<option value="flexible"${kind === 'flexible' ? ' selected=""' : ''}>% of remaining</option>`,
      `<option value="target"${kind === 'target' ? ' selected=""' : ''}>Advanced training targets</option>`
    ]);
    if (kind === 'target') {
      expect(body).toMatch(/aria-label="AT power target"[^>]*value="9223372036854775806"/);
      expect(body).toMatch(/aria-label="AT toughness target"[^>]*value="6000"/);
    } else {
      expect(body).not.toContain('aria-label="AT power target"');
      expect(body).not.toContain('aria-label="AT toughness target"');
    }
    if (kind === 'fixed') expect(body).toMatch(/aria-label="Duration"[^>]*value="5m"/);
    else expect(body).not.toContain('aria-label="Duration"');
    if (kind === 'flexible') {
      expect(body).toMatch(/aria-label="Flexible weight"[^>]*value="100"/);
      expect(body).toContain('100% of remaining time');
    } else expect(body).not.toContain('aria-label="Flexible weight"');
    expect(body).not.toContain('role="alert"');
    expect(stringifyJson(source)).toBe(before);
  });
  it('preserves lossless numeric targets and unknown fields in editable advanced JSON', () => {
    const source = parseJson(stringifyJson(plan).replace('"9223372036854775806"', '9223372036854775806')) as JsonObject;
    source.Future = { counter: parseJson('9007199254740993') };
    const before = stringifyJson(source);
    const { body } = render(JsonEditor, { props: { title: 'Plan', value: source, validate: () => {}, onapply: () => {}, ondirty: () => {} } });
    const textarea = body.match(/<textarea\b([^>]*)>([\s\S]*?)<\/textarea>/);
    expect(textarea).not.toBeNull();
    expect(textarea![1]).toContain('aria-label="Advanced Plan JSON"');
    expect(textarea![1]).not.toMatch(/\b(?:readonly|disabled)\b/);
    expect(textarea![1]).toContain('title="Keep all existing fields."');
    expect(body).not.toMatch(/autosave|save automatically|storage|polling/i);
    expect(body).not.toContain('Apply edits');
    expect(body).toContain('Discard edits');
    expect(textarea![2]).toContain('"TargetAdvancedTrainingPower": 9223372036854775806');
    expect(textarea![2]).toContain('"counter": 9007199254740993');
    expect(stringifyJson(parseJson(textarea![2]))).toBe(before);
    expect(stringifyJson(source)).toBe(before);
  });
  it('replaces plan-local gear preset CRUD with a global active reference without mutating legacy data', () => {
    const source = { ...plan, GoalPresets: [{ Name: 'Default', GearGoal: 9 }, { Name: 'NGU', GearGoal: 2 }] };
    const before = stringifyJson(source);
    const body = render(PlanGear, { props: { plan: source, presetLibrary: null, onchange: () => {} } }).body;
    expect(body).toContain('aria-label="Active preset"');
    expect(body).toContain('Fight scoring');
    const activePreset = body.match(/<select\b[^>]*aria-label="Active preset"[^>]*>([\s\S]*?)<\/select>/)?.[1];
    expect(activePreset).toMatch(/<option value="" selected="">Existing settings<\/option>/);
    expect(body).not.toContain('Remove preset');
    expect(body).not.toContain('Preset 1 name');
    expect(stringifyJson(source)).toBe(before);
  });
  it('starts with collapsed bar segments and all settings mounted in hidden panels', () => {
    const body = renderPlan();
    expect(body).toMatch(/<ol[^>]*class="stage-bar[^"]*"[^>]*aria-label="Plan stages"/);
    expect(body).toMatch(/<li[^>]*class="bar-segment[^"]*"[^>]*data-stage="\d+"/);
    expect(body).toMatch(/<button[^>]*aria-label="Edit stage 1: Custom"[^>]*aria-expanded="false"[^>]*aria-controls="stage-panel-\d+"/);
    expect(body).not.toContain('aria-expanded="true"');
    const panel = body.match(/<section[^>]*class="stage-panel[^"]*"[^>]*aria-label="Stage 1 settings"[^>]*hidden[^>]*>[\s\S]*?<\/section>/)?.[0];
    expect(panel).toBeTypeOf('string');
    for (const label of ['Focus preset', 'Flexible weight', 'Energy focus', 'Energy priorities', 'Magic focus', 'Magic priorities', 'Fight on entry']) {
      expect(panel).toContain(`aria-label="${label}"`);
    }
    expect(panel).toContain('Finish stage by');
    expect(panel).not.toContain('aria-label="Duration"');
    expect(panel).not.toContain('aria-label="Gear goal"');
    expect(body).not.toContain('Black-bar lookahead');
    expect(body).not.toContain('Cap buffer');
    expect(panel).toContain('aria-label="Close stage settings"');
    expect(body).not.toContain('Move earlier');
    expect(body).not.toContain('Move later');
    expect(body).not.toContain('Reorder stage');
    expect(body).not.toMatch(/class="handle\b|data-handle=/);
    const segment = body.match(/<button[^>]*class="stage-select[^>]*>/)?.[0];
    expect(segment).toContain('draggable="false"');
    expect(segment).toMatch(/data-select="\d+"/);
    for (const shortcut of ['Alt+ArrowLeft', 'Alt+ArrowRight', 'Alt+ArrowUp', 'Alt+ArrowDown']) expect(segment).toContain(shortcut);
    expect(panel).toContain('Repeat from here');
    expect(panel).toMatch(/<details class="stage-history[^>]*>\s*<summary[^>]*>Runtime history<\/summary>/);
    expect(panel).not.toMatch(/<details[^>]*\bopen\b/);
    expect(body).not.toContain('Stage 1 controls');
    expect(body).not.toMatch(/<table\b|<tr\b|class="(?:stage-table|stage-row)\b/);
  });
  it('keeps schedule and templates collapsed, omits history and exposes rebirth conditions', () => {
    const body = renderPlan();
    for (const summary of ['Schedule &amp; templates']) {
      const details = body.match(new RegExp(`<details([^>]*)>\\s*<summary[^>]*>${summary}</summary>`));
      expect(details, summary).not.toBeNull();
      expect(details![1]).not.toMatch(/\bopen\b/);
    }
    const scheduleStart = body.indexOf('<details class="schedule-settings');
    const schedule = body.slice(scheduleStart, body.indexOf('</details>', scheduleStart));
    expect(schedule).toContain('aria-label="Fallback time budget"');
    expect(schedule).toContain('Enable stages');
    expect(schedule).toContain('Repeat from');
    expect(schedule).toContain('Templates');
    expect(schedule).toContain('30m weighted cycle');
    expect(schedule).toContain('AT targets 6k/6k then active preset');
    expect(body).toMatch(/<article[^>]*aria-label="Rebirth conditions"/);
    expect(body).toContain('Allow auto-rebirth');
    expect(body).toContain('aria-label="Match conditions"');
    expect(body).toContain('Any condition');
    expect(body).toContain('All conditions');
    expect(body).not.toContain('>Rebirth settings</summary>');
    expect(body).not.toContain('aria-label="Active preset"');
    expect(body).not.toContain('Fight scoring');
    expect(body).not.toContain('Rebirth history');
  });
  it('keeps explicitly disabled rebirth conditions false without mounting their inputs', () => {
    const source = { ...plan, AutoRebirthEnabled: false, TimeTriggerEnabled: false, TimeTriggerSeconds: 7200, NumberTriggerEnabled: false, NumberMultiplierTarget: 25, NumberClimbEnabled: false, NumberClimbMinSeconds: 1800, NumberClimbMultiplierTarget: 3, PrepRequireMaxedFruit: false, TriggerMode: 1 };
    const before = stringifyJson(source);
    const body = renderPlan(source);
    for (const label of ['After a set time', 'When NUMBER grows']) expect(body).toContain(label);
    expect(body).not.toContain('When NUMBER grows after a minimum time');
    expect(body).not.toContain('Require maxed prep fruit');
    for (const label of ['Rebirth after', 'NUMBER gain (x)', 'Minimum rebirth age', 'NUMBER gain after minimum age (x)']) expect(body).not.toContain(`aria-label="${label}"`);
    expect(body).not.toMatch(/<input[^>]*type="checkbox"[^>]*checked/);
    expect(stringifyJson(source)).toBe(before);
  });
  it('formats enabled rebirth durations without changing saved seconds or unknown fields', () => {
    const source = { ...plan, TimeTriggerEnabled: true, TimeTriggerSeconds: 7200, NumberTriggerEnabled: true, NumberMultiplierTarget: 25, NumberClimbEnabled: true, NumberClimbMinSeconds: 1800, NumberClimbMultiplierTarget: 3, Future: 'keep' };
    const before = stringifyJson(source);
    const body = renderPlan(source);
    expect(body).toMatch(/aria-label="Rebirth after"[^>]*value="2h"/);
    expect(body).not.toContain('aria-label="Minimum rebirth age"');
    expect(body).toContain('aria-label="NUMBER gain (x)"');
    expect(body).not.toContain('aria-label="NUMBER gain after minimum age (x)"');
    expect(body).not.toContain('NUMBER gain compares');
    expect(body).not.toContain('Automatic rebirth waits for required stage work');
    expect(stringifyJson(source)).toBe(before);
  });
  it('warns when only the retired condition is enabled without changing legacy preferences', () => {
    const source = { ...plan, TimeTriggerEnabled: false, NumberTriggerEnabled: false, NumberClimbEnabled: true, PrepRequireMaxedFruit: true };
    const before = stringifyJson(source);
    const body = renderPlan(source);
    expect(body).toContain('Choose at least one condition to rebirth automatically.');
    expect(body).not.toContain('NUMBER gain compares');
    expect(body).not.toContain('Require maxed prep fruit');
    expect(stringifyJson(source)).toBe(before);
  });
  it('renders time textboxes with inline suffixes and no stage name or duration unit inputs', () => {
    const body = renderPlan({ ...plan, StageDurationSeconds: 10800, Stages: [1500, 2100, 7200].map(DurationSeconds => ({ Name: 'Saved name', Percent: 1, DurationSeconds, DurationUnit: 'min', CompletionMode: 0 })) });
    expect(body.match(/aria-label="Duration"[^>]*type="text"[^>]*value="[^"]*"/g)).toEqual([
      expect.stringContaining('value="25m"'), expect.stringContaining('value="35m"'), expect.stringContaining('value="2h"')
    ]);
    expect(body).not.toContain('aria-label="Stage timing"');
    expect(body).not.toContain('Choose timing per stage');
    expect(body.match(/<label>Finish stage by<select/g)).toHaveLength(3);
    expect(body.match(/<option value="fixed" selected="">Time<\/option>/g)).toHaveLength(3);
    expect(body.match(/<option value="flexible">% of remaining<\/option>/g)).toHaveLength(3);
    expect(body.match(/<option value="target">Advanced training targets<\/option>/g)).toHaveLength(3);
    expect(body).not.toContain('aria-label="Stage name"');
    expect(body).not.toContain('aria-label="Duration unit"');
    expect(body).not.toContain('aria-label="Schedule budget"');
    expect(body.match(/class="segment-duration\b[^"]*"[^>]*>[\s\S]*?<\/span>/g)).toEqual([
      expect.stringContaining('25m fixed'), expect.stringContaining('35m fixed'), expect.stringContaining('2h fixed')
    ]);
    expect(body).not.toContain('No time for flexible stages.');
    expect(body.match(/aria-label="Edit stage \d: Custom"[^>]*aria-expanded="false"/g)).toHaveLength(3);
    expect(body.match(/<section[^>]*aria-label="Stage \d settings"[^>]*hidden/g)).toHaveLength(3);
  });
  it('disables direct stage dragging when editing is disabled', () => {
    const { body } = render(StageEditor, { props: { plan, disabled: true, onchange: () => {} } });
    const segment = body.match(/<button[^>]*class="stage-select[^>]*>/)?.[0];
    expect(segment).toContain('draggable="false"');
  });
  it('keeps custom gear and cap out of normal controls without mutating the stored stage', () => {
    const custom = { ...plan, Stages: [{ ...(plan.Stages as JsonObject[])[0], GearGoal: 9, BlackBarLookaheadSeconds: 37, FightOnEntry: true }] };
    const before = stringifyJson(custom);
    const body = renderPlan(custom);
    const panel = body.match(/<section[^>]*class="stage-panel[^>]*>[\s\S]*?<\/section>/)?.[0];
    expect(panel).not.toContain('Gear goal');
    expect(panel).not.toContain('Black-bar lookahead');
    expect(panel).not.toContain('Cap buffer');
    expect(panel).toMatch(/aria-label="Fight on entry"[^>]*checked/);
    expect(stringifyJson(custom)).toBe(before);
    expect(body).not.toContain('>Gear settings</summary>');
    expect(body).not.toContain('aria-label="Active preset"');
    expect(body).not.toContain('>Gear goal<select');
  });
  it('presets assign their gear while preserving stored cap buffers and FightOnEntry', () => {
    const custom = { ...(plan.Stages as JsonObject[])[0], GearGoal: 9, BlackBarLookaheadSeconds: 37, FightOnEntry: true, Future: 'keep' };
    const before = stringifyJson(custom);
    for (const [index, GearGoal] of [5, 10, 12, 2, 3, 4, 11, 11].entries()) {
      expect(applyStagePreset(custom, index)).toMatchObject({ GearGoal, BlackBarLookaheadSeconds: 37, FightOnEntry: true, Future: 'keep' });
      expect(stringifyJson(custom)).toBe(before);
    }
    const next = applyStagePreset({ GearGoal: 9 }, 3);
    expect(next.GearGoal).toBe(2);
    expect(next).not.toHaveProperty('BlackBarLookaheadSeconds');
    expect(next).not.toHaveProperty('FightOnEntry');
  });
  it('labels segments and repeat choices from base focuses and gear regardless of priorities or saved names', () => {
    const body = renderPlan({ ...plan, Stages: [
      { Name: 'Time Machine', Percent: 1, EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['TM'] },
      { Name: 'Adventure Stats', Percent: 1, EnergyFocus: 7, MagicFocus: 4, GearGoal: 11 },
      { Name: 'Sustain', Percent: 1, EnergyFocus: 5, MagicFocus: 6, GearGoal: 2 },
      { Name: 'Time Machine', Percent: 1, EnergyFocus: 1, MagicFocus: 1, GearGoal: -1 }
    ] });
    for (const [index, label] of ['Time Machine', 'AT Adventure', 'Energy NGU', 'Custom'].entries()) {
      expect(body).toContain(`aria-label="Edit stage ${index + 1}: ${label}" aria-expanded="false"`);
      expect(body).toContain(`>${index + 1}: ${label}</option>`);
    }
    expect(body).not.toContain('>AT Wandoos</option>');
    expect(body).toContain('Use plan active preset');
    expect(body).not.toContain('>Adventure Stats</option>');
    expect(body).not.toContain('Selecting a preset resets priority orders.');
  });
  it('keeps saved rebirth history out of the plan editor without changing it', () => {
    const source = { ...plan, RebirthHistory: [{ TimestampLocal: '2026-10-14 12:30:00', RebirthSeconds: 3661, Future: 'retained' }] };
    const before = stringifyJson(source);
    const body = renderPlan(source);
    expect(body).not.toContain('Rebirth history');
    expect(body).not.toContain('2026-10-14 12:30:00');
    expect(stringifyJson(source)).toBe(before);
  });

  it('integrates estimated flexible time into stage segments without budget badges', () => {
    const source = { ...plan, TimeTriggerEnabled: true, TimeTriggerSeconds: 86400, Stages: [
      { DurationSeconds: 7200, Percent: 100 },
      { DurationSeconds: 0, Percent: 1 },
      { DurationSeconds: 0, Percent: 3 }
    ] };
    const before = stringifyJson(source);
    const body = renderPlan(source);
    expect(body).not.toContain('aria-label="Schedule budget"');
    expect(body.match(/class="segment-duration\b[^"]*"[^>]*>[\s\S]*?<\/span>/g)).toEqual([
      expect.stringContaining('2h fixed'), expect.stringContaining('330m flexible'), expect.stringContaining('990m flexible')
    ]);
    expect(body.match(/class="segment-share\b[^"]*"[^>]*>[\s\S]*?<\/span>/g)).toEqual([
      expect.stringContaining('25% of remaining'), expect.stringContaining('75% of remaining')
    ]);
    expect(stringifyJson(source)).toBe(before);
  });

  it('removes raw runtime chatter while keeping manual action safety gates', () => {
    const progress = {
      available: true, status: 'running', planName: 'test', planVersion: 17, schedulerMode: 'stages',
      activeStageIndex: 0, stageProgress: .5, protectedWorkComplete: false, protectedSecondsRemaining: 0,
      reason: 'Waiting for mandatory AT targets', timingReason: 'Restored saved progress; offline time is not protected training time.',
      lastStageOverride: 'skip stage 5 at 21748.141743334127', stages: [{ index: 0, state: 'current' }]
    };
    const { body } = render(StageEditor, { props: { plan, name: 'test', progress, connected: true, stageActionReason: 'Save pending edits first.', onstageaction: async () => {}, onchange: () => {} } });
    for (const noise of ['Auto-rebirth blocked', 'fixed time remaining', 'Waiting for mandatory AT targets', 'Restored saved progress', 'Last manual action', '21748.141743334127', 'Selecting a preset resets']) expect(body).not.toContain(noise);
    expect(body).not.toContain('Skip current stage');
    expect(body).toMatch(/disabled[^>]*title="Save pending edits first\."[^>]*>Restart current stage/);
    expect(body).not.toContain('class="manual-note');
  });

  it('warns about exhausted flexible time only when the schedule has flexible stages', () => {
    const source = { ...plan, StageDurationSeconds: 300, Stages: [
      { CompletionMode: 0, DurationSeconds: 300, DurationUnit: 's', Percent: 1 },
      { CompletionMode: 0, DurationSeconds: 0, DurationUnit: '%', Percent: 1 }
    ] };
    expect(renderPlan(source)).toContain('No time for flexible stages. Increase the budget or shorten fixed stages.');
    expect(renderPlan({ ...source, StageDurationSeconds: 200 })).toContain('Fixed stages exceed budget by 100s. Increase the budget or shorten fixed stages.');
  });
});

describe('augment results', () => {
  it('leads with one-based augment, energy, gold and expected time', () => {
    const { body } = render(AugmentTools, { props: { ...augmentProps, result: { command: 'previewAugments', success: true, message: 'Full optimizer message', data: { augmentIndex: 2, usesUpgrade: true, energyToAugment: 100, energyToUpgrade: 200, projectedGoldCost: 400, expectedSeconds: 300, valuePerSecond: 7 } } } });
    const primary = body.slice(0, body.indexOf('<details>'));
    expect(primary).toContain('Augment 3 + upgrade');
    expect(primary).toContain('<dt>Energy to augment</dt><dd>100</dd>');
    expect(primary).toContain('<dt>Energy to upgrade</dt><dd>200</dd>');
    expect(primary).toContain('<dt>Gold cost</dt><dd>400</dd>');
    expect(primary).toContain('<dt>Expected time (s)</dt><dd>300</dd>');
    expect(primary).not.toContain('Full optimizer message');
    expect(primary).not.toContain('Value per second');
    expect(primary).not.toContain('<pre>');
    expect(body).toContain('<details><summary>Details</summary>');
  });
  it('does not present negative or missing augment indexes as a chosen target', () => {
    const { body } = render(AugmentTools, { props: { ...augmentProps, result: { data: { augmentIndex: -1 } } } });
    expect(body).toContain('<dt>Augment</dt><dd>Not reported');
    expect(body).not.toContain('Augment 0');
  });
  it('keeps command errors visible rather than hiding them in details', () => {
    const { body } = render(AugmentTools, { props: { ...augmentProps, result: { success: false, message: 'No affordable unlocked augment target found.' } } });
    const primary = body.slice(0, body.indexOf('<details>'));
    expect(primary).toContain('role="alert">No affordable unlocked augment target found.');
    expect(primary).not.toContain('<dt>Augment</dt>');
  });
  it('keeps live command availability gates independent', () => {
    const { body } = render(AugmentTools, { props: { ...augmentProps, canAllocate: false, estimatePending: true } });
    expect(body).toMatch(/<button type="button">Preview<\/button>/);
    expect(body).toMatch(/<button[^>]*disabled[^>]*>Allocate<\/button>/);
    expect(body).toContain('Duration changes pending.');
    expect(body).not.toContain('Waiting for duration to save.');
    expect(body).toMatch(/<input type="number"[^>]*min="1"[^>]*max="2147483647"[^>]*step="1"[^>]*value="300"[^>]*disabled/);
  });
});

describe('advanced editing guidance', () => {
  it('identifies read-only plan fields and collapses extra guidance', () => {
    const { body } = render(JsonEditor, { props: { title: 'Plan', value: { ...plan, RebirthHistory: [] }, validate: () => {}, onapply: () => {}, ondirty: () => {}, guidance: 'Additional editing guidance.' } });
    expect(body).toContain('Read-only fields: Name, RebirthHistory, Version, __externalUiRevision.');
    expect(body).toContain('<details><summary>Editing details</summary><p class="muted">Additional editing guidance.</p></details>');
    expect(body).not.toContain('<details open');
    expect(body).toContain('9223372036854775806');
  });
});
