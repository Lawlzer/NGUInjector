import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { parseJson, stringifyJson, type JsonObject, type NguiState } from '../src/lib/api';
import { createController } from '../src/lib/controller';
import { injectorErrors } from '../src/lib/injector-errors';
import InjectorErrors from '../src/lib/InjectorErrors.svelte';

function state(runtime: JsonObject = {}) {
  const snapshot: NguiState = { runtime_dir: 'C:/Fixture', marker_enabled: true, connected: true, protocol: {}, settings: {}, runtime, profiles: [], plans: [], logs: [], command_results: [] };
  return { ...createController().current(), snapshot };
}

describe('injector error projection', () => {
  it('combines startup, caught errors, resources and domain failures without changing telemetry', () => {
    const value = state({ startupError: 'Settings cannot be loaded.',
      automationErrors: [{ context: 'Adventure', message: 'Route capture failed.\nFull exception', details: 'InvalidOperationException: route stack', firstSeenAt: 'first', lastSeenAt: 'last', count: 7, future: parseJson('9007199254740993') }],
      resourceAllocation: { state: 'NeedsUser', owner: 'stage:24hr', reason: 'Energy allocation changed externally.', resourceOutcome: 'ForeignConflict' },
      execution: { domains: { Route: { state: 'Failed', waitingReason: 'Adventure controller unavailable.' }, Magic: { state: 'NeedsUser', reason: 'Magic needs review.' } } }
    });
    const before = stringifyJson(value);
    const errors = injectorErrors(value);
    expect(errors.map(error => [error.context, error.message])).toEqual([
      ['Injector startup', 'Settings cannot be loaded.'], ['Adventure', 'Route capture failed.'],
      ['Resource allocation / stage:24hr', 'Energy allocation changed externally.'],
      ['Route', 'Adventure controller unavailable.'], ['Magic', 'Magic needs review.']
    ]);
    expect(errors[1].count).toBe(7);
    expect(errors[1].details).toContain('InvalidOperationException: route stack');
    expect(errors[1].details).toContain('9007199254740993');
    expect(stringifyJson(value)).toBe(before);
  });

  it('does not alarm on ordinary waits, cooldowns, observations or historical blocked metadata', () => {
    const value = state({ status: 'running', customStatus: 'Waiting for spawn.', focusStatus: 'waiting',
      resourceAllocation: { state: 'Waiting', reason: 'Waiting for combat preparation.', blockedDomain: 'gear' },
      execution: { waitingReason: 'Waiting for spawn.', domains: { Route: { state: 'Observed', controlled: true, waitingReason: 'Waiting for spawn.' }, Energy: { state: 'Waiting', waitingReason: 'Waiting for a lock.' } }, support: { verified: false }, resources: [{ verified: false }] },
      ironPill: { status: 'blocked', reason: 'Custom auto-rebirth is disabled.' },
      manualTitan: { status: 'Waiting for next spawn' }, combatPrep: { status: 'running', reason: 'Waiting for a lock.' },
      planProgress: { status: 'unavailable', available: true, reason: 'Waiting for the allocator to initialize the scheduler.' }
    });
    expect(injectorErrors(value)).toEqual([]);
  });

  it('surfaces explicitly blocked allocation even when its executor is waiting, and observation failures', () => {
    const value = state({ resourceAllocation: { state: 'Waiting', blocked: true, owner: 'stage:24hr', reason: 'Preset allocation blocked: route changed.' },
      execution: { status: 'error', available: false, waitingReason: 'Native resource observations failed.' } });
    expect(injectorErrors(value).map(error => error.message)).toEqual(['Preset allocation blocked: route changed.', 'Native resource observations failed.']);
    value.snapshot.runtime.resourceAllocation = { state: 'Applied', blocked: false };
    value.snapshot.runtime.execution = {};
    expect(injectorErrors(value)).toEqual([]);
  });

  it('uses the running plan and only its active failed stage, not historical or pending stages', () => {
    const value = state({ status: 'running', settings: { useCustomRebirthPlan: true, customPlanFile: '24hr' },
      planProgress: { status: 'running', planName: '24hr', activeStageIndex: 1, stages: [
        { name: 'Old stage', presetStatus: 'failed', presetReason: 'Old failure' },
        { name: 'NGU sustain', presetStatus: 'blocked', presetReason: 'Saved preset is missing.' },
        { name: 'Future stage', presetStatus: 'failed', presetReason: 'Future failure' }
      ] }
    });
    value.planName = 'default';
    expect(injectorErrors(value).map(error => [error.context, error.message])).toEqual([['Plan / 24hr / NGU sustain', 'Saved preset is missing.']]);
  });

  it('surfaces recovery, support, failed Quick and spell errors but not ordinary Iron Pill eligibility', () => {
    const value = state({ focusStatus: 'failed', activeFocusGoal: 'EnergyNgu', customStatus: 'Allocation rejected.',
      execution: { support: { desiredFeasible: false, reason: 'Insufficient gold for selected support.' } },
      executionReview: { reviewRequired: true, reason: 'Selected gear is missing.' },
      combatPrep: { status: 'failed', cleanupRecoveryRequired: true, cleanupRecoveryReason: 'Checkpoint access denied.' },
      bloodGoals: { status: 'error', reason: 'Ritual estimate failed.' }, ironPill: { status: 'blocked', reason: 'Cooldown.' }, nguAllocation: { status: 'error', available: false, reason: 'NGU observation failed.' }
    });
    expect(injectorErrors(value).map(error => error.message)).toEqual(['Insufficient gold for selected support.', 'Selected gear is missing.', 'Checkpoint access denied.', 'Allocation rejected.', 'Ritual estimate failed.', 'NGU observation failed.']);
  });

  it('shows command, save and poll failures together without duplicating the controller error', () => {
    const value = state();
    value.commandErrors = [{ command: 'quickSave', success: false, message: 'Save command rejected.' }];
    value.snapshot.command_results = [{ id: 'one', command: 'quickSave', success: false, message: 'Save command rejected.', completed_at: 'yesterday' }, { id: 'two', command: 'status', success: true, message: 'OK', completed_at: 'today' }];
    value.saveError = 'Settings revision changed.';
    value.pollError = 'Runtime read denied.';
    value.error = value.pollError;
    expect(injectorErrors(value).map(error => error.message)).toEqual(['Save command rejected.', 'Settings revision changed.', 'Runtime read denied.']);
  });

  it('identifies persisted command failures as earlier receipts even after a later successful read', () => {
    const value = state({ status: 'running', startedAt: '2026-10-24T11:26:10Z', execution: { lifecycle: 'Running' }, automationErrors: [] });
    value.snapshot.command_results = [
      { id: 'old-read', command: 'readPlan', success: false, message: 'Execution is quiesced; ordinary commands are not admitted.', completed_at: '2026-10-24T11:25:00Z' },
      { id: 'new-read', command: 'readPlan', success: true, message: 'Plan read and normalized.', completed_at: '2026-10-24T12:50:00Z' }
    ];
    const before = stringifyJson(value);
    const errors = injectorErrors(value);
    expect(errors.map(error => [error.context, error.message])).toEqual([['Earlier command failed: readPlan', 'Execution is quiesced; ordinary commands are not admitted.']]);
    expect(errors[0].details).toContain('old-read');
    expect(errors[0].details).toContain('2026-10-24T11:25:00Z');
    expect(stringifyJson(value)).toBe(before);
  });

  it('does not treat a quiescence message as grounds to hide a reported execution fault', () => {
    const value = state({ execution: { domains: { Route: { state: 'Failed', reason: 'Execution is quiesced; ordinary commands are not admitted.' } } } });
    expect(injectorErrors(value).map(error => [error.context, error.message])).toEqual([['Route', 'Execution is quiesced; ordinary commands are not admitted.']]);
  });

  it('accepts missing and unknown older telemetry without inventing errors or defaults', () => {
    const value = state({ future: { schema: 99 }, execution: { domains: { FutureDomain: { state: 'FutureState' } } }, automationErrors: [null, 42, {}, { context: 'empty', message: '' }] });
    expect(injectorErrors(value)).toEqual([]);
    expect(injectorErrors({ ...value, snapshot: null })).toEqual([]);
  });
});

