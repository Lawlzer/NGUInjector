import { LosslessNumber } from 'lossless-json';
import { isObject, type JsonObject } from './api';
import type { PlanProgressView } from './plan-progress';
import { planProgressDuration } from './plan-progress';

export const activityKinds = ['AT', 'Augments', 'Wandoos', 'NGU', 'Background'] as const;
export interface ActivityTime { kind: typeof activityKinds[number]; elapsedSeconds: number; active: boolean }
export interface ActivityTimingView { rows: ActivityTime[]; current: ActivityTime | null }

export function parseActivityTiming(runtime: JsonObject): ActivityTimingView | null {
  if (runtime.schemaVersion !== 1 || runtime.activityTimingScope !== 'run' ||
      runtime.activityTimingSemantics !== 'exclusive-phase-wall-time-including-setup' ||
      typeof runtime.runId !== 'string' || !runtime.runId.trim() ||
      !Array.isArray(runtime.activityTimes) || runtime.activityTimes.length !== activityKinds.length) return null;
  const rows: ActivityTime[] = [];
  let activeCount = 0;
  const running = runtime.active === true && runtime.status === 'running' && runtime.cleanupPending !== true && runtime.cleanupRecoveryRequired !== true;
  for (const [index, value] of runtime.activityTimes.entries()) {
    if (!isObject(value) || value.kind !== activityKinds[index] || typeof value.active !== 'boolean') return null;
    const seconds = value.elapsedSeconds instanceof LosslessNumber ? Number(value.elapsedSeconds.value) : value.elapsedSeconds;
    if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) return null;
    if (value.active && ++activeCount > 1) return null;
    rows.push({ kind: activityKinds[index], elapsedSeconds: seconds, active: running && value.active });
  }
  return { rows, current: rows.find(row => row.active) ?? null };
}

// The caller must supply matchingPlanProgress's result, never an ungated runtime view.
export function matchingStageActivityTiming(runtime: JsonObject, mapped: PlanProgressView | null): ActivityTimingView | null {
  if (!mapped || mapped.activeStageIndex === null || !mapped.stageEntry ||
      !mapped.stages[mapped.activeStageIndex]?.automatic || mapped.stages[mapped.activeStageIndex].state !== 'current' ||
      runtime.owner !== 'scheduled' || typeof runtime.planName !== 'string' || runtime.planName.toLowerCase() !== mapped.planName.toLowerCase() ||
      runtime.stageIndex !== mapped.activeStageIndex || runtime.stageEntry !== mapped.stageEntry || runtime.cleanupRecoveryRequired === true) return null;
  const timing = parseActivityTiming(runtime);
  return timing && mapped.status !== 'running'
    ? { rows: timing.rows.map(row => ({ ...row, active: false })), current: null } : timing;
}

export const activityDuration = (seconds: number): string => seconds > 0 && seconds < 1 ? '<1s' : planProgressDuration(seconds);
