using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class OwnershipChecks
{
    static Action PitRendered, PitInvoking, PitSetupReady;
    static object LastPitResult;
    static string PitText;
    static Exception NativePitError;
    static object PitManager => Manager("MoneyPitManager");
    static IList PitBag => (IList)G(G(Character, "inventory"), "inventory");
    static int EmptyPitSlots => PitBag.Cast<object>().Take(10).Count(x => (int)G(x, "id") == 0);
    static string CheckPitNow() => (string)Invoke(PitManager, "CheckMoneyPit");

    static void PreparePitRendering(ModuleDefinition module)
    {
        Constant(module.GetType("InventoryController").Methods.Single(x => x.Name == "updateItem" && x.Parameters.Count == 1), 0);
        var pit = module.GetType("PitController"); var engage = pit.Methods.Single(x => x.Name == "engage");
        engage.Body.GetILProcessor().InsertBefore(engage.Body.Instructions[0], Instruction.Create(OpCodes.Call, module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(PitInvoked), All))));
        engage.Name = "OwnershipNativeEngage";
        var entry = new MethodDefinition("engage", engage.Attributes, module.TypeSystem.Void); pit.Methods.Add(entry);
        var entryIl = entry.Body.GetILProcessor(); var start = entryIl.Create(OpCodes.Ldarg_0); var end = entryIl.Create(OpCodes.Ret);
        var handler = entryIl.Create(OpCodes.Call, module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(PitFailed), All)));
        entryIl.Append(start); entryIl.Emit(OpCodes.Call, engage); entryIl.Emit(OpCodes.Leave, end); entryIl.Append(handler); entryIl.Emit(OpCodes.Rethrow); entryIl.Append(end);
        entry.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) { TryStart = start, TryEnd = handler, HandlerStart = handler, HandlerEnd = end, CatchType = module.ImportReference(typeof(Exception)) });
        var setters = 0;
        foreach (var method in pit.Methods.Where(x => x.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.UI.Text" || call.Name != "set_text") continue;
            instruction.OpCode = OpCodes.Call; instruction.Operand = module.ImportReference(typeof(OwnershipChecks).GetMethod(nameof(PitRewardText), All)); setters++;
        }
        Check(setters > 0, "only pit text rendering is replaced; native reward and accounting IL remain");
    }
    public static void PitInvoked() { PitCalls++; PitInvoking?.Invoke(); }
    public static void PitFailed(Exception error) { NativePitError = error; }
    public static void PitRewardText(object text, string value) { PitText = value; }
    public static string PitTextRead(object text) => PitText ?? "fixture";
    public static void PitPresentation() { PitRendered?.Invoke(); }
    public static void PitSetupObservation() { PitSetupReady?.Invoke(); }
    public static void PitResultObservation(object result) { LastPitResult = result; }

    static void PrepareNativePitLoot()
    {
        var info = G(G(Character, "inventoryController"), "itemInfo");
        foreach (var field in info.GetType().GetFields(All).Where(x => !x.IsStatic && x.FieldType.IsArray)) field.SetValue(info, Array.CreateInstance(field.FieldType.GetElementType(), 600));
        Invoke(info, "constructItemInfo"); S(info, "character", Character); S(info, "ic", G(Character, "inventoryController"));
        S(Child(Character, "allItemList"), "character", Character);
        var dropped = (IList)P("ListField", G(G(Character, "inventory"), "itemList"), "itemDropped", 600); dropped[67] = true;
        Child(Character, "purchases"); P("ListField", Child(Character, "beastQuest"), "quirkLevel", 100); P("ListField", Child(G(Character, "adventure"), "itopod"), "perkLevel", 150);
        Equal("Accessory", ((Array)G(info, "type")).GetValue(67).ToString(), "real Looty metadata loaded");
    }

    static void CheckPitCurrentSuccessors()
    {
        foreach (var kind in new[] { "none", "all", "random", "two-random", "same-id", "optimal" })
        foreach (var successor in new[] { false, true })
        foreach (var renderFailure in new[] { false, true }) PitCase(() => {
            Setup(); Setting("ManageMagic", false); var inv = G(Character, "inventory");
            var old = G(inv, "head"); S(old, "level", 12);
            var head = PitGear(kind == "same-id" ? (int)G(old, "id") : 510, "Head", 90); var heart = PitGear(119, "Accessory", 20); var b = PitGear(511, "Head", 100);
            PitBag[0] = head; PitBag[1] = heart; PitBag[2] = b;
            if (kind == "optimal") { PitBag[0] = PitGear((int)G(old, "id"), "Head", 0); head = old; }
            var gold = kind == "all" ? 1e13 : kind == "two-random" ? 1e11 : 1e7; S(Character, "realGold", gold);
            if (kind == "none") Script(new[] { 1, 10, 9 });
            else if (kind == "all") Script(new[] { 1, 11, 4 });
            else if (kind == "two-random") Script(new[] { 1, 10, 4 }, new[] { -7, 0, -1 }, new[] { 0, 100, 1 }, new[] { -7, 0, -6 }, new[] { 0, 100, 99 });
            else Script(new[] { 1, 10, 4 }, new[] { -7, 0, -1 }, new[] { 0, 100, 37 });
            var plan = successor ? PreparePitSuccessorPlan(old, b) : null;
            object selection = null, commitment = null; string checkpoint = null;
            var setupObservations = 0;
            PitSetupReady = () => {
                setupObservations++;
                Equal(kind == "none" ? old : head, G(inv, "head"), "production pit request verifies its exact selected head before current B acquisition");
                if (successor) { selection = AdvancePitSuccessor(plan, b); commitment = Invoke(G(MainObject, "_protectedScheduler"), "ExportState"); checkpoint = File.ReadAllText((string)G(MainObject, "ProtectedCheckpointPath")); }
            };
            var seen = new System.Collections.Generic.List<int>(); var recalls = G(typeof(Program), "Recalls"); var swaps = G(typeof(Program), "Swaps");
            Program.NativeGearSwapped = item => seen.Add((int)G(item, "id"));
            PitRendered = () => {
                Equal(1, G(G(Character, "pit"), "tossCount"), "native toss commits before rendering"); Equal(0d, G(Character, "realGold"), "native feed consumes gold before rendering");
                Check(ReferenceEquals(kind == "none" ? old : head, G(inv, "head")), "native reward runs in exact selected instance");
                Equal(kind == "none" ? 12 : kind == "two-random" ? 91 : kind == "optimal" ? 13 : 91, G(kind == "none" ? old : head, "level"), "native reward hits exact head once");
                if (kind == "two-random" || kind == "all") Equal(21, G(heart, "level"), "native equipment reward hits selected heart");
                if (renderFailure) throw new InvalidOperationException("pit model committed; offline renderer failed");
            };
            string result;
            try { result = CheckPitNow(); }
            finally { PitRendered = PitSetupReady = null; Program.NativeGearSwapped = null; }
            Check(result.StartsWith("Money Pit Reward:"), kind + "/" + successor + "/" + renderFailure + " durable native feed succeeds despite optional render failure: " + result
                + "; before=" + G(PitRecord(), "Before") + "; after=" + G(PitRecord(), "After") + "; native=" + NativePitError);
            Equal(1, PitCalls, "actual native engage executes once"); Equal(gold, G(G(Character, "pit"), "totalGold"), "native cumulative gold accounting");
            Equal(1, setupObservations, "real request reaches selected-setup boundary exactly once");
            Equal(true, G(LastPitResult, "Committed"), "native pit result proves actual model commitment");
            Equal(true, G(LastPitResult, "Durable"), "native pit result is stored before reward publication");
            Equal(true, G(LastPitResult, "AuthorityCurrent"), "exact level-only material revision retains scoped current authority");
            Equal(true, G(LastPitResult, "Verified"), "real native leaf has current scoped authority and durable result: " + G(LastPitResult, "Reason"));
            Check(ReferenceEquals(successor ? b : kind == "none" ? old : head, G(inv, "head")), "exit resolves CURRENT B or preserves native gear without successor");
            if (kind != "none" && kind != "optimal") Check(!seen.SkipWhile(x => x != (int)G(head, "id")).Skip(1).Contains((int)G(old, "id")), "exit has no historical A flash");
            Equal(recalls, G(typeof(Program), "Recalls"), "pit preserving claim performs no recalls"); Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support, Route")), "pit retires its real claim");
            if (!successor && (kind == "none" || kind == "optimal")) Equal(swaps, G(typeof(Program), "Swaps"), "already-selected/no-equipment reward has no swaps");
            var writes = G(typeof(Program), "Swaps"); CheckPitNow(); Equal(1, PitCalls, "readiness prevents another feed"); Equal(writes, G(typeof(Program), "Swaps"), "repeat never restores twice");
            var record = PitRecord(); Equal("Committed", G(record, "State").ToString(), "finite ledger stores actual native commitment");
            Equal(renderFailure ? "ModelAppliedRenderFailed" : "AppliedVerified", G(record, "Disposition"), "ledger retains exact model/render disposition");
            if (successor)
            {
                M("RunCustomPlanAutomation", plan);
                Equal(true, M("InlineStageSetupVerified", plan), "current acquired stage verifies exact rewarded shared instances after pit release");
                Check(ReferenceEquals(selection, G(MainObject, "_inlineStageSelection")), "pit continuation retains B selection instead of acquiring a replacement");
                var current = Invoke(G(MainObject, "_protectedScheduler"), "ExportState");
                foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "earned", "completed", "visits" })
                    Equal(P("Json", commitment, field).ToString(), P("Json", current, field).ToString(), "native pit retains acquired B commitment/" + field);
                var saved = P("Parse", checkpoint); var durable = P("Json", saved, "scheduler");
                Equal("1", P("Json", durable, "stage").ToString(), "B was a durable actual scheduler row before native invocation");
                Equal(G(G(MainObject, "_protectedScheduler"), "Entry").ToString(), G(P("Json", durable, "entry"), "Value"), "B durable entry is the same current acquisition");
            }
        });
    }

    static object PreparePitSuccessorPlan(object original, object successor)
    {
        P("SetEnum", original, "spec1Type", "EnergyPower"); S(original, "spec1Cur", 1e8f);
        P("SetEnum", successor, "spec1Type", "MagicPower"); S(successor, "spec1Cur", 1e8f); P("RefreshBonuses");
        var plan = StagePlan(); var a = ((Array)G(plan, "Stages")).GetValue(0);
        var b = Activator.CreateInstance(a.GetType()); S(b, "GearGoal", 3); S(b, "DurationSeconds", 100);
        var stages = Array.CreateInstance(a.GetType(), 2); stages.SetValue(a, 0); stages.SetValue(b, 1); S(plan, "Stages", stages);
        var name = (string)G(Settings, "CustomPlanFile"); var store = G(MainType, "CustomPlanStore"); Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        M("RunCustomPlanAutomation", plan);
        Equal(true, M("InlineStageSetupVerified", plan), "A is an actual acquired saved-plan stage before temporary pit setup");
        Check(ReferenceEquals(original, G(G(Character, "inventory"), "head")), "actual A selects original Energy-power head");
        return plan;
    }

    static object AdvancePitSuccessor(object plan, object successor)
    {
        // Observe the real request after setup and before its grant is resolved.
        // A saved-plan command acquires B; no return map or authority flag is seeded.
        var scheduler = G(MainObject, "_protectedScheduler"); var entry = G(MainObject, "_protectedGeneration") + ":" + G(scheduler, "Entry");
        var args = "{\"name\":\"" + G(Settings, "CustomPlanFile") + "\",\"expectedRevision\":\"" + G(plan, "RuntimeRevision")
            + "\",\"expectedEntry\":\"" + entry + "\",\"action\":\"skip\",\"stageIndex\":0}";
        M("OverrideCustomStage", P("Parse", args)); M("RunCustomPlanAutomation", plan);
        var selection = G(MainObject, "_inlineStageSelection"); var gear = (Array)G(G(selection, "Baseline"), "Gear");
        Check(ReferenceEquals(successor, G(At(gear, -1), "equipment")), "actual scheduler B independently acquires its exact Magic-power head");
        Equal(1, G(scheduler, "StageIndex"), "real override selects B while pit setup remains covered");
        return selection;
    }

    static object PitRecord() => ((Array)G(M("get_FiniteActions"), "Records")).Cast<object>().Single(x => (string)G(G(x, "Identity"), "Kind") == "pit-feed");

    static void CheckPitSetupFailures()
    {
        foreach (var blocker in new[] { "claim", "drag", "full", "partial", "unknown", "epoch" }) PitCase(() => {
            Setup(); Setting("ManageMagic", false); S(Character, "realGold", 1e13); Script(new[] { 1, 11, 4 });
            var inv = G(Character, "inventory"); var head = PitGear(blocker == "full" ? (int)G(G(inv, "head"), "id") : 510, "Head", 90); PitBag[0] = head;
            PitBag[1] = PitGear(512, "Chest", 90); var a = CapturePitGear(); var swaps = (int)G(typeof(Program), "Swaps"); var recalls = G(typeof(Program), "Recalls");
            object foreign = null;
            if (blocker == "claim") foreign = BeginClaim("foreign-titan", "Support");
            if (blocker == "drag") S(G(Character, "inventoryController"), "midDrag", true);
            if (blocker == "full") for (var i = 2; i < PitBag.Count; i++) PitBag[i] = PitGear(80 + i, "Accessory", 100);
            if (blocker == "partial") S(typeof(Program), "FailRefreshAtSwap", swaps + 1);
            if (blocker == "epoch") S(Character, "stats", null);
            var stats = blocker == "epoch" ? null : G(Character, "stats"); var bag = G(inv, "inventory");
            if (blocker == "unknown") Program.NativeGearSwapped = _ => { Program.NativeGearSwapped = null; S(inv, "inventory", null); };
            var failure = Failure(() => CheckPitNow()); Program.NativeGearSwapped = null; S(inv, "inventory", bag);
            if (stats != null) S(Character, "stats", stats);
            Check(failure != null, "setup blocker rejects actual pit request: " + blocker); Equal(0, PitCalls, "unverified setup never enters native engage");
            Equal(recalls, G(typeof(Program), "Recalls"), "blocked setup never recalls resources");
            if (blocker == "partial" || blocker == "unknown")
            { Equal(swaps + 1, G(typeof(Program), "Swaps"), "partial/unknown setup stops at first actual swap"); Check(ReferenceEquals(head, G(inv, "head")), "no historical rollback after failed acquisition"); }
            else { SamePitMap(a, CapturePitGear(), "preflight blocker preserves map"); Equal(swaps, G(typeof(Program), "Swaps"), "rejected setup has no gear effects"); }
            if (foreign != null) { Equal(false, G(foreign, "Ended"), "pit cannot retire another owner's claim"); M("EndExecutionClaim", foreign, false); }
        });
    }

    static void CheckNativePitMilestones()
    {
        foreach (var topology in new[] { "same-id", "empty", "two-empty", "all-empty" })
        foreach (var free in new[] { 0, 1, 2 }) PitCase(() => {
            Setup(); PrepareNativePitLoot(); Setting("ManageMagic", false); var inv = G(Character, "inventory");
            S(Character, "realGold", 1e13); S(G(Character, "pit"), "totalGold", 1e11); S(G(Character, "pit"), "tier3TRewarded", false); Script(new[] { 1, 11, 4 });
            for (var i = 0; i < PitBag.Count; i++) PitBag[i] = PitGear(1, "atkBoost");
            var accs = (IList)G(inv, "accs");
            if (topology == "same-id") PitBag[0] = PitGear((int)G(G(inv, "head"), "id"), "Head", 90);
            else
            {
                accs[0] = PitGear(0, "Accessory"); PitBag[0] = PitGear(119, "Accessory", 20);
                if (topology != "empty") { accs[1] = PitGear(0, "Accessory"); PitBag[1] = PitGear(129, "Accessory", 30); }
                if (topology == "all-empty")
                    foreach (var slot in new[] { "head", "chest", "legs", "boots", "weapon" }) S(G(inv, slot), "id", 0);
            }
            for (var i = 10 - free; i < 10; i++) PitBag[i] = PitGear(0, "Accessory");
            var original = CapturePitGear(); var swaps = G(typeof(Program), "Swaps"); var failure = Failure(() => CheckPitNow());
            if (topology == "same-id" && free == 0)
            { Check(failure != null, "same-ID safe staging still requires an empty bag slot"); Equal(0, PitCalls, "full same-ID preflight withholds feed"); Equal(swaps, G(typeof(Program), "Swaps"), "unsafe staging has no mutation"); SamePitMap(original, CapturePitGear(), "full same-ID refusal preserves map"); return; }
            Check(failure == null, "no-successor reward does not invent historical restoration space: " + failure);
            Equal(1, PitCalls, "native cumulative reward engages once"); Equal(true, G(G(Character, "pit"), "tier3TRewarded"), "native Looty granted with real available space");
            Equal(1, PitBag.Cast<object>().Count(x => (int)G(x, "id") == 67), "actual native makeLoot allocates one Looty");
            Equal(false, M("ExecutionOwnsDomain", Domain("Gear, Support")), "completed reward does not retain snapshot authority");
            var map = CapturePitGear(); CheckPitNow(); SamePitMap(map, CapturePitGear(), "no successor preserves rewarded native map even with full bag"); Equal(1, PitCalls, "milestone cannot replay");
        });
    }

    static void CheckPitLedgerFaults()
    {
        foreach (var fault in new[] { "unknown", "result-storage", "prepare-storage", "permission" }) PitCase(() => {
            Setup(); Setting("ManageMagic", false); S(Character, "realGold", 1e7); Script(new[] { 1, 10, 4 }, new[] { -7, 0, -1 }, new[] { 0, 100, 37 });
            var inv = G(Character, "inventory"); var head = PitGear(510, "Head", 90); PitBag[0] = head; FileStream held = null;
            var directory = (string)G(MainType, "_dir");
            if (fault == "prepare-storage") { var file = Path.Combine(directory, "not-directory"); File.WriteAllText(file, "blocked"); S(MainType, "_dir", file); }
            PitInvoking = () => { if (fault == "unknown") PitThrowAt = 0; };
            PitRendered = () => {
                if (fault == "permission") M("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Quiescing"));
                if (fault != "result-storage") return;
                var identity = M("ActionIdentity", "pit-feed", "money-pit", "0", null);
                var hash = Invoke(Plugin.GetType("NGUInjector.JsonFiles", true), "ComputeRevision", System.Text.Encoding.UTF8.GetBytes((string)G(identity, "Key")));
                held = new FileStream(Path.Combine(directory, "finite-actions-v1", hash + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            };
            string result = null;
            try { result = CheckPitNow(); } finally { held?.Dispose(); PitRendered = PitInvoking = null; S(MainType, "_dir", directory); PitThrowAt = -1; }
            Check(!result.StartsWith("Money Pit Reward:"), "unverified finite result withholds dependent success: " + fault + ": " + result);
            Equal(fault == "prepare-storage" ? 0 : 1, PitCalls, "storage/authority fault has exact native invocation count");
            if (fault == "prepare-storage") { Equal(1e7, G(Character, "realGold"), "storage admission failure spends no gold"); return; }
            var record = PitRecord(); Equal(fault == "unknown" ? "Unknown" : "Committed", G(record, "State").ToString(), "real ledger retains native outcome despite fault");
            if (fault == "permission")
            {
                Equal(true, G(LastPitResult, "Committed"), "quiescing callback retains actual native feed commitment");
                Equal(true, G(LastPitResult, "Durable"), "quiescing callback durably records the native result");
                Equal(false, G(LastPitResult, "AuthorityCurrent"), "quiescing cannot approve the native material revision using revoked authority");
                Equal(false, G(LastPitResult, "Verified"), "known commitment cannot lend dependent success after lifecycle revocation");
            }
            if (fault == "unknown")
            {
                Equal("UnknownOutcome", G(LastPitResult, "Disposition").ToString(), "interrupted native reward retains typed unknown outcome");
                Equal(false, G(LastPitResult, "Committed"), "unknown native reward is not promoted to committed");
                Equal(true, G(LastPitResult, "Durable"), "unknown reward evidence is durable before rejection");
                Equal(false, G(LastPitResult, "AuthorityCurrent"), "missing expected level change cannot authorize dependent setup");
            }
            var map = CapturePitGear(); var swaps = G(typeof(Program), "Swaps");
            // Reload durable evidence, not a reset to an uninvoked in-memory action.
            S(MainObject, "_finiteActionLedger", null);
            if (fault == "permission") M("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Running"));
            S(Character, "realGold", 1e7); S(G(G(Character, "pit"), "pitTime"), "totalseconds", 1e9); S(G(Character, "pit"), "tossCount", 0);
            S(G(Character, "pit"), "pitState", NewPitState(0, 111));
            result = null; var replayError = Failure(() => result = CheckPitNow());
            if (fault == "permission" || fault == "unknown")
            {
                Check(replayError?.GetBaseException().GetType().Name == "SelectedSetupAuthorityException", "ordinary re-admission cannot bypass unapproved/unknown native material: " + replayError);
                Equal("gear", G(replayError.GetBaseException(), "Domain"), "re-admission rejects the exact unapproved material domain");
                Equal(fault == "unknown" ? "Unknown" : "Committed", G(PitRecord(), "State").ToString(), "durable native evidence survives rejected re-admission");
                Equal(null, result, "rejected material re-admission publishes no reward result");
            }
            else
            {
                Check(replayError == null, "ledger-only import rejection has no unrelated setup failure/" + fault + ": " + replayError);
                Check(!result.StartsWith("Money Pit Reward:"), "old commitment cannot lend fresh action credit");
            }
            Equal(1, PitCalls, "UNKNOWN/committed import never repeats native feed");
            Equal(1e7, G(Character, "realGold"), "import never spends again"); SamePitMap(map, CapturePitGear(), "import preserves exact current setup"); Equal(swaps, G(typeof(Program), "Swaps"), "import does not manufacture restoration swaps");
            if (fault == "unknown" || fault == "result-storage")
            {
                S(G(Character, "pit"), "tossCount", 17);
                var replacementError = Failure(() => result = CheckPitNow());
                if (fault == "unknown")
                {
                    Check(replacementError?.GetBaseException().GetType().Name == "SelectedSetupAuthorityException", "replacement opportunity cannot bypass unknown material review");
                    Equal("gear", G(replacementError.GetBaseException(), "Domain"), "unknown replacement remains scoped to material review");
                }
                else Check(replacementError == null, "unapproved result storage is blocked by actual ledger dependencies, not an unrelated setup fault: " + replacementError);
                Equal(1, PitCalls, "a replacement native opportunity cannot evade unresolved same-entry pit evidence");
                var replacement = M("ActionIdentity", "pit-feed", "money-pit", "17", null);
                Equal(true, Invoke(M("get_FiniteActions"), "HasUnresolved", replacement), "replacement opportunity is blocked by actual unresolved same-entry ledger evidence");
                Equal(1e7, G(Character, "realGold"), "changed opportunity cannot spend again under unknown history");
                Equal(1, ((Array)G(M("get_FiniteActions"), "Records")).Length, "unknown replacement creates no new action record");
            }
            var healthy = M("ResolveExecutionGrant", "healthy", "current", Domain("Energy"), (Func<bool>)(() => true), null, null); Equal(true, M("ValidateExecutionGrant", healthy, Domain("Energy")), "unresolved pit is scoped, not a global Energy stop");
            var intent = Activator.CreateInstance(Manager("ResourceSetupIntent"), true);
            var desired = (IDictionary)Invoke(Manager("ResourceWriter"), "EmptyDesiredMap", true, false, false);
            ((long[])desired[Enum.Parse(Manager("ResourceTarget"), "EnergyNgu")])[0] = 100;
            S(intent, "Owner", "healthy-current-energy"); S(intent, "Energy", true); S(intent, "Desired", desired);
            var coordinator = MainObject; S(intent, "Authorized", (Func<bool>)(() => ReferenceEquals(MainObject, coordinator)));
            Equal(true, G(M("ReconcileStageResourceSetup", intent), "Verified"), "actual healthy Energy actuator continues despite pit uncertainty");
            Equal(100L, G(((IList)G(G(Character, "NGU"), "skills"))[0], "energy"), "healthy Energy actually funds its declared native target");
            var ledger = G(MainObject, "_finiteActionLedger"); var priorRecord = PitRecord(); M("ObserveCustomRebirthEpoch"); ChangeEpoch("counter"); M("ObserveCustomRebirthEpoch");
            Check(ReferenceEquals(ledger, G(MainObject, "_finiteActionLedger")), "epoch retirement never resets irreversible-action ledger"); Check(ReferenceEquals(priorRecord, PitRecord()), "epoch observation retains exact unresolved/committed evidence");
        });
    }

    static void CheckNativePitMilestonePredicates()
    {
        foreach (var condition in new[] { "first", "second", "floor-ten", "eleven", "filtered", "full", "merge-reserved" }) PitCase(() => {
            Setup(); PrepareNativePitLoot(); var pit = G(Character, "pit"); S(pit, "totalGold", condition == "floor-ten" ? 9e10 : 1e11);
            S(pit, "tier3TRewarded", false); S(G(Character, "settings"), "filterAccessory", condition == "filtered");
            for (var i = 0; i < PitBag.Count; i++) PitBag[i] = PitGear(1, "atkBoost");
            if (condition != "full") PitBag[condition == "merge-reserved" ? 0 : 9] = PitGear(0, "Accessory");
            if (condition == "first") S(pit, "tier1TRewarded", false);
            if (condition == "second") S(pit, "tier2TRewarded", false);
            if (condition == "merge-reserved")
            { S(G(Character, "purchases"), "hasInvMerge", true); Equal(1, Invoke(G(Character, "inventoryController"), "totalInvMergeSlots"), "native merge reservation active"); Equal(false, Invoke(G(Character, "inventoryController"), "freeSpace"), "reserved empty is not native loot space"); }
            Invoke(G(Character, "pitController"), "totalReward");
            Equal(condition == "eleven", G(pit, "tier3TRewarded"), "native cumulative milestone precedence/filter/space: " + condition);
            Equal(condition == "eleven" ? 1 : 0, PitBag.Cast<object>().Count(x => (int)G(x, "id") == 67), "actual makeLoot respects native milestone admission");
            if (condition == "first") Equal(true, G(pit, "tier1TRewarded"), "first native milestone takes precedence");
            if (condition == "second") Equal(true, G(pit, "tier2TRewarded"), "second native milestone takes precedence");
        });
    }
}
