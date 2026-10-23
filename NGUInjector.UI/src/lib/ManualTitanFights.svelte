<script lang="ts">
  import type { JsonObject, JsonValue } from './api';
  import { manualCombatLoadouts, titanZones } from './options';

  export let settings: JsonObject;
  export let telemetry: JsonObject = {};
  export let connected = false;
  export let onedit: (key: string, value: JsonValue, delay?: number) => void;

  function numberInput(event: Event): JsonValue {
    const text = (event.currentTarget as HTMLInputElement | HTMLSelectElement).value;
    return text.trim() && Number.isFinite(Number(text)) ? Number(text) : text;
  }

  $: runtimeZone = titanZones.find(zone => zone.value === telemetry.zone)?.label ?? `Zone ${telemetry.zone ?? 'unknown'}`;
  $: recoveryError = typeof telemetry.recoveryError === 'string' ? telemetry.recoveryError : '';
</script>

<article class="card" aria-label="Manual Titan Fights">
  <h2>Manual Titan Fights</h2>
  <label class="toggle-row"><input type="checkbox" checked={settings._manualTitanEnabled === true} on:change={event => onedit('_manualTitanEnabled', (event.currentTarget as HTMLInputElement).checked)} /><span><strong>Enable manual titan fights</strong></span></label>
  <div class="form-grid">
    <label>Titan target<select value={settings._manualTitanZone ?? 16} on:change={event => onedit('_manualTitanZone', numberInput(event))}>{#each titanZones as zone}<option value={zone.value}>{zone.label}</option>{/each}</select></label>
    <label>Titan loadout<select value={settings._manualTitanLoadout ?? 1} on:change={event => onedit('_manualTitanLoadout', numberInput(event))}>{#each manualCombatLoadouts as loadout}<option value={loadout.value}>{loadout.label}</option>{/each}</select></label>
    <label>Max titan deaths<input type="number" min="1" max="1000" step="1" value={String(settings._manualTitanMaxDeaths ?? 3)} on:input={event => onedit('_manualTitanMaxDeaths', numberInput(event), 350)} /></label>
  </div>
  <label class="toggle-row"><input type="checkbox" aria-describedby="manual-titan-at-help" checked={(settings._manualTitanRequireAT ?? true) === true} on:change={event => onedit('_manualTitanRequireAT', (event.currentTarget as HTMLInputElement).checked)} /><span><strong>Require AT targets reached</strong></span></label>
  <p class="muted" id="manual-titan-at-help">Set positive Power and Toughness targets in-game.</p>
  <p class="muted">Gear swaps recall energy, magic, and R3.</p>
  <p class="muted" role="status">{!connected ? 'Disconnected' : telemetry.recovering === true ? `Recovering gear: ${runtimeZone} / Combat blocked` : typeof telemetry.active !== 'boolean' ? 'Manual titan status not reported.' : `${telemetry.active ? 'Active' : 'Inactive'}: ${runtimeZone} / ${telemetry.remainingDeaths ?? 'Unknown'} deaths remaining / ${telemetry.status ?? 'Status not reported'}`}</p>
  {#if connected && telemetry.recovering === true}
    <p class="notice">All combat stays blocked until original gear is restored, even if manual titan fights are disabled.</p>
  {/if}
  {#if connected && recoveryError}
    <p class="notice" role="alert"><strong>Exact gear restore failed:</strong> {recoveryError}</p>
  {/if}
  <details><summary>Attempt limits</summary><p class="muted">Stops after a kill, Walderp phase, the per-spawn death limit, or 10 minutes of active attempts. Resumes on the next spawn. Find Walderp in-game after each phase, before the next spawn. Restarting the injector resets the attempt and remaining deaths.</p></details>
</article>
