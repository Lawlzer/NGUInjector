import { describe, expect, it } from 'vitest';
import { render } from 'svelte/server';
import { stringifyJson, type JsonObject } from '../src/lib/api';
import NguAllocation from '../src/lib/NguAllocation.svelte';
import { energyNguNames, magicNguNames } from '../src/lib/options';

const energyWeights = [0, 0, 0, 0, 2, 0, 1, 0, 0];
const magicWeights = [1, 0, 0, 0, 0, 0, 0];
const target = (index: number, name: string, weight: number) => ({ index, name, weight, allocated: weight ? '250' : '0', bufferedCap: '2000', belowBufferedCap: true });
const data: JsonObject = {
  available: true, strategy: 'weighted-ngu-buffered-cap', capBasis: 'last-allocation-pass',
  energyWeights, magicWeights, energyAppliedWeights: energyWeights, magicAppliedWeights: magicWeights,
  energyWeightsApplied: true, magicWeightsApplied: true,
  energyTargets: energyNguNames.map((name, index) => target(index, name, energyWeights[index])),
  magicTargets: magicNguNames.map((name, index) => target(index, name, magicWeights[index]))
};
function display(patch: JsonObject = {}, connected = true) {
  return render(NguAllocation, { props: { data: { ...data, ...patch }, connected } }).body;
}
function visible(body: string) {
  return body.replace(/<!--[\s\S]*?-->/g, '').replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ');
}

describe('read-only weighted NGU telemetry', () => {
  it('shows positive selected weights without requiring legacy goal fields or offering writes', () => {
    const body = display();
    expect(visible(body)).toContain('Adventure Alpha w 2 250');
    expect(visible(body)).toContain('Drop Chance w 1 250');
    expect(visible(body)).toContain('Yggdrasil w 1 250');
    expect(visible(body)).not.toContain('Augments');
    expect(body).not.toContain('Selected weights applied.');
    expect(visible(body)).not.toContain('below buffered cap');
    expect(body).toMatch(/<strong\b[^>]*title="Below buffered cap at last update\."[^>]*>250<\/strong>/);
    expect(visible(body)).toContain('ENERGY');
    expect(visible(body)).toContain('MAGIC');
    expect(visible(body)).not.toContain('Read-only allocation telemetry');
    expect(body).not.toMatch(/<select|<input|<button|aria-busy|Pawgs|AdventureDrop/);
  });

  it.each([
    ['available', {}, true],
    ['disconnected', {}, false],
    ['unavailable', { available: false }, true]
  ] as const)('omits redundant plan context and raw allocation disclosure when %s', (_state, patch, connected) => {
    const body = display(patch, connected);
    expect(body).not.toContain('Active plan:');
    expect(body).not.toContain('Allocation details');
    expect(body).not.toMatch(/<details\b|<summary\b|<pre\b/);
    expect(body).not.toContain('"energyTargets"');
  });

  it('shows actual funded rows even when disabled in desired weights, distinguishing unapplied weights', () => {
    const rows = (data.energyTargets as JsonObject[]).map((row, index) => index === 0 ? { ...row, allocated: '1000' } : row);
    const body = display({ energyTargets: rows, energyWeightsApplied: false, energyAppliedWeights: [3, 0, 0, 0, 0, 0, 0, 0, 0] });
    expect(visible(body)).toContain('Weights not applied');
    expect(body).toContain('title="Actual allocations may reflect previous weights or other priorities."');
    expect(visible(body)).toContain('Augments w 0 / applied 3 1K');
    expect(visible(body)).toContain('Adventure Alpha w 2 / applied 0 250');
  });

  it('does not imply desired weights were applied when the application proof is missing', () => {
    const body = display({ energyWeightsApplied: null, energyAppliedWeights: null });
    expect(visible(body)).toContain('Weight application unknown');
    expect(visible(body)).toContain('w 2 / applied unknown');
  });

  it('keeps exact large amounts in titles without mutating telemetry', () => {
    const allocated = '009223372036854775805';
    const cap = '9223372036854775806';
    const rows = [{ ...target(4, 'Adventure Alpha', 2), allocated, bufferedCap: cap }];
    const before = stringifyJson(rows);
    const body = display({ energyTargets: rows });
    expect(body).toContain(`title="Adventure Alpha: allocated ${allocated}; buffered cap ${cap}"`);
    expect(visible(body)).toContain('9,223,372.04T');
    expect(stringifyJson(rows)).toBe(before);
  });

  it('does not invent amounts or caps from malformed rows', () => {
    const body = display({ energyTargets: [null, { index: -1, name: 'Invalid', allocated: '10' }, { index: 4, name: 'Adventure Alpha', weight: 2, allocated: 9007199254740992, bufferedCap: 'bad', belowBufferedCap: true }], magicTargets: [] });
    expect(visible(body)).toContain('Adventure Alpha w 2 unknown');
    expect(body).toContain('allocated unknown; buffered cap unknown');
    expect(visible(body)).not.toContain('Invalid');
    expect(body).not.toContain('title="Below buffered cap');
    expect(visible(body)).toContain('No targets reported.');
  });

  it.each([null, 'future-strategy'])('does not explain caps under an unknown strategy %s', strategy => {
    expect(display({ strategy })).not.toContain('title="Below buffered cap');
  });

  it('does not label a cap from an unknown allocation basis', () => {
    expect(display({ capBasis: 'unknown' })).not.toContain('title="Below buffered cap');
  });

  it('hides stale data while disconnected or explicitly unavailable', () => {
    expect(visible(display({}, false))).toContain('Connect to see NGU allocation.');
    expect(visible(display({}, false))).not.toContain('Adventure Alpha');
    expect(visible(display({ available: false, reason: 'NGU is locked.' }))).toContain('NGU is locked.');
    expect(visible(display({ available: false }))).not.toContain('Adventure Alpha');
  });
});
