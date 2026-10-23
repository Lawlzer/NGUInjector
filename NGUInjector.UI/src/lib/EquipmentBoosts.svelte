<script lang="ts">
  import type { JsonObject, JsonValue } from './api';
  import { object } from './model';

  export let settings: JsonObject;
  export let telemetry: JsonObject = {};
  export let connected = false;
  export let disabled = false;
  export let onedit: (key: string, value: JsonValue) => void;

  $: priorities = [...new Set(Array.isArray(settings._priorityBoosts) ? settings._priorityBoosts.filter((id): id is number => typeof id === 'number') : [])];
  $: blacklist = Array.isArray(settings._boostBlacklist) ? settings._boostBlacklist : [];
  $: items = (Array.isArray(telemetry.items) ? telemetry.items.map(object) : [])
    .map(item => ({ id: Number(item.id), name: String(item.name), copies: Number(item.copies), level: Number(item.level), fullyBoosted: item.fullyBoosted === true }))
    .filter(item => !(item.level >= 100 && item.fullyBoosted))
    .sort((a, b) => (priorities.includes(a.id) ? priorities.indexOf(a.id) : Infinity) - (priorities.includes(b.id) ? priorities.indexOf(b.id) : Infinity) || a.id - b.id);

  function setPriority(id: number, event: Event): void {
    if (disabled || !connected || telemetry.available !== true) return;
    const value = (event.currentTarget as HTMLSelectElement).value;
    const next = priorities.filter(other => other !== id);
    if (value !== '') next.splice(Number(value), 0, id);
    onedit('_priorityBoosts', next);
  }

  function setEnabled(id: number, event: Event): void {
    if (disabled || !connected || telemetry.available !== true) return;
    const enabled = (event.currentTarget as HTMLInputElement).checked;
    onedit('_boostBlacklist', enabled ? blacklist.filter(other => other !== id) : [...blacklist, id]);
  }
</script>

<article class="card span-all" aria-label="Equipment boost priorities">
  <div class="card-header"><h2>Equipment boost priorities</h2></div>
  <p class="muted">Priority 1 is boosted first. Default uses equipped gear, then locked or auto-merge-slot gear, with hearts last. Priorities do not make other unlocked inventory eligible.</p>
  <p class="muted">One row per item, including disabled items. Hidden only when every owned copy is level 100 and fully boosted.</p>
  {#if settings._customAutoBoostEnabled !== true}<p class="notice">Auto boost is off. Enable it in Adventure to use these priorities.</p>{/if}
  {#if !connected}
    <p class="muted" role="status">Connect to load owned equipment.</p>
  {:else if telemetry.available !== true}
    <p class="muted" role="status">{typeof telemetry.reason === 'string' && telemetry.reason ? telemetry.reason : 'Equipment list unavailable. Update and reconnect the injector.'}</p>
  {:else if !items.length}
    <p class="muted" role="status">No equipment needs levels or boosts.</p>
  {:else}
    <ul aria-label="Owned equipment">
      {#each items as item (item.id)}
        <li class:boost-disabled={blacklist.includes(item.id)}>
          <div class="item-name"><strong>{item.name}</strong><small>#{item.id} / Level {item.level} / {item.copies} {item.copies === 1 ? 'copy' : 'copies'} / {item.fullyBoosted ? 'Fully boosted' : 'Needs boosts'}</small></div>
          <label>Priority
            <select aria-label={`Boost priority for ${item.name}`} value={priorities.includes(item.id) ? String(priorities.indexOf(item.id)) : ''} {disabled} on:change={event => setPriority(item.id, event)}>
              <option value="">Default</option>
              {#each Array.from({ length: priorities.length + (priorities.includes(item.id) ? 0 : 1) }, (_, index) => index) as rank}
                <option value={String(rank)}>{rank + 1}{rank === 0 ? ' (first)' : ''}</option>
              {/each}
            </select>
          </label>
          <label class="boost-toggle"><input type="checkbox" aria-label={`Boost ${item.name}`} checked={!blacklist.includes(item.id)} {disabled} on:change={event => setEnabled(item.id, event)} />Boost</label>
        </li>
      {/each}
    </ul>
  {/if}
</article>

<style>
  .notice { margin-top: .7rem; }
  ul { list-style: none; margin: .8rem 0 0; padding: 0; }
  li { display: grid; grid-template-columns: minmax(0, 1fr) 8rem 5rem; align-items: center; gap: .75rem; padding: .75rem 0; border-top: 1px solid var(--line); }
  .item-name { min-width: 0; display: grid; gap: .25rem; overflow-wrap: anywhere; font-size: .88rem; }
  .boost-disabled .item-name { color: var(--muted); }
  select { padding: .45rem; }
  .boost-toggle { display: flex; align-items: center; gap: .45rem; }
  @media (max-width: 600px) {
    li { grid-template-columns: minmax(0, 1fr) 5rem; }
    .item-name { grid-column: 1 / -1; }
  }
</style>
