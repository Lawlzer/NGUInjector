using System;
using System.Collections.Generic;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        private void RotateGlobalPresetBeards(DateTime now) { }
        internal void RunBeardsForChecks(CustomPlan plan) { RunCustomBeardAutomation(plan); }
        internal void EnsureBeardForChecks(int required) { EnsureCustomBeards(required); }
        internal string ApplyBeardsForChecks(IEnumerable<int> required, DateTime now) => ApplyCustomBeards(required, now);
        internal string ApplyActivityBeardsForChecks(CustomPlan plan, DateTime now) => ApplyCustomBeards(CustomActivityBeards(plan), now);
        internal DateTime LastBeardRotationForChecks => _customLastBeardRotation;
        internal void MarkBeardMinuteDueForChecks() { _customLastBeardRotation = DateTime.UtcNow.AddSeconds(-60); }
        internal void SetAppliedFocusForBeardsForChecks(CustomFocusGoal? goal) { _customAppliedFocusGoal = goal; }
        internal int[] ActivityRequirementsForChecks(CustomPlan plan) => ResolveCustomBeardRequirements(CustomActivityBeards(plan));
        internal void InvalidateBeardsForChecks() => InvalidateCustomBeardRequirements();
    }

    // Selection is an explicit data boundary; application, grants, retained choices
    // and coupled support verification are the real production implementations.
    internal static class BeardAutomationChecks
    {
        private static int _assertions;
        private static readonly DateTime Entry = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static void Check(bool value, string name) { _assertions++; if (!value) throw new Exception(name); }

        internal static int Run()
        {
            var settings = Main.Settings; var character = Main.Character; var owner = Main.reference;
            var cases = new Action[] { ExactMinuteCadence, RetainedRetry, HardPrerequisites, CoupledForeignYield, EpochNeedsReview,
                RequirementEventsKeepMinute, BeardOnlyKeepsExactDiggers, RetainedContextNeedsReview,
                NativeRequirementMatrix, ReadOnlySchedulerQueries };
            try
            {
                foreach (var test in cases)
                {
                    EquipmentRecording.Reset(true); Main.Settings = new FocusSettings(); Main.Character = new FocusCharacter(); Main.reference = new Main();
                    try { test(); } catch (Exception error) { throw new Exception(test.Method.Name + " failed", error); }
                    Check(EquipmentRecording.Gear.Count == 0 && EquipmentRecording.ExactApplications.Count == 0, test.Method.Name + " never searches or swaps gear");
                }
                Console.WriteLine("PASS: exact beard/support automation: " + cases.Length + " cases, " + _assertions + " assertions.");
                return _assertions;
            }
            finally { EquipmentRecording.Reset(false); Main.Settings = settings; Main.Character = character; Main.reference = owner; }
        }

        private static void ExactMinuteCadence()
        {
            Main.reference.ApplyBeardsForChecks(new int[0], Entry);
            Check(Main.Character.beards.activeBeards.OrderBy(x => x).SequenceEqual(new[] { 1, 3, 5 }), "initial selection is installed as exact native support");
            var writes = EquipmentRecording.ExactSupport.Count; var selections = EquipmentRecording.BeardRandomize.Count;
            Main.reference.ApplyBeardsForChecks(new int[0], Entry.AddSeconds(59));
            Check(EquipmentRecording.ExactSupport.Count == writes && EquipmentRecording.BeardRandomize.Count == selections, "59 seconds neither selects nor writes");
            Main.reference.ApplyBeardsForChecks(new int[0], Entry.AddSeconds(60));
            Check(EquipmentRecording.BeardRandomize.Count == selections + 1, "exact minute admits one selection event");
            Check(EquipmentRecording.ExactSupport.Count == writes, "identical selected support does not produce a redundant model write");
            Check(Main.reference.LastBeardRotationForChecks == Entry.AddSeconds(60), "selection's accepted minute is retained after verification");
        }

        private static void RetainedRetry()
        {
            EquipmentRecording.ThrowNextBeards = true;
            var rejected = false;
            try { Main.reference.ApplyBeardsForChecks(new int[0], Entry); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && Main.reference.LastBeardRotationForChecks == DateTime.MinValue, "rejected native application consumes no minute");
            var selections = EquipmentRecording.BeardRandomize.Count;
            Main.reference.ApplyBeardsForChecks(new int[0], Entry.AddSeconds(1));
            Check(EquipmentRecording.BeardRandomize.Count == selections, "retry retains exact selected beards without another draw");
            Check(Main.Character.beards.activeBeards.OrderBy(x => x).SequenceEqual(new[] { 1, 3, 5 }), "retry verifies actual native active flags");
            Check(Main.reference.LastBeardRotationForChecks == Entry, "retry does not renew the original accepted event time");
        }

        private static void HardPrerequisites()
        {
            var rejected = false;
            try { Main.reference.EnsureBeardForChecks(0); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && EquipmentRecording.ExactSupport.Count == 0, "missing hard Fu Manchu rejects before native support mutation");
            EquipmentRecording.BeardMessage = "beards:0,1,3,5";
            Main.reference.EnsureBeardForChecks(0);
            Check(Main.Character.beards.activeBeards.Contains(0) && Main.Character.beards.beards[0].active, "hard prerequisite is verified in native selection and active flag");
        }

        private static void RequirementEventsKeepMinute()
        {
            EquipmentRecording.BeardMessage = "beards:1,3,4,5";
            Main.reference.ApplyBeardsForChecks(new[] { 4, 2 }, Entry);
            EquipmentRecording.BeardMessage = "beards:1,2,3,5";
            Main.reference.ApplyBeardsForChecks(new[] { 2, 4 }, Entry.AddSeconds(1));
            Check(Main.Character.beards.activeBeards.Contains(2) && !Main.Character.beards.activeBeards.Contains(4),
                "changed optional order installs the selected event immediately");
            Check(Main.reference.LastBeardRotationForChecks == Entry, "requirement event does not postpone independent minute");
            var selections = EquipmentRecording.BeardRandomize.Count; var writes = EquipmentRecording.ExactSupport.Count;
            Main.reference.ApplyBeardsForChecks(new[] { 5, 2, 4, 1, 3, 2, 6 }, Entry.AddSeconds(2));
            Check(EquipmentRecording.BeardRandomize.Count == selections && EquipmentRecording.ExactSupport.Count == writes,
                "core ordering and duplicates do not manufacture a new optional acquisition");
            Main.reference.InvalidateCustomEquipmentApplication();
            Main.reference.ApplyBeardsForChecks(new[] { 2, 4 }, Entry.AddSeconds(3));
            Check(EquipmentRecording.BeardRandomize.Count == selections && EquipmentRecording.ExactSupport.Count == writes,
                "ordinary equipment application invalidation retains the selected beard event");
            Main.reference.ApplyBeardsForChecks(new[] { 2, 4 }, Entry.AddSeconds(60));
            Check(EquipmentRecording.BeardRandomize.Count == selections + 1 && EquipmentRecording.ExactSupport.Count == writes,
                "minute selects independently without rewriting unchanged native support");
            Check(Main.reference.LastBeardRotationForChecks == Entry.AddSeconds(60), "minute anchor follows accepted minute, not latest requirement event");
        }

        private static void BeardOnlyKeepsExactDiggers()
        {
            var c = Main.Character;
            c.diggers.activeDiggers.Add(3); c.diggers.diggers[3].active = true; c.diggers.diggers[3].curLevel = 2;
            c.diggers.diggers[11].curLevel = 3;
            Main.reference.ApplyBeardsForChecks(new int[0], Entry);
            Main.reference.ApplyBeardsForChecks(new int[0], Entry.AddSeconds(60));
            Check(c.diggers.activeDiggers.SequenceEqual(new[] { 3 }) && c.diggers.diggers[3].curLevel == 2,
                "beard-only events preserve exact active diggers instead of applying a weighted manager policy");
            Check(c.diggers.diggers[11].curLevel == 3 && !c.diggers.diggers[11].active,
                "unrequested inactive levels remain native user state");
            Check(EquipmentRecording.ExactSupport.Count == 1 && Main.reference.LastBeardRotationForChecks == Entry.AddSeconds(60),
                "identical coupled support needs no minute model rewrite");
        }

        private static void RetainedContextNeedsReview()
        {
            foreach (var capacity in new[] { false, true })
            {
                EquipmentRecording.Reset(true); Main.Character = new FocusCharacter(); Main.reference = new Main();
                Main.reference.ApplyBeardsForChecks(new int[0], Entry);
                var native = Main.CaptureSupportForChecks().StableKey; var writes = EquipmentRecording.ExactSupport.Count;
                if (capacity) Main.Character.allBeards.Slots--;
                else Main.Character.challenges.trollChallenge.curCompletions = 6;
                var rejected = false;
                try { Main.reference.ApplyBeardsForChecks(new int[0], Entry.AddSeconds(1)); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && EquipmentRecording.ExactSupport.Count == writes,
                    "retained " + (capacity ? "capacity" : "unlock") + " change requires review, not implicit reselection");
                Check(Main.CaptureSupportForChecks().StableKey == native && Main.reference.LastBeardRotationForChecks == Entry,
                    "context change preserves exact native support and the original minute anchor");
            }
        }

        private static void CoupledForeignYield()
        {
            Main.reference.ApplyBeardsForChecks(new int[0], Entry);
            Main.Character.diggers.diggers[2].active = true; Main.Character.diggers.activeDiggers.Add(2);
            var writes = EquipmentRecording.ExactSupport.Count; var rejected = false;
            try { Main.reference.ApplyBeardsForChecks(new int[0], Entry.AddSeconds(60)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && EquipmentRecording.ExactSupport.Count == writes, "foreign digger edit yields the coupled beard/digger domain");
            Check(Main.Character.diggers.activeDiggers.Contains(2), "foreign digger policy is preserved rather than silently resized");
        }

        private static void EpochNeedsReview()
        {
            Main.reference.ApplyBeardsForChecks(new int[0], Entry);
            var writes = EquipmentRecording.ExactSupport.Count; Main.Character.stats.rebirthNumber++;
            var rejected = false;
            try { Main.reference.ApplyBeardsForChecks(new int[0], Entry.AddSeconds(60)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && EquipmentRecording.ExactSupport.Count == writes, "matching support in a new epoch is not cleanup or replacement authority");
            var grant = Main.reference.ResolveExecutionGrant("review", "support", Main.ExecutionDomain.Support, () => true);
            Check(Main.reference.ReauthorizeExecutionDomains(grant, Main.ExecutionDomain.Support, () => true), "explicit current-session review may rebind the support scope");
            Check(!Main.reference.ValidateExecutionGrant(grant, Main.ExecutionDomain.Support), "review grant is consumed by its own scoped revision");
        }

        private static void NativeRequirementMatrix()
        {
            for (var mask = 0; mask < 16; mask++)
            {
                Main.reference._bossPushRunning = (mask & 1) != 0; Main.reference._manualRebirthPending = (mask & 2) != 0;
                Main.reference._customTitanGoldActive = (mask & 4) != 0;
                Main.Character.wandoos98.wandoosEnergy = (mask & 8) != 0 ? 1 : 0;
                var required = Main.reference.ActivityRequirementsForChecks(null);
                Check(required.Contains(0) == ((mask & 1) != 0), "Fu follows real boss activity/" + mask);
                Check(required.Contains(2) == ((mask & 2) != 0), "NUMBER follows real rebirth preparation/" + mask);
                Check(required.Contains(6) == ((mask & 4) != 0), "Gold follows real titan context/" + mask);
                Check(required.Contains(4) == ((mask & 8) != 0 && (mask & 3) == 0), "Ladybeard follows actual W work except exclusive preparation/" + mask);
            }
            Check(EquipmentRecording.ExactSupport.Count == 0, "requirement observation never mutates native support");
        }

        private static void ReadOnlySchedulerQueries()
        {
            Main.Settings.UseCustomRebirthPlan = true;
            var plan = new CustomPlan { Name = "read-only", StageLoopStartIndex = -1, StageDurationSeconds = 60,
                Stages = new[] { new CustomPlanStage { EnergyFocus = (int)CustomStageFocus.Wandoos, DurationSeconds = 60 } } };
            Check(Main.reference.SchedulerStateForChecks == null, "query starts without scheduler commitments");
            Main.reference.ActivityRequirementsForChecks(plan);
            Check(Main.reference.SchedulerStateForChecks == null, "beard query cannot initialize an entry or deadline");
            Main.reference.GetProtectedStage(plan);
            var before = Main.reference.SchedulerStateForChecks;
            Main.reference.ActivityRequirementsForChecks(plan);
            Check(Main.reference.SchedulerStateForChecks == before, "beard query cannot complete AT, advance stages or renew credit");
            Check(EquipmentRecording.ExactSupport.Count == 0, "scheduler query does not apply support");
        }
    }
}
