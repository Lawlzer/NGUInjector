<script lang="ts">
  import type { JsonObject, JsonValue } from './api';
  import { validateSettings } from './model';
  import { beardOptions, diggerNames, presetNumberInput } from './presets';

  export let settings: JsonObject;
  export let disabled = false;
  export let onedit: (key: string, value: JsonValue, delay?: number) => void;

  const groups = [
    { key: '_manualAdventureGearWeights', label: 'Gear weights', names: ['Push', 'Adventure power', 'Adventure toughness', 'Drop chance', 'Gold'] },
    { key: '_manualAdventureBeardWeights', label: 'Beard priorities', names: beardOptions.map(beard => beard.name) },
    { key: '_manualAdventureDiggerWeights', label: 'Digger priorities', names: diggerNames }
  ];
  const weights = (key: string, count: number): JsonValue[] => Array.isArray(settings[key]) ? settings[key] as JsonValue[] : Array(count).fill(0);

  function edit(key: string, index: number, count: number, value: JsonValue, delay = 350): void {
    if (!disabled) onedit(key, weights(key, count).map((weight, i) => i === index ? value : weight), delay);
  }
  function validWeight(value: JsonValue): boolean {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1e6;
  }
  $: validation = (() => {
    try {
      validateSettings(Object.fromEntries(groups.filter(group => group.key in settings).map(group => [group.key, settings[group.key]])), settings);
      return '';
    } catch (err) { return (err as Error).message; }
  })();
</script>

<details class="manual-bonuses">
  <summary>Manual adventure bonuses</summary>
  {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
  <div class="bonus-groups">
    {#each groups as group}
      <fieldset {disabled}>
        <legend>{group.label}</legend>
        {#each group.names as name, index}
          {@const weight = weights(group.key, group.names.length)[index]}
          <div class="bonus-row">
            <label>{name}<input aria-label={`${group.label} ${name} bonus`} inputmode="decimal" value={String(weight ?? '')} on:input={event => edit(group.key, index, group.names.length, presetNumberInput((event.currentTarget as HTMLInputElement).value))} /></label>
            <button type="button" class="quiet" aria-label={`Increase ${group.label} ${name} bonus by 100`} disabled={!validWeight(weight) || Number(weight) >= 1e6} on:click={() => edit(group.key, index, group.names.length, Math.min(1e6, Number(weight) + 100), 0)}>+100</button>
          </div>
        {/each}
      </fieldset>
    {/each}
  </div>
</details>

<style>
  .manual-bonuses { margin-top: .7rem; }
  .bonus-groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 19rem), 1fr)); gap: .6rem; margin-top: .5rem; }
  fieldset { min-width: 0; margin: 0; padding: .4rem .55rem .55rem; border: 1px solid var(--line); border-radius: 4px; }
  legend { padding: 0 .25rem; font-size: .85rem; font-weight: 600; }
  .bonus-row { display: flex; align-items: center; gap: .35rem; margin-top: .3rem; }
  label { display: grid; grid-template-columns: minmax(0, 1fr) 6rem; align-items: center; gap: .35rem; flex: 1; min-width: 0; font-size: .8rem; overflow-wrap: anywhere; }
  input { min-width: 0; padding: .35rem .4rem; }
  button { flex-shrink: 0; padding: .35rem .4rem; font-size: .75rem; }
</style>
