import { isObject, planRevisionField, type JsonObject } from './api';
import { longTarget, numeric, stages, validatePriorities } from './model';
import { parsePlanProgress, planProgressDuration, type PlanProgressView } from './plan-progress';
import { fixedSeconds, scheduleTiming } from './stage-duration';

/** Matches runtime stage positions to the editor's ordered stages, not the whole config.
 * connected must be the caller's fresh heartbeat gate. No editor selection or clock
 * is used to infer a live position, including when a revision is not reported. */
export function matchingPlanProgress(plan: JsonObject, name: string, data: JsonObject, connected: boolean, dirty: boolean): PlanProgressView | null {
  const view = parsePlanProgress(data, connected);
  if (dirty || view.showWaitingPlanProgress || !isObject(plan)) return null;
  if (!name || typeof data.planName !== 'string' || data.planName.toLowerCase() !== name.toLowerCase()) return null;
  if (plan.Version !== 18 || view.planVersion !== '18') return null;
  if (Object.prototype.hasOwnProperty.call(data, 'planRevision') &&
      (typeof data.planRevision !== 'string' || !data.planRevision || data.planRevision !== plan[planRevisionField])) return null;

  try {
    const rows = stages(plan);
    if (rows.length !== view.stages.length) return null;
    const loop = numeric(plan.StageLoopStartIndex, 'StageLoopStartIndex', -1, rows.length - 1);
    if (data.repeating !== (loop >= 0) || data.loopStartIndex !== loop) return null;
    scheduleTiming(plan);
    if (view.schedulerMode !== 'stages') return null;
    const hasActivePreset = Object.prototype.hasOwnProperty.call(data, 'activePresetId');
    const activePreset = plan.ActivePresetId === undefined ? '' : plan.ActivePresetId;
    if (hasActivePreset && (typeof activePreset !== 'string' || activePreset !== '' && !/^[A-Za-z0-9_-]{1,64}$/.test(activePreset) || typeof data.activePresetId !== 'string' || data.activePresetId !== activePreset)) return null;

    const matches = rows.every((row, i) => {
      const runtime = (data.stages as JsonObject[])[i];
      const execution = numeric(row.ExecutionMode === undefined ? 0 : row.ExecutionMode, 'ExecutionMode', 0, 1);
      if ((runtime.executionMode === undefined ? 0 : runtime.executionMode) !== execution) return false;
      const adventureWeight = numeric(row.AdventureDynamicWeight === undefined ? 0 : row.AdventureDynamicWeight, 'AdventureDynamicWeight', 0, 1e6, false);
      if (numeric(runtime.adventureDynamicWeight === undefined ? 0 : runtime.adventureDynamicWeight, 'adventureDynamicWeight', 0, 1e6, false) !== adventureWeight) return false;
      const mode = numeric(row.CompletionMode, 'CompletionMode', 0, 1);
      if (runtime.completionMode !== mode && runtime.completionMode !== (mode === 1 ? 'advancedTraining' : 'time')) return false;
      const presetId = row.PresetId === undefined ? '' : row.PresetId;
      if (typeof presetId !== 'string' || presetId !== '' && presetId !== '@active' && !/^[A-Za-z0-9_-]{1,64}$/.test(presetId)) return false;
      const hasBinding = ['presetId', 'resolvedPresetId', 'presetName'].some(key => Object.prototype.hasOwnProperty.call(runtime, key));
      if (presetId || hasBinding) {
        const resolved = presetId === '@active' ? activePreset : presetId;
        if (runtime.presetId !== presetId || typeof runtime.resolvedPresetId !== 'string' || runtime.resolvedPresetId !== resolved || typeof runtime.presetName !== 'string') return false;
        if (presetId && (!hasActivePreset || typeof resolved !== 'string' || !/^[A-Za-z0-9_-]{1,64}$/.test(resolved) || !runtime.presetName.trim())) return false;
        if (!presetId && runtime.presetName !== '') return false;
      }
      // Weighted execution ignores the retained legacy tuple and priority fields.
      if (!presetId) {
        for (const [key, reported, min, max] of [
          ['EnergyFocus', 'energyFocus', 0, 7], ['MagicFocus', 'magicFocus', 0, 7], ['GearGoal', 'gearGoal', -1, 13]
        ] as const) {
          if (numeric(row[key], key, min, max) !== runtime[reported]) return false;
        }
        for (const [key, reported, energy] of [
          ['EnergyPriorities', 'configuredEnergyPriorities', true], ['MagicPriorities', 'configuredMagicPriorities', false]
        ] as const) {
          const configured = row[key];
          const actual = runtime[reported];
          if (!Array.isArray(configured) || !Array.isArray(actual)) return false;
          validatePriorities(configured, energy, key);
          if (configured.length !== actual.length || configured.some((token, j) => token !== actual[j])) return false;
        }
      }
      const configured = fixedSeconds(row);
      if (runtime.configuredDurationSeconds !== configured || runtime.flexWeight !== numeric(row.Percent ?? 100, 'Percent', Number.MIN_VALUE, Number.MAX_VALUE, false)) return false;
      if (runtime.timingKind !== (execution === 1 ? 'elapsed' : mode === 1 ? 'target' : configured > 0 ? 'fixed' : 'flexible')) return false;
      // Assigned durations shrink dynamically; compare the configured timing instead.
      numeric(runtime.durationSeconds, 'durationSeconds', 0);
      if (mode === 1 && runtime.durationSeconds !== 0) return false;
      if (mode === 1) {
        for (const [key, reported] of [
          ['TargetAdvancedTrainingPower', 'targetAdvancedTrainingPower'],
          ['TargetAdvancedTrainingToughness', 'targetAdvancedTrainingToughness']
        ] as const) {
          if (BigInt(longTarget(row[key], key, true)) !== BigInt(longTarget(runtime[reported], reported, true))) return false;
        }
      }
      return true;
    });
    return matches ? view : null;
  } catch {
    return null;
  }
}

export function matchingFightStatus(plan: JsonObject, data: JsonObject, mapped: PlanProgressView | null): string {
  if (!mapped || mapped.status === 'disabled' || mapped.activeStageIndex === null || !mapped.stageEntry) return '';
  const row = Array.isArray(plan.Stages) ? plan.Stages[mapped.activeStageIndex] : null;
  const schedule = isObject(row) ? row.FightSchedule : null;
  const fight = data.fightSchedule;
  if (!isObject(schedule) || typeof schedule.Id !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(schedule.Id) || !isObject(fight) || fight.stageEntry !== mapped.stageEntry || fight.scheduleId !== schedule.Id || typeof fight.reason !== 'string') return '';
  for (const key of ['nextFightAtSeconds', 'remainingSeconds']) {
    if (fight[key] !== null && (typeof fight[key] !== 'number' || !Number.isFinite(fight[key]) || fight[key] < 0)) return '';
  }
  switch (fight.state) {
    case 'waiting-anchor': return `Fight waiting${fight.reason ? `: ${fight.reason}` : ''}`;
    case 'waiting': return `Fight waiting${fight.reason ? `: ${fight.reason}` : ''}`;
    case 'scheduled': return typeof fight.remainingSeconds === 'number' ? `Next fight in ${planProgressDuration(fight.remainingSeconds)}` : '';
    case 'fighting': return 'Fighting';
    case 'completed': return 'Fights completed';
    case 'disabled': return 'Fights disabled';
    default: return '';
  }
}
