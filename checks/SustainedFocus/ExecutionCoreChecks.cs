using System;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class ExecutionCoreChecks
    {
        internal static int Run()
        {
            var settings = Main.Settings; var character = Main.Character; var main = Main.reference;
            try
            {
                var checks = RunResourcesAndSettings() + RunSupport() + SupportPrerequisiteChecks.Run() + RunCapacityBudget();
                Console.WriteLine("PASS: execution-core total: " + checks + " assertions.");
                return checks;
            }
            finally { EquipmentRecording.Reset(false); Main.Settings = settings; Main.Character = character; Main.reference = main; }
        }

        private static int RunResourcesAndSettings()
        {
            var checks = 0;
            void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
            Main.Settings = new FocusSettings(); Main.Settings.ManageR3 = false;
            Main.Character = new FocusCharacter(); Main.reference = new Main();
            Main.CustomPlanStore.Save("active", new CustomPlan());
            EquipmentRecording.Reset(true);
            var owner = Main.reference; var c = Main.Character;
            c.idleEnergy = 1000; c.magic.idleMagic = 700;
            owner.Begin(CustomFocusGoal.EnergyNgu); owner.Allocate(CustomFocusGoal.EnergyNgu);
            var initialEnergy = c.NGU.skills.Select(x => x.energy).ToArray();
            var claim = owner.BeginExecutionClaim("fruit", "opportunity-1", Main.ExecutionDomain.Magic, () => true);
            owner.SetExecutionResourceReservation(claim, 0, 200);
            var outcome = owner.ReconcileExecutionResourceClaim(claim);
            Check(outcome.Verified, "reservation is a verified exact resource transition");
            Check(c.magic.idleMagic == 200 && c.NGU.magicSkills.Sum(x => x.magic) == 500, "Magic baseline funds current budget less exact idle reservation");
            Check(c.NGU.skills.Select(x => x.energy).SequenceEqual(initialEnergy) && c.idleEnergy == 0, "Magic reservation leaves actual Energy and idle unchanged");
            Check(owner.ExecutionResourceReservationReady(claim), "reservation admission observes actual native idle");
            owner.SetExecutionResourceReservation(claim, 0, 250);
            owner.ReconcileExecutionResourceClaim(claim);
            Check(c.magic.idleMagic == 250 && c.NGU.magicSkills.Sum(x => x.magic) == 450, "changed reservation recalculates current baseline, not old allocations");
            c.magic.idleMagic += 50;
            owner.SetExecutionResourceReservation(claim, 0, 200);
            owner.ReconcileExecutionResourceClaim(claim);
            Check(c.magic.idleMagic == 200 && c.NGU.magicSkills.Sum(x => x.magic) == 550, "reservation uses growth in current native pool");
            owner.EndExecutionClaim(claim);
            Check(c.magic.idleMagic == 0 && c.NGU.magicSkills.Sum(x => x.magic) == 750, "ending claim funds CURRENT grown baseline directly");
            Check(c.EnergyRecalls == 0 && c.MagicRecalls == 0, "reservation and release never use whole-lane Recall");
            var policyClaim = owner.BeginExecutionClaim("pit", "feed-1", Main.ExecutionDomain.Magic, () => true);
            var exact = ResourceWriter.EmptyDesiredMap(false, true); exact[ResourceTarget.BloodRitual][0] = 25;
            owner.SetExecutionResourcePolicy(policyClaim, new ResourceSetupIntent { Owner = "pit", Magic = true, Desired = exact, Authorized = () => true });
            outcome = owner.ReconcileExecutionResourceClaim(policyClaim);
            Check(outcome.Verified && c.bloodMagic.ritual[0].magic == 25 && c.NGU.magicSkills.All(x => x.magic == 0), "temporary exact policy controls only its current Magic lane");
            Check(c.NGU.skills.Select(x => x.energy).SequenceEqual(initialEnergy), "temporary Magic policy leaves healthy Energy funded");
            Check(owner.GetCustomFocusStatus() != "applied", "temporary policy cannot manufacture full baseline credit");
            owner.EndExecutionClaim(policyClaim);
            Check(c.bloodMagic.ritual[0].magic == 0 && c.NGU.magicSkills.Sum(x => x.magic) == 750 && c.magic.idleMagic == 0,
                "policy removal recomputes CURRENT baseline without backoff renewal or old allocations");
            var energyGrant = owner.ResolveExecutionGrant("energy", "current", Main.ExecutionDomain.Energy, () => true);
            Main.Settings.ManageMagic = false;
            Check(owner.ValidateExecutionGrant(energyGrant, Main.ExecutionDomain.Energy), "unrelated Magic permission does not revoke Energy grant");
            Main.Settings.ManageMagic = true;
            owner.InvalidateExecutionObservations(Main.ExecutionDomain.Energy);
            Check(!owner.ValidateExecutionGrant(energyGrant, Main.ExecutionDomain.Energy), "affected observation revision revokes old callbacks");
            var supportClaim = owner.BeginExecutionClaim("fight", "fight-1", Main.ExecutionDomain.Support, () => true);
            var supportWrites = EquipmentRecording.ExactSupport.Count;
            var setup = new Main.SelectedSetupRequirements { ControlsGear = false,
                Support = new SupportSelection(new[] { 1 }, Array.Empty<SupportDiggerLevel>()), ChangeSupport = true, RequiredBeards = new[] { 0 } };
            var rejected = false;
            try { owner.ApplyExecutionClaim(supportClaim, setup); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && EquipmentRecording.ExactSupport.Count == supportWrites, "missing hard beard rejects before native support mutation");
            setup.Support = new SupportSelection(new[] { 0 }, Array.Empty<SupportDiggerLevel>());
            owner.ApplyExecutionClaim(supportClaim, setup);
            Check(c.beards.activeBeards.SequenceEqual(new[] { 0 }), "the SAME admitted claim can apply its exact verified hard prerequisite");
            owner.EndExecutionClaim(supportClaim);
            Check(c.beards.activeBeards.Count == 0, "support release resolves the retained current focus support directly");
            var settingsGrant = owner.ResolveExecutionGrant("settings", "one", Main.ExecutionDomain.NativeSettings, () => true);
            var value = 0; var writes = 0;
            var setting = owner.ApplyExecutionSetting(settingsGrant, () => { value = 7; writes++; throw new InvalidOperationException("render"); }, () => value == 7);
            Check(setting.Disposition == MutationDisposition.ModelAppliedRenderFailed && writes == 1, "exact setting classifies committed model plus render failure");
            owner.SetExecutionLifecycle(Main.ExecutionLifecycle.Quiescing);
            setting = owner.ApplyExecutionSetting(settingsGrant, () => writes++, () => false);
            Check(setting.Disposition == MutationDisposition.RejectedUnchanged && writes == 1, "revoked lifecycle authority cannot invoke setting mutation");
            Console.WriteLine("PASS: " + checks + " execution-core assertions using real production grant/reservation/resource code and data-only native boundaries.");
            return checks;
        }

        private static int RunCapacityBudget()
        {
            Main.Settings = new FocusSettings(); Main.Character = new FocusCharacter(); Main.reference = new Main();
            var c = Main.Character; var owner = Main.reference;
            c.training.attackEnergy[0] = 12; c.idleEnergy = 988; c.EnergyCapacity = 500;
            typeof(Main).GetMethod("LimitExecutionCapacityFunding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(owner, new object[] { 1 });
            if (owner.ExecutionResourceBudget(true, ResourceWriter.Reclaimable(true)) != 488 || c.curEnergy != 1000)
                throw new Exception("Capacity release budget must retain basic12 and delayed native current independently.");
            c.idleEnergy = 488;
            if (owner.ExecutionResourceBudget(true, ResourceWriter.Reclaimable(true)) != 488)
                throw new Exception("Observed native depletion releases only the scoped capacity limit.");
            c.EnergyCapacity = 600; c.idleEnergy += 100;
            if (owner.ExecutionResourceBudget(true, ResourceWriter.Reclaimable(true)) != 588)
                throw new Exception("After the hold, actual native growth must become fundable.");
            return 3;
        }

        internal static int RunSupport()
        {
            var settings = Main.Settings; var character = Main.Character; var main = Main.reference;
            var checks = 0;
            void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
            try
            {
                foreach (var boundary in new[] { "identity", "foreign-beard", "foreign-level", "epoch" })
                {
                    EquipmentRecording.Reset(true); Main.Settings = new FocusSettings();
                    Main.Character = new FocusCharacter(); Main.reference = new Main();
                    var owner = Main.reference; var c = Main.Character; var active = true;
                    c.diggers.activeDiggers.Add(3); c.diggers.diggers[3].active = true; c.diggers.diggers[3].curLevel = 2;
                    var claim = owner.BeginExecutionClaim("support-check", boundary, Main.ExecutionDomain.Support,
                        () => active && ReferenceEquals(Main.reference, owner) && ReferenceEquals(Main.Character, c));
                    var wanted = new Main.SelectedSetupRequirements { ControlsGear = false, ChangeSupport = true,
                        RequiredBeards = new[] { 0 }, Budget = ActivityPolicy.Current.DiggerBudgetFraction,
                        Support = new SupportSelection(new[] { 0, 1, 3, 5 }, new[] { new SupportDiggerLevel(3, 2) }) };
                    owner.ApplyExecutionClaim(claim, wanted);
                    var exact = Main.CaptureSupportForChecks().StableKey;
                    Check(exact == wanted.Support.StableKey && c.beards.beards[0].active, boundary + ": hard prerequisite installs exact native support");
                    Check(c.diggers.activeDiggers.SequenceEqual(new[] { 3 }) && c.diggers.diggers[3].curLevel == 2,
                        boundary + ": beard operation retains exact coupled digger levels");
                    var writes = EquipmentRecording.ExactSupport.Count;
                    owner.ApplyExecutionClaim(claim, wanted);
                    Check(EquipmentRecording.ExactSupport.Count == writes, boundary + ": verified replay makes no duplicate support write");
                    Check(owner.ExecutionOwnsDomain(Main.ExecutionDomain.Support), boundary + ": admitted claim retains its domain independently of baseline credit");
                    if (boundary == "identity") active = false;
                    else if (boundary == "foreign-beard") { c.beards.activeBeards.Remove(0); c.beards.beards[0].active = false; }
                    else if (boundary == "foreign-level") c.diggers.diggers[11].curLevel = 1;
                    else c.stats.rebirthNumber++;
                    var observed = c.beards.activeBeards.ToArray();
                    var level = c.diggers.diggers[11].curLevel;
                    var rejected = false;
                    try { owner.ApplyExecutionClaim(claim, wanted); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && EquipmentRecording.ExactSupport.Count == writes, boundary + ": stale or foreign support cannot authorize another write");
                    Check(c.beards.activeBeards.SequenceEqual(observed) && c.diggers.diggers[11].curLevel == level,
                        boundary + ": rejection preserves current native support, including inactive levels");
                    owner.EndExecutionClaim(claim);
                    Check(EquipmentRecording.ExactSupport.Count == writes, boundary + ": no current successor means no historical restore or implicit fallback");
                    Check(c.beards.activeBeards.SequenceEqual(observed) && c.diggers.diggers[11].curLevel == level,
                        boundary + ": claim exit leaves current native support untouched");
                    Check(EquipmentRecording.Gear.Count == 0 && EquipmentRecording.ExactApplications.Count == 0
                        && c.EnergyRecalls == 0 && c.MagicRecalls == 0, boundary + ": support claim never searches gear or recalls lanes");
                }
                Console.WriteLine("PASS: selected support authority: 4 timelines, " + checks + " assertions.");
                return checks;
            }
            finally { EquipmentRecording.Reset(false); Main.Settings = settings; Main.Character = character; Main.reference = main; }
        }
    }
}
