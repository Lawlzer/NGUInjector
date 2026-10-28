using System;
using System.Collections;
using System.IO;
using System.Linq;

// Preparation-specific native checks. The shared action selector owns dispatch.
public static partial class PresetIntegrationChecks
{
    private static void ConfigureStageFightSchedule(object stage, string id, int anchor = 0, int delay = 0, int interval = 0)
    {
        var schedule = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomFightSchedule", true));
        Set(schedule, "Id", id); Set(schedule, "Anchor", anchor); Set(schedule, "DelaySeconds", delay); Set(schedule, "IntervalSeconds", interval);
        Set(stage, "FightSchedule", schedule);
    }

    private static void CheckActionPreparationIdentityRegressions()
    {
        var before = Assertions;
        CheckExplicitPreparationIngress();
        CheckPreparationIngressScope();
        CheckNativeBusyPreparation();
        CheckSessionPreparationRetry();
        foreach (var automatic in new[] { false, true }) CheckStageEntryPreparationReceiptFailure(automatic);
        CheckStandardStageFightFunding();
        Equal(null, MainType.GetMethod("ExportFightPreparationCommitment", All), "session preparation exports no action history");
        Equal(null, MainType.GetMethod("ImportReviewedFightPreparation", All), "session preparation cannot resurrect a previous request");
        Console.WriteLine("Preparation session native regressions: " + (Assertions - before) + " assertions.");
    }

