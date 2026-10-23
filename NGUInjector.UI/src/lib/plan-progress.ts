import { isObject, type JsonObject } from './api';
import { gearGoals, stageFocuses } from './options';
import { stageLabel } from './stage-presets';

export type PlanProgressStatus = 'running' | 'paused' | 'interrupted' | 'disabled' | 'unavailable';
export type PlanStageState = 'completed' | 'skipped' | 'current' | 'pending' | 'unknown';

export interface PlanProgressStage {
  index: number;
  name: string;
  durationSeconds: number | null;
  configuredDurationSeconds: number | null;
  flexWeight: number | null;
  timingKind: 'fixed' | 'flexible' | 'target' | 'elapsed' | null;
  appliedSeconds: number | null;
  elapsedSeconds: number | null;
  visits: number | null;
  completionMode: string;
  automatic: boolean;
  presetBlocker: string;
  energy: string;
  magic: string;
  gear: string;
  targets: string;
  state: PlanStageState;
  loopStart: boolean;
}

export interface PlanProgressView {
  showWaitingPlanProgress: boolean;
  status: PlanProgressStatus;
  planName: string;
  planVersion: string;
  schedulerMode: 'stages' | null;
  stageEntry: string;
  lastStageOverride: string;
  lastStageOverrideAt: number | null;
  protectedWorkComplete: boolean | null;
  protectedSecondsRemaining: number | null;
  timingReason: string;
  scheduleComplete: boolean | null;
  activeStageIndex: number | null;
  stageProgress: number | null;
  elapsedSeconds: number | null;
  stageElapsedSeconds: number | null;
  cycle: number | null;
  repeating: boolean;
  loopStartIndex: number | null;
  stages: PlanProgressStage[];
  reason: string;
}

const text = (value: unknown): string => typeof value === 'string' ? value.trim() : '';
const seconds = (value: unknown): number | null => typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : null;
const index = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;

function allocation(focus: unknown, priorities: unknown): string {
  const label = typeof focus === 'number' ? stageFocuses.find(option => option.value === focus)?.label : text(focus);
  const tokens = Array.isArray(priorities) ? priorities.filter((token): token is string => typeof token === 'string' && !!token.trim()).map(token => token.trim()) : [];
  return [label, tokens.join(' > ')].filter(Boolean).join(' / ') || 'Not reported';
}

/** Pure runtime-only projection. connected MUST be the caller's fresh heartbeat gate,
 * not merely a transport connection. Never pass an editor draft as data. */
