using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class CombatPrepPreviewCache
        {
            internal string Id;
            internal double Created;
            internal CombatPrepCapture Capture;
            internal CombatPrepPlanResult Plan;
        }

        private sealed class CombatPrepRun
        {
            internal string Id, Label, Status = "running", Reason = "";
            internal SupportState ExpectedSupport;
            internal object[] ExpectedSupportObjects;
            internal string SupportStatus = "pending";
            internal SelectedSetupRequirements Setup;
            internal string[] ExpectedMaterial;
            internal string[] SetupMaterial;
            internal bool SetupVerified;
            internal bool RequirementsDue;
            internal string BlockedDomain, BlockedReason;
            internal ResourceMutationResult ResourceOutcome;
            internal bool OsInvoked;
            internal string OsOutcome;
            internal CombatPrepCapture Capture;
            internal CombatPrepCapture Forecast;
            internal Dictionary<Equipment, string> EquipmentMaterial;
            internal bool EquipmentReplanPending;
            internal double AdventureConfiguredWeight, AdventureRouteLastAttempt = -1;
            internal string AdventureIntent, AdventureObservation, AdventureStatus = "disabled";
            internal bool AdventureReplanPending, AdventureRouteOwned;
            internal bool AdventureAutoAttack;
            internal PresetCandidate AdventureRoute;
            internal object[] AdventureRouteObjects;
            internal Func<bool> EntryRouteAuthorized;
            internal string OwnerPlan, OwnerEntry, ReplanReason = "";
            internal int OwnerStage = -1, ReplanCount;
            internal double DeadlineRebirthSeconds, PolicyStarted, ObservedNetGps;
            internal bool Automatic => OwnerPlan != null;
            internal CombatPrepCapture Inputs => Forecast ?? Capture;
            internal CombatPrepAlternative Policy;
            internal double Started, Duration, PhaseStarted, PhaseDeadline, LastRefresh = -1;
            internal int PhaseIndex = -1;
            internal readonly double[] ActivitySeconds = new double[CombatPrepActivityKinds.Length];
            internal int ActivityIndex = -1;
            internal double ActivityStarted;
            internal double? EndedAt;
            internal ih[] ExpectedGear;
            internal long EnergyPool, MagicPool;
            internal int YieldedResourceLanes;
            internal int AppliedResourcePhase = -1;
            internal bool CleanupPending;
            internal bool CleanupResourcesRecalled;
            internal bool CleanupWandoosConflictRecorded;
            internal double CleanupLastAttempt = -1;
            internal int CleanupAttemptCount;
            internal bool Retired;
            internal string CleanupStatus, CleanupReason;
            internal object NativeNukeTimer, NativeNukeArbitrary, NativeNukeBoss;
            internal double OriginalNukeSeconds, NukeTimerLastReset;
            internal bool NukeTimerOwned;
            internal readonly List<Action> RestoreTargets = new List<Action>();
            internal readonly Dictionary<int, long[]> ExpectedTargets = new Dictionary<int, long[]>();
            internal readonly List<string> Warnings = new List<string>();
        }

        private CombatPrepPreviewCache _combatPrepPreview;
        private CombatPrepRun _combatPrepRun;
        private JSONObject _combatPrepRuntime;
        private double _combatPrepLastActualRefresh = -1;
        private CombatPrepCapture _combatPrepOsEpoch;
        private int _combatPrepPinnedOs;
        private bool _combatPrepOsPinDurable;
        private readonly HashSet<string> _combatPrepEndedRuns = new HashSet<string>();
        private static readonly string[] CombatPrepActivityKinds = { "AT", "Augments", "Wandoos", "NGU", "Background" };
        private const string CombatPrepOwnerPausedReason = "Scheduled owner paused, switched, disabled or current entry changed.";
        private static double CombatPrepClock => (double) Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private bool CombatPrepRunActive => _combatPrepRun != null && (_combatPrepRun.Status == "running" || _combatPrepRun.CleanupPending);
        private bool CombatPrepCleanupRecoveryRequired => !CombatPrepRunActive && _protectedScheduler?.AutoCleanupRecoveryRequired == true && ProtectedEpochMatches();
        private bool CombatPrepActive => CombatPrepRunActive || CombatPrepCleanupRecoveryRequired;
        private static readonly FieldInfo CombatPrepNukeTimerField = typeof(Arbitrary).GetField("nukeTimer", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo CombatPrepNukeSecondsField = typeof(PlayerTime).GetField("totalseconds", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo CombatPrepAutoNukeOnField = typeof(PlayerSettings).GetField("autoNukeOn", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo CombatPrepBoughtAutoNukeField = typeof(Arbitrary).GetField("boughtAutoNuke", BindingFlags.Public | BindingFlags.Instance);
        private static readonly MethodInfo CombatPrepNukeSetTime = typeof(PlayerTime).GetMethod("setTime", new[] { typeof(double) });
        private static readonly MethodInfo CombatPrepNukeThreshold = typeof(BossController).GetMethod("autoNukeThreshold", Type.EmptyTypes);

        private static bool TryCombatPrepNukeTimer(out object timer, out double seconds, out string error)
        {
            timer = null; seconds = double.NaN;
            error = "Native automatic nuke timer pause is unsupported or unavailable; execution requires Arbitrary.nukeTimer:PlayerTime, PlayerTime.totalseconds:double/setTime(double), and a 60-second autoNukeThreshold.";
            if (CombatPrepNukeTimerField?.FieldType != typeof(PlayerTime) || CombatPrepNukeSecondsField?.FieldType != typeof(double)
                || CombatPrepAutoNukeOnField?.FieldType != typeof(bool) || CombatPrepBoughtAutoNukeField?.FieldType != typeof(bool)
                || CombatPrepNukeSetTime?.ReturnType != typeof(void) || CombatPrepNukeThreshold?.ReturnType != typeof(int)
                || CombatPrepNukeSetTime.IsStatic || CombatPrepNukeThreshold.IsStatic || CombatPrepNukeSecondsField.IsInitOnly
                || Character?.arbitrary == null || Character.settings == null || Character.bossController == null) return false;
            try
            {
                timer = CombatPrepNukeTimerField.GetValue(Character.arbitrary);
                if (timer == null || timer.GetType() != typeof(PlayerTime) || (int) CombatPrepNukeThreshold.Invoke(Character.bossController, null) != 60) return false;
                seconds = (double) CombatPrepNukeSecondsField.GetValue(timer);
                if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return false;
                error = null;
                return true;
            }
            catch { return false; }
        }

        private static string CombatPrepNativeBossDiagnostic()
        {
            object timer; double seconds; string error;
            TryCombatPrepNukeTimer(out timer, out seconds, out error);
            var auto = "unavailable"; var bought = "unavailable";
            try
            {
                if (CombatPrepAutoNukeOnField?.FieldType == typeof(bool) && Character?.settings != null)
                    auto = (bool) CombatPrepAutoNukeOnField.GetValue(Character.settings) ? "true" : "false";
                if (CombatPrepBoughtAutoNukeField?.FieldType == typeof(bool) && Character?.arbitrary != null)
                    bought = (bool) CombatPrepBoughtAutoNukeField.GetValue(Character.arbitrary) ? "true" : "false";
            }
            catch { }
            var boss = Character?.bossController;
            return "nativeNukeTimer=" + (double.IsNaN(seconds) ? "unavailable" : seconds.ToString("R", CultureInfo.InvariantCulture))
                + "; autoNukeOn=" + auto + "; boughtAutoNuke=" + bought
                + "; bossController.isFighting=" + (boss == null ? "unavailable" : boss.isFighting.ToString())
                + "; bossController.nukeBoss=" + (boss == null ? "unavailable" : boss.nukeBoss.ToString());
        }

        private string CombatPrepCombatPreemption()
        {
            var predicates = new List<string>();
            if (_manualRebirthPending) predicates.Add("_manualRebirthPending=true");
            if (_customPreRebirthPrepRunning) predicates.Add("_customPreRebirthPrepRunning=true");
            if (BossPushRunning) predicates.Add("BossPushRunning=true");
            var boss = Character?.bossController;
            if (boss != null && boss.isFighting) predicates.Add("bossController.isFighting=true");
            if (boss != null && boss.nukeBoss) predicates.Add("bossController.nukeBoss=true");
            return predicates.Count == 0 ? null : "Combat/equipment preemption: " + string.Join(", ", predicates.ToArray()) + "; " + CombatPrepNativeBossDiagnostic();
        }

        private void PauseCombatPrepNativeNuke(CombatPrepRun run, bool acquire)
        {
            var epoch = new LoadoutManager.SnapshotEpoch();
            var settings = Settings;
            var phase = run.PhaseIndex; var policy = run.Policy;
            if (!ReferenceEquals(reference, this) || _unloaded || !ReferenceEquals(_combatPrepRun, run)
                || !CombatPrepEpochMatches(run.Capture)
                || run.Automatic && !AutomaticCombatPrepOwnerMatches(run))
                throw new InvalidOperationException("Native automatic nuke pause authority changed.");
            object timer; double seconds; string error;
            if (!TryCombatPrepNukeTimer(out timer, out seconds, out error)) throw new InvalidOperationException(error);
            var conflict = CombatPrepCombatPreemption();
            if (conflict != null) throw new InvalidOperationException(conflict);
            if (acquire)
            {
                run.NativeNukeTimer = timer; run.NativeNukeArbitrary = Character.arbitrary; run.NativeNukeBoss = Character.bossController;
                run.OriginalNukeSeconds = seconds;
            }
            else if (!run.NukeTimerOwned || !ReferenceEquals(timer, run.NativeNukeTimer) || !ReferenceEquals(Character.arbitrary, run.NativeNukeArbitrary)
                || !ReferenceEquals(Character.bossController, run.NativeNukeBoss) || seconds >= 60
                || seconds > Math.Max(0, CombatPrepClock - run.NukeTimerLastReset) + 1)
            {
                run.NukeTimerOwned = false;
                throw new InvalidOperationException("Native automatic nuke timer ownership changed; " + CombatPrepNativeBossDiagnostic());
            }
            Exception rendering = null;
            var grant = ResolveExecutionGrant("combat-nuke-pause:" + run.Id, run.Id, ExecutionDomain.NativeSettings,
                () => ReferenceEquals(_combatPrepRun, run) && CombatPrepEpochMatches(run.Capture)
                    && ReferenceEquals(policy, run.Policy) && phase == run.PhaseIndex && CombatPrepCombatPreemption() == null);
            try
            {
                if (!ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings)) throw new InvalidOperationException("Nuke pause grant changed.");
                if (seconds != 0) CombatPrepNukeSetTime.Invoke(timer, new object[] { 0d });
                if (!ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings)) throw new InvalidOperationException("Nuke pause callback authority changed.");
            }
            catch (Exception failure) { rendering = failure; }
            if (!epoch.Matches() || !ReferenceEquals(reference, this) || !ReferenceEquals(_combatPrepRun, run)
                || !ReferenceEquals(Settings, settings)
                || !ReferenceEquals(policy, run.Policy) || phase != run.PhaseIndex || !CombatPrepEpochMatches(run.Capture)
                || !ReferenceEquals(timer, CombatPrepNukeTimerField.GetValue(Character.arbitrary))
                || CombatPrepCombatPreemption() != null || run.Automatic && !AutomaticCombatPrepOwnerMatches(run)
                || (double)CombatPrepNukeSecondsField.GetValue(timer) != 0)
            {
                run.NukeTimerOwned = false;
                throw new InvalidOperationException("Native automatic nuke timer pause was not verified; no return authority acquired.", rendering);
            }
            run.NukeTimerLastReset = CombatPrepClock;
            run.NukeTimerOwned = true;
            if (rendering != null) CombatPrepCleanupWarning(run, "Native nuke timer model committed; presentation failed: " + rendering.Message);
        }

        private void RestoreCombatPrepNativeNuke(CombatPrepRun run, bool allowRestore)
        {
            if (!run.NukeTimerOwned) return;
            try
            {
                object timer; double seconds; string error;
                if (allowRestore && CombatPrepReturnAuthority(run) && CombatPrepCombatPreemption() == null
                    && TryCombatPrepNukeTimer(out timer, out seconds, out error) && ReferenceEquals(timer, run.NativeNukeTimer)
                    && ReferenceEquals(Character.arbitrary, run.NativeNukeArbitrary) && ReferenceEquals(Character.bossController, run.NativeNukeBoss)
                    && seconds < 60 && seconds <= Math.Max(0, CombatPrepClock - run.NukeTimerLastReset) + 1)
                {
                    Exception rendering = null;
                    var grant = ResolveExecutionGrantCore("combat-nuke-return:" + run.Id, run.Id, ExecutionDomain.NativeSettings,
                        () => CombatPrepReturnAuthority(run) && CombatPrepCombatPreemption() == null
                            && ReferenceEquals(Character.arbitrary, run.NativeNukeArbitrary)
                            && ReferenceEquals(Character.bossController, run.NativeNukeBoss)
                            && ReferenceEquals(timer, CombatPrepNukeTimerField.GetValue(Character.arbitrary)), null, null, true);
                    try
                    {
                        if (!ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings)) throw new InvalidOperationException("Nuke return grant changed.");
                        if (seconds != run.OriginalNukeSeconds) CombatPrepNukeSetTime.Invoke(timer, new object[] { run.OriginalNukeSeconds });
                        if (!ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings)) throw new InvalidOperationException("Nuke return callback authority changed.");
                    }
                    catch (Exception failure) { rendering = failure; }
                    if ((double) CombatPrepNukeSecondsField.GetValue(timer) != run.OriginalNukeSeconds) throw new InvalidOperationException("Native automatic nuke timer did not restore its original value.");
                    if (!ReferenceEquals(reference, this) || !ReferenceEquals(_combatPrepRun, run) || !CombatPrepEpochMatches(run.Capture)
                        || !ReferenceEquals(timer, CombatPrepNukeTimerField.GetValue(Character.arbitrary)))
                        throw new InvalidOperationException("Native nuke timer return authority changed during the callback.");
                    if (rendering != null) CombatPrepCleanupWarning(run, "Native nuke timer return committed; presentation failed: " + rendering.Message);
                    run.NukeTimerOwned = false;
                    CombatPrepCleanupWarning(run, "Native automatic nuke timer restored to its original " + run.OriginalNukeSeconds.ToString("R", CultureInfo.InvariantCulture) + " seconds; autoNukeOn was not changed.");
                }
                else
                {
                    run.NukeTimerOwned = false;
                    CombatPrepCleanupWarning(run, "Native automatic nuke timer was not restored after an ownership/epoch change or combat handoff; preferences and fight flags were left untouched.");
                }
            }
            catch (Exception error)
            {
                run.Status = "failed"; CombatPrepCleanupWarning(run, "Native automatic nuke timer restoration failed: " + error.Message, error);
                run.NukeTimerOwned = CombatPrepReturnAuthority(run) && CombatPrepNativeNukeStillOwned(run);
            }
        }

        private bool CombatPrepNativeNukeStillOwned(CombatPrepRun run)
        {
            object timer; double seconds; string error;
            return run.NukeTimerOwned && TryCombatPrepNukeTimer(out timer, out seconds, out error)
                && ReferenceEquals(timer, run.NativeNukeTimer) && ReferenceEquals(Character.arbitrary, run.NativeNukeArbitrary)
                && ReferenceEquals(Character.bossController, run.NativeNukeBoss) && seconds < 60
                && seconds <= Math.Max(0, CombatPrepClock - run.NukeTimerLastReset) + 1;
        }

        private static double CombatPrepNumber(JSONNode args, string key, double fallback, double min, double max)
        {
            var node = args?[key];
            if (args == null || !args.Linq.Any(x => x.Key == key)) return fallback;
            if (!node.IsNumber || double.IsNaN(node.AsDouble) || double.IsInfinity(node.AsDouble) || node.AsDouble < min || node.AsDouble > max)
                throw new FormatException("args." + key + " must be finite and between " + min + " and " + max + ".");
            return node.AsDouble;
        }

        private static void CombatPrepArgs(JSONNode args, params string[] names)
        {
            if (args != null && !args.IsNull && (!args.IsObject || args.Linq.Any(x => !names.Contains(x.Key))))
                throw new FormatException("Unexpected combat prep arguments.");
        }

        private static string CombatPrepToken(JSONNode args, string key)
        {
            var node = args?[key];
            if (node == null || !node.IsString || string.IsNullOrWhiteSpace(node.Value) || node.Value.Length > 128)
                throw new FormatException("args." + key + " must be a nonempty string of at most 128 characters.");
            return node.Value;
        }

        private string PreviewCombatPrep(JSONNode args, out JSONNode data)
        {
            const string message = "Read-only combat prep preview; no equipment, allocation, settings, plan or scheduler changes.";
            CombatPrepArgs(args, "durationSeconds", "backgroundEnergyPercent", "backgroundMagicPercent");
            var seconds = CombatPrepNumber(args, "durationSeconds", 7200, 1, 86400);
            if (seconds != Math.Floor(seconds)) throw new FormatException("args.durationSeconds must be an integer between 1 and 86400.");
            var energy = CombatPrepNumber(args, "backgroundEnergyPercent", 3, 0, 100);
            var magic = CombatPrepNumber(args, "backgroundMagicPercent", 3, 0, 100);
            var timer = Stopwatch.StartNew();
            var capture = CaptureCombatPrep(energy / 100, magic / 100);
            ApplyCombatPrepOsPin(capture);
            var plan = CombatPrepPlanner.Plan(capture.Snapshot, seconds);
            if (!plan.Success) throw new InvalidOperationException(plan.Error);
            var cache = new CombatPrepPreviewCache { Id = Guid.NewGuid().ToString("N"), Created = CombatPrepClock, Capture = capture, Plan = plan };
            var preview = new JSONObject();
            preview["schemaVersion"] = 1;
            preview["previewId"] = cache.Id;
            preview["generatedAt"] = DateTime.UtcNow.ToString("o");
            preview["durationSeconds"] = plan.DurationSeconds;
            preview["backgroundEnergyPercent"] = energy;
            preview["backgroundMagicPercent"] = magic;
            preview["recommendedCandidateId"] = "candidate-" + Array.IndexOf(plan.Alternatives, plan.Recommendation);
            preview["evaluationCount"] = plan.EvaluatedCandidates;
            preview["elapsedMilliseconds"] = timer.ElapsedMilliseconds;
            preview["assumptions"] = ToJsonArray(plan.Assumptions.Concat(new[] { "At most 12 ranked alternatives are returned; duplicate-family extras are omitted before family winners when needed to keep the complete response below the "
                + ExternalUiActionResultMaxBytes + "-byte action-result limit. Baseline and recommendation are retained." }).ToArray());
            preview["baseline"] = CombatPrepTotals(plan.Baseline);
            preview["baseline"]["support"] = CombatPrepSupportJson(capture.SupportContext, capture.Support["current"].Evaluation, 1, false);
            var candidates = new JSONArray();
            for (var i = 0; i < plan.Alternatives.Length; i++)
            {
                var policy = plan.Alternatives[i];
                var candidate = new JSONObject();
                candidate["id"] = "candidate-" + i;
                candidate["label"] = policy.Name + " / " + capture.Snapshot.OperatingSystems.Single(x => x.Id == policy.OsId).Name;
                candidate["os"] = policy.OsId;
                var blocker = policy.Phases.Length == 0 ? "Snapshot baseline only; no executable phases." : CombatPrepStartBlocker();
                candidate["available"] = blocker == null;
                candidate["reason"] = blocker ?? "";
                candidate["augmentSeconds"] = policy.Phases.Where(x => x.Kind == "Augments").Sum(x => x.DurationSeconds);
                candidate["atSeconds"] = policy.Phases.Where(x => x.Kind == "AT").Sum(x => x.DurationSeconds);
                candidate["wandoosSeconds"] = policy.Phases.Where(x => x.Kind == "Wandoos").Sum(x => x.DurationSeconds);
                candidate["backgroundSeconds"] = policy.Phases.Where(x => x.Kind == "Background").Sum(x => x.DurationSeconds);
                candidate["atEnergyShare"] = policy.Phases.FirstOrDefault(x => x.Kind == "AT")?.AtEnergyWeight ?? .5;
                candidate["augmentIndex"] = policy.Phases.FirstOrDefault(x => x.Kind == "Augments")?.AugmentTierId ?? -1;
                var phases = new JSONArray();
                foreach (var phase in policy.Phases)
                {
                    var row = new JSONObject();
                    row["kind"] = phase.Kind; row["label"] = phase.Kind; row["seconds"] = phase.DurationSeconds; row["gearGoal"] = phase.GearGoal;
                    var support = capture.Support[phase.GearProfileKey];
                    row["supportVariantKey"] = phase.GearProfileKey;
                    row["support"] = CombatPrepSupportJson(capture.SupportContext, support.Evaluation, support.BudgetFraction, support.EnforceBudget);
                    var gear = capture.Gear[phase.GearProfileKey].Where(x => x.id != 0).ToArray();
                    var ids = new JSONArray(); foreach (var item in gear) ids.Add(item.id);
                    row["gearIds"] = ids; row["gearNames"] = ToJsonArray(gear.Select(x => x.name ?? x.id.ToString()).ToArray());
                    row["energyAllocation"] = phase.Kind == "AT" ? "Capped AT-3/AT-4 " + phase.AtEnergyWeight.ToString("P0", CultureInfo.InvariantCulture)
                        + "/" + (1 - phase.AtEnergyWeight).ToString("P0", CultureInfo.InvariantCulture) + "; remainder weighted NGU"
                        : phase.Kind == "Augments" ? "Chosen augment/upgrade pair, equal capped shares; remainder weighted NGU"
                        : "Wandoos capped at " + (phase.EnergyResourceFraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "% of energy pool; remainder weighted NGU";
                    row["magicAllocation"] = phase.Kind == "AT" || phase.Kind == "NGU" || phase.Kind == "Augments" && !phase.BackgroundMagicWandoos
                        ? "Weighted NGU only; zero Wandoos"
                        : "Wandoos capped at " + (phase.MagicResourceFraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "% of magic pool; remainder weighted NGU";
                    row["atEnergyTarget"] = phase.AtEnergyTarget.ToString(CultureInfo.InvariantCulture);
                    row["atMagicTarget"] = phase.AtMagicTarget.ToString(CultureInfo.InvariantCulture);
                    phases.Add(row);
                }
                candidate["phases"] = phases;
                var projected = CombatPrepTotals(policy);
                projected["energyAtLevel"] = policy.AtEnergyLevel.ToString(CultureInfo.InvariantCulture);
                projected["magicAtLevel"] = policy.AtMagicLevel.ToString(CultureInfo.InvariantCulture);
                projected["wandoosEnergyLevel"] = policy.WandoosEnergyLevel.ToString(CultureInfo.InvariantCulture);
                projected["wandoosMagicLevel"] = policy.WandoosMagicLevel.ToString(CultureInfo.InvariantCulture);
                projected["goldRemaining"] = CombatPrepFinite(policy.GoldRemaining);
                candidate["projected"] = projected;
                candidate["gainFactor"] = CombatPrepFinite(policy.Gain);
                candidate["energyFocusEquivalentSeconds"] = policy.EnergyFocusEquivalentKnown ? (JSONNode) new JSONNumber(policy.EnergyFocusEquivalentSeconds) : JSONNull.CreateOrGet();
                candidate["magicFocusEquivalentSeconds"] = policy.MagicFocusEquivalentKnown ? (JSONNode) new JSONNumber(policy.MagicFocusEquivalentSeconds) : JSONNull.CreateOrGet();
                candidate["resourceReferenceProfileKey"] = policy.ResourceReferenceProfileKey;
                candidate["energyResourceFocusEquivalentSeconds"] = policy.EnergyResourceFocusEquivalentSeconds;
                candidate["magicResourceFocusEquivalentSeconds"] = policy.MagicResourceFocusEquivalentSeconds;
                candidate["energyResourceFocusCostKnown"] = policy.EnergyResourceFocusCostKnown;
                candidate["magicResourceFocusCostKnown"] = policy.MagicResourceFocusCostKnown;
                candidates.Add(candidate);
            }
            preview["candidates"] = candidates;
            data = new JSONObject(); data["combatPrepPreview"] = preview;
            // Attachment/publication adds the shared runtime and the command wrapper. Measure the
            // actual pretty-print format, reserving the largest legal ID and runtime revision.
            var runtime = BuildExternalUiRuntimeState(); runtime["runtimeRevision"] = ExternalUiMaxRuntimeRevision;
            var envelopeData = new JSONObject(); envelopeData["combatPrepPreview"] = preview; envelopeData["runtime"] = runtime;
            var envelope = new JSONObject(); envelope["id"] = new string('x', 128); envelope["command"] = "previewCombatPrep";
            envelope["success"] = true; envelope["message"] = message; envelope["completedAt"] = DateTime.UtcNow.ToString("o"); envelope["data"] = envelopeData;
            var baselineId = "candidate-" + Array.IndexOf(plan.Alternatives, plan.Baseline);
            var familyWinners = new HashSet<string>(plan.Alternatives.Select((policy, index) => new { policy.Family, Id = "candidate-" + index })
                .GroupBy(x => x.Family).Select(x => x.First().Id));
            while (Encoding.UTF8.GetByteCount(data.ToString()) >= ExternalUiActionResultMaxBytes - 4 * 1024
                || Encoding.UTF8.GetByteCount(envelope.ToString(2)) >= ExternalUiActionResultMaxBytes)
            {
                var eligible = candidates.Children.Where(x => x["id"].Value != baselineId && x["id"].Value != preview["recommendedCandidateId"].Value).ToArray();
                var removable = eligible.LastOrDefault(x => !familyWinners.Contains(x["id"].Value)) ?? eligible.LastOrDefault();
                if (removable == null) throw new InvalidOperationException("Combat prep preview exceeds the " + ExternalUiActionResultMaxBytes + "-byte action-result bound.");
                candidates.Remove(removable);
            }
            _combatPrepPreview = cache;
            return message;
        }

        private void ApplyCombatPrepOsPin(CombatPrepCapture capture)
        {
            var path = string.IsNullOrEmpty(_dir) ? null : Path.Combine(_dir, "combat-prep-os.json");
            if ((_combatPrepOsEpoch == null || !CombatPrepEpochMatches(_combatPrepOsEpoch)) && path != null && File.Exists(path))
            {
                try
                {
                    var pin = JsonFiles.ParseObject(File.ReadAllText(path));
                    long rebirth;
                    if (!pin["version"].IsNumber || pin["version"].AsDouble != 1 && pin["version"].AsDouble != 2
                        || pin["version"].AsDouble == 2 && (!pin["action"].IsString || string.IsNullOrEmpty(pin["action"].Value)
                            || !pin["runId"].IsString || !pin["entry"].IsString || !pin["invocation"].IsBoolean)
                        || !pin["rebirth"].IsString
                        || !long.TryParse(pin["rebirth"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rebirth)
                        || !FiniteCheckpointNumber(pin["origin"], false) || !FiniteCheckpointNumber(pin["elapsed"], true)
                        || !FiniteCheckpointNumber(pin["total"], true) || !pin["os"].IsNumber || pin["os"].AsDouble < 0
                        || pin["os"].AsDouble > 2 || pin["os"].AsDouble != Math.Floor(pin["os"].AsDouble))
                        throw new FormatException("Invalid OS pin metadata.");
                    if (rebirth == capture.Rebirth && Math.Abs(pin["origin"].AsDouble - capture.Origin) <= 2
                        && pin["elapsed"].AsDouble <= capture.Elapsed + .1 && pin["total"].AsDouble <= capture.Total + .1)
                    {
                        _combatPrepOsEpoch = capture; _combatPrepPinnedOs = pin["os"].AsInt; _combatPrepOsPinDurable = true;
                    }
                }
                catch
                {
                    // An unreadable pin never grants permission to automatically select another OS.
                    _combatPrepOsEpoch = capture; _combatPrepPinnedOs = capture.Snapshot.CurrentOsId; _combatPrepOsPinDurable = false;
                    capture.Snapshot.CaptureAssumptions += " OS pin metadata was unreadable; only the current OS is allowed.";
                }
            }
            if (_combatPrepOsEpoch == null || !CombatPrepEpochMatches(_combatPrepOsEpoch)) return;
            if (capture.Snapshot.CurrentOsId != _combatPrepPinnedOs)
                throw new InvalidOperationException("The pinned OS changed externally; no further automatic OS changes this rebirth.");
            foreach (var os in capture.Snapshot.OperatingSystems) os.Unlocked &= os.Id == _combatPrepPinnedOs;
        }

        private void PersistCombatPrepOsPin(CombatPrepCapture capture, int os)
        {
            if (_combatPrepOsPinDurable && _combatPrepOsEpoch != null && CombatPrepEpochMatches(_combatPrepOsEpoch)
                && _combatPrepPinnedOs == os) return;
            if (string.IsNullOrEmpty(_dir)) throw new InvalidOperationException("Combat prep cannot durably pin the OS without its runtime directory.");
            var pin = new JSONObject(); pin["version"] = 2; pin["rebirth"] = capture.Rebirth.ToString(CultureInfo.InvariantCulture);
            var run = _combatPrepRun;
            if (run == null || !ReferenceEquals(run.Capture, capture)) throw new InvalidOperationException("OS pin requires its current acquired run.");
            pin["runId"] = run.Id; pin["entry"] = run.OwnerEntry ?? "manual";
            pin["action"] = run.Id + ":" + (run.OwnerEntry ?? "manual") + ":OS:" + os;
            pin["invocation"] = (int)Character.wandoos98.os != os;
            pin["origin"] = capture.Origin; pin["elapsed"] = capture.Elapsed; pin["total"] = capture.Total; pin["os"] = os;
            JsonFiles.WriteAtomic(Path.Combine(_dir, "combat-prep-os.json"), pin.ToString());
            _combatPrepOsEpoch = capture; _combatPrepPinnedOs = os; _combatPrepOsPinDurable = true;
        }

        private string CombatPrepStartBlocker(CombatPrepRun acquisition = null)
        {
            if (CombatPrepActive) return "A combat prep run is already active.";
            if (_customRequestedFocusGoal.HasValue || _customSustainedFocusGoal.HasValue)
            {
                try
                {
                    var selection = _focusSetupSelection;
                    if (!FocusSetupCurrent(selection) || Settings.UseCustomRebirthPlan || _customFocusActivationPending
                        || _customFocusStatus != "applied" || _customAppliedFocusGoal != selection.Goal
                        || _customSustainedFocusGoal != selection.Goal || !selection.Desired.MaterialValid()
                        || !FocusSetupObservationMatches(selection) || !LoadoutManager.ExactLoadoutMatches(selection.Desired.Gear)
                        || CaptureSupportState().Selection.StableKey != selection.Desired.Support.StableKey
                        || _customFocusResourceOutcome?.Verified != true
                        || selection.Goal != CustomFocusGoal.MagicNgu && Settings.ManageEnergy
                            && _customFocusEnergyCleanupOwned?.Invoke() != true
                        || Settings.ManageMagic && _customFocusMagicCleanupOwned?.Invoke() != true
                        || _resourceSetupExecutor.Application.BlockedLanes != 0)
                        return "Current focus setup is incomplete or changed; apply the focus again before combat prep.";
                    PreflightExactSupport(selection.Desired.Support, selection.Desired.Budget, selection.Desired.EnforceSupportBudget);
                }
                catch (Exception focusError) { return "Current focus setup is not ready for combat prep: " + focusError.Message; }
            }
            if (Character?.stats != null && SelectedSetupConflicts.TryGetValue(Character, out var conflict)
                && conflict.Rebirth == Character.stats.rebirthNumber && (conflict.Gear != null || conflict.Support != null
                    || conflict.Route != null && Character.adventure.zone != -1)
                && !(acquisition?.Automatic == true && _selectedSetupReviewAuthorized?.Invoke("combat-stage:" + acquisition.OwnerPlan + ":" + acquisition.OwnerEntry) == true))
                return "Combat setup requires review: " + (conflict.Gear ?? conflict.Support ?? conflict.Route);
            if (Settings == null || !Settings.ManageEnergy || !Settings.ManageMagic) return "Combat prep requires managed energy AND magic; unmanaged lanes are never recalled.";
            var combat = CombatPrepCombatPreemption();
            if (combat != null) return combat;
            if (Controller.midDrag) return "Inventory drag is active.";
            if (_customTitanGoldActive || _customTitanFightActive || _customAdventureGoldPushActive || _maxItopodPushRemainingDeaths > 0)
                return "Adventure/titan preparation owns equipment.";
            if (Character.adventure != null && ZoneHelpers.ZoneIsTitan(Character.adventure.zone)) return "Leave the active titan zone before combat prep.";
            if (CustomPlanStore == null) return "A readable selected plan is required for leftover NGU weights.";
            object timer; double seconds; string error;
            if (!TryCombatPrepNukeTimer(out timer, out seconds, out error)) return error;
            return null;
        }

        private string StartCombatPrep(JSONNode args)
        {
            CombatPrepArgs(args, "previewId", "candidateId");
            var previewId = CombatPrepToken(args, "previewId"); var candidateId = CombatPrepToken(args, "candidateId");
            var blocker = CombatPrepStartBlocker();
            if (blocker != null) throw new InvalidOperationException(blocker);
            var cache = _combatPrepPreview;
            if (cache == null || cache.Id != previewId || CombatPrepClock - cache.Created > 120)
                throw new InvalidOperationException("Preview is missing, consumed or older than 120 seconds; preview again.");
            var index = Array.FindIndex(cache.Plan.Alternatives, x => "candidate-" + Array.IndexOf(cache.Plan.Alternatives, x) == candidateId);
            if (index < 0 || cache.Plan.Alternatives[index].Phases.Length == 0) throw new InvalidOperationException("Unknown or non-executable candidate.");
            if (!CombatPrepEpochMatches(cache.Capture) || cache.Capture.Material != CombatPrepMaterialKey())
                throw new InvalidOperationException("Save epoch or material inputs changed; preview again.");
            VerifyCombatPrepGear(cache.Capture, true);
            // Passive growth may change rates/gold/progress. Replay ONLY the cached server-owned policy against fresh inputs.
            var capture = CaptureCombatPrep(cache.Capture.Snapshot.BackgroundEnergyFraction, cache.Capture.Snapshot.BackgroundMagicFraction, cache.Capture);
            ApplyCombatPrepOsPin(capture);
            var selected = cache.Plan.Alternatives[index];
            var policy = CombatPrepPlanner.Project(capture.Snapshot, selected.OsId, selected.Phases);
            // Validate every pinned phase, including native input readiness, before consuming the token or pausing any owner.
            foreach (var key in policy.Phases.Select(x => x.GearProfileKey).Distinct())
            {
                var support = capture.Support[key];
                PreflightExactSupport(support.Evaluation.Selection, support.BudgetFraction, support.EnforceBudget);
            }
            if (CustomPlanStore.Load(Settings.CustomPlanFile)?.SourceJson == null)
                throw new InvalidOperationException("A readable selected plan is required for leftover NGU weights.");
            var run = new CombatPrepRun { Id = Guid.NewGuid().ToString("N"), Label = selected.Name + " / " + capture.Snapshot.OperatingSystems.Single(x => x.Id == policy.OsId).Name, Capture = capture, Policy = policy,
                Started = CombatPrepClock, Duration = cache.Plan.DurationSeconds, ExpectedSupport = capture.OriginalSupportState, ExpectedGear = capture.OriginalGear,
                EnergyPool = capture.Snapshot.Current.EnergyTotal, MagicPool = capture.Snapshot.Current.MagicTotal };
            InitializeCombatPrepTargets(run);
            run.Warnings.Add("Support uses pinned current/phase selections and exact fixed digger levels, compared on the same gear; not globally optimized. Passive beard growth is not forecast.");
            run.Warnings.Add("Forecast is a start-time replay with frozen bootup/non-gear factors; actual rates, float rounding, polling and setup can differ. The wall-clock deadline never extends.");
            run.Warnings.Add("Both augment and AT targets gate later phases. No Wandoos of either resource during AT. Original gear is the fixed boss reporting setup.");
            _combatPrepPreview = null;
            BeginCombatPrepRun(run);
            return "Combat prep started: " + run.Id + ". Auto-rebirth and normal allocation are overlaid, not saved or disabled.";
        }

        private static void InitializeCombatPrepTargets(CombatPrepRun run)
        {
            run.ExpectedTargets.Clear();
            for (var i = 0; i < run.Policy.Phases.Length; i++)
            {
                var phase = run.Policy.Phases[i];
                if (phase.Kind == "AT") run.ExpectedTargets.Add(i, new[] { Character.advancedTraining.levelTarget[3], Character.advancedTraining.levelTarget[4] });
                if (phase.Kind == "Augments")
                {
                    var aug = Character.augments.augs[phase.AugmentTierId];
                    run.ExpectedTargets.Add(i, new[] { aug.augmentTarget, aug.upgradeTarget });
                }
            }
        }

        private void BeginCombatPrepRun(CombatPrepRun run)
        {
            if (run.Automatic && (CombatPrepClock - run.Started >= run.Duration || Character.rebirthTime.totalseconds >= run.DeadlineRebirthSeconds))
                throw new InvalidOperationException("Automatic planning exhausted the original elapsed budget before acquisition.");
            AcquireCombatPrepEntryRoute(run);
            _combatPrepRun = run;
            run.ExpectedMaterial = run.ExpectedGear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            run.ExpectedSupportObjects = SupportObjects();
            SuspendProtectedStageProgress();
            try
            {
                PauseCombatPrepNativeNuke(run, true);
                if (_resourceReconciliationRunning) { AdvanceCombatPrep(); ReconcileCombatPrepRequirements(); }
                else TickCombatPrep();
                if (run.CleanupPending || !CombatPrepActive) throw new InvalidOperationException(run.Reason);
            }
            catch (Exception error)
            {
                if (!ReferenceEquals(reference, this) || _unloaded || Settings == null) { RetireCombatPrepAuthority(run); throw; }
                if (!run.CleanupPending) EndCombatPrep("failed", error.Message);
                throw;
            }
        }

        private string CancelCombatPrep(JSONNode args)
        {
            CombatPrepArgs(args, "runId"); var id = CombatPrepToken(args, "runId");
            if (_combatPrepEndedRuns.Contains(id)) return "Combat prep run is already inactive: " + id;
            if (!CombatPrepRunActive || _combatPrepRun.Id != id) throw new InvalidOperationException("Run ID does not match the active combat prep run.");
            if (_combatPrepRun.Automatic && CombatPrepEntryAuthority(_combatPrepRun))
                _automaticCancelledEntry = AutomaticRecoveryEntry;
            if (_combatPrepRun.CleanupPending) TickCombatPrep();
            else EndCombatPrep("cancelled", "Cancelled by user.");
            if (_combatPrepRun.CleanupPending) return "Combat prep cancellation cleanup is pending; normal automation remains blocked: " + id;
            if (CombatPrepCleanupRecoveryRequired) return ProtectedStageScheduler.CleanupRecoveryReason;
            return "Combat prep cancelled: " + id;
        }

        private void RequireCombatPrepIdle()
        {
            if (!CombatPrepRunActive) ServiceAutomaticRecovery();
            if (CombatPrepActive) throw new InvalidOperationException("Combat prep owns equipment/resources; cancel its run before this command.");
        }

        private static void RecallCombatPrepResources()
        {
            // Explicit release/gear-transition intent, never the recurring allocation strategy.
            var run = reference?._combatPrepRun;
            var energy = Settings.ManageEnergy && ((run?.YieldedResourceLanes ?? 0) & 1) == 0;
            var magic = Settings.ManageMagic && ((run?.YieldedResourceLanes ?? 0) & 2) == 0;
            var owner = reference;
            var settings = Settings;
            var epoch = new LoadoutManager.SnapshotEpoch();
            var result = owner._resourceSetupExecutor.Reconcile(new ResourceSetupIntent {
                Owner = "combat-release:" + (run?.Id ?? "explicit"), Desired = ResourceWriter.EmptyDesiredMap(energy, magic),
                Energy = energy, Magic = magic, Authorized = () => ReferenceEquals(reference, owner) && epoch.Matches()
                    && ReferenceEquals(settings, Settings) && ReferenceEquals(owner._combatPrepRun, run)
                    && (run == null || CombatPrepEpochMatches(run.Capture) && !run.Retired
                        && (!run.Automatic || owner.CombatPrepEntryAuthority(run)))
                    && (!energy || Settings.ManageEnergy) && (!magic || Settings.ManageMagic)
                    && Character.bossController != null && !Character.bossController.isFighting && !Character.bossController.nukeBoss
                    && Character.adventure.zone == -1 });
            if (run != null) run.ResourceOutcome = result;
            if (!result.Verified)
            {
                if (run != null && result.Disposition != MutationDisposition.RejectedUnchanged)
                {
                    run.YieldedResourceLanes |= result.AffectedLanes == 0 ? 3 : result.AffectedLanes;
                }
                throw new ResourceMutationException(result);
            }
            if (energy) BlackBarAllocator.ClearNguAppliedWeights(true);
            if (magic) BlackBarAllocator.ClearNguAppliedWeights(false);
        }

        private void TickCombatPrep()
        {
            if (!ReferenceEquals(reference, this) || _unloaded) { RetireCombatPrepAuthority(_combatPrepRun); return; }
            ReconcileCustomResources();
        }

        private void AdvanceCombatPrep()
        {
            if (!ReferenceEquals(reference, this) || _unloaded)
            {
                RetireCombatPrepAuthority(_combatPrepRun);
                return;
            }
            if (!CombatPrepRunActive) return;
            var run = _combatPrepRun;
            if (run.CleanupPending)
            {
                EndCombatPrep(run.CleanupStatus, run.CleanupReason);
                return;
            }
            try
            {
                if (!CombatPrepEpochMatches(run.Capture)) { EndCombatPrep("cancelled", "Save/rebirth epoch changed.", false); return; }
                if (run.Automatic && !AutomaticCombatPrepOwnerMatches(run))
                { EndCombatPrep("cancelled", CombatPrepOwnerPausedReason); return; }
                run.Capture.Elapsed = Character.rebirthTime.totalseconds; run.Capture.Total = Character.totalPlaytime.totalseconds;
                var combat = CombatPrepCombatPreemption();
                if (combat != null) { EndCombatPrep("cancelled", combat, false); return; }
                // BossController.Update runs independently of our allocation loops. Hold its idle
                // timer every active frame, not merely at the half-second allocation cadence.
                PauseCombatPrepNativeNuke(run, false);
                if (!CombatPrepTargetsMatch(run))
                { EndCombatPrep("cancelled", "Training targets changed externally; manual target edits are retained."); return; }
                if (!CombatPrepSupportStillOwned(run))
                {
                    run.SupportStatus = "ownership-lost";
                    RecordSelectedSetupConflict((Character)run.Capture.SupportObjects[0], run.Capture.Rebirth, "support", "Combat support changed externally.");
                    EndCombatPrep("cancelled", "Support selection, flags, levels or receipt ownership changed externally; manual support is retained."); return;
                }
                var now = CombatPrepClock;
                var phase = run.PhaseIndex < 0 ? null : run.Policy.Phases[run.PhaseIndex];
                if (run.Automatic && phase?.Kind == "AT" && CombatPrepHasNativeWandoosWork())
                {
                    EndCombatPrep("cancelled", "Wandoos work appeared during paid AT; further paid AT is forbidden.");
                    return;
                }
                if (now - run.Started >= run.Duration || run.Automatic && Character.rebirthTime.totalseconds >= run.DeadlineRebirthSeconds)
                {
                    var reached = phase != null && run.SetupVerified && run.ResourceOutcome?.Verified == true
                        && run.AppliedResourcePhase == run.PhaseIndex && CombatPrepTargetsReached(phase);
                    var laterPhases = run.PhaseIndex < run.Policy.Phases.Length - 1;
                    EndCombatPrep(reached && !laterPhases ? "completed" : "cancelled", !reached
                        ? "Deadline reached before " + (phase?.Kind ?? "setup") + " targets; later Wandoos was not started."
                        : laterPhases ? "Deadline reached before later phases could start; Wandoos budget was not extended." : "Original wall-clock budget completed.");
                    return;
                }
                if (now - run.LastRefresh < run.Inputs.Snapshot.AllocationRefreshSeconds) return;
                if (!Settings.ManageEnergy || !Settings.ManageMagic) { EndCombatPrep("cancelled", "Resource management was disabled."); return; }
                RequireCombatPrepContext();
                if (run.OsOutcome != null && (int) Character.wandoos98.os != run.Policy.OsId)
                { EndCombatPrep("cancelled", "OS changed externally."); return; }
                var liveSupport = CaptureSupportContext();
                if (CombatPrepSupportMaterial(liveSupport) != run.Inputs.SupportMaterial)
                { EndCombatPrep("cancelled", "Support purchased levels, slots or availability changed; pinned support will not be resized."); return; }
                if (!CombatPrepGearMatches(run.ExpectedGear))
                {
                    RecordSelectedSetupConflict((Character)run.Capture.SupportObjects[0], run.Capture.Rebirth, "gear", "Combat gear changed externally.");
                    EndCombatPrep("cancelled", "Equipment changed externally; manual gear is retained."); return;
                }
                var equipmentChanged = run.EquipmentReplanPending;
                var gatedPhase = phase ?? (run.Automatic && AutomaticContinuationPending ? run.Policy.Phases.FirstOrDefault() : null);
                var atGatePending = gatedPhase?.Kind == "AT" && !CombatPrepTargetsReached(gatedPhase);
                if (run.Automatic)
                {
                    ObserveCombatPrepAdventureChange(run);
                    if (equipmentChanged && atGatePending)
                    {
                        RefreshAutomaticAtResources(run, gatedPhase);
                    }
                }
                if (run.ExpectedGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != run.ExpectedMaterial[i]).Any())
                {
                    run.BlockedDomain = "gear";
                    run.BlockedReason = "Equipped material changed without an owned action; current setup requires review.";
                    RecordSelectedSetupConflict((Character)run.Capture.SupportObjects[0], run.Capture.Rebirth, "gear", run.BlockedReason);
                    SetResourceExecution(ResourceExecutionState.NeedsUser, "combat:" + run.Id, run.BlockedReason);
                    return;
                }
                if (Controller.midDrag) { EndCombatPrep("cancelled", "Inventory drag started.", false); return; }
                var boundary = phase != null && run.SetupVerified && run.ResourceOutcome?.Verified == true
                    && run.AppliedResourcePhase == run.PhaseIndex && now >= run.PhaseDeadline && CombatPrepTargetsReached(phase);
                var netGps = Character.grossGoldPerSecond() - Character.totalGPSDrain();
                var gpsShift = Math.Abs(netGps - run.ObservedNetGps) >= Math.Max(1, Math.Abs(run.ObservedNetGps) * .1);
                // A new forecast must never discard an already-acquired, unfinished paid AT gate.
                gpsShift &= !atGatePending;
                var equipmentReplan = !atGatePending && (equipmentChanged || run.EquipmentReplanPending);
                var adventureReplan = !atGatePending && run.AdventureReplanPending;
                if (run.Automatic && run.SetupVerified && run.ResourceOutcome?.Verified == true
                    && run.AppliedResourcePhase == run.PhaseIndex && (boundary || gpsShift || equipmentReplan || adventureReplan))
                {
                    if (boundary)
                    {
                        if (phase.Kind == "Augments") _protectedScheduler.AutoAugmentsCompleted = true;
                        if (phase.Kind == "AT") _protectedScheduler.AutoAtCompleted = true;
                    }
                    if (gatedPhase?.Kind == "AT" && CombatPrepTargetsReached(gatedPhase)) _protectedScheduler.AutoAtCompleted = true;
                    if (_protectedScheduler.AutoAtCompleted) _automaticAtContinuation = null;
                    ReplanAutomaticCombatPrep(run, equipmentReplan ? "material-equipment-change"
                        : boundary ? "phase-completed:" + phase.Kind : adventureReplan ? "adventure-intent-change" : "material-gps-change");
                    now = CombatPrepClock;
                    phase = null;
                    if (now - run.Started >= run.Duration || Character.rebirthTime.totalseconds >= run.DeadlineRebirthSeconds)
                    { EndCombatPrep("cancelled", "Replanning exhausted the original elapsed budget; no new phase was acquired."); return; }
                }
                var acquiredPhase = false;
                if (phase == null || boundary)
                {
                    SettleCombatPrepActivity(run, CombatPrepClock);
                    RestoreCombatPrepTargets(run);
                    run.PhaseIndex++;
                    if (run.PhaseIndex >= run.Policy.Phases.Length) { EndCombatPrep("completed", "All phase budgets completed."); return; }
                    phase = run.Policy.Phases[run.PhaseIndex];
                    // Scheduled absolute boundaries include ALL setup and lateness, never restart the global budget.
                    run.PhaseStarted = run.Automatic && phase.Kind == "AT" && _automaticAtContinuation?["entry"].Value == AutomaticRecoveryEntry
                        ? run.Started + _automaticAtContinuation["phaseStart"].AsDouble : CombatPrepClock;
                    run.ActivityIndex = Array.IndexOf(CombatPrepActivityKinds, phase.Kind);
                    run.ActivityStarted = run.Automatic && phase.Kind == "AT" && _automaticAtContinuation?["entry"].Value == AutomaticRecoveryEntry
                        ? CombatPrepClock : run.PhaseStarted;
                    run.PhaseDeadline = (run.Automatic ? run.PolicyStarted : run.Started) + run.Policy.Phases.Take(run.PhaseIndex + 1).Sum(x => x.DurationSeconds);
                    SelectCombatPrepRequirements(run, phase);
                    if (!CombatPrepTargetsMatch(run))
                    { EndCombatPrep("cancelled", "Training targets changed before phase acquisition; manual target edits are retained."); return; }
                    if (run.Automatic && phase.Kind == "AT")
                    {
                        var saved = new JSONObject(); saved["entry"] = AutomaticRecoveryEntry;
                        saved["energyTarget"] = phase.AtEnergyTarget.ToString(CultureInfo.InvariantCulture);
                        saved["magicTarget"] = phase.AtMagicTarget.ToString(CultureInfo.InvariantCulture);
                        saved["weight"] = phase.AtEnergyWeight;
                        // Keep the original acquired gate's window across fresh captures and native target-menu overwrites.
                        saved["phaseStart"] = _automaticAtContinuation?["entry"].Value == AutomaticRecoveryEntry
                            ? _automaticAtContinuation["phaseStart"].AsDouble : Math.Max(0, run.PhaseStarted - run.Started);
                        saved["phaseEnd"] = Math.Max(0, run.PhaseDeadline - run.Started);
                        _automaticAtContinuation = saved;
                        if (!AutomaticCombatPrepOwnerMatches(run) || !SaveProtectedStageProgress(true))
                            throw new InvalidOperationException("Cannot save acquired AT continuation targets before funding; check checkpoint permissions or file locks.");
                    }
                    acquiredPhase = true;
                }
                if (acquiredPhase) run.ObservedNetGps = Character.grossGoldPerSecond() - Character.totalGPSDrain();
                run.RequirementsDue = true;
                run.LastRefresh = CombatPrepClock;
            }
            catch (Exception error)
            {
                if (!ReferenceEquals(reference, this) || _unloaded || Settings == null) { RetireCombatPrepAuthority(run); return; }
                EndCombatPrep("failed", error.Message); LogError("Combat prep phase", error);
            }
        }

        private void ReconcileCombatPrepRequirements()
        {
            var run = _combatPrepRun;
            if (run == null || run.Status != "running" || run.CleanupPending || run.Setup == null || !run.RequirementsDue) return;
            run.RequirementsDue = false;
            var phase = run.Policy.Phases[run.PhaseIndex];
            try
            {
                if (run.BlockedReason != null)
                {
                    SetResourceExecution(ResourceExecutionState.NeedsUser, "combat:" + run.Id, run.BlockedReason);
                    return;
                }
                SuspendProtectedStageProgress();
                if (CombatPrepClock - run.Started >= run.Duration || run.Automatic && Character.rebirthTime.totalseconds >= run.DeadlineRebirthSeconds)
                { EndCombatPrep("cancelled", "Phase setup exhausted the original wall-clock budget."); return; }
                ApplyCombatPrepRequirements(run, phase);
                if (!CombatPrepRequirementsVerified(run, phase)) return;
                ApplyCombatPrepOperatingSystem(run, phase);
                if (run.RestoreTargets.Count == 0) SetCombatPrepTargets(run, phase);
                var pinnedSupport = run.Inputs.Support[phase.GearProfileKey];
                var feasibleSupport = SupportProjection.Evaluate(CaptureSupportContext(), pinnedSupport.Evaluation.Selection,
                    pinnedSupport.BudgetFraction, pinnedSupport.EnforceBudget);
                if (!feasibleSupport.Feasible || !CombatPrepSupportStillOwned(run))
                    throw new InvalidOperationException("Pinned support is no longer owned or sustainable; no resources funded. " + feasibleSupport.Reason);
                var plan = CustomPlanStore == null ? null : LoadRuntimeCustomPlan();
                if (plan?.SourceJson == null) throw new InvalidOperationException("A readable selected plan is required for leftover NGU weights.");
                var basic = Character.training.attackEnergy.Concat(Character.training.defenseEnergy).Aggregate(0m, (sum, x) => sum + Math.Max(0, x));
                // Captured totals constrain forecasts, not resources the game has actually generated.
                run.EnergyPool = Math.Min(ResourceWriter.Reclaimable(true), (long)Math.Max(0m, (decimal)Character.totalCapEnergy() - basic));
                run.MagicPool = Math.Min(ResourceWriter.Reclaimable(false), Math.Max(0, Character.totalCapMagic()));
                if (run.Automatic)
                {
                    if (!AutomaticCombatPrepOwnerMatches(run)) throw new InvalidOperationException("Scheduled owner changed before allocation.");
                    _protectedScheduler.AutoEnergyPool = run.EnergyPool; _protectedScheduler.AutoMagicPool = run.MagicPool;
                    var wandoosStarted = false;
                    if (phase.Kind == "Wandoos" || phase.Kind == "Background" || phase.BackgroundMagicWandoos)
                    {
                        wandoosStarted = !_protectedScheduler.AutoWandoosStarted;
                        _protectedScheduler.AutoWandoosStarted = true;
                    }
                    _protectedLastElapsed = Character.rebirthTime.totalseconds;
                    _protectedLastTotal = Character.totalPlaytime.totalseconds;
                    var saved = SaveProtectedStageProgress(wandoosStarted); // W history is durable before funding; observed pools use the normal heartbeat.
                    if (wandoosStarted ? !saved : !_protectedCheckpointDurable) throw new InvalidOperationException("Cannot durably record automatic continuation ownership.");
                }
                var allowTimeMachine = !run.Policy.Phases.Skip(run.PhaseIndex).Any(x => x.Kind == "Augments");
                var desired = ResolveCombatPrepResources(phase, run.Inputs.Snapshot, run.EnergyPool, run.MagicPool, allowTimeMachine, out var fallbacks);
                var settings = Settings; var epoch = new LoadoutManager.SnapshotEpoch(); var setup = run.Setup; var os = Character.wandoos98.os;
                var resourceObjects = ResourceWriter.CaptureResourceContext(Character);
                Func<bool> resourceContextOwned = () => {
                    var current = ResourceWriter.CaptureResourceContext(Character);
                    return current.Length == resourceObjects.Length && !current.Where((item, i) => !ReferenceEquals(item, resourceObjects[i])).Any();
                };
                run.ResourceOutcome = _resourceSetupExecutor.Reconcile(new ResourceSetupIntent {
                    Owner = "combat:" + run.Id, Desired = desired, Energy = settings.ManageEnergy, Magic = settings.ManageMagic,
                    Authorized = () => epoch.Matches() && ReferenceEquals(Settings, settings) && ReferenceEquals(run.Setup, setup)
                        && Character.wandoos98.os == os && CombatPrepRequirementsVerified(run, phase)
                        && resourceContextOwned() });
                if (!run.ResourceOutcome.Verified) throw new ResourceMutationException(run.ResourceOutcome);
                foreach (var fallback in fallbacks) BlackBarAllocator.ObserveApplied(fallback);
                run.AppliedResourcePhase = run.PhaseIndex;
                if (CombatPrepClock - run.Started >= run.Duration || run.Automatic && Character.rebirthTime.totalseconds >= run.DeadlineRebirthSeconds)
                {
                    var complete = CombatPrepTargetsReached(phase) && run.PhaseIndex == run.Policy.Phases.Length - 1;
                    EndCombatPrep(complete ? "completed" : "cancelled", "Original deadline reached during resource application; no later phase or route was admitted.");
                    return;
                }
                if (run.Automatic) RouteCombatPrepAdventure();
                if (!ReferenceEquals(reference, this) || _unloaded) { RetireCombatPrepAuthority(run); return; }
                SetResourceExecution(ResourceExecutionState.Applied, "combat:" + run.Id, "");
                SetCustomAutomationStatus("Combat prep: " + phase.Kind + "; normal allocation and auto-rebirth paused by runtime overlay.");
                CacheCombatPrepRuntime();
            }
            catch (Exception error)
            {
                if (!ReferenceEquals(reference, this) || _unloaded || Settings == null) { RetireCombatPrepAuthority(run); return; }
                var mutation = error as ResourceMutationException;
                var gear = error as SelectedSetupException;
                var support = error as SupportApplicationException;
                if (run.Setup?.ResourceOutcome != null)
                { run.ResourceOutcome = run.Setup.ResourceOutcome; run.YieldedResourceLanes |= run.ResourceOutcome.AffectedLanes; }
                if (error is SelectedSetupAuthorityException authority)
                { run.BlockedDomain = authority.Domain; run.BlockedReason = authority.Message; }
                var disposition = mutation?.Result.Disposition ?? gear?.Outcome.Disposition ?? support?.Result.Disposition;
                if (disposition == MutationDisposition.ForeignConflict || disposition == MutationDisposition.UnknownOutcome)
                {
                    if (mutation != null) run.YieldedResourceLanes |= mutation.Result.AffectedLanes;
                    else { run.BlockedDomain = gear != null ? "gear" : "support"; run.BlockedReason = error.Message; }
                }
                run.AppliedResourcePhase = -1;
                run.Reason = error.Message;
                if (!CombatPrepDeadlineAvailable(run))
                {
                    EndCombatPrep("cancelled", "Original deadline reached during application; no later phase or route was admitted.");
                    return;
                }
                SetResourceExecution(run.BlockedReason != null || run.YieldedResourceLanes != 0 ? ResourceExecutionState.NeedsUser
                    : ResourceExecutionState.Waiting, "combat:" + run.Id, error.Message);
                CacheCombatPrepRuntime();
            }
        }

        private static bool CombatPrepTargetsReached(CombatPrepPhase phase)
        {
            if (phase.Kind == "AT") return Character.advancedTraining.level[3] >= phase.AtEnergyTarget && Character.advancedTraining.level[4] >= phase.AtMagicTarget;
            if (phase.Kind != "Augments") return true;
            var aug = Character.augments.augs[phase.AugmentTierId];
            return aug.augLevel >= phase.AugmentTarget && aug.upgradeLevel >= phase.UpgradeTarget;
        }

        private static void SetCombatPrepTargets(CombatPrepRun run, CombatPrepPhase phase)
        {
            var owner = reference;
            var epoch = new LoadoutManager.SnapshotEpoch();
            var settings = Settings; var file = settings.CustomPlanFile;
            var preset = owner?._globalPresetReceipt; var focus = owner?._customRequestedFocusGoal;
            if (owner == null || !owner.CombatPrepRequirementsVerified(run, phase))
                throw new InvalidOperationException("Combat target prerequisites are unverified.");
            Func<bool> returnOwned = () => ReferenceEquals(reference, owner) && ReferenceEquals(owner._combatPrepRun, run)
                && ReferenceEquals(Settings, settings) && settings.CustomPlanFile == file
                && ReferenceEquals(owner._globalPresetReceipt, preset) && owner._customRequestedFocusGoal == focus
                && epoch.Matches() && owner.CombatPrepReturnAuthority(run);
            var phaseIndex = run.PhaseIndex;
            var targetGrant = owner.ResolveExecutionGrant("combat-targets:" + run.Id, run.Setup.Acquisition,
                ExecutionDomain.NativeSettings | ExecutionDomain.Energy | ExecutionDomain.Magic | ExecutionDomain.Gear | ExecutionDomain.Support,
                () => returnOwned() && run.Status == "running" && !run.CleanupPending && run.PhaseIndex == phaseIndex
                    && owner.CombatPrepDeadlineAvailable(run) && CombatPrepGearMatches(run.ExpectedGear) && owner.CombatPrepSupportStillOwned(run));
            Action require = () => { if (!owner.ValidateExecutionGrant(targetGrant, targetGrant.Domains))
                throw new InvalidOperationException("Combat borrowed target authority changed."); };
            Action<Action> returnField = write => {
                var grant = owner.ResolveExecutionGrantCore("combat-target-return:" + run.Id, run.Id, ExecutionDomain.NativeSettings,
                    returnOwned, null, null, true);
                if (!owner.ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings)) return;
                write();
                if (!owner.ValidateExecutionGrant(grant, ExecutionDomain.NativeSettings))
                    throw new InvalidOperationException("Combat borrowed target return authority changed.");
            };
            // Unlimited menu targets prevent the game's auto-advance from moving our pair into unrelated lanes.
            if (phase.Kind == "AT")
                foreach (var id in new[] { 3, 4 })
                {
                    var targets = Character.advancedTraining.levelTarget; var saved = targets[id]; require(); targets[id] = 0; require();
                    run.RestoreTargets.Add(() => { if (returnOwned() && ReferenceEquals(Character.advancedTraining.levelTarget, targets)
                        && targets[id] == 0) returnField(() => targets[id] = saved); });
                }
            if (phase.Kind == "Augments")
            {
                var aug = Character.augments.augs[phase.AugmentTierId];
                var a = aug.augmentTarget; var u = aug.upgradeTarget;
                require(); aug.augmentTarget = 0; require(); aug.upgradeTarget = 0; require();
                run.RestoreTargets.Add(() => { if (returnOwned() && ReferenceEquals(Character.augments.augs[phase.AugmentTierId], aug)
                    && aug.augmentTarget == 0) returnField(() => aug.augmentTarget = a); });
                run.RestoreTargets.Add(() => { if (returnOwned() && ReferenceEquals(Character.augments.augs[phase.AugmentTierId], aug)
                    && aug.upgradeTarget == 0) returnField(() => aug.upgradeTarget = u); });
            }
        }

        private static bool CombatPrepTargetsMatch(CombatPrepRun run)
        {
            foreach (var pair in run.ExpectedTargets.Where(x => x.Key >= run.PhaseIndex))
            {
                var phase = run.Policy.Phases[pair.Key];
                var owned = pair.Key == run.PhaseIndex && run.RestoreTargets.Count > 0;
                var first = owned ? 0 : pair.Value[0]; var second = owned ? 0 : pair.Value[1];
                if (phase.Kind == "AT")
                {
                    if (Character.advancedTraining.levelTarget[3] != first || Character.advancedTraining.levelTarget[4] != second) return false;
                }
                else
                {
                    var aug = Character.augments.augs[phase.AugmentTierId];
                    if (aug.augmentTarget != first || aug.upgradeTarget != second) return false;
                }
            }
            return true;
        }

        private static void RestoreCombatPrepTargets(CombatPrepRun run)
        {
            Exception failure = null;
            foreach (var restore in run.RestoreTargets.ToArray())
            {
                try { restore(); run.RestoreTargets.Remove(restore); }
                catch (Exception error) { failure = failure ?? error; }
            }
            if (failure != null) throw new InvalidOperationException("Combat prep target restoration failed.", failure);
        }

        private static long CombatPrepAmount(double amount) { return amount >= long.MaxValue ? long.MaxValue : amount <= 0 ? 0 : (long) amount; }

        private static long CombatPrepLaneCap(double unit, long budget)
        {
            if (double.IsNaN(unit) || unit < 0) throw new InvalidOperationException("Invalid live training rate.");
            return unit <= 0 || unit * budget <= 1e-9 ? 0 : Math.Min(budget, CombatPrepAmount(Math.Ceiling(1 / unit)));
        }

        private static Dictionary<ResourceTarget, long[]> ResolveCombatPrepResources(CombatPrepPhase phase, CombatPrepSnapshot snapshot,
            long energyPool, long magicPool, bool allowTimeMachine, out BlackBarAllocator.PriorityResourcePlan[] fallbacks)
        {
            var c = Character;
            var basic = c.training.attackEnergy.Concat(c.training.defenseEnergy).Aggregate(0m, (sum, x) => sum + Math.Max(0, x));
            var energy = Math.Max(0, Math.Min(energyPool, Math.Min(ResourceWriter.Reclaimable(true), (long)Math.Max(0m, (decimal)c.totalCapEnergy() - basic))));
            var magic = Math.Max(0, Math.Min(magicPool, Math.Min(ResourceWriter.Reclaimable(false), c.totalCapMagic())));
            var desired = ResourceWriter.EmptyDesiredMap(true, true);
            long first = 0, second = 0, spentEnergy = 0, spentMagic = 0;
            if (phase.Kind == "AT" || phase.Kind == "Augments")
            {
                var caps = new long[2];
                if (phase.Kind == "AT")
                {
                    var controllers = new[] { c.advancedTrainingController.wandoosEnergy, c.advancedTrainingController.wandoosMagic };
                    var targets = new[] { snapshot.AtEnergy.TargetLevel, snapshot.AtMagic.TargetLevel };
                    for (var i = 0; i < 2; i++)
                    {
                        var unit = Math.Sqrt(c.totalEnergyPower()) * c.totalAdvancedTrainingSpeedBonus() / (50d * controllers[i].baseTime * ((double) c.advancedTraining.level[i + 3] + 1));
                        var unlocked = i == 0 ? snapshot.AtEnergy.Unlocked : snapshot.AtMagic.Unlocked;
                        caps[i] = !unlocked || c.advancedTraining.level[i + 3] == long.MaxValue
                            || targets[i] > 0 && c.advancedTraining.level[i + 3] >= targets[i] ? 0 : CombatPrepLaneCap(unit, energy);
                    }
                }
                else
                {
                    var aug = c.augments.augs[phase.AugmentTierId]; var controller = c.augmentsController.augments[phase.AugmentTierId];
                    caps[0] = aug.augLevel == long.MaxValue || controller.augLocked() || aug.augProgress == 0 && c.realGold < controller.getAugCost()
                        ? 0 : CombatPrepLiveAugCap(controller, false, energy);
                    caps[1] = aug.upgradeLevel == long.MaxValue || controller.upgradeLocked() || aug.upgradeProgress == 0
                        && c.realGold < controller.getUpgradeCost() + (caps[0] > 0 && aug.augProgress == 0 ? controller.getAugCost() : 0)
                        ? 0 : CombatPrepLiveAugCap(controller, true, energy);
                }
                var weight = phase.Kind == "AT" ? phase.AtEnergyWeight : .5;
                var shares = ResourceAllocation.SplitPair(caps[0], caps[1], energy, weight);
                first = shares[0]; second = shares[1];
                if (phase.Kind == "AT")
                {
                    desired[ResourceTarget.AdvancedTraining][3] = first;
                    desired[ResourceTarget.AdvancedTraining][4] = second;
                }
                else
                {
                    desired[ResourceTarget.Augment][phase.AugmentTierId] = first;
                    desired[ResourceTarget.AugmentUpgrade][phase.AugmentTierId] = second;
                }
                spentEnergy = first + second;
            }
            var wEnergy = phase.Kind == "Wandoos" || phase.Kind == "Background";
            var wMagic = wEnergy || phase.Kind == "Augments" && phase.BackgroundMagicWandoos;
            if (wEnergy)
            {
                var amount = c.wandoos98.energyLevel == long.MaxValue ? 0 : CombatPrepGetterCap(amountToSample => c.wandoos98Controller.energyProgressToAdd(amountToSample),
                    Math.Min(energy, CombatPrepAmount(Math.Floor(energy * phase.EnergyResourceFraction))));
                desired[ResourceTarget.WandoosEnergy][0] = amount;
                spentEnergy += amount;
            }
            if (wMagic)
            {
                var amount = c.wandoos98.magicLevel == long.MaxValue ? 0 : CombatPrepGetterCap(amountToSample => c.wandoos98Controller.magicProgressToAdd(amountToSample),
                    Math.Min(magic, CombatPrepAmount(Math.Floor(magic * phase.MagicResourceFraction))));
                desired[ResourceTarget.WandoosMagic][0] = amount;
                spentMagic += amount;
            }
            fallbacks = BlackBarAllocator.CalculateCombatPrepFallbacks(desired, energy - spentEnergy, magic - spentMagic, allowTimeMachine && phase.Kind != "Augments");
            return desired;
        }

        private static long CombatPrepLiveAugCap(AugmentController controller, bool upgrade, long budget)
        {
            return CombatPrepGetterCap(amount => CombatPrepAugProgress(controller, upgrade, amount), budget);
        }

        private static long CombatPrepGetterCap(Func<long, double> rate, long budget)
        {
            if (budget <= 0) return 0;
            var q = rate(budget);
            if (double.IsNaN(q) || q < 0) throw new InvalidOperationException("Invalid live amount getter rate.");
            if (q <= 1e-9) return 0;
            if (q < 1) return budget;
            // The game's amount getters round to float and may clamp. Find the real one-tick cap without an inverse-clamp estimate.
            long low = 1, high = budget;
            while (low < high)
            {
                var mid = low + (high - low) / 2;
                if (rate(mid) >= 1) high = mid; else low = mid + 1;
            }
            return low;
        }

        private void EndCombatPrep(string status, string reason, bool releaseOwned = true)
        {
            if (!ReferenceEquals(reference, this))
            {
                RetireCombatPrepAuthority(_combatPrepRun);
                return;
            }
            if (!CombatPrepRunActive) return;
            var run = _combatPrepRun;
            FreezeCombatPrepTiming(run);
            var sameEpoch = CombatPrepEpochMatches(run.Capture);
            if (sameEpoch && !CombatPrepGearMatches(run.ExpectedGear))
                RecordSelectedSetupConflict((Character)run.Capture.SupportObjects[0], run.Capture.Rebirth, "gear", "Combat gear changed before release.");
            if (sameEpoch && !CombatPrepSupportStillOwned(run))
                RecordSelectedSetupConflict((Character)run.Capture.SupportObjects[0], run.Capture.Rebirth, "support", "Combat support changed before release.");
            var retry = run.CleanupPending;
            if (run.Automatic)
            {
                run.CleanupStatus = status; run.CleanupReason = reason;
                var entryAuthority = CombatPrepEntryAuthority(run);
                if (entryAuthority && AutomaticContinuationPending && !run.CleanupWandoosConflictRecorded && CombatPrepHasNativeWandoosWork())
                {
                    // A recall destroys allocation-only history. Save this entry's conflict before any cleanup/handoff can erase it.
                    _automaticSafeResourcesEntry = AutomaticRecoveryEntry;
                    if (!SaveProtectedStageProgress(true))
                    {
                        RetireCombatPrepAuthority(run);
                        run.Status = "failed";
                        run.Reason = "Wandoos conflict checkpoint could not be saved; native allocations retained. Check runtime directory permissions or file locks; automatic recovery will retry.";
                        CombatPrepCleanupWarning(run, run.Reason);
                        _combatPrepEndedRuns.Add(run.Id);
                        _automaticRecoveryRetryEntry = AutomaticRecoveryEntry;
                        _automaticRecoveryRetryAt = CombatPrepClock + 1;
                        CacheCombatPrepRuntime();
                        SetResourceExecution(ResourceExecutionState.Failed, AutomaticRecoveryEntry, run.Reason);
                        return;
                    }
                    run.CleanupWandoosConflictRecorded = true;
                }
            }
            if (!retry) Log("Combat prep ending runId=" + run.Id + "; status=" + status + "; reason=" + reason + "; " + CombatPrepNativeBossDiagnostic());
            run.CleanupStatus = status; run.CleanupReason = reason;
            run.Status = status; run.Reason = reason;
            // Ending the claim never creates an implicit historical setup target.
            // A still-owned active route must retire before any prerequisite release.
            if (!sameEpoch || CombatPrepCombatPreemption() != null || !releaseOwned
                || run.Automatic && !CombatPrepEntryAuthority(run))
            {
                if (sameEpoch && releaseOwned && CombatPrepCombatPreemption() == null && CombatPrepReturnAuthority(run))
                {
                    if (retry && !_unloaded && CombatPrepClock - run.CleanupLastAttempt < .5) return;
                    run.CleanupLastAttempt = CombatPrepClock;
                    if (run.CleanupAttemptCount < int.MaxValue) run.CleanupAttemptCount++;
                    try
                    {
                        RetireCombatPrepAdventureRoute(run);
                        RestoreCombatPrepTargets(run);
                        RestoreCombatPrepNativeNuke(run, true);
                        if (run.NukeTimerOwned) throw new InvalidOperationException("Native nuke timer return is pending.");
                    }
                    catch (Exception error)
                    {
                        run.CleanupPending = true;
                        CombatPrepCleanupWarning(run, "Borrowed setting return waiting: " + error.Message, error);
                        CacheCombatPrepRuntime();
                        return;
                    }
                }
                RestoreCombatPrepNativeNuke(run, false);
                RetireCombatPrepAuthority(run);
                run.Status = status; run.Reason = reason;
                _combatPrepEndedRuns.Add(run.Id);
                CacheCombatPrepRuntime();
                return;
            }
            try { PauseCombatPrepNativeNuke(run, false); }
            catch (Exception error) { RelinquishCombatPrepCleanup(run, error.Message); return; }
            if (retry && !_unloaded && CombatPrepClock - run.CleanupLastAttempt < .5) return;
            run.CleanupLastAttempt = CombatPrepClock;
            if (run.CleanupAttemptCount < int.MaxValue) run.CleanupAttemptCount++;
            try
            {
                RetireCombatPrepAdventureRoute(run);
                RestoreCombatPrepTargets(run);
                if (Character.adventure.zone == -1 && !run.CleanupResourcesRecalled && !CombatPrepHasResourceSuccessor(run, status, reason))
                {
                    RecallCombatPrepResources();
                    run.CleanupResourcesRecalled = true;
                }
            }
            catch (Exception error)
            {
                if (error is ResourceMutationException mutation)
                { run.ResourceOutcome = mutation.Result; run.YieldedResourceLanes |= mutation.Result.AffectedLanes; }
                if (error is SelectedSetupAuthorityException authority)
                { run.BlockedDomain = authority.Domain; run.BlockedReason = authority.Message; }
                run.CleanupPending = true;
                CombatPrepCleanupWarning(run, "Combat release waiting: " + error.Message, error);
                if (_unloaded) RelinquishCombatPrepCleanup(run, "Live release authority retired on unload.");
                CacheCombatPrepRuntime();
                return;
            }
            run.CleanupPending = false;
            RestoreCombatPrepNativeNuke(run, true);
            if (run.NukeTimerOwned)
            { run.CleanupPending = true; CacheCombatPrepRuntime(); return; }
            if (!ReferenceEquals(reference, this) || !CombatPrepEpochMatches(run.Capture)) { RetireCombatPrepAuthority(run); return; }
            run.Setup = null;
            run.SetupVerified = false;
            _combatPrepEndedRuns.Add(run.Id);
            InvalidateCustomRuntimePlanCache();
            _customLastQuickResourceRefresh = DateTime.MinValue;
            _resourceDemandIdentity = null;
            if (run.Automatic && CombatPrepEntryAuthority(run))
            {
                TryCommitProtectedEntryChange(scheduler => {
                    scheduler.AutoCleanupRecoveryRequired = false;
                    if (_protectedCheckpointDurable && reason != "Injector unloaded." && (status != "cancelled" || reason != CombatPrepOwnerPausedReason)
                        && (run.YieldedResourceLanes == 0 || Character.rebirthTime.totalseconds >= run.DeadlineRebirthSeconds
                            || _automaticCancelledEntry == AutomaticRecoveryEntry)
                        && (!AutomaticContinuationPending || _automaticCancelledEntry == AutomaticRecoveryEntry))
                        scheduler.CompleteAutomaticEntry(Character.rebirthTime.totalseconds);
                });
                InvalidateCustomEquipmentApplication();
                _customStageKey = null;
            }
            CacheCombatPrepRuntime();
            SetCustomAutomationStatus("Combat prep " + run.Status + ": " + reason);
        }

        private static void CombatPrepCleanupWarning(CombatPrepRun run, string message, Exception error = null)
        {
            if (run.Warnings.Count >= 32 || run.Warnings.Contains(message)) return;
            run.Warnings.Add(message);
            if (error != null) LogError("Combat prep cleanup", error);
        }

        private void RelinquishCombatPrepCleanup(CombatPrepRun run, string reason)
        {
            if (run.Retired) return;
            RetireCombatPrepAuthority(run);
            run.Reason = reason;
            CombatPrepCleanupWarning(run, reason);
            _combatPrepEndedRuns.Add(run.Id);
            if (CombatPrepEntryAuthority(run)) SaveProtectedStageProgress(true);
            CacheCombatPrepRuntime();
            SetCustomAutomationStatus(ProtectedStageScheduler.CleanupRecoveryReason);
        }

        private void RetireCombatPrepAuthority(CombatPrepRun run)
        {
            if (run == null || run.Retired) return;
            FreezeCombatPrepTiming(run);
            // Local-only retirement is safe even on a superseded coordinator. No native getters/setters or status publication.
            run.RestoreTargets.Clear();
            run.ExpectedTargets.Clear();
            run.AdventureRouteOwned = false;
            run.AdventureRoute = null;
            run.AdventureRouteObjects = null;
            run.EntryRouteAuthorized = null;
            run.SetupVerified = false;
            run.Setup = null;
            run.CleanupPending = false;
            run.Retired = true;
            run.Status = run.CleanupStatus == "cancelled" ? "cancelled" : "failed";
            run.Reason = "Combat claim authority retired; native state retained.";
            run.NukeTimerOwned = false;
            run.NativeNukeTimer = run.NativeNukeArbitrary = run.NativeNukeBoss = null;
        }

        private bool CombatPrepEntryAuthority(CombatPrepRun run)
        {
            return ReferenceEquals(reference, this) && ReferenceEquals(_combatPrepRun, run)
                && CombatPrepEpochMatches(run.Capture) && _protectedScheduler != null && ProtectedEpochMatches()
                && _protectedPlanFile == run.OwnerPlan && _protectedScheduler.StageIndex == run.OwnerStage
                && _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture) == run.OwnerEntry
                && (AutomaticCombatPrepOwnerMatches(run) || _unloaded && Settings?.CustomPlanFile == run.OwnerPlan);
        }

        private bool CombatPrepReturnAuthority(CombatPrepRun run)
        {
            return ReferenceEquals(reference, this) && ReferenceEquals(_combatPrepRun, run) && !run.Retired
                && CombatPrepEpochMatches(run.Capture) && (!run.Automatic || _protectedScheduler != null && ProtectedEpochMatches()
                    && Settings?.CustomPlanFile == run.OwnerPlan && _protectedPlanFile == run.OwnerPlan
                    && _protectedScheduler.StageIndex == run.OwnerStage
                    && _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture) == run.OwnerEntry);
        }

        private bool CombatPrepHasResourceSuccessor(CombatPrepRun run, string status, string reason)
        {
            if (_unloaded || reason == "Injector unloaded.") return false;
            if (!Settings.UseCustomRebirthPlan) return _globalPresetManualId != null || _customRequestedFocusGoal.HasValue;
            var plan = LoadRuntimeCustomPlan();
            if (plan?.StageSchedulerEnabled != true || _protectedScheduler == null || !_protectedScheduler.Matches(plan)) return false;
            if (!run.Automatic) return !_protectedScheduler.Finished;
            // Resolve scheduling on a detached semantic copy; never enter or credit a successor here.
            var next = new ProtectedStageScheduler();
            if (!next.RestoreState(plan, _protectedScheduler.ExportState())) return false;
            next.AllowRepeat = _protectedScheduler.AllowRepeat;
            next.AutoCleanupRecoveryRequired = false;
            if (_protectedCheckpointDurable && (status != "cancelled" || reason != CombatPrepOwnerPausedReason)
                && (run.YieldedResourceLanes == 0 || Character.rebirthTime.totalseconds >= run.DeadlineRebirthSeconds
                    || _automaticCancelledEntry == AutomaticRecoveryEntry)
                && (!AutomaticContinuationPending || _automaticCancelledEntry == AutomaticRecoveryEntry))
                next.CompleteAutomaticEntry(Character.rebirthTime.totalseconds);
            next.Update(plan, Character.rebirthTime.totalseconds, ProtectedHeartbeat, false,
                index => CustomStageAllocator.AdvancedTrainingStatsReached(plan.Stages[index]));
            return !next.Finished;
        }

        private bool CombatPrepSupportStillOwned(CombatPrepRun run)
        {
            try
            {
                return CombatPrepEpochMatches(run.Capture) && CombatPrepSupportKey() == run.ExpectedSupport.StableKey
                    && SupportObjects().Length == run.ExpectedSupportObjects.Length
                    && !SupportObjects().Where((item, i) => !ReferenceEquals(item, run.ExpectedSupportObjects[i])).Any()
                    && (_presetSupportContinuation == null || _presetSupportAcquisition?.Event != run.Setup?.Owner
                        || SupportMutationStillOwned(_presetSupportContinuation.Mutation));
            }
            catch { return false; }
        }

        private static bool CombatPrepGearMatches(ih[] wanted)
        {
            return LoadoutManager.ExactLoadoutMatches(wanted);
        }

        private bool CombatPrepDeadlineAvailable(CombatPrepRun run) => CombatPrepClock - run.Started < run.Duration
            && (!run.Automatic || Character.rebirthTime.totalseconds < run.DeadlineRebirthSeconds);

        private bool CombatPrepPhaseAuthority(CombatPrepRun run, CombatPrepPhase phase) => ReferenceEquals(reference, this)
            && !_unloaded && ReferenceEquals(_combatPrepRun, run) && !run.Retired && run.Status == "running" && !run.CleanupPending
            && run.PhaseIndex >= 0 && ReferenceEquals(run.Policy.Phases[run.PhaseIndex], phase)
            && CombatPrepEpochMatches(run.Capture) && (!run.Automatic || AutomaticCombatPrepOwnerMatches(run))
            && CombatPrepDeadlineAvailable(run) && Settings.ManageEnergy && Settings.ManageMagic && CombatPrepNativeNukeStillOwned(run)
            && CombatPrepCombatPreemption() == null && CombatPrepTargetsMatch(run);

        private void SelectCombatPrepRequirements(CombatPrepRun run, CombatPrepPhase phase)
        {
            if (!run.Automatic) VerifyCombatPrepGear(run.Inputs, false);
            var gear = run.Inputs.Gear[phase.GearProfileKey];
            run.SetupMaterial = gear.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
            var support = run.Inputs.Support[phase.GearProfileKey];
            var acquisition = run.Id + ":" + run.ReplanCount + ":" + run.PhaseIndex;
            run.SetupVerified = false;
            run.SupportStatus = "pending";
            run.AppliedResourcePhase = -1;
            run.Setup = new SelectedSetupRequirements { Owner = run.Automatic ? "combat-stage:" + run.OwnerPlan + ":" + run.OwnerEntry : "combat:" + run.Id, Acquisition = acquisition,
                Gear = gear, Recall = GearRecallFootprint.Energy | GearRecallFootprint.Magic,
                RetireRoute = true,
                Support = support.Evaluation.Selection, ChangeSupport = true, Budget = support.BudgetFraction,
                EnforceSupportBudget = support.EnforceBudget,
                Authorized = () => CombatPrepPhaseAuthority(run, phase) && run.BlockedReason == null,
                MaterialValid = () => !gear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != run.SetupMaterial[i]).Any(),
                RouteRetirementAuthorized = () => CombatPrepRouteRetirementAuthorized(run) || run.EntryRouteAuthorized?.Invoke() == true,
                ObserveRouteRetirement = () => run.EntryRouteAuthorized = null,
                ObserveProgress = (items, selected) => {
                    if (!CombatPrepPhaseAuthority(run, phase)) return;
                    run.ExpectedGear = items;
                    run.ExpectedMaterial = items.Select(x => LoadoutPresetPrimitiveKey(x.equipment)).ToArray();
                    run.ExpectedSupport = CaptureSupportState();
                    run.ExpectedSupportObjects = SupportObjects();
                } };
        }

        private void ApplyCombatPrepRequirements(CombatPrepRun run, CombatPrepPhase phase)
        {
            if (CombatPrepRequirementsVerified(run, phase)
                && (!_presetSupportPresentationPending || _resourceSetupExecutor.Application.BlockedLanes != 0)) return;
            if (!CombatPrepPhaseAuthority(run, phase) || !CombatPrepGearMatches(run.ExpectedGear) || !CombatPrepSupportStillOwned(run))
                throw new InvalidOperationException("Combat setup authority changed; native setup retained.");
            BeginPresetSupportAcquisition(run.Setup.Owner, run.Setup.Acquisition);
            ApplySelectedSetup(run.Setup);
            run.EntryRouteAuthorized = null;
            run.Setup.RetireRoute = false;
            run.SetupVerified = true;
            run.SupportStatus = "verified";
            run.ObservedNetGps = Character.grossGoldPerSecond() - Character.totalGPSDrain();
            run.ExpectedSupport = CaptureSupportState();
            if (run.Automatic) run.AdventureObservation = ObserveCombatPrepAdventure(run, out _);
            if (run.AdventureRouteOwned && Character.adventure.zone == -1)
            { run.AdventureRouteOwned = false; run.AdventureRoute = null; run.AdventureRouteObjects = null; }
        }

        private bool CombatPrepRequirementsVerified(CombatPrepRun run, CombatPrepPhase phase)
        {
            return run.SetupVerified && CombatPrepPhaseAuthority(run, phase) && run.BlockedReason == null
                && run.Setup != null && run.Setup.MaterialValid() && CombatPrepGearMatches(run.Setup.Gear)
                && !run.ExpectedGear.Where((x, i) => LoadoutPresetPrimitiveKey(x.equipment) != run.ExpectedMaterial[i]).Any()
                && CombatPrepSupportStillOwned(run) && CaptureSupportState().Selection.StableKey == run.Setup.Support.StableKey;
        }

        private void ApplyCombatPrepOperatingSystem(CombatPrepRun run, CombatPrepPhase phase)
        {
            if (!CombatPrepRequirementsVerified(run, phase)) throw new InvalidOperationException("OS prerequisites are unverified.");
            var w = Character.wandoos98;
            if ((int)w.os == run.Policy.OsId)
            {
                if (run.Automatic || run.Policy.Phases.Any(x => x.Kind == "Wandoos" || x.Kind == "Background" || x.BackgroundMagicWandoos))
                    PersistCombatPrepOsPin(run.Capture, run.Policy.OsId);
                if (run.OsOutcome == null) run.OsOutcome = MutationDisposition.AppliedVerified.ToString();
                return;
            }
            if (CombatPrepHasNativeWandoosWork() || !run.Capture.Snapshot.OperatingSystems.Single(x => x.Id == run.Policy.OsId).Unlocked)
                throw new InvalidOperationException("OS switching would erase existing work or use a locked OS.");
            var bootup = w.bootupTime.totalseconds;
            if (run.OsInvoked) throw new InvalidOperationException("OS invocation cannot be replayed; review the previous native outcome.");
            PersistCombatPrepOsPin(run.Capture, run.Policy.OsId);
            if (!CombatPrepRequirementsVerified(run, phase)) throw new InvalidOperationException("OS invocation authority changed after persistence.");
            // The durable pin is invocation authorization, never permission to replay an unknown reset.
            var accounting = ResourceWriter.AllocatedResourceKey();
            var grant = ResolveExecutionGrant("combat:" + run.Id, run.Id,
                ExecutionDomain.Energy | ExecutionDomain.Magic | ExecutionDomain.Gear | ExecutionDomain.Support
                    | ExecutionDomain.Route | ExecutionDomain.NativeSettings | ExecutionDomain.FiniteAction,
                () => CombatPrepRequirementsVerified(run, phase));
            var action = InvokeRouteAction("combat:" + run.Id + ":os:" + run.Policy.OsId, grant,
                RouteActionKind.CombatOperatingSystem, run.Policy.OsId);
            run.OsInvoked = action.Invoked;
            var rendering = action.Error;
            if (!action.Committed || !CombatPrepRequirementsVerified(run, phase) || (int)w.os != run.Policy.OsId
                || CombatPrepHasNativeWandoosWork() || w.bootupTime.totalseconds != bootup
                || ResourceWriter.AllocatedResourceKey() != accounting)
            {
                run.OsOutcome = MutationDisposition.UnknownOutcome.ToString();
                run.BlockedDomain = "OS";
                run.BlockedReason = "OS invocation outcome requires review; automatic reset will not repeat.";
                throw new InvalidOperationException(run.BlockedReason, rendering);
            }
            run.OsOutcome = (rendering == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed).ToString();
            if (rendering != null) CombatPrepCleanupWarning(run, "OS model committed; native presentation failed: " + rendering.Message);
            var presentation = ResolveExecutionGrant("combat:" + run.Id, run.Setup.Acquisition, ExecutionDomain.NativeSettings,
                () => CombatPrepRequirementsVerified(run, phase) && ReferenceEquals(Character.wandoos98, w));
            try
            {
                if (!ValidateExecutionGrant(presentation, ExecutionDomain.NativeSettings)) return;
                Character.wandoos98Controller.refreshMenu();
                if (!ValidateExecutionGrant(presentation, ExecutionDomain.NativeSettings))
                    throw new InvalidOperationException("OS presentation authority changed.");
            }
            catch (Exception error) { CombatPrepCleanupWarning(run, "OS committed; menu refresh failed: " + error.Message); }
        }

        private static double CombatPrepFinite(double value) { return double.IsNaN(value) || value < 0 ? 0 : Math.Min(1e300, value); }

        private static JSONObject CombatPrepSupportJson(SupportContext context, SupportEvaluation support, double fraction, bool phase)
        {
            var result = new JSONObject(); var beards = new JSONArray(); var diggers = new JSONArray();
            var names = PresetDefinitions.BeardNames;
            foreach (var id in support.Selection.Beards)
            {
                var row = new JSONObject(); row["id"] = id; row["name"] = names[id]; beards.Add(row);
            }
            foreach (var level in support.Selection.Diggers)
            {
                var d = context.Diggers.Single(x => x.Id == level.Id);
                var row = new JSONObject(); row["id"] = level.Id; row["name"] = d.Name;
                row["level"] = level.Level.ToString(CultureInfo.InvariantCulture); row["drain"] = CombatPrepFinite(d.Drain(level.Level)); diggers.Add(row);
            }
            result["beards"] = beards; result["diggers"] = diggers;
            result["grossGoldPerSecond"] = CombatPrepFinite(support.GrossGold); result["drainPerSecond"] = CombatPrepFinite(support.Drain);
            result["netGoldPerSecond"] = SupportProjection.Finite(support.NetGold) ? support.NetGold : 0d; result["budgetFraction"] = fraction;
            result["policy"] = phase ? "phase" : "current";
            result["wandoosBeardMultiplier"] = CombatPrepFinite(support.BeardW); result["wandoosDiggerMultiplier"] = CombatPrepFinite(support.DiggerW);
            return result;
        }

        private static JSONObject CombatPrepTotals(CombatPrepAlternative policy)
        {
            var result = new JSONObject();
            result["augmentMultiplier"] = CombatPrepFinite(policy?.EndAugmentTotal ?? 1);
            result["wandoosMultiplier"] = CombatPrepFinite(policy?.EndWandoosBonus ?? 1);
            result["combinedMultiplier"] = CombatPrepFinite(policy?.TotalBonus ?? 1);
            result["bossAttack"] = CombatPrepFinite(policy?.BossAttack ?? 0);
            result["bossDefense"] = CombatPrepFinite(policy?.BossDefense ?? 0);
            return result;
        }

        private static void SettleCombatPrepActivity(CombatPrepRun run, double now)
        {
            if (run.ActivityIndex < 0) return;
            run.ActivitySeconds[run.ActivityIndex] += Math.Max(0, now - run.ActivityStarted);
            run.ActivityIndex = -1;
        }

        private static void FreezeCombatPrepTiming(CombatPrepRun run)
        {
            if (run.EndedAt.HasValue) return;
            run.EndedAt = CombatPrepClock;
            SettleCombatPrepActivity(run, run.EndedAt.Value);
        }

        private static void SnapshotCombatPrepTiming(JSONObject state, CombatPrepRun run, double now)
        {
            now = run?.EndedAt ?? now;
            var phase = run == null || run.PhaseIndex < 0 || run.PhaseIndex >= run.Policy.Phases.Length ? null : run.Policy.Phases[run.PhaseIndex];
            var elapsed = run == null ? 0 : Math.Min(run.Duration, Math.Max(0, now - run.Started));
            state["durationSeconds"] = run?.Duration ?? 0; state["elapsedSeconds"] = elapsed;
            state["remainingSeconds"] = run == null ? 0 : Math.Max(0, run.Duration - elapsed);
            state["phaseElapsedSeconds"] = phase == null ? 0 : Math.Max(0, now - run.PhaseStarted);
            state["phaseRemainingSeconds"] = phase == null ? 0 : Math.Max(0, run.PhaseDeadline - now);
            state["activityTimingScope"] = "run";
            state["activityTimingSemantics"] = "exclusive-phase-wall-time-including-setup";
            var times = new JSONArray();
            for (var i = 0; i < CombatPrepActivityKinds.Length; i++)
            {
                var active = run != null && !run.EndedAt.HasValue && run.ActivityIndex == i;
                var row = new JSONObject(); row["kind"] = CombatPrepActivityKinds[i];
                // Reading an open interval must not settle it or depend on a replaced policy.
                row["elapsedSeconds"] = (run?.ActivitySeconds[i] ?? 0) + (active ? Math.Max(0, now - run.ActivityStarted) : 0);
                row["active"] = active; times.Add(row);
            }
            state["activityTimes"] = times;
        }

        private void CacheCombatPrepRuntime()
        {
            var run = _combatPrepRun;
            var recovery = CombatPrepCleanupRecoveryRequired;
            var state = new JSONObject(); state["schemaVersion"] = 1; state["active"] = CombatPrepRunActive;
            state["status"] = run?.Status ?? "idle"; state["runId"] = run?.Id ?? ""; state["candidateLabel"] = run?.Label ?? "";
            state["cleanupPending"] = run?.CleanupPending ?? false;
            state["cleanupRecoveryRequired"] = recovery;
            state["cleanupRecoveryReason"] = recovery ? ProtectedStageScheduler.CleanupRecoveryReason : "";
            state["cleanupAttemptCount"] = run?.CleanupAttemptCount ?? 0;
            state["owner"] = run == null ? "none" : run.Automatic ? "scheduled" : "manual";
            state["planName"] = run?.OwnerPlan ?? ""; state["stageIndex"] = run?.OwnerStage ?? -1;
            state["stageEntry"] = run?.OwnerEntry ?? ""; state["replanReason"] = run?.ReplanReason ?? "";
            state["replanCount"] = run?.ReplanCount ?? 0;
            state["equipmentReplanPending"] = run?.EquipmentReplanPending ?? false;
            state["adventureDynamicWeight"] = run?.Inputs?.AdventureDynamicWeight ?? 0;
            state["configuredAdventureWeight"] = run?.AdventureConfiguredWeight ?? 0;
            state["adventureTask"] = run?.Inputs?.AdventureTask ?? "";
            state["adventureTaskKey"] = run?.Inputs?.AdventureTaskKey ?? "";
            state["adventureStatus"] = run?.AdventureStatus ?? "disabled";
            state["adventureReplanPending"] = run?.AdventureReplanPending ?? false;
            state["energyPool"] = (run?.EnergyPool ?? 0).ToString(CultureInfo.InvariantCulture);
            state["magicPool"] = (run?.MagicPool ?? 0).ToString(CultureInfo.InvariantCulture);
            state["deadlineRebirthSeconds"] = run?.DeadlineRebirthSeconds ?? 0;
            if (recovery)
            {
                state["status"] = "failed"; state["owner"] = "scheduled"; state["planName"] = _protectedPlanFile;
                state["stageIndex"] = _protectedScheduler.StageIndex;
                state["stageEntry"] = _protectedGeneration + ":" + _protectedScheduler.Entry.ToString(CultureInfo.InvariantCulture);
                state["deadlineRebirthSeconds"] = _protectedScheduler.ExpectedEndSeconds;
            }
            var phase = run == null || run.PhaseIndex < 0 || run.PhaseIndex >= run.Policy.Phases.Length ? null : run.Policy.Phases[run.PhaseIndex];
            state["phase"] = phase?.Kind ?? ""; state["reason"] = run?.Reason ?? "";
            state["phaseIndex"] = run?.PhaseIndex ?? -1;
            state["policyOs"] = run?.Policy.OsId ?? -1;
            var policyOffset = run?.Automatic == true ? Math.Max(0, run.PolicyStarted - run.Started) : 0;
            state["policyStartedElapsedSeconds"] = policyOffset;
            state["phaseStartedElapsedSeconds"] = phase == null ? 0 : Math.Max(0, run.PhaseStarted - run.Started);
            state["phaseDeadlineElapsedSeconds"] = phase == null ? 0 : Math.Max(0, run.PhaseDeadline - run.Started);
            var phases = new JSONArray();
            foreach (var scheduled in run?.Policy.Phases.Take(3) ?? Enumerable.Empty<CombatPrepPhase>())
            {
                var row = new JSONObject(); row["index"] = phases.Count; row["kind"] = scheduled.Kind;
                row["gearProfileKey"] = scheduled.GearProfileKey; row["durationSeconds"] = scheduled.DurationSeconds;
                row["scheduledStartElapsedSeconds"] = policyOffset; policyOffset += scheduled.DurationSeconds;
                row["scheduledEndElapsedSeconds"] = policyOffset;
                row["augmentTierId"] = scheduled.AugmentTierId;
                row["augmentTarget"] = scheduled.AugmentTarget.ToString(CultureInfo.InvariantCulture);
                row["upgradeTarget"] = scheduled.UpgradeTarget.ToString(CultureInfo.InvariantCulture);
                row["atEnergyTarget"] = scheduled.AtEnergyTarget.ToString(CultureInfo.InvariantCulture);
                row["atMagicTarget"] = scheduled.AtMagicTarget.ToString(CultureInfo.InvariantCulture);
                row["atEnergyWeight"] = scheduled.AtEnergyWeight;
                row["backgroundMagicWandoos"] = scheduled.BackgroundMagicWandoos;
                row["energyResourceFraction"] = scheduled.EnergyResourceFraction; row["magicResourceFraction"] = scheduled.MagicResourceFraction;
                phases.Add(row);
            }
            state["phases"] = phases;
            SnapshotCombatPrepTiming(state, run, CombatPrepClock);
            var atGate = run?.Policy.Phases.FirstOrDefault(x => x.Kind == "AT");
            state["atGateRequired"] = atGate != null;
            state["atTargetsReached"] = run?.Capture != null && CombatPrepEpochMatches(run.Capture)
                && (atGate == null || CombatPrepTargetsReached(atGate));
            state["projected"] = CombatPrepTotals(run?.Policy);
            state["supportStatus"] = run?.SupportStatus ?? "idle";
            if (run?.ResourceOutcome != null)
            {
                state["resourceOutcome"] = run.ResourceOutcome.Disposition.ToString();
                state["resourcePresentationPending"] = _resourceSetupExecutor.Application.PresentationPending;
                state["yieldedResourceLanes"] = run.YieldedResourceLanes;
            }
            var routeIssue = Character?.stats != null && SelectedSetupConflicts.TryGetValue(Character, out var conflict)
                && conflict.Rebirth == Character.stats.rebirthNumber ? conflict.Route : null;
            state["blockedDomain"] = run?.BlockedDomain ?? (routeIssue != null ? "route" : "");
            state["blockedReason"] = run?.BlockedReason ?? routeIssue ?? "";
            state["osOutcome"] = run?.OsOutcome ?? "";
            state["plannedSupport"] = JSONNull.CreateOrGet();
            if (phase != null)
            {
                var support = run.Inputs.Support[phase.GearProfileKey];
                state["plannedSupport"] = CombatPrepSupportJson(run.Inputs.SupportContext, support.Evaluation, support.BudgetFraction, support.EnforceBudget);
            }
            CacheCombatPrepActual(state);
            _combatPrepRuntime = state;
        }

        private void CacheCombatPrepActual(JSONObject state)
        {
            var run = _combatPrepRun;
            var sameEpoch = run?.Capture != null && CombatPrepEpochMatches(run.Capture);
            var phase = !sameEpoch || run.PhaseIndex < 0 || run.PhaseIndex >= run.Policy.Phases.Length ? null : run.Policy.Phases[run.PhaseIndex];
            var actual = CombatPrepTotals(null);
            actual["available"] = false;
            foreach (var key in new[] { "energyAtLevel", "magicAtLevel", "wandoosEnergyLevel", "wandoosMagicLevel",
                "energyAtAllocation", "magicAtAllocation", "wandoosEnergyAllocation", "wandoosMagicAllocation",
                "augmentLevel", "upgradeLevel", "augmentEnergyAllocation", "upgradeEnergyAllocation" }) actual[key] = "0";
            actual["energyWandoosLevelsPerSecond"] = 0d; actual["magicWandoosLevelsPerSecond"] = 0d;
            foreach (var key in new[] { "augmentProgress", "upgradeProgress", "energyAtProgress", "magicAtProgress", "wandoosEnergyProgress", "wandoosMagicProgress" }) actual[key] = 0d;
            actual["augmentIndex"] = -1; actual["gold"] = 0d;
            var warnings = run == null ? new List<string>() : new List<string>(run.Warnings);
            state["actualSupport"] = JSONNull.CreateOrGet();
            try
            {
                var context = CaptureSupportContext();
                var observed = ObserveCombatPrepSupport(context);
                state["actualSupport"] = CombatPrepSupportJson(context, observed, 1, false);
            }
            catch (Exception error) { warnings.Add("Live support unavailable: " + error.Message); }
            if (CombatPrepRunActive && run.NukeTimerOwned)
                warnings.Add("Native automatic nuke timer paused at zero during combat prep; autoNukeOn preference is unchanged and manual fights/nukes still preempt. The global deadline is unchanged.");
            var c = Character;
            if (c?.advancedTraining?.level != null && c.advancedTraining.level.Length > 4
                && c.advancedTraining.energy != null && c.advancedTraining.energy.Length > 4 && c.wandoos98 != null)
            {
                actual["available"] = true;
                actual["energyAtLevel"] = c.advancedTraining.level[3].ToString(CultureInfo.InvariantCulture);
                actual["magicAtLevel"] = c.advancedTraining.level[4].ToString(CultureInfo.InvariantCulture);
                actual["energyAtAllocation"] = c.advancedTraining.energy[3].ToString(CultureInfo.InvariantCulture);
                actual["magicAtAllocation"] = c.advancedTraining.energy[4].ToString(CultureInfo.InvariantCulture);
                if (c.advancedTraining.barProgress != null && c.advancedTraining.barProgress.Length > 4)
                {
                    actual["energyAtProgress"] = CombatPrepFinite(c.advancedTraining.barProgress[3]);
                    actual["magicAtProgress"] = CombatPrepFinite(c.advancedTraining.barProgress[4]);
                }
                actual["wandoosEnergyLevel"] = c.wandoos98.energyLevel.ToString(CultureInfo.InvariantCulture);
                actual["wandoosMagicLevel"] = c.wandoos98.magicLevel.ToString(CultureInfo.InvariantCulture);
                actual["wandoosEnergyAllocation"] = c.wandoos98.wandoosEnergy.ToString(CultureInfo.InvariantCulture);
                actual["wandoosMagicAllocation"] = c.wandoos98.wandoosMagic.ToString(CultureInfo.InvariantCulture);
                actual["wandoosEnergyProgress"] = CombatPrepFinite(c.wandoos98.energyProgress);
                actual["wandoosMagicProgress"] = CombatPrepFinite(c.wandoos98.magicProgress);
                actual["gold"] = CombatPrepFinite(c.realGold);
                if (phase?.Kind == "Augments" && c.augments?.augs != null && phase.AugmentTierId >= 0 && phase.AugmentTierId < c.augments.augs.Length)
                {
                    var aug = c.augments.augs[phase.AugmentTierId];
                    actual["augmentIndex"] = phase.AugmentTierId;
                    actual["augmentLevel"] = aug.augLevel.ToString(CultureInfo.InvariantCulture);
                    actual["upgradeLevel"] = aug.upgradeLevel.ToString(CultureInfo.InvariantCulture);
                    actual["augmentProgress"] = CombatPrepFinite(aug.augProgress); actual["upgradeProgress"] = CombatPrepFinite(aug.upgradeProgress);
                    actual["augmentEnergyAllocation"] = aug.augEnergy.ToString(CultureInfo.InvariantCulture);
                    actual["upgradeEnergyAllocation"] = aug.upgradeEnergy.ToString(CultureInfo.InvariantCulture);
                }
                try
                {
                    if (!c.wandoos98.disabled && c.wandoos98Controller != null)
                    {
                        if (c.wandoos98.wandoosEnergy > 0) actual["energyWandoosLevelsPerSecond"] = CombatPrepActualDumpRate(c.wandoos98.wandoosEnergy, c.wandoos98Controller.energyProgressToAdd(c.wandoos98.wandoosEnergy));
                        if (c.wandoos98.wandoosMagic > 0) actual["magicWandoosLevelsPerSecond"] = CombatPrepActualDumpRate(c.wandoos98.wandoosMagic, c.wandoos98Controller.magicProgressToAdd(c.wandoos98.wandoosMagic));
                    }
                }
                catch (Exception error) { warnings.Add("Live Wandoos rates unavailable: " + error.Message); }
                try
                {
                    var a = c.augmentsController.totalBonus(); var w = c.wandoos98Controller.wandoosBonus();
                    actual["augmentMultiplier"] = CombatPrepFinite(a); actual["wandoosMultiplier"] = CombatPrepFinite(w); actual["combinedMultiplier"] = CombatPrepFinite(a * w);
                    var boss = sameEpoch ? CombatPrepBossStats(run.Capture.OriginalGear, raw: false, reportSupport: run.Capture.OriginalSupportState.Selection) : Tuple.Create(c.totalAttack(), c.totalDefense());
                    actual["bossAttack"] = CombatPrepFinite(boss.Item1); actual["bossDefense"] = CombatPrepFinite(boss.Item2);
                    if (sameEpoch) warnings.Add("Actual boss stats use original fixed gear AND original support with fresh native potential effects; forecast factors remain frozen.");
                }
                catch (Exception error) { warnings.Add("Live telemetry unavailable: " + error.Message); }
            }
            state["actual"] = actual; state["warnings"] = ToJsonArray(warnings.ToArray());
            _combatPrepLastActualRefresh = CombatPrepClock;
        }

        private static double CombatPrepActualDumpRate(long amount, double progressPerTick)
        {
            return amount <= 0 || double.IsNaN(progressPerTick) || progressPerTick <= 1e-9
                ? 0 : 50 / Math.Max(1, Math.Ceiling(1 / progressPerTick));
        }

        private JSONObject GetCombatPrepRuntime()
        {
            if (_combatPrepRuntime == null || _combatPrepRuntime["cleanupRecoveryRequired"].AsBool != CombatPrepCleanupRecoveryRequired) CacheCombatPrepRuntime();
            else if (!CombatPrepActive && CombatPrepClock - _combatPrepLastActualRefresh >= CombatPrepPlanner.AllocationRefreshSeconds)
                CacheCombatPrepActual(_combatPrepRuntime);
            if (_combatPrepRun == null || _combatPrepRun.EndedAt.HasValue) return _combatPrepRuntime;
            var snapshot = new JSONObject();
            foreach (var pair in _combatPrepRuntime.Linq) snapshot[pair.Key] = pair.Value;
            SnapshotCombatPrepTiming(snapshot, _combatPrepRun, CombatPrepClock);
            return snapshot;
        }
    }
}
