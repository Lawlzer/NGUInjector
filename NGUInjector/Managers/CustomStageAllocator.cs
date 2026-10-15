using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NGUInjector.CustomPlans;

namespace NGUInjector.Managers
{
    internal class CustomStageRuntime
    {
        internal CustomPlanStage Stage { get; set; }
        internal int StageIndex { get; set; }
        internal int StageStartSeconds { get; set; }
        internal int StageEndSeconds { get; set; }
        internal int RemainingSeconds { get; set; }
        internal string Message { get; set; }
        internal double StageElapsedSeconds { get; set; }
        internal int EffectiveDurationSeconds { get; set; }
    }

    public sealed class CustomPlanSchedulerSnapshot
    {
        public string SchedulerMode { get; private set; }
        public int ActiveStageIndex { get; private set; }
        public double? StageProgress { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public double StageElapsedSeconds { get; private set; }
        public double Cycle { get; private set; }
        public System.Collections.ObjectModel.ReadOnlyCollection<int> DurationSeconds { get; private set; }

        internal CustomPlanSchedulerSnapshot(string mode, int index, double? progress, double elapsed, double stageElapsed, double cycle, int[] durations)
        {
            SchedulerMode = mode;
            ActiveStageIndex = index;
            StageProgress = progress;
            ElapsedSeconds = elapsed;
            StageElapsedSeconds = stageElapsed;
            Cycle = cycle;
            DurationSeconds = Array.AsReadOnly(durations);
        }
    }

    public static class CustomStageAllocator
    {
        private static string _augmentTargetContextKey;
        private static int _augmentTargetIndex = -1;
        private static bool _augmentTargetUsesUpgrade;
        private static string _bloodTargetContextKey;
        private static int _bloodTargetRitualIndex = -1;
        private static string _sequentialPlanKey;
        private static int _sequentialStageIndex;
        private static double _sequentialStageStartedAt;
        private static double _sequentialLastElapsed = -1d;
        private static double _sequentialCycle;

        internal static readonly Dictionary<int, string> FocusLabels = new Dictionary<int, string>
        {
            {(int) CustomStageFocus.None, "None"},
            {(int) CustomStageFocus.TimeMachine, "TimeMachine"},
            {(int) CustomStageFocus.Augments, "Augments"},
            {(int) CustomStageFocus.Wandoos, "Wandoos"},
            {(int) CustomStageFocus.BloodMagic, "BloodMagic"},
            {(int) CustomStageFocus.EnergyNgu, "EnergyNGU"},
            {(int) CustomStageFocus.MagicNgu, "MagicNGU"},
            {(int) CustomStageFocus.AdvancedTraining, "AdvancedTraining"}
        };

        internal static CustomStageRuntime GetActiveStage(CustomPlan plan)
        {
            if (plan == null || !plan.StageSchedulerEnabled || plan.Stages == null || plan.Stages.Length == 0)
                return null;

            EffectiveStageDurations(plan);
            if (UsesSequentialStageRuntime(plan))
                return GetSequentialActiveStage(plan);

            if (_sequentialPlanKey != null) ResetStageRuntime();
            return GetTimeWindowActiveStage(plan);
        }

        private static CustomStageRuntime GetTimeWindowActiveStage(CustomPlan plan)
        {
            if (plan == null || plan.Stages == null || plan.Stages.Length == 0)
                return null;

            var durations = EffectiveStageDurations(plan);
            var elapsed = SafeElapsed(Main.Character.rebirthTime.totalseconds);
            var effectiveElapsed = GetEffectiveElapsed(plan, elapsed, durations);
            var cumulative = 0;

            for (var i = 0; i < plan.Stages.Length; i++)
            {
                var stage = plan.Stages[i];
                var startSeconds = cumulative;
                cumulative += durations[i];
                if (effectiveElapsed >= cumulative && i < plan.Stages.Length - 1)
                    continue;

                var endSeconds = cumulative;
                return new CustomStageRuntime
                {
                    Stage = stage,
                    StageIndex = i,
                    StageStartSeconds = startSeconds,
                    StageEndSeconds = endSeconds,
                    RemainingSeconds = (int) Math.Max(0, endSeconds - Math.Floor(effectiveElapsed)),
                    StageElapsedSeconds = Math.Max(0, effectiveElapsed - startSeconds),
                    EffectiveDurationSeconds = durations[i],
                    Message = $"Stage {i + 1}/{plan.Stages.Length}: {stage.Name} ({FormatSeconds(startSeconds)}-{FormatSeconds(endSeconds)})"
                };
            }

            return null;
        }

