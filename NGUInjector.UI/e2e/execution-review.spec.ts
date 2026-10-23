import { expect, test, type Page } from '@playwright/test';

async function openReview(page: Page, options: { recovery?: boolean; routes?: boolean; applyFailure?: 'enqueue' | 'result' } = {}) {
  await page.addInitScript(({ recovery = false, routes = false, applyFailure }) => {
    const fixture = window as typeof window & { reviewCalls: { command: string; args: Record<string, unknown> }[] };
    fixture.reviewCalls = [];
    const runtime = { externalUiEnabled: true, status: 'running', protocolVersion: 3, settingsCasVersion: 1, startedAt: 'review-session', runtimeRevision: 1,
      supportedCommands: ['previewExecutionRebind', 'previewExecutionRestart', 'applyExecutionRebind'],
      executionReview: { reviewRequired: true, canPreview: !recovery, reason: recovery ? 'Unreadable checkpoint' : 'Review acquired selection',
        ...(recovery ? { recovery: { checkpointRevision: 'b'.repeat(64), planRevision: 'c'.repeat(64), stages: [{ stageIndex: 0, name: 'Original AT stage' }] } } : {}) } };
    let result: unknown = null;
    let previewCount = 0;
    let applyCount = 0;
    let resultUnavailable = false;
    // Mirrors ExportRouteExecutionCommitments: selection rows use slot/id/material,
    // support lives on each process, and encounter/tower/boss history is separate.
    const routeCommitments = {
      version: 1, invalidEpoch: false, rebirth: '12', elapsed: 12345, origin: 54321,
      processes: [{ owner: 'boss-push', claimOwner: 'boss-push', acquisition: 'd'.repeat(32), domains: 248,
        permission: `True:${'d'.repeat(32)}`, zone: -1, idle: false, beast: false, controlsGear: true, changeSupport: true,
        gear: [{ slot: -1, id: 552, material: 'approved-boss-head' }, { slot: -2, id: 41, material: 'approved-chest' },
          { slot: -3, id: 42, material: 'approved-legs' }, { slot: -4, id: 43, material: 'approved-boots' },
          { slot: -5, id: 44, material: 'approved-weapon' }, { slot: 10000, id: 0, material: 'empty-equipment' }],
        beards: [0, 6], diggers: [{ id: 2, level: '7' }, { id: 4, level: '42' }] }],
      manual: [{ index: 4, started: true, disarmed: true, resetSeen: false, elapsed: 600,
        deaths: 1, limit: 3, kills: 2, phase: 1, ownedSeconds: 125.5, outcome: 'Waiting for next spawn', loadout: 1,
        gear: [{ slot: -1, id: 550, material: 'approved-titan-head' }] }], manualIndex: -1,
      towerDeaths: 2, towerFloor: 87, towerMode: true, towerHistory: 'lost', towerStart: 10, towerEnd: 10000, towerShifter: true,
      towerGear: [{ slot: -1, id: 551, material: 'approved-tower-head' }],
      goldIndex: -1, fightIndex: -1, goldStarted: '0', fightStarted: '0', goldPending: -1, goldGear: false, goldSpawns: [], cooldowns: [],
      adventureGoldZone: -1, adventureGoldHighest: -1, adventureGoldEntered: false, adventureGoldAttempted: -1,
      bossIdentity: 'd'.repeat(32), bossOrdinal: 1, bossSeconds: 2.5, bossNuke: false, bossStopping: false, bossId: 100,
      os: 2, wEnergyLevel: '7', wMagicLevel: '8', wEnergyProgress: .1, wMagicProgress: .2, wBootup: 20,
      atTargets: [3000, 3000, 0, 0, 0], actions: []
    };
    Object.assign(window, { __TAURI_INTERNALS__: { invoke: async (command: string, args: Record<string, unknown> = {}) => {
      const casPath = '/e2e/fixtures/settings-cas.ts';
      const cas = await import(casPath);
      if (command === 'get_ngui_state') return { runtime_dir: 'C:\\Fixture', marker_enabled: true, connected: true,
        ...await cas.settingsSnapshot({}), protocol: {}, runtime: { ...runtime, updatedAt: new Date().toISOString() }, profiles: [], plans: [], logs: [], command_results: [] };
      if (command === 'enqueue_command') {
        const envelope = args.envelope as { command: string; args: Record<string, unknown> };
        fixture.reviewCalls.push(structuredClone(envelope));
        const id = `review-${fixture.reviewCalls.length}`;
        resultUnavailable = false;
        if (envelope.command === 'applyExecutionRebind') {
          applyCount++;
          if (applyCount === 1 && applyFailure === 'enqueue') throw new Error('Approval transport lost');
          if (applyCount === 1 && applyFailure === 'result') resultUnavailable = true;
          else runtime.executionReview.reviewRequired = false;
        } else previewCount++;
        runtime.runtimeRevision++;
        const data: Record<string, unknown> = { runtime: { ...runtime, updatedAt: new Date().toISOString() } };
        if (envelope.command !== 'applyExecutionRebind') data.result = {
          token: (previewCount > 1 ? 'c' : 'a').repeat(32), checkpointRevision: 'b'.repeat(64),
          ...(recovery ? { operation: 'restart', stageName: 'Original AT stage' }
            : routes ? { owner: 'Route processes', commitments: routeCommitments }
              : { preset: { Name: 'Acquired NGU' }, equipment: { equipment: [{ targetSlot: -1, id: 552 }] },
                support: { beards: [3], diggers: [{ id: 4, level: '7' }] } })
        };
        result = { id, command: envelope.command, success: true, message: 'Reviewed command completed', completedAt: new Date().toISOString(), data };
        return id;
      }
      if (command === 'read_command_result') {
        if (resultUnavailable) throw new Error('Approval result unavailable');
        return result;
      }
      if (command === 'tail_log') return '';
      throw new Error(`Unexpected IPC: ${command}`);
    } } });
    localStorage.clear();
  }, options);
  await page.goto('/');
  return page.getByRole('article', { name: 'Selection review' });
}

