using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.Managers;
using Game = NGUInjector.Main;

internal static class Program
{
    private static int assertions;
    private static int scenarios;

    private static void Main()
    {
        Run("capacities 1-7, all constraints/current sets, retention and reroll oracles", ExhaustivePolicy);
        Run("core precedes optional requests at every capacity", CorePriority);
        Run("repeated minute selections avoid an available no-op", MinuteRerolls);
        Run("Fu/Gold retention breaks non-random ties", ProtectedRetention);
        Run("Gold normal-Troll threshold and missing rows", GoldUnlock);
        Run("hard prerequisites precede core, optional requests and current utility", ExhaustiveHardPrerequisites);
        Run("unavailable contexts fail without reading cap or mutating support", Unavailable);
        Console.WriteLine("PASS pure beard selection: " + scenarios + " scenarios; " + assertions + " assertions; production SelectBeards, no manager writers.");
    }

    private static void Run(string name, Action check) { check(); Console.WriteLine("PASS " + name); }
    private static Character Reset(int cap, params int[] active)
    {
        scenarios++;
        var c = new Character(cap);
        Game.Character = c; Game.Logs.Clear();
        foreach (var id in active) { c.beards.activeBeards.Add(id); c.beards.beards[id].active = true; }
        return c;
    }
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Set(IEnumerable<int> actual, params int[] expected)
    {
        Check(new HashSet<int>(actual).SetEquals(expected), "Expected {" + string.Join(",", expected) + "}; got {" + string.Join(",", actual) + "}");
    }
    private static int[] FromMask(int mask, int[] ids) { return ids.Where((id, bit) => (mask & (1 << bit)) != 0).ToArray(); }
    private static void Reject(Action select, string diagnostic)
    {
        try { select(); }
        catch (InvalidOperationException error) { Check(error.Message.Contains(diagnostic), "wrong selection rejection: " + error.Message); return; }
        throw new InvalidOperationException("Expected selection rejection: " + diagnostic);
    }
    private static void Unchanged(Character c, int[] before)
    {
        Check(c.beards.activeBeards.SequenceEqual(before), "selection changed native active list");
        Set(c.beards.beards.Select((row, id) => new { row, id }).Where(x => x.row?.active == true).Select(x => x.id), before);
        Check(c.allBeards.Activated.Count + c.allBeards.Deactivated.Count + c.allBeards.ClearCalls + c.allBeards.Refreshes == 0,
            "pure selector called a native beard writer");
        Check(c.allDiggers.Clears + c.allDiggers.Activated.Count + c.allDiggers.LevelsSet.Count + c.allDiggers.Refreshes + c.allDiggers.UpgradeCalls == 0,
            "pure selector changed diggers or spent gold");
        Check(c.realGold == 123456 && c.diggers.diggers.All(x => x.curLevel == 10 && x.maxLevel == 100), "pure selector changed native levels or currency");
    }

    private static void ExhaustivePolicy()
    {
        var ids = Enumerable.Range(0, 7).ToArray();
        var core = new[] { 1, 3, 5, 6 };
        var constraintIds = new[] { 0, 2, 4, 6 };
        for (var cap = 1; cap <= 7; cap++)
        for (var requiredMask = 0; requiredMask < 16; requiredMask++)
        {
            var required = FromMask(requiredMask, constraintIds);
            var mandatory = core.Concat(required).Distinct().Take(cap).ToArray();
            var legal = Enumerable.Range(0, 128).Select(mask => FromMask(mask, ids))
                .Where(set => set.Length == cap && mandatory.All(set.Contains)).ToArray();
            for (var currentMask = 0; currentMask < 128; currentMask++)
            {
                var before = FromMask(currentMask, ids);
                if (before.Length > cap) continue;
                foreach (var randomize in new[] { false, true })
                {
                    var c = Reset(cap, before);
                    var after = BeardManager.SelectBeards(required, randomize);
                    Check(after.Length == cap && after.Distinct().Count() == cap, "selection fills capacity without duplicates");
                    Check(legal.Any(set => new HashSet<int>(set).SetEquals(after)), "selection violates independent core/request oracle");
                    if (randomize)
                        Check(!legal.Any(set => !new HashSet<int>(before).SetEquals(set)) || !new HashSet<int>(before).SetEquals(after), "avoidable no-op reroll");
                    else
                    {
                        var maxRetained = legal.Max(set => set.Count(before.Contains));
                        Check(after.Count(before.Contains) == maxRetained, "non-random selection removed unnecessary beards");
                        var maxProtected = legal.Where(set => set.Count(before.Contains) == maxRetained)
                            .Max(set => set.Count(id => before.Contains(id) && (id == 0 || id == 6)));
                        Check(after.Count(id => before.Contains(id) && (id == 0 || id == 6)) == maxProtected, "Fu/Gold retention tie break");
                    }
                    Unchanged(c, before);
                }
            }
        }
    }

