<script lang="ts">
  import { isObject, type JsonObject, type JsonValue } from './api';
  import { object } from './model';
  import { optionLabel, titanZones, zones } from './options';

  export let review: JsonObject = {};
  export let lastResult: JsonObject | null = null;
  export let busy = false;
  export let canPreview = false;
  export let canApply = false;
  export let canRestart = false;
  export let oncommand: (command: string, args?: JsonObject) => Promise<void>;
  let preview: JsonObject | null = null;
  let consumedId = '';
  let restartStage = '';
  $: recovery = object(review.recovery);
  $: if ((lastResult?.command === 'previewExecutionRebind' || lastResult?.command === 'previewExecutionRestart') && lastResult.success === true && lastResult.id !== consumedId) {
    consumedId = String(lastResult.id);
    const result = object(object(lastResult.data).result);
    preview = typeof result.token === 'string' && typeof result.checkpointRevision === 'string' ? result : null;
  }
  $: equipment = object(preview?.equipment).equipment;
  $: support = object(preview?.support);
  $: commitments = object(preview?.commitments);
  $: processes = Array.isArray(commitments.processes) ? commitments.processes.filter(isObject) : [];
  $: manualHistory = Array.isArray(commitments.manual) ? commitments.manual.filter(isObject) : [];
  $: towerActive = processes.some(process => process.owner === 'max-itopod');
  $: hasTower = towerActive || Number(commitments.towerDeaths) > 0 || commitments.towerHistory === 'lost'
    || Array.isArray(commitments.towerGear) && commitments.towerGear.length > 0;
  $: hasBoss = processes.some(process => process.owner === 'boss-push')
    || typeof commitments.bossIdentity === 'string' && commitments.bossIdentity.length > 0;

  function titanName(index: unknown): string {
    return titanZones[Number(index)]?.label ?? `Titan ${String(index)}`;
  }

  function routeOwner(owner: JsonValue | undefined): string {
    const value = String(owner ?? '');
    if (value.startsWith('manual-titan:')) return `Manual titan: ${titanName(value.slice('manual-titan:'.length))}`;
    return ({ 'boss-push': 'Boss push', 'max-itopod': 'ITOPOD push', 'titan-gold': 'Titan gold',
      'titan-autokill': 'Titan autokill', 'adventure-gold': 'Adventure gold',
      'delegated-manual-adventure': 'Manual adventure' } as Record<string, string>)[value] ?? value;
  }
  async function apply(): Promise<void> {
    if (!preview) return;
    const args = { token: preview.token, checkpointRevision: preview.checkpointRevision };
    preview = null;
    await oncommand('applyExecutionRebind', args);
  }
</script>

