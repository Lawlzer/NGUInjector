using System;
using System.Collections;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static Type StageAllocator => Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);

    private static object PrepareLockedTimeMachineStage(string focus = "TM", bool controlGear = true)
    {
        var plan = SetupInlineStagePlan(controlGear);
        var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(stage, "EnergyFocus", focus == "TM" ? 1 : focus == "AUG" ? 2 : 3);
        Set(stage, "MagicFocus", focus == "TM" ? 1 : focus == "AUG" ? 4 : 3);
        Set(stage, "EnergyPriorities", new[] { focus, "TM", "AUG", "WAN" }.Distinct().ToArray());
        Set(stage, "MagicPriorities", new[] { "RIT", "TM", "WAN" });
        Set(stage, "UseWandoosWhenTimeMachineLocked", true);
        Set(stage, "GearGoal", controlGear ? StageGearGoal(focus == "TM" ? "TimeMachine" : focus == "AUG" ? "Augments" : "Wandoos") : -1);
        var stages = Array.CreateInstance(stage.GetType(), 1); stages.SetValue(stage, 0);
        Set(plan, "Stages", stages); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "StageDurationSeconds", 600);
        var items = (IList)Get(Get(Character, "inventory"), "inventory");
        for (var i = 0; i < 3; i++)
        {
            Set(items[i], "spec1Type", Enum.Parse(Field(items[i], "spec1Type").FieldType,
                i == 0 ? "Wandoos98" : i == 1 ? "Augs" : "EnergyPower"));
            Set(items[i], "spec1Cur", i == 2 ? 10000f : 1000000f);
        }
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", false);
        var tm = Get(Character, "timeMachineController"); Set(tm, "character", Character);
        Set(tm, "baseNormalSpeedDivider", 1e10f); Set(tm, "baseNormalGoldMultiDivider", 1e10f);
        Sequence(Get(Character, "bloodMagicController"), "normalSpeedDividers", 1, .018f);
        Set(Get(Character, "machine"), "realBaseGold", 1e9d);
        Invoke(typeof(Program), "RefreshBonuses");
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "ClearCache");
        return plan;
    }

    private static int StageGearGoal(string name) => (int)Enum.Parse(
        Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true), name);

    private static void SeedStageNgus()
    {
        foreach (var energy in new[] { true, false })
        {
            var skills = (IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills");
            foreach (var skill in skills) Set(skill, energy ? "energy" : "magic", 1L);
            var pool = energy ? Character : Get(Character, "magic");
            var idle = energy ? "idleEnergy" : "idleMagic";
            Set(pool, idle, (long)Get(pool, idle) - skills.Count);
        }
    }

    private static void AssertStageNguFree(string label, bool energy = true, bool magic = true)
    {
        foreach (var lane in new[] { true, false })
        {
            if (lane ? !energy : !magic) continue;
            var rows = (IList)Get(Get(Character, "NGU"), lane ? "skills" : "magicSkills");
            Check(rows.Cast<object>().All(x => (long)Get(x, lane ? "energy" : "magic") == 0),
                label + " clears every native " + (lane ? "Energy" : "Magic") + " NGU, including backing-array tails");
        }
    }

    private static string[] StagePriorities(object stage, bool energy, bool reserveBlood = false)
    {
        var args = new object[] { stage, energy, 600, 600, false, reserveBlood, false };
        var result = (string[])Invoke(StageAllocator, "EffectiveStagePriorities", args);
        Equal(false, args[4], "explicit/fallback priorities never introduce implicit first-half Wandoos AT");
        return result;
    }

    private static void AssertLockedStageResources(string label, bool sustainableBlood = true)
    {
        var w = Get(Character, "wandoos98"); var tm = Get(Character, "machine");
        Check((long)Get(w, "wandoosEnergy") > 0, label + " funds actual Wandoos Energy");
        var blood = ((IList)Get(Get(Character, "bloodMagic"), "ritual")).Cast<object>().Sum(x => (long)Get(x, "magic"));
        if (sustainableBlood)
        {
            Check(blood > (long)Get(Get(Character, "magic"), "curMagic") / 2, label + " gives Blood the majority of Magic before surplus");
            Check((long)Get(w, "wandoosMagic") > 0 && (long)Get(w, "wandoosMagic") < blood, label + " gives Wandoos only Blood surplus");
        }
        else
        {
            Equal(0d, Invoke(Character, "grossGoldPerSecond"), label + " uses the native zero-income TM challenge");
            Equal(0L, blood, label + " preserves the existing Blood sustainability prerequisite");
            Equal(Get(Get(Character, "magic"), "curMagic"), Get(w, "wandoosMagic"), label + " unavailable Blood leaves only Wandoos as surplus destination");
        }
        Equal(0L, Get(tm, "speedEnergy"), label + " does not fund TM Energy");
        Equal(0L, Get(tm, "goldMultiMagic"), label + " does not fund TM Magic");
        Check(((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>().All(x =>
            (long)Get(x, "augEnergy") == 0 && (long)Get(x, "upgradeEnergy") == 0), label + " funds no augment or upgrade");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum(), label + " funds no implicit AT");
        AssertStageNguFree(label);
    }

    private static void CheckLockedTimeMachineFallback()
    {
        var before = Assertions;
        foreach (var focus in new[] { "TM", "AUG", "WAN" })
        foreach (var unavailable in new[] { "locked", "challenge" })
            CheckLockedTimeMachineAllocation(focus, unavailable);
        CheckTimeMachineAvailabilityTransition();
        CheckUnlockedWandoosExplicitPriorities();
        CheckFinalMinuteAugmentSwitch();
        foreach (var lane in new[] { "energy", "magic", "both" }) CheckLockedTimeMachineDisabledLane(lane);
        CheckLockedTimeMachineOptIn();
        CheckLockedTimeMachineFlagPersistence();
        Console.WriteLine("Locked Time Machine fallback: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckLockedTimeMachineAllocation(string focus, string unavailable)
    {
        var plan = PrepareLockedTimeMachineStage(focus); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        if (unavailable != "locked") Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        if (unavailable == "challenge") Set(Get(Get(Character, "challenges"), "timeMachineChallenge"), "inChallenge", true);
        var label = focus + "/" + unavailable;
        SeedStageNgus();
        Equal("WAN", string.Join(",", StagePriorities(stage, true)), label + " resolves Energy only to Wandoos");
        Equal("RIT,WAN", string.Join(",", StagePriorities(stage, false)), label + " resolves Magic to Blood then Wandoos");
        Equal("RIT,WAN", string.Join(",", StagePriorities(stage, false, true)), label + " Iron Pill cannot invert Blood priority");
        RunVerifiedInline(plan, label);
        AssertLockedStageResources(label, unavailable != "challenge");
        var selection = Get(MainInstance, "_inlineStageSelection"); var policy = Get(selection, "Policy");
        Check(ReferenceEquals(stage, Get(policy, "Stage")), label + " keeps the original stage identity");
        Equal(StageGearGoal("Wandoos"), Get(selection, "GearGoal"), label + " selects effective Wandoos gear");
        Equal(550, Get(Get(Get(Character, "inventory"), "head"), "id"), label + " actually equips the Wandoos item");
        Check(((int[])Get(policy, "Beards")).Contains(4), label + " requests Ladybeard");
        var diggers = ((IEnumerable)Get(policy, "Diggers")).Cast<object>().Select(x => (int)Property(x, "Id")).ToArray();
        Check(diggers.Contains(1) && diggers.Contains(10) && !diggers.Contains(4) && !diggers.Contains(5), label + " requests Wandoos+Blood support, not NGU");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        AdvanceLockedStageClock(2);
        RunVerifiedInline(plan, label + " protected progress");
        Equal(2d, ((double[])Property(scheduler, "EarnedSeconds"))[0], label + " earns fixed-stage credit despite unavailable TM");
        Equal(entry, Property(scheduler, "Entry"), label + " does not replace scheduler identity");
    }

    private static void AdvanceLockedStageClock(double seconds)
    {
        foreach (var clock in new[] { "rebirthTime", "totalPlaytime" })
            Set(Get(Character, clock), "totalseconds", (double)Get(Get(Character, clock), "totalseconds") + seconds);
        Set(Get(MainInstance, "_protectedScheduler"), "_lastHeartbeat",
            (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - seconds);
    }

    private static void CheckTimeMachineAvailabilityTransition()
    {
        var plan = PrepareLockedTimeMachineStage(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        SeedStageNgus(); RunVerifiedInline(plan, "locked transition baseline");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        foreach (var unlocked in new[] { true, false, true })
        {
            Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", unlocked);
            Equal(false, CallMain("InlineStageSetupVerified", plan), "availability change invalidates old effective setup");
            Equal(unlocked ? "TM,AUG,WAN" : "WAN", string.Join(",", StagePriorities(stage, true)), "unlocked TM preserves explicit Energy order");
            Equal(unlocked ? "RIT,TM,WAN" : "RIT,WAN", string.Join(",", StagePriorities(stage, false, true)), "normal Magic keeps explicit Blood first even when reserved");
            RunVerifiedInline(plan, "availability transition/" + unlocked);
            var selection = Get(MainInstance, "_inlineStageSelection");
            Equal(StageGearGoal(unlocked ? "TimeMachine" : "Wandoos"), Get(selection, "GearGoal"), "availability transition reselects appropriate actual gear");
            Equal(unlocked ? 552 : 550, Get(Get(Get(Character, "inventory"), "head"), "id"), "availability transition equips the intended item");
            if (unlocked)
            {
                Check((long)Get(Get(Character, "machine"), "speedEnergy") > 0, "unlocked normal stage actually funds TM Energy");
                Check((long)Get(Get(Character, "machine"), "goldMultiMagic") > 0, "unlocked normal stage funds TM from Blood surplus");
            }
            else AssertLockedStageResources("relocked transition");
            AssertStageNguFree("availability transition");
            Equal(entry, Property(scheduler, "Entry"), "availability transition preserves scheduler entry");
            Check(ReferenceEquals(stage, ((Array)Get(plan, "Stages")).GetValue(0)), "availability transition never clones the stage");
        }
    }

    private static void CheckFinalMinuteAugmentSwitch()
    {
        var plan = PrepareLockedTimeMachineStage("AUG"); var early = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(early, "DurationSeconds", 2);
        var final = Activator.CreateInstance(early.GetType());
        Set(final, "Name", "Final minute Augments"); Set(final, "DurationSeconds", 60);
        Set(final, "EnergyFocus", 2); Set(final, "MagicFocus", 4); Set(final, "GearGoal", StageGearGoal("Augments"));
        Set(final, "EnergyPriorities", new[] { "AUG", "WAN" }); Set(final, "MagicPriorities", new[] { "RIT", "WAN" });
        var stages = Array.CreateInstance(early.GetType(), 2); stages.SetValue(early, 0); stages.SetValue(final, 1);
        Set(plan, "Stages", stages); Set(plan, "StageDurationSeconds", 62);
        SeedStageNgus(); RunVerifiedInline(plan, "early augment uses locked fallback"); AssertLockedStageResources("before final minute");
        AdvanceLockedStageClock(2); RunVerifiedInline(plan, "actual final-minute scheduler switch");
        Equal(1, Property(Get(MainInstance, "_protectedScheduler"), "StageIndex"), "fixed early fallback completes and enters final minute");
        Equal(60, ((IEnumerable)Property(Invoke(StageAllocator, "GetSchedulerSnapshot", plan,
            (double)Get(Get(Character, "rebirthTime"), "totalseconds")), "DurationSeconds")).Cast<int>().ElementAt(1), "final augment phase remains exactly sixty seconds");
        Check(((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>().Any(x =>
            (long)Get(x, "augEnergy") > 0), "final minute actually switches Energy to augments while TM stays locked");
        Equal(StageGearGoal("Augments"), Get(Get(MainInstance, "_inlineStageSelection"), "GearGoal"), "final minute selects Augment gear");
        Check((int)Get(Get(Get(Character, "inventory"), "head"), "id") != 550, "final minute replaces the fallback-only Wandoos item");
        AssertStageNguFree("final minute");
    }

    private static void CheckUnlockedWandoosExplicitPriorities()
    {
        var plan = PrepareLockedTimeMachineStage("WAN"); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        Set(stage, "MagicPriorities", new[] { "RIT", "WAN" });
        Equal("WAN,TM,AUG", string.Join(",", StagePriorities(stage, true)), "unlocked Wandoos keeps its explicit Energy chain in the first half");
        Equal("RIT,WAN", string.Join(",", StagePriorities(stage, false, true)), "unlocked Wandoos keeps Blood first during reservation");
        SeedStageNgus(); RunVerifiedInline(plan, "unlocked Wandoos explicit priorities");
        AssertLockedStageResources("unlocked Wandoos explicit priorities");
    }

    private static void CheckLockedTimeMachineDisabledLane(string lane)
    {
        var plan = PrepareLockedTimeMachineStage(controlGear: false); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        SeedStageNgus();
        var settings = Get(MainType, "Settings");
        var energy = lane != "energy" && lane != "both"; var magic = lane != "magic" && lane != "both";
        Set(settings, "_manageEnergy", energy); Set(settings, "_manageMagic", magic);
        var oldEnergy = CallMain("GlobalPresetLaneState", true); var oldMagic = CallMain("GlobalPresetLaneState", false);
        Invoke(StageAllocator, "ApplyStage", plan, stage, 600, 0, "disabled-" + lane, 600);
        if (!energy) Equal(oldEnergy, CallMain("GlobalPresetLaneState", true), lane + " preserves all unmanaged Energy allocations");
        else Check((long)Get(Get(Character, "wandoos98"), "wandoosEnergy") > 0, lane + " still funds the managed Energy lane");
        if (!magic) Equal(oldMagic, CallMain("GlobalPresetLaneState", false), lane + " preserves all unmanaged Magic allocations");
        else Check(((IList)Get(Get(Character, "bloodMagic"), "ritual")).Cast<object>().Any(x => (long)Get(x, "magic") > 0), lane + " still funds managed Blood");
        AssertStageNguFree("disabled " + lane, energy, magic);
        Equal(false, StageAllocator.GetProperty("LastStageApplicationSucceeded", All).GetValue(null), lane + " cannot earn fixed credit with a requested lane unmanaged");
        Equal(-1, Invoke(StageAllocator, "EffectiveStageGearGoal", stage), lane + " keeps an uncontrolled gear lane uncontrolled");
    }

    private static void CheckLockedTimeMachineOptIn()
    {
        var plan = PrepareLockedTimeMachineStage(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(stage, "UseWandoosWhenTimeMachineLocked", false);
        Set(stage, "EnergyPriorities", new[] { "TM", "NGU" }); Set(stage, "MagicPriorities", new[] { "TM", "NGU" });
        Equal("TM,NGU", string.Join(",", StagePriorities(stage, true)), "flag false retains old explicit priorities including NGUs");
        Equal("TM,RIT,NGU", string.Join(",", StagePriorities(stage, false, true)), "flag false retains old reservation ordering");
        Equal(StageGearGoal("TimeMachine"), Invoke(StageAllocator, "EffectiveStageGearGoal", stage), "flag false retains declared gear");
        Equal(false, Invoke(StageAllocator, "StageApplicationEligible", plan, stage, 600, 600), "flag false keeps locked declared TM ineligible");
        Set(stage, "UseWandoosWhenTimeMachineLocked", true);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        var controller = Get(Character, "timeMachineController"); Set(Character, "timeMachineController", null);
        try
        {
            Equal(true, Invoke(StageAllocator, "UsesLockedTimeMachineFallback", stage), "missing TM controller resolves to fallback");
            Equal("RIT,WAN", string.Join(",", StagePriorities(stage, false)), "missing TM controller cannot leave TM or NGU in Magic priorities");
        }
        finally { Set(Character, "timeMachineController", controller); }
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "wandoos"), "m_Interactable", false);
        Equal(false, Invoke(StageAllocator, "StageApplicationEligible", plan, stage, 600, 600), "fallback cannot fabricate eligibility for locked Wandoos");
        Set(Get(Get(Character, "buttons"), "wandoos"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "bloodMagic"), "m_Interactable", false);
        Equal(false, Invoke(StageAllocator, "StageApplicationEligible", plan, stage, 600, 600), "fallback cannot fabricate eligibility for locked Blood");
        var machine = Get(Character, "machine"); Set(Character, "machine", null);
        try { Equal(true, Invoke(StageAllocator, "UsesLockedTimeMachineFallback", stage), "missing TM model resolves to fallback rather than dereferencing it"); }
        finally { Set(Character, "machine", machine); }
    }

    private static void CheckLockedTimeMachineFlagPersistence()
    {
        var plan = PrepareLockedTimeMachineStage(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var store = Get(MainType, "CustomPlanStore");
        foreach (var enabled in new[] { true, false })
        {
            Set(stage, "UseWandoosWhenTimeMachineLocked", enabled);
            Invoke(store, "Save", "locked-tm-roundtrip", plan);
            var loaded = Invoke(store, "Load", "locked-tm-roundtrip");
            Equal(enabled, Get(((Array)Get(loaded, "Stages")).GetValue(0), "UseWandoosWhenTimeMachineLocked"), "fallback flag round-trips/" + enabled);
        }
        var legacy = Invoke(store, "DeserializeStage", JsonParse("{\"EnergyFocus\":1,\"MagicFocus\":1}"), 0);
        Equal(false, Get(legacy, "UseWandoosWhenTimeMachineLocked"), "legacy rows default to no locked-TM fallback");
        foreach (var invalid in new[] { "null", "0", "1", "\"true\"" })
            Throws(() => Invoke(store, "DeserializeStage", JsonParse("{\"UseWandoosWhenTimeMachineLocked\":" + invalid + "}"), 0),
                "must be a boolean", "fallback rejects nonboolean persisted flag/" + invalid);
    }
}
