import policies from '../../../NGUInjector/Data/ActivityPolicies.json' with { type: 'json' };

// Read expected data without importing Vite-only JSON imports into Node's ESM loader.
const prioritySets: Record<string, string[]> = policies.prioritySets;
export const stagePresets = policies.stagePresets.map(preset => ({
  ...preset,
  EnergyPriorities: [...prioritySets[preset.EnergyPriorities]],
  MagicPriorities: [...prioritySets[preset.MagicPriorities]]
}));

export { policies };
