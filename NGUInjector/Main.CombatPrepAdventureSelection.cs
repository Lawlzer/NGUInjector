using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private static void PrepareCombatPrepSelectionSnapshot(LoadoutPresetCapture capture)
        {
            var s = capture.Snapshot;
            foreach (var tier in s.Augments)
            {
                if (tier.Augment.Target > 0) tier.Augment.Target = 0;
                if (tier.Upgrade.Target > 0) tier.Upgrade.Target = 0;
            }
            for (var i = 3; i < s.AdvancedTraining.Length; i++)
                if (s.AdvancedTraining[i].Target > 0) s.AdvancedTraining[i].Target = 0;

            // Native W progress is amount * totalWandoosSpeed / baseTime (no resource power or /50).
            // Sampling live progress cannot recover a coefficient at zero bootup. Rank a full-boot
            // setup forward, while CombatPrepSnapshot independently retains the real bootup ramp.
            var c = Character;
            if (!c.canLevel() || !c.wandoos98.installed) return;
            try
            {
                var support = ObserveCombatPrepSupport(s);
                var bonuses = CombatPrepGearBonuses(CombatPrepEquipped());
                var completion = c.inventory.itemList.wandoosComplete ? (double)1.1f : 1;
                Func<bool, PresetActivityBar> bar = energy => {
                    var at = energy ? c.advancedTrainingController.wandoosEnergy : c.advancedTrainingController.wandoosMagic;
                    var coefficient = CombatPrepWandoosSpeedWithoutAtOrBootup(energy, bonuses, support)
                        * (1f + at.trainingBonus(0)) * completion
                        / (energy ? c.wandoos98Controller.baseEnergyTime() : c.wandoos98Controller.baseMagicTime());
                    if (!LoadoutPresetPlanner.Finite(coefficient) || coefficient <= 0)
                        throw new InvalidOperationException("Full-boot Wandoos selection coefficient is unavailable.");
                    return new PresetActivityBar { Level = energy ? c.wandoos98.energyLevel : c.wandoos98.magicLevel,
                        Progress = energy ? c.wandoos98.energyProgress : c.wandoos98.magicProgress,
                        Coefficient = coefficient, PowerExponent = 0, LevelExponent = 0, Unlocked = true };
                };
                s.WandoosOs = (int)c.wandoos98.os;
                s.WandoosEnergy = bar(true); s.WandoosMagic = bar(false);
                s.Available[8] = true; s.Reasons[8] = null;
            }
            catch (Exception error)
            {
                s.Available[8] = false; s.Reasons[8] = error.Message;
            }
        }

        private static void CaptureCombatPrepAdventureProfiles(CombatPrepCapture capture, double adventureWeight, int horizon)
        {
            // Resolve the dynamic task once, then reuse one immutable numeric capture for all four searches.
            var definition = new CustomLoadoutPreset { Id = "combat-prep-selection", Name = "Combat prep selection",
                Weights = new double[LoadoutPresetPlanner.Keys.Length], AdventureDynamicWeight = adventureWeight,
                AdvancedTrainingWeights = new[] { 0, 0, 0, 1, 1 }, SupportMode = "planned", SwapSeconds = 0,
                MagicBloodPercent = 0, DiggerBudgetPercent = (int)Math.Round(ActivityPolicy.Current.DiggerBudgetFraction * 100) };
            var selection = CaptureCombatPrepLoadoutDefinition(definition);
            capture.AdventureDynamicWeight = adventureWeight;
            capture.AdventureTaskKey = selection.AdventureTaskKey;
            capture.AdventureTask = selection.AdventureTask;
            var s = selection.Snapshot;
            var resolved = selection.ExecutionPreset;
            var adventure = Array.FindIndex(resolved.Weights, x => x > 0);
            if (adventure < 3 || adventure > 5 || !s.Available[adventure])
                throw new InvalidOperationException("Requested combat prep adventure is unavailable: "
                    + (adventure >= 0 ? s.Reasons[adventure] : "No resolved adventure objective."));
            var goals = new[] { 7, 9, 8, 0 };
            var keys = new[] { "augments-planned", "at-planned", "wandoos-planned", "ngu-planned" };
            var labels = new[] { "Augments", "AT", "Wandoos", "NGU" };
            for (var index = 0; index < goals.Length; index++)
            {
                var goal = goals[index]; var key = keys[index]; var label = labels[index];
                var unavailable = s.Available[goal] ? null : s.Reasons[goal];
                if (goal == 9 && !s.AdvancedTraining.Where((x, i) => i >= 3).Any(x => x.Unlocked))
                    unavailable = unavailable ?? "No enabled Wandoos AT lanes (3/4).";
                if (goal == 0 && !s.EnergyNgu.Any(x => x.Weight > 0 && x.Target >= 0 && x.Level < x.HardCap))
                    unavailable = "No enabled, positively weighted energy NGUs below their hard cap.";
                if (!s.Available[goal] || unavailable != null)
                {
                    capture.UnavailableGearGoals.Add(label + ": " + (unavailable ?? "Primary phase unavailable."));
                    continue;
                }
                var request = LoadoutPresetRequest(resolved, horizon);
                request.Weights[goal] = 100;
                request.SetupOnly = true;
                // Each search gets its own bounded clock; capture time never consumes a later goal's seed budget.
                var plan = LoadoutPresetPlanner.Plan(s, request);
                var winner = plan.Candidates[1];
                if (!winner.Feasible || !winner.Score.HasValue || !LoadoutPresetPlanner.Finite(winner.Score.Value)
                    || request.Weights.Where((weight, g) => weight > 0 && (!winner.Metrics[g].HasValue
                        || !LoadoutPresetPlanner.Finite(winner.Metrics[g].Value))).Any())
                {
                    capture.UnavailableGearGoals.Add(label + ": No feasible finite jointly weighted setup. " + string.Join(" ", winner.Warnings));
                    continue;
                }
                var gear = winner.Items.Select((item, i) => {
                    var wrapper = item.Id == 0 ? new ih { equipment = new Equipment(), id = 0 } : selection.Gear[item.SourceSlot];
                    return new ih { id = wrapper.id, name = wrapper.name, level = wrapper.level,
                        equipment = wrapper.equipment, slot = s.Slots[i] };
                }).ToArray();
                gear = LoadoutManager.ComposeExactLoadout(capture.OriginalGear, gear);
                if (gear.Where(x => x.id != 0).GroupBy(x => x.id).Any(x => x.Count() > 1))
                    throw new InvalidOperationException("Weighted prep gear contains conflicting item IDs.");
                var evaluation = SupportProjection.Evaluate(capture.SupportContext, new SupportSelection(winner.Support.Beards,
                    winner.Support.Diggers.Select(x => new SupportDiggerLevel(x.Id, x.Level)).ToArray()), request.DiggerPercent / 100d, true);
                if (!evaluation.Feasible)
                {
                    capture.UnavailableGearGoals.Add(label + ": " + evaluation.Reason);
                    continue;
                }
                winner.Key = key; winner.Label = label + " / weighted adventure";
                capture.Gear.Add(key, gear);
                capture.Support.Add(key, new CombatPrepSupport { ProfileGoal = label, BudgetFraction = request.DiggerPercent / 100d,
                    EnforceBudget = true, Evaluation = evaluation });
                capture.AdventureRoutes.Add(key, winner);
                capture.AdventureSnapshots.Add(key, s);
            }
        }
    }
}
