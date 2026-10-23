import { describe, expect, it } from 'vitest';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { INT_MAX, reorderStages } from '../src/lib/model';
import { changeStageTiming, editDurationText, editFlexWeight, fixedSeconds, formatStageDuration, formatTime, parseDurationText, scheduleTiming } from '../src/lib/stage-duration';

const rows = (value: JsonObject) => value.Stages as JsonObject[];
const plan = (): JsonObject => ({
  Version: 17, StageDurationSeconds: 1200, StageLoopStartIndex: 1,
  Stages: [{ CompletionMode: 1, DurationSeconds: 999 }, { DurationSeconds: 100 }, { Percent: 35 }, { Percent: 50 }, { Percent: 15 }, { DurationSeconds: 100 }]
});

describe('stage duration text', () => {
  it.each([
    ['25m', 25, 'min'], ['35min', 35, 'min'], ['2H', 2, 'h'], [' 1.5 h ', 1.5, 'h'], ['90s', 90, 's'], ['30%', 30, '%'], ['.5m', .5, 'min'], ['12', 12, 'min']
  ])('parses %s without changing units', (text, amount, unit) => {
    expect(parseDurationText(text as string, 'min')).toEqual({ amount, unit });
  });

  it.each(['', 'twenty', '-5m', '1e3h', 'Infinity', '25ms', '20%%', '1h rubbish', '1.2.3m'])('rejects malformed input %s', text => {
    expect(() => parseDurationText(text, 'min')).toThrow('Enter a duration');
  });

  it.each([[1500, '25m'], [7200, '2h'], [90, '90s'], [0, '0s']])('formats %s seconds as %s', (seconds, label) => {
    expect(formatTime(seconds as number)).toBe(label);
  });

  it('edits independent fixed durations without expanding the budget', () => {
    let next: JsonObject = { ...plan(), Stages: [{ Percent: 35 }, { Percent: 50 }, { Percent: 15 }] };
    for (const [index, text] of ['25m', '35m', '2h'].entries()) next = editDurationText(next, index, text);
    expect(scheduleTiming(next)).toEqual({ seconds: [1500, 2100, 7200], shares: [0, 0, 0], total: 1200, remaining: 0 });
    expect(rows(next).map((_, i) => formatStageDuration(next, i))).toEqual(['25m', '35m', '2h']);
    expect(next.StageDurationSeconds).toBe(1200);
  });

  it('uses the displayed suffix for bare numbers, not saved unit metadata', () => {
    const source = { ...plan(), Stages: [{ DurationSeconds: 7200, DurationUnit: 'min', Percent: 1 }] };
    expect(formatStageDuration(source, 0)).toBe('2h');
    expect(scheduleTiming(editDurationText(source, 0, '3')).seconds).toEqual([10800]);
    expect(scheduleTiming(editDurationText(source, 0, '90s')).seconds).toEqual([90]);
  });

  it.each(['0s', '0m', '.001m', '1.5s', '2147483648s'])('rejects invalid fixed time %s without mutation', text => {
    const source = plan();
    const before = stringifyJson(source);
    expect(() => editDurationText(source, 1, text)).toThrow();
    expect(stringifyJson(source)).toBe(before);
  });

  it('accepts fractional units only when they resolve to whole positive seconds', () => {
    expect(rows(editDurationText(plan(), 1, '.5m'))[1].DurationSeconds).toBe(30);
    expect(rows(editDurationText(plan(), 1, `${1 / 60}min`))[1].DurationSeconds).toBe(1);
  });

  it('applies fixed text directly without an overflowing intermediate default duration', () => {
    const source: JsonObject = { ...plan(), StageDurationSeconds: 0, Stages: [{ DurationSeconds: INT_MAX - 1 }, { Percent: 100 }] };
    const next = editDurationText(source, 1, '1s');
    expect(scheduleTiming(next).seconds).toEqual([INT_MAX - 1, 1]);
    expect(next.StageDurationSeconds).toBe(0);
  });
});

