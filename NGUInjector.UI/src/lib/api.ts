import { invoke } from '@tauri-apps/api/core';
import { LosslessNumber, isSafeNumber, parse, stringify } from 'lossless-json';

export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | LosslessNumber | JsonValue[] | { [key: string]: JsonValue };
export type JsonObject = { [key: string]: JsonValue };
export type PlanStageAction = 'skip' | 'set' | 'restart';
export const planRevisionField = '__externalUiRevision';
export const planVersion = 18;

export function normalizeFightSchedules(plan: JsonObject): JsonObject {
  if (typeof plan.Version !== 'number' || !Number.isInteger(plan.Version) || plan.Version < 14 || plan.Version > planVersion) throw new Error('Unsupported plan Version. Update the controller or connect to migrate legacy plans.');
  const ids = new Set<string>();
  const rows = Array.isArray(plan.Stages) ? plan.Stages.map((row, index) => {
    if (!isObject(row)) throw new Error('Stages must be an array of objects.');
    const label = `Stage ${index + 1}`;
    if ('FightOnEntry' in row && typeof row.FightOnEntry !== 'boolean') throw new Error(`${label} FightOnEntry must be boolean.`);
    const schedule = 'FightSchedule' in row ? row.FightSchedule : row.FightOnEntry === true
      ? { Id: `legacy-entry-v1-${index}`, Anchor: 0, DelaySeconds: 0, IntervalSeconds: 0 } : null;
    if (schedule !== null) {
      if (!isObject(schedule)) throw new Error(`${label} FightSchedule must be an object or null.`);
      if (typeof schedule.Id !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(schedule.Id)) throw new Error(`${label} FightSchedule Id must contain 1..128 ASCII letters, digits, underscores or hyphens.`);
      if (ids.has(schedule.Id)) throw new Error('FightSchedule IDs must be unique within the plan.');
      ids.add(schedule.Id);
      for (const key of ['Anchor', 'DelaySeconds', 'IntervalSeconds']) {
        const value = schedule[key];
        if (typeof value !== 'number' || !Number.isInteger(value) || value < 0 || value > (key === 'Anchor' ? 1 : 2147483647)) throw new Error(`${label} FightSchedule ${key} must be a nonnegative Int32${key === 'Anchor' ? ' (0 or 1)' : ''}.`);
      }
    }
    if (!('FightOnEntry' in row) && row.FightSchedule === schedule) return row;
    const next: JsonObject = { ...row, FightSchedule: schedule };
    delete next.FightOnEntry;
    return next;
  }) : plan.Stages;
  return plan.Version === planVersion && (!Array.isArray(rows) || rows.every((row, index) => row === (plan.Stages as JsonValue[])[index]))
    ? plan : { ...plan, Version: planVersion, ...(rows !== undefined ? { Stages: rows } : {}) };
}

export function requireCanonicalPlan(plan: JsonObject): JsonObject {
  if (plan.Version !== planVersion) throw new Error('Plan readback requires version 18 fight schedule support. Update and reconnect the injector.');
  return normalizeFightSchedules(plan);
}

// Migration belongs to the backend. Retired overrides must never re-enter a saved definition.
export function stripNguOverrides(value: JsonObject): JsonObject {
  const next = { ...value };
  let changed = ['NguGoal', 'EnergyNguWeights', 'MagicNguWeights'].some(key => Object.prototype.hasOwnProperty.call(value, key));
  delete next.NguGoal;
  delete next.EnergyNguWeights;
  delete next.MagicNguWeights;
  for (const key of ['Stages', 'LoadoutPresets', 'GoalPresets', 'Presets']) {
    const rows = next[key];
    if (!Array.isArray(rows)) continue;
    const cleaned = rows.map(row => isObject(row) ? stripNguOverrides(row) : row);
    if (cleaned.some((row, index) => row !== rows[index])) {
      next[key] = cleaned;
      changed = true;
    }
  }
  // Stage editors use clean row identities to retain selection and local drafts.
  return changed ? next : value;
}

export function parseJson(text: string): JsonValue {
  return parse(text, undefined, (token: string) => isSafeNumber(token) ? Number(token) : new LosslessNumber(token)) as JsonValue;
}

export function stringifyJson(value: unknown, space?: number): string {
  const text = stringify(value, undefined, space);
  if (text === undefined) throw new Error('Value has no JSON representation.');
  return text;
}

export function requirePlanRevision(plan: JsonObject): void {
  const revision = plan[planRevisionField];
  if (typeof revision !== 'string' || !/^[a-f0-9]{64}$/.test(revision)) throw new Error('Plan requires a valid SHA-256 revision. Reload the plan before saving.');
}

// Action args still use object IPC; a lossless scalar would otherwise become an object.
function requireObjectIpc(value: JsonValue): void {
  if (value instanceof LosslessNumber) throw new Error('Object IPC cannot carry an exact unsafe numeric token.');
  if (Array.isArray(value)) value.forEach(requireObjectIpc);
  else if (isObject(value)) Object.values(value).forEach(requireObjectIpc);
}

export interface LogFile {
  name: string;
  size: number;
  modified_epoch_ms?: number | null;
}

export interface CommandResultSummary {
  id: string;
  command: string;
  success: boolean;
  message: string;
  completed_at: string;
}

export interface NguiState {
  runtime_dir: string;
  marker_enabled: boolean;
  connected: boolean;
  protocol: JsonObject;
  settings: JsonObject;
  settings_revision?: string;
  runtime: JsonObject;
  profiles: string[];
  plans: string[];
  logs: LogFile[];
  command_results: CommandResultSummary[];
}

export const runtimeDirStorageKey = 'nguinjector.ui.runtimeDir';

