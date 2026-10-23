using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using NGUInjector.Managers;

internal static partial class Program
{
    private static int assertions;
    private static int Main()
    {
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            if (arguments.Length == 3 && arguments[1] == "--recorded-combined") return AnalyzeRecordedCombined(arguments[2]);
            if (arguments.Length == 2 && arguments[1] == "--varying-performance")
            {
                CheckLongVaryingPerformance();
                Console.WriteLine("Long varying-rate checks passed: " + assertions + " assertions.");
                return 0;
            }
            if (arguments.Length != 1) throw new ArgumentException("Expected --varying-performance or --recorded-combined <directory>.");
            CheckResourceFoundation();
            CheckWandoos();
            CheckNativeProgressionMath();
            CheckAugmentFloatTiming();
            CheckAdaptiveTiming();
            CheckBossClamp();
            CheckAt();
            CheckAugments();
            CheckOsAndSupport();
            CheckFocusEquivalents();
            CheckBackgroundAndGold();
            CheckProfileGold();
            CheckAtBeforeWandoos();
            CheckAlternativeDeduplication();
            CheckPolicyPools();
            CheckScheduledContinuation();
            CheckRequiredPhaseProfiles();
            CheckMissingAtPhaseProfile();
            CheckNguOnlyPhaseProfile();
            CheckMissingRequiredPhaseProfiles();
            CheckDefaultCurrentPhaseEligibility();
            CheckDefaultBaselineTie();
            DifferentialWandoos();
            DifferentialAt();
            DifferentialAugments();
            CheckSearchAndSafety();
            CheckPerformance();
            Console.WriteLine("Combat prep checks passed: " + assertions + " assertions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static CombatPrepSnapshot Snapshot(long energy = 100, long magic = 100)
    {
        return new CombatPrepSnapshot {
            Current = new CombatPrepProfile { Key = "current", SupportKey = "frozen", EnergyTotal = energy, EnergyCap = energy,
                MagicTotal = magic, MagicCap = magic, EnergyPower = 1, MagicPower = 1, AtSpeed = 1,
                WandoosEnergySpeedWithoutAt = 1, WandoosMagicSpeedWithoutAt = 1,
                ReferenceEnergyPerSecond = energy, ReferenceMagicPerSecond = magic },
            DifficultyKey = "normal", CurrentOsId = 0, WandoosAvailable = true,
            AtEnergy = new CombatPrepAtLane { Unlocked = true, BaseTime = 1, LevelFactor = 1 },
            AtMagic = new CombatPrepAtLane { Unlocked = true, BaseTime = 1, LevelFactor = 1 },
            OperatingSystems = new[] { Os(0, "normal") }, Gold = 100000, BaselineBossAttack = 10, BaselineBossDefense = 20
        };
    }

    private static void CheckScheduledContinuation()
    {
        var s = Snapshot(); s.Augments = new[] { Tier() };
        s.PaidAtForbidden = true;
        var result = CombatPrepPlanner.Plan(s, 10);
        Equal(true, result.Success, "scheduled continuation plans with current inputs");
        Equal(false, result.Alternatives.Any(x => x.Phases.Any(p => p.Kind == "AT")), "no paid AT candidates after W starts");
        Throws(() => Project(s, Phase("AT", 1), Phase("Wandoos", 1)), "explicit projection respects continuation AT prohibition");
        s.AugmentsCompleted = true;
        result = CombatPrepPlanner.Plan(s, 10);
        Equal(true, result.Success, "completed preparation leaves W/NGU continuations");
        Equal(false, result.Alternatives.Any(x => x.Phases.Any(p => p.Kind == "Augments" || p.Kind == "AT")), "completed phases never reappear");
        Throws(() => Project(s, Phase("Augments", 1)), "explicit projection rejects completed augments");
        s.PaidAtForbidden = false; s.AugmentsCompleted = false;
        Equal(true, CombatPrepPlanner.Plan(s, 10).Alternatives.Any(x => x.Phases.Any(p => p.Kind == "AT")), "manual/default search remains unchanged");
    }

    private static void CheckRequiredPhaseProfiles()
    {
        var s = Snapshot(1000, 1000); s.RequirePhaseProfiles = true;
        s.Augments = new[] { Tier() };
        s.OperatingSystems = Enumerable.Range(0, 3).Select(i => Os(i, "normal")).ToArray();
        s.Profiles = new[] { "Augments", "AT", "Wandoos", "NGU" }.SelectMany(goal => Enumerable.Range(0, 2).Select(i => {
            var p = s.Current.Copy(); p.Key = "weighted-" + goal + i; p.Goal = goal;
            p.OwnedGearRefs = new[] { p.Key + "-gear" };
            return p;
        })).ToArray();
        // Make the unscored Current choice attractive if it accidentally escapes eligibility.
        s.Current.EnergyPower = 10000; s.Current.AtSpeed = 100;
        s.Current.WandoosEnergySpeedWithoutAt = s.Current.WandoosMagicSpeedWithoutAt = 100;

        var result = CombatPrepPlanner.Plan(s, 20);
        True(result.Success, "required weighted phase-profile search succeeds: " + result.Error);
        True(result.EvaluatedCandidates > 500 && result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates,
            "eight weighted profiles across three OSes retain a bounded nontrivial search");
        foreach (var family in new[] { "FullAugments", "WandoosNoAT", "ATThenWandoos", "AugmentsThenWandoos",
            "AugmentsThenNGUBackground", "AugmentsATWandoos", "ATThenAugmentsWandoos", "ATThenAugmentsNGUBackground", "BackgroundOnly" })
            True(result.Alternatives.Any(x => x.Family == family), "required phase profiles retain executable family " + family);
        True(result.Recommendation.Phases.Length > 0, "weighted recommendation is executable rather than baseline reporting");
        foreach (var candidate in result.Alternatives.Where(x => x.Phases.Length > 0))
        {
            foreach (var phase in candidate.Phases)
            {
                Equal(false, phase.GearProfileKey == s.Current.Key, "Current cannot escape into weighted " + candidate.Family);
                var profile = s.Profiles.Single(x => x.Key == phase.GearProfileKey);
                Equal(phase.Kind == "Background" ? "NGU" : phase.Kind, profile.Goal,
                    "each weighted phase uses a profile scored for its own goal");
            }
            Near(20, candidate.Phases.Sum(x => x.DurationSeconds), "weighted phases use the original bounded horizon");
            Near(candidate.TotalBonus, CombatPrepPlanner.Project(s, candidate.OsId, candidate.Phases).TotalBonus,
                "weighted executable policy replays its bonus");
        }
    }

    private static void CheckMissingAtPhaseProfile()
    {
        var s = Snapshot(); s.RequirePhaseProfiles = true;
        s.Augments = new[] { Tier() };
        s.Profiles = new[] { "Augments", "Wandoos", "NGU" }.Select(goal => {
            var p = s.Current.Copy(); p.Key = "weighted-" + goal; p.Goal = goal;
            p.OwnedGearRefs = new[] { p.Key + "-gear" };
            return p;
        }).ToArray();
        var paidFamilies = new[] { "ATThenWandoos", "AugmentsATWandoos", "ATThenAugmentsWandoos", "ATThenAugmentsNGUBackground" };

        var result = CombatPrepPlanner.Plan(s, 20);
        True(result.Success, "missing scored AT still permits other weighted phases: " + result.Error);
        foreach (var family in new[] { "FullAugments", "WandoosNoAT", "AugmentsThenWandoos", "AugmentsThenNGUBackground", "BackgroundOnly" })
            True(result.Alternatives.Any(x => x.Family == family), "missing AT preserves executable family " + family);
        foreach (var family in paidFamilies)
            Equal(false, result.Alternatives.Any(x => x.Family == family), "missing scored AT excludes paid refinement family " + family);
        Equal(false, result.Alternatives.Any(x => x.Phases.Any(p => p.Kind == "AT")), "missing scored AT excludes every paid AT phase");
        True(result.EvaluatedCandidates > 0 && result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates,
            "missing-AT search remains bounded");

        // The same unlocked lanes and augment/W seeds can generate every paid family when Current is legal.
        s.RequirePhaseProfiles = false;
        var control = CombatPrepPlanner.Plan(s, 20);
        True(control.Success, "missing-AT control can use Current: " + control.Error);
        foreach (var family in paidFamilies)
            True(control.Alternatives.Any(x => x.Family == family && x.Phases.Any(p => p.Kind == "AT" && p.GearProfileKey == s.Current.Key)),
                "missing-AT fixture exercises the Current fallback for " + family);
    }

    private static void CheckNguOnlyPhaseProfile()
    {
        var s = Snapshot(); s.RequirePhaseProfiles = true;
        var ngu = s.Current.Copy(); ngu.Key = "weighted-ngu"; ngu.Goal = "NGU";
        ngu.WandoosEnergySpeedWithoutAt = ngu.WandoosMagicSpeedWithoutAt = 0;
        s.Profiles = new[] { ngu };

        var result = CombatPrepPlanner.Plan(s, 10);
        True(result.Success, "NGU-only weighted profile remains executable: " + result.Error);
        Equal(1, result.EvaluatedCandidates, "NGU-only eligibility evaluates exactly one executable policy");
        Equal(2, result.Alternatives.Length, "NGU-only result retains executable policy plus reporting baseline");
        True(result.Alternatives.Contains(result.Baseline), "equal-bonus baseline is retained for reporting");
        Equal(0, result.Baseline.Phases.Length, "reporting baseline has no executable phases");
        Equal("BackgroundOnly", result.Recommendation.Family, "equal bonus selects weighted NGU instead of baseline");
        Near(result.Baseline.TotalBonus, result.Recommendation.TotalBonus, "NGU-only recommendation has unchanged combat bonus");
        Near(1, result.Recommendation.Gain, "NGU-only forecast invents no combat growth");
        Equal(1, result.Recommendation.Phases.Length, "NGU-only recommendation contains one executable phase");
        var phase = result.Recommendation.Phases[0];
        Equal("Background", phase.Kind, "NGU-only policy uses the background terminal phase");
        Equal(ngu.Key, phase.GearProfileKey, "NGU-only policy never substitutes productive Current gear");
        Near(10, phase.DurationSeconds, "equal-bonus executable phase receives the full horizon");
        Near(1000, result.Recommendation.NguEnergyResourceSeconds, "NGU-only phase allocates the full unused energy budget to NGU");
        Near(1000, result.Recommendation.NguMagicResourceSeconds, "NGU-only phase allocates the full unused magic budget to NGU");
        Near(result.Recommendation.TotalBonus, Project(s, result.Recommendation.Phases).TotalBonus, "equal-bonus recommendation is replayable");
    }

    private static void CheckMissingRequiredPhaseProfiles()
    {
        var s = Snapshot(); s.RequirePhaseProfiles = true; s.Augments = new[] { Tier() };
        var result = CombatPrepPlanner.Plan(s, 20);
        Equal(false, result.Success, "Current alone cannot satisfy required scored phase profiles");
        Equal("No executable scored phase profiles are available.", result.Error, "missing phase profiles fail for eligibility, not invalid input");
        Equal<CombatPrepAlternative>(null, result.Recommendation, "missing phase profiles never recommend baseline reporting");
        Equal(0, result.Alternatives.Length, "failed eligibility exposes no executable alternatives");
        Equal(0, result.EvaluatedCandidates, "missing phase profiles evaluate no Current policies");
    }

    private static void CheckDefaultCurrentPhaseEligibility()
    {
        var s = Snapshot(); s.Augments = new[] { Tier() };
        Equal(false, s.RequirePhaseProfiles, "phase-profile requirement defaults to false");
        var result = CombatPrepPlanner.Plan(s, 20);
        True(result.Success, "default search still plans without scored profiles: " + result.Error);
        True(result.Recommendation.Phases.Length > 0, "default Current-only search still recommends training");
        True(result.Recommendation.TotalBonus > result.Baseline.TotalBonus, "default Current-only search retains combat growth");
        foreach (var kind in new[] { "Augments", "AT", "Wandoos", "Background" })
            True(result.Alternatives.Any(x => x.Phases.Any(p => p.Kind == kind && p.GearProfileKey == s.Current.Key)),
                "default Current remains eligible for " + kind);
        True(result.EvaluatedCandidates > 0 && result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates,
            "default Current-only search retains bounded evaluations");
        s.RequirePhaseProfiles = false;
        Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(CombatPrepPlanner.Plan(s, 20)),
            "default false and explicit false produce identical planner results");
    }

    private static void CheckDefaultBaselineTie()
    {
        var s = Snapshot();
        s.Current.WandoosEnergySpeedWithoutAt = s.Current.WandoosMagicSpeedWithoutAt = 0;
        var result = CombatPrepPlanner.Plan(s, 10);
        True(result.Success, "default search still permits unchanged-bonus forecasts: " + result.Error);
        True(result.Alternatives.Any(x => x.Phases.Length > 0), "default tie fixture includes executable alternatives");
        True(result.Recommendation == result.Baseline, "default false preserves baseline selection on an unchanged-bonus tie");
        Equal("Baseline", result.Recommendation.Family, "default false keeps the historical baseline recommendation");
    }

    private static CombatPrepOs Os(int id, string difficulty)
    {
        // MEH's independently transcribed actual formula: (1+E/5)*(1+2*M).
        return new CombatPrepOs { Id = id, Name = "OS " + id, DifficultyKey = difficulty, Unlocked = true,
            EnergyBaseTime = 100, MagicBaseTime = 100, BonusOffset = 0, BonusCoefficient = .4,
            EnergyLevelOffset = 5, MagicLevelOffset = .5, EnergyExponent = 1, MagicExponent = 1 };
    }

    private static CombatPrepAugmentTier Tier(int id = 0)
    {
        return new CombatPrepAugmentTier { Id = id, Name = "Aug " + id, BaseBoost = 2, TierExponent = 1,
            Augment = new CombatPrepAugmentLane { Unlocked = true, ProgressCoefficient = .01, CostCoefficient = 10 },
            Upgrade = new CombatPrepAugmentLane { Unlocked = true, ProgressCoefficient = .01, CostCoefficient = 5, CostExponent = 2 } };
    }

    private static CombatPrepPhase Phase(string kind, double seconds, double weight = .5)
    {
        return new CombatPrepPhase { Kind = kind, GearProfileKey = "current", GearGoal = "Current", DurationSeconds = seconds,
            AtEnergyWeight = weight, AugmentTierId = 0 };
    }

    private static CombatPrepAlternative Project(CombatPrepSnapshot s, params CombatPrepPhase[] phases)
    {
        return CombatPrepPlanner.Project(s, s.CurrentOsId, phases);
    }

    private static void CheckWandoos()
    {
        var s = Snapshot(1, 1);
        s.OperatingSystems[0].EnergyBaseTime = 1;
        s.OperatingSystems[0].MagicBaseTime = 1;
        s.Current.WandoosEnergySpeedWithoutAt = .6;
        s.Current.WandoosMagicSpeedWithoutAt = 2;
        s.WandoosEnergy.Progress = .75;
        var r = Project(s, Phase("Wandoos", .1));
        Equal(3L, r.WandoosEnergyLevel, "W overflow discarded, first partial respected");
        Near(0, r.WandoosEnergyProgress, "W overflow is not carried into next bar");
        Equal(5L, r.WandoosMagicLevel, "W never exceeds one level per tick at q > 1");
        Near((1 + 3d / 5) * 11, r.EndWandoosBonus, "actual MEH product, not sqrt levels");
        Near(r.TotalBonus, r.EndAugmentTotal * r.EndWandoosBonus, "objective is actual augment times W bonus");
        Near(r.Gain * 10, r.BossAttack, "fixed final attack baseline");
        Near(r.Gain * 20, r.BossDefense, "fixed final defense baseline");

        s.Current.WandoosEnergySpeedWithoutAt = .4;
        s.WandoosEnergy.Progress = .5;
        r = Project(s, Phase("Wandoos", .06));
        Equal(1L, r.WandoosEnergyLevel, "W first partial takes ceil remaining/q");
        Near(.4, r.WandoosEnergyProgress, "W retains final partial progress");
        s.Current.WandoosEnergySpeedWithoutAt = 1e-9;
        r = Project(s, Phase("Wandoos", 100));
        Equal(0L, r.WandoosEnergyLevel, "tiny progress <=1e-9 yields no levels");
        Near(.5, r.WandoosEnergyProgress, "tiny rate preserves saved progress");
        Near(0, r.EnergyResourceSeconds, "tiny unproductive lane is not funded");

        s = Snapshot(1, 0);
        s.OperatingSystems[0].EnergyBaseTime = 1;
        s.Current.WandoosEnergySpeedWithoutAt = .25;
        s.Current.EarlyEnergyBootup = 1; s.Current.LateEnergyBootup = 2; s.Current.BootupUntilSeconds = .04;
        r = Project(s, Phase("Wandoos", .08));
        Equal(1L, r.WandoosEnergyLevel, "captured early/late bootup boundary");
        Near(.5, r.WandoosEnergyProgress, "bootup split preserves progress");
        s.AtEnergy.Unlocked = false; s.AtMagic.Unlocked = false;
        r = Project(s, Phase("AT", .5), Phase("Wandoos", .04));
        Equal(1L, r.WandoosEnergyLevel, "bootup boundary uses total prep time, not active W time");
        Near(0, r.WandoosEnergyProgress, "no W energy or magic during preceding AT");

        s = Snapshot(); var os = s.OperatingSystems[0];
        s.WandoosEnergy.Level = 100; s.WandoosMagic.Level = 25;
        os.EnergyLevelOffset = 100; os.MagicLevelOffset = 25; os.EnergyExponent = .8; os.MagicExponent = .8;
        os.BonusCoefficient = Math.Pow(100, -.8) * Math.Pow(25, -.8);
        s.CurrentWandoosBonus = Math.Pow(4, .8);
        r = Project(s, Phase("NGU", 0));
        Near(Math.Pow(4, .8), r.EndWandoosBonus, "98 captured formula parity");
        s.WandoosEnergy.Level = 2; s.WandoosMagic.Level = 3;
        os.EnergyLevelOffset = 1d / 6; os.MagicLevelOffset = 1d / 40;
        os.EnergyExponent = os.MagicExponent = 1.0499999523162842;
        os.BonusCoefficient = Math.Pow(240, 1.0499999523162842);
        s.CurrentWandoosBonus = Math.Pow(13 * 121, 1.0499999523162842);
        r = Project(s, Phase("NGU", 0));
        Near(s.CurrentWandoosBonus, r.EndWandoosBonus, "XL captured formula parity");
        s.CurrentWandoosBonus *= 2;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "W formula parity mismatch rejects plan");
    }