describe('default stage budgets', () => {
  it('reserves every fixed stage including the tail before distributing raw 35/50/15 weights', () => {
    const source = plan();
    const before = stringifyJson(source);
    expect(scheduleTiming(source)).toEqual({ seconds: [null, 100, 350, 500, 150, 100], shares: [0, 0, 35, 50, 15, 0], total: 1200, remaining: 1000 });
    expect(formatStageDuration(source, 2)).toBe('35%');
    expect(stringifyJson(source)).toBe(before);
  });

  it('uses a positive enabled time trigger even for all-fixed plans', () => {
    const source = { ...plan(), TimeTriggerEnabled: true, TimeTriggerSeconds: 800 };
    expect(scheduleTiming(source)).toMatchObject({ total: 800, remaining: 600, seconds: [null, 100, 210, 300, 90, 100] });
    for (const patch of [{ TimeTriggerEnabled: false }, { TimeTriggerSeconds: 0 }]) expect(scheduleTiming({ ...source, ...patch }).total).toBe(1200);
    expect(scheduleTiming({ ...source, Stages: [{ DurationSeconds: 40 }] })).toEqual({ seconds: [40], shares: [0], total: 800, remaining: 760 });
  });

  it.each([0, 100, 200])('allows a %s-second budget and fixed overruns with zero flexible time', horizon => {
    const source = { ...plan(), StageDurationSeconds: horizon };
    expect(scheduleTiming(source)).toMatchObject({ seconds: [null, 100, 0, 0, 0, 100], total: horizon, remaining: 0 });
    const next = editDurationText(source, 1, '25m');
    expect(next.StageDurationSeconds).toBe(horizon);
    expect(scheduleTiming(next).seconds).toEqual([null, 1500, 0, 0, 0, 100]);
  });

  it('keeps both saved budgets unchanged on fixed edits and reallocates only flex', () => {
    const source = { ...plan(), TimeTriggerEnabled: true, TimeTriggerSeconds: 1000 };
    const next = editDurationText(source, 1, '200s');
    expect(next.StageDurationSeconds).toBe(1200);
    expect(next.TimeTriggerSeconds).toBe(1000);
    expect(scheduleTiming(next).seconds).toEqual([null, 200, 245, 350, 105, 100]);
    rows(next).forEach((row, i) => { if (i !== 1) expect(row).toBe(rows(source)[i]); });
  });

  it('conserves cumulative integer budgets with no midpoint rounding or one-second minimum', () => {
    const source: JsonObject = { ...plan(), StageDurationSeconds: 3, Stages: [{ CompletionMode: 1 }, { Percent: 35 }, { Percent: 50 }, { Percent: 15 }] };
    expect(scheduleTiming(source).seconds).toEqual([null, 1, 1, 1]);
    expect(scheduleTiming({ ...source, StageDurationSeconds: 1 }).seconds).toEqual([null, 0, 0, 1]);
    expect(scheduleTiming({ ...source, StageDurationSeconds: 0 }).seconds).toEqual([null, 0, 0, 0]);
    expect(scheduleTiming({ ...source, StageDurationSeconds: 5, Stages: [{ CompletionMode: 1 }, { Percent: 1 }, { Percent: 1 }] }).seconds).toEqual([null, 2, 3]);
  });

  it('reads DurationSeconds canonically without version-dependent reinterpretation', () => {
    for (const Version of [undefined, 14, 15, 16, 17]) {
      const source = { ...plan(), Version, Stages: [{ DurationSeconds: 200 }, { Percent: 1 }, { Percent: 3 }] } as JsonObject;
      expect(fixedSeconds(rows(source)[0])).toBe(200);
      expect(scheduleTiming(source).seconds).toEqual([200, 250, 750]);
    }
  });

  it.each([-1, NaN, Infinity, INT_MAX + 1, 1.5, '1200'])('rejects an invalid active budget %s', StageDurationSeconds => {
    expect(() => scheduleTiming({ ...plan(), StageDurationSeconds })).toThrow('Time budget');
  });

  it('validates the fixed total and active budget even for fixed-only or AT-only plans', () => {
    expect(() => scheduleTiming({ ...plan(), Stages: [{ DurationSeconds: INT_MAX }, { DurationSeconds: 1 }] })).toThrow('Total fixed');
    const source = parseJson('{"Version":17,"StageDurationSeconds":9007199254740993,"Stages":[{"CompletionMode":1}]}') as JsonObject;
    expect(() => scheduleTiming(source)).toThrow('Time budget');
    expect(scheduleTiming({ ...source, StageDurationSeconds: 0 })).toEqual({ seconds: [null], shares: [0], total: 0, remaining: 0 });
  });
});

