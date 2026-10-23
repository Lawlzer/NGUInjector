<script lang="ts">
  import type { JsonObject } from './api';
  export let data: JsonObject = {};
  export let connected = false;
</script>

<div class="pill-rules">
  {#if connected && data.available === true}
    {#if data.eligible !== true}
      <p class="notice" role="status">Blocked: {String(data.reason || 'Casting requirements are not met.')}</p>
    {:else if data.reservingBlood === true}
      <p class="notice" role="status" title="Blood Magic takes magic priority, except during Time Machine and Wandoos stages.">Saving blood for Iron Pill.</p>
    {:else if typeof data.retryInSeconds === 'number' && Number.isFinite(data.retryInSeconds) && data.retryInSeconds > 0}
      <p class="notice" role="status" title="Normal priorities restored.">Retry in {Math.ceil(data.retryInSeconds)}s.</p>
    {/if}
  {:else if connected}
    <p class="notice" role="status">{String(data.reason || 'Iron Pill status unavailable.')}</p>
  {:else}
    <p class="notice" role="status">Connect to see Iron Pill status.</p>
  {/if}
</div>

<style>
  .pill-rules { display: flex; align-items: baseline; flex-wrap: wrap; gap: .35rem .75rem; margin-top: .5rem; min-width: 0; }
  .notice { flex: 1 1 14rem; margin: 0; padding: .35rem .6rem; line-height: 1.4; }
</style>
