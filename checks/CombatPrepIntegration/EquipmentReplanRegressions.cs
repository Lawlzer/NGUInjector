using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;

public static partial class Program
{
    private static void MergeNativeEquipment(object item, float spec1, float spec2 = 0)
    {
        var revise = (Action)CallMain("CaptureCombatPrepMaterialRevision");
        var copy = Blank(item.GetType());
        foreach (var field in new[] { "id", "type", "bossRequired", "spec1Type", "spec2Type", "spec3Type" }) Set(copy, field, Get(item, field));
        Set(copy, "removable", true); Set(copy, "spec1Cur", spec1); Set(copy, "spec2Cur", spec2);
        var level = (int)Get(item, "level");
        Invoke(item, "mergeItem", copy); // Real native same-destination merge; not a mocked revision or optimizer.
        Equal(Math.Min(100, level + 1), Get(item, "level"), "native merge grows the same equipment instance's level");
        RefreshBonuses();
        revise?.Invoke();
    }

    private static bool CaptureContainsGear(object capture, object item)
    {
        return ((IDictionary)Get(capture, "Gear")).Values.Cast<Array>().SelectMany(x => x.Cast<object>())
            .Any(x => ReferenceEquals(Get(x, "equipment"), item));
    }

    private static void CheckEquipmentReplanRegressions()
    {
        CheckAutomaticNativeGearGrowth();
        CheckUnvisitedEquipmentChanges();
        CheckAtEquipmentGrowthGate();
        CheckManualEquipmentStaleness();
        CheckAutomaticEquipmentMapAndRestorationOwnership();
        CheckEquipmentChangesDoNotMaskContextFailures();
    }

