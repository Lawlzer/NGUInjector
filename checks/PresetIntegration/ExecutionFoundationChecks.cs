using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static object Domain(string names) => Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), names);

    private static void CheckExecutionFoundation()
    {
        var before = Assertions;
        SetupInlineStagePlan();
        Func<bool> current = () => true;
        var energy = Domain("Energy"); var magic = Domain("Magic");
        var uncontrolled = CallMain("ResolveExecutionGrant", "observe", "entry", Domain("None"), current, null, null);
        Equal(true, CallMain("ValidateExecutionGrant", uncontrolled, Domain("None")), "uncontrolled intent can retain a current observation identity");
        Equal(false, CallMain("ValidateExecutionGrant", uncontrolled, energy), "uncontrolled observation grants no resource write authority");
        var grant = CallMain("ResolveExecutionGrant", "test", "entry", energy, current, null, null);
        Equal(true, CallMain("ValidateExecutionGrant", grant, energy), "fresh Energy grant is current");
        Equal(false, CallMain("ValidateExecutionGrant", grant, magic), "grant cannot authorize an undeclared domain");
        CallMain("InvalidateExecutionObservations", magic);
        Equal(true, CallMain("ValidateExecutionGrant", grant, energy), "unrelated Magic invalidation leaves Energy authority healthy");
        CallMain("InvalidateExecutionObservations", energy);
        Equal(false, CallMain("ValidateExecutionGrant", grant, energy), "affected observation revision revokes an old grant");
        grant = CallMain("ResolveExecutionGrant", "test", "entry", energy, current, null, null);
        var settings = Get(MainType, "Settings"); Set(settings, "_manageMagic", false);
        Equal(true, CallMain("ValidateExecutionGrant", grant, energy), "unrelated Magic permission does not revoke Energy grant");
        Set(settings, "_manageEnergy", false);
        Equal(false, CallMain("ValidateExecutionGrant", grant, energy), "Energy permission revokes Energy grant");
        Set(settings, "_manageEnergy", true); Set(settings, "_manageMagic", true);
        var claim = CallMain("BeginExecutionClaim", "route-test", "attempt-1", Domain("Gear, Support, Route"), current);
        var stolen = CallMain("ResolveExecutionGrant", "foreign", "other", Domain("Gear"), current, null, null);
        Equal(false, CallMain("ValidateExecutionGrant", stolen, Domain("Gear")), "another owner cannot borrow the active claim footprint");
        var exact = CallMain("ResolveExecutionGrant", "route-test", "attempt-1", Domain("Gear"), current, claim, null);
        Equal(true, CallMain("ValidateExecutionGrant", exact, Domain("Gear")), "identified current claim grants its exact domain");
        Throws(() => CallMain("BeginExecutionClaim", "other", "attempt-2", Domain("Support"), current), "already owns", "overlapping claims reject before mutation");
        CallMain("EndExecutionClaim", claim, false);
        Equal(false, CallMain("ValidateExecutionGrant", exact, Domain("Gear")), "ending a claim revokes old callbacks");
        grant = CallMain("ResolveExecutionGrant", "test", "entry", energy, current, null, null);
        var lifecycle = MainType.GetNestedType("ExecutionLifecycle", All);
        CallMain("SetExecutionLifecycle", Enum.Parse(lifecycle, "Quiescing"));
        Equal(false, CallMain("ValidateExecutionGrant", grant, energy), "quiescence revokes native write authority");
        CallMain("SetExecutionLifecycle", Enum.Parse(lifecycle, "Running"));
        Equal(false, CallMain("ValidateExecutionGrant", grant, energy), "resuming cannot resurrect a pre-quiescence grant");
        CheckExecutionClaimSuccessor();
        CheckExecutionClaimNoGoal();
        CheckExecutionClaimForeignRelease();
        Console.WriteLine("Execution foundation: " + (Assertions - before) + " native assertions.");
    }

    private static object ExactClaimSetup(object head, string owner)
    {
        var manager = Plugin.GetType("NGUInjector.Managers.LoadoutManager", true);
        var map = (Array)Invoke(manager, "CaptureExactLoadout");
        var index = Array.FindIndex(map.Cast<object>().ToArray(), x => (int)Property(x, "slot") == -1);
        var item = Activator.CreateInstance(map.GetValue(index).GetType(), true);
        item.GetType().GetProperty("slot", All).SetValue(item, -1);
        item.GetType().GetProperty("id", All).SetValue(item, Get(head, "id"));
        item.GetType().GetProperty("equipment", All).SetValue(item, head);
        map.SetValue(item, index);
        var setup = Activator.CreateInstance(MainType.GetNestedType("SelectedSetupRequirements", All), true);
        Set(setup, "Owner", owner); Set(setup, "Acquisition", owner); Set(setup, "Gear", map);
        Set(setup, "Support", Property(CallMain("CaptureSupportState"), "Selection"));
        Set(setup, "Authorized", (Func<bool>)(() => true));
        Set(setup, "RouteRetirementAuthorized", (Func<bool>)(() => true));
        return setup;
    }

    private static void CheckExecutionClaimSuccessor()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "claim baseline A");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var temporary = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var successor = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
        var baseline = ExactClaimSetup(Get(inventory, "head"), "baseline-A");
        CallMain("PublishExecutionBaseline", baseline);
        var claim = CallMain("BeginExecutionClaim", "titan-test", "spawn-1", Domain("Gear, Route"), (Func<bool>)(() => true));
        var wanted = ExactClaimSetup(temporary, "titan-test");
        CallMain("ApplyExecutionClaim", claim, wanted);
        Equal(552, Get(Get(inventory, "head"), "id"), "explicit current claim equips its selected head");
        var b = ExactClaimSetup(successor, "baseline-B");
        CallMain("PublishExecutionBaseline", b);
        // Exercise the actual resolver before ending the temporary claim.
        CallMain("ApplySelectedSetup", b);
        Equal(552, Get(Get(inventory, "head"), "id"), "current B is retained without flashing beneath a temporary claim");
        var seen = new List<int>(); var writes = ResourceTransfers;
        GearRenderObserver = () => seen.Add((int)Get(Get(inventory, "head"), "id"));
        try { Equal(true, CallMain("EndExecutionClaim", claim, true), "claim release applies CURRENT B directly"); }
        finally { GearRenderObserver = null; }
        Equal(551, Get(Get(inventory, "head"), "id"), "release equips selected B, not original A");
        Check(!seen.Contains(550), "release has no historical A detour");
        Equal(writes, ResourceTransfers, "preserving setup handoff has no redundant resource clear/refill");
        var swaps = Get(typeof(Program), "Swaps");
        CallMain("EndExecutionClaim", claim, true);
        Equal(swaps, Get(typeof(Program), "Swaps"), "repeated release verifies B without another model swap");
    }

    private static void CheckExecutionClaimNoGoal()
    {
        SetupInlineStagePlan();
        var inventory = Get(Character, "inventory");
        var head = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var claim = CallMain("BeginExecutionClaim", "no-goal", "attempt", Domain("Gear, Route"), (Func<bool>)(() => true));
        CallMain("ApplyExecutionClaim", claim, ExactClaimSetup(head, "no-goal"));
        var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        CallMain("EndExecutionClaim", claim, true);
        Check(ReferenceEquals(head, Get(inventory, "head")), "no successor preserves native gear");
        Equal(swaps, Get(typeof(Program), "Swaps"), "no goal is not controlled-empty gear");
        Equal(writes, ResourceTransfers, "no goal is not an implicit resource clear or NGU fallback");
    }

    private static void CheckExecutionClaimForeignRelease()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "foreign-release baseline");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var head = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var foreign = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
        var claim = CallMain("BeginExecutionClaim", "foreign-release", "attempt", Domain("Gear, Route"), (Func<bool>)(() => true));
        CallMain("ApplyExecutionClaim", claim, ExactClaimSetup(head, "foreign-release"));
        var slot = items.IndexOf(foreign); items[slot] = Get(inventory, "head"); Set(inventory, "head", foreign);
        Invoke(typeof(Program), "RefreshBonuses");
        var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
        Equal(false, CallMain("EndExecutionClaim", claim, true), "foreign setup on release yields rather than overwrites");
        Check(ReferenceEquals(foreign, Get(inventory, "head")), "foreign native head survives claim release");
        Equal(swaps, Get(typeof(Program), "Swaps"), "foreign release cannot write successor gear");
        Equal(writes, ResourceTransfers, "foreign release cannot clear or refill resources");
    }
}