        private static bool UsesSequentialStageRuntime(CustomPlan plan)
        {
            return plan != null
                   && plan.Stages != null
                   && plan.Stages.Any(stage => stage != null && stage.CompletionMode != (int) CustomStageCompletionMode.TimePercent);
        }

        private static CustomStageRuntime GetSequentialActiveStage(CustomPlan plan)
        {
            var elapsed = SafeElapsed(Main.Character.rebirthTime.totalseconds);
            var key = SequentialPlanKey(plan);
            if (_sequentialPlanKey != key || _sequentialLastElapsed >= 0d && elapsed + 5d < _sequentialLastElapsed)
            {
                _sequentialPlanKey = key;
                _sequentialStageIndex = 0;
                _sequentialStageStartedAt = elapsed;
                _sequentialCycle = 0;
            }

            _sequentialLastElapsed = elapsed;
            _sequentialStageIndex = Math.Max(0, Math.Min(_sequentialStageIndex, plan.Stages.Length - 1));
            var guard = 0;
            while (guard++ < plan.Stages.Length * 2)
            {
                var stage = plan.Stages[_sequentialStageIndex];
                if (!SequentialStageComplete(plan, stage, _sequentialStageIndex, elapsed))
                    break;

                if (_sequentialStageIndex >= plan.Stages.Length - 1)
                {
                    if (plan.StageLoopStartIndex >= 0 && plan.StageLoopStartIndex < plan.Stages.Length)
                    {
                        _sequentialStageIndex = plan.StageLoopStartIndex;
                        _sequentialCycle++;
                        _sequentialStageStartedAt = elapsed;
                        continue;
                    }

                    break;
                }

                _sequentialStageIndex++;
                _sequentialStageStartedAt = elapsed;
            }

            return BuildSequentialRuntime(plan, _sequentialStageIndex, elapsed);
        }

        private static bool SequentialStageComplete(CustomPlan plan, CustomPlanStage stage, int index, double elapsed)
        {
            if (stage == null)
                return true;

            if (stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats)
                return AdvancedTrainingStatsReached(stage);

            var isLast = index >= plan.Stages.Length - 1;
            if (isLast && plan.StageLoopStartIndex < 0)
                return false;

            return elapsed - _sequentialStageStartedAt >= SequentialTimeStageDurationSeconds(plan, stage);
        }

        private static CustomStageRuntime BuildSequentialRuntime(CustomPlan plan, int index, double elapsed)
        {
            var stage = plan.Stages[index];
            var startSeconds = (int) Math.Min(int.MaxValue, Math.Max(0d, Math.Floor(_sequentialStageStartedAt)));
            var durationSeconds = SequentialRuntimeDurationSeconds(plan, stage);
            var endSeconds = (int) Math.Min(int.MaxValue, (long) startSeconds + durationSeconds);
            var remainingSeconds = stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent
                ? (int) Math.Max(0, durationSeconds - Math.Floor(elapsed - _sequentialStageStartedAt))
                : Math.Max(1, durationSeconds);

            return new CustomStageRuntime
            {
                Stage = stage,
                StageIndex = index,
                StageStartSeconds = startSeconds,
                StageEndSeconds = endSeconds,
                RemainingSeconds = remainingSeconds,
                StageElapsedSeconds = Math.Max(0, elapsed - _sequentialStageStartedAt),
                EffectiveDurationSeconds = durationSeconds,
                Message = $"Step {index + 1}/{plan.Stages.Length}: {stage.Name} ({SequentialStageRuntimeLabel(stage, durationSeconds)})"
            };
        }

        private static int SequentialRuntimeDurationSeconds(CustomPlan plan, CustomPlanStage stage)
        {
            if (stage != null && stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent)
                return SequentialTimeStageDurationSeconds(plan, stage);

            return 0;
        }

        private static int SequentialTimeStageDurationSeconds(CustomPlan plan, CustomPlanStage stage)
        {
            return EffectiveStageDurations(plan)[Array.IndexOf(plan.Stages, stage)];
        }

