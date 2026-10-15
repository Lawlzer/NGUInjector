using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Execute the compiled injector and the game's real blood-spell methods. No Unity
// constructors/native actions. Only external blood menu/tooltip rendering is disabled.
internal static class IronPillReservationChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly _assembly;
    private static Type _mainType, _stageType, _planType, _scheduler;
    private static object _main, _character, _settings, _blood, _spells, _clock, _plan;
    private static int _assertions;
    private static MemoryStream _log;

    internal static Assembly LoadGameWithoutBloodUi(string managedDirectory)
    {
        using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(managedDirectory, "Assembly-CSharp.dll")))
        using (var bytes = new MemoryStream())
        {
            var patched = 0;
            foreach (var type in game.MainModule.Types)
            foreach (var method in type.Methods)
            {
                var bloodMenu = type.Name == "RebirthPowerSpell" && new[] {
                    "updateMenu", "updateLootToggleState", "updateGoldToggleState", "updateRebirthToggleState"
                }.Contains(method.Name);
                var tooltip = type.Name == "HoverTooltip" && method.Name == "showTooltip" && method.Parameters.Count == 2;
                if (!bloodMenu && !tooltip) continue;
                if (method.ReturnType.FullName != "System.Void") throw new Exception("Unexpected UI boundary signature: " + method.FullName);
                method.Body = new Mono.Cecil.Cil.MethodBody(method);
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                patched++;
            }
            if (patched != 5) throw new Exception("Expected five external blood UI/tooltip methods, found " + patched);
            game.Write(bytes);
            return Assembly.Load(bytes.ToArray());
        }
    }

    internal static int Run(Assembly assembly)
    {
        _assembly = assembly;
        _mainType = assembly.GetType("NGUInjector.Main", true);
        _stageType = assembly.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
        _planType = assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true);
        _scheduler = assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        try
        {
            CheckOrders();
            CheckWindowAndStageTransitions();
            CheckMinimumAndSuccessfulCast();
            CheckRejectedPill();
            CheckRejectedFlush();
            CheckUnavailableState();
            CheckTimeout();
            CheckPreemption();
            CheckSequentialTimers();
            Console.WriteLine("Iron Pill reservation checks passed: " + _assertions + " assertions.");
            return _assertions;
        }
        finally
        {
            foreach (var name in new[] {"reference", "Character", "Settings", "CustomPlanStore"})
                _mainType.GetField(name, All).SetValue(null, null);
            _mainType.GetField("OutputWriter", All).SetValue(null, null);
            _scheduler.GetMethod("ResetStageRuntime", All).Invoke(null, null);
        }
    }

    private static void Setup()
    {
        _mainType.GetField("reference", All).SetValue(null, null);
        _log = new MemoryStream();
        _mainType.GetField("OutputWriter", All).SetValue(null, new StreamWriter(_log) { AutoFlush = true });
        _character = New(_mainType.GetField("Character", All).FieldType);
        _mainType.GetField("Character", All).SetValue(null, _character);
        _main = New(_mainType);
        Set(_main, "_customIronPillPrepStartedAt", -1d);
        Set(_main, "_customStageGearDone", true);
        _settings = Activator.CreateInstance(_assembly.GetType("NGUInjector.SavedSettings", true), new object[] {null});
        _mainType.GetField("Settings", All).SetValue(null, _settings);
        foreach (var name in new[] {"UseCustomRebirthPlan", "AutoRebirth", "CustomAutoBloodSpellsEnabled"}) Setting(name, true);
        Setting("ManageEnergy", false);
        Setting("ManageMagic", false);
        Setting("CustomIronPillSaveSeconds", 900);
        Setting("CustomIronPillMinWorth", 0d);
        Setting("CustomBloodSpaghettiGoalPercent", 0);
        Setting("CustomBloodCounterfeitGoalPercent", 0);
        _mainType.GetField("reference", All).SetValue(null, _main);

        _clock = Child(_character, "rebirthTime");
        Set(_clock, "totalseconds", 5900d);
        Set(_character, "bossID", 100);
        Set(_character, "attackMulti", 1d);
        Set(_character, "nextAttackMulti", 2d);
        Set(_character, "idleEnergy", 123456L);
        Child(_character, "settings");
        Child(_character, "magic");
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
        Set(_plan, "StageDurationSeconds", 12000);
        Set(_plan, "StageSchedulerEnabled", true);
        Set(_plan, "AutoRebirthEnabled", true);
        Set(_plan, "AutoActivateFruits", false);
        foreach (var name in new[] {"TimeTriggerEnabled", "NumberTriggerEnabled", "NumberClimbEnabled"}) Set(_plan, name, false);
        var stages = Array.CreateInstance(_stageType, 2);
        for (var i = 0; i < 2; i++)
        {
            var stage = Activator.CreateInstance(_stageType);
            Set(stage, "Name", "stage-" + i);
            Set(stage, "Percent", 1d);
            Set(stage, "GearGoal", -1);
            Set(stage, "EnergyFocus", 5);
            Set(stage, "MagicFocus", i == 0 ? 6 : 3);
            stages.SetValue(stage, i);
        }
        Set(_plan, "Stages", stages);
        Set(_plan, "SourceJson", Activator.CreateInstance(_assembly.GetType("SimpleJSON.JSONObject", true)));
        _mainType.GetField("CustomPlanStore", All).SetValue(null, New(_mainType.GetField("CustomPlanStore", All).FieldType));
        CachePlan();
        _scheduler.GetMethod("ResetStageRuntime", All).Invoke(null, null);
    }

    private static void CheckOrders()
    {
        Setup();
        var stage = Stage(0);
        Eq("RIT,NGU,WAN,TM", Order(stage, false, true), "default NGU magic promotes RIT");
        Eq("NGU,RIT,WAN,TM", Order(stage, false, false), "unreserved default restored");
        Set(stage, "MagicFocus", 3);
        Eq("WAN,RIT,NGU,TM", Order(stage, false, true), "Wandoos preserves WAN first");
        Eq("RIT,WAN,NGU,TM", Order(stage, false, false, 80, 100), "ordinary AT Wandoos retains existing order");
        Eq("WAN,RIT,NGU,TM", Order(stage, false, true, 80, 100), "saving AT Wandoos keeps WAN first");
        Eq("AT-3,AT-4,WAN,NGU,TM", Order(Stage(1), true, true, 80, 100, 3), "AT Wandoos energy unchanged");
        var energy = new[] {"AT-1", "NGU", "TM"};
        var magic = new[] {"NGU", "RIT-0", "TM", "RIT", "RIT-7", "NGU", "WAN"};
        Set(stage, "EnergyPriorities", energy);
        Set(stage, "MagicPriorities", magic);
        Eq("WAN,RIT,NGU,TM", Order(stage, false, true), "explicit rituals collapse without duplicating gold consumers");
        Eq("AT-1,NGU,TM", Order(stage, true, true), "explicit energy untouched");
        Eq("NGU,RIT-0,TM,RIT,RIT-7,NGU,WAN", string.Join(",", magic), "saved magic array remains immutable");
        Eq(true, ReferenceEquals(magic, Get(stage, "MagicPriorities")), "saved magic array identity retained");
        Eq(true, ReferenceEquals(energy, Get(stage, "EnergyPriorities")), "saved energy array identity retained");
        Eq("NGU,RIT-0,TM,RIT,RIT-7,NGU,WAN", Order(stage, false, false), "cancel restores exact explicit order");
        Set(stage, "MagicFocus", 6);
        Eq("RIT,NGU,TM,WAN", Order(stage, false, true), "WAN token alone does not make NGU a Wandoos stage");
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
        Eq(true, Get(_main, "_customStageGearDone"), "reservation does not invalidate equipped gear");
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
        Set(_clock, "totalseconds", 6000d);
        Call("RunCustomPlanAutomation", _plan);
        CachePlan();
        progress = Call("GetCustomPlanProgress");
        Eq("1", Json(progress, "activeStageIndex").ToString(), "stage transitions continue while saving");
        Eq("0", Json(progress, "stageElapsedSeconds").ToString(), "stage transition clock unchanged");
        Eq("[\"WAN\",\"RIT\",\"NGU\",\"TM\"]", Json(Row(progress, 1), "magicPriorities").ToString(), "new Wandoos stage gets correct live overlay");
        Eq(5900d, Get(_main, "_customIronPillPrepStartedAt"), "stage transition does not restart reservation");
        Set(_main, "_customLastQuickResourceRefresh", DateTime.MinValue);
        Call("RunQuickCustomResourceRefresh", _plan);
        Eq(321d, Get(_blood, "bloodPoints"), "quick allocation does not flush blood");
        Eq(123456L, Get(_character, "idleEnergy"), "normal stage passes do not trigger prep energy takeover");
        Set(_main, "_customStageGearDone", true);
        Set(_plan, "AutoIronPillEnabled", false);
        Call("RunCustomBloodSpellToggles", _plan);
        Eq(false, Saving(), "plan opt-out cancels reservation");
        Eq(true, Get(_main, "_customStageGearDone"), "cancel leaves stage gear intact");
        AssertToggles(true, true, false, "cancel restores ordinary loot/gold goals");
        Eq(321d, Get(_blood, "bloodPoints"), "cancel does not flush accumulated blood");
    }

    private static void CheckMinimumAndSuccessfulCast()
    {
        Setup();
        Set(_blood, "bloodPoints", .5d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(11.5d, Get(_blood, "rebirthPower"), "NUMBER accepts fractional blood below pill/loot/gold minima");
        Eq(true, Saving(), "fractional NUMBER flush begins reservation");
        Setting("CustomIronPillSaveSeconds", 10000);
        Setting("CustomIronPillMinWorth", 1d);
        Set(_blood, "bloodPoints", 99d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(99d, Get(_blood, "bloodPoints"), "pill minimum rejects insufficient banked blood");
        Eq(0, ((Array) _settings.GetType().GetProperty("CustomIronPillHistory").GetValue(_settings)).Length, "minimum failure never enters history");
        Set(_blood, "bloodPoints", 100d);
        Set(_clock, "totalseconds", 5910d);
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
        Eq(true, Get(_main, "_customStageGearDone"), "cast completion never invalidates stage gear");
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
        Set(_clock, "totalseconds", 6800d);
        Call("UpdateCustomIronPillPrep", _plan);
        Eq(true, Saving(), "retry at inclusive boundary can reserve again");
        Eq(10011d, Get(_blood, "rebirthPower"), "retry flushes retained blood once");
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
        var stages = Array.CreateInstance(_stageType, 3);
        for (var i = 0; i < 3; i++)
        {
            var stage = Activator.CreateInstance(_stageType);
            Set(stage, "Name", "sequential-" + i);
            Set(stage, "Percent", 1d);
            Set(stage, "DurationSeconds", i == 1 ? 0 : 20);
            Set(stage, "CompletionMode", i == 1 ? 1 : 0);
            Set(stage, "TargetAdvancedTrainingPower", i == 1 ? 100L : 0L);
            Set(stage, "TargetAdvancedTrainingToughness", 0L);
            stages.SetValue(stage, i);
        }
        Set(_plan, "Stages", stages);
        var training = Child(_character, "advancedTraining");
        Set(training, "level", new long[5]);
        var active = _scheduler.GetMethod("GetActiveStage", All);
        active.Invoke(null, new[] {_plan});
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(5900d, _scheduler.GetField("_sequentialStageStartedAt", All).GetValue(null), "reservation does not reset sequential stage timer");
        Set(_clock, "totalseconds", 5920d);
        var runtime = active.Invoke(null, new[] {_plan});
        Eq(1, runtime.GetType().GetProperty("StageIndex", All).GetValue(runtime), "saving permits transition into unmet AT step");
        Call("RunCustomBloodSpellToggles", _plan);
        Setting("CustomIronPillSaveSeconds", 1200);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(5920d, _scheduler.GetField("_sequentialStageStartedAt", All).GetValue(null), "quick/slow/tuning preserve AT timer");
        Eq(5900d, Get(_main, "_customIronPillPrepStartedAt"), "timing config edit preserves actual saving start");
        ((long[]) Get(training, "level"))[1] = 100L;
        Set(_clock, "totalseconds", 5930d);
        runtime = active.Invoke(null, new[] {_plan});
        Eq(2, runtime.GetType().GetProperty("StageIndex", All).GetValue(runtime), "saving permits real AT completion and next stage");
        Eq(5930d, _scheduler.GetField("_sequentialStageStartedAt", All).GetValue(null), "post-AT stage begins at actual completion");
        Set(_plan, "AutoIronPillEnabled", false);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(5930d, _scheduler.GetField("_sequentialStageStartedAt", All).GetValue(null), "cancelling reservation never resets sequential timer");
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
        Set(_clock, "totalseconds", 6859d);
        Call("RunCustomBloodSpellAutomation", _plan);
        Eq(true, Saving(), "worth-only window remains active just before finite deadline");
        Set(_clock, "totalseconds", 6860d);
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
        Call("RunCustomBloodSpellAutomation", _plan);
        Call("StartBossPush");
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
            new object[] {stage, energy, remaining, duration, false, reserve}));
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
