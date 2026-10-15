import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import PlanEditor from '../src/lib/PlanEditor.svelte';
import StageEditor from '../src/lib/StageEditor.svelte';
import AugmentTools from '../src/lib/AugmentTools.svelte';
import JsonEditor from '../src/lib/JsonEditor.svelte';
import { applyStagePreset } from '../src/lib/stage-presets';

const plan: JsonObject = {
  Name: 'test', Version: 14, __externalUiRevision: 'a'.repeat(64),
  GoalPresets: [{ Name: 'Default', GearGoal: 9 }], ActiveGoalPresetIndex: 0, GearGoal: 9,
  Stages: [{ Name: 'Training', Percent: 100, CompletionMode: 0, TargetAdvancedTrainingPower: '9223372036854775806', TargetAdvancedTrainingToughness: '6000', EnergyPriorities: ['AT'], MagicPriorities: ['RIT'], BlackBarLookaheadSeconds: 5 }]
};

function renderPlan(value = plan): string {
  return render(PlanEditor, { props: { plan: value, name: 'test', dirty: false, onchange: () => {} } }).body;
}

const augmentProps = { canPreview: true, canAllocate: true, busy: false, savedEstimate: 300, estimatePending: false, result: null, oncommand: async () => {} };

describe('plan editing display', () => {
  it('only offers AT target inputs for AT completion', () => {
    const timed = renderPlan();
    expect(timed).not.toContain('aria-label="AT power target"');
    expect(timed).toMatch(/<option value="1"[^>]*>AT power\/toughness targets<\/option>/);
    expect(timed).toContain('aria-label="Duration"');
    const body = renderPlan({ ...plan, Stages: [{ ...(plan.Stages as JsonObject[])[0], CompletionMode: 1 }] });
    expect(body).toMatch(/aria-label="AT power target"[^>]*value="9223372036854775806"/);
    expect(body).toMatch(/aria-label="AT toughness target"[^>]*value="6000"/);
    expect(body).not.toContain('aria-label="Duration"');
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
    expect(textarea![2]).toContain('"TargetAdvancedTrainingPower": 9223372036854775806');
    expect(textarea![2]).toContain('"counter": 9007199254740993');
    expect(stringifyJson(parseJson(textarea![2]))).toBe(before);
    expect(stringifyJson(source)).toBe(before);
  });
  it('protects the only gear preset from removal', () => {
    expect(renderPlan()).toMatch(/<button[^>]*aria-label="Remove preset 1"[^>]*disabled[^>]*>Remove preset<\/button>/);
    const body = renderPlan({ ...plan, GoalPresets: [{ Name: 'Default', GearGoal: 9 }, { Name: 'NGU', GearGoal: 2 }] });
    const buttons = body.match(/<button[^>]*aria-label="Remove preset \d"[^>]*>/g);
    expect(buttons).toHaveLength(2);
    expect(buttons?.join('')).not.toContain('disabled');
  });
  it('starts with collapsed bar segments and all settings mounted in hidden panels', () => {
    const body = renderPlan();
    expect(body).toMatch(/<ol[^>]*class="stage-bar[^"]*"[^>]*aria-label="Plan stages"/);
    expect(body).toMatch(/<li[^>]*class="bar-segment[^"]*"[^>]*data-stage="\d+"/);
    expect(body).toMatch(/<button[^>]*aria-label="Edit stage 1: Custom"[^>]*aria-expanded="false"[^>]*aria-controls="stage-panel-\d+"/);
    expect(body).not.toContain('aria-expanded="true"');
    const panel = body.match(/<section[^>]*class="stage-panel[^"]*"[^>]*aria-label="Stage 1 settings"[^>]*hidden[^>]*>[\s\S]*?<\/section>/)?.[0];
    expect(panel).toBeTypeOf('string');
    for (const label of ['Focus preset', 'Duration', 'Energy focus', 'Energy priorities', 'Magic focus', 'Magic priorities', 'Fight on entry']) {
      expect(panel).toContain(`aria-label="${label}"`);
    }
    expect(panel).toContain('Finish stage by');
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
    expect(panel).not.toContain('<details');
    expect(body).not.toContain('Stage 1 controls');
    expect(body).not.toMatch(/<table\b|<tr\b|class="(?:stage-table|stage-row)\b/);
  });
  it('keeps schedule, templates, gear, rebirth and history in collapsed details', () => {
    const body = renderPlan();
    for (const summary of ['Schedule &amp; templates', 'Gear settings', 'Rebirth settings', 'Rebirth history']) {
      const details = body.match(new RegExp(`<details([^>]*)>\\s*<summary[^>]*>${summary}</summary>`));
      expect(details, summary).not.toBeNull();
      expect(details![1]).not.toMatch(/\bopen\b/);
    }
    const schedule = body.slice(body.indexOf('<details class="schedule-settings'), body.indexOf('</details>'));
    expect(schedule).toContain('aria-label="Schedule length"');
    expect(schedule).toContain('Enable stages');
    expect(schedule).toContain('Repeat from');
    expect(schedule).toContain('Templates and timing');
    expect(schedule).toContain('30m NUMBER cycle');
    expect(schedule).toContain('AT Adventure 6k/6k to NGU');
  });
  it('renders time textboxes with inline suffixes and no stage name or duration unit inputs', () => {
    const body = renderPlan({ ...plan, Version: 15, Stages: [1500, 2100, 7200].map(DurationSeconds => ({ Name: 'Legacy name', Percent: 1, DurationSeconds, DurationUnit: 'min' })) });
    expect(body.match(/aria-label="Duration"[^>]*type="text"[^>]*value="[^"]*"/g)).toEqual([
      expect.stringContaining('value="25m"'), expect.stringContaining('value="35m"'), expect.stringContaining('value="2h"')
    ]);
    expect(body).toContain('aria-label="Stage timing"');
    expect(body).toContain('value="time"');
    expect(body).toContain('value="%"');
    expect(body).not.toContain('aria-label="Stage name"');
    expect(body).not.toContain('aria-label="Duration unit"');
    expect(body).toContain('<span>Total time</span><strong>3h</strong>');
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
    expect(body).toContain('>Gear settings</summary>');
    expect(body).toContain('>Gear goal<select');
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
    expect(body).toContain('>AT Wandoos</option>');
    expect(body).not.toContain('>Adventure Stats</option>');
    expect(body).toContain('Focuses and gear identify the base preset.');
    expect(body).toContain('Energy and magic priority orders are editable without changing its name; selecting a preset resets both orders.');
  });
  it('renders readable history with full fields in closed details', () => {
    const body = renderPlan({ ...plan, RebirthHistory: [{ TimestampLocal: '2026-10-14 12:30:00', RebirthSeconds: 3661, Reason: 'Time trigger', BossId: 110, NumberBefore: 100, NumberAfter: 250, NumberMultiplier: 2.5, Future: 'retained' }] });
    const visibleHistory = body.slice(body.indexOf('<h3>Rebirth history</h3>'), body.indexOf('<summary>Details</summary>'));
    expect(visibleHistory).toContain('2026-10-14 12:30:00 (local)');
    expect(visibleHistory).toContain('Time trigger / 1h 1m 1s / Boss 110');
    expect(visibleHistory).toContain('NUMBER 100 to 250 / 2.5x');
    expect(visibleHistory).not.toContain('<pre>');
    expect(body).toContain('"Future": "retained"');
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
    expect(body).toContain('Using saved duration. Save Settings or enter a duration here.');
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
