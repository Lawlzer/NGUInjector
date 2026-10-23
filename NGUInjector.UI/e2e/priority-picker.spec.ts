import { expect, test, type Page } from '@playwright/test';

type FixtureWindow = Window & { __priorityValue: string; __priorityEdits: number; __prioritySet: (props: Record<string, unknown>) => void };

// Standalone component fixture: no App, controller, native IPC or game operations.
async function mountPicker(page: Page, value: string, lane = 'energy', disabled = false): Promise<void> {
  await page.route(/http:\/\/127\.0\.0\.1:\d+\/$/, route => route.fulfill({ contentType: 'text/html', body: '<!doctype html><html><head><link rel="stylesheet" href="/src/styles.css"></head><body><main id="fixture" style="padding:8px;max-width:420px"></main></body></html>' }));
  await page.goto('/');
  const main = await (await page.request.get('/src/main.ts')).text();
  const runtimePath = main.match(/import\s*\{\s*mount\s*\}\s*from\s*["']([^"']+)["']/)?.[1];
  if (!runtimePath) throw new Error('Missing Svelte runtime');
  await page.evaluate(async ({ runtimePath, value, lane, disabled }) => {
    const { createClassComponent } = await import(runtimePath.replace(/svelte\.js(?=\?|$)/, 'svelte_legacy.js'));
    const componentPath = '/src/lib/PriorityPicker.svelte';
    const optionsPath = '/src/lib/priority-options.ts';
    const apiPath = '/src/lib/api.ts';
    const { default: Component } = await import(componentPath);
    const { energyPriorityOptions, magicPriorityOptions, beardPriorityOptions } = await import(optionsPath);
    const { parseJson, stringifyJson } = await import(apiPath);
    const fixture = window as unknown as FixtureWindow;
    fixture.__priorityValue = value;
    fixture.__priorityEdits = 0;
    const component = createClassComponent({ component: Component, target: document.getElementById('fixture'), props: {
      label: 'Priorities', value: parseJson(value), disabled,
      options: lane === 'energy' ? energyPriorityOptions : lane === 'magic' ? magicPriorityOptions : beardPriorityOptions,
      defaultLabel: 'Use automatic defaults',
      onchange: (next: unknown) => {
        fixture.__priorityValue = stringifyJson(next);
        fixture.__priorityEdits++;
        component.$set({ value: parseJson(fixture.__priorityValue) });
      }
    } });
    fixture.__prioritySet = props => component.$set(props);
  }, { runtimePath, value, lane, disabled });
  await expect(page.getByRole('group', { name: 'Priorities', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
}

async function saved(page: Page): Promise<string> {
  return page.evaluate(() => (window as unknown as FixtureWindow).__priorityValue);
}

test('named choices append, keyboard buttons reorder, and clearing returns to default []', async ({ page }) => {
  await mountPicker(page, '[]');
  const add = page.getByRole('combobox', { name: 'Add priorities', exact: true });
  await expect(page.getByText('Use automatic defaults', { exact: true })).toBeVisible();
  await add.selectOption({ label: 'Time Machine' });
  await add.selectOption({ label: 'NGU: Adventure Alpha' });
  await expect(page.getByRole('listitem')).toHaveCount(2);
  expect(await saved(page)).toBe('["TM","NGU-4"]');
  await expect(add.locator('option').filter({ hasText: /^Time Machine$/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Move Time Machine up', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Move NGU: Adventure Alpha up', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('listitem').first()).toContainText('NGU: Adventure Alpha');
  expect(await saved(page)).toBe('["NGU-4","TM"]');
  await expect(page.getByRole('button', { name: 'Remove NGU: Adventure Alpha', exact: true })).toBeFocused();
  await page.getByRole('button', { name: 'Move NGU: Adventure Alpha down', exact: true }).click();
  expect(await saved(page)).toBe('["TM","NGU-4"]');
  await page.getByRole('button', { name: 'Use automatic defaults', exact: true }).click();
  await expect(page.getByRole('listitem')).toHaveCount(0);
  expect(await saved(page)).toBe('[]');
  await expect(add).toBeFocused();
});

test('unknown and duplicate saved values survive reorder and can be individually removed', async ({ page }) => {
  await mountPicker(page, '[" tm ","FUTURE",9007199254740993,"TM"]');
  await expect(page.getByRole('listitem')).toHaveCount(4);
  await page.getByRole('button', { name: 'Move Unknown saved value: "FUTURE" up', exact: true }).click();
  expect(await saved(page)).toBe('["FUTURE"," tm ",9007199254740993,"TM"]');
  await page.getByRole('combobox', { name: 'Add priorities' }).selectOption({ label: 'Augments: Quadruple Sided Laser Sword' });
  expect(await saved(page)).toBe('["FUTURE"," tm ",9007199254740993,"TM","AUG-13"]');
  await page.getByRole('button', { name: 'Remove Unknown saved value: "FUTURE"', exact: true }).click();
  expect(await saved(page)).toBe('[" tm ",9007199254740993,"TM","AUG-13"]');
  await page.getByRole('button', { name: 'Remove Unknown saved value: 9007199254740993', exact: true }).click();
  expect(await saved(page)).toBe('[" tm ","TM","AUG-13"]');
  await expect(page.getByRole('listitem')).toHaveCount(3);
  await expect(page.getByRole('textbox')).toHaveCount(0);
});

test('beard names store numeric IDs and last removal restores auto', async ({ page }) => {
  await mountPicker(page, '[]', 'beard');
  const add = page.getByRole('combobox', { name: 'Add priorities' });
  await add.selectOption({ label: 'Ladybeard' });
  await add.selectOption({ label: 'Fu Manchu' });
  expect(await saved(page)).toBe('[4,0]');
  await page.getByRole('button', { name: 'Move Fu Manchu up', exact: true }).click();
  expect(await saved(page)).toBe('[0,4]');
  await page.getByRole('button', { name: 'Remove Ladybeard', exact: true }).click();
  await page.getByRole('button', { name: 'Remove Fu Manchu', exact: true }).click();
  expect(await saved(page)).toBe('[]');
  await expect(add).toBeFocused();
  await expect(page.getByText('Use automatic defaults', { exact: true })).toBeVisible();
});

test('disabled picker cannot emit edits and handles external readback', async ({ page }) => {
  await mountPicker(page, '["RIT-7","NGU"]', 'magic', true);
  await expect(page.getByRole('combobox', { name: 'Add priorities' })).toBeDisabled();
  for (const button of await page.getByRole('button').all()) await expect(button).toBeDisabled();
  await page.evaluate(() => {
    const picker = document.querySelector('fieldset')!;
    picker.querySelector<HTMLButtonElement>('[data-move="1"]')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    picker.querySelector<HTMLButtonElement>('[data-remove]')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    (window as unknown as FixtureWindow).__prioritySet({ value: ['RIT-0', 'TM'] });
  });
  await expect(page.getByRole('listitem').first()).toContainText('Blood Magic: Poke Yourself with a Tack');
  expect(await page.evaluate(() => (window as unknown as FixtureWindow).__priorityEdits)).toBe(0);
  expect(await saved(page)).toBe('["RIT-7","NGU"]');
});
