using System;
using System.Collections;

public static partial class GearResourceChecks
{
    private static Action SwapStarted, BonusesRefreshed, InventoryRefreshed;
    private static Action<int> RecallCompleted;
    private static int NativeCapLossLanes;
    private static readonly int[] NativeClampCounts = new int[2];

    private static object Typed(Array gear, int recall = 0, Func<bool> authorized = null)
    {
        return Call(Optimizer, "EquipGearObserved", gear,
            Enum.ToObject(Plugin.GetType("NGUInjector.Managers.GearRecallFootprint", true), recall), false, null, authorized);
    }

    private static void Disposition(string wanted, object result, string scenario)
    {
        Equal(wanted, Get(result, "Disposition").ToString(), scenario + ": " + Get(result, "Reason"));
    }

    private static Array TwoReplacements()
    {
        var bag = (IList)Get(Get(Character, "inventory"), "inventory");
        bag[0] = Equipment(100, "Head"); bag[1] = Equipment(101, "Chest");
        return Gear(bag[0], bag[1]);
    }

    private static void CheckTypedActuator()
    {
        // Fail before the second native operation, after the first of a multi-swap plan.
        Setup(); var gear = TwoReplacements(); var state = State();
        SwapStarted = () => { if (Swaps == 2) throw new InvalidOperationException("second swap failed"); };
        var outcome = Typed(gear);
        Disposition("KnownPartial", outcome, "typed prefix is not an unchanged rejection");
        Equal(2, Get(outcome, "PlannedSwaps"), "whole plan is known");
        Equal(1, Get(outcome, "VerifiedSwaps"), "only first swap verified");
        var operations = (IList)Get(outcome, "Operations");
        Equal(2, operations.Count, "no replay, refresh or continuation after failure");
        Disposition("RejectedUnchanged", operations[1], "failed second operation is observed unchanged");
        Equal(true, Get(operations[1], "BeforeMatches"), "unchanged evidence recorded");
        Equal(true, Get(operations[0], "ExpectedMatches"), "prefix exact evidence recorded");
        Unchanged(state, "partial swap");

        // A native swap may commit its model and then throw in its own rendering.
        Setup(); gear = TwoReplacements(); state = State();
        BonusesRefreshed = () => { if (Swaps == 1) throw new InvalidOperationException("native swap render failed"); };
        outcome = Typed(gear);
        Disposition("KnownPartial", outcome, "commit-then-throw prefix is known partial");
        Equal(1, Get(outcome, "VerifiedSwaps"), "throwing native call's committed map was observed");
        operations = (IList)Get(outcome, "Operations");
        Disposition("ModelAppliedRenderFailed", operations[0], "per-swap committed render failure");
        Equal(true, Get(operations[0], "ExpectedMatches"), "throwing native operation has exact expected evidence");
        Unchanged(state, "native commit-then-throw");

        Setup(); gear = TwoReplacements(); state = State();
        BonusesRefreshed = () => { if (Swaps == 2) throw new InvalidOperationException("last native swap render failed"); };
        outcome = Typed(gear);
        Disposition("ModelAppliedRenderFailed", outcome, "last native swap commit is observed before classifying its exception");
        Equal(2, Get(outcome, "VerifiedSwaps"), "throw after last native swap retains complete model result");
        Equal(2, ((IList)Get(outcome, "Operations")).Count, "native failure does not retry or continue presentation");
        Unchanged(state, "last native commit-then-throw");

        Setup(); gear = TwoReplacements(); state = State();
        InventoryRefreshed = () => { throw new InvalidOperationException("inventory render failed"); };
        outcome = Typed(gear);
        Disposition("ModelAppliedRenderFailed", outcome, "completed exact map survives render failure");
        Equal(2, Get(outcome, "VerifiedSwaps"), "render failure retains full model success");
        Equal(true, Property(outcome, "Verified"), "typed caller can accept verified model without presentation");
        Unchanged(state, "completed-map render failure");

        Setup(); gear = TwoReplacements(); state = State();
        SwapStarted = () => { throw new InvalidOperationException("first swap rejected"); };
        outcome = Typed(gear);
        Disposition("RejectedUnchanged", outcome, "throw-before-first-commit is observed unchanged");
        Equal(0, Get(outcome, "VerifiedSwaps"), "no swap was committed"); Unchanged(state, "first native failure");

        Setup(); gear = TwoReplacements();
        RecallCompleted = lane => { throw new InvalidOperationException("recall render failed"); };
        outcome = Typed(gear, 7);
        Disposition("KnownPartial", outcome, "recall commit-then-throw is not hidden by zero gear swaps");
        operations = (IList)Get(outcome, "Operations");
        Disposition("ModelAppliedRenderFailed", operations[0], "exact recall post-map observed despite throw");
        Equal(0, Swaps, "recall failure stops before gear mutation"); Equal(0, Recalls[1], "recall failure never continues to next lane");

        Setup(); gear = TwoReplacements();
        InventoryRefreshed = () => { Set(Get(Character, "inventory"), "inventory", null); };
        outcome = Typed(gear);
        Disposition("UnknownOutcome", outcome, "unreadable observation never claims unchanged or verified");
        Equal(2, Get(outcome, "VerifiedSwaps"), "unknown final observation retains previously verified swap evidence");

        foreach (var interference in new[] { "gear", "material", "epoch", "epoch-timer", "resource", "resource-entry", "base-cap", "authority" })
        {
            Setup(); gear = TwoReplacements(); state = State(); var allowed = true;
            BonusesRefreshed = () => {
                if (Swaps != 1) return;
                if (interference == "gear") Set(Get(Character, "inventory"), "boots", Equipment(777, "Boots"));
                if (interference == "material") Set(Get(Get(Character, "inventory"), "boots"), "level", 99);
                if (interference == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 1L);
                if (interference == "epoch-timer") Child(Character, "rebirthTime");
                if (interference == "resource") Set(Get(Character, "magic"), "idleMagic", 401L);
                if (interference == "base-cap") Set(Character, "capEnergy", 500L);
                if (interference == "resource-entry")
                {
                    var list = (IList)Get(Get(Character, "NGU"), "skills");
                    var clone = Blank(list[0].GetType()); Set(clone, "energy", Get(list[0], "energy")); list[0] = clone;
                }
                if (interference == "authority") allowed = false;
            };
            outcome = Typed(gear, 0, () => allowed);
            Disposition("ForeignConflict", outcome, "foreign callback " + interference + " stops exact actuator");
            Equal(1, Swaps, "foreign " + interference + " never reaches second swap");
            foreach (var count in Recalls) Equal(0, count, "foreign interference never triggers cleanup recall");
        }

        // Manage* flags are not permission to touch unclaimed/yielded lanes on the typed path.
        foreach (var lane in new[] { 0, 1, 2 })
        {
            Setup(); gear = TwoReplacements(); state = State();
            var before = new[] { State(0), State(1), State(2) };
            outcome = Typed(gear, 1 << lane);
            Disposition("AppliedVerified", outcome, "explicit single-lane footprint " + lane);
            for (var i = 0; i < 3; i++) Equal(i == lane ? 1 : 0, Recalls[i], "only explicitly claimed whole lane recalled");
            for (var i = 0; i < 3; i++) if (i != lane) Equal(before[i], State(i), "every unclaimed resource is preserved, including idle");
            Equal("100,0,0,0,0,0", string.Join(",", (long[])Get(Get(Character, "training"), "_attackEnergy")), "recall retains basic training");
        }
        Setup(); gear = TwoReplacements(); state = State();
        outcome = Typed(gear, 7, () => false);
        Disposition("RejectedUnchanged", outcome, "insufficient footprint authority rejects before writes");
        Equal(0, Swaps, "permission failure never swaps"); Unchanged(state, "permission failure");

        Setup(); gear = TwoReplacements();
        BonusesRefreshed = () => { Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1); };
        outcome = Typed(gear, 1);
        Disposition("ForeignConflict", outcome, "a claimed lane is not permission for an unexpected resource write");
        Equal(1, Swaps, "owned resource interference stops next swap");

