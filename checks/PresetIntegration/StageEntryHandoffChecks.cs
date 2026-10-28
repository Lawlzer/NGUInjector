using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static WeightedAtBudgetTrace WeightedAtBudget;
    private sealed class WeightedAtBudgetTrace
    {
        internal readonly List<string> Events = new List<string>();
        internal readonly Stopwatch Wall = Stopwatch.StartNew();
        internal readonly TimeSpan Cpu = Process.GetCurrentProcess().TotalProcessorTime;
        internal int Evaluations;
        internal void Record(string boundary, Stopwatch operation)
        {
            Events.Add(boundary + " op=" + operation?.ElapsedMilliseconds + "ms/running=" + operation?.IsRunning
                + " wall=" + Wall.ElapsedMilliseconds + "ms cpu=" + (Process.GetCurrentProcess().TotalProcessorTime - Cpu).TotalMilliseconds + "ms eval=" + Evaluations);
        }
    }

    public static void ObserveWeightedAtBudgetDeadline(Stopwatch clock)
    {
        if (WeightedAtBudget == null || clock.ElapsedMilliseconds < 3000) return;
        WeightedAtBudget.Record("deadline", clock);
        Console.WriteLine("Weighted AT deadline stack: " + Environment.StackTrace);
    }

    public static void ObserveWeightedAtBudgetResult(object result, Stopwatch clock, bool plan)
    {
        if (WeightedAtBudget == null) return;
        WeightedAtBudget.Record(plan ? "plan-return/evaluated=" + Get(result, "Evaluated") + "/budget=" + Get(result, "Budget") : "capture-return", clock);
    }

    // Parent wiring: compile this partial and dispatch --stage-entry-handoff to
    // CheckStageEntryHandoff(). Existing native route/boss observers stay real.
    private static void CheckStageEntryHandoff()
    {
        var before = Assertions;
        var failures = new List<Exception>();
        Action<string, Action> run = (name, test) => {
            try { test(); Console.WriteLine("PASS stage entry handoff/" + name); }
            catch (Exception error) { failures.Add(new Exception(name, error)); }
            finally
            {
                Program.FinitePreparationSelectionStarting = null;
                GearRenderObserver = null; PodInterference = null;
                AdventureZoneEntryObserver = AdventureZoneEntryCompletedObserver = null;
                RouteNativeActionReturned = null;
            }
        };
        // Reuse the loader's retained native getter, not its constant-one fallback:
        // these checks deliberately preserve nonzero W history across the handoff.
        var nativeWandoos = Program.RepeatablePurchaseFixtureActive;
        Program.RepeatablePurchaseFixtureActive = true;
        try
        {
            run("natural funded ordinary predecessor", () => CheckStageEntryNativeTimeline(false, true));
            run("lower Magic capacity ordinary predecessor", () => CheckStageEntryNativeTimeline(true, true));
            run("lower Magic capacity already safe", () => CheckStageEntryNativeTimeline(true, false));
        run("stale failed preparation", CheckStageEntryStalePreparation);
        foreach (var energy in new[] { true, false })
            run("fruit waits behind preparation/" + (energy ? "Energy" : "Magic"), () => CheckStageEntryFruitClaimWait(energy));
            run("pre-admission native re-entry control", () => CheckStageEntryPreAdmissionRoute(false));
            run("pre-admission foreign route after safe return", () => CheckStageEntryPreAdmissionRoute(true));
            foreach (var lane in new[] { "Energy", "Magic", "both" })
                run("unmanaged " + lane, () => CheckStageEntryUnmanaged(lane));
            foreach (var change in new[] { "gear", "support", "resource", "route", "permission" })
                run("retirement callback/" + change, () => CheckStageEntryForeignCallback(change));
        }
        finally { Program.RepeatablePurchaseFixtureActive = nativeWandoos; }
        if (failures.Count != 0) throw new AggregateException("Native stage entry handoff regressions", failures);
        Console.WriteLine("Stage entry handoff: " + (Assertions - before) + " native assertions.");
        CheckWeightedAtHandoff();
    }

    private static object PrepareWeightedAtHandoff(bool lazy, string scenario)
    {
        var id = "weighted-at-handoff-" + scenario;
        SetupAdventureDynamic(id, "[0,0,0,1,0,0,0,0,0,100,0,0]");
        var settings = Get(MainType, "Settings"); var store = Get(MainType, "CustomPlanStore");
        var library = Get(store, "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
        var preset = ((IEnumerable)GetChildren(Json(root, "Presets"))).Cast<object>().Single(x => Equals(id, Property(Json(x, "Id"), "Value")));
        SetJson(preset, "AdventureDynamicWeight", Json(JsonParse("{\"v\":0}"), "v"));
        SetJson(preset, "AdvancedTrainingWeights", JsonParse("[1,1,0,0,0]"));
        SetJson(preset, "EnergyPriorities", JsonParse("[\"AT\"]"));
        SetJson(preset, "MagicPriorities", JsonParse("[]"));
        SetJson(preset, "ResourcePrioritiesReviewed", Json(JsonParse("{\"v\":true}"), "v"));
        Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"weighted-ngu-successor-" + scenario + "\",\"Name\":\"NGU successor\",\"Weights\":[1,1,0,0,0,0,0,0,0,0,0,0],\"SupportMode\":\"current\",\"SwapSeconds\":0,\"EnergyPriorities\":[\"NGU\"],\"MagicPriorities\":[\"NGU\"],\"ResourcePrioritiesReviewed\":true}"));
        Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        Set(settings, "_useCustomRebirthPlan", true); Set(settings, "_autoRebirth", false);
        Set(settings, "_customAutoMoneyPitEnabled", false); Set(settings, "_customAutoExpEnabled", false);
        Set(settings, "_customAutoBoostEnabled", false); Set(settings, "_customAutoBloodSpellsEnabled", false);
        Set(settings, "_upgradeDiggers", false); Set(settings, "_blacklistedBosses", new int[0]);
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", false);
        Set(Character, "bossID", 200); Set(Character, "highestBoss", 200); Set(Character, "curHP", 1f);
        Set(Child(Character, "API"), "character", Character);
        Set(settings, "_manageR3", false); Set(settings, "_energyNguWeights", new[] { 1, 2, 0, 0, 0, 0, 0, 0, 0 });
        Set(settings, "_magicNguWeights", new[] { 2, 1, 0, 0, 0, 0, 0 });
        var at = Get(Character, "advancedTraining"); Set(at, "transferredBankedLevels", true);
        Set(at, "levelTarget", new long[] { scenario == "returned-targets" ? 37 : 3000, scenario == "returned-targets" ? 41 : 3000, 37, 41, 43 });
        var allAt = Get(Character, "advancedTrainingController");
        foreach (var name in new[] { "defense", "attack", "block", "wandoosEnergy", "wandoosMagic" }) Set(Get(allAt, name), "alladvancedTraining", allAt);
        var training = Get(Character, "training");
        foreach (var name in new[] { "_attackTraining", "_defenseTraining" }) Set(training, name, Enumerable.Repeat(25000L, 6).ToArray());
        ((long[])Property(training, "attackEnergy"))[0] = 7; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 7);
        var r3 = Get(Character, "res3"); Set(r3, "capRes3", 1000L); Set(r3, "curRes3", 731L); Set(r3, "idleRes3", 431L);
        Set(Get(Character, "wishesController"), "character", Character);
        ListField(Get(Character, "wishesController"), "properties", 512);
        ListField(Get(Character, "wishes"), "wishes", 512);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3", 300L);
        var wandoos = Get(Character, "wandoos98"); Set(wandoos, "energyLevel", 7L); Set(wandoos, "magicProgress", .25f);
        var arbitrary = Get(Character, "arbitrary"); Set(arbitrary, "boughtLazyITOPOD", lazy); Set(arbitrary, "lazyITOPODOn", lazy);
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory"); var head = Get(inventory, "head");
        for (var i = 0; i < 2; i++)
        {
            Set(items[i], "type", Get(head, "type")); Set(items[i], "id", 550 + i); Set(items[i], "bossRequired", 1); Set(items[i], "removable", true);
            Set(items[i], "spec1Type", Enum.Parse(Field(items[i], "spec1Type").FieldType, i == 0 ? "AdvTraining" : "NGU"));
            Set(items[i], "spec1Cur", 1000000f); Set(items[i], "spec1Cap", 1000000f);
        }
        Set(Character, "energyPower", 1e10f); Invoke(typeof(Program), "RefreshBonuses");
        var plan = Invoke(store, "Load", "fixture");
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "Name", "Mandatory weighted AT"); Set(stage, "PresetId", id); Set(stage, "CompletionMode", 1);
        Set(stage, "TargetAdvancedTrainingPower", 3000L); Set(stage, "TargetAdvancedTrainingToughness", 3000L); ConfigureStageFightSchedule(stage, "weighted-at-entry");
        var next = Activator.CreateInstance(stage.GetType()); Set(next, "Name", "NGU"); Set(next, "PresetId", "weighted-ngu-successor-" + scenario); Set(next, "DurationSeconds", 600);
        var stages = Array.CreateInstance(stage.GetType(), 2); stages.SetValue(stage, 0); stages.SetValue(next, 1);
        Set(plan, "Stages", stages); Set(plan, "StageSchedulerEnabled", true); Set(plan, "StageLoopStartIndex", -1);
        Set(plan, "AutoActivateFruits", false); Set(plan, "AutoRebirthEnabled", false); Set(plan, "TimeTriggerEnabled", true); Set(plan, "TimeTriggerSeconds", 86400);
        Set(plan, "AutoIronPillEnabled", false);
        Invoke(store, "Save", "fixture", plan); plan = Invoke(store, "Load", "fixture");
        InlineObservedError = null;
        CallMain("RunCustomPlanAutomation", plan);
        Equal(true, Property(MainInstance, "BossPushRunning"), "weighted AT FightOnEntry admits the actual native boss process: " + CallMain("GetCustomAutomationStatus") + "; " + InlineObservedError);
        RetireStageEntryNativeBoss(plan);
        Console.WriteLine("Weighted AT observers/" + scenario + ": capture=" + (PresetCaptureObserver != null) + " search=" + (PresetSearchObserver != null)
            + " completed=" + (PresetPlanCompletedObserver != null) + " evaluation=" + (PresetEvaluationObserver != null) + " native=" + (CaptureNativeObserver != null));
        var trace = new WeightedAtBudgetTrace(); WeightedAtBudget = trace;
        try { CallMain("RunCustomPlanAutomation", plan); }
        finally
        {
            WeightedAtBudget = null;
            Console.WriteLine("Weighted AT budget/" + scenario + ": " + string.Join("; ", trace.Events));
        }
        if (!(bool)CallMain("GlobalPresetStageReceiptValid", plan)) Console.WriteLine("Weighted AT acquisition stack/" + scenario + ": " + Get(MainInstance, "_globalPresetReconcileException"));
        Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "weighted mandatory AT acquires real native farming prerequisites: " + CallMain("GetCustomAutomationStatus"));
        Check(((long[])Get(at, "energy")).Take(2).All(x => x > 0), "both unfinished mandatory bars are actually funded");
        Equal(1000, Get(Get(Character, "adventure"), "zone"), "weighted predecessor enters native ITOPOD");
        Equal(0, ((IList)Property(MainInstance, "_executionClaims")).Count, "entry fight has no remaining live claims");
        return plan;
    }

    private static void CompleteWeightedAtHandoff()
    {
        var at = Get(Character, "advancedTraining"); var levels = (long[])Get(at, "level");
        for (var tick = 0; tick < 12000 && (levels[0] < 3000 || levels[1] < 3000); tick++)
            foreach (var name in new[] { "defense", "attack" }) Invoke(Get(Get(Character, "advancedTrainingController"), name), "updateAdvancedTraining");
        // hitTarget runs on the tick after the native level reaches its target.
        foreach (var name in new[] { "defense", "attack" }) Invoke(Get(Get(Character, "advancedTrainingController"), name), "updateAdvancedTraining");
        Equal(3000L, levels[0], "actual native toughness completes its original 3000 target");
        Equal(3000L, levels[1], "actual native power completes its original 3000 target");
        Check(((long[])Get(at, "energy")).Take(2).All(x => x == 0), "native hitTarget releases both bars without refreshing the injector receipt");
    }

    private static void ShiftWeightedAtFloor()
    {
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        var before = Get(adventure, "itopodStart"); var bonuses = (IList)Get(Get(Character, "inventory"), "macguffinBonuses"); bonuses[19] = 1.2f;
        var enemy = Child(controller, "currentEnemy"); Set(enemy, "enemyType", Enum.Parse(Field(enemy, "enemyType").FieldType, "itopod")); Set(enemy, "maxHP", 600f);
        Invoke(controller, "enemyDeath");
        Check(!Equals(before, Get(adventure, "itopodStart")), "actual native kill shifts Lazy ITOPOD away from the acquired candidate floor");
        Equal(Invoke(Character, "calculateBestItopodLevel"), Get(adventure, "itopodStart"), "shift is exactly the native current optimum");
        Equal(Get(adventure, "itopodStart"), Get(adventure, "itopodEnd"), "native Lazy range is one exact floor");
    }

    private static void CheckWeightedAtHandoff()
    {
        var before = Assertions; var failures = new List<Exception>();
        foreach (var mode in new[] { "release", "floor", "both", "returned-targets", "gear", "support", "resource", "partial-at", "uncompleted-release", "target", "foreign-floor", "foreign-single-floor", "lazy-disabled", "resource-model",
            "callback-gear", "callback-support", "callback-resource", "callback-route", "callback-permission", "callback-target", "callback-lazy", "callback-controller", "callback-model", "callback-revision" })
        try
        {
            var lazy = mode != "release"; var plan = PrepareWeightedAtHandoff(lazy, mode);
            var receipt = Get(MainInstance, "_globalPresetReceipt"); var policy = Get(receipt, "Policy"); var acquired = Get(policy, "Acquisition");
            var preserve = CaptureStageEntryPreservedState();
            var definition = Get(policy, "Definition"); var candidateFloor = Get(Get(policy, "Candidate"), "Floor");
            var horizon = Get(policy, "Horizon");
            var allocation = Get(receipt, "Allocations"); var fight = Get(MainInstance, "_customStageFightKey");
            var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry"); var generation = Get(MainInstance, "_protectedGeneration");
            if (mode != "floor" && mode != "uncompleted-release") CompleteWeightedAtHandoff();
            if (lazy) ShiftWeightedAtFloor();
            Equal(allocation, Get(receipt, "Allocations"), "native progress cannot rewrite original application evidence/" + mode);
            if (mode == "gear") Set(Get(Get(Character, "inventory"), "head"), "curAttack", 12345f);
            if (mode == "support") SetActiveBeards(3);
            if (mode == "resource") { Set(Get(Character, "machine"), "speedEnergy", 1L); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 1); }
            if (mode == "partial-at") { ((long[])Get(Get(Character, "advancedTraining"), "energy"))[0] = 2; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 2); }
            if (mode == "uncompleted-release")
            {
                var energy = (long[])Get(Get(Character, "advancedTraining"), "energy");
                Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + energy[0]); energy[0] = 0;
            }
            if (mode == "target") ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[0] = 3001;
            if (mode == "foreign-floor") Set(Get(Character, "adventure"), "itopodEnd", (int)Get(Get(Character, "adventure"), "itopodEnd") + 1);
            if (mode == "foreign-single-floor")
            {
                var route = Get(Character, "adventure"); var floor = (int)Get(route, "itopodStart") + 1;
                Set(route, "itopodStart", floor); Set(route, "itopodEnd", floor);
            }
            if (mode == "lazy-disabled") Set(Get(Character, "arbitrary"), "lazyITOPODOn", false);
            if (mode == "resource-model") Set(Character, "magic", Invoke(Get(Character, "magic"), "MemberwiseClone"));
            if (mode != "floor" && mode != "uncompleted-release") CallMain("GetProtectedStage", plan);
            if (mode == "returned-targets")
            {
                CallMain("ServiceGlobalPresetNativeTargets"); CallMain("ServiceExecutionNativeSettings");
                Equal(null, Get(MainInstance, "_globalPresetNativeTargets"), "authorized completed target return retires only its lease");
                Equal(37L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[0], "authorized return preserves original native toughness target");
                Equal(41L, ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[1], "authorized return preserves original native power target");
            }
            var commitment = Invoke(scheduler, "ExportState");
            var physical = CallMain("GlobalPresetAllocationState"); var swaps = Get(typeof(Program), "Swaps"); var routes = AdventureZoneEntries;
            var safe = false; var incomingGear = false;
            AdventureZoneEntryCompletedObserver = zone => {
                if (zone != -1) return; safe = true;
                Equal(swaps, Get(typeof(Program), "Swaps"), "old route retires before any incoming gear/" + mode);
                if (mode == "callback-gear") Set(Get(Get(Character, "inventory"), "head"), "curAttack", 12345f);
                if (mode == "callback-support") SetActiveBeards(3);
                if (mode == "callback-resource") { Set(Get(Character, "machine"), "speedEnergy", 1L); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 1); }
                if (mode == "callback-route") Set(Get(Character, "adventure"), "zone", 13);
                if (mode == "callback-permission") Set(Get(MainType, "Settings"), "_manageMagic", false);
                if (mode == "callback-target") ((long[])Get(Get(Character, "advancedTraining"), "levelTarget"))[0] = 3001;
                if (mode == "callback-lazy") Set(Get(Character, "arbitrary"), "lazyITOPODOn", false);
                if (mode == "callback-controller") Set(Get(Character, "adventureController"), "zoneSelector", Invoke(Get(Get(Character, "adventureController"), "zoneSelector"), "MemberwiseClone"));
                if (mode == "callback-model") Set(Character, "magic", Invoke(Get(Character, "magic"), "MemberwiseClone"));
                if (mode == "callback-revision") CallMain("InvalidateExecutionObservations", Domain("Energy"));
            };
            GearRenderObserver = () => { incomingGear = true; Equal(true, safe, "incoming gear requires prior safe retirement/" + mode); };
            if (mode == "floor" || mode == "uncompleted-release")
                Equal(mode == "floor", CallMain("RetirePreviousGlobalPresetStageRoute", "floor-only-control"), "only native-completion allocation deltas may accompany native floor drift");
            else CallMain("RunCustomPlanAutomation", plan);
            var positive = mode == "release" || mode == "floor" || mode == "both" || mode == "returned-targets";
            Equal(positive || mode.StartsWith("callback-", StringComparison.Ordinal), safe, "retirement admission distinguishes native effects from foreign edits/" + mode);
            Equal(acquired, Get(policy, "Acquisition"), "handoff preserves acquired predecessor identity/" + mode);
            Equal(definition, Get(policy, "Definition"), "handoff preserves acquired predecessor policy/" + mode);
            Equal(horizon, Get(policy, "Horizon"), "handoff never renews acquired predecessor horizon/" + mode);
            Equal(candidateFloor, Get(Get(policy, "Candidate"), "Floor"), "native floor observation never rebinds acquired candidate/" + mode);
            Equal(generation, Get(MainInstance, "_protectedGeneration"), "handoff never resets generation/" + mode);
            Equal(fight, Get(MainInstance, "_customStageFightKey"), "handoff never replays consumed AT entry fight/" + mode);
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "earned", "applied", "visits" })
                Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "handoff preserves current scheduler commitment/" + mode + "/" + field);
            preserve();
            if (positive && mode != "floor")
            {
                Equal(Convert.ToInt64(entry) + 1, Property(scheduler, "Entry"), "native completion naturally selects exactly one NGU successor");
                Equal(1, Property(scheduler, "StageIndex"), "actual scheduler advances to NGU without override");
                Equal("", Property(scheduler, "LastOverride"), "no stage restart/skip/set was used");
                Equal(true, ((bool[])Property(scheduler, "Completed"))[0], "mandatory AT completion history remains earned");
                Equal(true, CallMain("GlobalPresetStageReceiptValid", plan), "actual NGU successor acquires verified native funding");
                foreach (var energy in new[] { true, false })
                {
                    var skills = (IList)Get(Get(Character, "NGU"), energy ? "skills" : "magicSkills");
                    Check(skills.Cast<object>().Take(2).All(x => (long)Get(x, energy ? "energy" : "magic") > 0), "both positive Misc NGUs receive actual " + (energy ? "Energy" : "Magic"));
                    Check(skills.Cast<object>().Skip(2).All(x => (long)Get(x, energy ? "energy" : "magic") == 0), "Misc zero exclusions remain empty on " + (energy ? "Energy" : "Magic"));
                    var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
                    var native = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
                    var allocated = native.Keys.Cast<object>().Where(key => (int)Invoke(writer, "Lane", key) == (energy ? 0 : 1) && native[key] != null)
                        .Sum(key => ((long[])native[key]).Sum());
                    var model = energy ? Character : Get(Character, "magic");
                    Equal(Get(model, energy ? "curEnergy" : "curMagic"), allocated + (long)Get(model, energy ? "idleEnergy" : "idleMagic"), "NGU handoff conserves exact native lane budget/" + energy);
                }
                Equal(7L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], "native basic training survives current-successor funding");
                Check(incomingGear, "different current NGU setup actually reaches native gear actuator");
            }
            if (!positive)
            {
                if (mode == "uncompleted-release") CallMain("RetirePreviousGlobalPresetStageRoute", "floor-only-control");
                else CallMain("RunCustomPlanAutomation", plan);
                Equal(swaps, Get(typeof(Program), "Swaps"), "foreign state retains predecessor prerequisites/" + mode);
                Equal(true, Property(Json(CallMain("GetResourceCoordinationRuntime"), "blocked"), "AsBool"), "genuine refusal is explicitly blocked/" + mode);
                if (!mode.StartsWith("callback-", StringComparison.Ordinal))
                { Equal(routes, AdventureZoneEntries, "foreign admission makes no native route call/" + mode); Equal(physical, CallMain("GlobalPresetAllocationState"), "foreign allocations stay untouched/" + mode); }
            }
            Console.WriteLine("PASS weighted AT handoff/" + mode);
        }
        catch (Exception error) { failures.Add(new Exception(mode, error)); }
        finally { GearRenderObserver = null; AdventureZoneEntryCompletedObserver = null; }
        if (failures.Count != 0) throw new AggregateException("Weighted AT handoff", failures);
        Console.WriteLine("Weighted AT handoff: " + (Assertions - before) + " native assertions.");
    }

    private static object PrepareStageEntryHandoff(bool lowerMagicCapacity = false, bool ordinaryRoute = true)
    {
        var plan = SetupInlineStagePlan();
        var stages = (Array)Get(plan, "Stages"); var first = stages.GetValue(0); var successor = stages.GetValue(1);
        Set(first, "Name", "Funded inline Time Machine"); Set(first, "DurationSeconds", 2);
        Set(first, "EnergyPriorities", new[] { "TM" }); Set(first, "MagicPriorities", new[] { "TM" });
        Set(first, "GearGoal", StageGearGoal("TimeMachine")); Set(first, "FightSchedule", null);
        var automatic = Invoke(first, "MemberwiseClone");
        Set(automatic, "Name", "Automatic Augments + Wandoos with entry fight");
        Set(automatic, "ExecutionMode", 1); Set(automatic, "DurationSeconds", 120); ConfigureStageFightSchedule(automatic, "automatic-entry");
        Set(automatic, "EnergyPriorities", new[] { "AUG", "WAN" }); Set(automatic, "MagicPriorities", new[] { "WAN" });
        Set(successor, "Name", "Current mandatory AT successor"); Set(successor, "DurationSeconds", 60);
        Set(successor, "EnergyPriorities", new[] { "AT", "NGU" }); Set(successor, "MagicPriorities", new[] { "NGU" });
        Set(successor, "FightSchedule", null); Set(successor, "GearGoal", StageGearGoal("MagicCap"));
        stages = Array.CreateInstance(first.GetType(), 3);
        stages.SetValue(first, 0); stages.SetValue(automatic, 1); stages.SetValue(successor, 2);
        Set(plan, "Stages", stages); Set(plan, "StageLoopStartIndex", -1); Set(plan, "StageDurationSeconds", 3600);
        Set(plan, "AutoIronPillEnabled", false);
        var settings = Get(MainType, "Settings");
        Set(settings, "_manageR3", false); Set(settings, "_manualTitanEnabled", false);
        Set(settings, "_customAutoMoneyPitEnabled", false); Set(settings, "_customAutoExpEnabled", false);
        Set(settings, "_customAutoBoostEnabled", false); Set(settings, "_customAutoBloodSpellsEnabled", false);
        Set(settings, "_upgradeDiggers", false); Set(settings, "_blacklistedBosses", new int[0]);
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(Character, "bossID", 200); Set(Character, "highestBoss", 200); Set(Character, "curHP", 1f);
        Set(Child(Character, "API"), "character", Character);
        Equal(false, Get(Get(Character, "settings"), "submitHighscores"), "offline native beginFight cannot submit external scores");
        Check((double)Invoke(Get(Character, "wandoos98Controller"), "wandoosBonus") > 1d,
            "nonzero W history uses the retained actual native bonus getter, not the shared constant-one fallback");
        var training = Get(Character, "training");
        foreach (var name in new[] { "_attackTraining", "_defenseTraining" })
            Set(training, name, Enumerable.Repeat(25000L, 6).ToArray());
        var at = Get(Character, "advancedTraining"); Set(at, "levelTarget", new long[] { 0, 0, 37, 41, 43 });
        var allAt = Get(Character, "advancedTrainingController");
        foreach (var name in new[] { "defense", "attack", "block", "wandoosEnergy", "wandoosMagic" })
            Set(Get(allAt, name), "alladvancedTraining", allAt);
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        Set(items[0], "spec1Type", Enum.Parse(Field(items[0], "spec1Type").FieldType, "EnergyPower"));
        Set(items[0], "spec1Cur", 1000000f);
        Set(items[1], "spec1Cur", 20000f);
        // Keep a real cap cliff separate from ordinary-route admission. In particular,
        // already being in Safe Zone is not permission to preserve an over-cap TM bar.
        if (lowerMagicCapacity)
            foreach (var item in new[] { Get(inventory, "head"), items[0] })
            {
                Set(item, "spec2Type", Enum.Parse(Field(item, "spec2Type").FieldType, "MagicCap"));
                Set(item, "spec2Cur", 10000f);
            }
        Set(items[2], "spec3Type", Enum.Parse(Field(items[2], "spec3Type").FieldType, "Wandoos98"));
        Set(items[2], "spec3Cur", 1000000f);
        var tm = Get(Character, "timeMachineController"); Set(tm, "character", Character);
        // Native black-bar demand uses the serialized dividers, not the gold costs.
        // Leave demand above the entire pool so the predecessor is genuinely full.
        Set(tm, "baseNormalSpeedDivider", 1e10f); Set(tm, "baseNormalGoldMultiDivider", 1e10f);
        Set(tm, "baseSpeedCost", 5000000f); Set(tm, "baseGoldMultiCost", 5000000f);
        Set(Get(Character, "machine"), "transferredBankLevels", true);
        Invoke(typeof(Program), "RefreshBonuses");
        var magic = Get(Character, "magic");
        var energyPool = (long)Invoke(Character, "totalCapEnergy"); var magicPool = (long)Invoke(Character, "totalCapMagic");
        Set(Character, "curEnergy", energyPool); Set(Character, "idleEnergy", energyPool - 7);
        Set(magic, "curMagic", magicPool); Set(magic, "idleMagic", magicPool);
        Invoke(Plugin.GetType("NGUInjector.Managers.GearOptimizer", true), "ClearCache");
        var store = Get(MainType, "CustomPlanStore"); var file = Property(settings, "CustomPlanFile");
        Invoke(store, "Save", file, plan); plan = Invoke(store, "Load", file);
        RunVerifiedInline(plan, "stage 0 actual Time Machine acquisition");
        Equal(550, Get(Get(inventory, "head"), "id"), "Time Machine selects its own native power head");
        var selection = Get(MainInstance, "_inlineStageSelection");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        energyPool = (long)Invoke(Character, "totalCapEnergy"); magicPool = (long)Invoke(Character, "totalCapMagic");
        var energyGrowth = energyPool - (long)Get(Character, "curEnergy");
        var magicGrowth = magicPool - (long)Get(magic, "curMagic");
        Check(energyGrowth >= 0 && magicGrowth >= 0, "acquired TM capacity can hold the existing native pools");
        // Offline native-pool growth seam: a gear swap raises capacity, not current
        // resources. Add the missing pool to idle without changing any funded bar.
        Set(Character, "curEnergy", energyPool); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + energyGrowth);
        Set(magic, "curMagic", magicPool); Set(magic, "idleMagic", (long)Get(magic, "idleMagic") + magicGrowth);
        RunVerifiedInline(plan, "stage 0 funds actual acquired TM capacity after native-pool growth");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), "pool refill retains the acquired TM selection");
        Equal(entry, Property(scheduler, "Entry"), "pool refill cannot manufacture a new protected entry");
        Equal(Invoke(Character, "totalCapEnergy"), Get(Character, "curEnergy"), "TM predecessor has its actual post-acquisition Energy capacity filled");
        Equal(Invoke(Character, "totalCapMagic"), Get(magic, "curMagic"), "TM predecessor has its actual post-acquisition Magic capacity filled");
        Equal(energyPool - 7, Get(Get(Character, "machine"), "speedEnergy"), "all non-basic Energy is actually in TM");
        Equal(magicPool, Get(Get(Character, "machine"), "goldMultiMagic"), "all Magic is actually in TM");
        Equal(0L, Get(Character, "idleEnergy"), "entry has no idle Energy escape hatch");
        Equal(0L, Get(magic, "idleMagic"), "entry has no idle Magic escape hatch");
        Equal(0, Property(Get(MainInstance, "_protectedScheduler"), "StageIndex"), "native funding precedes the real stage boundary");
        if (ordinaryRoute)
        {
            Set(Get(Character, "adventure"), "autoattacking", true);
            Equal(true, Invoke(Get(MainInstance, "_combManager"), "MoveToZone", 12), "native executor actuates the owned ordinary predecessor");
            Equal(12, Get(Get(Character, "adventure"), "zone"), "ordinary predecessor really entered zone 12");
            Equal(true, CallMain("InlineStageSetupVerified", plan), "native ordinary route retains verified TM setup");
        }
        return plan;
    }

    private static void CheckStageEntryFruitClaimWait(bool energy)
    {
        PrepareFruitCapacityWorld(energy);
        var before = CapacityNativeMapKey(Character);
        var claim = CallMain("BeginExecutionClaim", "blocking-preparation", "same-opportunity", Domain("FiniteAction"), (Func<bool>)(() => true));
        var renders = 0;
        Program.FiniteFruitRendered = () => renders++;
        try
        {
            for (var i = 0; i < 3; i++)
                Equal(false, CallMain("ActivateFruitAction", 0), "busy finite-action ownership defers fruit without throwing");
            Equal(0, renders, "deferred fruit never enters the native leaf");
            Equal(before, CapacityNativeMapKey(Character), "deferred fruit preserves all allocated and idle resource accounting");
            Equal(false, Get(claim, "Ended"), "deferred fruit cannot retire the current preparation owner");
            Equal(1, ((IList)Property(MainInstance, "_executionClaims")).Count, "deferral acquires no competing or leaked claim");
        }
        finally
        {
            Program.FiniteFruitRendered = null;
            CallMain("EndExecutionClaim", claim, false);
        }
        Equal(true, CallMain("ActivateFruitAction", 0), "the original fruit opportunity can activate once after preparation releases");
        Equal(0, ((IList)Property(MainInstance, "_executionClaims")).Count, "successful fruit activation leaves no claim behind");
    }

    private static void AdvanceStageEntryClock(double seconds)
    {
        foreach (var name in new[] { "rebirthTime", "totalPlaytime" })
            Set(Get(Character, name), "totalseconds", (double)Get(Get(Character, name), "totalseconds") + seconds);
        // Clock seam only: the production scheduler must accrue and select the row.
        Set(Get(MainInstance, "_protectedScheduler"), "_lastHeartbeat",
            (double)MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null) - seconds);
    }

    private static Action CaptureStageEntryPreservedState()
    {
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var map = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
        var preserved = map.Keys.Cast<object>().Where(key => key.ToString() == "BasicAttack" || key.ToString() == "BasicDefense"
            || (int)Invoke(writer, "Lane", key) == 2).ToDictionary(key => key, key => ((long[])map[key])?.ToArray());
        var w = Get(Character, "wandoos98");
        var history = new[] { "os", "energyLevel", "magicLevel", "energyProgress", "magicProgress" }.ToDictionary(name => name, name => Get(w, name));
        return () => {
            var current = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
            foreach (var pair in preserved)
                Check(pair.Value == null ? current[pair.Key] == null : pair.Value.SequenceEqual((long[])current[pair.Key]), "handoff preserves native " + pair.Key);
            Equal(731L, Get(Get(Character, "res3"), "curRes3"), "handoff preserves unmanaged R3 total");
            Equal(431L, Get(Get(Character, "res3"), "idleRes3"), "handoff preserves unmanaged R3 idle");
            foreach (var pair in history) Equal(pair.Value, Get(w, pair.Key), "handoff never resets native W history/" + pair.Key);
        };
    }

    private static void EnterStageEntryFight(object plan)
    {
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Convert.ToInt64(Property(scheduler, "Entry"));
        AdvanceStageEntryClock(2);
        CallMain("RunCustomPlanAutomation", plan);
        Equal(1, Property(scheduler, "StageIndex"), "earned protected time naturally selects automatic stage 1");
        Equal(entry + 1, Property(scheduler, "Entry"), "natural transition acquires exactly one next entry");
        Equal(2d, ((double[])Property(scheduler, "EarnedSeconds"))[0], "stage 0 earned real applied time, not an override");
        Check(((bool[])Property(scheduler, "Completed"))[0], "scheduler observed completion of funded TM work");
        Equal("", Property(scheduler, "LastOverride"), "fixture never invokes set, skip or restart");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "incoming fight retires the actual native predecessor");
        Equal(552, Get(Get(Get(Character, "inventory"), "head"), "id"), "incoming fight actually equips its selected combat head");
        Equal(true, Property(MainInstance, "BossPushRunning"), "stage boundary makes useful progress into the real boss process: " + CallMain("GetCustomAutomationStatus"));
        Equal(null, Get(MainInstance, "_combatPrepRun"), "automatic capture cannot overtake entry fight");
    }

    private static void RetireStageEntryNativeBoss(object plan)
    {
        var nukes = RouteNativeNukes; var fights = RouteNativeFights;
        CallMain("BossPushTick");
        Equal(nukes + 1, RouteNativeNukes, "production boss tick invokes actual native startNuke exactly once");
        Equal(fights, RouteNativeFights, "native startNuke sets combat flags without calling beginFight");
        var native = Get(Character, "bossController");
        Equal(true, Get(native, "nukeBoss"), "real native nuke is active, not a synthetic receipt");
        Equal(true, Get(native, "isFighting"), "native startNuke also establishes actual busy combat");
        var process = Get(MainInstance, "_bossPushProcess"); var claim = Get(process, "Claim");
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        var beardCadence = Get(MainInstance, "_customLastBeardRotation");
        InlineObservedError = null;
        CallMain("RunCustomBeardAutomation", plan);
        Equal(null, InlineObservedError, "background beard work defers native boss ownership without an injector error");
        Equal(beardCadence, Get(MainInstance, "_customLastBeardRotation"), "busy native combat cannot consume the beard cadence");
        CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff"); CallMain("BossPushTick");
        Equal(nukes + 1, RouteNativeNukes, "both dispatch paths do not duplicate the busy entry fight");
        Equal(fights, RouteNativeFights, "ordinary boss fight waits for the native nuke outcome");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "busy native nuke prevents automatic capture");
        Equal(false, Get(claim, "Ended"), "busy native nuke retains acquired prerequisites");
        Equal(swaps, Get(typeof(Program), "Swaps"), "busy native nuke installs no successor gear");
        Equal(writes, ResourceTransfers, "busy native nuke changes no resource accounting");
        // Native nuke resolution and the injector's next boss tick are separate.
        // Neither startNuke nor nukeBosses invokes beginFight in the actual game.
        Invoke(native, "nukeBosses");
        Equal(false, Get(native, "isFighting"), "actual failed native nuke retires its fight");
        Equal(false, Get(native, "nukeBoss"), "actual native nuke outcome retires its nuke");
        Equal(fights, RouteNativeFights, "native nuke resolution has not started the next ordinary fight");
        CallMain("BossPushTick");
        Equal(fights + 1, RouteNativeFights, "next production boss tick invokes actual native beginFight exactly once");
        Equal(true, Get(native, "isFighting"), "native beginFight establishes the ordinary fight before cleanup");
        Equal(false, Get(native, "nukeBoss"), "ordinary fight does not manufacture another nuke");
        CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff"); CallMain("BossPushTick");
        Equal(fights + 1, RouteNativeFights, "busy ordinary fight is not toggled or restarted by production ticks");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "busy ordinary fight also prevents automatic capture");
        CallMain("StopBossPush");
        Equal(true, Property(MainInstance, "BossPushRunning"), "stop request remains pending during actual native fight");
        Equal(false, Get(claim, "Ended"), "stop request retains prerequisites while native combat is busy");
        Equal(swaps, Get(typeof(Program), "Swaps"), "pending native completion installs no successor gear");
        Equal(writes, ResourceTransfers, "pending native completion changes no resource accounting");
        // The real game's FIGHT button uses beginFight to toggle a running fight
        // off. Exercise that native stop, not a flag assignment or a fake receipt.
        Invoke(native, "beginFight");
        Equal(fights + 2, RouteNativeFights, "explicit native FIGHT-button stop invokes the real toggle once");
        Equal(false, Get(native, "isFighting"), "native button stop retires its own ordinary fight");
        Equal(false, Get(native, "nukeBoss"), "native button stop leaves the resolved nuke retired");
        CallMain("BossPushTick");
        Equal(false, Property(MainInstance, "BossPushRunning"), "producer observes actual native retirement");
        Equal(true, Get(claim, "Ended"), "native completion releases the acquired entry claim");
        Equal(nukes + 1, RouteNativeNukes, "native retirement never repeats the admitted nuke");
        Equal(fights + 2, RouteNativeFights, "cleanup invokes no further native fight or stop");
    }

    private static void CheckStageEntryNativeTimeline(bool lowerMagicCapacity, bool ordinaryRoute)
    {
        var plan = PrepareStageEntryHandoff(lowerMagicCapacity, ordinaryRoute);
        var preserve = CaptureStageEntryPreservedState();
        var scheduler = Get(MainInstance, "_protectedScheduler"); var generation = Get(MainInstance, "_protectedGeneration");
        var originalTargets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
        var oldMagicPool = (long)Get(Get(Character, "magic"), "curMagic");
        EnterStageEntryFight(plan); preserve();
        Check(originalTargets.SequenceEqual((long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "entry fight preserves every original native AT target before automatic capture");
        var entry = Property(scheduler, "Entry"); var entered = Property(scheduler, "EnteredAt");
        var deadline = (double)Property(scheduler, "ExpectedEndSeconds");
        Equal((double)entered + 120, deadline, "entry fight inherits the original automatic deadline");
        if (lowerMagicCapacity)
        {
            var cap = (long)Invoke(Character, "totalCapMagic");
            Check(cap < oldMagicPool, "selected fight gear really lowers native Magic capacity");
            Check((long)Get(Get(Character, "machine"), "goldMultiMagic") <= cap, "fight cannot retain the over-cap native TM allocation even in Safe Zone");
            var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var map = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
            var allocated = map.Keys.Cast<object>().Where(key => (int)Invoke(writer, "Lane", key) == 1 && map[key] != null)
                .Sum(key => ((long[])map[key]).Sum());
            Check(allocated <= cap, "temporary fight funding fits the lower native capacity across all Magic destinations");
            Equal((long)Get(Get(Character, "magic"), "curMagic"), allocated + (long)Get(Get(Character, "magic"), "idleMagic"),
                "temporary policy conserves actual Magic accounting before native cap debit");
            Check((long)Get(Get(Character, "magic"), "idleMagic") >= 0, "temporary fight policy never overdraws Magic");
            NativeRouteCapacityClamp(2);
            Equal(cap, Get(Get(Character, "magic"), "curMagic"), "actual native generator consumes the pending cap debit");
        }
        RetireStageEntryNativeBoss(plan); preserve();
        AdvanceStageEntryClock(7);
        CallMain("RunCustomPlanAutomation", plan);
        var run = Get(MainInstance, "_combatPrepRun");
        Check(run != null, "retired fight admits an actual automatic executor: " + CallMain("GetCustomAutomationStatus"));
        Equal("running", Get(run, "Status"), "native automatic acquisition succeeds: " + Get(run, "Reason"));
        var snapshot = Get(Get(run, "Capture"), "Snapshot");
        var capturedBonus = (double)Property(snapshot, "CurrentWandoosBonus");
        Check(capturedBonus > 1d, "automatic capture retains the real bonus from nonzero W history");
        Equal(Invoke(Get(Character, "wandoos98Controller"), "wandoosBonus"), capturedBonus,
            "automatic snapshot captures the actual native W bonus without replacing OS factors");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "fight/setup time never renews the acquired deadline");
        Equal(entered, Property(scheduler, "EnteredAt"), "automatic acquisition retains original entry start");
        Equal(entry, Property(scheduler, "Entry"), "automatic capture retains the naturally acquired entry");
        Check((double)Get(run, "Duration") - ((double)Property(MainInstance, "CombatPrepClock") - (double)Get(run, "Started")) <= 113.1,
            "seven actual setup seconds consume the original automatic budget");
        var phase = ((Array)Property(Get(run, "Policy"), "Phases")).GetValue((int)Get(run, "PhaseIndex"));
        var kind = (string)Property(phase, "Kind");
        Check(kind == "Augments" || kind == "Wandoos", "fixture acquires real AUG/W work, not a safe-background-only workaround: " + kind);
        var w = Get(Character, "wandoos98");
        var augmentFunding = ((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>().Sum(x => (long)Get(x, "augEnergy") + (long)Get(x, "upgradeEnergy"));
        Check(kind == "Augments" ? augmentFunding > 0 : (long)Get(w, "wandoosEnergy") > 0 && (long)Get(w, "wandoosMagic") > 0,
            "real automatic phase actually funds its native training bars");
        preserve();
        var nukes = RouteNativeNukes; var fights = RouteNativeFights;
        CallMain("QuickStuff"); CallMain("RunCustomPlanAutomation", plan);
        Check(ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "replayed dispatch retains one acquired automatic run");
        Equal(nukes, RouteNativeNukes, "completed entry receipt cannot repeat native nuke");
        Equal(fights, RouteNativeFights, "completed entry receipt cannot repeat native fight");
        var nativeEnergyPool = Get(Character, "curEnergy"); var nativeMagicPool = Get(Get(Character, "magic"), "curMagic");
        var cleanupHeads = new List<int>();
        GearRenderObserver = () => cleanupHeads.Add((int)Get(Get(Get(Character, "inventory"), "head"), "id"));
        AdvanceStageEntryClock(deadline - (double)Get(Get(Character, "rebirthTime"), "totalseconds"));
        try
        {
            CallMain("QuickStuff"); CallMain("RunCustomPlanAutomation", plan);
            RunVerifiedInline(plan, "automatic retirement resolves CURRENT mandatory AT stage");
        }
        finally { GearRenderObserver = null; }
        Equal(2, Property(scheduler, "StageIndex"), "original deadline naturally enters current mandatory AT successor");
        Equal(Convert.ToInt64(entry) + 1, Property(scheduler, "Entry"), "cleanup selects one successor entry, never a historical row");
        Equal(generation, Get(MainInstance, "_protectedGeneration"), "handoff never resets protected generation");
        Equal(551, Get(Get(Get(Character, "inventory"), "head"), "id"), "cleanup installs current Magic-cap head, not historical TM or fight gear");
        Check(cleanupHeads.All(id => id == 551), "native cleanup render trace has no historical TM/fight-gear detour");
        Equal(nativeEnergyPool, Get(Character, "curEnergy"), "successor handoff never fabricates a filled Energy pool");
        Equal(nativeMagicPool, Get(Get(Character, "magic"), "curMagic"), "higher-cap successor gear never fabricates a filled Magic pool");
        var at = Get(Character, "advancedTraining"); var targets = (long[])Get(at, "levelTarget");
        var atStage = ((Array)Get(plan, "Stages")).GetValue(2);
        Equal(20L, Get(atStage, "TargetAdvancedTrainingToughness"), "current successor retains its mandatory toughness completion target");
        Equal(10L, Get(atStage, "TargetAdvancedTrainingPower"), "current successor retains its mandatory power completion target");
        // Inline completion targets govern funding without replacing independent native menu targets.
        Equal(originalTargets[0], targets[0], "inline successor preserves native toughness menu target");
        Equal(originalTargets[1], targets[1], "inline successor preserves native power menu target");
        Check(originalTargets.Skip(2).SequenceEqual(targets.Skip(2)), "automatic cleanup restores unrelated native AT targets");
        Check(((long[])Get(at, "energy")).Take(2).All(x => x > 0), "both unfinished mandatory AT bars actually receive Energy");
        Equal(0L, Get(w, "wandoosEnergy"), "mandatory successor retires automatic W Energy before AT work");
        Equal(0L, Get(w, "wandoosMagic"), "mandatory successor retires automatic W Magic before AT work");
        Equal(false, CallMain("HasCompletedProtectedStageWork", plan), "elapsed automatic deadline cannot manufacture mandatory AT completion");
        var levels = (long[])Get(at, "level"); var before = levels.Take(2).ToArray();
        for (var tick = 0; tick < 25; tick++)
            foreach (var name in new[] { "defense", "attack" }) Invoke(Get(Get(Character, "advancedTrainingController"), name), "updateAdvancedTraining");
        Check(levels[0] > before[0] && levels[1] > before[1], "funded current successor makes actual native AT progress");
        preserve();
        Equal(nukes, RouteNativeNukes, "successor callbacks do not resurrect historical entry fight");
    }

    private static void CheckStageEntryStalePreparation()
    {
        var plan = PrepareStageEntryHandoff(); var preserve = CaptureStageEntryPreservedState();
        var map = CallMain("GlobalPresetAllocationState"); var swaps = Get(typeof(Program), "Swaps");
        Program.FinitePreparationSelectionStarting = () => { throw new InvalidOperationException("stage-entry original selection unavailable"); };
        var result = (string)CallMain("RunFightPrep", plan, false, "previous failed preparation", null, "previous-entry-failed-selection");
        Program.FinitePreparationSelectionStarting = null;
        Check(result.Contains("stage-entry original selection unavailable"), "prior attempt actually fails at the native selection boundary");
        var stale = Get(MainInstance, "_fightPreparationAttempt");
        Equal(false, Property(MainInstance, "BossPushRunning"), "failed selection starts no boss process");
        Equal(map, CallMain("GlobalPresetAllocationState"), "failed attempt retains fully funded TM predecessor");
        Equal(swaps, Get(typeof(Program), "Swaps"), "failed selection performs no native gear write");
        EnterStageEntryFight(plan);
        Check(!ReferenceEquals(stale, Get(MainInstance, "_fightPreparationAttempt")), "next natural entry does not inherit stale failed preparation");
        var current = Get(MainInstance, "_fightPreparationAttempt"); var process = Get(MainInstance, "_bossPushProcess");
        var searches = InlineGearSelections;
        CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff");
        Check(ReferenceEquals(current, Get(MainInstance, "_fightPreparationAttempt")), "same live entry does not create duplicate preparation");
        Check(ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), "same live entry retains one boss process");
        Equal(searches, InlineGearSelections, "same live entry performs no replacement gear search");
        RetireStageEntryNativeBoss(plan); preserve();
    }

    private static void CheckStageEntryUnmanaged(string lane)
    {
        var plan = PrepareStageEntryHandoff(true); var preserve = CaptureStageEntryPreservedState();
        AdvanceStageEntryClock(2); CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(1, Property(scheduler, "StageIndex"), "unmanaged control first earns the real stage boundary");
        var deadline = Property(scheduler, "ExpectedEndSeconds"); var entry = Property(scheduler, "Entry");
        var targets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
        var settings = Get(MainType, "Settings");
        if (lane != "Magic") Set(settings, "_manageEnergy", false);
        if (lane != "Energy") Set(settings, "_manageMagic", false);
        var map = CallMain("GlobalPresetAllocationState"); var support = CallMain("GlobalPresetSupportState");
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var routes = AdventureZoneEntries;
        var nukes = RouteNativeNukes; var fights = RouteNativeFights;
        CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff"); CallMain("BossPushTick");
        Equal(map, CallMain("GlobalPresetAllocationState"), "unmanaged preflight preserves every native bar/" + lane);
        Equal(support, CallMain("GlobalPresetSupportState"), "unmanaged preflight preserves coupled support/" + lane);
        Equal(swaps, Get(typeof(Program), "Swaps"), "unmanaged fight cannot install capacity-lowering gear/" + lane);
        Equal(writes, ResourceTransfers, "unmanaged fight cannot clear or redistribute resources/" + lane);
        Equal(routes, AdventureZoneEntries, "unmanaged fight cannot retire native predecessor/" + lane);
        Equal(nukes, RouteNativeNukes, "unmanaged fight invokes no native nuke/" + lane);
        Equal(fights, RouteNativeFights, "unmanaged fight invokes no native fight/" + lane);
        Equal(false, Property(MainInstance, "BossPushRunning"), "unmanaged preflight cannot admit a latent boss process/" + lane);
        Check(targets.SequenceEqual((long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "unmanaged preflight preserves original native AT targets/" + lane);
        Equal(deadline, Property(scheduler, "ExpectedEndSeconds"), "unmanaged preflight never renews deadline/" + lane);
        Equal(entry, Property(scheduler, "Entry"), "unmanaged preflight never consumes entry/" + lane);
        preserve();
    }

    private static void CheckStageEntryPreAdmissionRoute(bool foreign)
    {
        var plan = PrepareStageEntryHandoff(); var preserve = CaptureStageEntryPreservedState();
        var manager = Get(MainInstance, "_combManager"); var adventure = Get(Character, "adventure");
        var routes = AdventureZoneEntries;
        Equal(true, Invoke(manager, "MoveToZone", -1), "review fixture actually returns the owned ordinary route to Safe Zone");
        Equal(routes + 1, AdventureZoneEntries, "safe return invokes the actual native zone setter");
        Equal(-1, Get(adventure, "zone"), "native Safe Zone return is observed before any foreign edit");
        RunVerifiedInline(plan, "verified inline TM setup after ordinary Safe Zone return");
        Equal(true, Get(MainInstance, "_ordinaryRouteActuated"), "verified safe return retains actual prior ordinary actuation evidence");
        Equal(-1, Get(MainInstance, "_ordinaryRouteZone"), "retained ordinary evidence describes Safe Zone, not the incoming foreign zone");
        var scheduler = Get(MainInstance, "_protectedScheduler"); var previous = Convert.ToInt64(Property(scheduler, "Entry"));
        AdvanceStageEntryClock(2); CallMain("GetProtectedStage", plan);
        Equal(1, Property(scheduler, "StageIndex"), "review admission belongs to a naturally selected new automatic stage");
        Equal(previous + 1, Property(scheduler, "Entry"), "review fixture earns one new stage entry without an override");
        Equal(2d, ((double[])Property(scheduler, "EarnedSeconds"))[0], "review fixture earns real protected predecessor work");
        Equal("", Property(scheduler, "LastOverride"), "review fixture never forces the new entry");
        if (foreign)
            Set(adventure, "zone", 13); // User/native observation changes before admission, not inside a callback.
        else
            Equal(true, Invoke(manager, "MoveToZone", 13), "positive control actually actuates the non-titan re-entry");
        Equal(13, Get(adventure, "zone"), "pre-admission case observes the same current non-titan zone");
        Equal(foreign ? -1 : 13, Get(MainInstance, "_ordinaryRouteZone"), "only actual native re-entry updates the ordinary route receipt");
        var map = CallMain("GlobalPresetAllocationState"); var support = CallMain("GlobalPresetSupportState");
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var pods = PodRefreshCalls;
        routes = AdventureZoneEntries; var nukes = RouteNativeNukes; var fights = RouteNativeFights;
        var claim = CallMain("BeginExecutionClaim", "stage-entry-retirement-review",
            Get(MainInstance, "_protectedGeneration") + ":" + Property(scheduler, "Entry"),
            Domain("Gear, Support, Route"), (Func<bool>)(() => true));
        try
        {
            var witness = (Func<bool>)CallMain("CaptureOrdinaryRouteRetirement", claim);
            Equal(!foreign, witness(), "fresh new-stage capture accepts only actuated re-entry, never a foreign zone after Safe Zone");
            Equal(!foreign, ((Func<bool>)CallMain("CaptureOrdinaryRouteRetirement", claim))(), "repeated fresh capture cannot launder prior safe-route actuation");
        }
        finally { Equal(true, CallMain("EndExecutionClaim", claim, false), "review releases only its own unmutated acquisition"); }
        if (foreign)
        {
            CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff"); CallMain("BossPushTick");
            Equal(false, Property(MainInstance, "BossPushRunning"), "actual next-stage fight admission rejects the pre-existing foreign route");
        }
        Equal(13, Get(adventure, "zone"), "admission preserves the current native route without adopting or retiring it");
        Equal(routes, AdventureZoneEntries, "fresh capture and rejected admission invoke no native route setter");
        Equal(map, CallMain("GlobalPresetAllocationState"), "pre-admission route review preserves actual native funding");
        Equal(support, CallMain("GlobalPresetSupportState"), "pre-admission route review preserves exact native support");
        Equal(swaps, Get(typeof(Program), "Swaps"), "pre-admission route review performs no dependent gear writes");
        Equal(writes, ResourceTransfers, "pre-admission route review performs no resource accounting");
        Equal(pods, PodRefreshCalls, "pre-admission route review performs no support submissions");
        Equal(nukes, RouteNativeNukes, "pre-admission route review reaches no native nuke");
        Equal(fights, RouteNativeFights, "pre-admission route review reaches no native fight");
        preserve();
    }

    private static void CheckStageEntryForeignCallback(string change)
    {
        var plan = PrepareStageEntryHandoff(); var preserve = CaptureStageEntryPreservedState();
        var fired = false; object head = null; string map = null, support = null;
        var writes = 0; object swaps = null; var pods = 0;
        AdventureZoneEntryCompletedObserver = zone => {
            if (zone != -1 || fired) return;
            fired = true;
            var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
            if (change == "gear")
            {
                var old = Get(inventory, "head"); head = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
                Set(inventory, "head", head); items[items.IndexOf(head)] = old; Invoke(typeof(Program), "RefreshBonuses");
            }
            if (change == "support") SetActiveBeards(0);
            if (change == "resource")
            {
                var machine = Get(Character, "machine"); Set(machine, "speedEnergy", (long)Get(machine, "speedEnergy") - 1);
                Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
            }
            if (change == "route") Set(Get(Character, "adventure"), "zone", 13);
            if (change == "permission") Set(Get(MainType, "Settings"), "_manageMagic", false);
            map = (string)CallMain("GlobalPresetAllocationState"); support = (string)CallMain("GlobalPresetSupportState");
            writes = ResourceTransfers; swaps = Get(typeof(Program), "Swaps"); pods = PodRefreshCalls;
        };
        var nukes = RouteNativeNukes; var fights = RouteNativeFights;
        AdvanceStageEntryClock(2); CallMain("RunCustomPlanAutomation", plan);
        Equal(true, fired, "foreign control reaches actual native predecessor-retirement callback/" + change);
        CallMain("RunCustomPlanAutomation", plan); CallMain("QuickStuff"); CallMain("BossPushTick");
        Equal(map, CallMain("GlobalPresetAllocationState"), "callback conflict cannot authorize dependent native resource writes/" + change);
        Equal(support, CallMain("GlobalPresetSupportState"), "callback conflict cannot adopt and replace native support/" + change);
        Equal(swaps, Get(typeof(Program), "Swaps"), "callback conflict blocks subsequent gear writes/" + change);
        Equal(writes, ResourceTransfers, "callback conflict blocks subsequent resource accounting/" + change);
        Equal(pods, PodRefreshCalls, "callback conflict blocks subsequent support submissions/" + change);
        Equal(nukes, RouteNativeNukes, "foreign callback reaches no native boss nuke/" + change);
        Equal(fights, RouteNativeFights, "foreign callback reaches no native boss fight/" + change);
        Equal(false, Property(MainInstance, "BossPushRunning"), "foreign callback cannot admit a latent boss process/" + change);
        if (head != null) Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), "foreign head remains installed");
        if (change == "route") Equal(13, Get(Get(Character, "adventure"), "zone"), "foreign callback route is not forcibly retired or adopted");
        preserve();
    }
}