describe('raw flexible weights', () => {
  it('sets exact 35/50/15 weights sequentially without rewriting other rows or either budget', () => {
    let next: JsonObject = { ...plan(), TimeTriggerEnabled: true, TimeTriggerSeconds: 1200 };
    for (const [index, text] of ['35%', '50', '15'].entries()) {
      const before = next;
      next = editFlexWeight(next, index + 2, text);
      rows(next).forEach((row, i) => { if (i !== index + 2) expect(row).toBe(rows(before)[i]); });
      expect(rows(next)[index + 2].Percent).toBe(Number(text.replace('%', '')));
    }
    expect(rows(next).slice(2, 5).map(row => row.Percent)).toEqual([35, 50, 15]);
    expect(scheduleTiming(next).seconds).toEqual([null, 100, 350, 500, 150, 100]);
    expect([next.StageDurationSeconds, next.TimeTriggerSeconds]).toEqual([1200, 1200]);
  });

  it('percent text is a raw weight, not a normalized share edit', () => {
    const next = editDurationText(plan(), 2, '50%');
    expect(rows(next).slice(2, 5).map(row => row.Percent)).toEqual([50, 50, 15]);
    expect(formatStageDuration(next, 2)).toBe('43.478261%');
    expect(scheduleTiming(next).seconds).toEqual([null, 100, 434, 435, 131, 100]);
  });

  it.each(['35', ' 35% ', '35 %', '3.5e1'])('accepts %s and weights above 100', text => {
    const next = editFlexWeight(plan(), 2, text);
    expect(rows(next)[2].Percent).toBe(35);
    expect(rows(editFlexWeight(next, 2, '350'))[2].Percent).toBe(350);
  });

  it.each(['', ' ', '0', '0%', '-1', 'NaN', 'Infinity', '1e309', '1e-400', '35%%', '35m'])('rejects invalid weight %j without mutation', text => {
    const source = plan();
    const before = stringifyJson(source);
    expect(() => editFlexWeight(source, 2, text)).toThrow();
    expect(stringifyJson(source)).toBe(before);
  });

  it('normalizes a single flexible row for display only, even at zero time', () => {
    const source: JsonObject = { ...plan(), StageDurationSeconds: 0, Stages: [{ DurationSeconds: 100 }, { Percent: 100 }] };
    const next = editFlexWeight(source, 1, '350%');
    expect(rows(next)[1].Percent).toBe(350);
    expect(scheduleTiming(next)).toEqual({ seconds: [100, 0], shares: [0, 100], total: 0, remaining: 0 });
    expect(formatStageDuration(next, 1)).toBe('100%');
  });

  it('rejects weight edits on fixed or target rows and overflowing totals', () => {
    expect(() => editFlexWeight(plan(), 1, '35')).toThrow('flexible stage');
    expect(() => editFlexWeight(plan(), 0, '35')).toThrow('flexible stage');
    expect(() => editFlexWeight(plan(), 99, '35')).toThrow('no longer exists');
    expect(() => editFlexWeight({ ...plan(), Stages: [{ Percent: Number.MAX_VALUE }, { Percent: 1 }] }, 1, String(Number.MAX_VALUE))).toThrow('finite total');
  });

  it.each(['35', true, {}])('does not coerce a malformed configured weight %j', Percent => {
    const source: JsonObject = { ...plan(), Stages: [{ Percent }] };
    expect(() => scheduleTiming(source)).toThrow('Percent');
  });
});