    private static void CheckBossClamp()
    {
        var s = Snapshot(0, 1); var os = s.OperatingSystems[0];
        os.MagicBaseTime = 1; os.BonusOffset = 1; os.BonusCoefficient = 1;
        os.EnergyLevelOffset = 0; os.EnergyExponent = 0; os.MagicLevelOffset = 0; os.MagicExponent = 1;
        s.BaselineBossAttack = 1; s.BaselineBossDefense = 1;
        var baseline = Project(s, Phase("NGU", 0));
        var trained = Project(s, Phase("Wandoos", .42));
        Near(22, trained.Gain, "boss clamp regression has exactly 22x actual A/W gain");
        Near(100, baseline.BossAttack, "raw boss attack one clamps to 100 at baseline");
        Near(100, baseline.BossDefense, "raw boss defense one clamps to 100 at baseline");
        Near(100, trained.BossAttack, "raw attack one times 22 still clamps to 100, not 2200");
        Near(100, trained.BossDefense, "raw defense one times 22 still clamps to 100, not 2200");
        s.BaselineBossAttack = 5; s.BaselineBossDefense = 1000;
        trained = Project(s, Phase("Wandoos", .42));
        Near(110, trained.BossAttack, "raw boss attack can cross the floor after training");
        Near(22000, trained.BossDefense, "above-floor raw boss defense scales normally");
    }

