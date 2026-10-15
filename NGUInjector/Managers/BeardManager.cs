using System.Collections.Generic;
using System.Linq;

namespace NGUInjector.Managers
{
    internal static class BeardManager
    {
        private const int FuManchuBeardId = 0;

        internal static bool EnableFuManchu(bool clearIfFull)
        {
            if (!Main.Character.buttons.beards.interactable)
                return false;

            var beards = Main.Character.beards;
            var allBeards = Main.Character.allBeards;
            if (beards == null || allBeards == null || beards.disabled)
                return false;

            if (FuManchuBeardId >= beards.beards.Count)
                return false;

            if (beards.beards[FuManchuBeardId].active)
                return true;

            if (beards.activeBeards.Count >= allBeards.capBeards())
            {
                if (!clearIfFull)
                    return false;

                allBeards.clearActiveBeards();
            }

            allBeards.activateBeard(FuManchuBeardId);
            allBeards.refreshMenu();
            Main.Log("Enabled Fu Manchu beard");
            return true;
        }

        internal static string EquipBeards(IEnumerable<int> beardIds)
        {
            if (!Main.Character.buttons.beards.interactable)
                return "beards skipped: locked";

            var beards = Main.Character.beards;
            var allBeards = Main.Character.allBeards;
            if (beards == null || allBeards == null || beards.disabled)
                return "beards skipped: unavailable";

            var cap = allBeards.capBeards();
            if (cap <= 0)
                return "beards skipped: no active slots";

            var selected = (beardIds ?? new int[0])
                .Where(x => x >= 0 && x < beards.beards.Count)
                .Distinct()
                .Take(cap)
                .ToArray();
            if (selected.Length == 0)
                return "beards skipped: no unlocked requested beards";

            allBeards.clearActiveBeards();
            foreach (var id in selected)
            {
                if (beards.activeBeards.Count >= cap)
                    break;

                allBeards.activateBeard(id);
            }

            allBeards.refreshMenu();
            var message = $"beards: {string.Join(",", selected.Select(x => x.ToString()).ToArray())}";
            Main.Log(message);
            return message;
        }
    }
}
