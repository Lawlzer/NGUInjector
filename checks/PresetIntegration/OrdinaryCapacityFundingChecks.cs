using System;
using System.Collections;
using System.Linq;

public static partial class PresetIntegrationChecks
{
    private static void CheckOrdinaryCapacityFunding()
    {
        var before = Assertions;
        foreach (var lanes in new[] { 1, 2, 3 })
        foreach (var immediate in new[] { 0, 1, 2, 3 }.Where(mask => (mask & ~lanes) == 0))
            CheckOrdinaryInlineCapacityFunding(lanes, immediate);
        CheckCapturedMagicCapDebtRecovery();
        Console.WriteLine("Ordinary capacity funding: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckOrdinaryInlineCapacityFunding(int lanes, int immediate)
    {
        var label = "ordinary lanes=" + lanes + ", immediate=" + immediate;
        var plan = SetupInlineStagePlan(); var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        var settings = Get(MainType, "Settings"); var magic = Get(Character, "magic");
        Set(settings, "_manageEnergy", (lanes & 1) != 0); Set(settings, "_manageMagic", (lanes & 2) != 0);
        Set(settings, "_manageR3", false); Set(settings, "_autoRebirth", true); Set(plan, "AutoRebirthEnabled", true);
        Set(stage, "EnergyFocus", 0); Set(stage, "MagicFocus", 0);
        Set(stage, "EnergyPriorities", (lanes & 1) != 0 ? new[] { "NGU" } : new string[0]);
        Set(stage, "MagicPriorities", (lanes & 2) != 0 ? new[] { "NGU" } : new string[0]);
        Set(stage, "GearGoal", (int)Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "Fight"));
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
        var selected = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 552);
        // Only the managed lanes lose capacity; the other lane must survive the swap intact.
        foreach (var item in new[] { head, selected })
        {
            Set(item, "spec1Type", Enum.Parse(Field(item, "spec1Type").FieldType, "EnergyCap"));
            Set(item, "spec2Type", Enum.Parse(Field(item, "spec2Type").FieldType, "MagicCap"));
            Set(item, "spec1Cur", ReferenceEquals(item, head) || (lanes & 1) == 0 ? 10000f : 0f);
            Set(item, "spec2Cur", ReferenceEquals(item, head) || (lanes & 2) == 0 ? 10000f : 0f);
        }
        Set(Character, "capEnergy", 1000L); Set(magic, "capMagic", 1000L);
        Invoke(typeof(Program), "RefreshBonuses");
        var attack = (long[])Property(Get(Character, "training"), "attackEnergy");
        var defense = (long[])Property(Get(Character, "training"), "defenseEnergy");
        Array.Clear(attack, 0, attack.Length); Array.Clear(defense, 0, defense.Length);
        attack[0] = 7; defense[5] = 5;
        var energyNgu = ((IList)Get(Get(Character, "NGU"), "skills"))[0];
        var magicNgu = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0];
        var oldEnergy = (long)Invoke(Character, "totalCapEnergy"); var oldMagic = (long)Invoke(Character, "totalCapMagic");
        Set(energyNgu, "energy", 137L); Set(magicNgu, "magic", 211L);
        Set(Character, "curEnergy", oldEnergy); Set(Character, "idleEnergy", oldEnergy - 12 - 137);
        Set(magic, "curMagic", oldMagic); Set(magic, "idleMagic", oldMagic - 211);
        var energyBefore = CallMain("GlobalPresetLaneState", true); var magicBefore = CallMain("GlobalPresetLaneState", false);
        var idleEnergyBefore = Get(Character, "idleEnergy"); var idleMagicBefore = Get(magic, "idleMagic");
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        var original = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
        var preserved = original.Keys.Cast<object>()
            .Where(key => key.ToString() == "BasicAttack" || key.ToString() == "BasicDefense"
                || (int)Invoke(writer, "Lane", key) == 2)
            .ToDictionary(key => key, key => ((long[])original[key])?.ToArray());
        CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler"); var commitment = Invoke(scheduler, "ExportState");
        var generation = Get(MainInstance, "_protectedGeneration");
        var pendingFunding = lanes & ~immediate;
        Equal(0, Get(MainInstance, "_executionCapacityFundingLanes"), label + " starts without synthetic capacity scope");
        Action preserve = () => {
            var observed = (IDictionary)Invoke(writer, "CaptureAllocations", Character);
            foreach (var pair in preserved)
                Check(pair.Value == null ? observed[pair.Key] == null : pair.Value.SequenceEqual((long[])observed[pair.Key]), label + " preserves " + pair.Key);
            Equal(731L, Get(Get(Character, "res3"), "curRes3"), label + " preserves unmanaged R3 total");
            Equal(431L, Get(Get(Character, "res3"), "idleRes3"), label + " preserves unmanaged R3 idle");
            if ((lanes & 1) == 0)
            {
                Equal(energyBefore, CallMain("GlobalPresetLaneState", true), label + " preserves unmanaged Energy bars");
                Equal(oldEnergy, Get(Character, "curEnergy"), label + " preserves unmanaged Energy total");
                Equal(idleEnergyBefore, Get(Character, "idleEnergy"), label + " preserves unmanaged Energy idle");
            }
            if ((lanes & 2) == 0)
            {
                Equal(magicBefore, CallMain("GlobalPresetLaneState", false), label + " preserves unmanaged Magic bars");
                Equal(oldMagic, Get(magic, "curMagic"), label + " preserves unmanaged Magic total");
                Equal(idleMagicBefore, Get(magic, "idleMagic"), label + " preserves unmanaged Magic idle");
            }
            foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "visits" })
                Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), label + " preserves scheduler " + field);
            Equal(generation, Get(MainInstance, "_protectedGeneration"), label + " preserves generation");
            Equal(true, Property(settings, "AutoRebirth"), label + " preserves live auto-rebirth permission");
            Equal(true, Get(plan, "AutoRebirthEnabled"), label + " preserves plan auto-rebirth permission");
            Equal(pendingFunding, Get(MainInstance, "_executionCapacityFundingLanes"), label + " retains only pending verified-swap budget lanes");
            Equal(false, CallMain("ExecutionOwnsDomain", Domain("Energy, Magic, Res3, Route")), label + " needs no temporary resource/route claim");
        };
        var swaps = (int)Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        if (immediate != 0) GearRenderObserver = () => NativeRouteCapacityClamp(immediate);
        try { RunVerifiedInline(plan, label + " actual inline swap and funding"); }
        finally { GearRenderObserver = null; }
        Equal(swaps + 1, Get(typeof(Program), "Swaps"), label + " executes one real Inventory head swap");
        Check(ReferenceEquals(selected, Get(inventory, "head")), label + " installs the acquired lower-capacity head");
        Check(ResourceTransfers > writes, label + " actually funds native bars through the shared writer");
        var selection = Get(MainInstance, "_inlineStageSelection"); var acquisition = Get(selection, "Acquisition");
        foreach (var energy in new[] { true, false })
        {
            var lane = energy ? 1 : 2; if ((lanes & lane) == 0) continue;
            var cap = (long)Invoke(Character, energy ? "totalCapEnergy" : "totalCapMagic");
            var old = energy ? oldEnergy : oldMagic;
            Check(cap < old, label + " really reduces native " + (energy ? "Energy" : "Magic") + " capacity");
            Equal(cap - (energy ? 12 : 0), Get(energy ? energyNgu : magicNgu, energy ? "energy" : "magic"), label + " funds only post-debit capacity");
            Equal((immediate & lane) == 0 ? old : cap, Get(energy ? Character : magic, energy ? "curEnergy" : "curMagic"), label + " never fabricates native depletion");
            Equal((immediate & lane) == 0 ? old - cap : 0L, Get(energy ? Character : magic, energy ? "idleEnergy" : "idleMagic"), label + " retains exactly the pending native debit");
        }
        preserve();
        if (immediate != lanes)
        {
            var intent = New("ResourceSetupIntent"); var desired = (IDictionary)Invoke(writer, "EmptyDesiredMap", (lanes & 1) != 0, (lanes & 2) != 0, false);
            var target = desired.Keys.Cast<object>().Single(key => key.ToString() == ((lanes & ~immediate & 1) != 0 ? "EnergyNgu" : "MagicNgu"));
            var energy = target.ToString() == "EnergyNgu";
            ((long[])desired[target])[0] = (long)Invoke(Character, energy ? "totalCapEnergy" : "totalCapMagic") - (energy ? 12 : 0) + 1;
            var acquiredIntent = Get(Get(Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true), "_stageResourceRequirements"), "Intent");
            Set(intent, "Owner", Get(acquiredIntent, "Owner")); Set(intent, "Desired", desired);
            Set(intent, "Energy", (lanes & 1) != 0); Set(intent, "Magic", (lanes & 2) != 0); Set(intent, "Authorized", Get(acquiredIntent, "Authorized"));
            var map = CapacityNativeMapKey(Character); var idleE = Get(Character, "idleEnergy"); var idleM = Get(magic, "idleMagic"); writes = ResourceTransfers;
            var rejected = CallMain("ReconcileStageResourceSetup", intent);
            Equal("RejectedUnchanged", Get(rejected, "Disposition").ToString(), label + " common ingress refuses an unbudgeted desired map");
            Equal(map, CapacityNativeMapKey(Character), label + " rejected overfund preserves all native bars");
            Equal(idleE, Get(Character, "idleEnergy"), label + " rejected overfund preserves Energy buffer");
            Equal(idleM, Get(magic, "idleMagic"), label + " rejected overfund preserves Magic buffer");
            Equal(writes, ResourceTransfers, label + " rejected overfund makes no writes");
            Equal(pendingFunding, Get(MainInstance, "_executionCapacityFundingLanes"), label + " refused funding cannot retire a pending native debit");
        }
        NativeRouteCapacityClamp(lanes & ~immediate);
        foreach (var energy in new[] { true, false })
        {
            if ((lanes & (energy ? 1 : 2)) == 0) continue;
            Equal(Invoke(Character, energy ? "totalCapEnergy" : "totalCapMagic"), Get(energy ? Character : magic, energy ? "curEnergy" : "curMagic"), label + " real native generator applies the cap debit");
            Equal(0L, Get(energy ? Character : magic, energy ? "idleEnergy" : "idleMagic"), label + " native clamp cannot drive idle negative");
        }
        Equal(pendingFunding, Get(MainInstance, "_executionCapacityFundingLanes"), label + " actual native generation precedes budget retirement");
        RunVerifiedInline(plan, label + " same-entry replay after native clamp"); pendingFunding = 0; preserve();
        var fundedE = Get(energyNgu, "energy"); var fundedM = Get(magicNgu, "magic");
        var totalE = Get(Character, "curEnergy"); var totalM = Get(magic, "curMagic");
        if ((lanes & 1) != 0) Set(Character, "capEnergy", 1007L);
        if ((lanes & 2) != 0) Set(magic, "capMagic", 1007L);
        RunVerifiedInline(plan, label + " increased capacity alone");
        Equal(totalE, Get(Character, "curEnergy"), label + " capacity growth invents no Energy");
        Equal(totalM, Get(magic, "curMagic"), label + " capacity growth invents no Magic");
        Equal(fundedE, Get(energyNgu, "energy"), label + " no Energy funding without native generation");
        Equal(fundedM, Get(magicNgu, "magic"), label + " no Magic funding without native generation");
        NativeRouteCapacityClamp(lanes);
        RunVerifiedInline(plan, label + " actual native growth");
        Equal((long)totalE + ((lanes & 1) != 0 ? 1 : 0), Get(Character, "curEnergy"), label + " retains actual native Energy gain");
        Equal((long)totalM + ((lanes & 2) != 0 ? 1 : 0), Get(magic, "curMagic"), label + " retains actual native Magic gain");
        Equal((long)fundedE + ((lanes & 1) != 0 ? 1 : 0), Get(energyNgu, "energy"), label + " funds actual new Energy");
        Equal((long)fundedM + ((lanes & 2) != 0 ? 1 : 0), Get(magicNgu, "magic"), label + " funds actual new Magic");
        Check(ReferenceEquals(selection, Get(MainInstance, "_inlineStageSelection")), label + " keeps the same inline selection");
        Equal(acquisition, Get(selection, "Acquisition"), label + " never renews acquisition");
        Equal(swaps + 1, Get(typeof(Program), "Swaps"), label + " replay/growth never re-equips"); preserve();
        fundedE = Get(energyNgu, "energy"); fundedM = Get(magicNgu, "magic");
        totalE = Get(Character, "curEnergy"); totalM = Get(magic, "curMagic");
        foreach (var energy in new[] { true, false })
        {
            if ((lanes & (energy ? 1 : 2)) == 0) continue;
            var pool = energy ? Character : magic; var current = energy ? "curEnergy" : "curMagic"; var idle = energy ? "idleEnergy" : "idleMagic";
            Set(pool, current, (long)Get(pool, current) + 37); Set(pool, idle, (long)Get(pool, idle) + 37);
            Check((long)Get(pool, current) > (long)Invoke(Character, energy ? "totalCapEnergy" : "totalCapMagic"), label + " stable replay really observes an over-cap grown pool");
        }
        CallMain("InvalidateCustomEquipmentApplication");
        RunVerifiedInline(plan, label + " verified no-op replay with a grown pool");
        Equal((long)totalE + ((lanes & 1) != 0 ? 37 : 0), Get(Character, "curEnergy"), label + " stable replay preserves all observed Energy growth");
        Equal((long)totalM + ((lanes & 2) != 0 ? 37 : 0), Get(magic, "curMagic"), label + " stable replay preserves all observed Magic growth");
        Equal((long)fundedE + ((lanes & 1) != 0 ? 37 : 0), Get(energyNgu, "energy"), label + " no-op replay funds the complete current Energy pool");
        Equal((long)fundedM + ((lanes & 2) != 0 ? 37 : 0), Get(magicNgu, "magic"), label + " no-op replay funds the complete current Magic pool");
        if ((lanes & 1) != 0) Equal(0L, Get(Character, "idleEnergy"), label + " stable grown Energy is not clipped to native cap");
        if ((lanes & 2) != 0) Equal(0L, Get(magic, "idleMagic"), label + " stable grown Magic is not clipped to native cap");
        Equal(swaps + 1, Get(typeof(Program), "Swaps"), label + " grown-pool verification performs no new native swap"); preserve();
        if (lanes != 3)
        {
            var unmanagedEnergy = (lanes & 1) == 0;
            Set(unmanagedEnergy ? Character : magic, unmanagedEnergy ? "capEnergy" : "capMagic", 1L);
            var available = unmanagedEnergy ? oldEnergy - 12 : oldMagic;
            Equal(available, CallMain("ExecutionResourceBudget", unmanagedEnergy, available), label + " does not acquire an unmanaged over-cap budget");
            RunVerifiedInline(plan, label + " unrelated unmanaged cap loss"); preserve();
        }
    }

    private static void CheckCapturedMagicCapDebtRecovery()
    {
        var plan = SetupInlineStagePlan(false); var settings = Get(MainType, "Settings");
        var stage = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(settings, "_manageEnergy", false); Set(settings, "_manageR3", false);
        Set(stage, "EnergyFocus", 0); Set(stage, "EnergyPriorities", new string[0]);
        CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler"); var commitment = Invoke(scheduler, "ExportState");
        var magic = Get(Character, "magic"); var machine = Get(Character, "machine");
        Set(magic, "curMagic", 1037L); Set(magic, "idleMagic", 1037L);
        var swaps = Get(typeof(Program), "Swaps");
        RunVerifiedInline(plan, "gearless inline funding preserves the complete observed grown Magic pool");
        Equal(1037L, ((IList)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(skill => (long)Get(skill, "magic")), "gearless funding does not clip observed Magic to equipment cap");
        Equal(0L, Get(magic, "idleMagic"), "gearless grown-pool funding leaves no artificial cap buffer");
        Equal(swaps, Get(typeof(Program), "Swaps"), "gearless baseline never performs an owned gear swap");
        Equal(0, Get(MainInstance, "_executionCapacityFundingLanes"), "gearless funding never arms capacity scope");
        foreach (var skill in (IList)Get(Get(Character, "NGU"), "magicSkills")) Set(skill, "magic", 0L);
        Set(magic, "capMagic", 13451923L); Set(magic, "curMagic", 13810097L); Set(magic, "idleMagic", 0L);
        Set(machine, "goldMultiMagic", 13810097L); Set(machine, "levelGoldMulti", 212L); Set(machine, "goldMultiProgress", .13f);
        Set(machine, "levelSpeed", 2014L); Set(machine, "speedProgress", .4f); Set(machine, "speedTarget", 113L); Set(machine, "multiTarget", 227L);
        Set(Get(Character, "timeMachineController"), "character", Character);
        var energy = CallMain("GlobalPresetLaneState", true); var totalEnergy = Get(Character, "curEnergy"); var idleEnergy = Get(Character, "idleEnergy");
        var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
        Equal(13451923L, Invoke(Character, "totalCapMagic"), "captured control uses the actual native Magic cap getter");
        // Deliberately retain the captured pre-fix full allocation for the negative control.
        NativeRouteCapacityClamp(2);
        Equal(13451923L, Get(magic, "curMagic"), "native callback reproduces captured clamped total");
        Equal(-358174L, Get(magic, "idleMagic"), "native callback reproduces captured negative idle exactly");
        Equal(13810097L, Get(machine, "goldMultiMagic"), "native cap debit leaves the stale TM allocation untouched");
        var map = CapacityNativeMapKey(Character); var writes = ResourceTransfers;
        Throws(() => Invoke(writer, "AllocatedResourceKey"), "Invalid resource accounting", "coupled negative-accounting guard remains intact");
        var desired = Invoke(writer, "EmptyDesiredMap", false, true, false);
        var refused = Invoke(writer, "Reconcile", desired, false, true, false);
        Equal("RejectedUnchanged", Get(refused, "Disposition").ToString(), "ordinary writer cannot repair a negative native pool");
        Equal(map, CapacityNativeMapKey(Character), "negative-accounting refusal preserves native allocations");
        Equal(-358174L, Get(magic, "idleMagic"), "negative-accounting refusal never clamps idle to zero");
        CallMain("InvalidateCustomEquipmentApplication");
        CallMain("RunCustomPlanAutomation", plan);
        Equal(map, CapacityNativeMapKey(Character), "ordinary inline refusal cannot repair foreign native bars");
        Equal(-358174L, Get(magic, "idleMagic"), "ordinary inline refusal cannot repair foreign idle debt");
        Equal(false, Get(scheduler, "_armed"), "negative native accounting withholds protected stage credit");
        Equal(writes, ResourceTransfers, "ordinary negative-accounting refusal performs no resource writes");
        // This is the actual native TM-only recovery, not a new injector repair path.
        Invoke(Get(Character, "timeMachineController"), "removeAllMagic");
        Equal(0L, Get(machine, "goldMultiMagic"), "native scoped recovery clears only TM Magic");
        Equal(13451923L, Get(magic, "idleMagic"), "native scoped recall returns the actual conserved total");
        Equal(13451923L, Get(magic, "curMagic"), "native scoped recall fabricates no Magic");
        Equal(writes, ResourceTransfers, "native recovery invokes no injector resource writer");
        RunVerifiedInline(plan, "ordinary inline setup resumes only after native recovery");
        Equal(212L, Get(machine, "levelGoldMulti"), "native recovery preserves TM Magic levels");
        Equal(.13f, Get(machine, "goldMultiProgress"), "native recovery preserves TM Magic partial progress");
        Equal(2014L, Get(machine, "levelSpeed"), "native recovery preserves TM Energy levels");
        Equal(.4f, Get(machine, "speedProgress"), "native recovery preserves TM Energy partial progress");
        Equal(113L, Get(machine, "speedTarget"), "native recovery preserves TM Energy target");
        Equal(227L, Get(machine, "multiTarget"), "native recovery preserves TM Magic target");
        Equal(energy, CallMain("GlobalPresetLaneState", true), "native recovery and ordinary continuation preserve unmanaged Energy bars");
        Equal(totalEnergy, Get(Character, "curEnergy"), "native recovery preserves unmanaged Energy total");
        Equal(idleEnergy, Get(Character, "idleEnergy"), "native recovery preserves unmanaged Energy idle");
        Equal(7L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], "native recovery preserves basic training");
        Equal(731L, Get(Get(Character, "res3"), "curRes3"), "native recovery preserves unmanaged R3 total");
        Equal(431L, Get(Get(Character, "res3"), "idleRes3"), "native recovery preserves unmanaged R3 idle");
        Equal(300L, Get(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3"), "native recovery preserves funded R3");
        foreach (var field in new[] { "entry", "cycle", "enteredAt", "expectedEnd", "durations", "visits" })
            Equal(Json(commitment, field).ToString(), Json(Invoke(scheduler, "ExportState"), field).ToString(), "native recovery preserves original scheduler " + field);
    }
}
