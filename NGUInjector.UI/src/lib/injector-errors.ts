import { isObject, stringifyJson, type JsonObject, type JsonValue } from './api';
import type { UiState } from './controller';
import { object } from './model';
import { commandLabels } from './options';

export interface InjectorError {
  id: string;
  context: string;
  message: string;
  details: string;
  count?: number;
}

const text = (value: unknown): string => typeof value === 'string' ? value.trim() : '';
const failed = (value: unknown): boolean => ['failed', 'error', 'blocked', 'needsuser'].includes(text(value).toLowerCase());

// Read only explicit faults. Waiting, cooldowns, unverified observations and historical
// blockedDomain metadata alone do not establish an execution failure.
export function injectorErrors(state: Pick<UiState, 'snapshot' | 'error' | 'saveError' | 'pollError' | 'commandErrors' | 'lastResult'>): InjectorError[] {
  const errors: InjectorError[] = [];
  const add = (id: string, context: string, reason: unknown, data: JsonValue | undefined, count?: unknown) => {
    const message = text(reason).split(/\r?\n/)[0];
    if (!message || errors.some(error => error.context === context && error.message === message)) return;
    errors.push({ id, context, message, details: typeof data === 'string' ? data : stringifyJson(data ?? {}, 2),
      ...(typeof count === 'number' && Number.isSafeInteger(count) && count > 1 ? { count } : {}) });
  };
  const runtime = state.snapshot?.runtime ?? {};
  if (text(runtime.startupError) || runtime.status === 'failed') add('startup', 'Injector startup', runtime.startupError || 'Injector startup failed.', runtime.startupError);
  if (Array.isArray(runtime.automationErrors)) runtime.automationErrors.forEach((entry, index) => {
    if (isObject(entry)) add(`automation:${text(entry.context)}:${text(entry.message) || index}`, text(entry.context) || 'Automation', entry.message, entry, entry.count);
  });

  const resources = object(runtime.resourceAllocation);
  if (failed(resources.state)) add('resources', ['Resource allocation', text(resources.owner), text(resources.blockedDomain)].filter(Boolean).join(' / '), resources.reason || 'Resource allocation failed.', resources);
  const execution = object(runtime.execution);
  for (const [domain, value] of Object.entries(object(execution.domains))) {
    const row = object(value);
    if (failed(row.state)) add(`domain:${domain}`, domain, row.reason || row.waitingReason || `${domain} execution failed.`, row, row.count);
  }
  const support = object(execution.support);
  if (support.desiredFeasible === false) add('support', 'Support', support.reason || 'Selected support is not feasible.', support);
  const review = object(runtime.executionReview);
  if (review.reviewRequired === true) add('review', 'Selection review', review.reason || 'Review the acquired selection before resuming.', review);

  const progress = object(runtime.planProgress);
  const liveSettings = object(runtime.settings);
  const planRunning = (liveSettings.useCustomRebirthPlan ?? state.snapshot?.settings._useCustomRebirthPlan) === true;
  const planContext = ['Plan', text(progress.planName) || text(liveSettings.customPlanFile)].filter(Boolean).join(' / ');
  if (failed(progress.status) || planRunning && runtime.status === 'running' && progress.available === false) add('plan', planContext, progress.reason || 'Plan execution is unavailable.', progress);
  if (progress.cleanupRecoveryRequired === true) add('plan-recovery', planContext, progress.cleanupRecoveryReason || progress.reason || 'Plan recovery is blocked.', progress);
  if (Array.isArray(progress.stages) && planRunning && progress.status === 'running') progress.stages.forEach((value, index) => {
    const stage = object(value);
    if (index === progress.activeStageIndex && failed(stage.presetStatus)) add('stage', `${planContext} / ${text(stage.name) || `Stage ${index + 1}`}`, stage.presetReason || 'Stage preset execution is blocked.', stage);
  });
  const combat = object(runtime.combatPrep);
  if (combat.cleanupRecoveryRequired === true || failed(combat.status) || (combat.active === true || combat.cleanupPending === true) && text(combat.blockedReason)) {
    add('combat-prep', 'Combat prep', combat.cleanupRecoveryReason || combat.blockedReason || combat.reason || 'Combat prep failed.', combat);
  }
  if (runtime.focusStatus === 'failed') add('focus', ['Quick', text(runtime.activeFocusGoal)].filter(Boolean).join(' / '), runtime.customStatus || 'Quick execution failed.', { focusStatus: runtime.focusStatus, customStatus: runtime.customStatus ?? '' });
  for (const [key, context] of [['bloodGoals', 'Blood Magic'], ['ironPill', 'Iron Pill']] as const) {
    const row = object(runtime[key]);
    if (row.status === 'error') add(key, context, row.reason || `${context} failed.`, row);
  }

  // Command receipts are history, not proof that a currently active fault recovered.
  const results: JsonObject[] = [...state.commandErrors];
  if (state.lastResult?.success === false) results.push(state.lastResult);
  for (const result of state.snapshot?.command_results ?? []) if (result.success === false) results.push({ ...result });
  for (const result of results) {
    const command = text(result.command);
    add(`command:${text(result.id) || command}`, `Command failed: ${commandLabels[command] ?? (command || 'Action')}`, result.message || 'Command failed.', result);
  }
  if (state.saveError) add('save', 'Changes not saved', state.saveError, state.saveError);
  if (state.pollError) add('poll', 'Refresh failed', state.pollError, state.pollError);
  if (state.error && !errors.some(error => error.message === state.error.split(/\r?\n/)[0] || error.details === state.error)) add('ui', 'Action failed', state.error, state.error);
  return errors;
}
