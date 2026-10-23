import { LosslessNumber, isSafeNumber, parse, stringify } from 'lossless-json';

export function parseSettingsJson(text: string): Record<string, unknown> {
  const value = parse(text, undefined, token => isSafeNumber(token) ? Number(token) : new LosslessNumber(token));
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Settings JSON must be an object.');
  return value as Record<string, unknown>;
}

export function settingsPatch(args: Record<string, unknown>): Record<string, unknown> {
  if (typeof args.patchJson !== 'string' || typeof args.expectedRevision !== 'string'
    || !/^[a-f0-9]{64}$/.test(args.expectedRevision) || 'patch' in args) throw new Error('Expected patchJson and an explicit settings revision.');
  return parseSettingsJson(args.patchJson);
}

export async function settingsSnapshot(settings: Record<string, unknown>): Promise<{ settings_json: string; settings_revision: string }> {
  const settings_json = stringify(settings)!;
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(settings_json));
  const settings_revision = Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('');
  return { settings_json, settings_revision };
}

export async function commitSettings(settings: Record<string, unknown>, args: Record<string, unknown>, mode = 'offline'): Promise<string> {
  const patch = settingsPatch(args);
  const before = await settingsSnapshot(settings);
  // Compare again after hashing: a concurrent writer must not slip through the async digest.
  if (args.expectedRevision !== before.settings_revision || stringify(settings) !== before.settings_json) {
    throw new Error('Settings revision changed. Reload settings before saving.');
  }
  Object.assign(settings, patch);
  const committed = await settingsSnapshot(settings);
  return `{"success":true,"mode":${JSON.stringify(mode)},"data":{"settings":${committed.settings_json},"revision":"${committed.settings_revision}"}}`;
}