export function parsePlanProgress(data: JsonObject, connected: boolean): PlanProgressView {
  const waiting: PlanProgressView = {
    showWaitingPlanProgress: true, status: 'unavailable', planName: '', planVersion: '', schedulerMode: null,
    stageEntry: '', lastStageOverride: '', lastStageOverrideAt: null,
    protectedWorkComplete: null, protectedSecondsRemaining: null, timingReason: '', scheduleComplete: null,
    activeStageIndex: null, stageProgress: null, elapsedSeconds: null, stageElapsedSeconds: null,
    cycle: null, repeating: false, loopStartIndex: null, stages: [],
    reason: connected ? 'Waiting for runtime plan progression.' : 'Connect to see fresh runtime plan progression.'
  };
  if (!connected || !isObject(data)) return waiting;
  const status = data.status;
  if (status !== 'running' && status !== 'paused' && status !== 'interrupted' && status !== 'disabled' && status !== 'unavailable') return waiting;
  if (data.available !== true || status === 'unavailable') return { ...waiting, status: status === 'disabled' ? 'disabled' : 'unavailable', reason: text(data.reason) || waiting.reason };
  const rows = data.stages;
  // A partial/malformed stage list must not silently renumber editor navigation or the current marker.
  if (!Array.isArray(rows) || !rows.length || rows.some((row, i) => !isObject(row) || row.index !== i)) return waiting;
  if ((data.planVersion !== 17 && data.planVersion !== '17') || data.schedulerMode !== 'stages') return waiting;
  const scheduleComplete = typeof data.scheduleComplete === 'boolean' ? data.scheduleComplete : null;
  const active = index(data.activeStageIndex) && data.activeStageIndex < rows.length ? data.activeStageIndex : null;
  if (status === 'running' && active === null && scheduleComplete !== true) return waiting;
  if (data.activeStageIndex !== null && data.activeStageIndex !== -1 && active === null) return waiting;
  const loopStart = index(data.loopStartIndex) && data.loopStartIndex < rows.length ? data.loopStartIndex : null;
  if (data.repeating === true && loopStart === null) return waiting;
  const activeState = active === null ? undefined : (rows[active] as JsonObject).state;
  const knownPosition = status !== 'disabled' && active !== null && (activeState === 'current' || scheduleComplete === true && (activeState === 'completed' || activeState === 'skipped'));
  const progress = seconds(data.stageProgress);
  return {
    showWaitingPlanProgress: false, status,
    planName: text(data.planName) || 'Active plan',
    planVersion: index(data.planVersion) ? String(data.planVersion) : text(data.planVersion),
    schedulerMode: data.schedulerMode,
    stageEntry: typeof data.stageEntry === 'string' && data.stageEntry.trim() ? data.stageEntry : '',
    lastStageOverride: text(data.lastStageOverride),
    lastStageOverrideAt: seconds(data.lastStageOverrideAt),
    protectedWorkComplete: typeof data.protectedWorkComplete === 'boolean' ? data.protectedWorkComplete : null,
    protectedSecondsRemaining: seconds(data.protectedSecondsRemaining),
    timingReason: text(data.timingReason),
    scheduleComplete,
    activeStageIndex: knownPosition ? active : null,
    stageProgress: knownPosition && progress !== null && progress <= 1 ? progress : null,
    elapsedSeconds: seconds(data.elapsedSeconds), stageElapsedSeconds: seconds(data.stageElapsedSeconds),
    cycle: index(data.cycle) ? data.cycle : null,
    repeating: data.repeating === true, loopStartIndex: loopStart,
    reason: text(data.reason),
    stages: (rows as JsonObject[]).map((row, i) => {
      const automatic = row.executionMode === 1;
      const weighted = ['presetId', 'resolvedPresetId', 'presetName'].some(key => row[key] !== undefined && row[key] !== '');
      const presetStatus = text(row.presetStatus);
      const presetReason = text(row.presetReason);
      const atTargets = row.completionMode === 1 || row.completionMode === 'advancedTraining';
      const targets = atTargets ? [
        ['P', row.targetAdvancedTrainingPower], ['T', row.targetAdvancedTrainingToughness]
      ].map(([label, value]) => {
        const target = String(value ?? '');
        return `${label} ${/^\d+$/.test(target) && (typeof value !== 'number' || Number.isSafeInteger(value)) ? target : '?'}`;
      }).join(' / ') : '';
      return {
        index: i,
        name: !automatic && weighted ? text(row.presetName) || 'Global preset (name unavailable)' : stageLabel({ ...(row.executionMode !== undefined ? { ExecutionMode: row.executionMode } : {}), EnergyFocus: row.energyFocus, MagicFocus: row.magicFocus, GearGoal: row.gearGoal }),
        automatic,
        presetBlocker: status === 'running' && knownPosition && i === active && !automatic && weighted && ['failed', 'blocked', 'waiting'].includes(presetStatus)
          ? `Preset ${presetStatus}${presetReason ? `: ${presetReason}` : '.'}` : '',
        durationSeconds: seconds(row.durationSeconds),
        configuredDurationSeconds: seconds(row.configuredDurationSeconds),
        flexWeight: seconds(row.flexWeight),
        timingKind: row.timingKind === 'fixed' || row.timingKind === 'flexible' || row.timingKind === 'target' || automatic && row.timingKind === 'elapsed' ? row.timingKind : null,
        appliedSeconds: seconds(row.appliedSeconds),
        elapsedSeconds: seconds(row.elapsedSeconds),
        visits: index(row.visits) ? row.visits : null,
        completionMode: automatic ? 'Total elapsed budget' : atTargets ? 'AT targets' : row.completionMode === 0 || row.completionMode === 'time' ? 'Time' : text(row.completionMode) || 'Completion not reported',
        energy: automatic ? 'Automatic AT / augments / Wandoos' : weighted ? 'Global weighted preset' : allocation(row.energyFocus, row.energyPriorities), magic: automatic ? 'Automatic Wandoos' : weighted ? 'Global weighted preset' : allocation(row.magicFocus, row.magicPriorities),
        gear: automatic ? 'Automatic phase gear' : weighted ? 'Global weighted preset' : row.gearGoal === -1 ? 'Keep gear' : typeof row.gearGoal === 'number' ? gearGoals.find(option => option.value === row.gearGoal)?.label || 'Not reported' : text(row.gearGoal) || 'Not reported',
        targets,
        // Overrides can move in either direction. Position is never proof of training.
        state: status === 'disabled' ? 'unknown' : row.state === 'current' ? knownPosition && i === active ? 'current' : 'unknown' : row.state === 'completed' || row.state === 'skipped' || row.state === 'pending' ? row.state : 'unknown',
        loopStart: data.repeating === true && i === loopStart
      };
    })
  };
}

export function stageHistoryDetails(stage: PlanProgressStage | null, kind = stage?.timingKind): string {
  const duration = (value: number | null | undefined) => value == null ? 'Not recorded' : planProgressDuration(value);
  const state = stage?.state && stage.state !== 'unknown' ? stage.state[0].toUpperCase() + stage.state.slice(1) : 'Not recorded';
  return [
    stage?.automatic || kind === 'elapsed' ? 'Total elapsed budget (includes AT, planning and setup)' : kind === 'fixed' ? 'Fixed duration' : kind === 'flexible' ? 'Flexible share' : kind === 'target' ? 'AT targets / Mandatory' : 'Timing: Not recorded',
    `Actual applied: ${duration(stage?.appliedSeconds)}`,
    `Elapsed selected (including offline): ${duration(stage?.elapsedSeconds)}`,
    `Visits: ${stage?.visits ?? 'Not recorded'}`,
    `State: ${state}`
  ].join('\n');
}

export function planProgressDuration(value: number | null): string {
  if (value === null || !Number.isFinite(value) || value < 0) return 'Duration unknown';
  const total = Math.floor(value);
  return [Math.floor(total / 3600) ? `${Math.floor(total / 3600)}h` : '', Math.floor(total % 3600 / 60) ? `${Math.floor(total % 3600 / 60)}m` : '', total % 60 ? `${total % 60}s` : ''].filter(Boolean).join(' ') || '0s';
}
