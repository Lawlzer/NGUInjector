<script lang="ts">
  import { isSafeNumber } from 'lossless-json';
  import type { JsonObject, JsonValue } from './api';
  import { INT_MAX, validateSettings } from './model';
  import { energyNguNames, magicNguNames } from './options';

  export let settings: JsonObject;
  export let disabled = false;
  export let weightsWritable = true;
  export let onedit: (key: string, value: JsonValue, delay?: number) => void;

  $: hasWeights = Array.isArray(settings._energyNguWeights) && settings._energyNguWeights.length === 9 && Array.isArray(settings._magicNguWeights) && settings._magicNguWeights.length === 7;
  $: validation = hasWeights ? settingsError(settings) : '';

  function settingsError(value: JsonObject): string {
    try { validateSettings({ _energyNguWeights: value._energyNguWeights, _magicNguWeights: value._magicNguWeights }, value); return ''; } catch (err) { return (err as Error).message; }
  }
  function setWeight(key: string, index: number, event: Event): void {
    if (disabled || !weightsWritable || !hasWeights) return;
    const text = (event.currentTarget as HTMLInputElement).value;
    // Keep unsafe tokens local rather than rounding a fractional weight into an integer.
    const next = text.trim() && isSafeNumber(text, { approx: false }) ? Number(text) : text;
    onedit(key, (settings[key] as JsonValue[]).map((weight, i) => i === index ? next : weight), 350);
  }
</script>

<article id="ngu-weights" class="card span-all" aria-label="NGU weights" inert={disabled}>
  <h2>NGU weights</h2>
  <p class="muted">Global weights for all plans, presets, and Quick actions.</p>
  {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
  {#if !hasWeights}
    <p class="notice" role="status">Global NGU weights unavailable. Update the injector and reconnect to load settings.</p>
  {:else}
    {#if !weightsWritable}<p class="notice" role="status">Weight editing unavailable. Update the injector, reconnect, and reload.</p>{/if}
    <div class="weight-groups">
      {#each ([['_energyNguWeights', 'Energy', energyNguNames], ['_magicNguWeights', 'Magic', magicNguNames]] as const) as [key, resource, names]}
        <fieldset>
          <legend>{resource}</legend>
          <div class="weight-inputs">
            {#each names as target, index}
              <label>{target}<input aria-label={`${resource} ${target} weight`} type="number" min="0" max={INT_MAX} step="1" value={String((settings[key] as JsonValue[])[index])} disabled={disabled || !weightsWritable} on:input={event => setWeight(key, index, event)} /></label>
            {/each}
          </div>
        </fieldset>
      {/each}
    </div>
  {/if}
  <details><summary>Weight rules</summary><p class="muted">Excess at a buffered cap goes to other targets. All-zero weights skip NGU for that resource, allowing later priorities.</p></details>
</article>

<style>
  .weight-groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 22rem), 1fr)); gap: .6rem; }
  fieldset { min-width: 0; margin: 0; padding: .5rem .65rem .65rem; border: 1px solid var(--line); border-radius: 4px; }
  legend { padding: 0 .25rem; font-size: .85rem; font-weight: 600; }
  .weight-inputs { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 12rem), 1fr)); gap: .4rem .7rem; }
  label { min-width: 0; display: grid; grid-template-columns: minmax(0, 1fr) 5.5rem; align-items: center; gap: .4rem; font-size: .8rem; overflow-wrap: anywhere; }
  input { padding: .35rem .45rem; }
  details { margin-top: .4rem; }
  summary { font-size: .78rem; color: var(--muted); }
  .notice { margin: .4rem 0; }
</style>
