<script lang="ts">
  import type { JsonObject, JsonValue } from './api';
  import { object } from './model';
  import InlineNumber from './InlineNumber.svelte';
  import type { EditSession } from './controller';

  export let data: JsonObject;
  export let connected: boolean;
  export let pending = false;
  export let savedGoals: JsonObject = {};
  export let editable = false;
  export let beginGoalEdit: ((goal: string) => EditSession) | undefined = undefined;

  $: hasGoals = Object.keys(object(data.spaghetti)).length > 0 || Object.keys(object(data.counterfeit)).length > 0;
  $: unavailable = !connected || data.available === false || !hasGoals;
  $: unavailableReason = !connected ? 'Connect to see estimates.'
    : String(data.reason || 'Blood estimates unavailable.');

  function amount(value: JsonValue | undefined): string {
    return typeof value === 'number' && Number.isFinite(value) ? value.toLocaleString('en-US', { maximumFractionDigits: 2, notation: 'compact' }) : 'unknown';
  }
  function eta(goal: JsonObject): string {
    if (!connected) return 'Estimate unavailable';
    if (goal.status !== 'Casting') return String(goal.status ?? 'Estimate unavailable');
    const seconds = goal.estimatedSeconds;
    if (typeof seconds !== 'number' || !Number.isFinite(seconds)) return 'Time unavailable';
    const total = Math.ceil(Math.max(0, seconds));
    const time = [Math.floor(total / 3600) ? `${Math.floor(total / 3600)}h` : '', Math.floor(total % 3600 / 60) ? `${Math.floor(total % 3600 / 60)}m` : '', total % 60 ? `${total % 60}s` : ''].filter(Boolean).join(' ') || '0s';
    return `about ${time}`;
  }
</script>

{#if unavailable}
  <p class="notice" role="status">{unavailableReason}</p>
{:else}
<div class="blood-goals">
  {#each [['spaghetti', 'Blood Spaghetti (drop chance)'], ['counterfeit', 'Counterfeit Gold']] as [key, label]}
    {@const goal = object(data[key])}
    <section>
      <h3>{label}</h3>
      {#if Object.keys(goal).length}
        <div class="progress">{amount(goal.currentPercent)}% <span>/
          {#if beginGoalEdit}
            <InlineNumber label={key === 'spaghetti' ? 'Spaghetti goal (%)' : 'Counterfeit goal (%)'} value={savedGoals[key] ?? goal.goalPercent} display={`${amount(savedGoals[key] ?? goal.goalPercent)}%`} min={0} max={100000} disabled={!editable} onbegin={() => beginGoalEdit!(key)} />
          {:else}{amount(goal.goalPercent)}%{/if} goal</span>
        </div>
        <p><strong>Time:</strong> {savedGoals[key] !== undefined && Number(savedGoals[key]) !== Number(goal.goalPercent) ? 'Updating estimate...' : eta(goal)}</p>
        <p class="muted">{amount(goal.remainingBlood)} blood remaining; {amount(goal.bloodPerSecond)}/s</p>
        {#if goal.productionAssumption}
          <details><summary>Ritual details</summary><p class="muted">{String(goal.productionAssumption)}</p><p class="muted">Total blood needed: {amount(goal.requiredBlood)}.</p></details>
        {/if}
      {:else}
        <p class="muted">Estimate unavailable.</p>
      {/if}
    </section>
  {/each}
</div>
<p class="muted">Estimate: all magic, current gear.</p>
{/if}
{#if pending && !unavailable}<p class="notice">Using saved goals. Save Settings to update estimates.</p>{/if}

<style>
  .blood-goals { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1.25rem; }
  section { min-width: 0; }
  h3 { margin: 0 0 .5rem; }
  p { overflow-wrap: anywhere; }
  .progress { font-size: 1.5rem; font-weight: 700; margin: .5rem 0; }
  .progress span { font-size: .9rem; font-weight: 400; }
  @media (max-width: 650px) { .blood-goals { grid-template-columns: 1fr; } }
</style>
