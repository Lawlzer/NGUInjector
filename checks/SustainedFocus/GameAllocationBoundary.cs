global using Character = NGUInjector.FocusCharacter;
global using GearOptimizerResult = NGUInjector.Managers.GearResult;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NGUInjector.Managers;
using NGUInjector.CustomPlans;

[assembly: InternalsVisibleTo("ActivityPolicyIntegrationChecks")]

namespace NGUInjector
{
    internal partial class Main
    {
        internal void InvalidateCustomEquipmentApplication() { ResetCustomEquipmentSetup(); }

        private object _globalPresetApplyingPolicy, _presetSupportAcquisition;
        private FixtureQuestOwner _questManager;
        private bool _resourceReconciliationPending;
        private bool _resourceReconciliationRunning;
        private string _resourceDemandIdentity, _globalPresetManualId, _globalPresetNativeTargetConflict;
        private SelectedSetupRequirements _manualAdventureBaseline;
        private static double LoadoutPresetClock => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        private string _customStageEquipmentKey, _protectedGeneration;
        private ProtectedStageScheduler _protectedScheduler => _checkScheduler;
        private FixturePresetReceipt _globalPresetReceipt;
        private sealed class FixturePresetReceipt
        {
            internal FixturePresetPolicy Policy;
            internal FixturePresetRoute Execution;
            internal ih[] Gear;
            internal string Owner, Id, Support, Allocations;
            internal ih[] TransitionGear;
            internal string[] TransitionMaterial;
            internal string TransitionSupport;
        }
        private sealed class FixturePresetRoute { internal int Zone, Floor; }
        private sealed class FixturePresetPolicy
        {
            internal bool MaterialPersistencePending;
            internal FixturePresetDefinition ExecutionPreset;
            internal Equipment[] Items;
            internal string[] Material;
            internal CustomPlanStage Stage;
            internal string Acquisition;
        }
        private sealed class FixturePresetDefinition { internal double[] Weights; }
        private static bool GlobalPresetEpochMatches(FixturePresetPolicy policy) => throw new NotSupportedException("Preset route authority is tested in the native fixture.");
        private bool GlobalPresetNativeTargetsMatch(CustomPlanStage stage, string owner) => throw new NotSupportedException("Preset native targets are tested in the native fixture.");
        private static string GlobalPresetSupportState() => CaptureSupportState().StableKey;
        private static string GlobalPresetAllocationState() => ResourceWriter.AllocatedResourceKey();
        private static string DelegatedManualRouteSettingsKey() => throw new NotSupportedException("Preset route authority is tested in the native fixture.");
        private IEnumerable<FixturePresetPolicy> RetainedExecutionPresetPolicies() => Enumerable.Empty<FixturePresetPolicy>();
        internal bool FailSelectionPersistence;
        internal int SelectionPersistenceWrites;
        private readonly Dictionary<string, SelectedSetupRequirements> _recordedSelections = new Dictionary<string, SelectedSetupRequirements>();
        private void PersistExecutionSetupSelection(string kind, string definition, SelectedSetupRequirements setup)
        {
            if (FailSelectionPersistence) throw new System.IO.IOException("fixture selection persistence failed");
            var selected = setup.Copy(); selected.Gear = setup.Gear.ToArray();
            _recordedSelections[kind] = selected; SelectionPersistenceWrites++;
        }
        private void ClearPersistedExecutionSelection(string kind) { _recordedSelections.Remove(kind); }
        private bool ManualAdventureBaselineAuthorized() => _manualAdventureBaseline?.Authorized?.Invoke() == true;
        private void ReconcileCustomResources(CustomPlan plan = null, bool allowSetup = false)
        {
            if (_customSustainedFocusGoal.HasValue) RunCustomSustainedFocusAllocation(false);
        }
        private sealed class FixtureQuestOwner { internal int IsQuesting() { return -1; } }
        private void SuspendProtectedStageProgress() { }
        private void BeginPresetSupportAcquisition(string eventKey, string acquisition)
        {
            _presetSupportAcquisition = acquisition;
        }
        private sealed class SupportApplicationResult { internal MutationDisposition Disposition; internal object Mutation; }
        private sealed class SupportApplicationException : InvalidOperationException
        {
            internal SupportApplicationResult Result;
        }
        private sealed class FocusSupportSnapshot { internal string StableKey; internal SupportSelection Selection; }
        private static object[] SupportObjects() => new object[] { Character, Controller, Character.beards, Character.diggers };
        private static string LoadoutPresetPrimitiveKey(Equipment item) => item?.type.ToString() ?? "null";
        private Action<ih[], SupportSelection> BeginInlineStageTransition() => null; // Actual inline evidence is covered by the compiled fixture.
        private Action<ih[], SupportSelection> BeginPresetSetupTransition() => null; // Actual preset transition evidence is covered by the compiled fixture.
        private bool SupportMutationStillOwned(object mutation) => false;
        private static FocusSupportSnapshot CaptureSupportState()
        {
            var c = Character;
            return new FocusSupportSnapshot { Selection = new SupportSelection(c.beards.activeBeards.ToArray(),
                c.diggers.activeDiggers.Select(id => new SupportDiggerLevel(id, c.diggers.diggers[id].curLevel)).ToArray()),
                StableKey = string.Join(";", new[] {
                string.Join(",", c.beards.activeBeards), string.Join(",", c.beards.beards.Select(x => x.active)),
                string.Join(",", c.diggers.activeDiggers),
                string.Join(",", c.diggers.diggers.Select(x => x.active + ":" + x.curLevel))
            })};
        }

