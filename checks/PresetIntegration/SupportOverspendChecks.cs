using System;
using System.Collections;

public static partial class PresetIntegrationChecks
{
    private static void CheckSupportOverspend()
    {
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards();
        var all = Get(Character, "allDiggers"); var model = Get(Character, "diggers");
        var rows = (IList)Get(model, "diggers"); var active = (IList)Get(model, "activeDiggers");
        var machine = Get(Character, "machine");
        foreach (var row in (IList)Get(Get(Character, "beards"), "beards"))
        { Set(row, "beardLevel", 0L); Set(row, "permLevel", 0L); }
        Set(rows[0], "maxLevel", 20L); Set(rows[0], "curLevel", 7L);
        ((IList)Get(all, "baseGPSDrain"))[0] = 10d;
        ((IList)Get(all, "consumesGPS"))[0] = true;
        var empty = Selection(new int[0]); var cheaper = Selection(new int[0], 0, 4);
        var pod = ((IList)Get(all, "pods"))[0]; var input = Get(pod, "diggerLevelInput");
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");

        Action overspend = () => {
            Invoke(all, "clearAllActiveDiggers");
            Set(machine, "realBaseGold", 1000d); Set(rows[0], "curLevel", 7L);
            Invoke(all, "activateDigger", 0);
            Equal(true, Get(rows[0], "active"), "overspend fixture activates the current digger through native code while affordable");
            Set(machine, "realBaseGold", 400d);
            Equal(400d, Invoke(Character, "grossGoldPerSecond"), "overspend fixture retains finite nonnegative native gross GPS");
            Equal(640d, Invoke(all, "totalGPSDrain"), "overspend fixture retains the native current drain after income falls");
            Equal(-240d, (double)Invoke(Character, "grossGoldPerSecond") - (double)Invoke(all, "totalGPSDrain"), "overspend fixture has genuinely negative native gross-minus-drain GPS");
            Equal(0d, Invoke(Character, "goldPerSecond"), "native displayed GPS clamps overspending to zero rather than repairing current support");
        };
        Action<object, double, bool, string> reject = (selection, fraction, enforce, reason) => {
            var key = Property(CallMain("ObserveSupportState"), "StableKey"); var receipt = Get(MainInstance, "_supportMutation");
            var calls = PodRefreshCalls; var draft = Property(input, "text"); var id = Get(pod, "id"); var gold = Get(Character, "realGold");
            Throws(() => CallMain("PreflightExactSupport", selection, fraction, enforce), reason, "overspending current preflight rejects " + reason);
            Throws(() => CallMain("ApplyExactSupport", selection, fraction, enforce), reason, "overspending current apply rejects " + reason);
            Equal(key, Property(CallMain("ObserveSupportState"), "StableKey"), "rejected replacement preserves all current levels, lists and flags");
            Check(ReferenceEquals(receipt, Get(MainInstance, "_supportMutation")), "rejected replacement never publishes a mutation receipt");
            Equal(calls, PodRefreshCalls, "rejected replacement never invokes native clear, input or refresh");
            Equal(draft, Property(input, "text"), "rejected replacement preserves the user's input draft");
            Equal(id, Get(pod, "id"), "rejected replacement preserves the input controller ID");
            Equal(gold, Get(Character, "realGold"), "rejected replacement preserves native liquid gold");
        };

        foreach (var liquidGold in new[] { 1000d, -25d })
        {
            Set(Character, "realGold", liquidGold); overspend();
            var context = CallMain("CaptureSupportContext");
            Equal(400d, Get(context, "GrossGold"), "capture observes overspending current support without rebudgeting it");
            Equal("|0:7", Property(Property(context, "CurrentSelection"), "StableKey"), "capture preserves exact unaffordable current selection");
            Equal(false, Get(ProjectSupport(context, Property(context, "CurrentSelection")), "Feasible"), "retaining the overspending current selection is still unaffordable as a desired policy");
            var request = New("DiggerBudgetRequest"); Set(request, "Id", 0); Set(request, "Maximum", 20L); Set(request, "Weight", 1d);
            var requests = Array.CreateInstance(request.GetType(), 1); requests.SetValue(request, 0);
            var planned = Invoke(Plugin.GetType("NGUInjector.Managers.SupportProjection", true), "Plan", context, new int[0], requests, .25d);
            Equal(true, Get(planned, "Feasible"), "shared planner can replace an overspending baseline within the desired gross-GPS budget");
            Equal("|0:4", Property(Get(planned, "Selection"), "StableKey"), "shared planner selects the exact affordable level without adopting the current overspend");
            reject(Selection(new int[0], 0, 7), 1d, false, "affordable");
            reject(Selection(new int[0], 0, 6), .5d, true, "affordable");
            reject(Selection(new int[0], 0, 21), 1d, true, "purchased");
            Equal(true, Get(CallMain("PreflightExactSupport", Selection(new int[0], 0, 6), .5d, false), "Feasible"), "pinned non-budget policy still requires solvency but not the optional planning fraction");
            var preflight = CallMain("PreflightExactSupport", cheaper, .25d, true);
            Equal(true, Get(preflight, "Feasible"), "cheaper desired selection preflights independently of current negative net GPS");
            Equal(80d, Get(preflight, "Drain"), "desired drain uses exact replacement levels");
            Equal(100d, Get(preflight, "Budget"), "desired budget uses gross GPS rather than negative current net GPS");
            Equal(320d, Get(preflight, "NetGold"), "desired net GPS excludes the replaced current drain");
            var receipt = CallMain("ApplyExactSupport", cheaper, .25d, true);
            Equal(true, Get(receipt, "Completed"), "cheaper replacement completes from native overspend");
            Equal("|0:4", Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "native selection matches exact cheaper replacement");
            Equal(320d, Invoke(Character, "goldPerSecond"), "cheaper replacement restores affordable native net GPS");
            Equal(true, CallMain("SupportMutationStillOwned", receipt), "cheaper replacement retains normal transaction ownership");
            Equal(liquidGold, Get(Character, "realGold"), "support replacement does not clamp or spend negative liquid gold");
            Equal(7, Get(pod, "id"), "cheaper replacement restores borrowed pod ID");
            Equal("previous input", Property(input, "text"), "cheaper replacement restores the user's input draft");

            overspend(); Set(machine, "realBaseGold", 0d);
            Equal(-640d, (double)Invoke(Character, "grossGoldPerSecond") - (double)Invoke(all, "totalGPSDrain"), "empty replacement starts at zero gross and negative native gross-minus-drain GPS");
            receipt = CallMain("ApplyExactSupport", empty, 0d, true);
            Equal(true, Get(receipt, "Completed"), "empty replacement completes from zero-income overspend");
            Equal("|", Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey"), "empty replacement clears native lists and flags exactly");
            Equal(0d, Invoke(Character, "goldPerSecond"), "empty replacement removes all native GPS drain");
            Equal(7L, Get(rows[0], "curLevel"), "empty replacement does not silently resize the former digger level");
            Equal(liquidGold, Get(Character, "realGold"), "empty replacement leaves native liquid gold unchanged");
        }

        overspend();
        Set(rows[0], "curLevel", 21L); reject(empty, 1d, true, "bounds"); Set(rows[0], "curLevel", 7L);
        active.Add(0); reject(empty, 1d, true, "duplicate"); active.RemoveAt(1);
        Set(rows[0], "active", false); reject(empty, 1d, true, "flags"); Set(rows[0], "active", true);
        Set(rows[1], "maxLevel", 1L); Set(rows[1], "curLevel", 1L); Set(rows[1], "active", true); active.Add(1);
        reject(empty, 1d, true, "slots"); active.RemoveAt(1); Set(rows[1], "active", false);
        var foreign = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(Character.GetType());
        foreach (var controller in new[] { all, Get(Character, "allBeards"), Controller })
        {
            Set(controller, "character", foreign);
            reject(empty, 1d, true, ReferenceEquals(controller, Controller) ? "context" : "controllers");
            Set(controller, "character", Character);
        }
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1d })
        { Set(machine, "realBaseGold", value); reject(empty, 1d, true, "income/drain"); }
        Set(machine, "realBaseGold", 400d);
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1d })
        { ((IList)Get(all, "baseGPSDrain"))[0] = value; reject(empty, 1d, true, "numeric tables"); }
        ((IList)Get(all, "baseGPSDrain"))[0] = double.MaxValue;
        reject(empty, 1d, true, "income/drain");
        // The pure zero-drain shortcut is finite, but native 0 * Pow(2, 1099) is NaN.
        // An empty desired selection must not bypass parity of the observed native current state.
        ((IList)Get(all, "baseGPSDrain"))[0] = 0d; Set(rows[0], "maxLevel", 1100L); Set(rows[0], "curLevel", 1100L);
        reject(empty, 1d, true, "parity");
        ((IList)Get(all, "baseGPSDrain"))[0] = 10d; Set(rows[0], "maxLevel", 20L); Set(rows[0], "curLevel", 7L);
        CallMain("ApplyExactSupport", empty, 0d, true);
        Equal(recalls, Get(typeof(Program), "Recalls"), "support overspend recovery never recalls resources");
        Equal(swaps, Get(typeof(Program), "Swaps"), "support overspend recovery never swaps gear");
        CheckCombatPrepOverspend();
    }

    private static void CheckCombatPrepOverspend()
    {
        if (MainType.GetMethod("ObserveCombatPrepSupport", All) == null)
            throw new InvalidOperationException("Fresh production build required for Combat Prep overspend caller checks: ObserveCombatPrepSupport is missing.");
        Invoke(typeof(Program), "Setup");
        Character = Get(typeof(Program), "Character"); Controller = Get(typeof(Program), "Controller"); MainInstance = Get(typeof(Program), "MainInstance");
        PrepareSupport(); SetActiveBeards();
        foreach (var row in (IList)Get(Get(Character, "beards"), "beards"))
        { Set(row, "beardLevel", 0L); Set(row, "permLevel", 0L); }
        var all = Get(Character, "allDiggers"); var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(rows[1], "maxLevel", 20L); Set(rows[1], "curLevel", 7L);
        ((IList)Get(all, "baseGPSDrain"))[1] = 10d; ((IList)Get(all, "consumesGPS"))[1] = true;
        ((IList)Get(all, "boostPerLevel"))[1] = .125d;
        Set(Get(Character, "machine"), "realBaseGold", 1000d); Invoke(all, "activateDigger", 1);
        Set(Get(Character, "machine"), "realBaseGold", 400d); Set(Character, "realGold", -25d);
        var key = Property(CallMain("CaptureSupportState"), "StableKey"); var refreshes = PodRefreshCalls;
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        var context = CallMain("CaptureSupportContext"); var original = Property(context, "CurrentSelection");
        var observed = CallMain("ObserveCombatPrepSupport", context);
        Equal(false, Get(observed, "Feasible"), "read-only overspending observation never grants desired-state feasibility");
        Equal(400d, Get(observed, "GrossGold"), "observation reports actual native gross GPS");
        Equal(640d, Get(observed, "Drain"), "observation reports actual native drain without resizing");
        Equal(-240d, Get(observed, "NetGold"), "observation preserves signed gross-minus-drain rather than native displayed zero");
        Equal(Convert.ToDouble(Invoke(all, "totalWandoosSpeedBonus")), Get(observed, "DiggerW"), "overspending observation preserves exact native Wandoos digger multiplier");

        var request = New("DiggerBudgetRequest"); Set(request, "Id", 1); Set(request, "Maximum", 20L); Set(request, "Weight", 1d);
        var requests = Array.CreateInstance(request.GetType(), 1); requests.SetValue(request, 0);
        var planned = Invoke(Plugin.GetType("NGUInjector.Managers.SupportProjection", true), "Plan", context, new int[0], requests, .25d);
        Equal("|1:4", Property(Get(planned, "Selection"), "StableKey"), "replacement plan retains the requested Wandoos digger at an affordable exact level");
        var gear = CallMain("CombatPrepEquipped");
        var cached = Activator.CreateInstance(MainType.GetNestedType("CombatPrepCapture", All), true);
        foreach (var profile in new[] { "wandoos-planned", "wandoos-unaffordable", "wandoos-over-budget" })
        {
            var support = Activator.CreateInstance(MainType.GetNestedType("CombatPrepSupport", All), true);
            Set(support, "ProfileGoal", "Wandoos"); Set(support, "BudgetFraction", .25d); Set(support, "EnforceBudget", true);
            Set(support, "Evaluation", profile == "wandoos-planned" ? planned : ProjectSupport(context,
                profile == "wandoos-unaffordable" ? original : Selection(new int[0], 1, 6), .25d));
            ((IDictionary)Get(cached, "Gear"))[profile] = gear; ((IDictionary)Get(cached, "Support"))[profile] = support;
        }
        var capture = CallMain("CaptureCombatPrep", .03d, .03d, cached); var snapshot = Get(capture, "Snapshot");
        var supports = (IDictionary)Get(capture, "Support"); var profiles = (Array)Property(snapshot, "Profiles");
        Equal(false, Get(Get(supports["current"], "Evaluation"), "Feasible"), "captured current baseline remains an observation, not an affordable pinned target");
        Equal(-240d, Property(Property(snapshot, "Current"), "NetGoldPerSecond"), "current comparison profile retains negative native net GPS");
        Equal(-240d, Property(snapshot, "NetGoldPerSecond"), "snapshot retains the original negative native baseline rate");
        Equal(1, profiles.Length, "only the affordable pinned replacement survives capture");
        Equal("wandoos-planned", Property(profiles.GetValue(0), "Key"), "capture evaluates affordable replacement despite overspending baseline");
        Equal(320d, Property(profiles.GetValue(0), "NetGoldPerSecond"), "replacement profile uses its own affordable net GPS");
        Equal(false, supports.Contains("wandoos-unaffordable"), "fresh pinned solvency rejection is not bypassed for observation");
        Equal(false, supports.Contains("wandoos-over-budget"), "fresh pinned policy-budget rejection is not bypassed for observation");
        var forecast = Invoke(Plugin.GetType("NGUInjector.Managers.CombatPrepPlanner", true), "Plan", snapshot, 10d);
        Equal(true, Property(forecast, "Success"), "compiled planner compares affordable replacement against signed current observation");

        var stats = CallMain("CombatPrepBossStats", gear, false, original);
        Equal(Invoke(Character, "totalAttack"), Property(stats, "Item1"), "original-support reporting preserves actual native attack despite overspend");
        Equal(Invoke(Character, "totalDefense"), Property(stats, "Item2"), "original-support reporting preserves actual native defense despite overspend");
        var selection = CallMain("CaptureCombatPrepLoadoutDefinition", (object)null); var selectionSnapshot = Get(selection, "Snapshot");
        Equal(true, ((bool[])Get(selectionSnapshot, "Available"))[8], "overspending current support does not flag Wandoos selection unavailable");
        Equal(null, ((string[])Get(selectionSnapshot, "Reasons"))[8], "Wandoos selection does not report an economic observation as a capture error");
        foreach (var energy in new[] { true, false })
        {
            var actual = Convert.ToDouble(Invoke(Character, energy ? "totalWandoosEnergySpeed" : "totalWandoosMagicSpeed"))
                / Convert.ToDouble(Invoke(Get(Character, "wandoos98Controller"), energy ? "baseEnergyTime" : "baseMagicTime"));
            var coefficient = (double)Get(Get(selectionSnapshot, energy ? "WandoosEnergy" : "WandoosMagic"), "Coefficient");
            Check(actual > 0 && Math.Abs(coefficient - actual) <= actual * 1e-6, "overspending Wandoos selection retains native full-boot coefficient " + energy);
        }

        Throws(() => CallMain("ObserveCombatPrepSupport", context, Selection(new int[0], 1, 21)), "purchased", "observation cannot bypass purchased-level validation");
        var effects = (double[])Get(context, "DiggerEffective"); var w = effects[1]; effects[1] = double.NaN;
        Throws(() => CallMain("ObserveCombatPrepSupport", context), "invalid support projection", "negative net cannot hide a nonfinite observed support multiplier"); effects[1] = w;
        Set(rows[1], "curLevel", 21L);
        Throws(() => CallMain("CaptureCombatPrep", .03d, .03d, cached), "bounds", "baseline capture still rejects malformed native current levels"); Set(rows[1], "curLevel", 7L);
        ((IList)Get(all, "baseGPSDrain"))[1] = 0d; Set(rows[1], "maxLevel", 1100L); Set(rows[1], "curLevel", 1100L);
        Throws(() => CallMain("CaptureCombatPrep", .03d, .03d, cached), "parity", "baseline observation still requires finite native current-drain parity");
        ((IList)Get(all, "baseGPSDrain"))[1] = 10d; Set(rows[1], "maxLevel", 20L); Set(rows[1], "curLevel", 7L);
        Equal(key, Property(CallMain("CaptureSupportState"), "StableKey"), "capture and reporting preserve all current support levels, lists and flags");
        Equal(refreshes, PodRefreshCalls, "observation and reporting never invoke native support writes");
        Equal(recalls, Get(typeof(Program), "Recalls"), "overspending baseline comparison never recalls resources");
        Equal(swaps, Get(typeof(Program), "Swaps"), "overspending baseline comparison never swaps equipment");
        Equal(-25d, Get(Character, "realGold"), "observation and reporting never clamp negative native liquid gold");
    }
}
