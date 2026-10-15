using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GearSearchChecks
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type _optimizer, _candidate, _selected, _state, _stat, _goal, _mode, _item;
    private static int _assertions;

    internal static int Run(Assembly assembly)
    {
        _assertions = 0;
        _optimizer = assembly.GetType("NGUInjector.Managers.GearOptimizer", true);
        _candidate = _optimizer.GetNestedType("GearCandidate", BindingFlags.NonPublic);
        _selected = _optimizer.GetNestedType("SelectedGear", BindingFlags.NonPublic);
        _state = _optimizer.GetNestedType("SearchState", BindingFlags.NonPublic);
        _stat = _optimizer.GetNestedType("StatKey", BindingFlags.NonPublic);
        _goal = assembly.GetType("NGUInjector.Managers.GearOptimizationGoal", true);
        _mode = assembly.GetType("NGUInjector.Managers.FightScoreMode", true);
        _item = assembly.GetType("NGUInjector.Managers.ih", true);
        CheckDominance();
        CheckWeaponIds();
        CheckAccessoryIds();
        CheckFormerCap();
        CheckMainBounds();
        CheckGreedySeed();
        CheckCopyAwareImplications();
        CheckSymmetry();
        CheckNearTies();
        CheckRandomExhaustive();
        CheckStructuredExhaustive();
        CheckMixedTenSlots();
        Console.WriteLine("Gear search checks passed: " + _assertions + " assertions.");
        return _assertions;
    }

    private static void CheckDominance()
    {
        var weak = Candidate(100, "EnergyCap", 10d);
        var strong = Candidate(101, "EnergyCap", 30d);
        var strongCopy = Candidate(101, "EnergyCap", 20d);
        var survivors = Prune(List(_candidate, weak, strong, strongCopy), "EnergyCap", 2);
        Equal(2, survivors.Count, "copies of one dominating ID do not fill a two-ID cutoff");
        Equal(true, survivors.Contains(weak), "second distinct ID survives");
        Equal(true, survivors.Contains(strong), "strongest same-ID copy survives");
        Equal(false, survivors.Contains(strongCopy), "weaker same-ID copy is removed at cutoff two");

        var tie = Candidate(101, "EnergyCap", 30d);
        survivors = Prune(List(_candidate, strong, tie), "EnergyCap", 4);
        Equal(1, survivors.Count, "equal same-ID copies collapse");
        Equal(true, ReferenceEquals(strong, survivors[0]), "same-ID equality retains the first instance");

        var tradeoff = Candidate(101, "EnergyPower", 50d);
        survivors = Prune(List(_candidate, strong, strongCopy, tradeoff), "EnergyNgu", 2);
        Equal(2, survivors.Count, "same-ID nondominated tradeoffs survive");
        Equal(true, survivors.Contains(tradeoff), "power alternative is not reduced to the cap copy");

        var another = Candidate(102, "EnergyCap", 40d);
        survivors = Prune(List(_candidate, weak, strong, strongCopy, another), "EnergyCap", 2);
        Equal(false, survivors.Contains(weak), "two distinct dominating IDs can remove the third choice");
    }

    private static void CheckWeaponIds()
    {
        var first = Candidate(200, "Gold", 100d);
        var copy = Candidate(200, "Gold", 90d);
        var other = Candidate(201, "Gold", 10d);
        var weapons = List(_candidate, first, copy, other);
        var sets = (IList) Call("BuildWeaponSets", weapons, .5f);
        Equal(4, sets.Count, "weapon pairs exclude distinct instances sharing an ID");
        foreach (IList set in sets)
            Equal(false, Id(Get(set[0], "Candidate")) == Id(Get(set[1], "Candidate")), "main/offhand IDs differ");
        var best = (IList) Call("BestSingleStatWeaponSet", weapons, Enum.Parse(_stat, "Gold"), .5f);
        Equal(200, Id(Get(best[0], "Candidate")), "Gold chooses strongest mainhand ID");
        Equal(201, Id(Get(best[1], "Candidate")), "Gold offhand cannot reuse strongest ID");
        Near(.5, (double) Get(best[1], "Factor"), "Gold offhand preserves factor");
        best = (IList) Call("BestSingleStatWeaponSet", List(_candidate, copy, first), Enum.Parse(_stat, "Gold"), .5f);
        Equal(1, best.Count, "only one weapon ID leaves offhand empty");
        Equal(true, ReferenceEquals(first, Get(best[0], "Candidate")), "single-ID weapon chooses strongest copy");
        sets = (IList) Call("BuildWeaponSets", List(_candidate, first, copy), .5f);
        Equal(2, sets.Count, "multi-stat single-ID weapon retains both copy alternatives");
        foreach (IList set in sets) Equal(1, set.Count, "single-ID alternatives never fill offhand");
        sets = (IList) Call("BuildWeaponSets", weapons, 0f);
        Equal(3, sets.Count, "zero offhand factor searches single weapons");
    }

    private static void CheckAccessoryIds()
    {
        var first = Candidate(300, "EnergyCap", 100d);
        var copy = Candidate(300, "EnergyPower", 300d);
        var other = Candidate(301, "NguSpeed", 10d);
        var accessories = List(_candidate, first, copy, other);
        var groups = MainGroups(List(_candidate, Candidate(100, "EnergyCap", 1000d)));
        var weapons = WeaponSets(Candidate(200));
        var state = Search("EnergyNgu", "SqrtPowerToughness", groups, weapons, accessories, 3);
        var best = (IList) Get(state, "BestItems");
        Equal(4, best.Count, "accessory pick count is clamped to distinct IDs, not copies");
        Equal(true, ContainsCandidate(best, copy), "same-ID tradeoff copy is chosen in main-slot context");
        Equal(false, ContainsCandidate(best, first), "same accessory ID is not selected twice");
        Near(11d * 4d * 1.1, (double) Get(state, "BestScore"), "distinct-ID accessory optimum");
        state = Search("EnergyNgu", "SqrtPowerToughness", groups, weapons, List(_candidate), 0);
        Near(11, (double) Get(state, "BestScore"), "zero accessory slots still score main gear");
        Equal(2L, (long) Get(state, "EvaluatedCombinations"), "zero accessory search scores its complete seed and DFS leaf");
    }

    private static void CheckFormerCap()
    {
        // C(29,8) exceeds the old 1.5m limit. All 28 individually higher-scoring
        // cap items survive strict dominance, but the lower-ranked NGU item is essential.
        var accessories = List(_candidate);
        for (var i = 0; i < 28; i++) accessories.Add(Candidate(300 + i, "EnergyCap", 200d));
        var essential = Candidate(328, "NguSpeed", 199d);
        accessories.Add(essential);
        Equal(29, Prune(accessories, "EnergyNgu", 8).Count, "former-cap fixture retains all 29 candidates");
        var groups = MainGroups(List(_candidate, Candidate(100, "EnergyCap", 10000d, "EnergyPower", 10000d)));
        var timer = Stopwatch.StartNew();
        var state = Search("EnergyNgu", "SqrtPowerToughness", groups, WeaponSets(Candidate(200)), accessories, 8);
        timer.Stop();
        Near(115d * 101d * 2.99, (double) Get(state, "BestScore"), "exact optimum includes previously truncated candidate 29");
        Equal(true, ContainsCandidate((IList) Get(state, "BestItems"), essential), "candidate 29 is searched and selected");
        Console.WriteLine("Gear search 29-candidate regression: " + timer.ElapsedMilliseconds + "ms, " + Get(state, "EvaluatedCombinations") + " complete scores.");
    }

    private static void CheckMainBounds()
    {
        var groups = MainGroups();
        for (var slot = 0; slot < 4; slot++)
        {
            var options = List(_candidate, Candidate(100 + slot * 10, "EnergyCap", 1000d));
            for (var i = 1; i < 8; i++) options.Add(Candidate(100 + slot * 10 + i, "EnergyCap", (double) i));
            groups.Add(options);
        }
        var state = Search("EnergyCap", "SqrtPowerToughness", groups, WeaponSets(Candidate(200)), List(_candidate), 0);
        Near(4100, (double) Get(state, "BestScore"), "main-slot upper bound preserves optimum");
        Equal(1L, (long) Get(state, "EvaluatedCombinations") - (long) Get(state, "HeuristicEvaluations"),
            "main-slot bound eliminates inferior cartesian DFS branches");
    }

    private static void CheckGreedySeed()
    {
        var groups = MainGroups(List(_candidate, Candidate(100, "EnergyCap", 1000d), Candidate(101, "EnergyPower", 500d)));
        var state = Search("EnergyNgu", "SqrtPowerToughness", groups, WeaponSets(Candidate(200)),
            List(_candidate, Candidate(300, "EnergyCap", 1000d)), 1);
        Near(66, (double) Get(state, "SeedScore"), "local seed improves an earlier main-slot decision");
        Equal(3, ((IList) Get(state, "BestItems")).Count, "seed fills weapon, main, and accessory groups");
        state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(),
            WeaponSets(Candidate(200, "EnergyCap", 1000d), Candidate(201, "EnergyPower", 500d)),
            List(_candidate, Candidate(300, "EnergyCap", 1000d)), 1);
        Near(66, (double) Get(state, "SeedScore"), "local seed also improves the weapon-set group");

        var cap = Candidate(300, "EnergyCap", 1000d);
        var powerCopy = Candidate(300, "EnergyPower", 500d);
        var otherCap = Candidate(301, "EnergyCap", 1000d);
        state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)),
            List(_candidate, cap, powerCopy, otherCap), 2);
        Near(66, (double) Get(state, "SeedScore"), "local seed can change an earlier accessory copy");
        Equal(true, ContainsCandidate((IList) Get(state, "BestItems"), powerCopy), "seed preserves the actual alternative copy");
        state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)),
            List(_candidate, cap, otherCap, Candidate(302, "EnergyPower", 500d)), 2);
        Near(66, (double) Get(state, "SeedScore"), "local seed can replace a selected ID with an unused ID");

        // Two balanced items are a strict one-swap local maximum (9 versus 8.8).
        // Replacing both by complementary specialists wins, so DFS must still run.
        var accessories = List(_candidate, Candidate(300, "EnergyCap", 100d, "EnergyPower", 100d),
            Candidate(301, "EnergyCap", 100d, "EnergyPower", 100d),
            Candidate(302, "EnergyCap", 240d), Candidate(303, "EnergyPower", 240d));
        state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), accessories, 2);
        Near(9, (double) Get(state, "SeedScore"), "greedy/local-swap seed has a genuine local trap");
        Near(11.56, (double) Get(state, "BestScore"), "exact DFS escapes the heuristic local trap");
        Near(Exhaustive("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), accessories, 2),
            (double) Get(state, "BestScore"), "greedy-loss optimum agrees with exhaustive oracle");
    }

    private static void CheckCopyAwareImplications()
    {
        var weak = Candidate(300, "EnergyCap", 10d, "EnergyPower", 10d);
        var balanced = Candidate(301, "EnergyCap", 20d, "EnergyPower", 20d);
        var specialist = Candidate(301, "EnergyCap", 1000d);
        foreach (var accessories in new[] {List(_candidate, weak, balanced, specialist), List(_candidate, specialist, balanced, weak)})
        {
            var state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), accessories, 2);
            Near(12.21, (double) Get(state, "BestScore"), "dominating copy never forces the exact copy of an already selected ID");
            Equal(true, ContainsCandidate((IList) Get(state, "BestItems"), specialist), "tradeoff specialist remains eligible");
            Equal(true, ContainsCandidate((IList) Get(state, "BestItems"), weak), "weak copy complements the specialist");
            SearchAgainWithoutSeed(state);
            Near(12.21, (double) Get(state, "BestScore"), "exact copy-aware DFS is correct even without the protective seeded incumbent");
        }

        // A dominates only one B alternative; topological ordering must still put A first.
        var accessoriesWithCopies = List(_candidate,
            Candidate(300, "EnergyCap", 10d, "EnergyPower", 10d), Candidate(300, "EnergyPower", 200d),
            Candidate(301, "EnergyCap", 20d, "EnergyPower", 20d),
            Candidate(302, "EnergyCap", 30d, "EnergyPower", 30d), Candidate(302, "EnergyCap", 500d, "EnergyPower", 21d),
            Candidate(303, "EnergyCap", 200d));
        for (var picks = 1; picks <= 4; picks++)
        {
            var state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), accessoriesWithCopies, picks);
            Near(Exhaustive("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), accessoriesWithCopies, picks),
                (double) Get(state, "BestScore"), "copy-aware implications preserve optimum with " + picks + " picks");
        }

        var zeroFight = List(_candidate);
        for (var i = 0; i < 8; i++) zeroFight.Add(Candidate(300 + i, "Power", (double) (i + 1)));
        var dominatedState = Search("Fight", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), zeroFight, 3);
        Near(0, (double) Get(dominatedState, "BestScore"), "zero-toughness ties remain feasible under strict dominance");
        Equal(true, (long) Get(dominatedState, "DominancePrunedBranches") > 0, "dominance implications prune skipped dominating IDs");
    }

    private static void CheckSymmetry()
    {
        var accessories = List(_candidate);
        for (var i = 0; i < 50; i++)
            accessories.Add(Candidate(300 + i, "EnergyCap", 10d, "EnergyPower", 10d, "Gold", (double) i));
        var groups = MainGroups();
        for (var i = 0; i < 4; i++) groups.Add(List(_candidate, Candidate(100 + i)));
        var state = Search("EnergyNgu", "SqrtPowerToughness", groups, WeaponSets(Candidate(200)), accessories, 10);
        Near(4, (double) Get(state, "BestScore"), "50 identical relevant-stat IDs fill ten slots exactly");
        Equal(true, (long) Get(state, "VisitedNodes") < 2000, "50-way ties use a bounded node budget, not billions of subsets");
        Equal(true, (long) Get(state, "SymmetryPrunedBranches") > 0, "irrelevant Gold differences do not block exact symmetry");
        var best = (IList) Get(state, "BestItems");
        Equal(15, best.Count, "ten tied accessories plus all main slots are selected");
        for (var i = 0; i < 10; i++)
            Equal(true, ContainsCandidate(best, accessories[i]), "symmetry retains deterministic original-ID prefix " + i);
        Report("50 identical IDs / ten slots", state);

        accessories = List(_candidate);
        for (var i = 0; i < 16; i++)
        {
            // Reverse input order on alternating IDs and duplicate one vector within each ID.
            var cap = Candidate(300 + i, "EnergyCap", 20d);
            var power = Candidate(300 + i, "EnergyPower", 20d);
            accessories.Add(i % 2 == 0 ? cap : power);
            accessories.Add(i % 2 == 0 ? power : cap);
            accessories.Add(Candidate(300 + i, "EnergyCap", 20d, "Gold", (double) i));
        }
        state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), accessories, 6);
        Near(2.56, (double) Get(state, "BestScore"), "equivalent alternative sets retain every stat multiplicity");
        Equal(true, (long) Get(state, "VisitedNodes") < 1000, "symmetric alternatives avoid copy assignment permutations");
        var selectedIds = new HashSet<int>();
        foreach (var selected in (IList) Get(state, "BestItems"))
            Equal(true, selectedIds.Add(Id(Get(selected, "Candidate"))), "symmetric alternative picks keep distinct actual IDs");
    }

    private static void CheckNearTies()
    {
        var low = Candidate(300, "EnergyCap", 10d, "EnergyPower", 10d);
        var high = Candidate(301, "EnergyCap", 10d + 1e-9, "EnergyPower", 10d);
        var state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)), List(_candidate, low, high), 1);
        Equal(true, ContainsCandidate((IList) Get(state, "BestItems"), high), "near-equal vectors are not collapsed by a tolerance");
        var cap = Candidate(302, "EnergyCap", 200d + 1e-10);
        var power = Candidate(303, "EnergyPower", 200d + 1e-10);
        state = Search("EnergyNgu", "SqrtPowerToughness", MainGroups(), WeaponSets(Candidate(200)),
            List(_candidate, Candidate(300, "EnergyCap", 100d, "EnergyPower", 100d),
                Candidate(301, "EnergyCap", 100d, "EnergyPower", 100d), cap, power), 2);
        Equal(true, (double) Get(state, "BestScore") > (double) Get(state, "SeedScore"),
            "DFS retains an improvement smaller than the conservative bound margin");
        Equal(true, ContainsCandidate((IList) Get(state, "BestItems"), cap), "near-tie optimum retains cap specialist");
        Equal(true, ContainsCandidate((IList) Get(state, "BestItems"), power), "near-tie optimum retains power specialist");
        var relevant = (Array) Call("GetRelevantStats", Enum.Parse(_goal, "Fight"));
        var random = new Random(7193);
        for (var i = 0; i < 12; i++)
        {
            var groups = MainGroups(RandomCandidates(random, relevant, 3, 100, false));
            var weapons = WeaponSets(Candidate(200, "Power", 3d, "Toughness", 5d));
            var accessories = RandomCandidates(random, relevant, 8, 300, true);
            state = Search("Fight", "99", groups, weapons, accessories, 2);
            Near(Exhaustive("Fight", "99", groups, weapons, accessories, 2), (double) Get(state, "BestScore"),
                "undefined fight mode uses the default square-root bound " + i);
        }
    }

    private static void CheckMixedTenSlots()
    {
        foreach (var goal in new[] {"EnergyNgu", "AllNgu", "Fight"})
        {
            var relevant = (Array) Call("GetRelevantStats", Enum.Parse(_goal, goal));
            var random = new Random(19473);
            var groups = MainGroups();
            for (var i = 0; i < 4; i++) groups.Add(RandomCandidates(random, relevant, 3, 100 + i * 10, false));
            var weapons = (IList) Call("BuildWeaponSets", RandomCandidates(random, relevant, 4, 200, true), .5f);
            var accessories = RandomCandidates(random, relevant, 72, 300, true);
            var timer = Stopwatch.StartNew();
            var state = Search(goal, "SqrtPowerToughness", groups, weapons, accessories, 10);
            timer.Stop();
            Near((double) Get(state, "BestScore"), Score(goal, "SqrtPowerToughness", (IList) Get(state, "BestItems")),
                "mixed ten-slot selected items rescore " + goal);
            Equal(true, (double) Get(state, "BestScore") >= (double) Get(state, "SeedScore"), "exact search never loses its seed " + goal);
            Equal(16, ((IList) Get(state, "BestItems")).Count, "mixed ten-slot loadout is complete " + goal);
            Equal(true, (long) Get(state, "VisitedNodes") < 5000000, "mixed ten-slot deterministic node budget " + goal);
            Console.WriteLine("Gear search mixed ten-slot " + goal + ": " + timer.ElapsedMilliseconds + "ms.");
            Report(goal + " mixed / ten slots", state);
        }
    }

    private static void Report(string scenario, object state)
    {
        Console.WriteLine("Gear search " + scenario + ": " + Get(state, "VisitedNodes") + " nodes, " +
            Get(state, "EvaluatedCombinations") + " complete scores (" + Get(state, "HeuristicEvaluations") + " heuristic), " +
            Get(state, "PrunedBranches") + " prunes (" + Get(state, "DominancePrunedBranches") + " dominance, " +
            Get(state, "SymmetryPrunedBranches") + " symmetry).");
    }

    private static void CheckRandomExhaustive()
    {
        var random = new Random(73291);
        foreach (string goal in Enum.GetNames(_goal))
        foreach (string mode in Enum.GetNames(_mode))
        for (var trial = 0; trial < 8; trial++)
        {
            var relevant = (Array) Call("GetRelevantStats", Enum.Parse(_goal, goal));
            var groups = MainGroups();
            for (var slot = 0; slot < 3; slot++)
                groups.Add(RandomCandidates(random, relevant, 3, 100 + slot * 10, false));
            var weapons = RandomCandidates(random, relevant, 4, 200, true);
            var accessories = RandomCandidates(random, relevant, 8, 300, true);
            var factor = trial % 3 == 0 ? 0f : .5f;
            var weaponSets = (IList) Call("BuildWeaponSets", weapons, factor);
            var pickCount = trial % 5;
            var expected = Exhaustive(goal, mode, groups, weaponSets, accessories, pickCount);
            var state = Search(goal, mode, groups, weaponSets, accessories, pickCount);
            var scenario = goal + "/" + mode + "/" + trial;
            Near(expected, (double) Get(state, "BestScore"), "exhaustive unpruned " + scenario);

            var prunedGroups = MainGroups();
            foreach (IList group in groups) prunedGroups.Add(Prune(group, goal, 1));
            var prunedWeapons = Prune(weapons, goal, factor > 0 ? 2 : 1);
            var prunedAccessories = Prune(accessories, goal, Math.Max(1, pickCount));
            state = Search(goal, mode, prunedGroups, (IList) Call("BuildWeaponSets", prunedWeapons, factor), prunedAccessories, pickCount);
            Near(expected, (double) Get(state, "BestScore"), "exhaustive with dominance " + scenario);
            var best = (IList) Get(state, "BestItems");
            Near(expected, Score(goal, mode, best), "selected gear rescoring " + scenario);
            var ids = new HashSet<int>();
            foreach (var selected in best)
                Equal(true, ids.Add(Id(Get(selected, "Candidate"))), "selected IDs are unique " + scenario);
        }
    }

    private static IList RandomCandidates(Random random, Array relevant, int count, int firstId, bool copies)
    {
        var candidates = List(_candidate);
        for (var i = 0; i < count; i++)
        {
            var candidate = Candidate(firstId + (copies ? i / 2 : i));
            var stats = (IDictionary) Get(candidate, "Stats");
            foreach (var stat in relevant)
                if (random.Next(4) != 0) stats.Add(stat, random.Next(0, 10000) / 13d);
            candidates.Add(candidate);
        }
        return candidates;
    }

    private static void CheckStructuredExhaustive()
    {
        var random = new Random(19127);
        foreach (var goal in new[] {"EnergyNgu", "AllNgu", "Fight"})
        for (var trial = 0; trial < 24; trial++)
        {
            var relevant = (Array) Call("GetRelevantStats", Enum.Parse(_goal, goal));
            var accessories = List(_candidate);
            for (var id = 0; id < 6; id++)
            {
                var template = random.Next(5);
                for (var copy = 0; copy < (template == 4 ? 1 : 2); copy++)
                {
                    var candidate = Candidate(300 + id);
                    var stats = (IDictionary) Get(candidate, "Stats");
                    for (var s = 0; s < relevant.Length; s++)
                        stats.Add(relevant.GetValue(s), template >= 3 ? 40d + copy * 10 : ((template + copy * 2 + s) % 4) * 10d);
                    accessories.Add(candidate);
                }
            }
            var groups = MainGroups(RandomCandidates(random, relevant, 2, 100, false));
            var weapons = WeaponSets(Candidate(200));
            var mode = Enum.GetNames(_mode)[trial % 3];
            var picks = trial % 7;
            var state = Search(goal, mode, groups, weapons, accessories, picks);
            var expected = Exhaustive(goal, mode, groups, weapons, accessories, picks);
            Near(expected, (double) Get(state, "BestScore"),
                "structured ties/tradeoff copies/dominance oracle " + goal + "/" + trial);
            SearchAgainWithoutSeed(state);
            Near(expected, (double) Get(state, "BestScore"), "structured exact DFS without seed " + goal + "/" + trial);
        }
    }

    private static void SearchAgainWithoutSeed(object state)
    {
        Set(state, "BestScore", double.NegativeInfinity);
        Set(state, "BestItems", List(_selected));
        Call("SearchMainSlots", 0, 0, state);
    }

    private static double Exhaustive(string goal, string mode, IList groups, IList weaponSets, IList accessories, int picks)
    {
        var ids = new HashSet<int>();
        foreach (var accessory in accessories) ids.Add(Id(accessory));
        picks = Math.Min(picks, ids.Count);
        var best = double.NegativeInfinity;
        var selection = List(_selected);
        Action<int, int> visitAccessories = null;
        visitAccessories = (start, remaining) =>
        {
            if (remaining == 0)
            {
                best = Math.Max(best, Score(goal, mode, selection));
                return;
            }
            for (var i = start; i < accessories.Count; i++)
            {
                var candidate = accessories[i];
                if (!ids.Add(Id(candidate))) continue;
                selection.Add(Select(candidate));
                visitAccessories(i + 1, remaining - 1);
                selection.RemoveAt(selection.Count - 1);
                ids.Remove(Id(candidate));
            }
        };
        Action<int> visitMain = null;
        visitMain = index =>
        {
            if (index == groups.Count)
            {
                ids.Clear();
                visitAccessories(0, picks);
                return;
            }
            foreach (var candidate in (IList) groups[index])
            {
                selection.Add(Select(candidate));
                visitMain(index + 1);
                selection.RemoveAt(selection.Count - 1);
            }
        };
        foreach (IList set in weaponSets)
        {
            selection.Clear();
            foreach (var selected in set) selection.Add(selected);
            visitMain(0);
        }
        return best;
    }

    private static object Search(string goal, string mode, IList groups, IList weapons, IList accessories, int picks)
    {
        var state = Activator.CreateInstance(_state, true);
        Set(state, "Goal", Enum.Parse(_goal, goal));
        Set(state, "FightScoreMode", Enum.Parse(_mode, mode));
        Set(state, "RelevantStats", Call("GetRelevantStats", Enum.Parse(_goal, goal)));
        Call("SearchGear", groups, weapons, accessories, picks, state);
        return state;
    }

    private static IList MainGroups(params IList[] groups)
    {
        var result = List(typeof(List<>).MakeGenericType(_candidate));
        foreach (var group in groups) result.Add(group);
        return result;
    }

    private static IList WeaponSets(params object[] candidates)
    {
        var sets = List(typeof(List<>).MakeGenericType(_selected));
        foreach (var candidate in candidates) sets.Add(List(_selected, Select(candidate)));
        return sets;
    }

    private static IList Prune(IList candidates, string goal, int cutoff)
    {
        return (IList) Call("RemoveDominated", candidates, Call("GetRelevantStats", Enum.Parse(_goal, goal)), cutoff);
    }

    private static object Candidate(int id, params object[] stats)
    {
        var candidate = Activator.CreateInstance(_candidate, true);
        var item = FormatterServices.GetUninitializedObject(_item);
        Set(item, "id", id);
        Set(item, "equipment", FormatterServices.GetUninitializedObject(_item.GetProperty("equipment", Instance).PropertyType));
        Set(candidate, "Item", item);
        var values = (IDictionary) Get(candidate, "Stats");
        for (var i = 0; i < stats.Length; i += 2) values.Add(Enum.Parse(_stat, (string) stats[i]), (double) stats[i + 1]);
        return candidate;
    }

    private static object Select(object candidate)
    {
        var selected = Activator.CreateInstance(_selected, true);
        Set(selected, "Candidate", candidate);
        return selected;
    }

    private static int Id(object candidate) { return (int) Get(Get(candidate, "Item"), "id"); }
    private static bool ContainsCandidate(IList selection, object candidate)
    {
        foreach (var selected in selection) if (ReferenceEquals(candidate, Get(selected, "Candidate"))) return true;
        return false;
    }
    private static double Score(string goal, string mode, IList selection)
    {
        return (double) Call("ScoreItems", Enum.Parse(_goal, goal), Enum.Parse(_mode, mode), Call("GetRelevantStats", Enum.Parse(_goal, goal)), selection);
    }
    private static IList List(Type type, params object[] items)
    {
        var list = (IList) Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
        foreach (var item in items) list.Add(item);
        return list;
    }
    private static object Get(object owner, string name)
    {
        var property = owner.GetType().GetProperty(name, Instance);
        return property != null ? property.GetValue(owner) : owner.GetType().GetField(name, Instance).GetValue(owner);
    }
    private static void Set(object owner, string name, object value) { owner.GetType().GetProperty(name, Instance).SetValue(owner, value); }
    private static object Call(string method, params object[] args) { return _optimizer.GetMethod(method, Static).Invoke(null, args); }
    private static void Equal<T>(T expected, T actual, string scenario)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("Gear search " + scenario + ": expected " + expected + ", got " + actual);
    }
    private static void Near(double expected, double actual, string scenario)
    {
        _assertions++;
        if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > Math.Max(1d, Math.Abs(expected)) * 1e-10)
            throw new Exception("Gear search " + scenario + ": expected approximately " + expected + ", got " + actual);
    }
}
