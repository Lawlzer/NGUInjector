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
            if (args.Length != 2 && (args.Length != 3 || args[2] != "stage-support" && args[2] != "blood" && args[2] != "blood-sequential" && args[2] != "protected" && args[2] != "scheduling-regressions" && args[2] != "auto-boost"))
                throw new ArgumentException("Pass NGUInjector.dll and the game's Managed directory; optionally append stage-support, blood, blood-sequential, protected, scheduling-regressions, or auto-boost for targeted checks.");
            Assembly game = null;
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
            {
                var name = new AssemblyName(request.Name).Name;
                if (name == "Assembly-CSharp" && game != null) return game;
                var path = Path.Combine(args[1], name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            game = IronPillReservationChecks.LoadGameWithoutBloodUi(args[1]);
            _assembly = ProtectedTimingIntegrationChecks.LoadInjector(args[0]);
            _allocator = _assembly.GetType("NGUInjector.Managers.BlackBarAllocator", true);
            if (args.Length == 3 && args[2] == "scheduling-regressions")
            {
                _assertions += ProtectedTimingIntegrationChecks.RunSchedulingRegressions(_assembly);
                return 0;
            }
            if (args.Length == 3 && args[2] == "auto-boost")
            {
                _assertions += CustomAutoBoostChecks.Run(_assembly);
                return 0;
            }
            if (args.Length == 3 && args[2] == "protected")
            {
                _assertions += ProtectedTimingIntegrationChecks.Run(_assembly);
                return 0;
            }
            if (args.Length == 3 && (args[2] == "blood" || args[2] == "blood-sequential"))
            {
                CheckBloodGoals();
                CheckIronPill();
                _assertions += IronPillReservationChecks.Run(_assembly, args[2] == "blood-sequential");
                Console.WriteLine("Blood checks passed: " + _assertions + " assertions.");
                return 0;
            }
            if (args.Length == 3) { _assertions += StageSupportChecks.Run(_assembly); return 0; }
            var failures = new List<Exception>();
            foreach (var test in new Action[] {
                () => _assertions += StageSupportChecks.Run(_assembly),
                () => _assertions += ProtectedTimingIntegrationChecks.Run(_assembly),
                CheckOrderedAllocation, CheckWeightedSharing, CheckProjection, CheckRounding, CheckTokens,
                CheckNguSnapshot, CheckSavedStages, CheckIdleExemptionReset, CheckBloodGoals, CheckIronPill,
                () => _assertions += IronPillReservationChecks.Run(_assembly),
                () => _assertions += GearOptimizerChecks.Run(_assembly),
                () => _assertions += GearSearchChecks.Run(_assembly),
                () => _assertions += GearEquipChecks.Run(_assembly),
                () => _assertions += GearSingleStatChecks.Run(_assembly),
                () => _assertions += CustomAutoBoostChecks.Run(_assembly) })
            {
                try { test(); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0) throw new AggregateException("Black-bar checks failed; " + _assertions + " completed-group assertions (not a pass count).", failures);
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

    // Runtime/fight regressions now live in ProtectedTimingIntegrationChecks:
    // actual application, applied-time credit, AT/flex transitions and stable entries.
    private static void Equal<T>(T expected, T actual, string scenario)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(scenario + ": expected " + expected + ", got " + actual);
    }

    private static long[] Allocate(long budget, params long[] caps)
    {
        var allocations = new long[caps.Length];
        var priorities = Enumerable.Range(0, caps.Length).Select(index => index.ToString()).ToArray();
        Func<string, long, long> fund = (name, remaining) => {
            var index = int.Parse(name);
            return allocations[index] = Math.Min(remaining, Math.Max(0, caps[index]));
        };
        // Numeric ordering now lives in the pure lane resolver, not native write closures.
        // Compiled resource-domain regressions separately exercise the real ordered allocator.
        var remainingBudget = (long)_assembly.GetType("NGUInjector.Managers.ResourceLanePriority", true)
            .GetMethod("Resolve", StaticMethods).Invoke(null, new object[] {budget, priorities, fund});
        var distributed = budget - remainingBudget;
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
        Func<string, long, long> lazyFund = (name, remaining) => { visited++; return Math.Min(10, remaining); };
        var left = (long)_assembly.GetType("NGUInjector.Managers.ResourceLanePriority", true).GetMethod("Resolve", StaticMethods)
            .Invoke(null, new object[] {10L, new[] {"WAN", "NGU", "TM"}, lazyFund});
        Equal(0L, left, "ordered calculation spends the useful budget");
        Equal(1, visited, "exhausted budget never evaluates lower-priority targets");
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
        Equal(18, (int) planType.GetField("Version").GetValue(plan), "new fight schedule plan version");
        var settingsType = _assembly.GetType("NGUInjector.SavedSettings", true);
        var settings = Activator.CreateInstance(settingsType, new object[] {null});
        Equal("0,0,0,0,1,0,1,0,0", string.Join(",", (int[]) settingsType.GetProperty("EnergyNguWeights").GetValue(settings)), "default global energy lane weights");
        Equal("1,0,0,0,0,0,0", string.Join(",", (int[]) settingsType.GetProperty("MagicNguWeights").GetValue(settings)), "default global magic lane weights select Yggdrasil");
        Equal(true, planType.GetField("EnergyNguWeights") == null && planType.GetField("MagicNguWeights") == null, "plans cannot configure NGU ratios");
        Equal(true, planType.GetField("NguGoal") == null && _assembly.GetType("NGUInjector.CustomPlans.CustomNguGoal", false) == null,
            "legacy enum and field are absent from the runtime contract");
        Equal("NGU,WAN,TM,AT", string.Join(",", (string[])Call("DefaultPriorities", Enum.Parse(focus, "EnergyNgu"), true)), "Energy NGU never routes primary resources to augmentation feature");
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
        var scheduleType = _assembly.GetType("NGUInjector.CustomPlans.CustomFightSchedule", true);
        var schedule = Activator.CreateInstance(scheduleType);
        scheduleType.GetField("Id").SetValue(schedule, "native-clone-schedule");
        scheduleType.GetField("Anchor").SetValue(schedule, 1);
        scheduleType.GetField("DelaySeconds").SetValue(schedule, 60);
        scheduleType.GetField("IntervalSeconds").SetValue(schedule, 120);
        var sourceField = scheduleType.GetField("SourceJson", BindingFlags.Instance | BindingFlags.NonPublic);
        sourceField.SetValue(schedule, jsonType.GetMethod("Parse", StaticMethods).Invoke(null, new object[] {"{\"future\":9007199254740993}"}));
        stageType.GetField("FightSchedule").SetValue(stage, schedule);
        var array = Array.CreateInstance(stageType, 1);
        array.SetValue(stage, 0);
        var serialized = storeType.GetMethod("SerializeStages", StaticMethods).Invoke(null, new object[] {array});
        var node = serialized.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(serialized, new object[] {0});
        var restored = storeType.GetMethod("DeserializeStage", StaticMethods).Invoke(null, new object[] {node, 0});
        Equal("WAN,NGU-4,TM", string.Join(",", (string[])stageType.GetField("EnergyPriorities").GetValue(restored)), "saved energy order round trip");
        Equal("RIT-0,WAN", string.Join(",", (string[])stageType.GetField("MagicPriorities").GetValue(restored)), "saved magic order round trip");
        Equal(7, (int)stageType.GetField("BlackBarLookaheadSeconds").GetValue(restored), "saved lookahead round trip");
        Equal(61, (int)stageType.GetField("DurationSeconds").GetValue(restored), "saved fixed seconds round trip");
        Equal("min", (string)stageType.GetField("DurationUnit").GetValue(restored), "saved display unit round trip");
        var restoredSchedule = stageType.GetField("FightSchedule").GetValue(restored);
        Equal("native-clone-schedule", (string)scheduleType.GetField("Id").GetValue(restoredSchedule), "saved schedule ID round trip");
        Equal(1, (int)scheduleType.GetField("Anchor").GetValue(restoredSchedule), "saved schedule anchor round trip");
        Equal(60, (int)scheduleType.GetField("DelaySeconds").GetValue(restoredSchedule), "saved schedule delay round trip");
        Equal(120, (int)scheduleType.GetField("IntervalSeconds").GetValue(restoredSchedule), "saved schedule interval round trip");
        var native = _assembly.GetType("NGUInjector.SettingsForm", true);
        var nguPreset = native.GetMethod("StagePresetDefinition", StaticMethods).Invoke(null, new object[] {4});
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Equal(5, (int)nguPreset.GetType().GetField("EnergyFocus", fields).GetValue(nguPreset), "native Energy NGU preset selects energy NGUs");
        Equal(6, (int)nguPreset.GetType().GetField("MagicFocus", fields).GetValue(nguPreset), "native Energy NGU preset also selects magic NGUs");
        Equal(2, (int)nguPreset.GetType().GetField("GearGoal", fields).GetValue(nguPreset), "native paired preset retains energy-oriented gear");
        var clone = native.GetMethod("CloneStage", StaticMethods).Invoke(null, new[] {restored});
        Equal(61, (int)stageType.GetField("DurationSeconds").GetValue(clone), "native row clone retains canonical seconds");
        Equal("min", (string)stageType.GetField("DurationUnit").GetValue(clone), "native row clone retains display unit");
        var clonedSchedule = stageType.GetField("FightSchedule").GetValue(clone);
        Equal(false, ReferenceEquals(restoredSchedule, clonedSchedule), "native row clone deep copies schedule");
        Equal("native-clone-schedule", (string)scheduleType.GetField("Id").GetValue(clonedSchedule), "native row clone retains schedule ID");
        Equal(1, (int)scheduleType.GetField("Anchor").GetValue(clonedSchedule), "native row clone retains anchor");
        Equal(60, (int)scheduleType.GetField("DelaySeconds").GetValue(clonedSchedule), "native row clone retains delay");
        Equal(120, (int)scheduleType.GetField("IntervalSeconds").GetValue(clonedSchedule), "native row clone retains repeat interval");
        Equal(false, ReferenceEquals(sourceField.GetValue(restoredSchedule), sourceField.GetValue(clonedSchedule)), "native clone owns its schedule source JSON");
        Equal(sourceField.GetValue(restoredSchedule).ToString(), sourceField.GetValue(clonedSchedule).ToString(), "native clone preserves schedule extensions");
        Equal(true, sourceField.GetValue(clonedSchedule).ToString().Contains("9007199254740993"), "schedule extensions retain exact integers");
        scheduleType.GetField("DelaySeconds").SetValue(clonedSchedule, 99);
        Equal(60, (int)scheduleType.GetField("DelaySeconds").GetValue(restoredSchedule), "editing native clone does not change its source schedule");
        Equal(true, stageType.GetField("SourceJson", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clone) != null, "native row clone retains source extensions");
        foreach (var presetId in new[] { "@active", "named-preset" })
        {
            stageType.GetField("PresetId").SetValue(restored, presetId);
            clone = native.GetMethod("CloneStage", StaticMethods).Invoke(null, new[] {restored});
            array.SetValue(clone, 0);
            serialized = storeType.GetMethod("SerializeStages", StaticMethods).Invoke(null, new object[] {array});
            node = serialized.GetType().GetProperty("Item", new[] {typeof(int)}).GetValue(serialized, new object[] {0});
            var savedClone = storeType.GetMethod("DeserializeStage", StaticMethods).Invoke(null, new object[] {node, 0});
            Equal(presetId, (string)stageType.GetField("PresetId").GetValue(savedClone), "native row clone/save retains preset binding " + presetId);
        }
        var legacy = jsonType.GetMethod("Parse", StaticMethods).Invoke(null, new object[] {"{\"Name\":\"Existing stage\",\"EnergyFocus\":3}"});
        restored = storeType.GetMethod("DeserializeStage", StaticMethods).Invoke(null, new object[] {legacy, 0});
        Equal(5, (int)stageType.GetField("BlackBarLookaheadSeconds").GetValue(restored), "persisted stage missing lookahead gets five-second default");
        Equal(true, stageType.GetField("FightSchedule").GetValue(restored) == null, "legacy stage has no schedule by default");
        Equal(true, stageType.GetField("FightOnEntry") == null, "legacy entry-fight boolean is removed from model");
        Equal(true, stageType.GetField("EnergyPriorities").GetValue(restored) == null, "persisted stage missing order keeps preset defaults");
    }

    private static void CheckNguSnapshot()
    {
        var main = _assembly.GetType("NGUInjector.Main", true);
        var characterField = main.GetField("Character", StaticMethods);
        var previous = characterField.GetValue(null);
        var globalSettingsField = main.GetField("Settings", StaticMethods);
        var previousGlobalSettings = globalSettingsField.GetValue(null);
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
            var globalSettingsType = _assembly.GetType("NGUInjector.SavedSettings", true);
            var globalSettings = Activator.CreateInstance(globalSettingsType, new object[] {null});
            globalSettingsType.GetProperty("EnergyNguWeights").SetValue(globalSettings, energyWeights);
            globalSettingsType.GetProperty("MagicNguWeights").SetValue(globalSettings, magicWeights);
            globalSettingsField.SetValue(null, globalSettings);
            var runtime = Call("GetNguRuntime");
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
            Equal("Power", value(node(node(energyRows, 5), "name")), "energy index five is Power, not Power Alpha");
            Equal("1000", value(node(node(node(Call("GetNguRuntime"), "energyTargets"), 0), "allocated")), "read-only weighted snapshot does not rebalance resources");

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var settingsField = main.GetField("Settings", StaticMethods);
            var storeField = main.GetField("CustomPlanStore", StaticMethods);
            var referenceField = main.GetField("reference", StaticMethods);
            var previousSettings = settingsField.GetValue(null);
            var previousStore = storeField.GetValue(null);
            var previousReference = referenceField.GetValue(null);
            var runtimeDir = Path.Combine(Path.GetTempPath(), "ngu-telemetry-" + Guid.NewGuid().ToString("N"));
            var plansDir = Path.Combine(runtimeDir, "plans");
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
                settingsType.GetProperty("EnergyNguWeights").SetValue(settings, energyWeights);
                settingsType.GetProperty("MagicNguWeights").SetValue(settings, magicWeights);
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
                if (Directory.Exists(runtimeDir)) Directory.Delete(runtimeDir, true);
            }
        }
        finally
        {
            caps.Clear();
            characterField.SetValue(null, previous);
            globalSettingsField.SetValue(null, previousGlobalSettings);
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