    private static void CorePriority()
    {
        var ordered = new[] { 1, 3, 5, 6, 0, 4, 2 };
        for (var cap = 1; cap <= 7; cap++)
        foreach (var randomize in new[] { false, true })
        foreach (var required in new[] { new[] { 6, 0, 4, 2 }, new[] { 0, 4, 2 }, new[] { -1, 0, 0, 4, 2, 99 } })
        {
            var c = Reset(cap);
            Set(BeardManager.SelectBeards(required, randomize), ordered.Take(cap).ToArray());
            Unchanged(c, new int[0]);
        }
    }

    private static void MinuteRerolls()
    {
        foreach (var unlocked in new[] { false, true })
        for (var cap = 1; cap <= 7; cap++)
        foreach (var required in new[] { new int[0], new[] { 0 }, new[] { 6 }, new[] { 0, 2, 4, 6 } })
        {
            var c = Reset(cap);
            c.challenges.trollChallenge.curCompletions = unlocked ? 7 : 6;
            var available = Enumerable.Range(0, unlocked ? 7 : 6).ToArray();
            var mandatory = new[] { 1, 3, 5, 6 }.Concat(required).Where(available.Contains).Distinct().Take(cap).ToArray();
            var optionalSlots = Math.Min(cap, available.Length) - mandatory.Length;
            var canChange = optionalSlots > 0 && optionalSlots < available.Length - mandatory.Length;
            var before = BeardManager.SelectBeards(required, false);
            for (var minute = 0; minute < 100; minute++)
            {
                // Seed the next observed native input, not a replacement application workflow.
                c.beards.activeBeards = before.ToList();
                for (var id = 0; id < 7; id++) c.beards.beards[id].active = before.Contains(id);
                var after = BeardManager.SelectBeards(required, true);
                Check(new HashSet<int>(before).SetEquals(after) != canChange, "minute selection possible/impossible swap");
                Check(mandatory.All(after.Contains), "minute selection lost core/request priority");
                Unchanged(c, before);
                before = after;
            }
        }
    }

    private static void ProtectedRetention()
    {
        foreach (var required in new[] { null, new[] { 6 } })
        {
            var c = Reset(6, 0, 1, 2, 3, 4, 5);
            var after = BeardManager.SelectBeards(required, false);
            Check(after.Contains(0) && after.Contains(6), "NUMBER/Wandoos must be evicted before retained Fu and core Gold");
            Check(new[] { 1, 3, 5 }.All(after.Contains), "retention cannot evict core");
            Unchanged(c, new[] { 0, 1, 2, 3, 4, 5 });
        }
    }

