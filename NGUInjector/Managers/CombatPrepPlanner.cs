using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NGUInjector.Managers
{
    // ADAPTER CONTRACT: pure data only. All progress values are normalized [0,1),
    // all rates are PER GAME TICK (50 ticks/second), all resource counts are integers.
    // Capture getters in the CURRENT difficulty; never substitute normal-mode constants.
    // Call Plan(snapshot, seconds=7200). No Character, JSON, Unity or optimizer dependency.
    // Integration owner: production .csproj uses explicit Compile entries; add this file there.
    internal sealed class CombatPrepSnapshot
    {
        public CombatPrepProfile Current { get; set; }
        // Legal owned loadouts only. Key must identify exact equipment instances externally.
        // Projected Goal is Augments, AT, Wandoos or NGU, at most two per goal.
        // Current is an additional no-swap choice unless the adapter requires scored phase profiles.
        public CombatPrepProfile[] Profiles { get; set; } = new CombatPrepProfile[0];
        public bool RequirePhaseProfiles { get; set; }
        public CombatPrepAtLane AtEnergy { get; set; } = new CombatPrepAtLane(); // game AT index 3
        public CombatPrepAtLane AtMagic { get; set; } = new CombatPrepAtLane(); // game AT index 4; CONSUMES ENERGY
        public CombatPrepAugmentTier[] Augments { get; set; } = new CombatPrepAugmentTier[0];
        public CombatPrepOs[] OperatingSystems { get; set; } = new CombatPrepOs[0];
        public int CurrentOsId { get; set; }
        public string DifficultyKey { get; set; }
        // Adapter must capture true only for an available, enabled W system in a supported
        // context. False rejects Plan/Project rather than forecasting disabled/challenge W.
        public bool WandoosAvailable { get; set; }
        // Optional explicit reference for DUMP-equivalent seconds, not resource cost.
        // Null/empty selects the first Wandoos profile, otherwise Current when none exists.
        public string ReferenceWandoosProfileKey { get; set; }
        public CombatPrepBar WandoosEnergy { get; set; } = new CombatPrepBar();
        public CombatPrepBar WandoosMagic { get; set; } = new CombatPrepBar();
        // Native bootup advances even without dumps; progress uses the clock BEFORE that tick's
        // advance. Duration=0 retains the explicitly supplied profile early/late model.
        public double WandoosBootupElapsedSeconds { get; set; }
        public double WandoosBootupDurationSeconds { get; set; }
        public double WandoosBootupCompletionFactor { get; set; } = 1;
        // Wish 190: one level/tick at ZERO allocation after basic-training unlock, in EVERY
        // phase. Native hitTarget ignores menu targets at zero allocation. No paid AT policy.
        public bool FreeAdvancedTraining { get; set; }
        // Scheduled continuation constraints; defaults leave manual policy search unchanged.
        public bool PaidAtForbidden { get; set; }
        public bool AugmentsCompleted { get; set; }
        // Existing allocated resources ALSO prevent OS switching, even at zero levels/progress.
        public long WandoosEnergyAllocation { get; set; }
        public long WandoosMagicAllocation { get; set; }
        public double Gold { get; set; }
        // Frozen fallback for profiles without their own net rate. Negative income is allowed.
        public double NetGoldPerSecond { get; set; }
        // Actual AllAugsController.totalBonus(), including +1 and all global/challenge/NGU factors.
        // Formula is max(1,(1 + sum(BaseBoost*(U^2+1)*A^TierExponent))*AugmentGlobalFactor).
        public double AugmentGlobalFactor { get; set; } = 1;
        public double CurrentAugmentTotal { get; set; } = 1;
        public double CurrentWandoosBonus { get; set; } = 1;
        // RAW, UNCLAMPED final boss attack/defense on ONE fixed loadout/support at current A/W.
        // Do not capture the game's max(100,raw) getter. Forecast is max(100,raw*gain),
        // including baseline gain=1, never max(100,raw)*gain or prep gear's combat stats.
        public double BaselineBossAttack { get; set; }
        public double BaselineBossDefense { get; set; }
        // Phase entry refreshes immediately; allocations then stay fixed for this many seconds.
        // Must be a whole number of 50-Hz ticks. The executor must use the same cadence.
        public double AllocationRefreshSeconds { get; set; } = CombatPrepPlanner.AllocationRefreshSeconds;
        // false: every projected profile must have Current.SupportKey. true: phase-specific
        // support is already included in each profile. No support or future growth is simulated.
        public bool PhaseSpecificSupport { get; set; }
        public double BackgroundEnergyFraction { get; set; } = .03;
        public double BackgroundMagicFraction { get; set; } = .03;
        // Caller-supplied disclosure of capture limits, e.g. frozen bootup/support costs.
        public string CaptureAssumptions { get; set; }
    }

    internal sealed class CombatPrepProfile
    {
        public string Key { get; set; }
        public string Goal { get; set; } = "Current";
        public string SupportKey { get; set; }
        // Net of recurring expenses for this captured gear/support, signed and finite (|x| <= 1e300).
        // Null uses Snapshot.NetGoldPerSecond, never the previously visited profile's rate.
        public double? NetGoldPerSecond { get; set; }
        public string[] OwnedGearRefs { get; set; } = new string[0];
        public long EnergyTotal { get; set; }
        public long EnergyCap { get; set; }
        public long MagicTotal { get; set; }
        public long MagicCap { get; set; }
        public double EnergyPower { get; set; }
        public double MagicPower { get; set; } // captured for reporting; Wandoos does NOT use it
        public double AtSpeed { get; set; } // Character.totalAdvancedTrainingSpeedBonus()
        // Generic augment lane coefficient below is multiplied by this captured profile factor.
        // Ratio of ALL augment speed multipliers excluding EnergyPower to the reference
        // gear/support used for lane coefficients. Capture 1 on that reference gear.
        public double AugmentSpeedFactor { get; set; } = 1;
        // totalWandoos{Energy,Magic}Speed() / (1+ATLevelFactor*currentATLevel),
        // including ALL other actual speed factors, but EXCLUDING bootup if captured separately.
        public double WandoosEnergySpeedWithoutAt { get; set; }
        public double WandoosMagicSpeedWithoutAt { get; set; }
        // Absolute forecast time when early factors switch to late factors. Capture from game
        // bootup getters; if no exact split is available use equal factors and disclose frozen bootup.
        public double BootupUntilSeconds { get; set; }
        public double EarlyEnergyBootup { get; set; } = 1;
        public double LateEnergyBootup { get; set; } = 1;
        public double EarlyMagicBootup { get; set; } = 1;
        public double LateMagicBootup { get; set; } = 1;
        // Reference full-focus resource counts (resource-seconds per second), for opportunity
        // cost only. Zero defaults to this profile's min(total,cap). ResourceFocusEquivalentSeconds
        // sums lane-equivalent seconds; ResourceFocusCostKnown=false if a needed denominator is zero.
        public double ReferenceEnergyPerSecond { get; set; }
        public double ReferenceMagicPerSecond { get; set; }
        internal CombatPrepProfile Copy() { return (CombatPrepProfile) MemberwiseClone(); }
    }

    internal sealed class CombatPrepBar
    {
        public long Level { get; set; }
        public double Progress { get; set; }
    }

    internal sealed class CombatPrepAtLane
    {
        public bool Unlocked { get; set; }
        public long Level { get; set; }
        public double Progress { get; set; }
        public long TargetLevel { get; set; } // optional bound; 0 means no user bound
        // q = allocatedEnergy*sqrt(profile.EnergyPower)*profile.AtSpeed /
        //     (50*BaseTime*(level+1)); bonus = 1+LevelFactor*level.
        // Capture AdvancedTrainingController.baseTime and .levelFactor directly.
        public double BaseTime { get; set; } = 1;
        public double LevelFactor { get; set; }
    }

    internal sealed class CombatPrepAugmentLane
    {
        public bool Unlocked { get; set; }
        public long Level { get; set; }
        public double Progress { get; set; }
        // A partially filled bar has ALREADY paid for that level. If game paid a zero-progress
        // bar this flag must be true too. Engine charges at bar START, never on completion.
        public bool Paid { get; set; }
        // q = allocatedEnergy*profile.EnergyPower*profile.AugmentSpeedFactor*
        //     ProgressCoefficient/(level+1)^LevelExponent.
        // IL: normal/evil = all actual speed factors/(50000*difficulty lane divider),
        // sadistic = factors/(sadistic lane divider*sadisticDivider()). NOT an ETA.
        // Both augment AND upgrade progress use LevelExponent=1 in the inspected game.
        // Alternatively sample get{Aug,Upgrade}ProgressPerTick(long amount) with a sample
        // away from the tiny/float-max clamps, then divide by sample*referenceEnergyPower
        // and multiply by (currentLevel+1). This captures every live speed factor directly.
        public double ProgressCoefficient { get; set; }
        public double LevelExponent { get; set; } = 1;
        // cost = CostCoefficient*(level+1)^CostExponent. Include the actual gold discount.
        public double CostCoefficient { get; set; }
        public double CostExponent { get; set; } = 1; // augment 1, upgrade 2
    }

    internal sealed class CombatPrepAugmentTier
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public double BaseBoost { get; set; }
        public double TierExponent { get; set; } = 1;
        public CombatPrepAugmentLane Augment { get; set; } = new CombatPrepAugmentLane();
        public CombatPrepAugmentLane Upgrade { get; set; } = new CombatPrepAugmentLane();
    }

    internal sealed class CombatPrepOs
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DifficultyKey { get; set; }
        public bool Unlocked { get; set; }
        public double EnergyBaseTime { get; set; }
        public double MagicBaseTime { get; set; }
        // OSFactor() depends on OSLimit(). Ratio to the OS used to capture profile speeds.
        public double EnergySpeedFactor { get; set; } = 1;
        public double MagicSpeedFactor { get; set; } = 1;
        // Actual W bonus formula: BonusMultiplier * (BonusOffset +
        // BonusCoefficient * (energyLevel+EnergyLevelOffset)^EnergyExponent *
        //                    (magicLevel+MagicLevelOffset)^MagicExponent).
        // Adapter MUST capture these from controller/getters/IL, not infer from current ETA.
        // IL 98: ((1+E/100)*(1+M/25))^.8; MEH: (1+E/5)*(1+2*M);
        // XL: ((1+6*E)*(1+40*M))^1.0499999523162842. Express with level offsets,
        // exponents and coefficient (e.g. 98 offsets 100,25; exponents .8; offset 0;
        // coefficient 100^(-.8)*25^(-.8)). Engine clamps result to at least 1.
        public double BonusMultiplier { get; set; } = 1;
        public double BonusOffset { get; set; } = 1;
        public double BonusCoefficient { get; set; }
        public double EnergyLevelOffset { get; set; }
        public double MagicLevelOffset { get; set; }
        public double EnergyExponent { get; set; } = .5;
        public double MagicExponent { get; set; } = .5;
    }

    internal sealed class CombatPrepPhase
    {
        public string Kind { get; set; } // Augments, AT, Wandoos, NGU, Background
        public string GearGoal { get; set; }
        public string GearProfileKey { get; set; }
        public double DurationSeconds { get; set; }
        public int AugmentTierId { get; set; } = -1;
        public long AugmentTarget { get; set; }
        public long UpgradeTarget { get; set; }
        // Execution must share capped equal lanes for Augments, or these explicit AT weights.
        public double AtEnergyWeight { get; set; } = .5;
        public long AtEnergyTarget { get; set; }
        public long AtMagicTarget { get; set; }
        public bool BackgroundMagicWandoos { get; set; }
        // Background/Wandoos: separate fractions of each usable pool for W, capped at one tick/bar.
        // Background fractions are additionally bounded by snapshot.Background{Energy,Magic}Fraction.
        // Augments + BackgroundMagicWandoos: energy remains full pair sharing; only magic
        // uses MagicResourceFraction. AT always uses its weights, never funds W.
        public double EnergyResourceFraction { get; set; } = 1;
        public double MagicResourceFraction { get; set; } = 1;
        // Targets gate phase advancement if late; global deadline NEVER extends. No W of either
        // lane before any later AT, or during AT. AT -> Augments may background magic AFTER
        // the AT gate. NGU receives capped leftovers, not simulated growth.
    }

    internal sealed class CombatPrepAugmentEnd
    {
        public int Id { get; set; }
        public long AugmentLevel { get; set; }
        public double AugmentProgress { get; set; }
        public bool AugmentPaid { get; set; }
        public long UpgradeLevel { get; set; }
        public double UpgradeProgress { get; set; }
        public bool UpgradePaid { get; set; }
    }

    internal sealed class CombatPrepAlternative
    {
        public string Family { get; set; }
        public string Name { get; set; }
        public int OsId { get; set; }
        public CombatPrepPhase[] Phases { get; set; } = new CombatPrepPhase[0];
        public CombatPrepAugmentEnd[] Augments { get; set; } = new CombatPrepAugmentEnd[0];
        public double EndAugmentTotal { get; set; }
        public double EndWandoosBonus { get; set; }
        public double TotalBonus { get; set; }
        public double Gain { get; set; }
        public double BossAttack { get; set; }
        public double BossDefense { get; set; }
        public long AtEnergyLevel { get; set; }
        public double AtEnergyProgress { get; set; }
        public long AtMagicLevel { get; set; }
        public double AtMagicProgress { get; set; }
        public long WandoosEnergyLevel { get; set; }
        public double WandoosEnergyProgress { get; set; }
        public long WandoosMagicLevel { get; set; }
        public double WandoosMagicProgress { get; set; }
        public long WandoosEnergyAllocation { get; set; }
        public long WandoosMagicAllocation { get; set; }
        public double GoldRemaining { get; set; }
        public double GoldSpent { get; set; }
        public double EnergyResourceSeconds { get; set; }
        public double MagicResourceSeconds { get; set; }
        public long EndEnergyPool { get; set; }
        public long EndMagicPool { get; set; }
        // Allocation opportunity cost, NOT W training time or dump-equivalent seconds.
        public double ResourceFocusEquivalentSeconds { get; set; }
        public bool ResourceFocusCostKnown { get; set; }
        public string ResourceReferenceProfileKey { get; set; }
        public double EnergyResourceFocusEquivalentSeconds { get; set; }
        public double MagicResourceFocusEquivalentSeconds { get; set; }
        public bool EnergyResourceFocusCostKnown { get; set; }
        public bool MagicResourceFocusCostKnown { get; set; }
        // Completed W level gains / reference full-focus dumps per second, each lane separately.
        // Reference uses this alternative's OS, FINAL AT levels and bootup at prep end.
        // Partial-bar changes are reported above but are not counted as completed dump gains.
        public string ReferenceWandoosProfileKey { get; set; }
        public double EnergyReferenceDumpsPerSecond { get; set; }
        public double MagicReferenceDumpsPerSecond { get; set; }
        public double EnergyFocusEquivalentSeconds { get; set; }
        public double MagicFocusEquivalentSeconds { get; set; }
        // A zero reference rate yields seconds=0 and Known=false, never Infinity/NaN.
        public bool EnergyFocusEquivalentKnown { get; set; }
        public bool MagicFocusEquivalentKnown { get; set; }
        public double NguEnergyResourceSeconds { get; set; }
        public double NguMagicResourceSeconds { get; set; }
    }

    internal sealed class CombatPrepPlanResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public double DurationSeconds { get; set; }
        public string[] Assumptions { get; set; } = new string[0];
        public int EvaluatedCandidates { get; set; }
        public CombatPrepAlternative Baseline { get; set; }
        public CombatPrepAlternative Recommendation { get; set; }
        public CombatPrepAlternative[] Alternatives { get; set; } = new CombatPrepAlternative[0];
    }

    internal static class CombatPrepPlanner
    {
        public const double DefaultSeconds = 7200;
        public const double MaximumSeconds = 86400;
        public const int MaximumCandidates = 1440;
        public const double AllocationRefreshSeconds = .5;
        private const double Tiny = 1e-9;
        private const double Limit = 1e300;
        private static readonly double[] Fractions = { .125, .25, .5, .75 };
        private static readonly double[] Weights = { 0, .25, .5, .75, 1 };

        private sealed class Bar
        {
            internal long Level;
            internal double Progress;
            internal bool Paid;
            internal Bar Copy() { return (Bar) MemberwiseClone(); }
        }

        private sealed class State
        {
            internal Bar AtE, AtM, WE, WM;
            internal Bar[] Aug, Upg;
            internal int Ticks;
            internal int HoldTicks;
            internal long HeldFirst, HeldSecond, EPool, MPool;
            internal long WDumpE, WDumpM;
            internal double Gold, Spent, NetGoldPerSecond, EnergyCost, MagicCost, NguE, NguM;
            internal List<CombatPrepPhase> Phases = new List<CombatPrepPhase>();
            internal State Copy()
            {
                var copy = (State) MemberwiseClone();
                copy.AtE = AtE.Copy(); copy.AtM = AtM.Copy();
                copy.WE = WE.Copy(); copy.WM = WM.Copy();
                copy.Aug = Aug.Select(x => x.Copy()).ToArray();
                copy.Upg = Upg.Select(x => x.Copy()).ToArray();
                copy.Phases = new List<CombatPrepPhase>(Phases);
                return copy;
            }
        }

        // All caches belong to ONE call and therefore cannot reuse another save/support/gear
        // context. Callers must not mutate the DTOs concurrently with Plan/Project.
        private sealed class Context
        {
            internal CombatPrepSnapshot Snapshot;
            internal int Horizon, Evaluated;
            internal Dictionary<string, State> AugCache = new Dictionary<string, State>();
            internal Dictionary<string, State> AtCache = new Dictionary<string, State>();
            internal List<CombatPrepAlternative> Candidates = new List<CombatPrepAlternative>();
        }

        public static CombatPrepPlanResult Plan(CombatPrepSnapshot snapshot, double seconds = DefaultSeconds)
        {
            var result = new CombatPrepPlanResult();
            try
            {
                Validate(snapshot);
                if (!Finite(seconds) || seconds <= 0 || seconds > MaximumSeconds)
                    throw new ArgumentException("Duration must be positive and at most 86400 seconds.");
                var c = new Context { Snapshot = snapshot, Horizon = Ticks(seconds) };
                if (c.Horizon < 1) throw new ArgumentException("Duration must include at least one game tick.");
                result.DurationSeconds = c.Horizon / 50d;
                var currentOs = snapshot.OperatingSystems.Single(x => x.Id == snapshot.CurrentOsId);
                var baseline = Finish(c, Initial(snapshot), currentOs, "Baseline", "Current snapshot");
                result.Baseline = baseline;
                c.Candidates.Add(baseline);
                var augProfiles = snapshot.AugmentsCompleted ? new CombatPrepProfile[0] : Profiles(snapshot, "Augments");
                var atProfiles = Profiles(snapshot, "AT");
                var wProfiles = Profiles(snapshot, "Wandoos");
                var nguProfiles = Profiles(snapshot, "NGU");

                // Score every unlocked tier at each coarse duration on each augment profile.
                // Subsequent phases never spend augment gold, so for the SAME profile/time the
                // largest actual augment total dominates the other tier choices exactly.
                foreach (var p in augProfiles)
                    foreach (var tier in snapshot.Augments.Where(x => x.Augment.Unlocked || x.Upgrade.Unlocked))
                        foreach (var ticks in Fractions.Select(f => (int) (c.Horizon * f)).Concat(new[] { c.Horizon }).Distinct().OrderBy(x => x))
                        {
                            var state = AugPrefix(c, p, tier.Id, ticks);
                            if (ticks == c.Horizon)
                                Add(c, state, currentOs, "FullAugments", "Full augment pair " + tier.Id);
                        }

                foreach (var os in LegalOperatingSystems(snapshot))
                {
                    foreach (var p in augProfiles)
                    {
                        var prefix = BestAugPrefix(c, p, c.Horizon);
                        if (prefix == null) continue;
                        var state = prefix.Copy();
                        OverlayAugMagic(c, state, os, p);
                        Add(c, state, os, "FullAugments", "Full augment pair with budgeted magic Wandoos");
                    }
                    foreach (var p in wProfiles)
                    {
                        var state = Initial(snapshot);
                        Run(c, state, os, Phase("Wandoos", p, c.Horizon));
                        Add(c, state, os, "WandoosNoAT", "Wandoos without AT");
                    }
                    var atW = new List<CombatPrepAlternative>();
                    foreach (var at in snapshot.FreeAdvancedTraining || snapshot.PaidAtForbidden ? new CombatPrepProfile[0] : atProfiles)
                        foreach (var w in wProfiles)
                            foreach (var fraction in Fractions)
                                foreach (var weight in Weights)
                                {
                                    var state = Initial(snapshot);
                                    var ticks = (int) (c.Horizon * fraction);
                                    Run(c, state, os, AtPhase(at, ticks, weight));
                                    Run(c, state, os, Phase("Wandoos", w, c.Horizon - ticks));
                                    var candidate = Add(c, state, os, "ATThenWandoos", "AT then Wandoos");
                                    atW.Add(candidate);
                                }

                    var bestAt = atW.OrderByDescending(x => x.TotalBonus)
                        .SelectMany(x => x.Phases.Where(p => p.Kind == "AT")).FirstOrDefault();
                    var atProfile = bestAt == null ? (snapshot.RequirePhaseProfiles ? atProfiles.FirstOrDefault() : snapshot.Current)
                        : FindProfile(snapshot, bestAt.GearProfileKey);

                    var augW = new List<CombatPrepAlternative>();
                    foreach (var p in augProfiles)
                        foreach (var fraction in Fractions)
                        {
                            var ticks = (int) (c.Horizon * fraction);
                            var prefix = BestAugPrefix(c, p, ticks);
                            if (prefix == null) continue;
                            foreach (var background in new[] { false, true })
                            {
                                var prepared = prefix.Copy();
                                if (background) OverlayAugMagic(c, prepared, os, p);
                                foreach (var w in wProfiles)
                                {
                                    var state = prepared.Copy();
                                    Run(c, state, os, Phase("Wandoos", w, c.Horizon - ticks));
                                    var candidate = Add(c, state, os, "AugmentsThenWandoos", "Augments then Wandoos");
                                    augW.Add(candidate);
                                }
                                foreach (var ngu in nguProfiles)
                                {
                                    var state = prepared.Copy();
                                    Run(c, state, os, Phase("Background", ngu, c.Horizon - ticks,
                                        snapshot.BackgroundEnergyFraction, snapshot.BackgroundMagicFraction));
                                    Add(c, state, os, "AugmentsThenNGUBackground", "Augments then NGU with budgeted Wandoos");
                                }
                            }
                        }

                    // Only background-free prefixes may precede AT. Never reuse a scored
                    // magic-W overlay and pretend that its W gains happened after training.
                    var seeds = augW.Where(x => !x.Phases[0].BackgroundMagicWandoos)
                        .OrderByDescending(x => x.TotalBonus)
                        .GroupBy(x => GearIdentity(FindProfile(snapshot, x.Phases[0].GearProfileKey)) + ":" + x.Phases[0].AugmentTierId)
                        .Select(x => x.First()).Take(2).ToArray();
                    if (seeds.Length > 0 && atProfile != null && !snapshot.FreeAdvancedTraining && !snapshot.PaidAtForbidden)
                    {
                        var a = seeds[0].Phases[0];
                        var p = FindProfile(snapshot, a.GearProfileKey);
                        var prefix = AugPrefix(c, p, a.AugmentTierId, Ticks(a.DurationSeconds)).Copy();
                        foreach (var w in wProfiles)
                            foreach (var fraction in Fractions.Take(2))
                                foreach (var weight in Weights)
                                {
                                    var state = prefix.Copy();
                                    var ticks = (int) ((c.Horizon - state.Ticks) * fraction);
                                    Run(c, state, os, AtPhase(atProfile, ticks, weight));
                                    Run(c, state, os, Phase("Wandoos", w, c.Horizon - state.Ticks));
                                    Add(c, state, os, "AugmentsATWandoos", "Augments then AT then Wandoos");
                                }
                    }

                    // A bounded AT-first refinement enables budgeted magic W during augments
                    // only AFTER all planned AT. Three paired timings, not a Cartesian time grid.
                    var atFirstFractions = new[] { .025, .125, .25 };
                    foreach (var seed in snapshot.FreeAdvancedTraining || snapshot.PaidAtForbidden || atProfile == null ? new CombatPrepAlternative[0] : seeds)
                        for (var timing = 0; timing < atFirstFractions.Length; timing++)
                            foreach (var weight in new[] { .5 })
                            {
                                var prepared = Initial(snapshot);
                                Run(c, prepared, os, AtPhase(atProfile, (int) (c.Horizon * atFirstFractions[timing]), weight));
                                if (!prepared.Phases.Any(x => x.Kind == "AT")) continue;
                                var source = seed.Phases[0];
                                var aug = Phase("Augments", FindProfile(snapshot, source.GearProfileKey),
                                    (int) ((c.Horizon - prepared.Ticks) * Fractions[3 - timing]), 1, snapshot.BackgroundMagicFraction);
                                aug.AugmentTierId = source.AugmentTierId;
                                aug.BackgroundMagicWandoos = true;
                                Run(c, prepared, os, aug);
                                if (prepared.Phases.Count != 2 || prepared.Ticks >= c.Horizon) continue;
                                foreach (var w in wProfiles)
                                {
                                    var state = prepared.Copy();
                                    Run(c, state, os, Phase("Wandoos", w, c.Horizon - state.Ticks));
                                    Add(c, state, os, "ATThenAugmentsWandoos", "AT then Augments with magic Wandoos then Wandoos");
                                }
                                foreach (var ngu in nguProfiles)
                                {
                                    var state = prepared.Copy();
                                    Run(c, state, os, Phase("Background", ngu, c.Horizon - state.Ticks,
                                        snapshot.BackgroundEnergyFraction, snapshot.BackgroundMagicFraction));
                                    Add(c, state, os, "ATThenAugmentsNGUBackground", "AT then Augments with magic Wandoos then NGU background");
                                }
                            }
                    foreach (var p in nguProfiles)
                    {
                        var state = Initial(snapshot);
                        Run(c, state, os, Phase("Background", p, c.Horizon,
                            snapshot.BackgroundEnergyFraction, snapshot.BackgroundMagicFraction));
                        Add(c, state, os, "BackgroundOnly", "NGU with budgeted Wandoos");
                    }
                    RefineTimings(c, os);
                }
                // Goal/profile aliases for the same actual gear and executable policy are
                // one alternative, not several identically labeled choices. Dedup BEFORE top-N.
                var ranked = c.Candidates.Where(x => !snapshot.RequirePhaseProfiles || x.Phases.Length > 0)
                    .OrderByDescending(x => x.TotalBonus).ThenBy(x => x.EnergyResourceSeconds + x.MagicResourceSeconds)
                    .GroupBy(x => PolicyIdentity(snapshot, x), StringComparer.Ordinal).Select(x => x.First()).ToArray();
                if (ranked.Length == 0) throw new ArgumentException("No executable scored phase profiles are available.");
                var selected = new List<CombatPrepAlternative> { baseline };
                foreach (var item in ranked.GroupBy(x => x.Family).Select(x => x.First()).Concat(ranked))
                    if (!selected.Contains(item) && selected.Count < 12) selected.Add(item);
                result.Alternatives = selected.OrderByDescending(x => x.TotalBonus).ToArray();
                result.Recommendation = ranked[0];
                result.EvaluatedCandidates = c.Evaluated;
                result.Assumptions = new[] {
                    "Bounded named-family search plus three shrinking local time steps around six family winners per OS; not globally optimal. Joint seeds use one AT profile and at most two augment profile/tier choices.",
                    "Fixed captured totals/caps/power, income and formula factors; no future loot, NGU growth, bars regeneration or unmodeled support growth.",
                    "Each policy starts at Current usable pools and clamps them to each visited profile's min(total,cap); cap loss never refills. Unvisited profiles cannot reduce its pools.",
                    snapshot.PhaseSpecificSupport ? "Phase-specific support is included in captured profiles; support switching costs are not simulated." : "Current support is frozen; differing support keys are rejected.",
                    snapshot.WandoosBootupDurationSeconds > 0 ? "Native bootup ramp and completion set bonus advance on the shared clock, even without dumps. W allocations refresh on the captured cadence. OS switching preserves bootup and requires no dumps, levels or progress." : "Bootup uses the supplied profile early/late approximation. OS switching preserves bootup and requires no dumps, levels or progress.",
                    "50 ticks/second, at most one level per tick, overflow discarded. Double arithmetic models formulas, not bit-identical Unity float rounding. Rates <=1e-9 make no progress.",
                    "AT/augment allocations are held until the next captured refresh; level completions do not recap or redistribute. Target/phase transitions are observed at refresh boundaries, never beyond the global deadline.",
                    "Gold is paid at bar start, augment before upgrade. The active profile's captured net income (snapshot fallback when absent) accrues once after each tick. Unaffordable lanes wait; partial bars are already paid.",
                    "AT-3 and AT-4 consume energy only. No Wandoos on either lane before later AT or during AT, including magic augment background. AT-first augments may background magic only after the AT gate.",
                    snapshot.FreeAdvancedTraining ? "Wish 190 grants one free AT level/tick after native unlock in every phase, regardless of zero-allocation menu targets. Paid AT phases are excluded. W uses pre-tick AT levels; Unity callback ordering can differ by one tick." : "Paid AT uses captured targets and held allocations; no future AT unlock is assumed.",
                    "Alternatives deduplicate actual OwnedGearRefs/support plus executable policy fields, not profile/goal aliases. ResourceFocusEquivalentSeconds measures allocation cost only.",
                    "Energy/MagicFocusEquivalentSeconds are completed W gains divided by separate reference-profile dump rates at final AT and end bootup, with tick rounding and saturation; zero rates are marked unknown.",
                    "Projected phase durations are budgets; execution must gate on returned augment/AT targets when late and stop at the original global deadline, never extend it.",
                    snapshot.CaptureAssumptions ?? "No additional capture assumptions supplied."
                };
                result.Success = true;
            }
            catch (ArgumentException error) { result.Error = error.Message; }
            return result;
        }

        // Deterministic evaluation for an explicit policy, also used by offline differential
        // checks. It never mutates input phases; returned phases carry projected level targets.
        public static CombatPrepAlternative Project(CombatPrepSnapshot snapshot, int osId, CombatPrepPhase[] phases)
        {
            Validate(snapshot);
            if (phases == null || phases.Length > 3) throw new ArgumentException("At most three ordered phases expected.");
            var os = LegalOperatingSystems(snapshot).SingleOrDefault(x => x.Id == osId);
            // Baseline-only current OS may be locked, but execution may not train it.
            if (os == null) throw new ArgumentException("OS is locked, wrong difficulty, or switching would erase current Wandoos.");
            var seconds = phases.Sum(x => x == null ? double.NaN : x.DurationSeconds);
            if (!Finite(seconds) || seconds < 0 || seconds > MaximumSeconds) throw new ArgumentException("Invalid phase duration.");
            var c = new Context { Snapshot = snapshot, Horizon = Ticks(seconds) };
            var state = Initial(snapshot);
            var prepKinds = new HashSet<string>(StringComparer.Ordinal);
            var terminal = false;
            var priorW = false;
            double scheduledSeconds = 0;
            foreach (var phase in phases)
            {
                if (!Finite(phase.DurationSeconds) || phase.DurationSeconds < 0 || !Unit(phase.EnergyResourceFraction) ||
                    !Unit(phase.MagicResourceFraction) || !Unit(phase.AtEnergyWeight))
                    throw new ArgumentException("Invalid phase weights or duration.");
                var prep = phase.Kind == "Augments" || phase.Kind == "AT";
                if (terminal || (prep ? !prepKinds.Add(phase.Kind) :
                    phase.Kind != "Wandoos" && phase.Kind != "NGU" && phase.Kind != "Background"))
                    throw new ArgumentException("AT and Augments may occur once each in either order, followed by one terminal Wandoos/NGU/Background phase.");
                if (phase.Kind == "AT" && (priorW || phase.BackgroundMagicWandoos))
                    throw new ArgumentException("No Wandoos allocation is allowed before a later AT phase or during AT.");
                if (snapshot.FreeAdvancedTraining && phase.Kind == "AT")
                    throw new ArgumentException("Wish 190 uses passive zero-allocation AT in every phase, not a paid AT phase.");
                if (snapshot.PaidAtForbidden && phase.Kind == "AT" || snapshot.AugmentsCompleted && phase.Kind == "Augments")
                    throw new ArgumentException("Scheduled continuation cannot repeat completed preparation or buy AT after Wandoos started.");
                terminal = !prep;
                priorW |= AllowsWandoos(phase);
                scheduledSeconds += phase.DurationSeconds;
                var end = Math.Min(c.Horizon, RefreshBoundary(snapshot, Ticks(scheduledSeconds)));
                Run(c, state, os, phase, Math.Max(0, end - state.Ticks));
            }
            return Finish(c, state, os, "Explicit", "Explicit policy");
        }

        private static CombatPrepProfile[] Profiles(CombatPrepSnapshot s, string goal)
        {
            return (s.RequirePhaseProfiles ? Enumerable.Empty<CombatPrepProfile>() : new[] { s.Current })
                .Concat(s.Profiles.Where(x => x.Goal == goal)).ToArray();
        }

        private static void RefineTimings(Context c, CombatPrepOs os)
        {
            // Reserve at most 54 evaluations/OS: three two-phase and three three-phase
            // family winners, three scales, both directions at each internal boundary.
            var families = new[] { "ATThenWandoos", "AugmentsThenWandoos", "AugmentsThenNGUBackground",
                "AugmentsATWandoos", "ATThenAugmentsWandoos", "ATThenAugmentsNGUBackground" };
            foreach (var family in families)
            {
                var best = c.Candidates.Where(x => x.OsId == os.Id && x.Family == family)
                    .OrderByDescending(x => x.TotalBonus).FirstOrDefault();
                if (best == null) continue;
                foreach (var divisor in new[] { 16, 64, 256 })
                {
                    var seed = best;
                    var step = Math.Max(Ticks(c.Snapshot.AllocationRefreshSeconds), RefreshBoundary(c.Snapshot, c.Horizon / divisor));
                    for (var boundary = 0; boundary < seed.Phases.Length - 1; boundary++)
                        foreach (var direction in new[] { -1, 1 })
                        {
                            var durations = seed.Phases.Select(x => Ticks(x.DurationSeconds)).ToArray();
                            durations[boundary] += direction * step; durations[boundary + 1] -= direction * step;
                            if (durations.Any(x => x <= 0)) continue;
                            var state = Initial(c.Snapshot);
                            for (var i = 0; i < seed.Phases.Length; i++)
                            {
                                var phase = seed.Phases[i];
                                if (i == 0 && phase.Kind == "Augments")
                                {
                                    var profile = FindProfile(c.Snapshot, phase.GearProfileKey);
                                    state = AugPrefix(c, profile, phase.AugmentTierId, durations[i]).Copy();
                                    if (phase.BackgroundMagicWandoos) OverlayAugMagic(c, state, os, profile);
                                }
                                else Run(c, state, os, phase, durations[i]);
                            }
                            // Run copies policy fields and computes actual gates, never interpolates targets.
                            var candidate = Add(c, state, os, family, seed.Name);
                            if (candidate.TotalBonus > best.TotalBonus) best = candidate;
                        }
                }
            }
        }

        private static CombatPrepProfile FindProfile(CombatPrepSnapshot s, string key)
        {
            var profile = s.Current.Key == key ? s.Current : s.Profiles.SingleOrDefault(x => x.Key == key);
            if (profile == null) throw new ArgumentException("Unknown gear profile key: " + key);
            return profile;
        }

        private static bool AllowsWandoos(CombatPrepPhase phase)
        {
            return phase.Kind == "Wandoos" || phase.Kind == "Background" ||
                phase.Kind == "Augments" && phase.BackgroundMagicWandoos;
        }

        private static string GearIdentity(CombatPrepProfile profile)
        {
            // References are slot-aware external identities; array enumeration order is not
            // equipment order. Length-prefixing prevents delimiter collisions in metadata.
            return string.Concat(new[] { profile.SupportKey ?? "" }.Concat(profile.OwnedGearRefs.OrderBy(x => x, StringComparer.Ordinal).Select(x => x ?? ""))
                .Select(x => x.Length.ToString(CultureInfo.InvariantCulture) + ":" + x));
        }

        private static string PolicyIdentity(CombatPrepSnapshot s, CombatPrepAlternative alternative)
        {
            var parts = new List<string> { alternative.OsId.ToString(CultureInfo.InvariantCulture) };
            foreach (var phase in alternative.Phases)
            {
                parts.Add(phase.Kind); parts.Add(GearIdentity(FindProfile(s, phase.GearProfileKey)));
                parts.Add(phase.DurationSeconds.ToString("R", CultureInfo.InvariantCulture));
                if (phase.Kind == "AT")
                {
                    parts.Add(phase.AtEnergyWeight.ToString("R", CultureInfo.InvariantCulture));
                    parts.Add(phase.AtEnergyTarget.ToString(CultureInfo.InvariantCulture));
                    parts.Add(phase.AtMagicTarget.ToString(CultureInfo.InvariantCulture));
                }
                else if (phase.Kind == "Augments")
                {
                    parts.Add(phase.AugmentTierId.ToString(CultureInfo.InvariantCulture));
                    parts.Add(phase.AugmentTarget.ToString(CultureInfo.InvariantCulture));
                    parts.Add(phase.UpgradeTarget.ToString(CultureInfo.InvariantCulture));
                    parts.Add(phase.BackgroundMagicWandoos ? "magic-W" : "no-W");
                    if (phase.BackgroundMagicWandoos) parts.Add(phase.MagicResourceFraction.ToString("R", CultureInfo.InvariantCulture));
                }
                else if (phase.Kind != "NGU")
                {
                    parts.Add(phase.EnergyResourceFraction.ToString("R", CultureInfo.InvariantCulture));
                    parts.Add(phase.MagicResourceFraction.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            return string.Concat(parts.Select(x => x.Length.ToString(CultureInfo.InvariantCulture) + ":" + x));
        }

        private static IEnumerable<CombatPrepOs> LegalOperatingSystems(CombatPrepSnapshot s)
        {
            var fresh = s.WandoosEnergy.Level == 0 && s.WandoosMagic.Level == 0 &&
                s.WandoosEnergy.Progress == 0 && s.WandoosMagic.Progress == 0 &&
                s.WandoosEnergyAllocation == 0 && s.WandoosMagicAllocation == 0;
            return s.OperatingSystems.Where(x => x.Unlocked && x.DifficultyKey == s.DifficultyKey && (fresh || x.Id == s.CurrentOsId));
        }

        private static CombatPrepPhase Phase(string kind, CombatPrepProfile p, int ticks, double energyFraction = 1, double magicFraction = 1)
        {
            return new CombatPrepPhase { Kind = kind, GearGoal = p.Goal, GearProfileKey = p.Key,
                DurationSeconds = ticks / 50d, EnergyResourceFraction = energyFraction, MagicResourceFraction = magicFraction };
        }

        private static CombatPrepPhase AtPhase(CombatPrepProfile p, int ticks, double weight)
        {
            var phase = Phase("AT", p, ticks); phase.AtEnergyWeight = weight; return phase;
        }

        private static State Initial(CombatPrepSnapshot s)
        {
            return new State {
                AtE = new Bar { Level = s.AtEnergy.Level, Progress = s.AtEnergy.Progress },
                AtM = new Bar { Level = s.AtMagic.Level, Progress = s.AtMagic.Progress },
                WE = new Bar { Level = s.WandoosEnergy.Level, Progress = s.WandoosEnergy.Progress },
                WM = new Bar { Level = s.WandoosMagic.Level, Progress = s.WandoosMagic.Progress },
                Aug = s.Augments.Select(x => new Bar { Level = x.Augment.Level, Progress = x.Augment.Progress, Paid = x.Augment.Paid || x.Augment.Progress > 0 }).ToArray(),
                Upg = s.Augments.Select(x => new Bar { Level = x.Upgrade.Level, Progress = x.Upgrade.Progress, Paid = x.Upgrade.Paid || x.Upgrade.Progress > 0 }).ToArray(),
                EPool = Pool(s.Current.EnergyTotal, s.Current.EnergyCap), MPool = Pool(s.Current.MagicTotal, s.Current.MagicCap),
                Gold = s.Gold, NetGoldPerSecond = s.Current.NetGoldPerSecond ?? s.NetGoldPerSecond,
                WDumpE = s.WandoosEnergyAllocation, WDumpM = s.WandoosMagicAllocation
            };
        }

        private static State AugPrefix(Context c, CombatPrepProfile p, int tierId, int ticks)
        {
            ticks = Math.Min(c.Horizon, RefreshBoundary(c.Snapshot, ticks));
            // This cache ONLY contains BackgroundMagicWandoos=false prefixes. True overlays
            // are projected on copies and never stored under the background-free cache key.
            var stem = p.Key + ":" + (p.NetGoldPerSecond ?? c.Snapshot.NetGoldPerSecond).ToString("R", CultureInfo.InvariantCulture) + ":" + tierId + ":False:";
            var key = stem + ticks;
            State found;
            if (c.AugCache.TryGetValue(key, out found)) return found;
            // Continue from an earlier checkpoint with the SAME profile/tier; no cross-context cache.
            var previous = c.AugCache.Where(x => x.Key.StartsWith(stem, StringComparison.Ordinal) && x.Value.Ticks < ticks)
                .OrderByDescending(x => x.Value.Ticks).Select(x => x.Value).FirstOrDefault();
            var state = previous == null ? Initial(c.Snapshot) : previous.Copy();
            p = ApplyProfile(c.Snapshot, state, p);
            var phase = Phase("Augments", p, ticks - state.Ticks); phase.AugmentTierId = tierId;
            SimAug(c.Snapshot, state, p, phase.AugmentTierId, ticks - state.Ticks);
            phase.DurationSeconds = ticks / 50d;
            SetAugTargets(c.Snapshot, state, phase);
            state.Phases = new List<CombatPrepPhase> { phase };
            c.AugCache.Add(key, state);
            return state;
        }

        private static State BestAugPrefix(Context c, CombatPrepProfile p, int ticks)
        {
            if (ticks <= 0) return null;
            return c.Snapshot.Augments.Where(x => x.Augment.Unlocked || x.Upgrade.Unlocked)
                .Select(x => AugPrefix(c, p, x.Id, ticks)).OrderByDescending(x => AugTotal(c.Snapshot, x)).FirstOrDefault();
        }

        private static void OverlayAugMagic(Context c, State state, CombatPrepOs os, CombatPrepProfile p)
        {
            p = ApplyProfile(c.Snapshot, state, p);
            var used = SimW(c.Snapshot, state, os, p, 0, state.Ticks, 0, Budget(p.MagicTotal, p.MagicCap, c.Snapshot.BackgroundMagicFraction));
            // AugPrefix already credits unused magic to NGU; replace that share, don't double count.
            state.MagicCost = AddSafe(state.MagicCost, used.Item2);
            state.NguM = Math.Max(0, state.NguM - used.Item2);
            var phase = state.Phases[0];
            state.Phases[0] = new CombatPrepPhase { Kind = phase.Kind, GearGoal = phase.GearGoal,
                GearProfileKey = phase.GearProfileKey, DurationSeconds = phase.DurationSeconds,
                AugmentTierId = phase.AugmentTierId, AugmentTarget = phase.AugmentTarget,
                UpgradeTarget = phase.UpgradeTarget, BackgroundMagicWandoos = true,
                EnergyResourceFraction = 1, MagicResourceFraction = c.Snapshot.BackgroundMagicFraction };
        }

        private static void Run(Context c, State state, CombatPrepOs os, CombatPrepPhase input, int actualTicks = -1)
        {
            if (input.Kind == "AT" && (input.BackgroundMagicWandoos || state.Phases.Any(AllowsWandoos)))
                throw new ArgumentException("No Wandoos allocation is allowed before a later AT phase or during AT.");
            var ticks = Math.Min(actualTicks < 0 ? RefreshBoundary(c.Snapshot, Ticks(input.DurationSeconds)) : actualTicks, c.Horizon - state.Ticks);
            if (ticks <= 0) return; // A zero-budget/unvisited phase cannot lose capacity or start W.
            var p = ApplyProfile(c.Snapshot, state, FindProfile(c.Snapshot, input.GearProfileKey));
            state.HoldTicks = 0;
            var phase = Phase(input.Kind, p, ticks, input.EnergyResourceFraction, input.MagicResourceFraction);
            phase.AugmentTierId = input.AugmentTierId; phase.AtEnergyWeight = input.AtEnergyWeight;
            phase.BackgroundMagicWandoos = input.BackgroundMagicWandoos;
            if (phase.Kind == "Augments")
            {
                var before = state.Ticks;
                SimAug(c.Snapshot, state, p, phase.AugmentTierId, ticks);
                SetAugTargets(c.Snapshot, state, phase);
                if (phase.BackgroundMagicWandoos)
                {
                    phase.MagicResourceFraction = Math.Min(phase.MagicResourceFraction, c.Snapshot.BackgroundMagicFraction);
                    var used = SimW(c.Snapshot, state, os, p, before, ticks, 0,
                        Budget(p.MagicTotal, p.MagicCap, phase.MagicResourceFraction));
                    state.MagicCost = AddSafe(state.MagicCost, used.Item2);
                    state.NguM = Math.Max(0, state.NguM - used.Item2);
                }
            }
            else if (phase.Kind == "AT")
            {
                var weightIndex = Array.IndexOf(Weights, phase.AtEnergyWeight);
                var stem = p.Key + ":" + state.NetGoldPerSecond.ToString("R", CultureInfo.InvariantCulture) + ":" + weightIndex + ":" + p.EnergyTotal + ":" + p.MagicTotal + ":";
                var key = stem + ticks;
                // Explicit non-grid weights are valid policies, but are never cached by a shared -1 key.
                State delta;
                if (weightIndex < 0 || !c.AtCache.TryGetValue(key, out delta))
                {
                    var previous = weightIndex < 0 ? null : c.AtCache.Where(x => x.Key.StartsWith(stem, StringComparison.Ordinal) && x.Value.Ticks < ticks)
                        .OrderByDescending(x => x.Value.Ticks).Select(x => x.Value).FirstOrDefault();
                    delta = previous == null ? Initial(c.Snapshot) : previous.Copy();
                    delta.EPool = p.EnergyTotal; delta.MPool = p.MagicTotal;
                    delta.NetGoldPerSecond = state.NetGoldPerSecond;
                    SimAt(c.Snapshot, delta, p, ticks - delta.Ticks, phase.AtEnergyWeight);
                    delta.Ticks = ticks;
                    if (weightIndex >= 0) c.AtCache[key] = delta;
                }
                state.AtE = delta.AtE.Copy(); state.AtM = delta.AtM.Copy();
                state.EnergyCost = AddSafe(state.EnergyCost, delta.EnergyCost);
                state.NguE = AddSafe(state.NguE, delta.NguE); state.NguM = AddSafe(state.NguM, delta.NguM);
                // AT checkpoints contain training/cost deltas only. Credit this phase's
                // income once on the incoming state, not the checkpoint's initial gold.
                AdvanceGold(state, ticks); state.Ticks += ticks;
                state.WDumpE = 0; state.WDumpM = 0;
                phase.AtEnergyTarget = state.AtE.Level; phase.AtMagicTarget = state.AtM.Level;
            }
            else
            {
                if (phase.Kind == "Background")
                {
                    phase.EnergyResourceFraction = Math.Min(phase.EnergyResourceFraction, c.Snapshot.BackgroundEnergyFraction);
                    phase.MagicResourceFraction = Math.Min(phase.MagicResourceFraction, c.Snapshot.BackgroundMagicFraction);
                }
                var used = SimW(c.Snapshot, state, os, p, state.Ticks, ticks,
                    Budget(p.EnergyTotal, p.EnergyCap, phase.Kind == "NGU" ? 0 : phase.EnergyResourceFraction),
                    Budget(p.MagicTotal, p.MagicCap, phase.Kind == "NGU" ? 0 : phase.MagicResourceFraction));
                state.EnergyCost = AddSafe(state.EnergyCost, used.Item1); state.MagicCost = AddSafe(state.MagicCost, used.Item2);
                state.NguE = AddSafe(state.NguE, Math.Max(0, Pool(p.EnergyTotal, p.EnergyCap) * (ticks / 50d) - used.Item1));
                state.NguM = AddSafe(state.NguM, Math.Max(0, Pool(p.MagicTotal, p.MagicCap) * (ticks / 50d) - used.Item2));
                AdvanceGold(state, ticks); state.Ticks += ticks;
                AdvanceFreeAt(c.Snapshot, state);
            }
            state.Phases.Add(phase);
        }

        private static CombatPrepProfile ApplyProfile(CombatPrepSnapshot s, State state, CombatPrepProfile profile)
        {
            state.EPool = Math.Min(state.EPool, Pool(profile.EnergyTotal, profile.EnergyCap));
            state.MPool = Math.Min(state.MPool, Pool(profile.MagicTotal, profile.MagicCap));
            state.NetGoldPerSecond = profile.NetGoldPerSecond ?? s.NetGoldPerSecond;
            var effective = profile.Copy();
            effective.EnergyTotal = state.EPool; effective.MagicTotal = state.MPool;
            return effective;
        }

        private static int RefreshBoundary(CombatPrepSnapshot s, int ticks)
        {
            var interval = Ticks(s.AllocationRefreshSeconds);
            return ((ticks + interval - 1) / interval) * interval;
        }

        private static void SetAugTargets(CombatPrepSnapshot s, State state, CombatPrepPhase phase)
        {
            var index = Array.FindIndex(s.Augments, x => x.Id == phase.AugmentTierId);
            if (index < 0) throw new ArgumentException("Unknown augment tier.");
            phase.AugmentTarget = state.Aug[index].Level; phase.UpgradeTarget = state.Upg[index].Level;
        }

        private static void SimAt(CombatPrepSnapshot s, State state, CombatPrepProfile p, int ticks, double weight)
        {
            var remaining = ticks;
            var budget = Pool(p.EnergyTotal, p.EnergyCap);
            var eCoefficient = Rate(Math.Sqrt(p.EnergyPower), p.AtSpeed, 1, 50 * s.AtEnergy.BaseTime, 1, 0);
            var mCoefficient = Rate(Math.Sqrt(p.EnergyPower), p.AtSpeed, 1, 50 * s.AtMagic.BaseTime, 1, 0);
            while (remaining > 0)
            {
                if (state.HoldTicks == 0)
                {
                    var shares = ResourceAllocation.SplitPair(Cap(AtUnit(s.AtEnergy, state.AtE, eCoefficient, true), budget),
                        Cap(AtUnit(s.AtMagic, state.AtM, mCoefficient, true), budget), budget, weight);
                    state.HeldFirst = shares[0]; state.HeldSecond = shares[1];
                    state.HoldTicks = Ticks(s.AllocationRefreshSeconds);
                }
                var eq = ProgressRate(AtUnit(s.AtEnergy, state.AtE, eCoefficient, false), state.HeldFirst);
                var mq = ProgressRate(AtUnit(s.AtMagic, state.AtM, mCoefficient, false), state.HeldSecond);
                var step = Math.Min(Math.Min(remaining, state.HoldTicks), Math.Min(Until(state.AtE, eq), Until(state.AtM, mq)));
                Advance(state.AtE, eq, step); Advance(state.AtM, mq, step);
                state.EnergyCost = AddSafe(state.EnergyCost, ((double) state.HeldFirst + state.HeldSecond) * (step / 50d));
                state.NguE = AddSafe(state.NguE, (budget - state.HeldFirst - state.HeldSecond) * (step / 50d));
                remaining -= step; state.HoldTicks -= step;
            }
            state.NguM = AddSafe(state.NguM, Pool(p.MagicTotal, p.MagicCap) * (ticks / 50d));
        }

        private static double AtUnit(CombatPrepAtLane lane, Bar bar, double coefficient, bool checkTarget)
        {
            return !lane.Unlocked || bar.Level == long.MaxValue || (checkTarget && lane.TargetLevel > 0 && bar.Level >= lane.TargetLevel)
                ? 0 : coefficient / ((double) bar.Level + 1);
        }

        private static void SimAug(CombatPrepSnapshot s, State state, CombatPrepProfile p, int tierId, int ticks)
        {
            var index = Array.FindIndex(s.Augments, x => x.Id == tierId);
            if (index < 0) throw new ArgumentException("Unknown augment tier.");
            var tier = s.Augments[index]; var a = state.Aug[index]; var u = state.Upg[index];
            var budget = Pool(p.EnergyTotal, p.EnergyCap);
            var remaining = ticks;
            while (remaining > 0)
            {
                var aqUnit = AugUnit(tier.Augment, a, p); var uqUnit = AugUnit(tier.Upgrade, u, p);
                var ac = Cost(tier.Augment, a); var uc = Cost(tier.Upgrade, u);
                if (state.HoldTicks == 0)
                {
                    var aCap = a.Paid || ac <= state.Gold ? Cap(aqUnit, budget) : 0;
                    var reserved = aCap > 0 && !a.Paid ? ac : 0;
                    var uCap = u.Paid || uc <= state.Gold - reserved ? Cap(uqUnit, budget) : 0;
                    var shares = ResourceAllocation.SplitPair(aCap, uCap, budget, .5);
                    state.HeldFirst = shares[0]; state.HeldSecond = shares[1];
                    state.HoldTicks = Ticks(s.AllocationRefreshSeconds);
                }
                var ae = state.HeldFirst; var ue = state.HeldSecond;
                if (ae > 0 && !a.Paid && ac <= state.Gold) Pay(state, a, ac);
                if (ue > 0 && !u.Paid && uc <= state.Gold) Pay(state, u, uc);
                var aq = a.Paid ? ProgressRate(aqUnit, ae) : 0;
                var uq = u.Paid ? ProgressRate(uqUnit, ue) : 0;
                var step = Math.Min(Math.Min(remaining, state.HoldTicks), Math.Min(Until(a, aq), Until(u, uq)));
                // Only already-funded lanes may resume on income between refreshes. An
                // excluded lane gets no allocation until the next executor refresh.
                if (state.NetGoldPerSecond > 0)
                {
                    if (ae > 0 && !a.Paid && aqUnit > 0 && ac > state.Gold) step = Math.Min(step, WaitForGold(ac, state.Gold, state.NetGoldPerSecond));
                    if (ue > 0 && !u.Paid && uqUnit > 0 && uc > state.Gold) step = Math.Min(step, WaitForGold(uc, state.Gold, state.NetGoldPerSecond));
                }
                Advance(a, aq, step); Advance(u, uq, step);
                state.EnergyCost = AddSafe(state.EnergyCost, ((double) ae + ue) * (step / 50d));
                state.NguE = AddSafe(state.NguE, (budget - ae - ue) * (step / 50d));
                AdvanceGold(state, step); remaining -= step; state.HoldTicks -= step;
            }
            state.NguM = AddSafe(state.NguM, Pool(p.MagicTotal, p.MagicCap) * (ticks / 50d));
            state.Ticks += ticks;
            AdvanceFreeAt(s, state);
            state.WDumpE = 0; state.WDumpM = 0;
        }

        private static long FreeAtLevel(CombatPrepAtLane lane, int ticks)
        {
            return !lane.Unlocked ? lane.Level : lane.Level + Math.Min((long) ticks, long.MaxValue - lane.Level);
        }

        private static void AdvanceFreeAt(CombatPrepSnapshot s, State state)
        {
            if (!s.FreeAdvancedTraining || state.Ticks == 0) return;
            if (s.AtEnergy.Unlocked) { state.AtE.Level = FreeAtLevel(s.AtEnergy, state.Ticks); state.AtE.Progress = 0; }
            if (s.AtMagic.Unlocked) { state.AtM.Level = FreeAtLevel(s.AtMagic, state.Ticks); state.AtM.Progress = 0; }
        }

        private static double AugUnit(CombatPrepAugmentLane lane, Bar bar, CombatPrepProfile p)
        {
            return !lane.Unlocked || bar.Level == long.MaxValue ? 0 :
                Rate(p.EnergyPower, p.AugmentSpeedFactor, lane.ProgressCoefficient, 1, (double) bar.Level + 1, lane.LevelExponent);
        }

        private static double Cost(CombatPrepAugmentLane lane, Bar bar)
        {
            var ordinary = lane.CostCoefficient * Math.Pow((double) bar.Level + 1, lane.CostExponent);
            if (Finite(ordinary)) return Math.Min(Limit, ordinary);
            return Rate(lane.CostCoefficient, 1, 1, 1, (double) bar.Level + 1, -lane.CostExponent);
        }

        private static void Pay(State state, Bar bar, double cost)
        {
            state.Gold = Math.Max(0, state.Gold - cost); state.Spent = AddSafe(state.Spent, cost); bar.Paid = true;
        }

        private static int WaitForGold(double cost, double gold, double income)
        {
            return CeilTicks((cost - gold) / (income / 50d));
        }

        private static void AdvanceGold(State state, int ticks)
        {
            state.Gold = Math.Max(0, Math.Min(Limit, state.Gold + (state.NetGoldPerSecond / 50d) * ticks));
        }

        private static Tuple<double, double> SimW(CombatPrepSnapshot s, State state, CombatPrepOs os, CombatPrepProfile p,
            int start, int ticks, long energyBudget, long magicBudget)
        {
            double energyCost = 0, magicCost = 0;
            var remaining = ticks;
            var boundary = Ticks(Math.Min(MaximumSeconds, p.BootupUntilSeconds));
            var hold = 0;
            long e = 0, m = 0;
            while (remaining > 0)
            {
                var early = start < boundary;
                var step = early ? Math.Min(remaining, boundary - start) : remaining;
                var ramp = s.WandoosBootupDurationSeconds > 0 && s.WandoosBootupElapsedSeconds + start / 50d < s.WandoosBootupDurationSeconds;
                var varying = ramp || s.FreeAdvancedTraining;
                if (varying)
                {
                    // Allocation entry uses the pre-tick rate, then holds through the refresh.
                    // Split at boot completion: the full-set 1.1 jump is NOT part of the ramp.
                    step = Math.Min(step, hold > 0 ? hold : Ticks(s.AllocationRefreshSeconds));
                    if (ramp) step = Math.Min(step, CeilTicks((s.WandoosBootupDurationSeconds - s.WandoosBootupElapsedSeconds) * 50 - start));
                    if (s.FreeAdvancedTraining)
                    {
                        var eUntilMax = long.MaxValue - s.AtEnergy.Level - start;
                        var mUntilMax = long.MaxValue - s.AtMagic.Level - start;
                        if (s.AtEnergy.Unlocked && eUntilMax > 0) step = (int) Math.Min(step, eUntilMax);
                        if (s.AtMagic.Unlocked && mUntilMax > 0) step = (int) Math.Min(step, mUntilMax);
                    }
                }
                var eUnit = WUnit(s, os, p, true, state.AtE.Level, start);
                var mUnit = WUnit(s, os, p, false, state.AtM.Level, start);
                if (hold == 0)
                {
                    e = state.WE.Level == long.MaxValue ? 0 : Math.Min(energyBudget, Cap(eUnit, energyBudget));
                    m = state.WM.Level == long.MaxValue ? 0 : Math.Min(magicBudget, Cap(mUnit, magicBudget));
                    if (varying) hold = Ticks(s.AllocationRefreshSeconds);
                }
                if (hold > 0) step = Math.Min(step, hold);
                var eTicks = varying ? AdvanceVarying(s, state.WE, os, p, true, state.AtE.Level, start, step, e) : AdvanceConstant(state.WE, ProgressRate(eUnit, e), step);
                var mTicks = varying ? AdvanceVarying(s, state.WM, os, p, false, state.AtM.Level, start, step, m) : AdvanceConstant(state.WM, ProgressRate(mUnit, m), step);
                energyCost = AddSafe(energyCost, e * (eTicks / 50d)); magicCost = AddSafe(magicCost, m * (mTicks / 50d));
                state.WDumpE = state.WE.Level == long.MaxValue ? 0 : e;
                state.WDumpM = state.WM.Level == long.MaxValue ? 0 : m;
                hold = Math.Max(0, hold - step);
                start += step; remaining -= step;
            }
            return Tuple.Create(energyCost, magicCost);
        }

        private static double WUnit(CombatPrepSnapshot s, CombatPrepOs os, CombatPrepProfile p, bool energy, long atLevel, int time)
        {
            var lane = energy ? s.AtEnergy : s.AtMagic;
            if (s.FreeAdvancedTraining) atLevel = FreeAtLevel(lane, time);
            var bootup = BootupFactor(s, p, energy, time);
            return energy
                ? Rate(p.WandoosEnergySpeedWithoutAt, AddSafe(1, Multiply(s.AtEnergy.LevelFactor, atLevel)),
                    Multiply(os.EnergySpeedFactor, bootup), os.EnergyBaseTime, 1, 0)
                : Rate(p.WandoosMagicSpeedWithoutAt, AddSafe(1, Multiply(s.AtMagic.LevelFactor, atLevel)),
                    Multiply(os.MagicSpeedFactor, bootup), os.MagicBaseTime, 1, 0);
        }

        private static double BootupFactor(CombatPrepSnapshot s, CombatPrepProfile p, bool energy, int time)
        {
            var early = time < Ticks(Math.Min(MaximumSeconds, p.BootupUntilSeconds));
            return s.WandoosBootupDurationSeconds > 0
                ? s.WandoosBootupElapsedSeconds + time / 50d >= s.WandoosBootupDurationSeconds ? s.WandoosBootupCompletionFactor
                    : (s.WandoosBootupElapsedSeconds + time / 50d) / s.WandoosBootupDurationSeconds
                : energy ? early ? p.EarlyEnergyBootup : p.LateEnergyBootup : early ? p.EarlyMagicBootup : p.LateMagicBootup;
        }

        private static int AdvanceVarying(CombatPrepSnapshot s, Bar bar, CombatPrepOs os, CombatPrepProfile p,
            bool energy, long atLevel, int start, int ticks, long allocation)
        {
            if (allocation == 0 || bar.Level == long.MaxValue) return 0;
            var first = ProgressRate(WUnit(s, os, p, energy, atLevel, start), allocation);
            if (first >= 1) return AdvanceConstant(bar, 1, ticks); // all factors are nondecreasing
            var lane = energy ? s.AtEnergy : s.AtMagic;
            if (s.FreeAdvancedTraining) atLevel = FreeAtLevel(lane, start);
            var at = AddSafe(1, Multiply(lane.LevelFactor, atLevel));
            var atStep = s.FreeAdvancedTraining && lane.Unlocked && atLevel < long.MaxValue ? lane.LevelFactor : 0;
            var boot = BootupFactor(s, p, energy, start);
            var bootStep = s.WandoosBootupDurationSeconds > 0 && s.WandoosBootupElapsedSeconds + start / 50d < s.WandoosBootupDurationSeconds
                ? 1 / (50 * s.WandoosBootupDurationSeconds) : 0;
            var coefficient = energy ? Rate(p.WandoosEnergySpeedWithoutAt, os.EnergySpeedFactor, allocation, os.EnergyBaseTime, 1, 0)
                : Rate(p.WandoosMagicSpeedWithoutAt, os.MagicSpeedFactor, allocation, os.MagicBaseTime, 1, 0);
            var exactRate = coefficient == 0 || coefficient >= Limit / AddSafe(at, atStep * (ticks - 1));
            // Short held windows only. This preserves tiny-rate cutoffs and overflow discard at
            // every tick. Factors are affine within this window (split before discontinuities).
            for (var t = 0; t < ticks; t++)
            {
                var q = exactRate ? ProgressRate(WUnit(s, os, p, energy, atLevel, start + t), allocation)
                    : Multiply(Multiply(coefficient, AddSafe(at, atStep * t)), AddSafe(boot, bootStep * t));
                if (q <= Tiny) continue;
                if (q >= 1) return t + AdvanceConstant(bar, 1, ticks - t);
                bar.Progress += q;
                if (bar.Progress >= 1) { bar.Level++; bar.Progress = 0; }
                if (bar.Level == long.MaxValue) return t + 1;
            }
            return ticks;
        }

        private static double ReferenceDumpRate(CombatPrepSnapshot s, State state, CombatPrepOs os, CombatPrepProfile p, bool energy)
        {
            if ((energy ? state.WE.Level : state.WM.Level) == long.MaxValue) return 0;
            var pool = energy ? Math.Min(state.EPool, Pool(p.EnergyTotal, p.EnergyCap)) : Math.Min(state.MPool, Pool(p.MagicTotal, p.MagicCap));
            var unit = WUnit(s, os, p, energy, energy ? state.AtE.Level : state.AtM.Level, state.Ticks);
            var q = ProgressRate(unit, Cap(unit, pool));
            return q <= Tiny ? 0 : 50d / CeilTicks(1 / q);
        }

        // Constant Wandoos q permits direct batching of millions of levels. Each new bar
        // takes ceil(1/q) ticks; fractional overflow from a completion is never carried.
        private static int AdvanceConstant(Bar bar, double q, int ticks)
        {
            if (q <= Tiny || ticks <= 0 || bar.Level == long.MaxValue) return 0;
            var first = Until(bar, q);
            if (first > ticks) { bar.Progress = Math.Min(.9999999999999999, bar.Progress + q * ticks); return ticks; }
            Advance(bar, q, first);
            var left = ticks - first;
            var perLevel = CeilTicks(1 / q);
            var levels = left / perLevel;
            if (long.MaxValue - bar.Level <= levels)
            {
                var active = first + (int) ((long.MaxValue - bar.Level) * perLevel);
                bar.Level = long.MaxValue; bar.Progress = 0; return active;
            }
            bar.Level += levels;
            bar.Progress = q * (left % perLevel);
            return ticks;
        }

        private static int Until(Bar bar, double q)
        {
            return q <= Tiny || bar.Level == long.MaxValue ? int.MaxValue : CeilTicks((1 - bar.Progress) / q);
        }

        private static void Advance(Bar bar, double q, int ticks)
        {
            if (q <= Tiny || bar.Level == long.MaxValue) return;
            var progress = bar.Progress + q * ticks;
            // Completion is defined by ceil((1-progress)/q) ticks. Multiplication at that
            // boundary may round just below one; do not turn the same bar into an extra tick.
            if (ticks >= Until(bar, q))
            {
                bar.Level++; bar.Progress = 0; bar.Paid = false;
            }
            else bar.Progress = progress;
        }

        private static long Pool(long total, long cap) { return Math.Min(total, cap); }
        private static long Budget(long total, long cap, double fraction)
        {
            var pool = Pool(total, cap);
            return Math.Min(pool, ToLong(Math.Floor(pool * fraction)));
        }
        private static long ToLong(double value) { return value >= long.MaxValue ? long.MaxValue : value <= 0 ? 0 : (long) value; }
        private static int Ticks(double seconds) { return (int) Math.Floor(seconds * 50 + 1e-7); }
        private static int CeilTicks(double ticks) { return ticks >= int.MaxValue ? int.MaxValue : Math.Max(1, (int) Math.Ceiling(ticks)); }
        private static long Cap(double unit, long budget)
        {
            return unit <= 0 || ProgressRate(unit, budget) <= Tiny ? 0 : Math.Min(budget, ToLong(Math.Ceiling(1 / unit)));
        }
        private static double ProgressRate(double unit, long allocation) { var q = Multiply(unit, allocation); return q <= Tiny ? 0 : q; }
        private static double Rate(double a, double b, double c, double divisor, double level, double exponent)
        {
            if (a <= 0 || b <= 0 || c <= 0) return 0;
            // Keep ordinary exact binary rates exact; logs are only an extreme-range fallback.
            var numerator = a * b * c;
            var denominator = Math.Pow(level, exponent);
            var ordinary = numerator / divisor / denominator;
            if (Finite(numerator) && numerator > 0 && Finite(denominator) && denominator > 0 && ordinary > 0 && Finite(ordinary))
                return Math.Min(Limit, ordinary);
            var log = Math.Log(a) + Math.Log(b) + Math.Log(c) - Math.Log(divisor) - exponent * Math.Log(level);
            return log >= Math.Log(Limit) ? Limit : Math.Exp(log);
        }
        private static double Multiply(double a, double b) { return a <= 0 || b <= 0 ? 0 : a > Limit / b ? Limit : a * b; }
        private static double AddSafe(double a, double b) { return Math.Min(Limit, a + b); }
        private static double Pow(double a, double b) { return Math.Min(Limit, Math.Pow(a, b)); }

        private static double AugTotal(CombatPrepSnapshot s, State state)
        {
            double total = 1;
            for (var i = 0; i < s.Augments.Length; i++)
                total = AddSafe(total, Multiply(s.Augments[i].BaseBoost,
                    Multiply(AddSafe(Pow(state.Upg[i].Level, 2), 1), Pow(state.Aug[i].Level, s.Augments[i].TierExponent))));
            return Math.Max(1, Multiply(total, s.AugmentGlobalFactor));
        }

        private static double WBonus(CombatPrepOs os, State state)
        {
            if (state.WE.Level == 0 && state.WM.Level == 0) return 1;
            return Math.Max(1, Multiply(os.BonusMultiplier, AddSafe(os.BonusOffset, Multiply(os.BonusCoefficient,
                Multiply(Pow(AddSafe(state.WE.Level, os.EnergyLevelOffset), os.EnergyExponent),
                    Pow(AddSafe(state.WM.Level, os.MagicLevelOffset), os.MagicExponent))))));
        }

        private static CombatPrepAlternative Add(Context c, State state, CombatPrepOs os, string family, string name)
        {
            if (c.Evaluated >= MaximumCandidates) throw new ArgumentException("Combat prep policy grid exceeded its declared candidate bound.");
            var candidate = Finish(c, state, os, family, name);
            c.Candidates.Add(candidate); c.Evaluated++; return candidate;
        }

        private static CombatPrepAlternative Finish(Context c, State state, CombatPrepOs os, string family, string name)
        {
            var s = c.Snapshot; var a = AugTotal(s, state); var w = WBonus(os, state);
            var total = Multiply(a, w);
            // Normalize against the same accepted formula representation as the baseline,
            // avoiding a spurious gain from Unity float -> double parity tolerance.
            var initial = Initial(s);
            var baselineOs = s.OperatingSystems.Single(x => x.Id == s.CurrentOsId);
            var gain = Multiply(a / AugTotal(s, initial), w / WBonus(baselineOs, initial));
            var reference = s.Profiles.FirstOrDefault(x => x.Goal == "NGU") ?? s.Current;
            var referenceE = reference.ReferenceEnergyPerSecond > 0 ? reference.ReferenceEnergyPerSecond : Pool(reference.EnergyTotal, reference.EnergyCap);
            var referenceM = reference.ReferenceMagicPerSecond > 0 ? reference.ReferenceMagicPerSecond : Pool(reference.MagicTotal, reference.MagicCap);
            referenceE = Math.Min(state.EPool, Math.Min(Pool(reference.EnergyTotal, reference.EnergyCap), referenceE));
            referenceM = Math.Min(state.MPool, Math.Min(Pool(reference.MagicTotal, reference.MagicCap), referenceM));
            var wReference = string.IsNullOrEmpty(s.ReferenceWandoosProfileKey)
                ? s.Profiles.FirstOrDefault(x => x.Goal == "Wandoos") ?? s.Current : FindProfile(s, s.ReferenceWandoosProfileKey);
            var eRate = ReferenceDumpRate(s, state, os, wReference, true);
            var mRate = ReferenceDumpRate(s, state, os, wReference, false);
            return new CombatPrepAlternative {
                Family = family, Name = name, OsId = os.Id, Phases = state.Phases.ToArray(),
                Augments = s.Augments.Select((x, i) => new CombatPrepAugmentEnd { Id = x.Id,
                    AugmentLevel = state.Aug[i].Level, AugmentProgress = state.Aug[i].Progress, AugmentPaid = state.Aug[i].Paid,
                    UpgradeLevel = state.Upg[i].Level, UpgradeProgress = state.Upg[i].Progress, UpgradePaid = state.Upg[i].Paid }).ToArray(),
                EndAugmentTotal = a, EndWandoosBonus = w, TotalBonus = total, Gain = gain,
                BossAttack = Math.Max(100, Multiply(s.BaselineBossAttack, gain)), BossDefense = Math.Max(100, Multiply(s.BaselineBossDefense, gain)),
                AtEnergyLevel = state.AtE.Level, AtEnergyProgress = state.AtE.Progress,
                AtMagicLevel = state.AtM.Level, AtMagicProgress = state.AtM.Progress,
                WandoosEnergyLevel = state.WE.Level, WandoosEnergyProgress = state.WE.Progress,
                WandoosMagicLevel = state.WM.Level, WandoosMagicProgress = state.WM.Progress,
                WandoosEnergyAllocation = state.WDumpE, WandoosMagicAllocation = state.WDumpM,
                GoldRemaining = state.Gold, GoldSpent = state.Spent,
                EnergyResourceSeconds = state.EnergyCost, MagicResourceSeconds = state.MagicCost,
                EndEnergyPool = state.EPool, EndMagicPool = state.MPool,
                ResourceFocusEquivalentSeconds = AddSafe(referenceE <= 0 ? 0 : Math.Min(Limit, state.EnergyCost / referenceE),
                    referenceM <= 0 ? 0 : Math.Min(Limit, state.MagicCost / referenceM)),
                ResourceFocusCostKnown = (referenceE > 0 || state.EnergyCost == 0) && (referenceM > 0 || state.MagicCost == 0),
                ResourceReferenceProfileKey = reference.Key,
                EnergyResourceFocusEquivalentSeconds = referenceE <= 0 ? 0 : Math.Min(Limit, state.EnergyCost / referenceE),
                MagicResourceFocusEquivalentSeconds = referenceM <= 0 ? 0 : Math.Min(Limit, state.MagicCost / referenceM),
                EnergyResourceFocusCostKnown = referenceE > 0 || state.EnergyCost == 0,
                MagicResourceFocusCostKnown = referenceM > 0 || state.MagicCost == 0,
                ReferenceWandoosProfileKey = wReference.Key,
                EnergyReferenceDumpsPerSecond = eRate, MagicReferenceDumpsPerSecond = mRate,
                EnergyFocusEquivalentSeconds = eRate <= 0 ? 0 : Math.Min(Limit, (state.WE.Level - s.WandoosEnergy.Level) / eRate),
                MagicFocusEquivalentSeconds = mRate <= 0 ? 0 : Math.Min(Limit, (state.WM.Level - s.WandoosMagic.Level) / mRate),
                EnergyFocusEquivalentKnown = eRate > 0, MagicFocusEquivalentKnown = mRate > 0,
                NguEnergyResourceSeconds = state.NguE, NguMagicResourceSeconds = state.NguM
            };
        }

        private static void Validate(CombatPrepSnapshot s)
        {
            if (s == null || s.Current == null || s.Profiles == null || s.Augments == null || s.OperatingSystems == null ||
                s.AtEnergy == null || s.AtMagic == null || s.WandoosEnergy == null || s.WandoosMagic == null)
                throw new ArgumentException("Incomplete combat prep snapshot.");
            if (s.Augments.Length > 7 || s.OperatingSystems.Length > 3 || s.Profiles.Length > 8)
                throw new ArgumentException("Snapshot exceeds seven tiers, three OSes or two projected profiles per goal.");
            if (!s.WandoosAvailable) throw new ArgumentException("Wandoos is unavailable or disabled in this snapshot context.");
            if (!Nonnegative(s.WandoosBootupElapsedSeconds, s.WandoosBootupDurationSeconds) ||
                !Positive(s.WandoosBootupCompletionFactor) || s.WandoosBootupCompletionFactor < 1)
                throw new ArgumentException("Invalid native Wandoos bootup clock or completion factor.");
            if (!Finite(s.AllocationRefreshSeconds) || s.AllocationRefreshSeconds < .02 || s.AllocationRefreshSeconds > MaximumSeconds ||
                Math.Abs(s.AllocationRefreshSeconds * 50 - Math.Round(s.AllocationRefreshSeconds * 50)) > 1e-7)
                throw new ArgumentException("Allocation refresh must be a positive whole number of ticks, at most 86400 seconds.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in new[] { s.Current }.Concat(s.Profiles))
            {
                if (p == null || string.IsNullOrEmpty(p.Key) || p.Key.Length > 128 || p.Key.IndexOf(':') >= 0 || !keys.Add(p.Key) || p.OwnedGearRefs == null ||
                    p.EnergyTotal < 0 || p.EnergyCap < 0 || p.MagicTotal < 0 || p.MagicCap < 0 ||
                    !Nonnegative(p.EnergyPower, p.MagicPower, p.AtSpeed, p.AugmentSpeedFactor, p.WandoosEnergySpeedWithoutAt,
                        p.WandoosMagicSpeedWithoutAt, p.BootupUntilSeconds, p.EarlyEnergyBootup, p.LateEnergyBootup,
                        p.EarlyMagicBootup, p.LateMagicBootup, p.ReferenceEnergyPerSecond, p.ReferenceMagicPerSecond) ||
                    p.NetGoldPerSecond.HasValue && (!Finite(p.NetGoldPerSecond.Value) || Math.Abs(p.NetGoldPerSecond.Value) > Limit))
                    throw new ArgumentException("Invalid resource or gear profile (keys must be unique and colon-free).");
                if (!s.PhaseSpecificSupport && p.SupportKey != s.Current.SupportKey)
                    throw new ArgumentException("Projected support differs from frozen current support.");
            }
            if (s.Profiles.Any(x => x.Goal != "Augments" && x.Goal != "AT" && x.Goal != "Wandoos" && x.Goal != "NGU") ||
                s.Profiles.GroupBy(x => x.Goal).Any(x => x.Count() > 2)) throw new ArgumentException("Unknown goal or more than two profiles per goal.");
            if (!string.IsNullOrEmpty(s.ReferenceWandoosProfileKey))
            {
                var reference = FindProfile(s, s.ReferenceWandoosProfileKey);
                if (reference != s.Current && reference.Goal != "Wandoos") throw new ArgumentException("Reference W profile must be Current or Wandoos gear.");
            }
            ValidateBar(s.AtEnergy.Level, s.AtEnergy.Progress); ValidateBar(s.AtMagic.Level, s.AtMagic.Progress);
            foreach (var lane in new[] { s.AtEnergy, s.AtMagic })
                if (!Positive(lane.BaseTime) || !Nonnegative(lane.LevelFactor) || lane.TargetLevel < 0)
                    throw new ArgumentException("Invalid AT formula.");
            ValidateBar(s.WandoosEnergy.Level, s.WandoosEnergy.Progress); ValidateBar(s.WandoosMagic.Level, s.WandoosMagic.Progress);
            if (s.CaptureAssumptions != null && s.CaptureAssumptions.Length > 2048)
                throw new ArgumentException("Capture assumptions exceed 2048 characters.");
            if (s.WandoosEnergyAllocation < 0 || s.WandoosMagicAllocation < 0 || !Unit(s.BackgroundEnergyFraction) || !Unit(s.BackgroundMagicFraction) ||
                !Nonnegative(s.Gold, s.BaselineBossAttack, s.BaselineBossDefense) || !Finite(s.NetGoldPerSecond) || Math.Abs(s.NetGoldPerSecond) > Limit ||
                !Positive(s.AugmentGlobalFactor) || !Positive(s.CurrentAugmentTotal) || !Positive(s.CurrentWandoosBonus))
                throw new ArgumentException("Invalid snapshot gold, totals, background fraction or boss baseline.");
            var ids = new HashSet<int>();
            foreach (var tier in s.Augments)
            {
                if (tier == null || !ids.Add(tier.Id) || tier.Augment == null || tier.Upgrade == null ||
                    !Nonnegative(tier.BaseBoost) || !Positive(tier.TierExponent)) throw new ArgumentException("Invalid augment tier.");
                foreach (var lane in new[] { tier.Augment, tier.Upgrade })
                {
                    ValidateBar(lane.Level, lane.Progress);
                    if (!Nonnegative(lane.ProgressCoefficient, lane.LevelExponent, lane.CostCoefficient, lane.CostExponent))
                        throw new ArgumentException("Invalid augment lane coefficients.");
                }
            }
            ids.Clear();
            foreach (var os in s.OperatingSystems)
                if (os == null || !ids.Add(os.Id) || !Positive(os.EnergyBaseTime) || !Positive(os.MagicBaseTime) ||
                    !Nonnegative(os.EnergySpeedFactor, os.MagicSpeedFactor, os.BonusMultiplier, os.BonusOffset, os.BonusCoefficient,
                        os.EnergyLevelOffset, os.MagicLevelOffset, os.EnergyExponent, os.MagicExponent)) throw new ArgumentException("Invalid OS formula.");
            var current = s.OperatingSystems.SingleOrDefault(x => x.Id == s.CurrentOsId && x.DifficultyKey == s.DifficultyKey);
            if (current == null) throw new ArgumentException("Current OS is missing for current difficulty.");
            var initial = Initial(s);
            if (!Parity(s.CurrentAugmentTotal, AugTotal(s, initial))) throw new ArgumentException("Augment baseline formula parity failed; recapture actual factors.");
            if (!Parity(s.CurrentWandoosBonus, WBonus(current, initial))) throw new ArgumentException("Wandoos baseline formula parity failed; recapture actual OS factors.");
        }

        private static void ValidateBar(long level, double progress)
        {
            if (level < 0 || !Finite(progress) || progress < 0 || progress >= 1) throw new ArgumentException("Invalid level or normalized progress.");
        }
        private static bool Parity(double actual, double computed) { return Math.Abs(actual - computed) / Math.Max(1, actual) <= 1e-6; }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        private static bool Nonnegative(params double[] values) { return values.All(x => Finite(x) && x >= 0 && x <= Limit); }
        private static bool Positive(double x) { return Finite(x) && x > 0 && x <= Limit; }
        private static bool Unit(double x) { return Finite(x) && x >= 0 && x <= 1; }
    }
}
