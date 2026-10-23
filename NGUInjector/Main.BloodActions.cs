using System;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private sealed class NumberBloodAction : FiniteAction
        {
            internal double Blood, Number;
            internal override bool Prerequisites() => Character.bloodMagic.bloodPoints == Blood && Blood > 0d
                && Blood <= double.MaxValue && Character.bloodMagic.rebirthPower == Number;
            internal override string Observe() => ActionNumber(Character.bloodMagic.bloodPoints) + "|" + ActionNumber(Character.bloodMagic.rebirthPower);
            internal override void Invoke() => Character.bloodSpells.castRebirthSpell();
            internal override MutationDisposition Classify(string before, string after, Exception error) =>
                Character.bloodMagic.bloodPoints == 0d && Character.bloodMagic.rebirthPower == Number + Blood
                    ? error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed : MutationDisposition.UnknownOutcome;
        }

        private sealed class PillBloodAction : FiniteAction
        {
            internal double Blood;
            internal float Power, Toughness, Health, Regen, Worth;
            internal override bool Prerequisites() => Character.bloodMagic.bloodPoints == Blood && Blood >= Character.bloodSpells.minAdventureBlood()
                && Character.bloodMagic.adventureSpellTime.totalseconds >= Character.bloodSpells.adventureSpellCooldown
                && Character.adventure.attack == Power && Character.adventure.defense == Toughness
                && Character.adventure.maxHP == Health && Character.adventure.regen == Regen;
            internal override string Observe() => ActionNumber(Character.bloodMagic.bloodPoints) + "|"
                + ActionNumber(Character.bloodMagic.adventureSpellTime.totalseconds) + "|" + ActionNumber(Character.adventure.attack)
                + "|" + ActionNumber(Character.adventure.defense) + "|" + ActionNumber(Character.rebirthTime.totalseconds);
            internal override void Invoke() => Character.bloodSpells.castAdventurePowerupSpell();
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                // Match native stfld float32 after each complete add expression.
                // Locals/casts can retain desktop CLR extended precision at compare.
                var expected = new[] { Power + Worth, Toughness + Worth, Health + Worth * 3f, Regen + Worth * .03f };
                // Native tooltip precedes cooldown reset. Exact stats + zero blood
                // prove commitment even when presentation prevented that final reset.
                if (Character.bloodMagic.bloodPoints == 0d && Character.adventure.attack == expected[0]
                    && Character.adventure.defense == expected[1] && Character.adventure.maxHP == expected[2]
                    && Character.adventure.regen == expected[3])
                    return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                return MutationDisposition.UnknownOutcome;
            }
        }

        private sealed class MacguffinBloodAction : FiniteAction
        {
            internal bool Beta;
            internal double Blood;
            internal int Levels;
            internal long[] BeforeLevels;
            internal override bool Prerequisites() => Character.bloodMagic.bloodPoints == Blood
                && (Beta ? Character.adventure.itopod.perkLevel[73] >= 1 && Character.settings.rebirthDifficulty >= difficulty.evil
                    && Character.bloodMagic.macguffin2Time.totalseconds >= Character.bloodSpells.macguffin2Cooldown
                    && Blood >= Character.bloodSpells.minMacguffin2Blood()
                    : Character.adventure.itopod.perkLevel[72] >= 1
                    && Character.bloodMagic.macguffin1Time.totalseconds >= Character.bloodSpells.macguffin1Cooldown
                    && Blood >= Character.bloodSpells.minMacguffin1Blood())
                && Character.inventory.macguffins.Select(x => (long)x.level).SequenceEqual(BeforeLevels);
            internal override string Observe() => ActionNumber(Character.bloodMagic.bloodPoints) + "|"
                + string.Join(",", Character.inventory.macguffins.Select(x => x.id + ":" + x.level)) + "|"
                + ActionNumber(Beta ? Character.bloodMagic.macguffin2Time.totalseconds : Character.bloodMagic.macguffin1Time.totalseconds);
            internal override void Invoke()
            {
                if (Beta) Character.bloodSpells.castMacguffin2Spell();
                else Character.bloodSpells.castMacguffin1Spell();
            }
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                var current = Character.inventory.macguffins;
                var deltas = current.Select((x, i) => (long)x.level - BeforeLevels[i]).ToArray();
                var valid = Beta ? !deltas.Where((delta, i) => delta != (current[i].id == 0 ? 0 : Levels)).Any()
                    : deltas.All(x => x == 0 || x == Levels) && deltas.Count(x => x == Levels) <= 1;
                if (valid && Character.bloodMagic.bloodPoints == 0d
                    && (Beta ? Character.bloodMagic.macguffin2Time.totalseconds : Character.bloodMagic.macguffin1Time.totalseconds) == 0d)
                    return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                return MutationDisposition.UnknownOutcome;
            }
        }

        private string BloodActionWindow(int cooldown) => ActionNumber((long)Math.Floor(Character.totalPlaytime.totalseconds / Math.Max(1, cooldown)));

        internal void RequestThresholdBloodSpells(bool rebirth)
        {
            if (!Settings.CastBloodSpells || IronPillBloodReserved) return;
            var blood = Character.bloodMagic.bloodPoints;
            for (var beta = 1; beta >= 0; beta--)
            {
                var isBeta = beta == 1;
                var minimum = isBeta ? Character.bloodSpells.minMacguffin2Blood() : Character.bloodSpells.minMacguffin1Blood();
                var threshold = isBeta ? Settings.BloodMacGuffinBThreshold : Settings.BloodMacGuffinAThreshold;
                if (threshold <= 0 || blood < minimum) continue;
                var levels = isBeta ? (int)(Math.Log(blood / minimum, 20d) + 1d)
                    : (int)((Math.Log(blood / minimum, 10d) + 1d) * Character.wishesController.totalBloodGuffbonus());
                if (levels < threshold) continue;
                var id = ActionIdentity(isBeta ? "blood-macguffin-b" : "blood-macguffin-a", "blood-spells",
                    BloodActionWindow(isBeta ? Character.bloodSpells.macguffin2Cooldown : Character.bloodSpells.macguffin1Cooldown));
                var action = new MacguffinBloodAction { Identity = id, Beta = isBeta, Blood = blood, Levels = levels,
                    BeforeLevels = Character.inventory.macguffins.Select(x => (long)x.level).ToArray(),
                    Footprint = ExecutionDomain.FiniteAction | ExecutionDomain.Magic | ExecutionDomain.Inventory };
                var result = RunFiniteAction(action, ResolveExecutionGrant(id.Owner, id.Acquisition, action.Footprint,
                    () => Settings.CastBloodSpells && !IronPillBloodReserved));
                if (result.Committed || result.Disposition == MutationDisposition.UnknownOutcome) return;
            }
        }
    }
}
