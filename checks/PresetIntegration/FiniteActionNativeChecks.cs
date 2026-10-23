using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    internal static Action FiniteActionSpellRendered;
    internal static Action FinitePitRender;
    internal static object FinitePitRng;
    internal static string FinitePitText;
    internal static int FinitePitRenderCalls;
    internal static Action FinitePitSetupReady;
    internal static object LastFinitePitResult;
    internal static Action FinitePreparationSelectionStarting;
    internal static Action FiniteFruitRendered;
    internal static Action FinitePurchaseRendered;
    internal static int FiniteExpNativeCalls, FiniteDiggerNativeCalls;

    // Parent wires this optional hook in LoadFixtureGame, after ordinary scene
    // substitutions. The native cast/accounting and production authority stay real.
    private static void ConfigureFiniteActionGameFixture(AssemblyDefinition game)
    {
        var render = game.MainModule.GetType("RebirthPowerSpell").Methods.Single(x => x.Name == "updateMenu");
        Boundary(game.MainModule, render, nameof(FiniteActionSpellPresentation), false);
        Boundary(game.MainModule, game.MainModule.GetType("FruitController").Methods.Single(x => x.Name == "updateFruitDisplay"), nameof(FiniteFruitPresentation), false);
        Constant(game.MainModule.GetType("FruitController").Methods.Single(x => x.Name == "updateButton"), 0);
        foreach (var name in new[] { "EnergyPurchases", "MagicPurchases" })
        {
            var purchases = game.MainModule.GetType(name);
            var ui = purchases.Methods.Single(x => x.Name == (name == "EnergyPurchases" ? "updateEnergyPurchases" : "updateMagicPurchases"));
            Boundary(game.MainModule, ui, nameof(FinitePurchasePresentation), false);
            foreach (var buy in purchases.Methods.Where(x => x.Name == "buyCustomPower" || x.Name == "buyCustomCap" || x.Name == "buyCustomBar"))
                buy.Body.GetILProcessor().InsertBefore(buy.Body.Instructions[0], Instruction.Create(OpCodes.Call,
                    game.MainModule.ImportReference(typeof(Program).GetMethod(nameof(FiniteExpInvocation), All))));
        }
        var digger = game.MainModule.GetType("AllGoldDiggerController");
        var upgrade = digger.Methods.Single(x => x.Name == "upgradeMaxLevel");
        upgrade.Body.GetILProcessor().InsertBefore(upgrade.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            game.MainModule.ImportReference(typeof(Program).GetMethod(nameof(FiniteDiggerInvocation), All))));
        var diggerUi = digger.Methods.Single(x => x.Name == "refreshMenu");
        diggerUi.Body.GetILProcessor().InsertBefore(diggerUi.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            game.MainModule.ImportReference(typeof(Program).GetMethod(nameof(FinitePurchasePresentation), All))));
        ConfigureFiniteActionRng(game.MainModule);
        Constant(game.MainModule.GetType("InventoryController").Methods.Single(x => x.Name == "updateItem" && x.Parameters.Count == 1), 0);
        foreach (var method in game.MainModule.GetType("PitController").Methods.Where(x => x.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            var text = call?.DeclaringType.FullName == "UnityEngine.UI.Text" && call.Name == "set_text";
            var menus = call?.DeclaringType.FullName == "Character" && call.Name == "refreshMenus";
            if (!text && !menus) continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = game.MainModule.ImportReference(typeof(Program).GetMethod(text ? nameof(FinitePitTextWrite) : nameof(FinitePitPresentation), All));
        }
        Constant(game.MainModule.GetType("ButtonShower").Methods.Single(x => x.Name == "updateButtons"), 0);
    }
    public static void FiniteActionSpellPresentation() { FiniteActionSpellRendered?.Invoke(); }
    public static void FinitePitPresentation(object model) { FinitePitRenderCalls++; FinitePitRender?.Invoke(); }
    public static void FinitePitTextWrite(object text, string value) { FinitePitText = value; }
    public static string FinitePitTextRead(object text) => FinitePitText ?? "fixture";
    public static object FinitePitGetState() => FinitePitRng;
    public static void FinitePitSetState(object value) { FinitePitRng = value; }
    public static int FinitePitRange(int min, int max)
    {
        var next = Activator.CreateInstance(FinitePitRng.GetType());
        foreach (var field in next.GetType().GetFields(All)) field.SetValue(next, field.GetValue(FinitePitRng));
        var cursor = next.GetType().GetField("s0", All); cursor.SetValue(next, (int)cursor.GetValue(next) + 1);
        FinitePitRng = next;
        return min == 1 ? max == 6 ? 2 : 4 : min < 0 ? -1 : Math.Min(max - 1, 37);
    }

    private static void ConfigureFiniteActionPluginFixture(AssemblyDefinition plugin)
    {
        ConfigureFiniteActionRng(plugin.MainModule);
        var request = plugin.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "RequestMoneyPit");
        var select = plugin.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "SelectSetupGear");
        select.Body.GetILProcessor().InsertBefore(select.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(Program).GetMethod(nameof(FinitePreparationSelectionInput), All))));
        foreach (var instruction in request.Body.Instructions)
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.UI.Text" || call.Name != "get_text") continue;
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = plugin.MainModule.ImportReference(typeof(Program).GetMethod(nameof(FinitePitTextRead), All));
        }
        var setup = request.Body.Instructions.Single(x => x.Operand is MethodReference call && call.Name == "AcquireFiniteActionSetup");
        request.Body.GetILProcessor().InsertAfter(setup, Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(Program).GetMethod(nameof(FinitePitSetupObservation), All))));
        var run = request.Body.Instructions.Single(x => x.Operand is MethodReference call && call.Name == "RunFiniteAction");
        var duplicate = Instruction.Create(OpCodes.Dup); request.Body.GetILProcessor().InsertAfter(run, duplicate);
        request.Body.GetILProcessor().InsertAfter(duplicate, Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(Program).GetMethod(nameof(FinitePitResultObservation), All))));
    }
    public static void FinitePitSetupObservation() { FinitePitSetupReady?.Invoke(); }
    public static void FinitePitResultObservation(object result) { LastFinitePitResult = result; }
    public static void FinitePreparationSelectionInput() { FinitePreparationSelectionStarting?.Invoke(); }
    public static void FiniteFruitPresentation() { FiniteFruitRendered?.Invoke(); }
    public static void FinitePurchasePresentation() { FinitePurchaseRendered?.Invoke(); }
    public static void FiniteExpInvocation() { FiniteExpNativeCalls++; }
    public static void FiniteDiggerInvocation() { FiniteDiggerNativeCalls++; }

    private static void ConfigureFiniteActionRng(ModuleDefinition module)
    {
        var shim = new TypeDefinition("", "FiniteActionRngInputs", Mono.Cecil.TypeAttributes.NotPublic
            | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(shim);
        var adapters = new Dictionary<string, MethodDefinition>();
        IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
        foreach (var instruction in module.Types.SelectMany(Types).Where(x => x != shim).SelectMany(x => x.Methods)
            .Where(x => x.HasBody).SelectMany(x => x.Body.Instructions).ToArray())
        {
            var call = instruction.Operand as MethodReference;
            if (call?.DeclaringType.FullName != "UnityEngine.Random" || call.Name != "get_state" && call.Name != "set_state"
                && !(call.Name == "Range" && call.Parameters[0].ParameterType.FullName == "System.Int32")) continue;
            if (!adapters.TryGetValue(call.Name, out var adapter))
            {
                adapter = new MethodDefinition(call.Name, Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, call.ReturnType);
                shim.Methods.Add(adapter);
                foreach (var parameter in call.Parameters) adapter.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                var il = adapter.Body.GetILProcessor();
                foreach (var parameter in adapter.Parameters) il.Emit(OpCodes.Ldarg, parameter);
                if (call.Name == "set_state") il.Emit(OpCodes.Box, call.Parameters[0].ParameterType);
                il.Emit(OpCodes.Call, module.ImportReference(typeof(Program).GetMethod(call.Name == "get_state" ? nameof(FinitePitGetState)
                    : call.Name == "set_state" ? nameof(FinitePitSetState) : nameof(FinitePitRange), All)));
                if (call.Name == "get_state") il.Emit(OpCodes.Unbox_Any, call.ReturnType);
                il.Emit(OpCodes.Ret); adapters.Add(call.Name, adapter);
            }
            instruction.OpCode = OpCodes.Call; instruction.Operand = adapter;
        }
    }
}

