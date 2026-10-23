using System;
using System.Globalization;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private FiniteActionRecord _ironWindowRecord;
        private JSONObject IronWindowData => _ironWindowRecord == null ? null : JsonFiles.ParseObject(_ironWindowRecord.Before);

        private bool AcquireIronBloodWindow(CustomPlan plan, out double started)
        {
            started = -1d;
            var owner = Settings.CustomPlanFile ?? "default";
            var identity = ActionIdentity("iron-saving-window", owner, "1");
            var latest = FiniteActions.Records.Where(x => x.Identity.Kind == identity.Kind && x.Identity.Owner == owner
                && x.Identity.Acquisition == identity.Acquisition && x.Identity.Entry == identity.Entry)
                .OrderByDescending(x => long.Parse(x.Identity.Opportunity, CultureInfo.InvariantCulture)).FirstOrDefault();
            var now = Character.rebirthTime.totalseconds;
            if (latest != null)
            {
                if (latest.Dirty) FiniteActions.Save(latest);
                var saved = JsonFiles.ParseObject(latest.Before);
                var originalStart = saved["started"].AsDouble;
                var deadline = saved["deadline"].AsDouble;
                if (!(originalStart >= 0d && deadline >= originalStart && now >= originalStart)) return false;
                if (latest.State == FiniteActionState.Prepared)
                {
                    _ironWindowRecord = latest;
                    if (now < deadline) { started = originalStart; return true; }
                    FinishIronBloodWindow(false);
                    return false;
                }
                if (latest.State == FiniteActionState.Unknown || latest.State == FiniteActionState.Invocation) return false;
                if (latest.State == FiniteActionState.Rejected && now < deadline + 900d) return false;
                identity = ActionIdentity("iron-saving-window", owner,
                    (long.Parse(latest.Identity.Opportunity, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture));
            }
            var state = new JSONObject(); state["started"] = now;
            state["saveSeconds"] = EffectiveCustomIronPillSaveSeconds(plan);
            state["minWorth"] = EffectiveCustomIronPillMinWorth(plan);
            state["deadline"] = now + IronPillAutomation.PrepLimitSeconds(state["saveSeconds"].AsInt);
            _ironWindowRecord = new FiniteActionRecord { Identity = identity, State = FiniteActionState.Prepared, Before = state.ToString(),
                Epoch = identity.Acquisition, Domains = (int)ExecutionDomain.Magic };
            FiniteActions.Save(_ironWindowRecord);
            started = now;
            return true;
        }

        private void FinishIronBloodWindow(bool committed)
        {
            if (_ironWindowRecord == null) return;
            _ironWindowRecord.State = committed ? FiniteActionState.Committed : FiniteActionState.Rejected;
            _ironWindowRecord.After = ActionNumber(Character.rebirthTime.totalseconds);
            FiniteActions.Save(_ironWindowRecord);
            _ironWindowRecord = null;
        }
    }
}
