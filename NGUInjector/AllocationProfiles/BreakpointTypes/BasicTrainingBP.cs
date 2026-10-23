using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;

namespace NGUInjector.AllocationProfiles.BreakpointTypes
{
    internal class BasicTrainingBP : BaseBreakpoint
    {
        private int BTIndex => Index <= 5 ? Index : Index - 6;

        protected override bool Unlocked()
        {
            if (Index > 11)
                return false;

            if (Index == 0 || Index == 6)
                return true;

            var trainings = Index <= 5 ? Character.training.attackTraining : Character.training.defenseTraining;

            return trainings[BTIndex - 1] >= 5000 * BTIndex;
        }

        protected override bool TargetMet()
        {
            return false;
        }

        protected override bool CorrectResourceType()
        {
            return Type == ResourceType.Energy;
        }
    }
}