        // Numeric native boundary for the real SupportProjection/selected transition.
        // Native mutation/failure evidence is exercised by --reviewed-executor, not
        // supplied by these small data-only focus controllers.
        private static SupportContext CaptureSupportContext()
        {
            var c = Character;
            return new SupportContext { BeardsAvailable = c.buttons?.beards?.interactable == true && c.allBeards != null
                    && c.beards?.beards != null && c.beards.activeBeards != null,
                BeardsDisabled = c.beards?.disabled == true, DiggersAvailable = c.diggers?.diggers != null && c.diggers.activeDiggers != null,
                BeardSlots = c.allBeards?.capBeards() ?? 0, DiggerSlots = 12,
                UnlockedBeards = Enumerable.Range(0, 7).Where(id => c.beards?.beards != null && id < c.beards.beards.Count
                    && c.beards.beards[id] != null && (id != 6 || c.challenges?.trollChallenge?.curCompletions >= 7)).ToArray(),
                CurrentBeards = c.beards?.activeBeards?.ToArray() ?? Array.Empty<int>(),
                GoldActiveGross = 1000d, GoldInactiveGross = 1000d, GrossGold = 1000d,
                Diggers = c.diggers.diggers.Select((d, id) => new SupportDigger { Id = id, Level = d.curLevel,
                    Maximum = d.maxLevel, BaseDrain = 1d, Growth = 1.1d, ConsumesGold = true }).ToArray() };
        }

        internal static SupportContext CaptureSupportContextForChecks() => CaptureSupportContext();

        private static void PreflightExactSupport(SupportSelection selection, double fraction, bool enforceBudget)
        {
            var result = SupportProjection.Evaluate(CaptureSupportContext(), selection, fraction, enforceBudget);
            if (!result.Feasible) throw new InvalidOperationException(result.Reason);
        }

        private void ApplyPresetDesiredSupport(SupportSelection selection, double fraction, bool enforceBudget)
        {
            if (_selectedSetupSupportAuthorized != null && !_selectedSetupSupportAuthorized())
                throw new InvalidOperationException("Fixture selected support authority changed.");
            PreflightExactSupport(selection, fraction, enforceBudget);
            var c = Character;
            if (CaptureSupportState().Selection.StableKey == selection.StableKey) return;
            // Recording boundaries remain visible; no legacy application workflow is
            // substituted for the new production transition.
            EquipmentRecording.Beards.Add(selection.Beards);
            if (EquipmentRecording.ThrowNextBeards)
            {
                EquipmentRecording.ThrowNextBeards = false;
                throw new InvalidOperationException("beard controller unavailable");
            }
            EquipmentRecording.Diggers.Add(selection.Diggers.Select(d => new DiggerManager.DiggerRequest { Id = d.Id }).ToArray());
            EquipmentRecording.ExactSupport.Add(selection);
            EquipmentRecording.Budgets.Add(fraction);
            if (EquipmentRecording.ThrowNextDiggers)
            {
                EquipmentRecording.ThrowNextDiggers = false;
                throw new InvalidOperationException("digger controller unavailable");
            }
            c.beards.activeBeards = selection.Beards.ToList();
            for (var id = 0; id < c.beards.beards.Count; id++) c.beards.beards[id].active = selection.Beards.Contains(id);
            c.diggers.activeDiggers = selection.Diggers.Select(d => d.Id).ToList();
            for (var id = 0; id < c.diggers.diggers.Count; id++) c.diggers.diggers[id].active = c.diggers.activeDiggers.Contains(id);
            foreach (var d in selection.Diggers) c.diggers.diggers[d.Id].curLevel = d.Level;
        }
    }

