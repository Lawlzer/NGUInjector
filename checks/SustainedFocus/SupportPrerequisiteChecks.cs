using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal static class SupportPrerequisiteChecks
    {
        internal static int Run()
        {
            var settings = Main.Settings; var character = Main.Character; var main = Main.reference;
            var checks = 0; var cases = 0; var failures = new List<Exception>();
            void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
            try
            {
                foreach (var mode in new[] { "cached", "retained", "pending", "combat-prep", "support-claim" })
                foreach (var state in mode == "combat-prep" || mode == "support-claim"
                    ? new[] { "missing", "present", "flag-only", "locked", "disabled", "controller-missing", "gold-locked", "zero-capacity" } : new[] { "missing" })
                {
                    cases++;
                    EquipmentRecording.Reset(true); Main.Settings = new FocusSettings();
                    Main.Character = new FocusCharacter(); Main.reference = new Main();
                    var owner = Main.reference; var c = Main.Character;
                    c.allBeards.Slots = 3;
                    var now = DateTime.UtcNow.AddSeconds(-1);
                    try
                    {
                        if (mode == "cached" || mode == "retained" || mode == "pending")
                        {
                            c.diggers.activeDiggers.Add(3); c.diggers.diggers[3].active = true; c.diggers.diggers[3].curLevel = 2;
                            c.diggers.diggers[11].curLevel = 3;
                            EquipmentRecording.ThrowNextBeards = mode == "pending";
                            var interrupted = false;
                            try { owner.ApplyBeardsForChecks(new[] { 0 }, now); }
                            catch (InvalidOperationException) { interrupted = true; }
                            Check(interrupted == (mode == "pending"), mode + ": optional control reaches the intended native boundary");
                            Check(!c.beards.activeBeards.Contains(0), mode + ": optional Fu cannot displace the three-core prefix");
                            if (mode == "retained") owner.InvalidateBeardsForChecks();
                            EquipmentRecording.BeardMessage = "beards:0,1,3";
                            owner.EnsureBeardForChecks(0);
                            Check(c.beards.activeBeards.SequenceEqual(new[] { 0, 1, 3 }) && c.beards.beards[0].active,
                                mode + ": hard Fu replaces the optional-only acquired selection, not false cached readiness");
                            Check(c.diggers.activeDiggers.SequenceEqual(new[] { 3 }) && c.diggers.diggers[3].curLevel == 2
                                && !c.diggers.diggers[11].active && c.diggers.diggers[11].curLevel == 3,
                                mode + ": hard promotion retains exact active and inactive digger policy");
                            Check(owner.LastBeardRotationForChecks == now, mode + ": hard requirement does not renew the original accepted minute, including pending promotion");
                            var writes = EquipmentRecording.ExactSupport.Count; var selections = EquipmentRecording.BeardRandomize.Count;
                            var minute = owner.LastBeardRotationForChecks;
                            owner.EnsureBeardForChecks(0);
                            Check(EquipmentRecording.ExactSupport.Count == writes && EquipmentRecording.BeardRandomize.Count == selections,
                                mode + ": repeated hard request neither searches nor rewrites acquired support");
                            Check(owner.LastBeardRotationForChecks == minute, mode + ": repeated hard verification consumes no minute");
                        }
                        else
                        {
                            var selected = state == "missing" ? new[] { 1, 3, 5 }
                                : state == "gold-locked" ? new[] { 1, 3, 6 } : new[] { 0, 1, 3 };
                            if (mode == "support-claim")
                            {
                                var claim = owner.BeginExecutionClaim("prerequisite-check", state, Main.ExecutionDomain.Support,
                                    () => ReferenceEquals(Main.reference, owner) && ReferenceEquals(Main.Character, c));
                                owner.ApplyExecutionClaim(claim, new Main.SelectedSetupRequirements { ControlsGear = false, ChangeSupport = true,
                                    Budget = 1, Support = new SupportSelection(selected, Array.Empty<SupportDiggerLevel>()) });
                            }
                            else
                            {
                                c.beards.activeBeards = selected.ToList();
                                for (var id = 0; id < c.beards.beards.Count; id++) c.beards.beards[id].active = selected.Contains(id);
                                owner.CombatPrepForChecks = true;
                            }
                            if (state == "flag-only") c.beards.beards[0].active = false;
                            if (state == "locked") c.buttons.beards.interactable = false;
                            if (state == "disabled") c.beards.disabled = true;
                            if (state == "controller-missing") c.allBeards = null;
                            if (state == "gold-locked") c.challenges.trollChallenge.curCompletions = 6;
                            if (state == "zero-capacity") c.allBeards.Slots = 0;
                            var before = Main.CaptureSupportForChecks().StableKey;
                            var flags = c.beards.beards.Select(b => b.active).ToArray();
                            var writes = EquipmentRecording.ExactSupport.Count;
                            var selections = EquipmentRecording.BeardRandomize.Count;
                            owner.ApplyBeardsForChecks(new[] { 4 }, now);
                            var rejected = false;
                            try { owner.EnsureBeardForChecks(state == "gold-locked" ? 6 : 0); } catch (InvalidOperationException) { rejected = true; }
                            Check(rejected == (state != "present"), mode + "/" + state + ": frozen support verifies actual available hard prerequisites or rejects");
                            Check(Main.CaptureSupportForChecks().StableKey == before && flags.SequenceEqual(c.beards.beards.Select(b => b.active)),
                                mode + "/" + state + ": hard verification never changes the frozen native set or flags");
                            Check(EquipmentRecording.ExactSupport.Count == writes && EquipmentRecording.BeardRandomize.Count == selections,
                                mode + "/" + state + ": freeze/claim verification neither selects nor writes");
                            Check(owner.LastBeardRotationForChecks == DateTime.MinValue, mode + "/" + state + ": frozen prerequisite query consumes no minute");
                        }
                        Check(EquipmentRecording.Gear.Count == 0 && EquipmentRecording.ExactApplications.Count == 0
                            && c.EnergyRecalls == 0 && c.MagicRecalls == 0, mode + "/" + state + ": hard support verification never searches gear or recalls resources");
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine("FAIL hard support prerequisite " + mode + "/" + state + ": " + error.Message);
                        failures.Add(new InvalidOperationException(mode + "/" + state, error));
                    }
                }
                if (failures.Count != 0) throw new InvalidOperationException("Hard support prerequisite regressions: " + failures.Count + "/" + cases
                    + " failed: " + string.Join(", ", failures.Select(error => error.Message)), failures[0]);
                Console.WriteLine("PASS: hard support prerequisites: " + cases + " timelines, " + checks + " assertions.");
                return checks;
            }
            finally { EquipmentRecording.Reset(false); Main.Settings = settings; Main.Character = character; Main.reference = main; }
        }
    }
}
