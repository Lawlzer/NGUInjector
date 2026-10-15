export interface OptionItem {
  value: number;
  label: string;
}

export const commandLabels: Record<string, string> = {
  manualFightPrep: 'Fight bosses', manualRebirth: 'Rebirth', quickSave: 'Quicksave',
  safeProbe: 'Check game', probeCommands: 'Run diagnostic commands', precacheLoadouts: 'Prepare loadouts',
  resetRebirthPrep: 'Reset fight prep', reloadSettings: 'Reload settings', status: 'Refresh status', unload: 'Disconnect',
  focusGoal: 'Quick', stopFocus: 'Clear quick', previewGear: 'Preview gear', optimizeGear: 'Equip gear',
  previewAugments: 'Preview augment', allocateAugments: 'Allocate augment'
};

export const adventureModes: OptionItem[] = [
  { value: 0, label: 'Optimal ITOPOD' },
  { value: 1, label: 'Farm selected zone' },
  { value: 2, label: 'Farm boosts dynamically' }
];

export const triggerModes: OptionItem[] = [
  { value: 0, label: 'Any enabled trigger' },
  { value: 1, label: 'All enabled triggers' }
];

export const stageFocuses: OptionItem[] = [
  { value: 0, label: 'None' },
  { value: 1, label: 'Time Machine' },
  { value: 2, label: 'Augments' },
  { value: 3, label: 'Wandoos' },
  { value: 4, label: 'Blood Magic' },
  { value: 5, label: 'Energy NGU' },
  { value: 6, label: 'Magic NGU' },
  { value: 7, label: 'Advanced Training' }
];

export const energyNguNames = ['Augments', 'Wandoos', 'Respawn', 'Gold', 'Adventure Alpha', 'Power Alpha', 'Drop Chance', 'Magic NGU', 'PP'];
export const magicNguNames = ['Yggdrasil', 'EXP', 'Power Beta', 'Number', 'Time Machine', 'Energy NGU', 'Adventure Beta'];

export const gearGoals: OptionItem[] = [
  { value: 0, label: 'Energy Cap' },
  { value: 1, label: 'Magic Cap' },
  { value: 2, label: 'Energy NGU' },
  { value: 3, label: 'Magic NGU' },
  { value: 4, label: 'All NGUs' },
  { value: 5, label: 'Time Machine' },
  { value: 6, label: 'Blood Magic' },
  { value: 7, label: 'Gold' },
  { value: 8, label: 'Drop Chance' },
  { value: 9, label: 'Fight' },
  { value: 10, label: 'Augments' },
  { value: 11, label: 'Advanced Training' },
  { value: 12, label: 'Wandoos' },
  { value: 13, label: 'Manual Adventure' }
];

export const fightScoreModes: OptionItem[] = [
  { value: 0, label: 'sqrt(Power * Toughness) * Move Cooldown' },
  { value: 1, label: 'Power * Toughness * Move Cooldown' },
  { value: 2, label: '(Power + Toughness) * Move Cooldown' }
];

export const zones: OptionItem[] = [
  { value: -1, label: 'Safe Zone: Awakening Site' },
  { value: 0, label: 'Tutorial Zone' },
  { value: 1, label: 'Sewers' },
  { value: 2, label: 'Forest' },
  { value: 3, label: 'Cave of Many Things' },
  { value: 4, label: 'The Sky' },
  { value: 5, label: 'High Security Base' },
  { value: 6, label: 'Gordon Ramsay Bolton' },
  { value: 7, label: 'Clock Dimension' },
  { value: 8, label: 'Grand Corrupted Tree' },
  { value: 9, label: 'The 2D Universe' },
  { value: 10, label: 'Ancient Battlefield' },
  { value: 11, label: 'Jake From Accounting' },
  { value: 12, label: 'A Very Strange Place' },
  { value: 13, label: 'Mega Lands' },
  { value: 14, label: 'UUG THE UNMENTIONABLE' },
  { value: 15, label: 'The Beardverse' },
  { value: 16, label: 'WALDERP' },
  { value: 17, label: 'Badly Drawn World' },
  { value: 18, label: 'Boring-Ass Earth' },
  { value: 19, label: 'THE BEAST' },
  { value: 20, label: 'Chocolate World' },
  { value: 21, label: 'The Evilverse' },
  { value: 22, label: 'Pretty Pink Princess Land' },
  { value: 23, label: 'GREASY NERD' },
  { value: 24, label: 'Meta Land' },
  { value: 25, label: 'Interdimensional Party' },
  { value: 26, label: 'THE GODMOTHER' },
  { value: 27, label: 'Typo Zonw' },
  { value: 28, label: 'The Fad-Lands' },
  { value: 29, label: 'JRPGVille' },
  { value: 30, label: 'THE EXILE' },
  { value: 31, label: 'The Rad-lands' },
  { value: 32, label: 'Back To School' },
  { value: 33, label: 'The West World' },
  { value: 34, label: 'IT HUNGERS' },
  { value: 35, label: 'The Breadverse' },
  { value: 36, label: "That 70's Zone" },
  { value: 37, label: 'The Halloweenies' },
  { value: 38, label: 'ROCK LOBSTER' },
  { value: 39, label: 'Construction Zone' },
  { value: 40, label: 'DUCK DUCK ZONE' },
  { value: 41, label: 'The Nether Regions' },
  { value: 42, label: 'AMALGAMATE' },
  { value: 43, label: '7 Aethereal Seas' },
  { value: 1000, label: 'ITOPOD' }
];

export function optionLabel(options: OptionItem[], value: unknown): string {
  const numeric = typeof value === 'number' ? value : Number(value);
  return options.find((option) => option.value === numeric)?.label ?? String(value ?? '');
}