{#snippet gearSelection(rows: JsonValue | undefined, slotKey: 'slot' | 'targetSlot')}
  {#if Array.isArray(rows) && rows.length > 0}
    <table class="selection-gear" aria-label="Selected gear"><thead><tr><th>Slot</th><th>Item</th></tr></thead><tbody>
      {#each rows.filter(isObject) as item}<tr><td>{String(item[slotKey])}</td><td>{item.id === 0 ? 'Empty' : String(item.id)}</td></tr>{/each}
    </tbody></table>
  {/if}
{/snippet}

{#snippet supportSelection(selection: JsonObject)}
  {#if Array.isArray(selection.beards)}<p>Beards: {selection.beards.join(', ') || 'None'}</p>{/if}
  {#if Array.isArray(selection.diggers)}<p>Diggers: {selection.diggers.filter(isObject).map(d => `${d.id}: ${d.level}`).join(', ') || 'None'}</p>{/if}
{/snippet}

{#if review.reviewRequired === true}
  <article class="card span-all" aria-label="Selection review">
    <h2>Review Selection</h2>
    {#if review.reason}<p role="status">{String(review.reason)}</p>{/if}
    <div class="button-row">
      <button type="button" disabled={busy || !canPreview || review.canPreview !== true} on:click={() => { preview = null; void oncommand('previewExecutionRebind'); }}>Preview selection</button>
      {#if preview}<button type="button" disabled={busy || !canApply} on:click={apply}>{preview.operation === 'restart' ? 'Restart selected stage' : 'Approve selection'}</button>{/if}
    </div>
    {#if Array.isArray(recovery.stages)}
      <label>Restart stage<select bind:value={restartStage} disabled={busy} on:change={() => preview = null}>
        <option value="" disabled>Select stage</option>
        {#each recovery.stages.filter(isObject) as stage}<option value={String(stage.stageIndex)}>{String(stage.name)}</option>{/each}
      </select></label>
      <button type="button" disabled={busy || !canRestart || restartStage === ''} on:click={() => { preview = null; void oncommand('previewExecutionRestart', { stageIndex: Number(restartStage), checkpointRevision: recovery.checkpointRevision, planRevision: recovery.planRevision }); }}>Review restart</button>
    {/if}
    {#if preview}
      <h3>{String(object(preview.preset).Name ?? preview.owner ?? '')}</h3>
      {#if preview.domains}<p>Domains: {String(preview.domains)}</p>{/if}
      {#if object(preview.commitments).kind === 'fight-preparation'}
        <dl>
          <div><dt>Phase</dt><dd>{String(object(preview.commitments).stage ?? '')}</dd></div>
          <div><dt>Started</dt><dd>{String(object(object(preview.commitments).original).started ?? '')}s</dd></div>
          <div><dt>Power fruits</dt><dd>{object(object(preview.commitments).original).consumePowerFruits === true ? 'Yes' : 'No'}</dd></div>
        </dl>
      {/if}
      {#if preview.operation === 'restart'}<p>Restart {String(preview.stageName)} with a new entry. Original checkpoint is retained.</p>{/if}
      {@render gearSelection(equipment, 'targetSlot')}
      {@render supportSelection(support)}
      {#each processes as process}
        <section class="route-commitment" aria-label={`${routeOwner(process.owner)} selection`}>
          <h4>{routeOwner(process.owner)}</h4>
          <p>Zone: {optionLabel(zones, process.zone)}</p>
          {@render gearSelection(process.gear, 'slot')}
          {@render supportSelection(process)}
        </section>
      {/each}
      {#each manualHistory as attempt}
        <section class="route-commitment" aria-label={`${titanName(attempt.index)} history`}>
          <h4>{titanName(attempt.index)} history</h4>
          <dl class="stats">
            <div><dt>Deaths</dt><dd>{String(attempt.deaths)} / {String(attempt.limit)}</dd></div>
            <div><dt>Owned time</dt><dd>{String(attempt.ownedSeconds)}s</dd></div>
            {#if attempt.outcome}<div><dt>Outcome</dt><dd>{String(attempt.outcome)}</dd></div>{/if}
          </dl>
          {@render gearSelection(attempt.gear, 'slot')}
        </section>
      {/each}
      {#if hasTower}
        <section class="route-commitment" aria-label="ITOPOD push history">
          <h4>ITOPOD push</h4>
          <dl class="stats">
            <div><dt>Floor</dt><dd>{String(commitments.towerFloor)}</dd></div>
            <div><dt>Deaths remaining</dt><dd>{String(commitments.towerDeaths)}</dd></div>
            {#if commitments.towerHistory}<div><dt>Death history</dt><dd>{String(commitments.towerHistory)}</dd></div>{/if}
          </dl>
          {#if !towerActive}{@render gearSelection(commitments.towerGear, 'slot')}{/if}
        </section>
      {/if}
      {#if hasBoss && commitments.bossSeconds !== undefined}
        <section class="route-commitment" aria-label="Boss push history">
          <h4>Boss push</h4>
          <p>Remaining fight window: {String(commitments.bossSeconds)}s</p>
        </section>
      {/if}
    {/if}
  </article>
{/if}

<style>
  .route-commitment { border-top: 1px solid var(--line); margin-top: 1rem; padding-top: .75rem; min-width: 0; overflow-wrap: anywhere; }
  .selection-gear { width: 100%; border-collapse: collapse; margin-top: .75rem; }
  .selection-gear th, .selection-gear td { padding: .3rem .5rem .3rem 0; border-bottom: 1px solid var(--line); text-align: left; overflow-wrap: anywhere; }
</style>
