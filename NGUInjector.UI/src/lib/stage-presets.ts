import type { JsonObject } from './api';
import activityPolicies from '../../../NGUInjector/Data/ActivityPolicies.json';

export const automaticStageLabel = 'Augments + Wandoos (automatic, includes AT)';

export function isValidPriority(token: unknown, energy: boolean): token is string {
  if (typeof token !== 'string') return false;
  const parts = token.trim().toUpperCase().split('-');
  const feature = activityPolicies.features.find(feature => feature.token === parts[0]);
  const targets = feature?.[energy ? 'energyTargets' : 'magicTargets'] ?? -1;
  if (targets < 0) return false;
  return parts.length === 1 || (parts.length === 2 && /^(0|[1-9][0-9]*)$/.test(parts[1]) && Number(parts[1]) < targets);
}

export function resolvePrioritySet(key: unknown, energy: boolean, sets: Readonly<Record<string, unknown>> = activityPolicies.prioritySets): string[] {
  if (typeof key !== 'string') throw new Error('Expected a priority-set key string; inline arrays are not supported.');
  if (!Object.prototype.hasOwnProperty.call(sets, key)) throw new Error(`Unknown priority-set key: ${key}`);
  const priorities = sets[key];
  if (!Array.isArray(priorities) || priorities.some(token => !isValidPriority(token, energy) || token !== token.trim().toUpperCase())) {
    throw new Error(`Invalid ${energy ? 'energy' : 'magic'} priorities in set '${key}'.`);
  }
  return [...priorities];
}

export const stagePresets: {
  label: string;
  EnergyFocus: number;
  MagicFocus: number;
  GearGoal: number;
  EnergyPriorities: string[];
  MagicPriorities: string[];
}[] = activityPolicies.stagePresets.map(preset => ({
  ...preset,
  EnergyPriorities: resolvePrioritySet(preset.EnergyPriorities, true),
  MagicPriorities: resolvePrioritySet(preset.MagicPriorities, false)
}));

export function stagePresetIndex(row: JsonObject): number {
  if (row.ExecutionMode !== undefined && row.ExecutionMode !== 0) return -1;
  return stagePresets.findIndex(preset =>
    row.EnergyFocus === preset.EnergyFocus && row.MagicFocus === preset.MagicFocus && row.GearGoal === preset.GearGoal
  );
}

export function stageLabel(row: JsonObject): string {
  if (row.ExecutionMode === 1) return automaticStageLabel;
  if (row.PresetId === '@active') return 'Plan active preset';
  if (typeof row.PresetId === 'string' && row.PresetId) return typeof row.Name === 'string' && row.Name ? row.Name : `Preset: ${row.PresetId}`;
  return stagePresets[stagePresetIndex(row)]?.label ?? 'Custom';
}

export function weightedStage(source: JsonObject = {}): JsonObject {
  return { Name: 'Weighted preset', PresetId: '@active', ExecutionMode: 0, Percent: 100, DurationSeconds: 0, DurationUnit: '%', CompletionMode: 0, TargetAdvancedTrainingPower: '0', TargetAdvancedTrainingToughness: '0', BlackBarLookaheadSeconds: 5, ...source };
}

export function applyStagePreset(row: JsonObject, index: number): JsonObject {
  if (!Number.isInteger(index) || index < 0 || index >= stagePresets.length) throw new Error('Invalid stage preset index.');
  const preset = stagePresets[index];
  return {
    ...row,
    Name: preset.label,
    EnergyFocus: preset.EnergyFocus,
    MagicFocus: preset.MagicFocus,
    GearGoal: preset.GearGoal,
    EnergyPriorities: [...preset.EnergyPriorities],
    MagicPriorities: [...preset.MagicPriorities]
  };
}
