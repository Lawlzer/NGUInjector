using System;
using System.Linq;
using Mono.Cecil;

public static partial class Program
{
    // Existing shared LoadFixtureGame discovers this opt-in hook. No production
    // executor, selected actuator or native eligibility method is replaced.
    private static void ConfigureSupportFixture(AssemblyDefinition game)
    {
        foreach (var type in game.MainModule.Types)
        foreach (var method in type.Methods.Where(x => x.HasBody))
        {
            if (type.Name == "Character" && method.Name == "machineBossMulti"
                || type.Name == "AllBloodMagicController" && method.Name == "goldBonus"
                || type.Name == "TimeMachineChallengeController" && method.Name == "totalGPSbonus"
                || type.Name == "AllNGUController" && method.Name == "timeMachineBonus"
                || type.Name == "TimeMachineController" && new[] { "barFillsPerSecond", "speedGoldMultiBonus", "goldMultiBonus" }.Contains(method.Name)) Constant(method, 1);
            if (type.Name == "AllGoldDiggerController" && method.Name == "maxDiggerSlots") Constant(method, 1);
            if (type.Name == "AllBeardsController" && method.Name == "capBeards") Constant(method, 4);
            if ((type.Name == "AllBeardsController" || type.Name == "AllGoldDiggerController") && method.Name == "refreshMenu"
                || type.Name == "GoldDiggerUIController" && method.Name == "updateUI") Constant(method, 0);
        }
    }
}
