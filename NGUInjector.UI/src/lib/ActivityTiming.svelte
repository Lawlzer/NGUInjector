<script lang="ts">
  import { activityDuration, type ActivityTimingView } from './activity-timing';

  export let timing: ActivityTimingView | null = null;
  export let showNow = true;
  $: rows = timing?.rows.filter((row, index) => index < 3 || row.elapsedSeconds > 0 || row.active) ?? [];
  $: maximum = Math.max(1, ...rows.map(row => row.elapsedSeconds));
  $: total = rows.reduce((sum, row) => sum + row.elapsedSeconds / maximum, 0);
</script>

<section class="activity-timing" aria-label="Actual activity timing">
  {#if timing}
    <div class="timing-heading"><strong>Actual activity time</strong><span>This run only</span></div>
    {#if showNow && timing.current}<p class="timing-now">Now: {timing.current.kind} / {activityDuration(timing.current.elapsedSeconds)}</p>{/if}
    <div class="timing-strip" aria-hidden="true">
      {#each rows as row}<span data-kind={row.kind} style:width={`${total > 0 ? row.elapsedSeconds / maximum / total * 100 : 0}%`}></span>{/each}
    </div>
    <dl class="timing-rows">
      {#each rows as row}
        <div data-kind={row.kind} class:active={row.active}>
          <dt><span class="swatch" data-kind={row.kind} aria-hidden="true"></span>{row.kind}{#if row.active}<span class="active-badge">Active</span>{/if}</dt>
          <dd>{row.elapsedSeconds === 0 && !row.active ? 'Not visited' : activityDuration(row.elapsedSeconds)}</dd>
        </div>
      {/each}
    </dl>
    <details class="timing-details"><summary>How time is counted</summary><p>Actual exclusive phase wall-time, including phase setup, not a forecast or training credit. Parallel Wandoos and NGUs are not separately added. Excludes entry preparation and cleanup.</p></details>
  {:else}
    <p class="timing-missing">Activity timing not recorded</p>
  {/if}
</section>

<style>
  .activity-timing { min-width: 0; margin-top: .85rem; font-size: .76rem; }
  .timing-heading { display: flex; flex-wrap: wrap; justify-content: space-between; gap: .35rem 1rem; }
  .timing-heading > span, .timing-missing, .timing-details { color: var(--muted); }
  .timing-now { margin: .55rem 0; }
  .timing-strip { display: flex; overflow: hidden; height: 4px; margin: .65rem 0 .4rem; border-radius: 2px; background: #30363f; }
  .timing-strip > span { min-width: 0; }
  [data-kind='AT'] { --activity-color: #8ca9c6; }
  [data-kind='Augments'] { --activity-color: #b6c9a5; }
  [data-kind='Wandoos'] { --activity-color: #c7b5d5; }
  [data-kind='NGU'] { --activity-color: #cfb68c; }
  [data-kind='Background'] { --activity-color: #87919e; }
  .timing-strip > span, .swatch { background: var(--activity-color); }
  .timing-rows { margin: 0; }
  .timing-rows > div { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: baseline; gap: .3rem 1rem; padding: .4rem 0; border-bottom: 1px solid var(--line, #363c44); }
  dt { display: flex; flex-wrap: wrap; align-items: center; gap: .45rem; }
  dd { margin: 0; color: var(--muted); font-variant-numeric: tabular-nums; }
  .active dd { color: var(--ink, #e4e7ed); }
  .swatch { width: .4rem; height: .4rem; border-radius: 1px; }
  .active-badge { border: 1px solid #617284; border-radius: 3px; padding: .05rem .3rem; font-size: .65rem; }
  .timing-details { margin-top: .55rem; line-height: 1.5; overflow-wrap: anywhere; }
  .timing-details summary { font-size: .72rem; }
  .timing-details p { margin: .5rem 0 0; max-width: 70ch; }
  .timing-missing { margin: 0; }
</style>
