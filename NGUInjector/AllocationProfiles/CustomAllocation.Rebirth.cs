using System;
using NGUInjector.AllocationProfiles.RebirthStuff;

namespace NGUInjector.AllocationProfiles
{
    internal partial class CustomAllocation
    {
        public bool DoRebirth()
        {
            return DoRebirth(_wrapper?.Breakpoints?.Rebirth);
        }

        internal bool DoRebirth(BaseRebirth rebirth)
        {
            if (rebirth == null)
                return false;

            if (rebirth.RebirthAvailable())
            {
                if (_character.bossController.isFighting || _character.bossController.nukeBoss)
                {
                    Main.Log("Delaying rebirth while boss fight is in progress");
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (rebirth.DoRebirth())
            {
                ResetRebirthState();
                return true;
            }

            return false;
        }

        private void ResetRebirthState()
        {
            _currentDiggerBreakpoint = null;
            _currentEnergyBreakpoint = null;
            _currentGearBreakpoint = null;
            _currentWandoosBreakpoint = null;
            _currentMagicBreakpoint = null;
            _currentR3Breakpoint = null;
            _currentNguBreakpoint = null;
        }

        public void CastBloodSpells()
        {
            CastBloodSpells(false);
        }

        public void CastBloodSpells(bool rebirth)
        {
            if (!Main.Settings.CastBloodSpells)
                return;

            if (_wrapper.Breakpoints.Rebirth is TimeRebirth trb && Main.Settings.AutoRebirth)
            {
                if (trb.RebirthTime - _character.rebirthTime.totalseconds < 30 * 60 && !rebirth)
                {
                    return;
                }
            }

            float iron = 0;
            long mcguffA = 0;
            long mcguffB = 0;
            if (Main.Settings.BloodMacGuffinBThreshold > 0)
            {
                if (_character.adventure.itopod.perkLevel[73] >= 1L &&
                    _character.settings.rebirthDifficulty >= difficulty.evil)
                {
                    if (_character.bloodMagic.macguffin2Time.totalseconds > _character.bloodMagicController.spells.macguffin2Cooldown)
                    {
                        if (_character.bloodMagic.bloodPoints >= _character.bloodSpells.minMacguffin2Blood())
                        {
                            var a = _character.bloodMagic.bloodPoints / _character.bloodSpells.minMacguffin2Blood();
                            mcguffB = (int) (Math.Log(a, 20.0) + 1.0);
                        }

                        if (Main.Settings.BloodMacGuffinBThreshold <= mcguffB)
                        {
                            _character.bloodSpells.castMacguffin2Spell();
                            Main.LogPitSpin("Casting Blood MacGuffin B Spell power @ " + mcguffB);
                            return;
                        }
                        else
                        {
                            if (rebirth)
                            {
                                Main.Log("Casting Failed Blood MacGuffin B Spell - Insufficient Power " + mcguffB +
                                         " of " + Main.Settings.BloodMacGuffinBThreshold);
                            }
                        }
                    }
                }
            }

            if (Main.Settings.BloodMacGuffinAThreshold > 0)
            {
                if (_character.adventure.itopod.perkLevel[72] >= 1L)
                {
                    if (_character.bloodMagic.macguffin1Time.totalseconds >= _character.bloodMagicController.spells.macguffin1Cooldown)
                    {
                        if (_character.bloodMagic.bloodPoints > _character.bloodSpells.minMacguffin1Blood())
                        {
                            var a = _character.bloodMagic.bloodPoints / _character.bloodSpells.minMacguffin1Blood();
                            mcguffA = (int) ((Math.Log(a, 10.0) + 1.0) *
                                             _character.wishesController.totalBloodGuffbonus());
                        }
                        if (Main.Settings.BloodMacGuffinAThreshold <= mcguffA)
                        {
                            _character.bloodSpells.castMacguffin1Spell();
                            Main.LogPitSpin("Casting Blood MacGuffin A Spell power @ " + mcguffA);
                            return;
                        }
                        else
                        {
                            if (rebirth)
                            {
                                Main.Log("Casting Failed Blood MacGuffin A Spell - Insufficient Power " + mcguffA +
                                         " of " + Main.Settings.BloodMacGuffinAThreshold);
                            }
                        }
                    }
                }
            }

            if (Main.LegacyAutomationEnabled && _character.rebirthTime.totalseconds >= 3600d
                && _character.bossID >= 100 && Main.Settings.IronPillThreshold > 100)
            {
                if (_character.bloodMagic.adventureSpellTime.totalseconds >
                    _character.bloodSpells.adventureSpellCooldown)
                {
                    if (_character.bloodMagic.bloodPoints > _character.bloodSpells.minAdventureBlood())
                    {
                        iron = (float) Math.Floor(Math.Pow(_character.bloodMagic.bloodPoints, 0.25));
                        if (_character.settings.rebirthDifficulty >= difficulty.evil)
                        {
                            iron *= _character.adventureController.itopod.ironPillBonus();
                        }
                    }

                    if (Main.Settings.IronPillThreshold <= iron)
                    {
                        _character.bloodSpells.castAdventurePowerupSpell();
                        Main.LogPitSpin("Casting Iron Blood Spell power @ " + iron);
                    }
                    else
                    {
                        if (rebirth)
                        {
                            Main.Log("Casting Failed Iron Blood Spell - Insufficient Power " + iron + " of " +
                                     Main.Settings.IronPillThreshold);
                        }
                    }
                }
            }
        }
    }
}
