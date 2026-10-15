<script lang="ts">
  import type { JsonObject, JsonValue } from './api';
  import { INT_MAX, object, parseNumber, pretty } from './model';

  export let canPreview: boolean;
  export let canAllocate: boolean;
  export let busy: boolean;
  export let savedEstimate: JsonValue | undefined;
  export let estimatePending: boolean;
  export let result: JsonObject | null;
  export let oncommand: (command: string, args: JsonObject) => Promise<void>;
  let useSaved = true;
  let expectedSeconds = '300';
  $: if (useSaved) expectedSeconds = String(savedEstimate ?? 300);
  $: validation = useSaved ? '' : estimateError(expectedSeconds);
  $: data = object(result?.data);

  const metrics = [
    ['seconds', 'Time to next bar (s)'], ['valuePerSecond', 'Value per second'],
    ['expectedValue', 'Expected value'],
    ['projectedGoldBudget', 'Projected gold budget'], ['goldPerSecond', 'Gold per second'],
    ['energyPower', 'Energy power'], ['augmentSpeedMultiplier', 'Augment speed multiplier'],
    ['projectedWork', 'Projected work'], ['progressFraction', 'Progress fraction']
  ];
  function estimateError(text: string): string {
    try { parseNumber(text, 'Duration', 1); return ''; }
    catch (err) { return (err as Error).message; }
  }
  function run(command: string): void {
    if (validation || busy) return;
    void oncommand(command, useSaved ? {} : { expectedSeconds: parseNumber(expectedSeconds, 'Duration', 1) });
  }
  function metric(value: JsonValue | undefined): string {
    return typeof value === 'number' && Number.isFinite(value) ? value.toLocaleString(undefined, { maximumSignificantDigits: 6 }) : 'Not reported';
  }
</script>

<article class="card span-2">
  <p class="eyebrow">Augments</p>
  <h2>Choose an augment</h2>
  <div class="form-grid two">
    <label class="toggle-row compact"><input type="checkbox" bind:checked={useSaved} disabled={busy} /><span><strong>Use saved duration</strong></span></label>
    <label>Duration (s)<input type="number" min="1" max={INT_MAX} step="1" value={expectedSeconds} disabled={useSaved || busy} on:input={event => expectedSeconds = (event.currentTarget as HTMLInputElement).value} /></label>
  </div>
  {#if estimatePending && useSaved}<p class="notice">Using saved duration. Save Settings or enter a duration here.</p>{/if}
  {#if validation}<p class="notice error" role="alert">{validation}</p>{/if}
  <div class="button-row">
    <button type="button" disabled={busy || !canPreview || !!validation} on:click={() => run('previewAugments')}>Preview</button>
    <button type="button" class="danger" disabled={busy || !canAllocate || !!validation} on:click={() => run('allocateAugments')}>Allocate</button>
  </div>
  <p class="muted">Allocate recalculates and changes energy assignments.</p>
  {#if result}
    <h3>{result.command === 'allocateAugments' ? 'Last allocation' : 'Last preview'}</h3>
    {#if result.success === false}
      <p class="notice error" role="alert">{String(result.message || 'Unable to choose an augment.')}</p>
    {:else}
      <dl class="stats compact-stats">
        <div><dt>Augment</dt><dd>{typeof data.augmentIndex === 'number' && Number.isInteger(data.augmentIndex) && data.augmentIndex >= 0 ? `Augment ${data.augmentIndex + 1}` : 'Not reported'}{data.usesUpgrade === true ? ' + upgrade' : ''}</dd></div>
        <div><dt>Energy to augment</dt><dd>{metric(data.energyToAugment)}</dd></div>
        <div><dt>Energy to upgrade</dt><dd>{metric(data.energyToUpgrade)}</dd></div>
        <div><dt>Gold cost</dt><dd>{metric(data.projectedGoldCost)}</dd></div>
        <div><dt>Expected time (s)</dt><dd>{metric(data.expectedSeconds)}</dd></div>
      </dl>
    {/if}
    <details><summary>Details</summary>
      <p class="muted">Completed {String(result.completedAt ?? result.completed_at ?? 'at an unknown time')}. Result at that time, not a live estimate.</p>
      <dl class="stats compact-stats">{#each metrics as [key, label]}<div><dt>{label}</dt><dd>{metric(data[key])}</dd></div>{/each}</dl>
      {#if result.message}<p class="muted">{String(result.message)}</p>{/if}
      <details><summary>Full result</summary><pre>{pretty(data)}</pre></details>
    </details>
  {/if}
</article>
