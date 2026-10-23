<script lang="ts">
  import { onDestroy, tick } from 'svelte';
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
  export let ondirty: (dirty: boolean) => void = () => {};

  let editing = false;
  let saving = false;
  let draft = '';
  let error = '';
  let input: HTMLInputElement | undefined;
  let button: HTMLButtonElement | undefined;
  let session: EditSession | undefined;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let ready = false;
  let revision = 0;
  let dirty = false;

  $: if (editing && ready && !disabled && !saving) void save();

  onDestroy(() => {
    clearTimeout(timer);
    editing = false;
    session?.cancel();
    session = undefined;
    setDirty(false);
  });

  function setDirty(next: boolean): void {
    dirty = next;
    // Publish synchronously: navigation and beforeunload may run before a reactive flush.
    ondirty(next);
  }

  async function edit(): Promise<void> {
    if (disabled || saving || editing) return;
    session = onbegin();
    draft = String(value ?? '');
    error = '';
    editing = true;
    setDirty(false);
    await tick();
    input?.focus();
    input?.select();
  }

  async function cancel(): Promise<void> {
    clearTimeout(timer);
    ready = false;
    session?.cancel();
    session = undefined;
    editing = false;
    error = '';
    setDirty(false);
    await tick();
    button?.focus();
  }

  function change(event: Event): void {
    draft = (event.currentTarget as HTMLInputElement).value;
    ++revision;
    setDirty(true);
    clearTimeout(timer);
    ready = false;
    error = '';
    try {
      parseNumber(draft, label, min, max, integer);
      timer = setTimeout(() => { ready = true; }, 350);
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
    }
  }

  async function save(): Promise<void> {
    if (disabled || saving || !editing || !session) return;
    ready = false;
    error = '';
    const request = revision;
    const active = session;
    try {
      const next = parseNumber(draft, label, min, max, integer);
      saving = true;
      const success = await active.commit(next);
      if (!editing || session !== active) return;
      if (success) {
        // Renew now: keep this editor's identity and use the saved value as the rollback baseline.
        session = onbegin();
        setDirty(revision !== request);
      } else if (revision === request) {
        error = 'Not saved. Your edit is kept. Edit the value to retry.';
      }
    } catch (err) {
      if (editing && session === active && revision === request) error = err instanceof Error ? err.message : String(err);
    } finally {
      saving = false;
    }
  }

  function keydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      if (dirty) { clearTimeout(timer); ready = true; }
    }
    if (event.key === 'Escape') { event.preventDefault(); void cancel(); }
  }
</script>

{#if editing}
  <span class="inline-editor" role="group" aria-label={label} aria-busy={saving}>
    <input bind:this={input} aria-label={label} type="number" {min} {max} step={integer ? 1 : 'any'} value={draft} on:input={change} disabled={disabled && !saving} on:keydown={keydown} />
    {#if unit}<span class="muted">{unit}</span>{/if}
    <button type="button" class="ghost" title="Discard unsaved edits; saved changes are kept." on:click={cancel}>Cancel</button>
    {#if error}<span class="inline-error" role="alert">{error}</span>{/if}
  </span>
{:else}
  <button bind:this={button} type="button" class="inline-value" aria-label={`Edit ${label}`} title="Click to edit" {disabled} on:click={edit}>{display ?? String(value ?? 'Set value')}{display === undefined && unit ? ` ${unit}` : ''}</button>
{/if}
