import { expect, test, type Page } from '@playwright/test';

type FixtureWindow = typeof window & {
  __combatCalls: { command: string; args: Record<string, unknown> }[];
  __combatNativeCalls: string[];
  __combatRuntime: Record<string, unknown>;
  __combatFailure: string;
  __combatStalePreview: boolean;
  __combatLegacySupport: boolean;
  __combatProtocol: Record<string, unknown>;
  __combatEnvelopePadding: number;
};

async function openPlanner(page: Page, maxPreviewBytes = 256 * 1024, envelopePadding = 0) {
  // The real App/controller are exercised; only the native IPC boundary is replaced.
  await page.addInitScript(({ maxPreviewBytes, envelopePadding }) => {
    const fixture = window as FixtureWindow;
    fixture.__combatCalls = [];
    fixture.__combatNativeCalls = [];
    fixture.__combatFailure = '';
    fixture.__combatStalePreview = false;
    fixture.__combatLegacySupport = false;
    fixture.__combatProtocol = { maxResultBytes: 256 * 1024, combatPrep: { maxPreviewBytes } };
    fixture.__combatEnvelopePadding = envelopePadding;
    const plannedSupport = {
      beards: [{ id: 4, name: 'Wandoos beard' }],
      diggers: [{ id: 7, name: 'LongWandoosDiggerName'.repeat(12), level: '9007199254740993', drain: 1.25e100 }],
      grossGoldPerSecond: 2.5e100, drainPerSecond: 1.25e100, netGoldPerSecond: 1.25e100,
      budgetFraction: .75, policy: 'phase', wandoosBeardMultiplier: 2, wandoosDiggerMultiplier: 3
    };
    const actualSupport = { ...plannedSupport, budgetFraction: 1, policy: 'current', beards: [{ id: 0, name: 'Observed beard' }],
      diggers: [{ id: 1, name: 'Observed digger', level: '00042', drain: 0 }], netGoldPerSecond: -1e100 };
    fixture.__combatRuntime = {
      externalUiEnabled: true, status: 'running', protocolVersion: 3, settingsCasVersion: 1, startedAt: 'session-one',
      supportedCommands: ['previewCombatPrep', 'startCombatPrep', 'cancelCombatPrep'],
      combatPrep: { schemaVersion: 1, active: false, status: 'idle', phase: 'Wandoos', plannedSupport, actualSupport,
        supportStatus: 'applied', blockedDomain: 'support', blockedReason: 'Digger selection changed by user' }
    };
    let result: Record<string, unknown> | null = null;
    const baseline = { augmentMultiplier: 2, wandoosMultiplier: 3, combinedMultiplier: 6, bossAttack: 1200, bossDefense: 2400 };
    const candidate = (id: string, background: boolean, duration: number) => ({
      id, label: background ? 'Background casual' : 'Full focus', os: 2, available: true, reason: '',
      augmentSeconds: background ? 0 : duration / 4, atSeconds: background ? 0 : duration / 12,
      wandoosSeconds: background ? 0 : duration * 2 / 3, backgroundSeconds: background ? duration : 0,
      atEnergyShare: .5, augmentIndex: 3, gainFactor: background ? 1.1 : 2,
       energyFocusEquivalentSeconds: background ? 720 : null, magicFocusEquivalentSeconds: background ? 360 : null,
       resourceReferenceProfileKey: 'frozen-resource-profile', energyResourceFocusEquivalentSeconds: background ? 0 : 3600, magicResourceFocusEquivalentSeconds: background ? null : 1800,
       energyResourceFocusCostKnown: true, magicResourceFocusCostKnown: !background,
      projected: { augmentMultiplier: 4, wandoosMultiplier: 5, combinedMultiplier: 20, bossAttack: 4000, bossDefense: 8000,
        energyAtLevel: '9223372036854775807', magicAtLevel: '9007199254740993', wandoosEnergyLevel: '1234567890123456789', wandoosMagicLevel: '456', goldRemaining: 100000 },
      phases: [{ kind: 'at', label: 'Verify AT', seconds: duration / 12, gearGoal: 'AdvancedTraining', gearIds: [10, 20],
        gearNames: ['Owned hat', 'LongOwnedRingName'.repeat(12)], energyAllocation: 'Energy split between AT targets', magicAllocation: 'No Wandoos magic until AT targets verified', atEnergyTarget: '9223372036854775807', atMagicTarget: '9007199254740993',
        ...(fixture.__combatLegacySupport ? {} : { support: { ...actualSupport, policy: 'current' } }) },
        { kind: 'wandoos', label: 'Train Wandoos', seconds: background ? 0 : duration * 2 / 3,
          ...(fixture.__combatLegacySupport ? {} : { support: plannedSupport, supportVariantKey: 'wandoos-dedicated' }) }]
    });
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      fixture.__combatNativeCalls.push(command);
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      if (command === 'get_ngui_state') return {
        runtime_dir: 'C:\\Fixture\\NGUInjector', marker_enabled: true, connected: true, ...await cas.settingsSnapshot({ Future: 42 }), protocol: structuredClone(fixture.__combatProtocol),
        runtime: { updatedAt: new Date().toISOString(), ...structuredClone(fixture.__combatRuntime) }, profiles: [], plans: [], logs: [], command_results: []
      };
      if (command === 'enqueue_command') {
        const envelope = args.envelope as { command: string; args: Record<string, unknown> };
        fixture.__combatCalls.push(structuredClone(envelope));
        if (fixture.__combatFailure) throw new Error(fixture.__combatFailure);
        const id = `cmd-${fixture.__combatCalls.length}`;
        let data = {};
        if (envelope.command === 'previewCombatPrep') {
          const duration = envelope.args.durationSeconds as number;
          data = { combatPrepPreview: { schemaVersion: 1, previewId: 'preview-token', generatedAt: new Date(Date.now() - (fixture.__combatStalePreview ? 121000 : 0)).toISOString(),
            ...envelope.args, recommendedCandidateId: 'full', evaluationCount: 30, elapsedMilliseconds: 15,
            assumptions: ['Owned loadouts frozen; future growth held fixed'], baseline,
            candidates: [candidate('full', false, duration), candidate('casual', true, duration)] } };
        } else if (envelope.command === 'startCombatPrep') {
          fixture.__combatRuntime.combatPrep = { schemaVersion: 1, active: true, status: 'running', runId: 'runtime-run-not-candidate', candidateLabel: 'Background casual', phase: 'Background', reason: 'Training',
            durationSeconds: 7200, elapsedSeconds: 45, remainingSeconds: 7155, phaseElapsedSeconds: 45, phaseRemainingSeconds: 7155,
            atTargetsReached: false, actual: baseline, projected: { ...baseline, bossAttack: 4000 }, warnings: ['Frozen preview assumptions'] };
        } else if (envelope.command === 'cancelCombatPrep') {
          fixture.__combatRuntime.combatPrep = { schemaVersion: 1, active: false, status: 'cancelled', runId: 'runtime-run-not-candidate', candidateLabel: 'Background casual', phase: 'Background', reason: 'Explicit cancellation' };
        } else throw new Error(`Unexpected action: ${envelope.command}`);
        result = { id, command: envelope.command, success: true, data,
          ...(envelope.command === 'previewCombatPrep' ? { padding: 'x'.repeat(fixture.__combatEnvelopePadding) } : {}) };
        return id;
      }
      if (command === 'read_command_result') return result;
      if (command === 'tail_log') return '';
      throw new Error(`Unexpected native invocation: ${command}`);
    } } });
    localStorage.clear();
  }, { maxPreviewBytes, envelopePadding });
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  const planner = page.getByRole('article', { name: 'Combat prep planner', exact: true });
  await expect(planner.getByRole('button', { name: 'Preview', exact: true })).toBeEnabled();
  return planner;
}

