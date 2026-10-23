import { describe, expect, it, vi } from 'vitest';
import { render } from 'svelte/server';
import activityPolicies from '../../NGUInjector/Data/ActivityPolicies.json';
import presetDefinitions from '../../NGUInjector/Data/PresetDefinitions.json';
import { parseJson, stringifyJson, type JsonObject } from '../src/lib/api';
import { createController } from '../src/lib/controller';
import { energyNguNames, magicNguNames } from '../src/lib/options';
import { newPreset } from '../src/lib/presets';
import { beardPriorityOptions, energyPriorityOptions, magicPriorityOptions, priorityLabel, priorityOption } from '../src/lib/priority-options';
import PriorityPicker from '../src/lib/PriorityPicker.svelte';
import Presets from '../src/lib/Presets.svelte';
import StageEditor from '../src/lib/StageEditor.svelte';

describe('priority options', () => {
  it.each([true, false])('covers exactly the supported policy tokens for energy=%s', energy => {
    const options = energy ? energyPriorityOptions : magicPriorityOptions;
    const expected = activityPolicies.features.flatMap(feature => {
      const count = feature[energy ? 'energyTargets' : 'magicTargets'];
      return count < 0 ? [] : [feature.token, ...Array.from({ length: count }, (_, index) => `${feature.token}-${index}`)];
    });
    expect(options.map(option => option.value)).toEqual(expected);
    expect(new Set(options.map(option => option.value)).size).toBe(options.length);
    expect(options.every(option => option.label && option.group)).toBe(true);
  });

  it('uses the existing lane-specific NGU names', () => {
    for (const [options, names] of [[energyPriorityOptions, energyNguNames], [magicPriorityOptions, magicNguNames]] as const) {
      expect(names.map((_, index) => priorityLabel(options, `NGU-${index}`))).toEqual(names.map(name => `NGU: ${name}`));
    }
  });

  it('maps game bar indices without confusing augment upgrades or AT power/toughness', () => {
    expect(['AUG-0', 'AUG-1', 'AUG-12', 'AUG-13', 'AT-0', 'AT-1', 'AT-3', 'AT-4'].map(value => priorityLabel(energyPriorityOptions, value))).toEqual([
      'Augments: Safety Scissors', 'Augments: Danger Scissors', 'Augments: Laser Sword', 'Augments: Quadruple Sided Laser Sword',
      'Advanced Training: Toughness', 'Advanced Training: Power', 'Advanced Training: Wandoos energy', 'Advanced Training: Wandoos magic'
    ]);
    expect(priorityLabel(magicPriorityOptions, 'RIT-0')).toBe('Blood Magic: Poke Yourself with a Tack');
    expect(priorityLabel(magicPriorityOptions, 'RIT-7')).toBe('Blood Magic: Turn Yourself Inside Out');
    expect(beardPriorityOptions.map(option => [option.value, option.label])).toEqual(presetDefinitions.beards.map(beard => [beard.id, beard.name]));
  });

  it('recognizes case-insensitive stage tokens but does not coerce beard IDs', () => {
    expect(priorityOption(energyPriorityOptions, ' ngu-0 ')?.value).toBe('NGU-0');
    expect(priorityOption(beardPriorityOptions, 0)?.value).toBe(0);
    expect(priorityLabel(beardPriorityOptions, '0')).toBe('Unknown saved value: "0"');
    expect(priorityLabel(energyPriorityOptions, 'RIT')).toBe('Unknown saved value: "RIT"');
  });
});

