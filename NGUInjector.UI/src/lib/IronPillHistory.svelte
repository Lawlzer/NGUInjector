<script lang="ts">
  import type { JsonValue } from './api';
  import { object } from './model';

  export let history: JsonValue | undefined;
  $: entries = (Array.isArray(history) ? history : []).map(object)
    .filter(entry => typeof entry.CastAtUtc === 'string')
    .sort((a, b) => (Date.parse(String(b.CastAtUtc)) || 0) - (Date.parse(String(a.CastAtUtc)) || 0)).slice(0, 10);

  function duration(value: JsonValue | undefined): string {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) return 'unknown';
    const total = Math.floor(value);
    return `${Math.floor(total / 3600)}h ${Math.floor(total % 3600 / 60)}m ${total % 60}s`;
  }
  function worth(value: JsonValue | undefined): string {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) return 'unknown';
    return value >= 100000 || value > 0 && value < .001 ? value.toExponential(3) : value.toLocaleString('en-US', { maximumFractionDigits: 3 });
  }
  function timestamp(value: JsonValue | undefined): string {
    const time = Date.parse(String(value));
    return Number.isFinite(time) ? new Date(time).toISOString().replace('T', ' ').replace(/\.\d{3}Z$/, ' UTC') : 'Unknown timestamp';
  }
</script>

<section aria-label="Iron Pill cast history">
  <h3>Recent casts</h3>
  <ol>
    {#each entries as entry}
      <li><time datetime={String(entry.CastAtUtc)}>{timestamp(entry.CastAtUtc)}</time>
        <span><strong>Wait:</strong> {duration(entry.WaitSeconds)}</span>
        <span><strong>Worth:</strong> {worth(entry.Worth)}</span>
        <span><strong>Rebirth time:</strong> {duration(entry.RebirthSeconds)}</span></li>
    {:else}
      <li class="muted">No automatic casts yet.</li>
    {/each}
  </ol>
</section>

<style>
  section { margin-top: 1rem; }
  h3 { margin-bottom: .5rem; }
  ol { list-style: none; padding: 0; margin: 0; }
  li { display: flex; flex-wrap: wrap; gap: .5rem 1rem; padding: .75rem 0; border-bottom: 1px solid var(--line); overflow-wrap: anywhere; }
  time { flex-basis: 100%; font-weight: 600; }
</style>
