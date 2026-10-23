using System;
using System.Linq;
using System.Reflection;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private static void ConfigurePresetActivities(PresetRequest request, CustomLoadoutPreset preset)
        {
            CustomLoadoutPreset.Validate(preset);
            request.ATWeights = preset.AdvancedTrainingWeights.ToArray();
        }

        private static void CapturePresetActivities(PresetSnapshot snapshot, CustomLoadoutPreset preset)
        {
            CustomLoadoutPreset.Validate(preset);
            snapshot.ATGoalWeight = preset.Weights[9];
            snapshot.ATWeights = preset.AdvancedTrainingWeights.ToArray();
            snapshot.RequiredATTargets = new long[0];
            Array.Resize(ref snapshot.Available, LoadoutPresetPlanner.Keys.Length);
            Array.Resize(ref snapshot.Reasons, LoadoutPresetPlanner.Keys.Length);
            var c = Character;
            for (var g = 6; g < LoadoutPresetPlanner.ProductionGoalCount; g++)
            {
                snapshot.Available[g] = false;
                snapshot.Reasons[g] = "Native activity capture unavailable.";
                try
                {
                    if (!c.canLevel()) throw new InvalidOperationException("Native level gain is currently disabled.");
                    if (g == 6)
                    {
                        if (c.machine == null || c.timeMachineController == null || !c.buttons.brokenTimeMachine.interactable || c.bossID <= 29
                            || c.challenges.timeMachineChallenge.inChallenge || !c.machine.transferredBankLevels)
                            throw new InvalidOperationException("Time Machine locked, challenged, or bank transfer pending.");
                        var e = CapturePresetActivitySample(amount => c.timeMachineController.speedProgressPerTick(amount),
                            c.machine.levelSpeed, c.machine.speedTarget, c.machine.speedProgress, c.totalEnergyPower(), 1);
                        var m = CapturePresetActivitySample(amount => c.timeMachineController.goldMultiProgressPerTick(amount),
                            c.machine.levelGoldMulti, c.machine.multiTarget, c.machine.goldMultiProgress, c.totalMagicPower(), 1);
                        var speed = (double)c.hacksController.totalTMSpeedBonus() * c.allChallenges.timeMachineChallenge.TMSpeedBonus()
                            * c.cardsController.getBonus(cardBonus.TMSpeed) / 50000d;
                        if (c.settings.rebirthDifficulty >= difficulty.sadistic) speed /= c.timeMachineController.sadisticDivider();
                        SetPresetActivityCoefficient(e, speed / c.timeMachineController.baseSpeedDivider());
                        SetPresetActivityCoefficient(m, speed / c.timeMachineController.baseGoldMultiDivider());
                        // Native affordability tests the undiscounted cost before charging totalDiscount().
                        e.CostCoefficient = CapturePresetTimeMachineCost("baseSpeedCost", c.machine.levelSpeed, c.timeMachineController.machineSpeedGoldCost());
                        m.CostCoefficient = CapturePresetTimeMachineCost("baseGoldMultiCost", c.machine.levelGoldMulti, c.timeMachineController.machineGoldMultiCost());
                        snapshot.TimeMachineEnergy = e; snapshot.TimeMachineMagic = m;
                    }
                    if (g == 7)
                    {
                        if (c.augmentsController == null || !c.buttons.augmentation.interactable)
                            throw new InvalidOperationException("Augments locked.");
                        snapshot.Augments = c.augmentsController.augments.Select((controller, i) => {
                            var model = c.augments.augs[i];
                            var aug = CaptureCombatPrepAugLane(controller, false, model.augLevel, model.augProgress);
                            var up = CaptureCombatPrepAugLane(controller, true, model.upgradeLevel, model.upgradeProgress);
                            var a = PresetAugmentBar(aug, model.augmentTarget); var u = PresetAugmentBar(up, model.upgradeTarget);
                            SetPresetActivityCoefficient(a, PresetAugmentCoefficient(controller, false));
                            SetPresetActivityCoefficient(u, PresetAugmentCoefficient(controller, true));
                            return new PresetAugment { Id = i, BaseBoost = controller.baseBoost, TierExponent = controller.augTierBonus(),
                                Augment = a, Upgrade = u };
                        }).ToArray();
                        snapshot.AugmentGlobalFactor = (1 + .25 * c.allChallenges.noAugsChallenge.completions()) * c.NGUController.augmentBonus()
                            / (c.settings.rebirthDifficulty == difficulty.sadistic ? c.augmentsController.sadisticNerfModifier() : 1);
                        var total = Math.Max(1, (1 + snapshot.Augments.Sum(x => x.BaseBoost * (x.Upgrade.Level * x.Upgrade.Level + 1)
                            * Math.Pow(x.Augment.Level, x.TierExponent))) * snapshot.AugmentGlobalFactor);
                        if (!CombatPrepNear(total, c.augmentsController.totalBonus()))
                            throw new InvalidOperationException("Native augment bonus parity failed.");
                        if (!snapshot.Augments.Any(x => x.Augment.Unlocked || x.Upgrade.Unlocked))
                            throw new InvalidOperationException("All augment tiers locked or disabled by targets.");
                    }
                    if (g == 8)
                    {
                        if (c.wandoos98 == null || c.wandoos98Controller == null || !c.buttons.wandoos.interactable
                            || !c.settings.wandoos98On || c.wandoos98.disabled || !c.wandoos98.installed)
                            throw new InvalidOperationException("Wandoos must be enabled and installed; installation is not forecast.");
                        snapshot.WandoosOs = (int)c.wandoos98.os;
                        snapshot.WandoosEnergy = CapturePresetActivitySample(amount => c.wandoos98Controller.energyProgressToAdd(amount),
                            c.wandoos98.energyLevel, 0, c.wandoos98.energyProgress, 1, 0);
                        snapshot.WandoosMagic = CapturePresetActivitySample(amount => c.wandoos98Controller.magicProgressToAdd(amount),
                            c.wandoos98.magicLevel, 0, c.wandoos98.magicProgress, 1, 0);
                    }
                    if (g == 9)
                    {
                        if (c.advancedTrainingController == null || !c.buttons.advancedTraining.interactable
                            || !c.advancedTrainingController.advancedTrainingUnlocked() || !c.advancedTraining.transferredBankedLevels)
                            throw new InvalidOperationException("AT locked or native bank transfer pending.");
                        if (c.wishes.wishes[190].level > 0)
                            throw new InvalidOperationException("Wish 190 supplies passive AT; paid weighted AT is not supported.");
                        var controllers = new[] { c.advancedTrainingController.defense, c.advancedTrainingController.attack,
                            c.advancedTrainingController.block, c.advancedTrainingController.wandoosEnergy, c.advancedTrainingController.wandoosMagic };
                        snapshot.AdvancedTraining = controllers.Select((controller, i) => {
                            var lane = CaptureCombatPrepAt(i, controller);
                            var coefficient = c.totalAdvancedTrainingSpeedBonus() / (50d * lane.BaseTime);
                            var expected = c.totalCapEnergy() * Math.Sqrt(c.totalEnergyPower()) * coefficient / (lane.Level + 1d);
                            if (!CombatPrepNear(expected, controller.progressPerTick1000()))
                                throw new InvalidOperationException("Native AT progress parity failed.");
                            return new PresetActivityBar { Level = lane.Level, Target = c.advancedTraining.levelTarget[i],
                                Progress = lane.Progress, Coefficient = coefficient, PowerExponent = .5,
                                Unlocked = lane.Unlocked && (i < 3 || c.settings.wandoos98On) };
                        }).ToArray();
                    }
                    snapshot.Available[g] = true;
                    snapshot.Reasons[g] = null;
                }
                catch (Exception error)
                {
                    snapshot.Reasons[g] = error.Message;
                }
            }
        }

        private static void ApplyPresetStageTargets(PresetSnapshot snapshot, CustomPlanStage stage)
        {
            if (stage == null || stage.CompletionMode != (int)CustomStageCompletionMode.AdvancedTrainingStats) return;
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var targets = new[] { Math.Max(0, stage.TargetAdvancedTrainingToughness), Math.Max(0, stage.TargetAdvancedTrainingPower) };
            if (targets.Any(x => x > 9007199254740991L))
                throw new InvalidOperationException("Mandatory stage AT targets exceed the numeric planner's exact integer range.");
            if (targets.Any(x => x > 0) && (snapshot.AdvancedTraining.Length != 5 || snapshot.Available.Length <= 9 || !snapshot.Available[9]))
                throw new InvalidOperationException("Mandatory stage AT requires a successful native coefficient/parity capture: "
                    + (snapshot.Reasons.Length > 9 ? snapshot.Reasons[9] : "AT unavailable."));
            for (var i = 0; i < 2 && i < snapshot.AdvancedTraining.Length; i++)
            {
                var lane = snapshot.AdvancedTraining[i];
                if (targets[i] <= 0 || lane.Level >= targets[i]) continue;
                if (!LoadoutPresetPlanner.Finite(lane.Coefficient) || lane.Coefficient <= 0)
                    throw new InvalidOperationException("Mandatory stage AT native coefficient is unavailable.");
                if (!(snapshot.ATGoalWeight > 0) || snapshot.ATWeights.Length != 5 || snapshot.ATWeights[i] <= 0)
                    throw new InvalidOperationException("Mandatory stage AT " + (i == 0 ? "toughness" : "power")
                        + " is excluded by preset weights. Set Weights[9] and AdvancedTrainingWeights[" + i + "] positive before applying this stage.");
            }
            // These are snapshot-only execution constraints. Never overwrite native menu targets here.
            snapshot.RequiredATTargets = targets;
            for (var i = 0; i < 2 && i < snapshot.AdvancedTraining.Length; i++)
            {
                var lane = snapshot.AdvancedTraining[i];
                lane.Target = targets[i] > 0 ? targets[i] : -1;
                lane.Unlocked = targets[i] > lane.Level;
            }
        }

        private static PresetActivityBar PresetAugmentBar(CombatPrepAugmentLane lane, long target)
        {
            return new PresetActivityBar { Level = lane.Level, Progress = lane.Progress, Target = target,
                Coefficient = lane.ProgressCoefficient, Unlocked = lane.Unlocked && target >= 0,
                Paid = lane.Paid, CostCoefficient = lane.CostCoefficient * 1.000002, CostExponent = lane.CostExponent };
        }

        private static void SetPresetActivityCoefficient(PresetActivityBar bar, double coefficient)
        {
            // Keep material coefficients independent of level-dependent float sample rounding.
            if (!LoadoutPresetPlanner.Finite(coefficient) || coefficient <= 0
                || Math.Abs(bar.Coefficient - coefficient) > coefficient * 1e-6)
                throw new InvalidOperationException("Native activity forward coefficient parity failed.");
            bar.Coefficient = coefficient;
        }

        private static double CapturePresetTimeMachineCost(string fieldName, long level, double actual)
        {
            var controller = Character.timeMachineController;
            var field = controller.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Native TM base cost field unavailable.");
            var basis = Convert.ToSingle(field.GetValue(controller));
            if (!LoadoutPresetPlanner.Finite(basis) || basis < 0 || (float)((float)(level + 1) * basis) != actual)
                throw new InvalidOperationException("Native TM gold cost parity failed.");
            return basis * Math.Max(1, Character.totalDiscount()) * 1.000002;
        }

        private static double PresetAugmentCoefficient(AugmentController controller, bool upgrade)
        {
            var c = Character; var a = c.augmentsController; var id = controller.id;
            var divider = c.settings.rebirthDifficulty == difficulty.normal
                ? (upgrade ? a.normalUpgradeSpeedDividers[id] : a.normalAugSpeedDividers[id])
                : c.settings.rebirthDifficulty == difficulty.evil ? (upgrade ? a.evilUpgradeSpeedDividers[id] : a.evilAugSpeedDividers[id])
                : (upgrade ? a.sadisticUpgradeSpeedDividers[id] : a.sadisticAugSpeedDividers[id]);
            var value = 1d / divider / (c.settings.rebirthDifficulty == difficulty.sadistic ? 1 : 50000);
            value *= 1f + c.inventoryController.bonuses[specType.Augs];
            value *= c.inventory.macguffinBonuses[12]; value *= c.hacksController.totalAugSpeedBonus();
            value *= c.adventureController.itopod.totalAugSpeedBonus(); value *= c.cardsController.getBonus(cardBonus.augSpeed);
            value *= 1f + c.allChallenges.noAugsChallenge.evilCompletions() * .05f;
            if (c.allChallenges.noAugsChallenge.completions() >= 1) value *= (double)1.1f;
            if (c.allChallenges.noAugsChallenge.evilCompletions() >= c.allChallenges.noAugsChallenge.maxCompletions) value *= 1.25;
            if (c.settings.rebirthDifficulty >= difficulty.sadistic) value /= controller.sadisticDivider();
            return value;
        }

        private static PresetActivityBar CapturePresetActivitySample(Func<long, double> getter, long level, long target,
            double progress, double power, double levelExponent)
        {
            long sample = 1000;
            var rate = getter(sample);
            while (rate < .0001 && sample <= long.MaxValue / 100)
            { sample *= 100; rate = getter(sample); }
            var half = getter(sample / 2);
            if (!LoadoutPresetPlanner.Finite(rate) || rate <= 0 || rate >= float.MaxValue / 4
                || !LoadoutPresetPlanner.Finite(half) || half <= 0 || Math.Abs(half - rate / 2) > rate * 1e-6)
                throw new InvalidOperationException("Native activity coefficient sample is zero, clamped, or nonlinear.");
            return new PresetActivityBar { Level = level, Target = target, Progress = progress, Paid = progress > 0,
                Coefficient = rate / sample / power * Math.Pow(level + 1d, levelExponent), LevelExponent = levelExponent,
                PowerExponent = levelExponent == 0 ? 0 : 1, Unlocked = target >= 0 };
        }

        private static void ValidatePresetActivities(PresetCandidate candidate, long idleEnergy, long idleMagic)
        {
            var a = candidate.Activities;
            if (a == null) return;
            var c = Character;
            if (!candidate.Feasible || !candidate.Score.HasValue || a.AT == null || a.AT.Length != 5 || a.AT.Any(x => x < 0)
                || new[] { a.TimeMachineEnergy, a.TimeMachineMagic, a.WandoosEnergy, a.WandoosMagic, a.AugmentEnergy, a.UpgradeEnergy }.Any(x => x < 0)
                || a.EnergyTotal > idleEnergy || a.MagicTotal > idleMagic || idleEnergy < 0 || idleMagic < 0
                || (a.WandoosEnergy > 0 || a.WandoosMagic > 0) && (int)c.wandoos98.os != a.Os
                || a.AugmentId < -1 || a.AugmentId >= c.augments.augs.Length
                || a.AugmentId < 0 && (a.AugmentEnergy > 0 || a.UpgradeEnergy > 0)
                || c.advancedTraining.energy.Length < 5)
                throw new InvalidOperationException("Exact cached activity allocation is no longer applicable; preview again.");
        }
    }
}
