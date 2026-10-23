using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static partial class OwnershipChecks
{
    static object Focus(string name) => Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), name);
    static object UnownedMap() => Activator.CreateInstance(MainType.GetNestedType("FocusUnownedAllocations", All), All, null, new[] { Character }, null);

    static void CheckMagicFocus()
    {
        foreach (var manageEnergy in new[] { false, true })
        foreach (var autoRebirth in new[] { true, false })
        foreach (var affordable in new[] { true, false })
        {
            Setup(); Setting("ManageEnergy", manageEnergy); Setting("UseCustomRebirthPlan", true); Setting("AutoRebirth", autoRebirth);
            var energy = (long[])G(G(Character, "advancedTraining"), "energy"); energy[1] = affordable ? 1000 : 15000;
            S(Character, "curEnergy", affordable ? 10000L : 20000L); S(Character, "idleEnergy", affordable ? 8951L : 5000L);
            var hack = ((IList)G(G(Character, "hacks"), "hacks"))[0]; S(hack, "res3", 100L); S(G(Character, "res3"), "idleRes3", 900L);
            if (!affordable)
            {
                var head = G(G(Character, "inventory"), "head"); P("SetEnum", head, "spec1Type", "EnergyCap"); S(head, "spec1Cur", 1e9f);
                var replacement = NewGear(60, "Head"); P("SetEnum", replacement, "spec1Type", "MagicPower"); S(replacement, "spec1Cur", 1e9f);
                ((IList)G(G(Character, "inventory"), "inventory"))[0] = replacement; P("RefreshBonuses");
                var cap = (long)Invoke(Character, "totalCapEnergy"); Check(cap > 15000, "retained Energy is legal before Magic takeover");
                S(Character, "curEnergy", cap); S(Character, "idleEnergy", cap - 15000);
            }
            else
            {
                ((long[])G(G(Character, "training"), "attackEnergy"))[0] = 7; ((long[])G(G(Character, "training"), "defenseEnergy"))[1] = 9;
                var aug = ((Array)G(G(Character, "augments"), "augs")).GetValue(0); S(aug, "augEnergy", 3L); S(aug, "upgradeEnergy", 4L);
                S(G(Character, "wandoos98"), "wandoosEnergy", 5L); S(G(Character, "machine"), "speedEnergy", 6L);
                S(((IList)G(G(Character, "NGU"), "skills"))[2], "energy", 7L); S(((IList)G(G(Character, "wishes"), "wishes"))[3], "energy", 8L);
            }
            var retained = UnownedMap(); var recalls = G(typeof(Program), "Recalls");
            FocusAuthorityFailures.Clear(); ObserveFocusAuthority = true;
            string result; try { result = (string)M("ApplyCustomFocusGoal", Focus("MagicNgu")); } finally { ObserveFocusAuthority = false; }
            Equal(affordable ? 1000L : 15000L, energy[1], "Magic focus preserves actual unowned AT");
            Equal(100L, G(hack, "res3"), "Magic focus preserves R3"); Equal(true, Invoke(retained, "Matches", Character), "Magic focus preserves complete typed unmanaged map");
            Equal(recalls, G(typeof(Program), "Recalls"), "Magic focus does not recall unmanaged Energy");
            if (affordable)
            {
                Equal(Focus("MagicNgu"), M("GetActiveCustomFocusGoal"), "current focus identity: " + result);
                Equal("applied", M("GetCustomFocusStatus"), "legal Magic focus is applied: " + result);
                Equal(0, FocusAuthorityFailures.Count, "native settings callbacks retain actual focus authority");
                Equal(false, G(Settings, "UseCustomRebirthPlan"), "successful focus pauses old plan"); Equal(false, G(Settings, "AutoRebirth"), "successful focus pauses rebirth");
            }
            else
            {
                Check(result.Contains("retained allocations"), "infeasible focus reports capacity before effects: " + result);
                Equal(true, G(Settings, "UseCustomRebirthPlan"), "rejection preserves plan"); Equal(autoRebirth, G(Settings, "AutoRebirth"), "rejection preserves rebirth policy");
                Equal("failed", M("GetCustomFocusStatus"), "no-owner refusal retains failed UI state"); Equal(Focus("MagicNgu"), M("GetActiveCustomFocusGoal"), "failed request remains identified");
            }
        }
        foreach (var lane in new[] { "energy", "R3" })
        foreach (var funded in lane == "energy" ? new[] { 0L, 100L } : new[] { 0L, 100L, 1500L })
        {
            Setup(); Setting("UseCustomRebirthPlan", true); Setting("ManageEnergy", true); Setting("ManageR3", true);
            var inventory = G(Character, "inventory"); var head = G(inventory, "head");
            P("SetEnum", head, "spec1Type", lane == "energy" ? "EnergyCap" : "Res3Cap"); S(head, "spec1Cur", 1e9f);
            var replacement = NewGear(60, "Head"); P("SetEnum", replacement, "spec1Type", "MagicPower"); S(replacement, "spec1Cur", 1e9f);
            ((IList)G(inventory, "inventory"))[0] = replacement; P("RefreshBonuses");
            var owner = lane == "energy" ? Character : G(Character, "res3"); var total = lane == "energy" ? 15000L : 1500L;
            S(owner, lane == "energy" ? "curEnergy" : "curRes3", total); S(owner, lane == "energy" ? "idleEnergy" : "idleRes3", total - funded);
            if (lane == "energy") ((long[])G(G(Character, "advancedTraining"), "energy"))[1] = funded;
            else S(((IList)G(G(Character, "hacks"), "hacks"))[0], "res3", funded);
            var map = UnownedMap(); var recalls = G(typeof(Program), "Recalls"); var swaps = G(typeof(Program), "Swaps");
            var result = (string)M("ApplyCustomFocusGoal", Focus("MagicNgu"));
            Check(result.Contains(lane + " capacity"), "loss of unmanaged total/idle capacity rejects: " + result);
            Equal(true, Invoke(map, "Matches", Character), "capacity rejection preserves every unowned field");
            Equal(total, G(owner, lane == "energy" ? "curEnergy" : "curRes3"), "total pool preserved"); Equal(total - funded, G(owner, lane == "energy" ? "idleEnergy" : "idleRes3"), "idle pool preserved");
            Equal(true, G(Settings, "UseCustomRebirthPlan"), "capacity rejection does not pause plan"); Equal(true, G(Settings, "AutoRebirth"), "capacity rejection does not pause rebirth");
            Equal(recalls, G(typeof(Program), "Recalls"), "capacity preflight never recalls"); Equal(swaps, G(typeof(Program), "Swaps"), "capacity preflight never swaps");
            Check(ReferenceEquals(head, G(inventory, "head")), "capacity refusal preserves exact head");
        }
    }

    static void CheckRejectedFocusHandoff()
    {
        foreach (var previous in new[] { "EnergyNgu", "TimeMachine" })
        {
            Setup(); var inventory = G(Character, "inventory"); var head = G(inventory, "head");
            S(G(G(Character, "buttons"), "wandoos"), "m_Interactable", false); S(G(G(Character, "buttons"), "advancedTraining"), "m_Interactable", false);
            P("SetEnum", head, "spec1Type", "EnergyCap"); S(head, "spec1Cur", 1e9f); P("RefreshBonuses");
            M("ApplyCustomFocusGoal", Focus(previous)); Equal(previous, M("GetAppliedCustomFocusGoal"), "previous owner genuinely acquired");
            var acquired = EnergyAllocations(); Check(acquired["AUG"].Sum() > 0, "real standard policy funds AUG overflow");
            Equal((long)G(Character, "curEnergy"), (long)G(Character, "idleEnergy") + acquired.Values.SelectMany(x => x).Sum(), "acquisition conserves every allocation");
            S(MainObject, "_customFocusAugmentIndex", 2); S(MainObject, "_customFocusAugmentUsesUpgrade", true); S(MainObject, "_customFocusBloodRitualIndex", 4);
            S(MainObject, "_customFocusLastResourceRefresh", DateTime.UtcNow.AddSeconds(-10));
            var candidate = NewGear(60, "Head"); P("SetEnum", candidate, "spec1Type", "MagicPower"); S(candidate, "spec1Cur", 1e9f); ((IList)G(inventory, "inventory"))[0] = candidate;
            var cap = (long)Invoke(Character, "totalCapEnergy"); ((long[])G(G(Character, "advancedTraining"), "energy"))[1] = 15000;
            var seeded = EnergyAllocations(); var allocated = seeded.Values.SelectMany(x => x).Sum(); Check(cap >= allocated, "seeded complete map fits cap");
            S(Character, "curEnergy", cap); S(Character, "idleEnergy", cap - allocated);
            foreach (var pair in acquired.Where(x => x.Key != "AT")) Check(pair.Value.SequenceEqual(seeded[pair.Key]), "AT seed retains " + pair.Key);
            var fields = new[] { "_customRequestedFocusGoal", "_customAppliedFocusGoal", "_customSustainedFocusGoal", "_customFocusStatus", "_focusSetupSelection",
                "_customFocusAugmentIndex", "_customFocusAugmentUsesUpgrade", "_customFocusBloodRitualIndex", "_customFocusEnergyNguWeights", "_customFocusMagicNguWeights", "_customFocusLastResourceRefresh", "_customSustainedFocusLastStatus" };
            var receipt = fields.ToDictionary(x => x, x => G(MainObject, x)); var weights = receipt.Where(x => x.Value is int[]).ToDictionary(x => x.Key, x => ((int[])x.Value).ToArray());
            var settings = new[] { "AutoRebirth", "UseCustomRebirthPlan", "ManualAdventureEnabled", "ManageEnergy", "ManageMagic" }.ToDictionary(x => x, x => G(Settings, x));
            var allocator = Manager("BlackBarAllocator"); var applied = new[] { "AppliedEnergyNguWeights", "AppliedMagicNguWeights" }.ToDictionary(x => x, x => G(allocator, x));
            var retained = UnownedMap(); var idleMagic = G(G(Character, "magic"), "idleMagic"); var recalls = G(typeof(Program), "Recalls"); var swaps = G(typeof(Program), "Swaps");
            var result = (string)M("ApplyCustomFocusGoal", Focus("MagicNgu")); Check(result.Contains("retained allocations"), "takeover rejects infeasible material: " + result);
            foreach (var pair in receipt) Equal(pair.Value, G(MainObject, pair.Key), "rejected takeover retains receipt " + pair.Key);
            foreach (var pair in weights) Check(pair.Value.SequenceEqual((int[])G(MainObject, pair.Key)), "rejected takeover retains weight values");
            foreach (var pair in applied) Check(ReferenceEquals(pair.Value, G(allocator, pair.Key)), "rejected takeover retains allocator receipt");
            foreach (var pair in settings) Equal(pair.Value, G(Settings, pair.Key), "rejected takeover retains " + pair.Key);
            Equal(true, Invoke(retained, "Matches", Character), "read-only rejection retains exact typed map");
            foreach (var pair in seeded) Check(pair.Value.SequenceEqual(EnergyAllocations()[pair.Key]), "read-only rejection retains " + pair.Key);
            Equal(cap, G(Character, "curEnergy"), "rejection retains current Energy"); Equal(cap - allocated, G(Character, "idleEnergy"), "rejection retains accounted idle Energy");
            Equal(idleMagic, G(G(Character, "magic"), "idleMagic"), "rejection retains Magic"); Equal(recalls, G(typeof(Program), "Recalls"), "rejection has no recall"); Equal(swaps, G(typeof(Program), "Swaps"), "rejection has no swap");
            Check(ReferenceEquals(head, G(inventory, "head")), "previous exact head remains");
            S(G(G(Character, "buttons"), previous == "EnergyNgu" ? "ngu" : "brokenTimeMachine"), "m_Interactable", true);
            foreach (var lane in ((IEnumerable)G(G(Character, "NGU"), "skills")).Cast<object>().Concat(((IEnumerable)G(G(Character, "NGU"), "magicSkills")).Cast<object>())) S(lane, "level", 100L);
            M("RunCustomSustainedFocusAllocation", false); Equal(previous, M("GetAppliedCustomFocusGoal"), "previous focus continues after rejection");
            Check((DateTime)G(MainObject, "_customFocusLastResourceRefresh") > (DateTime)receipt["_customFocusLastResourceRefresh"], "healthy owner refreshes");
            Check(previous == "EnergyNgu" ? EnergyAllocations()["NGU"].Sum() > 0 : (long)G(G(Character, "machine"), "speedEnergy") > 0, "continued owner actually funds its feature");
            Equal(cap, (long)G(Character, "idleEnergy") + EnergyAllocations().Values.SelectMany(x => x).Sum(), "continued owner conserves all Energy");
        }
        Setup(); S(G(G(Character, "buttons"), "wandoos"), "m_Interactable", false); S(G(G(Character, "buttons"), "advancedTraining"), "m_Interactable", false);
        M("ApplyCustomFocusGoal", Focus("EnergyNgu")); Setting("ManageMagic", false); S(G(Character, "wandoos98"), "wandoosMagic", 100L); S(G(Character, "magic"), "idleMagic", 19900L);
        Invoke(Character, "removeMostEnergy"); ((long[])G(G(Character, "advancedTraining"), "energy"))[1] = 1000; S(Character, "idleEnergy", 9000L);
        var retainedEnergy = EnergyAllocations(); var refresh = G(MainObject, "_customFocusLastResourceRefresh"); var before = G(typeof(Program), "Recalls");
        var blocked = (string)M("ApplyCustomFocusGoal", Focus("AdvancedTraining")); Check(blocked.Contains("unmanaged magic"), "unmanaged Wandoos blocks AT preparation");
        Equal("EnergyNgu", M("GetAppliedCustomFocusGoal"), "AT refusal retains previous owner"); Equal(100L, G(G(Character, "wandoos98"), "wandoosMagic"), "AT refusal retains external Magic");
        Equal(before, G(typeof(Program), "Recalls"), "AT refusal is read-only"); Equal(refresh, G(MainObject, "_customFocusLastResourceRefresh"), "AT refusal retains refresh receipt");
        foreach (var pair in retainedEnergy) Check(pair.Value.SequenceEqual(EnergyAllocations()[pair.Key]), "AT refusal preserves " + pair.Key);

        foreach (var changed in new[] { false, true })
        {
            Setup(); S(G(G(Character, "buttons"), "wandoos"), "m_Interactable", false); S(G(G(Character, "buttons"), "advancedTraining"), "m_Interactable", false);
            M("ApplyCustomFocusGoal", Focus("EnergyNgu")); var inventory = G(Character, "inventory"); var originalChest = G(inventory, "chest");
            var failedHead = NewGear(60, "Head"); var failedChest = NewGear(61, "Chest");
            foreach (var gear in new[] { failedHead, failedChest }) { P("SetEnum", gear, "spec1Type", "MagicPower"); S(gear, "spec1Cur", 1e9f); }
            ((IList)G(inventory, "inventory"))[0] = failedHead; ((IList)G(inventory, "inventory"))[1] = failedChest;
            S(typeof(Program), "FailRefreshAtSwap", (int)G(typeof(Program), "Swaps") + 1);
            var failure = (string)M("ApplyCustomFocusGoal", Focus("MagicNgu")); Check(failure.Contains("fixture controller refresh failure"), "actual partial application failure surfaced");
            Check(ReferenceEquals(failedHead, G(inventory, "head")) && ReferenceEquals(originalChest, G(inventory, "chest")), "native actuator stops after first of two swaps");
            Equal("failed", M("GetCustomFocusStatus"), "failed takeover reports failed-new state"); Equal(Focus("MagicNgu"), M("GetActiveCustomFocusGoal"), "new intent retained");
            Equal(string.Empty, M("GetAppliedCustomFocusGoal"), "old applied receipt not resurrected"); Equal(Focus("MagicNgu"), G(MainObject, "_customSustainedFocusGoal"), "new sustained intent retained");
            Equal(-1, G(MainObject, "_customFocusAugmentIndex"), "new failure clears stale target pin"); Equal(true, G(MainObject, "_customFocusActivationPending"), "partial setup awaits completion");
            Equal(1, G(MainObject, "_customFocusRetryCount"), "partial setup records first failure"); Check(G(MainObject, "_customFocusActivationContextMatches") is Func<bool> matches && matches(), "actual retry context captured");
            // Drive the deadline explicitly; no assertion depends on wall-clock run speed.
            S(MainObject, "_customFocusNextRetry", DateTime.MaxValue); var map = CapturePitGear(); var resources = P("Resources"); var recalls = G(typeof(Program), "Recalls"); var swaps = G(typeof(Program), "Swaps");
            M("RunCustomSustainedFocusAllocation", false); Equal("failed", M("GetCustomFocusStatus"), "backoff does not apply early"); Equal(resources, P("Resources"), "backoff preserves pools");
            Equal(recalls, G(typeof(Program), "Recalls"), "backoff has no recall"); Equal(swaps, G(typeof(Program), "Swaps"), "backoff has no swap"); SamePitMap(map, CapturePitGear(), "backoff retains partial map");
            S(MainObject, "_customFocusNextRetry", DateTime.MinValue); if (changed) Setting("ManageR3", true);
            M("RunCustomSustainedFocusAllocation", false);
            if (changed)
            {
                Equal("failed", M("GetCustomFocusStatus"), "changed context requires explicit apply"); Equal(DateTime.MaxValue, G(MainObject, "_customFocusNextRetry"), "changed context disables automatic retry");
                Equal(string.Empty, M("GetAppliedCustomFocusGoal"), "changed context grants no applied credit"); Equal(resources, P("Resources"), "changed context leaves pools"); Equal(swaps, G(typeof(Program), "Swaps"), "changed context never swaps");
                M("ApplyCustomFocusGoal", Focus("MagicNgu"));
            }
            Equal("applied", M("GetCustomFocusStatus"), "unchanged retry or explicit apply completes retained intent"); Equal("MagicNgu", M("GetAppliedCustomFocusGoal"), "new owner becomes applied");
            Equal(false, G(MainObject, "_customFocusActivationPending"), "completion clears setup pending"); Equal(0, G(MainObject, "_customFocusRetryCount"), "completion clears retry count"); Equal(DateTime.MinValue, G(MainObject, "_customFocusNextRetry"), "completion clears backoff");
        }
    }

    static void CheckFocusMetadataGear()
    {
        Setup(); var type = Manager("ActivityPolicy"); var policy = G(type, "Current"); object json;
        using (var stream = Plugin.GetManifestResourceStream("NGUInjector.ActivityPolicies.json"))
        using (var reader = new StreamReader(stream)) json = P("Parse", reader.ReadToEnd());
        var row = P("Json", P("Json", json, "focuses"), "MagicNgu");
        row.GetType().GetProperty("Item", new[] { typeof(string) }).SetValue(row, Activator.CreateInstance(Plugin.GetType("SimpleJSON.JSONNumber", true), new object[] { 4d }), new object[] { "gearGoal" });
        var parsed = Invoke(type, "Parse", json.ToString()); var definitions = (IDictionary)G(policy, "focuses"); var original = definitions["MagicNgu"];
        try
        {
            definitions["MagicNgu"] = Invoke(parsed, "FocusDefinition", "MagicNgu"); Equal(4, G(G(M("SetupForFocus", Focus("MagicNgu")), "Stage"), "GearGoal"), "metadata supplies AllNgu goal");
            var inventory = G(Character, "inventory"); var all = G(inventory, "head"); P("SetEnum", all, "spec1Type", "EnergyPower"); S(all, "spec1Cur", 1e9f);
            var magic = NewGear(60, "Head"); P("SetEnum", magic, "spec1Type", "MagicPower"); S(magic, "spec1Cur", 10000f); ((IList)G(inventory, "inventory"))[0] = magic; P("RefreshBonuses");
            var mode = Enum.Parse(Manager("FightScoreMode"), "SqrtPowerToughness");
            foreach (var goal in new[] { "AllNgu", "MagicNgu" })
            {
                var selected = Invoke(Manager("GearOptimizer"), "Optimize", Enum.Parse(Manager("GearOptimizationGoal"), goal), mode);
                Check(((Array)G(selected, "Items")).Cast<object>().Any(x => ReferenceEquals(G(x, "equipment"), goal == "AllNgu" ? all : magic)), "optimizer positive control differs for " + goal);
            }
            M("ApplyCustomFocusGoal", Focus("MagicNgu")); Equal("applied", M("GetCustomFocusStatus"), "metadata-driven focus applies"); Check(ReferenceEquals(all, G(inventory, "head")), "actual equipped map follows metadata, not hardcoded goal");
        }
        finally { definitions["MagicNgu"] = original; }
        Check(ReferenceEquals(original, Invoke(policy, "FocusDefinition", "MagicNgu")), "registry restored after isolated metadata check");
    }

    static Dictionary<string, long[]> EnergyAllocations() => new Dictionary<string, long[]> {
        { "attack", ((long[])G(G(Character, "training"), "attackEnergy")).ToArray() }, { "defense", ((long[])G(G(Character, "training"), "defenseEnergy")).ToArray() },
        { "AT", ((long[])G(G(Character, "advancedTraining"), "energy")).ToArray() },
        { "AUG", ((IEnumerable)G(G(Character, "augments"), "augs")).Cast<object>().SelectMany(x => new[] { (long)G(x, "augEnergy"), (long)G(x, "upgradeEnergy") }).ToArray() },
        { "Wandoos", new[] { (long)G(G(Character, "wandoos98"), "wandoosEnergy") } }, { "TM", new[] { (long)G(G(Character, "machine"), "speedEnergy") } },
        { "NGU", ((IEnumerable)G(G(Character, "NGU"), "skills")).Cast<object>().Select(x => (long)G(x, "energy")).ToArray() },
        { "wishes", ((IEnumerable)G(G(Character, "wishes"), "wishes")).Cast<object>().Select(x => (long)G(x, "energy")).ToArray() } };
}
