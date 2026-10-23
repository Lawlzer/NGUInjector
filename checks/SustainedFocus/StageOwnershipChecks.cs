using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class StageOwnershipChecks
    {
        private const string Context = "stage-ownership:entry-1";
        private static int _assertions;

        private static void Assert(bool condition, string name)
        {
            _assertions++;
            if (!condition) throw new Exception(name);
        }

        private static FocusCharacter Fresh()
        {
            Main.Settings = new FocusSettings();
            Main.reference = new Main();
            Main.IronPillBloodReserved = false;
            var c = Main.Character = new FocusCharacter {idleEnergy = 1000, GoldPerSecond = 1000};
            c.magic.idleMagic = 700;
            c.rebirthTime.totalseconds = 100;
            c.totalPlaytime.totalseconds = 200;
            c.buttons.bloodMagic.interactable = true;
            for (var i = 0; i < 8; i++) c.bloodMagicController.normalSpeedDividers[i] = .001f;
            CustomStageAllocator.ResetResourceTargets();
            return c;
        }

        private static CustomPlanStage Stage()
        {
            return new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.EnergyNgu, MagicFocus = (int) CustomStageFocus.MagicNgu};
        }

        private static void Apply(CustomPlanStage stage = null, string context = Context)
        {
            CustomStageAllocator.ApplyStage(new CustomPlan {AutoIronPillEnabled = false}, stage ?? Stage(), 90, 0, context, 120);
        }

        internal static int Run()
        {
            var settings = Main.Settings;
            var character = Main.Character;
            var owner = Main.reference;
            var reserved = Main.IronPillBloodReserved;
            _assertions = 0;
            try
            {
                SelectionDoesNotCreateReceipt();
                ResourceProducerIsReadOnly();
                SupersededResourceRequirementsCannotApply();
                foreach (var target in new[] {ResourceTarget.EnergyNgu, ResourceTarget.AdvancedTraining, ResourceTarget.Augment,
                    ResourceTarget.AugmentUpgrade, ResourceTarget.TimeMachineEnergy, ResourceTarget.WandoosEnergy,
                    ResourceTarget.WishEnergy, ResourceTarget.BasicAttack, ResourceTarget.BasicDefense,
                    ResourceTarget.MagicNgu, ResourceTarget.BloodRitual, ResourceTarget.TimeMachineMagic,
                    ResourceTarget.WandoosMagic, ResourceTarget.WishMagic})
                    ChangedDestinationDisarms(target);
                PassiveGrowthRetainsOwnership();
                ContextKeysDoNotInheritOwnership();
                foreach (var energy in new[] {true, false})
                {
                    ManagedFlagsDisarm(energy);
                    UnmanagedDestinationsAreNotOwned(energy);
                    UnchangedMapNeverCallsThrowingUi(energy);
                }
                ManagedEmptyLaneIsObserved();
                foreach (var change in new[] {"character", "stats", "rebirth", "rollback", "origin", "inventory"})
                    ChangedEpochDisarms(change);
                UnavailableModelCannotRetainReceipt();
                FailedObservationCannotRetainReceipt();
                MissingManagedModelsDisarm();
                FailedCaptureCannotIssueReceipt();
                RejectedAttemptClearsReceipt();
                ResetKeepsTemporaryDemandCleanupSeparate();
                Console.WriteLine("PASS: ongoing stage ownership: " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                Main.Settings = settings;
                Main.Character = character;
                Main.reference = owner;
                Main.IronPillBloodReserved = reserved;
                CustomStageAllocator.ResetResourceTargets();
            }
        }

        private static void SelectionDoesNotCreateReceipt()
        {
            var c = Fresh();
            Assert(CustomStageAllocator.StageApplicationEligible(new CustomPlan(), Stage(), 90, 120), "fixture is eligible before application");
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "read-only eligibility cannot create observed ownership");
            Assert(c.idleEnergy == 1000 && c.magic.idleMagic == 700 && c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                "checking missing ownership never allocates or recalls resources");
            Apply();
            Assert(CustomStageAllocator.LastStageApplicationSucceeded && CustomStageAllocator.StageApplicationStillOwned(Context),
                "only the successful actual application establishes ongoing ownership");
        }

        private static void ResourceProducerIsReadOnly()
        {
            var c = Fresh();
            c.training.attackEnergy[0] = 13;
            c.idleEnergy -= 13;
            c.res3.curRes3 = 47; c.res3.idleRes3 = 17; c.wishes.wishes[0].res3 = 30;
            var stage = Stage(); var plan = new CustomPlan {AutoIronPillEnabled = false};
            var requirements = CustomStageAllocator.ResolveStageResources(plan, stage, 90, Context, 120);
            Assert(c.idleEnergy == 987 && c.magic.idleMagic == 700 && c.NGU.skills.All(x => x.energy == 0)
                && c.NGU.magicSkills.All(x => x.magic == 0), "resolving inline requirements performs no native resource writes");
            Assert(!CustomStageAllocator.LastStageApplicationSucceeded && !CustomStageAllocator.StageApplicationStillOwned(Context),
                "resolved requirements alone cannot issue stage credit");
            Assert(requirements.Intent.Owner == "inline-stage:" + Context && !requirements.Intent.Res3
                && !requirements.Intent.Desired.ContainsKey(ResourceTarget.BasicAttack)
                && !requirements.Intent.Desired.ContainsKey(ResourceTarget.BasicDefense),
                "inline producer retains exact ownership and excludes R3 and basic training");
            CustomStageAllocator.ApplyStageResources(requirements);
            var executor = (ResourceSetupExecutor) typeof(Main).GetField("_sharedResourceSetup",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Main.reference);
            Assert(executor.Current.Owner == requirements.Intent.Owner
                && ReferenceEquals(executor.Application.Last, CustomStageAllocator.LastStageResourceOutcome),
                "inline resources use the same retained executor and bounded application evidence as focus");
            Assert(c.NGU.skills.Sum(x => x.energy) == 987 && c.NGU.magicSkills.Sum(x => x.magic) == 700
                && c.training.attackEnergy[0] == 13 && c.res3.curRes3 == 47 && c.res3.idleRes3 == 17
                && c.wishes.wishes[0].res3 == 30, "shared inline execution preserves basic training and unmanaged R3 accounting");
            Assert(CustomStageAllocator.LastStageApplicationSucceeded && CustomStageAllocator.StageApplicationStillOwned(Context),
                "only the shared verified resource outcome admits ongoing stage ownership");
            CustomStageAllocator.ResetResourceTargets();
            Assert(ReferenceEquals(executor, typeof(Main).GetField("_sharedResourceSetup",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(Main.reference)),
                "stage target reset never replaces the shared native resource application");
            Apply(stage);
            Assert(ReferenceEquals(executor.Application.Last, CustomStageAllocator.LastStageResourceOutcome)
                && CustomStageAllocator.LastStageResourceOutcome.ModelWrites == 0,
                "reacquiring unchanged stage requirements uses shared evidence without repeated model accounting");
        }

        private static void SupersededResourceRequirementsCannotApply()
        {
            var c = Fresh(); var plan = new CustomPlan {AutoIronPillEnabled = false}; var stage = Stage();
            var a = CustomStageAllocator.ResolveStageResources(plan, stage, 90, Context, 120);
            var b = CustomStageAllocator.ResolveStageResources(plan, stage, 80, Context, 120);
            var rejected = false;
            try { CustomStageAllocator.ApplyStageResources(a); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("superseded"); }
            Assert(rejected && c.idleEnergy == 1000 && c.magic.idleMagic == 700,
                "same-entry superseded requirements reject before any native accounting");
            Assert(!CustomStageAllocator.LastStageApplicationSucceeded, "superseded requirements cannot manufacture stage credit");
            CustomStageAllocator.ApplyStageResources(b);
            Assert(CustomStageAllocator.LastStageApplicationSucceeded, "the current same-entry requirements can verify without a replacement owner");
        }

        private static void ChangedDestinationDisarms(ResourceTarget target)
        {
            var c = Fresh();
            Apply();
            var energy = target != ResourceTarget.MagicNgu && target != ResourceTarget.BloodRitual
                && target != ResourceTarget.TimeMachineMagic && target != ResourceTarget.WandoosMagic && target != ResourceTarget.WishMagic;
            var source = energy ? ResourceTarget.EnergyNgu : ResourceTarget.MagicNgu;
            var sourceIndex = energy ? 4 : 0;
            var targetIndex = target == ResourceTarget.EnergyNgu ? 5 : target == ResourceTarget.MagicNgu ? 1 : 0;
            var amount = ResourceWriter.RecallTarget(source, sourceIndex);
            ResourceWriter.Transfer(target, targetIndex, 1);
            ResourceWriter.Transfer(source, sourceIndex, amount - 1);
            Assert(CustomStageAllocator.StageApplicationEligible(new CustomPlan(), Stage(), 90, 120),
                target + ": external native allocation changes do not remove primary availability");

            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), target + ": changed native destination disarms an otherwise eligible stage");

            Assert(c.curEnergy == 1000 && c.magic.idleMagic == 0 && c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                target + ": observing interference does not recall, redistribute or restore resources");
            ResourceWriter.RecallTarget(target, targetIndex);
            ResourceWriter.Transfer(source, sourceIndex, 1);
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), target + ": restoring old amounts cannot resurrect an invalidated receipt");
            Apply();
            Assert(CustomStageAllocator.StageApplicationStillOwned(Context), target + ": a new successful pass can establish ownership again");
        }

        private static void PassiveGrowthRetainsOwnership()
        {
            var c = Fresh();
            Apply();
            c.idleEnergy += 17;
            c.magic.idleMagic += 23;
            c.magic.curMagic = 723;
            c.NGU.skills[4].level = 2000;
            c.NGU.magicSkills[0].level = 3000;
            c.inventory.macguffinBonuses[0] = 2;
            c.rebirthTime.totalseconds += 3;
            c.totalPlaytime.totalseconds += 3;
            c.res3.curRes3 = c.res3.idleRes3 = 20;
            c.hacks.hacks[0].res3 = 1;
            c.res3.idleRes3--;

            Assert(CustomStageAllocator.StageApplicationStillOwned(Context), "idle generation, growing cap inputs, native levels and normal clocks retain ownership");
            Assert(c.idleEnergy == 17 && c.magic.idleMagic == 23 && c.NGU.skills.Sum(x => x.energy) == 1000
                && c.NGU.magicSkills.Sum(x => x.magic) == 700 && c.EnergyRecalls == 0 && c.MagicRecalls == 0,
                "ongoing observation does not consume generated idle resources or recompute caps");
        }

        private static void ContextKeysDoNotInheritOwnership()
        {
            Fresh();
            Apply();
            Assert(!CustomStageAllocator.StageApplicationStillOwned("stage-ownership:entry-2"), "a new entry key cannot inherit an existing allocation receipt");
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context.ToUpperInvariant()), "resource context keys are ordinal and exact");
            Assert(CustomStageAllocator.StageApplicationStillOwned(Context), "querying another key does not alter ownership of the actual applied entry");
            Apply(context: "stage-ownership:entry-2");
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context)
                && CustomStageAllocator.StageApplicationStillOwned("stage-ownership:entry-2"), "successful handoff replaces rather than shares the observed receipt");
        }

        private static void ManagedFlagsDisarm(bool energy)
        {
            Fresh();
            Apply();
            if (energy) Main.Settings.ManageEnergy = false; else Main.Settings.ManageMagic = false;
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "changing managed " + (energy ? "energy" : "magic") + " disarms ongoing ownership");
            if (energy) Main.Settings.ManageEnergy = true; else Main.Settings.ManageMagic = true;
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "re-enabling a managed lane cannot revive its old receipt");
        }

        private static void UnmanagedDestinationsAreNotOwned(bool energy)
        {
            var c = Fresh();
            Main.Settings.ManageEnergy = energy;
            Main.Settings.ManageMagic = !energy;
            var stage = new CustomPlanStage {EnergyFocus = energy ? (int) CustomStageFocus.EnergyNgu : 0,
                MagicFocus = energy ? 0 : (int) CustomStageFocus.MagicNgu};
            Apply(stage);
            if (energy) { c.NGU.magicSkills[1].magic++; c.magic.idleMagic--; }
            else { c.NGU.skills[5].energy++; c.idleEnergy--; }
            Assert(CustomStageAllocator.StageApplicationStillOwned(Context), "changes to an unmanaged " + (energy ? "magic" : "energy") + " lane are not claimed by the stage");
            Main.Settings.ManageEnergy = Main.Settings.ManageMagic = true;
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "newly enabling a previously unobserved lane requires an actual application");
        }

        private static void ManagedEmptyLaneIsObserved()
        {
            var c = Fresh();
            Apply(new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.EnergyNgu});
            c.NGU.magicSkills[1].magic++;
            c.magic.idleMagic--;
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "a managed destination map is observed even when its baseline requested no work");
        }

        private static void ChangedEpochDisarms(string change)
        {
            var c = Fresh();
            Apply();
            if (change == "character")
            {
                var other = new FocusCharacter();
                for (var i = 0; i < c.NGU.skills.Count; i++) other.NGU.skills[i].energy = c.NGU.skills[i].energy;
                for (var i = 0; i < c.NGU.magicSkills.Count; i++) other.NGU.magicSkills[i].magic = c.NGU.magicSkills[i].magic;
                other.rebirthTime.totalseconds = c.rebirthTime.totalseconds;
                other.totalPlaytime.totalseconds = c.totalPlaytime.totalseconds;
                Main.Character = other;
            }
            else if (change == "stats") c.stats = new FocusStats();
            else if (change == "rebirth") c.stats.rebirthNumber++;
            else if (change == "rollback") { c.rebirthTime.totalseconds--; c.totalPlaytime.totalseconds--; }
            else if (change == "origin") c.totalPlaytime.totalseconds += 5;
            else c.inventory = new Inventory();
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), change + ": a new save/epoch cannot inherit identical destination amounts");
            Assert(c.NGU.skills.Sum(x => x.energy) == 1000 && c.NGU.magicSkills.Sum(x => x.magic) == 700,
                change + ": epoch rejection does not restore or recall old native allocations");
        }

        private static void UnchangedMapNeverCallsThrowingUi(bool energy)
        {
            var c = Fresh();
            Apply();
            var bar = energy ? c.NGUController.NGU[4] : c.NGUController.NGUMagic[0];
            bar.ThrowOnUpdate = true;
            var failed = false;
            try { Apply(); }
            catch (InvalidOperationException error) { failed = error.Message == "NGU UI unavailable"; }
            bar.ThrowOnUpdate = false;
            Assert(!failed && CustomStageAllocator.LastStageApplicationSucceeded && CustomStageAllocator.StageApplicationStillOwned(Context)
                && CustomStageAllocator.LastStageResourceOutcome.ModelWrites == 0,
                (energy ? "energy" : "magic") + ": unchanged desired map does not invoke a broken renderer or invalidate verified ownership");
            Assert(c.curEnergy == 1000 && c.magic.idleMagic + c.NGU.magicSkills.Sum(x => x.magic) == 700,
                "a throwing allocation never restores only one side of a native transfer");
        }

        private static void UnavailableModelCannotRetainReceipt()
        {
            var c = Fresh();
            Apply();
            var ngu = c.NGU;
            c.NGU = null;
            try { Apply(); }
            catch (NullReferenceException) { }
            finally { c.NGU = ngu; }
            Assert(!CustomStageAllocator.LastStageApplicationSucceeded && !CustomStageAllocator.StageApplicationStillOwned(Context),
                "unavailable native NGU data cannot retain an old receipt; a zero-budget calculation need not call an obsolete recall");
            Assert(c.NGU.skills.Sum(x => x.energy) == 1000 && c.NGU.magicSkills.Sum(x => x.magic) == 700,
                "read-only rejection preserves every previously allocated resource despite temporarily unavailable getters");
        }

        private static void FailedObservationCannotRetainReceipt()
        {
            var c = Fresh();
            Apply();
            var skill = c.NGU.skills[0];
            c.NGU.skills[0] = null;
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "unreadable managed destination maps fail closed without escaping the ownership predicate");
            c.NGU.skills[0] = skill;
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "restoring model readability does not manufacture an applied receipt");
        }

        private static void FailedCaptureCannotIssueReceipt()
        {
            var c = Fresh();
            Apply();
            var stats = c.stats;
            c.stats = null;
            var failed = false;
            try { Apply(); }
            catch (NullReferenceException) { failed = true; }
            finally { c.stats = stats; }
            Assert(failed && !CustomStageAllocator.LastStageApplicationSucceeded && !CustomStageAllocator.StageApplicationStillOwned(Context),
                "failed post-application epoch capture cannot leave a successful application flag or receipt");
        }

        private static void MissingManagedModelsDisarm()
        {
            foreach (var machine in new[] {true, false})
            {
                var c = Fresh();
                Apply();
                var tm = c.machine;
                var wan = c.wandoos98;
                if (machine) c.machine = null; else c.wandoos98 = null;
                Assert(!CustomStageAllocator.StageApplicationStillOwned(Context),
                    "a missing managed " + (machine ? "Time Machine" : "Wandoos") + " model is not an observed zero allocation");
                c.machine = tm;
                c.wandoos98 = wan;
                Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "restoring a missing destination model still requires a new application");
            }
        }

        private static void RejectedAttemptClearsReceipt()
        {
            Fresh();
            Apply();
            CustomStageAllocator.ApplyStage(new CustomPlan(), null, 90, 0, Context, 120);
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context), "early null-stage return invalidates the earlier receipt");
            Apply();
            var failed = false;
            try { CustomStageAllocator.ApplyStage(null, Stage(), 90, 0, Context, 120); }
            catch (InvalidOperationException) { failed = true; }
            Assert(failed && !CustomStageAllocator.StageApplicationStillOwned(Context), "pre-allocation plan rejection invalidates the earlier receipt");
            Apply();
            Main.Settings.ManageMagic = false;
            Apply();
            Assert(!CustomStageAllocator.LastStageApplicationSucceeded && !CustomStageAllocator.StageApplicationStillOwned(Context),
                "partial managed application never captures ongoing ownership");
        }

        private static void ResetKeepsTemporaryDemandCleanupSeparate()
        {
            var c = Fresh();
            var stage = new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.EnergyNgu};
            Main.IronPillBloodReserved = true;
            Apply(stage);
            Assert(CustomStageAllocator.StageApplicationStillOwned(Context) && c.bloodMagic.ritual.Sum(x => x.magic) == 51,
                "a successful temporary-demand pass has both application ownership and its separate cleanup receipt");
            CustomStageAllocator.ResetResourceTargets();
            Assert(!CustomStageAllocator.StageApplicationStillOwned(Context) && !CustomStageAllocator.LastStageApplicationSucceeded,
                "reset invalidates verification-only application ownership");
            Assert(c.bloodMagic.ritual.Sum(x => x.magic) == 51, "reset does not mutate the temporary allocation or discard its pending cleanup");
            Main.IronPillBloodReserved = false;
            Apply(stage);
            Assert(c.bloodMagic.ritual.Sum(x => x.magic) == 0 && c.magic.idleMagic == 700 && c.MagicRecalls == 0,
                "the independent temporary receipt still releases its exact allocation after application ownership was reset");
            Assert(CustomStageAllocator.StageApplicationStillOwned(Context), "successful current-baseline application establishes a new receipt after cleanup");
        }
    }
}
