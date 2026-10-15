import { describe, expect, it } from 'vitest';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { INT_MAX, longTarget, reorderStages } from '../src/lib/model';
import { changeDurationUnit, changeStageCompletion, changeTimingMode, durationAmount, durationUnit, editDuration, editDurationText, formatStageDuration, formatTime, parseDurationText, scheduleTiming, timingMode } from '../src/lib/stage-duration';

const plan = (): JsonObject => ({
  Version: 15, StageDurationSeconds: 1200, StageLoopStartIndex: 1,
  Stages: [{ Name: 'A', Percent: .2 }, { Name: 'B', Percent: .3 }, { Name: 'C', Percent: .5 }]
});

describe('inline durations and plan timing mode', () => {
  it.each([
    ['25m', 25, 'min'], ['35min', 35, 'min'], ['2H', 2, 'h'], [' 1.5 h ', 1.5, 'h'], ['90s', 90, 's'], ['30%', 30, '%'], ['.5m', .5, 'min'], ['12', 12, 'min']
  ])('parses %s without interpreting it as a different unit', (text, amount, unit) => {
    expect(parseDurationText(text as string, 'min')).toEqual({ amount, unit });
  });

  it.each(['', 'twenty', '-5m', '1e3h', 'Infinity', '25ms', '20%%', '1h rubbish', '1.2.3m'])('rejects malformed input %s', text => {
    expect(() => parseDurationText(text, 'min')).toThrow('Enter a duration');
  });

  it.each([[25 * 60, '25m'], [2 * 3600, '2h'], [90, '90s'], [0, '0s']])('formats %s seconds as %s', (seconds, label) => {
    expect(formatTime(seconds as number)).toBe(label);
  });

  it('supports an opener, timed training, and two-hour sustain without sharing their budgets', () => {
    let next = changeTimingMode(plan(), 'time');
    for (const [index, text] of ['25m', '35m', '2h'].entries()) next = editDurationText(next, index, text);
    expect(scheduleTiming(next).seconds).toEqual([1500, 2100, 7200]);
    expect(scheduleTiming(next).total).toBe(10800);
    expect(rows(next).map((_, index) => formatStageDuration(next, index))).toEqual(['25m', '35m', '2h']);
    expect(timingMode(next)).toBe('time');
    expect(plan().StageDurationSeconds).toBe(1200);
  });

  it('uses the displayed suffix for bare numbers even if saved unit metadata differs', () => {
    const source: JsonObject = { ...plan(), Stages: [{ DurationSeconds: 7200, DurationUnit: 'min', Percent: 1 }] };
    expect(formatStageDuration(source, 0)).toBe('2h');
    expect(scheduleTiming(editDurationText(source, 0, '3')).seconds).toEqual([10800]);
    expect(scheduleTiming(editDurationText(source, 0, '90s')).seconds).toEqual([90]);
  });

  it('rebalances percentage edits without changing the total or unrelated data', () => {
    const source = plan();
    const next = editDurationText(source, 0, '25%');
    expect(scheduleTiming(next).seconds).toEqual([300, 337, 563]);
    expect(formatStageDuration(next, 0)).toBe('25%');
    expect(next.StageDurationSeconds).toBe(1200);
    expect(next.StageLoopStartIndex).toBe(1);
    expect(rows(next)[1]).toBe(rows(source)[1]);
  });

  it('shows legacy mixed timing accurately and converts every timed stage together', () => {
    const source: JsonObject = { ...plan(), Stages: [{ DurationSeconds: 200, Percent: 1 }, { Percent: 1 }, { Percent: 3 }, { CompletionMode: 1, DurationSeconds: INT_MAX }] };
    const before = stringifyJson(source);
    expect(timingMode(source)).toBe('mixed');
    const time = changeTimingMode(source, 'time');
    const percent = changeTimingMode(time, '%');
    expect(timingMode(time)).toBe('time');
    expect(timingMode(percent)).toBe('%');
    expect(scheduleTiming(time).seconds).toEqual([200, 250, 750, null]);
    expect(scheduleTiming(percent).seconds).toEqual([200, 250, 750, null]);
    expect(rows(time)[3]).toBe(rows(source)[3]);
    expect(rows(percent)[3]).toBe(rows(source)[3]);
    expect(stringifyJson(source)).toBe(before);
  });

  it('preserves effective seconds across whole-plan conversions including sequential legacy rounding', () => {
    for (const sequential of [false, true]) for (const budget of [3, 5, 7, 31, 1200]) {
      const source: JsonObject = { ...plan(), StageDurationSeconds: budget, Stages: [...(sequential ? [{ CompletionMode: 1, Name: 'AT', TargetAdvancedTrainingPower: '9223372036854775807' }] : []), { Percent: 1 }, { Percent: 1 }, { Percent: 1 }] };
      const seconds = scheduleTiming(source).seconds;
      const time = changeTimingMode(source, 'time');
      expect(scheduleTiming(time).seconds).toEqual(seconds);
      expect(scheduleTiming(changeTimingMode(time, '%')).seconds).toEqual(seconds);
    }
  });

  it('does not migrate old plans when requesting fixed timing', () => {
    const source = { ...plan(), Version: 14 };
    expect(() => changeTimingMode(source, 'time')).toThrow('version 15');
    expect(timingMode(source)).toBe('%');
  });

  it('rejects fractional seconds and overflow in inline fixed times', () => {
    expect(() => editDurationText(plan(), 0, '.001m')).toThrow('whole seconds');
    expect(() => editDurationText(plan(), 0, '2147483648s')).toThrow('Fixed duration seconds');
    expect(() => editDurationText(plan(), 0, '0m')).toThrow('Duration');
    expect(() => editDurationText(plan(), 0, '101%')).toThrow('% share');
  });

  it('lets a zero-second legacy share be replaced with a positive fixed time', () => {
    const source: JsonObject = { ...plan(), StageDurationSeconds: 1, Stages: [{ Percent: 1 }, { Percent: 1 }] };
    expect(scheduleTiming(source).seconds).toEqual([0, 1]);
    const next = editDurationText(source, 0, '25m');
    expect(scheduleTiming(next).seconds).toEqual([1500, 1]);
    expect(next.StageDurationSeconds).toBe(1501);
    expect(() => changeTimingMode(source, 'time')).toThrow('Fixed duration seconds');
  });

  it('gives returning AT stages their own time without stealing another stage budget', () => {
    const source: JsonObject = { ...plan(), Stages: [{ CompletionMode: 1, Percent: 100, Future: 'keep', TargetAdvancedTrainingPower: '9223372036854775807' }, { Percent: 1 }] };
    const next = changeStageCompletion(source, 0, 0);
    expect(scheduleTiming(next).seconds).toEqual([300, 1200]);
    expect(next.StageDurationSeconds).toBe(1500);
    expect(rows(next)[0]).toMatchObject({ CompletionMode: 0, DurationSeconds: 300, Future: 'keep', TargetAdvancedTrainingPower: '9223372036854775807' });
    expect(rows(next)[1]).toBe(rows(source)[1]);
    expect(rows(source)[0].CompletionMode).toBe(1);
  });

  it('restores a dormant timed duration without reading an unused all-fixed reference budget', () => {
    const source = parseJson('{"Version":15,"StageDurationSeconds":9007199254740993,"Stages":[{"CompletionMode":1,"DurationSeconds":90},{"DurationSeconds":3600}]}') as JsonObject;
    const next = changeStageCompletion(source, 0, 0);
    expect(scheduleTiming(next).seconds).toEqual([90, 3600]);
    expect(stringifyJson(next)).toContain('"StageDurationSeconds":9007199254740993');
  });

  it('keeps dormant timing and exact targets when changing a timed stage to AT completion', () => {
    const source: JsonObject = { ...plan(), Stages: [{ DurationSeconds: 900, DurationUnit: 'min', TargetAdvancedTrainingPower: '9223372036854775807' }, { Percent: 100 }] };
    const next = changeStageCompletion(source, 0, 1);
    expect(rows(next)[0]).toEqual({ ...rows(source)[0], CompletionMode: 1 });
    expect(scheduleTiming(next).seconds).toEqual([null, 1200]);
    expect(changeStageCompletion(next, 0, 1)).toBe(next);
    expect(() => changeStageCompletion(source, 99, 0)).toThrow('no longer exists');
    expect(() => changeStageCompletion(source, 0, 5)).toThrow('Unknown completion');
  });
});
const rows = (value: JsonObject) => value.Stages as JsonObject[];

