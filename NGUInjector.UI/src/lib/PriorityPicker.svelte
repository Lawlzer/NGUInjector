<script lang="ts">
  import { tick } from 'svelte';
  import type { JsonValue } from './api';
  import { priorityLabel, priorityOption, type PriorityOption } from './priority-options';

  export let label: string;
  export let value: JsonValue | undefined = [];
  export let options: readonly PriorityOption[];
  export let defaultLabel = 'Auto';
  export let helper = 'First available priority is used first.';
  export let disabled = false;
  export let onchange: (value: JsonValue[]) => void;

  let picker: HTMLFieldSetElement;
  let announcement = '';
  $: rows = Array.isArray(value) ? value : value === undefined ? [] : [value];
  $: available = options.filter(option => !rows.some(row => priorityOption(options, row) === option));
  $: groups = [...new Set(available.map(option => option.group))];

  function add(event: Event): void {
    const select = event.currentTarget as HTMLSelectElement;
    if (!select.value) return;
    const option = available[Number(select.value)];
    select.value = '';
    if (disabled || !option) return;
    onchange([...rows, option.value]);
    announcement = `${option.label} added at priority ${rows.length + 1}.`;
  }
  async function move(index: number, direction: number): Promise<void> {
    const to = index + direction;
    if (disabled || to < 0 || to >= rows.length) return;
    const next = [...rows];
    [next[index], next[to]] = [next[to], next[index]];
    onchange(next);
    announcement = `${priorityLabel(options, next[to])} moved to priority ${to + 1}.`;
    await tick();
    const row = picker.querySelector(`[data-priority-index="${to}"]`);
    (row?.querySelector<HTMLButtonElement>(`[data-move="${direction}"]:not(:disabled)`) ?? row?.querySelector<HTMLButtonElement>('[data-remove]'))?.focus();
  }
  async function remove(index: number): Promise<void> {
    if (disabled) return;
    onchange(rows.filter((_, i) => i !== index));
    announcement = `${priorityLabel(options, rows[index])} removed.`;
    await tick();
    (picker.querySelector<HTMLButtonElement>(`[data-priority-index="${Math.min(index, rows.length - 1)}"] [data-remove]`) ?? picker.querySelector<HTMLSelectElement>('select'))?.focus();
  }
  async function reset(): Promise<void> {
    if (disabled) return;
    onchange([]);
    announcement = `${label}: ${defaultLabel}.`;
    await tick();
    picker.querySelector<HTMLSelectElement>('select')?.focus();
  }
</script>

<fieldset class="priority-picker" aria-label={label} {disabled} bind:this={picker}>
  <legend>{label}</legend>
  <p class="helper">{helper}</p>
  {#if rows.length}
    <ol aria-label={`${label} order`}>
      {#each rows as row, index}
        <li data-priority-index={index} class:unknown={!priorityOption(options, row)}>
          <span class="priority-name">{priorityLabel(options, row)}</span>
          <div class="priority-actions">
            <button type="button" class="ghost" data-move={-1} disabled={disabled || index === 0} aria-label={`Move ${priorityLabel(options, row)} up`} on:click={() => move(index, -1)}>Up</button>
            <button type="button" class="ghost" data-move={1} disabled={disabled || index === rows.length - 1} aria-label={`Move ${priorityLabel(options, row)} down`} on:click={() => move(index, 1)}>Down</button>
            <button type="button" class="ghost" data-remove aria-label={`Remove ${priorityLabel(options, row)}`} on:click={() => remove(index)}>Remove</button>
          </div>
        </li>
      {/each}
    </ol>
    <button type="button" class="ghost reset" on:click={reset}>{defaultLabel}</button>
  {:else}
    <p class="default">{defaultLabel}</p>
  {/if}
  <label class="add-priority">Add priority
    <select aria-label={`Add ${label.toLowerCase()}`} value="" disabled={disabled || !available.length} on:change={add}>
      <option value="" disabled>{available.length ? 'Choose a priority...' : 'All priorities selected'}</option>
      {#each groups as group}
        <optgroup label={group}>
          {#each available as option, index}
            {#if option.group === group}<option value={index}>{option.label}</option>{/if}
          {/each}
        </optgroup>
      {/each}
    </select>
  </label>
  <span class="sr-only" aria-live="polite">{announcement}</span>
</fieldset>

<style>
  .priority-picker { min-width: 0; margin: .8rem 0 0; padding: .7rem; border: 1px solid var(--line); border-radius: 4px; }
  legend { padding: 0 .3rem; font-size: .8rem; color: var(--muted); }
  .helper, .default { margin: 0 0 .6rem; font-size: .76rem; color: var(--muted); line-height: 1.5; }
  .default { color: var(--accent); }
  ol { margin: .6rem 0; padding-left: 1.6rem; }
  li { padding: .4rem 0; border-bottom: 1px solid var(--line); font-size: .8rem; }
  li::marker { color: var(--muted); font-variant-numeric: tabular-nums; }
  .priority-name { overflow-wrap: anywhere; }
  .unknown .priority-name { color: #edc78c; }
  .priority-actions { display: flex; flex-wrap: wrap; gap: .3rem; margin-top: .4rem; }
  .priority-actions button, .reset { padding: .4rem .5rem; font-size: .72rem; min-height: 32px; }
  .reset { margin-bottom: .65rem; }
  .add-priority { display: block; font-size: .76rem; }
  select { width: 100%; min-width: 0; max-width: 100%; margin-top: .3rem; }
  .sr-only { position: absolute; width: 1px; height: 1px; padding: 0; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
</style>