describe('injector error panel', () => {
  const callbacks = { onlogs: () => {}, onreview: () => {} };
  it('renders concise escaped errors with repeat counts and closed diagnostic disclosures', () => {
    const value = state({ automationErrors: [{ context: 'Adventure', message: '<script>unsafe</script>\nstack', details: 'DIAGNOSTIC_STACK', count: 3 }] });
    const body = render(InjectorErrors, { props: { state: value, connected: true, ...callbacks } }).body;
    expect(body).toContain('role="alert"');
    expect(body).toContain('Injector errors');
    expect(body).toContain('&lt;script>unsafe&lt;/script>');
    expect(body).not.toContain('<script>unsafe</script>');
    expect(body).toContain('(3 times)');
    expect(body).toContain('aria-label="Details: Adventure"');
    expect(body).toContain('DIAGNOSTIC_STACK');
    expect(body).not.toMatch(/<details[^>]*\bopen/);
    expect(body).not.toMatch(/Dismiss|Clear errors|Retry action/);
  });

  it('keeps faults visible when disconnected and never renders stale data as success', () => {
    const value = state({ status: 'running', resourceAllocation: { state: 'Failed', reason: 'Allocation failed.' } });
    const body = render(InjectorErrors, { props: { state: value, connected: false, ...callbacks } }).body;
    expect(body).toContain('Allocation failed.');
    expect(body).toContain('Disconnected. Current automation status is unknown.');
    const empty = render(InjectorErrors, { props: { state: state({ status: 'running' }), connected: false, ...callbacks } }).body;
    expect(empty).toContain('Injector status unavailable');
    expect(empty).not.toMatch(/No errors|Healthy|Success/);
  });

  it('renders no panel for healthy or intentionally offline sessions', () => {
    for (const connected of [true, false]) {
      const body = render(InjectorErrors, { props: { state: state({ status: connected ? 'running' : 'unloaded' }), connected, ...callbacks } }).body;
      expect(body).not.toContain('<aside');
    }
  });
});
