import { LosslessNumber } from 'lossless-json';
import { cloneObject, isObject, planRevisionField, requirePlanRevision, stringifyJson, type JsonObject, type JsonValue, type NguiState } from './api';

export const INT_MAX = 2147483647;
export const focusNames = ['AugmentsBloodMagic', 'TimeMachine', 'Wandoos', 'EnergyNgu', 'MagicNgu', 'Ngu', 'AdvancedTraining', 'Adventure', 'Fight', 'ManualTitans'];
export const gearNames = ['EnergyCap', 'MagicCap', 'EnergyNgu', 'MagicNgu', 'AllNgu', 'TimeMachine', 'Blood', 'Gold', 'Drop', 'Fight', 'Augments', 'AdvancedTraining', 'Wandoos', 'ManualAdventure'];
export const fightNames = ['SqrtPowerToughness', 'PowerTimesToughness', 'PowerPlusToughness'];
export const builtInPlans = [
  { name: 'default', label: 'Default' }, { name: '24hr', label: '24h' },
  { name: '1h-3m-rebirth', label: '1h / 3m' }, { name: 'infinite', label: 'Infinite' }
];
export function equal(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (a instanceof LosslessNumber || b instanceof LosslessNumber) {
    return (a instanceof LosslessNumber || typeof a === 'number') && (b instanceof LosslessNumber || typeof b === 'number') && String(a) === String(b);
  }
  if (!a || !b || typeof a !== 'object' || typeof b !== 'object' || Array.isArray(a) !== Array.isArray(b)) return false;
  const left = a as Record<string, unknown>;
  const right = b as Record<string, unknown>;
  const keys = Object.keys(left);
  return keys.length === Object.keys(right).length && keys.every(key => Object.prototype.hasOwnProperty.call(right, key) && equal(left[key], right[key]));
}
export const object = (value: JsonValue | undefined): JsonObject => isObject(value) ? value : {};
export const pretty = (value: unknown): string => stringifyJson(value ?? {}, 2);

const planMetadata = ['Name', 'RebirthHistory', 'Version', planRevisionField];
export function assertPlanMetadata(base: JsonObject, draft: JsonObject): void {
  for (const key of planMetadata) if (!equal(base[key], draft[key])) throw new Error(`${key} is backend-owned; preserve it unchanged and reload to obtain current normalized metadata.`);
}

export function numeric(value: unknown, label: string, min = 0, max = INT_MAX, integer = true): number {
  // Known C# doubles may be large; validate their range without altering the stored JSON token.
  if (!integer && value instanceof LosslessNumber) value = Number(value.value);
  if (typeof value !== 'number' || !Number.isFinite(value) || value < min || value > max || (integer && !Number.isInteger(value))) {
    throw new Error(`${label} must be a finite ${integer ? 'integer' : 'number'} between ${min} and ${max}.`);
  }
  return value;
}

export function parseNumber(text: string, label: string, min = 0, max = INT_MAX, integer = true): number {
  if (!text.trim()) throw new Error(`${label} is required; blank is not zero.`);
  return numeric(Number(text), label, min, max, integer);
}

// C# serializes Int64 targets as strings. Keep them exact, including values above JS's safe integer range.
export function longTarget(value: unknown, label: string, positive = false): string {
  if (typeof value === 'number' && !Number.isSafeInteger(value)) throw new Error(`${label}: use a quoted decimal integer for large targets.`);
  const text = String(value);
  if (!/^\d+$/.test(text) || BigInt(text) > 9223372036854775807n || (positive && BigInt(text) === 0n)) {
    throw new Error(`${label} must be a ${positive ? 'positive' : 'non-negative'} decimal Int64 integer.`);
  }
  return text;
}

export function validatePriorities(value: JsonValue | undefined, energy: boolean, label: string): void {
  if (value === undefined || value === null) return;
  const pattern = energy ? /^(WAN|TM|NGU(?:-[0-8])?|AUG(?:-(?:[0-9]|1[0-3]))?|AT(?:-[0-4])?)$/ : /^(WAN|TM|NGU(?:-[0-6])?|RIT(?:-[0-7])?)$/;
  if (!Array.isArray(value) || value.some(token => typeof token !== 'string' || !pattern.test(token))) {
    throw new Error(`${label}: use exact uppercase priority tokens, with no empty entries.`);
  }
}

