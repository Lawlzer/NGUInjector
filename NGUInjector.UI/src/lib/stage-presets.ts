import type { JsonObject } from './api';

export const stagePresets: {
  label: string;
  EnergyFocus: number;
  MagicFocus: number;
  GearGoal: number;
  EnergyPriorities: string[];
  MagicPriorities: string[];
}[] = [
  { label: 'Time Machine', EnergyFocus: 1, MagicFocus: 1, GearGoal: 5, EnergyPriorities: ['TM', 'NGU', 'WAN', 'AUG', 'AT'], MagicPriorities: ['TM', 'RIT', 'NGU', 'WAN'] },
  { label: 'Augments + Blood', EnergyFocus: 2, MagicFocus: 4, GearGoal: 10, EnergyPriorities: ['AUG', 'NGU', 'WAN', 'TM', 'AT'], MagicPriorities: ['RIT', 'NGU', 'WAN', 'TM'] },
  { label: 'Wandoos', EnergyFocus: 3, MagicFocus: 3, GearGoal: 12, EnergyPriorities: ['WAN', 'NGU', 'TM', 'AUG', 'AT'], MagicPriorities: ['WAN', 'RIT', 'NGU', 'TM'] },
  { label: 'Energy NGU', EnergyFocus: 5, MagicFocus: 6, GearGoal: 2, EnergyPriorities: ['NGU', 'WAN', 'TM', 'AUG', 'AT'], MagicPriorities: ['NGU', 'RIT', 'WAN', 'TM'] },
  { label: 'Magic NGU', EnergyFocus: 0, MagicFocus: 6, GearGoal: 3, EnergyPriorities: [], MagicPriorities: ['NGU', 'RIT', 'WAN', 'TM'] },
  { label: 'All NGU', EnergyFocus: 5, MagicFocus: 6, GearGoal: 4, EnergyPriorities: ['NGU', 'WAN', 'TM', 'AUG', 'AT'], MagicPriorities: ['NGU', 'RIT', 'WAN', 'TM'] },
  { label: 'AT Adventure', EnergyFocus: 7, MagicFocus: 4, GearGoal: 11, EnergyPriorities: ['AT', 'NGU', 'WAN', 'TM', 'AUG'], MagicPriorities: ['RIT', 'NGU', 'WAN', 'TM'] },
  {
    label: 'AT Wandoos', EnergyFocus: 7, MagicFocus: 3, GearGoal: 11,
    EnergyPriorities: ['AT-3', 'AT-4', 'WAN', 'NGU', 'TM', 'AUG', 'AT'],
    MagicPriorities: ['WAN', 'RIT', 'NGU', 'TM']
  }
];

export function stagePresetIndex(row: JsonObject): number {
  return stagePresets.findIndex(preset =>
    row.EnergyFocus === preset.EnergyFocus && row.MagicFocus === preset.MagicFocus && row.GearGoal === preset.GearGoal
  );
}

export function stageLabel(row: JsonObject): string {
  return stagePresets[stagePresetIndex(row)]?.label ?? 'Custom';
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
