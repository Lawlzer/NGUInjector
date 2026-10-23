using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using static NativePolicyFixture;

public static class PolicyChecks
{
    private static int Assertions;
    private static string ThrowOnRender;
    private static int RenderFailures;
    public static void Render(string boundary)
    {
        if (boundary != ThrowOnRender) return;
        RenderFailures++;
        throw new InvalidOperationException("UI unavailable: " + boundary);
    }
    public static void Run()
    {
        var failures = new List<Exception>();
        foreach (var test in new Action[] {CheckSelection, CheckPairCompatibility, CheckGoldAndRepin, CheckUnlocks,
            CheckSadistic, CheckPreparation, CheckStageRenderingFailures, CheckExplicitAllocation, CheckIntegrationIl})
        {
            var before = Assertions;
            try { test(); Console.WriteLine("PASS " + test.Method.Name + ": " + (Assertions - before) + " assertions."); }
            catch (Exception error) { failures.Add(new Exception(test.Method.Name, error)); }
            finally { ThrowOnRender = null; }
        }
        if (failures.Count != 0) throw new AggregateException("Ordinary policy failed after " + Assertions + " attempted assertions (not a pass count).", failures);
        Console.WriteLine("PASS ordinary policy: " + Assertions + " assertions (compiled production decisions, native getters and real execution authority).");
    }
    private static void Assert(bool value, string name)
    {
        Assertions++;
        if (!value) throw new Exception(name);
    }
    private static object Blank(Type type)
    {
        var value = FormatterServices.GetUninitializedObject(type);
        for (var t = type; t != null; t = t.BaseType)
        {
            var ptr = t.GetField("m_CachedPtr", All);
            if (ptr != null) { ptr.SetValue(value, new IntPtr(1)); break; }
        }
        return value;
    }
    private static object Child(object o, string name)
    {
        var field = o.GetType().GetField(name, All); var value = field.GetValue(o);
        if (value == null) { value = Blank(field.FieldType); field.SetValue(o, value); }
        return value;
    }
    private static void List(object o, string name, int count)
    {
        var field = o.GetType().GetField(name, All); var list = (IList) Activator.CreateInstance(field.FieldType);
        for (var i = 0; i < count; i++) list.Add(Blank(field.FieldType.GetGenericArguments()[0]));
        field.SetValue(o, list);
    }
    private static Array ArrayField(object o, string name, int count)
    {
        var field = o.GetType().GetField(name, All); var array = Array.CreateInstance(field.FieldType.GetElementType(), count);
        for (var i = 0; i < count; i++) array.SetValue(Blank(field.FieldType.GetElementType()), i);
        field.SetValue(o, array); return array;
    }
    internal static void SeedAugments(Character c)
    {
        // Preserve the shared fixture's epoch, inventory, support and static controller identities.
        Child(c, "settings"); c.bossID = 100; c.highestBoss = 100;
        c.energyPower = 1; c.curEnergy = c.idleEnergy = 10000; c.realGold = 1e20;
        c.machine.speedEnergy = 0;
        c.machine.realBaseGold = 1000000d;
        Child(c, "magic"); c.magic.magicPower = 1;
        Child(c, "challenges"); Child(c.challenges, "noAugsChallenge");
        Child(c, "arbitrary"); Child(c.arbitrary, "energyPotion1Time"); Child(c, "allArbitrary");
        c.inventoryController.bonuses = Enum.GetValues(typeof(specType)).Cast<specType>().ToDictionary(x => x, x => 0f);
        c.inventory.macguffinBonuses = Enumerable.Repeat(1f, 30).ToList();
        Child(c, "hacksController"); Child(c, "cardsController"); Child(Child(c, "adventureController"), "itopod");
        Child(c, "beastQuestPerkController"); Child(c, "wishesController");
        Child(c, "allChallenges"); Child(c.allChallenges, "noAugsChallenge"); c.allChallenges.noAugsChallenge.maxCompletions = 10;
        Child(c.allChallenges, "laserSwordChallenge"); c.allChallenges.laserSwordChallenge.maxCompletions = 10;
        Child(c, "wishes"); List(c.wishes, "wishes", 200);
        Child(c, "advancedTraining"); c.advancedTraining.level = new long[5]; c.advancedTraining.levelTarget = new long[5]; c.advancedTraining.energy = new long[5];
        Child(c, "advancedTrainingController"); c.advancedTrainingController.character = c;
        var names = new[] {"defense", "attack", "block", "wandoosEnergy", "wandoosMagic"};
        for (var i = 0; i < names.Length; i++)
        {
            var at = Child(c.advancedTrainingController, names[i]); Set(at, "character", c); Set(at, "baseTime", 1f); Set(at, "id", i);
        }
        Child(c, "augments"); ArrayField(c.augments, "augs", 7);
        Child(c, "augmentsController"); c.augmentsController.character = c;
        var bars = ArrayField(c.augmentsController, "augments", 7);
        foreach (var name in new[] {"normalAugSpeedDividers", "normalUpgradeSpeedDividers", "evilAugSpeedDividers", "evilUpgradeSpeedDividers", "sadisticAugSpeedDividers", "sadisticUpgradeSpeedDividers"})
            Set(c.augmentsController, name, Enumerable.Repeat(1f, 7).ToList());
        for (var i = 0; i < 7; i++)
        {
            var bar = bars.GetValue(i); Set(bar, "character", c); Set(bar, "allAugsController", c.augmentsController); Set(bar, "id", i);
            Set(bar, "baseBoost", 1L); Set(bar, "baseAugmentCost", 1f); Set(bar, "baseUpgradeCost", 1f);
            c.augments.augs[i].augmentTarget = -1; c.augments.augs[i].upgradeTarget = -1;
        }
        Child(c, "wandoos98"); Child(c.wandoos98, "bootupTime"); Child(c, "wandoos98Controller"); c.wandoos98Controller.character = c;
        foreach (var name in new[] {"augmentation", "advancedTraining", "wandoos", "ngu", "bloodMagic", "brokenTimeMachine"})
            Set(Child(c.buttons, name), "m_Interactable", name == "augmentation" || name == "advancedTraining" || name == "wandoos");
        Static("CustomStageAllocator", "ResetResourceTargets");
    }
    private static void Enable(Character c, int tier)
    {
        c.augments.augs[tier].augmentTarget = c.augments.augs[tier].upgradeTarget = 0;
    }
    private static long EnergyTotal(Character c)
    {
        return c.idleEnergy + c.augments.augs.Sum(x => x.augEnergy + x.upgradeEnergy)
            + c.advancedTraining.energy.Sum() + c.wandoos98.wandoosEnergy + c.machine.speedEnergy;
    }
    private static void Priorities(object stage, string[] energy, string[] magic = null)
    {
        Set(stage, "EnergyPriorities", energy); Set(stage, "MagicPriorities", magic);
    }
    private static long[] Desired(object requirements, string target)
    {
        var map = (IDictionary) Get(Get(requirements, "Intent"), "Desired");
        return (long[]) map[Enum.Parse(Production.GetType("NGUInjector.Managers.ResourceTarget", true), target)];
    }
    internal static long DesiredAugments(object requirements)
    {
        return (Desired(requirements, "Augment") ?? new long[0]).Sum() + (Desired(requirements, "AugmentUpgrade") ?? new long[0]).Sum();
    }
    private static void CheckSelection()
    {
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0); Enable(c, 6);
            c.augmentsController.augments[0].baseBoost = 100; c.augmentsController.augments[6].baseBoost = 3;
            c.augmentsController.normalAugSpeedDividers[6] = c.augmentsController.normalUpgradeSpeedDividers[6] = 1.35f;
            c.curEnergy = c.idleEnergy = 1000;
            var standalone = Static("AugmentOptimizer", "OptimizeBest", 300, true);
            var actual = Optimize(300, 1000);
            Assert(Get<bool>(standalone, "Success") && Get<bool>(actual, "Success"), "both explicit allocation policies are runnable");
            var tier = Get<int>(actual, "AugmentIndex");
            Assert(Get<int>(standalone, "AugmentIndex") != tier, "unequal standalone ratios and equal ordinary shares order tiers differently");
            var shares = (long[]) Static("BlackBarAllocator", "ShareAugmentBudget", new[] {tier * 2, tier * 2 + 1}, 1000L, 5);
            Assert(Get<long>(actual, "EnergyToAugment") == shares[0] && Get<long>(actual, "EnergyToUpgrade") == shares[1], "forecast uses exact ordinary shares");
            Assert(Get<double>(actual, "Seconds") > 0 && !double.IsInfinity(Get<double>(actual, "Seconds")), "fresh zero-allocation upgrade has finite native progress forecast");
            Assert(!Get<bool>(Optimize(300, 0), "Success"), "zero budget never uses current energy");
            Assert(!Get<bool>(Optimize(0, 1000), "Success"), "zero time never substitutes 300 seconds");
            Assert(c.idleEnergy == 1000 && c.augments.augs.All(x => x.augEnergy == 0 && x.upgradeEnergy == 0), "numeric selection and sharing never mutate native allocations");
            c.curEnergy = 1000000000;
            Assert(Get<int>(Optimize(300, c.curEnergy), "AugmentIndex") != tier, "regression selects a different tier from current rather than remaining energy");
            c.advancedTraining.levelTarget[1] = c.advancedTraining.levelTarget[0] = 1;
            c.advancedTrainingController.attack.baseTime = c.advancedTrainingController.defense.baseTime = 10000000;
            var cap = (long) Static("BlackBarAllocator", "AdvancedTrainingCapDelta", 1, 5, true, null);
            c.idleEnergy = cap * 2 + 1000;
            Assert(Get<int>(Optimize(300, c.idleEnergy), "AugmentIndex") != tier, "pre-priority pool would select the wrong tier");
            var stage = f.Stage(); Priorities(stage, new[] {"AT-1", "AT-0", "AUG"});
            f.Apply(stage);
            var allocated = c.augments.augs[tier];
            Assert(allocated.augEnergy == Get<long>(actual, "EnergyToAugment") && allocated.upgradeEnergy == Get<long>(actual, "EnergyToUpgrade"), "stage funds the choice from exactly 1000 after earlier caps");
            Assert(c.idleEnergy == 0 && c.advancedTraining.energy[0] == cap && c.advancedTraining.energy[1] == cap, "earlier capped priorities conserve the actual pool");
            f.Apply(stage);
            var outcome = Get(Production.GetType("NGUInjector.Managers.CustomStageAllocator"), "LastStageResourceOutcome");
            Assert(Get<int>(outcome, "ModelWrites") == 0 && allocated.augEnergy == shares[0] && allocated.upgradeEnergy == shares[1], "pinned refresh uses absolute caps without redundant model writes");
        }
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0); c.curEnergy = c.idleEnergy = 1000;
            c.augments.augs[0].augmentTarget = 1; c.augmentsController.normalAugSpeedDividers[0] = .001f;
            var actual = Optimize(300, 1000);
            Assert(Get<long>(actual, "EnergyToAugment") == 51 && Get<long>(actual, "EnergyToUpgrade") == 949, "capped sharing redistributes the unused half");
            Priorities(f.Stage(), new[] {"AUG"}); f.Apply(f.Stage());
            Assert(c.augments.augs[0].augEnergy == 51 && c.augments.augs[0].upgradeEnergy == 949, "real ordinary execution matches capped forecast");
        }
    }
    private static void CheckPairCompatibility()
    {
        foreach (var tier in Enumerable.Range(0, 7))
        foreach (var reverse in new[] {false, true})
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, tier); c.curEnergy = c.idleEnergy = 1001;
            var indexes = new[] {tier * 2, tier * 2 + 1}; if (reverse) Array.Reverse(indexes);
            Priorities(f.Stage(), indexes.Select(i => "AUG-" + i).ToArray()); f.Apply(f.Stage());
            Assert(c.augments.augs[tier].augEnergy == (reverse ? 500 : 501) && c.augments.augs[tier].upgradeEnergy == (reverse ? 501 : 500) && c.idleEnergy == 0,
                "ordinary pair " + tier + " retains first-token largest remainder; reverse=" + reverse);
        }
        foreach (var indexes in new[] {new[] {3, 4}, new[] {4, 3}, new[] {1, 0}, new[] {0, 1}})
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; c.curEnergy = c.idleEnergy = 1001;
            Priorities(f.Stage(), indexes.Select(i => "AT-" + i).ToArray()); f.Apply(f.Stage());
            Assert(c.advancedTraining.energy[indexes[0]] == 501 && c.advancedTraining.energy[indexes[1]] == 500 && c.idleEnergy == 0, "AT pair retains ordinary largest remainder: " + string.Join(",", indexes));
        }
    }
    private static void CheckGoldAndRepin()
    {
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0); var bar = c.augmentsController.augments[0]; c.realGold = 0;
            Assert(!Get<bool>(Static("AugmentOptimizer", "OptimizeBest", 1, true), "Success"), "future income cannot start an unpaid bar");
            Assert(!Get<bool>(Static("AugmentOptimizer", "OptimizeTarget", 0, true, 1, true), "Success"), "standalone unpaid pin also rejects future income");
            Assert(!Get<bool>(Optimize(1, 1000, tier: 0), "Success"), "unpaid ordinary pin fails immediately");
            c.augments.augs[0].augProgress = c.augments.augs[0].upgradeProgress = .99f;
            Assert(Get<bool>(Optimize(1, 1000, tier: 0), "Success"), "both paid partials run with zero gold");
            Assert(Get<bool>(Static("AugmentOptimizer", "OptimizeTarget", 0, true, 1, true), "Success"), "standalone paid partials are not charged twice");
            c.augments.augs[0].augProgress = c.augments.augs[0].upgradeProgress = 0;
            c.realGold = Math.Max(bar.getAugCost(), bar.getUpgradeCost());
            Assert(!Get<bool>(Optimize(1, 1000, tier: 0), "Success"), "two individually affordable starts cannot spend the wallet twice");
            Assert(((long[]) Static("BlackBarAllocator", "ShareAugmentBudget", new[] {0, 1}, 1000L, 5)).All(x => x == 0), "actual sharing rejects insufficient combined wallet");
            c.realGold = bar.getAugCost() + bar.getUpgradeCost();
            Assert(Get<bool>(Optimize(1, 1000, tier: 0), "Success"), "exact combined start price is affordable");
            Priorities(f.Stage(), new[] {"AUG"}); f.Apply(f.Stage());
            Assert(c.augments.augs[0].augEnergy > 0, "repin fixture really acquired and funded tier zero");
            Enable(c, 1); bar.baseAugmentCost = bar.baseUpgradeCost = 1000000;
            c.realGold = c.augmentsController.augments[1].getAugCost() + c.augmentsController.augments[1].getUpgradeCost();
            f.Apply(f.Stage());
            Assert(c.augments.augs[0].augEnergy == 0 && c.augments.augs[0].upgradeEnergy == 0 && c.augments.augs[1].augEnergy > 0, "unrunnable retained pin repins through actual authority without historical recall");
        }
    }
    private static void CheckUnlocks()
    {
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0);
            Set(c.buttons.augmentation, "m_Interactable", false);
            Assert(!Get<bool>(Optimize(300, 1000), "Success"), "locked augmentation menu rejects selection");
            Set(c.buttons.augmentation, "m_Interactable", true); c.challenges.noAugsChallenge.inChallenge = true;
            Assert(!Get<bool>(Optimize(300, 1000), "Success"), "No Augs challenge rejects selection");
            c.challenges.noAugsChallenge.inChallenge = false; c.bossID = 36;
            var selected = Optimize(300, 1000);
            Assert(Get<bool>(selected, "Success") && !Get<bool>(selected, "UsesUpgrade") && Get<long>(selected, "EnergyToUpgrade") == 0, "boss 36 permits augment-only, not locked upgrades");
            c.bossID = 37; selected = Optimize(300, 1000);
            Assert(Get<bool>(selected, "Success") && Get<bool>(selected, "UsesUpgrade") && Get<long>(selected, "EnergyToUpgrade") > 0, "boss 37 unlocks the paired ordinary decision");
            c.augments.augs[0].augmentTarget = 1; c.augments.augs[0].augLevel = 1;
            Assert(!Get<bool>(Optimize(300, 1000, tier: 0), "Success"), "completed native augment target rejects the exact pin");
        }
    }
    private static void CheckSadistic()
    {
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0); c.settings.rebirthDifficulty = difficulty.sadistic; c.energyPower = 100000;
            var bar = c.augmentsController.augments[0];
            foreach (var upgrade in new[] {false, true})
            {
                var cap = Static("AugmentOptimizer", "CalculateAugCapCalc", 0, upgrade ? 1 : 0, 100000000f, 100000000f);
                var amount = Get<long>(cap, "Num");
                var progress = upgrade ? (float) Call(Production.GetType("NGUInjector.Extensions", true), "getUpgradeProgressPerTick", bar, amount) : bar.getAugProgressPerTick(amount);
                Assert(amount < 100000, "Sadistic standalone cap has no extra 50000 factor");
                Assert(progress >= .99999f && progress < 2.01f, "Sadistic cap agrees with actual game progress scale");
                c.augments.augs[0].upgradeEnergy = amount;
                if (upgrade) Assert(Math.Abs(bar.getUpgradeProgressPerTick() - progress) < .00001f, "extension agrees with native no-argument upgrade progress getter");
            }
        }
    }
    private static string[] Effective(object stage, bool energy, bool reserve = false)
    {
        return (string[]) Static("CustomStageAllocator", "EffectiveStagePriorities", stage, energy, 250, 300, false, reserve, false);
    }
    private static void CheckPreparation()
    {
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; var stage = f.Stage(); Priorities(stage, null);
            Set(stage, "EnergyFocus", 5); Set(stage, "MagicFocus", 3);
            Assert(Effective(stage, false).First() == "WAN" && Effective(stage, false, true).First() == "WAN", "magic W focus alone never infers paid energy AT, including pill overlay");
            Set(stage, "EnergyFocus", 3);
            Assert(Effective(stage, true).Take(2).SequenceEqual(new[] {"AT-3", "AT-4"}) && !Effective(stage, false, true).Contains("WAN"), "one stage-wide paid energy preparation withholds W in both lanes");
            Set(stage, "MagicFocus", 0); Assert(Effective(stage, false).Length == 0, "energy preparation never invents empty magic work");
            Set(stage, "MagicFocus", 6); Assert(Effective(stage, false).First() == "NGU" && !Effective(stage, false).Contains("WAN"), "paid preparation preserves a different declared magic focus");
            Set(stage, "MagicFocus", 3); Set(c.buttons.advancedTraining, "m_Interactable", false);
            Assert(Effective(stage, false).First() == "WAN", "locked AT never withholds W for paid preparation");
            Set(c.buttons.advancedTraining, "m_Interactable", true);
            c.advancedTraining.levelTarget[3] = c.advancedTraining.levelTarget[4] = 1; c.advancedTraining.level[3] = c.advancedTraining.level[4] = 1;
            Assert(Effective(stage, false).First() == "WAN", "completed paid preparation does not withhold W");
            c.advancedTraining.levelTarget[3] = c.advancedTraining.levelTarget[4] = 0; c.wishes.wishes[190].level = 1;
            Assert(Effective(stage, true).First() == "WAN" && Effective(stage, false, true).First() == "WAN", "Wish 190 skips implicit paid preparation");
            foreach (var id in Enumerable.Range(0, 5))
            {
                Priorities(stage, new[] {"NGU", "AT-" + id, "WAN"});
                Assert(!Effective(stage, true).Contains("WAN") && !Effective(stage, false, true).Contains("WAN"), "explicit AT-" + id + " remains W-free even with free AT");
            }
            Priorities(stage, new[] {"NGU", "AT", "WAN"}); Assert(Effective(stage, false).Contains("WAN"), "fallback generic AT preserves ordinary W meaning");
            Priorities(stage, new[] {"AT", "NGU", "WAN"}); Assert(!Effective(stage, false).Contains("WAN"), "primary generic AT stays W-free");
            Priorities(stage, null); c.wishes.wishes[190].level = 0; f.Fixture("Setting", "ManageEnergy", false);
            Assert(Effective(stage, false).First() == "WAN", "unmanaged energy cannot claim paid preparation");
            f.Fixture("Setting", "ManageEnergy", true); f.Fixture("Setting", "ManageMagic", false); c.wandoos98.wandoosMagic = 1;
            Priorities(stage, new[] {"AT", "NGU", "WAN"});
            Assert(Static("CustomStageAllocator", "PrepareAtEntry", stage, 250, 300) != null && c.wandoos98.wandoosMagic == 1, "entry blocks and preserves unmanaged W magic before AT acquisition");
            f.Fixture("Setting", "ManageMagic", true); var before = c.magic.idleMagic;
            Assert(Static("CustomStageAllocator", "PrepareAtEntry", stage, 250, 300) == null && c.wandoos98.wandoosMagic == 0 && c.magic.idleMagic == before + 1, "entry releases managed W magic through real production authority before AT");
        }
    }
    private static void CheckStageRenderingFailures()
    {
        var cases = new[] {
            new {Priorities = new[] {"AUG-0", "AUG-1"}, Energy = true, Boundary = "AugmentController.updateAugTexts"},
            new {Priorities = new[] {"AUG-0", "AUG-1"}, Energy = true, Boundary = "AugmentController.updateUpgradeTexts"},
            new {Priorities = new[] {"AT-1"}, Energy = true, Boundary = "AdvancedTrainingController.updateText"},
            new {Priorities = new[] {"WAN"}, Energy = true, Boundary = "Wandoos98Controller.updateText"},
            new {Priorities = new[] {"WAN"}, Energy = false, Boundary = "Wandoos98Controller.updateText"}
        };
        foreach (var check in cases)
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0); c.curEnergy = c.idleEnergy = 1001; c.magic.curMagic = c.magic.idleMagic = 701;
            Priorities(f.Stage(), check.Energy ? check.Priorities : null, check.Energy ? null : check.Priorities);
            ThrowOnRender = check.Boundary; RenderFailures = 0;
            f.Apply(f.Stage());
            var allocator = Production.GetType("NGUInjector.Managers.CustomStageAllocator", true);
            var outcome = Get(allocator, "LastStageResourceOutcome");
            Assert(RenderFailures == 1 && Get(outcome, "Disposition").ToString() == "ModelAppliedRenderFailed", "real stage caller records committed render failure: " + check.Boundary);
            Assert(Get<bool>(allocator, "LastStageApplicationSucceeded"), "verified accounting retains its stage receipt despite render failure");
            Assert(EnergyTotal(c) == 1001 && c.magic.idleMagic + c.wandoos98.wandoosMagic == 701, "throwing presentation preserves exact resource totals");
            Assert(check.Energy ? c.idleEnergy < 1001 : c.magic.idleMagic < 701, "presentation failure never rolls back committed transfers");
            ThrowOnRender = null; f.Apply(f.Stage()); outcome = Get(allocator, "LastStageResourceOutcome");
            Assert(Get<bool>(allocator, "LastStageApplicationSucceeded") && Get<int>(outcome, "ModelWrites") == 0 && EnergyTotal(c) == 1001
                && c.magic.idleMagic + c.wandoos98.wandoosMagic == 701, "presentation-only retry cannot duplicate accounting");
        }
    }
    private static void CheckExplicitAllocation()
    {
        foreach (var mode in new[] {"ordinary", "best", "pinned"})
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0); c.curEnergy = c.idleEnergy = 1001; c.magic.curMagic = c.magic.idleMagic = 701;
            f.Fixture("Setting", "UseCustomRebirthPlan", false);
            var selected = mode == "ordinary" ? Optimize(300, 1001)
                : mode == "best" ? Static("AugmentOptimizer", "OptimizeBest", 300, false)
                : Static("AugmentOptimizer", "OptimizeTarget", 0, true, 300, false);
            Assert(Get<bool>(selected, "Success"), mode + ": current numeric decision is runnable");
            ThrowOnRender = "AugmentController.updateAugTexts"; RenderFailures = 0;
            var result = mode == "best" ? Static("AugmentOptimizer", "OptimizeAndAllocate", 300, false, 0) : f.Main("RequestAugmentAllocation", selected);
            Assert(Get<bool>(result, "Success") && RenderFailures == 1, mode + ": actual scoped claim retains verified committed accounting despite rendering failure");
            var main = Get(Production.GetType("NGUInjector.Main", true), "reference");
            var outcome = Get(Get(Get(main, "_resourceSetupExecutor"), "Application"), "Last");
            Assert(Get(outcome, "Disposition").ToString() == "ModelAppliedRenderFailed" && Get<int>(outcome, "ModelWrites") > 0,
                mode + ": real shared executor retains typed committed-render-failure evidence");
            Assert(c.augments.augs[0].augEnergy == Get<long>(selected, "EnergyToAugment") && c.augments.augs[0].upgradeEnergy == Get<long>(selected, "EnergyToUpgrade"), mode + ": exact selected bars are funded before presentation");
            Assert(EnergyTotal(c) == 1001 && c.magic.idleMagic == 701, mode + ": command conserves energy and preserves magic");
            ThrowOnRender = null; f.Main("RequestAugmentAllocation", selected);
            Assert(EnergyTotal(c) == 1001 && c.magic.idleMagic == 701, mode + ": repeated current command cannot duplicate resources");
        }
        using (var f = new NativePolicyFixture(300))
        {
            var c = f.Character; Enable(c, 0); var selected = Optimize(300, 1000);
            f.Fixture("Setting", "UseCustomRebirthPlan", false); f.Fixture("Setting", "ManageEnergy", false);
            var rejected = false;
            try { f.Main("RequestAugmentAllocation", selected); }
            catch (InvalidOperationException error) { rejected = error.Message == "Execution admission is not current."; }
            Assert(rejected && c.idleEnergy == 10000 && c.augments.augs.All(x => x.augEnergy == 0 && x.upgradeEnergy == 0), "real command cannot fund a decision without lane permission");
        }
    }
    private static void CheckIntegrationIl()
    {
        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(Program.InjectorPath))
        {
            var main = module.GetType("NGUInjector.Main");
            var allocator = module.GetType("NGUInjector.Managers.CustomStageAllocator");
            var optimizer = module.GetType("NGUInjector.Managers.AugmentOptimizer");
            bool Calls(Mono.Cecil.MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
                i.Operand is Mono.Cecil.MethodReference call && call.DeclaringType.FullName == type && call.Name == name);
            bool Reaches(Mono.Cecil.MethodDefinition method, string root, string name, HashSet<Mono.Cecil.MethodDefinition> visited)
            {
                if (!visited.Add(method) || !method.HasBody) return false;
                if (Calls(method, allocator.FullName, name)) return true;
                return method.Body.Instructions.Select(x => x.Operand as Mono.Cecil.MethodReference)
                    .Where(x => x != null && x.DeclaringType.FullName.StartsWith(main.FullName, StringComparison.Ordinal)
                        && x.Name.StartsWith("<" + root + ">", StringComparison.Ordinal)
                        && x.Module == module && x.DeclaringType.Scope == module)
                    .Any(x => Reaches(x.Resolve(), root, name, visited));
            }
            foreach (var name in new[] {"RunCustomPlanAutomation", "ReconcileCustomResources"})
                Assert(main.Methods.Where(x => x.Name == name).Any(x => Reaches(x, name, "ResolveStageResources", new HashSet<Mono.Cecil.MethodDefinition>())
                    && Reaches(x, name, "ApplyStageResources", new HashSet<Mono.Cecil.MethodDefinition>())), name + " resolves and applies its OWN real stage requirements, including compiler-generated callbacks, not ordinary fallback");
            Assert(Calls(main.Methods.Single(x => x.Name == "RunQuickCustomResourceRefresh"), main.FullName, "ReconcileCustomResources"), "quick scheduler ingress delegates to the real resource coordinator");
            Assert(Calls(allocator.Methods.Single(x => x.Name == "ApplyStageResources"), main.FullName, "ReconcileStageResourceSetup"), "stage actuator uses real production authority");
            Assert(Calls(allocator.Methods.Single(x => x.Name == "ObserveStageResources"), main.FullName, "ExecutionBaselineResourceMapMatches"), "stage credit requires actual baseline map observation");
            Assert(optimizer.Methods.Where(x => x.Name == "OptimizeAndAllocate").Any(x => Calls(x, main.FullName, "RequestAugmentAllocation")), "surviving optimizer command delegates authority, not private writes");
            var request = main.Methods.Single(x => x.Name == "RequestAugmentAllocation");
            foreach (var name in new[] {"BeginExecutionClaim", "SetExecutionResourcePolicy", "ReconcileExecutionResourceClaim", "EndExecutionClaim"})
                Assert(Calls(request, main.FullName, name), "explicit allocation retains real claim lifecycle: " + name);
            foreach (var name in new[] {"OptimizeAndAllocateIdle", "OptimizeAndAllocateTarget"})
                Assert(!optimizer.Methods.Any(x => x.Name == name), "obsolete private allocation writer remains removed: " + name);
        }
    }
}