export function stages(plan: JsonObject): JsonObject[] {
  if (!Array.isArray(plan.Stages) || plan.Stages.some(stage => !isObject(stage))) throw new Error('Stages must be an array of objects.');
  return plan.Stages as JsonObject[];
}

export function validatePlan(plan: JsonObject): void {
  if (plan.Name !== undefined && (typeof plan.Name !== 'string' || !plan.Name.trim())) throw new Error('Name must be a nonempty string; the backend owns the file name.');
  numeric(plan.Version, 'Version (connect and reload to migrate legacy plans)', 14);
  if (Number(plan.Version) < 16 && ['EnergyNguWeights', 'MagicNguWeights'].some(key => Object.prototype.hasOwnProperty.call(plan, key))) {
    throw new Error('NGU weight fields require a version 16 plan. Connect to the updated injector and reload the plan.');
  }
  if (Number(plan.Version) >= 16) {
    for (const [key, length] of [['EnergyNguWeights', 9], ['MagicNguWeights', 7]] as const) {
      const weights = plan[key];
      if (!Array.isArray(weights) || weights.length !== length) throw new Error(`${key} must contain exactly ${length} weights. Connect and reload the plan.`);
      weights.forEach((weight, index) => numeric(weight, `${key}[${index}]`));
    }
  }
  requirePlanRevision(plan);
  for (const key of ['AutoRebirthEnabled', 'TimeTriggerEnabled', 'NumberTriggerEnabled', 'NumberClimbEnabled', 'StageSchedulerEnabled', 'AutoActivateFruits', 'PrepRequireMaxedFruit']) {
    if (plan[key] !== undefined && typeof plan[key] !== 'boolean') throw new Error(`${key} must be boolean.`);
  }
  for (const key of ['TimeTriggerSeconds', 'NumberClimbMinSeconds']) if (plan[key] !== undefined) numeric(plan[key], key);
  if (plan.StageDurationSeconds !== undefined) numeric(plan.StageDurationSeconds, 'StageDurationSeconds', 1);
  for (const [key, min, max] of [['TriggerMode', 0, 1], ['GearGoal', 0, 13], ['FightScoreMode', 0, 2]] as const) {
    if (plan[key] !== undefined) numeric(plan[key], key, min, max);
  }
  for (const [key, min] of [['NumberMultiplierTarget', 0], ['NumberClimbMultiplierTarget', 1]] as const) {
    if (plan[key] !== undefined) numeric(plan[key], key, min, Number.MAX_VALUE, false);
  }
  const rows = stages(plan);
  if (!rows.length) throw new Error('At least one stage is required; C# replaces empty stages with defaults.');
  if (plan.StageLoopStartIndex !== undefined) numeric(plan.StageLoopStartIndex, 'StageLoopStartIndex', -1, rows.length - 1);
  let fixedTotal = 0;
  let weightTotal = 0;
  let weightedStages = 0;
  rows.forEach((stage, index) => {
    const label = `Stage ${index + 1}`;
    if (stage.Name !== undefined && (typeof stage.Name !== 'string' || !stage.Name.trim())) throw new Error(`${label}: Name cannot be blank.`);
    const weight = numeric(stage.Percent ?? 100, `${label} Percent`, Number.MIN_VALUE, Number.MAX_VALUE, false);
    const fixed = numeric(stage.DurationSeconds ?? 0, `${label} DurationSeconds`, 0);
    if (fixed && Number(plan.Version) < 15) throw new Error('Reload this plan with the updated injector before using fixed durations.');
    if (stage.DurationUnit !== undefined && (typeof stage.DurationUnit !== 'string' || !['%', 's', 'min', 'h'].includes(stage.DurationUnit))) throw new Error(`${label}: choose seconds, minutes, hours, or %.`);
    if (stage.CompletionMode !== 1) {
      weightTotal += weight;
      if (!Number.isFinite(weightTotal)) throw new Error('Stage weights must have a finite total.');
      fixedTotal += fixed;
      if (fixedTotal > INT_MAX) throw new Error('Total fixed stage time is too large.');
      if (!fixed) weightedStages++;
    }
    for (const key of ['EnergyFocus', 'MagicFocus']) if (stage[key] !== undefined) numeric(stage[key], `${label} ${key}`, 0, 7);
    if (stage.GearGoal !== undefined) numeric(stage.GearGoal, `${label} GearGoal`, -1, 13);
    if (stage.FightOnEntry !== undefined && typeof stage.FightOnEntry !== 'boolean') throw new Error(`${label} FightOnEntry must be boolean.`);
    if (stage.CompletionMode !== undefined) numeric(stage.CompletionMode, `${label} CompletionMode`, 0, 1);
    if (stage.BlackBarLookaheadSeconds !== undefined) numeric(stage.BlackBarLookaheadSeconds, `${label} BlackBarLookaheadSeconds`, 3);
    for (const key of ['TargetAdvancedTrainingPower', 'TargetAdvancedTrainingToughness']) {
      if (stage[key] !== undefined || stage.CompletionMode === 1) longTarget(stage[key], `${label} ${key}`, stage.CompletionMode === 1);
    }
    validatePriorities(stage.EnergyPriorities, true, `${label} EnergyPriorities`);
    validatePriorities(stage.MagicPriorities, false, `${label} MagicPriorities`);
  });
  if (weightedStages && fixedTotal >= Number(plan.StageDurationSeconds ?? 1800)) throw new Error('Schedule length must leave time for % stages.');
  if (plan.GoalPresets !== undefined) {
    if (!Array.isArray(plan.GoalPresets) || !plan.GoalPresets.length) throw new Error('GoalPresets must contain at least one preset.');
    plan.GoalPresets.forEach((preset, index) => {
      if (!isObject(preset) || typeof preset.Name !== 'string' || !preset.Name.trim()) throw new Error(`Goal preset ${index + 1} needs a name.`);
      numeric(preset.GearGoal, `Goal preset ${index + 1} GearGoal`, 0, 13);
    });
    const index = numeric(plan.ActiveGoalPresetIndex ?? 0, 'ActiveGoalPresetIndex', 0, plan.GoalPresets.length - 1);
    if (plan.GearGoal !== undefined && plan.GearGoal !== object(plan.GoalPresets[index]).GearGoal) throw new Error('GearGoal must match the active GoalPresets entry. Edit the preset gear goal.');
  } else if (plan.ActiveGoalPresetIndex !== undefined) numeric(plan.ActiveGoalPresetIndex, 'ActiveGoalPresetIndex (implicit Default preset)', 0, 0);
}

