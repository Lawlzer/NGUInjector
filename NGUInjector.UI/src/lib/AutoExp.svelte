<script lang="ts">
  import { isSafeNumber } from 'lossless-json';
  import type { JsonObject, JsonValue } from './api';
  import { autoExpRatioDefaults } from './model';

  export let settings: JsonObject;
  export let disabled = false;
  export let onedit: (key: string, value: JsonValue, delay?: number) => void;

  const groups = [
    { title: 'E:M BASE cap', fields: [
      ['_customExpEnergyRatio', 'Energy', 'Energy BASE cap (E:M) ratio'],
      ['_customExpMagicRatio', 'Magic', 'Magic BASE cap (E:M) ratio']
    ] },
    { title: 'Energy BASE Power:Cap:Bars', fields: [
      ['_customExpEnergyPowerRatio', 'Power', 'Energy BASE Power ratio'],
      ['_customExpEnergyCapRatio', 'Cap', 'Energy BASE Cap ratio'],
      ['_customExpEnergyBarsRatio', 'Bars', 'Energy BASE Bars ratio']
    ] },
    { title: 'Magic BASE Power:Cap:Bars', fields: [
      ['_customExpMagicPowerRatio', 'Power', 'Magic BASE Power ratio'],
      ['_customExpMagicCapRatio', 'Cap', 'Magic BASE Cap ratio'],
      ['_customExpMagicBarsRatio', 'Bars', 'Magic BASE Bars ratio']
    ] }
  ] as const;

  function editRatio(key: string, event: Event): void {
    const text = (event.currentTarget as HTMLInputElement).value;
    // Do not round a fractional or underflowing token into a valid whole ratio.
    onedit(key, text.trim() && isSafeNumber(text, { approx: false }) ? Number(text) : text, 350);
  }
</script>

<article class="card span-all" aria-label="Auto use EXP" inert={disabled}>
  <label class="toggle-row"><input type="checkbox" aria-label="Auto use EXP" checked={settings._customAutoExpEnabled === true} on:change={event => onedit('_customAutoExpEnabled', (event.currentTarget as HTMLInputElement).checked)} /><span>Auto use EXP</span></label>
  <div class="ratio-groups">
    {#each groups as group}
      <fieldset aria-describedby="auto-exp-help">
        <legend>{group.title}</legend>
        <div class="ratio-inputs">
          {#each group.fields as [key, label, accessibleLabel]}
            <label>{label}<input aria-label={accessibleLabel} type="number" min="1" max="1000000000" step="1" value={String(settings[key] ?? autoExpRatioDefaults[key])} on:input={event => editRatio(key, event)} /></label>
          {/each}
        </div>
      </fieldset>
    {/each}
  </div>
  <details><summary>Purchase rules</summary><p class="muted" id="auto-exp-help">Ratios target base stats, not gear totals or EXP spending. Unspent EXP is saved for affordable upgrades. Enter whole numbers (160k = 160000).</p></details>
</article>

<style>
  .toggle-row { align-items: center; padding: 0; border: 0; margin-bottom: .5rem; }
  .ratio-groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 17rem), 1fr)); gap: .6rem; }
  fieldset { min-width: 0; margin: 0; padding: .5rem .65rem .65rem; border: 1px solid var(--line); border-radius: 4px; }
  legend { padding: 0 .3rem; font-size: .85rem; font-weight: 600; }
  .ratio-inputs { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 5rem), 1fr)); gap: .4rem; }
  label { min-width: 0; gap: .25rem; }
  input[type="number"] { padding: .35rem .45rem; }
  details { margin-top: .4rem; }
  summary { font-size: .78rem; color: var(--muted); }
</style>
