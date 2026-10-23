<script lang="ts">
  import { getUnsafeNumberReason, UnsafeNumberReason } from 'lossless-json';
  import type { JsonObject, JsonValue } from './api';
  import { object } from './model';
  import { zones } from './options';

  export let settings: JsonObject;
  export let telemetry: JsonObject = {};
  export let connected = false;
  export let pending = false;
  export let onedit: (key: string, value: JsonValue, delay?: number) => void;

  const modes = [
    { value: 0, label: 'Optimal ITOPOD' },
    { value: 1, label: 'Idle farm zone' },
    { value: 2, label: 'Dynamic boosts' },
    { value: 4, label: 'Manual farm zone' }
  ];
  const minimums = [['MinPower', 'Minimum power'], ['MinToughness', 'Minimum toughness'], ['MinRegen', 'Minimum regen']];
  $: rules = Array.isArray(settings._customAdventureRules) ? settings._customAdventureRules.map(object) : [];
  $: reported = typeof telemetry.matchedRuleIndex === 'number' && Number.isInteger(telemetry.matchedRuleIndex) && telemetry.matchedRuleIndex >= -1;

  function save(next: JsonObject[], delay = 0): void {
    onedit('_customAdventureRules', next, delay);
  }
  function edit(index: number, key: string, value: JsonValue, delay = 0): void {
    save(rules.map((rule, row) => row === index ? { ...rule, [key]: value } : rule), delay);
  }
  function numberInput(event: Event): JsonValue {
    const text = (event.currentTarget as HTMLInputElement | HTMLSelectElement).value;
    return text.trim() && Number.isFinite(Number(text)) && getUnsafeNumberReason(text) !== UnsafeNumberReason.underflow ? Number(text) : text;
  }
  function move(from: number, to: number): void {
    const next = [...rules];
    const [rule] = next.splice(from, 1);
    next.splice(to, 0, rule);
    save(next);
  }
</script>

<section class="adventure-rules" aria-label="Global adventure rules">
  <div class="rule-heading"><h3>Global adventure rules</h3><small>{rules.length}/32</small></div>
  <p class="muted">First matching rule wins; otherwise uses Adventure mode.</p>
  {#if settings._manualAdventureEnabled === true}<p class="notice">Manual Adventure overrides these rules.</p>{/if}
  <ol>
    {#each rules as rule, index}
      <li>
        <fieldset>
          <legend>Rule {index + 1}</legend>
          <div class="rule-heading">
            <label class="rule-enabled"><input type="checkbox" checked={rule.Enabled === true} on:change={event => edit(index, 'Enabled', (event.currentTarget as HTMLInputElement).checked)} />Enabled</label>
            <div class="rule-actions">
              <button type="button" aria-label={`Move rule ${index + 1} up`} disabled={index === 0} on:click={() => move(index, index - 1)}>Up</button>
              <button type="button" aria-label={`Move rule ${index + 1} down`} disabled={index === rules.length - 1} on:click={() => move(index, index + 1)}>Down</button>
              <button type="button" aria-label={`Remove rule ${index + 1}`} on:click={() => save(rules.filter((_, row) => row !== index))}>Remove</button>
            </div>
          </div>
          <div class="rule-fields">
            {#each minimums as [key, label]}
              <label>{label}<input type="number" min="0" max={Number.MAX_VALUE} step="any" value={String(rule[key] ?? '')} on:input={event => edit(index, key, numberInput(event), 350)} /></label>
            {/each}
            <label>Target mode<select value={rule.Mode} on:change={event => edit(index, 'Mode', numberInput(event))}>{#each modes as mode}<option value={mode.value}>{mode.label}</option>{/each}</select></label>
            {#if rule.Mode === 1 || rule.Mode === 4}
              <label>Target zone<select value={rule.Zone} on:change={event => edit(index, 'Zone', numberInput(event))}>{#each zones as zone}<option value={zone.value}>{zone.label}</option>{/each}</select></label>
            {/if}
          </div>
        </fieldset>
      </li>
    {:else}<li class="muted">No rules.</li>{/each}
  </ol>
  <button type="button" disabled={rules.length >= 32} on:click={() => save([...rules, { Enabled: true, MinPower: 0, MinToughness: 0, MinRegen: 0, Mode: 0, Zone: 0 }])}>Add rule</button>
  <p class="muted live-match">
    {#if !connected}Connect to see live stats and the matching rule.
    {:else if !reported}Live rule status unavailable.
    {:else}
      Equipped stats: Power {String(telemetry.power ?? 'Unknown')} / Toughness {String(telemetry.toughness ?? 'Unknown')} / Regen {String(telemetry.regen ?? 'Unknown')}.
      {#if pending}Rule changes pending.
      {:else if telemetry.matchedRuleIndex === -1}No matching rule; Adventure mode fallback.
      {:else}First matching rule: {Number(telemetry.matchedRuleIndex) + 1}.{/if}
    {/if}
  </p>
  <details><summary>Rule precedence</summary><p class="muted">Rules apply across plans and rebirths using equipped stats. Idle rules keep current gear; manual farm rules use the Manual Adventure loadout and combat options without enabling Manual Adventure. Locked zones are skipped. Manual Adventure, titans, quests, fight/rebirth preparation, and max ITOPOD push take priority.</p></details>
</section>

<style>
  .adventure-rules { margin-top: 1rem; padding-top: .8rem; border-top: 1px solid var(--line); min-width: 0; }
  .rule-heading, .rule-actions { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: .4rem; }
  .rule-actions { justify-content: flex-start; }
  .rule-actions button { padding: .35rem .45rem; font-size: .75rem; }
  p { margin-top: .5rem; overflow-wrap: anywhere; }
  ol { list-style: none; margin: .6rem 0; padding: 0; display: grid; gap: .6rem; }
  fieldset { min-width: 0; margin: 0; padding: .5rem; border: 1px solid var(--line); border-radius: 4px; }
  legend { padding: 0 .3rem; font-size: .85rem; font-weight: 600; }
  .rule-enabled { display: flex; align-items: center; gap: .4rem; }
  .rule-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 8rem), 1fr)); gap: .5rem; margin-top: .5rem; }
  label { min-width: 0; }
  input[type="number"], select { padding: .4rem; }
  summary { font-size: .78rem; color: var(--muted); }
</style>