export function validateAugmentArgs(args: JsonObject): void {
  if (Object.keys(args).some(key => key !== 'expectedSeconds')) throw new Error('Augment commands accept only the optional expectedSeconds argument.');
  if (Object.prototype.hasOwnProperty.call(args, 'expectedSeconds')) numeric(args.expectedSeconds, 'expectedSeconds', 1);
}

export const settingsBounds: Record<string, [number, number, boolean?]> = {
  _customAutoBoostCubePowerRatio: [1, 4], _customAugmentsBloodEstimateSeconds: [1, 100000],
  _customBloodSpaghettiGoalPercent: [0, 100000], _customBloodCounterfeitGoalPercent: [0, 100000],
  _customIronPillSaveSeconds: [0, 604800], _customIronPillMinWorth: [0, 1e8, false],
  _moneyPitThreshold: [0, Number.MAX_VALUE, false], _bloodNumberThreshold: [0, Number.MAX_VALUE, false], _customAdventureMode: [0, 2],
  _customAdventureZone: [-1, 1000], _manualAdventureZone: [-1, 1000]
};

const integerSettings = new Set(['_snipeZone', '_combatMode', '_minorAbandonThreshold', '_questCombatMode', '_spaghettiThreshold', '_counterfeitThreshold', '_ironPillThreshold', '_bloodMacGuffinAThreshold', '_bloodMacGuffinBThreshold', '_cubePriority', '_resnipeTime', '_itopodCombatMode', '_yggSwapThreshold']);
const integerArrays = new Set(['_priorityBoosts', '_titanLoadout', '_yggdrasilLoadout', '_goldDropLoadout', '_moneyPitLoadout', '_boostBlacklist', '_quickLoadout', '_quickDiggers', '_wishPriorities', '_specialBoostBlacklist', '_blacklistedBosses']);
const booleanArrays = new Set(['_titanMoneyDone', '_titanGoldTargets', '_titanSwapTargets']);

