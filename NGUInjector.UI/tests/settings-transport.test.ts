import { beforeEach, describe, expect, it, vi } from 'vitest';
import { invoke } from '@tauri-apps/api/core';
import * as api from '../src/lib/api';

vi.mock('@tauri-apps/api/core', () => ({ invoke: vi.fn() }));
const native = vi.mocked(invoke);
const revision = 'a'.repeat(64);

beforeEach(() => { native.mockReset(); });

describe('settings string transport', () => {
  it('reads exact numeric settings and a separate byte revision without object IPC', async () => {
    native.mockResolvedValueOnce({ settings_json: '{"Future":9007199254740993,"fraction":0.12345678901234567890123456789}', settings_revision: revision });
    const state = await api.getState('A:/runtime');
    expect(api.stringifyJson(state.settings)).toBe('{"Future":9007199254740993,"fraction":0.12345678901234567890123456789}');
    expect(state.settings_revision).toBe(revision);
    expect(state.settings).not.toHaveProperty('__externalUiRevision');
  });

  it('accepts explicit absence rather than inventing a revision', async () => {
    native.mockResolvedValueOnce({ settings_json: '{}', settings_revision: '' });
    const state = await api.getState();
    expect(state.settings).toEqual({});
    expect(state.settings_revision).toBe('');
  });

  it.each([undefined, null, 'A'.repeat(64), 'a'.repeat(63)])('rejects invalid or absent read revision %s', async token => {
    native.mockResolvedValueOnce({ settings_json: '{}', settings_revision: token });
    await expect(api.getState()).rejects.toThrow('explicit revision');
  });

  it('rejects the obsolete settings object transport', async () => {
    native.mockResolvedValueOnce({ settings: {}, settings_revision: revision });
    await expect(api.getState()).rejects.toThrow('lossless JSON transport');
  });

  it.each([undefined, null, 'A'.repeat(64)])('does not submit a write without a valid explicit revision %s', async token => {
    await expect(api.saveSettingsPatch('A:/runtime', {}, token as string)).rejects.toThrow('explicit revision');
    expect(native).not.toHaveBeenCalled();
  });

  it('requires a committed snapshot even if a fresh heartbeat accompanied success', async () => {
    native.mockResolvedValueOnce('{"success":true,"result":{"completedAt":"2026-10-22T00:00:00Z","data":{"runtime":{"status":"running"}}}}');
    await expect(api.saveSettingsPatch('A:/runtime', {}, revision)).rejects.toThrow('committed snapshot');
    expect(native).toHaveBeenCalledExactlyOnceWith('save_settings_patch_json', { runtimeDir: 'A:/runtime', patchJson: '{}', expectedRevision: revision });
  });
});
