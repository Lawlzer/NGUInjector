<script lang="ts">
  import { onDestroy, tick } from 'svelte';
  import type { JsonObject, JsonValue } from './api';
  import { INT_MAX } from './model';
  import { formatTime, parseDurationText } from './stage-duration';
  export let plan: JsonObject;
  export let namespace = '';
  export let disabled = false;
  export let onchange: (plan: JsonObject) => void;
  export let ondirty: (dirty: boolean) => void = () => {};
  let drafts: Record<string, string> = {};
  let errors: Record<string, string> = {};
  let scope = '';
  let ready: string[] = [];
  const timers = new Map<string, ReturnType<typeof setTimeout>>();
  const conditions = [
    { toggle: 'TimeTriggerEnabled', label: 'After a set time', duration: 'TimeTriggerSeconds', durationLabel: 'Rebirth after', target: '', targetLabel: '', min: 0 },
    { toggle: 'NumberTriggerEnabled', label: 'When NUMBER grows', duration: '', durationLabel: '', target: 'NumberMultiplierTarget', targetLabel: 'NUMBER gain (x)', min: 0 }
  ];
  $: if (scope !== JSON.stringify([namespace, plan.Name])) {
    scope = JSON.stringify([namespace, plan.Name]);
    timers.forEach(clearTimeout);
    timers.clear();
    drafts = {}; errors = {}; ready = [];
  }
  $: for (const condition of conditions) {
    if (condition.duration in drafts && plan[condition.toggle] !== true) cancelDuration(condition.duration);
  }
  $: if (!disabled) for (const key of ready) saveDuration(key);
  $: ondirty(Object.keys(drafts).length > 0);
  onDestroy(() => { timers.forEach(clearTimeout); timers.clear(); });

  function set(key: string, value: JsonValue): void { onchange({ ...plan, [key]: value }); }
  function durationSeconds(text: string): number {
    if (/\.\s*(?:%|s|m|min|h)?$/i.test(text)) throw new Error('Finish the decimal value before saving.');
    const { amount, unit } = parseDurationText(text, 's');
    const rawSeconds = amount * (unit === 'h' ? 3600 : unit === 'min' ? 60 : 1);
    // Decimal unit conversion can land a few ulps away from a whole second.
    const seconds = Math.round(rawSeconds);
    if (unit === '%' || !Number.isInteger(seconds) || Math.abs(seconds - rawSeconds) > Math.max(1, Math.abs(rawSeconds)) * Number.EPSILON * 8 || seconds < 0 || seconds > INT_MAX) throw new Error('Use a whole-second duration, such as 30s, 8m, or 24h.');
    return seconds;
  }
  function changeDuration(key: string, text: string): void {
    if (disabled || !conditions.some(condition => condition.duration === key && plan[condition.toggle] === true)) return;
    clearTimeout(timers.get(key));
    timers.delete(key);
    ready = ready.filter(item => item !== key);
    drafts = { ...drafts, [key]: text };
    errors = { ...errors, [key]: '' };
    try {
      durationSeconds(text);
      const context = scope;
      timers.set(key, setTimeout(() => {
        timers.delete(key);
        if (context === scope && drafts[key] === text) ready = [...ready, key];
      }, 350));
    } catch (error) { errors = { ...errors, [key]: (error as Error).message }; }
  }
  function saveDuration(key: string): void {
    if (!(key in drafts)) return;
    clearTimeout(timers.get(key));
    timers.delete(key);
    if (disabled) { if (!ready.includes(key)) ready = [...ready, key]; return; }
    ready = ready.filter(item => item !== key);
    try {
      const seconds = durationSeconds(drafts[key]);
      set(key, seconds);
      cancelDuration(key);
    } catch (error) { errors = { ...errors, [key]: (error as Error).message }; }
  }
  function cancelDuration(key: string): void {
    clearTimeout(timers.get(key));
    timers.delete(key);
    ready = ready.filter(item => item !== key);
    const { [key]: _draft, ...remaining } = drafts;
    const { [key]: _error, ...otherErrors } = errors;
    drafts = remaining;
    errors = otherErrors;
  }
  async function blurDuration(event: FocusEvent, key: string): Promise<void> {
    const input = event.currentTarget as HTMLInputElement, context = scope;
    await tick();
    if (input.isConnected && document.activeElement !== input && context === scope) saveDuration(key);
  }
  function toggleCondition(key: string, checked: boolean, durationKey: string): void {
    if (!checked && durationKey) cancelDuration(durationKey);
    set(key, checked);
  }
</script>

<article class="card rebirth-conditions" aria-label="Rebirth conditions" inert={disabled}>
  <div class="card-header"><h2>Rebirth conditions</h2>
    <label class="permission"><input type="checkbox" checked={plan.AutoRebirthEnabled === true} on:change={event => set('AutoRebirthEnabled', event.currentTarget.checked)} />Allow auto-rebirth</label>
  </div>
  <label class="match">Rebirth when<select aria-label="Match conditions" value={plan.TriggerMode ?? 0} on:change={event => set('TriggerMode', Number(event.currentTarget.value))}><option value={0}>Any condition</option><option value={1}>All conditions</option></select></label>
  <div class="conditions">
    {#each conditions as condition}
      <div class="condition" class:enabled={plan[condition.toggle] === true}>
        <label class="toggle-row"><input type="checkbox" checked={plan[condition.toggle] === true} on:change={event => toggleCondition(condition.toggle, event.currentTarget.checked, condition.duration)} /><span><strong>{condition.label}</strong></span></label>
        {#if plan[condition.toggle] === true}
          <div class="condition-fields">
            {#if condition.duration}
              <label>{condition.durationLabel}<input aria-label={condition.durationLabel} value={drafts[condition.duration] ?? formatTime(Number(plan[condition.duration] ?? 0))} placeholder="e.g. 8m or 24h" aria-invalid={!!errors[condition.duration]} on:input={event => changeDuration(condition.duration, event.currentTarget.value)} on:blur={event => blurDuration(event, condition.duration)} on:keydown={event => { if (event.key === 'Enter') { event.preventDefault(); saveDuration(condition.duration); } if (event.key === 'Escape') { event.preventDefault(); cancelDuration(condition.duration); } }} /></label>
              {#if errors[condition.duration]}<p class="notice error" role="alert">{errors[condition.duration]}</p>{/if}
            {/if}
            {#if condition.target}
              <label>{condition.targetLabel}<input aria-label={condition.targetLabel} type="number" min={condition.min} step="any" value={String(plan[condition.target] ?? '')} on:input={event => set(condition.target, event.currentTarget.value.trim() ? Number(event.currentTarget.value) : '')} /></label>
            {/if}
          </div>
        {/if}
      </div>
    {/each}
  </div>
  {#if !conditions.some(condition => plan[condition.toggle] === true)}<p class="notice" role="status">Choose at least one condition to rebirth automatically.</p>{/if}
</article>

<style>
  .permission { display: flex; align-items: center; gap: .5rem; font-weight: 400; }
  .match { max-width: 22rem; grid-template-columns: auto minmax(0, 1fr); align-items: center; }
  .conditions { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .75rem; margin-top: 1rem; }
  .condition { min-width: 0; padding: .75rem; border: 1px solid var(--line); border-radius: 4px; }
  .condition.enabled { background: var(--input); }
  .toggle-row { border: 0; padding: 0; }
  .condition-fields { display: grid; gap: .7rem; margin-top: .8rem; }
  @media (max-width: 740px) { .conditions { grid-template-columns: minmax(0, 1fr); } }
</style>
