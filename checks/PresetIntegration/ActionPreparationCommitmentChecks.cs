using System;
using System.Collections;
using System.IO;
using System.Linq;

// Separate C-owned partial. Shared fixture owner adds this Compile entry and
// calls CheckActionPreparationIdentityRegressions() from its native action group.
public static partial class PresetIntegrationChecks
{
    private static void CheckActionPreparationIdentityRegressions()
    {
        var before = Assertions;
        foreach (var manualRebirth in new[] { false, true })
        {
            SetupAdventureDynamic("native-manual-prep-import-" + manualRebirth, "[0,0,0,0,0,0,0,0,0,0,0,0,1,0]");
            Equal(null, Get(MainInstance, "_protectedScheduler"), "manual preparation import fixture has no scheduler");
            Set(MainInstance, "_manualRebirthPending", manualRebirth);
            var opportunity = "native-explicit-preparation-" + manualRebirth;
            var summary = (string)CallMain("RunFightPrep", null, false, false, "native manual preparation import", null, opportunity);
            Check(summary.Contains("boss push running"), "actual manual preparation acquires exact native setup without a scheduler: " + summary);
            Equal(true, Property(MainInstance, "FightPreparationVerified"), "manual process has real native selected-setup proof before retirement");
            CallMain("StopBossPush");
            var commitment = CallMain("ExportFightPreparationCommitment");
            Equal("Committed", Property(Json(commitment, "state"), "Value"), "actual retired preparation retains terminal semantic history");
            var original = Json(commitment, "original").ToString();
            var path = Path.Combine((string)Get(MainType, "_dir"), "execution-fight-prep-selection.json");
            var setup = CallMain("ImportExactExecutionSelection", JsonParse(File.ReadAllText(path)));
            var swaps = Get(typeof(Program), "Swaps"); var rebirth = Get(Get(Character, "stats"), "rebirthNumber");
            Set(MainInstance, "_completedFightPreparationRecord", null);
            Set(MainInstance, "_manualRebirthPending", false); Set(MainInstance, "_customPreRebirthPrepCompleted", true);
            CallMain("ImportReviewedFightPreparation", commitment, setup);
            Equal(manualRebirth, Get(MainInstance, "_manualRebirthPending"), "committed import retains the original manual-rebirth choice");
            Equal(opportunity, Get(MainInstance, "_manualRebirthOpportunity"), "committed import retains original explicit opportunity, including fight-only requests");
            Equal(manualRebirth, Get(MainInstance, "_customPreRebirthPrepCompleted"), "fight-only import cannot authorize automatic rebirth completion");
            Equal(original, Json(CallMain("ExportFightPreparationCommitment"), "original").ToString(), "terminal import retains original entry/start/window rather than renewing it");
            Equal(null, Property(MainInstance, "FightPreparationClaim"), "terminal history import creates no native cleanup/action authority");
            Equal(false, Property(MainInstance, "BossPushRunning"), "terminal import does not re-enter a boss process");
            Equal(swaps, Get(typeof(Program), "Swaps"), "semantic completion import performs no native gear write");
            Equal(rebirth, Get(Get(Character, "stats"), "rebirthNumber"), "semantic completion import engages no native rebirth");
        }
        CheckNativePreparationFruitIdentity();
        CheckPreparationIngressAndEntryRegressions();
        Console.WriteLine("Preparation identity native regressions: " + (Assertions - before) + " assertions.");
    }

    private static void CheckPreparationIngressAndEntryRegressions()
    {
        CheckExplicitPreparationIngress();
        CheckPreparationIngressScope();
        foreach (var automatic in new[] { false, true })
        {
            CheckStageEntryPreparationRetry(automatic);
            CheckStageEntryPreparationReceiptFailure(automatic);
        }
        CheckStageEntryPreparationRetryGuards();
    }