export function validateSettings(patch: JsonObject, current: JsonObject): void {
  for (const [key, value] of Object.entries(patch)) {
    if (key === '_customIronPillHistory') throw new Error(`${key} is backend-owned and read-only; preserve it unchanged.`);
    const bounds = settingsBounds[key];
    if (bounds) numeric(value, key, ...bounds);
    else if (typeof current[key] === 'boolean' && typeof value !== 'boolean') throw new Error(`${key} must be boolean.`);
    else if (integerSettings.has(key)) numeric(value, key, -INT_MAX - 1, INT_MAX);
    else if (typeof current[key] === 'number') numeric(value, key, -Number.MAX_VALUE, Number.MAX_VALUE, false);
    else if (typeof current[key] === 'string' && typeof value !== 'string') throw new Error(`${key} must be a string.`);
    if (key === '_customAdventureZone' || key === '_manualAdventureZone') {
      if (value !== 1000) numeric(value, key, -1, 43);
    }
    if (key === '_customPlanFile' || key === '_allocationFile') validateName(String(value));
    if (integerArrays.has(key) || booleanArrays.has(key)) {
      if (!Array.isArray(value)) throw new Error(`${key} must be an array.`);
      for (const item of value) {
        if (integerArrays.has(key)) numeric(item, key, -INT_MAX - 1, INT_MAX);
        else if (typeof item !== 'boolean') throw new Error(`${key} entries must be booleans.`);
      }
      if (booleanArrays.has(key) && Array.isArray(current[key]) && value.length !== current[key].length) throw new Error(`${key} must retain one entry per titan.`);
    }
  }
}