public static partial class PresetIntegrationChecks
{
    private static object NativeNumberLeaf(string opportunity, double blood, double number)
    {
        var identity = CallMain("ActionIdentity", "blood-number", "action-checks", opportunity, null);
        var action = Activator.CreateInstance(MainType.GetNestedType("NumberBloodAction", All), true);
        Set(action, "Identity", identity); Set(action, "Blood", blood); Set(action, "Number", number);
        Set(action, "Footprint", Domain("FiniteAction, Magic"));
        return action;
    }

    private static object NativeActionGrant(object action, Func<bool> current, object claim = null)
    {
        var id = Get(action, "Identity");
        return CallMain("ResolveExecutionGrant", Get(id, "Owner"), Get(id, "Acquisition"), Get(action, "Footprint"), current, claim, null);
    }

    private static void CheckFiniteActionCutover()
    {
        var before = Assertions;
        var plan = SetupInlineStagePlan();
        var store = Get(MainType, "CustomPlanStore"); var planName = (string)Property(Get(MainType, "Settings"), "CustomPlanFile");
        Invoke(store, "Save", planName, plan); plan = Invoke(store, "Load", planName);
        RunVerifiedInline(plan, "finite-action baseline A");
        var spell = Get(Character, "bloodSpells"); Set(spell, "character", Character);
        var blood = Get(Character, "bloodMagic"); Set(blood, "bloodPoints", 100d); Set(blood, "rebirthPower", 5d);
        var action = NativeNumberLeaf("native-render-window", 100d, 5d);
        var identity = Get(action, "Identity");
        var claim = CallMain("BeginExecutionClaim", "action-checks", Get(identity, "Acquisition"),
            Domain("Gear, Support, Route, FiniteAction, Magic"), (Func<bool>)(() => true));
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var temporary = items.Cast<object>().Single(x => (int)Get(x, "id") == 552);
        var successor = items.Cast<object>().Single(x => (int)Get(x, "id") == 551);
        var setup = ExactClaimSetup(temporary, "action-checks");
        Func<bool> verified = () => (bool)CallMain("FiniteActionSetupVerified", claim, setup);
        var withheld = CallMain("RunFiniteAction", action, NativeActionGrant(action, verified, claim));
        Equal(false, Property(withheld, "Verified"), "unapplied exact setup withholds native NUMBER leaf");
        Equal(100d, Get(blood, "bloodPoints"), "unverified setup spends no blood");
        CallMain("ApplyExecutionClaim", claim, setup);
        Equal(true, verified(), "real selected gear/support/route proof admits action");
        AdvanceFiniteActionBaseline(plan);
        var renderCalls = 0;
        Program.FiniteActionSpellRendered = () => { renderCalls++; throw new InvalidOperationException("native model committed; scene render failed"); };
        try
        {
            var result = CallMain("RunFiniteAction", action, NativeActionGrant(action, verified, claim));
            Equal("ModelAppliedRenderFailed", Get(result, "Disposition").ToString(), "actual native NUMBER accounting survives render exception: " + Get(result, "Reason"));
            Equal(true, Property(result, "Verified"), "committed native result is durable before publication");
            Equal(0d, Get(blood, "bloodPoints"), "native spell consumes blood exactly once");
            Equal(105d, Get(blood, "rebirthPower"), "native NUMBER exact addition");
            CallMain("RunFiniteAction", action, NativeActionGrant(action, verified, claim));
            Equal(1, renderCalls, "committed result does not re-enter native cast or rendering");
        }
        finally { Program.FiniteActionSpellRendered = null; }
        CallMain("EndExecutionClaim", claim, true);
        RunVerifiedInline(plan, "finite-action actual CURRENT B after temporary claim release");
        Equal(551, Get(Get(inventory, "head"), "id"), "finite setup exit resolves CURRENT B, not entry A");

        // Hold only the result file replacement boundary, after native model writes.
        Set(blood, "bloodPoints", 20d); action = NativeNumberLeaf("result-failure-window", 20d, 105d);
        identity = Get(action, "Identity");
        var hash = Invoke(Plugin.GetType("NGUInjector.JsonFiles", true), "ComputeRevision", Encoding.UTF8.GetBytes((string)Get(identity, "Key")));
        var file = Path.Combine((string)Get(MainType, "_dir"), "finite-actions-v1", hash + ".json");
        FileStream held = null;
        Program.FiniteActionSpellRendered = () => held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var result = CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true));
            Equal(true, Get(result, "Committed"), "result persistence failure retains actual native commitment");
            Equal(false, Get(result, "Durable"), "result persistence failure withholds dependent success");
            Set(blood, "bloodPoints", 3d);
            var successorAction = NativeNumberLeaf("dependent-result-window", 3d, 125d);
            var calls = NativeNumberFlushCalls;
            var waiting = CallMain("RunFiniteAction", successorAction, NativeActionGrant(successorAction, () => true));
            Equal(false, Get(waiting, "Committed"), "dirty committed result cannot authorize successor native spell");
            Equal(calls, NativeNumberFlushCalls, "dependent spell is withheld before native invocation");
            Equal(false, Property(MainInstance, "RebirthFinitePrerequisitesResolved"), "dirty committed blood result blocks dependent rebirth preparation");
            var rebirths = Get(Get(Character, "stats"), "rebirthNumber");
            Equal(true, CallMain("PrepareFinalRebirthAction"), "actual selected final NUMBER setup can be observed independently of pending result storage");
            try { Equal(false, CallMain("InvokeFinalRebirthAction", "engage"), "dirty committed result withholds actual native rebirth entry"); }
            finally { CallMain("ReleaseFinalRebirthAction"); }
            Equal(rebirths, Get(Get(Character, "stats"), "rebirthNumber"), "result storage fault causes zero native rebirths");
            var oldCap = (long)Get(Character, "capEnergy"); var oldPool = (long)Get(Character, "curEnergy");
            Set(Character, "capEnergy", oldCap + 37); Set(Character, "curEnergy", oldPool + 37);
            Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 37);
            CallMain("InvalidateExecutionObservations", Domain("Energy")); CallMain("ReconcileCustomResources", plan, false);
            Equal(0L, Get(Character, "idleEnergy"), "healthy Energy actually allocates new capacity while Magic result storage is blocked");
            Equal(oldPool + 37 - 7, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum()
                + ((IList)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy")), "healthy lane native accounting includes grown pool, retaining basic training");
        }
        finally { Program.FiniteActionSpellRendered = null; held?.Dispose(); }
        Set(MainInstance, "_finiteActionLedger", null);
        Set(blood, "bloodPoints", 20d);
        var imported = CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true));
        Equal("UnknownOutcome", Get(imported, "Disposition").ToString(), "durable INVOCATION imports as unknown");
        Equal(20d, Get(blood, "bloodPoints"), "unknown import never repeats native spell");
        var energy = CallMain("ResolveExecutionGrant", "healthy-energy", "entry", Domain("Energy"), (Func<bool>)(() => true), null, null);
        Equal(true, CallMain("ValidateExecutionGrant", energy, Domain("Energy")), "unresolved finite blood does not globally disable healthy Energy");
        CheckNativePitAction();
        CheckNativeDirtyResultRecovery();
        CheckNativePreparationRetry();
        CheckNativeFiniteCallbackConflicts();
        CheckNativeFruitLeaves();
        CheckNativePurchaseLeaves();
        CheckActionPreparationIdentityRegressions();
        CheckActionPillPrecisionRegressions();
        CheckCapacityLifecycleRegressions();
        Console.WriteLine("Finite-action native checks: " + (Assertions - before) + " assertions.");
    }

    private static object NativeTypedLeaf(string type, string kind, string owner, string opportunity, string footprint)
    {
        var action = Activator.CreateInstance(MainType.GetNestedType(type, All), true);
        Set(action, "Identity", CallMain("ActionIdentity", kind, owner, opportunity, null));
        Set(action, "Footprint", Domain(footprint));
        return action;
    }

    private static void CheckNativePurchaseLeaves()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "native exact EXP baseline");
        var purchases = Child(Character, "energyPurchases"); Set(purchases, "character", Character); Child(purchases, "tooltip");
        var settings = Get(Character, "settings"); Set(settings, "customPowerAmount", 17);
        Set(Character, "realExp", 1000L); Set(Character, "energyPower", 100f);
        var cost = (long)Invoke(purchases, "customPowerCost", 1);
        var action = NativeTypedLeaf("ExpPurchaseAction", "exp-purchase:0", "auto-exp", "100", "FiniteAction, NativeSettings, Energy");
        Set(action, "Stat", 0); Set(action, "Amount", 1L); Set(action, "Cost", cost); Set(action, "Currency", 1000L); Set(action, "Base", 100m);
        Set(action, "Purchases", purchases); Set(action, "NativeSettings", settings); Set(action, "Input", Field(settings, "customPowerAmount"));
        Set(action, "Purchase", purchases.GetType().GetMethod("buyCustomPower", All));
        var grant = NativeActionGrant(action, () => true); Set(action, "Grant", grant);
        var calls = Program.FiniteExpNativeCalls;
        Program.FinitePurchaseRendered = () => { throw new InvalidOperationException("native EXP model committed then refresh failed"); };
        try
        {
            var result = CallMain("RunFiniteAction", action, grant);
            Equal("ModelAppliedRenderFailed", Get(result, "Disposition").ToString(), "actual native EXP accounting survives refresh failure");
            Equal(true, Property(result, "Verified"), "native EXP cost/base/currency proof admits durable commit");
            Equal(101f, Get(Character, "energyPower"), "native EXP raises exact base stat once");
            Equal(1000L - cost, Get(Character, "realExp"), "native EXP spends exact native cost once");
            Equal(17, Get(settings, "customPowerAmount"), "still-owned EXP input is returned even after native refresh throws");
            CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true));
            Equal(calls + 1, Program.FiniteExpNativeCalls, "refresh failure cannot re-invoke same native EXP purchase");
        }
        finally { Program.FinitePurchaseRendered = null; }

        var current = NativeTypedLeaf("ExpPurchaseAction", "exp-purchase:0", "auto-exp", "101", "FiniteAction, NativeSettings, Energy");
        Set(current, "Stat", 0); Set(current, "Amount", 1L); Set(current, "Cost", cost); Set(current, "Currency", 1000L - cost); Set(current, "Base", 101m);
        Set(current, "Purchases", purchases); Set(current, "NativeSettings", settings); Set(current, "Input", Field(settings, "customPowerAmount"));
        Set(current, "Purchase", purchases.GetType().GetMethod("buyCustomPower", All)); grant = NativeActionGrant(current, () => true); Set(current, "Grant", grant);
        Program.FinitePurchaseRendered = () => Set(settings, "customPowerAmount", 999);
        try
        {
            var result = CallMain("RunFiniteAction", current, grant);
            Equal(true, Get(result, "Committed"), "native EXP commitment is retained across foreign borrowed-input edit");
            Equal(999, Get(settings, "customPowerAmount"), "EXP return cannot overwrite newer native input edit");
        }
        finally { Program.FinitePurchaseRendered = null; }

        plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "native exact digger baseline");
        var controller = Get(Character, "allDiggers"); var diggers = (IList)Get(Get(Character, "diggers"), "diggers");
        Set(controller, "character", Character); Child(controller, "tooltip");
        Set(controller, "baseGoldCost", Enumerable.Repeat(10d, 12).ToList());
        var level = (long)Get(diggers[0], "maxLevel"); var price = (double)Invoke(controller, "upgradeCost", 0);
        Set(Character, "realGold", price * 5);
        action = NativeTypedLeaf("DiggerPurchaseAction", "digger-purchase:0", "digger-purchases", level.ToString(), "FiniteAction, Support");
        Set(action, "Id", 0); Set(action, "Level", level); Set(action, "Gold", price * 5); Set(action, "Cost", price + 1); Set(action, "Reserve", 0d);
        calls = Program.FiniteDiggerNativeCalls;
        var rejected = CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true));
        Equal(false, Get(rejected, "Committed"), "changed native digger price rejects before invocation");
        Equal(calls, Program.FiniteDiggerNativeCalls, "price proof rejection produces zero native digger calls");
        Set(action, "Cost", price);
        Program.FinitePurchaseRendered = () => { throw new InvalidOperationException("native digger model committed then refresh failed"); };
        try
        {
            var result = CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true));
            Equal("ModelAppliedRenderFailed", Get(result, "Disposition").ToString(), "native digger exact cost/level commitment survives refresh failure");
            Equal(level + 1, Get(diggers[0], "maxLevel"), "native digger maxLevel increases exactly once");
            Equal(price * 4, Get(Character, "realGold"), "native digger gold debit matches exact cost");
            CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true));
            Equal(calls + 1, Program.FiniteDiggerNativeCalls, "same native digger purchase never repeats after refresh failure");
        }
        finally { Program.FinitePurchaseRendered = null; }
    }

    private static void CheckNativeFiniteCallbackConflicts()
    {
        foreach (var change in new[] { "bar", "gear", "income", "model", "epoch" })
        {
            var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "native finite callback " + change);
            var blood = Get(Character, "bloodMagic"); Set(Get(Character, "bloodSpells"), "character", Character);
            Set(blood, "bloodPoints", 100d); Set(blood, "rebirthPower", 5d);
            var action = NativeNumberLeaf("native-conflict-" + change, 100d, 5d); var id = Get(action, "Identity");
            var footprint = Domain("FiniteAction, Magic, Gear, Support, Route"); Set(action, "Footprint", footprint);
            var claim = CallMain("BeginExecutionClaim", "action-checks", Get(id, "Acquisition"), footprint, (Func<bool>)(() => true));
            var setup = ExactClaimSetup(Get(Get(Character, "inventory"), "head"), "action-checks");
            CallMain("ApplyExecutionClaim", claim, setup);
            Equal(true, CallMain("FiniteActionSetupVerified", claim, setup), "callback matrix begins with exact verified setup/" + change);
            Program.FiniteActionSpellRendered = () => {
                if (change == "bar")
                {
                    var skill = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0]; Set(skill, "magic", (long)Get(skill, "magic") + 1);
                }
                else if (change == "gear")
                {
                    var head = Get(Get(Character, "inventory"), "head"); Set(head, "curAttack", (float)Get(head, "curAttack") + 1f);
                }
                else if (change == "income")
                {
                    var machine = Get(Character, "machine"); Set(machine, "realBaseGold", (double)Get(machine, "realBaseGold") + 1d);
                }
                else if (change == "model") Set(Character, "bloodMagic", System.Runtime.Serialization.FormatterServices.GetUninitializedObject(blood.GetType()));
                else Set(Get(Character, "stats"), "rebirthNumber", 8L);
            };
            try
            {
                var calls = NativeNumberFlushCalls;
                var result = CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true, claim));
                Equal(105d, Get(blood, "rebirthPower"), "actual original native model commits exact NUMBER before conflict/" + change);
                Equal(false, Property(result, "Verified"), "foreign callback cannot lend fresh dependent authority/" + change);
                Equal(true, Get(result, "Durable"), "callback conflict retains durable committed/unknown evidence/" + change);
                CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true, claim));
                Equal(calls + 1, NativeNumberFlushCalls, "callback conflict never repeats native effect/" + change);
                var energy = CallMain("ResolveExecutionGrant", "healthy-energy", "entry", Domain("Energy"), (Func<bool>)(() => true), null, null);
                Equal(true, CallMain("ValidateExecutionGrant", energy, Domain("Energy")), "foreign callback leaves independent Energy grant healthy/" + change);
            }
            finally { Program.FiniteActionSpellRendered = null; CallMain("EndExecutionClaim", claim, false); }
        }
    }

    private static void CheckNativeFruitLeaves()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "native fruit reservation baseline");
        var ygg = Get(Character, "yggdrasil"); var all = Get(Character, "yggdrasilController"); Set(all, "character", Character);
        var fruits = ListField(ygg, "fruits", 2); var controllers = ArrayField(all, "fruits", 1);
        var controller = controllers.GetValue(0); Set(controller, "character", Character); Child(controller, "tooltip");
        Set(all, "activationCost", new System.Collections.Generic.List<long> { 100L, 100L });
        Set(all, "usesEnergy", new System.Collections.Generic.List<bool> { false, false });
        Set(all, "baseSeedReward", new System.Collections.Generic.List<long> { 10L, 10L });
        Set(Get(Character, "NGUController"), "normalMagicBoostFactor", Enumerable.Repeat(.01f, 7).ToList());
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", true);
        Set(fruits[0], "maxTier", 1L); Set(fruits[1], "maxTier", 1L); Set(fruits[1], "permCostPaid", true);
        ListField(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed", 600);
        var energy = CallMain("GlobalPresetLaneState", true); var magic = Get(Character, "magic"); var pool = (long)Get(magic, "curMagic");
        var presentations = 0; Program.FiniteFruitRendered = () => { presentations++; throw new InvalidOperationException("native fruit renderer failed after activation"); };
        try { Equal(true, CallMain("ActivateFruitAction", 0), "actual native fruit activation commits despite final rendering failure"); }
        finally { Program.FiniteFruitRendered = null; }
        Equal(pool - 100, Get(magic, "curMagic"), "fruit spends exact current Magic cost once");
        Equal(0L, Get(magic, "idleMagic"), "fruit reservation release funds CURRENT native baseline remainder");
        Equal(pool - 100, ((IList)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic")), "native fruit funding returns only real remaining Magic");
        Equal(energy, CallMain("GlobalPresetLaneState", true), "Magic fruit preserves every Energy allocation/basic reservation");
        Equal(1, presentations, "activation renderer entered once");
        Invoke(all, "updateFruitTimers");
        Equal(true, Get(fruits[1], "activated"), "native permanent-cost perk auto-activates fruit without purchase leaf");
        Equal(pool - 100, Get(magic, "curMagic"), "permanent activation perk consumes no extra Magic");
        Set(fruits[0], "seconds", 3600f); Set(fruits[0], "eatFruit", false); Set(all, "curPage", 2);
        var expectedSeeds = Invoke(controller, "harvestSeedReward", 0, (int)Invoke(controller, "tierFactor", 1), 1f);
        var seeds = (long)Get(ygg, "seeds");
        Program.FiniteFruitRendered = () => { throw new InvalidOperationException("native harvest renderer failed after reward"); };
        try { Equal(true, CallMain("HarvestFruitAction", 0, true, false, null, null), "one native identified harvest survives committed render failure"); }
        finally { Program.FiniteFruitRendered = null; }
        Equal(seeds + (long)expectedSeeds, Get(ygg, "seeds"), "native harvest preserves exact seed/perk formula");
        Equal(1, Get(fruits[0], "harvests"), "exactly one native fruit harvest accounted");
        Equal(2, Get(all, "curPage"), "identified native fruit does not traverse or overwrite pages");
        Equal(false, CallMain("HarvestFruitAction", 0, true, false, null, null), "same consumed opportunity cannot harvest again");
        Equal(seeds + (long)expectedSeeds, Get(ygg, "seeds"), "rejected repeated harvest changes no reward accounting");
    }

    private static void CheckNativeDirtyResultRecovery()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "native result recovery baseline");
        var blood = Get(Character, "bloodMagic"); Set(Get(Character, "bloodSpells"), "character", Character);
        Set(blood, "bloodPoints", 10d); Set(blood, "rebirthPower", 0d);
        var action = NativeNumberLeaf("recover-first", 10d, 0d); var id = Get(action, "Identity");
        var hash = Invoke(Plugin.GetType("NGUInjector.JsonFiles", true), "ComputeRevision", Encoding.UTF8.GetBytes((string)Get(id, "Key")));
        var path = Path.Combine((string)Get(MainType, "_dir"), "finite-actions-v1", hash + ".json"); FileStream held = null;
        Program.FiniteActionSpellRendered = () => held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var first = CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true));
            Equal(true, Get(first, "Committed"), "native recovery predecessor is known committed");
            Equal(false, Get(first, "Durable"), "native recovery predecessor result initially fails storage");
        }
        finally { Program.FiniteActionSpellRendered = null; held?.Dispose(); }
        var calls = NativeNumberFlushCalls;
        Set(blood, "bloodPoints", 5d); var next = NativeNumberLeaf("recover-second", 5d, 10d);
        var result = CallMain("RunFiniteAction", next, NativeActionGrant(next, () => true));
        Equal(true, Property(result, "Verified"), "bounded storage-only flush admits current native successor");
        Equal(calls + 1, NativeNumberFlushCalls, "recovery invokes successor once, never predecessor again");
        Equal(15d, Get(blood, "rebirthPower"), "native recovery preserves exact total accounting");
        Equal(false, Property(CallMain("RunFiniteAction", action, NativeActionGrant(action, () => true)), "Verified"), "flushed result grants no fresh historical authority");
    }

    private static void CheckNativePreparationRetry()
    {
        var plan = SetupInlineStagePlan(); RunVerifiedInline(plan, "native preparation retry baseline");
        var swaps = Get(typeof(Program), "Swaps"); var selections = 0;
        Program.FinitePreparationSelectionStarting = () => { selections++; throw new InvalidOperationException("native selection input unavailable"); };
        try
        {
            var first = (string)CallMain("RunFightPrep", plan, false, false, "native retry", null, "explicit-native-prep-1");
            Check(first.Contains("native selection input unavailable"), "pre-model acquisition failure is reported without continuing");
            var original = CallMain("ExportFightPreparationCommitment"); var identity = Property(MainInstance, "FightPreparationIdentity");
            Equal(swaps, Get(typeof(Program), "Swaps"), "failed acquisition performs no native gear write");
            Equal(false, Property(MainInstance, "BossPushRunning"), "failed acquisition cannot mark preparation completed or start fight");
            var recordBytes = Json(original, "original").ToString();
            var selectionPath = Path.Combine((string)Get(MainType, "_dir"), "execution-fight-prep-selection.json"); File.WriteAllText(selectionPath, "{}");
            Set(Get(Character, "rebirthTime"), "totalseconds", 517d); Set(Get(Character, "totalPlaytime"), "totalseconds", 1517d);
            Program.FinitePreparationSelectionStarting = () => selections++;
            using (var held = new FileStream(selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var second = (string)CallMain("RunFightPrep", plan, false, false, "native retry", null, "explicit-native-prep-1");
                Check(second.Contains("blocked"), "selected commitment storage failure withholds native setup/fight");
                Equal(identity, Property(MainInstance, "FightPreparationIdentity"), "retry preserves original preparation identity");
                Equal(recordBytes, Json(CallMain("ExportFightPreparationCommitment"), "original").ToString(), "retry preserves original entry/start/window and options");
                Check(Get(MainInstance, "_fightPreparationSetup") != null, "selected exact intent survives storage failure for retry/review");
                Equal(swaps, Get(typeof(Program), "Swaps"), "selected intent is persisted before native gear effects");
            }
            var selected = Get(MainInstance, "_fightPreparationSetup"); var count = selections;
            var third = (string)CallMain("RunFightPrep", plan, false, false, "native retry", null, "explicit-native-prep-1");
            Check(third.Contains("boss push running"), "same identified process resumes after storage recovery: " + third);
            Equal(count, selections, "storage retry reuses acquired selection without a replacement search");
            Check(ReferenceEquals(selected, Get(MainInstance, "_fightPreparationSetup")), "retry retains the SAME selected setup object");
            Equal(identity, Property(MainInstance, "FightPreparationIdentity"), "successful retry retains original process ID");
            Equal(recordBytes, Json(CallMain("ExportFightPreparationCommitment"), "original").ToString(), "successful retry does not renew original preparation time/window");
            Equal(true, Property(MainInstance, "FightPreparationVerified"), "boss request follows real exact native setup verification");
            CallMain("StopBossPush");
        }
        finally { Program.FinitePreparationSelectionStarting = null; }
    }

    private static void AdvanceFiniteActionBaseline(object plan)
    {
        var scheduler = Get(MainInstance, "_protectedScheduler");
        var entry = Get(MainInstance, "_protectedGeneration") + ":" + Property(scheduler, "Entry");
        var name = (string)Property(Get(MainType, "Settings"), "CustomPlanFile");
        CallMain("OverrideCustomStage", JsonParse("{\"name\":\"" + name + "\",\"expectedRevision\":\"" + Get(plan, "RuntimeRevision")
            + "\",\"expectedEntry\":\"" + entry + "\",\"action\":\"skip\",\"stageIndex\":0}"));
        CallMain("RunCustomPlanAutomation", plan);
        var selected = Get(Get(MainInstance, "_inlineStageSelection"), "Baseline");
        Check(((Array)Get(selected, "Gear")).Cast<object>().Any(x => (int)Property(x, "slot") == -1 && (int)Property(x, "id") == 551),
            "real scheduler B retains its independently acquired Magic-cap head under action claim");
        Equal(552, Get(Get(Get(Character, "inventory"), "head"), "id"), "B intent does not overwrite exact temporary action setup");
        var path = (string)MainType.GetProperty("ProtectedCheckpointPath", All).GetValue(MainInstance);
        var saved = JsonParse(File.ReadAllText(path));
        Equal("1", Json(Json(saved, "scheduler"), "stage").ToString(), "CURRENT B is a durably selected real scheduler row, not a bare published return target");
        Equal(Property(scheduler, "Entry").ToString(), Property(Json(Json(saved, "scheduler"), "entry"), "Value"), "CURRENT B durable entry matches its actual acquisition");
    }

    private static void CheckNativePitAction()
    {
        var args = SetupAdventureDynamic("finite-pit-A", "[0,0,0,0,0,0,0,0,0,0,0,0,10,0]");
        var adventureController = Get(Character, "adventureController");
        Set(Child(adventureController, "enemyAI"), "player", Child(adventureController, "playerController"));
        var inventory = Get(Character, "inventory"); var items = (IList)Get(inventory, "inventory");
        var originalHead = Get(inventory, "head");
        for (var i = 0; i < 3; i++)
        {
            Set(items[i], "id", i == 2 ? 510 : 550 + i); Set(items[i], "type", Get(originalHead, "type")); Set(items[i], "bossRequired", 1);
            Set(items[i], "removable", true); Set(items[i], "curAttack", 1f); Set(items[i], "curDefense", 1f);
            Set(items[i], "spec1Type", Enum.Parse(Field(items[i], "spec1Type").FieldType, i == 0 ? "EnergyCap" : i == 1 ? "MagicCap" : "None"));
            Set(items[i], "spec1Cur", i == 2 ? 0f : 10000f); Set(items[i], "spec1Cap", 10000000f);
        }
        var tempHead = items[2]; var nextHead = items[1];
        Set(tempHead, "level", 90);
        CallMain("EquipGlobalPreset", args, null);
        Equal(1000, Get(Get(Character, "adventure"), "zone"), "pit A is real active baseline farming, not an assumed safe route");
        Equal(550, Get(Get(inventory, "head"), "id"), "farming A acquired Energy-cap head");
        var pit = Get(Character, "pit") ?? Child(Character, "pit");
        var ctrl = Get(Character, "pitController") ?? Child(Character, "pitController");
        Set(ctrl, "character", Character); Set(ctrl, "inventoryController", Controller); Set(ctrl, "itemInfo", Get(Controller, "itemInfo"));
        Set(Child(pit, "pitTime"), "totalseconds", 1e9); Set(pit, "tossCount", 0); Set(pit, "totalGold", 0d);
        Set(pit, "tier1TRewarded", true); Set(pit, "tier2TRewarded", true); Set(pit, "tier3TRewarded", true);
        Set(pit, "tier4TRewarded", true); Set(pit, "tier5TRewarded", true);
        var rng = Activator.CreateInstance(Field(pit, "pitState").FieldType); Set(rng, "s0", 1); Set(pit, "pitState", rng);
        Program.FinitePitRng = rng; Program.FinitePitRenderCalls = 0;
        var achievements = Get(Character, "achievements") ?? Child(Character, "achievements"); ListField(achievements, "achievementComplete", 512);
        Set(Child(Character, "allAchievements"), "character", Character);
        Set(Get(Character, "settings"), "badge2Part2Complete", true);
        ListField(Get(inventory, "itemList"), "itemMaxxed", 600);
        Set(Get(Character, "allDiggers"), "baseGoldCost", Enumerable.Repeat(1e200, 12).ToList());
        Set(Character, "realGold", 1e13); Set(Get(MainType, "Settings"), "_moneyPitThreshold", 200000d);
        var acquired = false; var seen = new List<int>(); var originalRender = GearRenderObserver;
        GearRenderObserver = () => {
            var head = (int)Get(Get(inventory, "head"), "id"); seen.Add(head);
            if (head == 510) acquired = true;
        };
        Program.FinitePitSetupReady = () => {
            Program.FinitePitSetupReady = null;
            var library = Get(Get(MainType, "CustomPlanStore"), "<PresetLibrary>k__BackingField"); var root = Invoke(library, "ReadExternalLibrary");
            Invoke(Json(root, "Presets"), "Add", "", JsonParse("{\"Id\":\"finite-pit-B\",\"Name\":\"B\",\"Weights\":[0,0,0,0,0,0,0,0,0,0,0,0,0,1],\"SupportMode\":\"current\",\"AutoUpdate\":true}"));
            var revision = Invoke(library, "SaveExternalLibrary", root, Property(Json(root, "__externalUiRevision"), "Value"));
            CallMain("EquipGlobalPreset", JsonParse("{\"presetId\":\"finite-pit-B\",\"libraryRevision\":\"" + revision + "\"}"), null);
            Equal(510, Get(Get(inventory, "head"), "id"), "new actual dashboard B stays covered by pit claim");
        };
        Program.FinitePitRender = () => {
            Equal(510, Get(Get(inventory, "head"), "id"), "actual native pit reward runs in selected pit gear");
            Equal(0d, Get(Character, "realGold"), "native feed already consumed gold before render fault");
            Equal(1, Get(pit, "tossCount"), "native toss count committed before render fault");
            throw new InvalidOperationException("native PIT committed then renderer threw");
        };
        try
        {
            var result = (string)CallMain("RequestMoneyPit");
            Check(result.Contains("Money Pit Reward"), "model-committed native pit renderer failure still publishes durable reward: " + result
                + "; gold=" + Get(Character, "realGold") + "; total=" + Get(pit, "totalGold") + "; toss=" + Get(pit, "tossCount")
                + "; renders=" + Program.FinitePitRenderCalls + "; error=" + (Program.LastFinitePitResult == null ? "none" : Get(Program.LastFinitePitResult, "Error")));
            Equal(true, acquired, "native pit selection actually swaps away from farming A");
            Equal(1e13, Get(pit, "totalGold"), "native pit totalGold exact committed addition");
            Equal(91, Get(tempHead, "level"), "actual native allLevelUp rewarded selected instance");
            Equal(551, Get(Get(inventory, "head"), "id"), "pit claim releases directly to CURRENT B");
            Check(!seen.SkipWhile(x => x != 510).Skip(1).Contains(550), "pit release never flashes historical farming A");
            CallMain("RequestMoneyPit"); Equal(1, Program.FinitePitRenderCalls, "native committed feed is never repeated");
        }
        finally { GearRenderObserver = originalRender; Program.FinitePitRender = null; Program.FinitePitSetupReady = null; }
    }
}