        // All-percent sequential plans retain legacy rounding; mixed plans conserve the budget.
        internal static int[] EffectiveStageDurations(CustomPlan plan)
        {
            if (plan.Stages.Any(stage => stage == null || stage.Percent <= 0d || double.IsNaN(stage.Percent) || double.IsInfinity(stage.Percent)))
                throw new InvalidOperationException("Custom stage weights must be finite and positive.");
            var sequential = UsesSequentialStageRuntime(plan);
            var timedStages = plan.Stages.Where(stage => stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent).ToArray();
            var fixedTotal = timedStages.Sum(stage => (long) stage.DurationSeconds);
            if (plan.Stages.Any(stage => stage.DurationSeconds < 0) || fixedTotal > int.MaxValue)
                throw new InvalidOperationException("Fixed stage durations must be nonnegative and fit the Int32 schedule limit.");
            var allFixed = timedStages.Length > 0 && timedStages.All(stage => stage.DurationSeconds > 0);
            var budget = allFixed ? (int) fixedTotal : Math.Max(1, plan.StageDurationSeconds);
            var weighted = timedStages.Where(stage => stage.DurationSeconds == 0).ToArray();
            if (weighted.Length > 0 && fixedTotal >= budget)
                throw new InvalidOperationException("Fixed stage durations must be less than the budget when percent stages exist.");
            var weight = weighted.Length == 0 ? 1d : TotalStageWeight(weighted);
            var remaining = budget - (int) fixedTotal;
            var result = new int[plan.Stages.Length];
            var cumulativeWeight = 0d;
            var assigned = 0;
            var lastWeighted = Array.FindLastIndex(plan.Stages, stage => stage.DurationSeconds == 0
                && stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent);
            for (var i = 0; i < result.Length; i++)
            {
                var stage = plan.Stages[i];
                if (stage.CompletionMode != (int) CustomStageCompletionMode.TimePercent)
                    continue;
                if (stage.DurationSeconds > 0)
                    result[i] = stage.DurationSeconds;
                else if (sequential && fixedTotal == 0)
                    result[i] = Math.Max(1, (int) Math.Round(stage.Percent / weight * remaining));
                else
                {
                    cumulativeWeight += stage.Percent;
                    var end = i == lastWeighted
                        ? remaining : (int) Math.Floor(cumulativeWeight / weight * remaining);
                    result[i] = end - assigned;
                    assigned = end;
                }
            }
            return result;
        }

        private static double SafeElapsed(double elapsed)
        {
            return double.IsNaN(elapsed) || double.IsInfinity(elapsed) ? 0d : Math.Max(0d, elapsed);
        }

        // Read-only: never invokes GetActiveStage, completes AT steps, or initializes/resets timers.
        public static CustomPlanSchedulerSnapshot GetSchedulerSnapshot(CustomPlan plan, double elapsedSeconds)
        {
            if (plan == null || plan.Stages == null || plan.Stages.Length == 0)
                return null;
            var elapsed = SafeElapsed(elapsedSeconds);
            var durations = EffectiveStageDurations(plan);
            var sequential = UsesSequentialStageRuntime(plan);
            var index = -1;
            var stageElapsed = 0d;
            var cycle = 0d;
            if (sequential)
            {
                if (_sequentialPlanKey == SequentialPlanKey(plan) && elapsed + 5d >= _sequentialLastElapsed)
                {
                    index = _sequentialStageIndex;
                    stageElapsed = Math.Max(0d, elapsed - _sequentialStageStartedAt);
                    cycle = _sequentialCycle;
                }
            }
            else
            {
                var effective = GetEffectiveElapsed(plan, elapsed, durations);
                var start = 0;
                for (var i = 0; i < durations.Length; i++)
                {
                    if (effective < start + durations[i] || i == durations.Length - 1)
                    {
                        index = i;
                        stageElapsed = Math.Max(0d, effective - start);
                        break;
                    }
                    start += durations[i];
                }
                var total = durations.Sum();
                if (plan.StageLoopStartIndex >= 0 && plan.StageLoopStartIndex < durations.Length && elapsed >= total)
                    cycle = 1d + Math.Floor((elapsed - total) / Math.Max(1, durations.Skip(plan.StageLoopStartIndex).Sum()));
            }
            double? progress = index < 0 || plan.Stages[index].CompletionMode != (int) CustomStageCompletionMode.TimePercent
                ? (double?) null : durations[index] == 0 ? 1d : Math.Min(1d, stageElapsed / durations[index]);
            return new CustomPlanSchedulerSnapshot(sequential ? "sequential" : "time", index, progress, elapsed, stageElapsed, cycle, durations);
        }

