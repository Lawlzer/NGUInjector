using System;
using System.Globalization;
using System.Linq;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal partial class Main
    {
        private JSONObject GetExecutionStateRuntime()
        {
            var result = new JSONObject(); result["lifecycle"] = _executionLifecycle.ToString();
            result["generation"] = _executionGeneration.ToString(CultureInfo.InvariantCulture);
            var claims = new JSONArray();
            foreach (var claim in _executionClaims.Where(x => !x.Ended))
            {
                var row = new JSONObject(); row["owner"] = claim.Owner; row["acquisition"] = claim.Acquisition;
                row["domains"] = claim.Domains.ToString(); claims.Add(row);
            }
            result["claims"] = claims;
            if (Character?.inventory == null || Settings == null) return result;
            try
            {
                var desired = _executionBaseline == null ? _quickExecutionClaim?.Setup : ResolveExecutionSetup(_executionBaseline);
                var actual = LoadoutManager.CaptureExactLoadout(); var gear = new JSONArray();
                foreach (var item in actual)
                {
                    var wanted = desired?.ControlsGear == true ? desired.Gear?.SingleOrDefault(x => x.slot == item.slot) : null;
                    var row = new JSONObject(); row["slot"] = item.slot; row["observed"] = item.id;
                    row["desired"] = wanted == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONNumber(wanted.id);
                    row["verified"] = wanted != null && (wanted.id == 0 ? item.id == 0 : ReferenceEquals(item.equipment, wanted.equipment))
                        && (desired.MaterialValid == null || desired.MaterialValid()); gear.Add(row);
                }
                result["gear"] = gear;
                var support = new JSONObject(); support["observed"] = CaptureSupportState().Selection.StableKey;
                support["desired"] = desired?.ChangeSupport == true ? desired.Support.StableKey : "uncontrolled";
                support["verified"] = _executionLifecycle == ExecutionLifecycle.Running && desired?.ChangeSupport == true
                    && desired.Authorized?.Invoke() == true && desired.Support.StableKey == support["observed"].Value;
                if (desired?.ChangeSupport == true)
                {
                    var feasibility = SupportProjection.Evaluate(CaptureSupportContext(), desired.Support, desired.Budget, desired.EnforceSupportBudget);
                    support["desiredFeasible"] = feasibility.Feasible; support["reason"] = feasibility.Reason ?? "";
                }
                result["support"] = support;
                var resourceIntent = _resourceSetupExecutor.Current;
                var observed = ResourceWriter.CaptureAllocations(Character); var resources = new JSONArray();
                if (resourceIntent?.Desired != null)
                    foreach (var pair in resourceIntent.Desired)
                    {
                        var row = new JSONObject(); row["target"] = pair.Key.ToString();
                        var wanted = new JSONArray(); var current = new JSONArray();
                        foreach (var amount in pair.Value ?? new long[0]) wanted.Add(amount.ToString(CultureInfo.InvariantCulture));
                        observed.TryGetValue(pair.Key, out var values);
                        foreach (var amount in values ?? new long[0]) current.Add(amount.ToString(CultureInfo.InvariantCulture));
                        row["desired"] = wanted; row["observed"] = current;
                        row["verified"] = _executionLifecycle == ExecutionLifecycle.Running && resourceIntent.Authorized()
                            && pair.Value != null && values != null && pair.Value.SequenceEqual(values);
                        resources.Add(row);
                    }
                result["resources"] = resources;
                var route = new JSONObject(); route["observedZone"] = Character.adventure.zone;
                route["observedStart"] = Character.adventure.itopodStart; route["observedEnd"] = Character.adventure.itopodEnd;
                var policy = _globalPresetReceipt?.Policy;
                route["desiredZone"] = policy?.ExecutionPreset.Weights.Skip(3).Take(3).Any(x => x > 0) == true
                    ? (JSONNode)new JSONNumber(policy.Candidate.Zone) : JSONNull.CreateOrGet();
                result["route"] = route;
                var native = new JSONObject(); native["os"] = (int)Character.wandoos98.os;
                var targets = new JSONArray(); foreach (var target in Character.advancedTraining.levelTarget) targets.Add(target.ToString(CultureInfo.InvariantCulture));
                native["atTargets"] = targets; result["nativeSettings"] = native;
                native["desiredOs"] = policy?.Candidate.Activities == null ? JSONNull.CreateOrGet() : (JSONNode)new JSONNumber(policy.Candidate.Activities.Os);
                native["desiredAtPower"] = policy?.Stage?.TargetAdvancedTrainingPower.ToString(CultureInfo.InvariantCulture) ?? "";
                native["desiredAtToughness"] = policy?.Stage?.TargetAdvancedTrainingToughness.ToString(CultureInfo.InvariantCulture) ?? "";
                var domains = new JSONObject();
                foreach (var domain in new[] { ExecutionDomain.Energy, ExecutionDomain.Magic, ExecutionDomain.Res3,
                    ExecutionDomain.Gear, ExecutionDomain.Support, ExecutionDomain.NativeSettings, ExecutionDomain.Route })
                {
                    var controlled = domain == ExecutionDomain.Gear ? desired?.ControlsGear == true
                        : domain == ExecutionDomain.Support ? desired?.ChangeSupport == true
                        : domain == ExecutionDomain.Energy ? resourceIntent?.Energy == true
                        : domain == ExecutionDomain.Magic ? resourceIntent?.Magic == true
                        : domain == ExecutionDomain.Res3 ? resourceIntent?.Res3 == true
                        : _executionClaims.Any(x => !x.Ended && (x.Domains & domain) != 0);
                    var state = new JSONObject(); state["controlled"] = controlled;
                    state["state"] = !controlled ? "Uncontrolled" : _executionLifecycle != ExecutionLifecycle.Running ? "Quiesced" : "Observed";
                    state["waitingReason"] = !controlled ? "" : domain == ExecutionDomain.Support ? support["reason"].Value
                        : _resourceExecutionReason ?? "";
                    domains[domain.ToString()] = state;
                }
                result["domains"] = domains;
                result["waitingReason"] = _resourceExecutionReason ?? "";
            }
            catch (Exception error)
            {
                result["available"] = false; result["status"] = "error";
                result["waitingReason"] = error.Message;
                LogError("Execution observation", error);
            }
            return result;
        }
    }
}
