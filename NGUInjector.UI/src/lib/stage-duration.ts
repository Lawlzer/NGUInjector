import type { JsonObject } from './api';
import { INT_MAX, numeric, stages } from './model';

export type DurationUnit = '%' | 's' | 'min' | 'h';
const factors = { s: 1, min: 60, h: 3600 };

export function formatTime(seconds: number): string {
  if (seconds > 0 && seconds % 3600 === 0) return `${seconds / 3600}h`;
  if (seconds > 0 && seconds % 60 === 0) return `${seconds / 60}m`;
  return `${seconds}s`;
}

export function parseDurationText(text: string, fallback: DurationUnit): { amount: number; unit: DurationUnit } {
  const match = text.trim().match(/^(\d+(?:\.\d*)?|\.\d+)\s*(%|s|m|min|h)?$/i);
  if (!match) throw new Error('Enter a duration such as 25m, 2h, or 30%.');
  const suffix = match[2]?.toLowerCase();
  return { amount: Number(match[1]), unit: suffix === 'm' ? 'min' : (suffix as DurationUnit | undefined) ?? fallback };
}

export function formatStageDuration(plan: JsonObject, index: number): string {
  const row = stages(plan)[index];
  return durationUnit(plan, row) === '%'
    ? `${Number(durationAmount(plan, index).toFixed(6))}%`
    : formatTime(scheduleTiming(plan).seconds[index]!);
}

export function editDurationText(plan: JsonObject, index: number, text: string): JsonObject {
  const fallback = parseDurationText(formatStageDuration(plan, index), '%').unit;
  const { amount, unit } = parseDurationText(text, fallback);
  if (unit !== '%' && scheduleTiming(plan).seconds[index] === 0) {
    if (Number(plan.Version) < 15) throw new Error('Connect and reload this plan to version 15 before using fixed durations.');
    const seconds = wholeSeconds(amount * factors[unit]);
    const next = { ...plan, StageDurationSeconds: numeric(scheduleTiming(plan).total + seconds, 'Schedule length', 1), Stages: stages(plan).map((row, i) => i === index ? { ...row, DurationSeconds: seconds, DurationUnit: unit } : row) };
    scheduleTiming(next);
    return next;
  }
  return editDuration(changeDurationUnit(plan, index, unit), index, amount);
}

export function changeStageCompletion(plan: JsonObject, index: number, mode: number): JsonObject {
  const rows = stages(plan);
  const row = rows[index];
  if (!row) throw new Error('Stage no longer exists.');
  if (mode !== 0 && mode !== 1) throw new Error('Unknown completion mode.');
  if (Number(row.CompletionMode ?? 0) === mode) return plan;
  let next: JsonObject = { ...plan, Stages: rows.map((item, i) => i === index ? { ...item, CompletionMode: mode } : item) };
  if (mode === 0 && Number(plan.Version) >= 15) {
    // An untimed row has no share in the current budget. Give it its own time
    // instead of making it compete with an already fully allocated schedule.
    const seconds = fixedSeconds(plan, row) || 300;
    const hasShares = rows.some((item, i) => i !== index && item.CompletionMode !== 1 && !fixedSeconds(plan, item));
    next = { ...next, ...(hasShares ? { StageDurationSeconds: numeric(scheduleTiming(plan).total + seconds, 'Schedule length', 1) } : {}), Stages: rows.map((item, i) => i === index ? { ...item, CompletionMode: mode, DurationSeconds: seconds, DurationUnit: 'min' } : item) };
  }
  scheduleTiming(next);
  return next;
}

export function timingMode(plan: JsonObject): 'time' | '%' | 'mixed' {
  const rows = stages(plan).filter(row => row.CompletionMode !== 1);
  const fixed = rows.filter(row => fixedSeconds(plan, row) > 0).length;
  return fixed === rows.length ? 'time' : fixed === 0 ? '%' : 'mixed';
}

export function changeTimingMode(plan: JsonObject, mode: 'time' | '%'): JsonObject {
  return stages(plan).reduce((next, row, index) => row.CompletionMode === 1 ? next : changeDurationUnit(next, index, mode === 'time' ? 'min' : '%'), plan);
}