        private static double TotalStageWeight(IEnumerable<CustomPlanStage> stages)
        {
            var total = 0d;
            foreach (var stage in stages)
            {
                if (stage == null || stage.Percent <= 0d || double.IsNaN(stage.Percent) || double.IsInfinity(stage.Percent))
                    throw new InvalidOperationException("Custom stage weights must be finite and positive.");
                total += stage.Percent;
            }
            if (total <= 0d || double.IsInfinity(total))
                throw new InvalidOperationException("Custom stage weights must have a finite, positive total.");
            return total;
        }

        private static string SequentialStageRuntimeLabel(CustomPlanStage stage, int durationSeconds)
        {
            if (stage == null)
                return "empty";

            if (stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats)
            {
                return $"until AT P/T {FormatLong(CurrentAdvancedTrainingPower())}/{FormatLong(CurrentAdvancedTrainingToughness())} >= {FormatLong(stage.TargetAdvancedTrainingPower)}/{FormatLong(stage.TargetAdvancedTrainingToughness)}";
            }

            return $"time {FormatSeconds(durationSeconds)}";
        }

        private static string SequentialPlanKey(CustomPlan plan)
        {
            var durations = EffectiveStageDurations(plan);
            // Ordered scheduling semantics only; stage labels and allocations do not restart timers.
            var stages = string.Join("|", plan.Stages.Select((stage, index) =>
                string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}", stage.CompletionMode, durations[index],
                    stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats ? stage.TargetAdvancedTrainingPower : 0,
                    stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats ? stage.TargetAdvancedTrainingToughness : 0)).ToArray());
            return $"{(plan.Name ?? string.Empty).Length}:{plan.Name}:{plan.StageLoopStartIndex}:{stages}";
        }

        internal static void ResetStageRuntime()
        {
            _sequentialPlanKey = null;
            _sequentialStageIndex = 0;
            _sequentialStageStartedAt = 0d;
            _sequentialLastElapsed = -1d;
            _sequentialCycle = 0d;
        }

        private static double GetEffectiveElapsed(CustomPlan plan, double elapsed, int[] durations)
        {
            var duration = durations.Sum();
            if (plan.StageLoopStartIndex < 0 || plan.StageLoopStartIndex >= plan.Stages.Length || elapsed < duration)
                return elapsed;

            var loopStartSeconds = durations.Take(plan.StageLoopStartIndex).Sum();
            var loopSeconds = Math.Max(1d, duration - loopStartSeconds);
            return loopStartSeconds + (elapsed - loopStartSeconds) % loopSeconds;
        }

        internal static string ApplyStage(CustomPlan plan, CustomPlanStage stage, int remainingSeconds, int settleMilliseconds = 0, string resourceContextKey = null, int stageDurationSeconds = 0)
        {
            if (stage == null)
                return "No custom stage active.";

            var messages = new List<string>();
            BlackBarAllocator.ResetExcessIdle();
            if (plan == null)
                throw new InvalidOperationException("Stage allocation requires a readable custom plan.");
            var c = Main.Character;
            foreach (var energy in new[] {true, false})
            {
                if (energy ? !Main.Settings.ManageEnergy : !Main.Settings.ManageMagic)
                    continue;

                var priorities = EffectiveStagePriorities(stage, energy, remainingSeconds, stageDurationSeconds, out var wandoosAt, Main.IronPillBloodReserved);
                var contextKey = resourceContextKey;
                if (!energy && wandoosAt)
                    contextKey = string.IsNullOrWhiteSpace(contextKey) ? "wandoos-at" : contextKey + ":wandoos-at";

                if (priorities == null || priorities.Length == 0)
                    continue;

                BlackBarAllocator.ClearNguAppliedWeights(energy);
                if (energy)
                    c.removeMostEnergy();
                else
                    c.removeMostMagic();
                Settle(settleMilliseconds);

                priorities = ResolveStagePriorities(stage, priorities, energy, remainingSeconds, contextKey, messages);
                // Stage completion targets, not unrelated menu targets, govern AT caps.
                var stageAtTargets = energy && stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats
                                     && priorities.Any(x => x == "AT-1" || x == "AT-0");
                var powerTarget = stageAtTargets ? c.advancedTraining.levelTarget[1] : 0;
                var toughnessTarget = stageAtTargets ? c.advancedTraining.levelTarget[0] : 0;
                try
                {
                    if (stageAtTargets)
                    {
                        c.advancedTraining.levelTarget[1] = stage.TargetAdvancedTrainingPower;
                        c.advancedTraining.levelTarget[0] = stage.TargetAdvancedTrainingToughness;
                    }

                    var result = BlackBarAllocator.AllocatePriorities(priorities, energy, Math.Max(3, stage.BlackBarLookaheadSeconds), energy ? plan.EnergyNguWeights : plan.MagicNguWeights,
                        (available, goldBudget) => SelectStageRitual(contextKey, available, goldBudget, messages));
                    if (!string.IsNullOrEmpty(result))
                        messages.Add(result);
                }
                finally
                {
                    if (stageAtTargets)
                    {
                        c.advancedTraining.levelTarget[1] = powerTarget;
                        c.advancedTraining.levelTarget[0] = toughnessTarget;
                    }
                }
            }

            return messages.Count == 0 ? "No custom stage allocation was needed." : string.Join("; ", messages.ToArray());
        }

