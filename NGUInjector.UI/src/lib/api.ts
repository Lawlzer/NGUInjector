import { invoke } from '@tauri-apps/api/core';
import { LosslessNumber, isSafeNumber, parse, stringify } from 'lossless-json';

export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | LosslessNumber | JsonValue[] | { [key: string]: JsonValue };
export type JsonObject = { [key: string]: JsonValue };
export const planRevisionField = '__externalUiRevision';

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

// Settings and action args still use object IPC; a lossless scalar would otherwise become an object.
function requireObjectIpc(value: JsonValue): void {
  if (value instanceof LosslessNumber) throw new Error('Object IPC cannot carry an exact unsafe numeric token. Keep unknown settings values unchanged; lossless numeric edits are supported in plan JSON only.');
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
  return invoke<NguiState>('get_ngui_state', { runtimeDir });
}

export async function saveSettingsPatch(runtimeDir: string, patch: JsonObject): Promise<JsonObject> {
  requireObjectIpc(patch);
  return invoke<JsonObject>('save_settings_patch', { runtimeDir, patch });
}

export async function readPlan(runtimeDir: string, name: string): Promise<JsonObject> {
  const text = await invoke<string>('read_plan', { runtimeDir, name });
  if (typeof text !== 'string') throw new Error('read_plan must return a JSON string; update the native backend.');
  const plan = parseJson(text);
  if (!isObject(plan)) throw new Error('Plan JSON must be an object.');
  requirePlanRevision(plan);
  if (typeof plan.Version !== 'number' || !Number.isInteger(plan.Version) || plan.Version < 14) throw new Error('Legacy plan cannot be edited offline. Connect to a running protocol v3 injector and reload to apply normalized migrations.');
  return plan;
}

export async function savePlan(runtimeDir: string, name: string, plan: JsonObject): Promise<JsonObject> {
  requirePlanRevision(plan);
  return invoke<JsonObject>('save_plan', { runtimeDir, name, planJson: stringifyJson(plan) });
}

export async function createPlan(runtimeDir: string, requestedName: string, source?: JsonObject): Promise<string> {
  const copy = source ? cloneObject(source) : undefined;
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
