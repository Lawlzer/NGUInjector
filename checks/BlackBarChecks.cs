using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

internal static class BlackBarChecks
{
    private static Type _allocator;
    private static Assembly _assembly;
    private static int _assertions;
    private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
                throw new ArgumentException("Pass NGUInjector.dll and the game's Managed directory.");
            Assembly game = null;
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
            {
                var name = new AssemblyName(request.Name).Name;
                if (name == "Assembly-CSharp" && game != null) return game;
                var path = Path.Combine(args[1], name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            game = IronPillReservationChecks.LoadGameWithoutBloodUi(args[1]);
            _assembly = Assembly.Load(File.ReadAllBytes(args[0]));
            _allocator = _assembly.GetType("NGUInjector.Managers.BlackBarAllocator", true);
            CheckOrderedAllocation();
            CheckWeightedSharing();
            CheckProjection();
            CheckRounding();
            CheckTokens();
            CheckNguSnapshot();
            CheckSavedStages();
            CheckStageRuntime();
            CheckFightOnEntry();
            CheckIdleExemptionReset();
            CheckBloodGoals();
            CheckIronPill();
            _assertions += IronPillReservationChecks.Run(_assembly);
            _assertions += GearOptimizerChecks.Run(_assembly);
            _assertions += GearSearchChecks.Run(_assembly);
            _assertions += GearEquipChecks.Run(_assembly);
            _assertions += GearSingleStatChecks.Run(_assembly);
            Console.WriteLine("Black-bar checks passed: " + _assertions + " assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CheckIronPill()
    {
        var policy = _assembly.GetType("NGUInjector.Managers.IronPillAutomation", true);
        var eligible = policy.GetMethod("Eligible", StaticMethods);
        foreach (var seconds in new[] {0d, 3599.99d, 3600d, 7200d})
        foreach (var stage in new[] {99, 100, 101})
        foreach (var enabled in new[] {false, true})
        foreach (var active in new[] {false, true})
            Equal(enabled && active && seconds >= 3600d && stage >= 100,
                (bool) eligible.Invoke(null, new object[] {enabled, active, seconds, stage}),
                $"Iron Pill eligibility {seconds}s/stage {stage}/enabled {enabled}/plan {active}");

        var reserve = policy.GetMethod("ShouldReserve", StaticMethods);
        Equal(false, (bool) reserve.Invoke(null, new object[] {true, false, 901d, 900, 0d}), "distant cooldown does not suppress spells");
        Equal(true, (bool) reserve.Invoke(null, new object[] {true, false, 900d, 900, 0d}), "reserve at inclusive lookahead before preparation loop");
        Equal(true, (bool) reserve.Invoke(null, new object[] {true, false, 0d, 900, 0d}), "ready pill immediately reserves blood");
        Equal(false, (bool) reserve.Invoke(null, new object[] {false, true, 0d, 900, 0d}), "lost eligibility cancels active saving");
        Equal(true, (bool) reserve.Invoke(null, new object[] {true, true, 1800d, 900, 0d}), "active saving retains priority until cast");
        Equal(true, (bool) reserve.Invoke(null, new object[] {true, false, 900d, 0, 500d}), "worth-only cooldown lookahead");
        Equal(false, (bool) reserve.Invoke(null, new object[] {true, false, 1d, 0, 0d}), "immediate mode waits until ready to reserve");
        Equal(true, (bool) reserve.Invoke(null, new object[] {true, false, 0d, 0, 0d}), "immediate mode reserves when ready");

        var target = policy.GetMethod("TargetReached", StaticMethods);
        Equal(false, (bool) target.Invoke(null, new object[] {899d, 499d, 900, 500d}), "neither target met");
        Equal(true, (bool) target.Invoke(null, new object[] {900d, 499d, 900, 500d}), "time target alone suffices");
        Equal(true, (bool) target.Invoke(null, new object[] {1d, 500d, 900, 500d}), "worth target alone suffices");
        Equal(false, (bool) target.Invoke(null, new object[] {900d, 499d, 0, 500d}), "disabled time cannot satisfy worth-only mode");
        Equal(false, (bool) target.Invoke(null, new object[] {899d, 500d, 900, 0d}), "disabled worth cannot satisfy time-only mode");
        Equal(true, (bool) target.Invoke(null, new object[] {0d, 0d, 0, 0d}), "both thresholds disabled means immediate");

        var worth = policy.GetMethod("Worth", StaticMethods);
        Equal(10d, (double) worth.Invoke(null, new object[] {10000d, 1f}), "pill worth fourth root");
        Equal(10d, (double) worth.Invoke(null, new object[] {14640d, 1f}), "pill worth rounds down before bonus");
        Equal(25d, (double) worth.Invoke(null, new object[] {14640d, 2.5f}), "Evil bonus multiplies rounded value");
        Equal(1e8d, (double) worth.Invoke(null, new object[] {1e100d, 100f}), "game caps pill worth at 100 million");

        var settingsType = _assembly.GetType("NGUInjector.SavedSettings", true);
        var settings = Activator.CreateInstance(settingsType, new object[] {null});
        var historyType = _assembly.GetType("NGUInjector.IronPillHistoryEntry", true);
        var addHistory = settingsType.GetMethod("AddIronPillHistory", BindingFlags.Instance | BindingFlags.NonPublic);
        Equal(900, (int) settingsType.GetProperty("CustomIronPillSaveSeconds").GetValue(settings), "new settings default to 15-minute saving");
        for (var i = 1; i <= 12; i++)
        {
            var entry = Activator.CreateInstance(historyType);
            historyType.GetField("Worth").SetValue(entry, (double) i);
            historyType.GetField("WaitSeconds").SetValue(entry, 60d * i);
            addHistory.Invoke(settings, new[] {entry});
        }
        var history = (Array) settingsType.GetProperty("CustomIronPillHistory").GetValue(settings);
        Equal(10, history.Length, "history retains only last ten casts");
        Equal(12d, (double) historyType.GetField("Worth").GetValue(history.GetValue(0)), "history newest cast first");
        Equal(3d, (double) historyType.GetField("Worth").GetValue(history.GetValue(9)), "history discards oldest casts");
        Equal(720d, (double) historyType.GetField("WaitSeconds").GetValue(history.GetValue(0)), "history keeps actual saving duration");
        var copy = Activator.CreateInstance(settingsType, new object[] {null});
        settingsType.GetMethod("MassUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(copy, new[] {settings});
        Equal(10, ((Array) settingsType.GetProperty("CustomIronPillHistory").GetValue(copy)).Length, "settings reload/patch copy retains history");
    }

    private static object Call(string method, params object[] args)
    {
        return _allocator.GetMethod(method, StaticMethods).Invoke(null, args);
    }

    private static void CheckStageRuntime()
    {
        var mainType = _assembly.GetType("NGUInjector.Main", true);
        var stageType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
        var planType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true);
        var scheduler = _assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        var characterField = mainType.GetField("Character", StaticMethods);
        // Data-only fixtures: no Unity constructors, controllers, injection, or game actions.
        var character = FormatterServices.GetUninitializedObject(characterField.FieldType);
        var timeField = character.GetType().GetField("rebirthTime");
        var time = Activator.CreateInstance(timeField.FieldType);
        timeField.SetValue(character, time);
        characterField.SetValue(null, character);
        var main = FormatterServices.GetUninitializedObject(mainType);
        mainType.GetField("_customIronPillPrepStartedAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, -1d);
        mainType.GetField("reference", StaticMethods).SetValue(null, main);
        var settingsType = _assembly.GetType("NGUInjector.SavedSettings", true);
        var settings = Activator.CreateInstance(settingsType, new object[] {null});
        mainType.GetField("Settings", StaticMethods).SetValue(null, settings);

        Action reset = () => scheduler.GetMethod("ResetStageRuntime", StaticMethods).Invoke(null, null);
        Func<double, int, object> makePlan = (weight, count) =>
        {
            var plan = Activator.CreateInstance(planType);
            planType.GetField("Name").SetValue(plan, "runtime-check");
            planType.GetField("StageSchedulerEnabled").SetValue(plan, true);
            planType.GetField("StageDurationSeconds").SetValue(plan, 100);
            planType.GetField("StageLoopStartIndex").SetValue(plan, -1);
            var stages = Array.CreateInstance(stageType, count);
            for (var i = 0; i < count; i++)
            {
                var stage = Activator.CreateInstance(stageType);
                stageType.GetField("Name").SetValue(stage, "stage-" + i);
                stageType.GetField("Percent").SetValue(stage, weight);
                stages.SetValue(stage, i);
            }
            planType.GetField("Stages").SetValue(plan, stages);
            return plan;
        };
        Action<object, double, int, int, int, int> expect = (plan, elapsed, index, start, end, remaining) =>
        {
            time.GetType().GetField("totalseconds").SetValue(time, elapsed);
            var runtime = scheduler.GetMethod("GetActiveStage", StaticMethods).Invoke(null, new[] {plan});
            foreach (var pair in new[] {
                new KeyValuePair<string, int>("StageIndex", index), new KeyValuePair<string, int>("StageStartSeconds", start),
                new KeyValuePair<string, int>("StageEndSeconds", end), new KeyValuePair<string, int>("RemainingSeconds", remaining) })
                Equal(pair.Value, (int) runtime.GetType().GetProperty(pair.Key, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime),
                    "scheduler at " + elapsed + "s: " + pair.Key);
        };
        try
        {
            foreach (var weight in new[] {50d, 1d, .25d, .00000025d, double.Epsilon})
            {
                reset();
                var plan = makePlan(weight, 2);
                expect(plan, 0, 0, 0, 50, 50);
                expect(plan, 49, 0, 0, 50, 1);
                expect(plan, 50, 1, 50, 100, 50);
                expect(plan, 75, 1, 50, 100, 25);
                expect(plan, 125, 1, 50, 100, 0);
                planType.GetField("StageLoopStartIndex").SetValue(plan, 1);
                expect(plan, 100, 1, 50, 100, 50);
                expect(plan, 125, 1, 50, 100, 25);
                expect(plan, 150, 1, 50, 100, 50);
                planType.GetField("StageLoopStartIndex").SetValue(plan, 0);
                expect(plan, 100, 0, 0, 50, 50);
                expect(plan, 150, 1, 50, 100, 50);

                reset();
                plan = makePlan(weight, 3);
                var stages = (Array) planType.GetField("Stages").GetValue(plan);
                // An already-met AT step exercises real sequential execution without a controller.
                stageType.GetField("CompletionMode").SetValue(stages.GetValue(1), 1);
                stageType.GetField("TargetAdvancedTrainingPower").SetValue(stages.GetValue(1), 0L);
                stageType.GetField("TargetAdvancedTrainingToughness").SetValue(stages.GetValue(1), 0L);
                expect(plan, 100, 0, 100, 150, 50);
                expect(plan, 149, 0, 100, 150, 1);
                expect(plan, 150, 2, 150, 200, 50);
                expect(plan, 175, 2, 150, 200, 25);
                settingsType.GetProperty("CustomIronPillSaveSeconds").SetValue(settings, 1200);
                settingsType.GetProperty("CustomIronPillMinWorth").SetValue(settings, 500d);
                settingsType.GetProperty("CustomAutoBoostEnabled").SetValue(settings, true);
                expect(plan, 180, 2, 150, 200, 20);
                var reload = Activator.CreateInstance(settingsType, new object[] {null});
                var massUpdate = settingsType.GetMethod("MassUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                massUpdate.Invoke(reload, new[] {settings});
                settingsType.GetProperty("CustomIronPillSaveSeconds").SetValue(reload, 2400);
                massUpdate.Invoke(settings, new[] {reload});
                expect(plan, 180, 2, 150, 200, 20);
                settingsType.GetProperty("UseCustomRebirthPlan").SetValue(settings, true);
                expect(plan, 180, 0, 180, 230, 50);
                expect(plan, 230, 2, 230, 280, 50);
                settingsType.GetProperty("CustomPlanFile").SetValue(settings, "other-" + weight);
                expect(plan, 240, 0, 240, 290, 50);
                settingsType.GetProperty("AutoRebirth").SetValue(settings, !(bool) settingsType.GetProperty("AutoRebirth").GetValue(settings));
                expect(plan, 250, 0, 250, 300, 50);

                reset();
                planType.GetField("StageLoopStartIndex").SetValue(plan, 0);
                expect(plan, 100, 0, 100, 150, 50);
                expect(plan, 150, 2, 150, 200, 50);
                expect(plan, 200, 0, 200, 250, 50);
                expect(plan, 250, 2, 250, 300, 50);
                expect(plan, 300, 0, 300, 350, 50);
                expect(plan, 0, 0, 0, 50, 50);
                settingsType.GetProperty("UseCustomRebirthPlan").SetValue(settings, false);

                plan = makePlan(weight, 2);
                stages = (Array) planType.GetField("Stages").GetValue(plan);
                stageType.GetField("Percent").SetValue(stages.GetValue(1), weight * 3d);
                expect(plan, 24, 0, 0, 25, 1);
                expect(plan, 25, 1, 25, 100, 75);
                planType.GetField("StageLoopStartIndex").SetValue(plan, 1);
                expect(plan, 100, 1, 25, 100, 75);
                expect(plan, 174, 1, 25, 100, 1);
                expect(plan, 175, 1, 25, 100, 75);

                reset();
                plan = makePlan(weight, 3);
                stages = (Array) planType.GetField("Stages").GetValue(plan);
                stageType.GetField("CompletionMode").SetValue(stages.GetValue(1), 1);
                stageType.GetField("Percent").SetValue(stages.GetValue(2), weight * 3d);
                expect(plan, 100, 0, 100, 125, 25);
                expect(plan, 125, 2, 125, 200, 75);
                expect(plan, 199, 2, 125, 200, 1);
            }
            foreach (var weight in new[] {double.NaN, double.PositiveInfinity, double.MaxValue})
            {
                try
                {
                    scheduler.GetMethod("GetActiveStage", StaticMethods).Invoke(null, new[] {makePlan(weight, 2)});
                    throw new Exception("Scheduler accepted a non-finite stage weight/total.");
                }
                catch (TargetInvocationException error)
                {
                    Equal(true, error.InnerException is InvalidOperationException && error.InnerException.Message.Contains("finite"), "invalid stage weights fail clearly");
                }
            }

            reset();
            var mixed = makePlan(1d, 4);
            var mixedStages = (Array) planType.GetField("Stages").GetValue(mixed);
            stageType.GetField("DurationSeconds").SetValue(mixedStages.GetValue(0), 13);
            stageType.GetField("DurationSeconds").SetValue(mixedStages.GetValue(2), 7);
            stageType.GetField("Percent").SetValue(mixedStages.GetValue(3), 3d);
            expect(mixed, 12, 0, 0, 13, 1);
            expect(mixed, 13, 1, 13, 33, 20);
            expect(mixed, 33, 2, 33, 40, 7);
            expect(mixed, 40, 3, 40, 100, 60);
            var snapshotMethod = scheduler.GetMethod("GetSchedulerSnapshot", StaticMethods);
            var snapshot = snapshotMethod.Invoke(null, new object[] {mixed, 70d});
            var snapshotType = snapshot.GetType();
            Equal(3, (int) snapshotType.GetProperty("ActiveStageIndex").GetValue(snapshot), "mixed snapshot uses allocator index");
            Equal(.5d, (double) snapshotType.GetProperty("StageProgress").GetValue(snapshot), "mixed snapshot progress uses effective duration");
            Equal("13,20,7,60", string.Join(",", (IEnumerable<int>) snapshotType.GetProperty("DurationSeconds").GetValue(snapshot)), "fixed durations exact and remaining weights normalized");
            planType.GetField("StageLoopStartIndex").SetValue(mixed, 2);
            expect(mixed, 100, 2, 33, 40, 7);
            expect(mixed, 107, 3, 40, 100, 60);
            expect(mixed, 167, 2, 33, 40, 7);
            snapshot = snapshotMethod.Invoke(null, new object[] {mixed, 167d});
            Equal(2d, (double) snapshotType.GetProperty("Cycle").GetValue(snapshot), "mixed loop cycle from actual loop suffix");

            var allFixed = makePlan(1d, 2);
            var fixedStages = (Array) planType.GetField("Stages").GetValue(allFixed);
            stageType.GetField("DurationSeconds").SetValue(fixedStages.GetValue(0), 11);
            stageType.GetField("DurationSeconds").SetValue(fixedStages.GetValue(1), 23);
            expect(allFixed, 10, 0, 0, 11, 1);
            expect(allFixed, 11, 1, 11, 34, 23);

            reset();
            var sequential = makePlan(1d, 3);
            var sequentialStages = (Array) planType.GetField("Stages").GetValue(sequential);
            stageType.GetField("DurationSeconds").SetValue(sequentialStages.GetValue(0), 20);
            stageType.GetField("CompletionMode").SetValue(sequentialStages.GetValue(1), 1);
            snapshot = snapshotMethod.Invoke(null, new object[] {sequential, 100d});
            Equal(-1, (int) snapshotType.GetProperty("ActiveStageIndex").GetValue(snapshot), "snapshot does not initialize sequential scheduler");
            expect(sequential, 100, 0, 100, 120, 20);
            snapshot = snapshotMethod.Invoke(null, new object[] {sequential, 130d});
            Equal(0, (int) snapshotType.GetProperty("ActiveStageIndex").GetValue(snapshot), "snapshot does not complete or advance sequential steps");
            expect(sequential, 130, 2, 130, 210, 80);
            snapshotMethod.Invoke(null, new object[] {sequential, 0d});
            expect(sequential, 140, 2, 130, 210, 70);
            Equal(130d, (double) scheduler.GetField("_sequentialStageStartedAt", StaticMethods).GetValue(null), "snapshot of earlier epoch does not reset actual timer");
            var fractional = makePlan(1d, 4);
            var fractionalStages = (Array) planType.GetField("Stages").GetValue(fractional);
            stageType.GetField("CompletionMode").SetValue(fractionalStages.GetValue(1), 1);
            stageType.GetField("DurationSeconds").SetValue(fractionalStages.GetValue(0), 1);
            snapshot = snapshotMethod.Invoke(null, new object[] {fractional, 0d});
            var fractionalDurations = (IEnumerable<int>) snapshotType.GetProperty("DurationSeconds").GetValue(snapshot);
            Equal("1,0,49,50", string.Join(",", fractionalDurations), "mixed sequential weighted timed remainder conserves odd-second budget; AT has no clock duration");
            reset();
            var atPlan = makePlan(1d, 3);
            var atStages = (Array) planType.GetField("Stages").GetValue(atPlan);
            stageType.GetField("CompletionMode").SetValue(atStages.GetValue(1), 1);
            stageType.GetField("TargetAdvancedTrainingPower").SetValue(atStages.GetValue(1), 1L);
            expect(atPlan, 100, 0, 100, 150, 50);
            expect(atPlan, 150, 1, 150, 150, 1);
            snapshot = snapshotMethod.Invoke(null, new object[] {atPlan, 500d});
            Equal(true, snapshotType.GetProperty("StageProgress").GetValue(snapshot) == null, "AT completion has no fabricated time progress");
            Equal(1, (int) snapshotType.GetProperty("ActiveStageIndex").GetValue(snapshot), "read-only AT snapshot stays on actual unmet target step");

            var progressPlan = makePlan(1d, 2);
            stageType.Assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true).GetField("SourceJson", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(progressPlan, Activator.CreateInstance(_assembly.GetType("SimpleJSON.JSONObject", true)));
            settingsType.GetProperty("UseCustomRebirthPlan").SetValue(settings, true);
            var name = (string) settingsType.GetProperty("CustomPlanFile").GetValue(settings);
            mainType.GetField("_customRuntimePlanCache", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, progressPlan);
            mainType.GetField("_customRuntimePlanCacheName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, name);
            mainType.GetField("_customRuntimePlanCacheLoadedAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, DateTime.Now);
            var storeForProgress = _assembly.GetType("NGUInjector.CustomPlans.CustomPlanStore", true);
            mainType.GetField("CustomPlanStore", StaticMethods).SetValue(null, FormatterServices.GetUninitializedObject(storeForProgress));
            time.GetType().GetField("totalseconds").SetValue(time, 25d);
            var getProgress = mainType.GetMethod("GetCustomPlanProgress", BindingFlags.Instance | BindingFlags.NonPublic);
            Func<string, string> progressValue = field => {
                var json = getProgress.Invoke(main, null);
                var node = json.GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(json, new object[] {field});
                return (string) node.GetType().GetProperty("Value").GetValue(node);
            };
            Equal("running", progressValue("status"), "root progress running state");
            Equal("0.5", progressValue("stageProgress"), "root progress shares effective stage duration");
            Equal(name, progressValue("planName"), "progress navigation uses selected filename rather than differing stored plan Name");
            Func<int, string, string> progressStageValue = (index, field) => {
                var json = getProgress.Invoke(main, null);
                var stages = json.GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(json, new object[] {"stages"});
                var stage = stages.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(stages, new object[] {index});
                return stage.GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(stage, new object[] {field}).ToString();
            };
            Equal("[]", progressStageValue(0, "configuredEnergyPriorities"), "missing energy overrides publish an empty configured chain");
            Equal("[]", progressStageValue(0, "configuredMagicPriorities"), "missing magic overrides publish an empty configured chain");
            var progressStages = (Array) planType.GetField("Stages").GetValue(progressPlan);
            var progressStage = progressStages.GetValue(0);
            stageType.GetField("EnergyFocus").SetValue(progressStage, 1);
            stageType.GetField("MagicFocus").SetValue(progressStage, 1);
            Equal("[]", progressStageValue(0, "configuredEnergyPriorities"), "focus defaults remain empty configured overrides");
            Equal(false, progressStageValue(0, "energyPriorities") == "[]", "effective energy priorities expand focus defaults separately");
            stageType.GetField("EnergyPriorities").SetValue(progressStage, new[] {"WAN", "NGU", "TM"});
            stageType.GetField("MagicPriorities").SetValue(progressStage, new[] {"RIT", "NGU"});
            Equal("[\"WAN\",\"NGU\",\"TM\"]", progressStageValue(0, "configuredEnergyPriorities"), "configured energy overrides preserve exact order");
            Equal("[\"RIT\",\"NGU\"]", progressStageValue(0, "configuredMagicPriorities"), "configured magic overrides preserve exact order");
            settingsType.GetProperty("UseCustomRebirthPlan").SetValue(settings, false);
            // Run-state changes invalidate the cache; reattach only the data fixture, not a timer.
            mainType.GetField("_customRuntimePlanCache", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, progressPlan);
            mainType.GetField("_customRuntimePlanCacheName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, name);
            mainType.GetField("_customRuntimePlanCacheLoadedAt", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, DateTime.Now);
            Equal("paused", progressValue("status"), "root progress never claims paused plan running");
            Equal("null", progressValue("stageProgress"), "paused progress is unknown, not rebirth-clock animation");
            var focusType = _assembly.GetType("NGUInjector.CustomFocusGoal", true);
            mainType.GetField("_customRequestedFocusGoal", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, Enum.Parse(focusType, "EnergyNgu"));
            Equal("interrupted", progressValue("status"), "Quick focus interrupts root progress");
            Equal(true, progressValue("reason").Contains("EnergyNgu"), "Quick interruption identifies requested goal");
            mainType.GetField("_customRequestedFocusGoal", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, null);
            planType.GetField("StageSchedulerEnabled").SetValue(progressPlan, false);
            Equal("disabled", progressValue("status"), "disabled scheduler root state");
            mainType.GetField("CustomPlanStore", StaticMethods).SetValue(null, null);
            Equal("unavailable", progressValue("status"), "missing store root state");

            var plansDir = Path.Combine(Path.GetTempPath(), "ngu-stage-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                var storeType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlanStore", true);
                var store = Activator.CreateInstance(storeType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] {plansDir}, null);
                var save = storeType.GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic);
                var plan = makePlan(.25d, 3);
                var stages = (Array) planType.GetField("Stages").GetValue(plan);
                stageType.GetField("CompletionMode").SetValue(stages.GetValue(1), 1);
                settingsType.GetProperty("CustomPlanFile").SetValue(settings, "runtime-check");
                save.Invoke(store, new[] {"runtime-check", plan});
                expect(plan, 100, 0, 100, 150, 50);
                expect(plan, 150, 2, 150, 200, 50);
                save.Invoke(store, new[] {"runtime-check", plan});
                expect(plan, 175, 2, 150, 200, 25);
                save.Invoke(store, new[] {"unselected-check", plan});
                expect(plan, 180, 2, 150, 200, 20);
                planType.GetField("AutoRebirthEnabled").SetValue(plan, true);
                save.Invoke(store, new[] {"runtime-check", plan});
                expect(plan, 190, 2, 150, 200, 10);

                mainType.GetField("CustomPlanStore", StaticMethods).SetValue(null, store);
                settingsType.GetProperty("UseCustomRebirthPlan").SetValue(settings, true);
                plan = makePlan(1d, 3);
                stages = (Array) planType.GetField("Stages").GetValue(plan);
                stageType.GetField("DurationSeconds").SetValue(stages.GetValue(0), 20);
                stageType.GetField("DurationUnit").SetValue(stages.GetValue(0), "s");
                stageType.GetField("CompletionMode").SetValue(stages.GetValue(1), 1);
                stageType.GetField("DurationSeconds").SetValue(stages.GetValue(2), 40);
                stageType.GetField("DurationUnit").SetValue(stages.GetValue(2), "s");
                save.Invoke(store, new[] {"runtime-check", plan});
                Equal(60, (int) planType.GetField("StageDurationSeconds").GetValue(plan), "all timed fixed rows normalize budget without counting AT");
                reset();
                Func<object> selected = () => mainType.GetMethod("LoadRuntimeCustomPlan", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(main, null);
                Func<object, int, object> row = (model, index) => ((Array) planType.GetField("Stages").GetValue(model)).GetValue(index);
                var sourceField = planType.GetField("SourceJson", BindingFlags.Instance | BindingFlags.NonPublic);
                var jsonType = _assembly.GetType("SimpleJSON.JSON", true);
                Action<object> saveCas = model => {
                    var baseline = storeType.GetMethod("ReadExternalPlan", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, new object[] {"runtime-check"});
                    var token = baseline.GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(baseline, new object[] {"__externalUiRevision"});
                    var revision = (string) token.GetType().GetProperty("Value").GetValue(token);
                    var text = storeType.GetMethod("SerializePlan", StaticMethods).Invoke(null, new[] {model});
                    var json = jsonType.GetMethod("Parse", StaticMethods).Invoke(null, new[] {text});
                    storeType.GetMethod("SaveExternalPlan", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store, new[] {"runtime-check", json, revision});
                };
                expect(selected(), 0, 0, 0, 20, 20);
                expect(selected(), 20, 2, 20, 60, 40);
                expect(selected(), 30, 2, 20, 60, 30);
                var edits = new KeyValuePair<string, Action<object>>[] {
                    new KeyValuePair<string, Action<object>>("active stage label only", model => stageType.GetField("Name").SetValue(row(model, 2), "Energy NGU")),
                    new KeyValuePair<string, Action<object>>("AT stage label only", model => stageType.GetField("Name").SetValue(row(model, 1), "AT Adventure")),
                    new KeyValuePair<string, Action<object>>("display unit", model => stageType.GetField("DurationUnit").SetValue(row(model, 2), "min")),
                    new KeyValuePair<string, Action<object>>("NGU weights", model => planType.GetField("EnergyNguWeights").SetValue(model, new[] {0, 0, 0, 0, 2, 0, 1, 0, 0})),
                    new KeyValuePair<string, Action<object>>("rebirth trigger", model => planType.GetField("TimeTriggerSeconds").SetValue(model, 5000)),
                    new KeyValuePair<string, Action<object>>("stage gear", model => stageType.GetField("GearGoal").SetValue(row(model, 2), 2)),
                    new KeyValuePair<string, Action<object>>("stage focus", model => stageType.GetField("EnergyFocus").SetValue(row(model, 2), 5)),
                    new KeyValuePair<string, Action<object>>("stage priorities", model => {
                        stageType.GetField("EnergyPriorities").SetValue(row(model, 2), new[] {"NGU", "TM"});
                        stageType.GetField("MagicPriorities").SetValue(row(model, 2), new[] {"NGU", "RIT"});
                    }),
                    new KeyValuePair<string, Action<object>>("preset label and allocation", model => {
                        var stage = row(model, 2);
                        stageType.GetField("Name").SetValue(stage, "Wandoos");
                        stageType.GetField("EnergyFocus").SetValue(stage, 3);
                        stageType.GetField("MagicFocus").SetValue(stage, 3);
                        stageType.GetField("GearGoal").SetValue(stage, 12);
                        stageType.GetField("EnergyPriorities").SetValue(stage, new string[0]);
                        stageType.GetField("MagicPriorities").SetValue(stage, new string[0]);
                    }),
                    new KeyValuePair<string, Action<object>>("source extension", model => {
                        var source = sourceField.GetValue(model);
                        source.GetType().GetProperty("Item", new[] {typeof(string)}).SetValue(source,
                            Activator.CreateInstance(_assembly.GetType("SimpleJSON.JSONString", true), new object[] {"keep"}), new object[] {"futureScheduleExtension"});
                    }),
                    new KeyValuePair<string, Action<object>>("dormant AT duration", model => stageType.GetField("DurationSeconds").SetValue(row(model, 1), int.MaxValue)),
                    new KeyValuePair<string, Action<object>>("dormant AT weight", model => stageType.GetField("Percent").SetValue(row(model, 1), double.MaxValue))
                };
                var editElapsed = 31;
                foreach (var edit in edits)
                {
                    plan = selected();
                    edit.Value(plan);
                    mainType.GetField("_customStageGearDone", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, true);
                    saveCas(plan);
                    Equal(true, mainType.GetField("_customRuntimePlanCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main) == null, edit.Key + " invalidates the actual plan cache");
                    Equal(false, (bool) mainType.GetField("_customStageGearDone", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main), edit.Key + " invalidates gear state independently of the clock");
                    Equal(20d, (double) scheduler.GetField("_sequentialStageStartedAt", StaticMethods).GetValue(null), edit.Key + " Save/CAS preserves the stage start before evaluation");
                    var reloaded = selected();
                    snapshot = snapshotMethod.Invoke(null, new object[] {reloaded, (double) editElapsed});
                    Equal(2, (int) snapshotType.GetProperty("ActiveStageIndex").GetValue(snapshot), edit.Key + " snapshot retains the active stage before evaluation");
                    Equal((double) editElapsed - 20d, (double) snapshotType.GetProperty("StageElapsedSeconds").GetValue(snapshot), edit.Key + " snapshot retains elapsed stage time");
                    expect(reloaded, editElapsed, 2, 20, 60, 60 - editElapsed);
                    editElapsed++;
                }
                plan = selected();
                var extension = sourceField.GetValue(plan).GetType().GetProperty("Item", new[] {typeof(string)}).GetValue(sourceField.GetValue(plan), new object[] {"futureScheduleExtension"});
                Equal("keep", (string) extension.GetType().GetProperty("Value").GetValue(extension), "non-scheduling CAS edits retain source extensions");
                snapshot = snapshotMethod.Invoke(null, new object[] {plan, 43d});
                Equal("20,0,40", string.Join(",", (IEnumerable<int>) snapshotType.GetProperty("DurationSeconds").GetValue(snapshot)), "dormant AT duration and weight do not change timed rows");

                stageType.GetField("DurationSeconds").SetValue(row(plan, 2), 60);
                saveCas(plan);
                snapshot = snapshotMethod.Invoke(null, new object[] {selected(), 45d});
                Equal(-1, (int) snapshotType.GetProperty("ActiveStageIndex").GetValue(snapshot), "changed schedule snapshot waits for the allocator rather than resetting its clock");
                Equal(20d, (double) scheduler.GetField("_sequentialStageStartedAt", StaticMethods).GetValue(null), "effective duration edit does not reset during Save or snapshot");
                expect(selected(), 45, 0, 45, 65, 20);
                plan = selected();
                stages = (Array) planType.GetField("Stages").GetValue(plan);
                var first = stages.GetValue(0);
                stages.SetValue(stages.GetValue(2), 0); stages.SetValue(first, 2);
                saveCas(plan);
                expect(selected(), 50, 0, 50, 110, 60);
                plan = selected();
                stageType.GetField("TargetAdvancedTrainingPower").SetValue(row(plan, 1), 2L);
                saveCas(plan);
                expect(selected(), 55, 0, 55, 115, 60);
                plan = selected();
                planType.GetField("StageLoopStartIndex").SetValue(plan, 0);
                saveCas(plan);
                expect(selected(), 60, 0, 60, 120, 60);
                plan = selected();
                stageType.GetField("TargetAdvancedTrainingToughness").SetValue(row(plan, 1), 3L);
                saveCas(plan);
                expect(selected(), 65, 0, 65, 125, 60);
                plan = selected();
                stageType.GetField("CompletionMode").SetValue(row(plan, 2), 1);
                saveCas(plan);
                expect(selected(), 70, 0, 70, 130, 60);
                plan = selected();
                planType.GetField("Name").SetValue(plan, "other-runtime-check");
                expect(plan, 75, 0, 75, 135, 60);

                plan = makePlan(.25d, 3);
                stageType.GetField("CompletionMode").SetValue(row(plan, 1), 1);
                stageType.GetField("EnergyFocus").SetValue(row(plan, 2), 5);
                saveCas(plan);
                expect(selected(), 1000, 0, 1000, 1050, 50);
                expect(selected(), 1050, 2, 1050, 1100, 50);
                expect(selected(), 1060, 2, 1050, 1100, 40);
                plan = selected();
                planType.GetField("StageDurationSeconds").SetValue(plan, 200);
                saveCas(plan);
                expect(selected(), 1070, 0, 1070, 1170, 100);
                plan = selected();
                stageType.GetField("Percent").SetValue(row(plan, 0), 1d);
                stageType.GetField("Percent").SetValue(row(plan, 2), 1d);
                saveCas(plan);
                expect(selected(), 1080, 0, 1070, 1170, 90);
                expect(selected(), 1170, 2, 1170, 1270, 100);
                plan = selected();
                stages = (Array) planType.GetField("Stages").GetValue(plan);
                first = stages.GetValue(0);
                stages.SetValue(stages.GetValue(2), 0); stages.SetValue(first, 2);
                saveCas(plan);
                plan = selected();
                Equal("stage-0", (string) stageType.GetField("Name").GetValue(row(plan, 2)), "equal-signature reorder reloads the moved row at the active index");
                // Labels and allocations differ, but the ordered scheduling signature is unchanged.
                expect(plan, 1180, 2, 1170, 1270, 90);

                plan = makePlan(double.MaxValue, 2);
                planType.GetField("StageDurationSeconds").SetValue(plan, 1);
                foreach (var stage in (Array) planType.GetField("Stages").GetValue(plan))
                {
                    stageType.GetField("CompletionMode").SetValue(stage, 1);
                    stageType.GetField("DurationSeconds").SetValue(stage, int.MaxValue);
                    stageType.GetField("TargetAdvancedTrainingPower").SetValue(stage, 100L);
                }
                saveCas(plan);
                expect(selected(), 1200, 0, 1200, 1200, 1);
                snapshot = snapshotMethod.Invoke(null, new object[] {selected(), 1200d});
                Equal("0,0", string.Join(",", (IEnumerable<int>) snapshotType.GetProperty("DurationSeconds").GetValue(snapshot)), "all-AT schedule has no timed clock despite large dormant fields");
                Equal(true, snapshotType.GetProperty("StageProgress").GetValue(snapshot) == null, "all-AT progress remains target-driven and nullable");
                plan = selected();
                planType.GetField("StageDurationSeconds").SetValue(plan, int.MaxValue);
                saveCas(plan);
                expect(selected(), 1201, 0, 1200, 1200, 1);
                plan = selected();
                stageType.GetField("CompletionMode").SetValue(row(plan, 0), 0);
                stageType.GetField("CompletionMode").SetValue(row(plan, 1), 0);
                stageType.GetField("DurationSeconds").SetValue(row(plan, 0), 10);
                stageType.GetField("DurationSeconds").SetValue(row(plan, 1), 10);
                saveCas(plan);
                expect(selected(), 1202, 1, 10, 20, 0);
                plan = selected();
                stageType.GetField("CompletionMode").SetValue(row(plan, 0), 1);
                stageType.GetField("CompletionMode").SetValue(row(plan, 1), 1);
                saveCas(plan);
                expect(selected(), 1203, 0, 1203, 1203, 1);
            }
            finally
            {
                if (Directory.Exists(plansDir)) Directory.Delete(plansDir, true);
            }
        }
        finally
        {
            reset();
            characterField.SetValue(null, null);
            mainType.GetField("Settings", StaticMethods).SetValue(null, null);
            mainType.GetField("reference", StaticMethods).SetValue(null, null);
            mainType.GetField("CustomPlanStore", StaticMethods).SetValue(null, null);
        }
    }

    private static void Equal<T>(T expected, T actual, string scenario)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(scenario + ": expected " + expected + ", got " + actual);
    }

    private static long[] Allocate(long budget, params long[] caps)
    {
        var allocations = new long[caps.Length];
        var targets = caps.Select((cap, index) => new KeyValuePair<long, Action<long>>(cap, amount => allocations[index] += amount)).ToArray();
        var distributed = (long)Call("AllocateInOrder", targets, budget);
        Equal(allocations.Sum(), distributed, "resource conservation");
        Equal(true, distributed <= Math.Max(0, budget), "cannot spend more than available");
        return allocations;
    }

    private static void CheckOrderedAllocation()
    {
        Equal("100,1000000,500", string.Join(",", Allocate(1000600, 100, 1000000, 5000)), "Wandoos -> NGU -> TM spillover");
        Equal("100,900,0", string.Join(",", Allocate(1000, 100, 1000000, 5000)), "uncapped high priority gets all remainder");
        Equal("100,1000100,400", string.Join(",", Allocate(1000600, 100, 1000100, 5000)), "growing NGU cap reclaims lower-priority allocation on next pass");
        Equal("10,20,0", string.Join(",", Allocate(1000, 10, 20, 0)), "all capped leaves excess idle");
        Equal("0,20,5", string.Join(",", Allocate(25, -10, 20, 10)), "completed or overcapped target does not consume resources");
        Equal("1,1,1", string.Join(",", Allocate(3, 1, 1, 1)), "small allocations have no equal-split remainder loss");
        Equal("0,0", string.Join(",", Allocate(0, 10, 10)), "empty resource pool");
        Equal("" + long.MaxValue + ",0", string.Join(",", Allocate(long.MaxValue, long.MaxValue, 1)), "long budget boundary");

        var visited = 0;
        var lazyTargets = CountedTargets(() => visited++);
        Equal(10L, (long)Call("AllocateInOrder", lazyTargets, 10L), "lazy allocation spends budget");
        Equal(1, visited, "exhausted budget never evaluates lower-priority targets");
    }

    private static IEnumerable<KeyValuePair<long, Action<long>>> CountedTargets(Action count)
    {
        for (var i = 0; i < 3; i++)
        {
            count();
            yield return new KeyValuePair<long, Action<long>>(10, amount => { });
        }
    }

    private static void CheckWeightedSharing()
    {
        Action<long, long[], int[], long[]> shares = (budget, caps, weights, expected) => {
            var capsBefore = caps.ToArray();
            var weightsBefore = weights.ToArray();
            var actual = (long[]) Call("ShareCappedBudget", caps, budget, weights);
            Equal(string.Join(",", expected), string.Join(",", actual), "weighted capped shares for budget " + budget);
            Equal(caps.Length, actual.Length, "public sharing helper accepts arbitrary pure-test counts");
            var remaining = Math.Max(0, budget);
            var unspendable = remaining;
            for (var i = 0; i < actual.Length; i++)
            {
                Equal(true, actual[i] >= 0 && actual[i] <= Math.Max(0, caps[i]) && (weights[i] > 0 || actual[i] == 0), "weighted cap and exclusion at " + i);
                Equal(true, actual[i] <= remaining, "exact conservation without overflowing a long sum");
                remaining -= actual[i];
                if (weights[i] > 0) unspendable -= Math.Min(unspendable, Math.Max(0, caps[i]));
            }
            Equal(unspendable, remaining, "all available resources or all eligible caps are spent exactly");
            Equal(true, caps.SequenceEqual(capsBefore) && weights.SequenceEqual(weightsBefore), "weighted sharing leaves inputs unchanged");
        };
        shares(1000, new[] {10000L, 10000L}, new[] {2, 1}, new[] {667L, 333L});
        shares(1, new[] {100L, 100L}, new[] {1, 2}, new[] {0L, 1L});
        shares(2, new[] {100L, 100L, 100L}, new[] {1, 1, 1}, new[] {1L, 1L, 0L});
        shares(2, new[] {100L, 100L, 100L}, new[] {1, 3, 2}, new[] {0L, 1L, 1L});
        shares(1000, new[] {100L, 1000L}, new[] {2, 1}, new[] {100L, 900L});
        shares(100, new[] {1L, 2L, 30L, 1000L}, new[] {1, 2, 3, 4}, new[] {1L, 2L, 30L, 67L});
        shares(1000, new[] {0L, -1L, 2000L}, new[] {1, 1, 2}, new[] {0L, 0L, 1000L});
        shares(1000, new[] {100L, 100L, 2000L}, new[] {0, -1, 2}, new[] {0L, 0L, 1000L});
        shares(1000, new[] {100L, 100L}, new[] {0, 0}, new[] {0L, 0L});
        shares(-1, new[] {100L, 100L}, new[] {2, 1}, new[] {0L, 0L});
        shares(long.MinValue, new[] {long.MaxValue}, new[] {int.MaxValue}, new[] {0L});
        shares(1, new long[0], new int[0], new long[0]);
        shares(long.MaxValue, new[] {long.MaxValue, long.MaxValue}, new[] {int.MaxValue, int.MaxValue}, new[] {4611686018427387904L, 4611686018427387903L});
        shares(long.MaxValue, new[] {long.MaxValue, long.MaxValue}, new[] {2, 1}, new[] {6148914691236517205L, 3074457345618258602L});
        shares(long.MaxValue, new[] {1L, long.MaxValue, long.MaxValue}, new[] {int.MaxValue, int.MaxValue, int.MaxValue}, new[] {1L, 4611686018427387903L, 4611686018427387903L});
    }

    private static void CheckProjection()
    {
        var projected = (double)Call("ProjectedNextLevel", 9999L, 0L, 5);
        Equal(10251d, projected, "five seconds at 50 levels/sec plus phase guard");
        Equal(10020d, (double)Call("ProjectedNextLevel", 9999L, 10020L, 5), "buffer stops at positive level target");
        Equal(10000d, (double)Call("ProjectedNextLevel", 9999L, 10000L, 5), "last required upgrade remains funded");
        Equal(true, (double)Call("ProjectedNextLevel", 1L, 0L, int.MaxValue) > int.MaxValue, "lookahead arithmetic does not overflow int");
        var reserved = (long)Call("CapToLong", projected * 100d * 1.000002d);
        for (var tick = 0; tick <= 250; tick++)
            Equal(true, reserved >= (10000d + tick) * 100d, "linear cap stays black-barred at tick " + tick);
    }

    private static void CheckRounding()
    {
        Equal(2L, (long)Call("CapToLong", 1.5d), "fractional cap always rounds up");
        Equal(1L, (long)Call("CapToLong", 0.01d), "small positive requirement needs one resource");
        Equal(0L, (long)Call("CapToLong", double.NaN), "invalid cap is not allocated");
        Equal(long.MaxValue, (long)Call("CapToLong", double.PositiveInfinity), "unreachable cap saturates");
        Equal(2000000000000000000L, (long)Call("CapToLong", 2e18), "real cap above game's hard cap is not clipped");
        Equal(0L, (long)Call("CapDelta", 1.5d, 2L), "already allocated exact cap");
        Equal(-98L, (long)Call("CapDelta", 1.5d, 100L), "flat cap identifies excess allocation");
    }

    private static void CheckTokens()
    {
        foreach (var token in new[] {"WAN", "TM", "NGU", "NGU-8", "AUG", "AUG-13", "AT", "AT-4"})
            Equal(true, (bool)Call("IsValidPriority", token, true), "valid energy token " + token);
        foreach (var token in new[] {"WAN", "TM", "NGU", "NGU-6", "RIT", "RIT-7"})
            Equal(true, (bool)Call("IsValidPriority", token, false), "valid magic token " + token);
        foreach (var token in new[] {"NGU-9", "NGU--1", "NGU-01", "NGU-+1", "AUG-14", "AT-5", "RIT", "WISH", ""})
            Equal(false, (bool)Call("IsValidPriority", token, true), "invalid energy token " + token);
        foreach (var token in new[] {"NGU-7", "AUG", "AT", "RIT-8"})
            Equal(false, (bool)Call("IsValidPriority", token, false), "invalid magic token " + token);
        var focus = _assembly.GetType("NGUInjector.CustomPlans.CustomStageFocus", true);
        Equal("WAN,NGU,TM,AT", string.Join(",", (string[])Call("DefaultPriorities", Enum.Parse(focus, "Wandoos"), true)), "Wandoos default order");
        Equal("", string.Join(",", (string[])Call("DefaultPriorities", Enum.Parse(focus, "None"), true)), "None does not manage resources");
        var planType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true);
        var plan = Activator.CreateInstance(planType);
        Equal(16, (int) planType.GetField("Version").GetValue(plan), "new weighted plan version");
        Equal("0,0,0,0,1,0,1,0,0", string.Join(",", (int[]) planType.GetField("EnergyNguWeights").GetValue(plan)), "default energy lane weights");
        Equal("1,0,0,0,0,0,0", string.Join(",", (int[]) planType.GetField("MagicNguWeights").GetValue(plan)), "default magic lane weights select Yggdrasil");
        Equal(true, planType.GetField("NguGoal") == null && _assembly.GetType("NGUInjector.CustomPlans.CustomNguGoal", false) == null,
            "legacy enum and field are absent from the runtime contract");
        Equal("NGU,WAN,TM,AT", string.Join(",", (string[])Call("DefaultPriorities", Enum.Parse(focus, "EnergyNgu"), true)), "Energy NGU never routes primary resources to augmentation feature");
    }

    private static void CheckFightOnEntry()
    {
        var mainType = _assembly.GetType("NGUInjector.Main", true);
        var planType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true);
        var stageType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
        var loadouts = _assembly.GetType("NGUInjector.Managers.LoadoutManager", true);
        var diggers = _assembly.GetType("NGUInjector.Managers.DiggerManager", true);
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var characterField = mainType.GetField("Character", StaticMethods);
        var settingsField = mainType.GetField("Settings", StaticMethods);
        var referenceField = mainType.GetField("reference", StaticMethods);
        var oldCharacter = characterField.GetValue(null);
        var oldSettings = settingsField.GetValue(null);
        var oldReference = referenceField.GetValue(null);
        var loadoutLock = loadouts.GetProperty("CurrentLock", StaticMethods);
        var diggerLock = diggers.GetProperty("CurrentLock", StaticMethods);
        var oldLoadoutLock = loadoutLock.GetValue(null);
        var oldDiggerLock = diggerLock.GetValue(null);
        var none = Enum.Parse(loadoutLock.PropertyType, "None");
        var gold = Enum.Parse(loadoutLock.PropertyType, "Gold");
        var main = FormatterServices.GetUninitializedObject(mainType);
        var character = FormatterServices.GetUninitializedObject(characterField.FieldType);
        var timeField = character.GetType().GetField("rebirthTime");
        var time = Activator.CreateInstance(timeField.FieldType);
        timeField.SetValue(character, time);
        var settings = Activator.CreateInstance(settingsField.FieldType, new object[] {null});
        var plan = Activator.CreateInstance(planType);
        planType.GetField("AutoActivateFruits").SetValue(plan, false);
        planType.GetField("AutoRebirthEnabled").SetValue(plan, true);
        planType.GetField("StageDurationSeconds").SetValue(plan, 100);
        planType.GetField("StageLoopStartIndex").SetValue(plan, 0);
        var stage = Activator.CreateInstance(stageType);
        stageType.GetField("GearGoal").SetValue(stage, 5); // Actual stage goal is TM, not Fight.
        var rows = Array.CreateInstance(stageType, 1); rows.SetValue(stage, 0);
        planType.GetField("Stages").SetValue(plan, rows);
        var run = mainType.GetMethod("RunCustomPlanAutomation", fields);
        var stop = mainType.GetMethod("StopBossPush", fields);
        var fightKey = mainType.GetField("_customStageFightKey", fields);
        var gearDone = mainType.GetField("_customStageGearDone", fields);
        Func<bool> pushing = () => (bool) mainType.GetProperty("BossPushRunning", fields).GetValue(main);
        try
        {
            characterField.SetValue(null, character); settingsField.SetValue(null, settings); referenceField.SetValue(null, main);
            settingsField.FieldType.GetProperty("UseCustomRebirthPlan").SetValue(settings, true);
            settingsField.FieldType.GetProperty("AutoRebirth").SetValue(settings, true);
            loadoutLock.SetValue(null, gold); diggerLock.SetValue(null, none);
            run.Invoke(main, new[] {plan});
            Equal(false, pushing(), "unflagged stage never starts a boss push");
            stageType.GetField("FightOnEntry").SetValue(stage, true);
            run.Invoke(main, new[] {plan});
            Equal(true, fightKey.GetValue(main) == null && !pushing(), "gear lock defers fight without consuming entry");
            loadoutLock.SetValue(null, none); diggerLock.SetValue(null, gold);
            run.Invoke(main, new[] {plan});
            Equal(true, fightKey.GetValue(main) == null && !pushing(), "digger lock also defers entire prep");
            diggerLock.SetValue(null, none);
            foreach (var blocker in new[] {"_manualRebirthPending", "_customPreRebirthPrepRunning", "_bossPushRunning"})
            {
                mainType.GetField(blocker, fields).SetValue(main, true);
                run.Invoke(main, new[] {plan});
                Equal(true, fightKey.GetValue(main) == null, blocker + " blocks stage fight entry");
                mainType.GetField(blocker, fields).SetValue(main, false);
            }
            gearDone.SetValue(main, true);
            // Controllers/inventory are deliberately absent: real prep catches those boundary
            // failures, while its real boss-push start and stage-entry coordination still run.
            run.Invoke(main, new[] {plan});
            Equal(true, pushing(), "flagged entry starts existing boss push despite unavailable prep boundaries");
            Equal(false, (bool) gearDone.GetValue(main), "fight invalidates even previously completed stage gear");
            var prepStatus = (string) mainType.GetMethod("GetCustomAutomationStatus", StaticMethods).Invoke(null, null);
            Equal(false, prepStatus.Contains("upgrade diggers") || prepStatus.Contains("consume Fruit of Power"),
                "stage prep omits rebirth-only upgrades and power-fruit steps");
            var firstKey = (string) fightKey.GetValue(main);
            Equal(true, !string.IsNullOrEmpty(firstKey), "attempt records stage entry");
            run.Invoke(main, new[] {plan});
            Equal(firstKey, (string) fightKey.GetValue(main), "running push cannot consume another entry");
            stop.Invoke(main, null);
            mainType.GetMethod("InvalidateCustomRuntimePlanCache", fields).Invoke(main, new object[] {false});
            loadoutLock.SetValue(null, gold);
            run.Invoke(main, new[] {plan});
            Equal(false, pushing(), "cache reload does not repeat a completed stage fight");
            var status = (string) mainType.GetMethod("GetCustomAutomationStatus", StaticMethods).Invoke(null, null);
            Equal(true, status.Contains("waiting for loadout lock") && !status.Contains("fight on entry waiting"),
                "after push, actual stage gear is pending rather than skipped or fighting again");
            loadoutLock.SetValue(null, none);
            time.GetType().GetField("totalseconds").SetValue(time, 100d);
            run.Invoke(main, new[] {plan});
            Equal(true, pushing() && firstKey != (string) fightKey.GetValue(main), "single-stage loop fights on its next entry");
            stop.Invoke(main, null);
            var twoRows = Array.CreateInstance(stageType, 2); twoRows.SetValue(stage, 0); twoRows.SetValue(stage, 1);
            planType.GetField("Stages").SetValue(plan, twoRows);
            time.GetType().GetField("totalseconds").SetValue(time, 150d);
            run.Invoke(main, new[] {plan});
            Equal(true, pushing(), "later flagged stage has its own entry fight");
            stop.Invoke(main, null);
            mainType.GetMethod("ResetCustomAutomationRuntime", fields).Invoke(main, null);
            Equal(true, fightKey.GetValue(main) == null, "rebirth reset clears fight attempt");
            time.GetType().GetField("totalseconds").SetValue(time, 0d);
            run.Invoke(main, new[] {plan});
            Equal(true, pushing(), "same stage fights again in new rebirth");
            Equal(true, (bool) settingsField.FieldType.GetProperty("UseCustomRebirthPlan").GetValue(settings)
                && (bool) settingsField.FieldType.GetProperty("AutoRebirth").GetValue(settings)
                && (bool) planType.GetField("AutoRebirthEnabled").GetValue(plan), "entry fights preserve all plan/rebirth switches");
            Equal(false, (bool) mainType.GetField("_manualRebirthPending", fields).GetValue(main), "entry fight never queues a manual rebirth");
        }
        finally
        {
            stop.Invoke(main, null);
            loadoutLock.SetValue(null, oldLoadoutLock); diggerLock.SetValue(null, oldDiggerLock);
            characterField.SetValue(null, oldCharacter); settingsField.SetValue(null, oldSettings); referenceField.SetValue(null, oldReference);
            _assembly.GetType("NGUInjector.Managers.CustomStageAllocator", true).GetMethod("ResetStageRuntime", StaticMethods).Invoke(null, null);
        }
    }

    private static void CheckSavedStages()
    {
        var stageType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlanStage", true);
        var storeType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlanStore", true);
        var jsonType = _assembly.GetType("SimpleJSON.JSON", true);
        var stage = Activator.CreateInstance(stageType);
        stageType.GetField("EnergyPriorities").SetValue(stage, new[] {"WAN", "NGU-4", "TM"});
        stageType.GetField("MagicPriorities").SetValue(stage, new[] {"RIT-0", "WAN"});
        stageType.GetField("BlackBarLookaheadSeconds").SetValue(stage, 7);
        stageType.GetField("DurationSeconds").SetValue(stage, 61);
        stageType.GetField("DurationUnit").SetValue(stage, "min");
        stageType.GetField("FightOnEntry").SetValue(stage, true);
        var array = Array.CreateInstance(stageType, 1);
        array.SetValue(stage, 0);
        var serialized = storeType.GetMethod("SerializeStages", StaticMethods).Invoke(null, new object[] {array});
        var node = serialized.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(serialized, new object[] {0});
        var restored = storeType.GetMethod("DeserializeStage", StaticMethods).Invoke(null, new[] {node});
        Equal("WAN,NGU-4,TM", string.Join(",", (string[])stageType.GetField("EnergyPriorities").GetValue(restored)), "saved energy order round trip");
        Equal("RIT-0,WAN", string.Join(",", (string[])stageType.GetField("MagicPriorities").GetValue(restored)), "saved magic order round trip");
        Equal(7, (int)stageType.GetField("BlackBarLookaheadSeconds").GetValue(restored), "saved lookahead round trip");
        Equal(61, (int)stageType.GetField("DurationSeconds").GetValue(restored), "saved fixed seconds round trip");
        Equal("min", (string)stageType.GetField("DurationUnit").GetValue(restored), "saved display unit round trip");
        Equal(true, (bool)stageType.GetField("FightOnEntry").GetValue(restored), "saved fight opt-in round trip");
        var native = _assembly.GetType("NGUInjector.SettingsForm", true);
        var nguPreset = native.GetMethod("StagePresetDefinition", StaticMethods).Invoke(null, new object[] {4});
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Equal(5, (int)nguPreset.GetType().GetField("EnergyFocus", fields).GetValue(nguPreset), "native Energy NGU preset selects energy NGUs");
        Equal(6, (int)nguPreset.GetType().GetField("MagicFocus", fields).GetValue(nguPreset), "native Energy NGU preset also selects magic NGUs");
        Equal(2, (int)nguPreset.GetType().GetField("GearGoal", fields).GetValue(nguPreset), "native paired preset retains energy-oriented gear");
        var clone = native.GetMethod("CloneStage", StaticMethods).Invoke(null, new[] {restored});
        Equal(61, (int)stageType.GetField("DurationSeconds").GetValue(clone), "native row clone retains canonical seconds");
        Equal("min", (string)stageType.GetField("DurationUnit").GetValue(clone), "native row clone retains display unit");
        Equal(true, (bool)stageType.GetField("FightOnEntry").GetValue(clone), "native row clone retains fight opt-in");
        Equal(true, stageType.GetField("SourceJson", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clone) != null, "native row clone retains source extensions");
        var legacy = jsonType.GetMethod("Parse", StaticMethods).Invoke(null, new object[] {"{\"Name\":\"Existing stage\",\"EnergyFocus\":3}"});
        restored = storeType.GetMethod("DeserializeStage", StaticMethods).Invoke(null, new[] {legacy});
        Equal(5, (int)stageType.GetField("BlackBarLookaheadSeconds").GetValue(restored), "persisted stage missing lookahead gets five-second default");
        Equal(false, (bool)stageType.GetField("FightOnEntry").GetValue(restored), "legacy stage has no entry fight by default");
        Equal(true, stageType.GetField("EnergyPriorities").GetValue(restored) == null, "persisted stage missing order keeps preset defaults");
    }

    private static void CheckNguSnapshot()
    {
        var main = _assembly.GetType("NGUInjector.Main", true);
        var characterField = main.GetField("Character", StaticMethods);
        var previous = characterField.GetValue(null);
        var character = FormatterServices.GetUninitializedObject(characterField.FieldType);
        var nguField = character.GetType().GetField("NGU");
        var ngu = FormatterServices.GetUninitializedObject(nguField.FieldType);
        nguField.SetValue(character, ngu);
        var caps = (IDictionary<string, long>) _allocator.GetField("NguBufferedCaps", StaticMethods).GetValue(null);
        try
        {
            foreach (var energy in new[] {true, false})
            {
                var field = ngu.GetType().GetField(energy ? "skills" : "magicSkills");
                var list = (System.Collections.IList) Activator.CreateInstance(field.FieldType);
                var skillType = field.FieldType.GetGenericArguments()[0];
                for (var i = 0; i < (energy ? 9 : 7); i++)
                {
                    var skill = FormatterServices.GetUninitializedObject(skillType);
                    skillType.GetField(energy ? "energy" : "magic").SetValue(skill, energy && i == 0 ? 1000L : i == 6 ? 10L : 0L);
                    list.Add(skill);
                }
                field.SetValue(ngu, list);
            }
            caps.Clear(); caps["E0"] = 2000L;
            characterField.SetValue(null, character);
            var energyWeights = new[] {0, 0, 0, 0, 2, 0, 1, 0, 0};
            var magicWeights = new[] {1, 0, 0, 0, 0, 0, 0};
            var runtime = Call("GetNguRuntime", energyWeights, magicWeights);
            Func<object, object, object> node = (root, key) => root.GetType().GetProperty("Item", new[] {key.GetType()}).GetValue(root, new[] {key});
            Func<object, string> value = root => (string) root.GetType().GetProperty("Value").GetValue(root);
            Func<object, bool> boolean = root => (bool) root.GetType().GetProperty("AsBool").GetValue(root);
            Equal("weighted-ngu-buffered-cap", value(node(runtime, "strategy")), "runtime identifies weighted NGU groups rather than fill-first targets");
            var energyRows = node(runtime, "energyTargets");
            var magicRows = node(runtime, "magicTargets");
            Equal(9, (int) energyRows.GetType().GetProperty("Count").GetValue(energyRows), "runtime includes every energy row");
            Equal(7, (int) magicRows.GetType().GetProperty("Count").GetValue(magicRows), "runtime includes every magic row");
            Equal("Augments", value(node(node(energyRows, 0), "name")), "runtime NGU Augments is named, not mistaken for augmentation feature");
            Equal("1000", value(node(node(energyRows, 0), "allocated")), "runtime per-target allocation is actual game resource field");
            Equal("0", value(node(node(energyRows, 0), "weight")), "zero-weight rows remain visible with existing allocations");
            Equal("2", value(node(node(energyRows, 4), "weight")), "runtime exposes uneven per-row energy weights");
            Equal("2", value(node(node(runtime, "energyWeights"), 4)), "runtime exposes configured energy array");
            Equal("1", value(node(node(runtime, "magicWeights"), 0)), "runtime exposes configured magic array");
            var shortage = node(node(energyRows, 0), "belowBufferedCap");
            Equal(true, (bool) shortage.GetType().GetProperty("AsBool").GetValue(shortage), "runtime reports genuine buffered-cap shortage");
            Equal("null", value(node(node(energyRows, 1), "bufferedCap")), "unvisited later target cap stays unknown, not a fake zero cap");
            Equal("Yggdrasil", value(node(node(magicRows, 0), "name")), "magic row zero is Yggdrasil");
            Equal("1", value(node(node(magicRows, 0), "weight")), "default magic configuration selects Yggdrasil");
            Equal("6", value(node(node(magicRows, 6), "index")), "full magic snapshot preserves actual seven-row indexes");
            Equal("10", value(node(node(magicRows, 6), "allocated")), "zero-weight Adventure Beta snapshot reads actual allocation");
            Equal("1000", value(node(node(node(Call("GetNguRuntime", energyWeights, magicWeights), "energyTargets"), 0), "allocated")), "read-only weighted snapshot does not rebalance resources");

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var settingsField = main.GetField("Settings", StaticMethods);
            var storeField = main.GetField("CustomPlanStore", StaticMethods);
            var referenceField = main.GetField("reference", StaticMethods);
            var previousSettings = settingsField.GetValue(null);
            var previousStore = storeField.GetValue(null);
            var previousReference = referenceField.GetValue(null);
            var plansDir = Path.Combine(Path.GetTempPath(), "ngu-telemetry-" + Guid.NewGuid().ToString("N"));
            try
            {
                var owner = FormatterServices.GetUninitializedObject(main);
                referenceField.SetValue(null, owner);
                var settingsType = _assembly.GetType("NGUInjector.SavedSettings", true);
                var settings = Activator.CreateInstance(settingsType, new object[] {null});
                settingsType.GetProperty("CustomPlanFile").SetValue(settings, "telemetry-check");
                settingsField.SetValue(null, settings);
                var storeType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlanStore", true);
                var store = Activator.CreateInstance(storeType, flags, null, new object[] {plansDir}, null);
                storeField.SetValue(null, store);
                var planType = _assembly.GetType("NGUInjector.CustomPlans.CustomPlan", true);
                var plan = Activator.CreateInstance(planType);
                planType.GetField("EnergyNguWeights").SetValue(plan, energyWeights);
                planType.GetField("MagicNguWeights").SetValue(plan, magicWeights);
                storeType.GetMethod("Save", flags).Invoke(store, new[] {"telemetry-check", plan});
                var focus = Enum.Parse(_assembly.GetType("NGUInjector.CustomFocusGoal", true), "MagicNgu");
                main.GetField("_customSustainedFocusGoal", flags).SetValue(owner, focus);
                main.GetField("_customRequestedFocusGoal", flags).SetValue(owner, focus);
                main.GetField("_customAppliedFocusGoal", flags).SetValue(owner, focus);
                var getRuntime = main.GetMethod("GetCustomNguRuntime", flags);
                var before = getRuntime.Invoke(owner, null);
                Equal(false, boolean(node(before, "energyWeightsApplied")), "telemetry never claims energy applied before a pass");
                Equal(false, boolean(node(before, "magicWeightsApplied")), "telemetry never claims magic applied before a pass");
                main.GetField("_customFocusMagicNguWeights", flags).SetValue(owner, magicWeights.ToArray());
                var applied = getRuntime.Invoke(owner, null);
                Equal(false, boolean(node(applied, "energyWeightsApplied")), "magic-only telemetry does not claim unmanaged energy weights applied");
                Equal(true, boolean(node(applied, "magicWeightsApplied")), "telemetry compares equal arrays by value, not identity");
                Equal("1", value(node(node(applied, "magicAppliedWeights"), 0)), "telemetry exports the affected magic lane snapshot");
                main.GetField("_customFocusMagicNguWeights", flags).SetValue(owner, new[] {0, 0, 1, 0, 0, 0, 0});
                var pending = getRuntime.Invoke(owner, null);
                Equal(false, boolean(node(pending, "magicWeightsApplied")), "pending config differs from its applied snapshot");
                Equal("1", value(node(node(pending, "magicWeights"), 0)), "pending telemetry retains configured weights");
                Equal("1", value(node(node(pending, "magicAppliedWeights"), 2)), "pending telemetry retains actual last-pass weights");
                Equal("1000", value(node(node(node(pending, "energyTargets"), 0), "allocated")), "real telemetry wrapper is read-only for unmanaged energy");
            }
            finally
            {
                settingsField.SetValue(null, previousSettings);
                storeField.SetValue(null, previousStore);
                referenceField.SetValue(null, previousReference);
                if (Directory.Exists(plansDir)) Directory.Delete(plansDir, true);
            }
        }
        finally
        {
            caps.Clear();
            characterField.SetValue(null, previous);
        }
    }

    private static void CheckIdleExemptionReset()
    {
        var properties = BindingFlags.Static | BindingFlags.NonPublic;
        _allocator.GetProperty("ExcessIdleEnergy", properties).SetValue(null, 100L);
        _allocator.GetProperty("ExcessIdleMagic", properties).SetValue(null, 200L);
        Call("ResetExcessIdle");
        Equal(-1L, (long)_allocator.GetProperty("ExcessIdleEnergy", properties).GetValue(null), "energy idle exemption cleared for new context");
        Equal(-1L, (long)_allocator.GetProperty("ExcessIdleMagic", properties).GetValue(null), "magic idle exemption cleared for new context");
    }

    private static void CheckBloodGoals()
    {
        var type = typeof(NGUInjector.Managers.BloodGoalEstimate);
        var calculate = type.GetMethod("Calculate", StaticMethods);
        Func<object, string, object> field = (value, name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        var loot = calculate.Invoke(null, new object[] {15, false, 10d, 100000000d, 20000000d, 10000d, 2000d, 2, true});
        Equal(163840000d, (double)field(loot, "RequiredBlood"), "15% Spaghetti total blood");
        Equal(53840000d, (double)field(loot, "RemainingBlood"), "Spaghetti subtracts invested blood and split pool");
        Equal(1000d, (double)field(loot, "BloodPerSecond"), "two spells share production");
        Equal(53840d, (double)field(loot, "EstimatedSeconds"), "Spaghetti time at current split");
        Equal("Casting", (string)field(loot, "Status"), "active Spaghetti estimate");

        var gold = calculate.Invoke(null, new object[] {100, true, 90d, 500000000d, 0d, 1000000d, 3000d, 3, true});
        Equal(512000000d, (double)field(gold, "RequiredBlood"), "100% Counterfeit total blood");
        Equal(12000d, (double)field(gold, "EstimatedSeconds"), "NUMBER participates in three-way split");

        var minimum = calculate.Invoke(null, new object[] {100, true, 99d, 511500000d, 500000d, 1000000d, 1000d, 1, true});
        Equal(500000d, (double)field(minimum, "RemainingBlood"), "near-goal gold still needs minimum cast funding");
        Equal(500d, (double)field(minimum, "EstimatedSeconds"), "ETA waits for minimum cast not just bonus deficit");
        var minimumStopped = calculate.Invoke(null, new object[] {100, true, 99d, 511500000d, 500000d, 1000000d, 0d, 1, true});
        Equal("No blood production", (string)field(minimumStopped, "Status"), "subminimum pool cannot cast without production");
        var splitFloor = calculate.Invoke(null, new object[] {1, false, 0d, 0d, 19999d, 10000d, 2d, 2, true});
        Equal(1d, (double)field(splitFloor, "RemainingBlood"), "pool share rounds down like game auto-cast");
        Equal(1d, (double)field(splitFloor, "EstimatedSeconds"), "fractional split cannot finish a cast early");

        var paused = calculate.Invoke(null, new object[] {15, false, 10d, 100000000d, 200000000d, 10000d, 2000d, 1, false});
        Equal("Spell off / paused", (string)field(paused, "Status"), "paused spell has no misleading countdown");
        Equal(true, field(paused, "EstimatedSeconds") == null, "paused ETA is unavailable");
        Equal(0d, (double)field(paused, "BloodPerSecond"), "paused spell receives no production share");
        Equal(63840000d, (double)field(paused, "RemainingBlood"), "paused spell cannot spend shared pool");

        var stopped = calculate.Invoke(null, new object[] {15, false, 10d, 0d, 0d, 10000d, 0d, 2, true});
        Equal("No blood production", (string)field(stopped, "Status"), "zero production status");
        Equal(true, field(stopped, "EstimatedSeconds") == null, "zero production does not serialize infinity");

        var ready = calculate.Invoke(null, new object[] {15, false, 10d, 0d, 327680000d, 10000d, 0d, 2, true});
        Equal("Enough blood; awaiting cast", (string)field(ready, "Status"), "banked pool can finish without production");
        Equal(0d, (double)field(ready, "EstimatedSeconds"), "banked blood ETA");
        var reached = calculate.Invoke(null, new object[] {15, false, 16d, 327680000d, 0d, 10000d, 0d, 0, false});
        Equal("Goal reached", (string)field(reached, "Status"), "completed goal takes precedence over paused production");
        Equal(0d, (double)field(reached, "RemainingBlood"), "completed remaining blood is clamped");

        var disabled = calculate.Invoke(null, new object[] {0, false, 0d, 0d, 0d, 10000d, 0d, 0, false});
        Equal("Disabled (0%)", (string)field(disabled, "Status"), "zero goal disables spell");
        var overflow = calculate.Invoke(null, new object[] {100000, false, 0d, 0d, 0d, 10000d, 2000d, 1, true});
        Equal("Goal too large to estimate", (string)field(overflow, "Status"), "exponential overflow is explicit");
        Equal(true, field(overflow, "RequiredBlood") == null, "overflow never enters JSON as infinity");

        var percent = type.GetMethod("WholePercent", StaticMethods);
        Equal(15d, (double)percent.Invoke(null, new object[] {(double)1.15f}), "float multiplier reaches exact 15% target");
        Equal(14d, (double)percent.Invoke(null, new object[] {(double)1.14f}), "one percent below target is not reached");
        Equal(100d, (double)percent.Invoke(null, new object[] {(double)2f}), "exact Counterfeit target");
    }
}
