import { describe, expect, it } from 'vitest';
import config from '../src-tauri/tauri.conf.json';

describe('native window drag and drop', () => {
  it('leaves HTML5 drag events to WebView2 instead of intercepting them as file drops', () => {
    expect(config.app.windows.length).toBeGreaterThan(0);
    for (const window of config.app.windows as { dragDropEnabled?: boolean }[]) {
      expect(window.dragDropEnabled).toBe(false);
    }
  });
});
