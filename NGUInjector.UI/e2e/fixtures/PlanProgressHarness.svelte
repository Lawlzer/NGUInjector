<script lang="ts">
  import type { JsonObject } from '../../src/lib/api';
  import PlanProgress from '../../src/lib/PlanProgress.svelte';

  export let data: JsonObject;
  export let connected = true;
  export let onedit: ((stageIndex: number) => void) | undefined = undefined;

  export function update(next: JsonObject, fresh: boolean): void {
    data = next;
    connected = fresh;
  }
</script>

<PlanProgress {data} {connected} {onedit} />
