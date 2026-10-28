using System;
using System.Globalization;
using System.Linq;
using System.Text;
using NGUInjector.CustomPlans;
using SimpleJSON;

namespace NGUInjector.Managers
{
    // Pure, positional runtime. The coordinator owns allocation, epoch validation and persistence.
    internal sealed class ProtectedStageScheduler
    {
        private const double Tolerance = 15d;
        private string _structureKey;
        private string _timingKey;
        private int[] _kinds = new int[0]; // 0 flex, 1 protected fixed, 2 mandatory AT, 3 automatic elapsed budget
        private double[] _durations = new double[0];
        private double[] _earned = new double[0];
        private bool[] _completed = new bool[0];
        private double[] _applied = new double[0];
        private double[] _elapsedByStage = new double[0];
        private int[] _visits = new int[0];
        private bool[] _skipped = new bool[0];
        private double _lastElapsed;
        private double _lastHeartbeat;
        private bool _armed;

        internal int StageIndex { get; private set; } = -1;
        internal long Entry { get; private set; }
        internal int Cycle { get; private set; }
        internal double EnteredAt { get; private set; }
        internal double StageElapsedSeconds { get; private set; }
        internal double ExpectedEndSeconds { get; private set; }
        internal bool ProtectedWorkComplete { get; private set; }
        internal bool Finished { get; private set; }
        internal bool AllowRepeat { get; set; } = true;
        internal bool AutoAugmentsCompleted { get; set; }
        internal bool AutoAtCompleted { get; set; }
        internal bool AutoWandoosStarted { get; set; }
        internal long? AutoEnergyPool { get; set; }
        internal long? AutoMagicPool { get; set; }
        internal bool AutoCleanupRecoveryRequired { get; set; }
        internal const string CleanupRecoveryReason = "Automatic recovery is retiring historical restoration ownership before reconciling current intent. The original entry and deadline remain unchanged.";
        internal double[] Durations { get { return (double[]) _durations.Clone(); } }
        internal double[] EarnedSeconds { get { return (double[]) _earned.Clone(); } }
        internal bool[] Completed { get { return (bool[]) _completed.Clone(); } }
        internal double[] AppliedSeconds { get { return (double[]) _applied.Clone(); } }
        internal double[] ElapsedSecondsByStage { get { return (double[]) _elapsedByStage.Clone(); } }
        internal int[] Visits { get { return (int[]) _visits.Clone(); } }
        internal bool[] Skipped { get { return (bool[]) _skipped.Clone(); } }
        internal string LastOverride { get; private set; } = string.Empty;
        internal double LastOverrideAt { get; private set; }
        internal string Reason { get; private set; } = "Not initialized";

        internal bool Matches(CustomPlan plan)
        {
            return _structureKey != null && _structureKey == StructureKey(plan);
        }

