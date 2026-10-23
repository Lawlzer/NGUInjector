using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class IronPillAllocationChecks
    {
        private static int _assertions;
        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static FocusCharacter Allocate(CustomPlanStage stage, bool saving)
        {
            Main.reference = new Main();
            var c = new FocusCharacter();
            Main.Character = c;
            Main.IronPillBloodReserved = saving;
            c.idleEnergy = 1000;
            c.magic.idleMagic = 1000;
            c.GoldPerSecond = 1000;
            c.buttons.bloodMagic.interactable = c.buttons.wandoos.interactable = true;
            c.buttons.advancedTraining.interactable = true;
            for (var i = 0; i < 8; i++) c.bloodMagicController.normalSpeedDividers[i] = .001f;
            CustomStageAllocator.ResetResourceTargets();
            CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0, 0, "pill-allocation-check");
            return c;
        }

        internal static int Run()
        {
            var settings = Main.Settings;
            var character = Main.Character;
            var owner = Main.reference;
            try
            {
                var stage = new CustomPlanStage {
                    EnergyFocus = (int) CustomStageFocus.EnergyNgu,
                    MagicFocus = (int) CustomStageFocus.MagicNgu,
                    GearGoal = (int) GearOptimizationGoal.AllNgu,
                    MagicPriorities = new[] {"NGU", "RIT-0", "RIT-1", "WAN"}
                };
                var saved = stage.MagicPriorities.ToArray();
                var ordinary = Allocate(stage, false);
                var ordinarySetup = Main.SetupForStage(stage);
                Equal(1000L, ordinary.NGU.magicSkills[0].magic, "ordinary stage allocates magic to NGU first");
                var saving = Allocate(stage, true);
                var savingSetup = Main.SetupForStage(stage);
                Equal(true, ReferenceEquals(stage, savingSetup.Stage), "equipment setup retains the saved stage while reserving");
                Equal(string.Join(",", ordinarySetup.Diggers.Select(x => x.Id + ":" + x.Weight)),
                    string.Join(",", savingSetup.Diggers.Select(x => x.Id + ":" + x.Weight)), "magic overlay does not replace the stage's equipment support intent");
                Equal(51L, saving.bloodMagic.ritual.Sum(r => r.magic), "saving promotes one real optimized ritual at allocation boundary");
                Equal(949L, saving.NGU.magicSkills[0].magic, "NGU gets ritual overflow only");
                Equal(1, saving.bloodMagic.ritual.Count(r => r.magic > 0), "explicit RIT-n entries cannot add extra gold consumers");
                Equal(7, saving.bloodMagic.ritual.FindIndex(r => r.magic > 0), "real optimizer chooses its deterministic best ritual");
                Equal(string.Join(",", ordinary.NGU.skills.Select(x => x.energy)), string.Join(",", saving.NGU.skills.Select(x => x.energy)), "overlay leaves stage energy allocation identical");
                Equal(1000L, saving.NGU.skills.Sum(x => x.energy), "active energy stage continues running");
                Equal(0, saving.EnergyRecalls, "ordinary stage applies desired energy differences without native recalls");
                Equal((int) GearOptimizationGoal.AllNgu, stage.GearGoal, "allocation overlay does not replace gear goal");
                Equal(string.Join(",", saved), string.Join(",", stage.MagicPriorities), "allocation keeps saved array immutable");
                stage.MagicFocus = (int) CustomStageFocus.Wandoos;
                saving = Allocate(stage, true);
                Equal(2L, saving.wandoos98.wandoosMagic, "Wandoos caps WAN before ritual");
                Equal(51L, saving.bloodMagic.ritual.Sum(r => r.magic), "Wandoos overflow reaches RIT second");
                Equal(947L, saving.NGU.magicSkills[0].magic, "Wandoos remaining magic retains original NGU fallback");
                // Reapply in the same context, as the quick refresh does. No extra ritual or pool growth.
                CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0, 0, "pill-allocation-check");
                Equal(1000L, saving.wandoos98.wandoosMagic + saving.bloodMagic.ritual.Sum(r => r.magic) + saving.NGU.magicSkills.Sum(x => x.magic) + saving.magic.idleMagic, "quick refresh conserves the entire magic pool");
                Equal(1, saving.bloodMagic.ritual.Count(r => r.magic > 0), "quick refresh retains one gold consumer");
                Equal(2L, saving.wandoos98.wandoosMagic, "quick refresh cannot bypass Wandoos overlay");
                Equal(51L, saving.bloodMagic.ritual.Sum(r => r.magic), "quick refresh cannot bypass ritual overlay");
                Main.IronPillBloodReserved = false;
                CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0, 0, "pill-allocation-check");
                Equal(1000L, saving.NGU.magicSkills[0].magic, "ending reservation restores original allocation order");
                Equal(0L, saving.bloodMagic.ritual.Sum(r => r.magic), "ending reservation releases ritual magic");
                Equal(0L, saving.wandoos98.wandoosMagic, "saved NGU-first order restored even on Wandoos focus override");
                stage.CompletionMode = (int) CustomStageCompletionMode.AdvancedTrainingStats;
                stage.TargetAdvancedTrainingPower = 100;
                stage.TargetAdvancedTrainingToughness = 200;
                stage.EnergyPriorities = new[] {"AT"};
                saving = Allocate(stage, true);
                Equal(1000L, saving.advancedTraining.energy.Sum(), "AT energy still allocates while blood is reserved");
                Equal(0L, saving.advancedTraining.levelTarget[1], "AT power menu target restored after stage-target allocation");
                Equal(0L, saving.advancedTraining.levelTarget[0], "AT toughness menu target restored after stage-target allocation");
                Equal(100L, stage.TargetAdvancedTrainingPower, "reservation leaves configured AT power target intact");
                Equal(200L, stage.TargetAdvancedTrainingToughness, "reservation leaves configured AT toughness target intact");
                EmptyBaselineReleasesOnlyTemporaryRitual();
                ManualRitualEditIsPreserved();
                OtherMagicEditsArePreserved();
                EndingDemandUsesLatestBaseline();
                foreach (var change in new[] {"character", "rebirth", "ritual"})
                    ChangedEpochOrRitualIsNotRecalled(change);
                ResetRetainsUnmanagedCleanup();
                RejectedReleaseRetainsBorrowEvidence();
                foreach (var manualReuse in new[] {false, true})
                    ThrowingCleanupIsIdempotent(manualReuse);
                ThrowingAllocationRetainsCleanupReceipt();
                _assertions += StageOwnershipChecks.Run();
                Console.WriteLine("PASS: Iron Pill allocation and demand cleanup: " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                Main.IronPillBloodReserved = false;
                Main.Settings = settings;
                Main.Character = character;
                Main.reference = owner;
                CustomStageAllocator.ResetResourceTargets();
            }
        }

        private static FocusCharacter StartEmptyDemand(out CustomPlanStage stage)
        {
            Main.Settings = new FocusSettings();
            Main.reference = new Main();
            stage = new CustomPlanStage();
            return Allocate(stage, true);
        }

        private static void Apply(CustomPlanStage stage)
        {
            CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0, 0, "pill-allocation-check");
        }

        private static void MagicConserved(FocusCharacter c, string name)
        {
            Equal(1000L, c.magic.idleMagic + c.NGU.magicSkills.Sum(x => x.magic) + c.bloodMagic.ritual.Sum(x => x.magic)
                + c.wishes.wishes.Sum(x => x.magic) + c.machine.goldMultiMagic + c.wandoos98.wandoosMagic, name);
        }

        private static void EmptyBaselineReleasesOnlyTemporaryRitual()
        {
            var c = StartEmptyDemand(out var stage);
            Equal(51L, c.bloodMagic.ritual[7].magic, "empty magic baseline still funds the live Iron Pill ritual");
            Equal(949L, c.magic.idleMagic, "empty baseline leaves non-ritual magic idle while demand is active");
            Main.IronPillBloodReserved = false;

            Apply(stage);

            Equal(0L, c.bloodMagic.ritual.Sum(x => x.magic), "ending demand releases owned ritual even when latest baseline is empty");
            Equal(1000L, c.magic.idleMagic, "empty baseline receives the exact ritual refund without restoring an allocation snapshot");
            Equal(1000L, c.idleEnergy, "temporary magic demand leaves all energy idle and unchanged");
            Equal(0, c.EnergyRecalls, "empty energy baseline is never recalled for magic cleanup");
            Equal(0, c.MagicRecalls, "ending empty-baseline demand uses an exact target difference, never a native recall");
            Apply(stage);
            Equal(1000L, c.magic.idleMagic, "repeated demand-off passes cannot refund the ritual twice");
            MagicConserved(c, "empty-baseline demand removal conserves magic");
        }

        private static void ManualRitualEditIsPreserved()
        {
            var c = StartEmptyDemand(out var stage);
            c.bloodMagic.ritual[7].magic += 13;
            c.magic.idleMagic -= 13;
            // A new baseline must not turn cleanup into permission to recall this foreign edit.
            stage.MagicFocus = (int) CustomStageFocus.MagicNgu;
            Main.IronPillBloodReserved = false;

            var message = CustomStageAllocator.ApplyStage(new CustomPlan(), stage, 0);

            Equal(64L, c.bloodMagic.ritual[7].magic, "cleanup cannot recall a ritual whose allocation was manually changed");
            Equal(936L, c.magic.idleMagic, "foreign ritual edit retains its current idle balance");
            Equal(0L, c.NGU.magicSkills.Sum(x => x.magic), "changed baseline is deferred rather than overwriting a foreign ritual edit");
            Equal(true, message.Contains("baseline refresh deferred"), "foreign lane edit reports the deferred baseline refresh");
            Equal(false, CustomStageAllocator.LastStageApplicationSucceeded, "a deferred magic baseline cannot issue an applied stage receipt");
            Equal(0, c.MagicRecalls, "manual ritual ownership loss never triggers a whole-lane recall");
            MagicConserved(c, "manual ritual edit survives demand removal with exact conservation");
        }

        private static void OtherMagicEditsArePreserved()
        {
            var c = StartEmptyDemand(out var stage);
            c.NGU.magicSkills[2].magic = 29;
            c.wishes.wishes[0].magic = 31;
            c.wandoos98.wandoosMagic = 37;
            c.machine.goldMultiMagic = 41;
            c.bloodMagic.ritual[0].magic = 43;
            c.magic.idleMagic -= 181;
            stage.MagicFocus = (int) CustomStageFocus.MagicNgu;
            Main.IronPillBloodReserved = false;

            Apply(stage);

            Equal(0L, c.bloodMagic.ritual[7].magic, "unrelated manual edits do not prevent releasing the exact still-owned ritual");
            Equal("29,31,37,41,43", string.Join(",", new[] {c.NGU.magicSkills[2].magic, c.wishes.wishes[0].magic,
                c.wandoos98.wandoosMagic, c.machine.goldMultiMagic, c.bloodMagic.ritual[0].magic}),
                "cleanup retains current manual NGU, wish, Wandoos, Time Machine and unrelated ritual allocations");
            Equal(819L, c.magic.idleMagic, "cleanup refunds only the owned 51 magic after unrelated lane edits");
            Equal(0L, c.NGU.magicSkills[0].magic, "cleanup cannot fund a new baseline over a manually edited lane");
            Equal(0, c.MagicRecalls, "manual lane edits prohibit broad native recall during cleanup");
            MagicConserved(c, "all manually edited magic destinations remain conserved");
        }

        private static void EndingDemandUsesLatestBaseline()
        {
            var c = StartEmptyDemand(out var stage);
            Main.Settings.MagicNguWeights = new[] {0, 1, 1, 0, 0, 0, 0};
            stage.MagicFocus = (int) CustomStageFocus.MagicNgu;
            stage.MagicPriorities = new[] {"NGU-1"};
            Apply(stage);
            Equal(949L, c.NGU.magicSkills[1].magic, "editing the baseline while demand remains active composes the new NGU target");
            Equal(51L, c.bloodMagic.ritual[7].magic, "new baseline keeps the temporary ritual while demand is live");
            stage.MagicPriorities = new[] {"NGU-2"};
            Main.IronPillBloodReserved = false;
            CustomStageAllocator.ResetResourceTargets();

            Apply(stage);

            Equal(1000L, c.NGU.magicSkills[2].magic, "demand removal recomputes the latest baseline instead of restoring a saved priority array");
            Equal(0L, c.NGU.magicSkills[1].magic, "baseline from the earlier live-demand refresh is not restored");
            Equal(0L, c.bloodMagic.ritual.Sum(x => x.magic), "latest NGU baseline has no stale temporary ritual");
            Equal("NGU-2", stage.MagicPriorities.Single(), "cleanup leaves the newly selected baseline definition unchanged");
            MagicConserved(c, "latest baseline application conserves the live magic pool");
        }

        private static void ChangedEpochOrRitualIsNotRecalled(string change)
        {
            var original = StartEmptyDemand(out var stage);
            var c = original;
            if (change == "character")
            {
                c = Main.Character = new FocusCharacter {idleEnergy = 1000};
                c.magic.idleMagic = 949;
                c.bloodMagic.ritual[7].magic = 51;
            }
            else if (change == "rebirth") c.stats.rebirthNumber++;
            else c.bloodMagic.ritual[7] = new Ritual {magic = 51};
            Main.IronPillBloodReserved = false;
            CustomStageAllocator.ResetResourceTargets();
            var recalls = c.MagicRecalls;

            Apply(stage);

            Equal(51L, c.bloodMagic.ritual[7].magic, change + ": old ownership cannot recall an identical amount in a new context");
            Equal(949L, c.magic.idleMagic, change + ": epoch or native ritual replacement cannot credit old resources to the current idle pool");
            Equal(recalls, c.MagicRecalls, change + ": empty baseline performs no full native recall after context replacement");
            Equal(51L, original.bloodMagic.ritual[7].magic, change + ": stale cleanup never writes the old character or retired allocation");
            MagicConserved(c, change + ": current save's magic is conserved");
        }

        private static void ResetRetainsUnmanagedCleanup()
        {
            var c = StartEmptyDemand(out var stage);
            Main.Settings.ManageMagic = false;
            Main.IronPillBloodReserved = false;
            CustomStageAllocator.ResetResourceTargets();
            Apply(stage);
            Equal(51L, c.bloodMagic.ritual[7].magic, "disabled managed magic prohibits temporary ritual cleanup writes");
            Equal(949L, c.magic.idleMagic, "unmanaged cleanup leaves the entire current magic pool untouched");
            Equal(0, c.MagicRecalls, "unmanaged cleanup performs no native lane recall");
            Main.Settings.ManageMagic = true;
            CustomStageAllocator.ResetResourceTargets();

            Apply(stage);

            Equal(0L, c.bloodMagic.ritual[7].magic, "optimizer resets retain the ownership receipt until managed cleanup can release safely");
            Equal(1000L, c.magic.idleMagic, "re-enabled managed cleanup returns the exact still-owned ritual allocation");
            MagicConserved(c, "deferred unmanaged cleanup conserves magic when resumed");
        }

        private static void ThrowingCleanupIsIdempotent(bool manualReuse)
        {
            var c = StartEmptyDemand(out var stage);
            stage.EnergyFocus = (int) CustomStageFocus.EnergyNgu;
            var controller = c.bloodMagicController.bloodMagics[7];
            c.bloodMagicController.bloodMagics[7] = null; // The native render boundary fails after the exact recall commits.
            Main.IronPillBloodReserved = false;
            var failed = false;
            try { Apply(stage); }
            catch (NullReferenceException) { failed = true; }
            finally { c.bloodMagicController.bloodMagics[7] = controller; }
            Equal(false, failed, "temporary ritual release reports rendering separately after complete model accounting");
            Equal(true, CustomStageAllocator.LastStageApplicationSucceeded, "verified accounting, not rendering success, admits applied stage credit");
            Equal(MutationDisposition.ModelAppliedRenderFailed, CustomStageAllocator.LastStageResourceOutcome.Disposition, "positive control: the missing native renderer was actually attempted");
            Equal(0L, c.bloodMagic.ritual[7].magic, "throwing renderer cannot roll back the committed ritual release");
            Equal(1000L, c.magic.idleMagic, "throwing renderer cannot split or duplicate the committed idle refund");
            Equal(0, c.EnergyRecalls, "failed cleanup stops before partially beginning the energy allocation pass");
            if (manualReuse) ResourceWriter.Transfer(ResourceTarget.BloodRitual, 7, 51);
            CustomStageAllocator.ResetResourceTargets();

            Apply(stage);
            Apply(stage);

            Equal(manualReuse ? 51L : 0L, c.bloodMagic.ritual[7].magic,
                "cleanup retry never mistakes a manual reuse of the released bar for its original allocation");
            Equal(manualReuse ? 949L : 1000L, c.magic.idleMagic, "repeated cleanup retry never refunds the same allocation twice");
            Equal(0, c.MagicRecalls, "throwing cleanup and its retries never broadly recall the magic lane");
            Equal(1000L, c.curEnergy, "cleanup retry conserves energy while resuming its current baseline");
            MagicConserved(c, "post-commit rendering failure and retry conserve the complete magic pool");
        }

        private static void RejectedReleaseRetainsBorrowEvidence()
        {
            var c = StartEmptyDemand(out var stage); Main.IronPillBloodReserved = false;
            LoadoutManager.CurrentLock = LockType.Gold;
            try
            {
                Apply(stage);
                Equal(MutationDisposition.AppliedVerified, CustomStageAllocator.LastStageResourceOutcome.Disposition, "a gear-only lock cannot block an independently authorized Magic return");
                Equal(0L, c.bloodMagic.ritual[7].magic, "exact authorized release zeroes only the borrowed target");
                Equal(1000L, c.magic.idleMagic, "exact release credits the performed return once");
                Equal(false, CustomStageAllocator.LastStageApplicationSucceeded, "independent resource service does not fabricate full coupled-setup credit");
            }
            finally { LoadoutManager.CurrentLock = LockType.None; }
            Apply(stage);
            Equal(0L, c.bloodMagic.ritual[7].magic, "readmitted release finishes the exact still-owned difference");
            Equal(1000L, c.magic.idleMagic, "readmitted release returns the borrow exactly once");
            Apply(stage);
            Equal(1000L, c.magic.idleMagic, "subsequent unchanged passes cannot refund the borrow again");
        }

        private static void ThrowingAllocationRetainsCleanupReceipt()
        {
            var c = StartEmptyDemand(out var stage);
            stage.MagicFocus = (int) CustomStageFocus.MagicNgu;
            c.NGUController.NGUMagic[0].ThrowOnUpdate = true;
            var failed = false;
            try { Apply(stage); }
            catch (InvalidOperationException error) { failed = error.Message == "NGU UI unavailable"; }
            Equal(false, failed, "verified allocation rendering failure does not masquerade as unknown resource mutation");
            Equal(MutationDisposition.ModelAppliedRenderFailed, CustomStageAllocator.LastStageResourceOutcome.Disposition, "positive control: native allocation renderer actually failed");
            Equal(51L, c.bloodMagic.ritual[7].magic, "partial allocation pass retains its committed temporary ritual");
            c.NGUController.NGUMagic[0].ThrowOnUpdate = false;
            stage.MagicFocus = (int) CustomStageFocus.None;
            Main.IronPillBloodReserved = false;
            CustomStageAllocator.ResetResourceTargets();

            Apply(stage);

            Equal(0L, c.bloodMagic.ritual[7].magic, "failed allocation still records ownership needed by later empty-baseline cleanup");
            Equal(51L, c.magic.idleMagic, "failed-pass cleanup returns only the temporary ritual magic");
            Equal(949L, c.NGU.magicSkills[0].magic, "empty-baseline cleanup does not restore, recall or rewrite other current allocations");
            MagicConserved(c, "allocation-render failure and later cleanup conserve magic");
        }
    }
}
