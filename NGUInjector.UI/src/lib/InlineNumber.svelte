<script lang="ts">
  import { tick } from 'svelte';
  import type { JsonValue } from './api';
  import { parseNumber } from './model';
  import type { EditSession } from './controller';

  export let label: string;
  export let value: JsonValue | undefined;
  export let display: string | undefined = undefined;
  export let unit = '';
  export let min = 0;
  export let max = Number.MAX_VALUE;
  export let integer = true;
  export let disabled = false;
  export let onbegin: () => EditSession;

  let editing = false;
  let saving = false;
  let draft = '';
  let error = '';
  let input: HTMLInputElement | undefined;
  let button: HTMLButtonElement | undefined;
  let session: EditSession | undefined;

  async function edit(): Promise<void> {
    if (disabled || saving || editing) return;
    session = onbegin();
    draft = String(value ?? '');
    error = '';
    editing = true;
    await tick();
    input?.focus();
    input?.select();
  }

  async function cancel(): Promise<void> {
    if (saving) return;
    session?.cancel();
    session = undefined;
    editing = false;
    error = '';
    await tick();
    button?.focus();
  }

  async function save(): Promise<void> {
    if (disabled || saving) return;
    error = '';
    try {
      const next = parseNumber(draft, label, min, max, integer);
      saving = true;
      const commit = session?.commit;
      if (!commit || !await commit(next)) {
        error = 'Not saved. Your edit is kept.';
        return;
      }
      editing = false;
      session = undefined;
      await tick();
      button?.focus();
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    } finally {
      saving = false;
    }
  }

  function keydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') { event.preventDefault(); void save(); }
    if (event.key === 'Escape') { event.preventDefault(); void cancel(); }
  }
</script>

{#if editing}
  <span class="inline-editor" role="group" aria-label={label}>
    <input bind:this={input} aria-label={label} type="number" {min} {max} step={integer ? 1 : 'any'} value={draft} on:input={event => draft = (event.currentTarget as HTMLInputElement).value} disabled={disabled || saving} on:keydown={keydown} />
    {#if unit}<span class="muted">{unit}</span>{/if}
    <button type="button" disabled={disabled || saving} on:click={save}>{saving ? 'Saving...' : 'Save'}</button>
    <button type="button" class="ghost" disabled={saving} on:click={cancel}>Cancel</button>
    {#if error}<span class="inline-error" role="alert">{error}</span>{/if}
  </span>
{:else}
  <button bind:this={button} type="button" class="inline-value" aria-label={`Edit ${label}`} title="Click to edit" {disabled} on:click={edit}>{display ?? String(value ?? 'Set value')}{display === undefined && unit ? ` ${unit}` : ''}</button>
{/if}
