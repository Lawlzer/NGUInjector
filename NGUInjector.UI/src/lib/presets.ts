import { cloneObject, isObject, requirePlanRevision, stripNguOverrides, type JsonObject, type JsonValue, type NguiState } from './api';
import { canCommand, equal, freshEdits, numeric, object } from './model';
import { presetZones } from './options';
import { isSafeNumber, LosslessNumber } from 'lossless-json';
import definitions from '../../../NGUInjector/Data/PresetDefinitions.json';
import activityPolicies from '../../../NGUInjector/Data/ActivityPolicies.json';
import type { PriorityOption } from './priority-options';

export const objectiveKeys = definitions.objectives.map(row => row.key);
export const objectiveLabels = definitions.objectives.map(row => row.label);
export const diggerNames = definitions.diggers.map(row => row.name);
export const beardOptions = definitions.beards;
export const presetFields = [...new Set(['Id', 'Name', ...Object.keys(definitions.defaults), 'BeardRotation', 'AdventureDynamicWeight', 'EnergyPriorities', 'MagicPriorities', 'ResourcePrioritiesReviewed'])];
export const energyLaneTokens = ['NGU', 'TM', 'AUG', 'WAN', 'AT'] as const;
export const magicLaneTokens = ['NGU', 'RIT', 'TM', 'WAN'] as const;
export type ResourceLanePriorities = {
  ResourcePrioritiesReviewed?: boolean;
  EnergyPriorities: (typeof energyLaneTokens)[number][];
  MagicPriorities: (typeof magicLaneTokens)[number][];
} | {
  ResourcePrioritiesReviewed?: false;
  EnergyPriorities?: null;
  MagicPriorities?: null;
};
export function resourceLanePrioritiesReady(snapshot: NguiState | null, preset: JsonObject | undefined): boolean {
  return preset?.ResourcePrioritiesReviewed !== true || snapshot?.runtime.resourceLanePrioritiesVersion === 1;
}
export function resourceLaneOptions(row: JsonObject, energy: boolean): PriorityOption[] {
  const indices = energy ? [0, 6, 7, 8, 9] : [1, 2, 6, 8];
  const saved = row[energy ? 'EnergyPriorities' : 'MagicPriorities'];
  return (energy ? energyLaneTokens : magicLaneTokens).flatMap((value, i) => (Array.isArray(row.Weights) && Number(row.Weights[indices[i]]) > 0) || (Array.isArray(saved) && saved.includes(value))
    ? [{ value, label: objectiveLabels[indices[i]], group: 'Activities' }] : []);
}
export function proposedResourcePriorities(row: JsonObject, energy: boolean): string[] {
  const indices = energy ? [0, 6, 7, 8, 9] : [1, 2, 6, 8];
  // Token order is the stable tie-breaker; only positively weighted activities are proposed.
  return (energy ? energyLaneTokens : magicLaneTokens).map((token, i) => ({ token, weight: Number(Array.isArray(row.Weights) ? row.Weights[indices[i]] : 0), i }))
    .filter(row => row.weight > 0).sort((a, b) => b.weight - a.weight || a.i - b.i).map(row => row.token);
}
export function dynamicPresetReady(snapshot: NguiState | null, preset: JsonObject | undefined): boolean {
  return !(Number(preset?.AdventureDynamicWeight) > 0) || snapshot?.runtime.adventureDynamicPresetsVersion === 1;
}
export function presetNumberInput(text: string): number | string {
  return /^[+-]?(?:\d+(?:\.\d+)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(text) && isSafeNumber(text.replace(/^\+/, ''), { approx: false }) ? Number(text) : text;
}
export function newPreset(source?: JsonObject): JsonObject {
  return normalizePreset({ ...cloneObject(source ?? { ...definitions.newPreset, ...defaultPresetBeards() }), Id: `preset_${crypto.randomUUID().replace(/-/g, '')}` });
}
export function normalizePreset(row: JsonObject): JsonObject {
  const normalized: JsonObject = { AdventureDynamicWeight: 0, EnergyPriorities: null, MagicPriorities: null, ResourcePrioritiesReviewed: false, ...cloneObject(definitions.defaults), ...stripNguOverrides(row) };
  delete normalized.HorizonSeconds;
  if (Array.isArray(row.Weights) && [6, 10, 12].includes(row.Weights.length)) normalized.Weights = [...row.Weights, ...Array(objectiveKeys.length - row.Weights.length).fill(0)];
  // Defined doubles compare by runtime value across .NET/JS formatting; unknown tokens stay exact.
  for (const key of ['MinimumPower', 'MinimumToughness', 'AdventureDynamicWeight']) if (normalized[key] instanceof LosslessNumber) normalized[key] = Number(normalized[key]);
  for (const key of ['Weights', 'DiggerWeights']) if (Array.isArray(normalized[key])) normalized[key] = (normalized[key] as JsonValue[]).map(value => value instanceof LosslessNumber ? Number(value) : value);
  return normalized;
}
export function defaultPresetBeards(): JsonObject {
  return { BeardPriority: [...activityPolicies.coreBeards], BeardRotation: [...activityPolicies.optionalBeards] };
}
export function presetBeards(row: JsonObject): { enabled: number[]; rng: number[]; legacy: boolean } {
  const enabled = Array.isArray(row.BeardPriority) ? row.BeardPriority.filter((id): id is number => typeof id === 'number') : [];
  if (Array.isArray(row.BeardRotation)) return { enabled, rng: row.BeardRotation.filter((id): id is number => typeof id === 'number'), legacy: false };
  return enabled.length ? { enabled, rng: [], legacy: true } : { enabled: [...activityPolicies.coreBeards], rng: [...activityPolicies.optionalBeards], legacy: true };
}
export function setPresetBeard(row: JsonObject, id: number, mode: string): JsonObject {
  const current = presetBeards(row);
  return { ...row, BeardPriority: [...current.enabled.filter(value => value !== id), ...(mode === 'enabled' ? [id] : [])], BeardRotation: [...current.rng.filter(value => value !== id), ...(mode === 'rng' ? [id] : [])] };
}
export function presetDiggerWeights(row: JsonObject, effectiveWeights?: JsonValue): JsonValue[] {
  if (Array.isArray(row.DiggerWeights) && row.DiggerWeights.length) return [...row.DiggerWeights];
  if (Number(row.AdventureDynamicWeight) > 0 && !Array.isArray(effectiveWeights)) return [];
  const source = effectiveWeights ?? row.Weights;
  const weights = objectiveKeys.map((_, i) => Math.max(0, Number(Array.isArray(source) ? source[i] ?? 0 : 0)));
  const sum = weights.reduce((total, weight) => total + weight, 0);
  const normalized = weights.map(weight => Number.isFinite(sum) && sum > 0 ? weight / sum : 0);
  return definitions.diggers.map(digger => digger.objectiveWeights.reduce((total, coefficient, i) => total + coefficient * normalized[i], 0) + (Number(row.MinimumPower) > 0 || Number(row.MinimumToughness) > 0 ? digger.minimumAdventureWeight : 0));
}
export function beardNames(value: JsonValue | undefined): string {
  return Array.isArray(value) ? value.map(id => beardOptions.find(beard => beard.id === id)?.name ?? `Unknown beard ${String(id)}`).join(', ') : 'Unknown';
}
export function globalPresetRows(library: JsonObject | null): JsonObject[] {
  return Array.isArray(library?.Presets) ? library.Presets.filter(isObject) : [];
}
export function presetSupportsAtTargets(preset: JsonObject | undefined): boolean {
  return !!preset && Array.isArray(preset.Weights) && Number(preset.Weights[9]) > 0 && Array.isArray(preset.AdvancedTrainingWeights) && Number(preset.AdvancedTrainingWeights[0]) > 0 && Number(preset.AdvancedTrainingWeights[1]) > 0;
}
export function normalizePresetLibrary(library: JsonObject): JsonObject {
  validateGlobalLibrary(library);
  return { ...stripNguOverrides(library), Presets: globalPresetRows(library).map(normalizePreset) };
}
export function validateGlobalLibrary(library: JsonObject): void {
  requirePlanRevision(library);
  if (library.Version !== 1) throw new Error('Preset library Version must be 1.');
  if (!Array.isArray(library.Presets)) throw new Error('Preset library Presets must be an array.');
  validatePresetLibrary(library.Presets, 1024);
}
// Merge by stable ID so unrelated external additions and edits need not overwrite local drafts.
export function rebasePresetLibrary(base: JsonObject, draft: JsonObject, fresh: JsonObject): JsonObject {
  const before = new Map(globalPresetRows(base).map(row => [String(row.Id), row]));
  const local = new Map(globalPresetRows(draft).map(row => [String(row.Id), row]));
  const disk = new Map(globalPresetRows(fresh).map(row => [String(row.Id), row]));
  const rows: JsonObject[] = [];
  for (const id of new Set([...disk.keys(), ...local.keys(), ...before.keys()])) {
    const a = before.get(id), b = local.get(id), c = disk.get(id);
    if (equal(a, b)) { if (c) rows.push(c); }
    else if (equal(a, c) || equal(b, c)) { if (b) rows.push(b); }
    else if (a && b && c) rows.push(freshEdits(a, b, c));
    else throw new Error(`Source changed at preset ${id}. Local edits retained; resolve or reload the library.`);
  }
  const withoutRows = (value: JsonObject) => { const copy = { ...value }; delete copy.Presets; delete copy.__externalUiRevision; return copy; };
  return { ...freshEdits(withoutRows(base), withoutRows(draft), withoutRows(fresh)), Presets: rows, __externalUiRevision: fresh.__externalUiRevision };
}
export function globalPresetArgs(id: string, library: JsonObject | null): JsonObject {
  if (!library || !globalPresetRows(library).some(row => row.Id === id)) throw new Error('Select a saved global preset first.');
  validateGlobalLibrary(library);
  return { presetId: id, libraryRevision: library.__externalUiRevision };
}
export function validateGlobalPresetArgs(command: string, args: JsonObject): void {
  const keys = ['presetId', 'libraryRevision', ...(command === 'applyGlobalPreset' ? ['previewId', 'candidateKey'] : [])];
  if (Object.keys(args).length !== keys.length || Object.keys(args).some(key => !keys.includes(key))) throw new Error(`${command} accepts exactly ${keys.join(', ')}.`);
  if (typeof args.presetId !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(args.presetId)) throw new Error('Choose a valid global preset ID.');
  requirePlanRevision({ __externalUiRevision: args.libraryRevision });
  if (command === 'applyGlobalPreset') {
    if (typeof args.previewId !== 'string' || !/^[a-fA-F0-9]{32}$/.test(args.previewId)) throw new Error('Apply requires a valid previewId.');
    if (typeof args.candidateKey !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(args.candidateKey)) throw new Error('Choose a valid candidate key.');
  }
}
export function globalPresetsReady(snapshot: NguiState | null, command: string, now = Date.now()): boolean {
  return canCommand(snapshot, command, now) && snapshot?.runtime.globalPresetsVersion === 2 && snapshot.runtime.globalNguWeightsVersion === 1 && typeof snapshot.runtime.startedAt === 'string' && !!snapshot.runtime.startedAt.trim();
}
export function globalPreviewMatches(preview: PresetPreview | null, args: JsonObject | null, runtimeDir: string, session: JsonValue | undefined, saved: boolean): boolean {
  return saved && !!preview && !!args && preview.runtimeDir === runtimeDir && preview.session === session && equal(preview.args, args);
}
export function presetRows(plan: JsonObject | null): JsonObject[] {
  return Array.isArray(plan?.LoadoutPresets) ? plan.LoadoutPresets.filter(isObject) : [];
}
export function validatePreset(row: JsonObject): void {
  row = normalizePreset(row);
  if (typeof row.Id !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(row.Id)) throw new Error('Preset Id must be 1..64 letters, digits, underscores or hyphens.');
  if (typeof row.Name !== 'string' || !row.Name.trim() || row.Name.length > 100) throw new Error('Preset name must contain 1..100 characters.');
  if (!Array.isArray(row.Weights) || row.Weights.length !== objectiveKeys.length) throw new Error(`Preset needs ${objectiveKeys.length} objective weights (legacy six, ten or twelve is accepted).`);
  row.Weights.forEach((weight, i) => numeric(weight, objectiveLabels[i], 0, 1e6, false));
  numeric(row.AdventureDynamicWeight, 'Dynamic adventure weight', 0, 1e6, false);
  if (!row.Weights.some(weight => Number(weight) > 0) && !(Number(row.AdventureDynamicWeight) > 0)) throw new Error('At least one objective weight must be positive.');
  numeric(row.MagicBloodPercent, 'Magic blood percent', -1, 100);
  if (row.SupportMode !== undefined && (typeof row.SupportMode !== 'string' || !['current', 'planned'].includes(row.SupportMode))) throw new Error('Choose current or planned support.');
  numeric(row.DiggerBudgetPercent, 'Digger budget percent', 0, 95);
  if (!presetZones.some(zone => zone.value === row.Zone)) throw new Error('Choose auto or a regular farming zone, not a titan or ITOPOD.');
  for (const key of ['MinimumPower', 'MinimumToughness']) numeric(row[key], key, 0, Number.MAX_VALUE, false);
  numeric(row.SwapSeconds, 'Swap seconds', 0, 3600);
  if (!Array.isArray(row.BeardPriority) || new Set(row.BeardPriority).size !== row.BeardPriority.length) throw new Error('Beard priority must be unique IDs 0..6 (empty means auto).');
  row.BeardPriority.forEach(id => numeric(id, 'Beard ID', 0, 6));
  if (row.BeardRotation !== undefined && row.BeardRotation !== null) {
    if (!Array.isArray(row.BeardRotation) || new Set(row.BeardRotation).size !== row.BeardRotation.length) throw new Error('Beard RNG pool must contain unique IDs 0..6.');
    row.BeardRotation.forEach(id => numeric(id, 'Beard RNG ID', 0, 6));
    if (row.BeardRotation.some(id => (row.BeardPriority as JsonValue[]).includes(id))) throw new Error('A beard cannot be both Enabled and RNG.');
  }
  if (!Array.isArray(row.DiggerWeights) || ![0, 12].includes(row.DiggerWeights.length)) throw new Error('Digger weights must be empty (auto) or exactly 12 numbers.');
  row.DiggerWeights.forEach(weight => numeric(weight, 'Digger weight', 0, 1e6, false));
  if (row.AutoUpdate !== undefined && typeof row.AutoUpdate !== 'boolean') throw new Error('AutoUpdate must be boolean.');
  if (typeof row.ResourcePrioritiesReviewed !== 'boolean') throw new Error('ResourcePrioritiesReviewed must be boolean.');
  if ((row.EnergyPriorities == null) !== (row.MagicPriorities == null) || row.ResourcePrioritiesReviewed && row.EnergyPriorities == null) throw new Error('EnergyPriorities and MagicPriorities must both be arrays or both null; reviewed priorities require both arrays.');
  for (const [key, tokens] of [['EnergyPriorities', energyLaneTokens], ['MagicPriorities', magicLaneTokens]] as const) {
    const values = row[key];
    if (values === null && !row.ResourcePrioritiesReviewed) continue;
    if (!Array.isArray(values) || values.length > tokens.length || new Set(values).size !== values.length || values.some(value => typeof value !== 'string' || !(tokens as readonly string[]).includes(value))) throw new Error(`${key} must contain unique uppercase activity tokens${row.ResourcePrioritiesReviewed ? ' (reviewed lanes require both arrays)' : ' or null'}.`);
  }
  if (row.AdvancedTrainingWeights !== undefined) {
    const weights = row.AdvancedTrainingWeights;
    if (!Array.isArray(weights) || weights.length !== 5) throw new Error('AdvancedTrainingWeights needs exactly 5 weights.');
    weights.forEach(weight => numeric(weight, 'AdvancedTrainingWeights'));
  }
}
export function validatePresetLibrary(value: JsonValue | undefined, limit = 32): void {
  if (value === undefined) return;
  if (!Array.isArray(value) || value.length > limit) throw new Error(`Preset library must be an array of at most ${limit} presets.`);
  const ids = new Set<string>();
  for (const row of value) {
    if (!isObject(row)) throw new Error('Each loadout preset must be an object.');
    validatePreset(row);
    if (ids.has(String(row.Id))) throw new Error('Preset IDs must be unique within the library.');
    ids.add(String(row.Id));
  }
}
export function presetArgs(row: JsonObject): JsonObject {
  validatePreset(row);
  // Unknown persisted fields stay in storage, not object IPC (which cannot carry lossless tokens).
  const normalized = normalizePreset(row);
  const preset = Object.fromEntries(presetFields.filter(key => normalized[key] !== undefined).map(key => [key, normalized[key]]));
  const args = cloneObject({ preset });
  // Defined doubles use backend double precision in object IPC, not lossless JSON token objects.
  for (const key of ['MinimumPower', 'MinimumToughness', 'AdventureDynamicWeight']) args.preset[key] = Number(args.preset[key]);
  for (const key of ['Weights', 'DiggerWeights']) args.preset[key] = (args.preset[key] as JsonValue[]).map(Number);
  validatePresetArgs('previewLoadoutPreset', args);
  return args;
}
export function validatePresetArgs(command: string, args: JsonObject): void {
  const keys = ['preset', ...(command === 'applyLoadoutPreset' ? ['candidateKey', 'previewId'] : [])];
  if (Object.keys(args).length !== keys.length || Object.keys(args).some(key => !keys.includes(key))) throw new Error(`${command} accepts exactly ${keys.join(', ')}.`);
  if (!isObject(args.preset)) throw new Error('Inline preset is required.');
  if ('EnergyNguWeights' in args.preset || 'MagicNguWeights' in args.preset) throw new Error('NGU weights are global settings, not preset overrides.');
  validatePreset(args.preset);
  if (command === 'applyLoadoutPreset' && (typeof args.candidateKey !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(args.candidateKey))) throw new Error('Choose a valid candidate key from the latest preview.');
  if (command === 'applyLoadoutPreset' && (typeof args.previewId !== 'string' || !/^[a-fA-F0-9]{32}$/.test(args.previewId))) throw new Error('Apply requires the server-owned 32-hex previewId from the latest preview.');
}
export interface PresetPreview {
  result: JsonObject;
  args: JsonObject;
  runtimeDir: string;
  session: string;
  preset?: JsonObject;
}
export function presetsReady(snapshot: NguiState | null, command: string, now = Date.now()): boolean {
  return canCommand(snapshot, command, now) && snapshot?.runtime.loadoutPresetsVersion === 2 && snapshot.runtime.globalNguWeightsVersion === 1 && typeof snapshot.runtime.startedAt === 'string' && !!snapshot.runtime.startedAt.trim();
}
export function readPresetPreview(response: JsonObject, args: JsonObject, preset?: JsonObject): JsonObject {
  const result = object(object(response.data).result);
  const row = object(args.preset);
  const global = typeof args.presetId === 'string';
  if (typeof result.previewId !== 'string' || !/^[a-fA-F0-9]{32}$/.test(result.previewId)) throw new Error('Preview response is missing a valid server-owned 32-hex previewId. Refresh preview.');
  const count = Array.isArray(result.objectiveKeys) ? result.objectiveKeys.length : 0;
  if (result.version !== 1 || result.presetId !== (global ? args.presetId : row.Id) || (!global && result.presetName !== row.Name) || typeof result.horizonSeconds !== 'number' || !Number.isFinite(result.horizonSeconds) || result.horizonSeconds <= 0 || !(global ? [10, 12, objectiveKeys.length] : [6, 10, 12, objectiveKeys.length]).includes(count) || !equal(result.objectiveKeys, objectiveKeys.slice(0, count)) || result.baselineKey !== 'current' || !Number.isFinite(Date.parse(String(result.capturedAt))) || !Array.isArray(result.candidates) || !result.candidates.length || !Array.isArray(result.specialists) || !Array.isArray(result.normalization) || result.normalization.length !== count) throw new Error('Preview response does not match the submitted preset contract. Refresh preview.');
  if (global && result.libraryRevision !== undefined && result.libraryRevision !== args.libraryRevision) throw new Error('Preview library revision changed. Refresh preview.');
  const dynamic = Number((preset ?? row).AdventureDynamicWeight) > 0;
  if (dynamic || result.effectiveWeights !== undefined) {
    if (!Array.isArray(result.effectiveWeights) || result.effectiveWeights.length !== Math.max(12, count) || result.effectiveWeights.some(weight => typeof weight !== 'number' || !Number.isFinite(weight) || weight < 0) || !result.effectiveWeights.some(weight => Number(weight) > 0)) throw new Error('Preview response is missing valid resolved adventure weights. Refresh preview.');
  }
  if (dynamic || result.adventureTask !== undefined || result.adventureTaskKey !== undefined) {
    if (typeof result.adventureTask !== 'string' || !result.adventureTask.trim() || typeof result.adventureTaskKey !== 'string' || !result.adventureTaskKey.trim() || dynamic && ![12, objectiveKeys.length].includes(count)) throw new Error('Preview response is missing a valid resolved adventure task. Refresh preview.');
  }
  const keys = new Set<string>();
  for (const candidate of result.candidates) {
    if (!isObject(candidate) || typeof candidate.key !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(candidate.key) || keys.has(candidate.key) || typeof candidate.feasible !== 'boolean' || !Array.isArray(candidate.metrics)) throw new Error('Preview candidates are malformed or ambiguous.');
    keys.add(candidate.key);
  }
  if (!keys.has('current')) throw new Error('Preview is missing the current setup baseline.');
  return result;
}
export function previewMatches(preview: PresetPreview | null, args: JsonObject | null, runtimeDir: string, session: JsonValue | undefined): boolean {
  return !!preview && !!args && preview.runtimeDir === runtimeDir && preview.session === session && equal(preview.args, args);
}
export function presetMetric(value: JsonValue | undefined, suffix = ''): string {
  if (value instanceof LosslessNumber) value = Number(value.value);
  return typeof value === 'number' && Number.isFinite(value) ? `${value.toLocaleString(undefined, { maximumSignificantDigits: 5 })}${suffix}` : 'Unknown';
}
export const resultRows = (value: JsonValue | undefined): JsonObject[] => Array.isArray(value) ? value.filter(isObject) : [];
export function relativeProduction(metric: JsonObject, baseline: JsonObject | undefined): string {
  if (metric.quality === 'unavailable') return 'Unknown';
  const value = metric.value instanceof LosslessNumber ? Number(metric.value.value) : metric.value;
  const before = baseline?.value instanceof LosslessNumber ? Number(baseline.value.value) : baseline?.value;
  if (typeof value === 'number' && Number.isFinite(value) && before === 0 && baseline?.quality !== 'unavailable') return value === 0 ? 'n/a' : value > 0 ? 'New' : 'Unknown';
  return presetMetric(metric.relativeToCurrent, 'x');
}
export function focusEfficiency(metric: JsonObject, specialists: JsonObject[]): string {
  // New captures carry frozen denominators. Explicit null/zero must never fall back to a new reference.
  if (['retainedProductionPercent', 'retainedProduction', 'specialistProduction', 'specialistReferenceKey'].some(key => Object.prototype.hasOwnProperty.call(metric, key))) {
    const reference = metric.specialistProduction instanceof LosslessNumber ? Number(metric.specialistProduction.value) : metric.specialistProduction;
    const percent = metric.retainedProductionPercent instanceof LosslessNumber ? Number(metric.retainedProductionPercent.value) : metric.retainedProductionPercent;
    if (metric.quality !== 'unavailable' && reference === 0) return 'n/a';
    return metric.quality !== 'unavailable' && typeof reference === 'number' && Number.isFinite(reference) && reference > 0 && typeof percent === 'number' && Number.isFinite(percent) && percent >= 0 ? presetMetric(percent, '%') : 'Unknown';
  }
  const values = specialists.filter(row => row.feasible === true).flatMap(row => resultRows(row.metrics)).filter(row => row.key === metric.key && row.quality !== 'unavailable' && typeof row.value === 'number' && Number.isFinite(row.value) && row.value > 0).map(row => row.value as number);
  if (metric.quality === 'unavailable' || typeof metric.value !== 'number' || !Number.isFinite(metric.value) || !values.length) return 'Unknown';
  return presetMetric(metric.value / Math.max(...values) * 100, '%');
}