test('selection review previews without mutation and applies only its token', async ({ page }) => {
  const card = await openReview(page);
  await expect(card.getByRole('button', { name: 'Approve selection' })).toHaveCount(0);
  await card.getByRole('button', { name: 'Preview selection' }).click();
  await expect(card.getByRole('cell', { name: '552', exact: true })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { reviewCalls: unknown[] }).reviewCalls)).toEqual([{ command: 'previewExecutionRebind', args: {} }]);
  await card.getByRole('button', { name: 'Approve selection' }).click();
  await expect(card).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { reviewCalls: unknown[] }).reviewCalls)).toEqual([
    { command: 'previewExecutionRebind', args: {} }, { command: 'applyExecutionRebind', args: { token: 'a'.repeat(32), checkpointRevision: 'b'.repeat(64) } }
  ]);
});

test('corrupt checkpoint recovery never implicitly chooses or restarts a stage', async ({ page }) => {
  const card = await openReview(page, { recovery: true });
  await expect(card.getByRole('button', { name: 'Review restart' })).toBeDisabled();
  await card.getByRole('combobox', { name: 'Restart stage' }).selectOption('0');
  await card.getByRole('button', { name: 'Review restart' }).click();
  await expect(card.getByRole('button', { name: 'Restart selected stage' })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { reviewCalls: unknown[] }).reviewCalls)).toEqual([
    { command: 'previewExecutionRestart', args: { stageIndex: 0, checkpointRevision: 'b'.repeat(64), planRevision: 'c'.repeat(64) } }
  ]);
  await card.getByRole('button', { name: 'Restart selected stage' }).click();
  await expect(card).toHaveCount(0);
});

