<script lang="ts">
  import type { JsonValue } from './api';
  import { object, pretty } from './model';

  export let history: JsonValue | undefined = undefined;
  $: entries = Array.isArray(history) ? history : [];

  function duration(seconds: JsonValue | undefined): string {
    if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) return 'Unknown';
    const total = Math.floor(seconds);
    return `${Math.floor(total / 3600)}h ${Math.floor(total % 3600 / 60)}m ${total % 60}s`;
  }
</script>

{#if entries.length}
  <!-- svelte-ignore a11y_no_noninteractive_tabindex (Keyboard focus enables horizontal scrolling.) -->
  <div class="history-scroll" role="region" aria-label="Rebirth history table" tabindex="0">
    <table>
      <caption>Rebirth history</caption>
      <thead><tr>
        <th scope="col">Local timestamp</th><th scope="col">Reason</th><th scope="col">Duration</th><th scope="col">Boss</th>
        <th scope="col">NUMBER before</th><th scope="col">NUMBER after</th><th scope="col">Gain</th><th scope="col">Details</th>
      </tr></thead>
      <tbody>
        {#each entries as entry}
          {@const record = object(entry)}
          <tr>
            <td>{String(record.TimestampLocal || 'Unknown time')}</td>
            <td>{String(record.Reason || 'Rebirth')}</td>
            <td>{duration(record.RebirthSeconds)}</td>
            <td>{String(record.BossId ?? '?')}</td>
            <td>{String(record.NumberBefore ?? '?')}</td>
            <td>{String(record.NumberAfter ?? '?')}</td>
            <td>{record.NumberMultiplier == null ? '?' : `${String(record.NumberMultiplier)}x`}</td>
            <td><details><summary>Details</summary><pre>{pretty(entry)}</pre></details></td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
{:else}
  <p class="muted">No rebirths yet.</p>
{/if}

<style>
  .history-scroll { min-width: 0; max-width: 100%; overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; font-size: .8rem; font-variant-numeric: tabular-nums; }
  caption { text-align: left; font-weight: 600; padding-bottom: .5rem; }
  th, td { text-align: left; vertical-align: top; padding: .45rem .5rem; border-bottom: 1px solid var(--line); white-space: nowrap; }
  th { color: var(--muted); font-size: .7rem; font-weight: 500; }
  summary { cursor: pointer; }
  pre { max-width: 28rem; white-space: pre-wrap; overflow-wrap: anywhere; }
  .muted { color: var(--muted); }
</style>