for (const scenario of [
  { label: 'legacy native 64 KiB', limit: 64 * 1024, padding: 96 * 1024, error: 'exceeds 64 KB' },
  { label: 'negotiated native 256 KiB', limit: 256 * 1024, padding: 96 * 1024, error: '' },
  { label: 'complete response above 256 KiB', limit: 256 * 1024, padding: 256 * 1024, error: 'exceeds 256 KB' },
  { label: 'invalid fractional metadata', limit: 256 * 1024 + .5, padding: 0, error: 'limit metadata is invalid' }
]) {
  test(`preview respects ${scenario.label} without automatic actions`, async ({ page }) => {
    const planner = await openPlanner(page, scenario.limit, scenario.padding);
    await planner.getByRole('button', { name: 'Preview', exact: true }).click();
    if (scenario.error) {
      await expect(page.getByRole('alert')).toContainText(scenario.error);
      await expect(planner.getByRole('radio')).toHaveCount(0);
      await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
    } else {
      await expect(planner.getByRole('radio')).toHaveCount(2);
      await expect(planner.getByRole('button', { name: 'Start selected' })).toBeEnabled();
    }
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([{ command: 'previewCombatPrep', args: { durationSeconds: 7200, backgroundEnergyPercent: 3, backgroundMagicPercent: 3 } }]);
    expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
  });
}

