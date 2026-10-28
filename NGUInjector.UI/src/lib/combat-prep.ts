import { LosslessNumber } from 'lossless-json';
import { isObject, stringifyJson, type JsonObject, type JsonValue, type NguiState } from './api';
import { canCommand, numeric, object, parseNumber, validateCombatPrepArgs } from './model';
import { formatTime } from './stage-duration';

export interface CombatPrepDraft { durationSeconds: string; backgroundEnergyPercent: string; backgroundMagicPercent: string }
export const combatPrepDefaults = (): CombatPrepDraft => ({ durationSeconds: '7200', backgroundEnergyPercent: '3', backgroundMagicPercent: '3' });
export const combatPrepCommands = ['previewCombatPrep', 'startCombatPrep', 'cancelCombatPrep'];
export const combatStats = [['augmentMultiplier', 'Augment multiplier'], ['wandoosMultiplier', 'Wandoos multiplier'], ['combinedMultiplier', 'Combined A/W multiplier'], ['bossAttack', 'Boss attack'], ['bossDefense', 'Boss defense']] as const;
export const combatSupportMetrics = [['grossGoldPerSecond', 'Gross gold / second'], ['drainPerSecond', 'Digger drain / second'], ['netGoldPerSecond', 'Net gold / second'], ['wandoosBeardMultiplier', 'Wandoos beard multiplier'], ['wandoosDiggerMultiplier', 'Wandoos digger multiplier']] as const;
export function combatPrepArgs(draft: CombatPrepDraft): JsonObject {
  return {
    durationSeconds: parseNumber(draft.durationSeconds, 'Duration seconds', 1, 86400),
    backgroundEnergyPercent: parseNumber(draft.backgroundEnergyPercent, 'Background energy percent', 0, 100, false),
    backgroundMagicPercent: parseNumber(draft.backgroundMagicPercent, 'Background magic percent', 0, 100, false)
  };
}
export function combatPrepDraftError(draft: CombatPrepDraft): string {
  try { combatPrepArgs(draft); return ''; } catch (err) { return (err as Error).message; }
}
export function combatPrepReady(snapshot: NguiState | null, dir: string, command: string, now = Date.now()): boolean {
  const runtime = object(snapshot?.runtime.combatPrep);
  return runtime.available !== false && runtime.status !== 'error' && !!dir && snapshot?.runtime_dir === dir && typeof snapshot.runtime.startedAt === 'string' && !!snapshot.runtime.startedAt.trim() && canCommand(snapshot, command, now);
}
export const combatRows = (value: JsonValue | undefined): JsonObject[] => Array.isArray(value) ? value.filter(isObject) : [];
export function readCombatPrepPreview(result: JsonObject, args: JsonObject, protocol?: JsonObject): JsonObject {
  const validLimit = (value: JsonValue | undefined): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
  const advertised = object(protocol?.combatPrep).maxPreviewBytes;
  const overall = protocol?.maxResultBytes;
  if ((protocol?.combatPrep !== undefined && !isObject(protocol.combatPrep)) || (overall !== undefined && !validLimit(overall)) ||
    (advertised !== undefined && (!validLimit(advertised) || !validLimit(overall) || advertised > overall))) throw new Error('Combat prep response limit metadata is invalid. Reload the native UI.');
  // Older native builds may omit feature metadata; never infer a larger limit.
  const maxBytes = validLimit(advertised) ? advertised : Math.min(64 * 1024, overall ?? 64 * 1024);
  if (new TextEncoder().encode(stringifyJson(result)).length > maxBytes) throw new Error(`Combat prep response exceeds ${maxBytes / 1024} KB.`);
  const preview = object(object(result.data).combatPrepPreview);
  if (preview.schemaVersion !== 1 || typeof preview.generatedAt !== 'string' || !Number.isFinite(Date.parse(preview.generatedAt))) throw new Error('Combat prep preview is missing or unsupported.');
  validateCombatPrepArgs('startCombatPrep', { previewId: preview.previewId, candidateId: preview.recommendedCandidateId });
  for (const key of Object.keys(args)) if (preview[key] !== args[key]) throw new Error('Combat prep preview options do not match the request.');
  const candidates = combatRows(preview.candidates);
  if (!candidates.length || !Array.isArray(preview.candidates) || candidates.length !== preview.candidates.length) throw new Error('Combat prep candidates are missing.');
  const ids = new Set<string>();
  for (const candidate of candidates) {
    validateCombatPrepArgs('startCombatPrep', { previewId: preview.previewId, candidateId: candidate.id });
    if (ids.has(String(candidate.id)) || typeof candidate.available !== 'boolean') throw new Error('Combat prep candidate identity or availability is invalid.');
    ids.add(String(candidate.id));
  }
  return preview;
}
export function combatPrepStartReason(preview: JsonObject | null, selectedId: string, draft: CombatPrepDraft, now = Date.now()): string {
  if (!preview) return 'Preview the current options before starting.';
  const age = now - Date.parse(String(preview.generatedAt));
  if (!Number.isFinite(age) || age < -2000 || age >= 120000) return 'Preview expired. Refresh preview before starting.';
  try {
    const args = combatPrepArgs(draft);
    for (const key of Object.keys(args)) if (preview[key] !== args[key]) return 'Options changed. Refresh preview before starting.';
    const candidate = combatRows(preview.candidates).find(row => row.id === selectedId);
    if (!candidate || candidate.available !== true) return 'Select an available candidate.';
    const total = ['augmentSeconds', 'atSeconds', 'wandoosSeconds', 'backgroundSeconds'].reduce((sum, key) => sum + numeric(candidate[key], key, 0, 86400, false), 0);
    if (Math.abs(total - Number(preview.durationSeconds)) > 0.001) return 'Candidate total does not match the fixed budget. Refresh preview.';
    return '';
  } catch (err) { return (err as Error).message; }
}
export function combatMetric(value: JsonValue | undefined): string {
  if (value instanceof LosslessNumber) value = Number(value.value);
  if (typeof value !== 'number' || !Number.isFinite(value)) return 'Unavailable';
  const magnitude = Math.abs(value);
  return magnitude >= 1e9 || (magnitude > 0 && magnitude < .001)
    ? value.toExponential(6) : value.toLocaleString(undefined, { maximumSignificantDigits: 7 });
}
export function combatTime(value: JsonValue | undefined): string {
  if (value instanceof LosslessNumber) value = Number(value.value);
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? formatTime(Math.round(value)) : 'Unavailable';
}
export function combatResourceCost(candidate: JsonObject, lane: 'energy' | 'magic'): string {
  return candidate[`${lane}ResourceFocusCostKnown`] === true ? combatTime(candidate[`${lane}ResourceFocusEquivalentSeconds`]) : 'Unavailable';
}
export function combatObserved(value: JsonValue | undefined): string {
  return typeof value === 'string' && /^\d+(?:\.\d+)?$/.test(value) ? value : combatMetric(value);
}
export function combatExactZero(value: JsonValue | undefined): boolean | null {
  return typeof value === 'string' && /^\d+(?:\.\d+)?$/.test(value) ? /^0+(?:\.0+)?$/.test(value) : null;
}
export function combatOs(value: JsonValue | undefined): string {
  return typeof value === 'number' ? ['98', 'MEH', 'XL'][value] ?? 'Unavailable' : 'Unavailable';
}
export function combatAtShare(value: JsonValue | undefined): string {
  if (value instanceof LosslessNumber) value = Number(value.value);
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > 1) return 'Unavailable';
  const percent = (share: number) => (share * 100).toLocaleString(undefined, { maximumFractionDigits: 2 });
  return `${percent(value)}% AT-3 / ${percent(1 - value)}% AT-4`;
}
export function combatSupportBudget(value: JsonValue | undefined): string {
  if (value instanceof LosslessNumber) value = Number(value.value);
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1
    ? `${(value * 100).toLocaleString(undefined, { maximumFractionDigits: 2 })}%` : 'Unavailable';
}
export function combatSupportUsage(drain: JsonValue | undefined, gross: JsonValue | undefined): string {
  if (drain instanceof LosslessNumber) drain = Number(drain.value);
  if (gross instanceof LosslessNumber) gross = Number(gross.value);
  if (typeof drain !== 'number' || !Number.isFinite(drain) || drain < 0 || typeof gross !== 'number' || !Number.isFinite(gross) || gross <= 0) return 'Unavailable';
  const percent = drain / gross * 100;
  return Number.isFinite(percent) ? `${combatMetric(percent)}%` : 'Unavailable';
}
export const combatText = (value: JsonValue | undefined): string => typeof value === 'string' && value.trim() ? value : 'Unavailable';
