using System;
using System.Collections;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    internal static Action RepeatableDiggerStarting;
    internal static Action<int, double> RepeatableDiggerCostRead;
    internal static int RepeatableDiggerCalls;
    internal static bool RepeatablePurchaseFixtureActive;

    // Add to the existing game-loader hooks. Accounting and native prices remain untouched.
    private static void ConfigureRepeatablePurchaseGameFixture(AssemblyDefinition game)
    {
        var bonus = game.MainModule.GetType("Wandoos98Controller").Methods.Single(x => x.Name == "wandoosBonus");
        var fallback = bonus.Body.Instructions.ToArray();
        using (var original = AssemblyDefinition.ReadAssembly(game.MainModule.FileName))
        {
            var native = original.MainModule.GetType("Wandoos98Controller").Methods.Single(x => x.Name == "wandoosBonus");
            if (native.HasParameters || native.Body.ExceptionHandlers.Count != 0 || bonus.Body.ExceptionHandlers.Count != 0)
                throw new InvalidOperationException("Unsupported native Wandoos getter shape for purchase fixture.");
            bonus.Body = new Mono.Cecil.Cil.MethodBody(bonus) { InitLocals = native.Body.InitLocals, MaxStackSize = native.Body.MaxStackSize };
            foreach (var variable in native.Body.Variables)
                bonus.Body.Variables.Add(new VariableDefinition(game.MainModule.ImportReference(variable.VariableType)));
            var instructions = native.Body.Instructions.ToDictionary(x => x, x => Instruction.Create(OpCodes.Nop));
            foreach (var instruction in native.Body.Instructions)
            {
                var copy = instructions[instruction]; copy.OpCode = instruction.OpCode;
                var operand = instruction.Operand;
                if (operand is Instruction branch) operand = instructions[branch];
                else if (operand is Instruction[] branches) operand = branches.Select(x => instructions[x]).ToArray();
                else if (operand is VariableDefinition variable) operand = bonus.Body.Variables[variable.Index];
                else if (operand is FieldReference field)
                    operand = game.MainModule.GetType(field.DeclaringType.FullName).Fields.Single(x => x.Name == field.Name);
                else if (operand is MethodReference method) operand = game.MainModule.ImportReference(method);
                else if (operand is TypeReference type) operand = game.MainModule.ImportReference(type);
                copy.Operand = operand; bonus.Body.Instructions.Add(copy);
            }
        }
        // Preserve either the parent's constant fallback or its retained native getter.
        var first = bonus.Body.Instructions[0]; var bonusIl = bonus.Body.GetILProcessor();
        bonusIl.InsertBefore(first, Instruction.Create(OpCodes.Call,
            game.MainModule.ImportReference(typeof(Program).GetMethod(nameof(RepeatablePurchaseNativeWandoos), All))));
        bonusIl.InsertBefore(first, Instruction.Create(OpCodes.Brtrue, first));
        foreach (var instruction in fallback)
        {
            if (instruction.Operand is VariableDefinition variable) instruction.Operand = bonus.Body.Variables[variable.Index];
            bonusIl.InsertBefore(first, instruction);
        }
        var controller = game.MainModule.GetType("AllGoldDiggerController");
        var buy = controller.Methods.Single(x => x.Name == "upgradeMaxLevel");
        buy.Body.GetILProcessor().InsertBefore(buy.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            game.MainModule.ImportReference(typeof(Program).GetMethod(nameof(RepeatableDiggerInvocation), All))));
        var cost = controller.Methods.Single(x => x.Name == "upgradeCost");
        foreach (var ret in cost.Body.Instructions.Where(x => x.OpCode == OpCodes.Ret).ToArray())
        {
            var il = cost.Body.GetILProcessor(); ret.OpCode = OpCodes.Dup;
            var id = Instruction.Create(OpCodes.Ldarg_1); il.InsertAfter(ret, id);
            var observe = Instruction.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(Program).GetMethod(nameof(RepeatableDiggerPrice), All)));
            il.InsertAfter(id, observe); il.InsertAfter(observe, Instruction.Create(OpCodes.Ret));
        }
    }

    public static void RepeatableDiggerInvocation() { RepeatableDiggerCalls++; RepeatableDiggerStarting?.Invoke(); }
    public static void RepeatableDiggerPrice(double price, int id) => RepeatableDiggerCostRead?.Invoke(id, price);
    public static bool RepeatablePurchaseNativeWandoos() => RepeatablePurchaseFixtureActive;
}

public static partial class PresetIntegrationChecks
{
    private static object RepeatablePurchasePlan;

