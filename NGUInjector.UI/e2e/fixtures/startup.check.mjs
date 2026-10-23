import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { test } from 'node:test';
import { resolveConfig } from 'vite';

test('each Playwright invocation owns a separate Vite cache and server', () => {
  const readConfig = () => JSON.parse(execFileSync(process.execPath, [
    '--experimental-strip-types', '--input-type=module', '-e',
    "import config from './playwright.config.ts'; console.log(JSON.stringify(config.webServer));"
  ], { encoding: 'utf8' }));
  const first = readConfig();
  const second = readConfig();
  assert.equal(typeof first.env?.NGUI_TEST_VITE_CACHE, 'string');
  assert.notEqual(first.env.NGUI_TEST_VITE_CACHE, second.env.NGUI_TEST_VITE_CACHE);
  assert.equal(first.reuseExistingServer, false);
});

test('Vite dependency discovery and watching exclude native build output', async () => {
  const config = await resolveConfig({}, 'serve');
  assert.deepEqual(config.optimizeDeps.entries, ['index.html']);
  assert.ok(config.server.watch.ignored.includes('**/src-tauri/**'));
});