    private static void CheckAutomaticNativeGearGrowth()
    {
        var plan = ScheduledSetup();
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        var head = Get(Get(Character, "inventory"), "head");
        SetEnum(head, "spec1Type", "EnergyPower"); Set(head, "spec1Cap", 1000000f); Set(head, "spec1Cur", 1f); RefreshBonuses();
        var run = StartScheduled(plan); var original = Get(run, "Capture"); var originalGear = Get(original, "OriginalGear");
        Equal(true, CallMain("CombatPrepGearMatches", Get(run, "ExpectedGear")), "same-map growth starts with owned gear");
        var deadline = Get(run, "DeadlineRebirthSeconds"); var started = Get(run, "Started"); var entry = Get(run, "OwnerEntry");
        var energy = (long)Get(run, "EnergyPool"); var magic = (long)Get(run, "MagicPool"); var os = Get(Get(Character, "wandoos98"), "os");
        MergeNativeEquipment(head, 100000f);
        ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "same-instance native stat growth is not a fatal stale-preview error");
        Equal(1, Get(run, "ReplanCount"), "native growth invokes one fresh shared planner search");
        Equal("material-equipment-change", Get(run, "ReplanReason"), "equipment replan has explicit cause");
        Equal(true, ReferenceEquals(original, Get(run, "Capture")), "fresh planning never replaces original restoration capture");
        Equal(true, ReferenceEquals(originalGear, Get(Get(run, "Capture"), "OriginalGear")), "original restoration map stays immutable");
        Equal(started, Get(run, "Started"), "stat growth never resets original start");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "stat growth never extends original deadline");
        Equal(entry, Get(run, "OwnerEntry"), "stat growth retains stage entry identity");
        Equal(os, Get(Get(Character, "wandoos98"), "os"), "stat growth retains OS");
        Equal(energy, Get(run, "EnergyPool"), "power-only growth observes unchanged actual energy");
        Equal(magic, Get(run, "MagicPool"), "power-only growth observes unchanged actual magic");
        ScheduledRefresh(run); Equal(1, Get(run, "ReplanCount"), "owned phase swaps do not retrigger material detection");
        Set(head, "path", "cosmetic-only-path"); ScheduledRefresh(run);
        Equal(1, Get(run, "ReplanCount"), "cosmetic fields do not invalidate automatic material inputs");
        CheckSelectedPolicyTelemetry(run);
        var grownLevel = Get(head, "level");
        CallMain("EndCombatPrep", "cancelled", "equipment regression cleanup", true);
        Equal(true, CallMain("CombatPrepGearMatches", originalGear), "cleanup restores the original instances after replanning");
        Equal(grownLevel, Get(head, "level"), "cleanup does not roll back native item growth");
    }

    private static void CheckUnvisitedEquipmentChanges()
    {
        foreach (var change in new[] { "merge", "add", "remove" })
        {
            var plan = ScheduledSetup();
            Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
            var inventory = Get(Character, "inventory"); var bag = (IList)Get(inventory, "inventory");
            var unused = Blank(Get(inventory, "head").GetType()); Set(unused, "id", 61); SetEnum(unused, "type", "Head");
            Set(unused, "bossRequired", 1); Set(unused, "removable", true); SetEnum(unused, "spec1Type", "Wandoos98"); Set(unused, "spec1Cap", 1000000f);
            if (change != "add") bag[0] = unused;
            var run = StartScheduled(plan); var original = Get(run, "Capture");
            Equal(false, CaptureContainsGear(original, unused), "non-winning item is not in the cached profile portfolio");
            var deadline = Get(run, "DeadlineRebirthSeconds"); var started = Get(run, "Started");
            var revise = (Action)CallMain("CaptureCombatPrepMaterialRevision");
            if (change == "merge") MergeNativeEquipment(unused, 100000f);
            if (change == "add") { Set(unused, "spec1Cur", 100000f); bag[0] = unused; }
            if (change == "remove") bag[0] = Blank(unused.GetType());
            revise?.Invoke();
            ScheduledRefresh(run);
            Equal("running", Get(run, "Status"), "unused candidate material changes are refreshed, not fatal: " + change);
            Equal("material-equipment-change", Get(run, "ReplanReason"), "all-owned material detector sees unused candidate change: " + change);
            Equal(1, Get(run, "ReplanCount"), "one candidate change causes one replan: " + change);
            Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "unused candidate change preserves original deadline");
            Equal(started, Get(run, "Started"), "unused candidate change preserves original elapsed accounting");
            Equal(true, ReferenceEquals(original, Get(run, "Capture")), "unused candidate change preserves original restoration references");
            ScheduledRefresh(run); Equal(1, Get(run, "ReplanCount"), "new phase acquisition is not treated as foreign material change");
            CallMain("EndCombatPrep", "cancelled", "equipment regression cleanup", true);
        }
    }

    private static void CheckAtEquipmentGrowthGate()
    {
        var plan = ScheduledSetup();
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var head = Get(Get(Character, "inventory"), "head");
        SetEnum(head, "spec1Type", "EnergyPower"); Set(head, "spec1Cap", 100000000f);
        SetEnum(head, "spec2Type", "MagicCap"); Set(head, "spec2Cap", 100000000f);
        ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0] = 500; Set(Character, "idleEnergy", 9500L); RefreshBonuses();
        var run = StartScheduled(plan); var policy = Get(run, "Policy"); var phase = ScheduledPhase(run);
        Equal("AT", Get(phase, "Kind"), "equipment-growth gate regression acquires real planner AT phase");
        var targetE = Get(phase, "AtEnergyTarget"); var targetM = Get(phase, "AtMagicTarget"); var phaseDeadline = Get(run, "PhaseDeadline");
        var deadline = Get(run, "DeadlineRebirthSeconds"); var restore = Get(run, "Capture");
        var allocations = (long[])Get(Get(Character, "advancedTraining"), "energy"); var firstE = allocations[3]; var firstM = allocations[4];
        MergeNativeEquipment(head, 10000000f, 10000000f);
        ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "active AT tolerates same-instance native merge");
        Equal(true, ReferenceEquals(policy, Get(run, "Policy")), "unfinished AT policy is never replaced by material re-search");
        Equal(0, Get(run, "ReplanCount"), "equipment search deferred while acquired AT is unfinished");
        Equal(true, Get(run, "EquipmentReplanPending"), "deferred equipment search is explicit");
        Equal(targetE, Get(ScheduledPhase(run), "AtEnergyTarget"), "acquired energy AT gate unchanged");
        Equal(targetM, Get(ScheduledPhase(run), "AtMagicTarget"), "acquired magic AT gate unchanged");
        Equal(phaseDeadline, Get(run, "PhaseDeadline"), "unfinished AT phase timing is unchanged");
        Equal(true, allocations[3] < firstE && allocations[4] < firstM, "current native power is used to recap active AT allocations");
        var capture = Get(run, "Forecast"); var snapshot = Get(capture, "Snapshot");
        var profile = new[] { Get(snapshot, "Current") }.Concat(((Array)Get(snapshot, "Profiles")).Cast<object>()).Single(x => Equals(Get(x, "Key"), Get(phase, "GearProfileKey")));
        Near(Convert.ToDouble(Invoke(Character, "totalEnergyPower")), (double)Get(profile, "EnergyPower"), "deferred AT profile refresh uses native energy power");
        Equal(Invoke(Character, "totalCapEnergy"), Get(profile, "EnergyCap"), "deferred AT profile refresh uses native energy cap");
        Equal(9500L, Get(profile, "EnergyTotal"), "AT material refresh keeps basic reservation and actual energy");
        Equal(20000L, Get(profile, "MagicTotal"), "high magic cap alone does not generate actual magic");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "no W energy before original AT gate");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "no W magic before original AT gate");
        CheckSelectedPolicyTelemetry(run);
        var levels = (long[])Get(Get(Character, "advancedTraining"), "level"); levels[3] = (long)targetE;
        ScheduledRefresh(run); Equal(0, Get(run, "ReplanCount"), "only one AT gate reached still cannot re-search");
        levels[4] = (long)targetM; ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "equipment search succeeds after original AT gates are satisfied");
        Equal(1, Get(run, "ReplanCount"), "deferred material change invokes one fresh remaining-budget search");
        Equal("material-equipment-change", Get(run, "ReplanReason"), "deferred equipment cause survives until safe gate completion");
        Equal(false, Get(run, "EquipmentReplanPending"), "successful equipment replan clears deferral");
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "completed AT preparation is never repeated");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "safe AT continuation keeps original global deadline");
        Equal(true, ReferenceEquals(restore, Get(run, "Capture")), "AT continuation keeps immutable restoration capture");
        CallMain("EndCombatPrep", "cancelled", "equipment regression cleanup", true);
    }

    private static void CheckManualEquipmentStaleness()
    {
        foreach (var timing in new[] { "preview", "running" })
        {
            Setup(); var head = Get(Get(Character, "inventory"), "head");
            SetEnum(head, "spec1Type", "EnergyPower"); Set(head, "spec1Cap", 1000000f); Set(head, "spec1Cur", 1f); RefreshBonuses();
            var preview = Preview(10); var id = JsonValue(preview, "previewId"); var candidate = FindCandidate(preview, "ATThenWandoos");
            if (timing == "running") Start(id, candidate);
            MergeNativeEquipment(head, 1000f);
            if (timing == "preview") Reject(() => Start(id, candidate), "manual exact cached policy still rejects native stat changes before start");
            else
            {
                var run = Get(MainInstance, "_combatPrepRun"); ScheduledRefresh(run);
                Equal("gear", Get(run, "BlockedDomain"), "manual exact execution yields unexplained same-instance stat changes");
                Equal(true, ((string)Get(run, "BlockedReason")).Contains("without an owned action"), "manual stale-policy error is retained");
                Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "unreviewed material cannot admit later W progression");
                Equal(0, Get(run, "ReplanCount"), "manual selected policy is never silently replanned");
            }
        }
    }

    private static void CheckAutomaticEquipmentMapAndRestorationOwnership()
    {
        var plan = ScheduledSetup(); var run = StartScheduled(plan);
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
        var replacement = Blank(head.GetType()); Set(replacement, "id", Get(head, "id")); SetEnum(replacement, "type", "Head");
        Set(replacement, "bossRequired", 1); Set(replacement, "removable", true); Set(inventory, "head", replacement); RefreshBonuses();
        ScheduledRefresh(run);
        Equal("cancelled", Get(run, "Status"), "same-ID manual map replacement still cancels automatic execution");
        Equal(true, ReferenceEquals(replacement, Get(inventory, "head")), "material handling never adopts or overwrites manual replacement");
        Equal(0, Get(run, "ReplanCount"), "manual map edits are not material-equipment replans");

        object originalMap; run = CleanupRecoveryFixture(out plan, out originalMap);
        inventory = Get(Character, "inventory"); var originalHead = Get(((Array)originalMap).GetValue(0), "equipment");
        var bag = (IList)Get(inventory, "inventory"); var index = Enumerable.Range(0, bag.Count).Single(i => ReferenceEquals(bag[i], originalHead));
        bag[index] = Blank(originalHead.GetType()); ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "loss of historical reporting gear does not create a restoration obligation");
        Equal(true, Get(run, "SetupVerified"), "still-selected phase setup remains independently verified");
        Equal(true, ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "historical reference loss cannot force reacquisition or renew the budget");
        CallMain("EndCombatPrep", "cancelled", "equipment regression cleanup", true);
    }

    private static void CheckSelectedPolicyTelemetry(object run)
    {
        var w = Get(Character, "wandoos98"); Set(w, "energyProgress", .25f); Set(w, "magicProgress", .75f);
        var current = ScheduledPhase(run);
        if ((string)Get(current, "Kind") == "Augments")
        {
            var aug = ((Array)Get(Get(Character, "augments"), "augs")).GetValue((int)Get(current, "AugmentTierId"));
            Set(aug, "augProgress", .375f); Set(aug, "upgradeProgress", .625f);
        }
        var recalls = Recalls; var swaps = Swaps;
        CallMain("CacheCombatPrepRuntime"); var state = CallMain("GetCombatPrepRuntime");
        var phases = (Array)Get(Get(run, "Policy"), "Phases");
        var rows = ((IEnumerable)Get(Json(state, "phases"), "Children")).Cast<object>().ToArray();
        Equal(phases.Length, rows.Length, "runtime publishes complete selected policy, not a preview surrogate");
        Equal(true, rows.Length <= 3, "runtime phase telemetry has fixed planner bound");
        for (var i = 0; i < rows.Length; i++)
        {
            var phase = phases.GetValue(i); var row = rows[i];
            Equal(Get(phase, "Kind"), JsonValue(row, "kind"), "selected phase kind is exact");
            Equal(Get(phase, "GearProfileKey"), JsonValue(row, "gearProfileKey"), "selected phase profile key is exact");
            foreach (var field in new[] { "AugmentTarget", "UpgradeTarget", "AtEnergyTarget", "AtMagicTarget" })
            {
                var key = char.ToLowerInvariant(field[0]) + field.Substring(1);
                Equal(true, Get(Json(row, key), "IsString"), "executor target is an exact decimal string");
                Equal(Convert.ToString(Get(phase, field), CultureInfo.InvariantCulture), JsonValue(row, key), "serialized target matches actual executable gate");
            }
            Near((double)Get(phase, "DurationSeconds"), (double)Get(Json(row, "scheduledEndElapsedSeconds"), "AsDouble") - (double)Get(Json(row, "scheduledStartElapsedSeconds"), "AsDouble"), "selected absolute phase window matches policy budget");
        }
        Equal(Get(run, "PhaseIndex"), Get(Json(state, "phaseIndex"), "AsInt"), "runtime identifies acquired phase index");
        Equal(Get(run, "EquipmentReplanPending"), Get(Json(state, "equipmentReplanPending"), "AsBool"), "runtime distinguishes deferred material search");
        Near(.25, (double)Get(Json(Json(state, "actual"), "wandoosEnergyProgress"), "AsDouble"), "actual W partial progress is reported");
        if ((string)Get(current, "Kind") == "Augments") Near(.375, (double)Get(Json(Json(state, "actual"), "augmentProgress"), "AsDouble"), "actual augment partial progress is reported");
        Equal(true, Encoding.UTF8.GetByteCount(state.ToString()) < 32768, "selected policy telemetry stays small and bounded");
        Equal(recalls, Recalls, "policy telemetry never recalls resources"); Equal(swaps, Swaps, "policy telemetry never equips gear");
        Set(w, "energyProgress", 0f); Set(w, "magicProgress", 0f); // Do not simulate foreign W work in the AT gate case.
    }

    private static void CheckEquipmentChangesDoNotMaskContextFailures()
    {
        var plan = ScheduledSetup(); var run = StartScheduled(plan);
        var head = Get(Get(Character, "inventory"), "head"); Set(head, "level", (int)Get(head, "level") + 1);
        Set(Get(Character, "wandoos98"), "bootupTime", null);
        ScheduledRefresh(run);
        Equal("failed", Get(run, "Status"), "a material change does not swallow unrelated native context failure");
        Equal(true, ((string)Get(run, "Reason")).Contains("native Wandoos bootup clock"), "original context failure remains explicit");
        Equal(0, Get(run, "ReplanCount"), "unrelated exceptions are not reinterpreted as recoverable equipment staleness");
    }
}