    private static IList PrepareRepeatableDiggerPurchases()
    {
        Program.RepeatableDiggerStarting = null; Program.RepeatableDiggerCostRead = null; Program.FinitePurchaseRendered = null;
        RepeatablePurchasePlan = SetupInlineStagePlan();
        var settings = Get(MainType, "Settings"); Set(settings, "_upgradeDiggers", true);
        Set(settings, "_moneyPitThreshold", 1e300d);
        Set(Get(Character, "settings"), "diggersOn", true); Set(Character, "highestBoss", 30); Set(Character, "realGold", 100d);
        var controller = Get(Character, "allDiggers");
        Set(controller, "baseGoldCost", Enumerable.Repeat(1e200d, 12).ToList());
        Set(controller, "gpsGrowthRate", Enumerable.Repeat(2d, 12).ToList());
        var rows = (IList)Get(Get(Character, "diggers"), "diggers");
        foreach (var row in rows) { Set(row, "maxLevel", 0L); Set(row, "curLevel", 0L); Set(row, "active", false); }
        ((IList)Get(Get(Character, "diggers"), "activeDiggers")).Clear();
        return rows;
    }

    private static void DiggerPrice(int id, double price) => ((IList)Get(Get(Character, "allDiggers"), "baseGoldCost"))[id] = price;

    private static void CheckRepeatablePurchaseActions()
    {
        var before = Assertions;
        Program.RepeatablePurchaseFixtureActive = true;
        try
        {
            CheckRepeatableDiggerSelection();
            CheckRepeatableDiggerAdmission();
            CheckCompatibleDiggerClaims();
            CheckRepeatableDiggerPreflight();
            CheckRepeatableDiggerFaults();
            CheckRepeatableDiggerForeignProof();
            CheckRepeatableDiggerGrowth(false, false);
            CheckRepeatableDiggerGrowth(true, false);
            CheckRepeatableDiggerGrowth(false, true);
            CheckRepeatablePurchaseCadence();
            Console.WriteLine("Repeatable purchases: " + (Assertions - before) + " native assertions. No live actions.");
        }
        finally { Program.RepeatablePurchaseFixtureActive = false; }
    }

    private static void CheckRepeatableDiggerSelection()
    {
        foreach (var scenario in new[] { "unowned", "unaffordable-unowned", "cheapest", "tie", "hardcap", "exact-price" })
        {
            var rows = PrepareRepeatableDiggerPurchases();
            Set(rows[0], "maxLevel", 1L); DiggerPrice(0, 1d); DiggerPrice(1, 50d);
            var selected = 1;
            if (scenario == "unaffordable-unowned") { DiggerPrice(1, 101d); selected = 0; }
            if (scenario == "cheapest") { Set(rows[1], "maxLevel", 1L); DiggerPrice(0, 3d); DiggerPrice(1, 2d); }
            if (scenario == "tie") { Set(rows[1], "maxLevel", 1L); DiggerPrice(1, 1d); selected = 0; }
            if (scenario == "hardcap")
            {
                Set(rows[0], "maxLevel", Invoke(Get(Character, "allDiggers"), "hardCapLevel", 0));
                DiggerPrice(1, 10d);
            }
            if (scenario == "exact-price") DiggerPrice(1, 100d);
            var before = rows.Cast<object>().Select(x => (long)Get(x, "maxLevel")).ToArray();
            var price = (double)Invoke(Get(Character, "allDiggers"), "upgradeCost", selected);
            var calls = Program.RepeatableDiggerCalls; var support = CallMain("CombatPrepSupportKey");
            var result = CallMain("PurchaseActionDiggers");
            Equal(true, Property(result, "Verified"), "selected purchase has native accounting proof/" + scenario + ": " + Get(result, "Reason"));
            Equal(calls + 1, Program.RepeatableDiggerCalls, "one native call despite other affordable upgrades/" + scenario);
            Equal(string.Join(",", before.Select((level, i) => level + (i == selected ? 1 : 0))),
                string.Join(",", rows.Cast<object>().Select(x => Get(x, "maxLevel"))), "exact selected maximum increment/" + scenario);
            Equal(100d - price, Get(Character, "realGold"), "native price with no MoneyPitThreshold reserve/" + scenario);
            Equal(support, CallMain("CombatPrepSupportKey"), "purchase never changes current levels, flags or selection/" + scenario);
        }
    }

