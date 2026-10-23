using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUInjector.Managers
{
    internal static class BeardManager
    {
        private static readonly int[] CoreBeards = ActivityPolicy.Current.CoreBeards();
        private static readonly Random Random = new Random();

        internal static int[] SelectBeards(IEnumerable<int> requiredBeards, bool randomize, int[] hardRequired = null)
        {
            var unavailable = UnavailableReason();
            if (unavailable != null) throw new InvalidOperationException(unavailable);
            var cap = Main.Character.allBeards.capBeards();
            hardRequired = (hardRequired ?? new int[0]).Distinct().ToArray();
            if (hardRequired.Length > cap || hardRequired.Any(id => !IsUnlocked(id)))
                throw new InvalidOperationException("Required beards are unavailable or exceed active slots.");
            if (cap <= 0) return new int[0];

            var available = Enumerable.Range(0, 7).Where(IsUnlocked).ToArray();
            var mandatory = hardRequired.Concat(CoreBeards).Concat(requiredBeards ?? new int[0])
                .Where(IsUnlocked).Distinct().Take(cap).ToArray();
            var optional = available.Except(mandatory).ToArray();
            var slots = Math.Min(cap, available.Length) - mandatory.Length;
            var current = new HashSet<int>(Main.Character.beards.activeBeards);
            var choices = new List<int[]>();
            for (var mask = 0; mask < (1 << optional.Length); mask++)
            {
                var pick = optional.Where((id, bit) => (mask & (1 << bit)) != 0).ToArray();
                if (pick.Length == slots) choices.Add(mandatory.Concat(pick).ToArray());
            }
            if (randomize && choices.Any(x => !current.SetEquals(x))) choices.RemoveAll(x => current.SetEquals(x));
            if (!randomize)
            {
                var retained = choices.Max(x => x.Count(current.Contains));
                choices.RemoveAll(x => x.Count(current.Contains) != retained);
                var protectedCount = choices.Max(x => x.Count(id => current.Contains(id) && (id == 0 || id == 6)));
                choices.RemoveAll(x => x.Count(id => current.Contains(id) && (id == 0 || id == 6)) != protectedCount);
            }
            return choices[choices.Count == 1 ? 0 : Random.Next(choices.Count)];
        }

        private static string UnavailableReason()
        {
            var character = Main.Character;
            if (character == null || character.beards?.beards == null || character.beards.activeBeards == null
                || character.allBeards == null || character.beards.disabled
                || character.beards.activeBeards.Any(id => id < 0 || id >= character.beards.beards.Count || character.beards.beards[id] == null))
                return "beards skipped: unavailable";
            return character.buttons?.beards?.interactable != true ? "beards skipped: locked" : null;
        }

        private static bool IsUnlocked(int id)
        {
            var character = Main.Character;
            return id >= 0 && id < 7 && id < character.beards.beards.Count && character.beards.beards[id] != null
                && (id != 6 || (character.challenges?.trollChallenge?.curCompletions ?? 0) >= 7);
        }
    }
}
