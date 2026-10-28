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
        internal string EntryIdentity { get; set; }
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
        public bool ProtectedWorkComplete { get; internal set; }
        public double ProtectedSecondsRemaining { get; internal set; }
        public bool ScheduleComplete { get; internal set; }
        public string TimingReason { get; internal set; }

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
        private static TemporaryBloodAllocation _temporaryBloodAllocation;
        private static StageApplicationReceipt _stageApplicationReceipt;
        private static CustomPlanStage _stageResourceStage;
        private static StageResourceRequirements _stageResourceRequirements;
        private static string _stageResourceContext;
        private static bool _stageMagicYielded;
        internal static ResourceMutationResult LastStageResourceOutcome { get; private set; }
        internal static bool StageResourcePresentationPending => Main.reference?.ResourceSetupPresentationPending == true;

        private sealed class StageApplicationReceipt
        {
            internal readonly Character Character = Main.Character;
            internal readonly LoadoutManager.SnapshotEpoch Epoch = new LoadoutManager.SnapshotEpoch();
            internal readonly bool ManageEnergy = Main.Settings.ManageEnergy, ManageMagic = Main.Settings.ManageMagic;
            internal readonly string EnergyAllocations, MagicAllocations, ContextKey;
            internal readonly int[] EnergyWeights, MagicWeights;

            internal StageApplicationReceipt(string contextKey, int[] energyWeights, int[] magicWeights)
            {
                ContextKey = contextKey;
                if (ManageEnergy) EnergyAllocations = ObserveEnergyAllocations();
                if (ManageMagic) MagicAllocations = ObserveMagicAllocations();
                if (Main.Character.NGU?.skills?.Any(x => x.energy > 0) == true) EnergyWeights = energyWeights?.ToArray();
                if (Main.Character.NGU?.magicSkills?.Any(x => x.magic > 0) == true) MagicWeights = magicWeights?.ToArray();
            }
        }

        internal sealed class TemporaryBloodAllocation
        {
            internal readonly Character Character = Main.Character;
            private readonly object stats = Main.Character.stats, blood = Main.Character.bloodMagic,
                elapsedClock = Main.Character.rebirthTime, totalClock = Main.Character.totalPlaytime;
            private readonly long rebirth = Main.Character.stats.rebirthNumber;
            private readonly double elapsed = Main.Character.rebirthTime.totalseconds, total = Main.Character.totalPlaytime.totalseconds;
            internal int Index;
            internal object Ritual;
            internal long Amount;
            // Lane observation detects interference; it never grants ownership of other destinations.
            internal string Allocations;
            internal bool Interfered;

            internal bool EpochMatches()
            {
                var c = Main.Character;
                return ReferenceEquals(c, Character) && c?.stats != null && c.rebirthTime != null && c.totalPlaytime != null
                    && ReferenceEquals(c.stats, stats) && ReferenceEquals(c.bloodMagic, blood)
                    && ReferenceEquals(c.rebirthTime, elapsedClock) && ReferenceEquals(c.totalPlaytime, totalClock)
                    && c.stats.rebirthNumber == rebirth && c.rebirthTime.totalseconds + .1d >= elapsed
                    && c.totalPlaytime.totalseconds + .1d >= total
                    && Math.Abs(c.totalPlaytime.totalseconds - c.rebirthTime.totalseconds - (total - elapsed)) <= 1d;
            }
        }

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

            return Main.reference?.GetProtectedStage(plan);
        }

        // Nominal preview only; the runtime scheduler accounts for applied time and AT gates.
        internal static int[] EffectiveStageDurations(CustomPlan plan)
        {
            if (plan.Stages.Any(stage => stage == null || stage.Percent <= 0d || double.IsNaN(stage.Percent) || double.IsInfinity(stage.Percent)))
                throw new InvalidOperationException("Custom stage weights must be finite and positive.");
            var timedStages = plan.Stages.Where(stage => stage.CompletionMode == (int) CustomStageCompletionMode.TimePercent).ToArray();
            var fixedTotal = timedStages.Sum(stage => (long) stage.DurationSeconds);
            if (timedStages.Any(stage => stage.DurationSeconds < 0) || fixedTotal > int.MaxValue)
                throw new InvalidOperationException("Fixed stage durations must be nonnegative and fit the Int32 schedule limit.");
            var budget = plan.TimeTriggerEnabled && plan.TimeTriggerSeconds > 0
                ? plan.TimeTriggerSeconds : Math.Max(0, plan.StageDurationSeconds);
            var weighted = timedStages.Where(stage => stage.DurationSeconds == 0).ToArray();
            var weight = weighted.Length == 0 ? 1d : TotalStageWeight(weighted);
            var remaining = Math.Max(0, budget - (int) fixedTotal);
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
                else
                {
                    cumulativeWeight += stage.Percent;
                    // Multiply first to preserve integer boundaries; normalize only on overflow.
                    var scaled = cumulativeWeight * remaining;
                    var end = i == lastWeighted
                        ? remaining : (int) Math.Floor(double.IsInfinity(scaled)
                            ? cumulativeWeight / weight * remaining : scaled / weight);
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
            return Main.reference?.GetProtectedSchedulerSnapshot(plan, elapsedSeconds)
                ?? new CustomPlanSchedulerSnapshot("stages", -1, null, SafeElapsed(elapsedSeconds), 0, 0, EffectiveStageDurations(plan));
        }

        internal static bool ProtectedWorkComplete(CustomPlan plan)
        {
            return plan == null || !plan.StageSchedulerEnabled
                   || Main.reference != null && Main.reference.HasCompletedProtectedStageWork(plan);
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

        internal static string ApplyStage(CustomPlan plan, CustomPlanStage stage, int remainingSeconds, int settleMilliseconds = 0, string resourceContextKey = null, int stageDurationSeconds = 0)
        {
            return ApplyStageResources(ResolveStageResources(plan, stage, remainingSeconds, resourceContextKey, stageDurationSeconds));
        }

        internal static string ApplyStageResources(StageResourceRequirements requirements)
        {
            if (requirements == null)
            {
                _stageApplicationReceipt = null;
                return "No custom stage active.";
            }
            if (!ReferenceEquals(requirements, _stageResourceRequirements))
                throw new InvalidOperationException("Stage resource requirements were superseded before application.");
            _stageApplicationReceipt = null;
            if (UnmanagedMagicBlocksAt(requirements.Stage, requirements.RemainingSeconds, requirements.DurationSeconds))
                return PrepareAtEntry(requirements.Stage, requirements.RemainingSeconds, requirements.DurationSeconds);
            var outcome = Main.reference.ReconcileStageResourceSetup(requirements.Intent);
            return ObserveStageResources(requirements, outcome);
        }

        // A concrete producer result, not another application/retry engine. Resolve
        // retains acquired activity pins but performs no native allocation writes.
        internal sealed class StageResourceRequirements
        {
            internal ResourceSetupIntent Intent;
            internal CustomPlan Plan;
            internal CustomPlanStage Stage;
            internal int RemainingSeconds, DurationSeconds;
            internal string Context;
            internal bool Verified, BloodDemand, PreserveMagic;
            internal readonly List<string> Messages = new List<string>();
            internal readonly List<BlackBarAllocator.PriorityResourcePlan> LanePlans = new List<BlackBarAllocator.PriorityResourcePlan>();
            internal TemporaryBloodAllocation PreviousBlood;
        }

        internal static StageResourceRequirements ResolveStageResources(CustomPlan plan, CustomPlanStage stage,
            int remainingSeconds, string resourceContextKey, int stageDurationSeconds)
        {
            LastStageResourceOutcome = null;
            _stageApplicationReceipt = null;
            _stageResourceRequirements = null;
            if (stage == null)
                return null;

            var requirements = new StageResourceRequirements { Plan = plan, Stage = stage, Context = resourceContextKey,
                RemainingSeconds = remainingSeconds, DurationSeconds = stageDurationSeconds };
            _stageResourceRequirements = requirements;
            var messages = requirements.Messages;
            BlackBarAllocator.ResetExcessIdle();
            if (plan == null)
                throw new InvalidOperationException("Stage allocation requires a readable custom plan.");
            var c = Main.Character;
            if (_stageResourceStage == null || _stageResourceContext != resourceContextKey
                || resourceContextKey == null && !ReferenceEquals(_stageResourceStage, stage))
            {
                _stageResourceStage = stage; _stageResourceContext = resourceContextKey;
                _stageMagicYielded = false;
            }
            _stageResourceStage = stage;
            var epoch = new LoadoutManager.SnapshotEpoch(); var settings = Main.Settings; var owner = Main.reference;
            var manageEnergy = settings.ManageEnergy; var manageMagic = settings.ManageMagic;
            if (UnmanagedMagicBlocksAt(stage, remainingSeconds, stageDurationSeconds))
                return requirements;
            var catchUpBloodGoals = Main.CustomBloodGoalCatchUpRequested(plan);
            var bloodDemand = Main.IronPillBloodReserved || BloodGoalsPrioritized(stage, catchUpBloodGoals);
            var previousBlood = _temporaryBloodAllocation;
            var endingBlood = !bloodDemand && previousBlood != null && previousBlood.EpochMatches()
                && previousBlood.Index < c.bloodMagic.ritual.Count && ReferenceEquals(previousBlood.Ritual, c.bloodMagic.ritual[previousBlood.Index]);
            _stageMagicYielded |= !bloodDemand && ReleaseTemporaryBloodAllocation();
            var preserveMagic = _stageMagicYielded;
            var verified = !preserveMagic;
            if (preserveMagic) messages.Add("Magic allocation changed externally; baseline refresh deferred.");
            var desired = new Dictionary<ResourceTarget, long[]>();
            var lanePlans = requirements.LanePlans;
            foreach (var energy in new[] {true, false})
            {
                if (energy ? !Main.Settings.ManageEnergy : !Main.Settings.ManageMagic)
                    continue;
                if (!energy && preserveMagic)
                    continue;

                var priorities = EffectiveStagePriorities(stage, energy, remainingSeconds, stageDurationSeconds, out var wandoosAt, Main.IronPillBloodReserved, catchUpBloodGoals);
                var contextKey = resourceContextKey;
                if (!energy && wandoosAt)
                    contextKey = string.IsNullOrWhiteSpace(contextKey) ? "wandoos-at" : contextKey + ":wandoos-at";

                if (priorities == null || priorities.Length == 0)
                    continue;

                priorities = ResolveStagePriorities(stage, priorities, energy, remainingSeconds, contextKey, messages);
                // Resolving completed AT targets to no bars is valid; silently skipping bad tokens is not.
                verified &= priorities.Length > 0 || energy
                    && stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats
                    && AdvancedTrainingStatsReached(stage);
                // Stage completion targets, not unrelated menu targets, govern AT caps.
                long[] atTargets = null;
                if (energy && stage.CompletionMode == (int) CustomStageCompletionMode.AdvancedTrainingStats)
                {
                    atTargets = c.advancedTraining.levelTarget.ToArray();
                    atTargets[1] = stage.TargetAdvancedTrainingPower; atTargets[0] = stage.TargetAdvancedTrainingToughness;
                }
                var lanePlan = BlackBarAllocator.CalculatePriorities(priorities, energy, Math.Max(3, stage.BlackBarLookaheadSeconds),
                        (available, goldBudget) => SelectStageRitual(contextKey, available, goldBudget, messages),
                        available =>
                        {
                            var estimateSeconds = remainingSeconds;
                            if (remainingSeconds == 0 && plan.StageSchedulerEnabled)
                            {
                                var snapshot = GetSchedulerSnapshot(plan, c.rebirthTime?.totalseconds ?? 0d);
                                if (snapshot != null && snapshot.ScheduleComplete
                                    && snapshot.ActiveStageIndex >= 0
                                    && snapshot.ActiveStageIndex < plan.Stages.Length
                                    && ReferenceEquals(stage, plan.Stages[snapshot.ActiveStageIndex]))
                                    // Holding the last actual allocation is ongoing work, not a new timed or AT phase.
                                    estimateSeconds = Math.Max(1, Main.Settings.CustomAugmentsBloodEstimateSeconds);
                            }
                            return SelectStageAugment(contextKey, estimateSeconds, available, stage.BlackBarLookaheadSeconds, messages);
                        }, atTargets);
                lanePlans.Add(lanePlan);
                foreach (var pair in lanePlan.Desired) desired.Add(pair.Key, pair.Value);
                if (!string.IsNullOrEmpty(lanePlan.Message)) messages.Add(lanePlan.Message);
            }

            if (endingBlood && manageMagic && !desired.ContainsKey(ResourceTarget.BloodRitual)
                && c.bloodMagic.ritual[previousBlood.Index].magic == previousBlood.Amount)
            {
                var rituals = c.bloodMagic.ritual.Select(x => x.magic).ToArray(); rituals[previousBlood.Index] = 0;
                desired.Add(ResourceTarget.BloodRitual, rituals);
            }
            if (manageMagic && !preserveMagic && StageBlocksWandoos(stage, remainingSeconds, stageDurationSeconds)
                && !desired.ContainsKey(ResourceTarget.WandoosMagic)) desired.Add(ResourceTarget.WandoosMagic, new long[1]);
            requirements.Intent = new ResourceSetupIntent { Owner = "inline-stage:" + (resourceContextKey ?? "custom-stage"),
                Desired = desired, Energy = manageEnergy, Magic = manageMagic, Authorized = () =>
                ReferenceEquals(requirements, _stageResourceRequirements) && epoch.Matches()
                && ReferenceEquals(settings, Main.Settings) && ReferenceEquals(_stageResourceStage, stage) && _stageResourceContext == resourceContextKey
                && ReferenceEquals(owner, Main.reference) };
            requirements.Verified = verified;
            requirements.BloodDemand = bloodDemand;
            requirements.PreserveMagic = preserveMagic;
            requirements.PreviousBlood = previousBlood;
            return requirements;
        }

        private static string ObserveStageResources(StageResourceRequirements requirements, ResourceMutationResult outcome)
        {
            // No stale result (even an AppliedVerified result) can authorize credit.
            _stageApplicationReceipt = null;
            LastStageResourceOutcome = outcome;
            if (requirements?.Intent == null || outcome == null)
                throw new InvalidOperationException("Stage verification requires resolved intent and a shared executor outcome.");
            var stage = requirements.Stage; var plan = requirements.Plan;
            var resourceContextKey = requirements.Context;
            var remainingSeconds = requirements.RemainingSeconds; var stageDurationSeconds = requirements.DurationSeconds;
            var settings = Main.Settings; var c = Main.Character;
            var messages = requirements.Messages; var lanePlans = requirements.LanePlans;
            var desired = requirements.Intent.Desired; var verified = requirements.Verified
                && Main.reference.ExecutionBaselineResourceMapMatches(requirements.Intent);
            var previousBlood = requirements.PreviousBlood;
            if (!LastStageResourceOutcome.Verified)
            {
                if (LastStageResourceOutcome.Disposition == MutationDisposition.RejectedUnchanged)
                    return "Resource allocation waiting: " + LastStageResourceOutcome.Reason;
                throw new ResourceMutationException(LastStageResourceOutcome);
            }
            if (!requirements.Intent.Authorized())
                throw new InvalidOperationException("Stage resource authority changed before verification.");
            if (verified && requirements.Intent.Energy && Main.reference.StageFightFundingContextMatches(resourceContextKey))
                Main.reference.ObserveStageFightAugmentFunding(desired);
            foreach (var lanePlan in lanePlans)
            {
                BlackBarAllocator.ObserveApplied(lanePlan);
                if (lanePlan.Desired.TryGetValue(lanePlan.Energy ? ResourceTarget.EnergyNgu : ResourceTarget.MagicNgu, out var ngu)
                    && ngu.Any(x => x > 0) && !lanePlan.Weights.SequenceEqual(lanePlan.Energy ? settings.EnergyNguWeights : settings.MagicNguWeights))
                {
                    verified = false;
                    messages.Add("Global NGU ratios changed during application; current intent awaits its next verified resource difference.");
                }
            }
            if (previousBlood != null && previousBlood.EpochMatches() && desired.TryGetValue(ResourceTarget.BloodRitual, out var desiredBlood)
                && previousBlood.Index < desiredBlood.Length && desiredBlood[previousBlood.Index] == 0)
                _temporaryBloodAllocation = null;
            if (requirements.BloodDemand && !requirements.PreserveMagic && requirements.Intent.Magic && lanePlans.Any(x => !x.Energy)
                && _bloodTargetRitualIndex >= 0 && _bloodTargetRitualIndex < c.bloodMagic.ritual.Count
                && c.bloodMagic.ritual[_bloodTargetRitualIndex].magic > 0)
            {
                _temporaryBloodAllocation = new TemporaryBloodAllocation { Index = _bloodTargetRitualIndex,
                    Ritual = c.bloodMagic.ritual[_bloodTargetRitualIndex], Amount = c.bloodMagic.ritual[_bloodTargetRitualIndex].magic,
                    Allocations = ObserveMagicAllocations() };
            }

            var eligible = StageApplicationEligible(plan, stage, remainingSeconds, stageDurationSeconds);
            if (!eligible)
                messages.Add("Protected stage timing blocked: requested lanes are unmanaged, primary work is unavailable, or no work was requested.");
            if (!verified)
                messages.Add("Protected stage timing blocked: allocation verification failed.");
            var message = messages.Count == 0 ? "No custom stage allocation was needed." : string.Join("; ", messages.ToArray());
            if (eligible && verified)
            {
                _stageApplicationReceipt = new StageApplicationReceipt(resourceContextKey,
                    lanePlans.FirstOrDefault(x => x.Energy)?.Weights, lanePlans.FirstOrDefault(x => !x.Energy)?.Weights);
            }
            return message;
        }

        internal static bool LastStageApplicationSucceeded => _stageApplicationReceipt != null;

        internal static bool StageApplicationStillOwned(string resourceContextKey)
        {
            var receipt = _stageApplicationReceipt;
            if (receipt == null || !string.Equals(receipt.ContextKey, resourceContextKey, StringComparison.Ordinal))
                return false;
            try
            {
                if (Main.Settings != null && ReferenceEquals(receipt.Character, Main.Character) && receipt.Epoch.Matches()
                    && receipt.ManageEnergy == Main.Settings.ManageEnergy && receipt.ManageMagic == Main.Settings.ManageMagic
                    && (receipt.EnergyWeights == null || receipt.EnergyWeights.SequenceEqual(Main.Settings.EnergyNguWeights))
                    && (receipt.MagicWeights == null || receipt.MagicWeights.SequenceEqual(Main.Settings.MagicNguWeights))
                    && (!receipt.ManageEnergy || receipt.EnergyAllocations == ObserveEnergyAllocations())
                    && (!receipt.ManageMagic || receipt.MagicAllocations == ObserveMagicAllocations()))
                    return true;
            }
            catch { }
            // Verification only: observing interference never restores or recalls native resources.
            _stageApplicationReceipt = null;
            return false;
        }

        private static string ObserveEnergyAllocations()
        {
            var c = Main.Character;
            return string.Join(";", new[] {
                c.training?.attackEnergy ?? Enumerable.Empty<long>(),
                c.training?.defenseEnergy ?? Enumerable.Empty<long>(),
                c.advancedTraining?.energy ?? Enumerable.Empty<long>(),
                c.augments?.augs?.Select(x => x.augEnergy) ?? Enumerable.Empty<long>(),
                c.augments?.augs?.Select(x => x.upgradeEnergy) ?? Enumerable.Empty<long>(),
                c.NGU?.skills?.Select(x => x.energy) ?? Enumerable.Empty<long>(),
                c.wishes?.wishes?.Select(x => x.energy) ?? Enumerable.Empty<long>(),
                c.machine == null ? Enumerable.Empty<long>() : new[] {c.machine.speedEnergy},
                c.wandoos98 == null ? Enumerable.Empty<long>() : new[] {c.wandoos98.wandoosEnergy}
            }.Select(amounts => string.Join(",", amounts)));
        }

        private static string ObserveMagicAllocations()
        {
            var c = Main.Character;
            return string.Join(";", new[] {
                c.NGU?.magicSkills?.Select(x => x.magic) ?? Enumerable.Empty<long>(),
                c.bloodMagic?.ritual?.Select(x => x.magic) ?? Enumerable.Empty<long>(),
                c.wishes?.wishes?.Select(x => x.magic) ?? Enumerable.Empty<long>(),
                c.machine == null ? Enumerable.Empty<long>() : new[] {c.machine.goldMultiMagic},
                c.wandoos98 == null ? Enumerable.Empty<long>() : new[] {c.wandoos98.wandoosMagic}
            }.Select(amounts => string.Join(",", amounts)));
        }

        private static bool ReleaseTemporaryBloodAllocation()
        {
            var owned = _temporaryBloodAllocation;
            if (owned == null) return false;
            var c = Main.Character;
            if (!owned.EpochMatches() || c.bloodMagic?.ritual == null || owned.Index >= c.bloodMagic.ritual.Count
                || !ReferenceEquals(owned.Ritual, c.bloodMagic.ritual[owned.Index]))
            {
                _temporaryBloodAllocation = null;
                return false;
            }
            if (!Main.Settings.ManageMagic) return false;

            owned.Interfered |= owned.Allocations != ObserveMagicAllocations();
            if (c.bloodMagic.ritual[owned.Index].magic != owned.Amount) _temporaryBloodAllocation = null;
            return owned.Interfered;
        }

        // Availability, not positive transfers: completed targets may legitimately leave resources idle.
        internal static bool StageApplicationEligible(CustomPlan plan, CustomPlanStage stage, int remainingSeconds, int stageDurationSeconds)
        {
            try
            {
                var c = Main.Character;
                if (plan == null || stage == null || c == null || Main.Settings == null)
                    return false;
                if (UnmanagedMagicBlocksAt(stage, remainingSeconds, stageDurationSeconds)) return false;

                var requested = false;
                var catchUpBloodGoals = Main.CustomBloodGoalCatchUpRequested(plan);
                foreach (var energy in new[] {true, false})
                {
                    var priorities = EffectiveStagePriorities(stage, energy, remainingSeconds, stageDurationSeconds,
                        out _, Main.IronPillBloodReserved, catchUpBloodGoals);
                    if (priorities == null || priorities.Length == 0)
                        continue;
                    requested = true;
                    if (energy ? !Main.Settings.ManageEnergy : !Main.Settings.ManageMagic)
                        return false;

                    var focus = EffectiveStageFocus(stage, energy);
                    // Wandoos' AT phase and blood overlays must not hide a locked declared focus.
                    var primary = focus == CustomStageFocus.None ? priorities[0]
                        : BlackBarAllocator.DefaultPriorities(focus, energy).FirstOrDefault();
                    var token = (primary ?? string.Empty).Trim().ToUpperInvariant();
                    if (!BlackBarAllocator.IsValidPriority(token, energy))
                        return false;
                    var parts = token.Split('-');
                    var indexed = parts.Length == 2;
                    var index = indexed ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
                    bool available;
                    switch (parts[0])
                    {
                        case "TM":
                            available = c.buttons.brokenTimeMachine.interactable && !c.challenges.timeMachineChallenge.inChallenge
                                        && c.machine != null;
                            break;
                        case "NGU":
                            var skills = energy ? c.NGU.skills : c.NGU.magicSkills;
                            var weights = energy ? Main.Settings.EnergyNguWeights : Main.Settings.MagicNguWeights;
                            available = c.buttons.ngu.interactable && skills != null
                                        && (indexed ? index < skills.Count && skills[index] != null && index < weights.Length && weights[index] > 0
                                            : weights != null && weights.Any(weight => weight > 0)
                                              && skills.Count >= weights.Length && skills.All(skill => skill != null));
                            break;
                        case "WAN":
                            available = c.buttons.wandoos.interactable && !c.wandoos98.disabled;
                            break;
                        case "AUG":
                            available = c.buttons.augmentation.interactable && !c.challenges.noAugsChallenge.inChallenge
                                        && c.augments != null && c.augmentsController.augments != null
                                        && (indexed ? index / 2 < c.augmentsController.augments.Length
                                            && (index % 2 == 0 ? !c.augmentsController.augments[index / 2].augLocked()
                                                : !c.augmentsController.augments[index / 2].upgradeLocked())
                                            : c.augmentsController.augments.Any(aug => aug != null && !aug.augLocked()));
                            break;
                        case "AT":
                            available = c.buttons.advancedTraining.interactable && c.advancedTraining != null;
                            break;
                        case "RIT":
                            available = c.buttons.bloodMagic.interactable && c.bloodMagic.ritual != null
                                        && c.bloodMagicController != null
                                        && (indexed ? index < c.bloodMagic.ritual.Count && index < c.bloodMagicController.ritualsUnlocked()
                                            && c.bossID > c.bloodMagic.ritual[index].boss
                                            : c.bloodMagic.ritual.Take(c.bloodMagicController.ritualsUnlocked())
                                                .Any(ritual => ritual != null && c.bossID > ritual.boss));
                            break;
                        default:
                            return false;
                    }
                    if (!available)
                        return false;
                }
                return requested;
            }
            catch
            {
                return false;
            }
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

        private static AugmentOptimizerResult SelectStageAugment(string contextKey, int remainingSeconds, long availableEnergy,
            int lookaheadSeconds, List<string> messages)
        {
            contextKey = string.IsNullOrWhiteSpace(contextKey) ? "custom-stage" : contextKey;
            if (_augmentTargetContextKey != contextKey)
            {
                _augmentTargetContextKey = contextKey;
                _augmentTargetIndex = -1;
                _augmentTargetUsesUpgrade = false;
            }
            var augment = _augmentTargetIndex >= 0
                ? AugmentOptimizer.OptimizeForActualBudget(remainingSeconds, availableEnergy, lookaheadSeconds,
                    _augmentTargetIndex, _augmentTargetUsesUpgrade) : null;
            if (augment == null || !augment.Success)
            {
                if (augment != null)
                    Main.Log($"Custom augment target repinning for {contextKey}: {augment.Message}");
                _augmentTargetIndex = -1;
                _augmentTargetUsesUpgrade = false;
                augment = AugmentOptimizer.OptimizeForActualBudget(remainingSeconds, availableEnergy, lookaheadSeconds);
                if (augment.Success)
                    Main.Log($"Custom augment target pinned for {contextKey}: pair {augment.AugmentIndex}, {(augment.UsesUpgrade ? "augment+upgrade" : "augment only")}");
            }
            if (!augment.Success) messages.Add(augment.Message);
            else
            {
                _augmentTargetIndex = augment.AugmentIndex;
                _augmentTargetUsesUpgrade = augment.UsesUpgrade;
            }
            return augment;
        }

        private static bool StageBlocksWandoos(CustomPlanStage stage, int remainingSeconds, int durationSeconds)
        {
            if (stage == null) return false;
            if (UsesLockedTimeMachineFallback(stage)) return false;
            var primary = ActivityPolicy.Current.PrimaryPriority(stage.EnergyPriorities, stage.EnergyFocus, true);
            // Primary generic AT means adventure training; a fallback AT must not turn
            // every ordinary focus into a Wandoos-free stage. Explicit paid bars do.
            if (primary == "AT") return true;
            return stage.EnergyPriorities != null && stage.EnergyPriorities.Any(x => {
                    var token = (x ?? "").Trim().ToUpperInvariant();
                    return token == "AT-0" || token == "AT-1" || token == "AT-2" || token == "AT-3" || token == "AT-4";
                }) || (stage.EnergyPriorities == null || stage.EnergyPriorities.Length == 0)
                && (stage.EnergyFocus == (int) CustomStageFocus.AdvancedTraining
                    || stage.EnergyFocus == (int) CustomStageFocus.Wandoos && WandoosAdvancedTrainingPhase(remainingSeconds, durationSeconds));
        }

        internal static bool UnmanagedMagicBlocksAt(CustomPlanStage stage, int remainingSeconds, int durationSeconds)
        {
            return StageBlocksWandoos(stage, remainingSeconds, durationSeconds) && !Main.Settings.ManageMagic
                && Main.Character?.wandoos98 != null && Main.Character.wandoos98.wandoosMagic > 0;
        }

        internal static string PrepareAtEntry(CustomPlanStage stage, int remainingSeconds, int durationSeconds)
        {
            if (!StageBlocksWandoos(stage, remainingSeconds, durationSeconds)) return null;
            var c = Main.Character;
            var epoch = new LoadoutManager.SnapshotEpoch(); var owner = Main.reference;
            if (UnmanagedMagicBlocksAt(stage, remainingSeconds, durationSeconds))
            {
                if (Main.Settings.ManageEnergy && c.advancedTraining.energy.Any(x => x > 0))
                {
                    var result = owner.ReconcileStageResourceSetup(new ResourceSetupIntent { Owner = "at-entry-energy-release",
                        Desired = new Dictionary<ResourceTarget, long[]> {
                            { ResourceTarget.AdvancedTraining, new long[c.advancedTraining.energy.Length] } }, Energy = true,
                        Authorized = () => Main.Settings.ManageEnergy && epoch.Matches() && ReferenceEquals(owner, Main.reference)
                            && ReferenceEquals(Main.Character, c) });
                    if (!result.Verified) throw new ResourceMutationException(result);
                }
                return "AT blocked: unmanaged magic is allocated to Wandoos; recall it manually or enable managed magic.";
            }
            // Even an empty/None magic policy must release old managed W before energy AT starts.
            if (Main.Settings.ManageMagic && c.wandoos98 != null && (c.wandoos98.wandoosMagic > 0 || owner.ResourceSetupPresentationPending))
            {
                var result = owner.ReconcileStageResourceSetup(new ResourceSetupIntent { Owner = "at-entry-magic-release",
                    Desired = new Dictionary<ResourceTarget, long[]> { { ResourceTarget.WandoosMagic, new long[1] } }, Magic = true,
                    Authorized = () => Main.Settings.ManageMagic && epoch.Matches() && ReferenceEquals(owner, Main.reference)
                        && ReferenceEquals(Main.Character, c) });
                if (!result.Verified) throw new ResourceMutationException(result);
            }
            return null;
        }

        internal static string[] EffectiveStagePriorities(CustomPlanStage stage, bool energy, int remainingSeconds, int durationSeconds, out bool wandoosAt, bool reserveBlood = false, bool catchUpBloodGoals = false)
        {
            wandoosAt = StageBlocksWandoos(stage, remainingSeconds, durationSeconds);
            if (UsesLockedTimeMachineFallback(stage))
                return energy ? new[] { "WAN" } : new[] { "RIT", "WAN" };
            var priorities = energy ? stage.EnergyPriorities : stage.MagicPriorities;
            var focus = (CustomStageFocus) (energy ? stage.EnergyFocus : stage.MagicFocus);
            if (priorities == null || priorities.Length == 0)
            {
                var laneAt = wandoosAt && focus == CustomStageFocus.Wandoos && stage.EnergyFocus == (int) CustomStageFocus.Wandoos
                    && (stage.EnergyPriorities == null || stage.EnergyPriorities.Length == 0);
                priorities = laneAt
                    ? ActivityPolicy.Current.PreparationPriorities(energy)
                    : BlackBarAllocator.DefaultPriorities(focus, energy);
            }
            if (wandoosAt) priorities = priorities.Where(x => !string.Equals((x ?? "").Trim(), "WAN", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (energy || !reserveBlood && !BloodGoalsPrioritized(stage, catchUpBloodGoals)) return priorities;

            var overridden = new List<string>();
            var explicitBloodPolicy = stage.UseWandoosWhenTimeMachineLocked && stage.MagicPriorities?.Length > 0;
            if (!explicitBloodPolicy && focus == CustomStageFocus.Wandoos && !wandoosAt) overridden.Add("WAN");
            if (!explicitBloodPolicy && focus == CustomStageFocus.TimeMachine) overridden.Add("TM");
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

        internal static bool UsesLockedTimeMachineFallback(CustomPlanStage stage)
        {
            if (stage?.UseWandoosWhenTimeMachineLocked != true) return false;
            var c = Main.Character;
            return c?.buttons?.brokenTimeMachine?.interactable != true
                || c.challenges?.timeMachineChallenge?.inChallenge != false
                || c.machine == null || c.timeMachineController == null;
        }

        internal static CustomStageFocus EffectiveStageFocus(CustomPlanStage stage, bool energy)
        {
            return UsesLockedTimeMachineFallback(stage)
                ? energy ? CustomStageFocus.Wandoos : CustomStageFocus.BloodMagic
                : (CustomStageFocus)(energy ? stage.EnergyFocus : stage.MagicFocus);
        }

        internal static int EffectiveStageGearGoal(CustomPlanStage stage)
        {
            // An uncontrolled gear lane remains uncontrolled even during fallback.
            return stage.GearGoal >= 0 && UsesLockedTimeMachineFallback(stage)
                ? (int)GearOptimizationGoal.Wandoos : stage.GearGoal;
        }

        internal static bool IsCasualEnergyNguStage(CustomPlanStage stage)
        {
            return stage != null && stage.EnergyFocus == (int) CustomStageFocus.EnergyNgu
                   && stage.MagicFocus == (int) CustomStageFocus.MagicNgu
                   && stage.GearGoal == (int) GearOptimizationGoal.EnergyNgu;
        }

        internal static bool BloodGoalsPrioritized(CustomPlanStage stage, bool requested)
        {
            if (!requested || !IsCasualEnergyNguStage(stage)) return false;
            var priorities = stage.MagicPriorities;
            if (priorities == null || priorities.Length == 0)
                priorities = BlackBarAllocator.DefaultPriorities(CustomStageFocus.MagicNgu, false);
            foreach (var priority in priorities)
            {
                var token = (priority ?? string.Empty).Trim().ToUpperInvariant();
                if (token == "RIT" || token.StartsWith("RIT-", StringComparison.Ordinal)) return false;
                if (token == "NGU" || token.StartsWith("NGU-", StringComparison.Ordinal)) return true;
            }
            return false;
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
            // Optimizer pins may reset, but temporary allocations retain their cleanup receipt.
            _stageApplicationReceipt = null;
            _stageResourceStage = null; _stageResourceContext = null;
            _stageResourceRequirements = null;
            _stageMagicYielded = false;
            LastStageResourceOutcome = null;
            BlackBarAllocator.ResetExcessIdle();
            _augmentTargetContextKey = null;
            _augmentTargetIndex = -1;
            _augmentTargetUsesUpgrade = false;
            _bloodTargetContextKey = null;
            _bloodTargetRitualIndex = -1;
        }

        // A verified native load replaced every allocation; the old cleanup receipt is obsolete.
        internal static void ResetAfterVerifiedSaveTestLoad()
        {
            ResetResourceTargets();
            _temporaryBloodAllocation = null;
        }

        internal static bool AdvancedTrainingStatsReached(CustomPlanStage stage)
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

        private static long CurrentAdvancedTrainingLevel(int id)
        {
            var c = Main.Character;
            if (c == null || c.advancedTraining == null || c.advancedTraining.level == null || id < 0 || id >= c.advancedTraining.level.Length)
                return 0;

            return c.advancedTraining.level[id];
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
                   && Main.Settings.ManageEnergy
                   && AdvancedTrainingUnlocked()
                   && Main.Character.wishes.wishes[190].level < 1
                   && (Main.Character.advancedTraining.levelTarget[3] >= 0
                       && (Main.Character.advancedTraining.levelTarget[3] == 0
                           || Main.Character.advancedTraining.level[3] < Main.Character.advancedTraining.levelTarget[3])
                       || Main.Character.advancedTraining.levelTarget[4] >= 0
                       && (Main.Character.advancedTraining.levelTarget[4] == 0
                           || Main.Character.advancedTraining.level[4] < Main.Character.advancedTraining.levelTarget[4]));
        }

        private static bool AdvancedTrainingUnlocked()
        {
            return Main.Character.buttons.advancedTraining.interactable;
        }

    }
}
