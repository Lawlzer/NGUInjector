using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        internal string ApplyEquipmentForChecks(CustomEquipmentSetup setup, string owner, DateTime now)
        {
            return ApplyCustomEquipmentSupport(setup, owner, now);
        }

        internal void ResetEquipmentForChecks() { ResetCustomEquipmentSetup(); }
        internal void RefreshFocusEquipmentForChecks() { RunCustomFocusEquipmentRefresh(); }
        internal static SupportSelection CaptureSupportForChecks() { return CaptureSupportState().Selection; }
        internal void SetManualAdventureSetupActiveForChecks(bool active) { _manualAdventureSetupActive = active; }
    }

    internal static class EquipmentSetupChecks
    {
        private static int _assertions;
        private static readonly DateTime Entry = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly int[] BaseIds = {0, 3, 6, 7, 8, 9};
        private static readonly int[] BaseWeights = {20, 20, 15, 15, 15, 15};

        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual);
        }

        private static void Sequence<T>(T[] expected, T[] actual, string name)
        {
            Equal(string.Join(",", expected), string.Join(",", actual), name);
        }

        private static void Weights(Main.CustomEquipmentSetup setup, int[] extraIds, int[] extraWeights, string name)
        {
            var expected = BaseIds.Concat(extraIds).Zip(BaseWeights.Concat(extraWeights), (id, weight) => id + ":" + weight).OrderBy(x => x).ToArray();
            var actual = setup.Diggers.Select(x => x.Id + ":" + x.Weight).OrderBy(x => x).ToArray();
            Sequence(expected, actual, name);
        }

        private static void NoGear(string name)
        {
            Equal(0, EquipmentRecording.Gear.Count, name + ": optimizer calls");
            Equal(0, EquipmentRecording.GearSwaps, name + ": resource-removing gear swaps");
            Equal(0, EquipmentRecording.CapacityChecks.Count, name + ": exact-map preflights");
            Equal(0, EquipmentRecording.ExactApplications.Count, name + ": exact-map applications");
        }

        private static SupportSelection DesiredSupport(Main.CustomEquipmentSetup setup)
        {
            var context = typeof(Main).GetMethod("CaptureSupportContext", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null) as SupportContext;
            var result = SupportProjection.Plan(context, setup.Beards, setup.Diggers.Select(x => new DiggerBudgetRequest {
                Id = x.Id, Weight = x.Weight, Maximum = context.Diggers.Single(d => d.Id == x.Id).Maximum
            }).ToArray(), ActivityPolicy.Current.DiggerBudgetFraction);
            Equal(true, result.Feasible, "real support projection is feasible");
            return result.Selection;
        }

        internal static int Run()
        {
            var previousSettings = Main.Settings;
            var previousCharacter = Main.Character;
            var previousOwner = Main.reference;
            var previousStatus = Main.Status;
            var cases = new Action[] {
                FocusPolicies, ExplicitPriorityOverrides, SavedStageOverridesUnaffected, WandoosDeduplication, StageGearIsIndependent,
                FightPolicy, FocusStageConsistency, SpecialAdventurePolicies,
                ExactSupportLifecycle, ExactSupportRetryAndForeign, SelectionPersistencePrecedesMutation,
                SlowFocusGuardsEntry, SlowFocusGuardsRefresh,
                InactiveManualAdventureAllowsFocusEntry, InactiveManualAdventureAllowsFocusRefresh,
                SlowFocusUsesAppliedOwner, FocusActivationUsesSharedSetup, FocusActivationWaitsForLocks,
                MagicFocusPreservesUnownedAllocations, MagicFocusRejectsCapacityBeforeRecall, MagicFocusRejectsUnaccountedRes3,
                MagicFocusUsesUnmatchedMetadataGear, QuickAllocationMatchesStandardStage, QuickRemainsManualIndefinitely,
                RapidSustainHasNoEquipment
            };
            try
            {
                Main.CustomPlanStore.Save("equipment-setup-checks", new CustomPlan());
                foreach (var check in cases)
                {
                    EquipmentRecording.Reset(true);
                    Main.Settings = new FocusSettings {CustomPlanFile = "equipment-setup-checks"};
                    Main.Character = new FocusCharacter();
                    Main.reference = new Main();
                    Main.Status = "unchanged";
                    try { check(); }
                    catch (Exception error) { throw new Exception(check.Method.Name + " failed", error); }
                }
                Console.WriteLine("PASS: equipment setup: " + cases.Length + " cases, " + _assertions + " assertions.");
                return _assertions;
            }
            finally
            {
                EquipmentRecording.Reset(false);
                Main.Settings = previousSettings;
                Main.Character = previousCharacter;
                Main.reference = previousOwner;
                Main.Status = previousStatus;
            }
        }

        private static void FocusPolicies()
        {
            foreach (CustomFocusGoal goal in Enum.GetValues(typeof(CustomFocusGoal)))
            {
                if (goal == CustomFocusGoal.ManualTitans) continue;
                var setup = Main.SetupForFocus(goal);
                var ids = goal == CustomFocusGoal.EnergyNgu || goal == CustomFocusGoal.Ngu ? new[] {4, 5}
                    : goal == CustomFocusGoal.MagicNgu ? new[] {5}
                    : goal == CustomFocusGoal.AugmentsBloodMagic || goal == CustomFocusGoal.AdvancedTraining || goal == CustomFocusGoal.Adventure ? new[] {10}
                    : goal == CustomFocusGoal.Wandoos ? new[] {1}
                    : goal == CustomFocusGoal.Fight ? new[] {2} : new int[0];
                Weights(setup, ids, ids.Select(id => id == 1 || id == 2 ? 100 : 50).ToArray(), goal + " exact digger policy");
                var required = goal == CustomFocusGoal.Fight ? new[] {0, 1, 3, 5}
                    : goal == CustomFocusGoal.Wandoos ? new[] {4, 1, 3, 5} : new[] {1, 3, 5};
                Sequence(required, setup.Beards, goal + " exact requirements, without automatic Gold or random optional");
                Equal(setup.Beards.Length, setup.Beards.Distinct().Count(), goal + " has no duplicate beard requests");
            }
            var energyNgu = Main.SetupForFocus(CustomFocusGoal.EnergyNgu);
            Equal((int) CustomStageFocus.EnergyNgu, energyNgu.Stage.EnergyFocus, "Energy NGU owns energy NGU");
            Equal((int) CustomStageFocus.MagicNgu, energyNgu.Stage.MagicFocus, "Energy NGU also owns magic NGU");
            var magicNgu = Main.SetupForFocus(CustomFocusGoal.MagicNgu);
            Equal((int) CustomStageFocus.None, magicNgu.Stage.EnergyFocus, "Magic NGU leaves energy unmanaged");
            Equal((int) CustomStageFocus.MagicNgu, magicNgu.Stage.MagicFocus, "Magic NGU owns magic NGU");
            Equal((int) CustomStageFocus.BloodMagic, Main.SetupForFocus(CustomFocusGoal.AdvancedTraining).Stage.MagicFocus, "AT owns blood magic support");
        }

        private static void ExplicitPriorityOverrides()
        {
            var stage = new CustomPlanStage {
                EnergyFocus = (int) CustomStageFocus.Wandoos,
                MagicFocus = (int) CustomStageFocus.Wandoos,
                EnergyPriorities = new[] {null, " RIT-0 ", "NGU-9", " ngu-8 ", "WAN"},
                MagicPriorities = new[] {"AUG-0", "NGU-7", " rit-7 ", "NGU-0", "WAN"}
            };
            var energy = stage.EnergyPriorities.ToArray();
            var magic = stage.MagicPriorities.ToArray();
            Weights(Main.SetupForStage(stage), new[] {4, 10}, new[] {50, 50}, "normalized indexed primaries override Wandoos and ignore invalid lane tokens");
            Sequence(new[] {1, 3, 5}, Main.SetupForStage(stage).Beards, "overridden Wandoos does not require Ladybeard");
            Sequence(energy, stage.EnergyPriorities, "energy priorities remain unchanged");
            Sequence(magic, stage.MagicPriorities, "magic priorities remain unchanged");
            stage.EnergyPriorities = new[] {"TM", "NGU-0", "WAN"};
            stage.MagicPriorities = new[] {" ngu-6 ", "RIT-0", "WAN"};
            Weights(Main.SetupForStage(stage), new[] {5}, new[] {50}, "only first valid primary gets lane support, not fallback NGU/RIT/WAN");
            stage.EnergyPriorities = new[] {"invalid"};
            stage.MagicPriorities = new[] {"RIT-8", "NGU-7"};
            Weights(Main.SetupForStage(stage), new int[0], new int[0], "invalid explicit priorities do not silently restore focus defaults");
            Sequence(new[] {1, 3, 5}, Main.SetupForStage(stage).Beards, "invalid explicit priorities do not restore Ladybeard either");
            stage.EnergyPriorities = new string[0];
            stage.MagicPriorities = null;
            Weights(Main.SetupForStage(stage), new[] {1}, new[] {100}, "empty and null priority arrays use preset defaults");
        }

        private static void WandoosDeduplication()
        {
            foreach (var lanes in new[] {1, 2, 3})
            {
                var stage = new CustomPlanStage {
                    EnergyFocus = (int) ((lanes & 1) != 0 ? CustomStageFocus.Wandoos : CustomStageFocus.None),
                    MagicFocus = (int) ((lanes & 2) != 0 ? CustomStageFocus.Wandoos : CustomStageFocus.None)
                };
                var setup = Main.SetupForStage(stage);
                Weights(setup, new[] {1}, new[] {100}, "Wandoos has one weight-100 digger for lane mask " + lanes);
                Equal(4, setup.Beards[0], "Ladybeard is first for Wandoos lane mask " + lanes);
                Equal(1, setup.Beards.Count(id => id == 4), "Ladybeard is deduplicated for lane mask " + lanes);
            }
        }

        private static void SavedStageOverridesUnaffected()
        {
            var stage = MatchingStandardStage(ReadPolicy(), CustomFocusGoal.TimeMachine);
            stage.EnergyPriorities = new[] {"NGU-8", "AUG", "TM"};
            stage.MagicPriorities = new[] {"RIT-7", "WAN"};
            Main.Settings.CustomPlanFile = "saved-quick-overrides";
            Main.CustomPlanStore.Save(Main.Settings.CustomPlanFile, new CustomPlan {Stages = new[] {stage}});
            var loaded = Main.CustomPlanStore.Load(Main.Settings.CustomPlanFile).Stages.Single();
            var energy = loaded.EnergyPriorities;
            var magic = loaded.MagicPriorities;
            var setup = Main.SetupForStage(loaded);
            Equal(true, ReferenceEquals(loaded, setup.Stage), "ordinary setup retains saved stage identity");
            Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.TimeMachine);
            Equal("applied", Main.reference.GetCustomFocusStatus(), "Quick activation succeeds alongside saved overrides");
            Equal(true, ReferenceEquals(energy, loaded.EnergyPriorities) && ReferenceEquals(magic, loaded.MagicPriorities), "Quick hydration never replaces saved override arrays");
            Sequence(stage.EnergyPriorities, loaded.EnergyPriorities, "in-memory saved energy overrides unchanged");
            Sequence(stage.MagicPriorities, loaded.MagicPriorities, "in-memory saved magic overrides unchanged");
            var reloaded = Main.CustomPlanStore.Load(Main.Settings.CustomPlanFile).Stages.Single();
            Sequence(stage.EnergyPriorities, reloaded.EnergyPriorities, "persisted energy overrides unchanged");
            Sequence(stage.MagicPriorities, reloaded.MagicPriorities, "persisted magic overrides unchanged");
        }

        private static void StageGearIsIndependent()
        {
            EquipmentRecording.BeardMessage = "beards:4,1,3,5";
            foreach (var goal in new[] {-1, (int) GearOptimizationGoal.Fight, (int) GearOptimizationGoal.Gold})
            {
                var stage = new CustomPlanStage {GearGoal = goal, EnergyFocus = (int) CustomStageFocus.Wandoos, MagicFocus = (int) CustomStageFocus.BloodMagic};
                var setup = Main.SetupForStage(stage);
                Equal(true, ReferenceEquals(stage, setup.Stage), "setup keeps caller's explicit stage");
                Equal(goal, stage.GearGoal, "resource policy preserves explicit gear goal including -1");
                Weights(setup, new[] {1, 10}, new[] {100, 50}, "resource support remains active regardless of gear goal " + goal);
                Main.reference.ApplyEquipmentForChecks(setup, "stage:" + goal, Entry);
                Sequence(setup.Beards.OrderBy(x => x).ToArray(), Main.CaptureSupportForChecks().Beards, "stage applies exact selected support even with independent gear");
            }
            Equal(1, EquipmentRecording.ExactSupport.Count, "identical current support is not rewritten for another gear-only owner");
            Equal(1, EquipmentRecording.Beards.Count, "gear-only owner changes preserve identical beard constraints");
            NoGear("stage support does not select or swap gear");
        }

        private static void ExactSupportLifecycle()
        {
            var setup = Main.SetupForFocus(CustomFocusGoal.EnergyNgu);
            var expected = DesiredSupport(setup);
            Main.reference.ApplyEquipmentForChecks(setup, "stage:current", Entry);
            Equal(expected.StableKey, Main.CaptureSupportForChecks().StableKey, "shared actuator installs exact planned levels and beard set");
            var writes = EquipmentRecording.ExactSupport.Count; var selections = EquipmentRecording.BeardRandomize.Count;
            Main.reference.ApplyEquipmentForChecks(setup, "stage:current", Entry.AddSeconds(59));
            Main.reference.ResetEquipmentForChecks();
            Main.reference.ApplyEquipmentForChecks(setup, "stage:current", Entry.AddSeconds(59));
            Equal(writes, EquipmentRecording.ExactSupport.Count, "ordinary replay and application invalidation perform no duplicate native support write");
            Equal(selections, EquipmentRecording.BeardRandomize.Count, "invalidation retains acquired support rather than selecting again");
            Main.reference.ApplyEquipmentForChecks(setup, "stage:current", Entry.AddSeconds(60));
            Equal(selections + 1, EquipmentRecording.BeardRandomize.Count, "independent minute admits one support selection event");
            Equal(writes, EquipmentRecording.ExactSupport.Count, "a minute selection identical to native state performs no model write");
            Equal(Entry.AddSeconds(60), Main.reference.LastBeardRotationForChecks, "verified minute event retains its original accepted time");
            Equal(expected.StableKey, Main.CaptureSupportForChecks().StableKey, "minute observation does not resize pinned levels outside policy");
            NoGear("exact support lifecycle");
        }

        private static void ExactSupportRetryAndForeign()
        {
            var setup = Main.SetupForFocus(CustomFocusGoal.Ngu);
            EquipmentRecording.ThrowNextDiggers = true;
            var rejected = false;
            try { Main.reference.ApplyEquipmentForChecks(setup, "stage:retry", Entry); }
            catch (InvalidOperationException) { rejected = true; }
            Equal(true, rejected, "native support rejection is surfaced before credit");
            var selections = EquipmentRecording.BeardRandomize.Count;
            Main.reference.ApplyEquipmentForChecks(setup, "stage:retry", Entry.AddSeconds(1));
            Equal(selections, EquipmentRecording.BeardRandomize.Count, "support retry keeps the SAME acquired selection");
            Equal(DesiredSupport(setup).StableKey, Main.CaptureSupportForChecks().StableKey, "retry converges to exact planned native support");
            var writes = EquipmentRecording.ExactSupport.Count;
            Main.Character.diggers.diggers[2].active = true; Main.Character.diggers.activeDiggers.Add(2);
            rejected = false;
            try { Main.reference.ApplyEquipmentForChecks(setup, "stage:retry", Entry.AddSeconds(2)); }
            catch (InvalidOperationException) { rejected = true; }
            Equal(true, rejected, "foreign digger change yields the coupled support domain");
            Equal(writes, EquipmentRecording.ExactSupport.Count, "foreign support is never overwritten by a retry");
            Equal(true, Main.Character.diggers.activeDiggers.Contains(2), "manual support survives rejection");
        }

        private static void SelectionPersistencePrecedesMutation()
        {
            Main.reference.FailSelectionPersistence = true;
            Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            Equal(0, EquipmentRecording.ExactApplications.Count, "failed selection persistence precedes native gear mutation");
            Equal(0, EquipmentRecording.ExactSupport.Count, "failed selection persistence precedes native support mutation");
            Equal(false, Main.reference.GetCustomFocusStatus() == "applied", "unpersisted selection cannot claim applied readiness");
            Main.reference.FailSelectionPersistence = false;
            Main.reference.MakeFocusRetryDueForChecks(); Main.reference.Tick();
            Equal("applied", Main.reference.GetCustomFocusStatus(), "same acquired focus can resume after storage recovers");
            Equal(1, Main.reference.SelectionPersistenceWrites, "retry durably records the retained selection before applying");
        }

        private static void FightPolicy()
        {
            var focus = Main.SetupForFocus(CustomFocusGoal.Fight);
            Weights(focus, new[] {2}, new[] {100}, "boss fight requests stat digger");
            Equal((int) GearOptimizationGoal.Fight, focus.Stage.GearGoal, "boss fight gear goal");
            Equal(0, focus.Beards[0], "Fu Manchu is first for boss fight");
            var stage = Main.SetupForStage(new CustomPlanStage {GearGoal = (int) GearOptimizationGoal.Fight});
            Weights(stage, new[] {2}, new[] {100}, "no-resource Fight stage infers boss fight policy");
            Equal(0, stage.Beards[0], "inferred Fight stage puts Fu Manchu first");
            var explicitFight = Main.SetupForStage(new CustomPlanStage {EnergyFocus = (int) CustomStageFocus.EnergyNgu}, bossFight: true);
            Weights(explicitFight, new[] {4, 2}, new[] {50, 100}, "explicit boss fight keeps resource support and adds stats");
            Equal(0, explicitFight.Beards[0], "explicit boss fight Fu Manchu first");
        }

        private static void FocusStageConsistency()
        {
            var root = ReadPolicy();
            var matched = 0;
            foreach (CustomFocusGoal goal in Enum.GetValues(typeof(CustomFocusGoal)))
            {
                var normal = MatchingStandardStage(root, goal);
                if (normal == null) continue;
                matched++;
                var focus = Main.SetupForFocus(goal);
                var stage = Main.SetupForStage(normal);
                Equal(normal.EnergyFocus, focus.Stage.EnergyFocus, goal + " standard energy lane");
                Equal(normal.MagicFocus, focus.Stage.MagicFocus, goal + " standard magic lane");
                Equal(normal.GearGoal, focus.Stage.GearGoal, goal + " standard gear policy");
                Sequence(normal.EnergyPriorities, focus.Stage.EnergyPriorities, goal + " full standard energy priorities, including AUG overflow");
                Sequence(normal.MagicPriorities, focus.Stage.MagicPriorities, goal + " full standard magic priorities");
                Sequence(stage.Diggers.Select(x => x.Id + ":" + x.Weight + ":" + x.Name).ToArray(), focus.Diggers.Select(x => x.Id + ":" + x.Weight + ":" + x.Name).ToArray(), goal + " focus/stage diggers agree");
                Sequence(stage.Beards, focus.Beards, goal + " full focus/stage beard requirements agree");
                foreach (var energy in new[] {true, false})
                    Sequence(CustomStageAllocator.EffectiveStagePriorities(normal, energy, 0, 0, out _),
                        CustomStageAllocator.EffectiveStagePriorities(focus.Stage, energy, 0, 0, out _), goal + " effective allocation order matches normal stage");
            }
            Equal(7, matched, "all matching Quick presets are checked");
        }

        private static JSONObject ReadPolicy()
        {
            using (var stream = typeof(ActivityPolicy).Assembly.GetManifestResourceStream(ActivityPolicy.ResourceName))
            using (var reader = new StreamReader(stream)) return JsonFiles.ParseObject(reader.ReadToEnd());
        }

        private static CustomPlanStage MatchingStandardStage(JSONObject root, CustomFocusGoal goal)
        {
            var tuple = root["focuses"][goal.ToString()];
            var preset = root["stagePresets"].Children.SingleOrDefault(row => row["GearGoal"].AsInt == tuple["gearGoal"].AsInt
                && row["EnergyFocus"].AsInt == tuple["energyFocus"].AsInt && row["MagicFocus"].AsInt == tuple["magicFocus"].AsInt);
            return preset == null ? null : new CustomPlanStage {GearGoal = preset["GearGoal"].AsInt,
                EnergyFocus = preset["EnergyFocus"].AsInt, MagicFocus = preset["MagicFocus"].AsInt,
                EnergyPriorities = root["prioritySets"][preset["EnergyPriorities"].Value].Children.Select(row => row.Value).ToArray(),
                MagicPriorities = root["prioritySets"][preset["MagicPriorities"].Value].Children.Select(row => row.Value).ToArray()};
        }

        private static long[] AllocationSnapshot(FocusCharacter c)
        {
            return new[] {c.idleEnergy, c.magic.idleMagic, c.machine.speedEnergy, c.machine.goldMultiMagic,
                    c.wandoos98.wandoosEnergy, c.wandoos98.wandoosMagic}
                .Concat(c.NGU.skills.Select(x => x.energy)).Concat(c.NGU.magicSkills.Select(x => x.magic))
                .Concat(c.advancedTraining.energy).Concat(c.bloodMagic.ritual.Select(x => x.magic))
                .Concat(c.augments.augs.SelectMany(x => new[] {x.augEnergy, x.upgradeEnergy})).ToArray();
        }

        private static void QuickAllocationMatchesStandardStage()
        {
            var root = ReadPolicy();
            AugmentOptimizer.Selected = new AugmentOptimizerResult {Success = true, UsesUpgrade = true, AugmentIndex = 2};
            try
            {
                foreach (CustomFocusGoal goal in Enum.GetValues(typeof(CustomFocusGoal)))
                foreach (var overflow in new[] {false, true})
                {
                    var normal = MatchingStandardStage(root, goal);
                    if (normal == null) continue;
                    Func<FocusCharacter> fresh = () => {
                        var c = new FocusCharacter {idleEnergy = 1000};
                        c.magic.idleMagic = 700;
                        c.buttons.ngu.interactable = !overflow;
                        c.buttons.augmentation.interactable = overflow;
                        for (var i = 0; i < 7; i++)
                            c.augmentsController.normalAugSpeedDividers[i] = c.augmentsController.normalUpgradeSpeedDividers[i] = 1;
                        return c;
                    };
                    EquipmentRecording.Reset(true);
                    Main.reference = new Main();
                    Main.Character = fresh();
                    var result = Main.reference.ApplyCustomFocusGoal(goal);
                    Equal("applied", Main.reference.GetCustomFocusStatus(), goal + " Quick applies: " + result);
                    Sequence(new[] {(GearOptimizationGoal) normal.GearGoal}, EquipmentRecording.Gear.ToArray(), goal + " actual gear request matches normal stage");
                    var support = Main.SetupForStage(normal);
                    Sequence(support.Beards.OrderBy(x => x).ToArray(), EquipmentRecording.Beards.Single(), goal + " exact selected beards match normal stage requirements");
                    Equal(DesiredSupport(support).StableKey, EquipmentRecording.ExactSupport.Single().StableKey,
                        goal + " actual exact support matches real projected normal-stage policy");
                    var quick = AllocationSnapshot(Main.Character);
                    Equal(1000L, Main.Character.curEnergy, goal + " Quick conserves all energy");
                    if (overflow)
                        Equal(goal == CustomFocusGoal.MagicNgu ? 0L : 1000L,
                            Main.Character.augments.augs.Sum(x => x.augEnergy + x.upgradeEnergy), goal + " Quick actually uses standard AUG overflow only on owned energy");
                    Main.Character = fresh();
                    CustomStageAllocator.ResetResourceTargets();
                    // Locked primaries intentionally expose overflow; protected timing eligibility is separate.
                    CustomStageAllocator.ApplyStage(Main.CustomPlanStore.Load(Main.Settings.CustomPlanFile), normal, 0);
                    Sequence(AllocationSnapshot(Main.Character), quick, goal + " actual Quick allocations equal normal preset, overflow=" + overflow);
                }
            }
            finally { AugmentOptimizer.Selected = null; CustomStageAllocator.ResetResourceTargets(); }
        }

        private static void QuickRemainsManualIndefinitely()
        {
            foreach (CustomFocusGoal goal in Enum.GetValues(typeof(CustomFocusGoal)))
            {
                EquipmentRecording.Reset(true);
                Main.reference = new Main();
                Main.Character = new FocusCharacter {idleEnergy = 1000};
                Main.Character.magic.idleMagic = 700;
                Main.Settings.AutoRebirth = Main.Settings.UseCustomRebirthPlan = true;
                Main.reference.ApplyCustomFocusGoal(goal);
                foreach (var elapsed in new[] {0d, 300d, 86400d, 1e12})
                {
                    foreach (var skill in Main.Character.NGU.skills.Concat(Main.Character.NGU.magicSkills)) skill.target = -1;
                    Main.Character.rebirthTime.totalseconds = elapsed;
                    Main.Character.totalPlaytime.totalseconds = elapsed;
                    Main.reference.MarkRefreshDue();
                    Main.reference.Tick();
                    Equal("applied", Main.reference.GetCustomFocusStatus(), goal + " remains applied at rebirth seconds " + elapsed);
                    Equal((CustomFocusGoal?) goal, Main.reference.GetActiveCustomFocusGoal(), goal + " manual request never auto-transitions");
                    Equal(goal.ToString(), Main.reference.GetAppliedCustomFocusGoal(), goal + " applied owner never auto-transitions");
                    Equal(false, Main.Settings.AutoRebirth || Main.Settings.UseCustomRebirthPlan, goal + " does not resume automatic rebirth or stage scheduling");
                    Equal(null, Main.reference.SchedulerStateForChecks, goal + " never initializes a timed stage scheduler");
                }
                Equal(1, EquipmentRecording.Gear.Count, goal + " repeated passes do not start new gear searches");
                Main.reference.StopCustomFocusGoal();
                Equal("idle", Main.reference.GetCustomFocusStatus(), goal + " only explicit stop releases manual owner");
            }
        }

        private static void MagicFocusUsesUnmatchedMetadataGear()
        {
            var root = ReadPolicy();
            root["focuses"]["MagicNgu"]["gearGoal"] = 4;
            var parsed = ActivityPolicy.Parse(root.ToString());
            var policy = ActivityPolicy.Current;
            var definitions = (IDictionary<string, ActivityPolicy.Focus>) typeof(ActivityPolicy)
                .GetField("focuses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(policy);
            var original = definitions["MagicNgu"];
            try
            {
                definitions["MagicNgu"] = parsed.FocusDefinition("MagicNgu");
                Main.Character.NGU.skills[4].energy = 37;
                Main.Character.magic.idleMagic = 100;
                Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);
                Equal("applied", Main.reference.GetCustomFocusStatus(), "unmatched custom Magic tuple applies");
                Sequence(new[] {GearOptimizationGoal.AllNgu}, EquipmentRecording.Gear.ToArray(), "actual Magic gear selection follows metadata, not hardcoded MagicNgu or preset labels");
                Equal(0, Main.Character.EnergyRecalls, "custom Magic never recalls non-owned energy");
                Equal(37L, Main.Character.NGU.skills[4].energy, "custom Magic retains non-owned allocation");
                Equal(100L, Main.Character.NGU.magicSkills.Sum(x => x.magic), "custom Magic still allocates owned magic");
                var stage = Main.SetupForFocus(CustomFocusGoal.MagicNgu).Stage;
                Equal((int) CustomStageFocus.None, stage.EnergyFocus, "custom Magic fallback keeps EnergyFocus None");
                Sequence(new string[0], stage.EnergyPriorities, "custom Magic fallback has explicit empty energy priorities");
                Sequence(ActivityPolicy.Current.DefaultPriorities(stage.MagicFocus, false), stage.MagicPriorities, "custom Magic fallback uses declared magic defaults");
            }
            finally { definitions["MagicNgu"] = original; }
            Equal(true, ReferenceEquals(original, policy.FocusDefinition("MagicNgu")), "metadata regression fixture restores original immutable definition");
        }

        private static void SpecialAdventurePolicies()
        {
            var titan = Main.SetupForFocus(CustomFocusGoal.ManualTitans);
            Sequence(new[] {"3:10000", "0:1000"}, titan.Diggers.Select(x => x.Id + ":" + x.Weight).ToArray(), "manual titans preserve special weights without base support");
            Equal(0, titan.Beards[0], "manual titans prioritize Fu Manchu");
            Sequence(new[] {0, 6, 1, 3, 5}, titan.Beards, "manual titans explicitly require Fu and Gold before core");
            Equal((int) GearOptimizationGoal.Fight, titan.Stage.GearGoal, "manual titans use Fight gear");
            var manual = Main.SetupForManualAdventure();
            Sequence(new[] {"3:800", "0:400"}, manual.Diggers.Select(x => x.Id + ":" + x.Weight).ToArray(), "manual adventure preserves special weights without base support");
            Equal((int) GearOptimizationGoal.ManualAdventure, manual.Stage.GearGoal, "manual adventure keeps its gear goal");
            Sequence(new[] {1, 3, 5}, manual.Beards, "manual adventure has only core requirements, no unconditional Gold");
        }

        private static void SetGuard(int guard, bool enabled)
        {
            Main.Settings.UseCustomRebirthPlan = guard == 0 && enabled;
            Main.Settings.ManualAdventureEnabled = guard == 1 && enabled;
            Main.reference.SetManualAdventureSetupActiveForChecks(guard == 1 && enabled);
            Main.reference.Blocked = guard == 2 && enabled;
            LoadoutManager.CurrentLock = guard == 3 && enabled ? LockType.Titan : LockType.None;
            DiggerManager.CurrentLock = guard == 4 && enabled ? LockType.Yggdrasil : LockType.None;
        }

        private static void SlowFocusGuardsEntry()
        {
            for (var guard = 0; guard < 5; guard++)
            {
                EquipmentRecording.Reset(true);
                // Recording reset is not a native support reset. Each guard is an
                // independent entry timeline, not another owner of the prior map.
                Main.Character = new FocusCharacter();
                Main.reference = new Main();
                Main.reference.Begin(CustomFocusGoal.EnergyNgu);
                SetGuard(guard, true);
                Main.reference.RefreshFocusEquipmentForChecks();
                Equal(0, EquipmentRecording.Beards.Count, "slow focus entry guarded beards: " + guard);
                Equal(0, EquipmentRecording.Diggers.Count, "slow focus entry guarded diggers: " + guard);
                SetGuard(guard, false);
                Main.reference.RefreshFocusEquipmentForChecks();
                Equal(1, EquipmentRecording.Beards.Count, "slow focus entry resumes immediately: " + guard);
                Equal(1, EquipmentRecording.Diggers.Count, "slow focus guard did not consume cooldown: " + guard);
                Equal(DesiredSupport(Main.SetupForFocus(CustomFocusGoal.EnergyNgu)).StableKey,
                    Main.CaptureSupportForChecks().StableKey, "released guard applies native selected entry: " + guard);
                Main.reference.RefreshFocusEquipmentForChecks();
                Equal(1, EquipmentRecording.Diggers.Count, "slow focus observes cooldown after success: " + guard);
                NoGear("slow focus entry guard " + guard);
            }
        }

        private static void SlowFocusGuardsRefresh()
        {
            for (var guard = 0; guard < 5; guard++)
            {
                EquipmentRecording.Reset(true);
                Main.Character = new FocusCharacter();
                Main.reference = new Main();
                Main.reference.Begin(CustomFocusGoal.EnergyNgu);
                Main.reference.RefreshFocusEquipmentForChecks();
                var before = Main.CaptureSupportForChecks().StableKey;
                // A new eligible support demand, rather than elapsed legacy owner
                // cadence, makes this recurring transition require a native write.
                EquipmentRecording.TitanSoon = true;
                SetGuard(guard, true);
                Main.reference.RefreshFocusEquipmentForChecks();
                Equal(1, EquipmentRecording.Diggers.Count, "slow focus due refresh remains guarded: " + guard);
                Equal(before, Main.CaptureSupportForChecks().StableKey, "guard preserves native selection: " + guard);
                SetGuard(guard, false);
                Main.reference.RefreshFocusEquipmentForChecks();
                Equal(2, EquipmentRecording.Diggers.Count, "slow focus due refresh retries immediately: " + guard);
                Equal(true, Main.CaptureSupportForChecks().Diggers.Any(x => x.Id == 11 && x.Level > 0),
                    "released guard funds newly eligible EXP support: " + guard);
                Equal(EquipmentRecording.ExactSupport.Last().StableKey, Main.CaptureSupportForChecks().StableKey,
                    "changed recurring selection is actually installed: " + guard);
                Main.reference.RefreshFocusEquipmentForChecks();
                Equal(2, EquipmentRecording.Diggers.Count, "stable changed selection has no duplicate writes: " + guard);
                NoGear("slow focus refresh guard " + guard);
            }
        }

        private static void InactiveManualAdventureAllowsFocusEntry()
        {
            Main.Settings.ManualAdventureEnabled = true;
            Main.reference.SetManualAdventureSetupActiveForChecks(false);
            Main.reference.Begin(CustomFocusGoal.EnergyNgu);
            Main.reference.RefreshFocusEquipmentForChecks();
            Equal(1, EquipmentRecording.Beards.Count, "enabled but inactive manual adventure allows focus beard entry");
            Equal(1, EquipmentRecording.Diggers.Count, "enabled but inactive manual adventure allows focus digger entry");
            NoGear("focus entry with inactive manual adventure");
        }

        private static void InactiveManualAdventureAllowsFocusRefresh()
        {
            Main.reference.Begin(CustomFocusGoal.EnergyNgu);
            Main.reference.RefreshFocusEquipmentForChecks();
            Main.Settings.ManualAdventureEnabled = true;
            Main.reference.SetManualAdventureSetupActiveForChecks(false);
            Main.reference.RefreshFocusEquipmentForChecks();
            Equal(1, EquipmentRecording.Diggers.Count, "inactive manual adventure does not rewrite exact current diggers");
            Equal(1, EquipmentRecording.Beards.Count, "inactive manual adventure does not rewrite exact current beards");
            Equal(DesiredSupport(Main.SetupForFocus(CustomFocusGoal.EnergyNgu)).StableKey,
                Main.CaptureSupportForChecks().StableKey, "inactive overlay retains actual selected support");
            NoGear("focus refresh with inactive manual adventure");
        }

        private static void SlowFocusUsesAppliedOwner()
        {
            Main.reference.RefreshFocusEquipmentForChecks();
            Equal(0, EquipmentRecording.Beards.Count, "idle slow focus has no owner to equip");
            Equal(0, EquipmentRecording.Diggers.Count, "idle slow focus never equips diggers");
            Main.reference.Begin(CustomFocusGoal.EnergyNgu);
            Main.reference.RefreshFocusEquipmentForChecks();
            Main.reference.Begin(CustomFocusGoal.AdvancedTraining);
            Main.reference.RefreshFocusEquipmentForChecks();
            Equal(2, EquipmentRecording.Beards.Count, "changed exact diggers use the coupled support boundary with the same required beards");
            Equal(2, EquipmentRecording.Diggers.Count, "new applied focus bypasses previous focus cooldown");
            Equal(true, EquipmentRecording.ExactSupport[1].Diggers.Any(x => x.Id == 10 && x.Level > 0), "new AT focus funds blood digger");
            Equal(false, EquipmentRecording.Diggers[1].Any(x => x.Id == 4 || x.Id == 5), "new AT focus does not reuse NGU policy");
            Equal(0, Main.Character.EnergyRecalls, "slow support never recalls energy");
            Equal(0, Main.Character.MagicRecalls, "slow support never recalls magic");
            NoGear("slow applied owner refresh");
        }

        private static void FocusActivationUsesSharedSetup()
        {
            Main.Settings.UseCustomRebirthPlan = Main.Settings.ManualAdventureEnabled = Main.Settings.AutoRebirth = true;
            var result = Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
            Equal("applied", Main.reference.GetCustomFocusStatus(), "focus activation succeeds: " + result);
            Equal(false, Main.Settings.UseCustomRebirthPlan, "manual focus pauses plan owner");
            Equal(false, Main.Settings.ManualAdventureEnabled, "manual focus pauses manual adventure owner");
            Equal(false, Main.Settings.AutoRebirth, "manual focus pauses auto rebirth");
            Sequence(new[] {GearOptimizationGoal.EnergyNgu}, EquipmentRecording.Gear.ToArray(), "initial activation equips focus gear once");
            Equal(0, EquipmentRecording.GearSwaps, "focus preparation does not use the unconditional gear-swap recall boundary");
            Equal(0, Main.Character.EnergyRecalls, "unchanged empty gear requires no Energy recall");
            Equal(0, Main.Character.MagicRecalls, "unchanged empty gear requires no Magic recall");
            Equal(DesiredSupport(Main.SetupForFocus(CustomFocusGoal.EnergyNgu)).StableKey,
                EquipmentRecording.ExactSupport.Single().StableKey, "activation applies exact projected NGU support");
            Equal(1, EquipmentRecording.Beards.Count, "initial activation equips beards once");
            Main.reference.RefreshFocusEquipmentForChecks();
            Equal(1, EquipmentRecording.Diggers.Count, "immediate slow refresh shares activation cooldown");
            Equal(1, EquipmentRecording.Beards.Count, "immediate slow refresh shares activation beard completion");
            Equal(1, EquipmentRecording.Gear.Count, "slow refresh never repeats activation gear");
        }

        private static void FocusActivationWaitsForLocks()
        {
            foreach (var gearLock in new[] {true, false})
            {
                EquipmentRecording.Reset(true);
                Main.Character = new FocusCharacter();
                Main.reference = new Main();
                if (gearLock) LoadoutManager.CurrentLock = LockType.Titan;
                else DiggerManager.CurrentLock = LockType.Yggdrasil;
                var result = Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
                Equal(gearLock ? "setup waiting for loadout lock Titan" : "setup waiting for digger lock Yggdrasil", result, "focus activation reports blocking lock");
                Equal("idle", Main.reference.GetCustomFocusStatus(), "blocked activation does not enter pending or applied state");
                Equal((CustomFocusGoal?) null, Main.reference.GetActiveCustomFocusGoal(), "blocked activation does not claim requested goal");
                Equal(0, EquipmentRecording.Beards.Count, "blocked activation does not apply beards");
                Equal(0, EquipmentRecording.Diggers.Count, "blocked activation does not apply diggers");
                NoGear("blocked focus activation");
                LoadoutManager.CurrentLock = DiggerManager.CurrentLock = LockType.None;
                Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
                Equal("applied", Main.reference.GetCustomFocusStatus(), "unblocked activation completes immediately");
                Equal(1, EquipmentRecording.Diggers.Count, "blocked activation did not consume entry");
            }
        }

        private static void MagicFocusPreservesUnownedAllocations()
        {
            var c = Main.Character;
            c.idleEnergy = 7;
            c.training.attackEnergy[0] = 11;
            c.training.defenseEnergy[1] = 13;
            c.advancedTraining.energy[2] = 17;
            c.augments.augs[0].augEnergy = 19;
            c.augments.augs[0].upgradeEnergy = 23;
            c.wandoos98.wandoosEnergy = 29;
            c.machine.speedEnergy = 31;
            c.NGU.skills[4].energy = 37;
            c.wishes.wishes[0].energy = 41;
            c.hacks.hacks[0].res3 = 43;
            c.wishes.wishes[0].res3 = 47;
            c.res3.idleRes3 = 5;
            c.res3.curRes3 = 95;
            c.magic.idleMagic = 100;
            var head = new ih {slot = -1, id = 1, equipment = new Equipment {type = part.Head}};
            var weapon = new ih {slot = -5, id = 2, equipment = new Equipment {type = part.Weapon}};
            var replacement = new ih {slot = 0, id = 3, equipment = new Equipment {type = part.Head}};
            EquipmentRecording.ExactLoadout = new[] {head, weapon};
            EquipmentRecording.SelectedGear = new[] {replacement};

            var result = Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);

            Equal("applied", Main.reference.GetCustomFocusStatus(), "Magic NGU activation succeeds: " + result);
            Sequence(new[] {GearOptimizationGoal.MagicNgu}, EquipmentRecording.Gear.ToArray(), "Magic NGU selects gear once");
            Sequence(new[] {228m, 95m}, EquipmentRecording.CapacityChecks.Single(), "capacity preflight retains full energy and R3 pools, including idle energy and idle R3");
            Equal(0, c.EnergyRecalls, "Magic NGU never recalls non-owned energy");
            Equal(7L, c.idleEnergy, "Magic NGU preserves idle energy");
            Equal(228L, c.curEnergy, "Magic NGU conserves total energy");
            Sequence(new[] {11L, 13, 17, 19, 23, 29, 31, 37, 41}, new[] {
                c.training.attackEnergy[0], c.training.defenseEnergy[1], c.advancedTraining.energy[2],
                c.augments.augs[0].augEnergy, c.augments.augs[0].upgradeEnergy, c.wandoos98.wandoosEnergy,
                c.machine.speedEnergy, c.NGU.skills[4].energy, c.wishes.wishes[0].energy}, "every non-owned energy allocation is preserved");
            Sequence(new[] {43L, 47, 5, 95}, new[] {c.hacks.hacks[0].res3, c.wishes.wishes[0].res3, c.res3.idleRes3, c.res3.curRes3}, "R3 allocations and pools are preserved");
            var applied = EquipmentRecording.ExactApplications.Single();
            Equal(true, ReferenceEquals(weapon, applied.Single(x => x.slot == -5)), "exact composition retains an unselected destination");
            Equal(true, ReferenceEquals(replacement.equipment, applied.Single(x => x.slot == -1).equipment), "selected head is mapped to the equipped destination");
            Equal(0, EquipmentRecording.GearSwaps, "Magic NGU avoids full-lane gear-swap recall");
            Equal(100L, c.NGU.magicSkills.Sum(x => x.magic), "Magic NGU really allocates its owned magic pool");
        }

        private static void MagicFocusRejectsCapacityBeforeRecall()
        {
            Main.Character.NGU.skills[0].energy = 37;
            Main.Character.NGU.magicSkills[0].magic = 53;
            EquipmentRecording.CapacityFailure = "fixture retained energy exceeds swap capacity";

            var result = Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);

            Equal(true, result.Contains(EquipmentRecording.CapacityFailure), "capacity rejection is surfaced");
            Equal("failed", Main.reference.GetCustomFocusStatus(), "rejected Magic NGU is not marked applied");
            Sequence(new[] {37m, 0m}, EquipmentRecording.CapacityChecks.Single(), "preflight sees retained energy");
            Equal(0, Main.Character.EnergyRecalls, "capacity rejection cannot recall energy");
            Equal(0, Main.Character.MagicRecalls, "capacity rejection happens before any magic recall");
            Equal(37L, Main.Character.NGU.skills[0].energy, "rejected activation preserves energy allocation");
            Equal(53L, Main.Character.NGU.magicSkills[0].magic, "rejected activation preserves magic allocation");
            Equal(0, EquipmentRecording.ExactApplications.Count, "rejected capacity never applies exact gear");
            Equal(0, EquipmentRecording.Beards.Count, "rejected capacity never applies support");
            Equal(0, EquipmentRecording.Diggers.Count, "rejected capacity never applies diggers");
        }

        private static void MagicFocusRejectsUnaccountedRes3()
        {
            Main.Character.res3.curRes3 = 1;
            var result = Main.reference.ApplyCustomFocusGoal(CustomFocusGoal.MagicNgu);
            Equal(true, result.Contains("Cannot account for all non-owned energy/R3 allocations"), "unknown R3 allocation fails closed");
            Equal("failed", Main.reference.GetCustomFocusStatus(), "unknown allocation cannot claim applied focus");
            NoGear("unknown non-owned allocation");
            Equal(0, Main.Character.MagicRecalls, "unknown non-owned allocation fails before recalling magic");
        }

        private static void RapidSustainHasNoEquipment()
        {
            foreach (var goal in new[] {CustomFocusGoal.TimeMachine, CustomFocusGoal.AugmentsBloodMagic, CustomFocusGoal.Wandoos,
                CustomFocusGoal.EnergyNgu, CustomFocusGoal.MagicNgu, CustomFocusGoal.Ngu, CustomFocusGoal.AdvancedTraining, CustomFocusGoal.Adventure})
            {
                Main.reference = new Main();
                Main.Character = new FocusCharacter();
                Main.Character.NGU.skills[0].energy = 1000;
                Main.Character.NGU.magicSkills[2].magic = 700;
                Main.Character.machine.speedTarget = Main.Character.machine.multiTarget = -1;
                Main.reference.Begin(goal);
                Main.reference.MarkRefreshDue();
                Main.reference.Tick();
                Equal("applied", Main.reference.GetCustomFocusStatus(), goal + " rapid sustain did not fail or clear focus");
                Equal(0, Main.Character.EnergyRecalls, goal + " rapid sustain uses exact managed energy differences");
                Equal(0, Main.Character.MagicRecalls, goal + " rapid sustain uses exact managed magic differences");
                Equal(0, EquipmentRecording.Beards.Count, goal + " rapid sustain never equips beards");
                Equal(0, EquipmentRecording.Diggers.Count, goal + " rapid sustain never equips diggers");
                NoGear(goal + " rapid sustain");
            }
        }
    }
}
