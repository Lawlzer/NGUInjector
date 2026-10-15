<script lang="ts">
  import { ask } from '@tauri-apps/plugin-dialog';
  import { cloneObject, isObject, parseJson, planRevisionField, type JsonObject } from './api';
  import { equal, pretty } from './model';
  export let title: string;
  export let value: JsonObject;
  export let validate: (value: JsonObject, base: JsonObject) => void;
  export let onapply: (draft: JsonObject, base: JsonObject) => void;
  export let ondirty: (dirty: boolean) => void;
  export let disabled = false;
  export let guidance = '';
  let text = '';
  let base: JsonObject = {};
  let dirty = false;
  let error = '';
  let confirming = false;
  let generation = 0;
  $: if (!dirty) { text = pretty(value); base = cloneObject(value); }
  $: ondirty(dirty);
  $: stale = dirty && !equal(base, value);
  $: readOnlyFields = ['Name', 'RebirthHistory', 'Version', planRevisionField, '_customIronPillHistory'].filter(key => key in value);
  async function apply(): Promise<void> {
    error = '';
    confirming = true;
    const request = generation;
    const original = cloneObject(base);
    try {
      const draft = parseJson(text);
      if (!isObject(draft)) throw new Error('JSON root must be an object.');
      validate(draft, original);
      // Missing keys are not deletions: backend saves are merge operations.
      for (const key of Object.keys(original)) if (!(key in draft)) throw new Error(`Keep the existing ${key} field.`);
      if (!await ask(`Apply and save ${title} edits?`, { title: 'Apply edits', kind: 'warning' })) return;
      if (generation !== request) return;
      onapply(draft, original);
      dirty = false;
    } catch (err) { error = (err as Error).message; }
    finally { confirming = false; }
  }
  export function reset(): void { ++generation; dirty = false; error = ''; }
</script>

<article class="card json-editor">
  <p class="eyebrow">Advanced {title}</p>
  <p class="muted">Keep existing fields. Applied edits save automatically.</p>
  {#if readOnlyFields.length}<p class="muted">Read-only fields: {readOnlyFields.join(', ')}.</p>{/if}
  {#if guidance}<details><summary>Editing details</summary><p class="muted">{guidance}</p></details>{/if}
  {#if stale}<p class="notice">Source changed. Conflicting edits cannot be applied.</p>{/if}
  {#if error}<p class="notice error" role="alert">{error}</p>{/if}
  <textarea aria-label={`Advanced ${title} JSON`} rows="22" spellcheck="false" value={text} disabled={confirming || disabled} on:input={event => { text = (event.currentTarget as HTMLTextAreaElement).value; dirty = true; }}></textarea>
  <div class="button-row"><button type="button" disabled={!dirty || confirming || disabled} on:click={apply}>Apply edits</button><button type="button" class="ghost" disabled={!dirty || confirming || disabled} on:click={reset}>Discard edits</button></div>
</article>
