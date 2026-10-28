using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class PresetIntegrationChecks
{
    private static Action<object, int, int> CapacityResourcePresentation;
    private static Action CapacityGearBoundary;
    private static int RouteNativeNukes, RouteNativeFights, RouteNativeOsChanges;
    private static Action<string> RouteNativeActionReturned;
    public static void ObserveNativeRouteAction(string name, bool after)
    {
        if (after) { RouteNativeActionReturned?.Invoke(name); return; }
        if (name == "startNuke") RouteNativeNukes++;
        else if (name == "beginFight") RouteNativeFights++;
        else RouteNativeOsChanges++;
    }
    public static void ObserveCapacityGearBoundary() => CapacityGearBoundary?.Invoke();
    public static void ObserveCapacityResourcePresentation(object character, int target, int index) => CapacityResourcePresentation?.Invoke(character, target, index);
    private static void ConfigureRouteCapacityPluginFixture(AssemblyDefinition plugin)
    {
        var method = plugin.MainModule.GetType("NGUInjector.Managers.ResourceWriter").Methods.Single(x => x.Name == "Refresh");
        var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
        foreach (var instruction in new[] { il.Create(OpCodes.Ldarg_0), il.Create(OpCodes.Ldarg_1), il.Create(OpCodes.Ldarg_2),
            il.Create(OpCodes.Call, plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveCapacityResourcePresentation), All))) }) il.InsertBefore(first, instruction);
        // The real Unload/quiesce code stays intact; only unavailable Unity
        // callback-scheduler shutdown leaves are isolated, as in the combat fixture.
        IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
        var shutdownLeaves = 0;
        foreach (var unload in plugin.MainModule.Types.SelectMany(Types).SelectMany(x => x.Methods).Where(x => x.HasBody && x.Name.Contains("<Unload>")))
        foreach (var instruction in unload.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.MonoBehaviour" || call.Name != "CancelInvoke" && call.Name != "StopAllCoroutines") continue;
            instruction.OpCode = OpCodes.Pop; instruction.Operand = null; shutdownLeaves++;
        }
        Equal(2, shutdownLeaves, "capacity lifecycle fixture substitutes only the two unavailable Unity scheduler shutdown calls");
    }
    private static void ConfigureRouteCombatGameFixture(AssemblyDefinition game)
    {
        var nativeRouteLeaves = 0;
        foreach (var type in game.MainModule.Types)
        foreach (var method in type.Methods.Where(x => x.HasBody))
        {
            if (type.Name == "BossController" && (method.Name == "startNuke" || method.Name == "beginFight")
                || type.Name == "Wandoos98" && method.Name == "changeOS")
            {
                nativeRouteLeaves++;
                var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
                var observer = game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveNativeRouteAction), All));
                foreach (var instruction in new[] { il.Create(OpCodes.Ldstr, method.Name), il.Create(OpCodes.Ldc_I4_0), il.Create(OpCodes.Call, observer) })
                    il.InsertBefore(first, instruction);
                foreach (var ret in method.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
                {
                    ret.OpCode = OpCodes.Ldstr; ret.Operand = method.Name;
                    var after = il.Create(OpCodes.Ldc_I4_1); il.InsertAfter(ret, after);
                    var callback = il.Create(OpCodes.Call, observer); il.InsertAfter(after, callback);
                    il.InsertAfter(callback, il.Create(OpCodes.Ret));
                }
            }
            if (type.Name == "Character" && new[] { "totalEnergyBar", "totalMagicBar", "totalMagicSpeed" }.Contains(method.Name))
                Constant(method, method.Name == "totalMagicSpeed" ? 50 : 1);
            if ((type.Name == "Energy" || type.Name == "MagicDisplay")
                && new[] { "updateEnergyText", "updateMagicText", "updateSlider" }.Contains(method.Name)) Constant(method, 0);
        }
        Equal(3, nativeRouteLeaves, "route fixture observes both actual native boss starts and the actual OS change leaf");
        var bonuses = game.MainModule.GetType("InventoryController").Methods.Single(x => x.Name == "updateBonuses");
        foreach (var ret in bonuses.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
            bonuses.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call,
                game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveCapacityGearBoundary), All))));
        foreach (var type in game.MainModule.Types.Where(x => x.Name == "PlayerController" || x.Name == "EnemyAI"))
        foreach (var method in type.Methods.Where(x => x.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.Random" || call.Name != "Range"
                || call.Parameters[0].ParameterType.FullName != "System.Single") continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(RouteCombatRandom), All));
        }
        var damageText = game.MainModule.GetType("PlayerController").Methods.Single(x => x.Name == "damageText");
        damageText.Body = new Mono.Cecil.Cil.MethodBody(damageText);
        damageText.Body.GetILProcessor().Emit(OpCodes.Ldstr, "native damage presentation"); damageText.Body.GetILProcessor().Emit(OpCodes.Ret);
        var idle = game.MainModule.GetType("IdleAttack").Methods.Single(x => x.Name == "setToggle");
        foreach (var instruction in idle.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.UI.Graphic" || call.Name != "set_color") continue;
            var shim = new MethodDefinition("RouteIdleColor" + idle.DeclaringType.Methods.Count,
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static, game.MainModule.TypeSystem.Void);
            shim.Parameters.Add(new ParameterDefinition(call.DeclaringType));
            shim.Parameters.Add(new ParameterDefinition(call.Parameters[0].ParameterType)); shim.Body.GetILProcessor().Emit(OpCodes.Ret);
            idle.DeclaringType.Methods.Add(shim); instruction.OpCode = OpCodes.Call; instruction.Operand = shim;
        }
    }

    public static float RouteCombatRandom(float min, float max) => min + (max - min) / 2;

    private static void ConfigureRouteCombatUiFixture(AssemblyDefinition ui)
    {
        // Keep native button readiness/cooldown state, remove only Unity scene
        // transition notifications from this setter.
        var selectable = ui.MainModule.GetType("UnityEngine.UI.Selectable");
        var method = selectable.Methods.Single(x => x.Name == "set_interactable");
        method.Body = new Mono.Cecil.Cil.MethodBody(method); var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, selectable.Fields.Single(x => x.Name == "m_Interactable")); il.Emit(OpCodes.Ret);
    }
    // Actual compiled producers and shared authority; externally supplied native
    // outcome observations are labeled at each boundary below.
    private static void CheckRouteExecutorCutover()
    {
        var before = Assertions;
        foreach (var owner in new[] { "manual-titan:4", "max-itopod", "adventure-gold" })
            CheckRouteCurrentSuccessor(owner);
        CheckRouteNoSuccessor();
        CheckRouteForeignSetup();
        CheckRouteGrantRevocation();
        CheckPresetManualRouteAcquisition();
        CheckAdventureDynamicManualHandoff();
        CheckNativeManualTitanProcess();
        CheckNativeManualTitanReadiness();
        CheckNativeManualTitanDeathHistory();
        CheckNativeRouteQuiescence();
        CheckNativeRouteBusyRestart();
        CheckNativeManualTitanRestart();
        CheckNativeTowerRestart();
        CheckNativeTowerProcess();
        CheckNativeAdventureGoldProcess();
        CheckNativeTitanGoldUpgrade();
        CheckNativeFightPreparationHandoff();
        CheckNativeTemporaryRouteHandoff();
        CheckNativeManualTitanCallbackFailures();
        CheckNativeTowerSetupFailure();
        CheckNativeTowerPendingSetupRetirement();
        CheckNativeTowerSupportGate();
        CheckNativeManualTowerDisable();
        CheckNativeTowerDeathBudget();
        CheckNativeTowerModelReplacement();
        CheckNativeAdventureGoldDeath();
        CheckNativeAdventureGoldCleanup();
        CheckNativeBossInvocation();
        CheckNativeBossUncertainty();
        CheckNativeWalderpAttack();
        CheckManualTitanCapacityTransition();
        CheckCapacityLifecycleRegressions();
        Console.WriteLine("Route executor cutover: " + (Assertions - before) + " native assertions.");
    }

    private static string RouteOwnerField(string owner) => owner == "max-itopod" ? "_maxItopodProcess"
        : owner == "adventure-gold" ? "_customAdventureGoldProcess" : "_manualTitanProcess";

    private static object RouteBaseline(object head, string owner)
    {
        var setup = ExactClaimSetup(head, owner);
        var model = Character; var runtime = MainInstance; var settings = Get(MainType, "Settings");
        var inventory = Get(model, "inventory"); var rebirth = Get(Get(model, "stats"), "rebirthNumber");
        Func<bool> current = () => ReferenceEquals(model, Character) && ReferenceEquals(runtime, MainInstance)
            && ReferenceEquals(settings, Get(MainType, "Settings")) && ReferenceEquals(inventory, Get(model, "inventory"))
            && Equals(rebirth, Get(Get(model, "stats"), "rebirthNumber"));
        Set(setup, "Authorized", current);
        Set(setup, "RouteRetirementAuthorized", (Func<bool>)(() => current() && (int)Get(Get(model, "adventure"), "zone") == -1));
        return setup;
    }

    private static object AcquireNativeRouteProcess(string owner, object head)
    {
        var selected = RouteBaseline(head, owner);
        var process = CallMain("BeginRouteProcess", owner, Get(selected, "Gear"), true,
            Domain("Gear, Support, Route, NativeSettings"), null);
        Set(MainInstance, RouteOwnerField(owner), process);
        Equal(true, CallMain("ApplyRouteProcess", process), owner + " exact native setup verifies");
        return process;
    }

    private static void CheckRouteCurrentSuccessor(string owner)
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, owner + " baseline A");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var temporary = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var successor = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
        CallMain("PublishExecutionBaseline", RouteBaseline(Get(inventory, "head"), "route-A"));
        var process = AcquireNativeRouteProcess(owner, temporary);
        var b = RouteBaseline(successor, "route-B");
        CallMain("PublishExecutionBaseline", b);
        CallMain("ApplySelectedSetup", b);
        Equal(552, Get(Get(inventory, "head"), "id"), owner + " B stays covered by acquired temporary setup");
        var seen = new List<int>(); var writes = ResourceTransfers;
        GearRenderObserver = () => seen.Add((int)Get(Get(inventory, "head"), "id"));
        try { Equal(true, CallMain("EndRouteProcess", process), owner + " releases directly to current B"); }
        finally { GearRenderObserver = null; }
        Equal(551, Get(Get(inventory, "head"), "id"), owner + " current B installed");
        Check(!seen.Contains(550), owner + " never flashes historical A");
        Equal(writes, ResourceTransfers, owner + " route-only preserving handoff adds no resource clears");
        var swaps = Get(typeof(Program), "Swaps");
        CallMain("EndRouteProcess", process);
        Equal(swaps, Get(typeof(Program), "Swaps"), owner + " repeated release does not repeat gear mutation");
    }

    private static void CheckRouteNoSuccessor()
    {
        SetupInlineStagePlan();
        var inventory = Get(Character, "inventory");
        var temporary = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var process = AcquireNativeRouteProcess("adventure-gold", temporary);
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        Equal(true, CallMain("EndRouteProcess", process), "route without successor retires authority");
        Check(ReferenceEquals(temporary, Get(inventory, "head")), "no successor preserves acquired native gear");
        Equal(swaps, Get(typeof(Program), "Swaps"), "no successor performs no historical restore");
        Equal(writes, ResourceTransfers, "no successor performs no implicit resource clear");
    }

    private static void CheckRouteForeignSetup()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "route foreign baseline");
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var temporary = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var foreign = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
        var process = AcquireNativeRouteProcess("adventure-gold", temporary);
        var slot = items.IndexOf(foreign); items[slot] = Get(inventory, "head"); Set(inventory, "head", foreign);
        Invoke(typeof(Program), "RefreshBonuses");
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        CallMain("EndRouteProcess", process);
        Check(ReferenceEquals(foreign, Get(inventory, "head")), "foreign setup is never adopted as cleanup authority");
        Equal(swaps, Get(typeof(Program), "Swaps"), "foreign route release does not write gear");
        Equal(writes, ResourceTransfers, "foreign route release does not clear bars");
    }

    private static void CheckRouteGrantRevocation()
    {
        SetupInlineStagePlan();
        var inventory = Get(Character, "inventory");
        var temporary = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var process = AcquireNativeRouteProcess("adventure-gold", temporary);
        var grant = CallMain("RouteProcessGrant", process, false);
        Equal(true, CallMain("ValidateExecutionGrant", grant, Domain("Route, NativeSettings")), "route grant begins current");
        var settings = Get(MainType, "Settings");
        var enabled = (bool)Get(settings, "_customAdventureEnabled");
        Set(settings, "_customAdventureEnabled", !enabled);
        Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("Route")), "route preference edit revokes captured callback");
        Set(settings, "_customAdventureEnabled", enabled);
        grant = CallMain("RouteProcessGrant", process, false);
        CallMain("InvalidateExecutionObservations", Domain("Route"));
        Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("Route")), "route observation revision revokes old callback");
        CallMain("EndRouteProcess", process);
    }

    private static void CheckPresetManualRouteAcquisition()
    {
        var before = Assertions;
        foreach (var kind in new[] { "retry", "route", "support", "floor-start", "floor-end", "material", "resources" })
        {
            var args = SetupAdventureDynamic("preset-route-" + kind, "[1,1,0,0,0,0,0,0,0,0,0,0]");
            Invoke(MainInstance, "EquipGlobalPreset", args, null);
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), kind + " starts from a verified native preset");
            PrepareAdventureDynamicManualRule();
            var adventure = Get(Character, "adventure"); var inventory = Get(Character, "inventory");
            var support = Property(CallMain("CaptureSupportState"), "StableKey");
            var allocations = CallMain("GlobalPresetAllocationState");
            var routes = AdventureZoneEntries; var swaps = Get(typeof(Program), "Swaps");
            var writes = ResourceTransfers; var pods = PodRefreshCalls; var recalls = Get(typeof(Program), "Recalls");
            var searches = PresetSearches; var captures = PresetCaptures; var gearSelections = InlineGearSelections;
            FailAdventureZoneEntry = true;
            try
            {
                Throws(() => CallMain("TryRouteGlobalPresetManualAdventure", (object)null), "fixture native route retirement failure",
                    kind + " reaches native predecessor retirement before support changes");
            }
            finally { FailAdventureZoneEntry = false; }
            var process = Get(MainInstance, "_delegatedManualAdventureProcess");
            var claim = Get(process, "Claim"); var setup = Get(process, "Setup");
            Equal(false, Get(process, "Applied"), kind + " failed retirement retains an unapplied process");
            Check(ReferenceEquals(setup, Get(claim, "Setup")), kind + " incoming selection is already installed on the claim");
            Check(!Equals(Property(Get(setup, "Support"), "StableKey"), Property(Property(CallMain("CaptureSupportState"), "Selection"), "StableKey")),
                kind + " incoming support genuinely differs from the native predecessor");
            Equal(false, CallMain("ExecutionSetupVerified"), kind + " incoming setup cannot verify before predecessor retirement");
            Equal(true, ((Func<bool>)Get(setup, "RouteRetirementAuthorized"))(), kind + " exact predecessor independently authorizes retirement");
            Equal(1000, Get(adventure, "zone"), kind + " failed native retirement retains ITOPOD");
            Equal(routes + 1, AdventureZoneEntries, kind + " failed retirement attempts only Safe Zone once");
            Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), kind + " failed retirement preserves native support");

            if (kind == "route") Set(adventure, "zone", 4);
            if (kind == "support") SetActiveBeards(0);
            if (kind == "floor-start" || kind == "floor-end")
            {
                var field = kind == "floor-start" ? "itopodStart" : "itopodEnd";
                Set(adventure, field, (int)Get(adventure, field) + 1);
            }
            if (kind == "material") Set(Get(inventory, "head"), "curAttack", (float)Get(Get(inventory, "head"), "curAttack") + 1);
            if (kind == "resources")
            {
                var row = ((IList)Get(Get(Character, "NGU"), "skills"))[0]; var amount = (long)Get(row, "energy");
                Check(amount > 0, "resource negative control starts with real preset funding");
                Set(row, "energy", amount - 1); Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
            }
            var expectedZone = Get(adventure, "zone"); var start = Get(adventure, "itopodStart"); var end = Get(adventure, "itopodEnd");
            var expectedSupport = Property(CallMain("CaptureSupportState"), "StableKey");
            var expectedAllocations = CallMain("GlobalPresetAllocationState");
            if (kind == "retry")
            {
                var zones = new List<int>();
                AdventureZoneEntryObserver = zone => {
                    zones.Add(zone);
                    Equal(zone == -1 ? 1000 : -1, Get(adventure, "zone"), "retry observes predecessor then Safe Zone before manual entry");
                    if (zone == -1) Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), "retry retires before changing support");
                };
                try { Equal(true, CallMain("TryRouteGlobalPresetManualAdventure", (object)null), "unchanged predecessor retries retained admission"); }
                finally { AdventureZoneEntryObserver = null; }
                Equal("-1,0", string.Join(",", zones), "retry retires the exact predecessor then enters manual combat once");
                Equal(0, Get(adventure, "zone"), "retry enters selected native manual zone");
                Equal(true, CallMain("RouteProcessReady", process), "retry verifies the retained selected setup");
                Equal(start, Get(adventure, "itopodStart"), "retirement never changes acquired start floor");
                Equal(end, Get(adventure, "itopodEnd"), "retirement never changes acquired end floor");
                Equal(true, CallMain("TryRouteGlobalPresetManualAdventure", (object)null), "applied retry services the same process");
                Equal(routes + 3, AdventureZoneEntries, "applied retry repeats neither retirement nor entry");
            }
            else
            {
                Equal(false, ((Func<bool>)Get(setup, "RouteRetirementAuthorized"))(), kind + " invalidates the captured predecessor witness");
                Throws(() => CallMain("TryRouteGlobalPresetManualAdventure", (object)null),
                    kind == "support" || kind == "material" ? "Temporary setup changed externally" : "current adventure owner",
                    kind + " cannot reauthorize from newer foreign state");
                Equal(false, Get(process, "Applied"), kind + " never fabricates an applied setup");
                Equal(expectedZone, Get(adventure, "zone"), kind + " preserves foreign route");
                Equal(start, Get(adventure, "itopodStart"), kind + " preserves native start floor");
                Equal(end, Get(adventure, "itopodEnd"), kind + " preserves native end floor");
                Equal(expectedSupport, Property(CallMain("CaptureSupportState"), "StableKey"), kind + " preserves native support");
                Equal(expectedAllocations, CallMain("GlobalPresetAllocationState"), kind + " preserves native resources");
                Equal(routes + 1, AdventureZoneEntries, kind + " reaches no further native route entry");
                Equal(pods, PodRefreshCalls, kind + " performs no support writes");
            }
            Check(ReferenceEquals(process, Get(MainInstance, "_delegatedManualAdventureProcess"))
                && ReferenceEquals(claim, Get(process, "Claim")) && ReferenceEquals(setup, Get(process, "Setup")), kind + " retains exact process, acquisition and selection");
            Equal(searches, PresetSearches, kind + " never searches for a replacement preset");
            Equal(captures, PresetCaptures, kind + " never captures a replacement policy");
            Equal(gearSelections, InlineGearSelections, kind + " never reselects incoming gear");
            Equal(swaps, Get(typeof(Program), "Swaps"), kind + " never swaps Keep current gear");
            Equal(recalls, Get(typeof(Program), "Recalls"), kind + " never recalls baseline resources");
            Equal(writes, ResourceTransfers, kind + " never transfers baseline resources");
            if (kind == "retry") Equal(allocations, CallMain("GlobalPresetAllocationState"), "successful retry preserves baseline funding");
        }
        Console.WriteLine("Preset manual route acquisition: " + (Assertions - before) + " native assertions.");
    }

    private static List<string> PrepareRouteProcessWorld()
    {
        SetupInlineStagePlan();
        var settings = Get(MainType, "Settings");
        Set(settings, "_useCustomRebirthPlan", false);
        Set(settings, "_manualTitanEnabled", true); Set(settings, "_manualTitanZone", 16);
        Set(settings, "_manualTitanLoadout", -1); Set(settings, "_manualTitanRequireAT", true); Set(settings, "_manualTitanMaxDeaths", 2);
        Set(settings, "_manualAdventureRecoverHealth", false); Set(settings, "_manualAdventurePrecastBuffs", false);
        Set(settings, "_blacklistedBosses", new int[0]);
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        var idle = Child(controller, "idleAttackMove"); Set(idle, "character", Character); Set(idle, "pc", Get(controller, "playerController"));
        foreach (var name in new[] { "healMove", "hyperRegenMove" })
            Set(Child(Child(controller, name), "button"), "m_Interactable", false);
        Set(Character, "bossID", 200); Set(Character, "highestBoss", 200);
        Set(adventure, "waldoDefeats", 4); Set(adventure, "waldoFinds", 4); Set(adventure, "titan5Kills", 0);
        Set(adventure, "maxHP", 100f); Set(adventure, "autoattacking", false); Set(adventure, "curHP", 100f);
        var timer = Get(adventure, "boss5Spawn") ?? Child(adventure, "boss5Spawn");
        Set(timer, "totalseconds", 0d);
        CallMain("ObserveManualTitanOpportunities");
        Set(timer, "totalseconds", Convert.ToDouble(Invoke(controller, "boss5SpawnTime")) + 1);
        Set(Get(Character, "advancedTraining"), "levelTarget", new long[] { 10, 20, 0, 0, 0 });
        Set(Get(Character, "advancedTraining"), "level", new long[] { 10, 20, 0, 0, 0 });
        ((long[])Get(Get(Character, "training"), "_attackTraining"))[1] = 1;
        var log = Get(controller, "log"); var entries = new List<string> { new string("native encounter baseline".ToCharArray()) };
        Set(log, "Eventlog", entries);
        Set(MainType, "PlayerController", Get(controller, "playerController"));
        Set(MainInstance, "_combManager", New("CombatManager"));
        Set(MainInstance, "_customTitanGoldPendingIndex", -1); Set(MainInstance, "_customTitanGoldIndex", -1); Set(MainInstance, "_customTitanFightIndex", -1);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "adventure"), "m_GroupsAllowInteraction", true);
        Equal(true, CallMain("ManualTitanUnlocked", 4), "native Walderp manual unlock is configured");
        Check(Convert.ToDouble(Invoke(Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true), "TitanSpawnSecondsRemaining", 4)) <= 0,
            "native Walderp timer is due");
        return entries;
    }

    private static void CheckNativeManualTitanProcess()
    {
        PrepareRouteProcessWorld();
        var admitted = CallMain("TryRouteManualTitan");
        Equal(true, admitted, "actual manual Walderp process admits completed positive native AT: " + CallMain("GetManualTitanRuntime"));
        Equal(true, Get(MainInstance, "_manualTitanActive"), "actual manual titan claim remains active");
        Equal(16, Get(Get(Character, "adventure"), "zone"), "actual manual titan enters native Walderp route");
        var inventory = Get(Character, "inventory");
        var successor = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 551);
        CallMain("PublishExecutionBaseline", RouteBaseline(successor, "actual-process-B"));
        var writes = ResourceTransfers;
        Set(Get(Character, "adventure"), "titan5Kills", 1); // Native encounter outcome observation boundary.
        Equal(false, CallMain("TryRouteManualTitan"), "actual titan outcome retires process");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "actual titan route retires before successor gear");
        Equal(551, Get(Get(inventory, "head"), "id"), "actual process release installs CURRENT B");
        Equal(writes, ResourceTransfers, "actual process release does not clear unrelated resource lanes");
        var runtime = CallMain("GetManualTitanRuntime");
        Equal(true, Property(Json(runtime, "disarmed"), "AsBool"), "completed actual spawn stays disarmed");
        Equal(false, CallMain("TryRouteManualTitan"), "still-due completed spawn never renews attempt");
        var controller = Get(Character, "adventureController"); var timer = Get(Get(Character, "adventure"), "boss5Spawn");
        Set(timer, "totalseconds", 0d); CallMain("TryRouteManualTitan");
        Set(timer, "totalseconds", Convert.ToDouble(Invoke(controller, "boss5SpawnTime")) + 1);
        Equal(true, CallMain("TryRouteManualTitan"), "observed timer reset followed by due transition rearms actual spawn");
        CallMain("CancelManualTitan", "native check complete");
    }

    private static void CheckNativeManualTitanDeathHistory()
    {
        var entries = PrepareRouteProcessWorld();
        Equal(true, CallMain("TryRouteManualTitan"), "death-budget process starts");
        Set(MainInstance, "_manualTitanEnemyName", "WALDERP");
        entries.Add(new string("WALDERP has defeated you!".ToCharArray()));
        Set(Get(Character, "adventure"), "zone", -1);
        Equal(true, CallMain("ObserveManualTitanDeaths"), "continuous native death event is consumed once");
        var runtime = CallMain("GetManualTitanRuntime");
        Equal(1, Property(Json(runtime, "deaths"), "AsInt"), "native death consumes original budget");
        CallMain("ObserveManualTitanDeaths");
        Equal(1, Property(Json(CallMain("GetManualTitanRuntime"), "deaths"), "AsInt"), "same native death is not recounted");
        entries.Clear(); entries.Add("new history without the retained cursor");
        Equal(false, CallMain("TryRouteManualTitan"), "lost native history disarms actual process");
        Equal(true, Property(Json(CallMain("GetManualTitanRuntime"), "disarmed"), "AsBool"), "lost history cannot silently baseline/restart");
        Equal(false, CallMain("TryRouteManualTitan"), "lost-history retry does not renew original budget");
    }

    private static void CheckNativeManualTitanReadiness()
    {
        foreach (var kind in new[] { "zero", "partial", "power", "toughness", "health" })
        {
            PrepareRouteProcessWorld();
            var at = Get(Character, "advancedTraining"); var targets = (long[])Get(at, "levelTarget"); var levels = (long[])Get(at, "level");
            if (kind == "zero") targets[0] = targets[1] = 0;
            if (kind == "partial") targets[1] = 0;
            if (kind == "power") levels[1] = targets[1] - 1;
            if (kind == "toughness") levels[0] = targets[0] - 1;
            if (kind == "health")
            {
                Set(Get(MainType, "Settings"), "_manualAdventureRecoverHealth", true);
                Set(Get(Character, "adventure"), "curHP", 0f);
            }
            var entries = AdventureZoneEntries; var swaps = Get(typeof(Program), "Swaps");
            Equal(kind == "health", CallMain("TryRouteManualTitan"), kind + " actual manual titan readiness");
            Equal(-1, Get(Get(Character, "adventure"), "zone"), kind + " cannot enter native combat before prerequisites");
            Equal(entries, AdventureZoneEntries, kind + " no transient native combat entry");
            Equal(swaps, Get(typeof(Program), "Swaps"), kind + " Keep current never trials gear");
            if (kind == "health")
            {
                var claim = Get(Get(MainInstance, "_manualTitanProcess"), "Claim");
                Set(Get(Character, "adventure"), "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
                Equal(true, CallMain("TryRouteManualTitan"), "actual native HP recovery resumes retained manual process");
                Equal(16, Get(Get(Character, "adventure"), "zone"), "native HP readiness permits selected titan entry");
                Check(ReferenceEquals(claim, Get(Get(MainInstance, "_manualTitanProcess"), "Claim")), "HP recovery never renews acquisition");
                CallMain("CancelManualTitan", "readiness complete");
            }
        }
    }

    private static void CheckNativeRouteQuiescence()
    {
        PrepareRouteProcessWorld(); CallMain("TryRouteManualTitan");
        CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Quiescing"));
        CallMain("CancelManualTitan", "quiescing native check");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "bounded quiescing grant actually retires native route");
        Equal(false, Get(MainInstance, "_manualTitanActive"), "quiescing retirement actually finishes process");
        CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Running"));
    }

    private static void RestartRouteCoordinator()
    {
        // Replace only the injector coordinator. Native character, controllers, gear,
        // resources, timers and user settings survive unchanged, as on reinjection.
        MainInstance = Invoke(typeof(Program), "Blank", MainType);
        Set(typeof(Program), "MainInstance", MainInstance); Set(MainType, "reference", MainInstance);
        Set(MainInstance, "_combatPrepEndedRuns", new HashSet<string>());
        Set(MainInstance, "_resourceExecutionOwner", ""); Set(MainInstance, "_resourceExecutionReason", "");
        Set(MainInstance, "_customIronPillPrepStartedAt", -1d);
        foreach (var field in new[] { "_manualTitanIndex", "_customTitanGoldPendingIndex", "_customTitanGoldIndex", "_customTitanFightIndex" })
            Set(MainInstance, field, -1);
        Set(MainInstance, "_combManager", New("CombatManager"));
    }

    private static void SelectRestartFarm()
    {
        var settings = Get(MainType, "Settings");
        Set(settings, "_customAdventureEnabled", true); Set(settings, "_customAdventureGoldPush", false);
        Set(settings, "_customAdventureMode", 1); Set(settings, "_customAdventureZone", 0);
        Set(settings, "_customAdventureRules", Array.CreateInstance(Plugin.GetType("NGUInjector.AdventureRule", true), 0));
        Set(Get(Character, "adventure"), "autoattacking", true);
    }

    private static void CheckNativeRouteBusyRestart()
    {
        PrepareRouteProcessWorld(); SelectRestartFarm();
        var native = Get(Character, "bossController"); Invoke(native, "startNuke");
        Equal(true, Get(native, "isFighting"), "restart begins with actual native boss combat, not an imported claim");
        Equal(true, Get(native, "nukeBoss"), "native nuke is active at session loss");
        RestartRouteCoordinator();
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var routes = AdventureZoneEntries;
        var nukes = RouteNativeNukes; var fights = RouteNativeFights; var os = RouteNativeOsChanges;
        var gear = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout");
        var support = Property(CallMain("CaptureSupportState"), "StableKey"); var allocations = CapacityNativeMapKey(Character);
        Throws(() => CallMain("StartBossPush"), "busy", "new session cannot duplicate an existing native boss start");
        Throws(() => CallMain("StartMaxItopodPush", 2), "busy", "native boss blocks new bounded tower setup");
        var ordinary = CallMain("ObserveRouteGrant", -1);
        Equal(false, CallMain("ValidateExecutionGrant", ordinary, Domain("Route, NativeSettings")), "native busy flags deny ordinary setup authority without an imported claim");
        Check(((string)CallMain("CombatPrepStartBlocker", (object)null)).Contains("bossController.isFighting=true"), "actual native boss busy state blocks prep acquisition before OS/recall/setup");
        CallMain("CustomAdventureTick"); CallMain("CustomAdventureTick"); CallMain("BossPushTick");
        Equal(false, CallMain("TryRouteManualTitan"), "native boss blocks manual setup without an imported claim");
        Equal(false, CallMain("ExecutionOwnsDomain", Domain("Gear, Support, Route, NativeSettings")), "restart imports no prerequisite claim");
        Equal(nukes, RouteNativeNukes, "busy restart invokes no duplicate native nuke");
        Equal(fights, RouteNativeFights, "busy restart invokes no duplicate native fight");
        Equal(os, RouteNativeOsChanges, "busy restart invokes no OS reset");
        Equal(swaps, Get(typeof(Program), "Swaps"), "busy restart performs no gear setup");
        Equal(writes, ResourceTransfers, "busy restart performs no recall or funding");
        Equal(routes, AdventureZoneEntries, "busy restart performs no disruptive route retirement");
        Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), "busy restart preserves exact support");
        Equal(allocations, CapacityNativeMapKey(Character), "busy restart preserves all current allocations");
        Invoke(native, "nukeBosses");
        Equal(false, Get(native, "isFighting"), "native failed nuke retires itself after restart");
        Equal(false, Get(native, "nukeBoss"), "native retirement clears its own nuke flag");
        CallMain("CustomAdventureTick");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "ordinary enabled farm resumes from current native state when idle");
        Equal(true, Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "ExactLoadoutMatches", gear), "restart never restores historical gear");
        Equal(nukes, RouteNativeNukes, "ordinary resume does not restart cancelled boss request");
    }

    private static void CheckNativeManualTitanRestart()
    {
        PrepareRouteProcessWorld(); CallMain("TryRouteManualTitan");
        var oldCoordinator = MainInstance;
        var oldProcess = Get(MainInstance, "_manualTitanProcess"); var oldGrant = CallMain("RouteProcessGrant", oldProcess, false);
        SelectRestartFarm(); RestartRouteCoordinator();
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        Equal(false, CallMain("TryRouteManualTitan"), "startup-current-due manual titan receives no fresh budget");
        Equal(true, Property(Json(CallMain("GetManualTitanRuntime"), "disarmed"), "AsBool"), "interrupted spawn waits for a native reset");
        Equal(0, Property(Json(CallMain("GetManualTitanRuntime"), "remainingDeaths"), "AsInt"), "no old death cursor or budget is imported");
        Equal(false, CallMain("ValidateExecutionGrant", oldGrant, Domain("Route")), "old-session route grant has no current authority");
        var zone = Get(Get(Character, "adventure"), "zone"); var maps = CapacityNativeMapKey(Character);
        Invoke(oldCoordinator, "CustomAdventureTick"); Invoke(oldCoordinator, "CancelManualTitan", "late old-session callback");
        Equal(zone, Get(Get(Character, "adventure"), "zone"), "superseded coordinator callbacks cannot retire the new session's native route");
        Equal(maps, CapacityNativeMapKey(Character), "superseded coordinator callbacks cannot recall the new session's native resources");
        var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanEnabled", false); CallMain("TryRouteManualTitan");
        Set(settings, "_manualTitanMaxDeaths", 9); Set(settings, "_manualTitanEnabled", true);
        Equal(false, CallMain("TryRouteManualTitan"), "settings edits and off/on do not create a spawn opportunity");
        CallMain("CustomAdventureTick");
        Equal(0, Get(Get(Character, "adventure"), "zone"), "cancelled bounded titan does not poison ordinary farming");
        Equal(swaps, Get(typeof(Program), "Swaps"), "cancelled old titan never restores original gear"); Equal(writes, ResourceTransfers, "session cancellation never clears old resources");
        var adventure = Get(Character, "adventure"); var timer = Get(adventure, "boss5Spawn");
        var setTime = timer.GetType().GetMethod("setTime", new[] { typeof(double) });
        var observation = ((IDictionary)Get(MainInstance, "_manualTitanSpawns"))[4];
        Equal(false, Get(observation, "Started"), "startup disarm does not fabricate an acquired attempt");
        Equal(null, Get(observation, "Acquisition"), "startup disarm imports no acquisition");
        var kills = (int)Get(adventure, "titan5Kills");
        // Observe the native kill counter/reset outcome, not a replacement injector budget.
        Set(adventure, "titan5Kills", kills + 1); setTime.Invoke(timer, new object[] { 0d });
        Equal(false, CallMain("TryRouteManualTitan"), "observed reset alone is not a due opportunity");
        Equal(true, Get(observation, "ResetSeen"), "native counter/timer transition is observed before the next due spawn");
        Equal(true, Get(observation, "Disarmed"), "countdown retains the startup disarm");
        Equal(0, Property(Json(CallMain("GetManualTitanRuntime"), "remainingDeaths"), "AsInt"), "native reset grants no early death budget");
        Equal(null, Get(MainInstance, "_manualTitanProcess"), "native reset acquires no route before due");
        Equal(true, Invoke(Get(MainInstance, "_combManager"), "MoveToZone", -1), "ordinary farm retires through its actual native route grant");
        setTime.Invoke(timer, new object[] { Convert.ToDouble(Invoke(Get(Character, "adventureController"), "boss5SpawnTime")) + 1 });
        Check(Convert.ToDouble(Invoke(Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true), "TitanSpawnSecondsRemaining", 4)) <= 0,
            "the observed reset reaches the actual native due boundary");
        var resumed = CallMain("TryRouteManualTitan");
        Equal(true, resumed, "next observed native spawn permits one new bounded attempt; " + CallMain("GetManualTitanRuntime") + "; " + InlineObservedError);
        var process = Get(MainInstance, "_manualTitanProcess");
        Equal(kills + 1, Get(observation, "Kills"), "new attempt captures the current native opportunity counter");
        Check(!ReferenceEquals(oldProcess, process), "a new native spawn never revives the interrupted old process");
        var claim = Get(process, "Claim"); var acquisition = Get(claim, "Acquisition");
        var limit = Get(observation, "Limit"); var seconds = (double)Get(observation, "OwnedSeconds");
        resumed = CallMain("TryRouteManualTitan");
        Equal(true, resumed, "same opportunity continues its one acquired attempt; " + CallMain("GetManualTitanRuntime") + "; " + InlineObservedError);
        Check(ReferenceEquals(process, Get(MainInstance, "_manualTitanProcess")), "current-opportunity dedup retains exact process");
        Check(ReferenceEquals(claim, Get(process, "Claim")), "same opportunity retains its original claim");
        Equal(acquisition, Get(claim, "Acquisition"), "same opportunity never reacquires another attempt identity");
        Equal(limit, Get(observation, "Limit"), "same opportunity never replenishes its death limit");
        Check((double)Get(observation, "OwnedSeconds") >= seconds, "same opportunity charges time against the acquired attempt budget");
        CallMain("CancelManualTitan", "restart check complete");
    }

    private static void CheckNativeTowerRestart()
    {
        foreach (var request in new[] { "command", "mode" })
        {
            PrepareNativeTowerProcess(); var adventure = Get(Character, "adventure"); var settings = Get(MainType, "Settings");
            Set(settings, "_customAdventureMaxItopodDeaths", 2);
            Set(settings, "_customAdventureMode", 3);
            var start = Get(adventure, "itopodStart"); var end = Get(adventure, "itopodEnd"); var shifter = Get(Get(Character, "arbitrary"), "lazyITOPODOn");
            RestartRouteCoordinator(); var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var routes = AdventureZoneEntries;
            CallMain("CustomAdventureTick"); CallMain("CustomAdventureTick");
            Equal(0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "startup-selected tower mode cannot renew old budget");
            Equal(null, Get(MainInstance, "_maxItopodProcess"), "restart imports no tower field-return or gear claim");
            Equal(start, Get(adventure, "itopodStart"), "restart cannot restore old original tower start"); Equal(end, Get(adventure, "itopodEnd"), "restart cannot restore old original tower end");
            Equal(shifter, Get(Get(Character, "arbitrary"), "lazyITOPODOn"), "restart imports no shifter return authority");
            Equal(3, Property(settings, "CustomAdventureMode"), "budget cancellation never writes user desired mode");
            Set(settings, "_customAdventureEnabled", false); CallMain("CustomAdventureTick");
            Set(settings, "_customAdventureEnabled", true); CallMain("CustomAdventureTick");
            Equal(0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "enable toggle alone does not replenish startup tower budget");
            Equal(swaps, Get(typeof(Program), "Swaps"), "tower restart makes no historical gear return"); Equal(writes, ResourceTransfers, "tower restart makes no resource change");
            Equal(routes, AdventureZoneEntries, "tower restart makes no old-claim route cleanup");
            if (request == "command") CallMain("StartMaxItopodPush", 2);
            else
            {
                Set(settings, "_customAdventureMode", 0); CallMain("SynchronizeMaxItopodAdventureMode");
                Set(settings, "_customAdventureMode", 3); CallMain("SynchronizeMaxItopodAdventureMode");
            }
            Equal(2, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "new explicit " + request + " acquires a new bounded request");
            CallMain("CancelMaxItopodPush"); CallMain("SynchronizeMaxItopodAdventureMode");
            Equal(0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "cancelled new request cannot automatically renew its budget");
            Equal(3, Property(settings, "CustomAdventureMode"), "cancellation preserves selected tower mode");
            SelectRestartFarm(); CallMain("CustomAdventureTick");
            Equal(0, Get(adventure, "zone"), "later ordinary farm runs without importing old tower cleanup authority");
            Equal(0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "ordinary farming never replenishes cancelled tower budget");
        }
    }

    private static void PrepareNativeTowerProcess(int start = 3, int end = 4)
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true);
        Set(settings, "_customAdventureMode", 0); Set(settings, "_manualAdventureEnabled", false);
        var adventure = Get(Character, "adventure");
        Set(adventure, "autoattacking", true); Set(adventure, "highestItopodLevel", 60);
        Set(adventure, "itopodStart", start); Set(adventure, "itopodEnd", end);
        Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        CallMain("StartMaxItopodPush", 2);
        Equal(true, CallMain("TryRouteMaxItopodPush"), "actual bounded tower process routes");
        Equal(1000, Get(adventure, "zone"), "actual tower process enters native ITOPOD");
    }

    private static void CheckNativeTowerProcess()
    {
        foreach (var kind in new[] { "return", "disable", "quiesce", "foreign", "zero" })
        {
            PrepareNativeTowerProcess(kind == "zero" ? 0 : 3, kind == "zero" ? 0 : 4);
            var adventure = Get(Character, "adventure"); var settings = Get(MainType, "Settings");
            var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
            var writes = ResourceTransfers;
            var pushedStart = Get(adventure, "itopodStart");
            if (kind == "disable") Set(settings, "_customAdventureEnabled", false);
            if (kind == "quiesce") CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Quiescing"));
            if (kind == "foreign") Set(adventure, "itopodEnd", 123);
            CallMain("CancelMaxItopodPush");
            if (kind == "foreign")
            {
                Equal(123, Get(adventure, "itopodEnd"), "foreign borrowed tower field is not overwritten");
                Equal(1000, Get(adventure, "zone"), "foreign tower activity is not adopted for retirement");
                Check(ReferenceEquals(head, Get(inventory, "head")), "foreign tower prerequisites are retained");
            }
            else
            {
                Equal(-1, Get(adventure, "zone"), kind + " actual native tower route retires before return");
                Equal(kind == "zero" ? pushedStart : 3, Get(adventure, "itopodStart"), kind + " narrow owned field return is independent of forward enable preference");
                Equal(false, Get(MainInstance, "_maxItopodPushRouting"), kind + " actual tower retirement completes");
            }
            Equal(writes, ResourceTransfers, kind + " tower-only retirement never clears baseline resources");
            if (kind == "quiesce") CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Running"));
        }
    }

    private static void CheckNativeAdventureGoldProcess()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true); Set(settings, "_customAdventureGoldPush", true);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        Set(Get(Character, "adventure"), "autoattacking", true);
        Set(Get(Character, "adventure"), "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        ((long[])Get(Get(Character, "training"), "_attackTraining"))[1] = 0;
        Equal(true, CallMain("TryRouteCustomAdventureGoal"), "actual adventure-gold process enters selected boss route");
        Equal(true, Get(MainInstance, "_customAdventureGoldPushActive"), "actual gold setup is retained");
        Set(Get(Character, "adventure"), "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        CallMain("TryRouteCustomAdventureGoal");
        Equal(Get(MainInstance, "_customAdventureGoldPushZone"), Get(Get(Character, "adventure"), "zone"), "gold entered native route");
        var controller = Get(Character, "adventureController");
        var enemy = Child(controller, "currentEnemy");
        Set(enemy, "name", "native gold boss"); Set(enemy, "enemyType", Enum.Parse(Field(enemy, "enemyType").FieldType, "boss"));
        CallMain("TryRouteCustomAdventureGoal");
        Equal(true, Get(Get(MainInstance, "_combManager"), "_isFighting"), "actual gold manager observes native encounter; zone="
            + Get(Get(Character, "adventure"), "zone") + " HP=" + Get(Get(Character, "adventure"), "curHP"));
        Set(controller, "currentEnemy", null); // Native encounter outcome boundary, not a fake completion callback.
        var inventory = Get(Character, "inventory");
        var b = ((IList)Get(inventory, "inventory")).Cast<object>().FirstOrDefault(x => (int)Get(x, "id") == 551);
        if (b == null) b = Get(inventory, "head");
        CallMain("PublishExecutionBaseline", RouteBaseline(b, "gold-current-B"));
        CallMain("TryRouteCustomAdventureGoal");
        Equal(false, Get(MainInstance, "_customAdventureGoldPushActive"), "actual CombatManager outcome retires actual gold process");
        Check(ReferenceEquals(b, Get(inventory, "head")), "actual gold completion returns directly to CURRENT B");
    }

    private static void CheckNativeTitanGoldUpgrade()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings"); var adventure = Get(Character, "adventure");
        var controller = Get(Character, "adventureController");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_manageGoldLoadouts", true);
        Set(Get(Character, "settings"), "autoKillTitans", true);
        Set(adventure, "attack", 4000f); Set(adventure, "defense", 3000f); Set(adventure, "regen", 10f);
        var targets = new bool[12]; targets[0] = true;
        Set(settings, "_titanGoldTargets", targets); Set(settings, "_titanMoneyDone", new bool[12]);
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var gold = items.Cast<object>().Single(x => (int)Get(x, "id") == 550);
        Set(gold, "spec1Type", Enum.Parse(Field(gold, "spec1Type").FieldType, "GoldDropAmount")); Set(gold, "spec1Cur", 1000000f);
        var timer = Get(adventure, "boss1Spawn") ?? Child(adventure, "boss1Spawn");
        Set(timer, "totalseconds", Convert.ToDouble(Invoke(controller, "boss1SpawnTime")) - 2);
        Equal(true, CallMain("TryRouteCustomTitanGoldKill"), "actual pre-spawn titan gold process starts");
        Equal(true, Get(MainInstance, "_customTitanGoldActive"), "actual titan batch remains admitted");
        var started = Get(MainInstance, "_customTitanGoldStartedAt");
        targets[1] = true;
        var second = Get(adventure, "boss2Spawn") ?? Child(adventure, "boss2Spawn");
        Set(second, "totalseconds", Convert.ToDouble(Invoke(controller, "boss2SpawnTime")) - 2);
        Equal(false, Invoke(Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true), "TitanAutokillAchieved", 1), "stronger overlapping target needs a new pure selection");
        CallMain("TryRouteCustomTitanGoldKill"); CallMain("TryRouteCustomTitanGoldKill");
        var admitted = (IDictionary)Get(MainInstance, "_customTitanGoldSpawns");
        Check(admitted.Contains(0) && admitted.Contains(1), "pure retained upgrade preserves old and admits stronger obligations");
        Equal(started, Get(MainInstance, "_customTitanGoldStartedAt"), "upgrade never renews original stall deadline");
        Equal(true, Invoke(Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true), "TitanAutokillAchieved", 0), "old native autokill obligation still verifies");
        Equal(true, Invoke(Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true), "TitanAutokillAchieved", 1), "stronger native autokill obligation verifies after one selected application");
        Set(timer, "totalseconds", 0d); Set(second, "totalseconds", 0d);
        CallMain("TryRouteCustomTitanGoldKill");
        Equal(false, Get(MainInstance, "_customTitanGoldActive"), "observed native spawn resets complete actual titan batch");
    }

    private static void CheckNativeFightPreparationHandoff()
    {
        PrepareRouteProcessWorld();
        var model = Character; var runtime = MainInstance;
        var claim = CallMain("BeginExecutionClaim", "fight-preparation", "retained-fight-entry",
            Domain("Gear, Support, Route, FiniteAction, Inventory"), (Func<bool>)(() => ReferenceEquals(model, Character) && ReferenceEquals(runtime, MainInstance)));
        var gear = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout");
        var support = Property(CallMain("CaptureSupportState"), "Selection");
        var setup = CallMain("AcquireFiniteActionSetup", claim, gear, support, false, false);
        var attempt = Activator.CreateInstance(MainType.GetNestedType("FightPreparationAttempt", All), true);
        var identity = Activator.CreateInstance(Plugin.GetType("NGUInjector.FiniteActionIdentity", true), All, null,
            new object[] { "fight-preparation", "fight-preparation", "retained-fight-entry", "entry-1", "manual-prep" }, null);
        Set(attempt, "Identity", identity); Set(attempt, "Settings", Get(MainType, "Settings"));
        Set(attempt, "PlanFile", Property(Get(MainType, "Settings"), "CustomPlanFile"));
        Set(MainInstance, "_fightPreparationClaim", claim); Set(MainInstance, "_fightPreparationSetup", setup);
        Set(MainInstance, "_fightPreparationAttempt", attempt); Set(MainInstance, "_fightPreparationStarted", true);
        Equal(true, Property(MainInstance, "FightPreparationVerified"), "real finite preparation setup verifies before boss admission");
        var swaps = Get(typeof(Program), "Swaps"); var searches = InlineGearSelections;
        CallMain("StartBossPush");
        var process = Get(MainInstance, "_bossPushProcess");
        Check(ReferenceEquals(claim, Get(process, "Claim")), "boss consumes the exact existing fight-preparation claim");
        Check(ReferenceEquals(setup, Get(process, "Setup")), "boss consumes exact retained fight setup without a new selection");
        Equal(searches, InlineGearSelections, "prepared boss admission has no extra equipment search");
        Equal(swaps, Get(typeof(Program), "Swaps"), "prepared boss admission has no equipment reapplication");
        var native = Get(Character, "bossController"); Set(native, "isFighting", true);
        var grant = CallMain("RouteProcessGrant", process, false);
        Equal(true, CallMain("ValidateExecutionGrant", grant, Domain("FiniteAction")), "active native boss does not revoke its retained prerequisites");
        CallMain("StopBossPush");
        Equal(true, Property(MainInstance, "BossPushRunning"), "stop waits for actual native fight retirement");
        Check(ReferenceEquals(claim, Property(MainInstance, "FightPreparationClaim")), "pending stop never withdraws preparation claim");
        Set(native, "isFighting", false);
        CallMain("StopBossPush");
        Equal(false, Property(MainInstance, "BossPushRunning"), "observed native retirement completes boss process");
        Equal(null, Property(MainInstance, "FightPreparationClaim"), "preparation completes only after actual native retirement");
    }

    private static void CheckNativeTemporaryRouteHandoff()
    {
        PrepareNativeTowerProcess();
        var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanEnabled", true);
        ((long[])Get(Get(Character, "training"), "_attackTraining"))[1] = 0;
        var inventory = Get(Character, "inventory");
        var b = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 551);
        CallMain("PublishExecutionBaseline", RouteBaseline(b, "temporary-handoff-current-B"));
        var writes = ResourceTransfers; var seen = new List<int>(); var searches = InlineGearSelections;
        GearRenderObserver = () => seen.Add((int)Get(Get(inventory, "head"), "id"));
        try { Equal(true, CallMain("TryRouteManualTitan"), "actual manual titan preempts exact owned tower process"); }
        finally { GearRenderObserver = null; }
        Equal(16, Get(Get(Character, "adventure"), "zone"), "temporary successor enters only after owned tower retirement");
        Check(!seen.Contains(551), "temporary-to-temporary handoff never flashes current baseline B between owners");
        Equal(writes, ResourceTransfers, "temporary-to-temporary handoff adds no resource clear");
        Equal(searches, InlineGearSelections, "keep-current manual successor adds no optimizer search");
        CallMain("CancelManualTitan", "finish temporary handoff check");
        Check(ReferenceEquals(b, Get(inventory, "head")), "final temporary exit installs CURRENT B directly");
        CallMain("CancelMaxItopodPush");
    }

    private static void CheckNativeManualTitanCallbackFailures()
    {
        foreach (var kind in new[] { "permission", "epoch", "foreign-route" })
        {
            PrepareRouteProcessWorld();
            var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1);
            var calls = 0; var writes = ResourceTransfers;
            GearRenderObserver = () => {
                if (++calls != 1) return;
                if (kind == "permission") Set(settings, "_manualTitanEnabled", false);
                else if (kind == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
                else Set(Get(Character, "adventure"), "zone", 0);
            };
            try { Equal(false, CallMain("TryRouteManualTitan"), kind + " callback rejects dependent actual titan combat"); }
            finally { GearRenderObserver = null; }
            Check(calls > 0, kind + " failure was injected at a real native gear presentation callback");
            Equal(kind == "foreign-route" ? 0 : -1, Get(Get(Character, "adventure"), "zone"), kind + " callback cannot activate or retire a foreign route");
            Equal(writes, ResourceTransfers, kind + " callback failure never clears unrelated resources");
            Equal(false, Get(MainInstance, "_manualTitanActive"), kind + " failed process retires its authority without historical gear rollback");
        }
    }

    private static void CheckNativeTowerSetupFailure()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true);
        Set(settings, "_manualAdventureEnabled", true); Set(settings, "_manualAdventureLoadout", 1);
        CallMain("StartMaxItopodPush", 2);
        var calls = 0; var writes = ResourceTransfers;
        GearRenderObserver = () => { if (++calls == 1) Set(settings, "_customAdventureEnabled", false); };
        try { CallMain("CustomAdventureTick"); }
        finally { GearRenderObserver = null; }
        Check(calls > 0, "tower failure reaches actual native gear callback");
        Equal(0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "failed tower acquisition is cancelled, not renewed");
        Equal(null, Get(MainInstance, "_maxItopodProcess"), "failed pre-routing acquisition retires its process");
        Equal(false, CallMain("ExecutionOwnsDomain", Domain("Gear, Support, Route, NativeSettings")), "failed tower acquisition cannot leak actual executor authority");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "failed tower setup cannot enter native combat");
        Equal(writes, ResourceTransfers, "failed tower setup never clears resource work");
    }

    private static void CheckNativeTowerSupportGate()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true);
        Set(settings, "_manualAdventureEnabled", true); Set(settings, "_manualAdventureLoadout", -1);
        SetActiveBeards();
        CallMain("StartMaxItopodPush", 2);
        var supportWrites = 0; var entries = AdventureZoneEntries;
        BeardRefreshInterference = () => { supportWrites++; Set(settings, "_manualAdventureEnabled", false); };
        try { CallMain("CustomAdventureTick"); }
        finally { BeardRefreshInterference = null; }
        Check(supportWrites > 0, "manual tower reaches actual shared support application");
        Equal(entries, AdventureZoneEntries, "unverified support cannot admit dependent tower entry");
        Equal(-1, Get(Get(Character, "adventure"), "zone"), "support failure remains outside combat");
        Equal(false, Get(MainInstance, "_maxItopodPushRouting"), "unverified support never becomes a running tower route");
        Equal(false, CallMain("ExecutionOwnsDomain", Domain("Gear, Support, Route, NativeSettings")), "support failure retires original authority without rollback");
    }

    private static void CheckNativeTowerPendingSetupRetirement()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true);
        Set(settings, "_manualAdventureEnabled", true); Set(settings, "_manualAdventureLoadout", -1);
        CallMain("StartMaxItopodPush", 2);
        // Stop between the actual admission and setup steps: this real claim is
        // deliberately unapplied, not a fabricated successful setup receipt.
        var gear = Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "CaptureExactLoadout");
        var process = CallMain("BeginRouteProcess", "max-itopod", gear, false, Domain("Gear, Support, Route, NativeSettings"), null);
        Set(MainInstance, "_maxItopodProcess", process);
        var native = Get(Character, "bossController"); var writes = ResourceTransfers;
        Invoke(native, "startNuke"); CallMain("CancelMaxItopodPush");
        Equal(true, Get(native, "nukeBoss"), "native user nuke really starts before setup application");
        Check(ReferenceEquals(process, Get(MainInstance, "_maxItopodProcess")), "unapplied acquisition retains pending native retirement process");
        var claim = Get(process, "Claim"); Equal(false, Get(claim, "Ended"), "active native nuke retains exact prerequisite claim");
        Throws(() => CallMain("StartMaxItopodPush", 2), "busy", "pending native retirement cannot acquire a fresh death budget");
        var swaps = Get(typeof(Program), "Swaps"); CallMain("CancelMaxItopodPush");
        Equal(swaps, Get(typeof(Program), "Swaps"), "pending native activity cannot trigger historical equipment return");
        Equal(true, Get(native, "isFighting"), "cancellation never clears native activity as a substitute for retirement");
        Invoke(native, "nukeBosses"); Equal(false, Get(native, "isFighting"), "native failed nuke retires its own activity");
        CallMain("CancelMaxItopodPush");
        Equal(null, Get(MainInstance, "_maxItopodProcess"), "observed native completion finishes pending pre-routing retirement");
        Equal(true, Get(claim, "Ended"), "native completion retires original actual claim");
        Equal(writes, ResourceTransfers, "pending setup retirement never clears unrelated resource work");
    }

    private static void CheckNativeTowerDeathBudget()
    {
        PrepareNativeTowerProcess();
        var process = Get(MainInstance, "_maxItopodProcess");
        var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
        var entries = (IList)Get(Get(controller, "log"), "Eventlog");
        entries.Add(new string("native tower enemy has defeated you!".ToCharArray()));
        Set(adventure, "zone", -1); Set(adventure, "curHP", 0f);
        CallMain("ObserveMaxItopodPushDeaths");
        Equal(1, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "one continuous native death consumes exactly one original budget");
        CallMain("ObserveMaxItopodPushDeaths");
        Equal(1, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "same native event never recounts");
        CallMain("TryRouteMaxItopodPush");
        Equal(-1, Get(adventure, "zone"), "post-death tower waits for real native HP recovery");
        Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        CallMain("TryRouteMaxItopodPush");
        Equal(1000, Get(adventure, "zone"), "verified native HP resumes bounded tower");
        Equal(1, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "reentry does not refill deaths");
        entries.Clear(); entries.Add("retained cursor lost");
        CallMain("ObserveMaxItopodPushDeaths");
        Equal(0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "lost log disarms rather than baselining old deaths");
        Equal(false, Get(MainInstance, "_maxItopodPushRouting"), "lost-history tower retires safely");
        Equal(true, Get(Get(process, "Claim"), "Ended"), "original tower authority ended before reentry");
    }

    private static void CheckNativeManualTowerDisable()
    {
        foreach (var adventureEnabled in new[] { false, true })
        {
            PrepareRouteProcessWorld();
            var settings = Get(MainType, "Settings"); var adventure = Get(Character, "adventure");
            var idle = Child(Get(Character, "adventureController"), "idleAttackMove"); Set(idle, "character", Character);
            Set(idle, "pc", Get(Get(Character, "adventureController"), "playerController"));
            Set(Get(idle, "pc"), "character", Character);
            Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", adventureEnabled);
            Set(settings, "_manualAdventureEnabled", true); Set(settings, "_manualAdventureLoadout", -1);
            Set(adventure, "autoattacking", false); Set(adventure, "highestItopodLevel", 60);
            Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
            CallMain("StartMaxItopodPush", 2); CallMain("CustomAdventureTick");
            Equal(1000, Get(adventure, "zone"), "manual tower enters with verified setup");
            Equal(false, Get(adventure, "autoattacking"), "manual tower begins in native manual mode");
            Set(settings, "_manualAdventureEnabled", false); InlineObservedError = null; CallMain("CustomAdventureTick");
            Equal(true, Get(adventure, "autoattacking"), "explicit manual disable returns native idle once under actual route authority: " + InlineObservedError);
            Equal(adventureEnabled ? 2 : 0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "disable preserves or cancels original tower budget");
            if (!adventureEnabled)
            {
                Equal(false, Get(MainInstance, "_maxItopodPushRouting"), "fully disabled tower retires");
                Equal(false, CallMain("ExecutionOwnsDomain", Domain("Gear, Support, Route, NativeSettings")), "fully disabled tower releases actual authority");
                Set(adventure, "autoattacking", false); CallMain("CustomAdventureTick");
                Equal(false, Get(adventure, "autoattacking"), "consumed disable never overwrites a later native/manual preference");
            }
            CallMain("CancelMaxItopodPush");
        }
    }

    private static void CheckNativeTowerModelReplacement()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true);
        Set(Get(Character, "adventure"), "highestItopodLevel", 60); Set(Get(Character, "adventure"), "autoattacking", true);
        CallMain("StartMaxItopodPush", 2);
        var before = ResourceTransfers; object foreign = null; var calls = 0;
        InputInterference = (input, text) => {
            if (++calls != 1) return;
            InputInterference = null;
            foreign = Activator.CreateInstance(Get(Character, "adventure").GetType());
            Set(foreign, "zone", 4); Set(foreign, "itopodStart", 123); Set(foreign, "itopodEnd", 124);
            Set(Character, "adventure", foreign);
        };
        try { CallMain("CustomAdventureTick"); } finally { InputInterference = null; }
        Check(calls > 0, "tower model replacement occurs at actual native input callback");
        Check(ReferenceEquals(foreign, Get(Character, "adventure")), "foreign adventure model is never restored over");
        Equal(4, Get(foreign, "zone"), "foreign route not adopted for native cancellation");
        Equal(123, Get(foreign, "itopodStart"), "foreign start never overwritten by borrowed return");
        Equal(124, Get(foreign, "itopodEnd"), "foreign end never overwritten by borrowed return");
        Equal(false, Get(MainInstance, "_maxItopodPushRouting"), "foreign-model tower retires old authority");
        Equal(0, Get(MainInstance, "_maxItopodPushRemainingDeaths"), "foreign-model attempt cannot restart with renewed deaths");
        Equal(before, ResourceTransfers, "foreign-model cancellation never clears unrelated work");
    }

    private static void PrepareAdventureGoldReview()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanEnabled", false); Set(settings, "_customAdventureEnabled", true); Set(settings, "_customAdventureGoldPush", true);
        Set(settings, "_customAdventureMode", 1); Set(settings, "_customAdventureZone", 0);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        var adventure = Get(Character, "adventure"); Set(adventure, "autoattacking", true);
        Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        ((long[])Get(Get(Character, "training"), "_attackTraining"))[1] = 0;
        CallMain("TryRouteCustomAdventureGoal");
        Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
        CallMain("TryRouteCustomAdventureGoal");
        Equal(true, Get(MainInstance, "_customAdventureGoldPushEnteredZone"), "review fixture enters actual gold attempt after native HP readiness");
    }

    private static void CheckNativeAdventureGoldDeath()
    {
        PrepareAdventureGoldReview();
        var process = Get(MainInstance, "_customAdventureGoldProcess"); var claim = Get(process, "Claim");
        var original = (int)Get(MainInstance, "_customAdventureGoldPushHighestZone");
        var adventure = Get(Character, "adventure"); var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        // Actual native death observation, not authority granted by a boolean.
        Set(adventure, "zone", -1); Set(adventure, "curHP", 0f);
        CallMain("TryRouteCustomAdventureGoal");
        Equal(true, Get(MainInstance, "_customAdventureGoldPushActive"), "native death retains actual gold process for bounded lower-zone fallback");
        Check(ReferenceEquals(claim, Get(Get(MainInstance, "_customAdventureGoldProcess"), "Claim")), "death never replaces acquired gold claim");
        var lower = (int)Get(MainInstance, "_customAdventureGoldPushZone");
        Check(lower < original && !(bool)Invoke(Plugin.GetType("NGUInjector.Managers.ZoneHelpers", true), "ZoneIsTitan", lower), "fallback skips titan zones");
        Equal(-1, Get(adventure, "zone"), "fallback waits for real HP recovery");
        CallMain("TryRouteCustomAdventureGoal"); Equal(lower, Get(MainInstance, "_customAdventureGoldPushZone"), "healing is not another death");
        Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP"))); CallMain("TryRouteCustomAdventureGoal");
        Equal(lower, Get(adventure, "zone"), "healed actual process enters lower boss route");
        Equal(swaps, Get(typeof(Program), "Swaps"), "death/fallback never re-equips or restores original gear");
        Equal(writes, ResourceTransfers, "death/fallback never clears baseline work");
        CallMain("CancelCustomAdventureGoldPush", "fallback complete");
    }

    private static void CheckNativeAdventureGoldCleanup()
    {
        foreach (var kind in new[] { "disable", "cutoff", "foreign", "partial" })
        {
            if (kind == "partial")
            {
                PrepareRouteProcessWorld();
                var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanEnabled", false);
                Set(settings, "_customAdventureEnabled", true); Set(settings, "_customAdventureGoldPush", true);
                Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
                var calls = 0;
                GearRenderObserver = () => { if (++calls == 1) Set(settings, "_customAdventureEnabled", false); };
                try { CallMain("CustomAdventureTick"); } finally { GearRenderObserver = null; }
                Check(calls > 0, "gold setup boundary reached actual gear application");
                Equal(false, Get(MainInstance, "_customAdventureGoldPushActive"), "unverified gold application retires its owner");
                Equal(false, CallMain("ExecutionOwnsDomain", Domain("Gear, Support, Route, NativeSettings")), "partial gold setup leaks no actual claim");
                Equal(-1, Get(Get(Character, "adventure"), "zone"), "partial gold setup cannot enter combat");
                continue;
            }
            PrepareAdventureGoldReview();
            var adventure = Get(Character, "adventure");
            var process = Get(MainInstance, "_customAdventureGoldProcess"); var selected = Get(process, "Setup");
            var claim = Get(process, "Claim"); var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
            if (kind == "disable") Set(Get(MainType, "Settings"), "_customAdventureEnabled", false);
            if (kind == "cutoff") Set(Get(Character, "rebirthTime"), "totalseconds", 3600d);
            if (kind == "foreign") Set(adventure, "zone", 4);
            CallMain("TryRouteCustomAdventureGoal");
            Equal(false, Get(MainInstance, "_customAdventureGoldPushActive"), kind + " gold owner retired");
            Equal(true, Get(claim, "Ended"), kind + " actual claim ended");
            Equal(swaps, Get(typeof(Program), "Swaps"), kind + " no-successor cleanup never restores historical gear");
            Equal(writes, ResourceTransfers, kind + " cleanup no resource withdrawal");
            if (kind == "foreign")
            {
                Equal(4, Get(adventure, "zone"), "foreign route cannot be overwritten by cleanup/fallback");
                var routes = AdventureZoneEntries;
                CallMain("TryRouteCustomAdventureGoal"); CallMain("CustomAdventureTick");
                Equal(4, Get(adventure, "zone"), "later ordinary ticks cannot adopt unresolved foreign route authority");
                Equal(routes, AdventureZoneEntries, "unresolved route conflict prevents all native fallback entries");
            }
            else Equal(true, Invoke(Plugin.GetType("NGUInjector.Managers.LoadoutManager", true), "ExactLoadoutMatches", Get(selected, "Gear")), kind + " no successor preserves actual selected map");
        }
    }

    private static void CheckNativeBossInvocation()
    {
        PrepareRouteProcessWorld();
        var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanEnabled", false);
        var native = Get(Character, "bossController");
        CallMain("StartBossPush"); CallMain("BossPushTick");
        Equal(true, Get(native, "isFighting"), "actual production finite action calls native startNuke/beginFight");
        Equal(true, Get(native, "nukeBoss"), "native startNuke, not a model flag assignment, admits nuke");
        var process = Get(MainInstance, "_bossPushProcess"); var claim = Get(process, "Claim");
        var inventory = Get(Character, "inventory");
        var b = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 551);
        CallMain("PublishExecutionBaseline", RouteBaseline(b, "native-boss-current-B"));
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers;
        CallMain("StopBossPush");
        Equal(true, Property(MainInstance, "BossPushRunning"), "pending stop retains actual native activity");
        Equal(false, Get(claim, "Ended"), "pending native nuke retains exact prerequisite authority");
        Equal(swaps, Get(typeof(Program), "Swaps"), "pending native nuke never installs successor prerequisites early");
        // Real failed native stat comparison retires its own fight/nuke flags.
        // No cancellation method or model-state clearing is invented by this test.
        Invoke(native, "nukeBosses");
        Equal(false, Get(native, "isFighting"), "actual failed native nuke retires combat");
        Equal(false, Get(native, "nukeBoss"), "actual native nuke outcome clears its own flag");
        CallMain("BossPushTick");
        Equal(false, Property(MainInstance, "BossPushRunning"), "producer observes native completion and finishes pending stop");
        Equal(true, Get(claim, "Ended"), "native completion ends actual prerequisite claim");
        Check(ReferenceEquals(b, Get(inventory, "head")), "only after native completion installs CURRENT B");
        Equal(writes, ResourceTransfers, "native boss retirement adds no resource clear");
    }

    private static void CheckNativeBossUncertainty()
    {
        PrepareRouteProcessWorld(); Set(Get(MainType, "Settings"), "_manualTitanEnabled", false);
        CallMain("StartBossPush");
        RouteNativeActionReturned = name => {
            if (name != "startNuke") return;
            RouteNativeActionReturned = null;
            CallMain("InvalidateExecutionObservations", Domain("Route"));
        };
        var nukes = RouteNativeNukes;
        try { CallMain("BossPushTick"); }
        finally { RouteNativeActionReturned = null; }
        Equal(nukes + 1, RouteNativeNukes, "uncertain callback follows one actual native invocation");
        Equal(true, Get(MainInstance, "_bossPushStopping"), "lost callback authority retires the current bounded boss attempt");
        CallMain("BossPushTick"); CallMain("BossPushTick");
        Equal(nukes + 1, RouteNativeNukes, "uncertain attempt is not replayed while native combat is busy");
        Invoke(Get(Character, "bossController"), "nukeBosses"); CallMain("BossPushTick");
        Equal(false, Property(MainInstance, "BossPushRunning"), "native retirement finishes cancelled uncertain attempt");
        CallMain("StartBossPush");
        var process = Get(MainInstance, "_bossPushProcess"); CallMain("ApplyRouteProcess", process);
        var stale = CallMain("RouteProcessGrant", process, false);
        CallMain("InvalidateExecutionObservations", Domain("Route"));
        var kind = Enum.Parse(MainType.GetNestedType("RouteActionKind", All), "BossNuke");
        var rejected = CallMain("InvokeRouteAction", "uninvoked-stale-grant", stale, kind, 0);
        Equal(false, Get(rejected, "Invoked"), "runner reports no invocation for a stale grant");
        Equal(false, Property(rejected, "Verified"), "stale grant is never a verified effect");
        Equal(nukes + 1, RouteNativeNukes, "rejected preflight reaches no native start");
        CallMain("BossPushTick");
        Equal(nukes + 2, RouteNativeNukes, "new explicit attempt can invoke the same kind after uncertainty");
        CallMain("BossPushTick"); Equal(nukes + 2, RouteNativeNukes, "current native opportunity deduplicates repeated ticks");
        CallMain("StopBossPush"); Invoke(Get(Character, "bossController"), "nukeBosses"); CallMain("BossPushTick");
    }

    private static void CheckNativeWalderpAttack()
    {
        foreach (var says in new[] { false, true })
        {
            PrepareRouteProcessWorld(); CallMain("TryRouteManualTitan");
            var adventure = Get(Character, "adventure"); var controller = Get(Character, "adventureController");
            var pc = Get(controller, "playerController"); var ai = Get(controller, "enemyAI");
            Set(pc, "character", Character); Set(pc, "ac", controller); Set(pc, "log", Get(controller, "log"));
            Set(pc, "offenseBuffFactor", 1f); Set(pc, "offenseDebuffFactor", 1f); Set(pc, "chargeFactor", 1f); Invoke(pc, "set_moveTimer", 0f);
            Set(controller, "regAttackMulti", 1f); Set(adventure, "attack", 10000f);
            var enemy = Child(controller, "currentEnemy");
            Set(enemy, "name", "WALDERP"); Set(enemy, "enemyType", Enum.Parse(Field(enemy, "enemyType").FieldType, "bigBoss5"));
            Set(enemy, "maxHP", 1000000f); Set(enemy, "curHP", 1000000f); Set(enemy, "defense", 0f);
            Set(ai, "character", Character); Set(ai, "ac", controller); Set(ai, "player", pc); Set(ai, "log", Get(controller, "log"));
            Set(ai, "defenseFactor", 1f); Set(ai, "inWaldoSaysLoop", true); Set(ai, "waldoSays", says);
            Set(ai, "waldoAttackID", says ? 3 : 6); Set(ai, "lastAttackID", 0);
            foreach (var name in new[] { "regularAttackMove", "strongAttackMove", "pierceMove", "ultimateAttackMove" })
            {
                var move = Child(controller, name); Set(move, "character", Character); Set(move, "pc", pc);
                var button = Child(move, "button"); Set(button, "m_Interactable", true); Set(button, "m_GroupsAllowInteraction", true);
            }
            var claim = Get(Get(MainInstance, "_manualTitanProcess"), "Claim");
            Equal(true, CallMain("TryRouteManualTitan"), "actual producer answers native Walderp prompt");
            Check((float)Get(enemy, "curHP") < 1000000f && (float)Get(enemy, "curHP") > 0, "real native attack/damage changes enemy HP, says=" + says);
            Equal(false, Get(ai, "inWaldoSaysLoop"), "native takeDamage/didCorrectWaldoMove accepts actual response, says=" + says);
            Equal(0, Get(ai, "waldoAttackID"), "native AI consumes prompt identity itself");
            Equal(1f, Property(pc, "moveTimer"), "native move starts actual shared cooldown");
            Equal(false, Property(Get(Get(controller, "regularAttackMove"), "button"), "interactable"), "native move consumes actual button readiness");
            Equal(true, ReferenceEquals(claim, Get(Get(MainInstance, "_manualTitanProcess"), "Claim")), "native attack retains original process authority");
            var hp = Get(enemy, "curHP"); CallMain("TryRouteManualTitan"); Equal(hp, Get(enemy, "curHP"), "actual shared cooldown prevents repeat native damage");
            CallMain("CancelManualTitan", "native attack check");
        }
    }

    private static void NativeRouteCapacityClamp(int lanes)
    {
        for (var lane = 0; lane < 2; lane++)
        {
            if ((lanes & (1 << lane)) == 0) continue;
            var native = Invoke(typeof(Program), "Blank", Game.GetType(lane == 0 ? "Energy" : "MagicDisplay", true));
            Set(native, "character", Character);
            Invoke(native, lane == 0 ? "addEnergy" : "updateMagicBar");
        }
    }

    private static object PrepareFundedTitanCapacityWorld()
    {
        PrepareRouteProcessWorld(); SetActiveBeards();
        var settings = Get(MainType, "Settings"); Set(settings, "_useCustomRebirthPlan", false);
        var inventory = Get(Character, "inventory"); var head = Get(inventory, "head");
        Set(Character, "capEnergy", 16480190L); Set(Get(Character, "magic"), "capMagic", 1290170L);
        Set(head, "spec1Type", Enum.Parse(Field(head, "spec1Type").FieldType, "EnergyCap")); Set(head, "spec1Cur", 90000f);
        Set(head, "spec2Type", Enum.Parse(Field(head, "spec2Type").FieldType, "MagicCap")); Set(head, "spec2Cur", 90000f);
        var items = (IList)Get(inventory, "inventory");
        var fight = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var b = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
        foreach (var item in new[] { fight, b })
            foreach (var slot in new[] { "spec1", "spec2", "spec3" })
            { Set(item, slot + "Type", Enum.Parse(Field(item, slot + "Type").FieldType, "None")); Set(item, slot + "Cur", 0f); }
        Set(fight, "spec1Type", Get(head, "spec1Type")); Set(fight, "spec1Cur", 10000f);
        Set(fight, "spec2Type", Get(head, "spec2Type")); Set(fight, "spec2Cur", 10000f);
        Invoke(typeof(Program), "RefreshBonuses");
        var training = Get(Character, "training");
        Array.Clear((long[])Property(training, "attackEnergy"), 0, 6); Array.Clear((long[])Property(training, "defenseEnergy"), 0, 6);
        ((long[])Property(training, "attackEnergy"))[0] = 12;
        Set(Character, "curEnergy", 164801893L); Set(Character, "idleEnergy", 0L);
        Set(Get(Character, "magic"), "curMagic", 12901697L); Set(Get(Character, "magic"), "idleMagic", 0L);
        Set(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy", 164801881L);
        Set(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic", 12901697L);
        Set(settings, "_energyNguWeights", new[] { 1, 1, 0, 1, 0, 1, 0, 0, 0 });
        Set(settings, "_magicNguWeights", new[] { 1, 1, 0, 0, 0, 0, 0 });
        foreach (var index in new[] { 0, 1, 3, 5 }) Set(((IList)Get(Get(Character, "NGU"), "skills"))[index], "level", 1000000L);
        foreach (var index in new[] { 0, 1 }) Set(((IList)Get(Get(Character, "NGU"), "magicSkills"))[index], "level", 1000000L);
        return fight;
    }

    private static void CheckManualTitanCapacityTransition()
    {
        CheckManualTitanCapacityDiggerGrowth();
        var before = Assertions;
        foreach (var immediate in new[] { false, true })
        {
            var fight = PrepareFundedTitanCapacityWorld();
            var model = Character; var runtime = MainInstance; var settings = Get(MainType, "Settings");
            Func<bool> current = () => ReferenceEquals(model, Character) && ReferenceEquals(runtime, MainInstance)
                && ReferenceEquals(settings, Get(MainType, "Settings")) && (bool)Property(settings, "ManageEnergy") && (bool)Property(settings, "ManageMagic");
            var claim = CallMain("BeginExecutionClaim", "capacity-assumption", "original-capacity-attempt",
                Domain("Gear, Support, Route, NativeSettings, Energy, Magic"), current);
            var wanted = RouteBaseline(fight, "capacity-assumption"); Set(wanted, "Authorized", current);
            var original = CallMain("GlobalPresetAllocationState"); var swaps = Get(typeof(Program), "Swaps");
            Throws(() => CallMain("ApplyExecutionClaim", claim, wanted), "capacity", "preserving actual actuator rejects fully funded lower-capacity fight gear");
            Equal(swaps, Get(typeof(Program), "Swaps"), "preserving rejection performs no native swap");
            Equal(original, CallMain("GlobalPresetAllocationState"), "preserving rejection does not alter any funded target/basic/R3");
            var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var policy = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceSetupIntent", true), true);
            Set(policy, "Owner", "capacity-assumption"); Set(policy, "Energy", true); Set(policy, "Magic", true);
            Set(policy, "Desired", Invoke(writer, "EmptyDesiredMap", true, true, false)); Set(policy, "Authorized", current);
            CallMain("SetExecutionResourcePolicy", claim, policy);
            var empty = CallMain("ReconcileStageResourceSetup", policy);
            Equal(true, Property(empty, "Verified"), "real common executor establishes authorized controlled-empty policy");
            Equal(164801893L, Get(Character, "curEnergy"), "withdrawal does not pretend native current Energy depleted");
            Equal(164801881L, Get(Character, "idleEnergy"), "fully funded NGU Energy becomes actual idle, basic12 remains");
            Equal(12901697L, Get(Get(Character, "magic"), "curMagic"), "withdrawal does not lower native current Magic");
            var r3 = Get(Get(Character, "res3"), "curRes3");
            Set(wanted, "Recall", Enum.Parse(Plugin.GetType("NGUInjector.Managers.GearRecallFootprint", true), "Energy, Magic"));
            if (immediate) GearRenderObserver = () => NativeRouteCapacityClamp(3);
            try { CallMain("ApplyExecutionClaim", claim, wanted); } finally { GearRenderObserver = null; }
            Equal(552, Get(Get(Get(Character, "inventory"), "head"), "id"), "real actuator installs selected lower-capacity fight head");
            if (!immediate) Equal(164801893L, Get(Character, "curEnergy"), "delayed clamp retains actual old over-cap current, not a lowered preflight argument");
            NativeRouteCapacityClamp(3);
            Equal(Invoke(Character, "totalCapEnergy"), Get(Character, "curEnergy"), "real native Energy.addEnergy depletes to actual fight capacity");
            Equal((long)Get(Character, "curEnergy") - 12, Get(Character, "idleEnergy"), "real native depletion preserves basic12 and idle safety buffer");
            Equal(Invoke(Character, "totalCapMagic"), Get(Get(Character, "magic"), "curMagic"), "real native MagicDisplay depletion");
            Equal(r3, Get(Get(Character, "res3"), "curRes3"), "no R3 transition permission or depletion");
            // Keep a real resource-only claim during the separate CURRENT-B setup.
            CallMain("EndExecutionClaim", claim, false);
            var hold = CallMain("BeginExecutionClaim", "capacity-release", "original-capacity-attempt", Domain("Energy, Magic"), current);
            CallMain("SetExecutionResourcePolicy", hold, policy);
            var inventory = Get(Character, "inventory"); var b = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 551);
            var successor = RouteBaseline(b, "capacity-current-B"); Set(successor, "Authorized", current);
            var resolved = Array.CreateInstance(hold.GetType(), 1); resolved.SetValue(hold, 0); Set(successor, "ResolvedClaims", resolved);
            Set(successor, "Recall", Get(wanted, "Recall")); CallMain("ApplySelectedSetup", successor);
            NativeRouteCapacityClamp(3);
            Equal(551, Get(Get(inventory, "head"), "id"), "changed lower-cap CURRENT B installs with empty-lane authority retained");
            Equal(Invoke(Character, "totalCapEnergy"), Get(Character, "curEnergy"), "actual lower B depletion is observed without fabricating refill");
            CallMain("EndExecutionClaim", hold, false);
        }
        Console.WriteLine("Manual titan decisive capacity assumption: " + (Assertions - before) + " native assertions.");
        CheckManualTitanCapacityConsumer();
        CheckManualTitanCapacityLanes();
        CheckManualTitanCapacityFailures();
        CheckManualTitanCapacityNoopAndRetry();
        CheckManualTitanCapacityRestart();
        CheckManualTitanCapacityIntermediateDip();
    }

    private static void CheckManualTitanCapacityDiggerGrowth()
    {
        var before = Assertions;
        foreach (var change in new[] { "owned", "render", "support", "resources", "conflict", "revision" })
        {
            PrepareFundedTitanCapacityWorld();
            var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1); Set(settings, "_upgradeDiggers", true);
            Set(Get(Character, "settings"), "diggersOn", true);
            var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
            foreach (var digger in diggers) { Set(digger, "maxLevel", 0L); Set(digger, "curLevel", 0L); Set(digger, "active", false); }
            ((IList)Get(Get(Character, "diggers"), "activeDiggers")).Clear();
            var controller = Get(Character, "allDiggers");
            Set(controller, "baseGoldCost", Enumerable.Repeat(1e200d, 12).ToList());
            Set(controller, "gpsGrowthRate", Enumerable.Repeat(2d, 12).ToList());
            DiggerPrice(0, 10d); Set(Character, "realGold", 100d);
            PrepareRepeatablePurchaseLoop();
            Equal(true, CallMain("TryRouteManualTitan"), change + " actual capacity route starts before ordinary purchase");
            var process = Get(MainInstance, "_manualTitanProcess"); var claim = Get(process, "Claim"); var setup = Get(process, "Setup");
            Equal(3, Convert.ToInt32(Get(process, "ResourceRecall")), change + " route really owns both controlled-empty resource lanes");
            var adventure = Get(Character, "adventure");
            Set(MainInstance, "_manualTitanEnemyName", "WALDERP");
            ((IList)Get(Get(Get(Character, "adventureController"), "log"), "Eventlog")).Add(new string("WALDERP has defeated you!".ToCharArray()));
            Set(adventure, "zone", -1); Set(adventure, "curHP", 0f);
            Equal(true, CallMain("ObserveManualTitanDeaths"), change + " actual native death observation consumes the original budget");
            Set(adventure, "curHP", Convert.ToSingle(Invoke(Character, "totalAdvHP")));
            Equal(true, CallMain("TryRouteManualTitan"), change + " original route resumes with one death already consumed");
            var spawn = ((IDictionary)Get(MainInstance, "_manualTitanSpawns"))[4];
            var acquisition = Get(claim, "Acquisition"); var limit = Get(spawn, "Limit"); var deaths = Get(spawn, "Deaths");
            Equal(1, deaths, change + " regression starts with one genuinely consumed death, not a fresh budget");
            var seconds = (double)Get(spawn, "OwnedSeconds");
            Set(MainInstance, "_manualTitanLastTick", DateTime.UtcNow.AddSeconds(-30));
            var combat = CallMain("RouteProcessGrant", process, false); var transition = Get(process, "TransitionGrant");
            Equal(true, CallMain("ValidateExecutionGrant", combat, Get(claim, "Domains")), change + " original cached combat grant starts current");
            Equal(true, CallMain("ValidateExecutionGrant", transition, Get(claim, "Domains")), change + " original cached transition grant starts current");
            var selections = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps");
            var support = Property(CallMain("CaptureSupportState"), "StableKey"); var calls = Program.RepeatableDiggerCalls;
            var compatible = change == "owned" || change == "render";
            Program.FinitePurchaseRendered = () => {
                if (change == "render") throw new InvalidOperationException("verified route growth presentation failure");
                if (change == "support") Set(diggers[11], "curLevel", 1L);
                if (change == "resources")
                {
                    Set(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy", 1L);
                    Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") - 1);
                }
                if (change == "conflict") CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "support", "foreign support conflict during native purchase");
            };
            if (change == "revision") CallMain("InvalidateExecutionObservations", Domain("Support"));
            try { CallMain("AutomationRoutine"); }
            finally { Program.FinitePurchaseRendered = null; }
            Equal(calls + (change == "revision" ? 0 : 1), Program.RepeatableDiggerCalls, change + " ordinary coordinator buys at most one native digger increment");
            Equal(change == "revision" ? 0L : 1L, Get(diggers[0], "maxLevel"), change + " native purchased maximum is retained");
            Equal(change == "revision" ? 100d : 90d, Get(Character, "realGold"), change + " native purchase pays exactly once");
            var maps = CapacityNativeMapKey(Character); var zone = Get(adventure, "zone"); var writes = ResourceTransfers;
            CallMain("CustomAdventureTick");
            Equal(compatible, Get(MainInstance, "_manualTitanActive"), change + " next actual coordinator tick retains only verified compatible growth: " + CallMain("GetManualTitanRuntime"));
            Equal(swaps, Get(typeof(Program), "Swaps"), change + " growth never re-equips or restores historical gear");
            Equal(selections, InlineGearSelections, change + " growth never selects another setup");
            Equal(limit, Get(spawn, "Limit"), change + " growth never replenishes the acquired death limit");
            Equal(deaths, Get(spawn, "Deaths"), change + " growth never clears the consumed native death");
            Equal(acquisition, Get(claim, "Acquisition"), change + " growth never renews the original acquisition");
            if (compatible)
            {
                Check(ReferenceEquals(process, Get(MainInstance, "_manualTitanProcess")) && ReferenceEquals(claim, Get(process, "Claim"))
                    && ReferenceEquals(setup, Get(process, "Setup")), change + " route continues the same original owner and exact setup");
                Equal(16, Get(adventure, "zone"), change + " legitimate manual titan combat continues after the ordinary purchase");
                Equal(support, Property(CallMain("CaptureSupportState"), "StableKey"), change + " pinned current support stays unchanged");
                Check((double)Get(spawn, "OwnedSeconds") >= seconds + 29, change + " continuation charges elapsed time against the original ten-minute deadline");
                Check(!ReferenceEquals(combat, Get(process, "CombatGrant")) && !ReferenceEquals(transition, Get(process, "TransitionGrant")), change + " both affected grants are renewed, not just combat");
                Equal(true, CallMain("ValidateExecutionGrant", Get(process, "TransitionGrant"), Get(claim, "Domains")), change + " renewed transition grant is current");
                Equal(true, CallMain("ValidateExecutionGrant", CallMain("RouteProcessGrant", process, false), Get(claim, "Domains")), change + " renewed combat grant is current");
                Equal(maps, CapacityNativeMapKey(Character), change + " continuation preserves exact controlled-empty resources");
                CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "support", "later foreign conflict cannot borrow verified purchase approval");
                CallMain("CustomAdventureTick");
                Equal(false, Get(MainInstance, "_manualTitanActive"), change + " a later conflict cannot reuse the one-purchase approval");
            }
            else
            {
                Check(ReferenceEquals(combat, Get(process, "CombatGrant")) && ReferenceEquals(transition, Get(process, "TransitionGrant")), change + " foreign state cannot acquire either replacement grant");
                Equal(true, Get(spawn, "Disarmed"), change + " rejected growth cancels only the bounded current opportunity");
                Equal(maps, CapacityNativeMapKey(Character), change + " rejection preserves foreign resource evidence");
                Equal(writes, ResourceTransfers, change + " rejection performs no resource cleanup");
                if (change != "revision") Equal(zone, Get(adventure, "zone"), change + " rejection does not retire a foreign or conflicted route");
            }
        }
        Console.WriteLine("Manual titan ordinary digger growth: " + (Assertions - before) + " actual coordinator/native assertions.");
    }

    private static object AcquireCapacityBaseline(bool successor = false)
    {
        var settings = Get(MainType, "Settings");
        Set(settings, "_manualTitanLoadout", 1); Set(settings, "_manualAdventureRecoverHealth", false);
        if (!successor) Set(Get(Character, "adventure"), "autoattacking", true);
        Set(Get(Character, "adventure"), "highestItopodLevel", 60);
        var controller = Get(Character, "adventureController");
        var idle = Get(controller, "idleAttackMove") ?? Child(controller, "idleAttackMove");
        Set(idle, "character", Character); Set(idle, "pc", Get(controller, "playerController"));
        Set(Get(controller, "playerController"), "character", Character);
        Set(Get(Get(Character, "inventory"), "itemList"), "itopodKeyComplete", true);
        var enemies = ListField(controller, "itopodEnemyList", 1); Set(enemies[0], "maxHP", 600f); Set(enemies[0], "defense", 10f);
        var id = successor ? "capacity-current-B" : "capacity-current-baseline";
        var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField");
        var root = Invoke(library, "ReadExternalLibrary");
        var weights = successor ? "[1,1,0,0,0,0,0,0,0,0,1000000,0,0,0]" : "[1000000,1000000,0,0.001,0,0,0,0,0,0,0,0]";
        var support = successor ? "\"planned\",\"BeardPriority\":[1,3,5,6],\"DiggerWeights\":[0,0,0,0,0,0,0,0,0,0,0,0]" : "\"current\"";
        var definition = JsonParse("{\"Id\":\"" + id + "\",\"Name\":\"Capacity baseline\",\"Weights\":" + weights + ",\"SupportMode\":" + support + ",\"SwapSeconds\":0,\"AutoUpdate\":false}");
        var rows = Json(root, "Presets");
        var old = ((IEnumerable)GetChildren(rows)).Cast<object>().ToList().FindIndex(x => Property(Json(x, "Id"), "Value").Equals(id));
        if (old < 0) Invoke(rows, "Add", "", definition);
        else rows.GetType().GetProperty("Item", new[] { typeof(int) }).SetValue(rows, definition, new object[] { old });
        var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
        var args = JsonParse("{\"presetId\":\"" + id + "\",\"libraryRevision\":\"" + revision + "\"}");
        var preview = CallMain("PreviewGlobalPreset", args);
        Invoke(MainInstance, "ApplyGlobalPreset", JsonParse(args.ToString().TrimEnd('}') + ",\"previewId\":" + Json(preview, "previewId") + ",\"candidateKey\":\"balanced\"}"), null);
        if (!successor)
        {
            Equal(true, CallMain("GlobalPresetReceiptValid", "dashboard"), "funded capacity fixture acquires an actual native baseline receipt: " + Get(MainInstance, "_globalPresetFailure"));
            Equal(1000, Get(Get(Character, "adventure"), "zone"), "actual baseline owns native ITOPOD predecessor");
        }
        else Equal(id, Get(MainInstance, "_globalPresetManualId"), "actual public Preview+Apply acquires CURRENT B during covered fight");
        return Get(MainInstance, "_globalPresetReceipt");
    }

    private static void CheckManualTitanCapacityConsumer()
    {
        var before = Assertions;
        foreach (var immediate in new[] { 0, 1, 2, 3 })
        {
            PrepareFundedTitanCapacityWorld();
            var futureB = ((IList)Get(Get(Character, "inventory"), "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 551);
            Set(futureB, "curAttack", 100000000f); Set(futureB, "curDefense", 1f);
            var receipt = AcquireCapacityBaseline();
            Equal(0L, Get(Character, "idleEnergy"), "actual baseline starts fully NGU-funded with basic12, not mostly idle");
            Equal(0L, Get(Get(Character, "magic"), "idleMagic"), "actual baseline starts fully Magic-NGU-funded");
            var oldHead = Get(Get(Character, "inventory"), "head");
            var energy = Get(Character, "curEnergy"); var magic = Get(Get(Character, "magic"), "curMagic");
            var allocations = CallMain("GlobalPresetAllocationState"); var routeEntries = new List<int>(); var beforeWithdrawal = false;
            AdventureZoneEntryObserver = zone => {
                routeEntries.Add(zone);
                if (zone == -1) { beforeWithdrawal = true; Equal(allocations, CallMain("GlobalPresetAllocationState"), "actual predecessor retirement precedes every resource withdrawal"); }
            };
            var ownershipChecks = 0;
            CapacityResourcePresentation = (c, target, index) => {
                if (ownershipChecks++ != 0) return;
                Equal(-1, Get(Get(Character, "adventure"), "zone"), "native withdrawal presentation follows actual predecessor retirement");
                var acquired = Get(MainInstance, "_manualTitanProcess");
                Equal(true, Get(acquired, "CapacityTransition"), "session process opts into controlled-empty capacity before callback");
                Equal(3, Convert.ToInt32(Get(acquired, "ResourceRecall")), "session process declares exact affected lanes before native effects");
                Equal(123, Convert.ToInt32(Get(Get(acquired, "Claim"), "Domains")), "claim declares full G/S/Route/settings/E/M footprint before mutation");
            };
            if (immediate != 0) GearRenderObserver = () => NativeRouteCapacityClamp(immediate);
            try { Equal(true, CallMain("TryRouteManualTitan"), "actual fully funded manual-titan consumer admits lower-capacity gear: " + CallMain("GetManualTitanRuntime")); }
            finally { AdventureZoneEntryObserver = null; GearRenderObserver = null; CapacityResourcePresentation = null; }
            Check(beforeWithdrawal, "real native Safe Zone barrier executes before empty-lane application");
            Equal("-1,16", string.Join(",", routeEntries), "actual producer retires predecessor then admits Walderp exactly once");
            Equal(552, Get(Get(Get(Character, "inventory"), "head"), "id"), "actual consumer chooses native fight head");
            var process = Get(MainInstance, "_manualTitanProcess"); var claim = Get(process, "Claim");
            Equal("Energy, Magic", Get(process, "ResourceRecall").ToString(), "actual consumer acquires both affected lanes explicitly");
            Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "affected Energy is actually controlled empty");
            Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "affected Magic is actually controlled empty");
            if ((immediate & 1) == 0) Equal(energy, Get(Character, "curEnergy"), "consumer does not lower the delayed Energy current argument");
            if ((immediate & 2) == 0) Equal(magic, Get(Get(Character, "magic"), "curMagic"), "consumer does not fabricate delayed Magic depletion");
            CallMain("RunManualGlobalPresetRefresh");
            Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "ordinary baseline cannot refill affected Energy during fight");
            var acquisition = Get(claim, "Acquisition");
            AcquireCapacityBaseline(true);
            Equal(552, Get(Get(Get(Character, "inventory"), "head"), "id"), "actual CURRENT B remains covered until fight retirement");
            var seen = new List<int>(); GearRenderObserver = () => seen.Add((int)Get(Get(Get(Character, "inventory"), "head"), "id"));
            try { CallMain("CancelManualTitan", "capacity current-B handoff"); } finally { GearRenderObserver = null; }
            Equal(false, Get(MainInstance, "_manualTitanActive"), "current-B handoff retires actual manual titan owner");
            Equal(551, Get(Get(Get(Character, "inventory"), "head"), "id"), "CURRENT lower B installs before releasing empty-lane authority");
            Check(!seen.Contains((int)Get(oldHead, "id")), "lower-capacity handoff never flashes historical A");
            Equal(true, Get(claim, "Ended"), "original acquired claim retires only after safe successor gear");
            Equal(acquisition, Get(claim, "Acquisition"), "release never renews original acquisition");
            Equal(true, CallMain("ReconcileManualAdventureClaims"), "released capacity handoff leaves no unresolved manual-overlay claim: " + CallMain("GetCustomAutomationStatus"));
            CallMain("RunManualGlobalPresetRefresh");
            Equal(551, Get(Get(Get(Character, "inventory"), "head"), "id"), "ordinary CURRENT-B reconciliation never returns to A");
            Check((long)Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy") > 0, "actual CURRENT B funds native NGUs after hold release: "
                + CallMain("GetResourceCoordinationRuntime") + "; " + Get(MainInstance, "_globalPresetReconcileException"));
            Check((long)Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy") <= (long)Invoke(Character, "totalCapEnergy") - 12,
                "delayed native old current cannot consume B's required idle buffer");
            NativeRouteCapacityClamp(3);
            Check((long)Get(Character, "idleEnergy") >= 0, "delayed B clamp cannot consume a prematurely funded idle buffer");
            Equal(12L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], "consumer transition/release preserves basic12");
            Equal("1,3,5,6", string.Join(",", (int[])Property(Property(CallMain("CaptureSupportState"), "Selection"), "Beards")), "CURRENT B's changed support replaces fight support without historical restoration");
            CallMain("RunManualGlobalPresetRefresh");
            var pool = (long)Get(Character, "curEnergy"); var funded = pool - (long)Get(Character, "idleEnergy") - 12;
            Set(Character, "capEnergy", (long)Get(Character, "capEnergy") + 2);
            for (var tick = 0; tick < 100 && (long)Get(Character, "curEnergy") == pool; tick++) NativeRouteCapacityClamp(1);
            Check((long)Get(Character, "curEnergy") > pool, "actual native generator supplies genuine capacity growth after release");
            CallMain("RunManualGlobalPresetRefresh");
            Equal(funded + (long)Get(Character, "curEnergy") - pool,
                (long)Get(Character, "curEnergy") - (long)Get(Character, "idleEnergy") - 12,
                "hold gone: CURRENT B funds actual new native growth through its acquired NGU/AT split");
        }
        Console.WriteLine("Manual titan actual capacity consumer: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckManualTitanCapacityLanes()
    {
        var before = Assertions;
        foreach (var lanes in new[] { 1, 2, 3 })
        foreach (var clamp in new[] { 0, 1, 2, 3 })
        {
            var fight = PrepareFundedTitanCapacityWorld(); var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1);
            if (lanes == 1) { Set(fight, "spec2Cur", 90000f); Set(settings, "_manageMagic", false); }
            if (lanes == 2) { Set(fight, "spec1Cur", 90000f); Set(settings, "_manageEnergy", false); }
            var energyBefore = CallMain("GlobalPresetLaneState", true); var magicBefore = CallMain("GlobalPresetLaneState", false);
            var e = Get(Character, "curEnergy"); var m = Get(Get(Character, "magic"), "curMagic");
            if ((clamp & lanes) != 0) GearRenderObserver = () => NativeRouteCapacityClamp(clamp & lanes);
            try { Equal(true, CallMain("TryRouteManualTitan"), "real independent-lane consumer admission"); }
            finally { GearRenderObserver = null; }
            var process = Get(MainInstance, "_manualTitanProcess"); Equal(lanes, Convert.ToInt32(Get(process, "ResourceRecall")), "pure preflight derives only actually affected permitted lanes");
            if ((lanes & 1) == 0) { Equal(energyBefore, CallMain("GlobalPresetLaneState", true), "unmanaged Energy map preserved"); Equal(e, Get(Character, "curEnergy"), "unmanaged Energy total preserved"); }
            if ((lanes & 2) == 0) { Equal(magicBefore, CallMain("GlobalPresetLaneState", false), "unmanaged Magic map preserved"); Equal(m, Get(Get(Character, "magic"), "curMagic"), "unmanaged Magic total preserved"); }
            Set(Character, "capEnergy", (long)Get(Character, "capEnergy") + 37);
            CallMain("TryRouteManualTitan");
            if ((lanes & 1) != 0) Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "active hold never funds capacity growth");
            NativeRouteCapacityClamp(lanes);
            var curE = Get(Character, "curEnergy"); var curM = Get(Get(Character, "magic"), "curMagic");
            CallMain("CancelManualTitan", "capacity no-goal release");
            Equal(false, Get(MainInstance, "_manualTitanActive"), "no-goal capacity owner retires");
            Equal(552, Get(Get(Get(Character, "inventory"), "head"), "id"), "no goal preserves current fight equipment, never A");
            Equal(curE, Get(Character, "curEnergy"), "release invents no Energy refill"); Equal(curM, Get(Get(Character, "magic"), "curMagic"), "release invents no Magic refill");
            Equal(731L, Get(Get(Character, "res3"), "curRes3"), "unmanaged R3 total preserved");
            Equal(431L, Get(Get(Character, "res3"), "idleRes3"), "unmanaged R3 idle preserved");
            Equal(300L, Get(((IList)Get(Get(Character, "wishes"), "wishes"))[0], "res3"), "unmanaged R3 funded map preserved");
        }
        foreach (var lane in new[] { "energy", "magic", "yielded" })
        {
            PrepareFundedTitanCapacityWorld(); var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1);
            if (lane != "yielded") Set(settings, lane == "energy" ? "_manageEnergy" : "_manageMagic", false);
            else
            {
                var executor = Property(MainInstance, "_resourceSetupExecutor"); var result = Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers.ResourceMutationResult", true), true);
                Set(result, "AffectedLanes", 1); Set(result, "Disposition", Enum.Parse(Field(result, "Disposition").FieldType, "ForeignConflict"));
                Invoke(Get(executor, "Application"), "RecordUnresolved", Character, result);
            }
            var original = CallMain("GlobalPresetAllocationState"); var swaps = Get(typeof(Program), "Swaps");
            CallMain("TryRouteManualTitan");
            Equal(false, Get(MainInstance, "_manualTitanActive"), lane + " insufficient authority cannot begin capacity transition");
            Equal(original, CallMain("GlobalPresetAllocationState"), lane + " preflight preserves every allocation"); Equal(swaps, Get(typeof(Program), "Swaps"), lane + " preflight performs no native swap");
        }
        Console.WriteLine("Manual titan capacity lanes/clamps: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckManualTitanCapacityFailures()
    {
        var before = Assertions;
        foreach (var boundary in new[] { "withdrawal", "entry" })
        foreach (var kind in new[] { "lane", "route", "settings", "entry", "epoch", "model", "support", "bars" })
        {
            PrepareFundedTitanCapacityWorld(); var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1);
            var fired = false; object process = null;
            Action interfere = () => {
                if (fired) return; fired = true; process = Get(MainInstance, "_manualTitanProcess");
                if (kind == "lane") Set(settings, "_manageEnergy", false);
                if (kind == "route") CallMain("InvalidateExecutionObservations", Domain("Route"));
                if (kind == "settings") CallMain("InvalidateExecutionObservations", Domain("NativeSettings"));
                if (kind == "entry") Set(MainInstance, "_customStageKey", "foreign-native-entry");
                if (kind == "epoch") Set(Get(Character, "stats"), "rebirthNumber", 8L);
                if (kind == "model") Set(Get(Character, "NGU"), "skills", Activator.CreateInstance(Get(Get(Character, "NGU"), "skills").GetType()));
                if (kind == "support") SetActiveBeards(0);
                if (kind == "bars") Set(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy", 1L);
            };
            if (boundary == "withdrawal") CapacityResourcePresentation = (c, target, index) => interfere();
            else AdventureZoneEntryCompletedObserver = zone => { if (zone == 16) interfere(); };
            try { CallMain("TryRouteManualTitan"); }
            finally { CapacityResourcePresentation = null; AdventureZoneEntryCompletedObserver = null; }
            Check(fired, boundary + "/" + kind + " reaches real native callback");
            if (boundary == "withdrawal") Equal(-1, Get(Get(Character, "adventure"), "zone"), kind + " unverified empty setup cannot admit native fight");
            var claim = Get(process, "Claim"); var acquisition = Get(claim, "Acquisition");
            var selections = InlineGearSelections; var swaps = Get(typeof(Program), "Swaps"); var state = CallMain("GlobalPresetAllocationState");
            if (kind == "epoch")
            {
                var map = CapacityNativeMapKey(Character); var zone = Get(Get(Character, "adventure"), "zone"); var routes = AdventureZoneEntries;
                var transfers = ResourceTransfers;
                CallMain("ResetCustomAutomationRuntime"); CallMain("ResetManualTitanRuntime");
                Equal(false, Get(MainInstance, "_manualTitanActive"), boundary + " callback epoch eventually clears through NORMAL reset");
                Equal(false, Get(MainInstance, "_manualTitanRecovering"), boundary + " normal reset clears old recovery, not uncertainty in the current epoch");
                Equal(null, Get(MainInstance, "_manualTitanProcess"), boundary + " normal reset drops expired producer reference");
                Equal(map, CapacityNativeMapKey(Character), boundary + " reset preserves all exact new-Epoch native maps");
                Equal(zone, Get(Get(Character, "adventure"), "zone"), boundary + " stale reset never moves new-Epoch adventure");
                Equal(routes, AdventureZoneEntries, boundary + " stale reset performs no native route operation");
                Equal(transfers, ResourceTransfers, boundary + " stale reset performs no native resource operation");
                Equal(swaps, Get(typeof(Program), "Swaps"), boundary + " stale reset performs no native gear operation");
            }
            else CallMain("TryRouteManualTitan");
            Equal(selections, InlineGearSelections, kind + " callback retry/reset never selects replacement gear");
            Equal(swaps, Get(typeof(Program), "Swaps"), kind + " callback retry/reset never replays native gear effects");
            Equal(state, CallMain("GlobalPresetAllocationState"), kind + " callback retry/reset never adopts or clears foreign bars");
            Equal(acquisition, Get(claim, "Acquisition"), kind + " callback never renews original acquisition");
            var current = Get(MainInstance, "_manualTitanProcess");
            if (current != null) Equal(true, Get(MainInstance, "_manualTitanRecovering"), kind + " same-epoch unresolved retirement stays scoped blocked");
        }
        foreach (var unknown in new[] { false, true })
        {
            PrepareFundedTitanCapacityWorld(); Set(Get(MainType, "Settings"), "_manualTitanLoadout", 1);
            Set(Get(Character, "adventure"), "zone", 1000);
            if (unknown) CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "route", "Preserved UnknownOutcome route");
            var state = CallMain("GlobalPresetAllocationState"); var entries = AdventureZoneEntries;
            CallMain("TryRouteManualTitan"); CallMain("TryRouteManualTitan");
            Equal(entries, AdventureZoneEntries, "receiptless/Unknown route never gains Safe Zone permission from claim admission");
            Equal(state, CallMain("GlobalPresetAllocationState"), "receiptless/Unknown route cannot withdraw funded prerequisites");
            Equal(1000, Get(Get(Character, "adventure"), "zone"), "receiptless/Unknown native route preserved");
        }
        Console.WriteLine("Manual titan capacity failures: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckManualTitanCapacityNoopAndRetry()
    {
        PrepareFundedTitanCapacityWorld();
        var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", -1);
        // A natural over-cap pool in unchanged equipment still needs a real empty
        // policy; the gear actuator's no-op must not manufacture that proof.
        Set(Character, "capEnergy", 1648019L); Set(Get(Character, "magic"), "capMagic", 129017L);
        var swaps = Get(typeof(Program), "Swaps");
        Equal(true, CallMain("TryRouteManualTitan"), "gear no-op admits only after actual empty policy");
        Equal(swaps, Get(typeof(Program), "Swaps"), "gear no-op performs no swap");
        Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "gear no-op still withdraws affected NGU through common executor");
        CallMain("CancelManualTitan", "no-op capacity complete");

        PrepareFundedTitanCapacityWorld(); Set(Get(MainType, "Settings"), "_manualTitanLoadout", 1);
        var items = (IList)Get(Get(Character, "inventory"), "inventory"); var chest = items[3];
        Set(chest, "id", 553); Set(chest, "type", Enum.Parse(Field(chest, "type").FieldType, "Chest"));
        Set(chest, "removable", true); Set(chest, "bossRequired", 1); Set(chest, "curAttack", 1000000f); Set(chest, "curDefense", 1000000f);
        var nativeSwaps = (int)Get(typeof(Program), "Swaps");
        GearRenderObserver = () => { if ((int)Get(typeof(Program), "Swaps") == nativeSwaps + 1) throw new InvalidOperationException("known committed capacity prefix render failure"); };
        try { Equal(true, CallMain("TryRouteManualTitan"), "known partial capacity application retains bounded original attempt"); }
        finally { GearRenderObserver = null; }
        var process = Get(MainInstance, "_manualTitanProcess"); var claim = Get(process, "Claim");
        var selected = Get(process, "Setup"); var acquisition = Get(claim, "Acquisition"); var searches = InlineGearSelections;
        Equal("KnownPartial", Get(Get(selected, "GearOutcome"), "Disposition").ToString(), "native typed prefix is known partial, not fake completed setup");
        Equal(true, CallMain("TryRouteManualTitan"), "same selection completes after exact known prefix");
        Check(ReferenceEquals(process, Get(MainInstance, "_manualTitanProcess")), "partial retry retains exact actual process");
        Equal(acquisition, Get(claim, "Acquisition"), "partial retry retains acquisition"); Equal(searches, InlineGearSelections, "partial retry performs no replacement search");
        Equal(16, Get(Get(Character, "adventure"), "zone"), "only completed capacity policy/setup admits native fight");
        CallMain("CancelManualTitan", "known partial complete");

        PrepareFundedTitanCapacityWorld(); AcquireCapacityBaseline();
        Equal(true, CallMain("TryRouteManualTitan"), "no-goal fixture admits actual baseline preemption");
        var head = Get(Get(Character, "inventory"), "head"); var pool = Get(Character, "curEnergy");
        CallMain("StopManualGlobalPreset"); CallMain("CancelManualTitan", "normal goal removed during fight");
        Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), "actual normal-goal stop never restores historical gear");
        Equal(pool, Get(Character, "curEnergy"), "no-goal release does not invent depletion or refill");
        Equal(0L, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "no goal leaves affected lane idle rather than inventing a fallback");
    }

    private static void CheckManualTitanCapacityRestart()
    {
        var before = Assertions;
        foreach (var foreignBars in new[] { false, true })
        {
            PrepareFundedTitanCapacityWorld(); Set(Get(MainType, "Settings"), "_manualTitanLoadout", 1);
            Equal(true, CallMain("TryRouteManualTitan"), "restart fixture admits actual funded capacity process");
            var process = Get(MainInstance, "_manualTitanProcess"); var grant = CallMain("RouteProcessGrant", process, false);
            if (foreignBars) Set(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy", 1L);
            var map = CapacityNativeMapKey(Character); var head = Get(Get(Character, "inventory"), "head"); var zone = Get(Get(Character, "adventure"), "zone");
            var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var routes = AdventureZoneEntries;
            RestartRouteCoordinator();
            Equal(false, CallMain("TryRouteManualTitan"), "restart cancels current-due capacity attempt instead of renewing it");
            Equal(true, Property(Json(CallMain("GetManualTitanRuntime"), "disarmed"), "AsBool"), "unfinished capacity attempt waits for next native spawn");
            Equal(false, CallMain("ExecutionOwnsDomain", Domain("Energy, Magic")), "restart imports no capacity resource authority");
            Equal(false, CallMain("ValidateExecutionGrant", grant, Domain("Route")), "old capacity grant is not current-session authority");
            Equal(map, CapacityNativeMapKey(Character), "restart uses actual maps without old controlled-empty cleanup");
            Check(ReferenceEquals(head, Get(Get(Character, "inventory"), "head")), "restart retains current gear instead of restoring old original");
            Equal(zone, Get(Get(Character, "adventure"), "zone"), "restart imports no old route-retirement right");
            Equal(swaps, Get(typeof(Program), "Swaps"), "restart performs no native gear effect");
            Equal(writes, ResourceTransfers, "restart performs no native resource effect"); Equal(routes, AdventureZoneEntries, "restart performs no native cleanup route effect");
        }
        Console.WriteLine("Manual titan capacity restart cancellation: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckManualTitanCapacityIntermediateDip()
    {
        var before = Assertions;
        PrepareFundedTitanCapacityWorld(); Set(Get(MainType, "Settings"), "_manualTitanLoadout", 1);
        var accs = (IList)Get(Get(Character, "inventory"), "accs"); Set(accs[0], "id", 0);
        var cap = accs[1]; var capId = Get(cap, "id");
        Set(cap, "spec1Type", Enum.Parse(Field(cap, "spec1Type").FieldType, "EnergyCap")); Set(cap, "spec1Cur", 90000f);
        Set(cap, "spec2Type", Enum.Parse(Field(cap, "spec2Type").FieldType, "MagicCap")); Set(cap, "spec2Cur", 90000f);
        Set(cap, "curAttack", 1f); Set(cap, "curDefense", 1f); Invoke(typeof(Program), "RefreshBonuses");
        var original = (long)Get(Character, "curEnergy"); var clamps = 0;
        CapacityGearBoundary = () => {
            var mask = ((long)Get(Character, "curEnergy") > (long)Invoke(Character, "totalCapEnergy") ? 1 : 0)
                | ((long)Get(Get(Character, "magic"), "curMagic") > (long)Invoke(Character, "totalCapMagic") ? 2 : 0);
            if (mask != 0) { NativeRouteCapacityClamp(mask); clamps++; }
        };
        try { Equal(true, CallMain("TryRouteManualTitan"), "actual consumer handles transient accessory staging capacity dip"); }
        finally { CapacityGearBoundary = null; }
        Equal(16, Get(Get(Character, "adventure"), "zone"), "intermediate-dip consumer enters only after full proof");
        Equal(capId, Get(accs[0], "id"), "real actuator relocates acquired cap accessory through inventory");
        Check(clamps > 0, "real native depletion executes during an intermediate slot map");
        Check((long)Invoke(Character, "totalCapEnergy") > original, "final fight capacity itself would have retained the original pool");
        Check((long)Get(Character, "curEnergy") < original, "intermediate native loss is not refilled when final capacity returns");
        Equal(3, Convert.ToInt32(Get(Get(MainInstance, "_manualTitanProcess"), "ResourceRecall")), "typed preflight derives both lanes from intermediate maps, not final capacity alone");
        Equal(12L, ((long[])Property(Get(Character, "training"), "attackEnergy"))[0], "intermediate native clamp retains immutable basic12");
        CallMain("CancelManualTitan", "intermediate dip complete");
        Console.WriteLine("Manual titan actual intermediate dip: " + (Assertions - before) + " native assertions.");
    }

    private static void CheckCapacityLifecycleRegressions()
    {
        var before = Assertions; var failures = new List<Exception>();
        foreach (var entry in new[] { "quiesce", "epoch", "strict" })
        {
            try
            {
                if (entry == "quiesce") CheckCapacityQuiescentRetirement();
                else if (entry == "epoch") CheckCapacityNormalEpochCleanup();
                else CheckCapacityLifecycleOwnershipGuards();
                Console.WriteLine("PASS capacity lifecycle " + entry);
            }
            catch (Exception error)
            { failures.Add(new Exception(entry, error)); Console.Error.WriteLine("FAIL capacity lifecycle " + entry + ": " + error); }
        }
        Console.WriteLine("Capacity lifecycle closure: " + (Assertions - before) + " native assertions; failed groups=" + failures.Count);
        if (failures.Count != 0) throw new AggregateException("Capacity lifecycle regressions failed.", failures);
    }

    private static string CapacityNativeMapKey(object model)
    {
        var map = (IDictionary)Invoke(Plugin.GetType("NGUInjector.Managers.ResourceWriter", true), "CaptureAllocations", model);
        return string.Join("|", map.Keys.Cast<object>().Select(key => key + ":" + (map[key] == null ? "null" : string.Join(",", (long[])map[key]))));
    }

    private static void CheckCapacityQuiescentRetirement()
    {
        foreach (var entry in new[] { "service", "unload", "forward-disabled" })
        {
            PrepareFundedTitanCapacityWorld(); var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1);
            Equal(true, CallMain("TryRouteManualTitan"), entry + " actual funded capacity actor admits");
            var process = Get(MainInstance, "_manualTitanProcess"); var claim = Get(process, "Claim");
            Equal(3, Convert.ToInt32(Get(process, "ResourceRecall")), entry + " takes nonzero E/M capacity branch");
            var model = Character; var inventory = Get(model, "inventory"); var head = Get(inventory, "head");
            var b = ((IList)Get(inventory, "inventory")).Cast<object>().Single(x => (int)Get(x, "id") == 551);
            CallMain("PublishExecutionBaseline", RouteBaseline(b, "quiesce-current-B"));
            if (entry == "forward-disabled") Set(settings, "_manualTitanEnabled", false);
            var allocations = CapacityNativeMapKey(model); var e = Get(model, "curEnergy"); var idle = Get(model, "idleEnergy");
            var m = Get(Get(model, "magic"), "curMagic"); var mIdle = Get(Get(model, "magic"), "idleMagic"); var r3 = Get(Get(model, "res3"), "curRes3");
            var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var entries = AdventureZoneEntries;
            Check((long)e > (long)Invoke(model, "totalCapEnergy"), entry + " really retains delayed native depletion");
            if (entry == "unload") CallMain("Unload");
            else
            {
                CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Quiescing"));
                Equal(true, CallMain("ServiceLifecycleRetirement"), entry + " healthy nonzero capacity hold completes actual lifecycle retirement");
                Equal(true, CallMain("ServiceLifecycleRetirement"), entry + " repeated terminal retirement is idempotent");
                Equal(3, Get(MainInstance, "_executionCapacityFundingLanes"), entry + " retains only session-local delayed-clamp lane safety");
                CallMain("ReconcileCustomResources", null, true);
                Equal(writes, ResourceTransfers, entry + " Quiescing cannot fund CURRENT B");
                CallMain("Unload");
            }
            Equal(-1, Get(Get(model, "adventure"), "zone"), entry + " real bounded grant retires native combat to Safe Zone");
            Equal(entries + 1, AdventureZoneEntries, entry + " retirement enters Safe Zone exactly once");
            Equal(true, Get(claim, "Ended"), entry + " terminal quiescence ends actual empty-lane authority");
            Equal(true, Get(MainInstance, "_unloaded"), entry + " actual Unload completes");
            Check(ReferenceEquals(head, Get(inventory, "head")), entry + " never installs CURRENT B or historical A during shutdown");
            Equal(swaps, Get(typeof(Program), "Swaps"), entry + " shutdown adds no native gear operation"); Equal(writes, ResourceTransfers, entry + " shutdown adds no native resource operation");
            Equal(e, Get(model, "curEnergy"), entry + " does not pretend native Energy clamped"); Equal(idle, Get(model, "idleEnergy"), entry + " retains native idle debit buffer");
            Equal(m, Get(Get(model, "magic"), "curMagic"), entry + " preserves delayed native Magic"); Equal(r3, Get(Get(model, "res3"), "curRes3"), entry + " preserves unmanaged R3");
            Equal(mIdle, Get(Get(model, "magic"), "idleMagic"), entry + " retains exact native Magic buffer");
            Equal(allocations, CapacityNativeMapKey(model), entry + " preserves exact E/M/basic/R3 native allocation maps");
            var writer = Plugin.GetType("NGUInjector.Managers.ResourceWriter", true);
            var map = (IDictionary)Invoke(writer, "CaptureAllocations", model);
            Equal(12L, ((long[])Property(Get(model, "training"), "attackEnergy"))[0], entry + " immutable basic12 survives");
            Check(map.Values.Cast<long[]>().Where(x => x != null).All(x => x.All(value => value >= 0)), entry + " actual native maps remain nonnegative");
            CallMain("Unload"); Equal(entries + 1, AdventureZoneEntries, entry + " repeated Unload never repeats native retirement");
            Equal(writes, ResourceTransfers, entry + " repeated Unload never withdraws/funds native resources");
        }
    }

    private static void CheckCapacityNormalEpochCleanup()
    {
        PrepareFundedTitanCapacityWorld(); var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1);
        Equal(true, CallMain("TryRouteManualTitan"), "normal epoch cleanup fixture admits actual funded capacity actor");
        var oldProcess = Get(MainInstance, "_manualTitanProcess"); var oldClaim = Get(oldProcess, "Claim");
        Equal(3, Convert.ToInt32(Get(oldProcess, "ResourceRecall")), "epoch cleanup exercises nonzero capacity dispatch");
        var model = Character; var runtime = MainInstance; var adventure = Get(model, "adventure");
        Set(Get(model, "stats"), "rebirthNumber", 8L); Set(Get(model, "rebirthTime"), "totalseconds", 25d);
        Set(Get(model, "totalPlaytime"), "totalseconds", 1501d);
        Set(adventure, "zone", 4); Set(adventure, "autoattacking", true);
        Set(model, "curEnergy", 4000L); Set(model, "idleEnergy", 1000L);
        Set(((IList)Get(Get(model, "NGU"), "skills"))[0], "energy", 2988L);
        Set(Get(model, "magic"), "curMagic", 2000L); Set(Get(model, "magic"), "idleMagic", 500L);
        Set(((IList)Get(Get(model, "NGU"), "magicSkills"))[0], "magic", 1500L);
        var state = CallMain("GlobalPresetAllocationState"); var allMaps = CapacityNativeMapKey(model); var head = Get(Get(model, "inventory"), "head");
        var swaps = Get(typeof(Program), "Swaps"); var writes = ResourceTransfers; var entries = AdventureZoneEntries;
        CallMain("ResetCustomAutomationRuntime");
        Equal(true, Get(oldClaim, "Ended"), "NORMAL runtime reset actually ends the expired original claim without reconciliation");
        CallMain("ResetManualTitanRuntime");
        Equal(false, Get(MainInstance, "_manualTitanActive"), "normal epoch cleanup clears stale active capacity producer");
        Equal(false, Get(MainInstance, "_manualTitanRecovering"), "normal epoch cleanup clears stale capacity recovery latch");
        Equal(null, Get(MainInstance, "_manualTitanProcess"), "normal epoch cleanup drops expired process reference");
        Equal(null, Get(MainInstance, "_manualTitanEpoch"), "expired manual history cannot become current-session authority");
        Equal(0, ((IDictionary)Get(MainInstance, "_manualTitanSpawns")).Count, "old semantic spawn does not become a new death budget/cursor");
        Equal(state, CallMain("GlobalPresetAllocationState"), "normal reset/manual cleanup preserves exact new-Epoch funded maps");
        Equal(allMaps, CapacityNativeMapKey(model), "normal epoch cleanup preserves complete basic and R3 maps as well as E/M");
        Equal(4, Get(adventure, "zone"), "expired actor cannot move the new-Epoch native route");
        Equal(4000L, Get(model, "curEnergy"), "expired cleanup preserves new Energy total"); Equal(1000L, Get(model, "idleEnergy"), "expired cleanup preserves new Energy idle");
        Equal(2000L, Get(Get(model, "magic"), "curMagic"), "expired cleanup preserves new Magic total");
        Equal(500L, Get(Get(model, "magic"), "idleMagic"), "expired cleanup preserves new Magic idle");
        Equal(12L, ((long[])Property(Get(model, "training"), "attackEnergy"))[0], "expired cleanup preserves new basic12");
        Equal(731L, Get(Get(model, "res3"), "curRes3"), "expired cleanup preserves new R3 total"); Equal(431L, Get(Get(model, "res3"), "idleRes3"), "expired cleanup preserves new R3 idle");
        Check(ReferenceEquals(head, Get(Get(model, "inventory"), "head")), "expired cleanup preserves actual new-Epoch equipment");
        var successor = CallMain("BeginExecutionClaim", "native-new-epoch-owner", "explicit-new-epoch-entry", Domain("Gear, Support, Route, NativeSettings, Energy, Magic"),
            (Func<bool>)(() => ReferenceEquals(model, Character) && ReferenceEquals(runtime, MainInstance) && (long)Get(Get(model, "stats"), "rebirthNumber") == 8));
        var grant = CallMain("ResolveExecutionGrant", "native-new-epoch-owner", "explicit-new-epoch-entry", Domain("Route, NativeSettings"),
            (Func<bool>)(() => ReferenceEquals(model, Character) && ReferenceEquals(runtime, MainInstance)), successor, null);
        Equal(true, CallMain("ValidateExecutionGrant", grant, Domain("Route, NativeSettings")), "expired producer does not block explicit newer authority");
        Equal(true, CallMain("EndRouteProcess", oldProcess), "repeated expired process retirement ends only stale authority");
        CallMain("ResetManualTitanRuntime");
        Equal(true, CallMain("ValidateExecutionGrant", grant, Domain("Route, NativeSettings")), "repeated old cleanup cannot revoke explicit newer authority");
        Equal(swaps, Get(typeof(Program), "Swaps"), "expired cleanup performs ZERO native gear writes"); Equal(writes, ResourceTransfers, "expired cleanup performs ZERO native resource writes");
        Equal(entries, AdventureZoneEntries, "expired cleanup performs ZERO native route writes"); Equal(state, CallMain("GlobalPresetAllocationState"), "repeated stale retirement preserves exact newer allocations");
        Equal(false, CallMain("ManualTitanBlocksZone", 4), "stale recovery cannot block unrelated new-Epoch adventure");
        CallMain("EndExecutionClaim", successor, false);
    }

    private static void CheckCapacityLifecycleOwnershipGuards()
    {
        foreach (var kind in new[] { "lane", "foreign-route", "unknown", "bars", "ended" })
        {
            PrepareFundedTitanCapacityWorld(); var settings = Get(MainType, "Settings"); Set(settings, "_manualTitanLoadout", 1);
            Equal(true, CallMain("TryRouteManualTitan"), kind + " strict retirement fixture admits");
            var process = Get(MainInstance, "_manualTitanProcess"); var claim = Get(process, "Claim");
            if (kind == "lane") Set(settings, "_manageEnergy", false);
            if (kind == "foreign-route") Set(Get(Character, "adventure"), "zone", 4);
            if (kind == "unknown") CallMain("RecordSelectedSetupConflict", Character, Get(Get(Character, "stats"), "rebirthNumber"), "route", "Preserved same-epoch UnknownOutcome");
            if (kind == "bars") Set(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy", 1L);
            if (kind == "ended") CallMain("EndExecutionClaim", claim, false);
            var zone = Get(Get(Character, "adventure"), "zone"); var entries = AdventureZoneEntries; var state = CallMain("GlobalPresetAllocationState");
            var writes = ResourceTransfers; var swaps = Get(typeof(Program), "Swaps");
            CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Quiescing"));
            Equal(true, CallMain("ServiceLifecycleRetirement"), kind + " lost authority retires only the current bounded claim without cleanup");
            Equal(true, CallMain("ServiceLifecycleRetirement"), kind + " repeated cancellation does not revive cleanup authority");
            Equal(true, Get(claim, "Ended"), kind + " cancellation ends authority, not earned completion");
            if (kind != "lane")
            { Equal(zone, Get(Get(Character, "adventure"), "zone"), kind + " cannot acquire permission to move foreign/unowned native route"); Equal(entries, AdventureZoneEntries, kind + " no native route mutation"); }
            else Equal(-1, Get(Get(Character, "adventure"), "zone"), "lane permission loss permits only exact narrow Safe Zone retirement");
            Equal(state, CallMain("GlobalPresetAllocationState"), kind + " does not discard or overwrite native resource evidence");
            Equal(writes, ResourceTransfers, kind + " no resource cleanup/funding"); Equal(swaps, Get(typeof(Program), "Swaps"), kind + " no historical/B gear application");
        }
    }
}