    private static object SetupPreparationIngress()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "explicit preparation ingress baseline");
        Set(Get(Character, "allDiggers"), "baseGoldCost", Enumerable.Repeat(1e200, 12).ToList());
        ListField(Get(Character, "yggdrasil"), "fruits", 7);
        var all = Get(Character, "yggdrasilController"); Set(all, "character", Character);
        Set(ArrayField(all, "fruits", 1).GetValue(0), "character", Character);
        return plan;
    }

    private static void InvokeExplicitPreparation(string ingress, bool rebirth)
    {
        if (ingress == "probe")
        {
            CallMain("RunProbeCommand", new System.Collections.Generic.List<string>(), rebirth ? "rebirth" : "fight-prep");
            return;
        }
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
            Check(((string)CallMain("RunManualFightPrep")).Contains("identified explicit request"), "raw fight preparation requires an explicit identity");
            Check(((string)CallMain("RunManualRebirth")).Contains("identified explicit request"), "raw rebirth requires an explicit identity");
            Equal(null, Property(MainInstance, "FightPreparationIdentity"), "raw calls cannot acquire a preparation attempt");
            InvokeExplicitPreparation(ingress, rebirth);
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            var opportunity = (string)Get(Get(attempt, "Identity"), "Opportunity");
            Check(Guid.TryParseExact(opportunity, "N", out _), ingress + " supplies an explicit request identity");
            Equal(rebirth, Get(MainInstance, "_manualRebirthPending"), ingress + " retains only the current session's rebirth intent");
            Equal(null, Get(MainInstance, "_currentExecutionCommandId"), ingress + " clears scoped command identity after dispatch");
            Equal(true, Property(MainInstance, "BossPushRunning"), ingress + " admits the real prepared boss process");
            Equal(true, Property(MainInstance, "FightPreparationVerified"), ingress + " verifies real native setup");
            Equal(false, File.Exists(Path.Combine((string)Get(MainType, "_dir"), "execution-fight-prep-selection.json")), "fight preparation creates no restorable selection journal");
            CallMain("StopBossPush"); CallMain("ResetCustomPreRebirthPrep");
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
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            var process = Get(MainInstance, "_bossPushProcess");
            Equal(external, Get(Get(attempt, "Identity"), "Opportunity"), "nested ingress retains external command identity");
            Equal(external, Get(MainInstance, "_currentExecutionCommandId"), "nested ingress restores the outer scope");
            InvokeExplicitPreparation("probe", false);
            Check(ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), "same session request cannot acquire a second boss process");
            Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "same request retains its selected attempt");
            CallMain("StopBossPush"); InvokeExplicitPreparation("probe", false);
            Equal(false, Property(MainInstance, "BossPushRunning"), "completed session request cannot re-enter its boss process");
        }
        finally { Set(MainInstance, "_currentExecutionCommandId", null); }
    }

    private static void CheckNativeBusyPreparation()
    {
        var plan = SetupPreparationIngress();
        var boss = Get(Character, "bossController");
        foreach (var field in new[] { "isFighting", "nukeBoss" })
        {
            Set(boss, field, true);
            var searches = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps"); var transfers = ResourceTransfers;
            var result = (string)CallMain("RunFightPrep", plan, true, "native busy preparation", null, "busy-" + field);
            Check(result.Contains("waiting for the current native boss fight"), "native busy preparation waits before selection: " + result);
            Equal(null, Property(MainInstance, "FightPreparationIdentity"), "foreign native fight creates no preparation authority");
            Equal(searches, InlineGearSelections, "native busy preparation performs no gear search");
            Equal(swaps, Get(typeof(Program), "Swaps"), "native busy preparation performs no equipment write");
            Equal(transfers, ResourceTransfers, "native busy preparation performs no recall");
            Equal(false, CallMain("PrepareFinalRebirthAction"), "native busy blocks final NUMBER setup");
            Set(boss, field, false);
        }
    }

    private static void CheckSessionPreparationRetry()
    {
        var plan = SetupPreparationIngress();
        var searches = 0;
        Program.FinitePreparationSelectionStarting = () => { searches++; throw new InvalidOperationException("selection unavailable"); };
        try
        {
            var result = (string)CallMain("RunFightPrep", plan, false, "retry preparation", null, "retry-original");
            Check(result.Contains("selection unavailable"), "selection denial is exercised");
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            var identity = Property(MainInstance, "FightPreparationIdentity");
            Equal(true, CallMain("FightPreparationCanRetry", identity), "uninvoked attempt remains retryable");
            Equal(false, Get(attempt, "BossStartAttempted"), "selection denial consumes no boss-start step");
            Program.FinitePreparationSelectionStarting = () => searches++;
            result = (string)CallMain("RunFightPrep", plan, false, "retry preparation", null, "retry-original");
            Check(result.Contains("boss push running"), "recovered selection admits original process: " + result);
            Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "retry retains original options and epoch");
            Equal(identity, Get(MainInstance, "_bossPushIdentity"), "boss process consumes the original preparation identity");
            var count = searches; var process = Get(MainInstance, "_bossPushProcess");
            CallMain("RunFightPrep", plan, false, "retry preparation", null, "retry-original");
            Equal(count, searches, "waiting for boss admission never repeats equipment selection");
            Check(ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), "waiting preparation retains one process");
            CallMain("StopBossPush");
            Equal(false, CallMain("FightPreparationCanRetry", identity), "completed preparation has no live retry authority");
            Equal(false, Property(MainInstance, "BossPushRunning"), "completion does not re-enter a process");
        }
        finally { Program.FinitePreparationSelectionStarting = null; }
    }

    private static void CheckStageEntryPreparationReceiptFailure(bool automatic)
    {
        var plan = SetupPreparationIngress();
        var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        ConfigureStageFightSchedule(stage, "entry-receipt"); Set(stage, "ExecutionMode", automatic ? 1 : 0);
        var store = Get(MainType, "CustomPlanStore"); var name = Property(Get(MainType, "Settings"), "CustomPlanFile");
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        var runtime = CallMain("GetProtectedStage", plan);
        CallMain("OverrideCustomStage", JsonParse("{\"name\":\"" + name + "\",\"expectedRevision\":\"" + Get(plan, "RuntimeRevision")
            + "\",\"expectedEntry\":\"" + Property(runtime, "EntryIdentity") + "\",\"action\":\"restart\",\"stageIndex\":0}"));
        var path = (string)Property(MainInstance, "ProtectedCheckpointPath"); var searches = InlineGearSelections;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Equal(true, CallMain("ServiceStageFightSchedule", plan), "entry marker storage fault withholds preparation/automatic=" + automatic);
            Equal(null, Get(MainInstance, "_customStageFightKey"), "failed stage marker does not consume the entry");
            Equal(null, Property(MainInstance, "FightPreparationIdentity"), "undurable stage entry grants no session attempt");
            Equal(searches, InlineGearSelections, "undurable stage marker performs no selection");
        }
        Equal(true, CallMain("ServiceStageFightSchedule", plan), "stage marker recovery admits its original entry");
        Equal(true, Property(MainInstance, "BossPushRunning"), "marker recovery starts one verified preparation");
        CallMain("StopBossPush");
        Equal(false, CallMain("ServiceStageFightSchedule", plan), "consumed stage entry cannot start another preparation");
        CallMain("InitializeProtectedStageProgress", plan); runtime = CallMain("GetProtectedStage", plan);
        Equal(false, CallMain("ServiceStageFightSchedule", plan), "stage marker survives reload without restoring preparation authority");
    }

    private static void CheckStandardStageFightFunding()
    {
        foreach (var preset in new[] { false, true })
        foreach (var funded in new[] { false, true })
        {
            var plan = PrepareLockedTimeMachineStage("AUG", false);
            var stage = ((Array)Get(plan, "Stages")).GetValue(0);
            // The reused fixture otherwise replaces AUG with WAN while Time Machine is locked.
            Set(stage, "UseWandoosWhenTimeMachineLocked", false);
            Equal("AUG", StagePriorities(stage, true)[0], "funding fixture retains the real Augments priority");
            ConfigureStageFightSchedule(stage, "standard-funding", 1, 600);
            Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", funded);
            var store = Get(MainType, "CustomPlanStore");
            if (preset)
            {
                var library = Get(store, "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
                var id = "scheduled-augment-" + Guid.NewGuid().ToString("N");
                Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Scheduled Augments\","
                    + "\"Weights\":[0,0,0,0,0,0,0,1,0,0,0,0,0,0],\"EnergyPriorities\":[\"AUG\"],\"MagicPriorities\":[],"
                    + "\"ResourcePrioritiesReviewed\":true,\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":true}"));
                Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
                Set(stage, "PresetId", id);
            }
            var name = (string)Property(Get(MainType, "Settings"), "CustomPlanFile");
            Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
            CallMain("RunCustomPlanAutomation", plan);
            var actual = ((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>()
                .Sum(augment => (long)Get(augment, "augEnergy") + (long)Get(augment, "upgradeEnergy"));
            Equal(funded, actual > 0, "standard schedule fixture exercises actual positive funding/preset=" + preset + "/funded=" + funded
                + "; status=" + CallMain("GetCustomAutomationStatus") + "; error=" + InlineObservedError);
            var anchor = Get(Get(MainInstance, "_stageFightSchedule"), "AnchorAt");
            Equal(funded ? Get(Get(Character, "rebirthTime"), "totalseconds") : null, anchor,
                "standard schedule anchor follows verified native Augments, not configuration/preset=" + preset + "/funded=" + funded);
            Equal(false, Property(MainInstance, "BossPushRunning"), "first-funding delay never runs a fight early");
            if (funded)
            {
                var saved = JsonParse(File.ReadAllText((string)Property(MainInstance, "ProtectedCheckpointPath")));
                Equal(anchor, Property(Json(Json(saved, "fightSchedule"), "anchorAt"), "AsDouble"), "standard funding anchor is checkpointed");
            }
        }
    }
}
