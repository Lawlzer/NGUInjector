using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NGUInjector.Managers;

// Only unrelated application services are substituted. Gear planning, actuation,
// resource observation/recall and capacity projection compile from current source.
namespace NGUInjector
{
    public sealed class Main
    {
        public static Character Character;
        public static InventoryController Controller;
        public static SavedSettings Settings;
        public static StreamWriter OutputWriter;
        public static Main reference;
        public void InvalidateCustomEquipmentApplication() { }
        public bool ServiceCustomAdventureGoldPushCleanup() { return false; }
        public static void Log(string message) { }
        public static string LogError(string context, Exception error) { return context + ": " + error.Message; }
    }

    public sealed class SavedSettings
    {
        public SavedSettings(object unused) { }
        public bool ManageEnergy { get; set; }
        public bool ManageMagic { get; set; }
        public bool ManageR3 { get; set; }
        public bool ManageGoldLoadouts, DoGoldSwap;
        public int[] TitanLoadout = new int[0], GoldDropLoadout = new int[0], YggdrasilLoadout = new int[0], MoneyPitLoadout = new int[0];
    }

    public static class Extensions
    {
        public static ih GetInventoryHelper(this Equipment item, int slot) => new ih {
            equipment = item, slot = slot, id = item.id, level = item.level, locked = !item.removable,
            name = Main.Controller.itemInfo.itemName[item.id] };
        public static IEnumerable<ih> GetConvertedInventory(this Inventory inventory) => inventory.inventory
            .Select((item, slot) => item.GetInventoryHelper(slot)).Where(x => x.id != 0);
        public static IEnumerable<ih> GetConvertedEquips(this Inventory inventory) => LoadoutManager.CaptureExactLoadout().Where(x => x.id != 0);
        public static ih MaxItem(this IEnumerable<ih> items) => items.OrderByDescending(x => x.level).First();
    }
}

namespace NGUInjector.Managers
{
    public sealed class ih
    {
        public Equipment equipment { get; set; }
        public int slot { get; set; }
        public int id { get; set; }
        public int level { get; set; }
        public bool locked { get; set; }
        public string name { get; set; }
    }
    internal static class ZoneHelpers
    {
        internal sealed class TitanStatus { internal bool SpawningSoon = false, RunMoneyLoadout = false; internal int GoldTargetIndex = 0; }
        internal static TitanStatus TitansSpawningSoon() => new TitanStatus();
        internal static void MarkTitanMoneyDone(int index) { }
    }
}
