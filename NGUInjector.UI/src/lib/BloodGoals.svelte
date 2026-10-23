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
  export let ondirty: (dirty: boolean) => void = () => {};
  let goalDrafts: Record<string, boolean> = {};

  function setGoalDirty(goal: string, dirty: boolean): void {
    goalDrafts = { ...goalDrafts, [goal]: dirty };
    ondirty(Object.values(goalDrafts).some(Boolean));
  }

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
    return `~${time}`;
  }
</script>

{#if unavailable}
  <p class="notice" role="status">{unavailableReason}</p>
{/if}
{#if !unavailable || beginGoalEdit}
<div class="blood-goals">
  {#each [['spaghetti', 'Spaghetti'], ['counterfeit', 'Counterfeit Gold']] as [key, label]}
    {@const goal = object(data[key])}
    <section>
      <h3>{label}</h3>
      <div class="progress">{#if !unavailable && Object.keys(goal).length}{amount(goal.currentPercent)}%{' / '}{/if}<span>
        {#if beginGoalEdit}
          <InlineNumber label={key === 'spaghetti' ? 'Spaghetti goal (%)' : 'Counterfeit goal (%)'} value={savedGoals[key] ?? goal.goalPercent} display={`${amount(savedGoals[key] ?? goal.goalPercent)}%`} min={0} max={100000} disabled={!editable} onbegin={() => beginGoalEdit!(key)} ondirty={dirty => setGoalDirty(key, dirty)} />
        {:else if Object.keys(goal).length}{amount(goal.goalPercent)}%{/if}</span>
      </div>
      {#if !unavailable && Object.keys(goal).length}
        {#if pending || savedGoals[key] !== undefined && Number(savedGoals[key]) !== Number(goal.goalPercent)}
          <p class="muted" role="status">Updating estimate...</p>
        {:else}
          <p class="muted">{#if typeof goal.remainingBlood === 'number' && Number.isFinite(goal.remainingBlood) && goal.remainingBlood > 0}<span>{amount(goal.remainingBlood)} blood left</span>{/if}<span title={goal.status === 'Casting' ? 'ETA assumes all magic with current gear.' : undefined}>{`${goal.status === 'Casting' ? 'ETA ' : ''}${eta(goal)}`}</span></p>
        {/if}
      {:else if !unavailable}
        <p class="muted">Estimate unavailable.</p>
      {/if}
    </section>
  {/each}
</div>
{/if}

<style>
  .blood-goals { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 15rem), 1fr)); gap: .5rem; margin-top: .5rem; }
  section { min-width: 0; display: flex; align-items: baseline; align-content: start; flex-wrap: wrap; gap: .2rem .6rem; padding: .5rem .65rem; border: 1px solid var(--line); border-radius: 4px; }
  h3 { margin: 0; font-size: .85rem; margin-right: auto; }
  p { flex-basis: 100%; display: flex; flex-wrap: wrap; gap: .1rem .75rem; line-height: 1.4; overflow-wrap: anywhere; }
  .progress { min-width: 0; font-size: .85rem; font-variant-numeric: tabular-nums; }
  .progress span { color: var(--muted); }
</style>
