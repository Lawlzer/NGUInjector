<script lang="ts">
  import type { JsonObject } from './api';
  import { globalPresetRows } from './presets';

  export let library: JsonObject | null = null;
  export let value = '';
  export let label = 'Global preset';
  export let inherit = false;
  export let activePresetId = '';
  export let legacy = false;
  export let disabled = false;
  export let onchange: (id: string) => void;
  $: rows = globalPresetRows(library);
  $: missing = !!value && value !== '@active' && !rows.some(row => row.Id === value);
  $: active = rows.find(row => row.Id === activePresetId);
</script>

<label>{label}
  <select aria-label={label} {value} {disabled} on:change={event => { onchange(event.currentTarget.value); event.currentTarget.value = value; }}>
    <option value="" disabled={!legacy}>{legacy ? 'Existing settings' : 'Choose a preset'}</option>
    {#if inherit}<option value="@active" disabled={!active}>Use plan active preset{active ? `: ${String(active.Name)}` : ' (not selected)'}</option>{/if}
    {#if missing}<option {value} disabled>Missing preset: {value}</option>{/if}
    {#each rows as row}<option value={String(row.Id)}>{String(row.Name)}</option>{/each}
  </select>
  {#if missing}<small role="status">Choose a saved preset to replace the missing one.</small>{/if}
  {#if inherit && value === '@active' && !active}<small role="status">Choose an active preset in Gear settings before saving or running this stage.</small>{/if}
</label>
