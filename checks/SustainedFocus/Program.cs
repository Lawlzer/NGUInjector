using System;
using System.IO;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;
using SimpleJSON;

namespace NGUInjector
{
    internal static class SustainedFocusChecks
    {
        private static int _assertions;
        private static readonly int[] InitialEnergy = {1, 0, 0, 0, 0, 0, 0, 0, 0};
        private static readonly int[] InitialMagic = {0, 0, 1, 0, 0, 0, 0};
        private static readonly int[] EditedEnergy = {0, 0, 0, 0, 2, 0, 1, 0, 0};
        private static readonly int[] EditedMagic = {1, 0, 0, 0, 0, 0, 0};
        private static void Assert(bool condition, string name)
        {
            _assertions++;
            if (!condition) throw new Exception(name);
        }

        private static void SaveWeights(string name, int[] energy, int[] magic)
        {
            var plan = NGUInjector.Main.CustomPlanStore.ReadExternalPlan(name);
            var energyJson = new JSONArray();
            var magicJson = new JSONArray();
            foreach (var weight in energy) energyJson.Add(weight);
            foreach (var weight in magic) magicJson.Add(weight);
            plan["EnergyNguWeights"] = energyJson;
            plan["MagicNguWeights"] = magicJson;
            NGUInjector.Main.CustomPlanStore.SaveExternalPlan(name, plan, plan["__externalUiRevision"].Value);
        }

