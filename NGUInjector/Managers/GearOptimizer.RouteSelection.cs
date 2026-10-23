using System;
using System.Diagnostics;

namespace NGUInjector.Managers
{
    internal static partial class GearOptimizer
    {
        internal static GearOptimizerResult SelectForAutokill(Func<double, double, double> fitness)
        {
            var timer = Stopwatch.StartNew();
            var result = OptimizeAutokillCandidates(BuildCandidates(),
                Math.Max(0, Math.Min(Main.Controller.accessorySpaces(), Main.Character.inventory.accs.Count)),
                Main.Controller.weapon2Unlocked() ? Main.Controller.weapon2Factor() : 0f, fitness);
            result.ElapsedMilliseconds = timer.ElapsedMilliseconds;
            return result;
        }
    }
}
