import definitions from '../../../NGUInjector/Data/PresetDefinitions.json' with { type: 'json' };

// Preserve fixture reference IDs/order while using the runtime's catalog and defaults.
export const globalPresets = [
  'builtin_timeMachine', 'builtin_augmentsBlood', 'builtin_wandoos',
  'builtin_energyNgu', 'builtin_magicNgu', 'builtin_allNgu',
  'builtin_atAdventure', 'builtin_atWandoos',
  'builtin_adventurePower', 'builtin_adventureToughness'
].map((id, index) => {
  const preset = definitions.presets.find(row => row.Id === id);
  if (!preset) throw new Error(`Missing production preset definition: ${id}`);
  return { ...structuredClone(definitions.defaults), ...structuredClone(preset), Id: `global_${index}` };
});

export const presetLibrary = { Version: 1, Presets: globalPresets, __externalUiRevision: 'c'.repeat(64) };