    private static void SetupPreparationIngress()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "explicit preparation ingress baseline");
        Set(Get(Character, "allDiggers"), "baseGoldCost", Enumerable.Repeat(1e200, 12).ToList());
        ListField(Get(Character, "yggdrasil"), "fruits", 7);
        var all = Get(Character, "yggdrasilController"); Set(all, "character", Character);
        Set(ArrayField(all, "fruits", 1).GetValue(0), "character", Character);
    }

    private static void InvokeExplicitPreparation(string ingress, bool rebirth)
    {
        if (ingress == "probe")
        {
            CallMain("RunProbeCommand", new System.Collections.Generic.List<string>(), rebirth ? "rebirth" : "fight-prep");
            return;
        }
        // No window/control construction: run the actual click handler with only
        // its optional presentation fields absent and its button map empty.
        var form = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Plugin.GetType("NGUInjector.SettingsForm", true));
        GC.SuppressFinalize(form);
        Set(form, "_customFocusButtons", Activator.CreateInstance(Field(form, "_customFocusButtons").FieldType));
        Invoke(form, rebirth ? "CustomRunRebirth_Click" : "CustomRunFightPrep_Click", null, EventArgs.Empty);
    }

    private static void CheckExplicitPreparationIngress()
    {
        foreach (var ingress in new[] { "winforms", "probe" })
        foreach (var rebirth in new[] { false, true })
        {
            SetupPreparationIngress();
            Check(((string)CallMain("RunManualFightPrep")).Contains("identified explicit request"), "raw fight preparation still requires an explicit identity");
            Check(((string)CallMain("RunManualRebirth")).Contains("identified explicit request"), "raw rebirth still requires an explicit identity");
            Equal(null, Property(MainInstance, "FightPreparationIdentity"), "raw calls cannot acquire a preparation record");
            string previous = null;
            for (var request = 0; request < 2; request++)
            {
                InvokeExplicitPreparation(ingress, rebirth);
                var commitment = CallMain("ExportFightPreparationCommitment");
                var opportunity = (string)Property(Json(commitment, "opportunity"), "Value");
                Check(Guid.TryParseExact(opportunity, "N", out _), ingress + " assigns an explicit request identity before preparation");
                Check(opportunity != previous, ingress + " creates a fresh identity only for a new explicit invocation");
                Equal(rebirth, Property(Json(Json(commitment, "original"), "manualRebirth"), "AsBool"), ingress + " preserves the selected fight/rebirth intent");
                Equal(null, Get(MainInstance, "_currentExecutionCommandId"), ingress + " clears its scoped identity after dispatch");
                Equal(true, Property(MainInstance, "BossPushRunning"), ingress + " actually admits the prepared boss process");
                Equal(true, Property(MainInstance, "FightPreparationVerified"), ingress + " uses real native selected-setup proof");
                if (rebirth) Equal(opportunity, Get(MainInstance, "_manualRebirthOpportunity"), ingress + " retains the original opportunity for asynchronous rebirth continuation");
                CallMain("StopBossPush"); CallMain("ResetCustomPreRebirthPrep");
                previous = opportunity;
            }
        }
    }

    private static void CheckPreparationIngressScope()
    {
        SetupPreparationIngress();
        const string external = "original-external-probe-command";
        Set(MainInstance, "_currentExecutionCommandId", external);
        try
        {
            InvokeExplicitPreparation("probe", false);
            var original = CallMain("ExportFightPreparationCommitment").ToString();
            var process = Get(MainInstance, "_bossPushProcess");
            Equal(external, Property(Json(CallMain("ExportFightPreparationCommitment"), "opportunity"), "Value"), "nested probe preserves its audited external command identity");
            Equal(external, Get(MainInstance, "_currentExecutionCommandId"), "nested ingress restores the outer external scope");
            InvokeExplicitPreparation("probe", false);
            Check(ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), "same external opportunity cannot acquire a second boss process");
            Equal(original, CallMain("ExportFightPreparationCommitment").ToString(), "same external opportunity retains exact preparation commitments");
            CallMain("StopBossPush");
            var completed = CallMain("ExportFightPreparationCommitment").ToString();
            InvokeExplicitPreparation("probe", false);
            Equal(false, Property(MainInstance, "BossPushRunning"), "completed external opportunity cannot replay through explicit ingress");
            Equal(completed, CallMain("ExportFightPreparationCommitment").ToString(), "completed external history is not replaced by a generated identity");
        }
        finally { Set(MainInstance, "_currentExecutionCommandId", null); }

        SetupPreparationIngress();
        Set(MainInstance, "_currentExecutionCommandId", external);
        var stats = Get(Character, "stats"); Set(Character, "stats", null);
        try
        {
            Throws(() => CallMain("RunManualPreparationIngress", true), "NullReferenceException", "native metadata failure propagates out of explicit ingress");
            Equal(external, Get(MainInstance, "_currentExecutionCommandId"), "throwing ingress restores its original external identity in finally");
        }
        finally { Set(Character, "stats", stats); Set(MainInstance, "_currentExecutionCommandId", null); }
        CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Quiescing"));
        Throws(() => CallMain("RunManualPreparationIngress", false), "quiesced", "explicit ingress cannot bypass lifecycle admission");
        Equal(null, Get(MainInstance, "_currentExecutionCommandId"), "quiesced rejection mints no dispatch identity");
        Equal(null, Property(MainInstance, "FightPreparationIdentity"), "failed ingresses produce no preparation or native action authority");
    }

    private static object SetupEntryPreparation(bool automatic = false)
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "stage entry preparation baseline");
        var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(stage, "FightOnEntry", true); Set(stage, "ExecutionMode", automatic ? 1 : 0);
        var store = Get(MainType, "CustomPlanStore"); var name = Property(Get(MainType, "Settings"), "CustomPlanFile");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        CallMain("GetProtectedStage", plan);
        return plan;
    }

    private static void CheckStageEntryPreparationRetry(bool automatic)
    {
        var plan = SetupEntryPreparation(automatic); var runtime = CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler"); var entry = Property(scheduler, "Entry");
        var deadline = Property(scheduler, "ExpectedEndSeconds"); var entered = Property(scheduler, "EnteredAt");
        var fightKey = Property(Get(MainType, "Settings"), "CustomPlanFile") + ":" + CallMain("CustomStageRuntimeKey", plan, runtime);
        var checkpoint = (string)Property(MainInstance, "ProtectedCheckpointPath");
        var swaps = Get(typeof(Program), "Swaps"); var transfers = ResourceTransfers; var selections = 0;
        Program.FinitePreparationSelectionStarting = () => { selections++; throw new InvalidOperationException("entry selection unavailable"); };
        try
        {
            CallMain("RunCustomPlanAutomation", plan);
            Check(((string)CallMain("GetCustomAutomationStatus")).Contains("entry selection unavailable"), "entry selection fault is actually exercised");
            Equal(false, Property(MainInstance, "BossPushRunning"), "failed entry selection never starts a boss process/automatic=" + automatic);
            Equal(fightKey, Get(MainInstance, "_customStageFightKey"), "failed setup retains its original durable attempt receipt");
            Equal(fightKey, Property(Json(JsonParse(File.ReadAllText(checkpoint)), "fightEntry"), "Value"), "attempt receipt is saved before selection");
            var identity = Property(MainInstance, "FightPreparationIdentity");
            var original = Json(CallMain("ExportFightPreparationCommitment"), "original").ToString();
            var selectionPath = Path.Combine((string)Get(MainType, "_dir"), "execution-fight-prep-selection.json");
            File.WriteAllText(selectionPath, "{}");
            Program.FinitePreparationSelectionStarting = () => selections++;
            using (var held = new FileStream(selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Equal(true, CallMain("TryRunCustomStageEntryFight", plan, runtime), "same entry resumes acquisition but withholds setup on real selection-checkpoint failure");
                Equal(false, Property(MainInstance, "BossPushRunning"), "storage rejection never starts the entry fight");
                Equal(swaps, Get(typeof(Program), "Swaps"), "selection storage must precede native gear writes");
            }
            var selected = Get(MainInstance, "_fightPreparationSetup");
            Check(selected != null, "same entry retains its selected setup after storage rejection");
            var count = selections; var current = (long)Get(Character, "curEnergy"); var idle = (long)Get(Character, "idleEnergy");
            Set(Character, "curEnergy", current + 6000); Set(Character, "idleEnergy", idle + 6000);
            Equal(true, CallMain("TryRunCustomStageEntryFight", plan, runtime), "same entry retries retained setup through actual capacity preflight");
            Equal(false, Property(MainInstance, "BossPushRunning"), "infeasible retained setup admits no boss process");
            Equal(false, Property(MainInstance, "FightPreparationVerified"), "capacity rejection cannot fabricate verified preparation");
            Equal(swaps, Get(typeof(Program), "Swaps"), "capacity rejection changes no equipment");
            Equal(transfers, ResourceTransfers, "failed entry preparation performs no resource transfers");
            Set(Character, "curEnergy", current); Set(Character, "idleEnergy", idle);
            CallMain("RunCustomPlanAutomation", plan);
            Equal(true, Property(MainInstance, "BossPushRunning"), "recovered preparation actually starts its original boss process");
            Equal(true, Property(MainInstance, "FightPreparationVerified"), "recovered entry has real selected-setup proof");
            Equal(count, selections, "retry never selects replacement gear after exact selection");
            Check(ReferenceEquals(selected, Get(MainInstance, "_fightPreparationSetup")), "all setup retries retain the SAME selected object");
            Equal(identity, Get(MainInstance, "_bossPushIdentity"), "entry boss process consumes the original preparation identity");
            Equal(original, Json(CallMain("ExportFightPreparationCommitment"), "original").ToString(), "retry cannot renew original entry/start/window/options");
            Equal(entry, Property(scheduler, "Entry"), "retry grants no new scheduler entry");
            Equal(entered, Property(scheduler, "EnteredAt"), "retry does not restart elapsed accounting");
            Equal(deadline, Property(scheduler, "ExpectedEndSeconds"), "retry preserves original automatic/fixed deadline");
            var process = Get(MainInstance, "_bossPushProcess");
            Equal(false, CallMain("TryRunCustomStageEntryFight", plan, runtime), "started preparation never re-enters stage dispatch");
            Check(ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), "started entry retains its single boss process");
            CallMain("StopBossPush");
            var completed = CallMain("ExportFightPreparationCommitment").ToString();
            Equal(false, CallMain("TryRunCustomStageEntryFight", plan, runtime), "completed entry never acquires a second preparation");
            CallMain("InitializeProtectedStageProgress", plan); runtime = CallMain("GetProtectedStage", plan);
            Equal(false, CallMain("TryRunCustomStageEntryFight", plan, runtime), "checkpoint reload cannot renew entry-fight authority");
            Equal(completed, CallMain("ExportFightPreparationCommitment").ToString(), "reload retains terminal preparation history without another receipt");
            Equal(fightKey, Get(MainInstance, "_customStageFightKey"), "retry/start/completion/reload all retain the same durable fight key");
            Equal(count, selections, "started/completed/reloaded entry performs no replacement selection");
        }
        finally { Program.FinitePreparationSelectionStarting = null; }
    }

    private static void CheckStageEntryPreparationReceiptFailure(bool automatic)
    {
        var plan = SetupEntryPreparation(automatic); var runtime = CallMain("GetProtectedStage", plan);
        var path = (string)Property(MainInstance, "ProtectedCheckpointPath"); var searches = InlineGearSelections;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Equal(true, CallMain("TryRunCustomStageEntryFight", plan, runtime), "entry receipt storage fault withholds preparation/automatic=" + automatic);
            Equal(null, Get(MainInstance, "_customStageFightKey"), "failed receipt publication does not consume the entry");
            Equal(null, Property(MainInstance, "FightPreparationIdentity"), "undurable entry grants no preparation identity");
            Equal(searches, InlineGearSelections, "undurable entry performs no equipment selection");
            Equal(false, Property(MainInstance, "BossPushRunning"), "undurable entry performs no boss admission");
        }
        Equal(true, CallMain("TryRunCustomStageEntryFight", plan, runtime), "receipt storage recovery admits the original entry");
        Equal(true, Property(MainInstance, "BossPushRunning"), "receipt recovery actually starts one verified preparation");
        CallMain("StopBossPush");
    }

    private static void CheckStageEntryPreparationRetryGuards()
    {
        foreach (var change in new[] { "reload", "entry", "epoch", "fight-requested", "unknown" })
        {
            var plan = SetupEntryPreparation(); var runtime = CallMain("GetProtectedStage", plan);
            Program.FinitePreparationSelectionStarting = () => { throw new InvalidOperationException("retain unstarted entry"); };
            try { Equal(true, CallMain("TryRunCustomStageEntryFight", plan, runtime), "guard fixture acquires a genuinely unstarted preparation"); }
            finally { Program.FinitePreparationSelectionStarting = null; }
            var original = Json(CallMain("ExportFightPreparationCommitment"), "original").ToString();
            var key = Get(MainInstance, "_customStageFightKey");
            if (change == "reload")
            {
                CallMain("InitializeProtectedStageProgress", plan); runtime = CallMain("GetProtectedStage", plan);
            }
            else if (change == "entry")
            {
                var args = JsonParse("{\"name\":\"fixture\",\"expectedRevision\":\"" + Get(plan, "RuntimeRevision")
                    + "\",\"expectedEntry\":\"" + Get(MainInstance, "_protectedGeneration") + ":" + Property(Get(MainInstance, "_protectedScheduler"), "Entry")
                    + "\",\"action\":\"restart\",\"stageIndex\":0}");
                CallMain("OverrideCustomStage", args); // The old runtime token is intentionally retained below.
            }
            else if (change == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
            else
            {
                var record = Get(MainInstance, "_fightPreparationRecord");
                if (change == "fight-requested") Set(record, "After", change);
                else Set(record, "State", Enum.Parse(Plugin.GetType("NGUInjector.FiniteActionState", true), "Unknown"));
                Invoke(Property(MainInstance, "FiniteActions"), "Save", record);
            }
            var searches = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps"); var transfers = ResourceTransfers;
            Equal(false, CallMain("TryRunCustomStageEntryFight", plan, runtime), "unstarted receipt cannot renew authority after " + change);
            Equal(false, Property(MainInstance, "BossPushRunning"), "guard rejects boss admission after " + change);
            Equal(key, Get(MainInstance, "_customStageFightKey"), "guard cannot mint another entry receipt after " + change);
            Equal(original, Json(CallMain("ExportFightPreparationCommitment"), "original").ToString(), "guard preserves original commitments after " + change);
            Equal(searches, InlineGearSelections, "guard rejects replacement search after " + change);
            Equal(swaps, Get(typeof(Program), "Swaps"), "guard performs no gear write after " + change);
            Equal(transfers, ResourceTransfers, "guard performs no resource transfer after " + change);
        }
    }

    private static void CheckNativePreparationFruitIdentity()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "preparation fruit identity baseline");
        var all = Get(Character, "yggdrasilController"); Set(all, "character", Character);
        var fruits = ListField(Get(Character, "yggdrasil"), "fruits", 2);
        var controllers = ArrayField(all, "fruits", 1); var controller = controllers.GetValue(0);
        Set(controller, "character", Character); Child(controller, "tooltip");
        Set(all, "activationCost", new System.Collections.Generic.List<long> { 1, 1 });
        Set(all, "baseSeedReward", new System.Collections.Generic.List<long> { 10, 10 });
        Set(all, "usesEnergy", new System.Collections.Generic.List<bool> { true, true });
        Set(Get(Character, "NGUController"), "normalMagicBoostFactor", Enumerable.Repeat(.01f, 7).ToList());
        ListField(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed", 600);
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", true);
        Set(fruits[1], "maxTier", 1L); Set(fruits[1], "seconds", 3600f); Set(fruits[1], "activated", true);
        Set(fruits[1], "eatFruit", false);
        var identity = CallMain("ActionIdentity", "fight-preparation", "fight-preparation", "native-original-fruit", null);
        var claim = CallMain("BeginExecutionClaim", Get(identity, "Owner"), Get(identity, "Acquisition"),
            Domain("Gear, Support, Route, Inventory, FiniteAction"), (Func<bool>)(() => true));
        var setup = ExactClaimSetup(Get(Get(Character, "inventory"), "head"), "fight-preparation");
        CallMain("ApplyExecutionClaim", claim, setup);
        var opportunity = CallMain("PreparationFruitOpportunity", identity, 1);
        try
        {
            Equal(true, CallMain("RunFruitHarvestAction", 1, true, false, claim, setup, opportunity, 0), "original preparation fruit invokes one actual native harvest");
            var seeds = Get(Get(Character, "yggdrasil"), "seeds");
            Equal(1, Get(fruits[1], "harvests"), "original native preparation fruit is accounted once");
            Set(fruits[1], "seconds", 3600f); Set(fruits[1], "activated", true);
            Equal(true, CallMain("RunFruitHarvestAction", 1, true, false, claim, setup, opportunity, 0), "same identified completed step is recognized only under current verified setup");
            Equal(1, Get(fruits[1], "harvests"), "a later ripe cycle does not generate another preparation fruit effect");
            Set(MainInstance, "_finiteActionLedger", null);
            Equal(true, CallMain("RunFruitHarvestAction", 1, true, false, claim, setup, opportunity, 0), "ledger reload retains original per-preparation fruit opportunity");
            Equal(1, Get(fruits[1], "harvests"), "ledger reload cannot consume the later native fruit cycle");
            Equal(seeds, Get(Get(Character, "yggdrasil"), "seeds"), "repeated/imported preparation steps award no duplicate native seeds");
        }
        finally { CallMain("EndExecutionClaim", claim, false); }
    }
}
