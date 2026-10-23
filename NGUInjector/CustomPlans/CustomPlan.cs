using System;
using SimpleJSON;

namespace NGUInjector.CustomPlans
{
    internal enum CustomStageFocus
    {
        None = 0,
        TimeMachine = 1,
        Augments = 2,
        Wandoos = 3,
        BloodMagic = 4,
        EnergyNgu = 5,
        MagicNgu = 6,
        AdvancedTraining = 7
    }

    internal enum CustomStageExecutionMode
    {
        Standard = 0,
        AutoAugmentsWandoos = 1
    }

    internal enum CustomStageCompletionMode
    {
        TimePercent = 0,
        AdvancedTrainingStats = 1
    }

    [Serializable]
    public class CustomPlan
    {
        // Source travels with the model/row, never with its position in an array.
        [NonSerialized] internal JSONObject SourceJson;
        [NonSerialized] internal string RuntimeRevision;
        public int Version = 17;
        public string Name = "default";
        public string ActivePresetId = string.Empty;
        public string[] PresetMigrationIssues = new string[0];

        public bool AutoRebirthEnabled = true;
        public bool AutoIronPillEnabled = true;
        // Unset overrides retain the legacy global settings.
        public bool? AutoBloodSpellsEnabled;
        public int? BloodSpaghettiGoalPercent;
        public int? BloodCounterfeitGoalPercent;
        public int? IronPillSaveSeconds;
        public double? IronPillMinWorth;
        public bool TimeTriggerEnabled = true;
        public int TimeTriggerSeconds = 3600;
        public bool NumberTriggerEnabled;
        public double NumberMultiplierTarget = 100.0;
        // Retired trigger fields remain serialized for existing plans.
        public bool NumberClimbEnabled;
        public int NumberClimbMinSeconds = 480;
        public double NumberClimbMultiplierTarget = 100.0;
        public int TriggerMode;

        public bool StageSchedulerEnabled = true;
        public int StageDurationSeconds = 1800;
        public int StageLoopStartIndex = 5;
        public CustomPlanStage[] Stages = DefaultNumberClimbStages();

        public bool AutoActivateFruits = true;

        public int GearGoal;
        public int FightScoreMode;
        public int ActiveGoalPresetIndex;
        public CustomGoalLoadoutPreset[] GoalPresets =
        {
            new CustomGoalLoadoutPreset {Name = "Default", GearGoal = 0}
        };
        public CustomLoadoutPreset[] LoadoutPresets = new CustomLoadoutPreset[0];

        public bool PrepRequireMaxedFruit;

        public CustomRebirthHistoryEntry[] RebirthHistory = new CustomRebirthHistoryEntry[0];

        internal static CustomPlanStage[] DefaultNumberClimbStages()
        {
            return new[]
            {
                new CustomPlanStage
                {
                    Name = "Time Machine opener",
                    Percent = 16.667,
                    EnergyFocus = (int) CustomStageFocus.TimeMachine,
                    MagicFocus = (int) CustomStageFocus.TimeMachine,
                    GearGoal = (int) Managers.GearOptimizationGoal.TimeMachine
                },
                new CustomPlanStage
                {
                    Name = "Augments + Blood",
                    Percent = 10.0,
                    EnergyFocus = (int) CustomStageFocus.Augments,
                    MagicFocus = (int) CustomStageFocus.BloodMagic,
                    GearGoal = (int) Managers.GearOptimizationGoal.Augments
                },
                new CustomPlanStage
                {
                    Name = "Time Machine retry",
                    Percent = 10.0,
                    EnergyFocus = (int) CustomStageFocus.TimeMachine,
                    MagicFocus = (int) CustomStageFocus.TimeMachine,
                    GearGoal = (int) Managers.GearOptimizationGoal.TimeMachine
                },
                new CustomPlanStage
                {
                    Name = "Augments top-up",
                    Percent = 3.333,
                    EnergyFocus = (int) CustomStageFocus.Augments,
                    MagicFocus = (int) CustomStageFocus.BloodMagic,
                    GearGoal = (int) Managers.GearOptimizationGoal.Augments
                },
                new CustomPlanStage
                {
                    Name = "Wandoos push",
                    Percent = 10.0,
                    EnergyFocus = (int) CustomStageFocus.Wandoos,
                    MagicFocus = (int) CustomStageFocus.Wandoos,
                    GearGoal = (int) Managers.GearOptimizationGoal.Fight
                },
                new CustomPlanStage
                {
                    Name = "Time Machine long",
                    Percent = 16.667,
                    EnergyFocus = (int) CustomStageFocus.TimeMachine,
                    MagicFocus = (int) CustomStageFocus.TimeMachine,
                    GearGoal = (int) Managers.GearOptimizationGoal.TimeMachine
                },
                new CustomPlanStage
                {
                    Name = "Augments + Blood long",
                    Percent = 16.667,
                    EnergyFocus = (int) CustomStageFocus.Augments,
                    MagicFocus = (int) CustomStageFocus.BloodMagic,
                    GearGoal = (int) Managers.GearOptimizationGoal.Augments
                },
                new CustomPlanStage
                {
                    Name = "Wandoos long",
                    Percent = 16.667,
                    EnergyFocus = (int) CustomStageFocus.Wandoos,
                    MagicFocus = (int) CustomStageFocus.Wandoos,
                    GearGoal = (int) Managers.GearOptimizationGoal.Fight
                }
            };
        }

