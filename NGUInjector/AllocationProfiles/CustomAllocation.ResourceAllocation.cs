using System;
using System.Linq;
using NGUInjector.AllocationProfiles.BreakpointTypes;

namespace NGUInjector.AllocationProfiles
{
    internal partial class CustomAllocation
    {
        public override void AllocateEnergy()
        {
            if (_wrapper == null)
                return;

            var bp = GetCurrentBreakpoint(true);
            if (bp == null)
                return;

            if (bp.Time != _currentEnergyBreakpoint.Time)
            {
                _currentEnergyBreakpoint = bp;
            }

            var temp = bp.Priorities.Where(x => x.IsValid()).ToList();
            if (temp.Count == 0)
                return;
            var prioCount = temp.Count(x => !x.IsCapPrio());


            if (temp.Any(x => x is BasicTrainingBP))
                _character.removeAllEnergy();
            else
                _character.removeMostEnergy();

            var toAdd = (long)Math.Ceiling((double)_character.idleEnergy / prioCount);
            SetInput(toAdd);

            foreach (var prio in temp)
            {
                if (!prio.IsCapPrio())
                {
                    prioCount--;
                }

                if (prio.Allocate())
                {
                    toAdd = (long)Math.Ceiling((double)_character.idleEnergy / prioCount);
                    SetInput(toAdd);
                }
            }

            _character.NGUController.refreshMenu();
            _character.wandoos98Controller.refreshMenu();
            _character.advancedTrainingController.refresh();
            _character.timeMachineController.updateMenu();
            _character.allOffenseController.refresh();
            _character.allDefenseController.refresh();
            _character.wishesController.updateMenu();
            _character.augmentsController.updateMenu();
        }

        public override void AllocateMagic()
        {
            if (_wrapper == null)
                return;

            var bp = GetCurrentBreakpoint(false);
            if (bp == null)
                return;

            if (bp.Time != _currentMagicBreakpoint.Time)
            {
                _currentMagicBreakpoint = bp;
            }

            var temp = bp.Priorities.Where(x => x.IsValid()).ToList();
            if (temp.Count == 0)
                return;
            var prioCount = temp.Count(x => !x.IsCapPrio());

            _character.removeMostMagic();
            var toAdd = (long)Math.Ceiling((double)_character.magic.idleMagic / prioCount);
            SetInput(toAdd);

            foreach (var prio in temp)
            {
                if (!prio.IsCapPrio())
                {
                    prioCount--;
                }

                if (prio.Allocate())
                {
                    toAdd = (long)Math.Ceiling((double)_character.magic.idleMagic / prioCount);
                    SetInput(toAdd);
                }
            }

            _character.timeMachineController.updateMenu();
            _character.bloodMagicController.updateMenu();
            _character.NGUController.refreshMenu();
            _character.wandoos98Controller.refreshMenu();
            _character.wishesController.updateMenu();
        }

        public override void AllocateR3()
        {
            if (_wrapper == null)
                return;

            var bp = GetCurrentR3Breakpoint();
            if (bp == null)
                return;

            if (bp.Time != _currentR3Breakpoint.Time)
            {
                _currentR3Breakpoint = bp;
            }

            var temp = bp.Priorities.Where(x => x.IsValid()).ToList();
            if (temp.Count == 0)
                return;

            var prioCount = temp.Count(x => !x.IsCapPrio() && !(x is HackBP)) + (temp.Any(x => x is HackBP && !x.IsCapPrio()) ? 1 : 0);
            _character.removeAllRes3();
            var toAdd = (long) Math.Ceiling((double) _character.res3.idleRes3 / prioCount);
            SetInput(toAdd);

            var hackAllocated = false;

            foreach (var prio in temp)
            {
                switch (prio)
                {
                    case HackBP _ when hackAllocated:
                        continue;
                    case HackBP _:
                        hackAllocated = true;
                        break;
                }

                if (!prio.IsCapPrio())
                {
                    prioCount--;
                }

                if (prio.Allocate())
                {
                    toAdd = (long)Math.Ceiling((double)_character.res3.idleRes3 / prioCount);
                    SetInput(toAdd);
                }
            }

            _character.hacksController.refreshMenu();
            _character.wishesController.updateMenu();
        }

        private void SetInput(float val)
        {
            _character.energyMagicPanel.energyRequested.text = val.ToString();
            _character.energyMagicPanel.validateInput();
        }
    }
}