export function getStoredRuntimeDir(): string | undefined {
  return localStorage.getItem(runtimeDirStorageKey) || undefined;
}

export function storeRuntimeDir(path: string): void {
  localStorage.setItem(runtimeDirStorageKey, path);
}

export async function getState(runtimeDir?: string): Promise<NguiState> {
  const state = await invoke<Omit<NguiState, 'settings'> & { settings_json: string }>('get_ngui_state', { runtimeDir });
  if (typeof state.settings_json !== 'string') throw new Error('Settings read requires lossless JSON transport; update the native backend.');
  const settings = parseJson(state.settings_json);
  if (!isObject(settings)) throw new Error('Settings JSON must be an object.');
  requireSettingsRevision(state.settings_revision);
  return { ...state, settings };
}

export function requireSettingsRevision(revision: unknown): asserts revision is string {
  if (typeof revision !== 'string' || revision !== '' && !/^[a-f0-9]{64}$/.test(revision)) throw new Error('Settings require an explicit revision. Reload settings before saving.');
}

export function committedSettings(result: JsonObject, keys: string[] = []): { settings: JsonObject; revision: string } {
  const data = isObject(result.data) ? result.data : isObject(result.result) && isObject(result.result.data) ? result.result.data : {};
  if (result.success !== true || !isObject(data.settings) || keys.some(key => !Object.prototype.hasOwnProperty.call(data.settings, key)) || typeof data.revision !== 'string' || !/^[a-f0-9]{64}$/.test(data.revision)) throw new Error('Settings save did not return a committed snapshot and revision. Outcome may be unknown; edits retained.');
  return { settings: data.settings, revision: data.revision };
}

export async function saveSettingsPatch(runtimeDir: string, patch: JsonObject, expectedRevision: string): Promise<JsonObject> {
  requireSettingsRevision(expectedRevision);
  const text = await invoke<string>('save_settings_patch_json', { runtimeDir, patchJson: stringifyJson(patch), expectedRevision });
  if (typeof text !== 'string') throw new Error('Settings save requires lossless JSON transport; update the native backend.');
  const result = parseJson(text);
  if (!isObject(result)) throw new Error('Settings result must be an object.');
  committedSettings(result, Object.keys(patch));
  return result;
}

export async function readPlan(runtimeDir: string, name: string): Promise<JsonObject> {
  const text = await invoke<string>('read_plan', { runtimeDir, name });
  if (typeof text !== 'string') throw new Error('read_plan must return a JSON string; update the native backend.');
  const plan = parseJson(text);
  if (!isObject(plan)) throw new Error('Plan JSON must be an object.');
  requirePlanRevision(plan);
  return stripNguOverrides(requireCanonicalPlan(plan));
}

export async function savePlan(runtimeDir: string, name: string, plan: JsonObject): Promise<JsonObject> {
  requirePlanRevision(plan);
  return invoke<JsonObject>('save_plan', { runtimeDir, name, planJson: stringifyJson(stripNguOverrides(normalizeFightSchedules(plan))) });
}

export async function readPresetLibrary(runtimeDir: string): Promise<JsonObject> {
  const text = await invoke<string>('read_preset_library', { runtimeDir });
  if (typeof text !== 'string') throw new Error('read_preset_library must return a JSON string; update the native backend.');
  const library = parseJson(text);
  if (!isObject(library) || library.Version !== 1 || !Array.isArray(library.Presets)) throw new Error('Preset library must contain Version 1 and Presets.');
  requirePlanRevision(library);
  return stripNguOverrides(library);
}

export async function savePresetLibrary(runtimeDir: string, library: JsonObject): Promise<JsonObject> {
  requirePlanRevision(library);
  return invoke<JsonObject>('save_preset_library', { runtimeDir, libraryJson: stringifyJson(stripNguOverrides(library)) });
}

export async function createPlan(runtimeDir: string, requestedName: string, source?: JsonObject): Promise<string> {
  const copy = source ? stripNguOverrides(normalizeFightSchedules(cloneObject(source))) : undefined;
  if (copy) delete copy[planRevisionField];
  return invoke<string>('create_plan', { runtimeDir, requestedName, sourceJson: copy ? stringifyJson(copy) : undefined });
}

export async function deletePlan(runtimeDir: string, name: string): Promise<JsonObject> {
  return invoke<JsonObject>('delete_plan', { runtimeDir, name });
}

export async function enqueueCommand(runtimeDir: string, command: string, args: JsonObject = {}): Promise<string> {
  requireObjectIpc(args);
  return invoke<string>('enqueue_command', { runtimeDir, envelope: { command, args } });
}

export async function readCommandResult(runtimeDir: string, id: string): Promise<JsonObject | null> {
  return invoke<JsonObject | null>('read_command_result', { runtimeDir, id });
}

// Runtime is additive to action data, not a replacement for gear/augment metrics.
export function commandRuntime(result: JsonObject): JsonObject | null {
  return isObject(result.data) && isObject(result.data.runtime) ? result.data.runtime : null;
}

export function runtimeRevision(runtime: JsonObject): number | null {
  const value = runtime.runtimeRevision;
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;
}

export async function tailLog(runtimeDir: string, name: string, maxLines = 250): Promise<string> {
  return invoke<string>('tail_log', { runtimeDir, name, maxLines });
}

export async function setExternalUiEnabled(runtimeDir: string, enabled: boolean): Promise<void> {
  await invoke('set_external_ui_enabled', { runtimeDir, enabled });
}

export function isObject(value: unknown): value is JsonObject {
  return !!value && typeof value === 'object' && !Array.isArray(value) && !(value instanceof LosslessNumber);
}

export function cloneObject<T extends JsonObject>(value: T): T {
  return parseJson(stringifyJson(value)) as T;
}
