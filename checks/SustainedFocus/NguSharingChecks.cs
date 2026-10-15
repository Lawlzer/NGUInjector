using System;
using System.Linq;
using System.Numerics;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class NguSharingChecks
    {
        private static int _assertions;
        private static readonly int[] EnergyWeights = {0, 0, 0, 0, 1, 0, 1, 0, 0};
        private static readonly int[] MagicWeights = {1, 0, 0, 0, 0, 0, 0};
        private static void Assert(bool condition, string name)
        {
            _assertions++;
            if (!condition) throw new Exception(name);
        }

        private static void Shares(long available, long[] caps, int[] weights, params long[] expected)
        {
            var beforeCaps = caps.ToArray();
            var beforeWeights = weights.ToArray();
            var actual = BlackBarAllocator.ShareCappedBudget(caps, available, weights);
            Assert(actual.SequenceEqual(expected), "weighted capped sharing: budget " + available + ", caps " + string.Join(",", caps) + ", weights " + string.Join(",", weights));
            var sum = actual.Aggregate(BigInteger.Zero, (total, amount) => total + amount);
            var eligibleCaps = caps.Select((cap, i) => weights[i] > 0 ? Math.Max(0, cap) : 0)
                .Aggregate(BigInteger.Zero, (total, cap) => total + cap);
            Assert(sum == BigInteger.Min(Math.Max(0, available), eligibleCaps), "sharing exactly spends the budget or all eligible caps without overflow");
            Assert(actual.Select((amount, i) => amount >= 0 && amount <= Math.Max(0, caps[i]) && (weights[i] > 0 || amount == 0)).All(x => x),
                "sharing respects caps and excludes nonpositive weights");
            Assert(caps.SequenceEqual(beforeCaps) && weights.SequenceEqual(beforeWeights), "sharing does not mutate inputs");
        }

        private static FocusCharacter Allocate(long available, int[] weights, string[] priorities, Action<FocusCharacter> setup = null, bool energy = true)
        {
            Assert(weights.Length == (energy ? 9 : 7), "integration uses actual lane weight lengths");
            var c = new FocusCharacter();
            Main.Character = c;
            if (energy) c.idleEnergy = available;
            else c.magic.idleMagic = available;
            setup?.Invoke(c);
            BlackBarAllocator.AllocatePriorities(priorities, energy, 5, weights);
            var spent = energy ? c.NGU.skills.Sum(skill => skill.energy) + c.machine.speedEnergy + c.wandoos98.wandoosEnergy
                : c.NGU.magicSkills.Sum(skill => skill.magic) + c.machine.goldMultiMagic + c.wandoos98.wandoosMagic;
            Assert(spent + (energy ? c.idleEnergy : c.magic.idleMagic) == available, "priority integration conserves the managed resource");
            return c;
        }

        internal static int Run()
        {
            _assertions = 0;
            Shares(1000, new[] {10000L, 10000L}, new[] {1, 1}, 500, 500);
            Shares(1000, new[] {10000L, 10000L}, new[] {2, 1}, 667, 333);
            Shares(2, new[] {100L, 100L}, new[] {2, 1}, 1, 1);
            Shares(1, new[] {100L, 100L}, new[] {1, 2}, 0, 1);
            Shares(2, new[] {100L, 100L, 100L}, new[] {1, 1, 1}, 1, 1, 0);
            Shares(2, new[] {100L, 100L, 100L}, new[] {1, 3, 2}, 0, 1, 1);
            Shares(1000, new[] {100L, 1000L}, new[] {2, 1}, 100, 900);
            Shares(10, new[] {1L, 100L, 100L}, new[] {5, 2, 1}, 1, 6, 3);
            Shares(100, new[] {1L, 2L, 30L, 1000L}, new[] {1, 2, 3, 4}, 1, 2, 30, 67);
            Shares(1000, new[] {100L, 200L}, new[] {1, 2}, 100, 200);
            Shares(1000, new[] {0L, -1L, 2000L}, new[] {1, 1, 2}, 0, 0, 1000);
            Shares(1000, new[] {100L, 100L, 2000L}, new[] {0, -1, 2}, 0, 0, 1000);
            Shares(1000, new[] {100L, 100L}, new[] {0, 0}, 0, 0);
            Shares(0, new[] {100L, 100L}, new[] {2, 1}, 0, 0);
            Shares(-1, new[] {100L, 100L}, new[] {2, 1}, 0, 0);
            Shares(long.MinValue, new[] {long.MaxValue}, new[] {int.MaxValue}, 0);
            Shares(1, new long[0], new int[0]);
            Shares(long.MaxValue, new[] {long.MaxValue, long.MaxValue}, new[] {int.MaxValue, int.MaxValue}, 4611686018427387904L, 4611686018427387903L);
            Shares(long.MaxValue, new[] {long.MaxValue, long.MaxValue}, new[] {2, 1}, 6148914691236517205L, 3074457345618258602L);
            Shares(long.MaxValue, new[] {1L, long.MaxValue, long.MaxValue}, new[] {int.MaxValue, int.MaxValue, int.MaxValue}, 1, 4611686018427387903L, 4611686018427387903L);
            Shares(long.MaxValue, new[] {long.MaxValue, long.MaxValue}, new[] {0, int.MaxValue}, 0, long.MaxValue);
            // An independent uncapped BigInteger quotient/remainder oracle stresses huge unequal products.
            var preciseWeights = new[] {int.MaxValue, int.MaxValue - 1, int.MaxValue - 2};
            var weightSum = preciseWeights.Aggregate(BigInteger.Zero, (total, weight) => total + weight);
            var fractions = new BigInteger[preciseWeights.Length];
            var expected = preciseWeights.Select((weight, i) => (long) BigInteger.DivRem((BigInteger) long.MaxValue * weight, weightSum, out fractions[i])).ToArray();
            var left = (int) ((BigInteger) long.MaxValue - expected.Aggregate(BigInteger.Zero, (total, amount) => total + amount));
            foreach (var i in Enumerable.Range(0, preciseWeights.Length).OrderByDescending(i => fractions[i]).ThenBy(i => i).Take(left)) expected[i]++;
            Shares(long.MaxValue, new[] {long.MaxValue, long.MaxValue, long.MaxValue}, preciseWeights, expected);

            var c = Allocate(1001, EnergyWeights, new[] {"NGU"});
            Assert(c.NGU.skills[4].energy == 501 && c.NGU.skills[6].energy == 500 && c.idleEnergy == 0, "equal weights split an odd energy budget with stable remainder");
            var runtime = BlackBarAllocator.GetNguRuntime(EnergyWeights, MagicWeights);
            Assert(runtime["strategy"].Value == "weighted-ngu-buffered-cap" && runtime["energyTargets"].Count == 9 && runtime["magicTargets"].Count == 7,
                "runtime reports weighted strategy and every actual lane row");
            Assert(runtime["energyTargets"][4]["bufferedCap"].Value == "252001" && runtime["energyTargets"][6]["bufferedCap"].Value == "252001"
                && runtime["energyTargets"][4]["belowBufferedCap"].AsBool && runtime["energyTargets"][6]["belowBufferedCap"].AsBool,
                "runtime evaluates every positive-weight cap before a shortage exhausts energy");
            Assert(runtime["energyTargets"][0]["weight"].AsInt == 0 && runtime["energyTargets"][4]["weight"].AsInt == 1
                && runtime["energyWeights"][6].AsInt == 1 && runtime["magicWeights"][0].AsInt == 1, "runtime exposes configured arrays and per-row weights including exclusions");
            BlackBarAllocator.ResetExcessIdle();
            Assert(BlackBarAllocator.GetNguRuntime(EnergyWeights, MagicWeights)["energyWeightsApplied"].AsBool,
                "idle-warning resets between scheduled passes do not invalidate successfully applied NGU weights");
            BlackBarAllocator.ClearNguAppliedWeights(true);
            Assert(!BlackBarAllocator.GetNguRuntime(EnergyWeights, MagicWeights)["energyWeightsApplied"].AsBool,
                "actual resource recall explicitly invalidates the managed lane's applied weights");
            var uneven = EnergyWeights.ToArray();
            uneven[4] = 2;
            c = Allocate(1000, uneven, new[] {"NGU"});
            Assert(c.NGU.skills[4].energy == 667 && c.NGU.skills[6].energy == 333, "uneven 2:1 lane weights use largest fractional remainder");
            c = Allocate(1, EnergyWeights, new[] {"NGU"});
            Assert(c.NGU.skills[4].energy == 1 && c.NGU.skills.Sum(skill => skill.energy) == 1, "tiny NGU budget is not lost or duplicated");
            c = Allocate(10000, uneven, new[] {"NGU"}, x => x.NGU.skills[4].target = 1);
            Assert(c.NGU.skills[4].energy == 1001 && c.NGU.skills[6].energy == 8999, "a small weighted cap redistributes excess to remaining members");
            c = Allocate(1000, EnergyWeights, new[] {"NGU"}, x => { x.NGU.skills[4].target = 1; x.NGU.skills[4].level = 1; });
            Assert(c.NGU.skills[4].energy == 0 && c.NGU.skills[6].energy == 1000, "completed NGUs do not consume a share");
            c = Allocate(1000, EnergyWeights, new[] {"NGU", "TM"}, x => { x.NGU.skills[4].target = -1; x.NGU.skills[6].target = -1; x.buttons.brokenTimeMachine.interactable = true; });
            Assert(c.machine.speedEnergy == 1000 && c.NGU.skills.Sum(skill => skill.energy) == 0, "all completed group members allow downstream allocation");
            foreach (var energy in new[] {true, false})
            {
                c = Allocate(1000, new int[energy ? 9 : 7], new[] {"NGU", "TM"}, x => x.buttons.brokenTimeMachine.interactable = true, energy);
                Assert((energy ? c.machine.speedEnergy : c.machine.goldMultiMagic) == 1000, "all-zero lane weights leave the pool for downstream priorities");
                c = Allocate(1000, new int[energy ? 9 : 7], new[] {"NGU", "NGU-0"}, energy: energy);
                Assert((energy ? c.NGU.skills[0].energy : c.NGU.magicSkills[0].magic) == 1000, "zero-weight rows are excluded before visited so later explicit NGU-n still works");
            }

            c = Allocate(2000000, EnergyWeights, new[] {"WAN", "NGU", "TM"}, x => { x.buttons.wandoos.interactable = true; x.buttons.brokenTimeMachine.interactable = true; });
            Assert(c.wandoos98.wandoosEnergy == 2 && c.NGU.skills[4].energy == 252001 && c.NGU.skills[6].energy == 252001 && c.machine.speedEnergy == 1495996,
                "outer priorities cap Wandoos before sharing NGU and spill capped excess into Time Machine");
            c = Allocate(1, EnergyWeights, new[] {"WAN", "NGU"}, x => x.buttons.wandoos.interactable = true);
            Assert(c.wandoos98.wandoosEnergy == 1 && c.NGU.skills.Sum(skill => skill.energy) == 0, "higher outer priority can consume the entire budget");
            c = Allocate(1000, EnergyWeights, new[] {"NGU-4", "NGU"});
            Assert(c.NGU.skills[4].energy == 1000 && c.NGU.skills[6].energy == 0, "explicit singleton before the group retains ordered priority");
            c = Allocate(10000, EnergyWeights, new[] {"NGU-4", "NGU"}, x => x.NGU.skills[4].target = 1);
            Assert(c.NGU.skills[4].energy == 1001 && c.NGU.skills[6].energy == 8999, "generic group does not revisit an earlier explicit singleton");
            c = Allocate(1000, EnergyWeights, new[] {"NGU", "NGU-4", "NGU"});
            Assert(c.NGU.skills[4].energy == 500 && c.NGU.skills[6].energy == 500, "later explicit and duplicate generic tokens cannot allocate a positive-weight target twice");
            c = Allocate(300000, EnergyWeights, new[] {"NGU", "NGU-0"}, x => x.NGU.skills[6].target = -1);
            Assert(c.NGU.skills[4].energy == 252001 && c.NGU.skills[0].energy == 47999, "a zero-weight row remains explicitly eligible after a nonempty generic group");
            c = Allocate(1000, EnergyWeights, new[] {"NGU", "TM"}, x => { x.buttons.ngu.interactable = false; x.buttons.brokenTimeMachine.interactable = true; });
            Assert(c.NGU.skills.Sum(skill => skill.energy) == 0 && c.machine.speedEnergy == 1000, "locked NGUs leave the budget for downstream priorities");
            c = Allocate(700, MagicWeights, new[] {"NGU"}, energy: false);
            Assert(c.NGU.magicSkills[0].magic == 700 && c.magic.idleMagic == 0, "Yggdrasil index zero receives the default magic share");
            c = Allocate(2000, MagicWeights, new[] {"NGU", "TM"}, x => { x.NGU.magicSkills[0].target = 1; x.buttons.brokenTimeMachine.interactable = true; }, false);
            Assert(c.NGU.magicSkills[0].magic == 1001 && c.machine.goldMultiMagic == 999, "capped magic group spills into its next outer priority");
            c = Allocate(701, new[] {2, 1, 0, 0, 0, 0, 0}, new[] {"NGU"}, energy: false);
            Assert(c.NGU.magicSkills[0].magic == 467 && c.NGU.magicSkills[1].magic == 234, "magic lane also supports uneven weighted shares");
            foreach (var energy in new[] {true, false})
            foreach (var explicitTarget in new[] {false, true})
            {
                c = new FocusCharacter();
                Main.Character = c;
                c.idleEnergy = 1000;
                c.magic.idleMagic = 700;
                c.NGUController.NGU[4].ThrowOnUpdate = c.NGUController.NGUMagic[0].ThrowOnUpdate = true;
                var failed = false;
                try
                {
                    BlackBarAllocator.AllocatePriorities(new[] {explicitTarget ? energy ? "NGU-4" : "NGU-0" : "NGU"}, energy, 5, energy ? EnergyWeights : MagicWeights);
                }
                catch (InvalidOperationException error) { failed = error.Message == "NGU UI unavailable"; }
                Assert(failed, "controller failure injection reaches the actual NGU update boundary");
                Assert(c.NGU.skills.Sum(skill => skill.energy) + c.idleEnergy == 1000 && c.NGU.magicSkills.Sum(skill => skill.magic) + c.magic.idleMagic == 700,
                    "weighted and singleton transfers conserve both pools even when a controller throws");
                Assert(energy ? c.NGU.skills[4].energy == (explicitTarget ? 1000 : 500) : c.NGU.magicSkills[0].magic == 700,
                    "resource transfer is committed before a failing UI refresh");
            }
            return _assertions;
        }
    }
}