        internal static CustomPlan TwentyFourHourPlan()
        {
            return LongNguPlan("24hr", true, 86400);
        }

        internal static CustomPlan InfinitePlan()
        {
            return LongNguPlan("infinite", false, 86400);
        }

        internal static CustomPlan OneHourThreeMinuteRebirthPlan()
        {
            return new CustomPlan
            {
                Name = "1h-3m-rebirth",
                AutoRebirthEnabled = true,
                TimeTriggerEnabled = true,
                TimeTriggerSeconds = 3600,
                NumberTriggerEnabled = true,
                NumberMultiplierTarget = 1.0,
                NumberClimbEnabled = false,
                NumberClimbMinSeconds = 0,
                NumberClimbMultiplierTarget = 1.0,
                TriggerMode = 1,
                StageSchedulerEnabled = true,
                StageDurationSeconds = 3600,
                StageLoopStartIndex = -1,
                GearGoal = (int) Managers.GearOptimizationGoal.Augments,
                GoalPresets = new[]
                {
                    new CustomGoalLoadoutPreset {Name = "Default", GearGoal = (int) Managers.GearOptimizationGoal.Augments}
                },
                Stages = OneHourThreeMinuteRebirthStages()
            };
        }

        private static CustomPlan LongNguPlan(string name, bool rebirthAtTime, int durationSeconds)
        {
            return new CustomPlan
            {
                Name = name,
                AutoRebirthEnabled = rebirthAtTime,
                TimeTriggerEnabled = rebirthAtTime,
                TimeTriggerSeconds = durationSeconds,
                NumberTriggerEnabled = true,
                NumberMultiplierTarget = 1.0,
                NumberClimbEnabled = false,
                NumberClimbMinSeconds = 0,
                NumberClimbMultiplierTarget = 1.0,
                TriggerMode = 1,
                StageSchedulerEnabled = true,
                StageDurationSeconds = durationSeconds,
                StageLoopStartIndex = 4,
                GearGoal = (int) Managers.GearOptimizationGoal.EnergyNgu,
                GoalPresets = new[]
                {
                    new CustomGoalLoadoutPreset {Name = "Default", GearGoal = (int) Managers.GearOptimizationGoal.EnergyNgu}
                },
                Stages = LongNguStages()
            };
        }

