import { expect, type Locator } from '@playwright/test';

export async function setPriorities(scope: Locator, label: string, priorities: string[]): Promise<void> {
  const picker = scope.getByRole('group', { name: label, exact: true });
  const remove = picker.getByRole('button', { name: /^Remove / });
  while (await remove.count()) await remove.first().click();
  for (const priority of priorities) {
    await picker.getByRole('combobox', { name: `Add ${label.toLowerCase()}`, exact: true }).selectOption({ label: priority });
  }
  await expect(picker.locator('.priority-name')).toHaveText(priorities);
}
