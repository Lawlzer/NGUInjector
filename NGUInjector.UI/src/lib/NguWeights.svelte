<script lang="ts">
  import { isSafeNumber } from 'lossless-json';
  import type { JsonObject, JsonValue } from './api';
  import { INT_MAX, validatePlan } from './model';
  import { energyNguNames, magicNguNames } from './options';

  export let plan: JsonObject;
  export let name: string;
  export let dirty: boolean;
  export let activePlanName = '';
  export let disabled = false;
  export let weightsWritable = true;
  export let onchange: (plan: JsonObject) => void;

  $: hasWeights = Number(plan.Version) >= 16 && Array.isArray(plan.EnergyNguWeights) && plan.EnergyNguWeights.length === 9 && Array.isArray(plan.MagicNguWeights) && plan.MagicNguWeights.length === 7;
  $: validation = planError(plan);

  function planError(value: JsonObject): string {
    try { validatePlan(value); return ''; } catch (err) { return (err as Error).message; }
  }
  function setWeight(key: string, index: number, event: Event): void {
    if (disabled || !weightsWritable || !hasWeights) return;
    const text = (event.currentTarget as HTMLInputElement).value;
    // Keep unsafe tokens local rather than rounding a fractional weight into an integer.
    const next = text.trim() && isSafeNumber(text, { approx: false }) ? Number(text) : text;
    onchange({ ...plan, [key]: (plan[key] as JsonValue[]).map((weight, i) => i === index ? next : weight) });
  }
</script>

<article id="ngu-weights" class="card span-all" aria-label="NGU weights" inert={disabled}>
  <h2>NGU weights</h2>
  <p class="muted">Editing plan: <strong>{name}{dirty ? ' *' : ''}</strong>.</p>
  {#if activePlanName}<p class="muted">Active plan: {activePlanName}. {activePlanName === name ? 'Editing this active plan.' : `Edits apply to ${name}, not automatically to the active plan.`}</p>{/if}
  <p class="muted">Uses the plan selected above. Changes save automatically.</p>
  {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
  <p class="muted">0 disables a target. Positive weights are ratios, not percentages: 1:1 shares equally; 2:1 gives twice the share until buffered caps redistribute it. All-zero weights skip generic NGU for that resource and allow later energy or magic priorities.</p>
  {#if !hasWeights}
    <p class="notice" role="status">NGU weights require a version 16 plan with all 9 energy and 7 magic weights. Connect to the updated injector and reload this plan; the editor does not migrate legacy plans.</p>
  {:else}
    {#if !weightsWritable}<p class="notice" role="status">NGU weight editing requires a connected injector advertising planVersion 16 or newer with plan read/save support. Update, reconnect, and reload.</p>{/if}
    {#each ([['EnergyNguWeights', 'Energy', energyNguNames], ['MagicNguWeights', 'Magic', magicNguNames]] as const) as [key, resource, names]}
      <h3>{resource}</h3>
      <div class="form-grid three">
        {#each names as target, index}
          <label>{resource} {target} weight<input aria-label={`${resource} ${target} weight`} type="number" min="0" max={INT_MAX} step="1" value={String((plan[key] as JsonValue[])[index])} disabled={disabled || !weightsWritable} on:input={event => setWeight(key, index, event)} /></label>
        {/each}
      </div>
    {/each}
  {/if}
</article>