    private static void CheckRepeatableDiggerAdmission()
    {
        foreach (var blocker in new[] { "toggle", "boss", "native-unlock", "controller", "missing-table", "short-table", "short-rows", "bad-row", "negative-level", "nan-gold", "infinite-gold", "zero-price", "nan-price", "growth", "unprepared-claim", "lifecycle" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); DiggerPrice(0, 10d);
            var controller = Get(Character, "allDiggers"); object claim = null;
            if (blocker == "toggle") Set(Get(MainType, "Settings"), "_upgradeDiggers", false);
            if (blocker == "boss") Set(Character, "highestBoss", 29);
            if (blocker == "native-unlock") Set(Get(Character, "settings"), "diggersOn", false);
            if (blocker == "controller") Set(controller, "character", null);
            if (blocker == "missing-table") Set(controller, "baseGoldCost", null);
            if (blocker == "short-table") ((IList)Get(controller, "baseGoldCost")).RemoveAt(11);
            if (blocker == "short-rows") rows.RemoveAt(11);
            if (blocker == "bad-row") rows[11] = null;
            if (blocker == "negative-level") Set(rows[11], "maxLevel", -1L);
            if (blocker == "nan-gold") Set(Character, "realGold", double.NaN);
            if (blocker == "infinite-gold") Set(Character, "realGold", double.PositiveInfinity);
            if (blocker == "zero-price") DiggerPrice(0, 0d);
            if (blocker == "nan-price") DiggerPrice(0, double.NaN);
            if (blocker == "growth") ((IList)Get(controller, "gpsGrowthRate"))[0] = 1d;
            if (blocker == "unprepared-claim") claim = CallMain("BeginExecutionClaim", "manual-support", "purchase-refusal", Domain("Support"), (Func<bool>)(() => true));
            if (blocker == "lifecycle") CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), "Quiescing"));
            var gold = Get(Character, "realGold"); var calls = Program.RepeatableDiggerCalls;
            try
            {
                var result = CallMain("PurchaseActionDiggers");
                Equal(false, Get(result, "Invoked"), "unavailable purchase never invokes native code/" + blocker);
                Equal(calls, Program.RepeatableDiggerCalls, "native call count unchanged/" + blocker);
                Equal(gold, Get(Character, "realGold"), "invalid native state is preserved, not repaired/" + blocker);
            }
            finally { if (claim != null) CallMain("EndExecutionClaim", claim, false); }
        }
    }

    private static object BeginCompatibleDiggerClaim(string domains = "Support")
    {
        var model = Character; var owner = MainInstance;
        var claim = CallMain("BeginExecutionClaim", "compatible-purchase:" + domains, "selected-current-setup", Domain(domains),
            (Func<bool>)(() => ReferenceEquals(model, Get(MainType, "Character")) && ReferenceEquals(owner, Get(MainType, "reference"))));
        try
        {
            var setup = CallMain("AcquireFiniteActionSetup", claim, CallMain("CombatPrepEquipped"),
                Property(CallMain("CaptureSupportState"), "Selection"), false, false);
            Equal(true, CallMain("FiniteActionSetupVerified", claim, setup), "compatible owner has actually applied and verified its exact setup");
            return claim;
        }
        catch { CallMain("EndExecutionClaim", claim, false); throw; }
    }

    private static void CheckCompatibleDiggerClaims()
    {
        foreach (var domains in new[] { "Support", "Support, FiniteAction", "Gear, Support, FiniteAction", "split-owners" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); DiggerPrice(0, 10d);
            Set(Get(Character, "adventure"), "zone", -1);
            var claim = BeginCompatibleDiggerClaim(domains == "split-owners" ? "Support" : domains); var setup = Get(claim, "Setup");
            var additional = domains == "split-owners" ? BeginCompatibleDiggerClaim("FiniteAction") : null;
            var observedGear = Get(claim, "ObservedGear"); var observedSupport = Get(claim, "ObservedSupport");
            var selectedSupport = Get(setup, "Support"); var active = CallMain("CombatPrepSupportKey");
            var calls = Program.RepeatableDiggerCalls;
            try
            {
                var result = CallMain("PurchaseActionDiggers");
                Equal(true, Property(result, "Verified"), "exact current overlapping claim permits background growth/" + domains + ": " + Get(result, "Reason"));
                Equal(calls + 1, Program.RepeatableDiggerCalls, "compatible admission invokes only one native upgrade/" + domains);
                Equal(1L, Get(rows[0], "maxLevel"), "compatible background purchase raises only the selected maximum/" + domains);
                Equal(90d, Get(Character, "realGold"), "compatible background purchase pays its actual price/" + domains);
                Check(ReferenceEquals(setup, Get(claim, "Setup")) && ReferenceEquals(selectedSupport, Get(setup, "Support")), "purchase does not replace the other owner's setup or support/" + domains);
                Check(ReferenceEquals(observedGear, Get(claim, "ObservedGear")), "purchase does not adopt a new claim gear observation/" + domains);
                Equal(observedSupport, Get(claim, "ObservedSupport"), "purchase does not replace the claim's support observation/" + domains);
                Equal(active, CallMain("CombatPrepSupportKey"), "active levels and flags remain unchanged under the claim/" + domains);
                Equal(true, CallMain("FiniteActionSetupVerified", claim, setup), "original claim setup remains verified after maximum growth/" + domains);
                if (additional != null) Equal(true, CallMain("FiniteActionSetupVerified", additional, Get(additional, "Setup")), "every disjoint overlapping owner retains its own verified setup");
            }
            finally
            {
                if (additional != null) CallMain("EndExecutionClaim", additional, false);
                CallMain("EndExecutionClaim", claim, false);
            }
        }

        foreach (var predicate in new[] { "BoundaryAuthorized", "MaterialValid", "GearAppliedValid" })
        {
            PrepareRepeatableDiggerPurchases(); DiggerPrice(0, 10d);
            Set(Get(Character, "adventure"), "zone", -1);
            var claim = BeginCompatibleDiggerClaim(); Set(Get(claim, "Setup"), predicate, (Func<bool>)(() => false));
            var calls = Program.RepeatableDiggerCalls;
            try
            {
                var result = CallMain("PurchaseActionDiggers");
                Equal(false, Get(result, "Invoked"), "a current claim cannot bypass its blocked setup boundary/" + predicate);
                Equal(calls, Program.RepeatableDiggerCalls, "blocked setup observation performs no native purchase/" + predicate);
                Equal(100d, Get(Character, "realGold"), "blocked setup observation spends no gold/" + predicate);
            }
            finally { CallMain("EndExecutionClaim", claim, false); }
        }

        foreach (var change in new[] { "gear", "support", "setup", "permission", "budget", "observation", "new-owner" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); DiggerPrice(0, 10d); Set(rows[11], "maxLevel", 2L);
            Set(Get(Character, "adventure"), "zone", -1);
            var claim = BeginCompatibleDiggerClaim(); var setup = Get(claim, "Setup");
            var reads = 0; var fired = false; object added = null;
            Program.RepeatableDiggerCostRead = (id, price) => {
                if (id != 0 || ++reads != 2) return;
                fired = true;
                if (change == "gear")
                {
                    var head = Get(Get(Character, "inventory"), "head"); Set(head, "curAttack", (float)Get(head, "curAttack") + 1f);
                }
                if (change == "support") Set(rows[11], "curLevel", 1L);
                if (change == "setup") Set(claim, "Setup", Invoke(setup, "MemberwiseClone"));
                if (change == "permission") Set(claim, "Current", (Func<bool>)(() => false));
                if (change == "budget") Set(setup, "Budget", .5d);
                if (change == "observation") Set(claim, "ObservedGear", ((Array)Get(claim, "ObservedGear")).Clone());
                if (change == "new-owner") added = CallMain("BeginExecutionClaim", "new-finite-owner", "after-purchase-capture", Domain("FiniteAction"), (Func<bool>)(() => true));
            };
            var calls = Program.RepeatableDiggerCalls;
            try
            {
                var result = CallMain("PurchaseActionDiggers");
                Equal(true, fired, "foreign claim/setup change occurs after compatible capture/" + change);
                Equal(false, Get(result, "Invoked"), "changed owner/setup cannot borrow the captured compatibility grant/" + change);
                Equal(calls, Program.RepeatableDiggerCalls, "foreign claim/setup change performs no upgrade/" + change);
                Equal(0L, Get(rows[0], "maxLevel"), "refused claim mutation leaves selected maximum alone/" + change);
                Equal(100d, Get(Character, "realGold"), "refused claim mutation spends no gold/" + change);
                if (change == "support") Equal(1L, Get(rows[11], "curLevel"), "foreign support level is retained without repair");
            }
            finally
            {
                Program.RepeatableDiggerCostRead = null;
                if (added != null) CallMain("EndExecutionClaim", added, false);
                CallMain("EndExecutionClaim", claim, false);
            }
        }
    }

    private static void CheckRepeatableDiggerPreflight()
    {
        foreach (var change in new[] { "price", "currency", "maximum", "row" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); DiggerPrice(0, 10d);
            var reads = 0; var fired = false;
            Program.RepeatableDiggerCostRead = (id, price) => {
                if (id != 0 || ++reads != 2) return;
                fired = true;
                if (change == "price") DiggerPrice(0, 20d);
                if (change == "currency") Set(Character, "realGold", 99d);
                if (change == "maximum") Set(rows[0], "maxLevel", 1L);
                if (change == "row") rows[0] = Activator.CreateInstance(rows[0].GetType());
            };
            var calls = Program.RepeatableDiggerCalls;
            try
            {
                var result = CallMain("PurchaseActionDiggers");
                Equal(true, fired, "native cost observer actually changes preflight input/" + change);
                Equal(false, Get(result, "Invoked"), "stale purchase does not enter native code/" + change);
                Equal(calls, Program.RepeatableDiggerCalls, "no fallback purchase in the stale pass/" + change);
            }
            finally { Program.RepeatableDiggerCostRead = null; }
            var next = CallMain("PurchaseActionDiggers");
            Equal(true, Property(next, "Verified"), "next ordinary pass fresh-reads changed inputs/" + change + ": " + Get(next, "Reason"));
            Equal(calls + 1, Program.RepeatableDiggerCalls, "only the fresh pass invokes/" + change);
        }
    }

    private static void CheckRepeatableDiggerFaults()
    {
        foreach (var fault in new[] { "before", "after", "currency", "reentrant" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); DiggerPrice(0, 10d);
            var calls = Program.RepeatableDiggerCalls; object nested = null;
            if (fault == "before") Program.RepeatableDiggerStarting = () => { throw new InvalidOperationException("before native digger mutation"); };
            if (fault == "after") Program.FinitePurchaseRendered = () => { throw new InvalidOperationException("after native digger mutation"); };
            if (fault == "currency") Program.FinitePurchaseRendered = () => Set(Character, "realGold", 1d);
            if (fault == "reentrant") Program.FinitePurchaseRendered = () => nested = CallMain("PurchaseActionDiggers");
            try
            {
                var result = CallMain("PurchaseActionDiggers");
                Equal(true, Get(result, "Invoked"), "one native invocation is observed/" + fault);
                Equal(calls + 1, Program.RepeatableDiggerCalls, "fault cannot trigger a second purchase in this pass/" + fault);
                Equal(fault == "before" ? 0L : 1L, Get(rows[0], "maxLevel"), "actual native maximum retained/" + fault);
                Equal(fault == "after" || fault == "reentrant", Property(result, "Verified"), "success requires exact native accounting/" + fault);
                if (fault == "after") Equal("ModelAppliedRenderFailed", Get(result, "Disposition").ToString(), "render failure retains exact native commitment");
                if (nested != null) Equal(false, Get(nested, "Invoked"), "callback cannot reenter the digger purchase service");
            }
            finally { Program.RepeatableDiggerStarting = null; Program.FinitePurchaseRendered = null; }
            if (fault == "currency") Set(Character, "realGold", 100d);
            var level = Get(rows[0], "maxLevel");
            var next = CallMain("PurchaseActionDiggers");
            Equal(true, Property(next, "Verified"), "next normal pass is not quarantined by a native failure/" + fault + ": " + Get(next, "Reason"));
            Equal((long)level + 1, Get(rows[0], "maxLevel"), "fresh retry starts at observed maximum/" + fault);
            Equal(calls + 2, Program.RepeatableDiggerCalls, "exactly one invocation in each independent pass/" + fault);
        }
    }

    private static void CheckRepeatableDiggerGrowth(bool renderFailure, bool foreign)
    {
        var rows = PrepareRepeatableDiggerPurchases();
        Set(Character, "highestBoss", 100);
        Set(rows[1], "maxLevel", 10L); ((IList)Get(Get(Character, "allDiggers"), "boostPerLevel"))[1] = .125d;
        SetupRepeatablePurchaseAutomaticPlan();
        var run = Get(MainInstance, "_combatPrepRun");
        Check(run != null && (string)Get(run, "Status") == "running", "real automatic prep acquired before background growth: " + CallMain("GetCustomAutomationStatus"));
        Equal(true, Get(run, "SetupVerified"), "growth fixture has real setup proof");
        Equal(true, Property(Get(run, "ResourceOutcome"), "Verified"), "growth fixture has real native allocation proof");
        var claim = BeginCompatibleDiggerClaim("Support, FiniteAction"); var claimedSetup = Get(claim, "Setup");
        Set(run, "LastRefresh", CallMain("get_CombatPrepClock"));
        CallMain("AdvanceCombatPrep");
        Equal(false, Get(run, "SetupVerified"), "real prep service suspends its exclusive setup grant under the compatible owner");
        Equal("running", Get(run, "Status"), "suspended setup permission retains the actual acquired phase");
        DiggerPrice(0, 10d); Set(Character, "realGold", 100d);
        var inputs = Property(run, "Inputs"); var material = Get(inputs, "SupportMaterial");
        var previousContext = Get(inputs, "SupportContext");
        var previousRates = new[] { Property(Get(inputs, "Snapshot"), "Current") }
            .Concat(((Array)Property(Get(inputs, "Snapshot"), "Profiles")).Cast<object>())
            .ToDictionary(profile => (string)Property(profile, "Key"), profile => (double)Property(profile, "WandoosEnergySpeedWithoutAt"));
        var setup = Get(run, "Setup"); var policy = Get(run, "Policy");
        var searches = PresetSearches; var selections = InlineGearSelections;
        var supports = (IDictionary)Get(inputs, "Support");
        var pinned = supports.Keys.Cast<string>().ToDictionary(key => key, key => new {
            Selection = Get(Get(supports[key], "Evaluation"), "Selection"), Budget = Get(supports[key], "BudgetFraction"), Enforce = Get(supports[key], "EnforceBudget")
        });
        var support = CallMain("CombatPrepSupportKey"); var allocations = CallMain("GlobalPresetAllocationState");
        var fields = new[] { "Started", "Duration", "PhaseIndex", "PhaseStarted", "PhaseDeadline", "DeadlineRebirthSeconds", "OwnerEntry", "ReplanCount" };
        var original = fields.Select(name => Get(run, name)).ToArray();
        var targets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
        if (renderFailure) Program.FinitePurchaseRendered = () => { throw new InvalidOperationException("owned growth presentation failure"); };
        if (foreign) Program.FinitePurchaseRendered = () => Set(rows[11], "maxLevel", (long)Get(rows[11], "maxLevel") + 1);
        object result;
        try
        {
            result = CallMain("PurchaseActionDiggers");
            Check(ReferenceEquals(claimedSetup, Get(claim, "Setup")), "owned growth does not replace the existing support claim");
            Equal(true, CallMain("FiniteActionSetupVerified", claim, claimedSetup), "the compatible claim retains its selected native setup after growth");
            Equal(false, Get(run, "SetupVerified"), "observed growth cannot restore prep actuation permission or resource credit");
        }
        finally { Program.FinitePurchaseRendered = null; CallMain("EndExecutionClaim", claim, false); }
        Equal(!foreign, Property(result, "Verified"), "only exact owned growth is verified");
        Equal(1L, Get(rows[0], "maxLevel"), "the actual purchased zero-to-one maximum is retained");
        Equal(90d, Get(Character, "realGold"), "growth spends the native price without reserve");
        Equal(support, CallMain("CombatPrepSupportKey"), "background purchase preserves selected support and active levels");
        Equal(allocations, CallMain("GlobalPresetAllocationState"), "background purchase does not move any allocated resource");
        Check(ReferenceEquals(setup, Get(run, "Setup")) && ReferenceEquals(policy, Get(run, "Policy")), "growth never acquires replacement setup or policy");
        Equal(searches, PresetSearches, "owned growth never starts a new preset optimizer search");
        Equal(selections, InlineGearSelections, "owned growth never selects replacement phase gear");
        foreach (var pair in pinned)
        {
            Check(ReferenceEquals(pair.Value.Selection, Get(Get(supports[pair.Key], "Evaluation"), "Selection")), "growth retains the exact pinned support levels/" + pair.Key);
            Equal(pair.Value.Budget, Get(supports[pair.Key], "BudgetFraction"), "growth preserves the pinned support budget/" + pair.Key);
            Equal(pair.Value.Enforce, Get(supports[pair.Key], "EnforceBudget"), "growth preserves budget enforcement/" + pair.Key);
        }
        for (var i = 0; i < fields.Length; i++) Equal(original[i], Get(run, fields[i]), "growth preserves acquired prep " + fields[i]);
        Equal(string.Join(",", targets), string.Join(",", (long[])Get(Get(Character, "advancedTraining"), "levelTarget")), "growth preserves exact native AT targets");
        if (foreign) Equal(material, Get(inputs, "SupportMaterial"), "an extra foreign maximum is not approved with the owned increment");
        else
        {
            Equal(CallMain("CombatPrepSupportMaterial", CallMain("CaptureSupportContext")), Get(inputs, "SupportMaterial"), "exact owned maximum advances the active material boundary");
            Check(!ReferenceEquals(previousContext, Get(inputs, "SupportContext")), "owned growth actually publishes a fresh rate context under the compatible claim");
            Equal(Get(CallMain("CaptureSupportContext"), "DiggerLevelBonus"), Get(Get(inputs, "SupportContext"), "DiggerLevelBonus"), "active rate context reflects the native purchased-level bonus");
            var profiles = new[] { Property(Get(inputs, "Snapshot"), "Current") }.Concat(((Array)Property(Get(inputs, "Snapshot"), "Profiles")).Cast<object>());
            foreach (var profile in profiles)
            {
                var key = (string)Property(profile, "Key");
                var evaluation = Get(((IDictionary)Get(inputs, "Support"))[key], "Evaluation");
                var bonuses = CallMain("CombatPrepGearBonuses", ((IDictionary)Get(inputs, "Gear"))[key]);
                Equal(CallMain("CombatPrepWandoosSpeedWithoutAtOrBootup", true, bonuses, evaluation), Property(profile, "WandoosEnergySpeedWithoutAt"), "pinned Energy W rate refreshed/" + key);
                Equal(CallMain("CombatPrepWandoosSpeedWithoutAtOrBootup", false, bonuses, evaluation), Property(profile, "WandoosMagicSpeedWithoutAt"), "pinned Magic W rate refreshed/" + key);
            }
            Check(profiles.Any(profile => (double)Property(profile, "WandoosEnergySpeedWithoutAt") > previousRates[(string)Property(profile, "Key")]),
                "at least one pinned Wandoos profile receives the actual purchased-level rate increase");
        }
        Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
        Equal(foreign ? "cancelled" : "running", Get(run, "Status"), "owned growth continues prep while unapproved growth retains cancellation: " + Get(run, "Reason"));
        if (!foreign)
        {
            Check(ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")), "owned growth keeps the original active run");
            Equal(original[5], Get(run, "DeadlineRebirthSeconds"), "post-growth refresh cannot renew the original deadline");
            Set(rows[11], "maxLevel", (long)Get(rows[11], "maxLevel") + 1);
            Set(run, "LastRefresh", -1d); CallMain("TickCombatPrep");
            Equal("cancelled", Get(run, "Status"), "later foreign maximum growth remains rejected after one owned approval");
        }
    }

    private static void CheckRepeatableDiggerForeignProof()
    {
        foreach (var change in new[] { "maximum", "level", "flags", "selection", "row", "table", "settings", "grant" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); DiggerPrice(0, 10d); Set(rows[11], "maxLevel", 2L);
            var calls = Program.RepeatableDiggerCalls; object claim = null; object replacement = null;
            Program.FinitePurchaseRendered = () => {
                if (change == "maximum") Set(rows[11], "maxLevel", 3L);
                if (change == "level") Set(rows[11], "curLevel", 1L);
                if (change == "flags") Set(rows[11], "active", true);
                if (change == "selection")
                {
                    Set(rows[11], "curLevel", 1L); Set(rows[11], "active", true);
                    ((IList)Get(Get(Character, "diggers"), "activeDiggers")).Add(11);
                }
                if (change == "row") { replacement = Activator.CreateInstance(rows[11].GetType()); Set(replacement, "maxLevel", 2L); rows[11] = replacement; }
                if (change == "table")
                {
                    replacement = ((IList)Get(Get(Character, "allDiggers"), "baseGoldCost")).Cast<double>().ToList();
                    Set(Get(Character, "allDiggers"), "baseGoldCost", replacement);
                }
                if (change == "settings") { replacement = Invoke(Get(Character, "settings"), "MemberwiseClone"); Set(Character, "settings", replacement); }
                if (change == "grant") claim = CallMain("BeginExecutionClaim", "foreign-support", "native-callback", Domain("Support"), (Func<bool>)(() => true));
            };
            try
            {
                var result = CallMain("PurchaseActionDiggers");
                Equal(false, Property(result, "Verified"), "foreign callback cannot lend growth authority/" + change);
                Equal(calls + 1, Program.RepeatableDiggerCalls, "foreign callback never triggers an immediate retry/" + change);
                Equal(1L, Get(rows[0], "maxLevel"), "original native maximum stays committed without compensation/" + change);
                Equal(90d, Get(Character, "realGold"), "original native debit stays committed without compensation/" + change);
                if (change == "maximum") Equal(3L, Get(rows[11], "maxLevel"), "foreign maximum is not rolled back");
                if (change == "level" || change == "selection") Equal(1L, Get(rows[11], "curLevel"), "foreign current level is not rolled back");
                if (change == "flags" || change == "selection") Equal(true, Get(rows[11], "active"), "foreign active flag is not rolled back");
                if (change == "row") Check(ReferenceEquals(replacement, rows[11]), "foreign row instance is preserved");
                if (change == "table") Check(ReferenceEquals(replacement, Get(Get(Character, "allDiggers"), "baseGoldCost")), "foreign table instance is preserved");
                if (change == "settings") Check(ReferenceEquals(replacement, Get(Character, "settings")), "foreign settings instance is preserved");
            }
            finally
            {
                Program.FinitePurchaseRendered = null;
                if (claim != null) CallMain("EndExecutionClaim", claim, false);
            }
        }
    }

    private static void PrepareRepeatablePurchaseLoop()
    {
        var settings = Get(MainType, "Settings");
        foreach (var field in new[] { "_globalEnabled", "_customAutoBoostEnabled", "_customAutoBloodSpellsEnabled", "_customAutoMoneyPitEnabled", "_customAutoExpEnabled" })
            Set(settings, field, false);
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", false);
        CallMain("ObserveCustomRebirthEpoch");
    }

    private static void PrepareRepeatableLoopExp()
    {
        Set(Get(MainType, "Settings"), "_customAutoExpEnabled", true);
        Set(Character, "realExp", 80L); Set(Character, "energyPower", 100f); Set(Character, "energyBars", 0L);
        Set(Character, "capEnergy", 10000L);
        var purchases = Child(Character, "energyPurchases"); Set(purchases, "character", Character); Child(purchases, "tooltip");
        foreach (var input in new[] { "powerInput", "capInput", "barInput" }) Child(purchases, input);
        Set(Get(Character, "settings"), "customBarAmount", 7);
    }

    private static void CheckRepeatablePurchaseCadence()
    {
        foreach (var mode in new[] { "ordinary", "manual-prep", "automatic-entry", "automatic-active", "reentry", "native-failure" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); PrepareRepeatablePurchaseLoop();
            var settings = Get(MainType, "Settings");
            if (mode == "manual-prep" || mode.StartsWith("automatic", StringComparison.Ordinal)) Set(Character, "highestBoss", 100);
            if (mode == "automatic-entry") SetupRepeatablePurchaseAutomaticPlan(false);
            else if (mode == "automatic-active") SetupRepeatablePurchaseAutomaticPlan();
            else Set(settings, "_useCustomRebirthPlan", false);
            if (mode == "manual-prep")
            {
                Check((double)Invoke(Get(Character, "wandoos98Controller"), "wandoosBonus") > 1d,
                    "manual prep retains the actual nonzero-level native OS bonus, not the shared constant-one boundary");
                var cached = Activator.CreateInstance(MainType.GetNestedType("CombatPrepCapture", All), true);
                var capture = CallMain("CaptureCombatPrep", .03d, .03d, cached);
                var phase = Invoke(typeof(Program), "Phase", "Wandoos", "current", 120d);
                Invoke(typeof(Program), "StartServerPolicy", capture, new[] { phase });
            }
            if (mode == "native-failure") PrepareRepeatableLoopExp();
            var run = Get(MainInstance, "_combatPrepRun");
            if (mode == "manual-prep" || mode == "automatic-active")
            {
                Check(run != null && (string)Get(run, "Status") == "running", "cadence fixture has a real active prep/" + mode);
                Equal(true, Get(run, "SetupVerified"), "cadence starts from verified prep/" + mode);
            }
            var deadline = run == null ? null : Get(run, "PhaseDeadline");
            var policy = run == null ? null : Get(run, "Policy");
            DiggerPrice(0, 10d); Set(Character, "realGold", 100d);
            var calls = Program.RepeatableDiggerCalls; var callbacks = 0;
            Program.RepeatableDiggerStarting = () => {
                callbacks++;
                Equal(true, Get(MainInstance, "_automationRoutineRunning"), "only the ordinary pass enters the purchase service/" + mode);
                if (mode == "automatic-entry") Equal(null, Get(MainInstance, "_combatPrepRun"), "purchase precedes automatic-stage acquisition and its early return");
                if (mode == "reentry")
                {
                    CallMain("AutomationRoutine");
                    Equal(true, Get(MainInstance, "_automationRoutineRunning"), "refused nested routine cannot release the outer pass guard");
                }
                if (mode == "native-failure") throw new InvalidOperationException("ordinary digger failure before accounting");
            };
            try { CallMain("AutomationRoutine"); }
            finally { Program.RepeatableDiggerStarting = null; }
            Equal(1, callbacks, "ordinary pass invokes one fresh native purchase/" + mode);
            Equal(calls + 1, Program.RepeatableDiggerCalls, "ordinary pass never bursts upgrades/" + mode);
            Equal(mode == "native-failure" ? 0L : 1L, Get(rows[0], "maxLevel"), "ordinary pass preserves the actual native result/" + mode);
            Equal(false, Get(MainInstance, "_automationRoutineRunning"), "ordinary pass releases its guard on success, failure and early returns/" + mode);
            Equal(10f, Get(MainInstance, "_timeLeft"), "ordinary pass resets its normal cadence/" + mode);
            if (run != null)
            {
                Check(ReferenceEquals(run, Get(MainInstance, "_combatPrepRun")) && ReferenceEquals(policy, Get(run, "Policy")), "ordinary purchase keeps actual prep and policy/" + mode);
                Equal("running", Get(run, "Status"), "owned maximum growth does not cancel active prep/" + mode + ": " + Get(run, "Reason"));
                Equal(deadline, Get(run, "PhaseDeadline"), "ordinary purchase does not renew the active phase/" + mode);
            }
            if (mode == "automatic-entry")
            {
                var acquired = Get(MainInstance, "_combatPrepRun");
                Check(acquired != null && (string)Get(acquired, "Status") == "running", "automatic-stage early return follows a real purchase and actual acquisition");
            }
            if (mode == "native-failure")
            {
                Equal(1L, Get(Character, "energyBars"), "digger failure cannot starve the independent EXP service");
                Equal(0L, Get(Character, "realExp"), "independent EXP service pays its native bar price");
                Equal(7, Get(Get(Character, "settings"), "customBarAmount"), "independent EXP service returns its borrowed input");
            }
            for (var quick = 0; quick < 3; quick++) CallMain("QuickStuff");
            Equal(calls + 1, Program.RepeatableDiggerCalls, "rapid passes never buy diggers/" + mode);
            CallMain("AutomationRoutine");
            Equal(calls + 2, Program.RepeatableDiggerCalls, "next ordinary pass retries or buys exactly once/" + mode);
            Equal(mode == "native-failure" ? 1L : 2L, Get(rows[0], "maxLevel"), "next ordinary pass starts from freshly observed maximum/" + mode);
            Equal(mode == "native-failure" ? 90d : 70d, Get(Character, "realGold"), "ordinary passes use fresh native prices without reserves/" + mode);
        }
        foreach (var lifecycle in new[] { "Quiescing", "LoadingSave", "Unloaded" })
        {
            var rows = PrepareRepeatableDiggerPurchases(); PrepareRepeatablePurchaseLoop(); DiggerPrice(0, 10d);
            CallMain("SetExecutionLifecycle", Enum.Parse(MainType.GetNestedType("ExecutionLifecycle", All), lifecycle));
            var calls = Program.RepeatableDiggerCalls;
            CallMain("AutomationRoutine"); CallMain("QuickStuff");
            Equal(calls, Program.RepeatableDiggerCalls, "non-running lifecycle admits no ordinary or rapid purchase/" + lifecycle);
            Equal(0L, Get(rows[0], "maxLevel"), "lifecycle refusal leaves purchased maximum alone/" + lifecycle);
            Equal(100d, Get(Character, "realGold"), "lifecycle refusal leaves gold alone/" + lifecycle);
            Equal(false, Get(MainInstance, "_automationRoutineRunning"), "lifecycle refusal acquires no pass guard/" + lifecycle);
        }
    }

    private static object SetupRepeatablePurchaseAutomaticPlan(bool start = true)
    {
        var settings = Get(MainType, "Settings");
        var store = Get(MainType, "CustomPlanStore"); var name = (string)Property(settings, "CustomPlanFile");
        var plan = RepeatablePurchasePlan; var first = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(first, "ExecutionMode", 1); Set(first, "DurationSeconds", 120);
        var stages = Array.CreateInstance(first.GetType(), 1); stages.SetValue(first, 0);
        Set(plan, "Stages", stages); Set(plan, "StageLoopStartIndex", 0); Set(plan, "StageDurationSeconds", 86400);
        Set(plan, "AutoRebirthEnabled", false); Set(plan, "AutoIronPillEnabled", false);
        var w = Get(Character, "wandoos98");
        Set(w, "energyLevel", 0L); Set(w, "magicLevel", 0L); Set(w, "energyProgress", 0f); Set(w, "magicProgress", 0f);
        Invoke(store, "Save", name, plan); plan = Invoke(store, "Load", name);
        CallMain("InvalidateCustomRuntimePlanCache");
        if (start) CallMain("RunCustomPlanAutomation", plan);
        return plan;
    }
}
