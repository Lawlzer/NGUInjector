using System;
using System.Linq;
using System.Text.Json;
using NGUInjector.Managers;

internal static partial class Program
{
    private static CombatPrepProfile GoldProfile(CombatPrepSnapshot s, string key, string goal, double? income)
    {
        var p = s.Current.Copy();
        p.Key = key; p.Goal = goal; p.SupportKey = "support-" + key; p.NetGoldPerSecond = income;
        return p;
    }

    private static CombatPrepPhase ProfilePhase(string kind, string key, double seconds)
    {
        var phase = Phase(kind, seconds); phase.GearProfileKey = key; return phase;
    }

    private static void CheckProfileGold()
    {
        CheckPhaseGoldRates();
        CheckGoldSupportWinner();
        CheckPaidGoldTiming();
        CheckGoldPrefixCaches();
        CheckFrozenGoldFallback();
    }

    private static void CheckPhaseGoldRates()
    {
        var s = Snapshot(10, 10); s.PhaseSpecificSupport = true;
        s.Gold = 100; s.NetGoldPerSecond = -4; s.Current.NetGoldPerSecond = 7;
        var tier = Tier(); tier.Augment.Unlocked = tier.Upgrade.Unlocked = false;
        s.Augments = new[] { tier };
        foreach (var kind in new[] { "AT", "Augments", "Wandoos", "Background", "NGU" })
        {
            var p = GoldProfile(s, "dedicated", kind == "Background" ? "NGU" : kind, 25);
            s.Profiles = new[] { p };
            Near(103.5, Project(s, Phase(kind, .5)).GoldRemaining, kind + " uses Current profile income, not snapshot income");
            Near(112.5, Project(s, ProfilePhase(kind, p.Key, .5)).GoldRemaining, kind + " uses dedicated profile income");
            p.NetGoldPerSecond = 0;
            Near(100, Project(s, ProfilePhase(kind, p.Key, .5)).GoldRemaining, kind + " explicit zero is not fallback");
            p.NetGoldPerSecond = -25;
            Near(87.5, Project(s, ProfilePhase(kind, p.Key, .5)).GoldRemaining, kind + " accepts signed negative profile income");
            s.Gold = 1;
            Near(0, Project(s, ProfilePhase(kind, p.Key, .5)).GoldRemaining, kind + " profile drain clamps at zero");
            s.Gold = 100; p.NetGoldPerSecond = null;
            Near(98, Project(s, ProfilePhase(kind, p.Key, .5)).GoldRemaining, kind + " null uses snapshot, not Current override");
        }

        var at = GoldProfile(s, "at", "AT", 20);
        var aug = GoldProfile(s, "aug", "Augments", -10);
        var w = GoldProfile(s, "w", "Wandoos", 30);
        s.Profiles = new[] { at, aug, w };
        var before = JsonSerializer.Serialize(s);
        var a = ProfilePhase("Augments", aug.Key, .5); a.BackgroundMagicWandoos = true;
        var result = Project(s, ProfilePhase("AT", at.Key, .5), a, ProfilePhase("Wandoos", w.Key, .5));
        Near(120, result.GoldRemaining, "AT, augment and W rates accrue once per actual phase, including magic overlay");
        Equal(before, JsonSerializer.Serialize(s), "binding profile income and pools never mutates capture DTOs");
        aug.NetGoldPerSecond = null;
        result = Project(s, ProfilePhase("AT", at.Key, .5), a, ProfilePhase("Background", "current", .5));
        Near(111.5, result.GoldRemaining, "null phase resets to snapshot fallback rather than retaining prior profile income");
        aug.NetGoldPerSecond = -10;
        result = Project(s, ProfilePhase("AT", at.Key, .1), ProfilePhase("Augments", aug.Key, .5), ProfilePhase("Wandoos", w.Key, .6));
        Near(111, result.GoldRemaining, "refresh-late gates use actual .5/.5/.2 phase income, not nominal budgets");
        Near(1.2, result.Phases.Sum(x => x.DurationSeconds), "gold accounting never extends the global deadline");
        result = Project(s, ProfilePhase("AT", at.Key, .1), ProfilePhase("Wandoos", w.Key, .4));
        Near(110, result.GoldRemaining, "late AT consumes entire horizon without crediting unvisited W income");
        Equal(1, result.Phases.Length, "zero remaining time never binds a future income profile");

        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e301, -1e301 })
        {
            w.NetGoldPerSecond = invalid;
            True(!CombatPrepPlanner.Plan(s, 1).Success, "invalid optional profile net income rejected: " + invalid);
            Throws(() => Project(s, Phase("NGU", 1)), "Project validates invalid profile net income even when unvisited");
        }
        w.NetGoldPerSecond = null; s.Current.NetGoldPerSecond = double.NaN;
        True(!CombatPrepPlanner.Plan(s, 1).Success, "Current optional income has the same validation as dedicated profiles");
    }

    private static void CheckGoldSupportWinner()
    {
        var s = Snapshot(10, 0); s.PhaseSpecificSupport = true;
        s.Gold = 0; s.NetGoldPerSecond = 0; s.Current.AugmentSpeedFactor = 0;
        var tier = Tier(); tier.Upgrade.Unlocked = false; tier.Augment.ProgressCoefficient = 1;
        s.Augments = new[] { tier };
        var first = GoldProfile(s, "aug-current-support", "Augments", 100);
        var second = GoldProfile(s, "aug-dedicated-support", "Augments", 0);
        first.SupportKey = s.Current.SupportKey;
        first.AugmentSpeedFactor = second.AugmentSpeedFactor = 1;
        // Same gear, distinct support identities and only numeric factors in the pure DTO.
        first.OwnedGearRefs = second.OwnedGearRefs = new[] { "owned-head" };
        s.Profiles = new[] { first, second };
        var result = CombatPrepPlanner.Plan(s, 2);
        True(result.Success, "gold support variant search succeeds: " + result.Error);
        Equal(first.Key, result.Recommendation.Phases.First(x => x.Kind == "Augments").GearProfileKey,
            "equal-speed support variant wins because its phase income can afford augment starts");
        True(result.Recommendation.GoldSpent > 0 && result.Recommendation.EndAugmentTotal > 1,
            "support winner actually buys and completes augment levels");
        Near(0, Project(s, ProfilePhase("Augments", second.Key, 2)).GoldSpent, "zero-income variant cannot buy an unpaid start");
        var oldScore = result.Recommendation.TotalBonus;
        first.NetGoldPerSecond = 0; second.NetGoldPerSecond = 100;
        s.Profiles = new[] { second, first };
        result = CombatPrepPlanner.Plan(s, 2);
        Equal(second.Key, result.Recommendation.Phases.First(x => x.Kind == "Augments").GearProfileKey,
            "reversing income and profile enumeration reverses the support winner with unchanged keys");
        Near(oldScore, result.Recommendation.TotalBonus, "symmetric support-rate reversal preserves the attainable objective");
        Near(0, Project(s, ProfilePhase("Augments", first.Key, 2)).GoldSpent, "same-key changed DTO cannot reuse prior call's funded prefix");

        s = Snapshot(100, 0); s.Gold = 10; s.NetGoldPerSecond = 0; s.Current.NetGoldPerSecond = 50;
        tier = Tier(); tier.Augment.ProgressCoefficient = 1; tier.Upgrade.Unlocked = false; s.Augments = new[] { tier };
        result = CombatPrepPlanner.Plan(s, .5);
        var held = Project(s, Phase("Augments", .5));
        Equal(2L, held.Augments[0].AugmentLevel, "held lane waits on active profile income between refreshes");
        Near(30, held.GoldSpent, "active profile income funds exactly two starts in tick order");
        Near(5, held.GoldRemaining, "held-lane wait and accrual use the same profile rate");
        True(result.Recommendation.TotalBonus >= held.TotalBonus, "maximum-stats objective retains the full-budget augment candidate");
    }

    private static void CheckPaidGoldTiming()
    {
        var s = Snapshot(1, 0); s.PhaseSpecificSupport = true; s.Gold = 0; s.NetGoldPerSecond = 100000;
        s.Current.NetGoldPerSecond = 0;
        var tier = Tier(); tier.Upgrade.Unlocked = false;
        tier.Augment.ProgressCoefficient = .25; tier.Augment.Progress = .75;
        s.Augments = new[] { tier };
        var future = GoldProfile(s, "future-gold", "Wandoos", 1000); s.Profiles = new[] { future };
        var result = Project(s, Phase("Augments", .5), ProfilePhase("Wandoos", future.Key, .5));
        Equal(1L, result.Augments[0].AugmentLevel, "paid partial completes without a second charge");
        Near(0, result.Augments[0].AugmentProgress, "unpaid next augment cannot borrow future-phase gold");
        True(!result.Augments[0].AugmentPaid, "unaffordable next bar remains unpaid");
        Near(0, result.GoldSpent, "paid partial never consumes future gold or snapshot fallback");
        Near(500, result.GoldRemaining, "future W income accrues only after augment work is over");

        tier.Augment.Progress = 0; tier.Augment.Paid = true;
        result = Project(s, Phase("Augments", .02), ProfilePhase("Wandoos", future.Key, .48));
        Equal(1L, result.Augments[0].AugmentLevel, "explicit paid zero-progress bar trains while gold-starved");
        Near(0, result.GoldSpent, "explicit paid start is not charged again");
        Near(0, result.GoldRemaining, "late paid augment gate cannot credit W income past the deadline");

        s.Gold = 10; s.Current.NetGoldPerSecond = -50; tier.Augment.Paid = false;
        result = Project(s, Phase("Augments", .5), ProfilePhase("Wandoos", future.Key, .5));
        Equal(1L, result.Augments[0].AugmentLevel, "negative net income does not undo an already purchased level");
        Near(10, result.GoldSpent, "negative profile income is not counted as purchases");
        Near(500, result.GoldRemaining, "gold floors at zero during drain, then future income starts fresh");

        s.Gold = 0; s.Current.NetGoldPerSecond = 0; tier.Augment.ProgressCoefficient = 1;
        var at = GoldProfile(s, "early-gold", "AT", 40); s.Profiles = new[] { at, future };
        result = Project(s, ProfilePhase("AT", at.Key, .5), Phase("Augments", .5), ProfilePhase("Wandoos", future.Key, .5));
        Equal(1L, result.Augments[0].AugmentLevel, "actual earlier AT income can fund a later augment start");
        Near(10, result.GoldSpent, "earlier AT credits only its twenty gold, not future W income");
        Near(510, result.GoldRemaining, "AT -> augment -> W conserves income and purchases without cache double credit");
    }

    private static void CheckGoldPrefixCaches()
    {
        var s = Snapshot(100, 100); s.PhaseSpecificSupport = true;
        s.Gold = 1000000; s.NetGoldPerSecond = 12345; s.Current.NetGoldPerSecond = -2;
        s.Current.OwnedGearRefs = new[] { "current-gear" };
        s.Current.AtSpeed = 50; s.Current.AugmentSpeedFactor = 0;
        s.Augments = new[] { Tier(), Tier(1) };
        s.Profiles = new[] {
            GoldProfile(s, "aug-gain", "Augments", 17), GoldProfile(s, "aug-loss", "Augments", -23),
            GoldProfile(s, "at-gain", "AT", 31), GoldProfile(s, "at-loss", "AT", -37),
            GoldProfile(s, "w-gain", "Wandoos", 41), GoldProfile(s, "w-loss", "Wandoos", -43),
            GoldProfile(s, "ngu-gain", "NGU", 47), GoldProfile(s, "ngu-loss", "NGU", -53)
        };
        foreach (var p in s.Profiles)
        {
            p.OwnedGearRefs = new[] { p.Key + "-gear" };
            p.AugmentSpeedFactor = p.Goal == "Augments" ? 2 : 1;
            p.EnergyCap = 80; p.MagicCap = 60;
            if (p.Goal == "Wandoos") p.WandoosEnergySpeedWithoutAt = p.WandoosMagicSpeedWithoutAt = 5;
        }
        var before = JsonSerializer.Serialize(s);
        var result = CombatPrepPlanner.Plan(s, 20);
        True(result.Success, "signed profile prefix-cache search succeeds: " + result.Error);
        True(result.EvaluatedCandidates <= 1440, "profile income variants do not expand the candidate bound");
        foreach (var candidate in result.Alternatives.Where(x => x.Phases.Length > 0))
        {
            var income = candidate.Phases.Sum(phase => phase.DurationSeconds *
                (new[] { s.Current }.Concat(s.Profiles).Single(p => p.Key == phase.GearProfileKey).NetGoldPerSecond ?? s.NetGoldPerSecond));
            Near(s.Gold + income - candidate.GoldSpent, candidate.GoldRemaining,
                "cached " + candidate.Family + " conserves actual phase income minus purchases");
            var replay = Project(s, candidate.Phases);
            Near(candidate.GoldSpent, replay.GoldSpent, "cached " + candidate.Family + " purchases replay");
            Near(candidate.GoldRemaining, replay.GoldRemaining, "cached " + candidate.Family + " profile income replays");
            Near(candidate.TotalBonus, replay.TotalBonus, "cached " + candidate.Family + " objective replays");
        }
        True(result.Alternatives.Any(x => x.Family == "AugmentsATWandoos"), "gold cache regression includes augment -> AT checkpoints");
        True(result.Alternatives.Any(x => x.Family == "ATThenAugmentsWandoos"), "gold cache regression includes AT -> augment recomputation");
        Equal(before, JsonSerializer.Serialize(s), "cache construction never changes captured profile rates, pools or bars");

        s.Gold = 1;
        foreach (var p in s.Profiles.Where(x => x.Goal == "Augments")) p.NetGoldPerSecond = -23;
        result = CombatPrepPlanner.Plan(s, 20);
        True(result.Success, "gold-floor prefix-cache search succeeds: " + result.Error);
        Near(0, result.Alternatives.First(x => x.Family == "FullAugments").GoldRemaining,
            "loss-only cached augment prefixes drain to zero without retaining phantom gold");
        foreach (var candidate in result.Alternatives.Where(x => x.Phases.Length > 0))
        {
            var replay = Project(s, candidate.Phases);
            Near(candidate.GoldRemaining, replay.GoldRemaining, "gold-floor cached " + candidate.Family + " income replays");
            Near(candidate.GoldSpent, replay.GoldSpent, "gold-floor cached " + candidate.Family + " purchases replay");
            Near(candidate.TotalBonus, replay.TotalBonus, "gold-floor cached " + candidate.Family + " affordability replays");
        }
    }

    private static void CheckFrozenGoldFallback()
    {
        var s = Snapshot(100, 100); s.NetGoldPerSecond = -5;
        s.Augments = new[] { Tier() }; s.Current.OwnedGearRefs = new[] { "current-gear" };
        s.Profiles = new[] { "Augments", "AT", "Wandoos", "NGU" }.Select(goal => {
            var p = s.Current.Copy(); p.Key = goal; p.Goal = goal; p.OwnedGearRefs = new[] { goal + "-gear" };
            return p;
        }).ToArray();
        var before = JsonSerializer.Serialize(s);
        var legacy = CombatPrepPlanner.Plan(s, 20);
        True(legacy.Success, "legacy frozen-support capture without optional profile income still plans");
        Equal(before, JsonSerializer.Serialize(s), "fallback capture remains unchanged after planning");
        s.Current.NetGoldPerSecond = s.NetGoldPerSecond;
        foreach (var p in s.Profiles) p.NetGoldPerSecond = s.NetGoldPerSecond;
        var explicitRates = CombatPrepPlanner.Plan(s, 20);
        Equal(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(explicitRates),
            "old frozen capture and explicit identical rates produce exactly the same result and pools");
        foreach (var p in new[] { s.Current }.Concat(s.Profiles))
        {
            Equal(100L, p.EnergyTotal, "fallback search never replaces own pool with a candidate-wide minimum");
            Equal(100L, p.MagicTotal, "fallback search preserves own captured magic pool");
        }
        s.Profiles[0].SupportKey = "other";
        True(!CombatPrepPlanner.Plan(s, 1).Success, "optional income does not relax frozen-support key validation");
    }
}
