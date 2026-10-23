import { describe, expect, it } from 'vitest';
import policies from '../../NGUInjector/Data/ActivityPolicies.json';
import { applyStagePreset, isValidPriority, resolvePrioritySet, stagePresetIndex, stagePresets } from '../src/lib/stage-presets';
import { validatePriorities } from '../src/lib/model';

const prioritySets: Readonly<Record<string, readonly string[]>> = policies.prioritySets;

describe('shared activity policy', () => {
  it('uses the actual backend resource source for UI presets', () => {
    expect(stagePresets).toEqual(policies.stagePresets.map(preset => ({
      ...preset,
      EnergyPriorities: resolvePrioritySet(preset.EnergyPriorities, true),
      MagicPriorities: resolvePrioritySet(preset.MagicPriorities, false)
    })));
    expect(policies.version).toBe(1);
  });

  it('keeps implicit defaults separate from intentionally different explicit preset orders', () => {
    expect(resolvePrioritySet(policies.defaults.find(row => row.name === 'TimeMachine')?.energy, true)).toEqual(['TM', 'NGU', 'WAN', 'AT']);
    expect(stagePresets[0].EnergyPriorities).toEqual(['TM', 'NGU', 'WAN', 'AUG', 'AT']);
    expect(resolvePrioritySet(policies.defaults.find(row => row.name === 'AdvancedTraining')?.energy, true)).toEqual(['AT', 'NGU', 'TM']);
    expect(stagePresets[6].EnergyPriorities).toEqual(['AT', 'NGU', 'WAN', 'TM', 'AUG']);
    expect(stagePresets[7].EnergyPriorities).toEqual(['AT-3', 'AT-4', 'WAN', 'NGU', 'TM', 'AUG', 'AT']);
    expect(stagePresets[7].MagicPriorities).toEqual(['RIT', 'NGU', 'TM']);
  });

  it('clones registry arrays before export and on every preset application', () => {
    stagePresets.forEach((preset, index) => {
      expect(preset).not.toBe(policies.stagePresets[index]);
      const energy = prioritySets[policies.stagePresets[index].EnergyPriorities];
      const magic = prioritySets[policies.stagePresets[index].MagicPriorities];
      expect(preset.EnergyPriorities).not.toBe(energy);
      expect(preset.MagicPriorities).not.toBe(magic);
      const applied = applyStagePreset({}, index);
      (applied.EnergyPriorities as string[]).push('WAN');
      (applied.MagicPriorities as string[]).push('TM');
      expect(preset.EnergyPriorities).toEqual(energy);
      expect(preset.MagicPriorities).toEqual(magic);
      expect(stagePresetIndex({ ...applied, EnergyPriorities: ['invalid'] })).toBe(index);
    });
  });

  it('retains core, optional, and quick-focus resource versus gear intent', () => {
    expect(policies.coreBeards).toEqual([1, 3, 5, 6]);
    expect(policies.optionalBeards).toEqual([0, 2, 4]);
    expect(policies.focuses.Adventure).toEqual({ gearGoal: 9, energyFocus: 7, magicFocus: 4 });
    expect(policies.focuses.Fight).toEqual({ gearGoal: 9, energyFocus: 0, magicFocus: 0 });
    expect(policies.support.diggers.manualAdventure).toEqual([{ id: 3, weight: 800, name: 'adventure' }, { id: 0, weight: 400, name: 'drop' }]);
    expect(policies.support.diggers.imminentTitan).toEqual([{ id: 11, weight: 1000, name: 'EXP titan' }]);
    expect(policies.support.diggerBudgetFraction).toBe(.75);
  });

  it('defines every unique ordered sequence once with only valid key references', () => {
    const sequences = Object.values(prioritySets).map(value => JSON.stringify(value));
    expect(new Set(sequences).size).toBe(sequences.length);
    const references = [
      ...policies.defaults.flatMap(row => [[row.energy, true], [row.magic, false]] as const),
      ...policies.stagePresets.flatMap(row => [[row.EnergyPriorities, true], [row.MagicPriorities, false]] as const),
      [policies.preparation.energy, true], [policies.preparation.magic, false]
    ] as const;
    for (const [key, energy] of references) {
      expect(typeof key).toBe('string');
      expect(Object.prototype.hasOwnProperty.call(prioritySets, key)).toBe(true);
      expect(resolvePrioritySet(key, energy)).toEqual(prioritySets[key]);
    }
    expect(new Set(references.map(([key]) => key)).size).toBe(Object.keys(prioritySets).length);
  });

  it('resolves all backend default profiles to the exact previous orders', () => {
    expect(policies.defaults.map(row => [row.name, resolvePrioritySet(row.energy, true), resolvePrioritySet(row.magic, false)])).toEqual([
      ['None', [], []],
      ['TimeMachine', ['TM', 'NGU', 'WAN', 'AT'], ['TM', 'RIT', 'NGU', 'WAN']],
      ['Augments', ['AUG', 'NGU', 'WAN', 'TM'], ['RIT', 'NGU', 'WAN', 'TM']],
      ['Wandoos', ['WAN', 'NGU', 'TM', 'AT'], ['WAN', 'RIT', 'NGU', 'TM']],
      ['BloodMagic', [], ['RIT', 'NGU', 'WAN', 'TM']],
      ['EnergyNgu', ['NGU', 'WAN', 'TM', 'AT'], []],
      ['MagicNgu', [], ['NGU', 'RIT', 'WAN', 'TM']],
      ['AdvancedTraining', ['AT', 'NGU', 'TM'], ['RIT', 'NGU', 'TM']]
    ]);
  });

  it('shares magic preparation with matching defaults and presets while retaining exact preparation orders', () => {
    expect(policies.preparation).toEqual({ energy: 'WandoosAtEnergy', magic: 'WandoosAtMagic' });
    expect(resolvePrioritySet(policies.preparation.energy, true)).toEqual(['AT-3', 'AT-4', 'NGU', 'TM']);
    expect(resolvePrioritySet(policies.preparation.magic, false)).toEqual(['RIT', 'NGU', 'TM']);
    expect(policies.defaults[7].magic).toBe(policies.preparation.magic);
    expect(policies.stagePresets[7].MagicPriorities).toBe(policies.preparation.magic);
  });

  it('propagates a single set edit to every referencing default and preset without rewriting saved arrays', () => {
    const changed = { ...policies.prioritySets, BloodMagic: ['TM', 'NGU', 'RIT', 'WAN'] };
    const defaults = policies.defaults.filter(row => row.magic === 'BloodMagic');
    const presets = policies.stagePresets.filter(row => row.MagicPriorities === 'BloodMagic');
    expect(defaults.map(row => row.name)).toEqual(['Augments', 'BloodMagic']);
    expect(presets.map(row => row.label)).toEqual(['Augments + Blood', 'AT Adventure']);
    for (const key of [...defaults.map(row => row.magic), ...presets.map(row => row.MagicPriorities)]) {
      expect(resolvePrioritySet(key, false, changed)).toEqual(['TM', 'NGU', 'RIT', 'WAN']);
    }
    expect(prioritySets.BloodMagic).toEqual(['RIT', 'NGU', 'WAN', 'TM']);
    const saved = { EnergyFocus: 2, MagicFocus: 4, GearGoal: 10, EnergyPriorities: ['NGU-8', 'TM'], MagicPriorities: ['WAN', 'RIT-7'] };
    const before = JSON.stringify(saved);
    expect(stagePresetIndex(saved)).toBe(1);
    validatePriorities(saved.EnergyPriorities, true, 'saved energy');
    validatePriorities(saved.MagicPriorities, false, 'saved magic');
    expect(JSON.stringify(saved)).toBe(before);
  });

  it('returns independent arrays from shared set resolution', () => {
    const first = resolvePrioritySet('BloodMagic', false);
    const second = resolvePrioritySet('BloodMagic', false);
    expect(first).not.toBe(second);
    expect(first).not.toBe(prioritySets.BloodMagic);
    first.reverse();
    expect(second).toEqual(['RIT', 'NGU', 'WAN', 'TM']);
    expect(prioritySets.BloodMagic).toEqual(second);
  });

  it.each([undefined, null, 1, [], ['TM'], {}, '', 'unknown', 'toString', '__proto__'])('rejects malformed or unknown reference %j without inline-array fallback', key => {
    expect(() => resolvePrioritySet(key, true)).toThrow(/priority-set key/);
  });

  it.each([null, 1, 'TM', {}, ['UNKNOWN'], ['tm'], [' TM '], ['NGU-9'], ['RIT']])('rejects malformed or wrong-lane set contents %j', value => {
    expect(() => resolvePrioritySet('test', true, { test: value })).toThrow("Invalid energy priorities in set 'test'");
  });

  it.each([
    ['WAN', 0, 0], ['TM', 0, 0], ['NGU', 9, 7], ['AUG', 14, -1], ['AT', 5, -1], ['RIT', -1, 8]
  ] as const)('validates every executable %s group and indexed lane boundary', (kind, energyTargets, magicTargets) => {
    expect(policies.features.find(row => row.token === kind)).toEqual({ token: kind, energyTargets, magicTargets });
    for (const energy of [true, false]) {
      const count = energy ? energyTargets : magicTargets;
      expect(isValidPriority(kind, energy)).toBe(count >= 0);
      for (let id = -2; id <= 15; id++) {
        expect(isValidPriority(`${kind}-${id}`, energy)).toBe(id >= 0 && id < count);
        expect(isValidPriority(` ${kind.toLowerCase()}-${id}\t`, energy)).toBe(id >= 0 && id < count);
      }
      for (const suffix of ['', '00', '01', '+1', ' 1', '1.0', '1e0', '-1', '2147483648', '0-1', 'x', '\u0661']) {
        expect(isValidPriority(`${kind}-${suffix}`, energy)).toBe(false);
      }
    }
  });

  it('recognizes exactly the implemented handlers, not synthetic unknown features', () => {
    expect(policies.features.map(row => row.token).sort()).toEqual(['AT', 'AUG', 'NGU', 'RIT', 'TM', 'WAN']);
    for (const token of [null, undefined, 1, {}, '', ' ', 'UNKNOWN', 'HACK', 'WAN-0', 'TM-0', 'NGU_1', 'NGU -1']) {
      expect(isValidPriority(token, true)).toBe(false);
      expect(isValidPriority(token, false)).toBe(false);
    }
  });

  it('keeps editor validation canonical even though backend-style recognition normalizes input', () => {
    expect(isValidPriority(' ngu-8 ', true)).toBe(true);
    expect(() => validatePriorities([' ngu-8 '], true, 'Energy')).toThrow('exact uppercase');
    expect(() => validatePriorities(['ngu'], true, 'Energy')).toThrow('exact uppercase');
    for (const preset of stagePresets) {
      expect(validatePriorities(preset.EnergyPriorities, true, preset.label)).toBeUndefined();
      expect(validatePriorities(preset.MagicPriorities, false, preset.label)).toBeUndefined();
    }
    for (const row of policies.defaults) {
      expect(validatePriorities(resolvePrioritySet(row.energy, true), true, row.name)).toBeUndefined();
      expect(validatePriorities(resolvePrioritySet(row.magic, false), false, row.name)).toBeUndefined();
    }
    expect(() => validatePriorities(['NGU-7'], false, 'Magic')).toThrow('exact uppercase');
    expect(() => validatePriorities('TM', true, 'Energy')).toThrow('exact uppercase');
  });
});
