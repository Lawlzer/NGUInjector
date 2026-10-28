using System;
using System.Diagnostics;
using System.Linq;

namespace NGUInjector.Managers
{
    internal static partial class LoadoutPresetPlanner
    {
        // Acquired setup/route decisions are reusable; resource funding is an observation,
        // not a decision to pin across pool, cap, progress or temporary-demand changes.
        internal static PresetCandidate EvaluateExecution(PresetSnapshot snapshot, PresetRequest request, PresetItem[] items,
            double[] weights, double[] normalization, string key, string label, PresetCandidate policy = null)
        {
            if (!request.ResourcePrioritiesReviewed || policy == null)
                return Evaluate(snapshot, request, items, weights, normalization, key, label, policy);
            var activities = policy.Activities;
            if (activities == null) throw new InvalidOperationException("Acquired activity decisions are missing; preview again.");
            var wandoos = activities.WandoosEnergy > 0 || activities.WandoosMagic > 0
                || (request.EnergyPriorities?.Contains("WAN") ?? false) || (request.MagicPriorities?.Contains("WAN") ?? false);
            if (wandoos && activities.Os != snapshot.WandoosOs)
                throw new InvalidOperationException("Acquired Wandoos OS is incompatible with the installed OS; preview again.");
            var funded = EvaluateSelection(FundingSnapshot(snapshot, policy), request, items, weights, normalization, key, label, null, policy.Support, activities);
            var replay = policy.Copy();
            replay.PriorityFunding = funded.PriorityFunding;
            replay.Activities = funded.Activities;
            replay.Ritual = funded.Ritual;
            replay.BloodPercent = funded.BloodPercent;
            return Evaluate(snapshot, request, items, weights, normalization, key, label, replay);
        }

        internal static PresetPlan ReplayExecution(PresetSnapshot snapshot, PresetRequest request, PresetPlan acquired, Stopwatch clock)
        {
            if (!request.ResourcePrioritiesReviewed) return Replay(snapshot, request, acquired, clock);
            // Replay's normalizations and selected item policies remain the original ones.
            var result = new PresetPlan { Normalization = acquired.Normalization, ReferenceKeys = acquired.ReferenceKeys,
                ReferenceProduction = acquired.ReferenceProduction, Evaluated = acquired.Evaluated, Budget = acquired.Budget };
            result.Candidates = System.Array.ConvertAll(acquired.Candidates, candidate => EvaluateExecution(snapshot, request,
                candidate.Items, NormalizeWeights(request.Weights), acquired.Normalization, candidate.Key, candidate.Label, candidate));
            result.Specialists = System.Array.ConvertAll(acquired.Specialists, candidate => EvaluateExecution(snapshot, request,
                candidate.Items, NormalizeWeights(request.Weights), acquired.Normalization, candidate.Key, candidate.Label, candidate));
            result.Milliseconds = clock?.ElapsedMilliseconds ?? 0;
            return result;
        }
    }
}
