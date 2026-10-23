import activityPolicies from '../../../NGUInjector/Data/ActivityPolicies.json';
import presetDefinitions from '../../../NGUInjector/Data/PresetDefinitions.json';
import { stringifyJson, type JsonValue } from './api';
import { energyNguNames, magicNguNames } from './options';

export interface PriorityOption {
  value: string | number;
  label: string;
  group: string;
}

const advancedTrainingNames = ['Toughness', 'Power', 'Block', 'Wandoos energy', 'Wandoos magic'];
// Game bar order: augment and upgrade alternate (BlackBarAllocator uses index / 2).
// Names: ngu-idle.fandom.com/wiki/Augmentations and /wiki/Blood_Magic.
const augmentNames = [
  'Safety Scissors', 'Danger Scissors', 'Milk Infusion', 'Drinking the Milk Too',
  'Cannon Implant', 'Missile Launcher', 'Shoulder Mounted Minigun', 'Actual Ammunition',
  'Energy Buster', 'Charge Shot', 'Advanced Exoskeleton', 'Energy Shield',
  'Laser Sword', 'Quadruple Sided Laser Sword'
];
const ritualNames = [
  'Poke Yourself with a Tack', 'Fifty Papercuts', 'A Big-Ass Hickey', 'Eat a Bowl of Barbed Wire',
  'Grand Theft Blood Bank', 'Self Decapitation', 'Hug a Woodchipper', 'Turn Yourself Inside Out'
];

function stageOptions(energy: boolean): PriorityOption[] {
  const groups: Record<string, { label: string; names: string[]; group: string }> = {
    WAN: { label: 'Wandoos', names: [], group: 'Wandoos' },
    TM: { label: 'Time Machine', names: [], group: 'Time Machine' },
    NGU: { label: 'NGU (weighted allocation)', names: energy ? energyNguNames : magicNguNames, group: 'NGU' },
    AUG: { label: 'Augments (automatic selection)', names: augmentNames, group: 'Augments' },
    AT: { label: 'Advanced Training (power + toughness)', names: advancedTrainingNames, group: 'Advanced Training' },
    RIT: { label: 'Blood Magic (automatic ritual)', names: ritualNames, group: 'Blood Magic' }
  };
  return activityPolicies.features.flatMap(feature => {
    const count = feature[energy ? 'energyTargets' : 'magicTargets'];
    if (count < 0) return [];
    const { label, names, group } = groups[feature.token];
    return [
      { value: feature.token, label, group },
      ...Array.from({ length: count }, (_, index) => ({
        value: `${feature.token}-${index}`,
        label: `${group}: ${names[index] ?? `Target ${index + 1}`}`,
        group
      }))
    ];
  });
}

export const energyPriorityOptions = stageOptions(true);
export const magicPriorityOptions = stageOptions(false);
export const beardPriorityOptions: PriorityOption[] = presetDefinitions.beards.map(beard => ({ value: beard.id, label: beard.name, group: 'Beards' }));

export function priorityOption(options: readonly PriorityOption[], value: JsonValue): PriorityOption | undefined {
  // Runtime tokens are case-insensitive. Match for display, but never rewrite saved tokens.
  return options.find(option => typeof value === 'string' && typeof option.value === 'string'
    ? option.value === value.trim().toUpperCase() : option.value === value);
}

export function priorityLabel(options: readonly PriorityOption[], value: JsonValue): string {
  return priorityOption(options, value)?.label ?? `Unknown saved value: ${stringifyJson(value)}`;
}