    private static void CheckAt()
    {
        var s = Snapshot(100, 0);
        Near(CombatPrepPlanner.AllocationRefreshSeconds, s.AllocationRefreshSeconds, "snapshot defaults to the shared executor cadence constant");
        s.Current.EnergyPower = 4; s.Current.AtSpeed = 25;
        var r = Project(s, Phase("AT", .1, 0));
        Equal(0L, r.AtEnergyLevel, "zero weight excludes AT energy lane");
        Equal(2L, r.AtMagicLevel, "AT magic consumes held energy with zero magic pool");
        Near(.1, r.EnergyResourceSeconds, "AT holds cap one throughout the first .1 seconds");
        Near(0, r.MagicResourceSeconds, "AT never consumes magic");
        Equal(0L, r.WandoosEnergyLevel, "no W energy during AT");
        Equal(0L, r.WandoosMagicLevel, "no W magic during AT");
        r = Project(s, Phase("AT", .1, 1));
        Equal(2L, r.AtEnergyLevel, "weight one excludes AT magic lane");
        Equal(0L, r.AtMagicLevel, "weight one has no implicit equal sharing");
        r = Project(s, Phase("AT", .5, 1));
        Equal(6L, r.AtEnergyLevel, "25 held ticks at cap one produce six AT levels, not 25");
        Near(4d / 7, r.AtEnergyProgress, "held AT allocation retains the seventh level's partial progress");
        Near(.5, r.EnergyResourceSeconds, "cap is not increased after each AT completion");
        s.AtEnergy.TargetLevel = 2;
        r = Project(s, Phase("AT", 1, 1));
        Equal(6L, r.AtEnergyLevel, "AT target is observed at refresh, not immediately on completion");
        Near(.5, r.EnergyResourceSeconds, "completed target returns held energy only on the next refresh");
        s.WandoosMagic.Level = 2; s.WandoosMagic.Progress = .5; s.WandoosMagicAllocation = 100;
        s.CurrentWandoosBonus = 5;
        r = Project(s, Phase("AT", .1));
        Equal(2L, r.WandoosMagicLevel, "AT preserves pre-existing W levels without training them");
        Near(.5, r.WandoosMagicProgress, "AT preserves pre-existing W partial progress");
        Equal(0L, r.WandoosMagicAllocation, "AT recalls existing magic W dump");

        s = Snapshot(10, 10);
        s.AtEnergy.LevelFactor = 0;
        s.OperatingSystems[0].EnergyBaseTime = 10;
        s.OperatingSystems[0].MagicBaseTime = 1000;
        var unequal = Project(s, Phase("AT", 2, 0), Phase("Wandoos", 20));
        var equal = Project(s, Phase("AT", 2, .5), Phase("Wandoos", 20));
        var opposite = Project(s, Phase("AT", 2, 1), Phase("Wandoos", 20));
        True(unequal.TotalBonus > equal.TotalBonus && equal.TotalBonus > opposite.TotalBonus,
            "asymmetric useful AT lane makes endpoint split strictly best");
        var noAt = Project(s, Phase("Wandoos", 22));
        True(noAt.AtEnergyLevel == 0 && noAt.AtMagicLevel == 0, "zero AT baseline remains available");
        var forbidden = Phase("AT", .1); forbidden.BackgroundMagicWandoos = true;
        Throws(() => Project(s, forbidden), "explicit background magic during AT is rejected");

        s = Snapshot(100, 100); s.Current.EnergyPower = 4; s.Current.AtSpeed = 25;
        s.WandoosMagic.Progress = .5;
        var deadline = Project(s, Phase("AT", .1, 1), Phase("Wandoos", .4));
        Equal(6L, deadline.AtEnergyLevel, "early AT target completion cannot hand off before the next refresh");
        Equal(0L, deadline.WandoosMagicLevel, "no W starts when the next refresh is the global deadline");
        Near(.5, deadline.WandoosMagicProgress, "deadline-truncated AT preserves existing W partial");
        Equal(1, deadline.Phases.Length, "zero-remaining-budget W is not entered");
        Near(.5, deadline.Phases[0].DurationSeconds, "AT absorbs refresh lateness without extending the deadline");

        s.Augments = new[] { Tier() };
        var rounded = Project(s, Phase("Augments", .1), Phase("AT", .5), Phase("Wandoos", .6));
        Near(.5, rounded.Phases[0].DurationSeconds, "augment handoff rounds to its first allocation refresh");
        Near(.5, rounded.Phases[1].DurationSeconds, "AT consumes only its remaining absolute scheduled budget");
        Near(.2, rounded.Phases[2].DurationSeconds, "W receives only the original horizon's remainder");
        Near(1.2, rounded.Phases.Sum(x => x.DurationSeconds), "rounded phases cannot extend the requested global horizon");

        s.AllocationRefreshSeconds = .26;
        var custom = Project(s, Phase("AT", .1, 1), Phase("Wandoos", .4));
        Near(.26, custom.Phases[0].DurationSeconds, "captured non-default whole-tick cadence controls handoff");
        Near(.24, custom.Phases[1].DurationSeconds, "custom cadence retains the absolute global horizon");
        s.AllocationRefreshSeconds = .001;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "sub-tick allocation cadence rejected");
        s.AllocationRefreshSeconds = .51;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "non-whole-tick allocation cadence rejected");

        s = Snapshot(10, 10); s.AtEnergy.Level = 10;
        s.OperatingSystems[0].EnergyBaseTime = 100000; s.OperatingSystems[0].MagicBaseTime = 100000;
        var splits = new[] { 0d, .25, .5, .75, 1 }.Select(weight =>
            Project(s, Phase("AT", 10, weight), Phase("Wandoos", 50000))).ToArray();
        True(splits[1].TotalBonus > splits[0].TotalBonus && splits[1].TotalBonus > splits[2].TotalBonus &&
            splits[1].TotalBonus > splits[3].TotalBonus && splits[1].TotalBonus > splits[4].TotalBonus,
            "25/75 is strictly better than equal sharing and both endpoints with asymmetric initial AT");
    }

    private static void CheckAugments()
    {
        var s = Snapshot(1, 0); var tier = Tier(); s.Augments = new[] { tier };
        tier.Augment.ProgressCoefficient = .25; tier.Upgrade.Unlocked = false;
        s.Gold = 10;
        var r = Project(s, Phase("Augments", .02));
        Equal(0L, r.Augments[0].AugmentLevel, "pay before completion");
        Near(.25, r.Augments[0].AugmentProgress, "new bar progress");
        True(r.Augments[0].AugmentPaid, "new partial bar paid flag");
        Near(10, r.GoldSpent, "gold charged at bar start"); Near(0, r.GoldRemaining, "gold removed immediately");
        tier.Augment.Progress = .75; tier.Augment.Paid = false; s.Gold = 0;
        r = Project(s, Phase("Augments", .02));
        Equal(1L, r.Augments[0].AugmentLevel, "partial bars infer already paid even with no gold");
        Near(0, r.GoldSpent, "partial bar is never charged twice");
        Near(3, r.EndAugmentTotal, "actual augment +1 formula");
        True(!r.Augments[0].AugmentPaid, "completion clears paid flag");
        tier.Augment.Progress = 0; tier.Augment.Paid = true;
        r = Project(s, Phase("Augments", .02));
        Near(.25, r.Augments[0].AugmentProgress, "explicit paid zero-progress bar is respected");
        tier.Augment.Paid = false; s.NetGoldPerSecond = 500;
        r = Project(s, Phase("Augments", .04));
        Near(0, r.Augments[0].AugmentProgress, "unfunded lane cannot acquire allocation between refreshes");
        Near(0, r.GoldSpent, "no spending on an unallocated lane before next refresh"); Near(20, r.GoldRemaining, "income accrues while waiting for refresh");
        r = Project(s, Phase("Augments", .52));
        Near(.25, r.Augments[0].AugmentProgress, "income-funded bar starts on the next refresh");
        Near(10, r.GoldSpent, "income-funded start charges once"); Near(250, r.GoldRemaining, "refresh-delayed income conservation");

        s = Snapshot(100, 0); tier = Tier(); s.Augments = new[] { tier };
        tier.Augment.ProgressCoefficient = 1; tier.Upgrade.Unlocked = false; s.Gold = 10;
        r = Project(s, Phase("Augments", 1));
        Equal(1L, r.Augments[0].AugmentLevel, "gold-starved next bar cannot complete under the held allocation");
        Near(.5, r.EnergyResourceSeconds, "gold-starved allocated energy is held until refresh, not returned at completion");
        Near(10, r.GoldSpent, "gold starvation never charges an unaffordable next bar");
        s.NetGoldPerSecond = 50;
        r = Project(s, Phase("Augments", .5));
        Equal(2L, r.Augments[0].AugmentLevel, "already allocated lane resumes between refreshes when income pays the next bar");
        Near(30, r.GoldSpent, "held allocated lane pays starts in game tick order");
        Near(5, r.GoldRemaining, "held lane income and paid starts conserve gold");

        s = Snapshot(1, 0); tier = Tier(); s.Augments = new[] { tier };
        tier.Augment.Unlocked = false; tier.Upgrade.Level = 2;
        tier.Upgrade.ProgressCoefficient = 1; tier.Upgrade.LevelExponent = 2;
        tier.Upgrade.CostCoefficient = 2.5; s.Gold = 22.5;
        r = Project(s, Phase("Augments", .02));
        Near(22.5, r.GoldSpent, "upgrade cost coefficient times (U+1)^2 includes captured discount");
        Near(1d / 9, r.Augments[0].UpgradeProgress, "generic lane level exponent is captured, not hardcoded");

        s = Snapshot(11, 5); tier = Tier(); s.Augments = new[] { tier };
        tier.Augment.CostCoefficient = 0; tier.Upgrade.CostCoefficient = 0;
        tier.Augment.ProgressCoefficient = 1; tier.Upgrade.ProgressCoefficient = .01;
        r = Project(s, Phase("Augments", .02));
        Equal(1L, r.Augments[0].AugmentLevel, "augment saturated cap gets only one energy");
        Near(.1, r.Augments[0].UpgradeProgress, "equal capped sharing redistributes ten energy to upgrade");
        Near(.22, r.EnergyResourceSeconds, "augment pair resource conservation");

        var backgroundSnapshot = Snapshot(11, 100); backgroundSnapshot.Augments = new[] { Tier() };
        var background = Phase("Augments", .1); background.BackgroundMagicWandoos = true;
        var withMagic = Project(backgroundSnapshot, background);
        Near(.15, withMagic.WandoosMagicProgress, "augment phase backgrounds only budgeted magic W");
        Equal(3L, withMagic.WandoosMagicAllocation, "augment background is not full magic");
        Equal(0L, withMagic.WandoosEnergyAllocation, "augment phase never backgrounds energy W");

        tier.Augment.Level = 2; tier.Upgrade.Level = 3; tier.BaseBoost = 4; tier.TierExponent = 2;
        var second = Tier(1); second.Augment.Level = 3; second.Upgrade.Level = 1; second.BaseBoost = 5;
        s.Augments = new[] { tier, second }; s.AugmentGlobalFactor = 2;
        s.CurrentAugmentTotal = (1 + 4 * 10 * 4 + 5 * 2 * 3) * 2;
        r = Project(s, Phase("NGU", 0));
        Near(382, r.EndAugmentTotal, "all tiers sum before fixed challenge/NGU global factor");
        Near(1, r.Gain, "baseline compares same total formula");
        s.CurrentAugmentTotal = 381;
        True(!CombatPrepPlanner.Plan(s).Success, "augment parity mismatch rejects misleading plan");
    }

    private static void CheckOsAndSupport()
    {
        var s = Snapshot(); var alternative = Os(1, "normal"); var foreign = Os(2, "evil");
        s.OperatingSystems = new[] { s.OperatingSystems[0], alternative, foreign };
        var phase = Phase("Wandoos", 1);
        var r = CombatPrepPlanner.Project(s, 1, new[] { phase });
        Equal(1, r.OsId, "fresh state allows unlocked OS");
        Throws(() => CombatPrepPlanner.Project(s, 2, new[] { phase }), "wrong difficulty OS cannot train");
        alternative.Unlocked = false;
        Throws(() => CombatPrepPlanner.Project(s, 1, new[] { phase }), "locked OS cannot train");
        alternative.Unlocked = true; s.WandoosEnergyAllocation = 1;
        Throws(() => CombatPrepPlanner.Project(s, 1, new[] { phase }), "existing dumps protect current OS even at level zero");
        s.WandoosEnergyAllocation = 0; s.WandoosMagic.Progress = .1;
        Throws(() => CombatPrepPlanner.Project(s, 1, new[] { phase }), "existing partial progress protects current OS");
        s.WandoosMagic.Progress = 0; s.WandoosEnergy.Level = 1; s.CurrentWandoosBonus = 1.2;
        Throws(() => CombatPrepPlanner.Project(s, 1, new[] { phase }), "existing levels protect current OS");
        s.WandoosEnergy.Level = 0; s.CurrentWandoosBonus = 1;
        alternative.EnergySpeedFactor = 2; alternative.EnergyBaseTime = 1000;
        r = CombatPrepPlanner.Project(s, 1, new[] { Phase("Wandoos", .1) });
        Equal(1L, r.WandoosEnergyLevel, "captured per-OS speed ratio and difficulty base both apply");

        var projected = new CombatPrepProfile { Key = "w", Goal = "Wandoos", SupportKey = "phase", EnergyTotal = 100,
            EnergyCap = 100, MagicTotal = 100, MagicCap = 100, EnergyPower = 1,
            WandoosEnergySpeedWithoutAt = 10, WandoosMagicSpeedWithoutAt = 10 };
        s.Profiles = new[] { projected };
        True(!CombatPrepPlanner.Plan(s, 1).Success, "frozen support rejects incompatible projection");
        s.PhaseSpecificSupport = true;
        True(CombatPrepPlanner.Plan(s, 1).Success, "captured phase-specific support is explicit opt-in");
        s.Current.WandoosEnergySpeedWithoutAt = .01; s.Current.WandoosMagicSpeedWithoutAt = .01;
        var planned = CombatPrepPlanner.Plan(s, 1);
        True(planned.Recommendation.Phases.Any(x => x.GearProfileKey == "w"), "phase-specific projected actual speeds affect recommendation");
        s.Profiles = new[] { new CombatPrepProfile { Key = "current", Goal = "NGU" } };
        True(!CombatPrepPlanner.Plan(s, 1).Success, "duplicate gear keys cannot poison local caches");
    }

    private static void DifferentialWandoos()
    {
        var random = new Random(1881);
        for (var i = 0; i < 150; i++)
        {
            var s = Snapshot(random.Next(1, 20), random.Next(1, 20));
            s.Current.WandoosEnergySpeedWithoutAt = random.Next(1, 30) / 8d;
            s.Current.WandoosMagicSpeedWithoutAt = random.Next(1, 30) / 8d;
            // Binary-exact rates isolate discrete semantics from repeated-addition rounding,
            // which is explicitly not claimed to reproduce Unity's float implementation.
            s.OperatingSystems[0].EnergyBaseTime = Math.Pow(2, random.Next(0, 8));
            s.OperatingSystems[0].MagicBaseTime = Math.Pow(2, random.Next(0, 8));
            s.WandoosEnergy.Progress = random.Next(8) / 8d; s.WandoosMagic.Progress = random.Next(8) / 8d;
            s.Current.BootupUntilSeconds = random.Next(0, 50) / 50d;
            s.Current.EarlyEnergyBootup = .25; s.Current.EarlyMagicBootup = .5;
            s.Current.LateEnergyBootup = 1; s.Current.LateMagicBootup = 2;
            var ticks = random.Next(1, 300);
            var expectedE = RefW(s.WandoosEnergy.Progress, ticks, s.Current.EnergyTotal, s.Current.WandoosEnergySpeedWithoutAt,
                s.OperatingSystems[0].EnergyBaseTime, s.Current.BootupUntilSeconds, .25, 1);
            var expectedM = RefW(s.WandoosMagic.Progress, ticks, s.Current.MagicTotal, s.Current.WandoosMagicSpeedWithoutAt,
                s.OperatingSystems[0].MagicBaseTime, s.Current.BootupUntilSeconds, .5, 2);
            var r = Project(s, Phase("Wandoos", ticks / 50d));
            Equal(expectedE.Item1, r.WandoosEnergyLevel, "random W energy ticks");
            Near(expectedE.Item2, r.WandoosEnergyProgress, "random W energy progress");
            Equal(expectedM.Item1, r.WandoosMagicLevel, "random W magic ticks");
            Near(expectedM.Item2, r.WandoosMagicProgress, "random W magic progress");
        }
    }

    private static void CheckFocusEquivalents()
    {
        var s = Snapshot(1, 1);
        s.OperatingSystems[0].EnergyBaseTime = 1; s.OperatingSystems[0].MagicBaseTime = 1;
        s.Current.WandoosEnergySpeedWithoutAt = .6; s.Current.WandoosMagicSpeedWithoutAt = .25;
        s.WandoosEnergy.Level = 8; s.WandoosMagic.Level = 3; s.CurrentWandoosBonus = 18.2;
        var r = Project(s, Phase("Wandoos", .2));
        Equal(13L, r.WandoosEnergyLevel, "dump equivalent subtracts existing energy levels");
        Equal(5L, r.WandoosMagicLevel, "dump equivalent subtracts existing magic levels");
        Near(25, r.EnergyReferenceDumpsPerSecond, "reference q .6 gives one dump per two ticks, not 30/sec");
        Near(12.5, r.MagicReferenceDumpsPerSecond, "separate magic reference rate");
        Near(.2, r.EnergyFocusEquivalentSeconds, "five gained energy dumps divided by 25/sec");
        Near(.16, r.MagicFocusEquivalentSeconds, "two gained magic dumps divided by 12.5/sec");
        Near(.4, r.ResourceFocusEquivalentSeconds, "resource allocation cost remains a separate summed metric");
        True(r.EnergyFocusEquivalentKnown && r.MagicFocusEquivalentKnown && r.ResourceFocusCostKnown, "known nonzero reference rates");
        var baseline = Project(s, Phase("NGU", 0));
        Near(0, baseline.EnergyFocusEquivalentSeconds, "existing energy dumps are not baseline gains");
        Near(0, baseline.MagicFocusEquivalentSeconds, "existing magic dumps are not baseline gains");
        s.Profiles = new[] { new CombatPrepProfile { Key = "ngu-zero", Goal = "NGU", SupportKey = "frozen" } };
        s.Profiles[0].ReferenceEnergyPerSecond = s.Profiles[0].ReferenceMagicPerSecond = 1000000;
        r = Project(s, Phase("Wandoos", .2));
        True(!r.ResourceFocusCostKnown && r.EnergyFocusEquivalentKnown && r.MagicFocusEquivalentKnown,
            "unavailable NGU allocation reference does not invalidate actual W dump equivalents");
        Near(.2, r.EnergyFocusEquivalentSeconds, "dump equivalent is independent of NGU allocation denominator");

        var reference = new CombatPrepProfile { Key = "reference", Goal = "Wandoos", SupportKey = "frozen",
            EnergyTotal = 1, EnergyCap = 1, MagicTotal = 1, MagicCap = 1,
            WandoosEnergySpeedWithoutAt = .2, WandoosMagicSpeedWithoutAt = .6 };
        s.Profiles = new[] { new CombatPrepProfile { Key = "other", Goal = "Wandoos", SupportKey = "frozen",
            EnergyTotal = 1, EnergyCap = 1, MagicTotal = 1, MagicCap = 1,
            WandoosEnergySpeedWithoutAt = 100, WandoosMagicSpeedWithoutAt = 100 }, reference };
        s.ReferenceWandoosProfileKey = "reference";
        r = Project(s, Phase("Wandoos", .2));
        Equal("reference", r.ReferenceWandoosProfileKey, "explicit reference profile overrides first W profile");
        Near(.5, r.EnergyFocusEquivalentSeconds, "dump time uses reference gear, not prep gear");
        Near(.08, r.MagicFocusEquivalentSeconds, "magic dump time has its own gear rate");
        s.OperatingSystems[0].EnergySpeedFactor = 2;
        r = Project(s, Phase("Wandoos", .2));
        Near(50d / 3, r.EnergyReferenceDumpsPerSecond, "reference dump rate applies the alternative OS speed ratio");
        Near(.6, r.EnergyFocusEquivalentSeconds, "OS-adjusted gains and reference rate stay consistent");
        s.OperatingSystems[0].EnergySpeedFactor = 1;
        s.ReferenceWandoosProfileKey = null;
        r = Project(s, Phase("Wandoos", .2));
        Equal("other", r.ReferenceWandoosProfileKey, "default reference is the first captured W profile");
        s.ReferenceWandoosProfileKey = "reference";

        s.Current.EnergyPower = 4; s.Current.AtSpeed = 25;
        reference.BootupUntilSeconds = .55; reference.EarlyEnergyBootup = 1; reference.LateEnergyBootup = 2;
        r = Project(s, Phase("AT", .5, 1), Phase("Wandoos", .1));
        Equal(6L, r.AtEnergyLevel, "reference-rate fixture trains final AT with held allocation");
        Near(50, r.EnergyReferenceDumpsPerSecond, "reference rate uses final AT and end-of-prep bootup, saturated at 50/sec");
        Near(.1, r.EnergyFocusEquivalentSeconds, "five energy dump gains divided by final reference rate");
        Near(25, r.MagicReferenceDumpsPerSecond, "untrained AT magic lane keeps independent reference rate");

        reference.EnergyTotal = 0; reference.MagicTotal = 0;
        r = Project(s, Phase("Wandoos", .2));
        Near(0, r.EnergyReferenceDumpsPerSecond, "zero reference energy pool has zero dump rate");
        Near(0, r.MagicReferenceDumpsPerSecond, "zero reference magic pool has zero dump rate");
        Near(0, r.EnergyFocusEquivalentSeconds, "unavailable energy reference never returns infinity");
        Near(0, r.MagicFocusEquivalentSeconds, "unavailable magic reference never returns infinity");
        True(!r.EnergyFocusEquivalentKnown && !r.MagicFocusEquivalentKnown, "zero reference rates explicitly marked unknown");
        reference.EnergyTotal = 1; reference.MagicTotal = 1;
        reference.LateEnergyBootup = 1;
        reference.WandoosEnergySpeedWithoutAt = 1e-9; reference.WandoosMagicSpeedWithoutAt = 1e-9;
        r = Project(s, Phase("Wandoos", .2));
        True(!r.EnergyFocusEquivalentKnown && !r.MagicFocusEquivalentKnown, "tiny reference rates are not productive");
        s.ReferenceWandoosProfileKey = "missing";
        True(!CombatPrepPlanner.Plan(s, 1).Success, "unknown W reference profile rejected");

        s = Snapshot(); s.WandoosAvailable = false;
        var unavailable = CombatPrepPlanner.Plan(s, 1);
        True(!unavailable.Success && unavailable.Error.Contains("unavailable"), "disabled/unavailable W snapshot cannot plan");
        Throws(() => Project(s, Phase("Wandoos", 1)), "disabled/unavailable W snapshot cannot project");
        True(!new CombatPrepSnapshot().WandoosAvailable, "availability capture is fail-closed by default");
    }

    private static void CheckBackgroundAndGold()
    {
        var s = Snapshot(100, 200);
        Near(.03, s.BackgroundEnergyFraction, "default energy background fraction is three percent");
        Near(.03, s.BackgroundMagicFraction, "default magic background fraction is three percent");
        s.BackgroundEnergyFraction = .02; s.BackgroundMagicFraction = .04;
        var plan = CombatPrepPlanner.Plan(s, 10);
        var background = plan.Alternatives.Single(x => x.Family == "BackgroundOnly");
        Near(.02, background.Phases[0].EnergyResourceFraction, "returned energy background budget");
        Near(.04, background.Phases[0].MagicResourceFraction, "returned magic background budget");
        Equal(2L, background.WandoosEnergyAllocation, "two-percent energy dump, not magic percent");
        Equal(8L, background.WandoosMagicAllocation, "four-percent magic dump, not energy percent");
        Near(20, background.EnergyResourceSeconds, "independent energy background cost");
        Near(80, background.MagicResourceSeconds, "independent magic background cost");
        var replay = Project(s, background.Phases);
        Near(background.TotalBonus, replay.TotalBonus, "separate background fractions replay exactly");
        var bounded = Project(s, Phase("Background", 10));
        Equal(2L, bounded.WandoosEnergyAllocation, "explicit background cannot exceed snapshot energy budget");
        Equal(8L, bounded.WandoosMagicAllocation, "explicit background cannot exceed snapshot magic budget");

        s.Current.AtSpeed = 50; s.NetGoldPerSecond = -25;
        var tier = Tier(); tier.Augment.ProgressCoefficient = 1; tier.Upgrade.ProgressCoefficient = 1;
        s.Augments = new[] { tier };
        s.WandoosEnergy.Progress = .5; s.WandoosMagic.Progress = .25; s.WandoosMagicAllocation = 200;
        var aug = Phase("Augments", .5); aug.BackgroundMagicWandoos = true;
        var at = Phase("AT", .5);
        Throws(() => Project(s, aug, at), "augment magic W may not train before a future AT phase");
        var afterAt = Project(s, at);
        Near(.5, afterAt.WandoosEnergyProgress, "AT preserves initial energy W partial without training");
        Equal(0L, afterAt.WandoosMagicLevel, "AT does not train initial magic W allocation");
        Near(.25, afterAt.WandoosMagicProgress, "AT preserves initial magic W partial");
        Equal(0L, afterAt.WandoosEnergyAllocation, "multiphase AT ends with no energy W allocation");
        Equal(0L, afterAt.WandoosMagicAllocation, "multiphase AT ends with no magic W allocation");
        Near(0, afterAt.MagicResourceSeconds, "all magic stays out of W until the AT gate completes");
        var afterAug = Project(s, at, aug);
        Equal(12L, afterAug.WandoosMagicLevel, "post-AT augment magic W uses the completed AT speed bonus");
        Near(.56, afterAug.WandoosMagicProgress, "post-AT augment magic W retains final partial after discarded overflow");
        Equal(8L, afterAug.WandoosMagicAllocation, "post-AT augment magic W obeys its four-percent budget");
        var finished = Project(s, at, aug, Phase("Wandoos", .5));
        Equal(6L, finished.AtEnergyLevel, "multiphase final AT energy levels");
        Equal(6L, finished.AtMagicLevel, "multiphase final AT magic levels");
        Equal(25L, finished.WandoosEnergyLevel, "energy W begins only after AT using final boost");
        Equal(37L, finished.WandoosMagicLevel, "post-AT augment magic W gains are included before final W");
        Near(0, finished.WandoosMagicProgress, "W discards completion overflow after preserved partial");
        Near(11.5, finished.MagicResourceSeconds, "magic cost is four background resource-seconds plus 15 resources for .5s W");
        Near(9.5, finished.EnergyResourceSeconds, "energy cost includes held augment, held AT and final W once each");
        Near(980, finished.GoldSpent, "seven paid augment/upgrade starts include final partials, no later phase charges");
        Near(98982.5, finished.GoldRemaining, "finish subtracts purchases and all 37.5 gold of recurring net expenses");
        var replayed = Project(s, finished.Phases);
        Near(finished.GoldSpent, replayed.GoldSpent, "returned multiphase gold purchases replay");
        Near(finished.GoldRemaining, replayed.GoldRemaining, "returned multiphase recurring expenses replay");

        s.Gold = 1; s.Augments = new CombatPrepAugmentTier[0];
        finished = Project(s, Phase("AT", 1), Phase("Wandoos", 1));
        Near(0, finished.GoldRemaining, "recurring expenses never make finish gold negative");
        Near(0, finished.GoldSpent, "recurring expenses are not mislabeled as augment purchases");
        s.BackgroundEnergyFraction = double.NaN;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "non-finite energy background fraction rejected");
        s.BackgroundEnergyFraction = .03; s.BackgroundMagicFraction = 1.01;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "magic background fraction above one rejected");
        s.BackgroundMagicFraction = .03; s.NetGoldPerSecond = -1e301;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "out-of-range recurring net expenses rejected");
    }

    private static void CheckAtBeforeWandoos()
    {
        var s = Snapshot(); s.Augments = new[] { Tier() };
        var magicAug = Phase("Augments", .5); magicAug.BackgroundMagicWandoos = true;
        Throws(() => Project(s, magicAug, Phase("AT", .5), Phase("Wandoos", .5)), "joint policy rejects magic W before later AT");
        Throws(() => Project(s, Phase("Wandoos", .5), Phase("AT", .5)), "Wandoos then AT is never legal");
        Throws(() => Project(s, Phase("Background", .5), Phase("AT", .5)), "background W then AT is never legal");
        Throws(() => Project(s, Phase("AT", .5), magicAug, Phase("AT", .5)), "earlier AT cannot authorize W before another later AT");
        Throws(() => Project(s, Phase("Augments", .5), Phase("Augments", .5)), "duplicate augment prep rejected");
        Throws(() => Project(s, Phase("Wandoos", .5), Phase("Augments", .5)), "terminal W cannot precede more prep");
        Throws(() => Project(s, Phase("NGU", .5), Phase("AT", .5)), "terminal NGU cannot precede more prep");
        s.Current.MagicTotal = 0;
        Throws(() => Project(s, magicAug, Phase("AT", .5)), "zero current magic cannot bypass the declared no-background-before-AT rule");
        s.Current.MagicTotal = 100;

        // Existing mid-rebirth dumps/progress are baseline state, not newly scheduled W work.
        s.WandoosEnergy.Level = 2; s.WandoosMagic.Level = 3;
        s.WandoosEnergy.Progress = .25; s.WandoosMagic.Progress = .75;
        s.WandoosEnergyAllocation = s.WandoosMagicAllocation = 100;
        s.CurrentWandoosBonus = 9.8;
        var held = Project(s, Phase("Augments", .5), Phase("AT", .5));
        Equal(2L, held.WandoosEnergyLevel, "initial energy dumps are preserved before future AT");
        Equal(3L, held.WandoosMagicLevel, "initial magic dumps are preserved before future AT");
        Near(.25, held.WandoosEnergyProgress, "initial energy W partial is not erased or trained");
        Near(.75, held.WandoosMagicProgress, "initial magic W partial is not erased or trained");
        Equal(0L, held.WandoosEnergyAllocation, "AT recalls initial energy W allocation");
        Equal(0L, held.WandoosMagicAllocation, "AT recalls initial magic W allocation");
        var allowed = Project(s, Phase("AT", .5), magicAug, Phase("Background", .5));
        Equal("AT", allowed.Phases[0].Kind, "AT-first policy retains its leading gate");
        True(allowed.Phases[1].BackgroundMagicWandoos && allowed.MagicResourceSeconds > 0,
            "magic W during augment phase is permitted after AT, including terminal NGU background");

        var result = CombatPrepPlanner.Plan(s, 20);
        True(result.Success, "strict-order search succeeds: " + result.Error);
        foreach (var alternative in result.Alternatives)
        {
            CheckNoWBeforeAt(alternative);
            if (alternative.Phases.Length > 0)
            {
                var replay = CombatPrepPlanner.Project(s, alternative.OsId, alternative.Phases);
                Near(alternative.TotalBonus, replay.TotalBonus, "strict-order candidate replays without hidden pre-AT W gains");
                Near(alternative.MagicResourceSeconds, replay.MagicResourceSeconds, "strict-order candidate replays magic W cost");
            }
        }
        var joint = result.Alternatives.First(x => x.Family == "AugmentsATWandoos");
        True(!joint.Phases[0].BackgroundMagicWandoos, "joint augment -> AT prefix must explicitly disable magic W");
        var firstW = result.Alternatives.First(x => x.Family == "ATThenAugmentsWandoos");
        var firstNgu = result.Alternatives.First(x => x.Family == "ATThenAugmentsNGUBackground");
        True(firstW.Phases[0].Kind == "AT" && firstW.Phases[1].Kind == "Augments" && firstW.Phases[1].BackgroundMagicWandoos,
            "search includes AT -> budgeted magic augment W -> W family");
        Equal("Background", firstNgu.Phases[2].Kind, "search includes AT -> budgeted magic augment W -> NGU background family");
    }

    private static void CheckNoWBeforeAt(CombatPrepAlternative alternative)
    {
        var lastAt = Array.FindLastIndex(alternative.Phases, x => x.Kind == "AT");
        if (lastAt < 0) return;
        for (var i = 0; i <= lastAt; i++)
        {
            var phase = alternative.Phases[i];
            True(phase.Kind != "Wandoos" && phase.Kind != "Background" && !phase.BackgroundMagicWandoos,
                "no W lane is scheduled before all AT prep completes: " + alternative.Family);
        }
    }

    private static string AlternativeSignature(CombatPrepSnapshot snapshot, CombatPrepAlternative alternative)
    {
        return JsonSerializer.Serialize(new { alternative.OsId, Phases = alternative.Phases.Select(phase => {
            var profile = new[] { snapshot.Current }.Concat(snapshot.Profiles).Single(x => x.Key == phase.GearProfileKey);
            return new { Gear = profile.OwnedGearRefs.OrderBy(x => x, StringComparer.Ordinal).ToArray(), profile.SupportKey,
                phase.Kind, phase.DurationSeconds, phase.AugmentTierId, phase.AugmentTarget, phase.UpgradeTarget,
                phase.AtEnergyWeight, phase.AtEnergyTarget, phase.AtMagicTarget, phase.BackgroundMagicWandoos,
                phase.EnergyResourceFraction, phase.MagicResourceFraction };
        }).ToArray() });
    }

    private static void CheckAlternativeDeduplication()
    {
        var s = Snapshot(); s.Current.OwnedGearRefs = new[] { "-1/head-instance", "-5/weapon-instance" };
        var alias = new CombatPrepProfile { Key = "alias-w", Goal = "Wandoos", SupportKey = "frozen",
            OwnedGearRefs = s.Current.OwnedGearRefs.Reverse().ToArray(), EnergyTotal = 100, EnergyCap = 100,
            MagicTotal = 100, MagicCap = 100, EnergyPower = 1, MagicPower = 1, AtSpeed = 1,
            WandoosEnergySpeedWithoutAt = 1, WandoosMagicSpeedWithoutAt = 1,
            ReferenceEnergyPerSecond = 100, ReferenceMagicPerSecond = 100 };
        s.Profiles = new[] { alias };
        var result = CombatPrepPlanner.Plan(s, 10);
        True(result.Success, "same-gear alias search succeeds");
        Equal(1, result.Alternatives.Count(x => x.Family == "WandoosNoAT"), "Current/W aliases with same real gear produce one W policy");
        Equal(result.Alternatives.Length, result.Alternatives.Select(x => AlternativeSignature(s, x)).Distinct().Count(),
            "all returned alternatives are distinct by actual gear and executable policy, not profile titles");
        True(result.Alternatives.Any(x => x.Family == "ATThenWandoos"), "same gear with different AT policy remains a separate choice");
        True(alias.OwnedGearRefs.SequenceEqual(s.Current.OwnedGearRefs.Reverse()), "canonicalizing gear identity does not reorder captured metadata");

        alias.OwnedGearRefs = new[] { "-1/other-head-instance", "-5/weapon-instance" };
        result = CombatPrepPlanner.Plan(s, 10);
        Equal(2, result.Alternatives.Count(x => x.Family == "WandoosNoAT"), "genuinely different gear with identical gain is not over-deduplicated");
        alias.OwnedGearRefs = s.Current.OwnedGearRefs.Reverse().ToArray();
        alias.SupportKey = "different-support"; s.PhaseSpecificSupport = true;
        result = CombatPrepPlanner.Plan(s, 10);
        Equal(2, result.Alternatives.Count(x => x.Family == "WandoosNoAT"), "same gear with different captured support remains a different executable policy");
    }

    private static void CheckPolicyPools()
    {
        var s = Snapshot(100, 200);
        s.AtEnergy.Unlocked = s.AtMagic.Unlocked = false;
        var tier = Tier(); tier.Augment.Unlocked = tier.Upgrade.Unlocked = false;
        s.Augments = new[] { tier };
        var low = new CombatPrepProfile { Key = "low", Goal = "Augments", SupportKey = "frozen",
            EnergyTotal = 100, EnergyCap = 10, MagicTotal = 200, MagicCap = 20,
            EnergyPower = 1, AtSpeed = 1, WandoosEnergySpeedWithoutAt = 1, WandoosMagicSpeedWithoutAt = 1 };
        s.Profiles = new[] { low };
        var aug = Phase("Augments", .5); aug.GearProfileKey = "low";
        var trained = Project(s, aug, Phase("AT", .5), Phase("Wandoos", 1));
        Equal(10L, trained.EndEnergyPool, "low-cap augment gear permanently reduces this policy's energy pool");
        Equal(20L, trained.EndMagicPool, "low-cap augment gear permanently reduces this policy's magic pool");
        Equal(10L, trained.WandoosEnergyAllocation, "later higher-cap gear cannot refill lost energy");
        Equal(20L, trained.WandoosMagicAllocation, "later higher-cap gear cannot refill lost magic");
        Equal(5L, trained.WandoosEnergyLevel, "W training uses depleted energy pool");
        Equal(10L, trained.WandoosMagicLevel, "W training uses depleted magic pool");
        Near(5, trained.EnergyReferenceDumpsPerSecond, "reference W focus is also limited by final available energy");
        Near(10, trained.MagicReferenceDumpsPerSecond, "reference W focus is also limited by final available magic");
        Near(10, trained.EnergyResourceSeconds, "depleted-policy energy resource accounting");
        Near(20, trained.MagicResourceSeconds, "depleted-policy magic resource accounting");
        Equal(100L, low.EnergyTotal, "effective policy profiles never mutate captured totals");
        Equal(200L, low.MagicTotal, "effective policy profiles never mutate captured magic");
        var direct = Project(s, Phase("Wandoos", 1));
        Equal(100L, direct.EndEnergyPool, "unvisited low-cap profile cannot reduce direct W energy");
        Equal(200L, direct.EndMagicPool, "unvisited low-cap profile cannot reduce direct W magic");
        var zeroAug = Phase("Augments", 0); zeroAug.GearProfileKey = "low";
        var skipped = Project(s, zeroAug, Phase("AT", .5), Phase("Wandoos", 1));
        Equal(100L, skipped.EndEnergyPool, "zero-duration low-cap phase is not a visited loadout");
        Equal(200L, skipped.EndMagicPool, "zero-duration phase cannot consume future magic capacity");
        var planned = CombatPrepPlanner.Plan(s, 10);
        var noAt = planned.Alternatives.Single(x => x.Family == "WandoosNoAT");
        Equal(100L, noAt.EndEnergyPool, "search does not apply unrelated profile's global minimum energy");
        Equal(200L, noAt.EndMagicPool, "search does not apply unrelated profile's global minimum magic");

        tier.Augment.Unlocked = tier.Upgrade.Unlocked = true;
        s.AtEnergy.Unlocked = s.AtMagic.Unlocked = true;
        s.Current.AtSpeed = 50; s.Current.AugmentSpeedFactor = 0;
        low.AugmentSpeedFactor = 100;
        planned = CombatPrepPlanner.Plan(s, 20);
        foreach (var alternative in planned.Alternatives.Where(x => x.Phases.Length > 0))
        {
            var replay = Project(s, alternative.Phases);
            Equal(alternative.AtEnergyLevel, replay.AtEnergyLevel, "AT cache is scoped to each policy's incoming energy pool");
            Equal(alternative.AtMagicLevel, replay.AtMagicLevel, "AT cache cannot reuse a larger-pool prefix");
            Near(alternative.AtEnergyProgress, replay.AtEnergyProgress, "AT cache retains held-allocation progress at checkpoints");
            Equal(alternative.EndEnergyPool, replay.EndEnergyPool, "search pool depletion replays per policy");
            Near(alternative.TotalBonus, replay.TotalBonus, "policy-dependent cached training scores replay");
        }
    }

    private static void CheckPerformance()
    {
        var s = Snapshot(1000000000, 500000000);
        s.Current.EnergyPower = 100000; s.Current.MagicPower = 50000; s.Current.AtSpeed = 25;
        s.Current.WandoosEnergySpeedWithoutAt = 10; s.Current.WandoosMagicSpeedWithoutAt = 8;
        s.AtEnergy.BaseTime = 300000; s.AtMagic.BaseTime = 600000;
        s.AtEnergy.LevelFactor = .05; s.AtMagic.LevelFactor = .05;
        s.Gold = 1e18; s.NetGoldPerSecond = 1e12;
        s.Augments = Enumerable.Range(0, 7).Select(i => new CombatPrepAugmentTier { Id = i, Name = "Tier " + i,
            BaseBoost = Math.Pow(10, i), TierExponent = 1 + .1 * i,
            Augment = new CombatPrepAugmentLane { Unlocked = true, ProgressCoefficient = 1e-9 / Math.Pow(10, i), CostCoefficient = 100 * Math.Pow(100, i) },
            Upgrade = new CombatPrepAugmentLane { Unlocked = true, ProgressCoefficient = 5e-10 / Math.Pow(10, i), CostCoefficient = 1000 * Math.Pow(100, i), CostExponent = 2 }
        }).ToArray();
        s.OperatingSystems = Enumerable.Range(0, 3).Select(i => { var os = Os(i, "normal");
            os.EnergyBaseTime = os.MagicBaseTime = Math.Pow(1000, i) * 1e9;
            if (i == 0) { os.EnergyLevelOffset = 100; os.MagicLevelOffset = 25;
                os.EnergyExponent = os.MagicExponent = .8; os.BonusCoefficient = Math.Pow(100, -.8) * Math.Pow(25, -.8); }
            if (i == 2) { os.EnergyLevelOffset = 1d / 6; os.MagicLevelOffset = 1d / 40;
                os.EnergyExponent = os.MagicExponent = 1.0499999523162842; os.BonusCoefficient = Math.Pow(240, 1.0499999523162842); }
            return os; }).ToArray();
        s.Profiles = new[] { "Augments", "AT", "Wandoos", "NGU" }.Select((goal, i) => new CombatPrepProfile {
            Key = goal.ToLowerInvariant(), Goal = goal, SupportKey = "frozen", OwnedGearRefs = new[] { "owned-loadout-" + i },
            EnergyTotal = 1000000000, EnergyCap = 900000000, MagicTotal = 500000000, MagicCap = 450000000,
            EnergyPower = 200000, MagicPower = 100000, AtSpeed = goal == "AT" ? 50 : 25,
            AugmentSpeedFactor = goal == "Augments" ? 2 : 1,
            WandoosEnergySpeedWithoutAt = goal == "Wandoos" ? 20 : 10,
            WandoosMagicSpeedWithoutAt = goal == "Wandoos" ? 16 : 8
        }).ToArray();
        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var watch = Stopwatch.StartNew();
        var result = CombatPrepPlanner.Plan(s, 7200);
        watch.Stop();
        var cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        True(result.Success, "realistic two-hour benchmark succeeds: " + result.Error);
        True(result.EvaluatedCandidates > 500 && result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates, "five-profile benchmark covers bounded coarse and adaptive families");
        Equal(7200d, result.DurationSeconds, "realistic benchmark uses full two-hour horizon");
        True(result.Recommendation.TotalBonus > 1, "realistic benchmark predicts actual training growth");
        Console.WriteLine("Combat prep 7-tier/3-OS/5-profile/7200s: " + watch.ElapsedMilliseconds + "ms wall, " + cpuMs.ToString("0") + "ms CPU, " + result.EvaluatedCandidates + " policies.");
        True(cpuMs < 2000, "realistic two-hour search CPU must be under two seconds; measured " + cpuMs.ToString("0") + "ms (offline .NET, not Unity)");

        s.WandoosEnergyAllocation = 1; // Mid-rebirth state keeps the current OS, not a reset.
        cpuBefore = process.TotalProcessorTime; watch.Restart();
        result = CombatPrepPlanner.Plan(s, 7200);
        watch.Stop(); cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        True(result.Success, "mid-rebirth two-hour benchmark succeeds: " + result.Error);
        True(result.EvaluatedCandidates > 150 && result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates, "current-OS search includes bounded local refinements");
        True(result.Alternatives.All(x => x.OsId == s.CurrentOsId), "mid-rebirth search never switches away from existing W dumps");
        Console.WriteLine("Combat prep current-OS/7-tier/5-profile/7200s: " + watch.ElapsedMilliseconds + "ms wall, " + cpuMs.ToString("0") + "ms CPU, " + result.EvaluatedCandidates + " policies.");
        True(cpuMs < 2000, "mid-rebirth two-hour search CPU must be under two seconds (offline .NET, not Unity)");

        s.WandoosEnergyAllocation = 0; s.PhaseSpecificSupport = true;
        s.Current.NetGoldPerSecond = -1000;
        s.Profiles = s.Profiles.SelectMany(p => Enumerable.Range(0, 2).Select(i => {
            var variant = p.Copy(); variant.Key += i; variant.SupportKey = i == 0 ? s.Current.SupportKey : "dedicated-" + p.Goal;
            variant.NetGoldPerSecond = i == 0 ? -2000 : 1e11;
            return variant;
        })).ToArray();
        foreach (var horizon in new[] { 900d, 3600, 7200, 86400 })
        {
            cpuBefore = process.TotalProcessorTime; watch.Restart();
            result = CombatPrepPlanner.Plan(s, horizon);
            watch.Stop(); cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            True(result.Success, "maximum support-profile shape plans " + horizon + " seconds: " + result.Error);
            True(result.EvaluatedCandidates > 1100 && result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates, "every horizon retains broad families and bounded adaptive refinement");
            True(result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates, "support variants never exceed 1440 candidates");
            Equal(horizon, result.DurationSeconds, "user horizon is not auto-extended");
            foreach (var candidate in result.Alternatives.Where(x => x.Phases.Length > 0))
            {
                Near(horizon, candidate.Phases.Sum(x => x.DurationSeconds), "every trained alternative stays within and uses its full horizon");
                True(candidate.EnergyResourceSeconds + candidate.NguEnergyResourceSeconds <= s.Current.EnergyTotal * horizon + 1,
                    "horizon energy budget cannot overflow or invent resources");
                True(candidate.MagicResourceSeconds + candidate.NguMagicResourceSeconds <= s.Current.MagicTotal * horizon + 1,
                    "horizon magic budget cannot overflow or invent resources");
                True(!double.IsNaN(candidate.TotalBonus) && !double.IsInfinity(candidate.TotalBonus) && candidate.GoldRemaining >= 0,
                    "horizon objective and gold stay finite and nonnegative");
                True(result.Recommendation.TotalBonus >= candidate.TotalBonus,
                    "horizon selection maximizes stats, not minimum focus time");
                var replay = CombatPrepPlanner.Project(s, candidate.OsId, candidate.Phases);
                Near(candidate.TotalBonus, replay.TotalBonus, "horizon cached objective replays");
                Near(candidate.GoldRemaining, replay.GoldRemaining, "horizon cached profile income replays");
                Near(candidate.GoldSpent, replay.GoldSpent, "horizon cached purchases replay");
            }
            Console.WriteLine("Combat prep 7-tier/3-OS/9-profile/" + horizon + "s: " + watch.ElapsedMilliseconds +
                "ms wall, " + cpuMs.ToString("0") + "ms CPU, " + result.EvaluatedCandidates + " policies.");
            True(cpuMs < Math.Max(2000, 2000 * horizon / 7200),
                "maximum-shape CPU budget is two seconds through two hours, scaled linearly for 24 hours (offline .NET)");

            var extreme = Snapshot(0, 0); extreme.Gold = 1e300; extreme.Current.NetGoldPerSecond = 1e300;
            Near(1e300, Project(extreme, Phase("NGU", horizon)).GoldRemaining, "extreme profile income saturates without overflow at " + horizon);
            extreme.Current.NetGoldPerSecond = -1e300;
            Near(0, Project(extreme, Phase("NGU", horizon)).GoldRemaining, "extreme profile drain floors without overflow at " + horizon);
        }
        s.WandoosBootupDurationSeconds = 3600;
        s.WandoosBootupCompletionFactor = (double) 1.1f;
        foreach (var freeAt in new[] { false, true })
        {
            s.FreeAdvancedTraining = freeAt;
            cpuBefore = process.TotalProcessorTime; watch.Restart();
            result = CombatPrepPlanner.Plan(s, 7200);
            watch.Stop(); cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            True(result.Success, "native zero-boot maximum-shape search succeeds: " + result.Error);
            True(result.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates, "native ramp does not enlarge candidate budget");
            foreach (var candidate in result.Alternatives.Where(x => x.Phases.Length > 0))
            {
                var replay = CombatPrepPlanner.Project(s, candidate.OsId, candidate.Phases);
                Near(candidate.TotalBonus, replay.TotalBonus, "native ramp cached policy objective replays");
                Near(candidate.EnergyResourceSeconds, replay.EnergyResourceSeconds, "native ramp energy opportunity cost replays");
                Near(candidate.MagicResourceSeconds, replay.MagicResourceSeconds, "native ramp magic opportunity cost replays");
                Equal(candidate.AtEnergyLevel, replay.AtEnergyLevel, "native free AT cache follows absolute time");
            }
            Console.WriteLine("Combat prep native zero-boot/9-profile/7200s/freeAT=" + freeAt + ": " + watch.ElapsedMilliseconds + "ms wall, " + cpuMs.ToString("0") + "ms CPU, " + result.EvaluatedCandidates + " policies.");
            True(cpuMs < 2000, "native bootup/free AT search CPU stays under two seconds (offline .NET)");
        }
    }

    private static Tuple<long, double> RefW(double progress, int ticks, long pool, double speed, double baseTime,
        double bootupUntil, double early, double late)
    {
        long level = 0;
        for (var t = 0; t < ticks; t++)
        {
            var unit = speed * (t < (int) Math.Round(bootupUntil * 50) ? early : late) / baseTime;
            var cap = (long) Math.Ceiling(1 / unit);
            var q = Math.Min(pool, cap) * unit;
            if (q <= 1e-9) continue;
            progress += q;
            if (progress >= 1) { level++; progress = 0; }
        }
        return Tuple.Create(level, progress);
    }

    private static void DifferentialAt()
    {
        var random = new Random(3881);
        for (var i = 0; i < 120; i++)
        {
            var s = Snapshot(random.Next(1, 20), 0);
            s.AllocationRefreshSeconds = new[] { .5, .5, .5, .1, .26 }[random.Next(5)];
            // Non-boundary rates avoid conflating repeated IEEE addition drift with the
            // independently checked ceil-to-tick model; binary-exact boundaries are fixed above.
            s.Current.EnergyPower = 4; s.Current.AtSpeed = random.Next(1, 8) * 1.000001;
            s.AtEnergy.BaseTime = random.Next(1, 8); s.AtMagic.BaseTime = random.Next(1, 8);
            s.AtEnergy.Level = random.Next(4); s.AtMagic.Level = random.Next(4);
            s.AtEnergy.Progress = random.Next(4) / 4d; s.AtMagic.Progress = random.Next(4) / 4d;
            var weight = new[] { 0d, .25, .5, .75, 1 }[random.Next(5)];
            var ticks = random.Next(1, 200);
            long eLevel = s.AtEnergy.Level, mLevel = s.AtMagic.Level;
            double ep = s.AtEnergy.Progress, mp = s.AtMagic.Progress, cost = 0;
            long a = 0, b = 0;
            for (var t = 0; t < ticks; t++)
            {
                var eu = 2 * s.Current.AtSpeed / (50 * s.AtEnergy.BaseTime * (eLevel + 1));
                var mu = 2 * s.Current.AtSpeed / (50 * s.AtMagic.BaseTime * (mLevel + 1));
                if (t % (int) Math.Round(s.AllocationRefreshSeconds * 50) == 0)
                    RefShare(s.Current.EnergyTotal, (long) Math.Ceiling(1 / eu), (long) Math.Ceiling(1 / mu), weight, out a, out b);
                cost += (a + b) / 50d;
                ep += a * eu; mp += b * mu;
                if (ep >= 1) { eLevel++; ep = 0; }
                if (mp >= 1) { mLevel++; mp = 0; }
            }
            var r = Project(s, Phase("AT", ticks / 50d, weight));
            Equal(eLevel, r.AtEnergyLevel, "random AT energy levels " + i); Near(ep, r.AtEnergyProgress, "random AT energy progress " + i);
            Equal(mLevel, r.AtMagicLevel, "random AT magic levels " + i); Near(mp, r.AtMagicProgress, "random AT magic progress " + i);
            Near(cost, r.EnergyResourceSeconds, "random AT caps and weights resource cost");
        }
    }

    private static void RefShare(long budget, long capA, long capB, double weight, out long a, out long b)
    {
        // Independent exhaustive integer allocation interpretation of capped shares.
        if (weight == 0) { a = 0; b = Math.Min(budget, capB); return; }
        if (weight == 1) { a = Math.Min(budget, capA); b = 0; return; }
        b = Math.Min(capB, (long) Math.Floor(budget * (1 - weight)));
        a = Math.Min(capA, budget - (long) Math.Floor(budget * (1 - weight)));
        while (a + b < budget && (a < capA || b < capB))
        {
            if (a < capA) a++;
            else b++;
        }
    }

    private static void DifferentialAugments()
    {
        var random = new Random(4881);
        for (var i = 0; i < 120; i++)
        {
            var s = Snapshot(random.Next(1, 20), 0); var tier = Tier(); s.Augments = new[] { tier };
            s.AllocationRefreshSeconds = new[] { .5, .5, .5, .1, .26 }[random.Next(5)];
            tier.Augment.ProgressCoefficient = random.Next(1, 10) / 32d * 1.000001;
            tier.Upgrade.ProgressCoefficient = random.Next(1, 10) / 32d * 1.000001;
            tier.Augment.Level = random.Next(3); tier.Upgrade.Level = random.Next(3);
            tier.Augment.Progress = random.Next(4) / 4d; tier.Upgrade.Progress = random.Next(4) / 4d;
            s.CurrentAugmentTotal = 1 + 2 * (tier.Upgrade.Level * tier.Upgrade.Level + 1) * tier.Augment.Level;
            s.Gold = random.Next(30); s.NetGoldPerSecond = random.Next(-5, 30) * 12.5;
            s.Current.NetGoldPerSecond = i % 3 == 0 ? (double?) null : random.Next(-5, 30) * 12.5;
            var income = s.Current.NetGoldPerSecond ?? s.NetGoldPerSecond;
            var ticks = random.Next(1, 200);
            long al = tier.Augment.Level, ul = tier.Upgrade.Level;
            double ap = tier.Augment.Progress, up = tier.Upgrade.Progress, gold = s.Gold, spent = 0, cost = 0;
            bool paidA = ap > 0, paidU = up > 0;
            long a = 0, u = 0;
            for (var t = 0; t < ticks; t++)
            {
                var au = tier.Augment.ProgressCoefficient / (al + 1);
                var uu = tier.Upgrade.ProgressCoefficient / (ul + 1);
                var ac = 10 * (al + 1); var uc = 5 * (ul + 1) * (ul + 1);
                if (t % (int) Math.Round(s.AllocationRefreshSeconds * 50) == 0)
                {
                    var ca = paidA || gold >= ac ? (long) Math.Ceiling(1 / au) : 0;
                    var reserve = ca > 0 && !paidA ? ac : 0;
                    var cu = paidU || gold >= uc + reserve ? (long) Math.Ceiling(1 / uu) : 0;
                    RefShare(s.Current.EnergyTotal, ca, cu, .5, out a, out u);
                }
                if (a > 0 && !paidA && gold >= ac) { gold -= ac; spent += ac; paidA = true; }
                if (u > 0 && !paidU && gold >= uc) { gold -= uc; spent += uc; paidU = true; }
                cost += (a + u) / 50d;
                if (paidA) ap += a * au;
                if (paidU) up += u * uu;
                if (ap >= 1) { al++; ap = 0; paidA = false; }
                if (up >= 1) { ul++; up = 0; paidU = false; }
                gold = Math.Max(0, gold + income / 50);
            }
            var r = Project(s, Phase("Augments", ticks / 50d));
            Equal(al, r.Augments[0].AugmentLevel, "random augment level"); Near(ap, r.Augments[0].AugmentProgress, "random augment progress");
            Equal(ul, r.Augments[0].UpgradeLevel, "random upgrade level"); Near(up, r.Augments[0].UpgradeProgress, "random upgrade progress");
            Equal(paidA, r.Augments[0].AugmentPaid, "random augment paid flag"); Equal(paidU, r.Augments[0].UpgradePaid, "random upgrade paid flag");
            Near(gold, r.GoldRemaining, "random gold remains nonnegative"); Near(spent, r.GoldSpent, "random gold charged at start");
            Near(cost, r.EnergyResourceSeconds, "random augment held resource cost " + i);
        }
    }

    private static void CheckSearchAndSafety()
    {
        var s = Snapshot(1000, 1000); s.Augments = Enumerable.Range(0, 7).Select(Tier).ToArray();
        s.OperatingSystems = Enumerable.Range(0, 3).Select(i => Os(i, "normal")).ToArray();
        var profiles = new List<CombatPrepProfile>();
        foreach (var goal in new[] { "Augments", "AT", "Wandoos", "NGU" })
            for (var i = 0; i < 2; i++) profiles.Add(new CombatPrepProfile { Key = goal + i, Goal = goal, SupportKey = "frozen",
                OwnedGearRefs = new[] { "owned-head-" + i, "owned-weapon-" + i }, EnergyTotal = 1000, EnergyCap = 900,
                MagicTotal = 800, MagicCap = 750, EnergyPower = 4, MagicPower = 9, AtSpeed = 2,
                AugmentSpeedFactor = 2, WandoosEnergySpeedWithoutAt = 2, WandoosMagicSpeedWithoutAt = 3 });
        s.Profiles = profiles.ToArray();
        var goldBefore = s.Gold;
        var r = CombatPrepPlanner.Plan(s, 20);
        True(r.Success, "maximum supported search snapshot succeeds: " + r.Error);
        True(r.EvaluatedCandidates > 1100 && r.EvaluatedCandidates <= CombatPrepPlanner.MaximumCandidates, "maximum supported shape searches coarse and adaptive families without exceeding its budget");
        True(r.Alternatives.Length <= 12, "bounded UI alternatives");
        True(r.Alternatives.Contains(r.Baseline), "baseline included");
        True(r.Alternatives.Contains(r.Recommendation), "recommendation included");
        True(r.Recommendation.TotalBonus >= r.Baseline.TotalBonus, "baseline bounds recommendation from below");
        True(r.Alternatives.Select(x => x.Family).Distinct().Count() == 10, "all nine named families plus baseline retained");
        Equal(r.Alternatives.Length, r.Alternatives.Select(x => AlternativeSignature(s, x)).Distinct().Count(), "maximum-shape alternatives are semantically distinct");
        True(JsonSerializer.Serialize(r).Length < 65536, "planner response fits 64 KB");
        foreach (var candidate in r.Alternatives.Where(x => x.Phases.Length > 0))
        {
            var again = CombatPrepPlanner.Project(s, candidate.OsId, candidate.Phases);
            Near(candidate.TotalBonus, again.TotalBonus, "returned phase weights reproduce objective");
            Near(candidate.GoldSpent, again.GoldSpent, "returned phase policy reproduces gold spending");
            Near(candidate.GoldRemaining, again.GoldRemaining, "cached prefixes reproduce final gold after expenses");
            Equal(candidate.AtEnergyLevel, again.AtEnergyLevel, "cached held AT levels reproduce on policy replay");
            Equal(candidate.EndEnergyPool, again.EndEnergyPool, "cached prefixes reproduce policy-local energy depletion");
            Equal(candidate.EndMagicPool, again.EndMagicPool, "cached prefixes reproduce policy-local magic depletion");
            Near(candidate.NguMagicResourceSeconds, again.NguMagicResourceSeconds, "AT cache is isolated by incoming magic pool as well as energy");
            True(candidate.Phases.Sum(x => x.DurationSeconds) <= 20.00001, "global deadline never grows");
            foreach (var at in candidate.Phases.Where(x => x.Kind == "AT"))
                True(!at.BackgroundMagicWandoos, "search never funds magic W during AT");
            CheckNoWBeforeAt(candidate);
        }
        Equal(0L, s.Augments[0].Augment.Level, "search does not mutate snapshot levels");
        Near(goldBefore, s.Gold, "search does not mutate snapshot gold");

        s = Snapshot(long.MaxValue, long.MaxValue);
        s.Current.EnergyCap = 3; s.Current.MagicCap = 7;
        var capped = Project(s, Phase("Wandoos", 1));
        True(capped.EnergyResourceSeconds <= 3 && capped.MagicResourceSeconds <= 7, "usable pools clamp to projected caps");
        s.BackgroundEnergyFraction = .5; s.BackgroundMagicFraction = .5;
        var background = Phase("Background", 1); background.EnergyResourceFraction = .5; background.MagicResourceFraction = .5;
        capped = Project(s, background);
        True(capped.EnergyResourceSeconds <= 1 && capped.MagicResourceSeconds <= 3, "background fraction uses min(total,cap) before rounding");
        Near(3, capped.EnergyResourceSeconds + capped.NguEnergyResourceSeconds, "background energy conservation");
        Near(7, capped.MagicResourceSeconds + capped.NguMagicResourceSeconds, "background magic conservation");

        s = Snapshot(long.MaxValue, 0); s.Current.EnergyCap = long.MaxValue - 5;
        s.Current.WandoosEnergySpeedWithoutAt = 1e-25; s.OperatingSystems[0].EnergyBaseTime = 1;
        capped = Project(s, Phase("Wandoos", .02));
        Equal(long.MaxValue - 5, capped.WandoosEnergyAllocation, "double rounding near long-max cannot allocate above an exact cap");

        s = Snapshot(long.MaxValue, long.MaxValue);
        s.Current.WandoosEnergySpeedWithoutAt = 1e300; s.Current.WandoosMagicSpeedWithoutAt = 1e300;
        s.Current.EnergyPower = 1e300; s.Current.AtSpeed = 1e300;
        s.WandoosEnergy.Level = long.MaxValue - 1;
        s.CurrentWandoosBonus = (1 + (double) s.WandoosEnergy.Level / 5);
        capped = Project(s, Phase("Wandoos", 1));
        Equal(long.MaxValue, capped.WandoosEnergyLevel, "level addition saturates without long overflow");
        Near(.02, capped.EnergyResourceSeconds, "saturated long-max lane returns resource after its final tick");
        Equal(0L, capped.WandoosEnergyAllocation, "long-max lane ends with no dump");
        Near(0, capped.EnergyReferenceDumpsPerSecond, "saturated long-max reference cannot train further dumps");
        True(!capped.EnergyFocusEquivalentKnown, "saturated long-max reference time is marked unavailable");
        True(!double.IsInfinity(capped.TotalBonus) && !double.IsNaN(capped.TotalBonus), "extreme score stays finite");
        capped = Project(s, Phase("AT", .04, .5));
        Equal(2L, capped.AtEnergyLevel, "extreme AT rate stays safe");
        Equal(2L, capped.AtMagicLevel, "extreme magic AT energy share stays safe");

        s = Snapshot(100, 100);
        var watch = Stopwatch.StartNew();
        capped = Project(s, Phase("Wandoos", 86400));
        watch.Stop();
        Equal(4320000L, capped.WandoosEnergyLevel, "24-hour W batching exact one level per tick");
        True(watch.ElapsedMilliseconds < 2000, "long saturated W projection batches directly");
        True(CombatPrepPlanner.Plan(s).DurationSeconds == 7200, "default duration 7200");
        True(!CombatPrepPlanner.Plan(s, 86400.02).Success, "hard duration maximum 86400");
        True(!CombatPrepPlanner.Plan(s, double.NaN).Success, "NaN duration rejected");
        True(!CombatPrepPlanner.Plan(s, .001).Success, "sub-tick prep duration rejected");
        s.Current.EnergyPower = double.PositiveInfinity;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "infinite capture rejected");

        s = Snapshot(1, 0); var expensive = Tier();
        expensive.Augment.Level = 1000000000000000000;
        expensive.Augment.CostCoefficient = 1e-300; expensive.Augment.CostExponent = 20;
        expensive.Augment.LevelExponent = 0; expensive.Augment.ProgressCoefficient = 1;
        expensive.Upgrade.Unlocked = false; s.Augments = new[] { expensive };
        s.Gold = 1e50; s.CurrentAugmentTotal = 1 + 2d * expensive.Augment.Level;
        capped = Project(s, Phase("Augments", .02));
        Equal(expensive.Augment.Level, capped.Augments[0].AugmentLevel, "extreme cost multiplication is not prematurely clamped to an affordable value");
        Near(0, capped.GoldSpent, "unaffordable extreme cost does not spend gold");

        // Same keys, different capture factors must never share cache entries across calls.
        s = Snapshot(10, 10); var slow = CombatPrepPlanner.Plan(s, 10);
        s.Current.WandoosEnergySpeedWithoutAt = 100; s.Current.WandoosMagicSpeedWithoutAt = 100;
        var fast = CombatPrepPlanner.Plan(s, 10);
        True(fast.Recommendation.TotalBonus > slow.Recommendation.TotalBonus, "cache is scoped to one capture context");

        s = Snapshot(); s.Profiles = new[] { new CombatPrepProfile { Key = "worse", Goal = "Wandoos", SupportKey = "frozen",
            EnergyTotal = 100, EnergyCap = 100, MagicTotal = 100, MagicCap = 100,
            WandoosEnergySpeedWithoutAt = .001, WandoosMagicSpeedWithoutAt = .001 } };
        r = CombatPrepPlanner.Plan(s, 1);
        True(r.Recommendation.Phases.Length > 0 && r.Recommendation.Phases.All(x => x.GearProfileKey == "current"),
            "current gear remains a trained alternative even when projected profiles exist");
    }

    private static void True(bool value, string scenario) { assertions++; if (!value) throw new Exception(scenario); }
    private static void Equal<T>(T expected, T actual, string scenario)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(scenario + ": expected " + expected + ", got " + actual);
    }
    private static void Near(double expected, double actual, string scenario)
    {
        assertions++;
        if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > 1e-7 * Math.Max(1, Math.Abs(expected)))
            throw new Exception(scenario + ": expected " + expected + ", got " + actual);
    }
    private static void Throws(Action action, string scenario)
    {
        assertions++;
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception(scenario + ": expected ArgumentException");
    }
}