    internal sealed partial class FocusCharacter
    {
        // Explicit native capacity inputs. The real capacity projection/gear
        // sequence and native clamps remain compiled-native fixture requirements.
        internal long EnergyCapacity = 1000, MagicCapacity = 1000;
        internal long totalCapEnergy() => EnergyCapacity;
        internal long totalCapMagic() => MagicCapacity;
        internal TrainingBars allOffenseController = new TrainingBars(), allDefenseController = new TrainingBars();
        internal FruitData yggdrasil = new FruitData();
        internal FruitController yggdrasilController = new FruitController();
        internal Clock totalPlaytime = new Clock();
        internal FocusStats stats = new FocusStats();
        internal FocusDiggers diggers = new FocusDiggers();

        internal void removeAllEnergy()
        {
            removeMostEnergy();
            foreach (var lane in new[] {training.attackEnergy, training.defenseEnergy})
                for (var i = 0; i < lane.Length; i++) { idleEnergy += lane[i]; lane[i] = 0; }
        }

        internal void removeAllRes3()
        {
            foreach (var hack in hacks.hacks) { res3.idleRes3 += hack.res3; hack.res3 = 0; }
            foreach (var wish in wishes.wishes) { res3.idleRes3 += wish.res3; wish.res3 = 0; }
        }

        // Account for every non-owned lane in the same way as the data-only game fixture.
        internal long curEnergy => idleEnergy + training.attackEnergy.Sum() + training.defenseEnergy.Sum()
            + advancedTraining.energy.Sum() + augments.augs.Sum(x => x.augEnergy + x.upgradeEnergy)
            + wandoos98.wandoosEnergy + machine.speedEnergy + NGU.skills.Sum(x => x.energy)
            + wishes.wishes.Sum(x => x.energy);
    }

    internal sealed class TrainingBars { internal NguBar[] trains = Enumerable.Range(0, 6).Select(_ => new NguBar()).ToArray(); }
    internal sealed class FruitData { internal List<object> fruits = new List<object>(); }
    internal sealed class FruitBar { internal void activate(int index) { throw new NotSupportedException("Fruit activation is outside this focus fixture."); } }
    internal sealed class FruitController
    {
        internal FruitBar[] fruits = Enumerable.Range(0, 9).Select(_ => new FruitBar()).ToArray();
        internal void changePage(int page) { throw new NotSupportedException("Fruit activation is outside this focus fixture."); }
    }

    internal sealed class BasicTraining { internal long[] attackEnergy = new long[6], defenseEnergy = new long[6]; }
    internal sealed class FocusStats { internal long rebirthNumber; }
    internal sealed class FocusDigger { internal bool active; internal long curLevel, maxLevel = 4; }
    internal sealed class FocusDiggers
    {
        internal List<FocusDigger> diggers = Enumerable.Range(0, 12).Select(_ => new FocusDigger()).ToList();
        internal List<int> activeDiggers = new List<int>();
    }
    internal sealed class Hack { internal long res3; }
    internal sealed class Hacks { internal Hack[] hacks = Enumerable.Range(0, 16).Select(_ => new Hack()).ToArray(); }
    internal sealed class Res3 { internal long curRes3, idleRes3; }
    internal sealed class Beard { internal bool active; }
    internal sealed class Beards
    {
        internal List<Beard> beards = Enumerable.Range(0, 7).Select(_ => new Beard()).ToList();
        internal List<int> activeBeards = new List<int>();
        internal bool disabled;
    }
    internal sealed class AllBeardsController { internal int Slots = 7; internal int capBeards() { return Slots; } }
    internal enum part { Head, Chest, Legs, Boots, Weapon, Accessory }
    internal sealed class Equipment { internal part type; }
    internal sealed class ih
    {
        internal int slot, id, level;
        internal string name;
        internal Equipment equipment;
    }
}

