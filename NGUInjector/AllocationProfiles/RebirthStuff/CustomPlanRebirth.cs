using System;
using System.Linq;
using NGUInjector.CustomPlans;

namespace NGUInjector.AllocationProfiles.RebirthStuff
{
    internal class CustomPlanRebirth : BaseRebirth
    {
        internal const double NumberTriggerConfirmSeconds = 15d;
        private const double NumberSpikeResetRatio = 1.25d;
        private static string _numberConfirmationKey;
        private static string _numberConfirmationConfirmedKey;
        private static double _numberConfirmationStartedAt = -1d;
        private static double _numberConfirmationBestMultiplier;
        private readonly CustomPlan _plan;

        internal CustomPlanRebirth(CustomPlan plan)
        {
            _plan = plan;
            ChallengeTargets = new RCTarget[0];
        }

        internal override bool RebirthAvailable()
        {
            return RebirthAvailable(true);
        }

        internal bool PeekRebirthAvailable()
        {
            return RebirthAvailable(false);
        }

        private bool RebirthAvailable(bool updateNumberConfirmation)
        {
            if (!Main.Settings.AutoRebirth || _plan == null || !_plan.AutoRebirthEnabled)
                return false;

            if (!_plan.TimeTriggerEnabled && !_plan.NumberTriggerEnabled && !_plan.NumberClimbEnabled)
                return false;

            if (!BaseRebirthChecks())
                return false;

            if (CharObj.attackMulti > 0 && CharObj.nextAttackMulti < CharObj.attackMulti)
            {
                if (updateNumberConfirmation)
                    ResetNumberConfirmation();
                return false;
            }

            var seconds = CharObj.rebirthTime.totalseconds;
            var multiplier = CurrentNumberMultiplier();
            bool? timeReady = _plan.TimeTriggerEnabled
                ? (bool?) (seconds >= _plan.TimeTriggerSeconds)
                : null;
            bool? numberReady = _plan.NumberTriggerEnabled
                ? (bool?) NumberTargetReached(_plan.NumberMultiplierTarget)
                : null;
            bool? numberClimbReady = _plan.NumberClimbEnabled
                ? (bool?) (seconds >= _plan.NumberClimbMinSeconds && NumberTargetReached(_plan.NumberClimbMultiplierTarget))
                : null;

            var results = new[]
            {
                timeReady,
                numberReady,
                numberClimbReady
            }.Where(x => x.HasValue).Select(x => x.Value).ToArray();

            var triggerReady = _plan.TriggerMode == 1 ? results.All(x => x) : results.Any(x => x);
            if (!triggerReady)
            {
                if (updateNumberConfirmation)
                    ResetNumberConfirmation();
                return false;
            }

            var numberBasedReady = numberReady.GetValueOrDefault() || numberClimbReady.GetValueOrDefault();
            if (!numberBasedReady)
            {
                if (updateNumberConfirmation)
                    ResetNumberConfirmation();
                return true;
            }

            return NumberConfirmationReady(seconds, multiplier, updateNumberConfirmation);
        }

        private bool NumberTargetReached(double target)
        {
            return CharObj.attackMulti > 0 && CharObj.nextAttackMulti >= CharObj.attackMulti * target;
        }

        private double CurrentNumberMultiplier()
        {
            return CharObj.attackMulti > 0 ? CharObj.nextAttackMulti / CharObj.attackMulti : 0d;
        }

        private bool NumberConfirmationReady(double seconds, double multiplier, bool updateNumberConfirmation)
        {
            var key = NumberConfirmationKey();
            if (_numberConfirmationConfirmedKey == key)
                return true;

            if (_numberConfirmationKey != key || _numberConfirmationStartedAt < 0 || seconds < _numberConfirmationStartedAt)
            {
                if (updateNumberConfirmation)
                    StartNumberConfirmation(key, seconds, multiplier, "ready");
                return false;
            }

            var significantSpike = _numberConfirmationBestMultiplier > 0
                                   && multiplier > _numberConfirmationBestMultiplier
                                   && multiplier >= _numberConfirmationBestMultiplier * NumberSpikeResetRatio;
            if (significantSpike)
            {
                if (updateNumberConfirmation)
                    StartNumberConfirmation(key, seconds, multiplier, "improved");
                return false;
            }

            if (updateNumberConfirmation && multiplier > _numberConfirmationBestMultiplier)
            {
                _numberConfirmationBestMultiplier = multiplier;
            }

            if (seconds - _numberConfirmationStartedAt < NumberTriggerConfirmSeconds)
                return false;

            if (updateNumberConfirmation)
                _numberConfirmationConfirmedKey = key;
            return true;
        }

        private string NumberConfirmationKey()
        {
            return $"{_plan.Name}:{_plan.NumberTriggerEnabled}:{_plan.NumberMultiplierTarget:0.###}:{_plan.NumberClimbEnabled}:{_plan.NumberClimbMinSeconds}:{_plan.NumberClimbMultiplierTarget:0.###}:{_plan.TriggerMode}";
        }

        private static void StartNumberConfirmation(string key, double seconds, double multiplier, string reason)
        {
            _numberConfirmationKey = key;
            _numberConfirmationConfirmedKey = null;
            _numberConfirmationStartedAt = seconds;
            _numberConfirmationBestMultiplier = Math.Max(0d, multiplier);
            Main.Log($"Custom rebirth NUMBER {reason} at x{multiplier:0.###}; confirming for {NumberTriggerConfirmSeconds:0}s before rebirth.");
        }

        private static void ResetNumberConfirmation()
        {
            _numberConfirmationKey = null;
            _numberConfirmationConfirmedKey = null;
            _numberConfirmationStartedAt = -1d;
            _numberConfirmationBestMultiplier = 0d;
        }

        internal static void ResetRuntimeState()
        {
            ResetNumberConfirmation();
        }
    }
}