test('planner preview is read-only and retains selection and drafts across tabs', async ({ page }) => {
  const planner = await openPlanner(page);
  await expect(planner.getByRole('spinbutton', { name: 'Total budget (seconds)' })).toHaveValue('7200');
  await expect(planner.getByRole('spinbutton', { name: 'Background energy (%)' })).toHaveValue('3');
  await expect(planner.getByRole('spinbutton', { name: 'Background magic (%)' })).toHaveValue('3');
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([]);
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  await expect(planner.getByRole('radio', { name: /Full focus/ })).toBeChecked();
  await planner.getByRole('radio', { name: 'Background casual', exact: true }).check();
  await expect(planner.locator('.candidate > details')).toHaveCount(2);
  await expect(planner.locator('.candidate > details[open]')).toHaveCount(0);
  const full = planner.getByRole('region', { name: 'Full focus', exact: true });
  await full.getByText('Phases, gear, support & level targets', { exact: true }).click();
  await expect(full.getByRole('region', { name: 'Verify AT', exact: true })).toContainText('9223372036854775807');
  await expect(planner.getByRole('region', { name: 'Combat prep runtime' })).toBeVisible();
  await planner.getByText('Model assumptions & comparison', { exact: true }).click();
  await expect(planner.getByRole('listitem').filter({ hasText: 'Owned loadouts frozen; future growth held fixed' })).toBeVisible();
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeEnabled();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Dashboard', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  await expect(planner.getByRole('radio', { name: 'Background casual', exact: true })).toBeChecked();
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([{ command: 'previewCombatPrep', args: { durationSeconds: 7200, backgroundEnergyPercent: 3, backgroundMagicPercent: 3 } }]);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: test.info().outputPath('combat-prep.png'), fullPage: true });
});

test('lane allocation costs stay distinct from Wandoos gains and optional automatic runtime metadata is read-only', async ({ page }) => {
  const planner = await openPlanner(page);
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  const full = planner.getByRole('region', { name: 'Full focus', exact: true });
  const casual = planner.getByRole('region', { name: 'Background casual', exact: true });
  for (const [region, label, expected] of [[full, 'Energy allocation opportunity cost', '1h'], [full, 'Magic allocation opportunity cost', '30m'], [casual, 'Energy allocation opportunity cost', '0s'], [casual, 'Magic allocation opportunity cost', 'Unavailable']] as const) {
    await expect(region.locator('div').filter({ has: page.getByText(label, { exact: true }) }).locator('dd')).toHaveText(expected);
  }
  await casual.getByText('Phases, gear, support & level targets', { exact: true }).click();
  await expect(casual.locator('div').filter({ has: page.getByText('Energy full-focus equivalent (Wandoos dump gain)', { exact: true }) }).locator('dd')).toHaveText('12m');
  await expect(casual.getByText('Resource reference profile', { exact: true })).toHaveCount(0);
  await planner.getByText('Model assumptions & comparison', { exact: true }).click();
  await expect(planner).toContainText('do not sum them');
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, { owner: 'scheduled', planName: 'saved-plan', stageIndex: 0, replanCount: 0, replanReason: 'AT boundary verified', deadlineRebirthSeconds: 900 }));
  const runtime = planner.getByRole('region', { name: 'Combat prep runtime' });
  await expect(runtime).toContainText('Automatic plan stage');
  await expect(runtime).toContainText('saved-plan');
  await expect(runtime.locator('div').filter({ has: page.getByText('Fixed deadline (rebirth elapsed)', { exact: true }) }).locator('dd')).toHaveText('15m');
  await expect(runtime.getByText('Boundary replans', { exact: true })).toHaveCount(0);
  await expect(runtime.locator('div').filter({ has: page.getByText('Plan stage', { exact: true }) }).locator('dd')).toHaveText('1');
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('actual activity timing in Advanced is run-local, updates through replans and retains a terminal receipt without actions', async ({ page }) => {
  const planner = await openPlanner(page);
  const runtime = planner.getByRole('region', { name: 'Combat prep runtime' });
  await expect(runtime).toContainText('Activity timing not recorded');
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, {
    active: true, status: 'running', owner: 'manual', runId: 'timing-run', phase: 'Augments',
    activityTimingScope: 'run', activityTimingSemantics: 'exclusive-phase-wall-time-including-setup',
    activityTimes: ['AT', 'Augments', 'Wandoos', 'NGU', 'Background'].map((kind, index) => ({ kind, elapsedSeconds: [61, 125, 0, 0, 0][index], active: kind === 'Augments' }))
  }));
  const timing = runtime.getByRole('region', { name: 'Actual activity timing' });
  await expect(timing).toContainText('Now: Augments / 2m 5s');
  await expect(timing.locator('.timing-rows dd')).toHaveText(['1m 1s', '2m 5s', 'Not visited']);
  await page.evaluate(() => {
    const run = (window as FixtureWindow).__combatRuntime.combatPrep as Record<string, unknown>;
    Object.assign(run, { replanCount: 2, phase: 'Wandoos', phaseElapsedSeconds: 0 });
    const rows = run.activityTimes as { active: boolean; elapsedSeconds: number }[];
    rows[1].active = false;
    rows[2].active = true;
    rows[2].elapsedSeconds = 30;
    rows[3].elapsedSeconds = 5;
  });
  await expect(timing).toContainText('Now: Wandoos / 30s');
  await expect(timing.locator('.timing-rows dd')).toHaveText(['1m 1s', '2m 5s', '30s', '5s']);
  await timing.getByText('How time is counted', { exact: true }).click();
  for (const text of ['Actual exclusive phase wall-time', 'including phase setup', 'not a forecast', 'not separately added', 'Excludes offline time and cumulative stage history']) await expect(timing).toContainText(text);
  await expect(timing).not.toContainText(/in memory|storage|injector session|polling/i);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await timing.scrollIntoViewIfNeeded();
  await page.screenshot({ path: test.info().outputPath('advanced-activity-timing.png'), fullPage: true });
  for (const state of [{ active: true, status: 'failed', cleanupPending: true }, { active: false, status: 'completed', cleanupPending: false }]) {
    await page.evaluate(state => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, state), state);
    await expect(timing.getByText(/^Now:/)).toHaveCount(0);
    await expect(timing.locator('.active-badge')).toHaveCount(0);
    await expect(timing.locator('.timing-rows dd')).toHaveText(['1m 1s', '2m 5s', '30s', '5s']);
  }
  await page.evaluate(() => { ((window as FixtureWindow).__combatRuntime.combatPrep as Record<string, unknown>).activityTimes = []; });
  await expect(timing).toContainText('Activity timing not recorded');
  await expect(timing.locator('dl')).toHaveCount(0);
  await page.evaluate(() => {
    const fixture = window as FixtureWindow;
    Object.assign(fixture.__combatRuntime.combatPrep as object, {
      active: true, status: 'running', activityTimes: ['AT', 'Augments', 'Wandoos', 'NGU', 'Background'].map(kind => ({ kind, elapsedSeconds: 0, active: kind === 'AT' }))
    });
    fixture.__combatRuntime.startedAt = 'stale-session';
    fixture.__combatRuntime.updatedAt = new Date(Date.now() - 60000).toISOString();
  });
  await expect(runtime.getByRole('status').first()).toContainText('Disconnected');
  await expect(timing).toContainText('Activity timing not recorded');
  await expect(timing.getByText(/^Now:/)).toHaveCount(0);
  await expect(timing.locator('dl')).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
});

