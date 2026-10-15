<script lang="ts">
  import type { JsonObject } from '../../src/lib/api';
  import NguAllocation from '../../src/lib/NguAllocation.svelte';

  export let data: JsonObject;
  export let activePlanName = '24hr';
  export let connected = true;

  export function update(next: JsonObject, plan = activePlanName, fresh = connected): void {
    data = next;
    activePlanName = plan;
    connected = fresh;
  }
</script>

<NguAllocation {data} {activePlanName} {connected} />