describe('priority picker display', () => {
  it('leaves empty defaults unmaterialized and exposes selection rather than raw entry', () => {
    const onchange = vi.fn();
    const body = render(PriorityPicker, { props: { label: 'Energy priorities', options: energyPriorityOptions, value: [], defaultLabel: 'Use focus defaults', onchange } }).body;
    expect(body).toContain('Use focus defaults');
    expect(body).toContain('aria-label="Add energy priorities"');
    expect(body).toContain('NGU (weighted allocation)');
    expect(body).not.toMatch(/<input\b|<textarea\b/);
    expect(onchange).not.toHaveBeenCalled();
  });

  it('keeps unknown, duplicate and lossless saved values visible and removable without mutation', () => {
    const value = parseJson('[" tm ","TM","FUTURE",9007199254740993,{"exact":9007199254740995}]');
    const before = stringifyJson(value), onchange = vi.fn();
    const body = render(PriorityPicker, { props: { label: 'Energy priorities', options: energyPriorityOptions, value, onchange } }).body;
    expect(body).toContain('aria-label="Move Time Machine down"');
    expect(body).toContain('aria-label="Remove Unknown saved value: &quot;FUTURE&quot;"');
    expect(body).toContain('9007199254740993');
    expect(body).toContain('9007199254740995');
    expect(body.match(/data-remove/g)).toHaveLength(5);
    expect(body).not.toMatch(/<option[^>]*>Time Machine<\/option>/);
    expect(stringifyJson(value)).toBe(before);
    expect(onchange).not.toHaveBeenCalled();
  });

  it('renders a disabled fieldset and disabled selection when editing is blocked', () => {
    const body = render(PriorityPicker, { props: { label: 'Beard priorities', options: beardPriorityOptions, value: [0], disabled: true, onchange: () => {} } }).body;
    expect(body).toMatch(/<fieldset[^>]*aria-label="Beard priorities"[^>]*disabled/);
    expect(body).toMatch(/<select[^>]*aria-label="Add beard priorities"[^>]*disabled/);
  });
});

describe('priority picker integration', () => {
  it.each([0, 1])('uses clear timing labels and honest relative weights for execution mode %s', ExecutionMode => {
    const plan: JsonObject = { StageDurationSeconds: 1800, Stages: [{ ExecutionMode, Percent: 150, EnergyPriorities: ['NGU-4', 'FUTURE'], MagicPriorities: ['RIT-7'], Future: 'keep' }] };
    const before = stringifyJson(plan), onchange = vi.fn();
    const body = render(StageEditor, { props: { plan, onchange } }).body;
    expect(body).toContain('<option value="fixed">Time</option>');
    expect(body).toContain('<option value="flexible" selected="">% of remaining</option>');
    expect(body).toContain('Relative weight, not a literal percentage; weights do not need to total 100.');
    expect(body).toContain('value="150"');
    if (ExecutionMode === 0) {
      expect(body).toContain('>Advanced training targets</option>');
      expect(body).toContain('aria-label="Add energy priorities"');
      expect(body).toContain('aria-label="Add magic priorities"');
      expect(body).toContain('NGU: Adventure Alpha');
      expect(body).toContain('Blood Magic: Turn Yourself Inside Out');
      expect(body).not.toMatch(/<input[^>]*aria-label="(?:Energy|Magic) priorities"/);
    } else expect(body).toContain('total elapsed budgets, including AT, planning and setup.');
    expect(stringifyJson(plan)).toBe(before);
    expect(onchange).not.toHaveBeenCalled();
  });

  it('keeps beard recovery and pending state without routine saving copy', () => {
    const ui = createController();
    const preset = { ...newPreset(), Id: 'test', SupportMode: 'planned', BeardPriority: [0, 3, 99], BeardRotation: [] };
    const library: JsonObject = { Version: 1, Presets: [preset], __externalUiRevision: 'a'.repeat(64) };
    const state = { ...ui.current(), presetLibrary: library, presetLibraryBase: library, loadoutPresetSelectedId: 'test' };
    const onchange = vi.fn();
    const props = { state, onchange, onselect: () => {}, oncandidate: () => {}, onpreview: async () => {}, onapply: async () => {}, onequip: async () => {}, onstop: async () => {}, onreload: async () => {}, ondiscard: async () => {} };
    const before = stringifyJson(library);
    const body = render(Presets, { props }).body;
    expect(body).toContain('aria-label="Fu Manchu beard"');
    expect(body).toContain('aria-label="Move Fu Manchu beard down"');
    expect(body).toContain('Beard ID');
    expect(body).toContain('Use default beards');
    expect(body).not.toContain('Beard IDs');
    expect(body).not.toContain('Save library');
    expect(body).not.toContain('Unsaved changes');
    expect(body).not.toMatch(/autosave|all changes saved|storage|polling/i);
    const pending = render(Presets, { props: { ...props, state: { ...state, presetLibraryDirty: true, presetLibrary: { ...library, Presets: [{ ...preset, BeardPriority: [0, 3] }] } } } }).body;
    expect(pending).toContain('Preset changes pending.');
    expect(pending).toContain('Discard library edits');
    expect(pending).not.toMatch(/autosave|all changes saved|storage|polling/i);
    expect(stringifyJson(library)).toBe(before);
    expect(onchange).not.toHaveBeenCalled();
    ui.dispose();
  });
});