test('route preview exposes actual selections and retained encounter budgets without applying them', async ({ page }) => {
  const card = await openReview(page, { routes: true });
  await card.getByRole('button', { name: 'Preview selection' }).click();
  const boss = card.getByRole('region', { name: 'Boss push selection' });
  await expect(boss).toBeVisible();
  await expect(boss).toContainText('Safe Zone: Awakening Site');
  const bossGear = boss.getByRole('table', { name: 'Selected gear' });
  await expect(bossGear.getByRole('row').filter({ hasText: '552' }).getByRole('cell')).toHaveText(['-1', '552']);
  await expect(bossGear.getByRole('cell', { name: 'Empty', exact: true })).toBeVisible();
  await expect(boss).toContainText('Beards: 0, 6');
  await expect(boss).toContainText('Diggers: 2: 7, 4: 42');
  const manual = card.getByRole('region', { name: 'WALDERP history' });
  await expect(manual).toContainText('1 / 3');
  await expect(manual).toContainText('125.5s');
  await expect(manual).toContainText('Waiting for next spawn');
  await expect(manual.getByRole('cell', { name: '550', exact: true })).toBeVisible();
  const tower = card.getByRole('region', { name: 'ITOPOD push history' });
  await expect(tower.getByText('87', { exact: true })).toBeVisible();
  await expect(tower.getByText('2', { exact: true })).toBeVisible();
  await expect(tower.getByRole('cell', { name: '551', exact: true })).toBeVisible();
  await expect(card.getByRole('region', { name: 'Boss push history' })).toContainText('2.5s');
  await expect(card).not.toContainText('approved-boss-head');
  expect(await page.evaluate(() => (window as unknown as { reviewCalls: unknown[] }).reviewCalls)).toEqual([{ command: 'previewExecutionRebind', args: {} }]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('route approval sends only the exact reviewed token and checkpoint revision', async ({ page }) => {
  const card = await openReview(page, { routes: true });
  await card.getByRole('button', { name: 'Preview selection' }).click();
  await expect(card.getByRole('region', { name: 'Boss push selection' })).toBeVisible();
  await card.getByRole('button', { name: 'Approve selection' }).click();
  await expect(card).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { reviewCalls: unknown[] }).reviewCalls)).toEqual([
    { command: 'previewExecutionRebind', args: {} }, { command: 'applyExecutionRebind', args: { token: 'a'.repeat(32), checkpointRevision: 'b'.repeat(64) } }
  ]);
});

for (const applyFailure of ['enqueue', 'result'] as const) {
  test(`route approval ${applyFailure} failure consumes its token and requires an explicit fresh review`, async ({ page }) => {
    const card = await openReview(page, { routes: true, applyFailure });
    await card.getByRole('button', { name: 'Preview selection' }).click();
    await card.getByRole('button', { name: 'Approve selection' }).click();
    await expect(page.getByRole('alert')).toContainText('Outcome may be unknown');
    await expect(page.getByRole('alert')).toContainText('fresh review');
    await expect(card.getByRole('button', { name: 'Approve selection' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Refresh', exact: true }).click();
    await expect(card.getByRole('button', { name: 'Approve selection' })).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { reviewCalls: unknown[] }).reviewCalls)).toEqual([
      { command: 'previewExecutionRebind', args: {} }, { command: 'applyExecutionRebind', args: { token: 'a'.repeat(32), checkpointRevision: 'b'.repeat(64) } }
    ]);
    await card.getByRole('button', { name: 'Preview selection' }).click();
    await expect(card.getByRole('region', { name: 'Boss push selection' })).toBeVisible();
    await card.getByRole('button', { name: 'Approve selection' }).click();
    await expect(card).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { reviewCalls: unknown[] }).reviewCalls)).toEqual([
      { command: 'previewExecutionRebind', args: {} }, { command: 'applyExecutionRebind', args: { token: 'a'.repeat(32), checkpointRevision: 'b'.repeat(64) } },
      { command: 'previewExecutionRebind', args: {} }, { command: 'applyExecutionRebind', args: { token: 'c'.repeat(32), checkpointRevision: 'b'.repeat(64) } }
    ]);
  });
}