export function fixedSeconds(plan: JsonObject, row: JsonObject): number {
  return Number(plan.Version) >= 15 ? numeric(row.DurationSeconds ?? 0, 'DurationSeconds', 0) : 0;
}

export function durationUnit(plan: JsonObject, row: JsonObject): DurationUnit {
  if (!fixedSeconds(plan, row)) return '%';
  return row.DurationUnit === 'min' || row.DurationUnit === 'h' ? row.DurationUnit : 's';
}

export function scheduleTiming(plan: JsonObject): { seconds: (number | null)[]; shares: number[]; total: number; remaining: number } {
  const rows = stages(plan);
  const sequential = rows.some(row => row.CompletionMode === 1);
  const fixed = rows.map(row => row.CompletionMode === 1 ? 0 : fixedSeconds(plan, row));
  const fixedTotal = fixed.reduce((sum, value) => sum + value, 0);
  const weights = rows.map((row, i) => row.CompletionMode === 1 || fixed[i] ? 0 : numeric(Number(row.Percent ?? 100), 'Percent', Number.MIN_VALUE, Number.MAX_VALUE, false));
  const sum = weights.reduce((a, b) => a + b, 0);
  if (!Number.isFinite(sum)) throw new Error('Stage weights must have a finite total.');
  if (fixedTotal > INT_MAX) throw new Error('Total fixed stage time is too large.');
  const total = sum ? numeric(plan.StageDurationSeconds ?? 1800, 'Schedule length', 1) : fixedTotal;
  const remaining = total - fixedTotal;
  if (sum && remaining <= 0) throw new Error('Schedule length must leave positive time for % share stages.');
  const shares = weights.map(weight => sum ? weight / sum * 100 : 0);
  let cumulative = 0;
  let assigned = 0;
  const last = weights.reduce((last, weight, i) => weight > 0 ? i : last, -1);
  const seconds = rows.map((row, i) => {
    if (row.CompletionMode === 1) return null;
    if (fixed[i]) return fixed[i];
    if (sequential && fixedTotal === 0) {
      const exact = weights[i] / sum * remaining;
      const floor = Math.floor(exact);
      // C# Math.Round uses midpoint-to-even, not JavaScript's midpoint-up.
      return Math.max(1, exact - floor === .5 ? floor + floor % 2 : Math.round(exact));
    }
    cumulative += weights[i];
    const end = i === last ? remaining : Math.floor(cumulative / sum * remaining);
    const duration = end - assigned;
    assigned = end;
    return duration;
  });
  return { seconds, shares, total, remaining };
}

export function durationAmount(plan: JsonObject, index: number): number {
  const row = stages(plan)[index];
  if (row.CompletionMode === 1) throw new Error('AT target stages have no editable timed duration.');
  const unit = durationUnit(plan, row);
  const timing = scheduleTiming(plan);
  return unit === '%' ? timing.shares[index] : timing.seconds[index]! / factors[unit];
}

function wholeSeconds(value: number): number {
  // Unit arithmetic can leave a few ulps around an exact whole second.
  const rounded = Math.round(value);
  if (Math.abs(rounded - value) > Math.max(1, Math.abs(value)) * Number.EPSILON * 8) {
    throw new Error('Fixed duration must resolve to whole seconds; use % share for fractional seconds.');
  }
  return numeric(rounded, 'Fixed duration seconds', 1);
}

