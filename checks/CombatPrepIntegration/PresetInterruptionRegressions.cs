using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

public static partial class Program
{
    private static void ConfigurePresetInterruptionFixture(AssemblyDefinition game)
    {
        // Same recurring inputs as PresetIntegration; keep native totals, support and execution intact.
        var factors = new[] { "totalLevelBonus", "goldBonus", "lootBonus", "luckBonus", "totalDropChanceBonus",
            "totalAdventureBonus", "adventureBonus", "adventureBonus2", "PPBonus", "totalPPGainBonus" };
        foreach (var type in game.MainModule.Types)
        foreach (var method in type.Methods.Where(x => x.HasBody))
        {
            if (type.Name != "AllBeardsController" && type.Name != "AllGoldDiggerController"
                && factors.Contains(method.Name) && new[] { "System.Single", "System.Double" }.Contains(method.ReturnType.FullName))
                Constant(method, 1);
            if (type.Name == "AllAdvancedTraining" && new[] { "adventurePowerBonus", "adventureToughnessBonus" }.Contains(method.Name)) Constant(method, 0);
            if (type.Name == "Character" && method.Name == "idleAttackPower") Constant(method, 1.2);
            if (type.Name == "AdventureController" && method.Name == "respawnTime") Constant(method, 4);
            if (type.Name == "AllNGUController" && method.Name == "refreshMenu") Constant(method, 0);
        }
    }

    private static object PrepareInterruptionPreset(bool autoUpdate = true)
    {
        Set(MainInstance, "_globalPresetPolicies", Activator.CreateInstance(Field(MainInstance, "_globalPresetPolicies").FieldType));
        Set(MainInstance, "_globalPresetBeardRandom", new Random(42));
        // Setup already binds the actual PresetIntegration support fixture via PrepareSupportFixture.
        foreach (var name in new[] { "skills", "magicSkills" })
        {
            var lanes = (IList)Get(Get(Character, "NGU"), name);
            while (lanes.Count < 11) lanes.Add(Blank(lanes.GetType().GetGenericArguments()[0]));
        }
        Set(Get(Character, "NGU"), "autoAdvance", false);
        var library = Get(Get(MainType, "CustomPlanStore"), "PresetLibrary");
        var root = Invoke(library, "ReadExternalLibrary");
        var id = "interruption-" + Guid.NewGuid().ToString("N");
        var definition = Parse("{\"Id\":\"" + id + "\",\"Name\":\"Interruption fixture\",\"AutoUpdate\":" + (autoUpdate ? "true" : "false")
            + ",\"Weights\":[1,1,0,0,0,0,0,0,0,0,0,0,1,0],\"SupportMode\":\"current\",\"SwapSeconds\":0}");
        Invoke(Json(root, "Presets"), "Add", "", definition);
        var revision = Invoke(library, "SaveExternalLibrary", root, JsonValue(root, "__externalUiRevision"));
        return Parse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
    }