        internal static string StructureKey(CustomPlan plan)
        {
            if (plan == null || plan.Stages == null || plan.Stages.Length == 0) return null;
            var name = plan.Name ?? string.Empty;
            var key = new StringBuilder();
            key.Append(name.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(name)
                .Append('|').Append(plan.StageLoopStartIndex.ToString(CultureInfo.InvariantCulture));
            foreach (var stage in plan.Stages)
            {
                if (stage == null) return null;
                var kind = Kind(stage);
                key.Append('|').Append(kind);
                if (kind == 2)
                    key.Append(':').Append(stage.TargetAdvancedTrainingPower.ToString(CultureInfo.InvariantCulture))
                        .Append(':').Append(stage.TargetAdvancedTrainingToughness.ToString(CultureInfo.InvariantCulture));
            }
            // Same-kind rows have positional identity, even if their cosmetic allocations are reordered.
            return key.ToString();
        }

        internal void Update(CustomPlan plan, double elapsed, double heartbeatSeconds, bool canCredit, Func<int, bool> atReached)
        {
            ValidatePlan(plan);
            if (!Finite(elapsed) || elapsed < 0 || !Finite(heartbeatSeconds) || heartbeatSeconds < 0)
                throw new ArgumentOutOfRangeException("elapsed", "Scheduler clocks must be finite and nonnegative.");
            if (AutoCleanupRecoveryRequired)
            {
                if (!Matches(plan)) throw new InvalidOperationException(CleanupRecoveryReason);
                Accrue(elapsed, heartbeatSeconds, false);
                StageElapsedSeconds = Math.Max(0, elapsed - EnteredAt);
                Reason = CleanupRecoveryReason;
                return;
            }

            if (!Matches(plan))
            {
                var reset = _structureKey != null;
                _structureKey = StructureKey(plan);
                _timingKey = TimingKey(plan);
                _kinds = new int[plan.Stages.Length];
                _durations = new double[_kinds.Length];
                _earned = new double[_kinds.Length];
                _completed = new bool[_kinds.Length];
                _applied = new double[_kinds.Length];
                _elapsedByStage = new double[_kinds.Length];
                _visits = new int[_kinds.Length];
                _skipped = new bool[_kinds.Length];
                LastOverride = string.Empty;
                LastOverrideAt = 0;
                for (var i = 0; i < _kinds.Length; i++) _kinds[i] = Kind(plan.Stages[i]);
                StageIndex = 0;
                Cycle = 0;
                Finished = false;
                AutoAugmentsCompleted = AutoAtCompleted = AutoWandoosStarted = false;
                AutoEnergyPool = AutoMagicPool = null;
                EnteredAt = elapsed;
                StageElapsedSeconds = 0;
                _armed = false;
                _lastElapsed = elapsed;
                _lastHeartbeat = heartbeatSeconds;
                Allocate(plan, elapsed, false);
                var initial = SkipUnfundedFlex(plan, 0, elapsed);
                Finished = initial == _kinds.Length;
                StageIndex = Finished ? -1 : initial;
                ExpectedEndSeconds = elapsed;
                if (!Finished)
                {
                    Entry++;
                    _visits[initial]++;
                    ExpectedEndSeconds = _kinds[initial] == 1 ? _durations[initial] : elapsed + _durations[initial];
                }
                RefreshGate();
                Reason = Finished ? "Finished; no funded allocation selected"
                    : (reset ? "Structure changed; " : string.Empty) + "Stage " + initial + " pending application";
                return; // Never infer historical work, including an already-satisfied first AT gate.
            }

            if (StageIndex < 0) return; // A terminal all-flex pass may never have selected an allocation.

            var creditedSeconds = Accrue(elapsed, heartbeatSeconds, canCredit);

            var timingKey = TimingKey(plan);
            if (_timingKey != timingKey)
            {
                var oldDuration = _durations[StageIndex];
                Allocate(plan, elapsed, true);
                if (!Finished && _kinds[StageIndex] == 1)
                    ExpectedEndSeconds += _durations[StageIndex] - oldDuration;
                _timingKey = timingKey;
            }

            // At most one new cycle per update; zero-time AT-only loops cannot spin.
            while (!Finished)
            {
                var kind = _kinds[StageIndex];
                var done = kind == 2 ? atReached != null && atReached(StageIndex)
                    : kind == 1 ? _earned[StageIndex] >= _durations[StageIndex]
                        || (_armed && creditedSeconds > 0 && _durations[StageIndex] > Tolerance
                            && _durations[StageIndex] - _earned[StageIndex] <= Tolerance
                            && elapsed >= ExpectedEndSeconds)
                    : elapsed >= ExpectedEndSeconds;
                if (!done) break;
                _completed[StageIndex] = true;
                _armed = false;
                var next = SkipUnfundedFlex(plan, StageIndex + 1, elapsed);
                if (next == _kinds.Length)
                {
                    if (!AllowRepeat || elapsed >= Horizon(plan) || plan.StageLoopStartIndex < 0)
                    {
                        Finished = true;
                        break;
                    }
                    Cycle++;
                    for (var i = plan.StageLoopStartIndex; i < _kinds.Length; i++)
                    {
                        _earned[i] = 0;
                        _completed[i] = false;
                        _skipped[i] = false;
                    }
                    next = SkipUnfundedFlex(plan, plan.StageLoopStartIndex, elapsed);
                    if (next == _kinds.Length)
                    {
                        Finished = true;
                        break;
                    }
                    Select(plan, next, elapsed, elapsed);
                    break;
                }
                else
                {
                    var boundary = kind == 2 || kind == 3 || elapsed - ExpectedEndSeconds > Tolerance
                        ? elapsed : ExpectedEndSeconds;
                    Select(plan, next, elapsed, boundary);
                }
            }
            StageElapsedSeconds = _kinds[StageIndex] == 1 ? _earned[StageIndex] : Math.Max(0, elapsed - EnteredAt);
            RefreshGate();
            Reason = Finished ? "Finished; holding last allocation"
                : _kinds[StageIndex] == 2 ? "Waiting for mandatory AT targets"
                : _kinds[StageIndex] == 1 ? "Waiting for protected applied time"
                : _kinds[StageIndex] == 3 ? "Automatic Augments + Wandoos elapsed budget (setup included)" : "Flexible wall-clock allocation";
        }

        internal void MarkApplied(double elapsed, double heartbeatSeconds)
        {
            if (StageIndex < 0 || AutoCleanupRecoveryRequired) return;
            if (!Finite(elapsed) || elapsed < _lastElapsed || !Finite(heartbeatSeconds) || heartbeatSeconds < _lastHeartbeat)
            {
                Suspend();
                return;
            }
            Accrue(elapsed, heartbeatSeconds, false);
            _armed = true;
        }

        // Accrue the selected allocation, never progress it or evaluate an AT target.
        private double Accrue(double elapsed, double heartbeatSeconds, bool canCredit)
        {
            var rbDelta = elapsed - _lastElapsed;
            var heartbeatDelta = heartbeatSeconds - _lastHeartbeat;
            var continuous = canCredit && rbDelta >= 0 && rbDelta <= Tolerance
                             && heartbeatDelta >= 0 && heartbeatDelta <= Tolerance;
            var applied = _armed && continuous ? Math.Min(rbDelta, heartbeatDelta) : 0;
            if (StageIndex >= 0)
            {
                if (rbDelta >= 0)
                    _elapsedByStage[StageIndex] = Math.Min(double.MaxValue, _elapsedByStage[StageIndex] + rbDelta);
                if (applied > 0)
                {
                    _applied[StageIndex] = Math.Min(double.MaxValue, _applied[StageIndex] + applied);
                    if (!Finished && _kinds[StageIndex] == 1)
                        _earned[StageIndex] = Math.Min(_durations[StageIndex], _earned[StageIndex] + applied);
                }
            }
            if (!continuous) _armed = false;
            _lastElapsed = elapsed;
            _lastHeartbeat = heartbeatSeconds;
            return applied;
        }

        internal void OverrideStage(CustomPlan plan, string action, int stageIndex, double elapsed, double heartbeatSeconds)
        {
            ValidatePlan(plan);
            if (action != "skip" && action != "set" && action != "restart")
                throw new ArgumentException("Stage action must be skip, set or restart.", "action");
            if (!Matches(plan))
                throw new InvalidOperationException("Stage overrides require an initialized matching plan.");
            if (stageIndex < 0 || stageIndex >= _kinds.Length
                || (action != "set" && stageIndex != StageIndex))
                throw new ArgumentOutOfRangeException("stageIndex", "Skip and restart must target the selected stage.");
            if (!Finite(elapsed) || elapsed < _lastElapsed || !Finite(heartbeatSeconds) || heartbeatSeconds < _lastHeartbeat)
                throw new ArgumentOutOfRangeException("elapsed", "Override clocks must be finite and nondecreasing.");

            Accrue(elapsed, heartbeatSeconds, true);
            _armed = false;
            AutoCleanupRecoveryRequired = false;
            LastOverrideAt = elapsed;
            LastOverride = action + " stage " + (stageIndex + 1).ToString(CultureInfo.InvariantCulture)
                + " at " + elapsed.ToString("R", CultureInfo.InvariantCulture);
            if (action == "skip")
            {
                _completed[stageIndex] = _skipped[stageIndex] = true;
                var next = Finished ? _kinds.Length : SkipUnfundedFlex(plan, stageIndex + 1, elapsed);
                Finished = next == _kinds.Length;
                if (Finished)
                {
                    Entry++; // A terminal override changes identity without inventing another visit.
                    Allocate(plan, elapsed, true);
                    StageElapsedSeconds = _kinds[stageIndex] == 1 ? _earned[stageIndex] : Math.Max(0, elapsed - EnteredAt);
                }
                else Select(plan, next, elapsed, elapsed);
            }
            else
            {
                for (var i = 0; i < stageIndex; i++)
                    if (!_completed[i]) _completed[i] = _skipped[i] = true;
                for (var i = stageIndex; i < _kinds.Length; i++)
                {
                    _earned[i] = 0;
                    _completed[i] = _skipped[i] = false;
                }
                Finished = false;
                Select(plan, stageIndex, elapsed, elapsed);
            }
            _timingKey = TimingKey(plan);
            RefreshGate();
            Reason = "Manual " + LastOverride + (Finished ? "; finished; holding last allocation" : "; pending application");
        }

        internal void Suspend()
        {
            _armed = false;
        }

        internal void CompleteAutomaticEntry(double elapsed)
        {
            if (StageIndex >= 0 && _kinds[StageIndex] == 3 && !Finished && !AutoCleanupRecoveryRequired)
                ExpectedEndSeconds = Math.Min(ExpectedEndSeconds, elapsed);
        }

        private void Select(CustomPlan plan, int index, double elapsed, double boundary)
        {
            StageIndex = index;
            Entry++;
            _visits[index] = (int) Math.Min(int.MaxValue, (long) _visits[index] + 1);
            EnteredAt = elapsed;
            AutoAugmentsCompleted = AutoAtCompleted = AutoWandoosStarted = false;
            AutoEnergyPool = AutoMagicPool = null;
            StageElapsedSeconds = 0;
            _armed = false;
            Allocate(plan, elapsed, false);
            ExpectedEndSeconds = _kinds[index] == 1 ? boundary + _durations[index] : elapsed + _durations[index];
        }

        private void Allocate(CustomPlan plan, double elapsed, bool edit)
        {
            var active = StageIndex;
            var fraction = 1d;
            var served = Math.Max(0, elapsed - EnteredAt);
            if (edit && _kinds[active] == 0)
                fraction = _durations[active] <= 0 ? 0 : Math.Max(0, ExpectedEndSeconds - elapsed) / _durations[active];
            var reserve = 0d;
            var weight = 0d;
            for (var i = 0; i < _kinds.Length; i++)
            {
                if (_kinds[i] == 3 && (plan.Stages[i].DurationSeconds > 0 || edit && i == active))
                {
                    // Once entered, automatic deadlines and budgets are immutable, even on edits.
                    if (!(i == active && edit)) _durations[i] = plan.Stages[i].DurationSeconds;
                    if (!_completed[i]) reserve += i == active
                        ? Math.Max(0, _durations[i] - served) : _durations[i];
                }
                if (_kinds[i] == 1)
                {
                    _durations[i] = plan.Stages[i].DurationSeconds;
                    if (!_completed[i]) reserve += Math.Max(0, _durations[i] - _earned[i]);
                }
                if (i >= active && !_completed[i] && (_kinds[i] == 0 || _kinds[i] == 3 && plan.Stages[i].DurationSeconds == 0 && !(edit && i == active)))
                    weight += plan.Stages[i].Percent * (i == active ? fraction : 1);
            }
            var pool = Math.Max(0, Horizon(plan) - elapsed - reserve);
            for (var i = active; i < _kinds.Length; i++)
            {
                if ((_kinds[i] != 0 && !(_kinds[i] == 3 && plan.Stages[i].DurationSeconds == 0 && !(edit && i == active))) || _completed[i]) continue;
                var remainingWeight = plan.Stages[i].Percent * (i == active ? fraction : 1);
                var weightedPool = pool * remainingWeight;
                // Preserve integral boundaries unless the product overflows or underflows to zero.
                var allocation = weight <= 0 ? 0 : !Finite(weightedPool) || (weightedPool == 0 && pool > 0 && remainingWeight > 0)
                    ? pool * (remainingWeight / weight) : weightedPool / weight;
                _durations[i] = allocation + (edit && i == active ? served : 0);
                if (edit && i == active && !Finished) ExpectedEndSeconds = elapsed + allocation;
            }
        }

        private void RefreshGate()
        {
            ProtectedWorkComplete = _structureKey != null;
            for (var i = 0; i < _kinds.Length; i++)
                if (_kinds[i] != 0 && !_completed[i]) ProtectedWorkComplete = false;
        }

        private int SkipUnfundedFlex(CustomPlan plan, int index, double elapsed)
        {
            var reserve = 0d;
            for (var i = 0; i < _kinds.Length; i++)
                if (_kinds[i] == 1 && !_completed[i]) reserve += Math.Max(0, plan.Stages[i].DurationSeconds - _earned[i]);
                else if (_kinds[i] == 3 && !_completed[i] && plan.Stages[i].DurationSeconds > 0)
                    reserve += i == StageIndex ? Math.Max(0, ExpectedEndSeconds - elapsed) : plan.Stages[i].DurationSeconds;
            if (Horizon(plan) - elapsed - reserve > 0) return index;
            while (index < _kinds.Length && (_kinds[index] == 0 || _kinds[index] == 3 && plan.Stages[index].DurationSeconds == 0))
            {
                _completed[index] = true;
                _durations[index++] = 0;
            }
            return index;
        }

        private static int Kind(CustomPlanStage stage)
        {
            if (stage.ExecutionMode == (int) CustomStageExecutionMode.AutoAugmentsWandoos) return 3;
            return stage.CompletionMode == 1 ? 2 : stage.DurationSeconds > 0 ? 1 : 0;
        }

        private static double Horizon(CustomPlan plan)
        {
            return plan.TimeTriggerEnabled && plan.TimeTriggerSeconds > 0 ? plan.TimeTriggerSeconds : plan.StageDurationSeconds;
        }

        private static string TimingKey(CustomPlan plan)
        {
            var key = new StringBuilder(Horizon(plan).ToString("R", CultureInfo.InvariantCulture));
            foreach (var stage in plan.Stages)
                key.Append('|').Append(Kind(stage) == 3 ? stage.DurationSeconds.ToString(CultureInfo.InvariantCulture) + ":" + stage.Percent.ToString("R", CultureInfo.InvariantCulture)
                    : Kind(stage) == 1 ? stage.DurationSeconds.ToString(CultureInfo.InvariantCulture)
                    : Kind(stage) == 0 ? stage.Percent.ToString("R", CultureInfo.InvariantCulture) : "AT");
            return key.ToString();
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void ValidatePlan(CustomPlan plan)
        {
            if (StructureKey(plan) == null || Horizon(plan) < 0 || plan.StageLoopStartIndex < -1
                || plan.StageLoopStartIndex >= plan.Stages.Length)
                throw new ArgumentException("A nonempty plan with a valid horizon and loop boundary is required.", "plan");
            try { plan.ValidateFightSchedules(); }
            catch (FormatException error) { throw new ArgumentException(error.Message, "plan", error); }
            var weights = 0d;
            foreach (var stage in plan.Stages)
            {
                if (!Finite(stage.AdventureDynamicWeight) || stage.AdventureDynamicWeight < 0 || stage.AdventureDynamicWeight > 1e6)
                    throw new ArgumentException("Stage AdventureDynamicWeight must be a finite number between 0 and 1000000.", "plan");
                if (stage.ExecutionMode < 0 || stage.ExecutionMode > 1 || stage.ExecutionMode == 1 && stage.CompletionMode != 0
                    || stage.CompletionMode < 0 || stage.CompletionMode > 1 || stage.DurationSeconds < 0
                    || ((Kind(stage) == 0 || Kind(stage) == 3 && stage.DurationSeconds == 0) && (!Finite(stage.Percent) || stage.Percent <= 0)))
                    throw new ArgumentException("Stage kinds, durations and flexible weights must be valid.", "plan");
                if (Kind(stage) == 0 || Kind(stage) == 3 && stage.DurationSeconds == 0) weights += stage.Percent;
            }
            if (!Finite(weights)) throw new ArgumentException("Total flexible weight must be finite.", "plan");
        }

        internal JSONObject ExportState()
        {
            var root = new JSONObject();
            root["version"] = 2;
            root["structure"] = _structureKey ?? string.Empty;
            root["timing"] = _timingKey ?? string.Empty;
            root["stage"] = StageIndex;
            // SimpleJSON stores numbers as double; string encoding preserves the full Int64 entry token.
            root["entry"] = Entry.ToString(CultureInfo.InvariantCulture);
            root["cycle"] = Cycle;
            root["enteredAt"] = EnteredAt;
            root["stageElapsed"] = StageElapsedSeconds;
            root["expectedEnd"] = ExpectedEndSeconds;
            root["lastElapsed"] = _lastElapsed;
            root["lastHeartbeat"] = _lastHeartbeat;
            root["finished"] = Finished;
            root["reason"] = Reason;
            root["lastOverride"] = LastOverride;
            root["lastOverrideAt"] = LastOverrideAt;
            root["autoAugmentsCompleted"] = AutoAugmentsCompleted;
            root["autoAtCompleted"] = AutoAtCompleted;
            root["autoWandoosStarted"] = AutoWandoosStarted;
            root["autoEnergyPool"] = AutoEnergyPool.HasValue ? (JSONNode) new JSONString(AutoEnergyPool.Value.ToString(CultureInfo.InvariantCulture)) : JSONNull.CreateOrGet();
            root["autoMagicPool"] = AutoMagicPool.HasValue ? (JSONNode) new JSONString(AutoMagicPool.Value.ToString(CultureInfo.InvariantCulture)) : JSONNull.CreateOrGet();
            root["autoCleanupRecoveryRequired"] = AutoCleanupRecoveryRequired;
            var durations = new JSONArray();
            var earned = new JSONArray();
            var completed = new JSONArray();
            var applied = new JSONArray();
            var elapsedByStage = new JSONArray();
            var visits = new JSONArray();
            var skipped = new JSONArray();
            for (var i = 0; i < _durations.Length; i++)
            {
                durations.Add(_durations[i]);
                earned.Add(_earned[i]);
                completed.Add(_completed[i]);
                applied.Add(_applied[i]);
                elapsedByStage.Add(_elapsedByStage[i]);
                visits.Add(_visits[i]);
                skipped.Add(_skipped[i]);
            }
            root["durations"] = durations;
            root["earned"] = earned;
            root["completed"] = completed;
            root["applied"] = applied;
            root["elapsedByStage"] = elapsedByStage;
            root["visits"] = visits;
            root["skipped"] = skipped;
            return root;
        }

        // Transactional import: failure leaves this instance untouched; success is always unarmed.
        internal bool RestoreState(CustomPlan plan, JSONObject root)
        {
            try { ValidatePlan(plan); }
            catch (ArgumentException) { return false; }
            if (root == null || !Number(root["version"], 2, 2, true)
                || !root["structure"].IsString || root["structure"].Value != StructureKey(plan)
                || !root["timing"].IsString || string.IsNullOrEmpty(root["timing"].Value)
                || !Number(root["stage"], -1, plan.Stages.Length - 1, true)
                || !Number(root["cycle"], 0, int.MaxValue, true)
                || !root["entry"].IsString || !root["finished"].IsBoolean || !root["reason"].IsString
                || !root["lastOverride"].IsString)
                return false;
            long entry;
            if (!long.TryParse(root["entry"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out entry)
                || entry < 0 || (root["stage"].AsInt >= 0 && entry == 0))
                return false;
            long? energyPool, magicPool;
            if (!Pool(root["autoEnergyPool"], out energyPool) || !Pool(root["autoMagicPool"], out magicPool)) return false;
            foreach (var field in new[] { "enteredAt", "stageElapsed", "expectedEnd", "lastElapsed", "lastHeartbeat", "lastOverrideAt" })
                if (!Number(root[field], 0, double.MaxValue, false)) return false;
            var n = plan.Stages.Length;
            if (!(root["durations"] is JSONArray) || root["durations"].Count != n
                || !(root["earned"] is JSONArray) || root["earned"].Count != n
                || !(root["completed"] is JSONArray) || root["completed"].Count != n
                || !(root["applied"] is JSONArray) || root["applied"].Count != n
                || !(root["elapsedByStage"] is JSONArray) || root["elapsedByStage"].Count != n
                || !(root["visits"] is JSONArray) || root["visits"].Count != n
                || !(root["skipped"] is JSONArray) || root["skipped"].Count != n) return false;
            var kinds = new int[n];
            var durations = new double[n];
            var earned = new double[n];
            var completed = new bool[n];
            var applied = new double[n];
            var elapsedByStage = new double[n];
            var visits = new int[n];
            var skipped = new bool[n];
            var index = root["stage"].AsInt;
            var finished = root["finished"].AsBool;
            var recovery = root["autoCleanupRecoveryRequired"];
            if (root.Linq.Any(x => x.Key == "autoCleanupRecoveryRequired") && !recovery.IsBoolean) return false;
            var recoveryRequired = recovery != null && recovery.AsBool;
            if (recoveryRequired && (index < 0 || finished || Kind(plan.Stages[index]) != 3)) return false;
            for (var i = 0; i < n; i++)
            {
                if (!Number(root["durations"][i], 0, double.MaxValue, false)
                    || !Number(root["earned"][i], 0, double.MaxValue, false) || !root["completed"][i].IsBoolean
                    || !Number(root["applied"][i], 0, double.MaxValue, false)
                    || !Number(root["elapsedByStage"][i], 0, double.MaxValue, false)
                    || !Number(root["visits"][i], 0, int.MaxValue, true) || !root["skipped"][i].IsBoolean)
                    return false;
                kinds[i] = Kind(plan.Stages[i]);
                if (kinds[i] == 3 && (!root["autoAugmentsCompleted"].IsBoolean
                    || !root["autoAtCompleted"].IsBoolean || !root["autoWandoosStarted"].IsBoolean)) return false;
                durations[i] = root["durations"][i].AsDouble;
                earned[i] = root["earned"][i].AsDouble;
                completed[i] = root["completed"][i].AsBool;
                applied[i] = root["applied"][i].AsDouble;
                elapsedByStage[i] = root["elapsedByStage"][i].AsDouble;
                visits[i] = root["visits"][i].AsInt;
                skipped[i] = root["skipped"][i].AsBool;
                if ((kinds[i] != 1 && earned[i] != 0) || (kinds[i] == 2 && durations[i] != 0)
                    || (kinds[i] == 1 && completed[i] && earned[i] == 0 && !skipped[i])
                    || (skipped[i] && !completed[i]) || earned[i] > applied[i] || applied[i] > elapsedByStage[i]
                    || (visits[i] == 0 && (applied[i] != 0 || elapsedByStage[i] != 0))
                    || (i == index && visits[i] == 0)
                    || (index < 0 && (!finished || kinds[i] != 0 && !(kinds[i] == 3 && plan.Stages[i].DurationSeconds == 0) || durations[i] != 0))
                    || (index < 0 && (visits[i] != 0 || skipped[i]))
                    || (kinds[i] == 1 && (durations[i] < 1 || durations[i] > int.MaxValue
                        || Math.Floor(durations[i]) != durations[i] || earned[i] > int.MaxValue))
                    || (i > index && earned[i] != 0)
                    || (i < index && !completed[i]) || (i > index && completed[i] != finished)
                    || (i == index && completed[i] != finished)) return false;
            }
            if (root["enteredAt"].AsDouble > root["lastElapsed"].AsDouble
                || root["lastOverrideAt"].AsDouble > root["lastElapsed"].AsDouble
                || (root["lastOverride"].Value.Length == 0 && root["lastOverrideAt"].AsDouble != 0)
                || (index < 0 && (root["stageElapsed"].AsDouble != 0 || root["cycle"].AsInt != 0))
                || (index >= 0 && kinds[index] == 1 && root["stageElapsed"].AsDouble != earned[index])
                || (root["cycle"].AsInt > 0 && plan.StageLoopStartIndex < 0)) return false;

            _structureKey = root["structure"].Value;
            _timingKey = root["timing"].Value;
            StageIndex = index;
            Entry = entry;
            Cycle = root["cycle"].AsInt;
            EnteredAt = root["enteredAt"].AsDouble;
            StageElapsedSeconds = root["stageElapsed"].AsDouble;
            ExpectedEndSeconds = root["expectedEnd"].AsDouble;
            _lastElapsed = root["lastElapsed"].AsDouble;
            _lastHeartbeat = root["lastHeartbeat"].AsDouble;
            Finished = finished;
            Reason = root["reason"].Value;
            _kinds = kinds;
            _durations = durations;
            _earned = earned;
            _completed = completed;
            _applied = applied;
            _elapsedByStage = elapsedByStage;
            _visits = visits;
            _skipped = skipped;
            LastOverride = root["lastOverride"].Value;
            LastOverrideAt = root["lastOverrideAt"].AsDouble;
            AutoAugmentsCompleted = root["autoAugmentsCompleted"].AsBool;
            AutoAtCompleted = root["autoAtCompleted"].AsBool;
            AutoWandoosStarted = root["autoWandoosStarted"].AsBool;
            AutoEnergyPool = energyPool; AutoMagicPool = magicPool;
            AutoCleanupRecoveryRequired = recoveryRequired;
            if (recoveryRequired) Reason = CleanupRecoveryReason;
            _armed = false;
            AllowRepeat = true;
            RefreshGate();
            return true;
        }

        private static bool Number(JSONNode node, double min, double max, bool integer)
        {
            return node != null && node.IsNumber && Finite(node.AsDouble) && node.AsDouble >= min
                   && node.AsDouble <= max && (!integer || Math.Floor(node.AsDouble) == node.AsDouble);
        }

        private static bool Pool(JSONNode node, out long? value)
        {
            value = null;
            if (node == null || node.IsNull) return true;
            long parsed;
            if (!node.IsString || !long.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < 0) return false;
            value = parsed;
            return true;
        }
    }
}
