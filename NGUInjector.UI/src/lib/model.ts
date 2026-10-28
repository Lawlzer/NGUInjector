import { LosslessNumber } from 'lossless-json';
import { cloneObject, isObject, normalizeFightSchedules, planRevisionField, requirePlanRevision, stringifyJson, type JsonObject, type JsonValue, type NguiState } from './api';
import { validatePresetLibrary } from './presets';
import { isValidPriority } from './stage-presets';
import { titanZones } from './options';

export const INT_MAX = 2147483647;
export const focusNames = ['AugmentsBloodMagic', 'TimeMachine', 'Wandoos', 'EnergyNgu', 'MagicNgu', 'Ngu', 'AdvancedTraining', 'Adventure', 'Fight', 'ManualTitans'];
export const gearNames = ['EnergyCap', 'MagicCap', 'EnergyNgu', 'MagicNgu', 'AllNgu', 'TimeMachine', 'Blood', 'Gold', 'Drop', 'Fight', 'Augments', 'AdvancedTraining', 'Wandoos', 'ManualAdventure'];
export const fightNames = ['SqrtPowerToughness', 'PowerTimesToughness', 'PowerPlusToughness'];
export const builtInPlans = [
  { name: '24hr', label: '24h NGU' },
  { name: '15m-NUMBER-increase', label: '15m NUMBER' },
  { name: '30m-NUMBER-increase', label: '30m NUMBER' },
  { name: '1h-1m-NUMBER-increase', label: '1h1m NUMBER' }
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
  if (!Array.isArray(value) || value.some(token => !isValidPriority(token, energy) || token !== token.trim().toUpperCase())) {
    throw new Error(`${label}: use exact uppercase priority tokens, with no empty entries.`);
  }
}

export function stages(plan: JsonObject): JsonObject[] {
  if (!Array.isArray(plan.Stages) || plan.Stages.some(stage => !isObject(stage))) throw new Error('Stages must be an array of objects.');
  return plan.Stages as JsonObject[];
}

