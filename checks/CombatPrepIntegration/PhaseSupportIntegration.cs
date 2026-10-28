using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    // Opt-in only for this runner; other suites source-link the common gear/resource fixture.
    private static void ConfigureSupportFixture(AssemblyDefinition game)
    {
        foreach (var type in game.MainModule.Types)
        foreach (var method in type.Methods.Where(x => x.HasBody))
        {
            if (type.Name == "Character" && method.Name == "machineBossMulti") Constant(method, 1);
            if ((type.Name == "AllBloodMagicController" && method.Name == "goldBonus")
                || (type.Name == "TimeMachineChallengeController" && method.Name == "totalGPSbonus")
                || (type.Name == "AllNGUController" && method.Name == "timeMachineBonus")) Constant(method, 1);
            if (type.Name == "AllGoldDiggerController" && method.Name == "maxDiggerSlots") Boundary(game.MainModule, method, "FixtureDiggerSlots", false);
            if (type.Name == "AllBeardsController" && method.Name == "capBeards") Boundary(game.MainModule, method, "FixtureBeardSlots", false);
            if (type.Name == "AllBeardsController" && method.Name == "beardSize") Constant(method, 7);
            if ((type.Name == "AllGoldDiggerController" || type.Name == "AllBeardsController") && method.Name == "refreshMenu") Constant(method, 0);
            if (type.Name == "GoldDiggerUIController" && method.Name == "updateUI")
            {
                method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod("PodRefresh", All))); il.Emit(OpCodes.Ret);
            }
            if (type.Name == "HoverTooltip" && method.Name == "showOverrideTooltip") Constant(method, 0);
        }
    }

    private static Assembly LoadSupportUi(string path) { return (Assembly)Invoke(typeof(PresetIntegrationChecks), "LoadUi", path); }

    private static void PrepareSupportFixture()
    {
        foreach (var pair in new[] { new KeyValuePair<string, object>("Game", Game), new KeyValuePair<string, object>("Plugin", Plugin),
            new KeyValuePair<string, object>("MainType", MainType), new KeyValuePair<string, object>("MainInstance", MainInstance),
            new KeyValuePair<string, object>("Character", Character), new KeyValuePair<string, object>("Controller", Controller) })
            Set(typeof(PresetIntegrationChecks), pair.Key, pair.Value);
        Invoke(typeof(PresetIntegrationChecks), "PrepareSupport");
        foreach (var flag in new[] { "FailPodRefresh", "ForeignPodRefresh", "DropGoldPodRefresh", "FailBeardRefresh" })
            Set(typeof(PresetIntegrationChecks), flag, false);
    }

    private static void CheckPhaseSupport()
    {
        Setup();
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        var beardRows = (IList)Get(Get(Character, "beards"), "beards");
        Set(beardRows[4], "beardLevel", 10000L); Set(beardRows[0], "beardLevel", 10000L);
        foreach (var row in rows) Set(row, "maxLevel", 20L);
        var diggers = Get(Character, "allDiggers");
        ((IList)Get(diggers, "boostPerLevel"))[1] = 2d;
        var before = CallMain("CombatPrepSupportKey"); var calls = Get(typeof(PresetIntegrationChecks), "PodRefreshCalls");
        var capture = CallMain("CaptureCombatPrep", .03, .07, null);
        Equal(before, CallMain("CombatPrepSupportKey"), "phase capture never toggles support or edits inactive levels");
        Equal(calls, Get(typeof(PresetIntegrationChecks), "PodRefreshCalls"), "capture never touches native input pods");
        var support = (IDictionary)Get(capture, "Support"); var gear = (IDictionary)Get(capture, "Gear");
        foreach (var key in new[] { "augments", "at", "wandoos", "ngu" })
        {
            Equal(true, ReferenceEquals(gear[key + "-current"], gear[key + "-planned"]), "support variants compare identical gear: " + key);
            Equal(.75, Get(support[key + "-planned"], "BudgetFraction"), "ordinary budget fraction: " + key);
            Equal(true, Get(support[key + "-planned"], "EnforceBudget"), "planned budget enforced: " + key);
        }
        var wSupport = Get(support["wandoos-planned"], "Evaluation");
        var selected = (Array)Get(Get(wSupport, "Selection"), "Diggers");
        Equal(true, selected.Cast<object>().Any(x => (int)Get(x, "Id") == 1 && (long)Get(x, "Level") == 20), "planned W uses purchased-bound native levels, not level one");
        Equal(false, ((int[])Get(Get(wSupport, "Selection"), "Beards")).Contains(4), "four core slots do not invent a fifth Lady slot");
        var snapshot = Get(capture, "Snapshot");
        var profiles = ((Array)Get(snapshot, "Profiles")).Cast<object>().ToArray();
        var w = profiles.Single(x => (string)Get(x, "Key") == "wandoos-planned");
        var currentW = profiles.Single(x => (string)Get(x, "Key") == "wandoos-current");
        Near((double)Get(wSupport, "DiggerW"), (double)Get(w, "WandoosEnergySpeedWithoutAt") / (double)Get(currentW, "WandoosEnergySpeedWithoutAt"), "support projected before search changes W rate only once");
        Equal(Get(wSupport, "NetGold"), Get(w, "NetGoldPerSecond"), "profile gets shared support income");
        var run = StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60));
        Equal("verified", Get(run, "SupportStatus"), "phase support verified before funding");
        Equal(20L, Get(rows[1], "curLevel"), "execution uses exact native selected level");
        Equal(Get(wSupport, "DiggerW"), Convert.ToDouble(Invoke(diggers, "totalWandoosSpeedBonus")), "projected W equals untouched native effective getter");
        Equal(Get(wSupport, "GrossGold"), Invoke(Character, "grossGoldPerSecond"), "projected GPS equals untouched native getter");
        var retained = CallMain("CombatPrepSupportKey"); calls = Get(typeof(PresetIntegrationChecks), "PodRefreshCalls");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        Equal(retained, CallMain("CombatPrepSupportKey"), "no successor preserves actual phase support and inactive levels");
        Equal(calls, Get(typeof(PresetIntegrationChecks), "PodRefreshCalls"), "claim release performs no historical support writes");
        CheckSupportReplay();
        CheckSupportPreflight();
        CheckSupportFailures();
        CheckSupportForeignEdits();
        CheckSupportReporting();
        CheckSupportTransitionsAndCleanup();
        CheckSupportSerialization();
        Setup();
    }

    private static void PreparePaidW()
    {
        Setup(); BeardSlots = 5;
        var rows = (IList)Get(Get(Character, "diggers"), "diggers"); Set(rows[1], "maxLevel", 100L);
        var controller = Get(Character, "allDiggers");
        ((IList)Get(controller, "consumesGPS"))[1] = true;
        ((IList)Get(controller, "baseGPSDrain"))[1] = 1d;
        ((IList)Get(controller, "boostPerLevel"))[1] = 2d;
        var beardRows = (IList)Get(Get(Character, "beards"), "beards");
        Set(beardRows[4], "beardLevel", 10000L); Set(beardRows[0], "beardLevel", 10000L);
    }

    private static object SupportEvaluation(object capture, string key)
    { return Get(((IDictionary)Get(capture, "Support"))[key], "Evaluation"); }

    private static void CheckSupportReplay()
    {
        PreparePaidW();
        var capture = CallMain("CaptureCombatPrep", .03, .07, null);
        var evaluation = SupportEvaluation(capture, "wandoos-planned");
        var selection = Get(evaluation, "Selection");
        Equal(true, ((int[])Get(selection, "Beards")).Contains(4), "Lady fits only after ordinary core with fifth slot");
        foreach (var key in new[] { "augments-planned", "ngu-planned" })
        {
            var background = Get(SupportEvaluation(capture, key), "Selection");
            Equal(true, ((int[])Get(background, "Beards")).Contains(4), "positive background magic protects Lady: " + key);
            Equal(false, ((Array)Get(background, "Diggers")).Cast<object>().Any(x => (int)Get(x, "Id") == 1), "background does not add a W digger to NGU intent: " + key);
        }
        var at = Get(SupportEvaluation(capture, "at-planned"), "Selection");
        Equal(false, ((int[])Get(at, "Beards")).Contains(4), "AT retains original Fu/other optional before unused Lady");
        var oldLevel = (long)Get(((Array)Get(selection, "Diggers")).GetValue(0), "Level");
        Set(Get(Character, "machine"), "realBaseGold", 1e9d);
        var replay = CallMain("CaptureCombatPrep", .03, .07, capture);
        Equal(Get(selection, "StableKey"), Get(Get(SupportEvaluation(replay, "wandoos-planned"), "Selection"), "StableKey"), "fresh GPS never resizes pinned support");
        Equal(true, (double)Get(SupportEvaluation(replay, "wandoos-planned"), "NetGold") > (double)Get(evaluation, "NetGold"), "fresh replay recomputes income");
        var run = StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60));
        Equal(oldLevel, Get(((IList)Get(Get(Character, "diggers"), "diggers"))[1], "curLevel"), "start replay executes preview-pinned level despite income growth");
        var live = Convert.ToDouble(Invoke(Character, "totalWandoosEnergySpeed"));
        var snapshot = Get(Get(run, "Capture"), "Snapshot");
        var profile = ((Array)Get(snapshot, "Profiles")).Cast<object>()
            .Single(x => (string)Get(x, "Key") == "wandoos-planned");
        Near((double)Get(profile, "WandoosEnergySpeedWithoutAt"), live, "native Lady AND W digger speed matches projected combined effects");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));

        PreparePaidW(); capture = CallMain("CaptureCombatPrep", .03, .07, null);
        Set(Get(Character, "machine"), "realBaseGold", 0d);
        replay = CallMain("CaptureCombatPrep", .03, .07, capture);
        Equal(false, ((IDictionary)Get(replay, "Support")).Contains("wandoos-planned"), "newly infeasible pinned profile is removed, never displaced under its key");
        var recalls = Recalls;
        Reject(() => StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60)), "start cannot substitute another support profile after GPS loss");
        Equal(recalls, Recalls, "invalid pinned profile rejects before recall");
        Equal(true, Get(MainInstance, "_combatPrepPreview") != null, "invalid replay retains unconsumed token");
    }

    private static void CheckSupportPreflight()
    {
        PreparePaidW();
        var pod = ((IList)Get(Get(Character, "allDiggers"), "pods"))[0];
        Set(pod, "diggerLevelInput", null);
        var capture = CallMain("CaptureCombatPrep", .03, .07, null); var recalls = Recalls; var swaps = Swaps;
        var os = Get(Get(Character, "wandoos98"), "os");
        Exception nativePreflight = null;
        try { StartServerPolicy(capture, Phase("AT", "current", 10), Phase("Wandoos", "wandoos-planned", 50)); }
        catch (Exception error) { nativePreflight = error; }
        Equal(true, nativePreflight != null && nativePreflight.ToString().Contains("Exact digger input controller is unavailable"), "future phase requires native exact pod before ANY phase starts");
        Equal(recalls, Recalls, "future native preflight failure makes zero recalls"); Equal(swaps, Swaps, "future native preflight failure makes zero swaps");
        Equal(os, Get(Get(Character, "wandoos98"), "os"), "future native preflight failure never changes OS");
        Equal(null, Get(MainInstance, "_combatPrepRun"), "future native preflight does not acquire run");
        Equal(true, Get(MainInstance, "_combatPrepPreview") != null, "future native preflight leaves preview token intact");
        foreach (var material in new[] { "purchased", "slots", "inactive", "objects" })
        {
            PreparePaidW(); capture = CallMain("CaptureCombatPrep", .03, .07, null); recalls = Recalls;
            if (material == "purchased") Set(((IList)Get(Get(Character, "diggers"), "diggers"))[1], "maxLevel", 101L);
            if (material == "slots") BeardSlots = 6;
            if (material == "inactive") Set(((IList)Get(Get(Character, "diggers"), "diggers"))[1], "curLevel", 1L);
            if (material == "objects") Set(Get(Character, "diggers"), "activeDiggers", Activator.CreateInstance(Get(Get(Character, "diggers"), "activeDiggers").GetType()));
            Reject(() => StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60)), "material support invalidates preview: " + material);
            Equal(recalls, Recalls, "material support rejection is read-only: " + material);
        }
    }

    private static void CheckSupportFailures()
    {
        foreach (var foreign in new[] { false, true })
        {
            Setup(); var rows = (IList)Get(Get(Character, "diggers"), "diggers");
            Set(rows[0], "maxLevel", 4L); Set(rows[11], "maxLevel", 2L);
            var capture = CallMain("CaptureCombatPrep", .03, .07, null); var before = CallMain("CombatPrepSupportKey");
            Set(typeof(PresetIntegrationChecks), foreign ? "ForeignPodRefresh" : "FailPodRefresh", true);
            Exception rejected = null;
            try { StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60)); }
            catch (Exception error) { rejected = error; }
            Equal(foreign, rejected != null, "only foreign support interference rejects native run admission: " + rejected);
            var run = Get(MainInstance, "_combatPrepRun");
            Equal(false, Get(run, "SetupVerified"), "support failure never verifies the phase prerequisites");
            Equal(-1, Get(run, "AppliedResourcePhase"), "unverified support cannot credit resource application");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "unverified support never funds W");
            if (foreign)
            {
                Equal(true, rejected.ToString().Contains("Unexpected foreign support mutation"), "foreign native callback is rejected by the real support guard");
                Equal("ForeignConflict", Get(Get(MainInstance, "_presetSupportOutcome"), "Disposition").ToString(), "shared actuator rejects foreign partial evidence");
                Equal(2L, Get(rows[11], "curLevel"), "foreign inactive level survives failed acquisition cleanup");
            }
            else
            {
                var entry = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
                ScheduledRefresh(run);
                Equal(true, Get(run, "SetupVerified"), "shared bounded continuation completes the same selected support");
                Equal(entry, Get(run, "Started"), "support retry preserves entry time");
                Equal(deadline, Get(run, "PhaseDeadline"), "support retry cannot renew phase deadline");
            }
            var retained = CallMain("ObserveSupportState"); var calls = Get(typeof(PresetIntegrationChecks), "PodRefreshCalls");
            CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            Equal(Get(retained, "StableKey"), Get(CallMain("ObserveSupportState"), "StableKey"), "cancel preserves committed or foreign partial support, never historical rollback");
            Equal(calls, Get(typeof(PresetIntegrationChecks), "PodRefreshCalls"), "partial cancel has no support detour");
        }
    }

    private static void CheckSupportForeignEdits()
    {
        foreach (var foreign in new[] { "inactive", "flags", "selection", "receipt", "model", "epoch", "budget", "purchased", "drag", "claim" })
        {
            PreparePaidW(); var rows = (IList)Get(Get(Character, "diggers"), "diggers"); Set(rows[11], "maxLevel", 2L);
            var capture = CallMain("CaptureCombatPrep", .03, .07, null);
            var run = StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60));
            if (foreign == "inactive") Set(rows[11], "curLevel", 2L);
            if (foreign == "flags") Set(rows[11], "active", true);
            if (foreign == "selection") Invoke(Get(Character, "allDiggers"), "clearAllActiveDiggers");
            if (foreign == "receipt")
            {
                var selection = Get(Get(run, "ExpectedSupport"), "Selection");
                var before = CallMain("CombatPrepSupportKey");
                var writes = Get(typeof(PresetIntegrationChecks), "PodRefreshCalls");
                Exception rejected = null;
                try { Invoke(MainInstance, "ApplyExactSupport", selection, .75d, true); }
                catch (Exception error) { rejected = error; }
                Equal(true, rejected != null && rejected.ToString().Contains("no longer authorized"), "raw exact support rejects missing authority even for matching values");
                Equal(before, CallMain("CombatPrepSupportKey"), "raw rejection preserves all native support values");
                Equal(writes, Get(typeof(PresetIntegrationChecks), "PodRefreshCalls"), "raw rejection never reaches a native support callback");
                Invoke(typeof(PresetIntegrationChecks), "InvokeGrantedSupportActuator", "ApplyExactSupport", new object[] { selection, .75d, true });
            }
            if (foreign == "model") Set(Get(Character, "diggers"), "activeDiggers", Activator.CreateInstance(Get(Get(Character, "diggers"), "activeDiggers").GetType()));
            if (foreign == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            if (foreign == "budget") Set(Get(Character, "machine"), "realBaseGold", 0d);
            if (foreign == "purchased") Set(rows[1], "maxLevel", 101L);
            if (foreign == "drag") Set(Controller, "midDrag", true);
            var claim = foreign == "claim" ? BeginFixtureClaim("Support", "phase-support-interference") : null;
            var retained = CallMain("ObserveSupportState");
            var committedWrites = ResourceCommittedWrites; var started = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
            Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
            if (foreign == "receipt")
            {
                Equal(true, Get(run, "SetupVerified"), "a semantically identical support write grants no new selection or rollback");
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            }
            else if (foreign == "claim")
            {
                Equal(false, CallMain("CombatPrepRequirementsVerified", run, ScheduledPhase(run)), "foreign support claim denies current phase prerequisites");
                Equal(false, Get(run, "SetupVerified"), "foreign support claim withholds phase setup proof");
                Equal(-1, Get(run, "AppliedResourcePhase"), "foreign support claim withholds dependent resource credit");
                Equal(committedWrites, ResourceCommittedWrites, "foreign support claim performs no dependent resource writes");
                Equal(started, Get(run, "Started"), "claim wait retains the original run start");
                Equal(deadline, Get(run, "PhaseDeadline"), "claim wait cannot renew the phase deadline");
                Equal(true, CallMain("EndExecutionClaim", claim, false), "foreign support claim releases without restoration");
                ScheduledRefresh(run);
                Equal(true, Get(run, "SetupVerified"), "released support claim permits actual setup verification");
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            }
            else if (foreign == "budget")
            {
                Equal(-1, Get(run, "AppliedResourcePhase"), "income loss withholds dependent funding without silently resizing fixed support");
                CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
            }
            else Equal(false, Get(MainInstance, "CombatPrepActive"), "support interference cancels: " + foreign);
            Equal(Get(retained, "StableKey"), Get(CallMain("ObserveSupportState"), "StableKey"), "current support survives release without historical restoration: " + foreign);
        }
        Setup(); var original = CallMain("CombatPrepSupportKey");
        var unchanged = StartServerPolicy(CallMain("CaptureCombatPrep", .03, .03, null), Phase("AT", "current", 60));
        Equal(null, Get(Get(MainInstance, "_presetSupportOutcome"), "Mutation"), "unchanged support verifies without unnecessary clear/apply");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(unchanged, "Id") + "\"}"));
        Equal(original, CallMain("CombatPrepSupportKey"), "no-mutation Current preserves all inactive levels");

        PreparePaidW(); DiggerSlots = 2;
        var foreignRows = (IList)Get(Get(Character, "diggers"), "diggers"); Set(foreignRows[11], "maxLevel", 2L);
        Invoke(Get(Character, "allBeards"), "deactivateBeard", 6);
        var goldCapture = CallMain("CaptureCombatPrep", .03, .07, null);
        var goldRun = StartServerPolicy(goldCapture, Phase("Wandoos", "wandoos-planned", 60));
        Set(foreignRows[11], "curLevel", 1L); Invoke(Get(Character, "allDiggers"), "activateDigger", 11);
        CallMain("TickCombatPrep");
        Equal(false, Get(MainInstance, "CombatPrepActive"), "foreign digger ends the claim without applying original Gold-off support");
        Equal(true, Get(foreignRows[11], "active"), "Gold deactivation cannot clear a foreign active digger during cleanup");
        Equal(true, ((IList)Get(Get(Character, "beards"), "activeBeards")).Contains(6), "dependent Gold beard is retained after support ownership loss");
    }

    private static void CheckSupportReporting()
    {
        PreparePaidW();
        var rows = (IList)Get(Get(Character, "diggers"), "diggers"); Set(rows[2], "maxLevel", 5L);
        ((IList)Get(Get(Character, "allDiggers"), "boostPerLevel"))[2] = .2d;
        var capture = CallMain("CaptureCombatPrep", .03, .07, null);
        var original = Get(Get(capture, "OriginalSupportState"), "Selection");
        var expected = CallMain("CombatPrepBossStats", Get(capture, "OriginalGear"), false, original);
        var selectionType = Plugin.GetType("NGUInjector.Managers.SupportSelection", true);
        var levelType = Plugin.GetType("NGUInjector.Managers.SupportDiggerLevel", true);
        var levels = Array.CreateInstance(levelType, 1);
        levels.SetValue(Activator.CreateInstance(levelType, All, null, new object[] { 2, 5L }, null), 0);
        var selection = Activator.CreateInstance(selectionType, All, null, new object[] { new[] { 0, 1, 3, 5, 6 }, levels }, null);
        Invoke(typeof(PresetIntegrationChecks), "InvokeGrantedSupportActuator", "ApplyExactSupport", new object[] { selection, 1d, false });
        var actual = CallMain("CombatPrepBossStats", Get(capture, "OriginalGear"), false, original);
        Equal(Get(expected, "Item1"), Get(actual, "Item1"), "training Stat beard/digger cannot inflate original-support reported attack");
        Equal(Get(expected, "Item2"), Get(actual, "Item2"), "training Stat beard/digger cannot inflate original-support reported defense");
        Equal(true, (double)Invoke(Character, "totalAttack") > (double)Get(actual, "Item1"), "positive control: native temporary combat support really changes native attack");
    }

    private static void CheckSupportTransitionsAndCleanup()
    {
        Setup();
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(rows[1], "maxLevel", 10L); Set(rows[5], "maxLevel", 10L); Set(rows[5], "curLevel", 3L);
        var capture = CallMain("CaptureCombatPrep", .03, .07, null); var original = CallMain("CombatPrepSupportKey");
        var run = StartServerPolicy(capture, Phase("AT", "at-planned", 10), Phase("Wandoos", "wandoos-planned", 10));
        Equal(10L, Get(rows[5], "curLevel"), "AT actual intent selects magic NGU digger");
        var phase = ((Array)Get(Get(run, "Policy"), "Phases")).GetValue(0);
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level");
        levels[3] = (long)Get(phase, "AtEnergyTarget"); levels[4] = (long)Get(phase, "AtMagicTarget");
        Set(run, "PhaseDeadline", (double)Get(run, "Started") - 1); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal(1, Get(run, "PhaseIndex"), "W phase acquires after AT target gate");
        Equal(10L, Get(rows[1], "curLevel"), "later W phase applies its own pinned digger");
        var finalSupport = CallMain("CombatPrepSupportKey");
        var finalWrites = Get(typeof(PresetIntegrationChecks), "PodRefreshCalls");
        CallMain("EndCombatPrep", "completed", "fixture completed", true);
        Equal(finalSupport, CallMain("CombatPrepSupportKey"), "multi-phase release preserves last desired selection and actual inactive levels");
        Equal(finalWrites, Get(typeof(PresetIntegrationChecks), "PodRefreshCalls"), "multi-phase release has no history-union rollback writes");

        capture = CallMain("CaptureCombatPrep", .03, .07, null);
        run = StartServerPolicy(capture, Phase("AT", "at-planned", 10), Phase("Wandoos", "current", 10));
        phase = ((Array)Get(Get(run, "Policy"), "Phases")).GetValue(0);
        levels[3] = (long)Get(phase, "AtEnergyTarget"); levels[4] = (long)Get(phase, "AtMagicTarget");
        Set(run, "PhaseDeadline", (double)Get(run, "Started") - 1); Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal(1, Get(run, "PhaseIndex"), "Current physical support may follow dedicated support");
        Equal(Get(Get(Get(capture, "OriginalSupportState"), "Selection"), "StableKey"),
            Get(Get(CallMain("ObserveSupportState"), "Selection"), "StableKey"), "Current means this run's acquired physical selection, not the latest temporary selection");
        CallMain("EndCombatPrep", "completed", "fixture completed", true);

        foreach (var failure in new[] { "resources", "targets", "gear" })
        {
            PreparePaidW(); var inventory = Get(Character, "inventory");
            if (failure == "gear")
            {
                var head = Blank(Get(inventory, "head").GetType()); Set(head, "id", 60); SetEnum(head, "type", "Head");
                Set(head, "bossRequired", 1); Set(head, "removable", true); SetEnum(head, "spec1Type", "Wandoos98"); Set(head, "spec1Cur", 1e30f);
                ((IList)Get(inventory, "inventory"))[0] = head;
            }
            capture = CallMain("CaptureCombatPrep", .03, .07, null); original = CallMain("CombatPrepSupportKey");
            run = StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60));
            if (failure == "resources") FailDesiredEnergyPreflight = true;
            if (failure == "targets") ((System.Collections.Generic.List<Action>)Get(run, "RestoreTargets")).Add(() => { throw new InvalidOperationException("fixture target cleanup failure"); });
            if (failure == "gear") FailRefreshAtSwap = Swaps + 1;
            var retained = CallMain("CombatPrepSupportKey"); var swaps = Swaps;
            CallMain("EndCombatPrep", "cancelled", "fixture cleanup failure", true);
            Equal(failure != "gear", Get(run, "CleanupPending"), "only actual borrowed-field/resource release failures defer handoff: " + failure);
            if (failure == "resources")
            {
                Equal(1, DesiredEnergyPreflightFailures, "positive control: resource cleanup reached an actual unchanged preflight failure");
                var release = Get(run, "ResourceOutcome");
                Equal("RejectedUnchanged", Get(release, "Disposition").ToString(), "manual resource cleanup failure remains correctly typed");
                Equal(0, Get(release, "ModelWrites"), "rejected cleanup observation performs zero resource accounting writes");
            }
            if (failure == "gear") Equal(swaps + 1, FailRefreshAtSwap, "no historical gear return exists to trigger its fault");
            Equal(swaps, Swaps, "release performs zero historical gear swaps: " + failure);
            Equal(retained, CallMain("CombatPrepSupportKey"), "release preserves all-level current support even after outer failure: " + failure);
        }
        PreparePaidW(); capture = CallMain("CaptureCombatPrep", .03, .07, null);
        run = StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60));
        var beardRows = (IList)Get(Get(Character, "beards"), "beards"); Set(beardRows[4], "beardLevel", 20000L);
        Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal(true, Get(MainInstance, "CombatPrepActive"), "passive beard growth does not break all-level selection ownership");
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
    }

    private static void CheckSupportSerialization()
    {
        PreparePaidW();
        var preview = Preview(10); var before = CallMain("CombatPrepSupportKey");
        Equal("current", JsonValue(Json(Json(preview, "baseline"), "support"), "policy"), "baseline exposes original support under additive schema one");
        foreach (var candidate in ((IEnumerable)Get(Json(preview, "candidates"), "Children")).Cast<object>())
        foreach (var phase in ((IEnumerable)Get(Json(candidate, "phases"), "Children")).Cast<object>())
        {
            var support = Json(phase, "support");
            Equal(true, new[] { "current", "phase" }.Contains(JsonValue(support, "policy")), "phase support policy has stable enum");
            Equal(true, !string.IsNullOrEmpty(JsonValue(phase, "supportVariantKey")), "phase exposes optional support variant key");
            foreach (var digger in ((IEnumerable)Get(Json(support, "diggers"), "Children")).Cast<object>())
                Equal(true, (bool)Get(Json(digger, "level"), "IsString"), "support digger levels are exact decimal strings");
            foreach (var key in new[] { "grossGoldPerSecond", "drainPerSecond", "netGoldPerSecond", "budgetFraction", "wandoosBeardMultiplier", "wandoosDiggerMultiplier" })
                Equal(true, (bool)Get(Json(support, key), "IsNumber"), "support financial/effect field is numeric: " + key);
        }
        Equal(before, CallMain("CombatPrepSupportKey"), "serializing preview does not mutate support");
        var capture = Get(Get(MainInstance, "_combatPrepPreview"), "Capture");
        var run = StartServerPolicy(capture, Phase("Wandoos", "wandoos-planned", 60));
        var runtime = CallMain("GetCombatPrepRuntime");
        Equal("phase", JsonValue(Json(runtime, "plannedSupport"), "policy"), "runtime planned support is the current pinned phase");
        Equal("verified", JsonValue(runtime, "supportStatus"), "runtime reports verified support");
        Equal(Get(SupportEvaluation(Get(run, "Capture"), "wandoos-planned"), "NetGold"), Get(Json(Json(runtime, "actualSupport"), "netGoldPerSecond"), "AsDouble"), "runtime actual support comes from native observed selection");
        Equal(true, System.Text.Encoding.UTF8.GetByteCount(runtime.ToString()) < (int)Get(MainType, "ExternalUiActionResultMaxBytes") - 4 * 1024, "nested runtime support remains below action-result headroom bound");
        var retainedSupport = Json(runtime, "actualSupport").ToString();
        CallMain("CancelCombatPrep", Parse("{\"runId\":\"" + Get(run, "Id") + "\"}"));
        runtime = CallMain("GetCombatPrepRuntime");
        Equal(retainedSupport, Json(runtime, "actualSupport").ToString(), "terminal telemetry reports actual retained support, not historical restoration");
    }
}
