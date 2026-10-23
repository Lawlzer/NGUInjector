using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal DateTime FocusRetryAtForChecks => _customFocusNextRetry;
        internal int FocusRetryCountForChecks => _customFocusRetryCount;
        internal bool FocusActivationPendingForChecks => _customFocusActivationPending;
        internal ResourceMutationResult FocusOutcomeForChecks => _customFocusResourceOutcome;
        internal void MakeFocusRetryDueForChecks() { _customFocusNextRetry = DateTime.MinValue; }
    }

    internal static class FocusFailureChecks
    {
        private static int _assertions;

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new Exception(message);
        }

        internal static int Run()
        {
            var settings = Main.Settings;
            var character = Main.Character;
            var owner = Main.reference;
            var status = Main.Status;
            try
            {
                Main.CustomPlanStore.Save("focus-failure-checks", new CustomPlan());
                foreach (var goal in new[] {CustomFocusGoal.EnergyNgu, CustomFocusGoal.MagicNgu})
                {
                    Fresh();
                    RenderFailureRetainsModelAndUsesLatestWeights(goal);
                }
                Fresh();
                RepeatedRenderFailureKeepsVerifiedModel();
                Fresh();
                ActivationFailureRetriesSetup();
                Fresh();
                StopCancelsFailedActivation();
                Main.CustomPlanStore.Save("focus-failure-other", new CustomPlan());
                var changes = new[] {
                    new {Name = "manual adventure", Change = (Action)(() => Main.Settings.ManualAdventureEnabled = true)},
                    new {Name = "scheduled plan", Change = (Action)(() => Main.Settings.UseCustomRebirthPlan = true)},
                    new {Name = "selected plan", Change = (Action)(() => Main.Settings.CustomPlanFile = "focus-failure-other")},
                    new {Name = "auto rebirth", Change = (Action)(() => Main.Settings.AutoRebirth = true)},
                    new {Name = "managed lane", Change = (Action)(() => Main.Settings.ManageEnergy = false)},
                    new {Name = "save identity", Change = (Action)(() => Main.Character.stats = new FocusStats())},
                    new {Name = "rebirth epoch", Change = (Action)(() => Main.Character.stats.rebirthNumber++)},
                    new {Name = "clock rollback", Change = (Action)(() => { Main.Character.rebirthTime.totalseconds--; Main.Character.totalPlaytime.totalseconds--; })},
                    new {Name = "gear identity", Change = (Action)(() => EquipmentRecording.ExactLoadout[0] = new ih {slot = -1, id = 1, equipment = new Equipment {type = part.Head}})},
                    new {Name = "gear primitive", Change = (Action)(() => EquipmentRecording.ExactLoadout[0].level++)},
                    new {Name = "beard selection", Change = (Action)(() => { Main.Character.beards.activeBeards.Add(0); Main.Character.beards.beards[0].active = true; })},
                    new {Name = "digger level", Change = (Action)(() => Main.Character.diggers.diggers[0].curLevel++)},
                    new {Name = "resource allocation", Change = (Action)(() => { Main.Character.NGU.skills[6].energy++; Main.Character.idleEnergy--; })}
                };
                foreach (var change in changes)
                {
                    Fresh();
                    ActivationContextChangeRequiresExplicitApply(change.Name, change.Change);
                }
                Fresh();
                ActivationRetryAllowsPassiveGrowth();
                foreach (var managed in new[] {true, false})
                foreach (var energy in new[] {true, false})
                {
                    Fresh();
                    MagicFocusReservesFullPoolsBeforeRecall(managed, energy);
                }
                foreach (var energy in new[] {true, false})
                {
                    Fresh();
                    MagicFocusDetectsIdlePoolLoss(energy);
                }
                var stopChecks = new Action[] {
                    () => StopAfterRejectedPreflightPreservesAllocations(CustomFocusGoal.EnergyNgu),
                    () => StopAfterRejectedPreflightPreservesAllocations(CustomFocusGoal.MagicNgu),
                    () => StopPreservesEditedLane(true), () => StopPreservesEditedLane(false),
                    () => StopPreservesNewDestinationAfterIdleGrowth(true), () => StopPreservesNewDestinationAfterIdleGrowth(false),
                    () => StopAfterVerifiedRenderFailureReleasesOwnedLanes(true), () => StopAfterVerifiedRenderFailureReleasesOwnedLanes(false),
                    () => StopAfterActivationContextRefusalPreservesManualMaps(false),
                    () => StopAfterActivationContextRefusalPreservesManualMaps(true), MagicStopPreservesNonOwnedPools,
                    () => StopAfterEpochChangePreservesAllocations(true), () => StopAfterEpochChangePreservesAllocations(false),
                    () => StopPreservesUnmanagedLane(true), () => StopPreservesUnmanagedLane(false),
                    StopUsesCurrentIdlePools, RejectedHandoffRetainsPreviousCleanupPermission, RequestedIntentDoesNotGrantCleanup
                };
                foreach (var check in stopChecks)
                {
                    Fresh();
                    check();
                }
                Console.WriteLine("PASS: focus failure compatibility: " + (12 + changes.Length + stopChecks.Length) + " cases, " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                EquipmentRecording.Reset(false);
                CustomStageAllocator.ResetResourceTargets();
                Main.Settings = settings;
                Main.Character = character;
                Main.reference = owner;
                Main.Status = status;
            }
        }

        private static void Fresh()
        {
            EquipmentRecording.Reset(true);
            Main.Settings = new FocusSettings {CustomPlanFile = "focus-failure-checks"};
            Main.Character = new FocusCharacter {idleEnergy = 1000};
            Main.Character.magic.idleMagic = 700;
            Main.reference = new Main();
            CustomStageAllocator.ResetResourceTargets();
        }

        private static void Conserved(string context)
        {
            var c = Main.Character;
            Assert(c.curEnergy == 1000, context + ": energy is conserved");
            Assert(c.magic.idleMagic + c.NGU.magicSkills.Sum(x => x.magic) == 700,
                context + ": magic is conserved");
        }

        private static void RenderFailureRetainsModelAndUsesLatestWeights(CustomFocusGoal goal)
        {
            var owner = Main.reference;
            var c = Main.Character;
            var energy = goal != CustomFocusGoal.MagicNgu;
            owner.Begin(goal);
            owner.MarkRefreshDue();
            var bar = energy ? c.NGUController.NGU[4] : c.NGUController.NGUMagic[0];
            bar.ThrowOnUpdate = true;
            owner.Tick();
            Assert(owner.GetActiveCustomFocusGoal() == goal && owner.GetCustomFocusStatus() == "applied"
                && owner.GetAppliedCustomFocusGoal() == goal.ToString(), goal + ": verified model remains applied when rendering fails");
            Assert(owner.FocusOutcomeForChecks.Disposition == MutationDisposition.ModelAppliedRenderFailed
                && !owner.FocusActivationPendingForChecks && owner.FocusRetryCountForChecks == 0,
                goal + ": rendering-only retry never grants gear replay or model failure backoff");
            Assert(energy ? c.NGU.skills[4].energy == 500 && c.NGU.skills[6].energy == 500 && c.idleEnergy == 0
                    : c.NGU.magicSkills[0].magic == 700 && c.magic.idleMagic == 0,
                goal + ": all accounting, not a partial first transfer, commits before rendering");
            Conserved(goal + " failed refresh");

            Main.Settings.EnergyNguWeights = new[] {0, 0, 0, 0, 2, 0, 1, 0, 0};
            Main.Settings.MagicNguWeights = new[] {0, 1, 0, 0, 0, 0, 0};
            var energyRecalls = c.EnergyRecalls;
            var magicRecalls = c.MagicRecalls;
            bar.ThrowOnUpdate = false;
            owner.Tick();
            Assert(owner.GetActiveCustomFocusGoal() == goal && owner.GetAppliedCustomFocusGoal() == goal.ToString()
                && owner.GetCustomFocusStatus() == "applied", goal + ": due retry recovers retained intent");
            Assert(owner.FocusRetryCountForChecks == 0 && owner.FocusRetryAtForChecks == DateTime.MinValue,
                goal + ": recovery clears retry state");
            Assert(c.NGU.magicSkills[1].magic == 700 && c.NGU.magicSkills[0].magic == 0
                && (energy ? c.NGU.skills[4].energy == 667 && c.NGU.skills[6].energy == 333
                    : c.idleEnergy == 1000 && c.EnergyRecalls == 0), goal + ": recovery uses latest weights only on owned lanes");
            Assert(owner.AppliedMagicWeights.SequenceEqual(Main.Settings.MagicNguWeights)
                && (energy ? owner.AppliedEnergyWeights.SequenceEqual(Main.Settings.EnergyNguWeights) : owner.AppliedEnergyWeights == null),
                goal + ": recovery snapshots only successfully applied lanes");
            Assert(c.EnergyRecalls == energyRecalls && c.MagicRecalls == magicRecalls && owner.Publications == 1 && owner.PublishedRuntime["focusStatus"].Value == "applied",
                goal + ": recovery is published immediately");
            Assert(EquipmentRecording.Gear.Count == 0 && EquipmentRecording.Diggers.Count == 0 && EquipmentRecording.Beards.Count == 0,
                goal + ": resource retry does not replay equipment setup");
            Conserved(goal + " recovered refresh");
        }

        private static void RepeatedRenderFailureKeepsVerifiedModel()
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Begin(CustomFocusGoal.EnergyNgu);
            owner.MarkRefreshDue();
            c.NGUController.NGU[4].ThrowOnUpdate = true;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                owner.MarkRefreshDue();
                owner.Tick();
                Assert(owner.FocusRetryCountForChecks == 0 && owner.FocusRetryAtForChecks == DateTime.MinValue
                    && owner.GetCustomFocusStatus() == "applied", "render failure " + attempt + ": model remains verified, not an endless failed allocation");
                Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0 && (attempt == 0 || owner.FocusOutcomeForChecks.ModelWrites == 0),
                    "render failure " + attempt + ": presentation retry cannot recall or replay committed model writes");
                owner.Tick();
                Assert(c.EnergyRecalls == 0 && owner.Publications == 0,
                    "render failure " + attempt + ": normal cadence suppresses duplicate callbacks and publications");
                Conserved("failure " + attempt);
            }
        }

        private static void ActivationFailureRetriesSetup()
        {
            var owner = Main.reference;
            var c = Main.Character;
            EquipmentRecording.ThrowNextDiggers = true;
            var message = owner.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            Assert(message.Contains("digger")
                && owner.GetCustomFocusStatus() == "failed" && owner.GetActiveCustomFocusGoal() == CustomFocusGoal.EnergyNgu,
                "activation failure retains the requested focus and surfaces the rendering error");
            Assert(owner.FocusActivationPendingForChecks && owner.FocusRetryCountForChecks == 1
                && owner.GetAppliedCustomFocusGoal() == string.Empty, "activation failure requires setup replay before an applied receipt");
            Assert(EquipmentRecording.Gear.Count == 1 && EquipmentRecording.Diggers.Count == 1,
                "activation failure happens after actual gear and support boundaries");
            Conserved("failed activation");
            var recalls = c.EnergyRecalls;
            owner.Tick();
            Assert(c.EnergyRecalls == recalls && EquipmentRecording.Gear.Count == 1, "activation retry obeys backoff");
            c.NGUController.NGU[4].ThrowOnUpdate = false;
            owner.MakeFocusRetryDueForChecks();
            var ngu = c.NGU;
            var publications = owner.Publications;
            var magicRecalls = c.MagicRecalls;
            var status = Main.Status;
            c.NGU = null;
            owner.Tick();
            Assert(owner.FocusRetryCountForChecks == 1 && owner.FocusRetryAtForChecks == DateTime.MinValue
                && owner.FocusActivationPendingForChecks, "missing activation retry data waits without advancing failure backoff");
            Assert(owner.GetActiveCustomFocusGoal() == CustomFocusGoal.EnergyNgu && owner.GetCustomFocusStatus() == "failed"
                && owner.Publications == publications && Main.Status == status,
                "missing activation retry data preserves retained intent and failure telemetry");
            Assert(c.EnergyRecalls == recalls && c.MagicRecalls == magicRecalls
                && EquipmentRecording.Gear.Count == 1 && EquipmentRecording.Diggers.Count == 1,
                "missing activation retry data performs no resource writes or setup replay");
            c.NGU = ngu;
            owner.Tick();
            Assert(owner.GetCustomFocusStatus() == "applied" && owner.GetAppliedCustomFocusGoal() == "EnergyNgu"
                && !owner.FocusActivationPendingForChecks && owner.FocusRetryCountForChecks == 0,
                "activation retry completes the retained setup intent");
            Assert(EquipmentRecording.Gear.Count == 1 && EquipmentRecording.Diggers.Count == 2,
                "activation retry completes retained exact support without a replacement gear search");
            Assert(c.NGU.skills[4].energy == 500 && c.NGU.skills[6].energy == 500 && c.NGU.magicSkills[0].magic == 700,
                "activation retry commits both focus lanes");
            Conserved("recovered activation");
        }

        private static void StopCancelsFailedActivation()
        {
            var owner = Main.reference;
            var c = Main.Character;
            EquipmentRecording.ThrowNextDiggers = true;
            owner.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            var energyRecalls = c.EnergyRecalls;
            var magicRecalls = c.MagicRecalls;
            var message = owner.StopCustomFocusGoal();
            Assert(owner.GetActiveCustomFocusGoal() == null && owner.GetCustomFocusStatus() == "idle"
                && !owner.FocusActivationPendingForChecks && owner.FocusRetryCountForChecks == 0
                && owner.FocusRetryAtForChecks == DateTime.MinValue, "explicit stop clears retained failure and retry intent");
            Assert(c.idleEnergy == 1000 && c.magic.idleMagic == 700 && c.NGU.skills.All(x => x.energy == 0),
                "stopping a genuine setup failure preserves the unfunded resource lanes");
            Assert(c.EnergyRecalls == energyRecalls && c.MagicRecalls == magicRecalls
                && message.Contains("left untouched") && message.Contains("review"),
                "failed activation intent grants no recall permission and stop requests review of preserved resources");
            var recalls = c.EnergyRecalls;
            var publications = owner.Publications;
            var gear = EquipmentRecording.Gear.Count;
            owner.MakeFocusRetryDueForChecks();
            owner.MarkRefreshDue();
            owner.Tick();
            Assert(c.EnergyRecalls == recalls && owner.Publications == publications && EquipmentRecording.Gear.Count == gear,
                "later ticks cannot resurrect explicitly stopped retry intent");
            Conserved("stopped activation");
        }

        private static void ActivationContextChangeRequiresExplicitApply(string context, Action change)
        {
            var owner = Main.reference;
            var c = Main.Character;
            c.rebirthTime.totalseconds = 100;
            c.totalPlaytime.totalseconds = 200;
            EquipmentRecording.ExactLoadout = new[] {new ih {slot = -1, id = 1, equipment = new Equipment {type = part.Head}}};
            EquipmentRecording.ThrowNextDiggers = true;
            owner.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            Assert(owner.FocusActivationPendingForChecks && owner.GetCustomFocusStatus() == "failed", context + ": fixture failed after handoff");
            c.NGUController.NGU[4].ThrowOnUpdate = false;
            change();
            var policy = new[] {Main.Settings.ManualAdventureEnabled, Main.Settings.UseCustomRebirthPlan, Main.Settings.AutoRebirth};
            var planFile = Main.Settings.CustomPlanFile;
            var elapsed = c.rebirthTime.totalseconds;
            var total = c.totalPlaytime.totalseconds;
            var energyRecalls = c.EnergyRecalls;
            var magicRecalls = c.MagicRecalls;
            var gear = EquipmentRecording.ExactLoadout[0];
            var gearLevel = gear.level;
            var beards = c.beards.activeBeards.ToArray();
            var diggerLevel = c.diggers.diggers[0].curLevel;
            var allocations = c.NGU.skills.Select(x => x.energy).ToArray();
            var idle = c.idleEnergy;
            owner.MakeFocusRetryDueForChecks();

            owner.Tick();

            Assert(owner.GetActiveCustomFocusGoal() == CustomFocusGoal.EnergyNgu && owner.GetCustomFocusStatus() == "failed"
                && owner.GetAppliedCustomFocusGoal() == string.Empty && owner.FocusActivationPendingForChecks,
                context + ": ownership loss retains failed requested intent");
            Assert(Main.Status.Contains("context changed") && Main.Status.Contains("apply the focus again")
                && owner.FocusRetryAtForChecks == DateTime.MaxValue, context + ": ownership loss requires actionable explicit acknowledgement");
            Assert(c.EnergyRecalls == energyRecalls && c.MagicRecalls == magicRecalls && c.idleEnergy == idle
                && c.NGU.skills.Select(x => x.energy).SequenceEqual(allocations), context + ": stale retry cannot recall or reallocate resources");
            Assert(EquipmentRecording.Gear.Count == 1 && EquipmentRecording.Diggers.Count == 1 && EquipmentRecording.Beards.Count == 1
                && ReferenceEquals(gear, EquipmentRecording.ExactLoadout[0]) && gear.level == gearLevel
                && c.beards.activeBeards.SequenceEqual(beards) && c.diggers.diggers[0].curLevel == diggerLevel,
                context + ": stale retry leaves newer gear and support untouched");
            Assert(policy.SequenceEqual(new[] {Main.Settings.ManualAdventureEnabled, Main.Settings.UseCustomRebirthPlan, Main.Settings.AutoRebirth})
                && Main.Settings.CustomPlanFile == planFile && c.rebirthTime.totalseconds == elapsed && c.totalPlaytime.totalseconds == total,
                context + ": stale retry does not pause newer settings, restore a plan, or reset clocks");
            var publications = owner.Publications;
            owner.Tick();
            Assert(owner.Publications == publications && c.EnergyRecalls == energyRecalls && EquipmentRecording.Gear.Count == 1,
                context + ": needs-user state suppresses further automatic takeover");

            owner.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);

            Assert(owner.GetCustomFocusStatus() == "applied" && owner.GetAppliedCustomFocusGoal() == "EnergyNgu"
                && !owner.FocusActivationPendingForChecks && EquipmentRecording.Gear.Count == 2,
                context + ": explicit apply acknowledges the new context and may finish setup");
            Assert(!Main.Settings.ManualAdventureEnabled && !Main.Settings.UseCustomRebirthPlan && !Main.Settings.AutoRebirth
                && Main.Settings.CustomPlanFile == planFile && c.rebirthTime.totalseconds == elapsed && c.totalPlaytime.totalseconds == total,
                context + ": explicit apply pauses current owners without restoring original plan or clocks");
        }

        private static void ActivationRetryAllowsPassiveGrowth()
        {
            var owner = Main.reference;
            var c = Main.Character;
            EquipmentRecording.ThrowNextDiggers = true;
            owner.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            c.NGUController.NGU[4].ThrowOnUpdate = false;
            c.rebirthTime.totalseconds += 3;
            c.totalPlaytime.totalseconds += 3;
            c.idleEnergy += 17;
            c.res3.idleRes3 = c.res3.curRes3 = 5;
            owner.MakeFocusRetryDueForChecks();

            owner.Tick();

            Assert(owner.GetCustomFocusStatus() == "applied" && !owner.FocusActivationPendingForChecks
                && EquipmentRecording.Gear.Count == 1, "unchanged ownership permits retained-selection retry after normal clock and idle growth");
            Assert(c.curEnergy == 1017 && c.NGU.skills.Sum(x => x.energy) == 1017 && c.res3.curRes3 == 5 && c.res3.idleRes3 == 5,
                "retry observes generated resources instead of restoring a failed-attempt pool snapshot");
        }

        private static void MagicFocusReservesFullPoolsBeforeRecall(bool managed, bool energy)
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Begin(CustomFocusGoal.EnergyNgu);
            Main.Settings.ManageEnergy = Main.Settings.ManageR3 = managed;
            Main.Settings.AutoRebirth = Main.Settings.ManualAdventureEnabled = Main.Settings.UseCustomRebirthPlan = true;
            c.idleEnergy = c.res3.idleRes3 = 950;
            c.NGU.skills[0].energy = c.hacks.hacks[0].res3 = 50;
            c.res3.curRes3 = 1000;
            EquipmentRecording.EnergySwapCapacity = energy ? 999 : 1000;
            EquipmentRecording.Res3SwapCapacity = energy ? 1000 : 999;
            EquipmentRecording.AfterExactApplication = () => {
                if (energy) c.idleEnergy--;
                else { c.res3.idleRes3--; c.res3.curRes3--; }
            };

            var message = owner.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);

            Assert(message.Contains(energy ? "retained energy exceeds swap capacity" : "retained R3 exceeds swap capacity"),
                "Magic NGU near-capacity preflight rejects full " + (energy ? "energy" : "R3") + " pool; managed=" + managed);
            Assert(EquipmentRecording.CapacityChecks.Single().SequenceEqual(new[] {1000m, 1000m}),
                "Magic NGU capacity budgets include idle/current pools independently of global management flags");
            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0 && EquipmentRecording.ExactApplications.Count == 0
                && EquipmentRecording.Beards.Count == 0 && EquipmentRecording.Diggers.Count == 0,
                "capacity rejection happens before any recall, equipment application, support write, or idle clamp");
            Assert(c.idleEnergy == 950 && c.curEnergy == 1000 && c.res3.idleRes3 == 950 && c.res3.curRes3 == 1000
                && c.NGU.skills[0].energy == 50 && c.hacks.hacks[0].res3 == 50, "capacity rejection loses no idle or allocated resources");
            Assert(owner.GetActiveCustomFocusGoal() == CustomFocusGoal.EnergyNgu && owner.GetCustomFocusStatus() == "applied"
                && Main.Settings.AutoRebirth && Main.Settings.ManualAdventureEnabled && Main.Settings.UseCustomRebirthPlan,
                "Magic NGU capacity preflight rejection preserves the previous owner and settings");
        }

        private static void MagicFocusDetectsIdlePoolLoss(bool energy)
        {
            var owner = Main.reference;
            var c = Main.Character;
            c.res3.idleRes3 = c.res3.curRes3 = 1000;
            // A real changed gear map is necessary to exercise its mutation callback;
            // unchanged exact maps correctly perform no native write or recall.
            EquipmentRecording.SelectedGear = new[] { new ih { slot = 0, id = 2, equipment = new Equipment { type = part.Head } } };
            EquipmentRecording.AfterExactApplication = () => {
                if (energy) c.idleEnergy--;
                else { c.res3.idleRes3--; c.res3.curRes3--; }
            };

            var message = owner.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);

            Assert(message.Contains("Non-owned energy/R3 pools changed") && owner.GetCustomFocusStatus() == "failed"
                && owner.GetAppliedCustomFocusGoal() == string.Empty, "Magic NGU rejects an application that silently loses idle " + (energy ? "energy" : "R3"));
            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 1 && c.NGU.magicSkills.All(x => x.magic == 0)
                && EquipmentRecording.Beards.Count == 0 && EquipmentRecording.Diggers.Count == 0,
                "full-pool mismatch stops before support or focus funding, without recalling non-owned resources");
            Assert(c.idleEnergy == (energy ? 999 : 1000) && c.res3.idleRes3 == (energy ? 1000 : 999)
                && c.res3.curRes3 == c.res3.idleRes3, "mismatch detection never fabricates resources by restoring snapshots");
        }

        private static void StopAfterRejectedPreflightPreservesAllocations(CustomFocusGoal goal)
        {
            var owner = Main.reference;
            var c = Main.Character;
            c.NGU.skills[4].energy = 137;
            c.idleEnergy -= 137;
            c.NGU.magicSkills[2].magic = 211;
            c.magic.idleMagic -= 211;
            var ngu = c.NGU;
            c.NGU = null;
            try { owner.ApplyCustomFocusGoal(goal); }
            finally { c.NGU = ngu; }
            Assert(owner.GetActiveCustomFocusGoal() == goal && owner.GetCustomFocusStatus() == "failed"
                && !owner.FocusActivationPendingForChecks && c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                goal + ": failed preflight records intent without acquiring any resource lane");

            var message = owner.StopCustomFocusGoal();

            Assert(c.NGU.skills[4].energy == 137 && c.idleEnergy == 863 && c.NGU.magicSkills[2].magic == 211
                && c.magic.idleMagic == 489 && c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                goal + ": stopping rejected intent cannot recall pre-existing manual allocations");
            Assert(owner.GetActiveCustomFocusGoal() == null && owner.GetCustomFocusStatus() == "idle"
                && message.Contains("left untouched") && message.Contains("review"),
                goal + ": stop cancels unacquired intent while reporting that resources were preserved");
            Conserved(goal + " rejected preflight stop");
        }

        private static void StopPreservesEditedLane(bool energy)
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Allocate(CustomFocusGoal.EnergyNgu);
            owner.Begin(CustomFocusGoal.EnergyNgu);
            if (energy) { c.NGU.skills[4].energy--; c.NGU.skills[5].energy++; }
            else { c.NGU.magicSkills[0].magic--; c.NGU.magicSkills[1].magic++; }

            var message = owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                "stop uses exact shared-resource differences, never whole-lane recalls");
            Assert(energy ? c.NGU.skills[4].energy == 499 && c.NGU.skills[5].energy == 1 && c.NGU.skills[6].energy == 500
                    && c.idleEnergy == 0 && c.magic.idleMagic == 700 && c.NGU.magicSkills.All(x => x.magic == 0)
                : c.NGU.magicSkills[0].magic == 699 && c.NGU.magicSkills[1].magic == 1 && c.magic.idleMagic == 0
                    && c.idleEnergy == 1000 && c.NGU.skills.All(x => x.energy == 0),
                "stop preserves every current allocation in the edited lane and releases the verified other lane");
            Assert(message.Contains("Released verified") && message.Contains("left untouched")
                && owner.GetCustomFocusStatus() == "idle", "mixed-ownership stop reports safe release and preserved allocations");
            Conserved("per-lane edited allocation stop");
        }

        private static void StopAfterVerifiedRenderFailureReleasesOwnedLanes(bool energy)
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Allocate(CustomFocusGoal.EnergyNgu);
            owner.Begin(CustomFocusGoal.EnergyNgu);
            (energy ? c.NGUController.NGU[4] : c.NGUController.NGUMagic[0]).ThrowOnUpdate = true;
            if (energy) { c.NGU.skills[4].energy--; c.idleEnergy++; }
            else { c.NGU.magicSkills[0].magic--; c.magic.idleMagic++; }
            owner.MarkRefreshDue();
            owner.Tick();
            Assert(owner.GetCustomFocusStatus() == "applied" && owner.FocusOutcomeForChecks.Disposition == MutationDisposition.ModelAppliedRenderFailed,
                "fixture commits a complete verified model before its renderer throws");
            var energyRecalls = c.EnergyRecalls;
            var magicRecalls = c.MagicRecalls;

            var message = owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == energyRecalls && c.MagicRecalls == magicRecalls,
                "explicit stop releases exact verified targets without replaying whole-lane recalls after presentation failure");
            Assert(c.idleEnergy == 1000 && c.magic.idleMagic == 700 && c.NGU.skills.All(x => x.energy == 0) && c.NGU.magicSkills.All(x => x.magic == 0),
                "explicit release conserves all fully committed model accounting");
            Assert(message.Contains("Released focused resources") && owner.GetActiveCustomFocusGoal() == null,
                "explicit stop cancels presentation retry intent without requiring fake unknown-outcome recovery");
            Conserved("partial refresh stop");
        }

        private static void StopPreservesNewDestinationAfterIdleGrowth(bool energy)
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Allocate(CustomFocusGoal.EnergyNgu);
            owner.Begin(CustomFocusGoal.EnergyNgu);
            if (energy)
            {
                c.idleEnergy += 16;
                c.advancedTraining.energy[2] = 1;
            }
            else
            {
                c.magic.idleMagic += 16;
                c.bloodMagic.ritual[0].magic = 1;
            }

            owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                "new AT or blood allocations never authorize a whole-lane recall");
            Assert(energy ? c.NGU.skills.Sum(x => x.energy) == 1000 && c.advancedTraining.energy[2] == 1 && c.idleEnergy == 16
                    && c.magic.idleMagic == 700
                : c.NGU.magicSkills.Sum(x => x.magic) == 700 && c.bloodMagic.ritual[0].magic == 1 && c.magic.idleMagic == 16
                    && c.idleEnergy == 1000,
                "cleanup observes all native destinations, not only the focused NGU bars, and preserves generated idle resources");
            Assert(c.curEnergy == (energy ? 1017 : 1000)
                && c.magic.idleMagic + c.NGU.magicSkills.Sum(x => x.magic) + c.bloodMagic.ritual.Sum(x => x.magic) == (energy ? 700 : 717),
                "manual destinations funded from generated resources are conserved without restoring earlier pool totals");
        }

        private static void StopAfterActivationContextRefusalPreservesManualMaps(bool restoreAmounts)
        {
            var owner = Main.reference;
            var c = Main.Character;
            EquipmentRecording.ThrowNextDiggers = true;
            owner.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            c.NGUController.NGUMagic[0].ThrowOnUpdate = false;
            ResourceWriter.Transfer(ResourceTarget.EnergyNgu, 4, 500, false);
            ResourceWriter.Transfer(ResourceTarget.EnergyNgu, 6, 500, false);
            ResourceWriter.Transfer(ResourceTarget.MagicNgu, 0, 700, false);
            c.NGU.skills[4].energy--;
            c.NGU.skills[5].energy++;
            c.NGU.magicSkills[0].magic -= 100;
            c.NGU.magicSkills[1].magic += 100;
            Main.Settings.ManualAdventureEnabled = true;
            var gear = new ih {slot = -1, id = 19, equipment = new Equipment {type = part.Head}};
            EquipmentRecording.ExactLoadout = new[] {gear};
            owner.MakeFocusRetryDueForChecks();
            owner.Tick();
            Assert(Main.Status.Contains("context changed") && owner.FocusRetryAtForChecks == DateTime.MaxValue,
                "fixture activation retry refuses newer manual settings and allocations");
            if (restoreAmounts)
            {
                c.NGU.skills[4].energy++;
                c.NGU.skills[5].energy--;
            }
            var energyRecalls = c.EnergyRecalls;
            var magicRecalls = c.MagicRecalls;

            owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == energyRecalls && c.MagicRecalls == magicRecalls && c.idleEnergy == 0 && c.magic.idleMagic == 0
                && c.NGU.skills[4].energy == (restoreAmounts ? 500 : 499) && c.NGU.skills[5].energy == (restoreAmounts ? 0 : 1)
                && c.NGU.skills[6].energy == 500
                && c.NGU.magicSkills[0].magic == 600 && c.NGU.magicSkills[1].magic == 100,
                "stop after refused activation cannot turn retained intent into recall permission over newer manual maps");
            Assert(Main.Settings.ManualAdventureEnabled && ReferenceEquals(EquipmentRecording.ExactLoadout[0], gear)
                && owner.GetCustomFocusStatus() == "idle", "stopping refused activation leaves newer manual settings and gear untouched");
            Conserved("refused activation stop");
        }

        private static void MagicStopPreservesNonOwnedPools()
        {
            var owner = Main.reference;
            var c = Main.Character;
            c.NGU.skills[4].energy = 137;
            c.idleEnergy -= 137;
            c.hacks.hacks[0].res3 = 20;
            c.res3.idleRes3 = 80;
            c.res3.curRes3 = 100;
            owner.Allocate(CustomFocusGoal.MagicNgu);
            owner.Begin(CustomFocusGoal.MagicNgu);

            var message = owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == 0 && c.NGU.skills[4].energy == 137 && c.idleEnergy == 863
                && c.hacks.hacks[0].res3 == 20 && c.res3.idleRes3 == 80 && c.res3.curRes3 == 100,
                "Magic-only cleanup owns neither the energy nor R3 pool even with all global management flags enabled");
            Assert(c.MagicRecalls == 0 && c.NGU.magicSkills.All(x => x.magic == 0) && c.magic.idleMagic == 700
                && message.Contains("Released focused resources"), "Magic-only stop releases exactly its matching committed magic lane");
            Conserved("Magic-only owned cleanup");
        }

        private static void StopAfterEpochChangePreservesAllocations(bool characterChanged)
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Allocate(CustomFocusGoal.EnergyNgu);
            owner.Begin(CustomFocusGoal.EnergyNgu);
            if (characterChanged)
            {
                var next = new FocusCharacter();
                for (var i = 0; i < c.NGU.skills.Count; i++) next.NGU.skills[i].energy = c.NGU.skills[i].energy;
                for (var i = 0; i < c.NGU.magicSkills.Count; i++) next.NGU.magicSkills[i].magic = c.NGU.magicSkills[i].magic;
                Main.Character = next;
            }
            else c.stats.rebirthNumber++;
            var current = Main.Character;
            var energyRecalls = current.EnergyRecalls;
            var magicRecalls = current.MagicRecalls;

            owner.StopCustomFocusGoal();

            Assert(current.EnergyRecalls == energyRecalls && current.MagicRecalls == magicRecalls
                && current.NGU.skills.Sum(x => x.energy) == 1000 && current.NGU.magicSkills.Sum(x => x.magic) == 700,
                "old cleanup permission cannot recall identical resource maps in a different character or rebirth epoch");
            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0, "epoch rejection never writes back to the old character either");
            Conserved("new epoch stop");
        }

        private static void StopPreservesUnmanagedLane(bool energy)
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Allocate(CustomFocusGoal.EnergyNgu);
            owner.Begin(CustomFocusGoal.EnergyNgu);
            if (energy) Main.Settings.ManageEnergy = false; else Main.Settings.ManageMagic = false;

            owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                "disabled management never falls back to whole-lane recall");
            Assert(energy ? c.NGU.skills.Sum(x => x.energy) == 1000 && c.magic.idleMagic == 700
                : c.NGU.magicSkills.Sum(x => x.magic) == 700 && c.idleEnergy == 1000,
                "stop preserves the unmanaged lane while returning the other still-owned lane to idle");
            Conserved("management-disabled stop");
        }

        private static void StopUsesCurrentIdlePools()
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Allocate(CustomFocusGoal.EnergyNgu);
            owner.Begin(CustomFocusGoal.EnergyNgu);
            c.idleEnergy += 17;
            c.magic.idleMagic += 23;
            c.rebirthTime.totalseconds += 3;
            c.totalPlaytime.totalseconds += 3;

            owner.StopCustomFocusGoal();

            Assert(c.idleEnergy == 1017 && c.magic.idleMagic == 723 && c.NGU.skills.All(x => x.energy == 0)
                && c.NGU.magicSkills.All(x => x.magic == 0) && c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                "cleanup observes ownership without mistaking idle growth for interference or restoring saved pool totals");
        }

        private static void RejectedHandoffRetainsPreviousCleanupPermission()
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Allocate(CustomFocusGoal.EnergyNgu);
            owner.Begin(CustomFocusGoal.EnergyNgu);
            EquipmentRecording.CapacityFailure = "fixture rejected Magic NGU before handoff";
            owner.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);
            Assert(owner.GetActiveCustomFocusGoal() == CustomFocusGoal.EnergyNgu && owner.GetCustomFocusStatus() == "applied",
                "read-only rejection retains the previous acquired focus and its cleanup receipts");

            owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0 && c.idleEnergy == 1000 && c.magic.idleMagic == 700,
                "rejected Magic-only handoff cannot erase legitimate paired cleanup permission from the previous focus");
            Conserved("previous owner stop after rejected handoff");
        }

        private static void RequestedIntentDoesNotGrantCleanup()
        {
            var owner = Main.reference;
            var c = Main.Character;
            owner.Begin(CustomFocusGoal.EnergyNgu);
            c.NGU.skills[4].energy = 137;
            c.idleEnergy -= 137;
            c.NGU.magicSkills[2].magic = 211;
            c.magic.idleMagic -= 211;

            owner.StopCustomFocusGoal();

            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0 && c.NGU.skills[4].energy == 137 && c.NGU.magicSkills[2].magic == 211,
                "requested/applied intent labels alone cannot substitute for a committed resource application receipt");
            Conserved("intent-only stop");
        }
    }
}