export function validatePlan(plan: JsonObject): void {
  normalizeFightSchedules(plan);
  validatePresetLibrary(plan.LoadoutPresets);
  if (plan.ActivePresetId !== undefined && (typeof plan.ActivePresetId !== 'string' || plan.ActivePresetId !== '' && !/^[A-Za-z0-9_-]{1,64}$/.test(plan.ActivePresetId))) throw new Error('ActivePresetId must be a global preset ID or empty for an existing legacy plan.');
  if (plan.Name !== undefined && (typeof plan.Name !== 'string' || !plan.Name.trim())) throw new Error('Name must be a nonempty string; the backend owns the file name.');
  numeric(plan.Version, 'Version (connect and reload to migrate legacy plans)', 14, 18);
  requirePlanRevision(plan);
  for (const key of ['AutoRebirthEnabled', 'AutoIronPillEnabled', 'TimeTriggerEnabled', 'NumberTriggerEnabled', 'NumberClimbEnabled', 'StageSchedulerEnabled', 'AutoActivateFruits', 'PrepRequireMaxedFruit']) {
    if (plan[key] !== undefined && typeof plan[key] !== 'boolean') throw new Error(`${key} must be boolean.`);
  }
  if (plan.AutoBloodSpellsEnabled != null && typeof plan.AutoBloodSpellsEnabled !== 'boolean') throw new Error('AutoBloodSpellsEnabled must be boolean or null.');
  for (const [key, max, integer] of [
    ['BloodSpaghettiGoalPercent', 100000, true], ['BloodCounterfeitGoalPercent', 100000, true],
    ['IronPillSaveSeconds', 604800, true], ['IronPillMinWorth', 1e8, false]
  ] as const) {
    if (plan[key] != null) numeric(plan[key], key, 0, max, integer);
  }
  for (const key of ['TimeTriggerSeconds', 'NumberClimbMinSeconds', 'RebirthPrepStartSeconds']) if (plan[key] !== undefined) numeric(plan[key], key);
  if (plan.StageDurationSeconds !== undefined) numeric(plan.StageDurationSeconds, 'StageDurationSeconds');
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
  rows.forEach((stage, index) => {
    const label = `Stage ${index + 1}`;
    if (stage.PresetId !== undefined && (typeof stage.PresetId !== 'string' || stage.PresetId !== '' && stage.PresetId !== '@active' && !/^[A-Za-z0-9_-]{1,64}$/.test(stage.PresetId))) throw new Error(`${label}: PresetId must be a global preset ID, @active, or empty for legacy inline settings.`);
    if (stage.PresetId === '@active' && !plan.ActivePresetId) throw new Error(`${label}: PresetId '@active' requires a nonempty plan.ActivePresetId. Choose an active global preset first.`);
    if (stage.ExecutionMode !== undefined) numeric(stage.ExecutionMode, `${label} ExecutionMode`, 0, 1);
    if (stage.AdventureDynamicWeight !== undefined) numeric(stage.AdventureDynamicWeight, `${label} AdventureDynamicWeight`, 0, 1e6, false);
    if (stage.ExecutionMode === 1 && (stage.CompletionMode ?? 0) !== 0) throw new Error(`${label}: automatic Augments + Wandoos requires a time budget, not AT target completion.`);
    if (stage.Name !== undefined && (typeof stage.Name !== 'string' || !stage.Name.trim())) throw new Error(`${label}: Name cannot be blank.`);
    const weight = numeric(stage.Percent ?? 100, `${label} Percent`, Number.MIN_VALUE, Number.MAX_VALUE, false);
    const fixed = numeric(stage.DurationSeconds ?? 0, `${label} DurationSeconds`, 0);
    if (stage.DurationUnit !== undefined && (typeof stage.DurationUnit !== 'string' || !['%', 's', 'min', 'h'].includes(stage.DurationUnit))) throw new Error(`${label}: choose seconds, minutes, hours, or %.`);
    if (stage.CompletionMode !== 1) {
      if (!fixed) weightTotal += weight;
      if (!Number.isFinite(weightTotal)) throw new Error('Stage weights must have a finite total.');
      fixedTotal += fixed;
      if (fixedTotal > INT_MAX) throw new Error('Total fixed stage time is too large.');
    }
    for (const key of ['EnergyFocus', 'MagicFocus']) if (stage[key] !== undefined) numeric(stage[key], `${label} ${key}`, 0, 7);
    if (stage.GearGoal !== undefined) numeric(stage.GearGoal, `${label} GearGoal`, -1, 13);
    if (stage.UseWandoosWhenTimeMachineLocked !== undefined && typeof stage.UseWandoosWhenTimeMachineLocked !== 'boolean') throw new Error(`${label} UseWandoosWhenTimeMachineLocked must be boolean.`);
    if (stage.CompletionMode !== undefined) numeric(stage.CompletionMode, `${label} CompletionMode`, 0, 1);
    if (stage.BlackBarLookaheadSeconds !== undefined) numeric(stage.BlackBarLookaheadSeconds, `${label} BlackBarLookaheadSeconds`, 3);
    for (const key of ['TargetAdvancedTrainingPower', 'TargetAdvancedTrainingToughness']) {
      if (stage[key] !== undefined || stage.CompletionMode === 1) longTarget(stage[key], `${label} ${key}`, stage.CompletionMode === 1);
    }
    validatePriorities(stage.EnergyPriorities, true, `${label} EnergyPriorities`);
    validatePriorities(stage.MagicPriorities, false, `${label} MagicPriorities`);
  });
  const horizon = plan.TimeTriggerEnabled === true && Number(plan.TimeTriggerSeconds) > 0 ? Number(plan.TimeTriggerSeconds) : Number(plan.StageDurationSeconds ?? 1800);
  const flexibleBudget = Math.max(0, horizon - fixedTotal);
  rows.forEach((stage, index) => {
    if (stage.ExecutionMode !== 1) return;
    // Match CustomPlanStore's nominal share, not the rounded schedule display.
    const fixed = Number(stage.DurationSeconds ?? 0);
    const budget = fixed > 0 ? fixed : flexibleBudget * (Number(stage.Percent ?? 100) / weightTotal);
    if (budget > 86400) throw new Error(`Stage ${index + 1}: automatic Augments + Wandoos effective stage budget must not exceed 86400 seconds (24 hours), including flexible allocation. Reduce its duration, weight, or plan horizon.`);
  });
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

export function validateMaxItopodPushArgs(command: string, args: JsonObject): void {
  if (command === 'cancelMaxItopodPush') {
    if (Object.keys(args).length) throw new Error('cancelMaxItopodPush does not accept args.');
    return;
  }
  if (Object.keys(args).some(key => key !== 'deaths')) throw new Error('startMaxItopodPush accepts only deaths.');
  numeric(args.deaths, 'Deaths', 1, 1000);
}

export function validateCombatPrepArgs(command: string, args: JsonObject): void {
  const keys = command === 'previewCombatPrep' ? ['durationSeconds', 'backgroundEnergyPercent', 'backgroundMagicPercent']
    : command === 'startCombatPrep' ? ['previewId', 'candidateId'] : command === 'cancelCombatPrep' ? ['runId'] : [];
  if (!keys.length || Object.keys(args).length !== keys.length || Object.keys(args).some(key => !keys.includes(key))) throw new Error(`${command} accepts exactly ${keys.join(', ')}.`);
  if (command === 'previewCombatPrep') {
    numeric(args.durationSeconds, 'Duration seconds', 1, 86400);
    for (const key of keys.slice(1)) {
      if (typeof args[key] !== 'number') throw new Error(`${key} must be a number.`);
      numeric(args[key], key, 0, 100, false);
    }
  } else for (const key of keys) {
    if (typeof args[key] !== 'string' || !args[key].trim() || args[key].length > 128) throw new Error(`${key} must be a nonempty string of at most 128 characters.`);
  }
}

export const autoExpRatioDefaults = {
  _customExpEnergyRatio: 5, _customExpMagicRatio: 1,
  _customExpEnergyPowerRatio: 5, _customExpEnergyCapRatio: 160000, _customExpEnergyBarsRatio: 4,
  _customExpMagicPowerRatio: 5, _customExpMagicCapRatio: 160000, _customExpMagicBarsRatio: 4
};

export const manualAdventureWeightCounts: Record<string, number> = {
  _manualAdventureGearWeights: 5, _manualAdventureBeardWeights: 7, _manualAdventureDiggerWeights: 12
};

export const settingsBounds: Record<string, [number, number, boolean?]> = {
  ...Object.fromEntries(Object.keys(autoExpRatioDefaults).map(key => [key, [1, 1000000000]])),
  _customAutoBoostCubePowerRatio: [1, 4], _customAugmentsBloodEstimateSeconds: [1, 100000],
  _customBloodSpaghettiGoalPercent: [0, 100000], _customBloodCounterfeitGoalPercent: [0, 100000],
  _customIronPillSaveSeconds: [0, 604800], _customIronPillMinWorth: [0, 1e8, false],
  _moneyPitThreshold: [0, Number.MAX_VALUE, false], _bloodNumberThreshold: [0, Number.MAX_VALUE, false], _customAdventureMode: [0, 3],
  _customAdventureMaxItopodDeaths: [1, 1000],
  _manualAdventureLoadout: [-1, 2], _manualTitanLoadout: [-1, 2], _manualTitanMaxDeaths: [1, 1000],
  _manualTitanZone: [6, 42],
  _customAdventureZone: [-1, 1000], _manualAdventureZone: [-1, 1000]
};

const integerSettings = new Set(['_snipeZone', '_combatMode', '_minorAbandonThreshold', '_questCombatMode', '_spaghettiThreshold', '_counterfeitThreshold', '_ironPillThreshold', '_bloodMacGuffinAThreshold', '_bloodMacGuffinBThreshold', '_cubePriority', '_resnipeTime', '_itopodCombatMode', '_yggSwapThreshold']);
const integerArrays = new Set(['_priorityBoosts', '_titanLoadout', '_yggdrasilLoadout', '_goldDropLoadout', '_moneyPitLoadout', '_boostBlacklist', '_quickLoadout', '_quickDiggers', '_wishPriorities', '_specialBoostBlacklist', '_blacklistedBosses']);
const booleanArrays = new Set(['_titanMoneyDone', '_titanGoldTargets', '_titanSwapTargets']);

export function validateSettings(patch: JsonObject, current: JsonObject): void {
  for (const [key, value] of Object.entries(patch)) {
    if (Object.prototype.hasOwnProperty.call(manualAdventureWeightCounts, key)) {
      const count = manualAdventureWeightCounts[key];
      if (!Array.isArray(value) || value.length !== count) throw new Error(`${key} must contain exactly ${count} weights.`);
      value.forEach((weight, index) => numeric(weight, `${key}[${index}]`, 0, 1e6, false));
      continue;
    }
    if (key === '_energyNguWeights' || key === '_magicNguWeights') {
      const count = key === '_energyNguWeights' ? 9 : 7;
      if (!Array.isArray(value) || value.length !== count) throw new Error(`${key} must contain exactly ${count} weights.`);
      for (const [index, weight] of value.entries()) numeric(weight, `${key}[${index}]`);
      continue;
    }
    if (key === '_customAdventureRules') {
      if (!Array.isArray(value) || value.length > 32) throw new Error(`${key} must be an array of at most 32 rules.`);
      const fields = ['Enabled', 'MinPower', 'MinToughness', 'MinRegen', 'Mode', 'Zone'];
      value.forEach((rule, index) => {
        const label = `${key}[${index}]`;
        if (!isObject(rule) || Object.keys(rule).length !== fields.length || fields.some(field => !Object.prototype.hasOwnProperty.call(rule, field))) throw new Error(`${label} must contain exactly ${fields.join(', ')}.`);
        if (typeof rule.Enabled !== 'boolean') throw new Error(`${label} Enabled must be boolean.`);
        for (const field of ['MinPower', 'MinToughness', 'MinRegen']) numeric(rule[field], `${label} ${field}`, 0, Number.MAX_VALUE, false);
        const mode = numeric(rule.Mode, `${label} Mode`, 0, 4);
        if (mode === 3) throw new Error(`${label} Mode must be 0, 1, 2, or 4.`);
        if (rule.Zone !== 1000) numeric(rule.Zone, `${label} Zone`, -1, 43);
      });
      continue;
    }
    if (key === '_customIronPillHistory') throw new Error(`${key} is backend-owned and read-only; preserve it unchanged.`);
    const bounds = settingsBounds[key];
    if (bounds) numeric(value, key, ...bounds);
    else if ((['_customAutoExpEnabled', '_manualTitanEnabled', '_manualTitanRequireAT'].includes(key) || typeof current[key] === 'boolean') && typeof value !== 'boolean') throw new Error(`${key} must be boolean.`);
    else if (integerSettings.has(key)) numeric(value, key, -INT_MAX - 1, INT_MAX);
    else if (typeof current[key] === 'number') numeric(value, key, -Number.MAX_VALUE, Number.MAX_VALUE, false);
    else if (typeof current[key] === 'string' && typeof value !== 'string') throw new Error(`${key} must be a string.`);
    if (key === '_customAdventureZone' || key === '_manualAdventureZone') {
      if (value !== 1000) numeric(value, key, -1, 43);
    }
    if (key === '_manualTitanZone' && !titanZones.some(zone => zone.value === value)) throw new Error(`${key} must be a titan zone.`);
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
  const lifecycle = object(state?.runtime.execution).lifecycle;
  // Older bridges omit lifecycle; an advertised non-running lifecycle follows injector admission.
  return (lifecycle === undefined || lifecycle === 'Running' || ['status', 'unload', 'restoreSaveTest'].includes(command))
    && (!['readPlan', 'savePlan', 'createPlan', 'setPlanStage'].includes(command) || planCapabilitiesReady(state))
    && heartbeat(state, now).connected && Number(state?.runtime.protocolVersion) >= 3 && Array.isArray(state?.runtime.supportedCommands) && state.runtime.supportedCommands.includes(command);
}

export function planCapabilitiesReady(state: NguiState | null): boolean {
  const runtime = state?.runtime;
  return runtime?.stageTimingVersion === 1 && runtime.fightScheduleVersion === 1 && typeof runtime.planVersion === 'number' && Number.isInteger(runtime.planVersion) && runtime.planVersion >= 18;
}

export function assertFightScheduleReadback(expected: JsonObject, actual: JsonObject): void {
  const schedules = (plan: JsonObject) => stages(normalizeFightSchedules(plan)).map(row => row.FightSchedule);
  if (!equal(schedules(expected), schedules(actual))) throw new Error('Saved fight schedules differ from readback. Edits retained; update or reconnect the injector before retrying.');
}

export function planDeletionReason(state: NguiState | null, name: string, now = Date.now(), patch: JsonObject = {}): string {
  if (!name) return 'Select a plan to delete.';
  const normalized = name.toLowerCase();
  if (builtInPlans.some(plan => plan.name.toLowerCase() === normalized)) return 'Built-in plans cannot be deleted. Copy one to create a removable plan.';
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