    private static void GoldUnlock()
    {
        foreach (var completions in new[] { 0, 6, 7, 8 })
        for (var cap = 1; cap <= 7; cap++)
        foreach (var randomize in new[] { false, true })
        {
            var c = Reset(cap);
            c.challenges.trollChallenge.curCompletions = completions;
            Set(BeardManager.SelectBeards(new[] { 6, 0, 2, 4, 6, -1, 7, 999 }, randomize),
                (completions >= 7 ? new[] { 1, 3, 5, 6, 0, 2, 4 } : new[] { 1, 3, 5, 0, 2, 4 }).Take(cap).ToArray());
            Unchanged(c, new int[0]);
        }
        foreach (var missingChallenge in new[] { false, true })
        {
            var c = Reset(7);
            if (missingChallenge) c.challenges = null; else c.challenges.trollChallenge = null;
            Set(BeardManager.SelectBeards(new[] { 6 }, true), 0, 1, 2, 3, 4, 5);
        }
        var missing = Reset(7);
        missing.beards.beards[3] = null; missing.beards.beards.RemoveAt(6);
        Set(BeardManager.SelectBeards(new[] { 3, 6, -1, 100 }, false), 0, 1, 2, 4, 5);
    }

    private static void ExhaustiveHardPrerequisites()
    {
        var ids = Enumerable.Range(0, 7).ToArray();
        foreach (var unlocked in new[] { false, true })
        for (var cap = 0; cap <= 7; cap++)
        for (var mask = 0; mask < 128; mask++)
        foreach (var randomize in new[] { false, true })
        {
            var c = Reset(cap, 0, 2, 4);
            c.challenges.trollChallenge.curCompletions = unlocked ? 7 : 6;
            var hard = FromMask(mask, ids);
            var duplicateHard = hard.Concat(hard).ToArray();
            if (hard.Length > cap || !unlocked && hard.Contains(6))
                Reject(() => BeardManager.SelectBeards(new[] { 4, 2 }, randomize, duplicateHard), "Required beards");
            else
            {
                var after = BeardManager.SelectBeards(new[] { 4, 2 }, randomize, duplicateHard);
                var mandatory = hard.Concat(new[] { 1, 3, 5, 6, 4, 2 }).Where(id => unlocked || id != 6).Distinct().Take(cap).ToArray();
                Check(hard.All(after.Contains), "hard prerequisites were truncated by utility/core");
                Check(mandatory.All(after.Contains) && after.Length == Math.Min(cap, unlocked ? 7 : 6), "hard/core/optional order or fill count");
                Check(after.Distinct().Count() == after.Length, "duplicate hard prerequisites consume only one slot");
            }
            Unchanged(c, new[] { 0, 2, 4 });
        }
        foreach (var invalid in new[] { -1, 7, 99 })
        {
            Reset(7);
            Reject(() => BeardManager.SelectBeards(null, false, new[] { invalid }), "Required beards");
        }
        var missing = Reset(7); missing.beards.beards[0] = null;
        Reject(() => BeardManager.SelectBeards(null, false, new[] { 0 }), "Required beards");
    }

    private static void Unavailable()
    {
        foreach (var mutation in new Action<Character>[] {
            c => Game.Character = null, c => c.beards = null, c => c.beards.beards = null,
            c => c.beards.activeBeards = null, c => c.beards.beards[3] = null, c => c.beards.activeBeards.Add(99),
            c => c.allBeards = null, c => c.beards.disabled = true, c => c.buttons = null,
            c => c.buttons.beards = null, c => c.buttons.beards.interactable = false })
        {
            var c = Reset(4, 1, 3, 5, 2); var native = c.allBeards;
            native.ThrowOnCap = true;
            mutation(c);
            Reject(() => BeardManager.SelectBeards(new[] { 0 }, true), "beards skipped:");
            Check(native.CapCalls == 0, "unavailable context must reject before native cap getter");
            Check(native.Activated.Count + native.Deactivated.Count + native.ClearCalls == 0, "unavailable selection mutated support");
        }
        Reset(0); Set(BeardManager.SelectBeards(null, false));
        Reset(-1); Reject(() => BeardManager.SelectBeards(null, false), "Required beards");
        var empty = Reset(7); empty.beards.beards.Clear(); Set(BeardManager.SelectBeards(null, false));
    }
}
