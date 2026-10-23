using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static partial class Program
{
    private sealed class AllocationRefresh
    {
        internal string Key;
        internal long IdleEnergy, IdleMagic, TotalEnergy, TotalMagic, TmEnergy, TmMagic;
        internal long[] At, Augments, Wandoos;
    }

    private static readonly List<AllocationRefresh> AllocationRefreshes = new List<AllocationRefresh>();
    private static bool ThrowTmRefresh, ModelNativeCapLossOnSwap;
    private static int ResourceModelWrites;
    private static Action<string> AllocationRefreshObserver;

    private static void ConfigureResourceExecutionObservers(AssemblyDefinition plugin)
    {
        var write = plugin.MainModule.Types.Single(x => x.FullName == "NGUInjector.Managers.ResourceWriter").Methods.Single(x => x.Name == "SetAmount");
        write.Body.GetILProcessor().InsertBefore(write.Body.Instructions[0], Instruction.Create(OpCodes.Call,
            plugin.MainModule.ImportReference(typeof(Program).GetMethod("CountResourceModelWrite", All))));
    }

    public static void CountResourceModelWrite() { ResourceModelWrites++; }

    private static void ConfigureLiveResourceFixture(AssemblyDefinition game)
    {
        foreach (var type in game.MainModule.Types)
        foreach (var method in type.Methods.Where(x => x.HasBody))
        {
            // These are serialized/recurring external multipliers, not allocation or progression methods.
            if (new[] { "totalEnergyBarBonus", "totalMagicBarBonus", "totalTMSpeedBonus", "TMSpeedBonus", "totalDiscount" }.Contains(method.Name)
                && new[] { "System.Single", "System.Double" }.Contains(method.ReturnType.FullName)) Constant(method, 1);
            if (type.Name == "Energy" && new[] { "updateEnergyText", "updateSlider" }.Contains(method.Name)
                || type.Name == "MagicDisplay" && new[] { "updateMagicText", "updateSlider" }.Contains(method.Name)
                || type.Name == "TimeMachineController" && new[] { "updateSpeedBar", "updateGoldMultiBar", "updateMachineBar", "updateMenu" }.Contains(method.Name))
                Constant(method, 0);
            var rendering = type.Name == "AdvancedTrainingController" && method.Name == "updateText"
                || type.Name == "AugmentController" && new[] { "updateAugTexts", "updateUpgradeTexts" }.Contains(method.Name)
                || type.Name == "Wandoos98Controller" && method.Name == "updateText"
                || type.Name == "NGUController" && method.Name == "updateText"
                || type.Name == "NGUMagicController" && method.Name == "updateText"
                || type.Name == "TimeMachineController" && new[] { "updateSpeedText", "updateGoldMultiText" }.Contains(method.Name);
            if (!rendering) continue;
            method.Body = new Mono.Cecil.Cil.MethodBody(method);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, type.Name + "." + method.Name);
            il.Emit(OpCodes.Call, game.MainModule.ImportReference(typeof(Program).GetMethod("ObserveAllocationRefresh", All)));
            il.Emit(OpCodes.Ret);
        }
    }

    private static void PrepareLiveResourceFixture()
    {
        AllocationRefreshes.Clear(); ThrowTmRefresh = false; ModelNativeCapLossOnSwap = false;
        ResourceModelWrites = 0; AllocationRefreshObserver = null;
        Set(Get(Character, "timeMachineController"), "character", Character);
        foreach (var name in new[] { "baseNormalSpeedDivider", "baseEvilSpeedDivider", "baseSadisticSpeedDivider",
            "baseNormalGoldMultiDivider", "baseEvilGoldMultiDivider", "baseSadisticGoldMultiDivider" })
            Set(Get(Character, "timeMachineController"), name, 1f);
        Set(Get(Character, "timeMachineController"), "baseSpeedCost", 5000000f);
        Set(Get(Character, "timeMachineController"), "baseGoldMultiCost", 5000000f);
        Set(Get(Character, "machine"), "transferredBankLevels", true);
        Set(Character, "energyBars", 100L);
        Set(Get(Character, "magic"), "magicPerBar", 200L); Set(Get(Character, "magic"), "magicBarSpeed", 50f);
        Child(Get(Character, "arbitrary"), "energyBarBar1Time"); Child(Get(Character, "arbitrary"), "magicBarBar1Time");
    }

    public static void ObserveAllocationRefresh(object controller, string key)
    {
        if (key == "AdvancedTrainingController.updateText") key = "AT" + Get(controller, "id");
        var w = Get(Character, "wandoos98"); var machine = Get(Character, "machine");
        AllocationRefreshes.Add(new AllocationRefresh {
            Key = key, IdleEnergy = (long)Get(Character, "idleEnergy"), IdleMagic = (long)Get(Get(Character, "magic"), "idleMagic"),
            TotalEnergy = OwnedResourceTotal(true), TotalMagic = OwnedResourceTotal(false),
            At = (long[])((long[])Get(Get(Character, "advancedTraining"), "energy")).Clone(),
            Augments = ((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>()
                .SelectMany(x => new[] { (long)Get(x, "augEnergy"), (long)Get(x, "upgradeEnergy") }).ToArray(),
            Wandoos = new[] { (long)Get(w, "wandoosEnergy"), (long)Get(w, "wandoosMagic") },
            TmEnergy = (long)Get(machine, "speedEnergy"), TmMagic = (long)Get(machine, "goldMultiMagic")
        });
        AllocationRefreshObserver?.Invoke(key);
        if (ThrowTmRefresh && key.StartsWith("TimeMachineController."))
        { ThrowTmRefresh = false; throw new InvalidOperationException("fixture TM allocation refresh failure"); }
    }

    private static long OwnedResourceTotal(bool energy)
    {
        var ngu = Get(Character, "NGU"); var w = Get(Character, "wandoos98"); var machine = Get(Character, "machine");
        if (!energy) return (long)Get(Get(Character, "magic"), "idleMagic") + (long)Get(w, "wandoosMagic")
            + (long)Get(machine, "goldMultiMagic") + ((IEnumerable)Get(ngu, "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic"));
        return (long)Get(Character, "idleEnergy") + ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum()
            + ((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>().Sum(x => (long)Get(x, "augEnergy") + (long)Get(x, "upgradeEnergy"))
            + (long)Get(w, "wandoosEnergy") + (long)Get(machine, "speedEnergy")
            + ((IEnumerable)Get(ngu, "skills")).Cast<object>().Sum(x => (long)Get(x, "energy"));
    }

    private static void NativeGenerate(bool energy)
    {
        var generator = Blank(Game.GetType(energy ? "Energy" : "MagicDisplay", true));
        Set(generator, "character", Character);
        Invoke(generator, energy ? "addEnergy" : "updateMagicBar");
    }

    private static void ApplyNativeCapLossOnSwap()
    {
        if (!ModelNativeCapLossOnSwap) return;
        if ((long)Get(Character, "curEnergy") > (long)Invoke(Character, "totalCapEnergy")) NativeGenerate(true);
        if ((long)Get(Get(Character, "magic"), "curMagic") > (long)Invoke(Character, "totalCapMagic")) NativeGenerate(false);
    }

    private static void AllocateFixturePhase(object phase, object snapshot, long energy, long magic, bool allowTimeMachine)
    {
        var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
        ApplyResolvedCombatResources(phase, snapshot, energy, magic, allowTimeMachine);
    }

    private static void CheckLiveResourceRegressions()
    {
        var before = Assertions;
        CheckSharedResourceDomains();
        CheckSharedCombatResourceExecution();
        CheckRecallAllCombatPrepDestinations();
        CheckDirectLivePoolCaps();
        CheckNativeGeneratedPools();
        CheckFallbackTmEligibility();
        CheckFallbackIndependentBudgets();
        CheckFallbackPriorityAndCaps();
        CheckDirectAllocationRefreshes();
        CheckThrowingTmConservation();
        CheckAutomaticFreshPoolReplan();
        CheckFallbackIncomeDuringAt();
        CheckFallbackGoldPermission();
        CheckDirectAugmentGoldProtection();
        CheckAugmentPolicyGoldProtection();
        Setup();
        Console.WriteLine("Live resource regressions: " + (Assertions - before) + " assertions.");
    }

    private static void CheckSharedResourceDomains()
    {
        foreach (var focus in new[] { false, true })
        {
            Setup();
            var settings = Get(MainType, "Settings");
            Set(settings, "_energyNguWeights", new[] { 2, 1, 0, 0, 0, 0, 0, 0, 0 });
            Set(settings, "_magicNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0 });
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            foreach (var skill in ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>()
                .Concat(((IEnumerable)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>())) Set(skill, "level", 100000L);
            var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "EnergyPriorities", new[] { "NGU" }); Set(stage, "MagicPriorities", new[] { "NGU" });
            var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
            var goal = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "EnergyNgu");
            Action apply = () => {
                if (focus) CallMain("AllocateCustomFocusResources", goal, false, stage);
                else Invoke(allocator, "ApplyStage", plan, stage, 90, 0, "compiled-resource-domain", 120);
            };
            var energy = (IList)Get(Get(Character, "NGU"), "skills"); var magic = (IList)Get(Get(Character, "NGU"), "magicSkills");
            var label = focus ? "focus" : "inline";
            apply();
            var shared = Get(MainInstance, "_sharedResourceSetup");
            Equal(true, shared != null, label + " actual native resources enter the shared setup executor");
            var application = Get(shared, "Application");
            if (!focus)
            {
                Equal("inline-stage:compiled-resource-domain", Get(Get(shared, "Current"), "Owner"), "inline producer supplies its current entry identity to the shared executor");
                Equal(true, ReferenceEquals(Get(application, "Last"), Get(allocator, "LastStageResourceOutcome")), "inline verification uses actual shared actuator evidence, not a second resource application");
            }
            Equal(6667L, Get(energy[0], "energy"), label + " uses current within-NGU 2:1 weights");
            Equal(3333L, Get(energy[1], "energy"), label + " conserves the integer Energy lane remainder");
            Equal(20000L, Get(magic[0], "magic"), label + " funds Magic independently");
            var writes = ResourceModelWrites; var renders = AllocationRefreshes.Count; var recalls = Recalls;
            apply();
            Equal(writes, ResourceModelWrites, label + " unchanged desired state performs zero model writes");
            Equal(renders, AllocationRefreshes.Count, label + " unchanged desired state performs zero rendering callbacks");
            Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
            Set(Get(Character, "machine"), "levelGoldMulti", 1000000L);
            Set(stage, "MagicPriorities", new[] { "TM", "NGU" }); apply();
            Equal(20000L, Get(Get(Character, "machine"), "goldMultiMagic"), label + " new higher Magic priority preempts lower Magic NGU");
            Equal(0L, Get(magic[0], "magic"), label + " preempted Magic NGU is controlled empty");
            Equal(6667L, Get(energy[0], "energy"), label + " Magic preemption never displaces Energy");
            Equal(writes + 2, ResourceModelWrites, label + " preemption writes only the two changed Magic destinations");
            Set(stage, "MagicPriorities", new[] { "NGU" }); apply();
            Equal(20000L, Get(magic[0], "magic"), label + " release recomputes the present baseline directly");
            Equal(0L, Get(Get(Character, "machine"), "goldMultiMagic"), label + " release has no predecessor restore pass");
            Equal(writes + 4, ResourceModelWrites, label + " release writes only current resource differences");
            Equal(recalls, Recalls, label + " refresh, preemption and release never invoke native recalls");

            Set(energy[0], "energy", 6666L); Set(Character, "idleEnergy", 1L);
            var fired = false;
            AllocationRefreshObserver = key => {
                fired = true; AllocationRefreshObserver = null;
                Equal(6667L, Get(energy[0], "energy"), label + " rendering sees completed accounting");
                Equal(20000L, Get(magic[0], "magic"), label + " rendering sees the independently complete Magic map");
                throw new InvalidOperationException("fixture committed resource render failure");
            };
            apply();
            Equal(true, fired, label + " render failure positive control actually fires");
            var outcome = focus ? Get(MainInstance, "_customFocusResourceOutcome") : Get(allocator, "LastStageResourceOutcome");
            Equal("ModelAppliedRenderFailed", Get(outcome, "Disposition").ToString(), label + " retains verified application despite render error");
            writes = ResourceModelWrites; apply();
            Equal(writes, ResourceModelWrites, label + " separate render retry never repeats additive accounting");
            if (!focus) Equal(true, Get(allocator, "LastStageApplicationSucceeded"), "render-only recovery retains stage credit eligibility");

            Set(energy[0], "energy", 6666L); Set(Character, "idleEnergy", 1L);
            AllocationRefreshObserver = key => {
                AllocationRefreshObserver = null;
                Set(magic[0], "magic", 19999L); Set(Get(Character, "magic"), "idleMagic", 1L);
                throw new InvalidOperationException("fixture foreign callback");
            };
            Reject(apply, label + " foreign callback is not a presentation-only success");
            outcome = focus ? Get(MainInstance, "_customFocusResourceOutcome") : Get(allocator, "LastStageResourceOutcome");
            Equal("ForeignConflict", Get(outcome, "Disposition").ToString(), label + " foreign mutation is typed");
            Equal(2, Get(outcome, "AffectedLanes"), label + " foreign callback identifies only Magic interference");
            if (!focus)
            {
                Invoke(allocator, "ResetResourceTargets");
                Equal(true, ReferenceEquals(shared, Get(MainInstance, "_sharedResourceSetup")), "inline decision reset cannot replace shared unresolved native evidence");
                Equal(2, Get(application, "BlockedLanes"), "inline reset retains the affected native lane's unresolved authority");
            }
            writes = ResourceModelWrites; Reject(apply, label + " unresolved foreign evidence cannot retry");
            Equal(writes, ResourceModelWrites, label + " unresolved foreign evidence never replays model writes");
            Equal(19999L, Get(magic[0], "magic"), label + " foreign allocation is retained");
            if (!focus) Equal(false, Get(allocator, "LastStageApplicationSucceeded"), "foreign result never grants stage credit");
            Set(settings, "_energyNguWeights", new[] { 1, 1, 0, 0, 0, 0, 0, 0, 0 });
            Reject(apply, label + " healthy Energy continuation does not verify yielded Magic");
            Equal(5000L, Get(energy[0], "energy"), label + " shared executor can reconcile healthy Energy after Magic authority loss");
            Equal(5000L, Get(energy[1], "energy"), label + " healthy Energy continuation preserves integer conservation");
            Equal(19999L, Get(magic[0], "magic"), label + " healthy continuation never adopts or overwrites foreign Magic");
            Equal(writes + 2, ResourceModelWrites, label + " healthy continuation changes only the two current Energy destinations");
            outcome = focus ? Get(MainInstance, "_customFocusResourceOutcome") : Get(allocator, "LastStageResourceOutcome");
            Equal(1, Get(outcome, "VerifiedLanes"), label + " shared outcome verifies only the independent Energy lane");
            if (!focus) Equal(false, Get(allocator, "LastStageApplicationSucceeded"), "partial shared-lane verification cannot arm protected stage credit");
        }
        CheckOrderedCapRefresh();
        CheckResourceAdmissionGuards();
        CheckRatioIntentChangesDuringRendering();
        var timedPlan = ScheduledSetup(StandardRow);
        CallMain("RunCustomPlanAutomation", timedPlan);
        var scheduler = Get(MainInstance, "_protectedScheduler"); var deadline = Get(scheduler, "ExpectedEndSeconds"); var entry = Get(scheduler, "Entry");
        Equal(true, Get(scheduler, "_armed"), "real inline stage dispatch arms only after verified desired-map application");
        Set(scheduler, "_lastHeartbeat", (double)Get(MainType, "ProtectedHeartbeat") - 2);
        ScheduledTime(502); CallMain("RunCustomPlanAutomation", timedPlan);
        Equal(2d, ((double[])Get(scheduler, "AppliedSeconds"))[0], "fixed stage earns actual verified time, not allocation count");
        Equal(deadline, Get(scheduler, "ExpectedEndSeconds"), "unchanged resource reconciliation does not renew the fixed deadline");
        Equal(entry, Get(scheduler, "Entry"), "unchanged resource reconciliation retains fixed entry identity");
    }

    private static void CheckOrderedCapRefresh()
    {
        foreach (var focus in new[] { false, true })
        foreach (var at in new[] { false, true })
        {
            Setup();
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            foreach (var skill in ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>()) Set(skill, "level", 100000L);
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "EnergyPriorities", new[] { at ? "AT" : "AUG", "NGU" });
            Set(stage, "MagicPriorities", new[] { "NGU" });
            if (at) { Set(stage, "CompletionMode", 1); Set(stage, "TargetAdvancedTrainingPower", 10L); Set(stage, "TargetAdvancedTrainingToughness", 20L); }
            var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
            var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
            var goal = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "AugmentsBloodMagic");
            var originalTargets = ((long[])Get(Get(Character, "advancedTraining"), "levelTarget")).ToArray();
            Action apply = () => {
                if (focus) CallMain("AllocateCustomFocusResources", goal, false, stage);
                else Invoke(allocator, "ApplyStage", plan, stage, 90, 0, "compiled-cap-refresh:" + at, 120);
            };
            apply();
            var aug = ((Array)Get(Get(Character, "augments"), "augs")).GetValue(0);
            var allocated = at ? ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum()
                : (long)Get(aug, "augEnergy") + (long)Get(aug, "upgradeEnergy");
            Equal(true, allocated > 0, "ordered pair positive control actually funds " + (at ? "AT" : "AUG") + "/focus=" + focus);
            var writes = ResourceModelWrites; apply();
            Equal(writes, ResourceModelWrites, "owned pair refresh recomputes absolute useful caps, not native-allocation deltas: " + at + "/" + focus);
            Equal(string.Join(",", originalTargets), string.Join(",", (long[])Get(Get(Character, "advancedTraining"), "levelTarget")),
                "pure ordered calculations never temporarily overwrite native AT menu targets");
            if (!focus)
            {
                if (at)
                {
                    ((long[])Get(Get(Character, "advancedTraining"), "level"))[0] = 20;
                    ((long[])Get(Get(Character, "advancedTraining"), "level"))[1] = 10;
                }
                else { Set(aug, "augmentTarget", 1L); Set(aug, "upgradeTarget", 1L); Set(aug, "augLevel", 1L); Set(aug, "upgradeLevel", 1L); }
                apply();
                Equal(10000L, ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy")),
                    "completed ordered work spills the whole remaining Energy pool into NGU without a restore pass");
            }
        }
    }

    private static void CheckSharedCombatResourceExecution()
    {
        Setup();
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        foreach (var skill in ((IEnumerable)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>()) Set(skill, "level", 100000L);
        var capture = CallMain("CaptureCombatPrep", .03, .03, null);
        var run = StartServerPolicy(capture, Phase("AT", "current", 60), Phase("Wandoos", "current", 60));
        var phase = ((Array)Get(Get(run, "Policy"), "Phases")).GetValue(0);
        var targetE = Get(phase, "AtEnergyTarget"); var targetM = Get(phase, "AtMagicTarget");
        var started = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
        var writes = ResourceModelWrites; var recalls = Recalls;
        ScheduledRefresh(run);
        Equal(writes, ResourceModelWrites, "combat unchanged phase performs zero model writes");
        Equal(recalls, Recalls, "combat unchanged phase never recalls whole lanes");
        var at = (long[])Get(Get(Character, "advancedTraining"), "energy");
        at[3]--; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        AllocationRefreshObserver = key => { AllocationRefreshObserver = null; throw new InvalidOperationException("fixture phase rendering failure"); };
        ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "committed resource rendering failure never ends the combat phase");
        Equal("ModelAppliedRenderFailed", Get(Get(run, "ResourceOutcome"), "Disposition").ToString(), "combat uses the common typed rendering outcome");
        Equal(true, ReferenceEquals(Get(run, "ResourceOutcome"), Get(Get(Get(MainInstance, "_sharedResourceSetup"), "Application"), "Last")), "combat result belongs to the same shared executor as presets/focus/inline");
        writes = ResourceModelWrites; ScheduledRefresh(run);
        Equal(writes, ResourceModelWrites, "combat presentation retry does not replay phase or fallback funding");
        Equal(started, Get(run, "Started"), "combat rendering recovery retains original global budget");
        Equal(deadline, Get(run, "PhaseDeadline"), "combat rendering recovery retains acquired phase deadline");
        ControlledCombatPrepClock = (double)started + 61; ScheduledRefresh(run);
        Equal(0, Get(run, "PhaseIndex"), "overdue paid AT remains the exact acquired gate until actual targets are reached");
        Equal(targetE, Get(phase, "AtEnergyTarget"), "resource retry cannot replace acquired Energy AT target");
        Equal(targetM, Get(phase, "AtMagicTarget"), "resource retry cannot replace acquired Magic AT target");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "unreached AT never funds Wandoos Magic");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "unreached AT never funds Wandoos Energy");
        var magic = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0]; var magicAmount = (long)Get(magic, "magic");
        Equal(true, magicAmount > 0, "foreign callback control has real Magic funding");
        at[3]--; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        AllocationRefreshObserver = key => {
            AllocationRefreshObserver = null; Set(magic, "magic", magicAmount - 1);
            Set(Get(Character, "magic"), "idleMagic", (long)Get(Get(Character, "magic"), "idleMagic") + 1);
        };
        ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "foreign resource callback retains acquired phase while refusing dependent credit");
        Equal(-1, Get(run, "AppliedResourcePhase"), "foreign resource callback cannot verify the complete phase");
        Equal(2, Get(run, "YieldedResourceLanes"), "foreign Magic callback yields only that resource lane");
        Equal(magicAmount - 1, Get(magic, "magic"), "combat cleanup does not recall the foreign Magic lane");
        writes = ResourceModelWrites; CallMain("TickCombatPrep");
        Equal(writes, ResourceModelWrites, "terminated uncertain combat operation never replays its phase");

        Setup();
        capture = CallMain("CaptureCombatPrep", .03, .03, null);
        run = StartServerPolicy(capture, Phase("AT", "current", 60), Phase("Wandoos", "current", 60));
        started = Get(run, "Started"); var duration = Get(run, "Duration");
        at = (long[])Get(Get(Character, "advancedTraining"), "energy");
        at[3]--; Set(Character, "idleEnergy", (long)Get(Character, "idleEnergy") + 1);
        AllocationRefreshObserver = key => { AllocationRefreshObserver = null; ControlledCombatPrepClock = (double)started + (double)duration + 1; };
        ScheduledRefresh(run);
        Equal(false, Get(MainInstance, "CombatPrepActive"), "deadline expiry during rendering retires the verified allocation instead of admitting another action");
        Equal(0, Get(run, "PhaseIndex"), "resource rendering cannot extend the budget into a later Wandoos phase");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "deadline-crossing resource commit never funds later Wandoos Energy");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "deadline-crossing resource commit never funds later Wandoos Magic");
        Equal(started, Get(run, "Started"), "deadline-crossing rendering never renews global start");
        Equal(duration, Get(run, "Duration"), "deadline-crossing rendering never renews global duration");
    }

    private static void CheckResourceAdmissionGuards()
    {
        foreach (var focus in new[] { false, true })
        foreach (var change in new[] { "epoch", "permission", "domain", "generation" })
        {
            Setup(); Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "EnergyPriorities", new[] { "NGU" }); Set(stage, "MagicPriorities", new[] { "NGU" });
            var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
            var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
            var goal = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "EnergyNgu");
            var lifecycle = MainType.GetNestedType("ExecutionLifecycle", All);
            Action apply = () => {
                if (focus) CallMain("AllocateCustomFocusResources", goal, false, stage);
                else Invoke(allocator, "ApplyStage", plan, stage, 90, 0, "compiled-admission:" + change, 120);
            };
            var before = ResourceModelWrites;
            CallMain("SetExecutionLifecycle", Enum.Parse(lifecycle, "Quiescing"));
            if (focus) Reject(apply, "quiesced focus authority rejects unchanged before model writes");
            else
            {
                apply();
                Equal("RejectedUnchanged", Get(Get(allocator, "LastStageResourceOutcome"), "Disposition").ToString(), "quiesced inline authority waits unchanged without consuming its entry");
            }
            Equal(before, ResourceModelWrites, "quiesced preflight never changes resource accounting");
            CallMain("SetExecutionLifecycle", Enum.Parse(lifecycle, "Running"));
            var fired = false;
            AllocationRefreshObserver = key => {
                AllocationRefreshObserver = null; fired = true;
                if (change == "epoch") Set(Get(Character, "stats"), "rebirthNumber", (long)Get(Get(Character, "stats"), "rebirthNumber") + 1);
                else if (change == "permission") Setting("ManageMagic", false);
                else if (change == "generation") Set(MainType, "reference", Blank(MainType));
                else CallMain("InvalidateExecutionObservations", Enum.Parse(MainType.GetNestedType("ExecutionDomain", All), "Energy, Magic"));
            };
            Reject(apply, "changed resource admission during rendering cannot issue a verified receipt");
            Equal(true, fired, "readmitted preflight reaches real resource rendering before " + change + " interference");
            var outcome = focus ? Get(MainInstance, "_customFocusResourceOutcome") : Get(allocator, "LastStageResourceOutcome");
            Equal("UnknownOutcome", Get(outcome, "Disposition").ToString(), "changed " + change + " revokes acquired resource authority");
            Equal(10000L, OwnedResourceTotal(true), "changed " + change + " conserves exact Energy accounting");
            Equal(20000L, OwnedResourceTotal(false), "changed " + change + " conserves exact Magic accounting");
            before = ResourceModelWrites; Reject(apply, "revoked authority cannot automatically readmit itself");
            Equal(before, ResourceModelWrites, "revoked " + change + " performs no subsequent model writes");
            if (!focus) Equal(false, Get(allocator, "LastStageApplicationSucceeded"), "revoked stage authority never earns protected credit");
        }
    }

    private static void CheckRatioIntentChangesDuringRendering()
    {
        foreach (var focus in new[] { false, true })
        {
            Setup(); Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            Setting("EnergyNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 });
            foreach (var skill in ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>()) Set(skill, "level", 100000L);
            var stage = Activator.CreateInstance(Plugin.GetType("NGUInjector.CustomPlans.CustomPlanStage", true));
            Set(stage, "EnergyPriorities", new[] { "NGU" });
            var plan = Invoke(Get(MainType, "CustomPlanStore"), "Load", "fixture");
            var allocator = Plugin.GetType("NGUInjector.Managers.CustomStageAllocator", true);
            var goal = Enum.Parse(Plugin.GetType("NGUInjector.CustomFocusGoal", true), "EnergyNgu");
            Action apply = () => {
                if (focus) CallMain("AllocateCustomFocusResources", goal, false, stage);
                else Invoke(allocator, "ApplyStage", plan, stage, 90, 0, "compiled-ratio-change", 120);
            };
            AllocationRefreshObserver = key => { AllocationRefreshObserver = null; Setting("EnergyNguWeights", new[] { 0, 1, 0, 0, 0, 0, 0, 0, 0 }); };
            apply();
            var energy = (IList)Get(Get(Character, "NGU"), "skills");
            Equal(10000L, Get(energy[0], "energy"), "a ratio edit during rendering cannot replay an already committed resource transfer");
            if (focus) Equal(1, ((int[])Get(MainInstance, "_customFocusEnergyNguWeights"))[0], "focus records the ratios actually calculated, not a callback's newer input");
            else Equal(false, Get(allocator, "LastStageApplicationSucceeded"), "new ratio intent cannot receive credit from the previous ratio's committed map");
            apply();
            Equal(0L, Get(energy[0], "energy"), "next reconciliation releases the obsolete ratio target directly");
            Equal(10000L, Get(energy[1], "energy"), "next reconciliation applies the latest ratio intent without a replacement gear search");
            if (!focus)
            {
                Equal(true, Get(allocator, "LastStageApplicationSucceeded"), "current ratios receive stage credit only after their own verified application");
                Setting("EnergyNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 });
                Equal(false, Invoke(allocator, "StageApplicationStillOwned", "compiled-ratio-change"), "later ratio edits disarm credit immediately, before a resource refresh");
            }
        }
    }

    private static void CheckRecallAllCombatPrepDestinations()
    {
        Setup();
        var ngu = Get(Character, "NGU"); var machine = Get(Character, "machine");
        Set(((IList)Get(ngu, "skills"))[4], "energy", 123L); Set(Character, "idleEnergy", 9877L);
        Set(machine, "speedEnergy", 321L); Set(Character, "idleEnergy", 9556L);
        Set(((IList)Get(ngu, "magicSkills"))[0], "magic", 234L); Set(Get(Character, "magic"), "idleMagic", 19766L);
        Set(machine, "goldMultiMagic", 432L); Set(Get(Character, "magic"), "idleMagic", 19334L);
        CallMain("RecallCombatPrepResources");
        Equal(10000L, Get(Character, "idleEnergy"), "recall returns energy from both NGU and TM");
        Equal(20000L, Get(Get(Character, "magic"), "idleMagic"), "recall returns magic from both NGU and TM");
        Equal(0L, Get(((IList)Get(ngu, "skills"))[4], "energy"), "recall clears NGU energy destination");
        Equal(0L, Get(((IList)Get(ngu, "magicSkills"))[0], "magic"), "recall clears NGU magic destination");
        Equal(0L, Get(machine, "speedEnergy"), "recall clears TM energy destination");
        Equal(0L, Get(machine, "goldMultiMagic"), "recall clears TM magic destination");
        CallMain("RecallCombatPrepResources");
        Equal(10000L, Get(Character, "idleEnergy"), "second recall cannot duplicate energy");
        Equal(20000L, Get(Get(Character, "magic"), "idleMagic"), "second recall cannot duplicate magic");
    }

    private static void CheckDirectLivePoolCaps()
    {
        foreach (var reservation in new[] { 200L, 800L, 900L })
        foreach (var explicitPool in new[] { 100L, 1000L })
        foreach (var idleEnergy in new[] { 90L, 900L })
        {
            Setup(); var s = Get(CallMain("CaptureCombatPrep", .03, .03, null), "Snapshot");
            var p = Get(s, "Current"); Set(p, "EnergyTotal", 1L); Set(p, "MagicTotal", 2L);
            Set(Character, "capEnergy", 800L); Set(Character, "idleEnergy", idleEnergy);
            ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0] = reservation;
            var idleMagic = idleEnergy == 90 ? 70L : 700L;
            Set(Get(Character, "magic"), "capMagic", 400L); Set(Get(Character, "magic"), "idleMagic", idleMagic);
            var expectedE = Math.Min(idleEnergy, Math.Min(explicitPool, Math.Max(800 - reservation, 0)));
            var expectedM = Math.Min(idleMagic, Math.Min(explicitPool, 400));
            var wc = Get(Character, "wandoos98Controller");
            if (expectedE > 0) Equal(true, (float)Invoke(wc, "energyProgressToAdd", expectedE) > 0 && (float)Invoke(wc, "energyProgressToAdd", expectedE) < 1, "native W energy is budget-limited in cap fixture");
            Equal(true, (float)Invoke(wc, "magicProgressToAdd", expectedM) > 0 && (float)Invoke(wc, "magicProgressToAdd", expectedM) < 1, "native W magic is budget-limited in cap fixture");
            AllocateFixturePhase(Phase("Wandoos", "current", 60), s, explicitPool, explicitPool, true);
            Equal(expectedE, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "direct allocation ignores stale profile and subtracts basic exactly once");
            Equal(expectedM, Get(Get(Character, "wandoos98"), "wandoosMagic"), "direct allocation independently caps magic by pool/live/native");
            Equal(idleEnergy - expectedE, Get(Character, "idleEnergy"), "energy outside live usable pool remains idle");
            Equal(idleMagic - expectedM, Get(Get(Character, "magic"), "idleMagic"), "magic outside live usable pool remains idle");
            Equal(reservation, ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0], "basic reservation is not recalled or funded twice");
        }
    }

    private static void CheckNativeGeneratedPools()
    {
        foreach (var basic in new[] { 0L, 50L })
        {
            Setup(); var magic = Get(Character, "magic");
            Set(Character, "capEnergy", 1000L); Set(Character, "curEnergy", 100L); Set(Character, "idleEnergy", 100L - basic);
            ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0] = basic;
            Set(magic, "capMagic", 2000L); Set(magic, "curMagic", 200L); Set(magic, "idleMagic", 200L);
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            var run = StartServerPolicy(capture, Phase("Wandoos", "current", 60));
            var originalSnapshot = Get(Get(run, "Capture"), "Snapshot");
            var start = Get(run, "Started"); var deadline = Get(run, "PhaseDeadline");
            ScheduledRefresh(run); AssertLivePools(run, 100 - basic, 200, "high native cap alone");
            Equal(100L, Invoke(Character, "totalEnergyBar"), "native energy generation input is exact");
            Equal(200L, Invoke(Character, "totalMagicBar"), "native magic generation input is exact");
            NativeGenerate(true); ScheduledRefresh(run); AssertLivePools(run, 200 - basic, 200, "actual energy generation only");
            NativeGenerate(false); ScheduledRefresh(run); AssertLivePools(run, 200 - basic, 400, "actual magic generation only");
            Equal(100 - basic, Get(Get(originalSnapshot, "Current"), "EnergyTotal"), "execution does not rewrite original captured energy");
            Equal(200L, Get(Get(originalSnapshot, "Current"), "MagicTotal"), "execution does not rewrite original captured magic");
            // Native generation owns over-cap loss, not the executor or a mocked pool calculation.
            CallMain("RecallCombatPrepResources"); Set(Character, "capEnergy", 150L); Set(magic, "capMagic", 250L);
            NativeGenerate(true); NativeGenerate(false); ScheduledRefresh(run); AssertLivePools(run, 150 - basic, 250, "native cap shrink");
            Set(Character, "capEnergy", 1000L); Set(magic, "capMagic", 2000L);
            ScheduledRefresh(run); AssertLivePools(run, 150 - basic, 250, "capacity restored without refill");
            NativeGenerate(true); NativeGenerate(false); ScheduledRefresh(run); AssertLivePools(run, 250 - basic, 450, "actual refill after shrink");
            Equal(250 - basic, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "production Tick funds W with actual refilled energy beyond stale capture");
            Equal(450L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "production Tick funds W with actual refilled magic beyond stale capture");
            Equal(start, Get(run, "Started"), "live pool changes preserve run start");
            Equal(deadline, Get(run, "PhaseDeadline"), "live pool changes preserve phase deadline");
            Equal(0, Get(run, "ReplanCount"), "manual resource growth does not silently replan");
            CallMain("EndCombatPrep", "cancelled", "live resource cleanup", true);
            Equal(250 - basic, Get(Character, "idleEnergy"), "cleanup recalls the grown energy pool");
            Equal(450L, Get(magic, "idleMagic"), "cleanup recalls the grown magic pool");
            Equal(basic, ((long[])Get(Get(Character, "training"), "_attackEnergy"))[0], "native shrink/refill never recalls basic reservation");
        }
    }

    private static void AssertLivePools(object run, long energy, long magic, string context)
    {
        Equal("running", Get(run, "Status"), context + " retains active owner");
        Equal(energy, Get(run, "EnergyPool"), context + " exact live energy pool");
        Equal(magic, Get(run, "MagicPool"), context + " exact live magic pool");
        Equal(energy, OwnedResourceTotal(true), context + " conserves all owned energy destinations");
        Equal(magic, OwnedResourceTotal(false), context + " conserves all owned magic destinations");
    }

    private static void CheckFallbackTmEligibility()
    {
        foreach (var nguState in new[] { "locked", "zero-weights", "completed" })
        foreach (var tmState in new[] { "open", "locked", "challenge", "completed" })
        {
            Setup(); var machine = Get(Character, "machine");
            Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", nguState != "locked");
            Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", tmState != "locked");
            if (nguState == "zero-weights") { Setting("EnergyNguWeights", new int[9]); Setting("MagicNguWeights", new int[7]); }
            if (nguState == "completed")
                foreach (var name in new[] { "skills", "magicSkills" })
                foreach (var lane in (IEnumerable)Get(Get(Character, "NGU"), name))
                { Set(lane, "level", 1L); Set(lane, "target", 1L); }
            if (tmState == "challenge") Set(Get(Get(Character, "challenges"), "timeMachineChallenge"), "inChallenge", true);
            if (tmState == "completed")
            { Set(machine, "levelSpeed", 1L); Set(machine, "speedTarget", 1L); Set(machine, "levelGoldMulti", 1L); Set(machine, "multiTarget", 1L); }
            ApplyFallbackRequirements(900L, 700L, true);
            var e = tmState == "open" ? 900L : 0L; var m = tmState == "open" ? 700L : 0L;
            Equal(e, Get(machine, "speedEnergy"), nguState + "/" + tmState + " exact TM energy fallback");
            Equal(m, Get(machine, "goldMultiMagic"), nguState + "/" + tmState + " exact TM magic fallback");
            Equal(10000 - e, Get(Character, "idleEnergy"), "unspent energy remains idle, never another fallback");
            Equal(20000 - m, Get(Get(Character, "magic"), "idleMagic"), "unspent magic remains idle, never RIT/WAN");
            Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum(), "fallback cannot introduce paid AT");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "fallback cannot introduce W energy");
            Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "fallback cannot introduce W magic");
            Equal(0L, ((IEnumerable)Get(Get(Character, "augments"), "augs")).Cast<object>().Sum(x => (long)Get(x, "augEnergy") + (long)Get(x, "upgradeEnergy")), "fallback cannot introduce augment allocations");
            Equal(0L, ((IEnumerable)Get(Get(Character, "bloodMagic"), "ritual")).Cast<object>().Sum(x => (long)Get(x, "magic")), "fallback cannot introduce ritual allocations");
            Equal(10000L, OwnedResourceTotal(true), "fallback conserves all energy destinations");
            Equal(20000L, OwnedResourceTotal(false), "fallback conserves all magic destinations");
        }
    }

    private static long NativeTmBufferedCap(bool energy)
    {
        var c = Get(Character, "timeMachineController"); var machine = Get(Character, "machine");
        var level = (long)Get(machine, energy ? "levelSpeed" : "levelGoldMulti");
        var divisor = Convert.ToDouble(Invoke(Character, energy ? "totalEnergyPower" : "totalMagicPower"))
            * Convert.ToDouble(Invoke(Get(Get(Character, "allChallenges"), "timeMachineChallenge"), "TMSpeedBonus"))
            * Convert.ToDouble(Invoke(Get(Character, "hacksController"), "totalTMSpeedBonus"))
            * Convert.ToDouble(Invoke(Get(Character, "cardsController"), "getBonus", Enum.Parse(Game.GetType("cardBonus", true), "TMSpeed")));
        var cap = (long)Math.Ceiling(50000d * (level + 252d) * Convert.ToDouble(Invoke(c, energy ? "baseSpeedDivider" : "baseGoldMultiDivider")) / divisor * 1.000002d);
        var field = energy ? "speedEnergy" : "goldMultiMagic"; var levelField = energy ? "levelSpeed" : "levelGoldMulti";
        var saved = Get(machine, field); Set(machine, field, cap); Set(machine, levelField, level + 251);
        Equal(true, (float)Invoke(c, energy ? "speedProgressPerTick" : "goldMultiProgressPerTick") >= 1f, "five-second TM cap black-bars the native buffered endpoint");
        Set(machine, field, saved); Set(machine, levelField, level);
        return cap;
    }

    private static void CheckFallbackIndependentBudgets()
    {
        foreach (var budgets in new[] { new[] { 0L, 700L }, new[] { 900L, 0L }, new[] { -1L, 700L }, new[] { 20000L, 30000L } })
        {
            Setup(); Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
            ApplyFallbackRequirements(budgets[0], budgets[1], true);
            var energy = Math.Min(10000, Math.Max(0, budgets[0])); var magic = Math.Min(20000, Math.Max(0, budgets[1]));
            Equal(energy, Get(Get(Character, "machine"), "speedEnergy"), "TM energy budget is independent and bounded by actual idle");
            Equal(magic, Get(Get(Character, "machine"), "goldMultiMagic"), "zero/negative energy budget does not suppress magic TM");
            Equal(10000 - energy, Get(Character, "idleEnergy"), "independent budget leaves exact parked energy");
            Equal(20000 - magic, Get(Get(Character, "magic"), "idleMagic"), "independent budget leaves exact parked magic");
        }
    }

    private static void CheckFallbackPriorityAndCaps()
    {
        foreach (var finite in new[] { false, true })
        {
            Setup(); Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
            Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
            Setting("EnergyNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }); Setting("MagicNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0 });
            var lane = ((IList)Get(Get(Character, "NGU"), "skills"))[0]; Set(lane, "target", finite ? 1L : 0L);
            var magicLane = ((IList)Get(Get(Character, "NGU"), "magicSkills"))[0]; Set(magicLane, "target", finite ? 1L : 0L);
            // Zero lookahead is two native ticks (next level plus rounding), not TM's 252 ticks.
            // Other serialized NGU inputs are one in this fixture; retain actual support multipliers.
            var divisor = Convert.ToDouble(Invoke(Character, "totalEnergyPower")) * Convert.ToDouble(Invoke(Character, "totalNGUSpeedBonus"))
                * Convert.ToDouble(Invoke(Get(Character, "allDiggers"), "totalEnergyNGUBonus"));
            var magicDivisor = Convert.ToDouble(Invoke(Character, "totalMagicPower")) * Convert.ToDouble(Invoke(Character, "totalNGUSpeedBonus"))
                * Convert.ToDouble(Invoke(Get(Character, "allDiggers"), "totalMagicNGUBonus"));
            var nguCap = (long)Math.Ceiling((finite ? 1000d : 2000d) / divisor * 1.000002d);
            var magicNguCap = (long)Math.Ceiling((finite ? 1000d : 2000d) / magicDivisor * 1.000002d);
            var tmE = NativeTmBufferedCap(true); var tmM = NativeTmBufferedCap(false);
            var totalE = nguCap + tmE + 777; var totalM = magicNguCap + tmM + 999;
            Set(Character, "idleEnergy", totalE); Set(Get(Character, "magic"), "idleMagic", totalM);
            ApplyFallbackRequirements(totalE, totalM, true);
            Equal(nguCap, Get(lane, "energy"), "energy NGU receives its exact zero-lookahead/finite-target cap before TM");
            Equal(magicNguCap, Get(magicLane, "magic"), "magic NGU receives its independent zero-lookahead/finite-target cap");
            Set(lane, "level", finite ? 0L : 1L); Set(magicLane, "level", finite ? 0L : 1L);
            Equal(true, (float)Invoke(((Array)Get(Get(Character, "NGUController"), "NGU")).GetValue(0), "progressPerTick") >= 1f, "energy NGU cap black-bars the real native endpoint getter");
            Equal(true, (float)Invoke(((Array)Get(Get(Character, "NGUController"), "NGUMagic")).GetValue(0), "progressPerTick") >= 1f, "magic NGU cap black-bars the real native endpoint getter");
            Equal(tmE, Get(Get(Character, "machine"), "speedEnergy"), "TM energy uses five-second buffered cap, not unlimited idle");
            Equal(tmM, Get(Get(Character, "machine"), "goldMultiMagic"), "TM magic uses its independent native power and buffered cap");
            Equal(777L, Get(Character, "idleEnergy"), "all eligible energy sinks capped leaves exact excess idle");
            Equal(999L, Get(Get(Character, "magic"), "idleMagic"), "all eligible magic sinks capped leaves exact excess idle");
            foreach (var refresh in AllocationRefreshes)
            {
                Equal(totalE, refresh.TotalEnergy, "each fallback UI boundary sees conserved energy");
                Equal(totalM, refresh.TotalMagic, "each fallback UI boundary sees conserved magic");
            }
            var first = AllocationRefreshes.First(x => x.Key == "NGUController.updateText");
            Equal(tmE, first.TmEnergy, "NGU rendering sees the final Energy TM fallback accounting");
            Equal(tmM, first.TmMagic, "NGU rendering sees the final Magic TM fallback accounting");
        }
    }

    private static void CheckDirectAllocationRefreshes()
    {
        foreach (var kind in new[] { "AT", "Augments", "Wandoos" })
        {
            Setup(); Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
            var s = Get(CallMain("CaptureCombatPrep", .03, .03, null), "Snapshot");
            if (kind == "Augments")
            {
                var model = Get(Character, "augments"); var old = (Array)Get(model, "augs");
                var pairs = Array.CreateInstance(old.GetType().GetElementType(), 2);
                pairs.SetValue(old.GetValue(0), 0); pairs.SetValue(Blank(old.GetType().GetElementType()), 1); Set(model, "augs", pairs);
            }
            AllocationRefreshes.Clear(); AllocateFixturePhase(Phase(kind, "current", 60), s, 10000, 20000, true);
            var keys = kind == "AT" ? new[] { "AT3", "AT4" } : kind == "Augments"
                ? new[] { "AugmentController.updateAugTexts", "AugmentController.updateUpgradeTexts" } : new[] { "Wandoos98Controller.updateText" };
            foreach (var key in keys)
            {
                var observed = AllocationRefreshes.LastOrDefault(x => x.Key == key);
                Equal(true, observed != null, kind + " refreshes native allocation display " + key);
                Equal(string.Join("/", (long[])Get(Get(Character, "advancedTraining"), "energy")), string.Join("/", observed.At), "AT display observes final funded fields");
                var aug = ((Array)Get(Get(Character, "augments"), "augs")).GetValue(0);
                Equal((long)Get(aug, "augEnergy"), observed.Augments[0], "augment display observes final selected pair");
                Equal((long)Get(aug, "upgradeEnergy"), observed.Augments[1], "upgrade display observes final selected pair");
                Equal((long)Get(Get(Character, "wandoos98"), "wandoosEnergy"), observed.Wandoos[0], "W display observes final energy");
                Equal((long)Get(Get(Character, "wandoos98"), "wandoosMagic"), observed.Wandoos[1], "W display observes final magic");
                Equal(Get(Get(Character, "machine"), "speedEnergy"), observed.TmEnergy, "direct display sees the fully calculated Energy fallback");
                Equal(Get(Get(Character, "machine"), "goldMultiMagic"), observed.TmMagic, "direct display sees the fully calculated Magic fallback");
                Equal(Get(Character, "idleEnergy"), observed.IdleEnergy, "direct display sees the final exact idle Energy after all funding");
                Equal(Get(Get(Character, "magic"), "idleMagic"), observed.IdleMagic, "direct display sees the final exact idle Magic after all funding");
                Equal(10000L, observed.TotalEnergy, "direct display sees committed energy debit and credit");
                Equal(20000L, observed.TotalMagic, "direct display sees committed magic debit and credit");
            }
            if (kind == "AT")
            {
                var amounts = (long[])Get(Get(Character, "advancedTraining"), "energy");
                Equal(true, amounts[3] > 0 && amounts[4] > 0, "both paid AT displays have genuinely nonzero funded values");
                Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "paid AT never funds W energy");
                Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "paid AT never funds W magic");
                Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Take(3).Sum(), "AT funding stays pinned to pair 3/4");
            }
            if (kind == "Augments")
            {
                var pairs = (Array)Get(Get(Character, "augments"), "augs"); var selected = pairs.GetValue(0); var other = pairs.GetValue(1);
                Equal(true, (long)Get(selected, "augEnergy") > 0 && (long)Get(selected, "upgradeEnergy") > 0, "both selected augment displays have genuinely nonzero funded values");
                Equal(0L, Get(other, "augEnergy"), "augment funding stays pinned to selected pair");
                Equal(0L, Get(other, "upgradeEnergy"), "upgrade funding stays pinned to selected pair");
                Equal(0L, Get(Get(Character, "machine"), "speedEnergy"), "current Augments denies TM energy even with caller permission");
                Equal(0L, Get(Get(Character, "machine"), "goldMultiMagic"), "current Augments denies TM magic even with caller permission");
            }
            if (kind == "Wandoos") Equal(true, (long)Get(Get(Character, "wandoos98"), "wandoosEnergy") > 0 && (long)Get(Get(Character, "wandoos98"), "wandoosMagic") > 0, "W display cannot persist zero allocations after funding");
        }
    }

    private static void CheckThrowingTmConservation()
    {
        foreach (var magicFailure in new[] { false, true })
        {
            Setup(); Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
            if (magicFailure) Set(Get(Character, "machine"), "speedTarget", -1L);
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            var run = StartServerPolicy(capture, Phase("AT", "current", 60));
            var original = Get(capture, "OriginalGear"); var support = Get(Get(capture, "OriginalSupportState"), "StableKey");
            var machine = Get(Character, "machine"); var target = magicFailure ? "goldMultiMagic" : "speedEnergy";
            Set(machine, target, (long)Get(machine, target) - 1);
            var idle = magicFailure ? Get(Character, "magic") : Character; var idleField = magicFailure ? "idleMagic" : "idleEnergy";
            Set(idle, idleField, (long)Get(idle, idleField) + 1);
            AllocationRefreshes.Clear(); ThrowTmRefresh = true; ScheduledRefresh(run);
            Equal("running", Get(run, "Status"), "throwing TM UI refresh cannot fail a verified complete phase model");
            Equal("ModelAppliedRenderFailed", Get(Get(run, "ResourceOutcome"), "Disposition").ToString(), "TM rendering failure is separate from verified accounting");
            var refresh = AllocationRefreshes.First(x => x.Key == "TimeMachineController." + (magicFailure ? "updateGoldMultiText" : "updateSpeedText"));
            Equal(10000L, refresh.TotalEnergy, "throwing TM boundary sees conserved energy before cleanup");
            Equal(20000L, refresh.TotalMagic, "throwing TM boundary sees conserved magic before cleanup");
            Equal(true, (magicFailure ? refresh.TmMagic : refresh.TmEnergy) > 0, "fault occurs after actual TM credit");
            Equal(10000L, OwnedResourceTotal(true), "render failure leaves the complete Energy accounting conserved");
            Equal(20000L, OwnedResourceTotal(false), "render failure leaves the complete Magic accounting conserved");
            Equal(true, CallMain("CombatPrepGearMatches", original), "TM rendering failure does not replace gear");
            Equal(support, CallMain("CombatPrepSupportKey"), "TM rendering failure does not replace support");
            var writes = ResourceModelWrites; ScheduledRefresh(run);
            Equal(writes, ResourceModelWrites, "TM rendering retry never recalls or replays model accounting");
            Equal(true, Get(MainInstance, "CombatPrepActive"), "TM presentation-only failure retains the current combat intent");
            CallMain("EndCombatPrep", "cancelled", "TM presentation regression cleanup", true);
        }
    }

    private static void CheckAutomaticFreshPoolReplan()
    {
        var plan = ScheduledSetup(); var magic = Get(Character, "magic");
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        Set(((IList)Get(Get(Character, "wishes"), "wishes"))[190], "level", 1L);
        Set(Character, "curEnergy", 1000L); Set(Character, "idleEnergy", 1000L);
        Set(magic, "curMagic", 2000L); Set(magic, "idleMagic", 2000L);
        var run = StartScheduled(plan); var deadline = Get(run, "DeadlineRebirthSeconds"); var started = Get(run, "Started");
        var scheduler = Get(MainInstance, "_protectedScheduler");
        NativeGenerate(true); NativeGenerate(false);
        Set(Get(Character, "machine"), "realBaseGold", 1000000d); ScheduledRefresh(run);
        Equal(1, Get(run, "ReplanCount"), "generated resources plus material native GPS cause one fresh replan");
        AssertLivePools(run, 1100, 2200, "automatic fresh capture after generation");
        var snapshot = Get(Get(run, "Forecast"), "Snapshot");
        Equal(1100L, Get(Get(snapshot, "Current"), "EnergyTotal"), "fresh forecast energy is not clamped to prior run pool");
        Equal(2200L, Get(Get(snapshot, "Current"), "MagicTotal"), "fresh forecast magic is not clamped to prior run pool");
        Equal(1100L, Get(scheduler, "AutoEnergyPool"), "checkpoint records latest observed energy");
        Equal(2200L, Get(scheduler, "AutoMagicPool"), "checkpoint records latest observed magic");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "fresh pool replan keeps original deadline");
        Equal(started, Get(run, "Started"), "fresh pool replan keeps original start");
        Equal(true, Get(scheduler, "AutoWandoosStarted"), "fresh pool replan retains W history");
        Equal(false, ((Array)Get(Get(run, "Policy"), "Phases")).Cast<object>().Any(x => (string)Get(x, "Kind") == "AT"), "resource growth cannot reopen paid AT after W");
        CallMain("EndCombatPrep", "cancelled", "live resource cleanup", true);
    }

    private static void CheckFallbackIncomeDuringAt()
    {
        var plan = ScheduledSetup(); Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "augmentation"), "m_Interactable", false);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        foreach (var name in new[] { "wandoosEnergy", "wandoosMagic" }) Set(Get(Get(Character, "advancedTrainingController"), name), "levelFactor", 1000f);
        var run = StartScheduled(plan); var phase = ScheduledPhase(run); var policy = Get(run, "Policy");
        Equal("AT", Get(phase, "Kind"), "TM-income regression acquires actual paid AT");
        var targetE = Get(phase, "AtEnergyTarget"); var targetM = Get(phase, "AtMagicTarget");
        var deadline = Get(run, "DeadlineRebirthSeconds"); var phaseDeadline = Get(run, "PhaseDeadline");
        var support = CallMain("CombatPrepSupportKey"); var os = Get(Get(Character, "wandoos98"), "os");
        var machine = Get(Character, "machine"); var controller = Get(Character, "timeMachineController");
        var gps = (double)Invoke(Character, "grossGoldPerSecond");
        Equal(true, (long)Get(machine, "speedEnergy") > 0 && (long)Get(machine, "goldMultiMagic") > 0, "AT leaves both independent remainders available to TM");
        for (var tick = 0; tick < 50; tick++) { Invoke(controller, "advanceSpeedProgress"); Invoke(controller, "advanceGoldMultiProgress"); }
        Equal(true, (long)Get(machine, "levelSpeed") > 0 && (long)Get(machine, "levelGoldMulti") > 0, "actual native TM ticks grow both funded lanes");
        Equal(true, (double)Invoke(Character, "grossGoldPerSecond") > gps, "actual native TM levels increase current income");
        ScheduledRefresh(run);
        Equal("running", Get(run, "Status"), "native TM growth preserves active AT");
        Equal(true, ReferenceEquals(policy, Get(run, "Policy")), "TM income cannot replace unfinished acquired AT policy");
        Equal(0, Get(run, "ReplanCount"), "TM income replan waits for original AT gate");
        Equal(targetE, Get(ScheduledPhase(run), "AtEnergyTarget"), "TM growth preserves energy AT gate");
        Equal(targetM, Get(ScheduledPhase(run), "AtMagicTarget"), "TM growth preserves magic AT gate");
        Equal(deadline, Get(run, "DeadlineRebirthSeconds"), "TM income cannot extend global deadline");
        Equal(phaseDeadline, Get(run, "PhaseDeadline"), "TM income cannot restart phase clock");
        Equal(support, CallMain("CombatPrepSupportKey"), "TM income cannot switch pinned support");
        Equal(os, Get(Get(Character, "wandoos98"), "os"), "TM income cannot switch pinned OS");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "TM income never leaks fallback into W energy");
        Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "TM income never leaks fallback into W magic");
        Equal(10000L, OwnedResourceTotal(true), "native TM growth and refresh conserve energy");
        Equal(20000L, OwnedResourceTotal(false), "native TM growth and refresh conserve magic");
        CallMain("EndCombatPrep", "cancelled", "live resource cleanup", true);
    }

    private static object GoldProtectionFixture()
    {
        Setup();
        Set(Get(Get(Character, "buttons"), "ngu"), "m_Interactable", true);
        Set(Get(Get(Character, "buttons"), "brokenTimeMachine"), "m_Interactable", true);
        Setting("EnergyNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 });
        Setting("MagicNguWeights", new[] { 1, 0, 0, 0, 0, 0, 0 });
        Set(Get(Character, "machine"), "realBaseGold", 0d); // No forecast income can replenish the constrained gold.
        Set(Character, "realGold", 12000000d);
        var controller = ((Array)Get(Get(Character, "augmentsController"), "augments")).GetValue(0);
        Set(controller, "baseAugmentCost", 3000000f); Set(controller, "baseUpgradeCost", 4000000f);
        Equal(3000000f, Invoke(controller, "getAugCost"), "native augment cost makes TM spending capable of starving primary training");
        Equal(4000000f, Invoke(controller, "getUpgradeCost"), "native upgrade cost shares the constrained gold budget");
        var tm = Get(Character, "timeMachineController");
        Equal(5000000f, Invoke(tm, "machineSpeedGoldCost"), "native TM energy tick has a real positive gold cost");
        Equal(5000000f, Invoke(tm, "machineGoldMultiCost"), "native TM magic tick has a real positive gold cost");
        return controller;
    }

    private static void AssertTimeMachineGoldProtected(double gold)
    {
        var machine = Get(Character, "machine"); var controller = Get(Character, "timeMachineController");
        Equal(0L, Get(machine, "speedEnergy"), "augment gold protection denies TM energy allocation");
        Equal(0L, Get(machine, "goldMultiMagic"), "augment gold protection denies TM magic allocation");
        for (var tick = 0; tick < 50; tick++)
        { Invoke(controller, "advanceSpeedProgress"); Invoke(controller, "advanceGoldMultiProgress"); }
        Equal(gold, Get(Character, "realGold"), "actual native TM ticks cannot spend protected augment gold");
        Equal(0L, Get(machine, "levelSpeed"), "denied TM energy never gains a native level");
        Equal(0L, Get(machine, "levelGoldMulti"), "denied TM magic never gains a native level");
        Equal(0f, Get(machine, "speedProgress"), "denied TM energy never starts a paid bar");
        Equal(0f, Get(machine, "goldMultiProgress"), "denied TM magic never starts a paid bar");
    }

    private static void CheckFallbackGoldPermission()
    {
        foreach (var allow in new[] { false, true })
        {
            GoldProtectionFixture();
            var energyDivisor = Convert.ToDouble(Invoke(Character, "totalEnergyPower")) * Convert.ToDouble(Invoke(Character, "totalNGUSpeedBonus"))
                * Convert.ToDouble(Invoke(Get(Character, "allDiggers"), "totalEnergyNGUBonus"));
            var magicDivisor = Convert.ToDouble(Invoke(Character, "totalMagicPower")) * Convert.ToDouble(Invoke(Character, "totalNGUSpeedBonus"))
                * Convert.ToDouble(Invoke(Get(Character, "allDiggers"), "totalMagicNGUBonus"));
            var nguEnergy = (long)Math.Ceiling(2000d / energyDivisor * 1.000002d);
            var nguMagic = (long)Math.Ceiling(2000d / magicDivisor * 1.000002d);
            ApplyFallbackRequirements(900L, 700L, allow);
            Equal(nguEnergy, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "TM permission does not change exact energy NGU funding");
            Equal(nguMagic, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "TM permission does not change exact magic NGU funding");
            Equal(allow ? 900 - nguEnergy : 0L, Get(Get(Character, "machine"), "speedEnergy"), "TM energy receives only explicitly permitted remainder");
            Equal(allow ? 700 - nguMagic : 0L, Get(Get(Character, "machine"), "goldMultiMagic"), "TM magic receives only explicitly permitted remainder");
            Equal(allow ? 9100L : 10000 - nguEnergy, Get(Character, "idleEnergy"), "denied TM leaves its exact energy remainder idle");
            Equal(allow ? 19300L : 20000 - nguMagic, Get(Get(Character, "magic"), "idleMagic"), "denied TM leaves its exact magic remainder idle");
            if (!allow) AssertTimeMachineGoldProtected(12000000d);
            else
            {
                var tm = Get(Character, "timeMachineController");
                Invoke(tm, "advanceSpeedProgress"); Invoke(tm, "advanceGoldMultiProgress");
                Equal(2000000d, Get(Character, "realGold"), "positive control: permitted native TM lanes spend ten million gold");
                Equal(true, (double)Get(Character, "realGold") < 3000000d, "positive control: ungated TM leaves too little gold even to start the primary augment");
            }
            Equal(10000L, OwnedResourceTotal(true), "TM permission preserves total owned energy");
            Equal(20000L, OwnedResourceTotal(false), "TM permission preserves total owned magic");
        }
    }

    private static void CheckDirectAugmentGoldProtection()
    {
        var controller = GoldProtectionFixture();
        var snapshot = Get(CallMain("CaptureCombatPrep", .03, .03, null), "Snapshot");
        var model = Get(Character, "augments"); var original = (Array)Get(model, "augs");
        var pairs = Array.CreateInstance(original.GetType().GetElementType(), 2);
        pairs.SetValue(original.GetValue(0), 0); pairs.SetValue(Blank(original.GetType().GetElementType()), 1); Set(model, "augs", pairs);
        var selected = pairs.GetValue(0); var other = pairs.GetValue(1); var phase = Phase("Augments", "current", 60);
        AllocateFixturePhase(phase, snapshot, 10000, 20000, false);
        var energy = Get(selected, "augEnergy"); var upgrade = Get(selected, "upgradeEnergy");
        var nguEnergy = Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy");
        var nguMagic = Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic");
        Equal(true, (long)energy > 0 && (long)upgrade > 0, "constrained gold still funds both primary augment lanes");
        Equal(true, (long)nguEnergy > 0 && (long)nguMagic > 0, "gold protection retains both NGU fallback lanes");
        Equal(true, (long)Get(Character, "idleEnergy") > 0 && (long)Get(Get(Character, "magic"), "idleMagic") > 0, "direct augment fixture has real unused E/M remainders that TM could take");
        CallMain("RecallCombatPrepResources");
        AllocateFixturePhase(phase, snapshot, 10000, 20000, true);
        Equal(energy, Get(selected, "augEnergy"), "caller permission cannot change the pinned primary augment allocation");
        Equal(upgrade, Get(selected, "upgradeEnergy"), "caller permission cannot change the pinned primary upgrade allocation");
        Equal(nguEnergy, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "current augment guard preserves exact energy NGU funding");
        Equal(nguMagic, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "current augment guard preserves exact magic NGU funding");
        Equal(0L, Get(other, "augEnergy"), "gold protection cannot select another augment pair");
        Equal(0L, Get(other, "upgradeEnergy"), "gold protection cannot select another upgrade pair");
        Equal(0L, ((long[])Get(Get(Character, "advancedTraining"), "energy")).Sum(), "gold protection cannot introduce paid AT");
        AssertTimeMachineGoldProtected(12000000d);
        Invoke(controller, "advanceAug"); Invoke(controller, "advanceUpgrade");
        Equal(1L, Get(selected, "augLevel"), "protected gold lets the native primary augment complete a level");
        Equal(1L, Get(selected, "upgradeLevel"), "protected gold lets the native primary upgrade complete a level");
        Equal(5000000d, Get(Character, "realGold"), "only the native three-million/four-million augment pair spends protected gold");
        Equal(10000L, OwnedResourceTotal(true), "protected native augment ticks conserve energy");
        Equal(20000L, OwnedResourceTotal(false), "protected native augment ticks conserve magic");
    }

    private static void CheckAugmentPolicyGoldProtection()
    {
        foreach (var futureAugment in new[] { false, true })
        {
            var controller = GoldProtectionFixture(); ControlledCombatPrepClock = 10000;
            var capture = CallMain("CaptureCombatPrep", .03, .03, null);
            var augPhase = Phase("Augments", "current", 10); var wPhase = Phase("Wandoos", "current", 10);
            Set(wPhase, "EnergyResourceFraction", .03); Set(wPhase, "MagicResourceFraction", .07);
            var run = futureAugment
                ? StartServerPolicy(capture, Phase("AT", "current", 10), augPhase, wPhase)
                : StartServerPolicy(capture, augPhase, wPhase);
            var policy = Get(run, "Policy"); var started = Get(run, "Started"); var duration = Get(run, "Duration");
            var support = CallMain("CombatPrepSupportKey"); var os = Get(Get(Character, "wandoos98"), "os");
            Equal("running", Get(run, "Status"), "constrained-gold manual policy acquires real executor ownership");
            if (futureAugment)
            {
                Equal("AT", Get(ScheduledPhase(run), "Kind"), "future-augment gold guard is exercised during actual AT");
                var allocations = (long[])Get(Get(Character, "advancedTraining"), "energy");
                var energy = allocations[3]; var magic = allocations[4];
                var nguEnergy = Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy");
                var nguMagic = Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic");
                var targetE = Get(ScheduledPhase(run), "AtEnergyTarget"); var targetM = Get(ScheduledPhase(run), "AtMagicTarget");
                var phaseDeadline = Get(run, "PhaseDeadline");
                Equal(true, energy > 0 && magic > 0, "future augment does not suppress the acquired primary AT pair");
                Equal(true, (long)nguEnergy > 0 && (long)nguMagic > 0, "future augment retains both NGU fallback lanes during paid AT");
                AssertTimeMachineGoldProtected(12000000d);
                ScheduledRefresh(run);
                Equal(energy, allocations[3], "future-augment guard preserves exact primary AT3 allocation on refresh");
                Equal(magic, allocations[4], "future-augment guard preserves exact primary AT4 allocation on refresh");
                Equal(nguEnergy, Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy"), "future-augment guard preserves exact energy NGU allocation on refresh");
                Equal(nguMagic, Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic"), "future-augment guard preserves exact magic NGU allocation on refresh");
                Equal(targetE, Get(ScheduledPhase(run), "AtEnergyTarget"), "future-augment guard preserves acquired AT3 target");
                Equal(targetM, Get(ScheduledPhase(run), "AtMagicTarget"), "future-augment guard preserves acquired AT4 target");
                Equal(phaseDeadline, Get(run, "PhaseDeadline"), "future-augment guard never restarts AT timing");
                Equal(0L, Get(Get(Character, "wandoos98"), "wandoosEnergy"), "future augment cannot introduce W during paid AT");
                Equal(0L, Get(Get(Character, "wandoos98"), "wandoosMagic"), "future augment cannot introduce magic W during paid AT");
                AssertTimeMachineGoldProtected(12000000d);
                FinishTimingPhase(run);
            }
            Equal("Augments", Get(ScheduledPhase(run), "Kind"), "executor owns the current augment before permitting TM");
            var augment = ((Array)Get(Get(Character, "augments"), "augs")).GetValue(0);
            var pairEnergy = Get(augment, "augEnergy"); var pairUpgrade = Get(augment, "upgradeEnergy");
            Equal(true, (long)pairEnergy > 0 && (long)pairUpgrade > 0, "current constrained-gold pair is funded before NGU fallback");
            Equal(true, (long)Get(((IList)Get(Get(Character, "NGU"), "skills"))[0], "energy") > 0, "executor retains energy NGU while TM is denied");
            Equal(true, (long)Get(((IList)Get(Get(Character, "NGU"), "magicSkills"))[0], "magic") > 0, "executor retains magic NGU while TM is denied");
            AssertTimeMachineGoldProtected(12000000d);
            Invoke(controller, "advanceAug"); Invoke(controller, "advanceUpgrade");
            Equal(1L, Get(augment, "augLevel"), "native augment advances using gold protected by the real executor");
            Equal(1L, Get(augment, "upgradeLevel"), "native upgrade advances using gold protected by the real executor");
            Equal(5000000d, Get(Character, "realGold"), "native pair leaves exactly five million gold for later phases");
            Equal(Get(ScheduledPhase(run), "AugmentTarget"), Get(augment, "augLevel"), "native augment actually reaches the planned completion gate");
            Equal(Get(ScheduledPhase(run), "UpgradeTarget"), Get(augment, "upgradeLevel"), "native upgrade actually reaches the planned completion gate");
            ScheduledRefresh(run);
            Equal("Augments", Get(ScheduledPhase(run), "Kind"), "completed pair remains gold-protected until its phase boundary");
            AssertTimeMachineGoldProtected(5000000d);
            ControlledCombatPrepClock = (double)Get(run, "PhaseDeadline"); ScheduledRefresh(run);
            Equal("running", Get(run, "Status"), "native augment completion transitions without terminating the policy");
            Equal("Wandoos", Get(ScheduledPhase(run), "Kind"), "TM permission is released only after leaving the augment phase");
            var w = Get(Character, "wandoos98"); var machine = Get(Character, "machine");
            var nguE = ((IEnumerable)Get(Get(Character, "NGU"), "skills")).Cast<object>().Sum(x => (long)Get(x, "energy"));
            var nguM = ((IEnumerable)Get(Get(Character, "NGU"), "magicSkills")).Cast<object>().Sum(x => (long)Get(x, "magic"));
            Equal(10000L - (long)Get(w, "wandoosEnergy") - nguE, Get(machine, "speedEnergy"), "post-augment TM receives the exact remainder after primary W and NGU energy");
            Equal(20000L - (long)Get(w, "wandoosMagic") - nguM, Get(machine, "goldMultiMagic"), "post-augment TM receives the exact remainder after primary W and NGU magic");
            Equal(true, (long)Get(machine, "speedEnergy") > 0 && (long)Get(machine, "goldMultiMagic") > 0, "both native TM lanes become eligible after augment completion");
            Equal(0L, Get(augment, "augEnergy"), "post-augment transition recalls the pinned augment allocation");
            Equal(0L, Get(augment, "upgradeEnergy"), "post-augment transition recalls the pinned upgrade allocation");
            var tm = Get(Character, "timeMachineController");
            Invoke(tm, "advanceSpeedProgress"); Invoke(tm, "advanceGoldMultiProgress");
            Equal(0d, Get(Character, "realGold"), "permitted native TM spends the remaining five million only after augment completion");
            Equal(1L, Get(machine, "levelSpeed"), "released native TM energy actually progresses");
            Equal(0L, Get(machine, "levelGoldMulti"), "second native TM lane cannot spend gold already consumed by the first");
            Equal(true, ReferenceEquals(policy, Get(run, "Policy")), "gold gating never rewrites the selected manual policy");
            Equal(started, Get(run, "Started"), "gold gating preserves the original run start");
            Equal(duration, Get(run, "Duration"), "gold gating preserves the original global elapsed budget");
            Equal(support, CallMain("CombatPrepSupportKey"), "gold gating retains pinned support");
            Equal(os, Get(Get(Character, "wandoos98"), "os"), "gold gating retains pinned OS");
            Equal(10000L, OwnedResourceTotal(true), "native protected-pair/TM transition conserves energy");
            Equal(20000L, OwnedResourceTotal(false), "native protected-pair/TM transition conserves magic");
            CallMain("EndCombatPrep", "cancelled", "augment gold protection cleanup", true);
        }
    }
}
