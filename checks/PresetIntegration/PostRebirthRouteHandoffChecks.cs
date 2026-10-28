using System;
using System.Collections;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static void CheckPostRebirthRouteHandoff()
    {
        CheckPostRebirthNativeTimeline();
        CheckPostRebirthOrdinaryGoldEntry();
        CheckPostRebirthForeignCancellation();
        CheckPostRebirthRouteWitnessRejection();
    }

    private static object PreparePostRebirthRouteWorld()
    {
        var plan = SetupInlineStagePlan();
        var first = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(first, "Name", "Time Machine"); Set(first, "GearGoal", 5);
        Set(first, "EnergyPriorities", new[] { "TM" }); Set(first, "MagicPriorities", new[] { "TM" });
        Set(plan, "AutoIronPillEnabled", false);
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true);
        Set(settings, "_customAdventureGoldPush", true); Set(settings, "_customAdventureMode", 1); Set(settings, "_customAdventureZone", 12);
        Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
        Set(settings, "_beastMode", false); Set(settings, "_customAutoMoneyPitEnabled", true);
        Set(settings, "_customAutoExpEnabled", false); Set(settings, "_customAutoBoostEnabled", false);
        Set(settings, "_customAutoBloodSpellsEnabled", false); Set(settings, "_upgradeDiggers", false);
        Set(settings, "_moneyPitThreshold", 200000d);
        Set(settings, "_blacklistedBosses", new int[0]);
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(Get(Character, "stats"), "rebirthNumber", 231L);
        Set(Get(Character, "rebirthTime"), "totalseconds", 0d); Set(Get(Character, "totalPlaytime"), "totalseconds", 1000d);
        Set(Character, "bossID", 127); Set(Character, "highestBoss", 200);
        var tm = Get(Character, "timeMachineController"); Set(tm, "character", Character);
        Set(tm, "baseNormalSpeedDivider", 1f); Set(tm, "baseNormalGoldMultiDivider", 1f);
        Set(tm, "baseSpeedCost", 5000000f); Set(tm, "baseGoldMultiCost", 5000000f);
        Set(Get(Character, "machine"), "transferredBankLevels", true);
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        Set(adventure, "autoattacking", true); Set(adventure, "beastModeOn", false);
        Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        Set(Child(controller, "idleAttackMove"), "character", Character);
        Set(Get(controller, "idleAttackMove"), "pc", Get(controller, "playerController"));
        Set(Get(controller, "enemyAI"), "player", Get(controller, "playerController"));
        foreach (var name in new[] { "healMove", "hyperRegenMove" })
            Set(Child(Child(controller, name), "button"), "m_Interactable", false);
        ((long[])Get(Get(Character, "training"), "_attackTraining"))[1] = 0;
        Set(MainType, "PlayerController", Get(controller, "playerController"));
        Set(MainInstance, "_combManager", New("CombatManager"));
        Set(MainInstance, "_customTitanGoldPendingIndex", -1); Set(MainInstance, "_customTitanGoldIndex", -1); Set(MainInstance, "_customTitanFightIndex", -1);
        var store = Get(MainType, "CustomPlanStore");
        Invoke(store, "Save", Property(settings, "CustomPlanFile"), plan);
        plan = Invoke(store, "Load", Property(settings, "CustomPlanFile"));
        PreparePresetNativeBoost();
        RunVerifiedInline(plan, "rebirth 231 @0s native inline TimeMachine");
        Check((long)Get(Get(Character, "machine"), "speedEnergy") > 0, "@0s actual TimeMachine Energy is funded");
        Check((long)Get(Get(Character, "machine"), "goldMultiMagic") > 0, "@0s actual TimeMachine Magic is funded");
        PreparePostRebirthPit();
        return plan;
    }

    private static void PreparePostRebirthPit()
    {
        var pit = Get(Character, "pit") ?? Child(Character, "pit");
        var controller = Get(Character, "pitController") ?? Child(Character, "pitController");
        Set(controller, "character", Character); Set(controller, "inventoryController", Controller); Set(controller, "itemInfo", Get(Controller, "itemInfo"));
        Set(Child(pit, "pitTime"), "totalseconds", 1e9); Set(pit, "tossCount", 0); Set(pit, "totalGold", 0d);
        foreach (var name in new[] { "tier1TRewarded", "tier2TRewarded", "tier3TRewarded", "tier4TRewarded", "tier5TRewarded" }) Set(pit, name, true);
        var rng = Activator.CreateInstance(Field(pit, "pitState").FieldType); Set(rng, "s0", 1); Set(pit, "pitState", rng);
        Program.FinitePitRng = rng; Program.FinitePitRenderCalls = 0; Program.FinitePitRender = null; Program.FinitePitSetupReady = null;
        ListField(Child(Character, "achievements"), "achievementComplete", 512); Set(Child(Character, "allAchievements"), "character", Character);
        Set(Get(Character, "settings"), "badge2Part2Complete", true);
        ListField(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed", 1000);
        Set(Character, "realGold", 1e13);
    }

    private static void PostRebirthClock(double seconds)
    {
        Set(Get(Character, "rebirthTime"), "totalseconds", seconds);
        Set(Get(Character, "totalPlaytime"), "totalseconds", 1000d + seconds);
    }

    private static object EnterPostRebirthGold(object plan)
    {
        PostRebirthClock(12);
        Set(Get(Character, "adventure"), "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        CallMain("TryRouteCustomAdventureGoal");
        Set(Get(Character, "adventure"), "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        CallMain("TryRouteCustomAdventureGoal");
        Equal(true, Get(MainInstance, "_customAdventureGoldPushEnteredZone"), "@12s actual native gold push entered");
        Equal(18, Get(Get(Character, "adventure"), "zone"), "boss 127 selects actual Boring-Ass Earth zone 18");
        RunVerifiedInline(plan, "TimeMachine allocation continues under the exact gold claim");
        return Get(MainInstance, "_customAdventureGoldProcess");
    }

    private static void CheckPostRebirthNativeTimeline()
    {
        var plan = PreparePostRebirthRouteWorld();
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        var commitment = Invoke(scheduler, "ExportState");
        var generation = Get(MainInstance, "_protectedGeneration");
        var selection = Get(MainInstance, "_inlineStageSelection"); var acquisition = Get(selection, "Acquisition");
        var targets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
        var process = EnterPostRebirthGold(plan); var claim = Get(process, "Claim");
        var routes = AdventureZoneEntries; var swaps = Get(typeof(Program), "Swaps");
        var funded = (long)Get(Get(Character, "machine"), "speedEnergy");
        Set(Character, "capEnergy", (long)Get(Character, "capEnergy") + 137L);
        Set(Character, "curEnergy", (long)Get(Character, "curEnergy") + 137L);
        Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 137L);
        InlineObservedError = null;
        CallMain("AutomationRoutine");
        Equal(null, InlineObservedError, "ready pit waits behind gold without throwing from the ordinary loop");
        Equal(0, Get(Get(Character, "pit"), "tossCount"), "blocked admission never feeds the pit");
        Check((long)Get(Get(Character, "machine"), "speedEnergy") > funded, "blocked pit cannot starve healthy native TimeMachine funding");
        Equal(routes, AdventureZoneEntries, "blocked pit does not retire another active claim's route");
        Equal(swaps, Get(typeof(Program), "Swaps"), "blocked pit does not replace gold gear");
        Check(ReferenceEquals(claim, Get(Get(MainInstance, "_customAdventureGoldProcess"), "Claim")), "blocked pit retains the original gold acquisition");

        // As in the established native gold fixture, supply only encounter model
        // observations; the real CombatManager invokes its real completion path.
        PostRebirthClock(20);
        var controller = Get(Character, "adventureController"); var enemy = Child(controller, "currentEnemy");
        Set(enemy, "name", "post-rebirth gold boss"); Set(enemy, "enemyType", Enum.Parse(Field(enemy, "enemyType").FieldType, "boss"));
        CallMain("TryRouteCustomAdventureGoal");
        Equal(true, Get(Get(MainInstance, "_combManager"), "_isFighting"), "native gold manager observes the actual boss encounter");
        Set(controller, "currentEnemy", null); CallMain("TryRouteCustomAdventureGoal");
        Equal(false, Get(MainInstance, "_customAdventureGoldPushActive"), "@20s native completion retires gold authority");
        Equal(true, Get(claim, "Ended"), "gold completion releases the original claim");
        RunVerifiedInline(plan, "native completion returns directly to current inline TimeMachine");
        CallMain("TryRouteCustomAdventureGoal");
        Equal(12, Get(Get(Character, "adventure"), "zone"), "own ordinary farming resumes through native zone entry");

        PostRebirthClock(30); InlineObservedError = null;
        CallMain("AutomationRoutine");
        Equal(null, InlineObservedError, "@30s ready Money Pit and successor reconciliation do not abort ordinary work");
        Equal(1, Get(Get(Character, "pit"), "tossCount"), "own ordinary predecessor admits exactly one actual native feed");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "pit retires ordinary native combat before changing prerequisites");
        RunVerifiedInline(plan, "pit successor revalidates the same inline acquisition");
        CallMain("TryRouteCustomAdventureGoal");
        var head = Get(Get(Character, "inventory"), "head"); var material = ((string[])Get(selection, "Material")).ToArray();
        var searches = InlineGearSelections;
        ApplyPresetNativeBoost(head);
        Check(!material.SequenceEqual((string[])Get(selection, "Material")), "verified native material growth advances only the existing inline selection");
        Set(selection, "SupportAt", DateTime.MinValue); Set(MainInstance, "_customLastBeardRotation", DateTime.MinValue);
        Set(Get(Character, "machine"), "realBaseGold", 4000d);
        RunVerifiedInline(plan, "approved material and due support converge from an own ordinary route");
        Equal(acquisition, Get(selection, "Acquisition"), "material/support handoff does not reselect stage gear");
        Equal(searches, InlineGearSelections, "material/support handoff performs no new gear search");
        Equal(entry, Property(scheduler, "Entry"), "handoffs never reset stage entry");
        Equal(generation, Get(MainInstance, "_protectedGeneration"), "handoffs never reset stage generation");
        foreach (var field in new[] { "enteredAt", "expectedEnd", "durations", "visits", "cycle" })
            Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "handoffs preserve the original scheduler commitment/" + field);
        Check(targets.SequenceEqual((long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "handoffs never modify native AT targets");
        Equal(7L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], "handoffs preserve Basic Training");
        Equal(731L, Get(Get(Character, "res3"), "curRes3"), "handoffs preserve the unmanaged R3 pool");
    }

    private static void CheckPostRebirthOrdinaryGoldEntry()
    {
        var plan = PreparePostRebirthRouteWorld();
        Equal(true, Invoke(Get(MainInstance, "_combManager"), "MoveToZone", 12), "native ordinary farming precedes gold admission");
        var routes = AdventureZoneEntries;
        var process = EnterPostRebirthGold(plan);
        Equal(true, CallMain("RouteProcessReady", process), "ordinary-to-native-gold handoff verifies the exact acquired setup");
        Check(AdventureZoneEntries >= routes + 2, "native gold entry retires its ordinary predecessor before entering zone 18");
        CallMain("CancelCustomAdventureGoldPush", "fixture owned handoff complete");
        RunVerifiedInline(plan, "ordinary-to-native-gold release converges to the same inline owner");
    }

    private static void CheckPostRebirthForeignCancellation()
    {
        var plan = PreparePostRebirthRouteWorld(); var process = EnterPostRebirthGold(plan);
        var adventure = Get(Character, "adventure"); PostRebirthClock(20);
        Set(adventure, "zone", 12); // Foreign native route, not a successful executor move.
        var routes = AdventureZoneEntries; var swaps = Get(typeof(Program), "Swaps"); var resources = CallMain("GlobalPresetAllocationState");
        CallMain("ServiceCustomAdventureGoldPushCleanup");
        Equal(false, Get(MainInstance, "_customAdventureGoldPushActive"), "foreign-route cancellation withdraws gold authority");
        Equal(true, Get(Get(process, "Claim"), "Ended"), "foreign cancellation ends the admitted claim");
        Equal(12, Get(adventure, "zone"), "foreign native combat is retained, not adopted or forcibly retired");
        for (var i = 0; i < 3; i++)
        {
            PostRebirthClock(30 + i * 10); InlineObservedError = null;
            Check(((string)CallMain("RequestMoneyPit")).StartsWith("Money Pit waiting"), "foreign pit admission returns a wait result");
            CallMain("AutomationRoutine"); CallMain("CustomAdventureTick");
            Equal(null, InlineObservedError, "expected foreign admission adds no ordinary-loop error");
        }
        Equal(12, Get(adventure, "zone"), "repeated ordinary ticks preserve the rejected foreign route");
        Equal(routes, AdventureZoneEntries, "foreign cancellation/admission invokes no native route setter");
        Equal(swaps, Get(typeof(Program), "Swaps"), "foreign route retains its equipment prerequisites");
        Equal(resources, CallMain("GlobalPresetAllocationState"), "foreign setup cannot authorize resource withdrawal");
        Equal(0, Get(Get(Character, "pit"), "tossCount"), "rejected pit never invokes or consumes its native opportunity");
        Equal(false, CallMain("ExecutionOwnsDomain", Domain("Gear, Support, Route, FiniteAction")), "rejected admissions leak no execution claims");
    }

    private static void CheckPostRebirthRouteWitnessRejection()
    {
        foreach (var kind in new[] { "observed-only", "zone", "floor", "attack", "beast", "material", "support", "resource", "controller", "epoch", "review", "revision", "boss", "settings" })
        {
            PreparePostRebirthRouteWorld();
            var adventure = Get(Character, "adventure");
            if (kind == "observed-only")
            {
                Set(adventure, "zone", 12); CallMain("ObserveRouteGrant", 12);
            }
            else Equal(true, Invoke(Get(MainInstance, "_combManager"), "MoveToZone", 12), kind + " establishes an actual native ordinary route");
            var claim = CallMain("BeginExecutionClaim", "post-rebirth-witness", kind, Domain("Gear, Support, Route"), (Func<bool>)(() => true));
            var witness = (Func<bool>)CallMain("CaptureExecutionBaselineRouteRetirement", claim);
            Equal(kind != "observed-only", witness(), kind + " distinguishes native actuation from observation-only authority");
            var inventory = Get(Character, "inventory");
            var next = ((IList)Get(inventory, "inventory")).Cast<object>().First(x => (int)Get(x, "id") == 552);
            CallMain("PublishExecutionBaseline", RouteBaseline(next, "blocked-current-successor"));
            if (kind == "zone") Set(adventure, "zone", 13);
            if (kind == "floor") Set(adventure, "itopodEnd", (int)Get(adventure, "itopodEnd") + 1);
            if (kind == "attack") Set(adventure, "autoattacking", false);
            if (kind == "beast") Set(adventure, "beastModeOn", true);
            if (kind == "material") Set(Get(Get(Character, "inventory"), "head"), "level", 99);
            if (kind == "support") SetActiveBeards(0);
            if (kind == "resource") Set(Get(Character, "machine"), "speedEnergy", (long)Get(Get(Character, "machine"), "speedEnergy") - 1);
            if (kind == "controller") Child(Get(Character, "adventureController"), "zoneSelector");
            if (kind == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 232L);
            if (kind == "review") CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "route", "fixture unresolved route");
            if (kind == "revision") CallMain("InvalidateExecutionObservations", Domain("Route"));
            if (kind == "boss") Set(Get(Character, "bossController"), "nukeBoss", true);
            if (kind == "settings") Set(Get(MainType, "Settings"), "_customAdventureEnabled", false);
            Equal(false, witness(), kind + " stale predecessor witness fails closed");
            if (kind != "revision" && kind != "settings")
                Equal(false, ((Func<bool>)CallMain("CaptureExecutionBaselineRouteRetirement", claim))(), kind + " fresh admission cannot adopt the changed native predecessor");
            var routes = AdventureZoneEntries; var swaps = Get(typeof(Program), "Swaps");
            var zone = Get(adventure, "zone"); var resources = CallMain("GlobalPresetAllocationState");
            var support = Property(CallMain("CaptureSupportState"), "StableKey");
            Equal(false, CallMain("EndExecutionClaim", claim, true), kind + " unavailable successor admission returns false, not a finally exception");
            Equal(zone, Get(adventure, "zone"), kind + " preserves the native route");
            Equal(routes, AdventureZoneEntries, kind + " invokes no native route setter");
            Equal(swaps, Get(typeof(Program), "Swaps"), kind + " performs no setup mutation");
            Equal(resources, CallMain("GlobalPresetAllocationState"), kind + " preserves native bars");
            Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), kind + " preserves coupled support");
        }
    }
}
