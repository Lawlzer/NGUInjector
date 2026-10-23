using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NGUInjector.AllocationProfiles.BreakpointTypes
{
    internal class BestHackBP : HackBP
    {
        protected override bool Unlocked()
        {
            for (int i = 0; i < Character.hacks.hacks.Count; i++)
            {
                if (this.Unlocked(i))
                {
                    return true;
                }
            }
            return false;
        }

        protected bool Unlocked(int id) { 
            return id <= 14 && Character.buttons.hacks.interactable;
        }

        protected override bool TargetMet()
        {
            for (int i = 0; i < Character.hacks.hacks.Count; i++)
            {
                if (!this.TargetMet(i))
                {
                    return false;
                }
            }
            return true;
        }

        protected bool TargetMet(int id) { 
            return Character.hacksController.hitTarget(id);
        }

        protected override bool CorrectResourceType()
        {
            return Type == ResourceType.R3;
        }

        public float time(int id, float r3)
        {
            double num = (double)((float)r3 * Character.totalRes3Power() * Character.hacksController.totalHackSpeedBonus() / (Character.hacksController.properties[id].baseDivider * this.levelDivider(id)));
            if (num >= 3.4028234663852886E+38)
            {
                return float.MaxValue;
            }
            if (num <= -3.4028234663852886E+38)
            {
                return 0f;
            }
            return 1f / (float)num / 50f;
        }

        public float levelDivider(int id)
        {
            long target = Character.hacks.hacks[id].level + Character.hacksController.levelsToNextMilestone(id);
            double num = Math.Pow(1.0078, (double)target) * (double)(target + 1L);
            if (num > 3.4028234663852886E+38)
            {
                return float.MaxValue;
            }
            return (float)num;
        }
    }
}
