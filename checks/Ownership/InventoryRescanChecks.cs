using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class OwnershipChecks
{
    static Action InventoryHeadRendered, InventoryItemRendered, InventoryTooltipRendered, InventoryRandomReading, InventoryResultObserver;
    static readonly List<object> InventoryResults = new List<object>();
    static int InventoryNativeCalls;
    static object InventoryManager;
    static object InventoryController => G(Character, "inventoryController");
    static IList InventoryBag => (IList)G(G(Character, "inventory"), "inventory");

    // Runner integration: call this at the end of PrepareGame, after the native
    // head method has been retained, and before LoadFixtureGame's scene adapters.
    static void ConfigureInventoryRescanGameFixture(ModuleDefinition module)
    {
        var controller = module.GetType("InventoryController");
        GameBoundary(module, controller.Methods.Single(x => x.Name == "updateHead"), nameof(InventoryHeadPresentation));
        GameBoundary(module, module.GetType("ItemListDisplayController").Methods.Single(x => x.Name == "updateDisplay"), nameof(InventoryHeadPresentation));
        foreach (var swap in controller.Methods.Where(x => x.Parameters.Count == 0 && new[] {
            "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2", "swapAcc"
        }.Contains(x.Name)).ToArray())
        {
            controller.Methods.Add(new MethodDefinition("InventoryNative_" + swap.Name, swap.Attributes, swap.ReturnType) { Body = swap.Body });
            swap.Body = new Mono.Cecil.Cil.MethodBody(swap);
        }
        var update = module.GetType("ItemController").Methods.Single(x => x.Name == "updateItem" && x.Parameters.Count == 0);
        GameBoundary(module, update, nameof(InventoryItemPresentation));
        Constant(module.GetType("Trash").Methods.Single(x => x.Name == "updateItem" && x.Parameters.Count == 0), 0);
        foreach (var instruction in module.GetType("ItemController").Methods.Single(x => x.Name == "consumeItem").Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "HoverTooltip" || call.Parameters.Count != 2
                || call.Name != "showTooltip" && call.Name != "showOverrideTooltip") continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(InventoryTooltipPresentation), All));
        }

        foreach (var instruction in controller.Methods.Single(x => x.Name == "changePage").Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.Object" || call.Name != "op_Equality") continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(InventorySceneObjectsEqual), All));
        }
        foreach (var instruction in controller.Methods.Where(x => x.Name == "degradeBoost").SelectMany(x => x.Body.Instructions))
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.Random" || call.Name != "get_value") continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(InventoryRandomValue), All));
        }
    }

    // Runner integration: call from PreparePlugin. All selection, finite-action,
    // classification and native accounting bodies remain production implementations.
    static void ConfigureInventoryRescanPluginFixture(ModuleDefinition module)
    {
        var main = module.GetType("NGUInjector.Main");
        var native = main.NestedTypes.Single(x => x.Name == "InventoryLeafAction").Methods.Single(x => x.Name == "Invoke");
        foreach (var instruction in native.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "InventoryController" || !new[] {
                "swapHead", "swapChest", "swapLegs", "swapBoots", "swapWeapon", "swapWeapon2", "swapAcc"
            }.Contains(call.Name)) continue;
            instruction.Operand = new MethodReference(call.Name == "swapHead" ? "OwnershipNativeSwapHead" : "InventoryNative_" + call.Name,
                call.ReturnType, call.DeclaringType) { HasThis = true };
        }
        native.Body.GetILProcessor().InsertBefore(native.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(InventoryNativeInvoked), All))));
        var leaf = main.Methods.Single(x => x.Name == "RunInventoryLeaf");
        var run = leaf.Body.Instructions.Single(x => (x.Operand as MethodReference)?.Name == "RunFiniteAction");
        var duplicate = Instruction.Create(OpCodes.Dup); leaf.Body.GetILProcessor().InsertAfter(run, duplicate);
        leaf.Body.GetILProcessor().InsertAfter(duplicate, Instruction.Create(OpCodes.Call,
            module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(InventoryResultObserved), All))));
        foreach (var instruction in leaf.Body.Instructions.Where(x => x.Operand is Instruction && x.OpCode.Code.ToString().EndsWith("_S")))
        {
            var name = instruction.OpCode.Code.ToString();
            instruction.OpCode = (OpCode)typeof(OpCodes).GetField(name.Substring(0, name.Length - 2)).GetValue(null);
        }
        leaf.Body.MaxStackSize++;
        foreach (var instruction in main.Methods.Single(x => x.Name == "PredictNativeCubeLeaf").Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.Random" || call.Name != "get_value") continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(InventoryPredictionRandomValue), All));
        }
    }

    public static void InventoryHeadPresentation() { P("RefreshBonuses"); InventoryHeadRendered?.Invoke(); }
    public static void InventoryItemPresentation() { InventoryItemRendered?.Invoke(); }
    public static void InventoryTooltipPresentation(object tooltip, string text, float duration) { InventoryTooltipRendered?.Invoke(); }
    public static bool InventorySceneObjectsEqual(object left, object right) => ReferenceEquals(left, right);
    public static float InventoryRandomValue() { InventoryRandomReading?.Invoke(); return .5f; }
    public static float InventoryPredictionRandomValue() => .5f;
    public static void InventoryNativeInvoked() { InventoryNativeCalls++; }
    public static void InventoryResultObserved(object result) { InventoryResults.Add(result); InventoryResultObserver?.Invoke(); }

    static void SetupInventoryRescan()
    {
        Setup(); InventoryHeadRendered = InventoryItemRendered = InventoryTooltipRendered = InventoryRandomReading = InventoryResultObserver = null;
        InventoryResults.Clear(); InventoryNativeCalls = 0;
        Setting("BoostBlacklist", new int[0]); Setting("SpecialBoostBlacklist", new int[0]); Setting("PriorityBoosts", new int[0]);
        var inv = G(Character, "inventory"); S(Character, "itemInfo", G(InventoryController, "itemInfo"));
        // These fault cases distinguish the first head leaf from later chest work.
        Setting("PriorityBoosts", new[] { (int)G(G(inv, "head"), "id"), (int)G(G(inv, "chest"), "id") });
        S(Child(Character, "allItemList"), "character", Character);
        Child(G(Character, "allItemList"), "itemDisplay"); Child(G(Character, "allItemList"), "tooltip"); Child(Character, "purchases");
        P("ListField", G(inv, "itemList"), "itemMaxxed", 1000);
        P("ListField", G(inv, "itemList"), "itemDropped", 1000);
        P("ListField", G(inv, "itemList"), "itemFiltered", 1000);
        P("ListField", Child(Character, "beastQuest"), "quirkLevel", 300);
        P("ListField", G(Character, "wishes"), "wishes", 300);
        P("ListField", Child(G(Character, "adventure"), "itopod"), "perkLevel", 300);
        S(G(Character, "wishesController"), "character", Character);
        P("ListField", G(Character, "wishesController"), "properties", 300);
        foreach (var name in new[] { "itopod", "beastQuestPerkController" })
        {
            var perks = name == "itopod" ? G(G(Character, "adventureController"), name) : G(Character, name);
            S(perks, "character", Character); P("ListField", perks, "effectPerLevel", 300);
            P("ListField", perks, name == "itopod" ? "perkDifficultyReq" : "quirkDifficultyReq", 300);
        }
        S(G(G(Character, "allChallenges"), "level100Challenge"), "maxCompletions", 10);
        var slots = (Array)P("ArrayField", InventoryController, "inventory", 60);
        for (var i = 0; i < slots.Length; i++)
        {
            var item = slots.GetValue(i); S(item, "id", i); S(item, "character", Character);
            S(item, "inventoryController", InventoryController); S(item, "itemInfo", G(InventoryController, "itemInfo"));
            Child(item, "tooltip"); Child(item, "trash");
        }
        S(Child(Character, "beastQuestController"), "character", Character);
        S(Child(G(Character, "yggdrasil"), "goldFruit"), "maxTier", 1L);
        InventoryManager = Activator.CreateInstance(Manager("InventoryManager"), true); S(MainObject, "_invManager", InventoryManager);
        Equal(1f, Invoke(G(Character, "allItemList"), "boostBonus"), "inventory fixture retains actual zero-purchase native boost bonus");
        Equal(1f, Invoke(G(Character, "wishesController"), "totalBoostRatioDivider"), "inventory fixture retains native zero-level Wish cube ratio");
    }

    static Array ScanInventory()
    {
        var items = ((IEnumerable)Invoke(Plugin.GetType("NGUInjector.Extensions", true), "GetConvertedInventory", G(Character, "inventory"))).Cast<object>().ToArray();
        var result = Array.CreateInstance(Manager("ih"), items.Length);
        for (var i = 0; i < items.Length; i++) result.SetValue(items[i], i);
        return result;
    }
    static object InventoryItem(int id, string type, int level = 0)
    {
        var item = NewGear(id, type); S(item, "level", level); S(item, "curAttack", 0f); S(item, "curDefense", 0f);
        S(item, "capAttack", 0f); S(item, "capDefense", 0f); return item;
    }
    static object PutInventoryBoost(int slot, float amount = 10f)
    {
        var item = InventoryItem(1, "atkBoost"); S(item, "capAttack", amount); InventoryBag[slot] = item; return item;
    }
    static string InventoryBoostScan() => (string)Invoke(InventoryManager, "ManageCustomAutoBoost");
    static void AssertInventoryRescan(Exception error)
    {
        Equal("InventoryRescanRequiredException", error?.GetBaseException().GetType().Name, "uncertainty unwinds the complete outer inventory batch");
    }

    // A retained-selection input, not fabricated application/grant evidence. The
    // real policy constructor, material persistence and native leaves run unchanged.
    static object RetainInventoryPreset(bool autoUpdate, string owner = "dashboard", object stage = null)
    {
        var preset = Invoke(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true), "Parse", P("Parse",
            "{\"Id\":\"inventory-causal\",\"Name\":\"Inventory causal revision\",\"Weights\":[0,0,0,0,0,0,0,0,0,0,1,0,0,0],\"SupportMode\":\"current\",\"SwapSeconds\":0,\"AutoUpdate\":"
            + (autoUpdate ? "true" : "false") + "}"));
        var gear = CapturePitGear(); var slots = gear.Cast<object>().Select(x => (int)G(x, "slot")).ToArray();
        var candidate = Activator.CreateInstance(Manager("PresetCandidate"), true);
        var items = Array.CreateInstance(Manager("PresetItem"), gear.Length);
        var capture = Activator.CreateInstance(MainType.GetNestedType("LoadoutPresetCapture", All), true);
        var instances = (IDictionary)Activator.CreateInstance(capture.GetType().GetField("Instances", All).FieldType);
        for (var i = 0; i < gear.Length; i++)
        {
            var item = Activator.CreateInstance(Manager("PresetItem"), true); var equipment = G(gear.GetValue(i), "equipment");
            S(item, "Id", G(equipment, "id")); S(item, "SourceSlot", slots[i]); S(item, "Name", "selected " + slots[i]);
            S(item, "Group", Invoke(Manager("LoadoutPresetPlanner"), "Group", slots[i]));
            items.SetValue(item, i); instances.Add(slots[i], equipment);
        }
        S(candidate, "Items", items); S(candidate, "Key", "current"); S(candidate, "Label", "Current selected gear");
        var support = Activator.CreateInstance(Manager("PresetSupport"), true);
        S(support, "Beards", new int[0]); S(support, "Diggers", Array.CreateInstance(Manager("SupportDigger"), 0));
        S(candidate, "Support", support);
        var snapshot = Activator.CreateInstance(Manager("PresetSnapshot"), true); S(snapshot, "Slots", slots);
        foreach (var lane in new[] { "EnergyNgu", "MagicNgu" })
        {
            var weights = (int[])G(Settings, lane + "Weights"); var rows = (Array)P("ArrayField", snapshot, lane, weights.Length);
            for (var i = 0; i < rows.Length; i++) S(rows.GetValue(i), "Weight", weights[i]);
        }
        S(capture, "Snapshot", snapshot); S(capture, "Instances", instances); S(capture, "ExecutionPreset", preset); S(capture, "Stage", stage);
        S(capture, "Epoch", new[] { Character, InventoryController, G(Character, "inventory"), G(Character, "NGU"), G(Character, "magic"),
            G(Character, "adventure"), G(Character, "beards"), G(Character, "diggers"), G(Character, "stats") });
        var plan = Activator.CreateInstance(Manager("PresetPlan"), true); var count = ((Array)G(Manager("LoadoutPresetPlanner"), "Keys")).Length;
        S(plan, "Normalization", Enumerable.Repeat(1d, count).ToArray()); S(plan, "ReferenceKeys", Enumerable.Repeat("current", count).ToArray());
        S(plan, "ReferenceProduction", new double?[count]);
        var policy = M("CreateGlobalPresetPolicy", preset, capture, plan, candidate); S(policy, "Owner", owner);
        var retained = (IDictionary)Activator.CreateInstance(MainType.GetField("_retainedPresetSelections", All).FieldType);
        retained.Add(owner, policy); S(MainObject, "_retainedPresetSelections", retained);
        var policies = (IDictionary)Activator.CreateInstance(MainType.GetField("_globalPresetPolicies", All).FieldType);
        policies.Add("inventory-causal", policy); S(MainObject, "_globalPresetPolicies", policies);
        var receipt = Activator.CreateInstance(MainType.GetNestedType("GlobalPresetReceipt", All), true);
        S(receipt, "Policy", policy); S(receipt, "Id", "inventory-causal"); S(receipt, "Owner", owner);
        S(receipt, "Gear", gear); S(receipt, "Execution", candidate); S(receipt, "Support", M("GlobalPresetSupportState"));
        S(receipt, "AppliedEnergyWeights", ((int[])G(Settings, "EnergyNguWeights")).ToArray());
        S(receipt, "AppliedMagicWeights", ((int[])G(Settings, "MagicNguWeights")).ToArray());
        S(MainObject, "_globalPresetReceipt", receipt); S(MainObject, "_globalPresetManualId", owner == "dashboard" ? "inventory-causal" : null);
        S(MainObject, "_customEquipmentApplicationInvalidated", false); S(G(G(Character, "buttons"), "adventure"), "m_Interactable", true);
        return policy;
    }

    static void CheckInventoryRescans()
    {
        var before = Checks;
        try
        {
            Case(CheckInventoryNoWorkProgress);
            Case(CheckInventoryMergeRescan);
            Case(CheckInventoryDefaultOrderRescan);
            foreach (var reentrant in new[] { false, true }) Case(() => CheckInventoryBoostRescan(reentrant));
            Case(CheckInventoryVerifiedRenderFailure);
            Case(CheckInventoryMaterialStorageFailure);
            foreach (var autoUpdate in new[] { false, true })
            foreach (var retainedOnly in new[] { false, true }) Case(() => CheckInventoryPresetCausalRevision(autoUpdate, retainedOnly));
            Case(CheckInventoryPresetIdentityChange);
            foreach (var fault in new[] { "unknown", "foreign" }) Case(() => CheckInventoryMixedLeafRevisions(fault));
            Case(CheckInventoryCubeRescan);
            Case(CheckInventoryQuestRescan);
            Case(CheckInventoryConvertibleRescan);
            Case(CheckInventoryPageIdentityChange);
            Case(CheckInventoryPageReturnChange);
            Case(CheckInventoryRetainedConsumable);
            Case(CheckInventoryRetainedDonor);
            foreach (var change in new[] { "bar", "gear", "epoch", "input" }) Case(() => CheckInventoryForeignCallback(change));
        }
        finally { InventoryHeadRendered = InventoryItemRendered = InventoryTooltipRendered = InventoryRandomReading = InventoryResultObserver = null; }
        Console.WriteLine("Inventory rescans: " + (Checks - before) + " native assertions; no persistent action-history authority.");
    }

    static void CheckInventoryNoWorkProgress()
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); var head = G(inv, "head"); var chest = G(inv, "chest");
        S(head, "capDefense", 100f); S(chest, "capAttack", 100f); PutInventoryBoost(0);
        InventoryBoostScan();
        Equal(0f, G(head, "curDefense"), "inapplicable power boost leaves first toughness-only target alone");
        Equal(10f, G(chest, "curAttack"), "NoWork does not starve later eligible equipped target");
        Equal(1, InventoryNativeCalls, "only the eligible target enters the native leaf");
        Equal(0, G(InventoryBag[0], "id"), "native eligible boost is actually consumed");
    }

    static void CheckInventoryMergeRescan()
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); var head = G(inv, "head"); var chest = G(inv, "chest");
        S(head, "level", 99); InventoryBag[0] = InventoryItem((int)G(head, "id"), "Head");
        var donor = InventoryItem((int)G(chest, "id"), "Chest"); InventoryBag[1] = donor;
        var snapshot = ScanInventory(); var later = false;
        InventoryHeadRendered = () => {
            InventoryHeadRendered = null;
            InventoryBag[3] = donor; InventoryBag[1] = InventoryItem(0, "Accessory");
            throw new InvalidOperationException("merge renderer changed the remaining inventory map");
        };
        AssertInventoryRescan(Failure(() => { Invoke(InventoryManager, "MergeEquipped", snapshot); later = true; }));
        Equal(false, later, "uncertain merge does not return to later outer-batch work");
        Equal(100, G(head, "level"), "actual native first merge remains applied before item-list rendering");
        Equal(0, G(chest, "level"), "unverified merge stops before second equipped target");
        Equal(1, InventoryNativeCalls, "partial batch contains exactly one native merge");
        Equal("UnknownOutcome", G(InventoryResults.Single(), "Disposition").ToString(), "foreign slot move is this call's typed unknown outcome");
        Invoke(InventoryManager, "MergeEquipped", ScanInventory());
        Equal(1, G(chest, "level"), "fresh scan can merge the next target after unknown predecessor");
        Equal(0, G(InventoryBag[3], "id"), "fresh leaf consumes donor at its actual new slot");
        Equal(2, InventoryNativeCalls, "fresh scan does not replay the already-consumed first donor");
    }

    static void CheckInventoryBoostRescan(bool reentrant)
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); var head = G(inv, "head"); var chest = G(inv, "chest");
        S(head, "capAttack", 100f); S(chest, "capAttack", 100f); PutInventoryBoost(0); var remaining = PutInventoryBoost(1);
        Equal(true, ReferenceEquals(head, G(((Array)Invoke(InventoryManager, "GetCustomBoostSlots", ScanInventory())).GetValue(0), "equipment")),
            "uncertain-result fixture really targets head before later chest work");
        string nested = null;
        InventoryRandomReading = () => {
            InventoryRandomReading = null;
            if (reentrant) nested = InventoryBoostScan();
            S(head, "curAttack", 11f); S(head, "capAttack", 11f);
            InventoryBag[3] = remaining; InventoryBag[1] = InventoryItem(0, "Accessory");
            throw new InvalidOperationException("native boost callback changed target and donor map");
        };
        Equal("Auto boost skipped: inventory rescan required.", InventoryBoostScan(), "unverified boost exits top-level scan");
        Equal(1, InventoryNativeCalls, "unverified boost does not continue to another target or cube");
        Equal(0f, G(chest, "curAttack"), "later target is untouched in the uncertain pass");
        Equal(true, ReferenceEquals(remaining, InventoryBag[3]), "uncertain pass preserves the moved exact donor");
        if (reentrant) Equal("Auto boost skipped: inventory scan active.", nested, "nested callback cannot begin or reset the outer scan");
        InventoryBoostScan();
        Equal(10f, G(chest, "curAttack"), "next ordinary top-level scan progresses on current material");
        Equal(0, G(InventoryBag[3], "id"), "next scan consumes the actual new donor slot");
        Equal(2, InventoryNativeCalls, "unknown inventory outcome never bans a future action type");
    }

    static void CheckInventoryDefaultOrderRescan()
    {
        SetupInventoryRescan(); Setting("PriorityBoosts", new int[0]);
        var inv = G(Character, "inventory"); var head = G(inv, "head"); var chest = G(inv, "chest");
        S(head, "capAttack", 100f); S(chest, "capAttack", 100f); PutInventoryBoost(0); var remaining = PutInventoryBoost(1);
        var targets = (Array)Invoke(InventoryManager, "GetCustomBoostSlots", ScanInventory());
        Equal(-2, G(targets.GetValue(0), "slot"), "default armor order selects chest before head when no explicit priority exists");
        Equal(true, ReferenceEquals(chest, G(targets.GetValue(0), "equipment")), "default first target is the exact current chest");
        InventoryRandomReading = () => {
            InventoryRandomReading = null;
            Equal(10f, G(chest, "curAttack"), "native first call really boosts chest before the external callback");
            Equal(0f, G(head, "curAttack"), "native first chest call has not boosted head");
            S(chest, "curAttack", 11f); S(chest, "capAttack", 11f);
            InventoryBag[3] = remaining; InventoryBag[1] = InventoryItem(0, "Accessory");
            throw new InvalidOperationException("default-first chest callback changed material and donor slots");
        };
        Equal("Auto boost skipped: inventory rescan required.", InventoryBoostScan(), "default-order uncertainty exits the entire scan");
        Equal(1, InventoryNativeCalls, "uncertain default-first leaf enters native code only once");
        Equal(false, G(InventoryResults.Single(), "Verified"), "default-first callback produces an actual unverified native result");
        Equal(0f, G(head, "curAttack"), "unverified chest leaf stops before later head target");
        Equal(true, ReferenceEquals(remaining, InventoryBag[3]), "unverified default-order scan preserves moved donor");
        InventoryBoostScan();
        Equal(10f, G(head, "curAttack"), "fresh scan selects the remaining eligible head from actual new material");
        Equal(11f, G(chest, "curAttack"), "fresh scan never replays the earlier chest effect");
        Equal(0, G(InventoryBag[3], "id"), "fresh default-order scan consumes donor at its actual moved slot");
        Equal(2, InventoryNativeCalls, "fresh default-order scan progresses without a permanent unknown ban");
    }

    static void CheckInventoryVerifiedRenderFailure()
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); var head = G(inv, "head"); var chest = G(inv, "chest");
        S(head, "capAttack", 10f); S(chest, "capAttack", 10f); PutInventoryBoost(0); PutInventoryBoost(1);
        InventoryRandomReading = () => { InventoryRandomReading = null; throw new InvalidOperationException("external RNG failed after exact native boost/deletion"); };
        InventoryBoostScan();
        Equal("ModelAppliedRenderFailed", G(InventoryResults[0], "Disposition").ToString(), "exact native model proof confirms committed exception");
        Equal(true, G(InventoryResults[0], "Invoked"), "verified failure describes this actual native invocation");
        Equal(true, G(InventoryResults[0], "Verified"), "render/input failure is not an unknown model result");
        Equal(10f, G(head, "curAttack"), "first exact target retains native boost");
        Equal(10f, G(chest, "curAttack"), "verified mutation can safely progress to next exact target");
        Equal(2, InventoryNativeCalls, "confirmed first effect is never replayed");
        Equal(false, Directory.Exists(Path.Combine((string)G(MainType, "_dir"), "finite-actions-v1")), "inventory work creates no per-action disk ledger");
    }

    static void CheckInventoryCubeRescan()
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); PutInventoryBoost(0); var donor = PutInventoryBoost(1);
        InventoryRandomReading = () => {
            InventoryRandomReading = null;
            S(inv, "cubePower", (float)G(inv, "cubePower") + .25f);
            InventoryBag[3] = donor; InventoryBag[1] = InventoryItem(0, "Accessory");
            throw new InvalidOperationException("native cube callback changed inventory and cube accounting");
        };
        Equal("Auto boost skipped: inventory rescan required.", InventoryBoostScan(), "partial cube result exits full scan");
        Equal(1, InventoryNativeCalls, "uncertain cube does not consume a second boost");
        var power = (float)G(inv, "cubePower"); InventoryBoostScan();
        Equal(power + 10f / 100f, G(inv, "cubePower"), "fresh cube prediction uses actual post-failure native accounting");
        Equal(0, G(InventoryBag[3], "id"), "fresh cube scan consumes the new donor identity/location");
        Equal(2, InventoryNativeCalls, "cube unknown does not persist across scans");
    }

    static void CheckInventoryMaterialStorageFailure()
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); var head = G(inv, "head"); var chest = G(inv, "chest");
        S(head, "capAttack", 10f); S(chest, "capAttack", 10f);
        var plan = StagePlan(); var stage = ((Array)G(plan, "Stages")).GetValue(0); S(stage, "PresetId", "inventory-causal");
        M("InitializeProtectedStageProgress", plan);
        var scheduler = G(MainObject, "_protectedScheduler");
        Invoke(scheduler, "Update", plan, (double)G(G(Character, "rebirthTime"), "totalseconds"), 0d, false, (Func<int, bool>)(_ => false));
        Equal(0, G(scheduler, "StageIndex"), "metadata regression enters an actual scheduler row without granting training credit");
        var policy = RetainInventoryPreset(false, (string)G(MainObject, "ProtectedPresetOwner"), stage);
        var materialWitness = (string[])G(policy, "Material"); var originalMaterial = materialWitness.ToArray();
        S(MainObject, "_globalPresetReceipt", null); // The selected owner can be paused without losing its metadata obligation.
        PutInventoryBoost(0); var remaining = PutInventoryBoost(1);
        var directory = (string)G(MainType, "_dir"); var blocked = Path.Combine(directory, "inventory-material-not-directory");
        File.WriteAllText(blocked, "not a directory");
        try
        {
            S(MainType, "_dir", blocked); M("RunCustomAutoBoost");
            Equal(true, G(InventoryResults[0], "Committed"), "metadata failure does not erase known native commitment");
            Equal(true, G(InventoryResults[0], "Verified"), "metadata persistence remains separate from native verification");
            Equal(true, G(policy, "MaterialPersistencePending"), "paused selected policy retains its approved material write for retry");
            Check(materialWitness.SequenceEqual(originalMaterial), "failed metadata save cannot rewrite the earlier acquisition-material witness");
            Check(!ReferenceEquals(materialWitness, G(policy, "Material")), "verified native effect publishes replacement material even while storage is pending");
            Equal(1, InventoryNativeCalls, "metadata failure stops the current inventory batch without replay");
            Equal(true, ReferenceEquals(remaining, InventoryBag[1]), "metadata failure leaves later donor untouched");
            Check(Failure(() => M("RunCustomAutoBoost")) != null, "unwritable pending metadata remains a storage-only retry");
            Equal(1, InventoryNativeCalls, "failed metadata retry cannot enter native inventory work");
        }
        finally { S(MainType, "_dir", directory); }
        M("RunCustomAutoBoost");
        Equal(false, G(policy, "MaterialPersistencePending"), "next ordinary callback retries paused selected-material storage");
        Equal(1, InventoryNativeCalls, "successful metadata retry performs zero native inventory effects");
        Equal(10f, G(head, "curAttack"), "metadata retry cannot replay committed boost");
        Check(materialWitness.SequenceEqual(originalMaterial), "metadata-only retry retains immutable earlier acquisition material");
        Equal(true, ReferenceEquals(remaining, InventoryBag[1]), "metadata-only retry does not consume a later donor");
        M("RunCustomAutoBoost");
        Equal(2, InventoryNativeCalls, "later fresh scan progresses only after selected metadata has saved");
        Equal(10f, G(chest, "curAttack"), "post-storage scan applies the remaining donor to the next target");
        Equal(10f, G(head, "curAttack"), "post-storage scan never replays the earlier head boost");
    }

    static void CheckInventoryPresetCausalRevision(bool autoUpdate, bool retainedOnly)
    {
        SetupInventoryRescan(); var head = G(G(Character, "inventory"), "head"); S(head, "capAttack", 10f);
        var policy = RetainInventoryPreset(autoUpdate); var materialWitness = (string[])G(policy, "Material"); var before = materialWitness.ToArray();
        Equal(true, M("GlobalPresetReceiptValid", "dashboard", false, true), "retained preset fixture passes real current receipt observations");
        if (retainedOnly) { S(MainObject, "_globalPresetReceipt", null); S(MainObject, "_globalPresetManualId", null); }
        PutInventoryBoost(0); M("RunCustomAutoBoost");
        Equal(true, G(InventoryResults.Single(), "Verified"), "causal revision follows a real verified native leaf");
        var approved = (string[])G(policy, "Material");
        Check(!before.SequenceEqual(approved), "verified leaf advances retained approved material");
        Check(materialWitness.SequenceEqual(before), "verified leaf never retrospectively edits an acquired material witness");
        Check(!ReferenceEquals(materialWitness, approved), "verified leaf publishes a separate approved material array");
        var actual = ((Array)G(policy, "Items")).Cast<object>().Select(item => (string)M("LoadoutPresetPrimitiveKey", item));
        Check(actual.SequenceEqual(approved), "retained approved material equals the exact verified native result");
        Equal(autoUpdate && !retainedOnly ? G(policy, "Acquisition") : null, G(MainObject, "_globalPresetSelectionEvent"),
            "only current AutoUpdate policy receives an approved-delta selection event");
        Equal(autoUpdate && !retainedOnly, G(MainObject, "_customEquipmentApplicationInvalidated"),
            "cached and paused retained policies are not granted replacement-search authority");
        M("RunCustomAutoBoost");
        Equal(1, InventoryNativeCalls, "approved material delta does not replay the consumed boost");
        Equal(true, ReferenceEquals(approved, G(policy, "Material")), "no-work scan preserves the current approved material version");
        Equal(true, M("IsRetainedActionEquipment", head), "native boosting retains exact selected-instance protection");
    }

    static void CheckInventoryMixedLeafRevisions(string fault)
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); var head = G(inv, "head"); var chest = G(inv, "chest");
        S(head, "capAttack", 20f); S(chest, "capAttack", 10f);
        M("ApplyCustomFocusGoal", Focus("EnergyNgu"));
        Equal("EnergyNgu", M("GetAppliedCustomFocusGoal"), "mixed-result regression acquires the real focus selection");
        var focus = G(MainObject, "_focusSetupSelection"); var policy = RetainInventoryPreset(true);
        var beforePolicy = ((string[])G(policy, "Material")).ToArray(); var beforeFocus = ((string[])G(focus, "Material")).ToArray();
        PutInventoryBoost(0); PutInventoryBoost(1); var remaining = PutInventoryBoost(2);
        string[] approvedPolicy = null, approvedFocus = null;
        InventoryRandomReading = () => {
            if (InventoryNativeCalls != 2) return;
            InventoryRandomReading = null;
            approvedPolicy = ((string[])G(policy, "Material")).ToArray(); approvedFocus = ((string[])G(focus, "Material")).ToArray();
            if (fault == "unknown") S(head, "curAttack", 21f);
            else S(chest, "curDefense", 1f);
            if (fault == "unknown") { InventoryBag[3] = remaining; InventoryBag[2] = InventoryItem(0, "Accessory"); }
            throw new InvalidOperationException("second native boost returned unapproved material/" + fault);
        };
        M("RunCustomAutoBoost");
        Equal(2, InventoryNativeCalls, "later uncertain result stops complete wrapper batch/" + fault);
        Equal(true, G(InventoryResults[0], "Verified"), "first native leaf remains verified/" + fault);
        Equal(false, G(InventoryResults[1], "Verified"), "later native result cannot approve material/" + fault);
        Equal(fault == "foreign", G(InventoryResults[1], "Committed"), "regression distinguishes unknown model from committed effect with lost authority/" + fault);
        Check(approvedPolicy != null && !approvedPolicy.SequenceEqual(beforePolicy), "first verified leaf actually revises retained policy material/" + fault);
        Check(approvedFocus != null && !approvedFocus.SequenceEqual(beforeFocus), "first verified leaf actually revises acquired focus material/" + fault);
        Check(approvedPolicy != null && ((string[])G(policy, "Material")).SequenceEqual(approvedPolicy),
            "outer finally cannot replace the first leaf's approved policy material/" + fault);
        Check(approvedFocus != null && ((string[])G(focus, "Material")).SequenceEqual(approvedFocus),
            "outer finally cannot retrospectively approve unknown focus material/" + fault);
        var actual = ((Array)G(policy, "Items")).Cast<object>().Select(item => (string)M("LoadoutPresetPrimitiveKey", item));
        Check(!actual.SequenceEqual(approvedPolicy), "regression actually leaves native material outside approved policy/" + fault);
        Equal(null, G(MainObject, "_globalPresetSelectionEvent"), "unknown/foreign current material cannot request AutoUpdate search/" + fault);
        var remainingSlot = fault == "unknown" ? 3 : 2;
        Equal(true, ReferenceEquals(remaining, InventoryBag[remainingSlot]), "uncertain pass preserves exact next donor/" + fault);
        M("RunCustomAutoBoost");
        Equal(3, InventoryNativeCalls, "fresh top-level scan progresses without action-type quarantine/" + fault);
        Equal(0, G(InventoryBag[remainingSlot], "id"), "fresh scan uses actual current donor slot/" + fault);
        Equal(fault == "unknown" ? 21f : 20f, G(head, "curAttack"), "fresh scan does not replay earlier head effects/" + fault);
        Equal(approvedPolicy[0], ((string[])G(policy, "Material"))[0], "fresh work never adopts the unapproved earlier head material/" + fault);
    }

    static void CheckInventoryPresetIdentityChange()
    {
        SetupInventoryRescan(); var head = G(G(Character, "inventory"), "head"); S(head, "capAttack", 10f);
        var policy = RetainInventoryPreset(true); var successor = Guid.NewGuid().ToString("N"); PutInventoryBoost(0);
        InventoryRandomReading = () => { InventoryRandomReading = null; S(policy, "Acquisition", successor); };
        M("RunCustomAutoBoost");
        Equal(true, G(InventoryResults.Single(), "Verified"), "identity change does not erase independently verified native effect");
        Equal(successor, G(policy, "Acquisition"), "callback actually replaced captured preset acquisition identity");
        Equal(null, G(MainObject, "_globalPresetSelectionEvent"), "earlier acquisition cannot request replacement search for successor identity");
        Equal(false, G(MainObject, "_customEquipmentApplicationInvalidated"), "successor identity receives no fabricated application invalidation");
        Equal(1, InventoryNativeCalls, "identity change never replays the native boost");
    }

    static void CheckInventoryQuestRescan()
    {
        SetupInventoryRescan(); var quest = G(Character, "beastQuest");
        S(quest, "inQuest", true); S(quest, "questID", 278); S(quest, "targetDrops", 10);
        InventoryBag[0] = InventoryItem(278, "Misc"); var second = InventoryItem(278, "Misc"); InventoryBag[1] = second;
        InventoryTooltipRendered = () => {
            InventoryTooltipRendered = null;
            InventoryBag[3] = second; InventoryBag[1] = InventoryItem(0, "Accessory");
            throw new InvalidOperationException("native hand-in credited before tooltip/deletion failed");
        };
        AssertInventoryRescan(Failure(() => Invoke(InventoryManager, "ManageQuestItems", ScanInventory())));
        Equal(1, G(quest, "curDrops"), "partial native hand-in is reached but outer batch stops before second item");
        Equal(278, G(InventoryBag[0], "id"), "failed native hand-in has not yet deleted first item");
        Equal(1, InventoryNativeCalls, "only one native quest consumption attempted in uncertain scan");
        Invoke(InventoryManager, "ManageQuestItems", ScanInventory());
        Equal(3, G(quest, "curDrops"), "fresh scan follows actual remaining items, not a permanent consume quarantine");
        Equal(0, G(InventoryBag[3], "id"), "fresh quest hand-in resolves the moved second item");
    }

    static void CheckInventoryConvertibleRescan()
    {
        SetupInventoryRescan(); S(G(Character, "settings"), "yggdrasilOn", true);
        InventoryBag[0] = InventoryItem(92, "Accessory", 100); var second = InventoryItem(92, "Accessory", 100); InventoryBag[1] = second;
        InventoryItemRendered = () => {
            if ((int)G(InventoryBag[0], "id") != 0) return;
            InventoryItemRendered = null; InventoryBag[3] = second; InventoryBag[1] = InventoryItem(0, "Accessory");
            throw new InvalidOperationException("native convertible renderer changed remaining slots");
        };
        AssertInventoryRescan(Failure(() => Invoke(InventoryManager, "ManageConvertibles", ScanInventory())));
        Equal(200L, G(G(Character, "yggdrasil"), "seeds"), "first native seed conversion remains committed");
        Equal(true, ReferenceEquals(second, InventoryBag[3]), "uncertain conversion does not consume second item");
        Equal(1, InventoryNativeCalls, "conversion batch stops after unverified first result");
        Invoke(InventoryManager, "ManageConvertibles", ScanInventory());
        Equal(400L, G(G(Character, "yggdrasil"), "seeds"), "fresh conversion adds only actual remaining seed reward");
        Equal(0, G(InventoryBag[3], "id"), "fresh convertible scan uses moved item slot");
        Equal(2, InventoryNativeCalls, "already-consumed first convertible is not replayed");
    }

    static void CheckInventoryRetainedConsumable()
    {
        SetupInventoryRescan(); S(G(Character, "settings"), "yggdrasilOn", true);
        var retained = InventoryItem(53, "Accessory", 100); InventoryBag[0] = retained;
        InventoryBag[1] = InventoryItem(92, "Accessory", 100);
        Publish(WithGear(Helper(retained, 10000)), "retained-next-selection");
        Invoke(InventoryManager, "ManageConvertibles", ScanInventory());
        Equal(true, ReferenceEquals(retained, InventoryBag[0]), "retained exact selected convertible is never destroyed");
        Equal(53, G(retained, "id"), "retained selected instance cannot transform into its successor");
        Equal(200L, G(G(Character, "yggdrasil"), "seeds"), "retained NoWork does not starve later safe convertible");
        Equal(1, InventoryNativeCalls, "only nonretained convertible enters native consumption");
    }

    static void CheckInventoryPageIdentityChange()
    {
        SetupInventoryRescan(); var original = InventoryItem(278, "Misc"); var replacement = InventoryItem(278, "Misc");
        InventoryBag[0] = original; InventoryBag[1] = InventoryItem(278, "Misc");
        InventoryItemRendered = () => { InventoryItemRendered = null; InventoryBag[0] = replacement; };
        AssertInventoryRescan(Failure(() => Invoke(InventoryManager, "ManageQuestItems", ScanInventory())));
        Equal(0, InventoryNativeCalls, "same-valued object replacement during page rendering rejects before native consume");
        Equal(false, G(InventoryResults.Single(), "Invoked"), "preflight identity rejection is not reported as invocation");
        Equal(true, ReferenceEquals(replacement, InventoryBag[0]), "page preflight never consumes a foreign replacement by stale slot");
        Invoke(InventoryManager, "ManageQuestItems", ScanInventory());
        Equal(2, InventoryNativeCalls, "fresh scan can consume the actual replacement and later quest item");
        Equal(0, G(InventoryBag[0], "id"), "freshly selected replacement is consumed only after a new scan");
    }

    static void CheckInventoryPageReturnChange()
    {
        SetupInventoryRescan(); InventoryBag[0] = InventoryItem(278, "Misc");
        var second = InventoryItem(278, "Misc"); InventoryBag[1] = second;
        InventoryResultObserver = () => {
            InventoryResultObserver = null;
            InventoryItemRendered = () => {
                InventoryItemRendered = null; InventoryBag[3] = second; InventoryBag[1] = InventoryItem(0, "Accessory");
            };
        };
        AssertInventoryRescan(Failure(() => Invoke(InventoryManager, "ManageQuestItems", ScanInventory())));
        Equal(true, G(InventoryResults.Single(), "Verified"), "native consumption was proved before page-return callback");
        Equal(1, InventoryNativeCalls, "foreign page-return callback stops outer batch after known first effect");
        Equal(true, ReferenceEquals(second, InventoryBag[3]), "page return cannot authorize use of earlier converted slots");
        Invoke(InventoryManager, "ManageQuestItems", ScanInventory());
        Equal(0, G(InventoryBag[3], "id"), "next fresh scan resolves actual post-page-return slot");
        Equal(2, InventoryNativeCalls, "page-return rescan does not repeat consumed first item");
    }

    static void CheckInventoryRetainedDonor()
    {
        SetupInventoryRescan(); var head = G(G(Character, "inventory"), "head");
        var selected = InventoryItem((int)G(head, "id"), "Head"); InventoryBag[0] = selected;
        Publish(WithGear(Helper(selected, -1)), "retained-next-head");
        Invoke(InventoryManager, "MergeEquipped", ScanInventory());
        Equal(0, InventoryNativeCalls, "retained exact selected donor never enters native merge");
        Equal(true, ReferenceEquals(selected, InventoryBag[0]), "same-ID selected donor identity survives");
        Equal(0, G(head, "level"), "protecting selected donor leaves current target material unchanged");
    }

    static void CheckInventoryForeignCallback(string change)
    {
        SetupInventoryRescan(); var inv = G(Character, "inventory"); var head = G(inv, "head");
        S(head, "capAttack", 10f); PutInventoryBoost(0); var remaining = PutInventoryBoost(1);
        InventoryRandomReading = () => {
            InventoryRandomReading = null;
            if (change == "bar")
            {
                var skill = ((IList)G(G(Character, "NGU"), "skills"))[0]; S(skill, "energy", (long)G(skill, "energy") + 1);
            }
            else if (change == "gear") S(inv, "head", InventoryItem((int)G(head, "id"), "Head"));
            else if (change == "epoch") S(G(Character, "stats"), "rebirthNumber", 8L);
            else { S(inv, "item1", 901); S(inv, "item2", 902); }
        };
        InventoryBoostScan();
        if (change == "input")
        {
            Equal(901, G(inv, "item1"), "still-owned return preserves newer foreign first native input");
            Equal(902, G(inv, "item2"), "still-owned return preserves newer foreign second native input");
            Equal(true, G(InventoryResults[0], "Verified"), "foreign borrowed inputs do not erase exact native model commitment");
        }
        else
        {
            Equal(1, InventoryNativeCalls, "foreign callback ends outer scan before another native effect/" + change);
            Equal(false, G(InventoryResults[0], "Verified"), "foreign callback cannot lend dependent inventory authority/" + change);
            Equal(true, ReferenceEquals(remaining, InventoryBag[1]), "foreign callback preserves untouched next donor/" + change);
        }
    }
}
