namespace NGUInjector.Managers
{
    internal static class DiggerManager
    {
        internal class DiggerRequest
        {
            internal int Id { get; set; }
            internal int Weight { get; set; }
            internal string Name { get; set; }
        }

        internal static void UpgradeCheapestDigger(bool ignoreSetting = false, double reserveGold = -1)
        {
            Main.reference?.PurchaseActionDiggers(ignoreSetting, reserveGold);
        }
    }
}
