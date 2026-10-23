#if PRESET_STAGE_NATIVE_CHECKS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

internal static class PresetStageTargetsNativeChecks
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Type Fixture = typeof(PresetIntegrationChecks);
    private static Assembly Plugin, Game;
    private static Type MainType;
    private static object Character;
    private static int Assertions;

    private static object FixtureCall(string name, params object[] args)
    {
        try { return Fixture.GetMethod(name, All).Invoke(null, args); }
        catch (TargetInvocationException error) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static object Call(object target, string name, params object[] args) { return FixtureCall("Invoke", target, name, args); }
    private static object Get(object target, string name) { return FixtureCall("Get", target, name); }
    private static void Set(object target, string name, object value) { FixtureCall("Set", target, name, value); }
    private static object New(string name) { return Activator.CreateInstance(Plugin.GetType("NGUInjector.Managers." + name, true), true); }
    private static void Check(bool condition, string message) { Assertions++; if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException error) { Check(error.Message.Contains(message), "explicit rejection: " + error.Message); return; }
        throw new Exception("Expected rejection: " + message);
    }

    public static int Main(string[] args)
    {
        var temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "PresetStageTargets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var managed = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
                var name = new AssemblyName(request.Name).Name;
                if (name == "Assembly-CSharp") return Game;
                if (name == "UnityEngine.UI") return (Assembly)FixtureCall("LoadUi", Path.Combine(managed, name + ".dll"));
                var path = Path.Combine(managed, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            // The existing fixture keeps native AT progress/divisor/target/level rules, stubbing rendering leaves.
            Game = (Assembly)Call(typeof(Program), "LoadFixtureGame", Path.Combine(managed, "Assembly-CSharp.dll"));
            Plugin = (Assembly)FixtureCall("LoadPlugin", Path.GetFullPath(args[0])); MainType = Plugin.GetType("NGUInjector.Main", true);
            Directory.CreateDirectory(temp);
            foreach (var type in new[] { typeof(Program), Fixture })
            { Set(type, "Game", Game); Set(type, "Plugin", Plugin); Set(type, "MainType", MainType); }
            Set(typeof(Program), "Temp", temp);
            Call(typeof(Program), "Setup");
            Character = Get(typeof(Program), "Character");
            Check((int)Get(Get(Character, "adventure"), "zone") == -1
                && (int)FixtureCall("Property", Get(Character, "adventureController"), "zone") == -1,
                "alternate startup shares native saved/controller Safe Zone writes without Unity scene calls");
            Set(Fixture, "Character", Character); Set(Fixture, "Controller", Get(typeof(Program), "Controller"));
            Set(Fixture, "MainInstance", Get(typeof(Program), "MainInstance"));
            FixtureCall("PrepareSupport");
            Set(Get(Character, "timeMachineController"), "character", Character);
            Set(Get(Character, "advancedTraining"), "transferredBankedLevels", true);
            var allAt = Get(Character, "advancedTrainingController");
            foreach (var name in new[] { "defense", "attack", "block", "wandoosEnergy", "wandoosMagic" })
                Set(Get(allAt, name), "alladvancedTraining", allAt);
            CheckTargets();
            Console.WriteLine("PASS: " + Assertions + " compiled stage-target/native AT assertions. Isolated fixture only; no live game actions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    private static object Preset()
    {
        var p = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomLoadoutPreset", true));
        Set(p, "Id", "stage-at"); Set(p, "Name", "Stage AT");
        Set(p, "Weights", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0 });
        return p;
    }
    private static object Capture(object preset)
    {
        var s = New("PresetSnapshot"); var itemType = Plugin.GetType("NGUInjector.Managers.PresetItem", true);
        Set(s, "Current", Array.CreateInstance(itemType, 0)); Set(s, "Items", Array.CreateInstance(itemType, 0));
        Set(s, "Slots", new int[0]); Set(s, "Factors", new double[0]);
        Set(s, "Energy", 10000L); Set(s, "Magic", 20000L);
        Set(s, "BasePower", 1000d); Set(s, "BaseToughness", 1000d);
        foreach (var energy in new[] { true, false })
        foreach (var capacity in new[] { true, false })
        {
            var projection = New("ResourceProjection"); Set(projection, "Energy", energy); Set(projection, "Capacity", capacity);
            Set(projection, "BaseCap", energy ? 10000L : 20000L); Set(projection, "BasePower", energy ? 100f : 50f);
            Set(s, (energy ? "Energy" : "Magic") + (capacity ? "Capacity" : "PowerProjection"), projection);
        }
        var available = (bool[])Get(s, "Available"); available[10] = available[11] = true;
        Call(MainType, "CapturePresetActivities", s, preset);
        Check(((bool[])Get(s, "Available"))[9], "native AT coefficient capture succeeded: " + ((string[])Get(s, "Reasons"))[9]);
        Check(((bool[])Get(s, "Available")).Length == 14 && ((bool[])Get(s, "Available"))[10] && ((bool[])Get(s, "Available"))[11],
            "activity capture preserves base-captured immediate-stat availability instead of treating it as level production");
        return s;
    }
    private static object Evaluate(object snapshot, object preset, object policy = null)
    {
        var request = Call(MainType, "LoadoutPresetRequest", preset); Set(request, "Horizon", 10); Set(request, "Swap", 0);
        return Call(Plugin.GetType("NGUInjector.Managers.LoadoutPresetPlanner", true), "Evaluate", snapshot, request, Get(snapshot, "Current"),
            Call(Plugin.GetType("NGUInjector.Managers.LoadoutPresetPlanner", true), "NormalizeWeights", Get(preset, "Weights")),
            Enumerable.Repeat(1d, 12).ToArray(), "stage", "Stage", policy);
    }
    private static void CheckTargets()
    {
        var at = Get(Character, "advancedTraining"); var menu = (long[])Get(at, "levelTarget");
        var levels = (long[])Get(at, "level");
        for (var i = 0; i < 5; i++) menu[i] = -1;
        var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
        Set(stage, "CompletionMode", 1); Set(stage, "TargetAdvancedTrainingToughness", 3L); Set(stage, "TargetAdvancedTrainingPower", 4L);
        var p = Preset(); var s = Capture(p); var lanes = (Array)Get(s, "AdvancedTraining");
        var coefficient = Get(lanes.GetValue(1), "Coefficient"); var progress = Get(lanes.GetValue(1), "Progress");
        Check(!(bool)Get(lanes.GetValue(0), "Unlocked") && !(bool)Get(lanes.GetValue(1), "Unlocked"), "menu -1 initially disables both captured stat lanes");
        Call(MainType, "ApplyPresetStageTargets", s, stage);
        Check((double)Get(lanes.GetValue(0), "Target") == 3 && (double)Get(lanes.GetValue(1), "Target") == 4,
            "stage requirements override native -1 only in snapshot");
        Check((bool)Get(lanes.GetValue(0), "Unlocked") && (bool)Get(lanes.GetValue(1), "Unlocked"), "stage re-enables both required parity-captured lanes");
        Check(Equals(coefficient, Get(lanes.GetValue(1), "Coefficient")) && Equals(progress, Get(lanes.GetValue(1), "Progress")), "stage target override preserves native coefficients/progress");
        Check(menu.All(x => x == -1) && ((long[])Get(at, "energy")).All(x => x == 0), "capture/hook never mutates live menu or resources");
        Check(Enumerable.Range(2, 3).All(i => !(bool)Get(lanes.GetValue(i), "Unlocked") && (double)Get(lanes.GetValue(i), "Target") == -1), "unrelated disabled block/Wandoos lanes preserved");
        var candidate = Evaluate(s, p); var allocation = (long[])Get(Get(candidate, "Activities"), "AT");
        Check((bool)Get(candidate, "Feasible") && Get(candidate, "Score") != null && allocation[0] > 0 && allocation[1] > 0,
            "compiled weighted engine funds both stage stat requirements despite menu -1");
        Check(allocation.Skip(2).All(x => x == 0) && allocation.Sum() <= (long)Get(s, "Energy"), "stage allocations conserve energy and leave disabled lanes at zero");
        var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
        Check(!(bool)Call(allocator, "AdvancedTrainingStatsReached", stage), "actual stage gate is false before training");
        // Emulate only the runtime owner's scoped menu-target override, never a saved preset/plan edit.
        try
        {
            menu[0] = 3; menu[1] = 4;
            Call(MainType, "ApplyLoadoutPresetAllocations", candidate);
            var controllers = Get(Character, "advancedTrainingController");
            for (var tick = 0; tick < 4; tick++)
            {
                Call(Get(controllers, "defense"), "updateAdvancedTraining"); Call(Get(controllers, "attack"), "updateAdvancedTraining");
                if (tick == 2) Check(!(bool)Call(allocator, "AdvancedTrainingStatsReached", stage), "gate waits for power after toughness reaches its target");
            }
            Check(levels[0] == 3 && levels[1] == 4 && (bool)Call(allocator, "AdvancedTrainingStatsReached", stage), "real native AT ticks satisfy the actual stage completion gate");
        }
        finally { menu[0] = menu[1] = -1; }
        Check(menu.All(x => x == -1), "native execution scope restores original disabled menu targets");

        levels[1] = 0; var continuation = Capture(p); Call(MainType, "ApplyPresetStageTargets", continuation, stage);
        var continued = Evaluate(continuation, p, candidate); var continuedAT = (long[])Get(Get(continued, "Activities"), "AT");
        Check((bool)Get(continued, "Feasible") && continuedAT[0] == 0 && continuedAT[1] == allocation[1], "cached stage continuation stops completed toughness while retaining exact unfinished power allocation");
        Check(allocation[0] > 0 && allocation[1] > 0, "completed-target replay clipping never mutates cached allocation policy");

        levels[0] = levels[1] = 0;
        var nativeEnergy = ((long[])Get(at, "energy")).ToArray(); var idleEnergy = Get(Character, "idleEnergy");
        var recalls = Get(typeof(Program), "Recalls"); var swaps = Get(typeof(Program), "Swaps");
        foreach (var weights in new[] { new[] { 0, 1, 0, 0, 0 }, new[] { 1, 0, 0, 0, 0 } })
        {
            p = Preset(); Set(p, "AdvancedTrainingWeights", weights); s = Capture(p);
            Reject(() => Call(MainType, "ApplyPresetStageTargets", s, stage), "excluded by preset weights");
            Check(menu.All(x => x == -1) && ((Array)Get(s, "AdvancedTraining")).Cast<object>().Take(2).All(x => (double)Get(x, "Target") == -1), "excluded stat fails before snapshot/menu target mutation");
        }
        p = Preset(); Set(p, "Weights", new double[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 }); s = Capture(p);
        Reject(() => Call(MainType, "ApplyPresetStageTargets", s, stage), "Weights[9]");
        p = Preset(); s = Capture(p); ((bool[])Get(s, "Available"))[9] = false; ((string[])Get(s, "Reasons"))[9] = "parity refused";
        Reject(() => Call(MainType, "ApplyPresetStageTargets", s, stage), "parity refused");
        Check(((Array)Get(s, "AdvancedTraining")).Cast<object>().Take(2).All(x => !(bool)Get(x, "Unlocked")), "stage hook cannot manufacture availability after parity failure");
        Check(((long[])Get(at, "energy")).SequenceEqual(nativeEnergy) && Equals(idleEnergy, Get(Character, "idleEnergy"))
            && Equals(recalls, Get(typeof(Program), "Recalls")) && Equals(swaps, Get(typeof(Program), "Swaps")), "mandatory preflight rejection never recalls, swaps, or changes live energy");

        p = Preset(); s = Capture(p); Call(MainType, "ApplyPresetStageTargets", s, stage); candidate = Evaluate(s, p);
        ((long[])Get(Get(candidate, "Activities"), "AT"))[1] = 0;
        var unfunded = Evaluate(s, p, candidate);
        Check(!(bool)Get(unfunded, "Feasible") && ((string[])Get(unfunded, "Warnings")).Any(x => x.Contains("Mandatory stage AT")), "cached zero-power allocation is infeasible before any application");

        Set(stage, "TargetAdvancedTrainingToughness", 0L); p = Preset(); Set(p, "AdvancedTrainingWeights", new[] { 0, 1, 0, 0, 0 });
        s = Capture(p); Call(MainType, "ApplyPresetStageTargets", s, stage); lanes = (Array)Get(s, "AdvancedTraining"); candidate = Evaluate(s, p);
        Check((double)Get(lanes.GetValue(0), "Target") == -1 && !(bool)Get(lanes.GetValue(0), "Unlocked"), "zero stage requirement remains disabled rather than unlimited");
        Check((bool)Get(candidate, "Feasible") && ((long[])Get(Get(candidate, "Activities"), "AT"))[0] == 0
            && ((long[])Get(Get(candidate, "Activities"), "AT"))[1] > 0, "zero toughness requirement allows an explicit power-only preset");
        levels[1] = 4; s = Capture(p); Call(MainType, "ApplyPresetStageTargets", s, stage); candidate = Evaluate(s, p);
        Check((bool)Get(candidate, "Feasible") && ((double?[])Get(candidate, "Metrics"))[9] == 0
            && ((long[])Get(Get(candidate, "Activities"), "AT")).All(x => x == 0), "completed stage requirements remain known-zero production with no paid allocation");

        levels[1] = 0; Set(stage, "CompletionMode", 0); s = Capture(p); Call(MainType, "ApplyPresetStageTargets", s, stage);
        Check(((long[])Get(s, "RequiredATTargets")).Length == 0 && ((Array)Get(s, "AdvancedTraining")).Cast<object>().All(x => (double)Get(x, "Target") == -1), "ordinary timed stage leaves menu-based snapshot semantics unchanged");
    }
}
#endif
