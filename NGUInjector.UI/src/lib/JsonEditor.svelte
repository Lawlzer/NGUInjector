<script lang="ts">
  import { onDestroy } from 'svelte';
  import { cloneObject, isObject, parseJson, planRevisionField, type JsonObject } from './api';
  import { equal, pretty } from './model';
  export let title: string;
  export let value: JsonObject;
  export let validate: (value: JsonObject, base: JsonObject) => void;
  export let onapply: (draft: JsonObject, base: JsonObject) => void | Promise<void>;
  export let ondirty: (dirty: boolean) => void;
  export let disabled = false;
  export let guidance = '';
  let text = '';
  let base: JsonObject = {};
  let dirty = false;
  let error = '';
  let applying = false;
  let ready = false;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let generation = 0;
  let revision = 0;
  $: if (!dirty) { text = pretty(value); base = cloneObject(value); }
  $: ondirty(dirty);
  $: stale = dirty && !equal(base, value);
  $: readOnlyFields = ['Name', 'RebirthHistory', 'Version', planRevisionField, '_customIronPillHistory'].filter(key => key in value);
  $: if (dirty && ready && !disabled && !applying) void apply();

  onDestroy(() => { clearTimeout(timer); ++generation; });

  function parseDraft(original: JsonObject): JsonObject {
    const draft = parseJson(text);
    if (!isObject(draft)) throw new Error('JSON root must be an object.');
    validate(draft, original);
    // Missing keys are not deletions: backend saves are merge operations.
    for (const key of Object.keys(original)) if (!(key in draft)) throw new Error(`Keep the existing ${key} field.`);
    return draft;
  }

  function change(event: Event): void {
    text = (event.currentTarget as HTMLTextAreaElement).value;
    ++revision;
    dirty = true;
    ready = false;
    clearTimeout(timer);
    error = '';
    try {
      parseDraft(cloneObject(base));
      timer = setTimeout(() => { ready = true; }, 350);
    } catch (err) { error = err instanceof Error ? err.message : String(err); }
  }

  async function apply(): Promise<void> {
    ready = false;
    error = '';
    applying = true;
    const request = generation;
    const edit = revision;
    const original = cloneObject(base);
    try {
      const draft = parseDraft(original);
      await onapply(draft, original);
      if (generation !== request) return;
      // Newer text was based on this submitted draft, not the old source.
      base = cloneObject(draft);
      if (revision === edit) dirty = false;
    } catch (err) {
      if (generation === request && revision === edit) error = err instanceof Error ? err.message : String(err);
    } finally { applying = false; }
  }
  export function reset(): void {
    clearTimeout(timer);
    ++generation;
    ready = false;
    dirty = false;
    error = '';
  }
</script>

<article class="card json-editor">
  <p class="eyebrow">Advanced {title}</p>
  {#if readOnlyFields.length}<p class="muted">Read-only fields: {readOnlyFields.join(', ')}.</p>{/if}
  {#if guidance}<details><summary>Editing details</summary><p class="muted">{guidance}</p></details>{/if}
  {#if stale}<p class="notice">Source changed. Resolve conflicts or discard edits.</p>{/if}
  {#if error}<p class="notice error" role="alert">{error}</p>{/if}
  <textarea aria-label={`Advanced ${title} JSON`} title="Keep all existing fields." rows="22" spellcheck="false" value={text} disabled={disabled && !applying} on:input={change}></textarea>
  <div class="button-row"><button type="button" class="ghost" disabled={!dirty} on:click={reset}>Discard edits</button></div>
</article>