export function changeDurationUnit(plan: JsonObject, index: number, unit: DurationUnit): JsonObject {
  const rows = stages(plan);
  const row = rows[index];
  if (!row) throw new Error('Stage no longer exists.');
  if (row.CompletionMode === 1) throw new Error('AT target stages have no editable timed duration.');
  if (!['%', 's', 'min', 'h'].includes(unit)) throw new Error('Unknown duration unit.');
  if (unit !== '%' && Number(plan.Version) < 15) throw new Error('Connect and reload this plan to version 15 before using fixed durations.');
  const wasFixed = fixedSeconds(plan, row);
  const timing = scheduleTiming(plan);
  let patch: JsonObject;
  let budget = plan.StageDurationSeconds;
  if (unit === '%') {
    if (!wasFixed) return { ...plan, Stages: rows.map((item, i) => i === index ? { ...item, DurationUnit: '%' } : item) };
    const percentRows = rows.filter(item => item.CompletionMode !== 1 && !fixedSeconds(plan, item));
    // Join the remaining pool in the same ratio, retaining the original weight scale.
    const weight = percentRows.length
      ? percentRows.reduce((sum, item) => sum + Number(item.Percent ?? 100), 0) * wasFixed / timing.remaining
      : Number(row.Percent ?? 100);
    numeric(weight, 'Percent weight', Number.MIN_VALUE, Number.MAX_VALUE, false);
    patch = { DurationSeconds: 0, DurationUnit: '%', Percent: weight };
    if (!percentRows.length) budget = numeric(timing.total, 'Schedule length', 1);
  } else {
    patch = { DurationSeconds: wholeSeconds(timing.seconds[index]!), DurationUnit: unit };
  }
  let next: JsonObject = { ...plan, Stages: rows.map((item, i) => i === index ? { ...item, ...patch } : item), ...(budget === undefined ? {} : { StageDurationSeconds: budget }) };
  let preserved = false;
  try { preserved = scheduleTiming(next).seconds.every((seconds, i) => seconds === timing.seconds[i]); } catch { /* Legacy minimum-one durations may exceed the reference budget. */ }
  if (!preserved) {
    // Crossing the legacy-rounding/mixed-timing boundary can move whole seconds.
    // Place weighted cumulative boundaries inside their desired integer intervals.
    const nextRows = stages(next);
    const weighted = nextRows.map((item, i) => item.CompletionMode !== 1 && !fixedSeconds(next, item) ? i : -1).filter(i => i >= 0);
    const total = timing.seconds.reduce<number>((sum, seconds) => sum + (seconds ?? 0), 0);
    next = { ...next, StageDurationSeconds: numeric(total, 'Schedule length', 1), Stages: nextRows.map((item, i) => {
      const position = weighted.indexOf(i);
      if (position < 0) return item;
      const weight = timing.seconds[i]! * (weighted.length + 1) + (position === weighted.length - 1 ? 1 - weighted.length : 1);
      return { ...item, Percent: numeric(weight, 'Percent weight', Number.MIN_VALUE, Number.MAX_VALUE, false) };
    }) };
    if (scheduleTiming(next).seconds.some((seconds, i) => seconds !== timing.seconds[i])) throw new Error('Cannot convert units without changing effective stage times.');
  }
  // Do not rewrite a stale reference budget merely by displaying/changing fixed units.
  return next;
}

export function editDuration(plan: JsonObject, index: number, amount: number): JsonObject {
  const rows = stages(plan);
  const row = rows[index];
  if (!row) throw new Error('Stage no longer exists.');
  if (row.CompletionMode === 1) throw new Error('AT target stages have no editable timed duration.');
  numeric(amount, 'Duration', Number.MIN_VALUE, Number.MAX_VALUE, false);
  const unit = durationUnit(plan, row);
  let patch: JsonObject;
  let budget = plan.StageDurationSeconds;
  if (unit === '%') {
    numeric(amount, '% share', Number.MIN_VALUE, 100, false);
    const others = rows.filter((item, i) => i !== index && item.CompletionMode !== 1 && !fixedSeconds(plan, item));
    if (!others.length && amount !== 100) throw new Error('The only % share stage uses 100% of the remaining budget.');
    if (others.length && amount === 100) throw new Error('Leave a positive share for the other stages.');
    const weight = others.length ? others.reduce((sum, item) => sum + Number(item.Percent ?? 100), 0) * amount / (100 - amount) : Number(row.Percent ?? 100);
    numeric(weight, 'Percent weight', Number.MIN_VALUE, Number.MAX_VALUE, false);
    patch = { Percent: weight };
  } else {
    const seconds = wholeSeconds(amount * factors[unit]);
    if (rows.some(item => item.CompletionMode !== 1 && !fixedSeconds(plan, item))) {
      budget = numeric(Number(plan.StageDurationSeconds ?? 1800) + seconds - fixedSeconds(plan, row), 'Schedule length', 1, INT_MAX);
    }
    patch = { DurationSeconds: seconds };
  }
  const next = { ...plan, ...(budget === undefined ? {} : { StageDurationSeconds: budget }), Stages: rows.map((item, i) => i === index ? { ...item, ...patch } : item) };
  scheduleTiming(next);
  return next;
}
