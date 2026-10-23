<script lang="ts">
  import { isObject, type JsonObject, type JsonValue } from './api';
  import adventureZoneData from '../../../NGUInjector/Data/AdventureZones.json';
  import { object } from './model';
  import { presetZones } from './options';
  import { resultRows } from './presets';

  const data: JsonObject = isObject(adventureZoneData) ? adventureZoneData : {};
  export let zoneId = -1;
  $: zone = resultRows(data.zones).find(row => row.zoneId === zoneId);

  function metric(value: JsonValue | undefined): string {
    return typeof value === 'number' && Number.isFinite(value)
      ? value.toLocaleString(undefined, { maximumSignificantDigits: 15 }) : 'Unknown';
  }
</script>

<article class="card span-all" aria-label="Zone reference">
  <h2>Zone reference</h2>
  <label class="zone-picker">Reference zone
    <select bind:value={zoneId}>
      <option value={-1}>Select a zone</option>
      {#each presetZones.filter(row => row.value >= 0) as row}<option value={row.value}>{row.label}</option>{/each}
    </select>
  </label>
  {#if zone}
    <div class="card-header"><h3>{String(zone.name)} / {String(zone.difficulty)}</h3><a href={String(zone.sourceUrl)} target="_blank" rel="noreferrer">Zone wiki source</a></div>
    <p class="muted">Unlock boss: {metric(zone.unlockBoss)}</p>
    <div class="recommendations">
      {#each ['idle', 'oneHit'] as mode}
        <section aria-label={mode === 'idle' ? 'Idle recommendations' : 'One-hit recommendations'}>
          <h3>{mode === 'idle' ? 'Idle' : 'One-hit'}</h3>
          {#each resultRows(object(zone.recommendations)[mode]) as recommendation}
            <dl class="stats">
              {#each [['Power', 'power'], ['Toughness', 'toughness'], ['HP', 'hp']] as [label, key]}<div><dt>{label}</dt><dd>{metric(recommendation[key])}</dd></div>{/each}
              {#if typeof recommendation.beastMode === 'boolean'}<div><dt>Beast Mode</dt><dd>{recommendation.beastMode ? 'On' : 'Off'}</dd></div>{/if}
            </dl>
            {#if recommendation.condition}<p class="muted">{String(recommendation.condition)}</p>{/if}
          {:else}<p class="muted">No recommendation available.</p>{/each}
        </section>
      {/each}
    </div>
    {#if Array.isArray(zone.notes) && zone.notes.length}<details><summary>Source notes</summary><ul class="plain-list">{#each zone.notes as note}<li>{String(note)}</li>{/each}</ul></details>{/if}
  {/if}
</article>

<style>
  .zone-picker { max-width: 360px; margin: .8rem 0 1rem; }
  .recommendations { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.5rem; margin-top: 1rem; }
  .recommendations section { min-width: 0; }
  .stats { margin: .5rem 0 1rem; }
  a { color: var(--accent); font-size: .8rem; }
  @media (max-width: 740px) { .recommendations { grid-template-columns: minmax(0, 1fr); } }
</style>