        foreach (var lane in new[] { "energy", "magic", "R3" })
        {
            Setup(); var inv = Get(Character, "inventory"); var capItem = Get(inv, "head");
            Spec(capItem, lane == "energy" ? "EnergyCap" : lane == "magic" ? "MagicCap" : "Res3Cap", lane == "R3" ? 1000000000f : 10000f);
            var owner = lane == "energy" ? Character : Get(Character, lane == "magic" ? "magic" : "res3");
            Set(owner, lane == "energy" ? "curEnergy" : lane == "magic" ? "curMagic" : "curRes3", 1500L);
            Set(owner, lane == "energy" ? "idleEnergy" : lane == "magic" ? "idleMagic" : "idleRes3", 1500L);
            RefreshBonuses(); gear = TwoReplacements(); state = State();
            var permitted = lane == "energy" ? 6 : lane == "magic" ? 5 : 3;
            outcome = Typed(gear, permitted);
            Disposition("RejectedUnchanged", outcome, "unclaimed " + lane + " capacity rejects before any recall");
            Equal(0, Swaps, "insufficient capacity rejects whole plan"); Unchanged(state, "unclaimed capacity");
        }

        Setup(); gear = TwoReplacements();
        Spec(Get(Get(Character, "inventory"), "head"), "EnergyCap", 10000f);
        ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0] = 1500L;
        Set(Character, "curEnergy", 2000L); RefreshBonuses(); state = State();
        outcome = Typed(gear, 7);
        Disposition("RejectedUnchanged", outcome, "even full recall footprint cannot discard basic training for capacity");
        Equal(0, Swaps, "basic capacity rejects before swaps"); Unchanged(state, "retained basic capacity");

        Setup(); gear = TwoReplacements(); state = State();
        var loadouts = Plugin.GetType("NGUInjector.Managers.LoadoutManager", true);
        for (var i = 0; i < gear.Length; i++) ItemType.GetProperty("slot", All).SetValue(gear.GetValue(i), -1 - i);
        var wanted = Call(loadouts, "ComposeExactLoadout", Call(loadouts, "CaptureExactLoadout"), gear);
        outcome = Call(loadouts, "ApplyExactLoadoutObserved", wanted,
            Enum.ToObject(Plugin.GetType("NGUInjector.Managers.GearRecallFootprint"), 0), null);
        Disposition("AppliedVerified", outcome, "exact wrapper uses typed actuator");
        Equal(true, Call(loadouts, "ExactLoadoutMatches", wanted), "complete exact requested map applied");
        Unchanged(state, "exact typed no-recall wrapper");
        operations = (IList)Get(outcome, "Operations");
        Equal(true, Call(Get(operations[0], "Expected"), "Matches", Get(operations[0], "Observed")), "earlier detached evidence is not overwritten by subsequent swaps");
        CheckNativeOwnedCapDepletion();
        Console.WriteLine("Typed gear actuator failure/footprint regressions passed.");
    }

    private static Array OwnedCapDrop(bool staging = false)
    {
        Setup();
        // Consistent native accounting: 1,000 E = 950 idle + 50 basic; all M idle.
        foreach (var resource in Resources)
        {
            if (resource.Item2.IndexOf("res3", StringComparison.OrdinalIgnoreCase) >= 0
                || resource.Item2.StartsWith("cur") || resource.Item2.StartsWith("idle")) continue;
            var array = Get(resource.Item1, resource.Item2) as long[];
            if (array != null) Array.Clear(array, 0, array.Length); else Set(resource.Item1, resource.Item2, 0L);
        }
        ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0] = 50L;
        Set(Character, "capEnergy", 100L); Set(Character, "curEnergy", 1000L); Set(Character, "idleEnergy", 950L);
        var magic = Get(Character, "magic"); Set(magic, "capMagic", 100L); Set(magic, "curMagic", 1000L); Set(magic, "idleMagic", 1000L);
        var inv = Get(Character, "inventory"); var capItem = staging ? ((IList)Get(inv, "accs"))[1] : Get(inv, "head");
        Spec(capItem, "EnergyCap", 90000f); SetEnum(capItem, "spec2Type", "MagicCap"); Set(capItem, "spec2Cur", 90000f);
        var lower = Equipment(100, "Head"); ((IList)Get(inv, "inventory"))[0] = lower;
        RefreshBonuses(); Equal(1000L, Cap("energy"), "owned depletion starts at exact E capacity"); Equal(1000L, Cap("magic"), "owned depletion starts at exact M capacity");
        return Gear(staging ? capItem : lower);
    }

    private static void CheckNativeOwnedCapDepletion()
    {
        // Each generator can run independently during a native callback. Both its unchanged
        // and clamped states are exact, not a tolerance or an observed-value adoption.
        for (var nativeLanes = 0; nativeLanes < 4; nativeLanes++)
        {
            var gear = OwnedCapDrop(); var inv = Get(Character, "inventory"); var original = Get(inv, "head");
            var res3 = State(2); NativeCapLossLanes = nativeLanes;
            var outcome = Typed(gear, 3);
            Disposition("AppliedVerified", outcome, "owned E/M, no R3, native depletion mask " + nativeLanes);
            Equal((nativeLanes & 1) != 0 ? 100L : 1000L, Get(Character, "curEnergy"), "native E total is exact");
            Equal((nativeLanes & 1) != 0 ? 50L : 950L, Get(Character, "idleEnergy"), "native E idle debit is exact");
            Equal((nativeLanes & 2) != 0 ? 100L : 1000L, Get(Get(Character, "magic"), "curMagic"), "native M total is exact");
            Equal((nativeLanes & 2) != 0 ? 100L : 1000L, Get(Get(Character, "magic"), "idleMagic"), "native M idle debit is exact");
            Equal((nativeLanes & 1) != 0 ? 1 : 0, NativeClampCounts[0], "real Energy.addEnergy ran only when requested");
            Equal((nativeLanes & 2) != 0 ? 1 : 0, NativeClampCounts[1], "real MagicDisplay.updateMagicBar ran only when requested");
            Equal(50L, ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0], "native loss preserves basic allocation");
            Equal(res3, State(2), "owned E/M depletion preserves every R3 field"); Equal(0, Recalls[2], "no R3 recall authority");
            var operations = (IList)Get(outcome, "Operations");
            var swap = operations[2];
            Equal(true, Get(swap, "ExpectedMatches"), "depleted swap has exact expected evidence");
            Equal(true, Call(Get(swap, "Expected"), "Matches", Get(swap, "Observed")), "depletion evidence retains the verified native state");
            var energy = Get(Character, "curEnergy"); var magic = Get(Get(Character, "magic"), "curMagic");
            outcome = Typed(Gear(original), 3);
            Disposition("AppliedVerified", outcome, "restoring capacity never invents refilled pools");
            Equal(energy, Get(Character, "curEnergy"), "restored E capacity does not refill");
            Equal(magic, Get(Get(Character, "magic"), "curMagic"), "restored M capacity does not refill");
        }

        var staged = OwnedCapDrop(true); var r3 = State(2); NativeCapLossLanes = 3;
        var result = Typed(staged, 3);
        Disposition("AppliedVerified", result, "temporary lower-cap staging depletion is carried through restored final capacity");
        Equal(2, Get(result, "VerifiedSwaps"), "native accessory staging verifies both swaps");
        Equal(1000L, Cap("energy"), "staging restores final E capacity"); Equal(100L, Get(Character, "curEnergy"), "staging does not refill lost E");
        Equal(1000L, Cap("magic"), "staging restores final M capacity"); Equal(100L, Get(Get(Character, "magic"), "curMagic"), "staging does not refill lost M");
        Equal(r3, State(2), "staging preserves unclaimed R3");

        var throwing = OwnedCapDrop(); NativeCapLossLanes = 3;
        BonusesRefreshed = () => { if (Swaps == 1) throw new InvalidOperationException("render failed after real native depletion"); };
        result = Typed(throwing, 3);
        Disposition("ModelAppliedRenderFailed", result, "native depletion and swap commit are verified even after render throw");
        Equal(1, Get(result, "VerifiedSwaps"), "committed depleted map is not replayed");

        foreach (var lowerCapacity in new[] { false, true })
        foreach (var mutation in new[] { "energy", "magic", "energy-idle", "magic-total", "res3", "allocation" })
        {
            var gear = lowerCapacity ? OwnedCapDrop() : TwoReplacementsAfterSetup();
            NativeCapLossLanes = 3;
            var injected = false;
            BonusesRefreshed = () => {
                if (injected || Swaps != 1) return;
                injected = true;
                var lane = mutation.StartsWith("magic") ? "magic" : mutation == "res3" ? "res3" : "energy";
                var owner = lane == "energy" ? Character : Get(Character, lane);
                var total = lane == "energy" ? "curEnergy" : lane == "magic" ? "curMagic" : "curRes3";
                var idle = lane == "energy" ? "idleEnergy" : lane == "magic" ? "idleMagic" : "idleRes3";
                if (mutation != "energy-idle" && mutation != "allocation") Set(owner, total, (long)Get(owner, total) - 1);
                if (mutation != "magic-total") Set(owner, idle, (long)Get(owner, idle) - 1);
                if (mutation == "allocation")
                {
                    var ngu = (IList)Get(Get(Character, "NGU"), "skills"); Set(ngu[0], "energy", 1L);
                }
            };
            result = Typed(gear, 3);
            Disposition("ForeignConflict", result, "manual one-unit " + mutation + " change is foreign, lower capacity = " + lowerCapacity);
            Equal(true, injected, "negative native callback actually ran"); Equal(1, Swaps, "foreign debit is never followed by another swap");
            Equal(0, Recalls[2], "negative callback never grants R3 authority");
            if (lowerCapacity) Equal(1, NativeClampCounts[0], "negative test includes real native E depletion");
        }

        var insufficient = OwnedCapDrop(); var state = State();
        result = Typed(insufficient, 1);
        Disposition("RejectedUnchanged", result, "owned E cannot deplete unclaimed M to fit lower capacity");
        Equal(0, Swaps, "unclaimed M capacity rejects before writes"); Unchanged(state, "unclaimed native depletion rejection");

        var unverifiable = OwnedCapDrop();
        ((IDictionary)Get(Controller, "bonuses"))[Enum.Parse(Game.GetType("specType"), "MagicCap")] = 8f;
        state = State(); result = Typed(unverifiable, 3);
        Disposition("RejectedUnchanged", result, "owned M depletion still requires exact initial projection parity");
        Equal(0, Swaps, "owned projection mismatch rejects before writes"); Unchanged(state, "owned parity rejection");
    }

    private static Array TwoReplacementsAfterSetup()
    {
        Setup(); return TwoReplacements();
    }
}
