import type { JsonObject } from './api';
import { INT_MAX, numeric, stages } from './model';

export type DurationUnit = '%' | 's' | 'min' | 'h';
export type StageTimingKind = 'fixed' | 'flexible' | 'target';
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

export function fightDurationSeconds(text: string, current: number, repeat = false): number {
  if (/\.\s*(?:s|m|min|h)?$/i.test(text)) throw new Error('Finish the decimal value before saving.');
  const { amount, unit } = parseDurationText(text, parseDurationText(formatTime(current), 's').unit);
  if (unit === '%') throw new Error('Fight timing requires a duration, not a percentage.');
  const seconds = amount * factors[unit];
  const rounded = Math.round(seconds);
  if (Math.abs(rounded - seconds) > Math.max(1, Math.abs(seconds)) * Number.EPSILON * 8) throw new Error('Fight timing must resolve to whole seconds.');
  return numeric(rounded, repeat ? 'Repeat interval seconds' : 'First fight delay seconds', repeat ? 1 : 0);
}

export function fixedSeconds(row: JsonObject): number {
  return numeric(row.DurationSeconds ?? 0, 'DurationSeconds', 0);
}

export function formatStageDuration(plan: JsonObject, index: number): string {
  const row = stages(plan)[index];
  const fixed = fixedSeconds(row);
  return fixed ? formatTime(fixed) : `${Number(scheduleTiming(plan).shares[index].toFixed(6))}%`;
}

export function editDurationText(plan: JsonObject, index: number, text: string): JsonObject {
  const rows = stages(plan);
  const row = rows[index];
  if (!row) throw new Error('Stage no longer exists.');
  if (row.CompletionMode === 1) throw new Error('AT target stages have no editable timed duration.');
  const fallback = parseDurationText(formatStageDuration(plan, index), '%').unit;
  const { amount, unit } = parseDurationText(text, fallback);
  if (unit === '%') return editFlexWeight(changeStageTiming(plan, index, 'flexible'), index, String(amount));
  const seconds = wholeSeconds(amount * factors[unit]);
  const next = { ...plan, Stages: rows.map((item, i) => i === index ? { ...item, CompletionMode: 0, DurationSeconds: seconds, DurationUnit: unit } : item) };
  scheduleTiming(next);
  return next;
}

export function editFlexWeight(plan: JsonObject, index: number, text: string): JsonObject {
  const rows = stages(plan);
  const row = rows[index];
  if (!row) throw new Error('Stage no longer exists.');
  if (row.CompletionMode === 1 || fixedSeconds(row)) throw new Error('Flexible weights require a flexible stage.');
  if (!/^\s*(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?\s*%?\s*$/i.test(text)) throw new Error('Enter a positive relative weight, such as 35, 150, or 35%.');
  const weight = numeric(Number(text.trim().replace(/%$/, '').trim()), 'Flexible weight', Number.MIN_VALUE, Number.MAX_VALUE, false);
  const next = { ...plan, Stages: rows.map((item, i) => i === index ? { ...item, Percent: weight } : item) };
  scheduleTiming(next);
  return next;
}

export function changeStageTiming(plan: JsonObject, index: number, kind: StageTimingKind): JsonObject {
  const rows = stages(plan);
  const row = rows[index];
  if (!row) throw new Error('Stage no longer exists.');
  if (!['fixed', 'flexible', 'target'].includes(kind)) throw new Error('Unknown stage timing kind.');
  const patch: JsonObject = kind === 'target' ? { CompletionMode: 1 } : {
    CompletionMode: 0,
    DurationSeconds: kind === 'fixed' ? fixedSeconds(row) || scheduleTiming(plan).seconds[index] || 300 : 0,
    DurationUnit: kind === 'flexible' ? '%' : 's'
  };
  const next = { ...plan, Stages: rows.map((item, i) => i === index ? { ...item, ...patch } : item) };
  scheduleTiming(next);
  return next;
}

export function scheduleTiming(plan: JsonObject): { seconds: (number | null)[]; shares: number[]; total: number; remaining: number } {
  const rows = stages(plan);
  const fixed = rows.map(row => row.CompletionMode === 1 ? 0 : fixedSeconds(row));
  const fixedTotal = fixed.reduce((sum, value) => sum + value, 0);
  const weights = rows.map((row, i) => row.CompletionMode === 1 || fixed[i] ? 0 : numeric(row.Percent ?? 100, 'Percent', Number.MIN_VALUE, Number.MAX_VALUE, false));
  const sum = weights.reduce((a, b) => a + b, 0);
  if (!Number.isFinite(sum)) throw new Error('Stage weights must have a finite total.');
  if (fixedTotal > INT_MAX) throw new Error('Total fixed stage time is too large.');
  const total = numeric(plan.TimeTriggerEnabled === true && Number(plan.TimeTriggerSeconds) > 0 ? plan.TimeTriggerSeconds : plan.StageDurationSeconds ?? 1800, 'Time budget', 0);
  const remaining = Math.max(0, total - fixedTotal);
  const shares = weights.map(weight => sum ? weight / sum * 100 : 0);
  let cumulative = 0;
  let assigned = 0;
  const last = weights.reduce((last, weight, i) => weight > 0 ? i : last, -1);
  const seconds = rows.map((row, i) => {
    if (row.CompletionMode === 1) return null;
    if (fixed[i]) return fixed[i];
    cumulative += weights[i];
    // Cumulative integer boundaries conserve the pool, including zero-second shares.
    const boundary = Number.isFinite(cumulative * remaining) ? cumulative * remaining / sum : cumulative / sum * remaining;
    const end = i === last ? remaining : Math.floor(boundary);
    const duration = end - assigned;
    assigned = end;
    return duration;
  });
  return { seconds, shares, total, remaining };
}

function wholeSeconds(value: number): number {
  // Unit arithmetic can leave a few ulps around an exact whole second.
  const rounded = Math.round(value);
  if (Math.abs(rounded - value) > Math.max(1, Math.abs(value)) * Number.EPSILON * 8) {
    throw new Error('Fixed duration must resolve to whole seconds; use a flexible share for fractional seconds.');
  }
  return numeric(rounded, 'Fixed duration seconds', 1);
}
