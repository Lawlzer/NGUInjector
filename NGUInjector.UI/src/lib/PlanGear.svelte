<script lang="ts">
  import type { JsonObject } from './api';
  import { fightScoreModes } from './options';
  import GlobalPresetSelect from './GlobalPresetSelect.svelte';
  export let plan: JsonObject;
  export let presetLibrary: JsonObject | null = null;
  export let disabled = false;
  export let onchange: (plan: JsonObject) => void;
</script>

<article class="card" aria-label="Gear settings" inert={disabled}>
  <h2>Gear settings</h2>
  <p class="muted">Plan: <strong>{String(plan.Name ?? '')}</strong></p>
  <div class="form-grid">
    <GlobalPresetSelect library={presetLibrary} label="Active preset" value={String(plan.ActivePresetId ?? '')} legacy={!plan.ActivePresetId} {disabled} onchange={id => onchange({ ...plan, ActivePresetId: id })} />
    <label>Fight scoring<select value={plan.FightScoreMode ?? 0} {disabled} on:change={event => onchange({ ...plan, FightScoreMode: Number(event.currentTarget.value) })}>{#each fightScoreModes as option}<option value={option.value}>{option.label}</option>{/each}</select></label>
  </div>
  <p class="muted">Used by stages set to "Use plan active preset".</p>
</article>
