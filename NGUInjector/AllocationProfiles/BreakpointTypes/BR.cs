using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NGUInjector.AllocationProfiles.BreakpointTypes
{
    internal class BR : BaseBreakpoint
    {
        internal int RebirthTime { get; set; }
        protected override bool Unlocked()
        {
            return Character.buttons.bloodMagic.interactable;
        }

        protected override bool TargetMet()
        {
            return false;
        }

        protected override bool CorrectResourceType()
        {
            return Type == ResourceType.Magic;
        }

        private float RitualProgressPerTick(int id, long remaining)
        {
            var num1 = 0.0;
            if (Character.settings.rebirthDifficulty == difficulty.normal)
                num1 = remaining * (double)Character.totalMagicPower() / 50000.0 /
                       Character.bloodMagicController.normalSpeedDividers[id];
            else if (Character.settings.rebirthDifficulty == difficulty.evil)
                num1 = remaining * (double)Character.totalMagicPower() / 50000.0 /
                       Character.bloodMagicController.evilSpeedDividers[id];
            else if (Character.settings.rebirthDifficulty == difficulty.sadistic)
                num1 = remaining * (double)Character.totalMagicPower() /
                       Character.bloodMagicController.sadisticSpeedDividers[id];
            if (Character.settings.rebirthDifficulty >= difficulty.sadistic)
                num1 /= Character.bloodMagicController.bloodMagics[id].sadisticDivider();
            var num2 = num1 * Character.bloodMagicController.bloodMagics[id].totalBloodMagicSpeedBonus();
            if (num2 <= -3.40282346638529E+38)
                num2 = 0.0;
            if (num2 >= 3.40282346638529E+38)
                num2 = 3.40282346638529E+38;
            return (float)num2;
        }

        public float RitualTimeLeft(int id, long remaining)
        {
            return (float)((1.0 - Character.bloodMagic.ritual[id].progress) /
                           RitualProgressPerTick(id, remaining) / 50.0);
        }

        private long CalculateMaxAllocation(int id, long remaining)
        {
            var num1 = Character.bloodMagicController.bloodMagics[id].capValue();
            if (remaining > num1)
            {
                return num1;
            }

            var num2 = (long) ((double) num1 / Math.Ceiling((double) num1 / (double) remaining)) + 1L;
            return num2;
        }
    }
}
