<script lang="ts">
  import type { JsonObject, JsonValue } from './api';
  import { INT_MAX, object, pretty } from './model';

  export let data: JsonObject;
  export let activePlanName: string;
  export let connected: boolean;

  type Target = { index: number; name: string; weight: number | null; allocated: string | null; cap: string | null; below: boolean };
  const compact = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 2 });
  $: runtime = object(data);
  $: available = connected && runtime.available === true;
  $: energy = targets(runtime.energyTargets, runtime.energyWeights, 9);
  $: magic = targets(runtime.magicTargets, runtime.magicWeights, 7);
  $: unavailableReason = !connected ? 'Connect to see NGU allocation.'
    : typeof runtime.reason === 'string' && runtime.reason.trim() && runtime.available === false
      ? runtime.reason : 'NGU allocation unavailable.';

  function decimal(value: JsonValue | undefined): string | null {
    return typeof value === 'string' && /^\d+$/.test(value) ? value : null;
  }
  function weight(value: JsonValue | undefined): number | null {
    return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= INT_MAX ? value : null;
  }
  function targets(value: JsonValue | undefined, weights: JsonValue | undefined, length: number): Target[] {
    if (!Array.isArray(value)) return [];
    return value.map(object).filter(row => typeof row.index === 'number' && Number.isInteger(row.index) && row.index >= 0 && row.index < length
      && typeof row.name === 'string' && row.name.trim()).map(row => {
      const allocated = decimal(row.allocated);
      const cap = decimal(row.bufferedCap);
      return { index: row.index as number, name: row.name as string,
        weight: weight(Array.isArray(weights) ? weights[row.index as number] : row.weight), allocated, cap,
        below: row.belowBufferedCap === true && allocated !== null && cap !== null && BigInt(allocated) < BigInt(cap) };
    }).filter(row => (row.weight !== null && row.weight > 0) || (row.allocated !== null && BigInt(row.allocated) > 0n));
  }
  function amount(value: string | null): string {
    return value === null ? 'unknown' : compact.format(BigInt(value));
  }
  function appliedWeight(value: JsonValue | undefined, index: number): string {
    return String(Array.isArray(value) ? weight(value[index]) ?? 'unknown' : 'unknown');
  }
</script>

<section aria-label="NGU allocation">
  <span class="context">Active plan: <strong>{activePlanName.trim() || 'unavailable'}</strong>. Read-only allocation telemetry.</span>
  {#if !available}
    <p class="context" role="status">{unavailableReason}</p>
  {:else}
    {#each ([['ENERGY', energy, runtime.energyWeightsApplied, runtime.energyAppliedWeights], ['MAGIC', magic, runtime.magicWeightsApplied, runtime.magicAppliedWeights]] as const) as [resource, rows, applied, appliedWeights]}
      <div class="allocation-row">
        <span class="resource">{resource}</span>
        <div>
          <p class="context">{applied === true ? 'Selected weights applied.' : applied === false ? 'Selected weights not applied; actual allocations may reflect previous weights or other priorities.' : 'Weight application unknown.'}</p>
          <ul aria-label={`${resource} NGU targets`}>
            {#each rows as target}
              <li title={`${target.name}: allocated ${target.allocated ?? 'unknown'}; buffered cap ${target.cap ?? 'unknown'}`}>
                {target.name} <span class="context">weight {target.weight ?? 'unknown'}{#if applied !== true}{' / last applied weight '}{appliedWeight(appliedWeights, target.index)}{/if}</span>
                <strong>{amount(target.allocated)}</strong>
                {#if runtime.strategy === 'weighted-ngu-buffered-cap' && runtime.capBasis === 'last-allocation-pass' && target.below}<span class="context">below buffered cap</span>{/if}
              </li>
            {:else}<li class="context">No selected or allocated targets reported.</li>{/each}
          </ul>
        </div>
      </div>
    {/each}
    <details><summary>Allocation details</summary><pre>{pretty(runtime)}</pre></details>
  {/if}
</section>

<style>
  section { min-width: 0; display: grid; gap: .45rem; }
  .context { color: var(--muted, #a6afb9); font-size: .78rem; overflow-wrap: anywhere; }
  .context strong { color: var(--accent, #d5dbe3); font-weight: 600; }
  p { margin: 0 0 .25rem; }
  .allocation-row { display: grid; grid-template-columns: 4rem minmax(0, 1fr); gap: .5rem; align-items: baseline; }
  .resource { font: .68rem Consolas, monospace; letter-spacing: .06em; color: var(--muted, #a6afb9); }
  ul { display: flex; flex-wrap: wrap; gap: .25rem 1rem; list-style: none; padding: 0; margin: 0; }
  li { font-size: .8rem; overflow-wrap: anywhere; min-width: 0; }
  li strong { font-variant-numeric: tabular-nums; }
  details { margin: 0; }
  summary { font-size: .75rem; color: var(--muted, #a6afb9); padding: .15rem 0; }
  pre { margin-top: .35rem; max-height: 16rem; font-size: .72rem; overflow-wrap: anywhere; white-space: pre-wrap; }
</style>
