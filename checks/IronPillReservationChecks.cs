using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Execute the compiled injector and the game's real blood-spell methods. No Unity
// constructors/live actions. Blood menu/tooltip and AT rendering are disabled;
// sequential/protected credit additionally opts into explicit slot/progression inputs below.
public static class IronPillSetupBoundary
{
    internal static bool Enabled;
    public static bool Active() { return Enabled; }
}

internal static partial class IronPillReservationChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly _assembly;
    private static Type _mainType, _stageType, _planType, _scheduler;
    private static object _main, _character, _settings, _blood, _spells, _clock, _plan;
    private static int _assertions;
    private static MemoryStream _log;
    private static StreamWriter _writer;
    private static string _fixtureDirectory;

    internal static Assembly LoadGameWithoutBloodUi(string managedDirectory)
    {
        using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory, "Assembly-CSharp.dll")))
        using (var bytes = new MemoryStream())
        {
            var patched = 0;
            var patchedTrainingRefresh = 0;
            foreach (var type in game.MainModule.Types)
            foreach (var method in type.Methods)
            {
                // Opt-in external inputs for the sequential/protected-credit fixtures.
                // Keep the native game methods unchanged in every other scenario.
                double? setupInput = null;
                if (type.Name == "InventoryController" && new[] {"curSpaces", "accessorySpaces", "weapon2Unlocked", "weapon2Factor"}.Contains(method.Name)) setupInput = 0;
                if (type.Name == "Character" && method.Name == "grossGoldPerSecond") setupInput = 0;
                if (type.Name == "AllBeardsController" && method.Name == "capBeards") setupInput = 4;
                if (type.Name == "AllBeardsController" && method.Name == "beardSize") setupInput = 7;
                if ((type.Name == "AllBeardsController" || type.Name == "AllGoldDiggerController") && method.Name == "refreshMenu") setupInput = 0;
                if (type.Name == "Character" && method.Name == "machineBossMulti"
                    || type.Name == "TimeMachineController" && new[] {"barFillsPerSecond", "speedGoldMultiBonus", "goldMultiBonus"}.Contains(method.Name)
                    || type.Name == "AllBloodMagicController" && method.Name == "goldBonus"
                    || type.Name == "AllNGUController" && method.Name == "timeMachineBonus"
                    || type.Name == "TimeMachineChallengeController" && method.Name == "totalGPSbonus"
                    || type.Name == "AllGoldDiggerController" && method.Name == "totalLevelBonus") setupInput = 1;
                if (setupInput.HasValue && method.HasBody)
                {
                    var il = method.Body.GetILProcessor(); var native = method.Body.Instructions[0];
                    il.InsertBefore(native, il.Create(OpCodes.Call, game.MainModule.ImportReference(typeof(IronPillSetupBoundary).GetMethod("Active"))));
                    il.InsertBefore(native, il.Create(OpCodes.Brfalse, native));
                    if (method.ReturnType.FullName != "System.Void")
                        il.InsertBefore(native, method.ReturnType.FullName == "System.Single" ? il.Create(OpCodes.Ldc_R4, (float)setupInput.Value)
                            : method.ReturnType.FullName == "System.Double" ? il.Create(OpCodes.Ldc_R8, setupInput.Value) : il.Create(OpCodes.Ldc_I4, (int)setupInput.Value));
                    il.InsertBefore(native, il.Create(OpCodes.Ret));
                }
                var bloodMenu = type.Name == "RebirthPowerSpell" && new[] {
                    "updateMenu", "updateLootToggleState", "updateGoldToggleState", "updateRebirthToggleState"
                }.Contains(method.Name);
                var tooltip = type.Name == "HoverTooltip" && method.Name == "showTooltip" && method.Parameters.Count == 2;
                // Inspected game IL: refresh only toggles UI objects and calls
                // updateText/updateBar (UI-only). removeAllEnergy performs the real
                // energy transfer before this render call; do not replace that method.
                // Unity can resolve native UI ECalls even on a non-rendered menu.
                var trainingRefresh = type.Name == "AdvancedTrainingController" && method.Name == "refresh"
                    && method.Parameters.Count == 0 && !method.IsStatic;
                if (!bloodMenu && !tooltip && !trainingRefresh) continue;
                if (method.ReturnType.FullName != "System.Void") throw new Exception("Unexpected UI boundary signature: " + method.FullName);
                method.Body = new Mono.Cecil.Cil.MethodBody(method);
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                if (trainingRefresh) patchedTrainingRefresh++;
                else patched++;
            }
            if (patched != 5) throw new Exception("Expected five external blood UI/tooltip methods, found " + patched);
            if (patchedTrainingRefresh != 1) throw new Exception("Expected one external AT refresh rendering method, found " + patchedTrainingRefresh);
            game.Write(bytes);
            return Assembly.Load(bytes.ToArray());
        }
    }

    internal static int Run(Assembly assembly, bool includeSchedulerChecks = true)
    {
        _assembly = assembly;
        _mainType = assembly.GetType("NGUInjector.Main", true);
        _stageType = assembly.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
        _planType = assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true);
        _scheduler = assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        _assertions = 0;
        var saved = new Dictionary<FieldInfo, object>();
        foreach (var name in new[] {"reference", "Character", "Controller", "Settings", "CustomPlanStore", "settingsForm", "OutputWriter", "_customAutomationStatus", "_dir"})
        {
            var field = _mainType.GetField(name, All);
            saved.Add(field, field.GetValue(null));
        }
        foreach (var type in new[] {_scheduler, assembly.GetType("NGUInjector.Managers.BlackBarAllocator", true),
            assembly.GetType("NGUInjector.AllocationProfiles.RebirthStuff.CustomPlanRebirth", true)})
        foreach (var field in type.GetFields(All).Where(x => x.IsStatic && !x.IsInitOnly && !x.IsLiteral))
            saved.Add(field, field.GetValue(null));
        _fixtureDirectory = Path.Combine(Path.GetTempPath(), "ngu-iron-reservation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(_fixtureDirectory);
            var failures = new List<Exception>();
            var checks = new List<Action> { CheckOrders, CheckBloodGoalPriorities, CheckBloodGoalPreemptionAndReservation,
                CheckShortPlanBloodGoals, CheckBloodGoalTimerIsolation, CheckQuickBloodGoalTelemetry, CheckPlanScopedBloodRuntime,
                CheckPlanScopedIronPillRuntime, CheckWindowAndStageTransitions, CheckMinimumAndSuccessfulCast, CheckRejectedPill,
                CheckRejectedFlush, CheckUnavailableState, CheckTimeout, CheckPreemption };
            if (includeSchedulerChecks) checks.Add(CheckSequentialTimers);
            foreach (var check in checks)
            {
                try { check(); }
                catch (Exception error) { failures.Add(new Exception(check.Method.Name, error)); }
            }
            if (failures.Count != 0) throw new AggregateException("Iron Pill reservation failed after " + _assertions + " assertions (not a pass count).", failures);
            Console.WriteLine("Iron Pill reservation checks passed: " + _assertions + " assertions.");
            return _assertions;
        }
        finally
        {
            try { foreach (var field in saved) field.Key.SetValue(null, field.Value); }
            finally
            {
                if (_writer != null) _writer.Dispose();
                if (_log != null) _log.Dispose();
                if (Directory.Exists(_fixtureDirectory)) Directory.Delete(_fixtureDirectory, true);
            }
        }
    }

    private static void Setup(double elapsed = 5900d)
    {
        _mainType.GetField("reference", All).SetValue(null, null);
        if (_writer != null) _writer.Dispose();
        if (_log != null) _log.Dispose();
        _log = new MemoryStream();
        _writer = new StreamWriter(_log) { AutoFlush = true };
        _mainType.GetField("OutputWriter", All).SetValue(null, _writer);
        var directory = Path.Combine(_fixtureDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _mainType.GetField("_dir", All).SetValue(null, directory);
        _mainType.GetField("settingsForm", All).SetValue(null, null);
        _character = New(_mainType.GetField("Character", All).FieldType);
        _mainType.GetField("Character", All).SetValue(null, _character);
        _main = New(_mainType);
        NativeFightFixture.InitializeMain(_main);
        Set(_main, "_customIronPillPrepStartedAt", -1d);
        Set(_main, "_resourceExecutionOwner", ""); Set(_main, "_resourceExecutionReason", "");
        _settings = Activator.CreateInstance(_assembly.GetType("NGUInjector.SavedSettings", true), new object[] {null});
        _mainType.GetField("Settings", All).SetValue(null, _settings);
        foreach (var name in new[] {"UseCustomRebirthPlan", "AutoRebirth", "CustomAutoBloodSpellsEnabled"}) Setting(name, true);
        Setting("ManageEnergy", false);
        Setting("ManageMagic", true);
        Setting("CustomPlanFile", "iron-reservation-check");
        Setting("ManualAdventureEnabled", false);
        Setting("CustomIronPillSaveSeconds", 900);
        Setting("CustomIronPillMinWorth", 0d);
        Setting("CustomBloodSpaghettiGoalPercent", 0);
        Setting("CustomBloodCounterfeitGoalPercent", 0);
        _mainType.GetField("reference", All).SetValue(null, _main);

        _clock = Child(_character, "rebirthTime");
        Child(_character, "totalPlaytime");
        Set(Child(_character, "stats"), "rebirthNumber", 7L);
        AtTime(elapsed);
        Set(_character, "bossID", 100);
        Set(_character, "attackMulti", 1d);
        Set(_character, "nextAttackMulti", 2d);
        Set(_character, "idleEnergy", 123456L);
        Child(_character, "settings");
        Child(_character, "magic");
        Child(_character, "machine");
        Child(_character, "wandoos98");
        foreach (var name in new[] {"wandoos98Controller", "timeMachineController", "wishesController"})
            Set(Child(_character, name), "character", _character);
        var nguControllers = Child(_character, "NGUController");
        foreach (var name in new[] {"NGU", "NGUMagic"})
            Set(nguControllers, name, Array.CreateInstance(FindField(nguControllers.GetType(), name).FieldType.GetElementType(), 0));
        var ordinaryTraining = Child(_character, "advancedTraining");
        Set(ordinaryTraining, "level", new long[5]);
        Set(ordinaryTraining, "levelTarget", new long[5]);
        Set(ordinaryTraining, "energy", new long[5]);
        var ordinaryWishes = Child(_character, "wishes");
        var ordinaryWishField = ordinaryWishes.GetType().GetField("wishes", All);
        var ordinaryWishList = (IList) Activator.CreateInstance(ordinaryWishField.FieldType);
        for (var i = 0; i <= 190; i++) ordinaryWishList.Add(New(ordinaryWishField.FieldType.GetGenericArguments()[0]));
        ordinaryWishField.SetValue(ordinaryWishes, ordinaryWishList);
        _blood = Child(_character, "bloodMagic");
        var ritualsField = _blood.GetType().GetField("ritual", All);
        var rituals = (IList) Activator.CreateInstance(ritualsField.FieldType);
        rituals.Add(New(ritualsField.FieldType.GetGenericArguments()[0]));
        ritualsField.SetValue(_blood, rituals);
        Set(_blood, "bloodPoints", 10000d);
        Set(_blood, "rebirthPower", 11d);
        Set(_blood, "lootSpellBlood", 77d);
        Set(_blood, "goldSpellBlood", 88d);
        foreach (var name in new[] {"lootAutoSpell", "goldAutoSpell", "rebirthAutoSpell"}) Set(_blood, name, true);
        var cooldown = Child(_blood, "adventureSpellTime");
        Set(cooldown, "totalseconds", 10000d);
        _spells = Child(_character, "bloodSpells");
        Set(_spells, "character", _character);
        Set(_spells, "adventureSpellCooldown", 3600);
        Set(Child(_spells, "tooltip"), "character", _character);
        Set(Child(_character, "bloodMagicController"), "character", _character);
        Set(Get(_character, "bloodMagicController"), "spells", _spells);
        var barsField = Get(_character, "bloodMagicController").GetType().GetField("bloodMagics", All);
        var bars = Array.CreateInstance(barsField.FieldType.GetElementType(), 1);
        bars.SetValue(New(barsField.FieldType.GetElementType()), 0);
        Set(bars.GetValue(0), "character", _character);
        Set(bars.GetValue(0), "id", 0);
        barsField.SetValue(Get(_character, "bloodMagicController"), bars);
        var buttons = Child(_character, "buttons");
        foreach (var name in new[] {"bloodMagic", "advancedTraining"})
        {
            var button = Child(buttons, name);
            FindField(button.GetType(), "m_Interactable").SetValue(button, true);
        }
        Child(_character, "adventure");
        _plan = Activator.CreateInstance(_planType);
        Set(_plan, "Name", "iron-reservation-check");
        // Late activation has 200 flexible seconds left, split into two 100s
        // entry windows. These fixtures test overlays, not fixed-time guarantees.
        Set(_plan, "StageDurationSeconds", 6100);
        Set(_plan, "StageLoopStartIndex", -1);
        Set(_plan, "StageSchedulerEnabled", true);
        Set(_plan, "AutoRebirthEnabled", true);
        Set(_plan, "AutoIronPillEnabled", true);
        Set(_plan, "AutoActivateFruits", false);
        foreach (var name in new[] {"TimeTriggerEnabled", "NumberTriggerEnabled", "NumberClimbEnabled"}) Set(_plan, name, false);
        var stages = Array.CreateInstance(_stageType, 2);
        for (var i = 0; i < 2; i++)
        {
            var stage = Activator.CreateInstance(_stageType);
            Set(stage, "Name", "stage-" + i);
            Set(stage, "Percent", 1d);
            Set(stage, "DurationSeconds", 0);
            Set(stage, "GearGoal", -1);
            Set(stage, "EnergyFocus", i == 0 ? 5 : 3);
            Set(stage, "MagicFocus", i == 0 ? 6 : 3);
            stages.SetValue(stage, i);
        }
        Set(_plan, "Stages", stages);
        Set(_plan, "SourceJson", Activator.CreateInstance(_assembly.GetType("SimpleJSON.JSONObject", true)));
        _mainType.GetField("CustomPlanStore", All).SetValue(null, New(_mainType.GetField("CustomPlanStore", All).FieldType));
        CachePlan();
        ActiveStage();
        RequireCheckpoint("reservation fixture initializes actual durable coordinator state");
    }

    private static void CheckOrders()
    {
        Setup();
        Setting("ManageEnergy", true);
        var stage = Stage(0);
        Eq("RIT,NGU,WAN,TM", Order(stage, false, true), "default NGU magic promotes RIT");
        Eq("NGU,RIT,WAN,TM", Order(stage, false, false), "unreserved default restored");
        Set(stage, "MagicFocus", 1);
        Eq("TM,RIT,NGU,WAN", Order(stage, false, true), "Time Machine preserves TM before reserved blood");
        Set(stage, "MagicFocus", 3);
        Eq("WAN,RIT,NGU,TM", Order(stage, false, true), "Wandoos preserves WAN first");
        Eq("WAN,RIT,NGU,TM", Order(stage, false, true, 80, 100), "mixed Energy NGU/Magic Wandoos does not invent paid AT");
        Set(stage, "EnergyFocus", 3);
        Eq("RIT,NGU,TM", Order(stage, false, false, 80, 100), "ordinary AT Wandoos excludes magic Wandoos until preparation ends");
        Eq("RIT,NGU,TM", Order(stage, false, true, 80, 100), "saving AT Wandoos cannot reinsert magic Wandoos");
        Set(((IList) Get(Get(_character, "wishes"), "wishes"))[190], "level", 1);
        Eq("WAN,RIT,NGU,TM", Order(stage, false, true, 80, 100), "free Wish 190 skips implicit paid preparation even while saving");
        Set(((IList) Get(Get(_character, "wishes"), "wishes"))[190], "level", 0);
        Set(stage, "EnergyFocus", 5);
        Eq("AT-3,AT-4,NGU,TM", Order(Stage(1), true, true, 80, 100, 3), "AT Wandoos excludes dump training until preparation ends");
        var energy = new[] {"AT-1", "NGU", "TM"};
        var magic = new[] {"NGU", "RIT-0", "TM", "RIT", "RIT-7", "NGU", "WAN"};
        Set(stage, "EnergyPriorities", energy);
        Set(stage, "MagicPriorities", magic);
        Eq("RIT,NGU,TM", Order(stage, false, true), "explicit AT collapses rituals without reintroducing magic Wandoos");
        Eq("AT-1,NGU,TM", Order(stage, true, true), "explicit energy untouched");
        Eq("NGU,RIT-0,TM,RIT,RIT-7,NGU,WAN", string.Join(",", magic), "saved magic array remains immutable");
        Eq(true, ReferenceEquals(magic, Get(stage, "MagicPriorities")), "saved magic array identity retained");
        Eq(true, ReferenceEquals(energy, Get(stage, "EnergyPriorities")), "saved energy array identity retained");
        Set(stage, "EnergyPriorities", new[] {"NGU", "AT", "TM"});
        Eq("NGU,RIT-0,TM,RIT,RIT-7,NGU,WAN", Order(stage, false, false), "cancel restores exact explicit order");
        Set(stage, "MagicFocus", 6);
        Eq("RIT,NGU,TM,WAN", Order(stage, false, true), "WAN token alone does not make NGU a Wandoos stage");
        Set(stage, "MagicFocus", 1);
        Eq("TM,RIT,NGU,WAN", Order(stage, false, true), "Time Machine explicit priorities deduplicate protected TM and rituals");
    }

    private static void CheckBloodGoalPriorities()
    {
        Setup();
        Setting("ManageMagic", true);
        Setting("AutoRebirth", false);
        Set(_plan, "AutoRebirthEnabled", false);
        Set(_plan, "AutoIronPillEnabled", false);
        var stage = Stage(0);
        var energyGear = (int) Enum.Parse(_assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "EnergyNgu");
        Set(stage, "GearGoal", energyGear);
        var energy = new[] {"NGU", "WAN", "TM"};
        var magic = new[] {"NGU", "RIT-0", "TM", "RIT-7", "NGU", "WAN"};
        Set(stage, "EnergyPriorities", energy);
        Set(stage, "MagicPriorities", magic);
        CachePlan();
        Eq("NGU,RIT-0,TM,RIT-7,NGU,WAN", GoalOrder(stage), "disabled goals retain exact original priorities");
        foreach (var goals in new[] {new[] {15, 0}, new[] {0, 100}, new[] {15, 100}})
        {
            Setting("CustomBloodSpaghettiGoalPercent", goals[0]);
            Setting("CustomBloodCounterfeitGoalPercent", goals[1]);
            Eq(true, GoalRequest(), "ordinary goal deficit independent of pill/auto-rebirth permissions " + string.Join(",", goals));
            Eq("RIT,NGU,TM,WAN", GoalOrder(stage), "unmet one/both goals promote one generic ritual " + string.Join(",", goals));
            Call("RunCustomBloodSpellToggles", _plan);
            AssertToggles(goals[0] > 0, goals[1] > 0, false, "ordinary goal-aware spells");
        }
        Eq(10000d, Get(_blood, "bloodPoints"), "ordinary catch-up never flushes available blood");
        Eq(11d, Get(_blood, "rebirthPower"), "ordinary catch-up never casts NUMBER");
        Eq(-1d, Get(_main, "_customIronPillPrepStartedAt"), "catch-up is not an Iron Pill saving window");
        var progress = Call("GetCustomPlanProgress");
        Eq("true", Json(progress, "bloodGoalsPrioritized").ToString(), "active catch-up telemetry flag");
        Eq(false, string.IsNullOrEmpty((string) Json(progress, "bloodGoalsPriorityReason").GetType().GetProperty("Value").GetValue(Json(progress, "bloodGoalsPriorityReason"))), "active catch-up telemetry explains reason");
        Eq("[\"RIT\",\"NGU\",\"TM\",\"WAN\"]", Json(Row(progress, 0), "magicPriorities").ToString(), "telemetry shares actual catch-up order");
        Eq("false", Json(Row(progress, 1), "bloodGoalsPrioritized").ToString(), "inactive row does not claim catch-up");
        Eq("[\"NGU\",\"RIT-0\",\"TM\",\"RIT-7\",\"NGU\",\"WAN\"]", Json(Row(progress, 0), "configuredMagicPriorities").ToString(), "telemetry retains configured magic array");
        Set(_blood, "lootSpellBlood", 163840000d);
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(false, true, false, "real float Spaghetti threshold reached");
        Eq("RIT,NGU,TM,WAN", GoalOrder(stage), "one remaining gold goal keeps catch-up");
        Set(_blood, "goldSpellBlood", 512000000d);
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(false, false, true, "real float gold threshold reached");
        Eq(false, GoalRequest(), "both real game bonus thresholds clear catch-up");
        Eq("NGU,RIT-0,TM,RIT-7,NGU,WAN", GoalOrder(stage), "threshold crossing restores exact configured order");
        progress = Call("GetCustomPlanProgress");
        Eq("false", Json(progress, "bloodGoalsPrioritized").ToString(), "reached goals clear telemetry immediately");
        Eq("\"\"", Json(progress, "bloodGoalsPriorityReason").ToString(), "reached goals clear reason");
        Eq(true, ReferenceEquals(magic, Get(stage, "MagicPriorities")), "catch-up preserves saved magic array identity");
        Eq("NGU,RIT-0,TM,RIT-7,NGU,WAN", string.Join(",", magic), "catch-up never mutates magic array values");
        Eq(true, ReferenceEquals(energy, Get(stage, "EnergyPriorities")), "catch-up preserves saved energy array identity");
        Eq("NGU,WAN,TM", Order(stage, true, false), "catch-up leaves energy priorities unchanged");
        Eq(energyGear, Get(stage, "GearGoal"), "catch-up leaves stage gear unchanged");
        Set(_blood, "lootSpellBlood", 0d);
        foreach (var name in new[] {"MagicNgu", "AllNgu", "Fight"})
        {
            Set(stage, "GearGoal", (int) Enum.Parse(_assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true), name));
            Eq("NGU,RIT-0,TM,RIT-7,NGU,WAN", GoalOrder(stage), name + " gear tuple excluded");
            Eq("false", Json(Call("GetCustomPlanProgress"), "bloodGoalsPrioritized").ToString(), name + " telemetry cannot claim override");
        }
        Set(stage, "GearGoal", energyGear);
        Set(stage, "EnergyFocus", 0);
        Eq("NGU,RIT-0,TM,RIT-7,NGU,WAN", GoalOrder(stage), "dedicated magic lane excluded even with Energy NGU gear");
        Set(stage, "EnergyFocus", 5);
        foreach (var focus in new[] {1, 2, 3, 4, 7})
        {
            Set(stage, "MagicFocus", focus);
            Eq("NGU,RIT-0,TM,RIT-7,NGU,WAN", GoalOrder(stage), "other main magic focus excluded " + focus);
        }
        Set(stage, "MagicFocus", 6);
        Set(stage, "MagicPriorities", new[] {"RIT-2", "NGU"});
        Eq("RIT-2,NGU", GoalOrder(stage), "already blood-first order is not rewritten");
        Eq("false", Json(Call("GetCustomPlanProgress"), "bloodGoalsPrioritized").ToString(), "already blood-first stage cannot claim catch-up override");
        Set(stage, "MagicPriorities", new[] {"TM", "WAN"});
        Eq("TM,WAN", GoalOrder(stage), "explicit list without magic NGU is not changed");
        Set(stage, "MagicPriorities", magic);
        AtTime(6000d);
        ActiveStage();
        progress = Call("GetCustomPlanProgress");
        Eq("false", Json(progress, "bloodGoalsPrioritized").ToString(), "stage transition automatically clears irrelevant catch-up");
        Eq("[\"WAN\",\"RIT\",\"NGU\",\"TM\"]", Json(Row(progress, 1), "magicPriorities").ToString(), "unmanaged energy cannot withhold magic W as paid preparation");
    }

    private static void CheckBloodGoalPreemptionAndReservation()
    {
        Setup();
        Setting("ManageMagic", true);
        Setting("CustomBloodSpaghettiGoalPercent", 15);
        Setting("CustomBloodCounterfeitGoalPercent", 100);
        Setting("CustomIronPillMinWorth", 1d);
        Set(Stage(0), "GearGoal", (int) Enum.Parse(_assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "EnergyNgu"));
        Call("RunCustomBloodSpellAutomation", _plan);
        CachePlan();
        Eq(true, Saving(), "Iron Pill reservation starts with unmet ordinary goals");
        Eq(false, GoalRequest(), "reservation takes precedence over goal catch-up");
        Eq("false", Json(Call("GetCustomPlanProgress"), "bloodGoalsPrioritized").ToString(), "reservation is not misreported as ordinary catch-up");
        AssertToggles(false, false, false, "reservation preempts ordinary spending");
        Set(_blood, "bloodPoints", 100d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "successful pill ends reservation");
        Eq(true, GoalRequest(), "catch-up resumes after successful pill");
        AssertToggles(true, true, false, "ordinary goals resume after pill");
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(false, Saving(), "catch-up does not restart saving on same tick");
        Eq(10011d, Get(_blood, "rebirthPower"), "catch-up never creates an extra NUMBER flush");
        Set(_plan, "AutoIronPillEnabled", false);
        foreach (var flag in new[] {"_manualRebirthPending", "_customPreRebirthPrepRunning", "_bossPushRunning"})
        {
            Set(_main, flag, true);
            Eq(false, GoalRequest(), flag + " prevents catch-up");
            Set(_main, flag, false);
        }
        Setting("CustomAutoBloodSpellsEnabled", false);
        Eq(false, GoalRequest(), "blood spells off prevents catch-up");
        Setting("CustomAutoBloodSpellsEnabled", true);
        Setting("ManageMagic", false);
        Eq(false, GoalRequest(), "unmanaged magic cannot claim catch-up");
        Setting("ManageMagic", true);
        Set(_character, "bloodMagicController", null);
        Eq(false, GoalRequest(), "unavailable blood bonuses cannot promote magic");
        Setup();
        Setting("ManageMagic", true);
        Setting("CustomBloodSpaghettiGoalPercent", 15);
        Set(Stage(0), "GearGoal", (int) Enum.Parse(_assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "EnergyNgu"));
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "cancel fixture begins with a real reservation");
        Set(_blood, "bloodPoints", 321d);
        Set(_plan, "AutoIronPillEnabled", false);
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(false, Saving(), "plan pill opt-out cancels the current reservation");
        Eq(true, GoalRequest(), "ordinary catch-up resumes after reservation cancellation");
        AssertToggles(true, false, false, "ordinary goal spells resume after cancellation");
        Eq(321d, Get(_blood, "bloodPoints"), "cancellation catch-up does not repeat NUMBER flush");
    }

    private static void CheckShortPlanBloodGoals()
    {
        Setup(0d);
        Set(_plan, "AutoIronPillEnabled", false);
        Set(_plan, "StageDurationSeconds", 3600);
        AtTime(0d);
        ActiveStage();
        AtTime(100d);
        Setting("CustomBloodSpaghettiGoalPercent", 15);
        Setting("CustomBloodCounterfeitGoalPercent", 100);
        Set(Stage(0), "GearGoal", (int) Enum.Parse(_assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "EnergyNgu"));
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(true, true, false, "short casual stage services enabled goals instead of starving catch-up");
        AtTime(1800d);
        ActiveStage();
        Call("RunCustomBloodSpellToggles", _plan);
        AssertToggles(false, false, true, "other short-plan focus retains NUMBER-only policy");
    }

    private static void CheckWindowAndStageTransitions()
    {
        Setup();
        Eq(string.Empty, Call("CustomIronPillEligibilityBlocker", _plan), "fixture passes actual reservation eligibility");
        Eq(0d, Call("IronPillCooldownRemainingSeconds"), "fixture pill is ready");
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(0d, Get(_blood, "bloodPoints"), "banked blood flushed before saving");
        Eq(10011d, Get(_blood, "rebirthPower"), "flush funds NUMBER exactly");
        Eq(5900d, Get(_main, "_customIronPillPrepStartedAt"), "saving timer begins at entry");
        Eq(true, Saving(), "slow loop enters reservation");
        Eq(77d, Get(_blood, "lootSpellBlood"), "flush never spends on loot");
        Eq(88d, Get(_blood, "goldSpellBlood"), "flush never spends on gold");
        Eq(false, Get(_main, "_customEquipmentApplicationInvalidated"), "reservation does not request equipment reapplication");
        Eq(123456L, Get(_character, "idleEnergy"), "reservation does not recall energy");
        AssertToggles(false, false, false, "saving");
        Setting("CustomBloodSpaghettiGoalPercent", 15);
        Setting("CustomBloodCounterfeitGoalPercent", 100);
        Set(_blood, "bloodPoints", 321d);
        Call("RunCustomBloodSpellToggles", _plan);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(321d, Get(_blood, "bloodPoints"), "quick plus slow calls do not drain newly produced blood");
        Eq(10011d, Get(_blood, "rebirthPower"), "NUMBER flushed only once");
        Eq(false, Call("CustomQuickResourceRefreshBlocked", _plan), "saving does not block quick allocation");
        Call("RunCustomPlanAutomation", _plan);
        CachePlan();
        var progress = Call("GetCustomPlanProgress");
        Eq("running", Json(progress, "status").GetType().GetProperty("Value").GetValue(Json(progress, "status")), "saving is not a full plan interruption");
        Eq("true", Json(progress, "bloodReservedForIronPill").ToString(), "telemetry exposes reservation");
        Eq("[\"RIT\",\"NGU\",\"WAN\",\"TM\"]", Json(Row(progress, 0), "magicPriorities").ToString(), "telemetry uses allocation overlay");
        Eq("[\"WAN\",\"RIT\",\"NGU\",\"TM\"]", Json(Row(progress, 1), "magicPriorities").ToString(), "inactive telemetry not overlaid");
        AtTime(6000d);
        Call("RunCustomPlanAutomation", _plan);
        CachePlan();
        progress = Call("GetCustomPlanProgress");
        Eq("1", Json(progress, "activeStageIndex").ToString(), "stage transitions continue while saving");
        Eq("0", Json(progress, "stageElapsedSeconds").ToString(), "stage transition clock unchanged");
        Eq("[\"WAN\",\"RIT\",\"NGU\",\"TM\"]", Json(Row(progress, 1), "magicPriorities").ToString(), "reservation preserves W when unmanaged energy cannot perform paid preparation");
        Eq(5900d, Get(_main, "_customIronPillPrepStartedAt"), "stage transition does not restart reservation");
        Set(_main, "_customLastQuickResourceRefresh", DateTime.MinValue);
        Call("RunQuickCustomResourceRefresh", _plan);
        Eq(321d, Get(_blood, "bloodPoints"), "quick allocation does not flush blood");
        Eq(123456L, Get(_character, "idleEnergy"), "normal stage passes do not trigger prep energy takeover");
        var equipmentInvalidated = Get(_main, "_customEquipmentApplicationInvalidated");
        var selection = Get(_main, "_inlineStageSelection");
        Set(_plan, "AutoIronPillEnabled", false);
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(false, Saving(), "plan opt-out cancels reservation");
        Eq(equipmentInvalidated, Get(_main, "_customEquipmentApplicationInvalidated"), "cancel preserves equipment invalidation state");
        Eq(true, ReferenceEquals(selection, Get(_main, "_inlineStageSelection")), "cancel preserves acquired inline selection");
        AssertToggles(true, true, false, "cancel restores ordinary loot/gold goals");
        Eq(321d, Get(_blood, "bloodPoints"), "cancel does not flush accumulated blood");
    }

    private static void CheckMinimumAndSuccessfulCast()
    {
        Setup();
        Setting("CustomIronPillSaveSeconds", 10000);
        Setting("CustomIronPillMinWorth", 1d);
        Set(_blood, "bloodPoints", .5d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(11.5d, Get(_blood, "rebirthPower"), "NUMBER accepts fractional blood below pill/loot/gold minima");
        Eq(true, Saving(), "fractional NUMBER flush begins reservation");
        var window = Get(Get(_main, "_ironWindowRecord"), "Before");
        Setting("CustomIronPillMinWorth", 1e8d);
        Set(_blood, "bloodPoints", 99d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(99d, Get(_blood, "bloodPoints"), "pill minimum rejects insufficient banked blood");
        Eq(0, ((Array) _settings.GetType().GetProperty("CustomIronPillHistory").GetValue(_settings)).Length, "minimum failure never enters history");
        Eq(window, Get(Get(_main, "_ironWindowRecord"), "Before"), "live threshold edits cannot rewrite the acquired saving window");
        Set(_blood, "bloodPoints", 100d);
        AtTime(5910d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "successful real game cast ends reservation");
        Eq(0d, Get(_blood, "bloodPoints"), "pill consumes saved blood");
        Eq(0d, Get(Get(_blood, "adventureSpellTime"), "totalseconds"), "pill resets actual cooldown clock");
        AssertToggles(false, false, true, "successful cast");
        Eq(3f, Get(Get(_character, "adventure"), "attack"), "real pill grants expected power");
        var history = (Array) _settings.GetType().GetProperty("CustomIronPillHistory").GetValue(_settings);
        Eq(1, history.Length, "successful automatic pill enters history once");
        Eq(10d, Get(history.GetValue(0), "WaitSeconds"), "history retains actual reservation wait");
        Eq(3d, Get(history.GetValue(0), "Worth"), "history retains actual pill worth");
        Call("RunCustomBloodSpellToggles", _plan);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "no same-tick new prep even when lookahead exceeds entire cooldown");
        Eq(11.5d, Get(_blood, "rebirthPower"), "successful cast restoration never flushes new window");
        Eq(5920d, Get(_main, "_customIronPillRetryAt"), "post-cast guard lasts ten seconds");
        Eq(false, Get(_main, "_customEquipmentApplicationInvalidated"), "cast completion never requests equipment reapplication");
        Setup();
        Set(_blood, "bloodPoints", 0d);
        Set(_spells, "character", null);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(true, Saving(), "empty pool requires no NUMBER cast");
    }

    private static void CheckRejectedFlush()
    {
        Setup();
        Set(_spells, "character", null);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(false, Saving(), "failed game cast does not claim reservation started");
        Eq(10000d, Get(_blood, "bloodPoints"), "failed NUMBER cast retains existing blood");
        Eq(6800d, Get(_main, "_customIronPillRetryAt"), "rejected flush receives finite retry delay");
        Set(_spells, "character", _character);
        Call("RunCustomBloodSpellToggles", _plan);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(11d, Get(_blood, "rebirthPower"), "quick/slow retry cannot flush during backoff");
        AssertToggles(false, false, true, "flush rejection");
        AtTime(6800d);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(false, Saving(), "elapsed backoff cannot authorize replay of an unknown native invocation");
        Eq(10000d, Get(_blood, "bloodPoints"), "unknown native NUMBER evidence preserves the exact current bank");
        Eq(11d, Get(_blood, "rebirthPower"), "repairing the scene reference never repeats the uncertain NUMBER effect");
        var records = (Array)Property(Get(_main, "_finiteActionLedger"), "Records");
        Eq(1, records.Cast<object>().Count(record => (string)Get(Get(record, "Identity"), "Kind") == "blood-number"), "unknown flush retains one semantic action, not another retry opportunity");
        Eq("Unknown", Get(records.Cast<object>().Single(record => (string)Get(Get(record, "Identity"), "Kind") == "blood-number"), "State").ToString(), "native invocation uncertainty remains durable after the backoff");
        Setup();
        var other = New(_character.GetType());
        Child(other, "bloodMagic");
        Set(_spells, "character", other);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(false, Saving(), "nonthrowing game call without actual transfer cannot claim success");
        Eq(10000d, Get(_blood, "bloodPoints"), "post-cast validation requires current pool to drain");
    }

    private static void CheckRejectedPill()
    {
        Setup();
        Setting("CustomIronPillMinWorth", 1d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Set(_blood, "bloodPoints", 100d);
        // A separate game-side owner rejects on cooldown. The production precheck
        // passes against current Character, so this exercises post-cast validation.
        var other = New(_character.GetType());
        var otherBlood = Child(other, "bloodMagic");
        Child(otherBlood, "adventureSpellTime");
        Set(_spells, "character", other);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "game-rejected pill retains bounded reservation");
        Eq(100d, Get(_blood, "bloodPoints"), "game-rejected pill retains current saved blood");
        Eq(0, ((Array) _settings.GetType().GetProperty("CustomIronPillHistory").GetValue(_settings)).Length, "game-rejected pill never enters history");
        Eq(5900d, Get(_main, "_customIronPillPrepStartedAt"), "game-rejected pill does not restart saving timer");
    }

    private static void CheckSequentialTimers()
    {
        Setup();
        var setupBoundaryBefore = IronPillSetupBoundary.Enabled;
        try
        {
            IronPillSetupBoundary.Enabled = true;
            Setting("ManageEnergy", true);
            // Reservation adds real RIT work even to an energy-only row. Both
            // requested lanes must be managed and the actual unlock query must work.
            Setting("ManageMagic", true);
            Child(Child(_character, "challenges"), "trollChallenge");
            Set(Child(Child(_character, "allChallenges"), "trollChallenge"), "character", _character);
            var stages = Array.CreateInstance(_stageType, 3);
            for (var i = 0; i < 3; i++)
            {
                var stage = Activator.CreateInstance(_stageType);
                Set(stage, "Name", "sequential-" + i);
                Set(stage, "Percent", 1d);
                Set(stage, "DurationSeconds", i == 1 ? 0 : 4);
                Set(stage, "CompletionMode", i == 1 ? 1 : 0);
                Set(stage, "EnergyPriorities", new[] {"AT-1"});
                Set(stage, "TargetAdvancedTrainingPower", i == 1 ? 100L : 0L);
                Set(stage, "TargetAdvancedTrainingToughness", 0L);
                stages.SetValue(stage, i);
            }
            Set(_plan, "Stages", stages);
            PrepareSetupObservation(_assembly, _character);
            var training = Child(_character, "advancedTraining");
            Set(training, "level", new long[5]);
            Set(training, "energy", new long[5]);
            Set(training, "levelTarget", new[] {-1L, -1L, -1L, -1L, -1L});
            var atControllers = Child(_character, "advancedTrainingController");
            var atNames = new[] {"defense", "attack", "block", "wandoosEnergy", "wandoosMagic"};
            for (var i = 0; i < atNames.Length; i++)
            {
                var controller = Child(atControllers, atNames[i]);
                Set(controller, "character", _character); Set(controller, "id", i);
            }
            Set(((IList)Get(Get(_character, "wishes"), "wishes"))[190], "level", 1);
            var runtime = ActiveStage();
            Eq(0, Property(runtime, "StageIndex"), "late activation starts at the first fixed row, without historical credit");
            Eq(0d, Earned(0), "late rebirth time is not applied training time");
            Eq(false, ProtectedGate(), "fixed opener, AT gate and fixed tail initially block completion");
            var entry = Property(runtime, "EntryIdentity");
            Call("RunCustomBloodSpellAutomation", _plan);
            Eq(true, Saving(), "actual blood automation starts a reservation during the fixed opener");
            Eq(entry, Property(ActiveStage(), "EntryIdentity"), "reservation preserves the fixed opener entry identity");
            Eq(0d, Earned(0), "reservation alone earns no fixed work");
            CreditAppliedSeconds(2d);
            Eq(2d, Earned(0), "controlled successful application earns two real core seconds");
            Eq(false, ProtectedGate(), "partial opener remains protected");
            CreditAppliedSeconds(2d);
            Eq(1, Property(Core, "StageIndex"), "credited fixed opener reaches the real unmet AT gate");
            Eq(4d, Earned(0), "opener completes with actual applied credit");
            entry = Property(ActiveStage(), "EntryIdentity");
            AtTime(5920d);
            Eq(1, Property(ActiveStage(), "StageIndex"), "saving retains the unmet AT step");
            Call("RunCustomBloodSpellToggles", _plan);
            Setting("CustomIronPillSaveSeconds", 1200);
            Call("RunCustomBloodSpellAutomation", _plan);
            Eq(entry, Property(ActiveStage(), "EntryIdentity"), "quick/slow/tuning preserve the AT entry identity");
            Eq(4d, Earned(0), "reservation and tuning preserve already earned opener credit");
            Eq(5900d, Get(_main, "_customIronPillPrepStartedAt"), "timing config edit preserves actual saving start");
            ((long[]) Get(training, "level"))[1] = 100L;
            AtTime(5930d);
            runtime = ActiveStage();
            Eq(2, Property(runtime, "StageIndex"), "saving permits actual AT target completion and the fixed tail");
            Eq(false, Equals(entry, Property(runtime, "EntryIdentity")), "post-AT stage receives its own entry identity");
            Eq(0d, Earned(2), "AT wait does not become historical credit for the fixed tail");
            Eq(false, ProtectedGate(), "unapplied fixed tail still blocks the real gate");
            entry = Property(runtime, "EntryIdentity");
            Set(_plan, "AutoIronPillEnabled", false);
            Call("RunCustomBloodSpellAutomation", _plan);
            Eq(false, Saving(), "plan opt-out cancels the reservation on the fixed tail");
            Eq(entry, Property(ActiveStage(), "EntryIdentity"), "cancelling reservation never resets the fixed entry");
            CreditAppliedSeconds(2d);
            CreditAppliedSeconds(2d);
            Eq(4d, Earned(2), "tail also completes only through applied credit");
            RequireCheckpoint("fixed/AT/fixed completion is durably saved");
            Eq(true, ProtectedGate(), "actual shared completion gate opens only after both fixed rows and AT");
            Setting("ManageMagic", true);
            Setting("CustomBloodSpaghettiGoalPercent", 15);
            var casual = stages.GetValue(2);
            Set(casual, "EnergyFocus", 5);
            Set(casual, "MagicFocus", 6);
            Set(casual, "GearGoal", (int) Enum.Parse(_assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true), "EnergyNgu"));
            CachePlan();
            var state = CoreState();
            Eq(true, GoalRequest(), "ordinary catch-up resumes on post-AT casual stage");
            Call("RunCustomBloodSpellToggles", _plan);
            Eq("true", Json(Call("GetCustomPlanProgress"), "bloodGoalsPrioritized").ToString(), "post-AT catch-up telemetry follows actual active step");
            Eq(state, CoreState(), "catch-up/toggles/telemetry preserve the completed engine byte-for-byte");
            Eq(entry, Property(ActiveStage(), "EntryIdentity"), "catch-up preserves the fixed tail entry identity");
            Eq(2, Property(Core, "StageIndex"), "catch-up preserves the actual engine index");
            Eq(4d, Earned(2), "catch-up preserves actual fixed tail credit");
            Eq(-1d, Get(_main, "_customIronPillPrepStartedAt"), "catch-up does not restart the cancelled reservation");
        }
        finally
        {
            IronPillSetupBoundary.Enabled = setupBoundaryBefore;
        }
    }

    private static void CheckUnavailableState()
    {
        Setup();
        var controller = Get(_character, "bloodMagicController");
        Set(_character, "bloodMagicController", null);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(false, Saving(), "unavailable production state cannot start saving");
        Eq(10000d, Get(_blood, "bloodPoints"), "unavailable state never flushes on startup");
        Set(_character, "bloodMagicController", controller);
        var button = Get(Get(_character, "buttons"), "bloodMagic");
        FindField(button.GetType(), "m_Interactable").SetValue(button, false);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(false, Saving(), "locked Blood Magic cannot start saving");
        Eq(10000d, Get(_blood, "bloodPoints"), "locked Blood Magic retains existing blood");
        FindField(button.GetType(), "m_Interactable").SetValue(button, true);
        Set(controller, "bloodMagics", null);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(false, Saving(), "uninitialized ritual controllers cannot start saving");
        Eq(10000d, Get(_blood, "bloodPoints"), "uninitialized ritual state never flushes banked blood");
        Set(_blood, "adventureSpellTime", null);
        Eq(double.PositiveInfinity, Call("IronPillCooldownRemainingSeconds"), "uninitialized cooldown safely waits");
    }

    private static void CheckTimeout()
    {
        Setup();
        Setting("CustomIronPillSaveSeconds", 0);
        Setting("CustomIronPillMinWorth", 1e8d);
        Call("RunCustomBloodSpellAutomation", _plan);
        AtTime(6859d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "worth-only window remains active just before finite deadline");
        AtTime(6860d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "impossible worth/zero blood yields at 960 seconds");
        Eq(7760d, Get(_main, "_customIronPillRetryAt"), "timeout yields for 900 seconds");
        Set(_blood, "bloodPoints", 200d);
        Call("RunCustomBloodSpellToggles", _plan);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "timeout cannot immediately restart via either loop");
        Eq(200d, Get(_blood, "bloodPoints"), "timeout retry does not flush again");
        AssertToggles(false, false, true, "timeout");
        var policy = _assembly.GetType("NGUInjector.Managers.IronPillAutomation", true);
        foreach (var seconds in new[] {0, 1, 900, 1800, 604800})
            Eq(Math.Max(900d, seconds) + 60d, policy.GetMethod("PrepLimitSeconds", All).Invoke(null, new object[] {seconds}), "finite prep limit " + seconds);
    }

    private static void CheckPreemption()
    {
        foreach (var globalAuto in new[] {false, true})
        foreach (var planAuto in new[] {false, true})
        {
            Setup();
            Setting("AutoRebirth", globalAuto);
            Set(_plan, "AutoRebirthEnabled", planAuto);
            Eq(string.Empty, Call("CustomIronPillEligibilityBlocker", _plan), "pill eligibility is independent of both automatic rebirth switches");
            Call("RunCustomBloodSpellAutomation", _plan);
            Eq(true, Saving(), "active managed magic and pill opt-in start saving regardless of rebirth permission");
            Eq(0d, Get(_blood, "bloodPoints"), "eligible pill flushes the exact existing blood pool");
            Eq(10011d, Get(_blood, "rebirthPower"), "eligible pill transfers the exact pool to NUMBER");
        }
        Setup();
        Setting("ManageMagic", false);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq("Magic management is disabled.", Call("CustomIronPillEligibilityBlocker", _plan), "unmanaged magic blocks pill eligibility");
        Eq(false, Saving(), "unmanaged magic cannot start reservation");
        Eq(10000d, Get(_blood, "bloodPoints"), "unmanaged magic preserves the exact blood pool");
        Eq(11d, Get(_blood, "rebirthPower"), "unmanaged magic never casts NUMBER");
        AssertToggles(true, true, true, "unmanaged magic preserves native spell toggles");
        foreach (var flag in new[] {"_manualRebirthPending", "_customPreRebirthPrepRunning", "_bossPushRunning"})
        {
            Setup();
            Call("RunCustomBloodSpellAutomation", _plan);
            Set(_blood, "bloodPoints", 250d);
            Set(_main, flag, true);
            Call("RunCustomBloodSpellAutomation", _plan);
            Eq(false, Saving(), flag + " preempts saving");
            Eq(-1d, Get(_main, "_customIronPillPrepStartedAt"), flag + " clears raw window state");
            Eq(250d, Get(_blood, "bloodPoints"), flag + " does not drain reserved blood");
            AssertToggles(false, false, true, flag);
        }
        Setup();
        Set(_character, "capEnergy", 123456L); Set(_character, "curEnergy", 123456L);
        NativeFightFixture.Prepare(_assembly, _character);
        var directory = (string)_mainType.GetField("_dir", All).GetValue(null); var plans = Path.Combine(directory, "plans");
        Directory.CreateDirectory(plans);
        var store = New(_mainType.GetField("CustomPlanStore", All).FieldType); Set(store, "_plansDir", plans);
        Set(store, "<PresetLibrary>k__BackingField", Activator.CreateInstance(_assembly.GetType("NGUInjector.CustomPlans.PresetLibraryStore", true), All,
            null, new object[] { directory, plans }, null));
        _mainType.GetField("CustomPlanStore", All).SetValue(null, store);
        store.GetType().GetMethod("Save", All).Invoke(store, new object[] { "iron-reservation-check", _plan });
        Call("RunCustomBloodSpellAutomation", _plan);
        var summary = NativeFightFixture.Request(_main, directory, "manualFightPrep", "iron-pill-fight-preemption");
        Eq(true, Property(_main, "BossPushRunning"), "identified real fight preparation admits its actual process: " + summary);
        Eq(true, Property(_main, "FightPreparationVerified"), "fight preemption has actual native gear/support and hard-prerequisite proof");
        Eq(-1d, Get(_main, "_customIronPillPrepStartedAt"), "fight start immediately clears saving before next quick tick");
        Setup();
        Call("RunCustomBloodSpellAutomation", _plan);
        var rebirth = Child(_character, "rebirth");
        Set(rebirth, "character", _character);
        var wishes = Child(_character, "wishes");
        var listField = wishes.GetType().GetField("wishes", All);
        var list = (IList) Activator.CreateInstance(listField.FieldType);
        for (var i = 0; i < 21; i++) list.Add(New(listField.FieldType.GetGenericArguments()[0]));
        listField.SetValue(wishes, list);
        Child(Child(_character, "challenges"), "noRebirthChallenge");
        Set(_plan, "TimeTriggerEnabled", true);
        Set(_plan, "TimeTriggerSeconds", 5900);
        Eq(true, Call("CustomRebirthReady", _plan), "fixture exercises actual automatic rebirth readiness");
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(false, Saving(), "ready automatic rebirth preempts pill preparation");
        Eq("RebirthReady", Call("CurrentCustomGoal", _plan).ToString(), "rebirth wins goal arbitration");
        AssertToggles(false, false, true, "ready rebirth");
    }

    private static object Stage(int index) { return ((Array) Get(_plan, "Stages")).GetValue(index); }
    private static object Core { get { return Get(_main, "_protectedScheduler"); } }
    private static object Property(object owner, string name) { return owner.GetType().GetProperty(name, All).GetValue(owner); }
    private static object ActiveStage() { return _scheduler.GetMethod("GetActiveStage", All).Invoke(null, new[] {_plan}); }
    private static object Snapshot() { return _scheduler.GetMethod("GetSchedulerSnapshot", All).Invoke(null, new object[] {_plan, Get(_clock, "totalseconds")}); }
    private static double Earned(int index) { return ((double[]) Property(Core, "EarnedSeconds"))[index]; }
    private static string CoreState() { return Core.GetType().GetMethod("ExportState", All).Invoke(Core, null).ToString(); }
    private static bool ProtectedGate() { return (bool) _scheduler.GetMethod("ProtectedWorkComplete", All).Invoke(null, new[] {_plan}); }
    private static void AtTime(double seconds)
    {
        Set(_clock, "totalseconds", seconds);
        Set(Get(_character, "totalPlaytime"), "totalseconds", seconds + 100000d);
    }
    private static void CreditAppliedSeconds(double seconds)
    {
        // Obtain setup and resource evidence from the real coordinator. A fabricated
        // allocation receipt cannot establish inline setup authority or earn credit.
        Call("RunCustomPlanAutomation", _plan);
        var key = Call("ProtectedStageResourceKey", _plan);
        Eq(true, Call("InlineStageSetupVerified", _plan), "credited interval requires native setup verification; " + _mainType.GetMethod("GetCustomAutomationStatus", All).Invoke(null, null));
        Eq(true, _scheduler.GetMethod("StageApplicationStillOwned", All).Invoke(null, new[] {key}), "credited interval owns actual current-entry resource application");
        Eq(true, Call("ProtectedStageOwnershipAvailable", _plan), "actual applied interval passes ownership and work eligibility");
        var elapsed = (double) Get(_clock, "totalseconds");
        var heartbeat = (double) Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        Eq(true, Get(Core, "_armed"), "actual coordinator commit arms the real state engine");
        Set(Core, "_lastHeartbeat", heartbeat - seconds);
        AtTime(elapsed + seconds);
        ActiveStage();
    }
    internal static void PrepareGearObservation(Assembly assembly, object character)
    {
        var inventory = Child(character, "inventory");
        foreach (var slot in new[] {"head", "chest", "legs", "boots", "weapon", "weapon2"}) Child(inventory, slot);
        foreach (var name in new[] {"accs", "inventory", "macguffins"})
            Set(inventory, name, Activator.CreateInstance(FindField(inventory.GetType(), name).FieldType));
        var controller = Child(character, "inventoryController"); Set(controller, "character", character);
        Set(Child(controller, "itemInfo"), "itemName", new[] {"Empty"});
        assembly.GetType("NGUInjector.Main", true).GetField("Controller", All).SetValue(null, controller);
    }
    internal static void PrepareSetupObservation(Assembly assembly, object character)
    {
        // Complete native observation models with locked, empty support and empty
        // gear. The production selector, shared actuator and verifier still run.
        PrepareGearObservation(assembly, character);
        foreach (var name in new[] {"res3", "yggdrasil", "bossController", "adventure", "magic", "challenges", "allChallenges"})
            if (Get(character, name) == null) Child(character, name);
        Set(Get(character, "adventure"), "zone", -1);
        foreach (var name in new[] {"beards", "diggers"})
        {
            var model = Child(character, name);
            var field = FindField(model.GetType(), name); var list = (IList)Activator.CreateInstance(field.FieldType);
            for (var i = 0; i < (name == "beards" ? 7 : 12); i++) list.Add(New(field.FieldType.GetGenericArguments()[0]));
            field.SetValue(model, list);
            var active = FindField(model.GetType(), name == "beards" ? "activeBeards" : "activeDiggers");
            active.SetValue(model, Activator.CreateInstance(active.FieldType));
        }
        foreach (var name in new[] {"allBeards", "allDiggers"}) Set(Child(character, name), "character", character);
        Set(Get(character, "allBeards"), "usesEnergy", new bool[7]);
        var diggers = Get(character, "allDiggers");
        foreach (var name in new[] {"baseGPSDrain", "gpsGrowthRate", "startingBoost", "boostPerLevel"})
            Set(diggers, name, Enumerable.Repeat(name == "gpsGrowthRate" ? 2d : 0d, 12).ToList());
        Set(diggers, "consumesGPS", Enumerable.Repeat(false, 12).ToList());
        foreach (var name in new[] {"beards", "diggers"}) Child(Get(character, "buttons"), name);
        foreach (var name in new[] {"timeMachineChallenge", "trollChallenge"})
            if (Get(Get(character, "challenges"), name) == null) Child(Get(character, "challenges"), name);
        Child(Get(character, "allChallenges"), "timeMachineChallenge");
        if (Get(character, "bloodMagicController") == null) Child(character, "bloodMagicController");
        var augments = Child(character, "augmentsController");
        Set(augments, "augments", Array.CreateInstance(FindField(augments.GetType(), "augments").FieldType.GetElementType(), 0));
    }
    private static void RequireCheckpoint(string label)
    {
        Eq(true, Get(_main, "_protectedCheckpointDurable"), label + " has a durable coordinator checkpoint");
        Eq(true, File.Exists((string) _mainType.GetProperty("ProtectedCheckpointPath", All).GetValue(_main)), label + " writes a real checkpoint under temporary Main._dir");
    }
    private static bool Saving() { return (bool) Call("CustomIronPillPrepActive"); }
    private static object Call(string name, params object[] args) { return _mainType.GetMethod(name, All).Invoke(_main, args); }
    private static void Setting(string name, object value) { _settings.GetType().GetProperty(name).SetValue(_settings, value); }
    private static object Get(object owner, string name) { return FindField(owner.GetType(), name).GetValue(owner); }
    private static void Set(object owner, string name, object value) { FindField(owner.GetType(), name).SetValue(owner, value); }
    private static FieldInfo FindField(Type type, string name)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField(name, All);
            if (field != null) return field;
        }
        throw new MissingFieldException(name);
    }
    private static object New(Type type)
    {
        var value = FormatterServices.GetUninitializedObject(type);
        // Keep Unity's managed null comparison data-only; never invoke a native method.
        for (var current = type; current != null; current = current.BaseType)
        {
            var pointer = current.GetField("m_CachedPtr", All);
            if (pointer != null) { pointer.SetValue(value, new IntPtr(1)); break; }
        }
        return value;
    }
    private static object Child(object parent, string name)
    {
        var field = FindField(parent.GetType(), name);
        var child = New(field.FieldType);
        field.SetValue(parent, child);
        return child;
    }
    private static string Order(object stage, bool energy, bool reserve, int remaining = 0, int duration = 0, int energyFocus = -1)
    {
        if (energyFocus >= 0) Set(stage, "EnergyFocus", energyFocus);
        return string.Join(",", (string[]) _scheduler.GetMethod("EffectiveStagePriorities", All).Invoke(null,
            new object[] {stage, energy, remaining, duration, false, reserve, false}));
    }
    private static bool GoalRequest() { return (bool) Call("CustomBloodGoalCatchUpRequested", _plan); }
    private static string GoalOrder(object stage)
    {
        return string.Join(",", (string[]) _scheduler.GetMethod("EffectiveStagePriorities", All).Invoke(null,
            new object[] {stage, false, 0, 0, false, Saving(), GoalRequest()}));
    }
    private static void CachePlan()
    {
        Set(_main, "_customRuntimePlanCache", _plan);
        Set(_main, "_customRuntimePlanCacheName", _settings.GetType().GetProperty("CustomPlanFile").GetValue(_settings));
        Set(_main, "_customRuntimePlanCacheLoadedAt", DateTime.Now);
    }
    private static object Json(object root, string key) { return root.GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(root, new object[] {key}); }
    private static object Row(object progress, int index)
    {
        var rows = Json(progress, "stages");
        return rows.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(rows, new object[] {index});
    }
    private static void AssertToggles(bool loot, bool gold, bool number, string label)
    {
        Eq(loot, Get(_blood, "lootAutoSpell"), label + " loot toggle");
        Eq(gold, Get(_blood, "goldAutoSpell"), label + " gold toggle");
        Eq(number, Get(_blood, "rebirthAutoSpell"), label + " NUMBER toggle");
    }
    private static void Eq(object expected, object actual, string label)
    {
        _assertions++;
        if (!Equals(expected, actual)) throw new Exception(label + ": expected " + expected + ", got " + actual + "\n" + System.Text.Encoding.UTF8.GetString(_log.ToArray()));
    }
}
