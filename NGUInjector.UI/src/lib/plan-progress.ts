import { isObject, type JsonObject } from './api';
import { gearGoals, stageFocuses } from './options';
import { stageLabel } from './stage-presets';

export type PlanProgressStatus = 'running' | 'paused' | 'interrupted' | 'disabled' | 'unavailable';
export type PlanStageState = 'completed' | 'current' | 'future' | 'unknown';

export interface PlanProgressStage {
  index: number;
  name: string;
  durationSeconds: number | null;
  completionMode: string;
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
  schedulerMode: 'time' | 'sequential' | null;
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
  if (data.schedulerMode !== 'time' && data.schedulerMode !== 'sequential') return waiting;
  const active = index(data.activeStageIndex) && data.activeStageIndex < rows.length ? data.activeStageIndex : null;
  if (status === 'running' && active === null) return waiting;
  if (data.activeStageIndex !== null && data.activeStageIndex !== -1 && active === null) return waiting;
  const loopStart = index(data.loopStartIndex) && data.loopStartIndex < rows.length ? data.loopStartIndex : null;
  if (data.repeating === true && loopStart === null) return waiting;
  const knownPosition = status !== 'disabled' && active !== null;
  const progress = seconds(data.stageProgress);
  return {
    showWaitingPlanProgress: false, status,
    planName: text(data.planName) || 'Active plan',
    planVersion: index(data.planVersion) ? String(data.planVersion) : text(data.planVersion),
    schedulerMode: data.schedulerMode,
    activeStageIndex: knownPosition ? active : null,
    stageProgress: knownPosition && progress !== null && progress <= 1 ? progress : null,
    elapsedSeconds: seconds(data.elapsedSeconds), stageElapsedSeconds: seconds(data.stageElapsedSeconds),
    cycle: index(data.cycle) ? data.cycle : null,
    repeating: data.repeating === true, loopStartIndex: loopStart,
    reason: text(data.reason),
    stages: (rows as JsonObject[]).map((row, i) => {
      const atTargets = row.completionMode === 1 || row.completionMode === 'advancedTraining';
      const targets = atTargets ? [
        ['P', row.targetAdvancedTrainingPower], ['T', row.targetAdvancedTrainingToughness]
      ].map(([label, value]) => {
        const target = String(value ?? '');
        return `${label} ${/^\d+$/.test(target) && (typeof value !== 'number' || Number.isSafeInteger(value)) ? target : '?'}`;
      }).join(' / ') : '';
      return {
        index: i,
        name: stageLabel({ EnergyFocus: row.energyFocus, MagicFocus: row.magicFocus, GearGoal: row.gearGoal }),
        durationSeconds: seconds(row.durationSeconds),
        completionMode: atTargets ? 'AT targets' : row.completionMode === 0 || row.completionMode === 'time' ? 'Time' : text(row.completionMode) || 'Completion not reported',
        energy: allocation(row.energyFocus, row.energyPriorities), magic: allocation(row.magicFocus, row.magicPriorities),
        gear: row.gearGoal === -1 ? 'Keep gear' : typeof row.gearGoal === 'number' ? gearGoals.find(option => option.value === row.gearGoal)?.label || 'Not reported' : text(row.gearGoal) || 'Not reported',
        targets,
        // Only the reported position determines completion. After a loop, the intro stays
        // completed while stages after the current position are future in THIS cycle.
        state: !knownPosition ? 'unknown' : i < active ? 'completed' : i === active ? 'current' : 'future',
        loopStart: data.repeating === true && i === loopStart
      };
    })
  };
}

export function planProgressDuration(value: number | null): string {
  if (value === null || !Number.isFinite(value) || value < 0) return 'Duration unknown';
  const total = Math.floor(value);
  return [Math.floor(total / 3600) ? `${Math.floor(total / 3600)}h` : '', Math.floor(total % 3600 / 60) ? `${Math.floor(total % 3600 / 60)}m` : '', total % 60 ? `${total % 60}s` : ''].filter(Boolean).join(' ') || '0s';
}