describe('per-row timing changes', () => {
  it('changes only the selected row, retaining its raw weight and budget', () => {
    const source = plan();
    const fixed = changeStageTiming(source, 2, 'fixed');
    expect(rows(fixed)[2]).toEqual({ CompletionMode: 0, Percent: 35, DurationSeconds: 350, DurationUnit: 's' });
    for (const i of [0, 1, 3, 4, 5]) expect(rows(fixed)[i]).toBe(rows(source)[i]);
    const flexible = changeStageTiming(fixed, 2, 'flexible');
    expect(rows(flexible)[2]).toEqual({ CompletionMode: 0, Percent: 35, DurationSeconds: 0, DurationUnit: '%' });
    expect(scheduleTiming(flexible)).toEqual(scheduleTiming(source));
    expect(flexible.StageDurationSeconds).toBe(1200);
    expect(rows(changeStageTiming(fixed, 2, 'target'))[2]).toMatchObject({ CompletionMode: 1, DurationSeconds: 350, Percent: 35 });
  });

  it('defaults zero-budget flexible work to five fixed minutes without growing the horizon', () => {
    const source = { ...plan(), StageDurationSeconds: 0 };
    const fixed = changeStageTiming(source, 2, 'fixed');
    expect(rows(fixed)[2].DurationSeconds).toBe(300);
    expect(fixed.StageDurationSeconds).toBe(0);
    expect(rows(changeStageTiming(source, 0, 'flexible'))[0]).toMatchObject({ CompletionMode: 0, DurationSeconds: 0 });
  });

  it('restores dormant fixed time from AT work and retains exact targets and unknown tokens', () => {
    const source = parseJson('{"Version":17,"StageDurationSeconds":1200,"__externalUiRevision":"revision","Future":9007199254740993,"Stages":[{"Percent":35,"CompletionMode":1,"DurationSeconds":90,"TargetAdvancedTrainingPower":"9223372036854775807","Future":9007199254740995},{"Percent":50}]}') as JsonObject;
    const next = changeStageTiming(source, 0, 'fixed');
    expect(rows(next)[0]).toMatchObject({ CompletionMode: 0, DurationSeconds: 90, Percent: 35, TargetAdvancedTrainingPower: '9223372036854775807' });
    expect(stringifyJson(next)).toContain('"Future":9007199254740993');
    expect(stringifyJson(next)).toContain('"Future":9007199254740995');
    expect(next.__externalUiRevision).toBe('revision');
    expect(next.StageDurationSeconds).toBe(1200);
    expect(rows(next)[1]).toBe(rows(source)[1]);
  });

  it('excludes dormant AT weights and durations from the timed pool', () => {
    const source: JsonObject = { ...plan(), Stages: [{ CompletionMode: 1, Percent: 50, DurationSeconds: INT_MAX }, { Percent: 50 }] };
    expect(scheduleTiming(source)).toEqual({ seconds: [null, 1200], shares: [0, 100], total: 1200, remaining: 1200 });
    expect(() => editDurationText(source, 0, '10s')).toThrow('AT target');
    expect(() => changeStageTiming(source, 99, 'fixed')).toThrow('no longer exists');
    expect(() => changeStageTiming(source, 0, 'protected' as 'fixed')).toThrow('Unknown stage timing');
  });

  it('keeps timing and the loop boundary attached to reordered rows', () => {
    const source: JsonObject = { ...plan(), StageLoopStartIndex: 1, Stages: [{ DurationSeconds: 100 }, { Percent: 35 }, { Percent: 65 }] };
    const next = reorderStages(source, 1, 2);
    expect(next.StageLoopStartIndex).toBe(2);
    expect(scheduleTiming(next).seconds).toEqual([100, 715, 385]);
    expect(reorderStages(next, 2, null).StageLoopStartIndex).toBe(-1);
  });
});
