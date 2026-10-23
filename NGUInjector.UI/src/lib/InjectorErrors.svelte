<script lang="ts">
  import type { UiState } from './controller';
  import { injectorErrors } from './injector-errors';
  import { object } from './model';

  export let state: UiState;
  export let connected = false;
  export let onlogs: () => void;
  export let onreview: () => void;

  $: errors = injectorErrors(state);
  $: unavailable = !!state.snapshot && !connected && state.snapshot.runtime.status === 'running';
</script>

{#if errors.length || unavailable}
  <aside class="injector-errors" role="alert" aria-labelledby="injector-errors-title" aria-atomic="false">
    <h2 id="injector-errors-title">{errors.length ? 'Injector errors' : 'Injector status unavailable'}</h2>
    {#if !connected && state.snapshot}<p class="connection-warning">Disconnected. Current automation status is unknown.</p>{/if}
    {#if errors.length}
      <ul>
        {#each errors as error (error.id)}
          <li>
            <p><strong>{error.context}:</strong> {error.message}{#if error.count} <strong class="repeat">({error.count} times)</strong>{/if}</p>
            <details><summary aria-label={`Details: ${error.context}`}>Details</summary><pre>{error.details}</pre></details>
          </li>
        {/each}
      </ul>
    {/if}
    <div class="error-actions">
      {#if object(state.snapshot?.runtime.executionReview).reviewRequired === true}<button type="button" on:click={onreview}>Review selection</button>{/if}
      {#if errors.length}<button type="button" class="ghost" on:click={onlogs}>Logs</button>{/if}
    </div>
  </aside>
{/if}

<style>
  .injector-errors { min-width: 0; margin: 1rem 0; padding: 1rem; border: 2px solid #e78694; border-left-width: 6px; border-radius: 5px; background: #351e26; color: #ffe1e5; overflow-wrap: anywhere; }
  h2 { font-size: 1.35rem; font-weight: 700; }
  .connection-warning { margin-top: .4rem; font-weight: 600; }
  ul { padding-left: 1.2rem; margin: .6rem 0 0; }
  li { margin-top: .6rem; }
  li p { line-height: 1.45; }
  .repeat { white-space: nowrap; }
  details { margin-top: .2rem; }
  summary { width: fit-content; }
  pre { max-height: 18rem; font-weight: 400; }
  .error-actions { display: flex; flex-wrap: wrap; gap: .5rem; margin-top: .75rem; }
  .error-actions:empty { display: none; }
</style>
