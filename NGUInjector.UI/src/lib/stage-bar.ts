import { isObject, planRevisionField, type JsonObject } from './api';
import { longTarget, numeric, stages, validatePriorities } from './model';
import { parsePlanProgress, type PlanProgressView } from './plan-progress';
import { scheduleTiming } from './stage-duration';

/** Matches runtime stage positions to the editor's ordered stages, not the whole config.
 * connected must be the caller's fresh heartbeat gate. No editor selection or clock
 * is used to infer a live position, including when a revision is not reported. */
export function matchingPlanProgress(plan: JsonObject, name: string, data: JsonObject, connected: boolean, dirty: boolean): PlanProgressView | null {
  const view = parsePlanProgress(data, connected);
  if (dirty || view.showWaitingPlanProgress || !isObject(plan)) return null;
  if (!name || typeof data.planName !== 'string' || data.planName.toLowerCase() !== name.toLowerCase()) return null;
  if (typeof plan.Version !== 'number' || ![14, 15, 16].includes(plan.Version) || view.planVersion !== String(plan.Version)) return null;
  if (Object.prototype.hasOwnProperty.call(data, 'planRevision') &&
      (typeof data.planRevision !== 'string' || !data.planRevision || data.planRevision !== plan[planRevisionField])) return null;

  try {
    const rows = stages(plan);
    if (rows.length !== view.stages.length) return null;
    const loop = numeric(plan.StageLoopStartIndex, 'StageLoopStartIndex', -1, rows.length - 1);
    if (data.repeating !== (loop >= 0) || data.loopStartIndex !== loop) return null;
    const seconds = scheduleTiming(plan).seconds;
    if (view.schedulerMode !== (rows.some(row => row.CompletionMode === 1) ? 'sequential' : 'time')) return null;

    const matches = rows.every((row, i) => {
      const runtime = (data.stages as JsonObject[])[i];
      const mode = numeric(row.CompletionMode, 'CompletionMode', 0, 1);
      if (runtime.completionMode !== mode && runtime.completionMode !== (mode === 1 ? 'advancedTraining' : 'time')) return false;
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
      // AT stages have no timed duration in the editor; telemetry reports zero.
      if (runtime.durationSeconds !== (seconds[i] ?? 0)) return false;
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