namespace NGUInjector.Managers
{
    [Flags]
    internal enum GearRecallFootprint { None = 0, Energy = 1, Magic = 2, Res3 = 4 }
    internal sealed class GearMutationOutcome
    {
        internal MutationDisposition Disposition = MutationDisposition.AppliedVerified;
        internal string Reason, LegacyFailure;
        internal Exception Error;
        internal bool Verified => Disposition == MutationDisposition.AppliedVerified;
        internal bool KnownProgressStillOwned() => false; // This data boundary supplies no native partial-gear evidence.
    }
    internal static partial class LoadoutManager
    {
        internal static string ValidateExactLoadout(ih[] wanted) => wanted == null || wanted.Any(x => x?.equipment == null)
            || wanted.Select(x => x.slot).Distinct().Count() != wanted.Length
            || !wanted.Select(x => x.slot).OrderBy(x => x).SequenceEqual(EquipmentRecording.ExactLoadout.Select(x => x.slot).OrderBy(x => x))
                ? "Exact map must include every current slot." : null;
        internal static bool ExactLoadoutMatches(ih[] wanted)
        {
            return wanted.Length == EquipmentRecording.ExactLoadout.Length
                && wanted.All(x => EquipmentRecording.ExactLoadout.Any(y => x.slot == y.slot && ReferenceEquals(x.equipment, y.equipment)));
        }
        internal static GearMutationOutcome ApplyExactLoadoutObserved(ih[] wanted, GearRecallFootprint footprint, Func<bool> authorized = null)
        {
            if (!authorized()) return new GearMutationOutcome { Disposition = MutationDisposition.RejectedUnchanged };
            if (!ExactLoadoutMatches(wanted))
            {
                if ((footprint & GearRecallFootprint.Energy) != 0) ResourceWriter.Recall(true);
                if ((footprint & GearRecallFootprint.Magic) != 0) ResourceWriter.Recall(false);
                if ((footprint & GearRecallFootprint.Res3) != 0) ResourceWriter.RecallRes3();
                ApplyExactLoadout(wanted, true);
            }
            return new GearMutationOutcome { Disposition = authorized() ? MutationDisposition.AppliedVerified : MutationDisposition.ForeignConflict };
        }
        // Mirror the save/clock identity boundary; the production focus guard remains real.
        internal sealed class SnapshotEpoch
        {
            private readonly Character character = Main.Character;
            private readonly object inventory = Main.Character.inventory, diggers = Main.Character.diggers,
                yggdrasil = Main.Character.yggdrasil, stats = Main.Character.stats;
            private readonly long rebirth = Main.Character.stats.rebirthNumber;
            private readonly double elapsed = Main.Character.rebirthTime.totalseconds, total = Main.Character.totalPlaytime.totalseconds;

            internal bool Matches()
            {
                var c = Main.Character;
                return ReferenceEquals(c, character) && c?.rebirthTime != null && c.totalPlaytime != null
                    && ReferenceEquals(c.inventory, inventory) && ReferenceEquals(c.diggers, diggers)
                    && ReferenceEquals(c.yggdrasil, yggdrasil) && ReferenceEquals(c.stats, stats)
                    && c.stats.rebirthNumber == rebirth && c.rebirthTime.totalseconds + .1d >= elapsed
                    && c.totalPlaytime.totalseconds + .1d >= total
                    && Math.Abs(c.totalPlaytime.totalseconds - c.rebirthTime.totalseconds - (total - elapsed)) <= 1d;
            }
        }

        internal static ih[] CaptureExactLoadout()
        {
            return EquipmentRecording.ExactLoadout.ToArray();
        }

        internal static ih[] ComposeExactLoadout(ih[] current, ih[] replacements)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not compose gear.");
            var map = current.ToDictionary(item => item.slot);
            foreach (var item in replacements) map[item.slot] = item;
            return map.Values.OrderBy(item => item.slot).ToArray();
        }

        internal static string ApplyExactLoadout(ih[] wanted, bool resourcesAlreadyRemoved)
        {
            if (!EquipmentRecording.Enabled) throw new Exception("Sustain must not apply gear.");
            if (!resourcesAlreadyRemoved) throw new Exception("Magic-only focus must recall its owned resource lane before applying gear.");
            EquipmentRecording.ExactApplications.Add(wanted.ToArray());
            EquipmentRecording.ExactLoadout = wanted.ToArray();
            EquipmentRecording.AfterExactApplication?.Invoke();
            return null;
        }
    }
}