        private static string[] ResolveStagePriorities(CustomPlanStage stage, string[] priorities, bool energy, int remainingSeconds, string contextKey, List<string> messages)
        {
            contextKey = string.IsNullOrWhiteSpace(contextKey) ? "custom-stage" : contextKey;
            var resolved = new List<string>();
            foreach (var priority in priorities)
            {
                var token = (priority ?? string.Empty).Trim().ToUpperInvariant();
                if (!BlackBarAllocator.IsValidPriority(token, energy))
                {
                    messages.Add($"Invalid {(energy ? "energy" : "magic")} priority '{token}' skipped");
                    continue;
                }

                if (energy && token == "AUG")
                {
                    if (_augmentTargetContextKey != contextKey)
                    {
                        _augmentTargetContextKey = contextKey;
                        _augmentTargetIndex = -1;
                        _augmentTargetUsesUpgrade = false;
                    }

                    var augment = _augmentTargetIndex >= 0
                        ? AugmentOptimizer.OptimizeTarget(_augmentTargetIndex, _augmentTargetUsesUpgrade, remainingSeconds, false)
                        : null;
                    if (augment == null || !augment.Success)
                    {
                        if (augment != null)
                            Main.Log($"Custom augment target repinning for {contextKey}: {augment.Message}");
                        _augmentTargetIndex = -1;
                        _augmentTargetUsesUpgrade = false;
                        augment = AugmentOptimizer.OptimizeBest(remainingSeconds, false);
                        if (augment.Success)
                            Main.Log($"Custom augment target pinned for {contextKey}: pair {augment.AugmentIndex}, {(augment.UsesUpgrade ? "augment+upgrade" : "augment only")}");
                    }

                    if (!augment.Success)
                    {
                        messages.Add(augment.Message);
                        continue;
                    }

                    _augmentTargetIndex = augment.AugmentIndex;
                    _augmentTargetUsesUpgrade = augment.UsesUpgrade;
                    resolved.Add("AUG-" + _augmentTargetIndex * 2);
                    if (_augmentTargetUsesUpgrade)
                        resolved.Add("AUG-" + (_augmentTargetIndex * 2 + 1));
                    continue;
                }

                if (energy && stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats)
                {
                    if (token == "AT")
                    {
                        if (!AdvancedTrainingTargetReached(stage, 1))
                            resolved.Add("AT-1");
                        if (!AdvancedTrainingTargetReached(stage, 0))
                            resolved.Add("AT-0");
                        continue;
                    }

                    if (token == "AT-1" && AdvancedTrainingTargetReached(stage, 1)
                        || token == "AT-0" && AdvancedTrainingTargetReached(stage, 0))
                        continue;
                }

                resolved.Add(token);
            }

            return resolved.ToArray();
        }

        internal static string[] EffectiveStagePriorities(CustomPlanStage stage, bool energy, int remainingSeconds, int durationSeconds, out bool wandoosAt, bool reserveBlood = false)
        {
            wandoosAt = false;
            var priorities = energy ? stage.EnergyPriorities : stage.MagicPriorities;
            var focus = (CustomStageFocus) (energy ? stage.EnergyFocus : stage.MagicFocus);
            if (priorities == null || priorities.Length == 0)
            {
                wandoosAt = focus == CustomStageFocus.Wandoos && WandoosAdvancedTrainingPhase(remainingSeconds, durationSeconds);
                priorities = wandoosAt
                    ? energy ? new[] {"AT-3", "AT-4", "WAN", "NGU", "TM"} : new[] {"RIT", "WAN", "NGU", "TM"}
                    : BlackBarAllocator.DefaultPriorities(focus, energy);
            }
            if (energy || !reserveBlood) return priorities;

            var overridden = new List<string>();
            if (focus == CustomStageFocus.Wandoos) overridden.Add("WAN");
            overridden.Add("RIT");
            foreach (var priority in priorities)
            {
                var token = (priority ?? string.Empty).Trim().ToUpperInvariant();
                // One sustainable optimizer-selected ritual replaces all explicit rituals.
                if (token == "RIT" || token.StartsWith("RIT-", StringComparison.Ordinal) || overridden.Contains(token)) continue;
                overridden.Add(token);
            }
            return overridden.ToArray();
        }