export function validateName(name: string): void {
  if (!name || name !== name.trim() || name.endsWith('.') || name.length > 120 || /[\x00-\x1f<>:"/\\|?*]/.test(name) || /^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[1-9]|LPT[1-9])(?:\.|$)/i.test(name)) throw new Error('Use a nonempty, safe Windows file name (maximum 120 characters).');
}

export function diffObject(base: JsonObject, draft: JsonObject): JsonObject {
  let patch: JsonObject = {};
  for (const [key, value] of Object.entries(draft)) {
    if (equal(base[key], value)) continue;
    patch = { ...patch, [key]: isObject(value) && isObject(base[key]) ? diffObject(base[key], value) : value };
  }
  return patch;
}

export function mergePatch(base: JsonObject, patch: JsonObject): JsonObject {
  let next = cloneObject(base);
  for (const [key, value] of Object.entries(patch)) next = { ...next, [key]: isObject(value) && isObject(next[key]) ? mergePatch(next[key], value) : value };
  return next;
}

// Reject overlapping external edits instead of silently overwriting another writer. Arrays are atomic.
export function freshEdits(base: JsonObject, draft: JsonObject, fresh: JsonObject, omit: string[] = []): JsonObject {
  const patch = diffObject(base, draft);
  for (const key of omit) delete patch[key];
  function conflicts(before: JsonObject, changes: JsonObject, latest: JsonObject, path = ''): void {
    for (const [key, value] of Object.entries(changes)) {
      if (isObject(value) && isObject(before[key]) && isObject(latest[key])) conflicts(before[key], value, latest[key], `${path}${key}.`);
      else if (!equal(before[key], latest[key]) && !equal(value, latest[key])) throw new Error(`Source changed at ${path}${key}. Reload before applying/saving; local edits are retained.`);
    }
  }
  conflicts(base, patch, fresh);
  return mergePatch(fresh, patch);
}

export const freshPlan = (base: JsonObject, draft: JsonObject, fresh: JsonObject): JsonObject => freshEdits(base, draft, fresh, planMetadata);

export function pendingPlanEdits(base: JsonObject, draft: JsonObject): JsonObject {
  const edits = diffObject(base, draft);
  for (const key of planMetadata) delete edits[key];
  return edits;
}

export function reorderStages(plan: JsonObject, from: number, to: number | null): JsonObject {
  const rows = [...stages(plan)];
  const loop = Number(plan.StageLoopStartIndex ?? -1);
  const order = rows.map((_, i) => i);
  if (from < 0 || from >= rows.length || (to !== null && (to < 0 || to >= rows.length))) return plan;
  const [row] = rows.splice(from, 1);
  const [id] = order.splice(from, 1);
  if (to !== null) { rows.splice(to, 0, row); order.splice(to, 0, id); }
  // Follow the boundary stage when moved; when deleted use its successor, otherwise disable looping.
  const boundary = loop === from && to === null ? loop + 1 : loop;
  return { ...plan, Stages: rows, StageLoopStartIndex: loop < 0 ? -1 : order.indexOf(boundary) };
}

export function heartbeat(state: NguiState | null, now = Date.now()): { connected: boolean; age: number | null } {
  const time = Date.parse(String(state?.runtime.updatedAt ?? ''));
  const age = Number.isFinite(time) ? now - time : null;
  return { connected: !!state?.connected && state.runtime.externalUiEnabled === true && state.runtime.status === 'running' && age !== null && age >= -2000 && age <= 5000, age };
}

export function canCommand(state: NguiState | null, command: string, now = Date.now()): boolean {
  return heartbeat(state, now).connected && Number(state?.runtime.protocolVersion) >= 3 && Array.isArray(state?.runtime.supportedCommands) && state.runtime.supportedCommands.includes(command);
}

export function planDeletionReason(state: NguiState | null, name: string, now = Date.now(), patch: JsonObject = {}): string {
  if (!name) return 'Select a plan to delete.';
  const normalized = name.toLowerCase();
  if (builtInPlans.some(plan => plan.name === normalized)) return 'Built-in plans cannot be deleted. Copy one to create a removable plan.';
  if (state && state.plans.length <= 1) return 'The last plan cannot be deleted.';
  if (typeof patch._customPlanFile === 'string' && patch._customPlanFile.toLowerCase() === normalized) return 'Save or discard the pending selection of this plan before deleting it.';
  if (heartbeat(state, now).connected) {
    const settings = object(state?.runtime.settings);
    if (settings.useCustomRebirthPlan === true && String(settings.customPlanFile ?? '').toLowerCase() === normalized) return 'The active running plan cannot be deleted. Stop it first.';
    if (!canCommand(state, 'deletePlan', now)) return 'The connected injector does not support plan deletion. Refresh or update it first.';
  }
  return '';
}

export async function pollResult(id: string, command: string, read: () => Promise<JsonObject | null>, timeout = 12000, delay = 150): Promise<JsonObject> {
  const deadline = Date.now() + timeout;
  const timedOut = () => new Error(`${command} timed out. Outcome may be unknown if already running; refresh before retrying.`);
  while (Date.now() <= deadline) {
    let timer: ReturnType<typeof setTimeout> | undefined;
    let result: JsonObject | null;
    try {
      result = await Promise.race([read(), new Promise<never>((_, reject) => { timer = setTimeout(() => reject(timedOut()), Math.max(0, deadline - Date.now())); })]);
    } finally { clearTimeout(timer); }
    if (result) {
      if (result.id !== id || result.command !== command || typeof result.success !== 'boolean') throw new Error('Command result does not match the request.');
      if (!result.success) throw new Error(String(result.message ?? `${command} failed.`));
      return result;
    }
    await new Promise(resolve => setTimeout(resolve, delay));
  }
  throw timedOut();
}