describe('schedule durations', () => {
  it('normalizes old sub-one weights without rewriting the source', () => {
    const source = plan();
    const before = stringifyJson(source);
    expect(scheduleTiming(source).seconds).toEqual([240, 360, 600]);
    expect(durationAmount(source, 0)).toBe(20);
    expect(stringifyJson(source)).toBe(before);
    expect(scheduleTiming({ ...source, Stages: rows(source).map(row => ({ ...row, Percent: Number(row.Percent) * 1000 })) }).seconds).toEqual([240, 360, 600]);
  });

  it('leaves version 14 plans weighted even if unknown future duration fields exist', () => {
    const source: JsonObject = { ...plan(), Version: 14, Stages: [{ Percent: 1, DurationSeconds: 50 }, { Percent: 1 }] };
    expect(scheduleTiming(source).seconds).toEqual([600, 600]);
    expect(() => changeDurationUnit(source, 0, 's')).toThrow('version 15');
  });

  it('subtracts fixed times before distributing the remaining budget', () => {
    const source: JsonObject = { ...plan(), Stages: [{ Percent: 99, DurationSeconds: 200 }, { Percent: .25 }, { Percent: .75 }] };
    expect(scheduleTiming(source)).toEqual({ seconds: [200, 250, 750], shares: [0, 25, 75], total: 1200, remaining: 1000 });
  });

  it('converts to fixed and back without changing any effective times', () => {
    const source = plan();
    const fixed = changeDurationUnit(source, 1, 'min');
    expect(rows(fixed)[1]).toEqual({ Name: 'B', Percent: .3, DurationSeconds: 360, DurationUnit: 'min' });
    expect(durationAmount(fixed, 1)).toBe(6);
    expect(scheduleTiming(fixed).seconds).toEqual([expect.closeTo(240, 10), 360, 600]);
    const percent = changeDurationUnit(fixed, 1, '%');
    expect(scheduleTiming(percent).seconds).toEqual(scheduleTiming(source).seconds);
    expect(Number(rows(percent)[1].Percent)).toBeCloseTo(.3, 14);
    expect(percent.StageLoopStartIndex).toBe(1);
  });

  it('changes fixed units as display metadata only', () => {
    const source = changeDurationUnit(plan(), 0, 's');
    const next = changeDurationUnit(source, 0, 'h');
    expect(rows(next)[0].DurationSeconds).toBe(240);
    expect(rows(next)[0].Percent).toBe(.2);
    expect(durationUnit(next, rows(next)[0])).toBe('h');
    expect(durationAmount(next, 0)).toBe(240 / 3600);
    expect(scheduleTiming(next)).toEqual(scheduleTiming(source));
  });

  it('adjusts reference length by fixed edit delta, keeping every other duration stable', () => {
    const source = changeDurationUnit(plan(), 0, 'min');
    const next = editDuration(source, 0, 4.5);
    expect(next.StageDurationSeconds).toBe(1230);
    expect(scheduleTiming(next).seconds).toEqual([270, expect.closeTo(360, 10), 600]);
    expect(rows(next)[0].Percent).toBe(.2);
  });

  it('redistributes percentage edits within the remaining pool only', () => {
    const source = changeDurationUnit(plan(), 0, 's');
    const next = editDuration(source, 1, 50);
    expect(scheduleTiming(next).seconds).toEqual([240, 480, 480]);
    expect(next.StageDurationSeconds).toBe(1200);
    expect(rows(next)[2]).toBe(rows(source)[2]);
  });

  it('uses all-fixed sum without silently replacing stale reference metadata', () => {
    const source = { ...plan(), StageDurationSeconds: 9999, Stages: [{ DurationSeconds: 60, Percent: .2 }, { DurationSeconds: 120, Percent: .8 }] };
    expect(scheduleTiming(source).total).toBe(180);
    expect(changeDurationUnit(source, 0, 'min').StageDurationSeconds).toBe(9999);
    const edited = editDuration(source, 0, 90);
    expect(edited.StageDurationSeconds).toBe(9999);
    expect(scheduleTiming(edited).seconds).toEqual([90, 120]);
    const converted = changeDurationUnit(source, 0, '%');
    expect(converted.StageDurationSeconds).toBe(180);
    expect(scheduleTiming(converted).seconds).toEqual([60, 120]);
  });

  it('keeps source metadata, unknown numeric tokens and Int64 targets exact', () => {
    const source = parseJson('{"Version":15,"StageDurationSeconds":1200,"__externalUiRevision":"revision","Future":9007199254740993,"Stages":[{"Percent":1,"CompletionMode":1,"TargetAdvancedTrainingPower":"9223372036854775807","Future":9007199254740995},{"Percent":1}]}') as JsonObject;
    const next = changeDurationUnit(source, 1, 'min');
    expect(stringifyJson(next)).toContain('"Future":9007199254740993');
    expect(stringifyJson(next)).toContain('"Future":9007199254740995');
    expect(rows(next)[0].TargetAdvancedTrainingPower).toBe('9223372036854775807');
    expect(rows(next)[0].CompletionMode).toBe(1);
    expect(next.__externalUiRevision).toBe('revision');
    expect(longTarget(rows(next)[0].TargetAdvancedTrainingPower, 'AT target', true)).toBe('9223372036854775807');
  });

  it('does not round or replace an unused lossless all-fixed reference budget', () => {
    const source = parseJson('{"Version":15,"StageDurationSeconds":9007199254740993,"Stages":[{"DurationSeconds":60,"Percent":0.2}]}') as JsonObject;
    expect(scheduleTiming(source).total).toBe(60);
    expect(stringifyJson(changeDurationUnit(source, 0, 'min'))).toContain('"StageDurationSeconds":9007199254740993');
    expect(stringifyJson(editDuration(source, 0, 90))).toContain('"StageDurationSeconds":9007199254740993');
  });

  it('retains loop boundary and timing identity through reordering', () => {
    const source = changeDurationUnit(plan(), 1, 'min');
    const next = reorderStages(source, 1, 2);
    expect(next.StageLoopStartIndex).toBe(2);
    expect(scheduleTiming(next).seconds).toEqual([expect.closeTo(240, 10), 600, 360]);
    expect(reorderStages(next, 2, null).StageLoopStartIndex).toBe(-1);
  });

  it.each([0, -1, NaN, Infinity, INT_MAX + 1, 1.5])('rejects invalid fixed seconds %s', amount => {
    expect(() => editDuration(changeDurationUnit(plan(), 0, 's'), 0, amount)).toThrow();
  });

  it('accepts fractional units only when they resolve to whole positive seconds', () => {
    const source = changeDurationUnit(plan(), 0, 'min');
    expect(rows(editDuration(source, 0, 1 / 60))[0].DurationSeconds).toBe(1);
    expect(() => editDuration(source, 0, .001)).toThrow('whole seconds');
    expect(() => changeDurationUnit({ ...plan(), StageDurationSeconds: 1 }, 0, 's')).toThrow('Fixed duration seconds');
  });

  it('enforces Int32 seconds and reference-budget limits', () => {
    const source = { Version: 15, StageDurationSeconds: INT_MAX, Stages: [{ DurationSeconds: INT_MAX, Percent: 1 }] };
    expect(rows(editDuration(source, 0, INT_MAX))[0].DurationSeconds).toBe(INT_MAX);
    expect(() => editDuration({ ...source, Stages: [{ DurationSeconds: INT_MAX - 2, Percent: 1 }, { Percent: 1 }] }, 0, INT_MAX - 1)).toThrow('Schedule length');
    expect(() => scheduleTiming({ ...plan(), Stages: [{ DurationSeconds: 1200 }, { Percent: 1 }] })).toThrow('positive time');
  });

  it('rejects impossible percentage shares without zeroing other stages', () => {
    expect(() => editDuration(plan(), 0, 100)).toThrow('positive share');
    expect(() => editDuration({ ...plan(), Stages: [{ Percent: .2 }] }, 0, 50)).toThrow('only % share');
    expect(() => editDuration(plan(), 0, 0)).toThrow('Duration');
  });

  it('excludes dormant AT weights and fixed durations from the timed pool', () => {
    const source: JsonObject = { Version: 15, StageDurationSeconds: 600, Stages: [{ CompletionMode: 1, Percent: 50, DurationSeconds: INT_MAX }, { Percent: 50 }] };
    expect(scheduleTiming(source)).toEqual({ seconds: [null, 600], shares: [0, 100], total: 600, remaining: 600 });
    const fixed = changeDurationUnit(source, 1, 's');
    expect(rows(fixed)[1].DurationSeconds).toBe(600);
    expect(scheduleTiming(fixed).seconds).toEqual([null, 600]);
    expect(rows(fixed)[0]).toBe(rows(source)[0]);
    expect(() => changeDurationUnit(source, 0, 's')).toThrow('AT target');
    expect(() => editDuration(source, 0, 10)).toThrow('AT target');
    expect(() => durationAmount(source, 0)).toThrow('AT target');
  });

  it('uses legacy C# midpoint-to-even and minimum-one rounding for sequential percent stages', () => {
    const source: JsonObject = { Version: 14, StageDurationSeconds: 5, Stages: [{ CompletionMode: 1 }, { Percent: .5 }, { Percent: .5 }] };
    expect(scheduleTiming(source).seconds).toEqual([null, 2, 2]);
    expect(scheduleTiming({ ...source, StageDurationSeconds: 7 }).seconds).toEqual([null, 4, 4]);
    expect(scheduleTiming({ ...source, StageDurationSeconds: 1 }).seconds).toEqual([null, 1, 1]);
  });

  it('preserves rounded legacy effective times when converting to fixed and back', () => {
    const source: JsonObject = { Version: 15, StageDurationSeconds: 5, Stages: [{ CompletionMode: 1 }, { Percent: 1 }, { Percent: 1 }] };
    const fixed = changeDurationUnit(source, 1, 's');
    expect(scheduleTiming(fixed).seconds).toEqual([null, 2, 2]);
    expect(rows(fixed)[1].DurationSeconds).toBe(2);
    expect(scheduleTiming(changeDurationUnit(fixed, 1, '%')).seconds).toEqual([null, 2, 2]);
  });

  it('conserves mixed timed budgets and time-window budgets by cumulative integer boundaries', () => {
    const source: JsonObject = { Version: 15, StageDurationSeconds: 10, Stages: [{ Percent: 1 }, { Percent: 1 }, { Percent: 1 }] };
    expect(scheduleTiming(source).seconds).toEqual([3, 3, 4]);
    expect(scheduleTiming({ ...source, Stages: [{ CompletionMode: 1, DurationSeconds: 999 }, { DurationSeconds: 3 }, { Percent: 1 }, { Percent: 1 }] }).seconds).toEqual([null, 3, 3, 4]);
  });

  it('keeps even a tiny unused reference budget unchanged when editing an all-fixed timed pool', () => {
    const source: JsonObject = { Version: 15, StageDurationSeconds: 1, Stages: [{ CompletionMode: 1, Percent: 50 }, { DurationSeconds: 60 }, { DurationSeconds: 120 }] };
    const next = editDuration(source, 1, 30);
    expect(next.StageDurationSeconds).toBe(1);
    expect(scheduleTiming(next).seconds).toEqual([null, 30, 120]);
    expect(scheduleTiming(next).total).toBe(150);
  });

  it('does not adjust a fixed edit budget merely because an untimed AT row has a dormant weight', () => {
    const source: JsonObject = { Version: 15, StageDurationSeconds: 600, Stages: [{ CompletionMode: 1, Percent: 100 }, { DurationSeconds: 60 }] };
    expect(editDuration(source, 1, 30).StageDurationSeconds).toBe(600);
    const weighted: JsonObject = { ...source, Stages: [...rows(source), { Percent: .5 }] };
    const next = editDuration(weighted, 1, 30);
    expect(next.StageDurationSeconds).toBe(570);
    expect(scheduleTiming(next).seconds).toEqual([null, 30, 540]);
  });

  it('represents an AT-only plan as entirely untimed without reading its dormant budget', () => {
    const source = parseJson('{"Version":15,"StageDurationSeconds":9007199254740993,"Stages":[{"CompletionMode":1,"DurationSeconds":120,"Percent":1}]}') as JsonObject;
    expect(scheduleTiming(source)).toEqual({ seconds: [null], shares: [0], total: 0, remaining: 0 });
  });
});