        private static CustomPlanStage[] LongNguStages()
        {
            return new[]
            {
                new CustomPlanStage
                {
                    Name = "Time Machine opener",
                    Percent = 4.1667,
                    EnergyFocus = (int) CustomStageFocus.TimeMachine,
                    MagicFocus = (int) CustomStageFocus.TimeMachine,
                    GearGoal = (int) Managers.GearOptimizationGoal.TimeMachine
                },
                new CustomPlanStage
                {
                    Name = "Augments + Blood",
                    Percent = 2.0833,
                    EnergyFocus = (int) CustomStageFocus.Augments,
                    MagicFocus = (int) CustomStageFocus.BloodMagic,
                    GearGoal = (int) Managers.GearOptimizationGoal.Augments
                },
                new CustomPlanStage
                {
                    Name = "Wandoos",
                    Percent = 2.0833,
                    EnergyFocus = (int) CustomStageFocus.Wandoos,
                    MagicFocus = (int) CustomStageFocus.Wandoos,
                    GearGoal = (int) Managers.GearOptimizationGoal.Wandoos
                },
                new CustomPlanStage
                {
                    Name = "Time Machine long",
                    Percent = 12.5,
                    EnergyFocus = (int) CustomStageFocus.TimeMachine,
                    MagicFocus = (int) CustomStageFocus.TimeMachine,
                    GearGoal = (int) Managers.GearOptimizationGoal.TimeMachine
                },
                new CustomPlanStage
                {
                    Name = "NGU sustain",
                    Percent = 79.1667,
                    EnergyFocus = (int) CustomStageFocus.EnergyNgu,
                    MagicFocus = (int) CustomStageFocus.MagicNgu,
                    GearGoal = (int) Managers.GearOptimizationGoal.EnergyNgu
                }
            };
        }

        private static CustomPlanStage[] OneHourThreeMinuteRebirthStages()
        {
            return new[]
            {
                new CustomPlanStage
                {
                    Name = "Time Machine opener",
                    Percent = 16.6667,
                    EnergyFocus = (int) CustomStageFocus.TimeMachine,
                    MagicFocus = (int) CustomStageFocus.TimeMachine,
                    GearGoal = (int) Managers.GearOptimizationGoal.TimeMachine
                },
                new CustomPlanStage
                {
                    Name = "Augments + Blood ramp",
                    Percent = 33.3333,
                    EnergyFocus = (int) CustomStageFocus.Augments,
                    MagicFocus = (int) CustomStageFocus.BloodMagic,
                    GearGoal = (int) Managers.GearOptimizationGoal.Augments
                },
                new CustomPlanStage
                {
                    Name = "Time Machine long",
                    Percent = 31.6667,
                    EnergyFocus = (int) CustomStageFocus.TimeMachine,
                    MagicFocus = (int) CustomStageFocus.TimeMachine,
                    GearGoal = (int) Managers.GearOptimizationGoal.TimeMachine
                },
                new CustomPlanStage
                {
                    Name = "Wandoos cap push",
                    Percent = 13.3333,
                    EnergyFocus = (int) CustomStageFocus.Wandoos,
                    MagicFocus = (int) CustomStageFocus.Wandoos,
                    GearGoal = (int) Managers.GearOptimizationGoal.Wandoos
                },
                new CustomPlanStage
                {
                    Name = "Final Augments + Blood",
                    Percent = 5.0,
                    EnergyFocus = (int) CustomStageFocus.Augments,
                    MagicFocus = (int) CustomStageFocus.BloodMagic,
                    GearGoal = (int) Managers.GearOptimizationGoal.Augments
                }
            };
        }
    }

    [Serializable]
    public class CustomGoalLoadoutPreset
    {
        [NonSerialized] internal JSONObject SourceJson;
        public string Name = "Default";
        public int GearGoal;
    }

    [Serializable]
    public class CustomPlanStage
    {
        internal const int MaximumAutomaticDurationSeconds = 86400;
        [NonSerialized] internal JSONObject SourceJson;
        public string Name = "Stage";
        // @active inherits the plan selection; empty retains legacy inline allocation.
        public string PresetId = string.Empty;
        public double Percent = 100.0;
        public int DurationSeconds = 0;
        public string DurationUnit = "%";
        public int EnergyFocus;
        public int MagicFocus;
        public int ExecutionMode = (int) CustomStageExecutionMode.Standard;
        // Empty priorities use the focus preset's default chain.
        public string[] EnergyPriorities;
        public string[] MagicPriorities;
        public int BlackBarLookaheadSeconds = 5;
        public int GearGoal = -1;
        public double AdventureDynamicWeight = 0d;
        public bool FightOnEntry;
        public int CompletionMode = (int) CustomStageCompletionMode.TimePercent;
        public long TargetAdvancedTrainingPower;
        public long TargetAdvancedTrainingToughness;
    }

    [Serializable]
    public class CustomRebirthHistoryEntry
    {
        [NonSerialized] internal JSONObject SourceJson;
        public string TimestampLocal;
        public double RebirthSeconds;
        public double NumberBefore;
        public double NumberAfter;
        public double NumberMultiplier;
        public int BossId;
        public string Reason;
    }
}
