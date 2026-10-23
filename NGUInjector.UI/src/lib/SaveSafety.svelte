<script lang="ts">
  import { isObject, type JsonObject } from './api';

  export let data: unknown = undefined;
  export let connected = false;

  function seconds(value: unknown): number | null {
    if (typeof value !== 'string' || !/^\d+(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(value)) return null;
    const number = Number(value);
    return Number.isFinite(number) && number >= 0 ? number : null;
  }

  function hours(value: number | null): string {
    return value === null ? 'Unavailable' : `${(value / 3600).toLocaleString('en-US', { maximumFractionDigits: 2 })} h`;
  }

  function validIdentity(identity: JsonObject): boolean {
    const total = seconds(identity.totalPlaytimeSeconds);
    const elapsed = seconds(identity.rebirthSeconds);
    return total !== null && elapsed !== null && elapsed <= total
      && typeof identity.rebirth === 'string' && /^\d+$/.test(identity.rebirth)
      && BigInt(identity.rebirth) <= 9223372036854775807n;
  }

  $: safety = isObject(data) ? data : {};
  $: current = isObject(safety.current) ? safety.current : {};
  $: longest = isObject(safety.longest) ? safety.longest : {};
  $: currentSeconds = seconds(current.totalPlaytimeSeconds);
  $: longestSeconds = seconds(longest.totalPlaytimeSeconds);
  $: metadataValid = validIdentity(current) && validIdentity(longest);
  $: metadataBehind = metadataValid && currentSeconds !== null && longestSeconds !== null && currentSeconds < longestSeconds;
  $: scanCountsValid = (safety.pendingFiles === undefined || safety.pendingFiles === 0)
    && (safety.knownFiles === undefined || typeof safety.knownFiles === 'number' && Number.isSafeInteger(safety.knownFiles) && safety.knownFiles > 0);
  $: path = typeof longest.path === 'string' && longest.path.trim() ? longest.path : '';
  $: errors = Array.isArray(safety.errors) ? safety.errors.map(error => {
    if (typeof error === 'string' && error.trim()) return error;
    if (isObject(error) && typeof error.message === 'string' && error.message.trim()) {
      return typeof error.path === 'string' && error.path ? `${error.path}: ${error.message}` : error.message;
    }
    return 'Save scan error.';
  }) : [];
  $: recovery = safety.status === 'recoveryRequired';
  $: behind = !recovery && (safety.status === 'behind' || metadataBehind);
  $: testing = safety.status === 'testing' || safety.testActive === true;
  // Only a fresh, complete report can clear the warning. Rendering never changes save state.
  $: verified = connected && safety.version === 1 && safety.status === 'verified'
    && safety.testActive === false && safety.scanComplete === true && Array.isArray(safety.errors) && errors.length === 0
    && metadataValid && !metadataBehind && scanCountsValid && safety.ambiguousLongest !== true && !!path
    && typeof longest.sha256 === 'string' && /^[a-f\d]{64}$/i.test(longest.sha256);
  $: title = recovery ? 'Save recovery required' : behind ? 'Loaded save is behind'
    : testing ? 'Save testing active' : data == null ? 'Save safety unavailable' : 'Save safety unknown';
  $: issue = safety.status === 'verified' && !metadataValid ? 'Save metadata is invalid or inconsistent.'
    : safety.ambiguousLongest === true ? 'Different saves tie for longest playtime. Review the saved branches.'
      : !scanCountsValid ? 'Save scan counts are inconsistent or incomplete.'
        : metadataBehind && safety.status === 'verified' ? 'Loaded playtime is lower than the longest decoded save.' : '';
  $: backendMessage = safety.version === 1 && typeof safety.message === 'string'
    && safety.message.trim() && safety.status !== 'verified' ? safety.message : '';
  $: message = recovery && backendMessage ? backendMessage : issue || backendMessage;
</script>

{#if !verified && (connected || data !== undefined)}
  <aside class="save-safety" class:critical={behind || recovery} role={data == null ? 'status' : 'alert'} aria-label="Save safety">
    <h2>{title}</h2>
    {#if !connected}<p>Disconnected: showing last reported save safety; current safety is unknown.</p>{/if}
    {#if message}<p>{message}</p>{/if}
    {#if isObject(data)}
      <p class="comparison">Loaded save: <strong title={currentSeconds === null ? undefined : `${current.totalPlaytimeSeconds} seconds`}>{hours(currentSeconds)}</strong> / Longest known: <strong title={longestSeconds === null ? undefined : `${longest.totalPlaytimeSeconds} seconds`}>{hours(longestSeconds)}</strong></p>
      <p>Longest decoded save: <strong>{path || 'Unavailable'}</strong></p>
      {#if testing && (behind || recovery)}<p>Save testing is active.</p>{/if}
      {#if safety.scanComplete !== true || typeof safety.pendingFiles === 'number' && safety.pendingFiles > 0}<p>Save scan is incomplete.</p>{/if}
      {#if errors.length}<ul aria-label="Save scan errors">{#each errors as error}<li>{error}</li>{/each}</ul>{/if}
    {/if}
    <p class="action">{recovery ? 'Preserve your saves and resolve recovery before continuing.'
      : behind ? 'Do not overwrite your saves. Check the longest known save before continuing.'
        : testing ? 'Keep test saves separate from your normal saves.'
          : data == null ? 'Update and reconnect the injector to check the loaded save.'
            : 'Check your saves and resolve any scan errors before continuing.'}</p>
  </aside>
{/if}

<style>
  .save-safety { min-width: 0; margin: 1rem 0; padding: 1rem 1.25rem; border: 2px solid #b18b45; border-left-width: 8px; border-radius: 5px; background: #302719; color: #ffe3ac; overflow-wrap: anywhere; }
  .save-safety h2 { font-size: clamp(1.35rem, 3vw, 1.85rem); font-weight: 750; line-height: 1.25; }
  .save-safety p { margin-top: .5rem; }
  .save-safety .comparison { font-size: 1.1rem; }
  .save-safety .action { font-weight: 650; }
  .save-safety ul { margin: .5rem 0 0; padding-left: 1.25rem; }
  .save-safety.critical { padding: 1.25rem; border-color: #ff697a; background: #471c25; color: #ffe1e5; }
  .save-safety.critical h2 { font-size: clamp(1.65rem, 4vw, 2.25rem); }
  @media (max-width: 600px) {
    .save-safety, .save-safety.critical { padding: 1rem; }
  }
</style>