        public static int Main()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ngu-sustain-" + Guid.NewGuid().ToString("N"));
            try
            {
                NGUInjector.Main.Settings = new FocusSettings();
                NGUInjector.Main.CustomPlanStore = new CustomPlanStore(dir);
                _assertions += NguSharingChecks.Run();
                _assertions += IronPillAllocationChecks.Run();
                var defaults = new CustomPlan();
                Assert(defaults.Version == 16 && defaults.EnergyNguWeights.SequenceEqual(new[] {0, 0, 0, 0, 1, 0, 1, 0, 0})
                    && defaults.MagicNguWeights.SequenceEqual(EditedMagic), "new version-16 plans default to Adventure/Drop energy and Yggdrasil magic weights");
                foreach (var name in new[] {"24hr", "infinite"})
                {
                    var preset = NGUInjector.Main.CustomPlanStore.Load(name);
                    Assert(preset.EnergyNguWeights.SequenceEqual(defaults.EnergyNguWeights) && preset.MagicNguWeights.SequenceEqual(defaults.MagicNguWeights),
                        name + ": new NGU preset uses default lane weights");
                    NGUInjector.Main.Settings.CustomPlanFile = name;
                    foreach (var available in new[] {1000L, 253001L})
                    {
                        var owner = new NGUInjector.Main();
                        NGUInjector.Main.reference = owner;
                        var c = new FocusCharacter();
                        NGUInjector.Main.Character = c;
                        Assert(owner.AppliedEnergyWeights == null && owner.AppliedMagicWeights == null, "weights are not claimed applied before allocation");
                        c.NGU.skills[0].energy = available;
                        c.NGU.magicSkills[2].magic = 700;
                        owner.Allocate(CustomFocusGoal.EnergyNgu);
                        // Stubbed speed divider 1000, level 0, five-second lookahead: buffered cap 252001.
                        Assert(c.NGU.skills[4].energy == (available + 1) / 2 && c.NGU.skills[6].energy == available / 2,
                            name + ": equal default energy weights use ascending-index remainders");
                        Assert(c.NGU.skills[4].energy <= 252001L && c.NGU.skills[6].energy <= 252001L,
                            name + ": shares stay within each buffered cap");
                        Assert(c.NGU.skills.Where((_, index) => index != 4 && index != 6).All(skill => skill.energy == 0)
                            && c.NGU.skills.Sum(skill => skill.energy) + c.idleEnergy == available, name + ": preset excludes unrelated rows and conserves energy");
                        Assert(c.EnergyRecalls == 1 && c.MagicRecalls == 1 && c.NGU.magicSkills[0].magic == 700
                            && c.NGU.magicSkills.Skip(1).All(skill => skill.magic == 0) && c.magic.idleMagic == 0,
                            name + ": Energy NGU recalls both lanes and funds Yggdrasil index zero");
                        Assert(owner.AppliedEnergyWeights.SequenceEqual(preset.EnergyNguWeights) && owner.AppliedMagicWeights.SequenceEqual(preset.MagicNguWeights),
                            name + ": successful allocation snapshots both affected lanes");
                        Assert(!ReferenceEquals(owner.AppliedEnergyWeights, owner.LastLoadedPlan.EnergyNguWeights)
                            && !ReferenceEquals(owner.AppliedMagicWeights, owner.LastLoadedPlan.MagicNguWeights), name + ": snapshots are independent copies of loaded configuration");
                        owner.LastLoadedPlan.EnergyNguWeights[4] = 99;
                        owner.LastLoadedPlan.MagicNguWeights[0] = 99;
                        Assert(owner.AppliedEnergyWeights[4] == 1 && owner.AppliedMagicWeights[0] == 1,
                            name + ": in-memory config edits cannot mutate successful-pass snapshots");
                    }
                }
                SaveWeights("infinite", InitialEnergy, InitialMagic);
                var saved = new CustomPlanStore(dir).Load("infinite");
                Assert(saved.EnergyNguWeights.SequenceEqual(InitialEnergy) && saved.MagicNguWeights.SequenceEqual(InitialMagic),
                    "explicit persisted weights survive built-in preset initialization");
                NGUInjector.Main.Settings.CustomPlanFile = "active";
                NGUInjector.Main.CustomPlanStore.Save("active", new CustomPlan());
                NGUInjector.Main.CustomPlanStore.Save("editor", new CustomPlan());
                CheckSequentialTiming();
                var failedOwner = new NGUInjector.Main();
                NGUInjector.Main.reference = failedOwner;
                var failedCharacter = new FocusCharacter();
                NGUInjector.Main.Character = failedCharacter;
                failedCharacter.NGU.skills[0].energy = 1000;
                failedCharacter.NGU.magicSkills[2].magic = 700;
                failedCharacter.NGUController.NGU[4].ThrowOnUpdate = true;
                var updateFailed = false;
                try { failedOwner.Allocate(CustomFocusGoal.EnergyNgu); }
                catch (InvalidOperationException error) { updateFailed = error.Message == "NGU UI unavailable"; }
                Assert(updateFailed && failedOwner.AppliedEnergyWeights == null && failedOwner.AppliedMagicWeights == null,
                    "a failed focus pass cannot snapshot configured weights as successfully applied");
                Assert(failedCharacter.NGU.skills.Sum(skill => skill.energy) + failedCharacter.idleEnergy == 1000
                    && failedCharacter.NGU.magicSkills.Sum(skill => skill.magic) + failedCharacter.magic.idleMagic == 700,
                    "failed focus pass preserves both totals after UI accounting failure");
                foreach (var goal in new[] {CustomFocusGoal.EnergyNgu, CustomFocusGoal.MagicNgu, CustomFocusGoal.Ngu,
                    CustomFocusGoal.TimeMachine, CustomFocusGoal.Wandoos, CustomFocusGoal.AugmentsBloodMagic,
                    CustomFocusGoal.AdvancedTraining, CustomFocusGoal.Adventure})
                {
                    SaveWeights("active", InitialEnergy, InitialMagic);
                    var owner = new NGUInjector.Main();
                    NGUInjector.Main.reference = owner;
                    var c = new FocusCharacter();
                    NGUInjector.Main.Character = c;
                    // Primary non-NGU features are locked or complete, exposing their NGU fallback.
                    c.machine.speedTarget = c.machine.multiTarget = -1;
                    c.NGU.skills[0].energy = 1000;
                    c.NGU.magicSkills[2].magic = 700;
                    owner.Begin(goal);
                    var energy = goal != CustomFocusGoal.MagicNgu;
                    var originalEnergySnapshot = owner.AppliedEnergyWeights;
                    var originalMagicSnapshot = owner.AppliedMagicWeights;
                    var schedulerBefore = CustomStageAllocator.GetSchedulerSnapshot(NGUInjector.Main.CustomPlanStore.Load("active"), 0);
                    SaveWeights("active", EditedEnergy, EditedMagic);
                    owner.MarkJustRefreshed();
                    var refreshedAt = owner.LastRefresh;
                    Assert(owner.Invalidations == 1, goal + ": selected weight save invalidates the runtime plan cache");
                    owner.Tick();
                    Assert(c.EnergyRecalls == (energy ? 1 : 0) && c.MagicRecalls == 1, goal + ": live weight edit bypasses cooldown and recalls managed lanes once");
                    Assert(c.NGU.skills[0].energy == (energy ? 0 : 1000) && c.NGU.magicSkills[2].magic == 0,
                        goal + ": old allocations are removed only from managed lanes");
                    Assert(c.NGU.skills[4].energy == (energy ? 667 : 0) && c.NGU.skills[6].energy == (energy ? 333 : 0)
                        && c.NGU.magicSkills[0].magic == 700, goal + ": live edit applies 2:1 energy weights and Yggdrasil magic");
                    Assert(c.NGU.skills.Sum(x => x.energy) + c.idleEnergy == 1000 && c.NGU.magicSkills.Sum(x => x.magic) + c.magic.idleMagic == 700,
                        goal + ": both resource totals are conserved");
                    Assert((energy ? owner.AppliedEnergyWeights.SequenceEqual(EditedEnergy) : owner.AppliedEnergyWeights == null)
                        && owner.AppliedMagicWeights.SequenceEqual(EditedMagic) && owner.LastRefresh >= refreshedAt,
                        goal + ": only affected lanes acquire applied weight snapshots");
                    Assert((!energy || originalEnergySnapshot.SequenceEqual(InitialEnergy)) && originalMagicSnapshot.SequenceEqual(InitialMagic)
                        && !ReferenceEquals(owner.AppliedMagicWeights, EditedMagic), goal + ": new snapshots do not mutate or alias previous arrays");
                    var runtime = owner.PublishedRuntime["nguAllocation"];
                    Assert(owner.Publications == 1 && runtime["strategy"].Value == "weighted-ngu-buffered-cap"
                        && runtime["energyTargets"].Count == 9 && runtime["magicTargets"].Count == 7, goal + ": same-pass publication exposes the full weighted contract");
                    Assert(runtime["energyTargets"][4]["weight"].AsInt == 2 && runtime["magicTargets"][0]["weight"].AsInt == 1
                        && runtime["energyTargets"][4]["allocated"].Value == (energy ? "667" : "0")
                        && runtime["energyTargets"][6]["allocated"].Value == (energy ? "333" : "0")
                        && runtime["magicTargets"][0]["allocated"].Value == "700", goal + ": telemetry describes configured weights and actual lane allocations");
                    Assert(runtime["energyWeightsApplied"].AsBool == energy && runtime["magicWeightsApplied"].AsBool
                        && runtime["magicAppliedWeights"][0].AsInt == 1 && (!energy || runtime["energyAppliedWeights"][4].AsInt == 2),
                        goal + ": publication distinguishes configured from affected-lane applied weights");
                    Assert(!string.IsNullOrEmpty(NGUInjector.Main.Status) && NGUInjector.Main.Status.StartsWith("Sustaining "),
                        goal + ": status update bypasses its own throttle on a weight change");
                    var schedulerAfter = CustomStageAllocator.GetSchedulerSnapshot(NGUInjector.Main.CustomPlanStore.Load("active"), 0);
                    Assert(schedulerBefore.ActiveStageIndex == schedulerAfter.ActiveStageIndex
                        && schedulerBefore.DurationSeconds.SequenceEqual(schedulerAfter.DurationSeconds), goal + ": weight edit leaves duration scheduling unchanged");
                    owner.MarkJustRefreshed();
                    owner.Tick();
                    Assert(c.EnergyRecalls == (energy ? 1 : 0) && c.MagicRecalls == 1 && owner.Publications == 1,
                        goal + ": newly loaded equal-value arrays still observe normal cooldown");
                    SaveWeights("active", EditedEnergy.ToArray(), EditedMagic.ToArray());
                    owner.MarkJustRefreshed();
                    owner.Tick();
                    Assert(c.EnergyRecalls == (energy ? 1 : 0) && c.MagicRecalls == 1 && owner.Publications == 1,
                        goal + ": an equal-value CAS save does not masquerade as a live change");
                    SaveWeights("editor", InitialEnergy, InitialMagic);
                    owner.MarkJustRefreshed();
                    owner.Tick();
                    Assert(owner.AppliedMagicWeights.SequenceEqual(EditedMagic) && owner.Publications == 1,
                        goal + ": another editor plan cannot change active weights");
                    owner.MarkRefreshDue();
                    owner.Tick();
                    Assert(c.EnergyRecalls == (energy ? 2 : 0) && c.MagicRecalls == 2 && owner.Publications == 1,
                        goal + ": elapsed cooldown refreshes without fabricating a weight-change publication");
                    SaveWeights("active", InitialEnergy, InitialMagic);
                    owner.MarkJustRefreshed();
                    owner.Tick();
                    Assert(c.EnergyRecalls == (energy ? 3 : 0) && c.MagicRecalls == 3 && c.NGU.skills[0].energy == 1000
                        && c.NGU.skills[4].energy == 0 && c.NGU.skills[6].energy == 0
                        && c.NGU.magicSkills[2].magic == 700 && c.NGU.magicSkills[0].magic == 0,
                        goal + ": reverse weight edit immediately restores original allocations");
                    Assert(owner.AppliedMagicWeights.SequenceEqual(InitialMagic) && owner.Publications == 2
                        && owner.PublishedRuntime["nguAllocation"]["magicWeights"][2].AsInt == 1, goal + ": reverse edit publishes in the same pass");
                }

                foreach (var energyEdit in new[] {true, false})
                {
                    SaveWeights("active", InitialEnergy, InitialMagic);
                    var owner = new NGUInjector.Main();
                    NGUInjector.Main.reference = owner;
                    var c = new FocusCharacter();
                    NGUInjector.Main.Character = c;
                    c.NGU.skills[0].energy = 1000;
                    c.NGU.magicSkills[2].magic = 700;
                    owner.Begin(CustomFocusGoal.EnergyNgu);
                    SaveWeights("active", energyEdit ? EditedEnergy : InitialEnergy, energyEdit ? InitialMagic : EditedMagic);
                    owner.MarkJustRefreshed();
                    owner.Tick();
                    Assert(c.EnergyRecalls == 1 && c.MagicRecalls == 1 && owner.Publications == 1,
                        "editing either array alone immediately refreshes the paired focus");
                    Assert(c.NGU.skills[0].energy == (energyEdit ? 0 : 1000) && c.NGU.skills[4].energy == (energyEdit ? 667 : 0)
                        && c.NGU.skills[6].energy == (energyEdit ? 333 : 0)
                        && c.NGU.magicSkills[2].magic == (energyEdit ? 700 : 0) && c.NGU.magicSkills[0].magic == (energyEdit ? 0 : 700),
                        "single-lane config edit rebalances correctly while retaining the other lane's configured allocation");
                    Assert(owner.AppliedEnergyWeights.SequenceEqual(energyEdit ? EditedEnergy : InitialEnergy)
                        && owner.AppliedMagicWeights.SequenceEqual(energyEdit ? InitialMagic : EditedMagic),
                        "successful paired pass snapshots both lanes after either array changes");
                }

                // Editing an unmanaged lane must not disturb a magic-only focus.
                SaveWeights("active", InitialEnergy, InitialMagic);
                var scopedOwner = new NGUInjector.Main();
                NGUInjector.Main.reference = scopedOwner;
                var scopedCharacter = new FocusCharacter();
                NGUInjector.Main.Character = scopedCharacter;
                scopedCharacter.NGU.skills[0].energy = 1000;
                scopedCharacter.NGU.magicSkills[2].magic = 700;
                scopedOwner.Begin(CustomFocusGoal.MagicNgu);
                SaveWeights("active", EditedEnergy, InitialMagic);
                scopedOwner.MarkJustRefreshed();
                scopedOwner.Tick();
                Assert(scopedCharacter.EnergyRecalls == 0 && scopedCharacter.NGU.skills[0].energy == 1000
                    && scopedOwner.AppliedEnergyWeights == null, "Magic NGU never recalls or snapshots the unmanaged energy lane");
                SaveWeights("active", EditedEnergy, EditedMagic);
                scopedOwner.MarkJustRefreshed();
                scopedOwner.Tick();
                Assert(scopedCharacter.EnergyRecalls == 0 && scopedCharacter.NGU.skills[0].energy == 1000
                    && scopedCharacter.NGU.magicSkills[0].magic == 700 && scopedOwner.AppliedEnergyWeights == null,
                    "Magic NGU rebalances affected magic weights while preserving arbitrary energy allocation");

                foreach (var toggle in new[] {false, true})
                {
                    SaveWeights("active", InitialEnergy, InitialMagic);
                    var owner = new NGUInjector.Main();
                    NGUInjector.Main.reference = owner;
                    var c = new FocusCharacter();
                    NGUInjector.Main.Character = c;
                    c.NGU.skills[4].energy = 1000;
                    c.NGU.magicSkills[6].magic = 700;
                    owner.Allocate(CustomFocusGoal.EnergyNgu);
                    owner.Begin(CustomFocusGoal.EnergyNgu);
                    var action = toggle ? "toggle off" : "stop";
                    var message = toggle ? owner.ToggleCustomFocusGoal(CustomFocusGoal.EnergyNgu) : owner.StopCustomFocusGoal();
                    Assert(c.EnergyRecalls == 2 && c.MagicRecalls == 2, action + ": Energy NGU releases both managed lanes exactly once");
                    Assert(c.idleEnergy == 1000 && c.magic.idleMagic == 700
                        && c.NGU.skills.All(skill => skill.energy == 0) && c.NGU.magicSkills.All(skill => skill.magic == 0),
                        action + ": paired release returns all focused resources to idle");
                    Assert(owner.GetActiveCustomFocusGoal() == null && owner.GetAppliedCustomFocusGoal() == string.Empty
                        && owner.GetCustomFocusStatus() == "idle" && message.Contains("Released focused resources")
                        && owner.AppliedEnergyWeights == null && owner.AppliedMagicWeights == null, action + ": release clears focus and applied snapshots");
                    Assert(owner.Publications == 1 && owner.PublishedRuntime["activeFocusGoal"].Value == string.Empty
                        && owner.PublishedRuntime["focusStatus"].Value == "idle", action + ": cleared focus is published immediately");
                    SaveWeights("active", EditedEnergy, EditedMagic);
                    owner.MarkRefreshDue();
                    owner.Tick();
                    Assert(c.EnergyRecalls == 2 && c.MagicRecalls == 2 && c.idleEnergy == 1000 && c.magic.idleMagic == 700
                        && owner.Publications == 1, action + ": later ticks cannot reacquire released resources");
                }

                SaveWeights("active", InitialEnergy, InitialMagic);
                var blockedOwner = new NGUInjector.Main();
                NGUInjector.Main.reference = blockedOwner;
                var blockedCharacter = new FocusCharacter();
                NGUInjector.Main.Character = blockedCharacter;
                blockedCharacter.NGU.skills[0].energy = 1000;
                blockedCharacter.NGU.magicSkills[2].magic = 700;
                blockedOwner.Begin(CustomFocusGoal.EnergyNgu);
                SaveWeights("active", EditedEnergy, EditedMagic);
                blockedOwner.Blocked = true;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0
                    && blockedCharacter.NGU.skills[0].energy == 1000 && blockedCharacter.NGU.magicSkills[2].magic == 700,
                    "preparation guard keeps ownership of both existing lanes");
                Assert(blockedOwner.AppliedEnergyWeights.SequenceEqual(InitialEnergy) && blockedOwner.AppliedMagicWeights.SequenceEqual(InitialMagic)
                    && blockedOwner.Publications == 0, "blocked refresh cannot claim new weights were applied");
                blockedOwner.Blocked = false;
                var ngu = blockedCharacter.NGU;
                blockedCharacter.NGU = null;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0 && blockedOwner.Publications == 0, "unavailable save data cannot trigger paired resource recall");
                blockedCharacter.NGU = ngu;
                var skills = ngu.skills;
                ngu.skills = null;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0 && blockedOwner.Publications == 0, "missing energy skills cannot trigger paired resource recall");
                ngu.skills = skills;
                var lastEnergySkill = skills[8];
                skills.RemoveAt(8);
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0 && blockedOwner.Publications == 0, "partial energy skills cannot trigger paired resource recall");
                skills.Add(lastEnergySkill);
                var magicData = blockedCharacter.magic;
                blockedCharacter.magic = null;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0 && blockedOwner.Publications == 0, "missing magic data prevents even the energy half of Energy NGU recall");
                blockedCharacter.magic = magicData;
                var magicSkills = ngu.magicSkills;
                ngu.magicSkills = null;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0 && blockedOwner.Publications == 0, "missing magic skills cannot trigger paired resource recall");
                ngu.magicSkills = magicSkills;
                var lastMagicSkill = magicSkills[6];
                magicSkills.RemoveAt(6);
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0 && blockedOwner.Publications == 0, "partial magic skills cannot trigger paired resource recall");
                magicSkills.Add(lastMagicSkill);
                var clock = blockedCharacter.rebirthTime;
                blockedCharacter.rebirthTime = null;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0 && blockedOwner.Publications == 0, "unavailable rebirth clock cannot trigger paired resource recall");
                blockedCharacter.rebirthTime = clock;
                var store = NGUInjector.Main.CustomPlanStore;
                NGUInjector.Main.CustomPlanStore = null;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 0 && blockedCharacter.MagicRecalls == 0
                    && blockedOwner.AppliedMagicWeights.SequenceEqual(InitialMagic) && blockedOwner.Publications == 0, "missing plan store cannot silently change weights");
                NGUInjector.Main.CustomPlanStore = store;
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 1 && blockedCharacter.MagicRecalls == 1
                    && blockedCharacter.NGU.skills[0].energy == 0 && blockedCharacter.NGU.skills[4].energy == 667 && blockedCharacter.NGU.skills[6].energy == 333
                    && blockedCharacter.NGU.magicSkills[2].magic == 0 && blockedCharacter.NGU.magicSkills[0].magic == 700,
                    "restored save applies weighted energy and magic on its first eligible pass");
                Assert(blockedOwner.AppliedEnergyWeights.SequenceEqual(EditedEnergy) && blockedOwner.AppliedMagicWeights.SequenceEqual(EditedMagic)
                    && blockedOwner.Publications == 1, "pending weights stay unapplied through guards and publish only after full readiness");
                SaveWeights("active", InitialEnergy, InitialMagic);
                blockedOwner.MarkJustRefreshed();
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == 2 && blockedCharacter.MagicRecalls == 2
                    && blockedCharacter.NGU.skills[0].energy == 1000 && blockedCharacter.NGU.magicSkills[2].magic == 700,
                    "reverse weights also rebalance both lanes without waiting for cooldown");
                SaveWeights("active", EditedEnergy, EditedMagic);
                blockedOwner.Tick();
                var recalls = blockedCharacter.EnergyRecalls;
                var magicRecalls = blockedCharacter.MagicRecalls;
                var publications = blockedOwner.Publications;
                File.WriteAllText(Path.Combine(dir, "active.json"), "{");
                var loadFailed = false;
                try { store.Load("active"); }
                catch { loadFailed = true; }
                Assert(loadFailed, "malformed plan Load rethrows rather than fabricating a fallback");
                blockedOwner.MarkRefreshDue();
                blockedOwner.Tick();
                Assert(blockedCharacter.EnergyRecalls == recalls && blockedCharacter.MagicRecalls == magicRecalls && blockedOwner.Publications == publications
                    && blockedOwner.AppliedEnergyWeights.SequenceEqual(EditedEnergy) && blockedOwner.AppliedMagicWeights.SequenceEqual(EditedMagic)
                    && blockedCharacter.NGU.skills[4].energy == 667 && blockedCharacter.NGU.skills[6].energy == 333
                    && blockedCharacter.NGU.magicSkills[0].magic == 700, "unreadable active plan preserves existing snapshots and resources even when refresh is due");
                foreach (var invalidPlan in new[] {true, false})
                {
                    var owner = new NGUInjector.Main();
                    NGUInjector.Main.reference = owner;
                    var c = new FocusCharacter();
                    NGUInjector.Main.Character = c;
                    c.NGU.skills[4].energy = 1000;
                    c.NGU.magicSkills[6].magic = 700;
                    if (!invalidPlan)
                    {
                        NGUInjector.Main.CustomPlanStore.Save("active", new CustomPlan());
                        c.magic = null;
                    }
                    var message = owner.ApplyCustomFocusGoal(CustomFocusGoal.EnergyNgu);
                    Assert(message == "ERROR in Apply custom focus EnergyNgu: NGU focus requires loaded resource data and a readable active plan.",
                        "initial Energy NGU activation returns a readiness error before gear preparation");
                    Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0 && c.NGU.skills[4].energy == 1000 && c.NGU.magicSkills[6].magic == 700,
                        "initial activation guards preserve both existing resource allocations");
                    Assert(owner.GetCustomFocusStatus() == "failed" && owner.AppliedEnergyWeights == null && owner.AppliedMagicWeights == null,
                        "failed initial activation cannot publish applied lane snapshots");
                }
                Console.WriteLine("PASS: " + _assertions + " assertions against actual focus, allocator, scheduler, and plan sources; game boundaries stubbed.");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally
            {
                NGUInjector.Main.reference = null;
                NGUInjector.Main.Character = null;
                NGUInjector.Main.Settings = null;
                NGUInjector.Main.CustomPlanStore = null;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private static void CheckSequentialTiming()
        {
            var store = NGUInjector.Main.CustomPlanStore;
            var owner = new NGUInjector.Main();
            NGUInjector.Main.reference = owner;
            var c = new FocusCharacter();
            NGUInjector.Main.Character = c;
            NGUInjector.Main.Settings.CustomPlanFile = "timing";
            var plan = new CustomPlan { Name = "timing", StageLoopStartIndex = -1, StageDurationSeconds = 50, Stages = new[] {
                new CustomPlanStage {Name = "AT", CompletionMode = 1, TargetAdvancedTrainingPower = 10, TargetAdvancedTrainingToughness = 5},
                new CustomPlanStage {Name = "Timed A", DurationSeconds = 20, DurationUnit = "s"},
                new CustomPlanStage {Name = "Timed B", DurationSeconds = 30, DurationUnit = "s"} } };
            store.Save("timing", plan);
            CustomStageAllocator.ResetStageRuntime();
            c.rebirthTime.totalseconds = 100;
            var active = CustomStageAllocator.GetActiveStage(store.Load("timing"));
            Assert(active.StageIndex == 0 && active.EffectiveDurationSeconds == 0, "positive AT targets wait on stats with no clock duration");
            c.advancedTraining.level[1] = 10;
            c.advancedTraining.level[0] = 4;
            c.rebirthTime.totalseconds = 109;
            Assert(CustomStageAllocator.GetActiveStage(store.Load("timing")).StageIndex == 0, "AT waits for both power and toughness targets");
            c.advancedTraining.level[0] = 5;
            c.rebirthTime.totalseconds = 110;
            active = CustomStageAllocator.GetActiveStage(store.Load("timing"));
            Assert(active.StageIndex == 1 && active.StageStartSeconds == 110 && active.StageEndSeconds == 130,
                "meeting actual AT stats advances to the timed row at the current rebirth clock");
            var edits = new Action<JSONObject>[] {
                root => root["Stages"][1]["DurationUnit"] = "min",
                root => root["EnergyNguWeights"][4] = 2,
                root => root["TimeTriggerSeconds"] = 5000,
                root => root["Stages"][1]["GearGoal"] = 2,
                root => root["futureScheduleExtension"] = "keep",
                root => root["Stages"][0]["DurationSeconds"] = int.MaxValue,
                root => root["Stages"][0]["Percent"] = double.MaxValue
            };
            foreach (var edit in edits)
            {
                var root = store.ReadExternalPlan("timing");
                edit(root);
                store.SaveExternalPlan("timing", root, root["__externalUiRevision"].Value);
                c.rebirthTime.totalseconds = 115;
                active = CustomStageAllocator.GetActiveStage(store.Load("timing"));
                Assert(active.StageIndex == 1 && active.StageStartSeconds == 110 && active.StageEndSeconds == 130,
                    "non-scheduling CAS edit preserves the timed row after real positive AT completion");
            }
            var durationEdit = store.ReadExternalPlan("timing");
            durationEdit["Stages"][1]["DurationSeconds"] = 25;
            store.SaveExternalPlan("timing", durationEdit, durationEdit["__externalUiRevision"].Value);
            c.rebirthTime.totalseconds = 120;
            active = CustomStageAllocator.GetActiveStage(store.Load("timing"));
            Assert(active.StageIndex == 1 && active.StageStartSeconds == 120 && active.StageEndSeconds == 145,
                "effective duration edit restarts sequence and skips already-completed AT on the next pass");
            var targetEdit = store.ReadExternalPlan("timing");
            targetEdit["Stages"][0]["TargetAdvancedTrainingPower"] = "11";
            store.SaveExternalPlan("timing", targetEdit, targetEdit["__externalUiRevision"].Value);
            c.rebirthTime.totalseconds = 125;
            active = CustomStageAllocator.GetActiveStage(store.Load("timing"));
            Assert(active.StageIndex == 0 && active.StageStartSeconds == 125 && active.EffectiveDurationSeconds == 0,
                "AT target edit restarts at the now-unmet stat row");
            Assert(c.EnergyRecalls == 0 && c.MagicRecalls == 0, "scheduler evaluation and plan edits do not perform game resource actions");
            var rounded = new CustomPlan { StageDurationSeconds = 101, Stages = new[] {
                new CustomPlanStage {CompletionMode = 1}, new CustomPlanStage {Percent = 1}, new CustomPlanStage {Percent = 1} } };
            Assert(CustomStageAllocator.GetSchedulerSnapshot(rounded, 0).DurationSeconds.SequenceEqual(new[] {0, 50, 50}),
                "all-percent sequential timed rows retain nearest-even midpoint rounding");
            rounded.StageDurationSeconds = 1;
            Assert(CustomStageAllocator.GetSchedulerSnapshot(rounded, 0).DurationSeconds.SequenceEqual(new[] {0, 1, 1}),
                "all-percent sequential timed rows retain the legacy one-second minimum");
            rounded.Stages = rounded.Stages.Skip(1).ToArray();
            rounded.StageDurationSeconds = 101;
            Assert(CustomStageAllocator.GetSchedulerSnapshot(rounded, 0).DurationSeconds.SequenceEqual(new[] {50, 51}),
                "time-window rows use cumulative floor boundaries rather than sequential midpoint rounding");
            var tinyMixed = new CustomPlan { Name = "zero-tail", StageDurationSeconds = 2, StageLoopStartIndex = -1, Stages = new[] {
                new CustomPlanStage {CompletionMode = 1}, new CustomPlanStage {DurationSeconds = 1},
                new CustomPlanStage {Percent = 1}, new CustomPlanStage {Percent = double.Epsilon} } };
            Assert(CustomStageAllocator.GetSchedulerSnapshot(tinyMixed, 0).DurationSeconds.SequenceEqual(new[] {0, 1, 1, 0}),
                "mixed sequential floor rounding permits zero-second weighted rows while conserving the budget");
            c.rebirthTime.totalseconds = 200;
            CustomStageAllocator.GetActiveStage(tinyMixed);
            c.rebirthTime.totalseconds = 201;
            CustomStageAllocator.GetActiveStage(tinyMixed);
            c.rebirthTime.totalseconds = 202;
            active = CustomStageAllocator.GetActiveStage(tinyMixed);
            Assert(active.StageIndex == 3 && active.EffectiveDurationSeconds == 0, "zero-second last timed row retains the legacy final-focus hold");
            Assert(CustomStageAllocator.GetSchedulerSnapshot(tinyMixed, 202).StageProgress == 1,
                "zero-second timed progress is immediately complete, not an invented one-second clock");
            CustomStageAllocator.ResetStageRuntime();
            NGUInjector.Main.Settings.CustomPlanFile = "active";
        }
    }
}
