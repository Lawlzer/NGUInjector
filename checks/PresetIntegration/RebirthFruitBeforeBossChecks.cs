using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class PresetIntegrationChecks
{
    private static Action<int, bool> RebirthFruitInvoking;
    private static Action RebirthBossStarting;
    private static Action OrdinaryRebirthHarvestStarting;

    // Observe native entry points, not replacement harvest accounting or prep logic.
    private static void ConfigureRebirthFruitGameFixture(AssemblyDefinition game)
    {
        var controller = game.MainModule.GetType("FruitController");
        foreach (var name in new[] { "consumeFruit", "harvest" })
        {
            var method = controller.Methods.Single(x => x.Name == name && x.Parameters.Count == 1);
            var first = method.Body.Instructions[0]; var il = method.Body.GetILProcessor();
            il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_1));
            il.InsertBefore(first, Instruction.Create(name == "consumeFruit" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
            il.InsertBefore(first, Instruction.Create(OpCodes.Call,
                game.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveRebirthFruitInvocation), All))));
        }
    }

    private static void ConfigureRebirthFruitPluginFixture(AssemblyDefinition plugin)
    {
        var start = plugin.MainModule.GetType("NGUInjector.Main").Methods.Single(x => x.Name == "StartBossPush");
        start.Body.GetILProcessor().InsertBefore(start.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveRebirthBossStart), All))));
        var harvest = plugin.MainModule.GetType("NGUInjector.Managers.YggdrasilManager").Methods.Single(x => x.Name == "ManageYggHarvest");
        harvest.Body.GetILProcessor().InsertBefore(harvest.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(PresetIntegrationChecks).GetMethod(nameof(ObserveOrdinaryRebirthHarvestStart), All))));
    }

    public static void ObserveRebirthFruitInvocation(int id, bool eat) => RebirthFruitInvoking?.Invoke(id, eat);
    public static void ObserveRebirthBossStart() => RebirthBossStarting?.Invoke();
    public static void ObserveOrdinaryRebirthHarvestStart() => OrdinaryRebirthHarvestStarting?.Invoke();

    private static object PrepareRebirthFruits(out IList fruits)
    {
        var plan = SetupPreparationIngress();
        Set(plan, "PrepRequireMaxedFruit", true);
        Set(plan, "TimeTriggerEnabled", true); Set(plan, "TimeTriggerSeconds", 3600);
        var ygg = Get(Character, "yggdrasil"); var all = Get(Character, "yggdrasilController");
        fruits = ListField(ygg, "fruits", 21);
        var controller = ((Array)Get(all, "fruits")).GetValue(0); Child(controller, "tooltip");
        Set(all, "baseSeedReward", Enumerable.Repeat(10L, fruits.Count).ToList());
        Set(all, "activationCost", Enumerable.Repeat(100L, fruits.Count).ToList());
        Set(all, "usesEnergy", Enumerable.Repeat(true, fruits.Count).ToList());
        Set(Get(Get(Character, "buttons"), "yggdrasil"), "m_Interactable", true);
        Set(Get(Character, "NGUController"), "normalMagicBoostFactor", Enumerable.Repeat(.01f, 7).ToList());
        ListField(Get(Get(Character, "inventory"), "itemList"), "itemMaxxed", 600);
        Set(Child(Child(Character, "tooltip"), "log"), "Eventlog", new List<string>());
        foreach (var fruit in fruits)
        {
            Set(fruit, "maxTier", 24L); Set(fruit, "seconds", 3600f); Set(fruit, "harvests", 0);
            Set(fruit, "activated", true); Set(fruit, "permCostPaid", true); Set(fruit, "eatFruit", false);
        }
        Set(fruits[0], "eatFruit", true);
        Set(fruits[19], "seconds", 3599f);
        Set(fruits[20], "seconds", 0f); Set(fruits[20], "maxTier", 0L); Set(fruits[20], "activated", false);
        return plan;
    }

    private static void CheckRebirthFruitBeforeBoss()
    {
        var before = Assertions;
        CheckAutomaticRebirthFruitOrder(false);
        CheckAutomaticRebirthFruitOrder(true);
        CheckManualFightLeavesFruits();
        CheckManualRebirthFruitSafety();
        CheckRebirthFruitPreflightRetry();
        CheckRebirthFruitBossAdmissionWait();
        foreach (var change in new[] { "harvest", "identity", "count" }) CheckRebirthFruitReplacement(change);
        CheckRebirthFruitFinalCallbackReplacement();
        CheckUncertainRebirthFruit();
        CheckTimedRebirthPreparation();
        Console.WriteLine("Rebirth fruit-before-boss: " + (Assertions - before) + " native assertions.");
    }

    private static object PrepareTimedRebirthPlan(string name, out IList fruits)
    {
        PrepareRebirthFruits(out fruits);
        var store = Get(MainType, "CustomPlanStore");
        Invoke(store, "EnsureBuiltInPlans");
        var plan = Invoke(store, "Load", name);
        // Isolate prep admission from the independent protected-stage credit gate.
        Set(plan, "StageSchedulerEnabled", false);
        Set(Get(MainType, "Settings"), "_autoRebirth", true);
        Set(Character, "nextAttackMulti", Get(Character, "attackMulti"));
        Invoke(Plugin.GetType("NGUInjector.AllocationProfiles.RebirthStuff.CustomPlanRebirth", true), "ResetRuntimeState");
        CallMain("BeginRebirthAutomationPass");
        return plan;
    }

    private static void CheckTimedRebirthPreparation()
    {
        var before = Assertions; var failures = new List<Exception>();
        Action<string, Action> run = (name, test) => {
            var start = Assertions;
            try { test(); Console.WriteLine("PASS timed rebirth " + name + ": " + (Assertions - start) + " assertions."); }
            catch (Exception error) { failures.Add(new Exception(name, error)); Console.Error.WriteLine("FAIL timed rebirth " + name + ": " + error.Message); }
        };
        run("15m threshold", () => CheckTimedRebirthPrepThreshold("15m-NUMBER-increase", 900, 900));
        run("30m threshold", () => CheckTimedRebirthPrepThreshold("30m-NUMBER-increase", 1800, 1800));
        run("61m threshold and NUMBER hold", () => CheckTimedRebirthPrepThreshold("1h-1m-NUMBER-increase", 3600, 3660));
        run("61m final time gate", CheckTimedRebirthFinalTimeGate);
        foreach (var permission in new[] { "custom plan", "auto rebirth", "plan rebirth", "no-rebirth challenge" })
            run("permission/" + permission, () => CheckTimedRebirthPrepPermission(permission));
        run("24h zero-default legacy readiness", CheckLegacyRebirthPrepReadiness);
        foreach (var permissionOff in new[] { false, true })
            run("24h completed confirmation/permission-off=" + permissionOff, () => CheckLegacyCompletedRebirthConfirmation(permissionOff));
        foreach (var started in new[] { false, true })
            run("cancelled/started=" + started, () => CheckCancelledTimedRebirthPrep(started));
        foreach (var permission in new[] { "custom plan", "auto rebirth", "plan rebirth", "no-rebirth challenge" })
        {
            run("completed permission recovery/" + permission, () => CheckCompletedTimedPrepPermissionRecovery(permission));
            foreach (var nativeBusy in new[] { false, true })
                run("running permission recovery/" + permission + "/busy=" + nativeBusy,
                    () => CheckRunningTimedPrepPermissionRecovery(permission, nativeBusy));
        }
        run("permission retirement before observer", () => CheckRunningTimedPrepPermissionRecovery("auto rebirth", false, true));
        foreach (var change in new[] { "counter", "object", "epoch" })
        foreach (var rebirth in new[] { false, true })
            run("explicit uncertain recovery/" + change + "/rebirth=" + rebirth,
                () => CheckExplicitPrepAfterUncertainOpportunityChange(change, rebirth));
        foreach (var late in new[] { false, true })
            run("protected final minute/late=" + late, () => CheckProtectedTimedRebirthGate(late));
        foreach (var protectedPending in new[] { false, true })
            run("ordinary harvest reservation/protected=" + protectedPending, () => CheckOrdinaryRebirthHarvestReservation(protectedPending));
        run("pending preparation harvest reservation", CheckPendingPrepHarvestReservation);
        run("post-prep NUMBER confirmation", CheckPostPrepNumberConfirmation);
        run("native editor stage clone fallback", CheckNativeStageCloneFallback);
        foreach (var name in new[] { "24hr", "15m-NUMBER-increase", "30m-NUMBER-increase", "1h-1m-NUMBER-increase" })
            run("built-in deletion protection/" + name, () => CheckBuiltInRebirthPlanDeletion(name));
        Console.WriteLine("Timed rebirth preparation: " + (Assertions - before) + " native assertions; failures=" + failures.Count + ".");
        if (failures.Count != 0) throw new AggregateException("Timed rebirth preparation", failures);
    }

    private static void CheckTimedRebirthPrepThreshold(string name, int threshold, int rebirthAt)
    {
        var plan = PrepareTimedRebirthPlan(name, out var fruits);
        Equal(threshold, Get(plan, "RebirthPrepStartSeconds"), name + " uses shipped prep threshold");
        Equal(rebirthAt, Get(plan, "TimeTriggerSeconds"), name + " uses shipped final time");
        var calls = new List<string>(); var bossStarts = 0;
        var rebirths = Get(Get(Character, "stats"), "rebirthNumber");
        RebirthFruitInvoking = (id, eat) => {
            Equal(0, bossStarts, name + " native fruit precedes actual StartBossPush/" + id);
            Equal(false, Property(MainInstance, "BossPushRunning"), name + " fruit runs before boss process/" + id);
            calls.Add(id + ":" + eat);
        };
        RebirthBossStarting = () => {
            bossStarts++;
            for (var i = 0; i < fruits.Count; i++)
                Equal(i < 19 ? 1 : 0, Get(fruits[i], "harvests"), name + " available fruits committed before StartBossPush/" + i);
        };
        try
        {
            foreach (var seconds in new[] { 0d, threshold - 1d, threshold - .001d })
            {
                PostRebirthClock(seconds);
                Equal(false, CallMain("RunCustomRebirthAutomation", plan), name + " automation is idle before prep/" + seconds);
                Equal(0, calls.Count, name + " no native fruit before threshold/" + seconds);
                Equal(0, bossStarts, name + " no boss-start entry before threshold/" + seconds);
                Equal(false, Property(MainInstance, "BossPushRunning"), name + " no fight before threshold/" + seconds);
                Equal(null, Property(MainInstance, "FightPreparationIdentity"), name + " no early preparation attempt/" + seconds);
            }
            PostRebirthClock(threshold);
            Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), name + " insufficient NUMBER withholds final readiness");
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), name + " real automation starts prep exactly at threshold");
            Equal(true, Get(MainInstance, "_customPreRebirthPrepRunning"), name + " timed prep is running");
            Equal(true, Property(MainInstance, "BossPushRunning"), name + " real boss process admitted");
            Equal(string.Join(",", Enumerable.Range(0, 19).Select(i => i + ":" + (i == 0 || i == 1 || i == 6))),
                string.Join(",", calls), name + " native fruits preserve preferences and force-eat Alpha/Beta");
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            var process = Get(MainInstance, "_bossPushProcess"); var claim = Property(MainInstance, "FightPreparationClaim");
            Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), name + " running prep is still not final-ready");
            Equal(false, Get(attempt, "Cancelled"), name + " readiness observer does not cancel timed prep");
            Check(ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), name + " observer retains original running boss process");
            Check(ReferenceEquals(claim, Property(MainInstance, "FightPreparationClaim")), name + " observer retains exact preparation authority");
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), name + " running prep continues despite insufficient NUMBER");
            Equal(1, bossStarts, name + " running prep admits no duplicate boss");
            Equal(19, calls.Count, name + " running prep invokes no duplicate fruit");
            CallMain("StopBossPush");
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), name + " completed prep waits for final readiness");
            Equal(false, Get(MainInstance, "_customPreRebirthPrepRunning"), name + " finished prep leaves running state");
            Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), name + " finished prep retains completion");
            Equal(true, Get(attempt, "Completed"), name + " original attempt completed");
            for (var i = 0; i < 19; i++) Set(fruits[i], "seconds", 3600f);
            foreach (var seconds in new[] { threshold + 1d, rebirthAt - .001d, rebirthAt, rebirthAt + 60d }.Where(x => x >= threshold).Distinct().OrderBy(x => x))
            {
                PostRebirthClock(seconds); CallMain("BeginRebirthAutomationPass");
                Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), name + " NUMBER remains insufficient/" + seconds);
                Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), name + " observer retains completed timed prep/" + seconds);
                Equal(false, Get(attempt, "Cancelled"), name + " completed attempt remains uncancelled/" + seconds);
                Equal(false, CallMain("RunCustomRebirthAutomation", plan), name + " no final rebirth without NUMBER/" + seconds);
                Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), name + " no actual rebirth invocation/" + seconds);
                Equal(rebirths, Get(Get(Character, "stats"), "rebirthNumber"), name + " native rebirth count unchanged/" + seconds);
                Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), name + " completed attempt not replaced/" + seconds);
                Equal(19, calls.Count, name + " completed prep ignores a fresh fruit cycle/" + seconds);
                Equal(1, bossStarts, name + " completed prep does not restart boss/" + seconds);
            }
        }
        finally { RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void CheckTimedRebirthFinalTimeGate()
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out _);
        Set(plan, "NumberTriggerEnabled", false);
        var rebirths = Get(Get(Character, "stats"), "rebirthNumber");
        PostRebirthClock(3600);
        Equal(true, CallMain("RunCustomRebirthAutomation", plan), "61m time-only control starts prep at 3600");
        CallMain("StopBossPush");
        foreach (var seconds in new[] { 3600d, 3659d, 3659.999d })
        {
            PostRebirthClock(seconds); CallMain("BeginRebirthAutomationPass");
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "completed fight is not permission to rebirth early/" + seconds);
            Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), "time gate retains completed prep/" + seconds);
            Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), "no native rebirth attempt before 3660/" + seconds);
            Equal(rebirths, Get(Get(Character, "stats"), "rebirthNumber"), "no native rebirth before 3660/" + seconds);
        }
        PostRebirthClock(3660);
        Equal(true, CallMain("ObserveCustomRebirthReadiness", plan), "61m final time gate opens at exactly 3660, not at prep start");
        Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), "final-ready observation keeps completed preparation");
    }

    private static void CheckTimedRebirthPrepPermission(string permission)
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out var fruits);
        var settings = Get(MainType, "Settings"); var challenge = Get(Get(Character, "challenges"), "noRebirthChallenge");
        Action<bool> allow = value => {
            if (permission == "custom plan") Set(settings, "_useCustomRebirthPlan", value);
            else if (permission == "auto rebirth") Set(settings, "_autoRebirth", value);
            else if (permission == "plan rebirth") Set(plan, "AutoRebirthEnabled", value);
            else Set(challenge, "inChallenge", !value);
        };
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        RebirthBossStarting = () => bossStarts++;
        try
        {
            PostRebirthClock(3600); allow(false);
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), permission + " inhibits due timed prep");
            Equal(0, calls, permission + " denies every native fruit leaf");
            Equal(0, bossStarts, permission + " denies actual StartBossPush");
            Equal(null, Property(MainInstance, "FightPreparationIdentity"), permission + " cannot acquire preparation");
            Equal(false, Get(MainInstance, "_customPreRebirthPrepCompleted"), permission + " denial is not completion");
            Equal(0, fruits.Cast<object>().Sum(fruit => (int)Get(fruit, "harvests")), permission + " preserves native fruit opportunities");
            allow(true);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), permission + " restoration admits the same due threshold");
            Equal(19, calls, permission + " restored permission invokes available fruits once");
            Equal(1, bossStarts, permission + " restored permission admits one real boss process");
        }
        finally { allow(true); RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void CheckLegacyRebirthPrepReadiness()
    {
        var plan = PrepareTimedRebirthPlan("24hr", out _);
        Equal(0, Get(plan, "RebirthPrepStartSeconds"), "shipped 24h plan retains zero prep default");
        Equal(86400, Get(plan, "TimeTriggerSeconds"), "legacy final time remains 24 hours");
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        RebirthBossStarting = () => bossStarts++;
        try
        {
            foreach (var seconds in new[] { 3600d, 86399.999d, 86400d })
            {
                PostRebirthClock(seconds);
                Equal(false, CallMain("RunCustomRebirthAutomation", plan), "zero-default prep waits for full legacy readiness/" + seconds);
                Equal(0, calls, "zero-default does not harvest ahead of readiness/" + seconds);
                Equal(0, bossStarts, "zero-default does not fight ahead of readiness/" + seconds);
                Equal(null, Property(MainInstance, "FightPreparationIdentity"), "zero-default has no timed attempt/" + seconds);
            }
            PostRebirthClock(86415);
            Equal(true, CallMain("ObserveCustomRebirthReadiness", plan), "legacy NUMBER confirmation eventually makes 24h plan ready");
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "zero-default still starts prep from final readiness");
            Equal(19, calls, "legacy ready path invokes all available native fruits");
            Equal(1, bossStarts, "legacy ready path admits exactly one boss process");
            Set(Character, "nextAttackMulti", 0d);
            Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), "legacy readiness can become false again");
            Equal(false, Property(MainInstance, "BossPushRunning"), "zero-default retains legacy cancellation when readiness is lost");
            Equal(false, Get(MainInstance, "_customPreRebirthPrepCompleted"), "legacy readiness reset grants no completion");
        }
        finally { RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void CheckLegacyCompletedRebirthConfirmation(bool permissionOff)
    {
        var plan = PrepareTimedRebirthPlan("24hr", out var fruits);
        var rebirth = Plugin.GetType("NGUInjector.AllocationProfiles.RebirthStuff.CustomPlanRebirth", true);
        var settings = Get(MainType, "Settings"); var boss = Get(Character, "bossController");
        var previousProfile = Get(MainType, "_profile");
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        RebirthBossStarting = () => bossStarts++;
        try
        {
            Equal(0, Get(plan, "RebirthPrepStartSeconds"), "stock 24h completion uses the zero-default path");
            Equal(true, Get(plan, "NumberTriggerEnabled"), "stock 24h completion retains NUMBER confirmation");
            PostRebirthClock(86400);
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "stock readiness starts pre-prep NUMBER confirmation");
            PostRebirthClock(86415);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "preconfirmed stock 24h plan starts actual preparation");
            Equal(true, Property(MainInstance, "BossPushRunning"), "stock 24h prep admits a real boss process");
            Equal(19, calls, "stock 24h prep actually processes available native fruits");
            var confirmed = Get(rebirth, "_numberConfirmationConfirmedKey");
            Equal("24hr:True:1:1", confirmed, "stock NUMBER really was confirmed before boss completion");
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            var steps = Get(attempt, "FruitStepsDone"); var identity = Property(MainInstance, "FightPreparationIdentity");
            PostRebirthClock(86420); CallMain("StopBossPush");
            Equal(true, Get(attempt, "Completed"), "native boss retirement completes the original prep attempt");
            Equal(false, Get(MainInstance, "_customPreRebirthPrepCompleted"), "coordinator has not yet published completion after StopBossPush");
            Equal(confirmed, Get(rebirth, "_numberConfirmationConfirmedKey"), "retirement race starts with an existing confirmed NUMBER window");
            if (permissionOff)
            {
                Set(settings, "_autoRebirth", false);
                Equal(false, CallMain("RunCustomRebirthAutomation", plan), "permission-off coordinator admits no final rebirth");
                Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), "permission-off observation still publishes genuine completion");
                Equal(false, Get(attempt, "Cancelled"), "permission-off completion publication is not cancellation");
                Equal(-1d, Get(rebirth, "_numberConfirmationStartedAt"), "permission-off publication resets the old confirmation window");
                Equal(null, Get(rebirth, "_numberConfirmationConfirmedKey"), "permission-off completion cannot preserve pre-prep confirmation");
                PostRebirthClock(86421); Set(settings, "_autoRebirth", true);
            }
            var window = permissionOff ? 86421d : 86420d;
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "completed zero-default prep waits for fresh NUMBER confirmation");
            Equal(window, Get(rebirth, "_numberConfirmationStartedAt"), "fresh window begins before readiness is evaluated on completion/recovery");
            Equal(null, Get(rebirth, "_numberConfirmationConfirmedKey"), "old confirmed key is not reused by completed stock prep");
            for (var i = 0; i < 19; i++) Set(fruits[i], "seconds", 3600f);
            var profile = Activator.CreateInstance(Plugin.GetType("NGUInjector.AllocationProfiles.CustomAllocation", true),
                All, null, new object[] { (string)Get(MainType, "_dir"), "zero-prep-confirmation" }, null);
            Set(MainType, "_profile", profile);
            foreach (var seconds in new[] { window, window + 1d, window + 14.999d, window + 15d, window + 20d })
            {
                PostRebirthClock(seconds); CallMain("BeginRebirthAutomationPass");
                // At renewed readiness, exercise the real profile's native-busy guard, not a fake rebirth.
                Set(boss, "isFighting", seconds >= window + 15d);
                Equal(false, CallMain("RunCustomRebirthAutomation", plan), "stock coordinator waits for confirmation or native combat/" + seconds);
                Equal(window, Get(rebirth, "_numberConfirmationStartedAt"), "stock completed prep never restarts the new window/" + seconds);
                Equal(seconds >= window + 15d ? confirmed : null, Get(rebirth, "_numberConfirmationConfirmedKey"),
                    "stock coordinator regains confirmed readiness at exactly fifteen seconds/" + seconds);
                Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), "stock completion survives renewed confirmation/" + seconds);
                Equal(false, Get(MainInstance, "_customPreRebirthPrepRunning"), "stock completed prep is not running again/" + seconds);
                Equal(false, Get(attempt, "Cancelled"), "stock completed attempt never becomes cancelled/" + seconds);
                Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "stock coordinator retains original attempt/" + seconds);
                Check(ReferenceEquals(steps, Get(attempt, "FruitStepsDone")), "stock coordinator retains original spent fruit steps/" + seconds);
                Equal(identity, Property(MainInstance, "FightPreparationIdentity"), "stock completion retains its original identity/" + seconds);
                Equal(19, calls, "stock confirmation cannot replay fruits even after a new cycle ripens/" + seconds);
                Equal(1, bossStarts, "stock confirmation never restarts the completed boss push/" + seconds);
                Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), "confirmation and native combat guards withhold actual rebirth/" + seconds);
            }
        }
        finally
        {
            Set(MainType, "_profile", previousProfile); Set(settings, "_autoRebirth", true); Set(boss, "isFighting", false);
            RebirthFruitInvoking = null; RebirthBossStarting = null;
        }
    }

    private static void CheckCancelledTimedRebirthPrep(bool started)
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out _);
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => {
            calls++;
            if (!started && id == 3) throw new InvalidOperationException("uncertain timed preparation fruit");
        };
        RebirthBossStarting = () => bossStarts++;
        try
        {
            PostRebirthClock(3600);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "timed prep waits for fight or uncertain fruit");
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            if (started) CallMain("ResetCustomPreRebirthPrep");
            Equal(true, Get(attempt, "Cancelled"), "timed prep is genuinely cancelled/started=" + started);
            Equal(false, Get(MainInstance, "_customPreRebirthPrepRunning"), "cancelled timed prep is not running/started=" + started);
            Equal(false, Get(MainInstance, "_customPreRebirthPrepCompleted"), "cancelled timed prep cannot grant completion/started=" + started);
            Equal(false, Get(attempt, "Completed"), "cancelled original attempt is not marked completed/started=" + started);
            Equal(false, Property(MainInstance, "BossPushRunning"), "cancelled timed prep has no boss process/started=" + started);
            Equal(started ? 19 : 4, calls, "cancellation retains actual native fruit invocation count/started=" + started);
            Equal(started ? 1 : 0, bossStarts, "cancellation retains actual boss-start count/started=" + started);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "cancelled timed prep keeps final rebirth blocked/started=" + started);
            Equal(false, Get(MainInstance, "_customPreRebirthPrepCompleted"), "cancelled retry cannot fabricate completion/started=" + started);
            Equal(started ? 19 : 4, calls, "cancelled retry never repeats native fruit/started=" + started);
            Equal(started ? 1 : 0, bossStarts, "cancelled retry never re-enters boss push/started=" + started);
            Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), "cancelled prep never invokes native rebirth/started=" + started);
        }
        finally { RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void SetTimedPrepPermission(object plan, string permission, bool allow)
    {
        var settings = Get(MainType, "Settings");
        if (permission == "custom plan") Set(settings, "_useCustomRebirthPlan", allow);
        else if (permission == "auto rebirth") Set(settings, "_autoRebirth", allow);
        else if (permission == "plan rebirth") Set(plan, "AutoRebirthEnabled", allow);
        else Set(Get(Get(Character, "challenges"), "noRebirthChallenge"), "inChallenge", !allow);
    }

    private static void CheckCompletedTimedPrepPermissionRecovery(string permission)
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out var fruits);
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        RebirthBossStarting = () => bossStarts++;
        try
        {
            PostRebirthClock(3600);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "completed recovery starts actual timed prep/" + permission);
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            var steps = Get(attempt, "FruitStepsDone"); var identity = Property(MainInstance, "FightPreparationIdentity");
            CallMain("StopBossPush");
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "completed timed prep waits for final time/NUMBER/" + permission);
            Equal(true, Get(attempt, "Completed"), "original timed attempt genuinely completed/" + permission);
            for (var i = 0; i < 19; i++) Set(fruits[i], "seconds", 3600f);
            PostRebirthClock(3610); SetTimedPrepPermission(plan, permission, false);
            Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), "revoked permission withholds readiness/" + permission);
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "revoked permission admits no automatic work/" + permission);
            Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), "permission loss retains completed prep/" + permission);
            Equal(false, Get(attempt, "Cancelled"), "permission loss does not cancel completed identity/" + permission);
            Equal(19, calls, "permission loss repeats no fruit/" + permission);
            PostRebirthClock(3659); SetTimedPrepPermission(plan, permission, true);
            Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), "permission recovery cannot bypass final readiness/" + permission);
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "completed recovery still waits before 3660/" + permission);
            Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), "completion survives permission off/on/" + permission);
            Equal(false, Get(attempt, "Cancelled"), "recovered completed attempt remains uncancelled/" + permission);
            Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "completed recovery retains original attempt/" + permission);
            Check(ReferenceEquals(steps, Get(attempt, "FruitStepsDone")), "completed recovery retains original fruit steps/" + permission);
            Equal(identity, Property(MainInstance, "FightPreparationIdentity"), "completed recovery retains identity/" + permission);
            Equal(19, calls, "completed recovery never consumes a later fruit cycle/" + permission);
            Equal(1, bossStarts, "completed recovery never restarts boss push/" + permission);
            Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), "permission recovery never fabricates final rebirth permission/" + permission);
        }
        finally { SetTimedPrepPermission(plan, permission, true); RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void CheckRunningTimedPrepPermissionRecovery(string permission, bool nativeBusy, bool retireFromTick = false)
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out var fruits);
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        RebirthBossStarting = () => bossStarts++;
        var boss = Get(Character, "bossController");
        try
        {
            PostRebirthClock(3600);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "running recovery starts actual timed prep");
            var attempt = Get(MainInstance, "_fightPreparationAttempt"); var steps = Get(attempt, "FruitStepsDone");
            var claim = Property(MainInstance, "FightPreparationClaim"); var process = Get(MainInstance, "_bossPushProcess");
            var gear = Get(MainInstance, "_fightPreparationGear"); var support = Get(MainInstance, "_fightPreparationSupport");
            var identity = Property(MainInstance, "FightPreparationIdentity");
            var score = Get(attempt, "FightScoreMode"); var maxed = Get(attempt, "RequireMaxedFruit");
            var searches = InlineGearSelections;
            Set(boss, "isFighting", nativeBusy);
            SetTimedPrepPermission(plan, permission, false);
            if (retireFromTick) CallMain("BossPushTick");
            Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), "revocation withholds final readiness");
            Equal(false, Get(attempt, "Cancelled"), "permission suspension is not explicit cancellation");
            Equal(false, Get(attempt, "Completed"), "permission suspension grants no attempt completion");
            Equal(false, Get(MainInstance, "_customPreRebirthPrepCompleted"), "permission suspension grants no prep completion");
            Equal(false, Property(MainInstance, "FightPreparationVerified"), "revocation withholds dependent fight authority");
            Equal(nativeBusy, Property(MainInstance, "BossPushRunning"), "retirement waits for actual native combat only");
            if (nativeBusy)
            {
                Check(ReferenceEquals(claim, Property(MainInstance, "FightPreparationClaim")), "busy native fight retains original exact claim");
                Check(ReferenceEquals(process, Get(MainInstance, "_bossPushProcess")), "busy native fight retains original route process");
            }
            else Equal(null, Property(MainInstance, "FightPreparationClaim"), "safe suspension retires the original claim");
            for (var i = 0; i < 19; i++) Set(fruits[i], "seconds", 3600f);
            // Changed plan preferences cannot replace options acquired by the original attempt.
            Set(plan, "PrepRequireMaxedFruit", !(bool)maxed);
            Set(plan, "FightScoreMode", (int)score == 0 ? 1 : 0);
            SetTimedPrepPermission(plan, permission, true); PostRebirthClock(3659);
            if (nativeBusy)
            {
                Equal(true, CallMain("RunCustomRebirthAutomation", plan), "restored permission still waits for native retirement");
                Equal(false, Get(attempt, "Completed"), "busy restoration fabricates no completion");
                Equal(1, bossStarts, "native-busy restoration admits no replacement boss");
            }
            Set(boss, "isFighting", false);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "restored permission resumes original timed preparation");
            Equal(true, Property(MainInstance, "BossPushRunning"), "resumed preparation admits a live boss process");
            Equal(true, Property(MainInstance, "FightPreparationVerified"), "resume requires fresh exact authority");
            Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "resume retains original bounded attempt");
            Check(ReferenceEquals(steps, Get(attempt, "FruitStepsDone")), "resume retains original spent fruit steps");
            Check(ReferenceEquals(gear, Get(MainInstance, "_fightPreparationGear")), "resume retains originally selected gear");
            Check(ReferenceEquals(support, Get(MainInstance, "_fightPreparationSupport")), "resume retains originally selected support");
            Equal(identity, Property(MainInstance, "FightPreparationIdentity"), "resume retains original identity");
            Equal(score, Get(attempt, "FightScoreMode"), "resume retains acquired score option");
            Equal(maxed, Get(attempt, "RequireMaxedFruit"), "resume retains acquired fruit safety option");
            Equal(searches, InlineGearSelections, "resume performs no replacement gear search");
            Equal(true, Get(claim, "Ended"), "old claim is actually retired before resume");
            Check(!ReferenceEquals(claim, Property(MainInstance, "FightPreparationClaim")), "resume acquires new verified route authority rather than reviving an ended claim");
            Equal(19, calls, "resume never repeats a harvested native fruit");
            Equal(2, bossStarts, "running recovery admits exactly one resumed boss process");
            for (var i = 0; i < 19; i++) Equal(1, Get(fruits[i], "harvests"), "original fruit opportunity consumed only once/" + i);
            CallMain("StopBossPush");
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "resumed completion still respects final readiness");
            Equal(true, Get(attempt, "Completed"), "only actual resumed completion finishes the attempt");
        }
        finally { Set(boss, "isFighting", false); SetTimedPrepPermission(plan, permission, true); RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void CheckExplicitPrepAfterUncertainOpportunityChange(string change, bool rebirth)
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out var fruits);
        Set(plan, "PrepRequireMaxedFruit", false);
        var settings = Get(MainType, "Settings");
        Invoke(Get(MainType, "CustomPlanStore"), "Save", Property(settings, "CustomPlanFile"), plan);
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => { calls++; if (id == 3) throw new InvalidOperationException("uncertain original leaf"); };
        RebirthBossStarting = () => bossStarts++;
        try
        {
            PostRebirthClock(3600);
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "uncertain original auto prep blocks final rebirth");
            var original = Get(MainInstance, "_fightPreparationAttempt");
            Equal(true, Get(original, "Cancelled"), "original ambiguous attempt is cancelled");
            Equal(false, Get(original, "Completed"), "ambiguous attempt is never completed");
            RebirthFruitInvoking = (id, eat) => calls++;
            CallMain("RunFightPrep", plan, true, "unchanged ambiguous explicit request", null, "unchanged-opportunity");
            Equal(4, calls, "a new identity alone cannot replay the unchanged ambiguous leaf");
            Equal(0, bossStarts, "unchanged ambiguous opportunity cannot start another boss process");
            Check(ReferenceEquals(original, Get(MainInstance, "_fightPreparationAttempt")), "unchanged ambiguity retains original evidence");
            Set(MainInstance, "_currentExecutionCommandId", "unchanged-explicit-ingress");
            CallMain(rebirth ? "RunManualRebirth" : "RunManualFightPrep");
            Equal(4, calls, "identified manual ingress also cannot replay the unchanged ambiguous leaf");
            Equal(0, bossStarts, "identified manual ingress cannot fight over unchanged ambiguity");
            Check(ReferenceEquals(original, Get(MainInstance, "_fightPreparationAttempt")), "manual ingress retains unresolved original evidence");
            CallMain("ResetCustomPreRebirthPrep");
            Equal(false, CallMain("HarvestFruitAction", 3, false, false, null, null), "explicit reset never clears uncertainty");
            Equal(4, calls, "reset cannot replay the ambiguous leaf");
            if (change == "counter") Set(fruits[3], "harvests", 1);
            else if (change == "object") fruits[3] = Invoke(fruits[3], "MemberwiseClone");
            else
            {
                var stats = Get(Character, "stats");
                Set(stats, "rebirthNumber", (long)Get(stats, "rebirthNumber") + 1);
            }
            Set(MainInstance, "_currentExecutionCommandId", "fresh-after-" + change);
            var result = (string)CallMain(rebirth ? "RunManualRebirth" : "RunManualFightPrep");
            Check(result.Contains("boss push running"), "changed native opportunity permits fresh explicit preparation: " + result);
            Equal(true, Property(MainInstance, "BossPushRunning"), "fresh explicit request admits a real boss process");
            Check(!ReferenceEquals(original, Get(MainInstance, "_fightPreparationAttempt")), "changed opportunity acquires a fresh bounded attempt");
            Equal(rebirth ? 20 : 4, calls, "fresh request consumes only permitted current opportunities");
            Equal(1, bossStarts, "fresh explicit request admits exactly one boss process");
            Equal(rebirth ? change == "counter" ? 2 : 1 : change == "counter" ? 1 : 0,
                Get(fruits[3], "harvests"), "fresh request does not fabricate or duplicate original harvest accounting");
        }
        finally { Set(MainInstance, "_currentExecutionCommandId", null); RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void CheckProtectedTimedRebirthGate(bool late)
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out var fruits);
        Set(plan, "StageSchedulerEnabled", true);
        var stages = ((Array)Get(plan, "Stages")).Cast<object>().ToArray();
        Equal(3600, stages.Sum(stage => (int)Get(stage, "DurationSeconds")), "61m protects exactly one hour, not its whole horizon");
        Equal(60, Get(stages[3], "DurationSeconds"), "final augment stage protects sixty applied seconds");
        Equal(false, Get(stages[3], "UseWandoosWhenTimeMachineLocked"), "final augment minute cannot use locked-TM fallback");
        Equal(0, Get(stages[4], "DurationSeconds"), "61m prep hold is flexible rather than another protected minute");
        var start = late ? 3700d : 3540d;
        PostRebirthClock(start);
        CallMain("ForgetStageProgress", Property(Get(MainType, "Settings"), "CustomPlanFile"));
        Set(Get(Character, "machine"), "realBaseGold", 1e9d);
        Sequence(Get(Character, "bloodMagicController"), "normalSpeedDividers", 1, .018f);
        var calls = 0; var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        RebirthBossStarting = () => {
            bossStarts++;
            Equal(true, CallMain("HasCompletedProtectedStageWork", plan), "actual boss admission requires durable protected completion");
            for (var i = 0; i < fruits.Count; i++)
                Equal(i < 19 ? 1 : 0, Get(fruits[i], "harvests"), "protected completion still harvests available fruits before boss/" + i);
        };
        try
        {
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "absolute deadline alone cannot infer unobserved protected work");
            Equal(null, Property(MainInstance, "FightPreparationIdentity"), "unobserved work cannot acquire prep");
            RunVerifiedInline(plan, "late enable selects and applies the first actual protected stage");
            var scheduler = Get(MainInstance, "_protectedScheduler");
            Equal(0, Property(scheduler, "StageIndex"), "late enable never jumps directly to the wall-clock stage");
            Equal("0,0,0,0,0", string.Join(",", (double[])Property(scheduler, "EarnedSeconds")), "late enable invents no historical applied credit");
            Equal(false, CallMain("HasCompletedProtectedStageWork", plan), "initial real application is not protected completion");
            // Use the real explicit stage selection to isolate the final minute from earlier rows.
            Invoke(scheduler, "OverrideStage", plan, "set", 3, start, MainType.GetProperty("ProtectedHeartbeat", All).GetValue(null));
            RunVerifiedInline(plan, "actual final augment minute is applied before credit");
            Equal(StageGearGoal("Augments"), Get(Get(MainInstance, "_inlineStageSelection"), "GearGoal"), "final minute selects real augment setup");
            Check(((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>().Any(x => (long)Get(x, "augEnergy") > 0),
                "final protected minute actually funds native augments");
            foreach (var delta in new[] { 10d, 10d, 10d, 10d, 10d, 9.999d })
            {
                AdvanceLockedStageClock(delta); RunVerifiedInline(plan, "final augment applied credit");
                Equal(3, Property(scheduler, "StageIndex"), "incomplete final minute retains original stage");
                Equal(false, CallMain("RunCustomRebirthAutomation", plan), "prep cannot preempt final protected augment time");
                Equal(0, calls, "protected gate preserves every original fruit");
                Equal(0, bossStarts, "protected gate withholds actual StartBossPush");
            }
            Check(Math.Abs(((double[])Property(scheduler, "EarnedSeconds"))[3] - 59.999d) < .01d, "final minute earned actual applied time, not its wall-clock age");
            AdvanceLockedStageClock(.001d); RunVerifiedInline(plan, "final protected augment minute completes");
            Equal(true, ((bool[])Property(scheduler, "Completed"))[3], "final protected minute really completes");
            Equal(false, ((bool[])Property(scheduler, "Skipped"))[3], "final protected minute is earned, not skipped");
            Equal(true, CallMain("HasCompletedProtectedStageWork", plan), "completed final minute opens durable protection gate");
            Equal(late ? 3 : 4, Property(scheduler, "StageIndex"), "on-time completion enters flexible hold; late completion skips unfunded hold");
            Equal(late, Property(scheduler, "Finished"), "on-time preparation need not wait for the flexible hold to finish");
            Equal(start + 60d, Get(Get(Character, "rebirthTime"), "totalseconds"), "final augment minute retains its own sixty-second deadline");
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "real automation starts prep immediately after protected completion");
            Equal(true, Property(MainInstance, "BossPushRunning"), "completed final minute admits actual boss process");
            Equal(19, calls, "protected completion processes original fruits exactly once");
            Equal(1, bossStarts, "protected completion admits one boss push");
            Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), "prep admission is not final rebirth permission");
        }
        finally { RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static object PrepareRebirthAutomationRoutine(object plan)
    {
        var settings = Get(MainType, "Settings");
        foreach (var field in new[] { "_upgradeDiggers", "_customAutoExpEnabled", "_customAutoBoostEnabled", "_customAutoMoneyPitEnabled",
            "_customAutoBloodSpellsEnabled", "_manualTitanEnabled", "_swapYggdrasilLoadouts" }) Set(settings, field, false);
        Set(plan, "AutoActivateFruits", false); Set(plan, "AutoBloodSpellsEnabled", false);
        Set(MainInstance, "_yggManager", New("YggdrasilManager"));
        Set(Get(Character, "machine"), "realBaseGold", 1e9d);
        Sequence(Get(Character, "bloodMagicController"), "normalSpeedDividers", 1, .018f);
        var store = Get(MainType, "CustomPlanStore"); var name = Property(settings, "CustomPlanFile");
        Invoke(store, "Save", name, plan);
        Set(MainInstance, "_customRuntimePlanCache", null);
        return Invoke(store, "Load", name);
    }

    private static void CheckOrdinaryRebirthHarvestReservation(bool protectedPending)
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out var fruits);
        Set(plan, "StageSchedulerEnabled", protectedPending);
        if (protectedPending) CallMain("ForgetStageProgress", Property(Get(MainType, "Settings"), "CustomPlanFile"));
        plan = PrepareRebirthAutomationRoutine(plan);
        foreach (var id in new[] { 1, 6 }) Set(fruits[id], "maxTier", 1L);
        var ordinary = 0; var bossStarts = 0; var calls = new List<string>();
        OrdinaryRebirthHarvestStarting = () => ordinary++;
        RebirthFruitInvoking = (id, eat) => calls.Add(id + ":" + eat);
        RebirthBossStarting = () => {
            bossStarts++;
            Equal(1, ordinary, "no ordinary harvest entry steals fruits on the prep-deadline pass");
            Equal(2, Get(fruits[1], "harvests"), "Alpha native prep reward commits before boss start");
            Equal(2, Get(fruits[6], "harvests"), "Beta native prep reward commits before boss start");
        };
        try
        {
            PostRebirthClock(3599); InlineObservedError = null;
            CallMain("AutomationRoutine");
            Equal(null, InlineObservedError, "pre-deadline full automation completes without swallowed scene errors");
            Equal(1, ordinary, "ordinary production callsite remains active before absolute deadline");
            Equal("1:False,6:False", string.Join(",", calls), "ordinary native harvest uses power fruits' harvest preferences");
            Equal(0, bossStarts, "pre-deadline pass does not start prep");
            foreach (var id in new[] { 1, 6 }) Set(fruits[id], "seconds", 3600f);
            PostRebirthClock(3600); InlineObservedError = null;
            CallMain("AutomationRoutine");
            Equal(null, InlineObservedError, "deadline full automation completes without swallowed scene errors");
            Equal(1, ordinary, "absolute prep deadline reserves fruits at actual ordinary-harvest callsite");
            Equal(protectedPending ? 0 : 1, bossStarts, "protected gate controls boss admission independently of reservation");
            Equal(!protectedPending, Property(MainInstance, "BossPushRunning"), "full routine reaches expected actual boss state");
            if (protectedPending)
            {
                Equal(false, CallMain("HasCompletedProtectedStageWork", plan), "reservation does not fabricate protected completion");
                Equal(0, Property(Get(MainInstance, "_protectedScheduler"), "StageIndex"), "delayed protected first stage remains selected");
                Equal(null, Property(MainInstance, "FightPreparationIdentity"), "reserved fruits alone do not grant prep authority");
                Equal("1:False,6:False", string.Join(",", calls), "pending protected work leaves both native power opportunities unspent");
                foreach (var id in new[] { 1, 6 }) Equal(1, Get(fruits[id], "harvests"), "reserved power harvest counter unchanged/" + id);
            }
            else
            {
                Equal("1:False,6:False," + string.Join(",", Enumerable.Range(0, 19).Select(i => i + ":" + (i == 0 || i == 1 || i == 6))),
                    string.Join(",", calls), "full loop harvests all available prep fruits and force-eats both powers before boss");
                Equal(true, Get(MainInstance, "_customPreRebirthPrepRunning"), "full routine admits the real automatic prep owner");
            }
        }
        finally { OrdinaryRebirthHarvestStarting = null; RebirthFruitInvoking = null; RebirthBossStarting = null; }
    }

    private static void CheckPendingPrepHarvestReservation()
    {
        var plan = PrepareTimedRebirthPlan("24hr", out var fruits);
        plan = PrepareRebirthAutomationRoutine(plan);
        foreach (var id in new[] { 1, 6 }) Set(fruits[id], "maxTier", 1L);
        var ordinary = 0; var calls = 0;
        OrdinaryRebirthHarvestStarting = () => ordinary++;
        RebirthFruitInvoking = (id, eat) => calls++;
        try
        {
            PostRebirthClock(3600);
            var button = Get(Get(Character, "buttons"), "yggdrasil"); Set(button, "m_Interactable", false);
            var result = (string)CallMain("RunFightPrep", plan, true, "pending consume preparation", null, "pending-reservation");
            Check(result.Contains("waiting for fruit prerequisites"), "pending reservation captures a real uninvoked fruit attempt: " + result);
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            Equal(true, Get(attempt, "ConsumePowerFruits"), "pending attempt really reserves fruit consumption");
            Equal(false, Get(attempt, "Completed"), "uninvoked attempt is not completed");
            Set(button, "m_Interactable", true); InlineObservedError = null;
            CallMain("AutomationRoutine");
            Equal(null, InlineObservedError, "pending reservation runs the full production loop without scene errors");
            Equal(0, Get(plan, "RebirthPrepStartSeconds"), "pending reservation is independent of a timed deadline");
            Equal(0, ordinary, "pending consume attempt reserves fruits at the real ordinary callsite");
            Equal(0, calls, "full routine does not steal the pending attempt's native fruit opportunities");
            Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "ordinary loop retains original pending consumption attempt");
            result = (string)CallMain("RunFightPrep", plan, true, "pending consume preparation", null, "pending-reservation");
            Check(result.Contains("boss push running"), "original pending attempt can still consume and fight: " + result);
            Equal(19, calls, "pending attempt consumes original available opportunities once");
            Equal(1, Get(fruits[1], "harvests"), "pending Alpha not stolen or duplicated");
            Equal(1, Get(fruits[6], "harvests"), "pending Beta not stolen or duplicated");
        }
        finally { OrdinaryRebirthHarvestStarting = null; RebirthFruitInvoking = null; }
    }

    private static void CheckPostPrepNumberConfirmation()
    {
        var plan = PrepareTimedRebirthPlan("15m-NUMBER-increase", out _);
        var rebirth = Plugin.GetType("NGUInjector.AllocationProfiles.RebirthStuff.CustomPlanRebirth", true);
        var attack = (double)Get(Character, "attackMulti"); Set(Character, "nextAttackMulti", attack * 2);
        PostRebirthClock(900);
        Equal(false, CallMain("ObserveCustomRebirthReadiness", plan), "pre-prep NUMBER starts its original confirmation window");
        PostRebirthClock(915);
        Equal(true, CallMain("ObserveCustomRebirthReadiness", plan), "NUMBER is genuinely preconfirmed before fruit and fight");
        Equal(900d, Get(rebirth, "_numberConfirmationStartedAt"), "preconfirmed window has its original native rebirth timestamp");
        RebirthFruitInvoking = (id, eat) => {
            if (id == 18) Set(Character, "nextAttackMulti", attack * 2.1d);
        };
        try
        {
            Equal(true, CallMain("RunCustomRebirthAutomation", plan), "preconfirmed readiness still runs actual fruit/boss prep first");
            Equal(true, Property(MainInstance, "BossPushRunning"), "preconfirmed preparation admits a real boss process");
            PostRebirthClock(920); CallMain("StopBossPush");
            Equal(false, CallMain("RunCustomRebirthAutomation", plan), "completed prep cannot reuse pre-fruit NUMBER confirmation");
            Equal(true, Get(MainInstance, "_customPreRebirthPrepCompleted"), "actual preparation transitions to completed");
            Equal(920d, Get(rebirth, "_numberConfirmationStartedAt"), "new NUMBER window begins exactly at actual prep completion");
            Equal(null, Get(rebirth, "_numberConfirmationConfirmedKey"), "old confirmed readiness is withdrawn after fruit/fight");
            Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), "old confirmation cannot invoke final rebirth");
            foreach (var seconds in new[] { 920d, 925d, 934.999d })
            {
                PostRebirthClock(seconds); CallMain("BeginRebirthAutomationPass");
                Equal(false, CallMain("RunCustomRebirthAutomation", plan), "post-prep NUMBER requires a full new fifteen seconds/" + seconds);
                Equal(920d, Get(rebirth, "_numberConfirmationStartedAt"), "completed prep resets confirmation only once/" + seconds);
                Equal(false, Property(MainInstance, "RebirthAttemptedThisPass"), "new confirmation window withholds actual rebirth/" + seconds);
            }
            PostRebirthClock(935);
            Equal(true, CallMain("ObserveCustomRebirthReadiness", plan), "new confirmation opens exactly fifteen seconds after prep, without perpetual resets");
            Equal(920d, Get(rebirth, "_numberConfirmationStartedAt"), "confirmed post-prep window retains its original start");
        }
        finally { RebirthFruitInvoking = null; }
    }

    private static void CheckBuiltInRebirthPlanDeletion(string name)
    {
        var plan = PrepareTimedRebirthPlan(name, out _);
        var store = Get(MainType, "CustomPlanStore"); var settings = Get(MainType, "Settings");
        Set(plan, "StageSchedulerEnabled", true); Set(settings, "_customPlanFile", name);
        CallMain("GetProtectedStage", plan);
        var scheduler = Get(MainInstance, "_protectedScheduler");
        var planPath = (string)Invoke(store, "GetPlanPath", name);
        var checkpointPath = (string)Property(MainInstance, "ProtectedCheckpointPath");
        Equal(true, File.Exists(planPath), "built-in deletion fixture has actual persisted plan/" + name);
        Equal(true, File.Exists(checkpointPath), "built-in deletion fixture has actual scheduler checkpoint/" + name);
        var planBytes = File.ReadAllBytes(planPath); var checkpointBytes = File.ReadAllBytes(checkpointPath);
        var mixedCase = new string(name.Select((letter, index) => index % 2 == 0 ? char.ToUpperInvariant(letter) : char.ToLowerInvariant(letter)).ToArray());
        Check(mixedCase != name, "built-in deletion case exercises a distinct mixed-case spelling/" + name);
        foreach (var spelling in new[] { name, mixedCase })
        foreach (var method in new[] { "DeletePlan", "DeleteExternalPlan" })
        {
            var label = method + "/" + spelling;
            Throws(() => Invoke(store, method, spelling), "Built-in plans cannot be deleted", "native store rejects built-in deletion/" + label);
            Equal(true, File.Exists(planPath), "rejected built-in deletion retains actual plan file/" + label);
            Check(planBytes.SequenceEqual(File.ReadAllBytes(planPath)), "rejected built-in deletion preserves exact plan bytes/" + label);
            Equal(true, File.Exists(checkpointPath), "rejected built-in deletion retains actual checkpoint/" + label);
            Check(checkpointBytes.SequenceEqual(File.ReadAllBytes(checkpointPath)), "rejected built-in deletion preserves exact checkpoint bytes/" + label);
            Check(ReferenceEquals(scheduler, Get(MainInstance, "_protectedScheduler")), "rejected built-in deletion cannot forget active scheduler/" + label);
            Equal(name, Property(settings, "CustomPlanFile"), "rejected built-in deletion leaves active selection unchanged/" + label);
        }
    }

    private static void CheckNativeStageCloneFallback()
    {
        var plan = PrepareTimedRebirthPlan("1h-1m-NUMBER-increase", out _);
        var source = ((Array)Get(plan, "Stages")).GetValue(0);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", false);
        foreach (var enabled in new[] { true, false })
        {
            Set(source, "UseWandoosWhenTimeMachineLocked", enabled);
            var clone = Invoke(Plugin.GetType("NGUInjector.SettingsForm", true), "CloneStage", source);
            Check(!ReferenceEquals(source, clone), "native editor cloning creates a distinct stage/" + enabled);
            Equal(enabled, Get(clone, "UseWandoosWhenTimeMachineLocked"), "native editor clone preserves fallback opt-in/" + enabled);
            Equal(Get(source, "DurationSeconds"), Get(clone, "DurationSeconds"), "native editor clone preserves protected timing/" + enabled);
            Equal(enabled ? "WAN" : "TM,WAN,AUG", string.Join(",", StagePriorities(clone, true)), "cloned stage retains actual locked-TM routing behavior/" + enabled);
            Set(clone, "UseWandoosWhenTimeMachineLocked", !enabled);
            Equal(enabled, Get(source, "UseWandoosWhenTimeMachineLocked"), "editing clone cannot mutate source fallback/" + enabled);
        }
    }

    private static void CheckAutomaticRebirthFruitOrder(bool renderFailure)
    {
        var plan = PrepareRebirthFruits(out var fruits);
        Set(Get(MainType, "Settings"), "_autoRebirth", true); Set(plan, "AutoRebirthEnabled", true);
        var controller = ((Array)Get(Get(Character, "yggdrasilController"), "fruits")).GetValue(0);
        Equal(false, Invoke(controller, "fruitMaxxed", 1), "one-hour power fruit is not maxed");
        var calls = new List<string>(); var bossStarts = 0;
        RebirthFruitInvoking = (id, eat) => {
            Equal(false, Property(MainInstance, "BossPushRunning"), "every native fruit leaf precedes boss admission/" + id);
            Equal(0, bossStarts, "no boss-start invocation precedes a fruit leaf/" + id);
            calls.Add(id + ":" + eat);
        };
        RebirthBossStarting = () => {
            bossStarts++;
            for (var i = 0; i < fruits.Count; i++)
                Equal(i < 19 ? 1 : 0, Get(fruits[i], "harvests"), "all available fruits completed before actual StartBossPush/" + i);
        };
        if (renderFailure) Program.FiniteFruitRendered = () => { throw new InvalidOperationException("fruit model committed before scene failure"); };
        try
        {
            Equal(true, CallMain("RunCustomPreRebirthPrepIfNeeded", plan), "callable automatic prep starts before final readiness");
            Equal(true, Property(MainInstance, "BossPushRunning"), "verified fruit effects admit real boss process");
            Equal(1, bossStarts, "one preparation starts one boss process");
            Equal(string.Join(",", Enumerable.Range(0, 19).Select(i => i + ":" + (i == 0 || i == 1 || i == 6))),
                string.Join(",", calls), "native choices preserve non-power preference and force-eat both power fruits");
            CallMain("RunCustomPreRebirthPrepIfNeeded", plan);
            Equal(19, calls.Count, "running prep never repeats fruit leaves");
            CallMain("StopBossPush");
            Equal(false, CallMain("RunCustomPreRebirthPrepIfNeeded", plan), "finished push releases prep for final readiness evaluation");
            Equal(19, calls.Count, "completed prep never harvests a later opportunity");
        }
        finally { RebirthFruitInvoking = null; RebirthBossStarting = null; Program.FiniteFruitRendered = null; }
    }

    private static void CheckManualFightLeavesFruits()
    {
        var plan = PrepareRebirthFruits(out var fruits); var calls = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        try
        {
            var result = (string)CallMain("RunFightPrep", plan, false, "manual fruit-free prep", null, "fruit-free-request");
            Check(result.Contains("boss push running"), "manual fight still admits boss process: " + result);
            Equal(0, calls, "manual fight prep invokes no fruit leaf");
            Equal(0, fruits.Cast<object>().Sum(fruit => (int)Get(fruit, "harvests")), "manual fight preserves every harvest opportunity");
        }
        finally { RebirthFruitInvoking = null; }
    }

    private static void CheckManualRebirthFruitSafety()
    {
        var plan = PrepareRebirthFruits(out var fruits); var calls = 0;
        var settings = Get(MainType, "Settings");
        Invoke(Get(MainType, "CustomPlanStore"), "Save", Property(settings, "CustomPlanFile"), plan);
        Set(MainInstance, "_currentExecutionCommandId", "manual-maxed-only");
        RebirthFruitInvoking = (id, eat) => calls++;
        try
        {
            CallMain("RunManualRebirth");
            Equal(true, Property(MainInstance, "BossPushRunning"), "manual rebirth retains maxed-only safety without blocking fight");
            Equal(0, calls, "manual maxed-only safety leaves immature fruits untouched");
            Equal(0, fruits.Cast<object>().Sum(fruit => (int)Get(fruit, "harvests")), "manual safety spends no unmaxed opportunity");
        }
        finally { RebirthFruitInvoking = null; Set(MainInstance, "_currentExecutionCommandId", null); }
    }

    private static void CheckRebirthFruitPreflightRetry()
    {
        var plan = PrepareRebirthFruits(out var fruits);
        var button = Get(Get(Character, "buttons"), "yggdrasil"); Set(button, "m_Interactable", false);
        var first = (string)CallMain("RunFightPrep", plan, true, "fruit prerequisites", null, "original-fruits");
        Check(first.Contains("waiting for fruit prerequisites"), "uninvoked fruit denial waits rather than skips: " + first);
        Equal(false, Property(MainInstance, "BossPushRunning"), "fruit denial withholds boss process");
        Equal(0, fruits.Cast<object>().Sum(fruit => (int)Get(fruit, "harvests")), "preflight denial spends no fruit");
        var attempt = Get(MainInstance, "_fightPreparationAttempt");
        Set(button, "m_Interactable", true);
        var recovered = (string)CallMain("RunFightPrep", plan, true, "fruit prerequisites", null, "original-fruits");
        Check(recovered.Contains("boss push running"), "recovery executes captured original fruits: " + recovered);
        Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "recovery keeps original preparation identity");
        Equal(19, fruits.Cast<object>().Sum(fruit => (int)Get(fruit, "harvests")), "recovery harvests each original available fruit once");
    }

    private static void CheckRebirthFruitReplacement(string change)
    {
        var plan = PrepareRebirthFruits(out var fruits);
        Program.FinitePreparationSelectionStarting = () => {
            if (change == "harvest") Set(fruits[18], "harvests", 1);
            else if (change == "identity") fruits[18] = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(fruits[18].GetType());
            else fruits.RemoveAt(fruits.Count - 1);
        };
        var calls = 0; RebirthFruitInvoking = (id, eat) => calls++;
        try
        {
            var result = (string)CallMain("RunFightPrep", plan, true, "foreign non-power fruit", null, "foreign-" + change);
            Check(result.Contains("original fruit opportunity changed"), "foreign non-power fruit cancels original request/" + change + ": " + result);
            Equal(0, calls, "changed snapshot invokes no replacement fruit/" + change);
            Equal(false, Property(MainInstance, "BossPushRunning"), "changed snapshot cannot admit boss/" + change);
            Equal(false, CallMain("FightPreparationCanRetry", Property(MainInstance, "FightPreparationIdentity")), "changed snapshot cannot retry/" + change);
        }
        finally { Program.FinitePreparationSelectionStarting = null; RebirthFruitInvoking = null; }
    }

    private static void CheckRebirthFruitBossAdmissionWait()
    {
        var plan = PrepareRebirthFruits(out var fruits); var calls = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        RebirthBossStarting = () => Set(MainInstance, "_unloaded", true);
        try
        {
            var first = (string)CallMain("RunFightPrep", plan, true, "boss admission wait", null, "original-fruit-steps");
            Check(first.Contains("boss process admission is pending"), "boss-start boundary actually denies admission: " + first);
            Equal(false, Property(MainInstance, "BossPushRunning"), "denied boss admission has no running process");
            Equal(19, calls, "all fruits complete before denied admission");
            var attempt = Get(MainInstance, "_fightPreparationAttempt");
            for (var i = 0; i < 19; i++) Set(fruits[i], "seconds", 3600f);
            RebirthBossStarting = null; Set(MainInstance, "_unloaded", false);
            var recovered = (string)CallMain("RunFightPrep", plan, true, "boss admission wait", null, "original-fruit-steps");
            Check(recovered.Contains("boss push running"), "same session admits boss after recovery: " + recovered);
            Check(ReferenceEquals(attempt, Get(MainInstance, "_fightPreparationAttempt")), "boss admission recovery retains original attempt");
            Equal(19, calls, "admission recovery cannot consume another fruit cycle");
            for (var i = 0; i < 19; i++) Equal(1, Get(fruits[i], "harvests"), "each fruit step remains spent/" + i);
        }
        finally { RebirthFruitInvoking = null; RebirthBossStarting = null; Set(MainInstance, "_unloaded", false); }
    }

    private static void CheckRebirthFruitFinalCallbackReplacement()
    {
        var plan = PrepareRebirthFruits(out var fruits); var calls = 0;
        RebirthFruitInvoking = (id, eat) => calls++;
        Program.FiniteFruitRendered = () => {
            if ((int)Get(fruits[18], "harvests") == 1)
                fruits[0] = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(fruits[0].GetType());
        };
        try
        {
            var result = (string)CallMain("RunFightPrep", plan, true, "final fruit callback", null, "last-fruit-replacement");
            Check(result.Contains("original fruit opportunity changed"), "final native callback cannot replace an already-processed fruit: " + result);
            Equal(19, calls, "actual final fruit native accounting precedes the replacement");
            Equal(false, Property(MainInstance, "BossPushRunning"), "final snapshot verification withholds boss despite committed leaves");
        }
        finally { RebirthFruitInvoking = null; Program.FiniteFruitRendered = null; }
    }

    private static void CheckUncertainRebirthFruit()
    {
        var plan = PrepareRebirthFruits(out var fruits); var calls = 0;
        RebirthFruitInvoking = (id, eat) => {
            calls++;
            if (id == 3) throw new InvalidOperationException("uncertain native fruit invocation");
        };
        try
        {
            var result = (string)CallMain("RunFightPrep", plan, true, "uncertain fruit", null, "uncertain-fruits");
            Check(result.Contains("fruit outcome unverified"), "uncertain native leaf cancels the attempt: " + result);
            Equal(4, calls, "uncertain step stops subsequent fruits");
            Equal(0, Get(fruits[3], "harvests"), "uncertain leaf has no fabricated native accounting");
            Equal(false, Property(MainInstance, "BossPushRunning"), "uncertain fruit never starts boss process");
            CallMain("RunFightPrep", plan, true, "uncertain fruit", null, "uncertain-fruits");
            Equal(4, calls, "same bounded attempt never reinvokes an uncertain fruit");
            RebirthFruitInvoking = (id, eat) => calls++;
            var yggManager = Plugin.GetType("NGUInjector.Managers.YggdrasilManager", true);
            Invoke(yggManager, "HarvestAll");
            Equal(19, calls, "final rebirth harvest can process remaining fruits but cannot repeat the uncertain one");
            Equal(0, Get(fruits[3], "harvests"), "final rebirth fallback leaves the uncertain original opportunity untouched");
            Equal(false, CallMain("HarvestFruitAction", 3, false, false, null, null), "direct ordinary retry also withholds uncertain prep fruit");
            Equal(19, calls, "withheld ordinary retry has no native invocation");
            Set(fruits[3], "harvests", 1);
            Equal(true, CallMain("HarvestFruitAction", 3, false, false, null, null), "externally changed native opportunity is not globally quarantined");
            Equal(20, calls, "fresh native opportunity invokes exactly once");
        }
        finally { RebirthFruitInvoking = null; }
    }
}