        private static int SelectStageRitual(string contextKey, long availableMagic, double goldBudget, List<string> messages)
        {
            contextKey = string.IsNullOrWhiteSpace(contextKey) ? "custom-stage" : contextKey;
            if (_bloodTargetContextKey != contextKey)
            {
                _bloodTargetContextKey = contextKey;
                _bloodTargetRitualIndex = -1;
            }
            var blood = _bloodTargetRitualIndex >= 0
                ? BloodMagicOptimizer.OptimizeTarget(_bloodTargetRitualIndex, availableMagic, goldBudget)
                : null;
            if (blood == null || !blood.Success)
            {
                _bloodTargetRitualIndex = -1;
                blood = BloodMagicOptimizer.OptimizeBest(availableMagic, goldBudget);
                if (blood.Success)
                    Main.Log($"Custom Blood Magic target pinned for {contextKey}: ritual {blood.RitualIndex}");
            }
            if (!blood.Success)
            {
                messages.Add(blood.Message);
                return -1;
            }
            _bloodTargetRitualIndex = blood.RitualIndex;
            return _bloodTargetRitualIndex;
        }

        internal static void ResetResourceTargets()
        {
            BlackBarAllocator.ResetExcessIdle();
            _augmentTargetContextKey = null;
            _augmentTargetIndex = -1;
            _augmentTargetUsesUpgrade = false;
            _bloodTargetContextKey = null;
            _bloodTargetRitualIndex = -1;
        }

        private static bool AdvancedTrainingStatsReached(CustomPlanStage stage)
        {
            if (stage == null)
                return true;

            return AdvancedTrainingTargetReached(stage, 1) && AdvancedTrainingTargetReached(stage, 0);
        }

        private static bool AdvancedTrainingTargetReached(CustomPlanStage stage, int id)
        {
            var target = id == 1 ? stage.TargetAdvancedTrainingPower : stage.TargetAdvancedTrainingToughness;
            if (target <= 0)
                return true;

            return CurrentAdvancedTrainingLevel(id) >= target;
        }

        private static long CurrentAdvancedTrainingPower()
        {
            return CurrentAdvancedTrainingLevel(1);
        }

        private static long CurrentAdvancedTrainingToughness()
        {
            return CurrentAdvancedTrainingLevel(0);
        }

        private static long CurrentAdvancedTrainingLevel(int id)
        {
            var c = Main.Character;
            if (c == null || c.advancedTraining == null || c.advancedTraining.level == null || id < 0 || id >= c.advancedTraining.level.Length)
                return 0;

            return c.advancedTraining.level[id];
        }

        private static string FormatLong(long value)
        {
            return value.ToString("n0");
        }

        internal static string FocusLabel(int focus)
        {
            return FocusLabels.ContainsKey(focus) ? FocusLabels[focus] : FocusLabels[(int) CustomStageFocus.None];
        }

        internal static string FormatSeconds(int seconds)
        {
            if (seconds < 0)
                seconds = 0;

            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1
                ? string.Format("{0:0}:{1:00}:{2:00}", Math.Floor(span.TotalHours), span.Minutes, span.Seconds)
                : string.Format("{0:0}:{1:00}", span.Minutes, span.Seconds);
        }

        private static bool WandoosAdvancedTrainingPhase(int remainingSeconds, int stageDurationSeconds)
        {
            return stageDurationSeconds > 0
                   && remainingSeconds > Math.Max(1, stageDurationSeconds / 2)
                   && AdvancedTrainingUnlocked();
        }

        private static bool AdvancedTrainingUnlocked()
        {
            return Main.Character.buttons.advancedTraining.interactable;
        }

        private static void Settle(int milliseconds)
        {
            if (milliseconds > 0)
                System.Threading.Thread.Sleep(milliseconds);
        }
    }
}