test('no AT gate, including Wish 190, is not shown as failed verification', async ({ page }) => {
  const planner = await openPlanner(page);
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, { atGateRequired: false, atTargetsReached: false, phase: 'Wandoos', actual: { energyAtLevel: '0', magicAtLevel: '0' } }));
  const runtime = planner.getByRole('region', { name: 'Combat prep runtime' });
  const verified = runtime.locator('div').filter({ has: page.getByText('AT targets verified', { exact: true }) }).locator('dd');
  await expect(verified).toHaveText('Not applicable (no AT gate)');
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, { atGateRequired: true, atTargetsReached: false }));
  await expect(verified).toHaveText('No');
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, { atTargetsReached: true }));
  await expect(verified).toHaveText('Yes');
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, { atGateRequired: false, atTargetsReached: true }));
  await expect(verified).toHaveText('Not applicable (no AT gate)');
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('failed cleanup retains its owner, blocks Start and does not claim normal stage resumption', async ({ page }) => {
  const planner = await openPlanner(page);
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, { active: true, cleanupPending: true, status: 'failed', owner: 'scheduled', runId: 'cleanup-run', remainingSeconds: 0, supportRestoration: { status: 'restored' } }));
  const runtime = planner.getByRole('region', { name: 'Combat prep runtime' });
  await expect(runtime).toContainText('Status: failed');
  await expect(runtime.getByRole('status').filter({ hasText: 'Cleanup pending: combat prep still owns equipment and resources.' })).toBeVisible();
  await expect(runtime).not.toContainText('Support was restored at run end');
  await expect(planner.getByRole('button', { name: 'Cancel active run', exact: true })).toBeEnabled();
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  await expect(planner.getByRole('button', { name: 'Start selected', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await expect(planner.getByRole('button', { name: 'Start selected', exact: true })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.evaluate(() => Object.assign((window as FixtureWindow).__combatRuntime.combatPrep as object, { active: false, cleanupPending: false }));
  await expect(runtime).not.toContainText('Cleanup pending');
  await expect(runtime).not.toContainText('Support was restored at run end');
  await expect(planner.getByRole('button', { name: 'Start selected', exact: true })).toBeEnabled();
});

for (const source of ['combatPrep', 'planProgress'] as const) {
  test(`inactive ${source} recovery reconciles automatically, never manual Start or Cancel`, async ({ page }) => {
    const planner = await openPlanner(page);
    await page.evaluate(source => {
      const fixture = window as FixtureWindow;
      Object.assign(fixture.__combatRuntime.combatPrep as object, { active: false, cleanupPending: false, status: 'idle', runId: '', phase: '', cleanupAttemptCount: 0, supportRestoration: { status: 'restored' } });
      if (source === 'combatPrep') Object.assign(fixture.__combatRuntime.combatPrep as object, { cleanupRecoveryRequired: true, cleanupRecoveryReason: 'Original equipment references cannot be recovered.' });
      else fixture.__combatRuntime.planProgress = { cleanupRecoveryRequired: true, reason: 'Original equipment references cannot be recovered.' };
      fixture.__combatRuntime.resourceAllocation = { state: 'Failed', reason: 'Checkpoint write failed: access denied. Retrying automatically.' };
    }, source);
    const runtime = planner.getByRole('region', { name: 'Combat prep runtime' });
    await expect(runtime.getByRole('status').first()).toHaveText('Reconciling: Checkpoint write failed: access denied. Retrying automatically.');
    await expect(runtime).not.toContainText(/acknowledge|Restart current stage|Set as current stage|Skip to selected stage/);
    await expect(runtime).not.toContainText('Status: idle');
    await expect(runtime).not.toContainText('Support was restored at run end');
    await expect(planner.locator('.pill').first()).toHaveText('Reconciling');
    await planner.getByRole('button', { name: 'Preview', exact: true }).click();
    await expect(planner.getByRole('radio')).toHaveCount(2);
    await expect(planner.getByRole('button', { name: 'Start selected', exact: true })).toBeDisabled();
    await expect(planner.getByRole('button', { name: 'Cancel active run', exact: true })).toBeDisabled();
    await expect(planner.getByRole('button', { name: 'Cancel active run', exact: true })).toHaveAttribute('title', 'Reconciling: Checkpoint write failed: access denied. Retrying automatically.');
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(planner.getByRole('button', { name: 'Start selected', exact: true })).toBeDisabled();
    expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(1);
    expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}

test('seeded Wandoos support shows planned and observed selections, finances and cleanup without actions', async ({ page }) => {
  const planner = await openPlanner(page);
  const comparison = planner.getByRole('group', { name: 'Current phase support comparison' });
  const planned = comparison.getByRole('region', { name: 'Planned current-phase support', exact: true });
  const actual = comparison.getByRole('region', { name: 'Actual observed support', exact: true });
  await expect(planner.getByRole('region', { name: 'Combat prep runtime' })).toContainText('Phase: Wandoos');
  await expect(planned).toContainText('Dedicated phase support');
  await expect(planned.getByRole('listitem').filter({ hasText: 'Wandoos beard' })).toHaveText('Wandoos beard');
  await expect(planned).toContainText('Level 9007199254740993');
  await expect(planned).toContainText('Drain 1.250000e+100 gold / second');
  for (const [label, value] of [['Gross gold / second', '2.500000e+100'], ['Digger drain / second', '1.250000e+100'], ['Net gold / second', '1.250000e+100'], ['GPS budget limit', '75%'], ['Wandoos beard multiplier', '2'], ['Wandoos digger multiplier', '3']]) {
    await expect(planned.locator('div').filter({ has: page.getByText(label, { exact: true }) }).locator('dd')).toHaveText(value);
  }
  await expect(actual).toContainText('Current support');
  await expect(actual.getByRole('listitem').filter({ hasText: 'Observed beard' })).toHaveText('Observed beard');
  await expect(actual).toContainText('Observed digger / Level 00042 / Drain 0');
  await expect(actual).toContainText('-1.000000e+100');
  await expect(actual).not.toContainText('LongWandoosDiggerName');
  await expect(actual.locator('div').filter({ has: page.getByText('Share of gross GPS in use', { exact: true }) }).locator('dd')).toHaveText('50%');
  await expect(actual).not.toContainText('100%');
  await expect(actual).not.toContainText('GPS budget limit');
  const cleanup = planner.locator('dl[aria-label="Setup status"]');
  await expect(cleanup).toContainText('applied');
  await expect(cleanup).toContainText('support blocked');
  await expect(cleanup).toContainText('Digger selection changed by user');
  const plannedBox = (await planned.boundingBox())!;
  const actualBox = (await actual.boundingBox())!;
  if (page.viewportSize()!.width > 600) {
    expect(actualBox.x).toBeGreaterThan(plannedBox.x);
    expect(actualBox.y).toBe(plannedBox.y);
  } else {
    expect(actualBox.y).toBeGreaterThanOrEqual(plannedBox.y + plannedBox.height);
    expect(actualBox.x).toBe(plannedBox.x);
  }
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([]);
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  const full = planner.getByRole('region', { name: 'Full focus', exact: true });
  await expect(full.locator(':scope > details')).not.toHaveAttribute('open');
  await full.getByText('Phases, gear, support & level targets', { exact: true }).click();
  const wandoos = full.getByRole('region', { name: 'Train Wandoos', exact: true });
  await expect(wandoos.getByRole('region', { name: 'Planned phase support' })).toContainText('Level 9007199254740993');
  await expect(wandoos.getByRole('region', { name: 'Planned phase support' })).toContainText('GPS budget limit');
  await expect(wandoos).not.toContainText('Share of gross GPS in use');
  await expect(wandoos.getByText('Support variant:', { exact: false })).toHaveCount(0);
  await expect(full.getByRole('region', { name: 'Verify AT', exact: true })).toContainText('Current support');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
  await page.screenshot({ path: test.info().outputPath('combat-prep-support.png'), fullPage: true });
});

test('terminal support remains an actual observation without historical restoration claims', async ({ page }) => {
  const planner = await openPlanner(page);
  await page.evaluate(() => {
    const runtime = (window as FixtureWindow).__combatRuntime.combatPrep as Record<string, unknown>;
    runtime.status = 'completed';
    delete runtime.blockedDomain;
    delete runtime.blockedReason;
    runtime.supportRestoration = { status: 'restored', reason: 'Verified at run end' };
    const actual = runtime.actualSupport as Record<string, unknown>;
    actual.beards = [{ id: 2, name: 'Minute rotation beard' }];
    actual.drainPerSecond = 3.75e100;
  });
  const runtime = planner.getByRole('region', { name: 'Combat prep runtime' });
  const actual = runtime.getByRole('region', { name: 'Actual observed support', exact: true });
  const note = runtime.getByText(/Support was restored at run end/);
  await expect(note).toHaveCount(0);
  await expect(actual.getByRole('listitem').filter({ hasText: 'Minute rotation beard' })).toHaveText('Minute rotation beard');
  await expect(actual.locator('div').filter({ has: page.getByText('Share of gross GPS in use', { exact: true }) }).locator('dd')).toHaveText('150%');
  await expect(runtime.getByRole('alert')).toHaveCount(0);
  await page.evaluate(() => {
    const actual = ((window as FixtureWindow).__combatRuntime.combatPrep as Record<string, unknown>).actualSupport as Record<string, unknown>;
    actual.drainPerSecond = 0;
    actual.grossGoldPerSecond = 0;
  });
  await expect(actual.locator('div').filter({ has: page.getByText('Share of gross GPS in use', { exact: true }) }).locator('dd')).toHaveText('Unavailable');
  await expect(note).toHaveCount(0);
  await page.evaluate(() => { ((window as FixtureWindow).__combatRuntime.combatPrep as Record<string, unknown>).active = true; });
  await expect(note).toHaveCount(0);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('older schema-1 support remains unavailable without borrowing planned or observed readings', async ({ page }) => {
  const planner = await openPlanner(page);
  await page.evaluate(() => {
    const fixture = window as FixtureWindow;
    fixture.__combatLegacySupport = true;
    const runtime = fixture.__combatRuntime.combatPrep as Record<string, unknown>;
    delete runtime.actualSupport;
    delete runtime.supportStatus;
    delete runtime.supportRestoration;
    delete runtime.blockedDomain;
    delete runtime.blockedReason;
  });
  const planned = planner.getByRole('region', { name: 'Planned current-phase support', exact: true });
  const actual = planner.getByRole('region', { name: 'Actual observed support', exact: true });
  await expect(actual.locator('dd')).toHaveText(Array(9).fill('Unavailable'));
  await expect(planned).toContainText('Level 9007199254740993');
  await expect(planner.locator('dl[aria-label="Setup status"] dd')).toHaveText(['Unavailable']);
  await page.evaluate(() => {
    const runtime = (window as FixtureWindow).__combatRuntime.combatPrep as Record<string, unknown>;
    runtime.actualSupport = runtime.plannedSupport;
    delete runtime.plannedSupport;
  });
  await expect(planned.locator('dd')).toHaveText(Array(9).fill('Unavailable'));
  await expect(actual).toContainText('Level 9007199254740993');
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  const full = planner.getByRole('region', { name: 'Full focus', exact: true });
  await full.getByText('Phases, gear, support & level targets', { exact: true }).click();
  for (const phase of ['Verify AT', 'Train Wandoos']) {
    await expect(full.getByRole('region', { name: phase, exact: true }).getByRole('region', { name: 'Planned phase support' }).locator('dd')).toHaveText(Array(9).fill('Unavailable'));
  }
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeEnabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
});

test('budget shortcuts only edit the local fixed budget independently of global Quick and augment estimates', async ({ page }) => {
  const planner = await openPlanner(page);
  for (const [index, [label, seconds]] of [['15m', '900'], ['1h', '3600'], ['2h', '7200'], ['24h', '86400']].entries()) {
    await planner.getByRole('button', { name: 'Preview', exact: true }).click();
    await expect(planner.getByRole('button', { name: 'Start selected' })).toBeEnabled();
    await planner.getByRole('group', { name: 'Total budget shortcuts' }).getByRole('button', { name: label, exact: true }).click();
    await expect(planner.getByRole('spinbutton', { name: 'Total budget (seconds)' })).toHaveValue(seconds);
    await expect(planner.getByRole('radio')).toHaveCount(0);
    await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
    expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(index + 1);
  }
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Dashboard', exact: true }).click();
  const quick = page.getByRole('article', { name: 'Quick', exact: true });
  await expect(quick.getByRole('button', { name: 'Manage presets', exact: true })).toBeVisible();
  await expect(page.locator('#quick-preset-details')).toBeHidden();
  await expect(page.getByRole('button', { name: 'Edit Augment estimate (s)', exact: true })).toHaveCount(0);
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  await expect(planner.getByRole('spinbutton', { name: 'Total budget (seconds)' })).toHaveValue('86400');
  await expect(page.getByRole('spinbutton', { name: 'Duration (s)', exact: true })).toHaveValue('300');
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(4);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
});

test('explicit Start sends selected tokens and Cancel targets runtime ID while UI stays usable', async ({ page }) => {
  const planner = await openPlanner(page);
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  await planner.getByRole('radio', { name: 'Background casual', exact: true }).check();
  await planner.getByRole('button', { name: 'Start selected' }).click();
  await expect(planner.getByRole('region', { name: 'Combat prep runtime' })).toContainText('Status: running');
  await expect(planner.getByText('Run ID', { exact: true })).toHaveCount(0);
  await expect(planner.getByText('runtime-run-not-candidate', { exact: true })).toHaveCount(0);
  await expect(planner).toContainText('45s');
  await expect(planner.getByRole('button', { name: 'Preview', exact: true })).toBeEnabled();
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
  await expect(planner.getByRole('button', { name: 'Cancel active run' })).toBeEnabled();
  // A new preview selection must not alter which run Cancel addresses.
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  await planner.getByRole('radio', { name: /Full focus/ }).check();
  await planner.getByRole('button', { name: 'Cancel active run' }).click();
  await expect(planner.getByRole('region', { name: 'Combat prep runtime' })).toContainText('Status: cancelled');
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([
    { command: 'previewCombatPrep', args: { durationSeconds: 7200, backgroundEnergyPercent: 3, backgroundMagicPercent: 3 } },
    { command: 'startCombatPrep', args: { previewId: 'preview-token', candidateId: 'casual' } },
    { command: 'previewCombatPrep', args: { durationSeconds: 7200, backgroundEnergyPercent: 3, backgroundMagicPercent: 3 } },
    { command: 'cancelCombatPrep', args: { runId: 'runtime-run-not-candidate' } }
  ]);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('option edits invalidate preview and invalid inputs stay local without auto-preview', async ({ page }) => {
  const planner = await openPlanner(page);
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeEnabled();
  await planner.getByRole('spinbutton', { name: 'Background energy (%)' }).fill('4.5');
  await expect(planner.getByRole('radio')).toHaveCount(0);
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
  for (const invalid of ['', '0', '86401', '1.5']) {
    await planner.getByRole('spinbutton', { name: 'Total budget (seconds)' }).fill(invalid);
    await expect(planner.getByRole('alert')).toContainText('Duration seconds');
    await expect(planner.getByRole('button', { name: 'Preview', exact: true })).toBeDisabled();
  }
  await planner.getByRole('spinbutton', { name: 'Total budget (seconds)' }).fill('3600');
  await expect(planner.getByRole('button', { name: 'Preview', exact: true })).toBeEnabled();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Logs', exact: true }).click();
  await page.getByRole('navigation', { name: 'Primary' }).getByRole('button', { name: 'Advanced', exact: true }).click();
  await expect(planner.getByRole('spinbutton', { name: 'Total budget (seconds)' })).toHaveValue('3600');
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(1);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
});

test('expired previews and changed injector sessions cannot start', async ({ page }) => {
  const planner = await openPlanner(page);
  await page.evaluate(() => { (window as FixtureWindow).__combatStalePreview = true; });
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  await expect(planner).toContainText('Preview expired');
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
  await page.evaluate(() => { (window as FixtureWindow).__combatStalePreview = false; });
  await planner.getByRole('button', { name: 'Refresh preview', exact: true }).click();
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeEnabled();
  await page.evaluate(() => { (window as FixtureWindow).__combatRuntime.startedAt = 'session-two'; });
  await expect(planner.getByRole('radio')).toHaveCount(0);
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(2);
});

test('unknown Start outcome spends token without replay and missing telemetry stays unavailable', async ({ page }) => {
  const planner = await openPlanner(page);
  await planner.getByRole('button', { name: 'Preview', exact: true }).click();
  await page.evaluate(() => { (window as FixtureWindow).__combatFailure = 'Transport lost after enqueue'; });
  await planner.getByRole('button', { name: 'Start selected' }).click();
  await expect(page.getByRole('alert')).toContainText('Outcome may be unknown');
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
  await expect(planner.getByRole('button', { name: 'Preview', exact: true })).toBeEnabled();
  await page.evaluate(() => { (window as FixtureWindow).__combatRuntime.combatPrep = null; });
  const runtime = planner.getByRole('region', { name: 'Combat prep runtime' });
  await expect(runtime.getByRole('status')).toHaveText('Run status unavailable.');
  await expect(runtime.locator('dl')).toHaveCount(0);
  await expect(planner.getByRole('button', { name: 'Cancel active run' })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toHaveLength(2);
});

test('missing backend capabilities disable planner actions', async ({ page }) => {
  const planner = await openPlanner(page);
  await page.evaluate(() => { (window as FixtureWindow).__combatRuntime.supportedCommands = []; });
  await expect(planner.getByRole('button', { name: 'Preview', exact: true })).toBeDisabled();
  await expect(planner.getByRole('button', { name: 'Start selected' })).toBeDisabled();
  await expect(planner.getByRole('button', { name: 'Cancel active run' })).toBeDisabled();
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([]);
});

test('observed AT telemetry stays exact, read-only and distinguishes zero, nonzero and missing Wandoos magic', async ({ page }) => {
  const planner = await openPlanner(page);
  await page.evaluate(() => {
    (window as FixtureWindow).__combatRuntime.combatPrep = {
      schemaVersion: 1, active: true, status: 'running', phase: 'AT', runId: 'observed-run',
      actual: {
        energyAtLevel: '9223372036854775807', magicAtLevel: '9007199254740993',
        energyAtAllocation: '1234567890123456789', magicAtAllocation: '987654321012345678',
        wandoosEnergyLevel: '100000000000000001', wandoosMagicLevel: '200000000000000003',
        wandoosEnergyAllocation: '0', wandoosMagicAllocation: '0',
        energyWandoosLevelsPerSecond: 1.25, magicWandoosLevelsPerSecond: 0,
        gold: 100000, augmentIndex: 3, augmentLevel: '300000000000000007', upgradeLevel: '400000000000000009',
        augmentEnergyAllocation: '333333333333333333', upgradeEnergyAllocation: '444444444444444444'
      }
    };
  });
  const details = planner.locator('details[aria-label="Observed training details"]');
  const readings = details.locator('dl[aria-label="Observed training readings"]');
  await expect(details).toBeVisible();
  await expect(details).not.toHaveAttribute('open');
  await details.getByText('Observed levels, allocations & rates', { exact: true }).click();
  await expect(details).toContainText('Both AT-3 (energy AT) and AT-4 (magic AT) consume energy, not magic.');
  await expect(details.getByRole('status')).toHaveText('Wandoos magic held at zero');
  await expect(readings).toContainText('9223372036854775807');
  await expect(readings).toContainText('987654321012345678');
  await expect(readings).toContainText('400000000000000009');
  await expect(readings.locator('div').filter({ has: page.getByText('Wandoos energy levels / second', { exact: true }) }).locator('dd')).toHaveText('1.25');
  await expect(readings.locator('input')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.evaluate(() => {
    const runtime = (window as FixtureWindow).__combatRuntime.combatPrep as { actual: Record<string, unknown> };
    runtime.actual.wandoosMagicAllocation = '9007199254740993';
  });
  await expect(details.getByRole('alert')).toContainText('Wandoos magic is not zero during AT');
  await expect(details.getByRole('status')).toHaveCount(0);
  await expect(readings.locator('div').filter({ has: page.getByText('Wandoos magic allocation', { exact: true }) }).locator('dd')).toHaveText('9007199254740993');
  await page.evaluate(() => {
    const runtime = (window as FixtureWindow).__combatRuntime.combatPrep as { actual: Record<string, unknown> };
    delete runtime.actual.wandoosMagicAllocation;
  });
  await expect(details.getByRole('alert')).toContainText('Cannot verify Wandoos magic is zero during AT');
  await expect(details.getByRole('status')).toHaveCount(0);
  await expect(readings.locator('div').filter({ has: page.getByText('Wandoos magic allocation', { exact: true }) }).locator('dd')).toHaveText('Unavailable');
  expect(await page.evaluate(() => (window as FixtureWindow).__combatCalls)).toEqual([]);
  expect(await page.evaluate(() => (window as FixtureWindow).__combatNativeCalls)).not.toContain('save_settings_patch_json');
});
