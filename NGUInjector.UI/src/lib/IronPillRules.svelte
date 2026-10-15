<script lang="ts">
  import type { JsonObject } from './api';
  export let data: JsonObject = {};
  export let connected = false;
</script>

{#if connected && data.available === true}
  <p class="notice" role="status">{data.eligible === true ? 'Eligible. Waiting for cooldown and a casting threshold.' : `Blocked: ${String(data.reason || 'Casting requirements are not met.')}`}</p>
{:else if connected}
  <p class="notice" role="status">{String(data.reason || 'Iron Pill status unavailable.')}</p>
{:else}
  <p class="notice" role="status">Connect to see Iron Pill status.</p>
{/if}
<details>
  <summary>Casting rules</summary>
  <ul>
    <li>Run a plan that allows rebirth. Enable auto-rebirth and auto blood spells.</li>
    <li>Reach 1h of actual rebirth time and boss stage 100. Planned duration does not count.</li>
    <li>When ready, cast after either the saving time or minimum worth is reached.</li>
    <li>Zero disables a threshold. Both zero: cast as soon as ready.</li>
    <li>Reserve blood and focus Augments + Blood when ready or within the saving-time cooldown window. Worth-only mode uses a 900s lookahead.</li>
    <li>Worth is base Power and Toughness gained each, capped at 100 million.</li>
    <li>Manual rebirth and boss push take priority over blood reservation.</li>
    <li>Save Settings to apply edits. Defaults: 900s saving time, 0 minimum worth.</li>
  </ul>
</details>
