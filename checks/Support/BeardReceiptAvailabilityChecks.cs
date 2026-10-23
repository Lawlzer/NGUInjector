using System;
using System.Linq;
using NGUInjector.Managers;
using Game = NGUInjector.Main;

internal static class BeardReceiptAvailabilityChecks
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        foreach (var makeUnavailable in new Action<Character>[] {
            c => c.buttons.beards.interactable = false, c => c.buttons.beards = null, c => c.buttons = null,
            c => c.beards.disabled = true, c => c.beards.beards = null, c => c.beards.activeBeards = null,
            c => c.beards = null, c => c.allBeards = null, c => c.beards.activeBeards.Add(99),
            c => { c.beards.beards[1] = null; c.beards.activeBeards.Add(1); }, c => Game.Character = null })
        {
            var c = new Character(5); Game.Character = c;
            var native = c.allBeards; native.ThrowOnCap = true;
            makeUnavailable(c);
            Reject("beards skipped:");
            Check(native.CapCalls == 0, "unavailable selector never queries native cap");
            Check(native.Activated.Count + native.Deactivated.Count + native.ClearCalls == 0, "unavailable selection cannot mutate native support");

            Game.Character = new Character(5);
            var ready = Game.Character;
            Set(BeardManager.SelectBeards(new[] { 4 }, false), 1, 3, 4, 5, 6);
            Check(ready.allBeards.CapCalls == 1, "readiness transition retries the same request against actual cap");
            ready.allBeards.Cap = 4;
            Set(BeardManager.SelectBeards(new[] { 4 }, false), 1, 3, 5, 6);
            ready.challenges.trollChallenge.curCompletions = 6;
            Set(BeardManager.SelectBeards(new[] { 4 }, false), 1, 3, 4, 5);
            Check(ready.beards.activeBeards.Count == 0 && ready.beards.beards.All(b => !b.active), "capacity/unlock observations are read-only, not fake receipt application");
        }
        Game.Character = new Character(5);
        Game.Character.allBeards.ThrowOnCap = true;
        Reject("cap getter reached");
        Check(Game.Character.allBeards.CapCalls == 1, "available cap failure propagates from the actual getter");
        Console.WriteLine("PASS beard availability: " + assertions + " assertions; production SelectBeards and throwing native cap. Receipt integration is in SustainedFocus.");
    }

    private static void Set(int[] actual, params int[] expected)
    {
        Check(actual.OrderBy(x => x).SequenceEqual(expected), "wrong actual manager selection: " + string.Join(",", actual));
    }
    private static void Reject(string diagnostic)
    {
        try { BeardManager.SelectBeards(new[] { 4 }, false); }
        catch (InvalidOperationException error) { Check(error.Message.Contains(diagnostic), "unexpected availability failure: " + error.Message); return; }
        throw new InvalidOperationException("Expected failure: " + diagnostic);
    }
}