    private static object EquipInterruptionPreset(object args)
    {
        object[] invocation = { args, null };
        var message = (string)Invoke(MainInstance, "EquipGlobalPreset", invocation);
        Equal(true, message.StartsWith("Equipped global preset:"), "actual global Equip succeeds");
        Equal(true, invocation[1] != null, "actual global Equip returns its observed result");
        var receipt = Get(MainInstance, "_globalPresetReceipt");
        Equal(JsonValue(args, "presetId"), Get(receipt, "Id"), "receipt belongs to committed preset");
        Equal("dashboard", Get(receipt, "Owner"), "manual Equip installs dashboard ownership");
        Equal(false, Get(Settings, "UseCustomRebirthPlan"), "manual Equip pauses custom plan");
        Equal(false, Get(Settings, "AutoRebirth"), "manual Equip pauses automatic rebirth");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "dashboard receipt matches actual gear/support/resources");
        return receipt;
    }

    private static void RejectInterruptionEquip(Action action, string expected)
    {
        Exception failure = null;
        try { action(); } catch (Exception error) { failure = error; }
        Equal(true, failure != null && failure.ToString().Contains(expected), "Equip rejects for " + expected + ": " + failure);
    }

    private static void CheckPresetInterruptionRegressions()
    {
        var before = Assertions;
        CheckManualPrepPresetBlocker();
        CheckPresetInterruptionRecovery();
        CheckScheduledPresetInterruption();
        CheckPresetInterruptionValidation();
        CheckPresetInterruptionCleanup();
        Setup();
        Console.WriteLine("Preset interruption regressions: " + (Assertions - before) + " assertions.");
    }

    private static void CheckScheduledPresetInterruption()
    {
        var plan = ScheduledSetup(ScheduledRow + "," + StandardRow);
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        foreach (var row in diggers) Set(row, "maxLevel", 20L);
        ((IList)Get(Get(Character, "allDiggers"), "boostPerLevel"))[1] = 2d;
        var inventory = Get(Character, "inventory");
        var phaseHead = Blank(Get(inventory, "head").GetType()); Set(phaseHead, "id", 60); SetEnum(phaseHead, "type", "Head");
        Set(phaseHead, "bossRequired", 1); Set(phaseHead, "removable", true); SetEnum(phaseHead, "spec1Type", "Wandoos98"); Set(phaseHead, "spec1Cur", 100f);
        ((IList)Get(inventory, "inventory"))[0] = phaseHead;
        Set(Get(Get(Character, "arbitrary"), "nukeTimer"), "totalseconds", 17d);
        var original = CallMain("CombatPrepEquipped"); var originalSupport = CallMain("CombatPrepSupportKey");
        var run = StartScheduled(plan);
        Equal(false, CallMain("CombatPrepGearMatches", original), "interruption fixture owns non-original phase gear");
        Equal(false, Equals(originalSupport, CallMain("CombatPrepSupportKey")), "interruption fixture owns support distinct from original");
        Equal("verified", Get(run, "SupportStatus"), "scheduled phase support ownership is verified before interruption");
        Equal(20L, Get(diggers[1], "curLevel"), "scheduled phase acquires the beneficial Wandoos digger level");
        var args = PrepareInterruptionPreset();
        var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        Equal(originalSupport, Get(Get(Get(run, "Capture"), "OriginalSupportState"), "StableKey"), "restoration capture retains pre-run support");
        var head = Blank(Get(inventory, "head").GetType()); Set(head, "id", 61); SetEnum(head, "type", "Head");
        // Capacity gives a strict gear improvement even when both NGUs already hit their tick caps.
        Set(head, "bossRequired", 1); Set(head, "removable", true); SetEnum(head, "spec1Type", "EnergyCap"); Set(head, "spec1Cur", 1000000f);
        ((IList)Get(inventory, "inventory"))[1] = head;
        var phaseSupport = CallMain("CombatPrepSupportKey");
        var headTimeline = new List<object>(); NativeGearSwapped = item => headTimeline.Add(item);
        object receipt;
        try { receipt = EquipInterruptionPreset(args); }
        finally { NativeGearSwapped = null; }
        Equal("cancelled", Get(run, "Status"), "scheduled prep is cancelled before manual acquisition");
        Equal("Scheduled owner paused, switched, disabled or current entry changed.", Get(run, "Reason"), "interruption uses scheduler pause semantics");
        Equal(false, Get(run, "CleanupPending"), "preset waits for complete old-owner cleanup");
        Equal(true, headTimeline.Count == 1 && ReferenceEquals(headTimeline[0], head), "automatic combat -> current B has exactly one native head swap with no historical A detour");
        Equal(phaseSupport, CallMain("CombatPrepSupportKey"), "Keep current observes actual phase support, not a historical return target");
        Equal(true, ReferenceEquals(head, Get(inventory, "head")), "successor equips its own optimized head, distinct from original and phase gear");
        Equal(entry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity"), "manual takeover retains original scheduler entry");
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "manual takeover retains original scheduler deadline");
        Equal(0, Get(scheduler, "StageIndex"), "manual takeover does not consume automatic row");
        Equal(false, Get(scheduler, "Finished"), "manual takeover does not finish schedule");
        Equal(17d, Get(Get(Get(Character, "arbitrary"), "nukeTimer"), "totalseconds"), "old native nuke lease is restored before successor");

        var gear = CallMain("CombatPrepEquipped"); var support = CallMain("CombatPrepSupportKey");
        var allocations = CallMain("GlobalPresetAllocationState"); var targets = string.Join(",", (long[])Get(Get(Character, "advancedTraining"), "levelTarget"));
        var recalls = Recalls; var swaps = Swaps;
        var cancel = Parse("{\"runId\":\"" + Get(run, "Id") + "\"}");
        for (var tick = 0; tick < 3; tick++)
        {
            CallMain("TickCombatPrep"); CallMain("CancelCombatPrep", cancel);
        }
        Equal(recalls, Recalls, "late old-owner ticks/cancel never recall successor allocations");
        Equal(swaps, Swaps, "late old-owner ticks/cancel never swap successor gear");
        Equal(true, CallMain("CombatPrepGearMatches", gear), "late cleanup cannot restore old original gear over successor");
        Equal(support, CallMain("CombatPrepSupportKey"), "late cleanup cannot restore old support over successor");
        Equal(allocations, CallMain("GlobalPresetAllocationState"), "successor allocations survive late cancellation");
        Equal(targets, string.Join(",", (long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "late cleanup cannot overwrite native targets");
        Equal(true, ReferenceEquals(receipt, Get(MainInstance, "_globalPresetReceipt")), "late cancellation retains dashboard receipt");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "dashboard receipt remains valid after late cancellation");

        Setting("UseCustomRebirthPlan", true); ScheduledTime(550);
        var resumed = StartScheduled(plan);
        Equal(false, ReferenceEquals(run, resumed), "resume starts a fresh executor, not the ended run");
        Equal(entry, Get(resumed, "OwnerEntry"), "viable resume uses the same paused entry");
        Equal(deadline, Get(resumed, "DeadlineRebirthSeconds"), "viable resume cannot grant a fresh deadline");
        recalls = Recalls; swaps = Swaps;
        CallMain("CancelCombatPrep", cancel);
        Equal("running", Get(resumed, "Status"), "old cancel token cannot cancel resumed executor");
        Equal(recalls, Recalls, "old cancel token cannot recall resumed allocations");
        Equal(swaps, Swaps, "old cancel token cannot restore gear over resumed executor");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
    }

    private static void CheckPresetInterruptionValidation()
    {
        foreach (var rejection in new[] { "stale", "missing-policy", "readiness", "nonmanual" })
        {
            var plan = ScheduledSetup(); var args = PrepareInterruptionPreset(rejection != "missing-policy");
            var run = StartScheduled(plan); var scheduler = Get(MainInstance, "_protectedScheduler");
            var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
            var recalls = Recalls; var swaps = Swaps; var allocations = CallMain("GlobalPresetAllocationState");
            if (rejection == "stale")
            {
                var library = Get(Get(MainType, "CustomPlanStore"), "PresetLibrary");
                var root = Invoke(library, "ReadExternalLibrary");
                var saved = ((IEnumerable)Get(Json(root, "Presets"), "Children")).Cast<object>()
                    .Single(x => JsonValue(x, "Id") == JsonValue(args, "presetId"));
                Set(Json(saved, "Name"), "Value", "Changed after selection");
                Invoke(library, "SaveExternalLibrary", root, JsonValue(root, "__externalUiRevision"));
                RejectInterruptionEquip(() => CallMain("EquipGlobalPreset", args, null), "revision changed");
            }
            else if (rejection == "nonmanual")
            {
                var preset = CallMain("ResolveGlobalPreset", args);
                RejectInterruptionEquip(() => CallMain("EquipGlobalPresetDefinition", preset, "stage:fixture", "stage-entry", false, null), "Combat prep owns resources");
            }
            else
            {
                if (rejection == "readiness") Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", false);
                RejectInterruptionEquip(() => CallMain("EquipGlobalPreset", args, null), rejection == "readiness" ? "NGU is locked" : "No reusable policy");
            }
            Equal("running", Get(run, "Status"), "rejected Equip leaves scheduled prep running: " + rejection);
            Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "rejected Equip retains the exact active run: " + rejection);
            Equal(false, Get(run, "CleanupPending"), "rejected Equip does not initiate cleanup: " + rejection);
            Equal(recalls, Recalls, "rejected Equip never recalls: " + rejection);
            Equal(swaps, Swaps, "rejected Equip never swaps: " + rejection);
            Equal(allocations, CallMain("GlobalPresetAllocationState"), "rejected Equip preserves prep allocations: " + rejection);
            Equal(true, Get(Settings, "UseCustomRebirthPlan"), "rejected Equip leaves plan enabled: " + rejection);
            Equal(true, Get(Settings, "AutoRebirth"), "rejected Equip leaves auto-rebirth enabled: " + rejection);
            Equal(null, Get(MainInstance, "_globalPresetReceipt"), "rejected Equip installs no receipt: " + rejection);
            Equal(entry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity"), "rejected Equip retains entry: " + rejection);
            Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "rejected Equip retains deadline: " + rejection);
            CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
        }
    }

    private static void CheckManualPrepPresetBlocker()
    {
        Setup(); var args = PrepareInterruptionPreset();
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        var preview = Preview(60);
        Start(JsonValue(preview, "previewId"), JsonValue(preview, "recommendedCandidateId"));
        var run = Get(MainInstance, "_combatPrepRun"); var recalls = Recalls; var swaps = Swaps;
        Equal(false, Get(run, "Automatic"), "manual-prep negative control is not scheduled");
        RejectInterruptionEquip(() => CallMain("EquipGlobalPreset", args, null), "Combat prep owns resources");
        Equal("running", Get(run, "Status"), "manual Equip cannot interrupt manually started prep");
        Equal(recalls, Recalls, "manual prep rejection never recalls"); Equal(swaps, Swaps, "manual prep rejection never swaps");
        Equal(null, Get(MainInstance, "_globalPresetReceipt"), "manual prep rejection acquires no receipt");
        CallMain("EndCombatPrep", "cancelled", "test cleanup", true);
        EquipInterruptionPreset(args);
    }

    private static void CheckPresetInterruptionCleanup()
    {
        object plan, original;
        var run = CleanupRecoveryFixture(out plan, out original); var args = PrepareInterruptionPreset();
        var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        FailDesiredEnergyPreflight = true;
        RejectInterruptionEquip(() => CallMain("EquipGlobalPreset", args, null), "fixture desired-resource observation failure");
        Equal(false, Get(run, "CleanupPending"), "current preset preflight failure must not recreate old combat release ownership");
        Equal(1, DesiredEnergyPreflightFailures, "fault reaches current preset resource execution, not an old empty-map release");
        var outcome = Get(MainInstance, "_presetResourceOutcome");
        Equal("RejectedUnchanged", Get(outcome, "Disposition").ToString(), "actual successor failure remains typed rejected unchanged");
        Equal(0, Get(outcome, "ModelWrites"), "rejected successor commits no resource accounting");
        Equal(entry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity"), "cleanup retry does not consume original entry");
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "cleanup retry preserves original deadline");
        EquipInterruptionPreset(args);
        Equal(entry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity"), "successful Equip retry keeps paused entry");
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "successful Equip retry keeps paused deadline");
        Equal(0, Get(scheduler, "StageIndex"), "successful Equip retry never advances to successor row");
    }

    private static void CheckPresetInterruptionRecovery()
    {
        object plan, original;
        var run = CleanupRecoveryFixture(out plan, out original); var args = PrepareInterruptionPreset();
        var entry = Get(run, "OwnerEntry"); var deadline = Get(run, "DeadlineRebirthSeconds");
        ((List<Action>)Get(run, "RestoreTargets")).Add(() => { throw new InvalidOperationException("fixture borrowed return failure before handoff"); });
        CallMain("EndCombatPrep", "cancelled", Get(MainType, "CombatPrepOwnerPausedReason"), true);
        Set(run, "CleanupLastAttempt", (double)Get(MainType, "CombatPrepClock") + 100);
        var store = Get(MainType, "CustomPlanStore"); CallMain("Unload");
        MainInstance = Blank(MainType); Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>());
        Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
        Set(MainType, "reference", MainInstance); Set(MainType, "Character", Character); Set(MainType, "Controller", Controller);
        Set(MainType, "Settings", Settings); Set(MainType, "CustomPlanStore", store); Set(Controller, "midDrag", false);
        CallMain("InitializeProtectedStageProgress", plan);
        args = PrepareInterruptionPreset();
        Equal(null, Get(MainInstance, "_combatPrepRun"), "recovery-only fixture has no live run to cancel");
        Equal(true, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "orphan marker awaits guarded current-intent service");
        var recalls = Recalls; var swaps = Swaps;
        var currentEntry = entry;
        EquipInterruptionPreset(args);
        Equal(recalls, Recalls, "current manual preset intent uses exact resource differences, not historical whole-lane recalls");
        Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "current manual preset has verified native funding after historical ownership retirement");
        Equal(false, Get(MainInstance, "CombatPrepCleanupRecoveryRequired"), "manual preset needs no recovery acknowledgement");
        Equal(false, Get(Settings, "UseCustomRebirthPlan"), "successful current manual intent pauses plan");
        Equal(false, Get(Settings, "AutoRebirth"), "successful current manual intent pauses auto-rebirth");
        Equal(currentEntry, Get(CallMain("GetProtectedStage", plan), "EntryIdentity"), "manual preset leaves current paused entry unchanged");
        Equal(deadline, Get(Get(MainInstance, "_protectedScheduler"), "ExpectedEndSeconds"), "orphan recovery plus manual preset preserves paused original deadline");
        Equal(true, Get(MainInstance, "_globalPresetReceipt") != null, "manual preset acquires a fresh legitimate receipt");
    }
}
