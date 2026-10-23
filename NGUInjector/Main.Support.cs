using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NGUInjector.CustomPlans;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        // One synchronous transaction, replaced only after successful preflight. Never a rollback promise.
        private sealed class SupportMutation
        {
            internal SupportState Before, After;
            internal object[] Objects;
            internal long Rebirth;
            internal double Elapsed, Total;
            internal bool Completed, Interfered;
            internal Func<bool> Authorized;
            internal readonly HashSet<int> TouchedLevelIds = new HashSet<int>();
            internal SupportPodDraft[] PodDrafts = new SupportPodDraft[0];
        }
        private sealed class SupportPodDraft
        {
            internal GoldDiggerUIController Pod;
            internal UnityEngine.UI.InputField Input;
            internal int Id;
            internal string Text, ExpectedText;
            internal bool Owned = true;
        }
        private SupportMutation _supportMutation;

        private static SupportState CaptureSupportState()
        {
            var state = ObserveSupportState();
            var b = state.Selection.Beards; var d = state.Selection.Diggers.Select(x => x.Id).ToArray();
            if (b.Distinct().Count() != b.Length || d.Distinct().Count() != d.Length
                || !state.BeardFlags.SequenceEqual(b) || !state.DiggerFlags.SequenceEqual(d))
                throw new InvalidOperationException("Support active flags/lists disagree or contain duplicate IDs.");
            return state;
        }

        private static SupportState ObserveSupportState()
        {
            var c = Character;
            if (c?.beards?.beards == null || c.beards.activeBeards == null || c.diggers?.diggers == null || c.diggers.activeDiggers == null
                || c.beards.beards.Count > 7 || c.diggers.diggers.Count > 12 || c.beards.beards.Any(x => x == null) || c.diggers.diggers.Any(x => x == null))
                throw new InvalidOperationException("Support models unavailable or outside bounded capture.");
            if (c.beards.activeBeards.Count > 7 || c.diggers.activeDiggers.Count > 12)
                throw new InvalidOperationException("Support active lists contain duplicate IDs or exceed bounded capture.");
            var b = c.beards.activeBeards.ToArray(); var d = c.diggers.activeDiggers.ToArray();
            if (b.Any(x => x < 0 || x >= c.beards.beards.Count) || d.Any(x => x < 0 || x >= c.diggers.diggers.Count))
                throw new InvalidOperationException("Support active IDs are outside model bounds.");
            return new SupportState(new SupportSelection(b, d.Select(i => new SupportDiggerLevel(i, c.diggers.diggers[i].curLevel)).ToArray()),
                c.diggers.diggers.Select((x, i) => new SupportDiggerLevel(i, x.curLevel)).ToArray(),
                Enumerable.Range(0, c.beards.beards.Count).Where(i => c.beards.beards[i].active).ToArray(),
                Enumerable.Range(0, c.diggers.diggers.Count).Where(i => c.diggers.diggers[i].active).ToArray());
        }

        private static object[] SupportObjects()
        {
            var c = Character;
            var pods = c.allDiggers.pods;
            if (pods != null && pods.Count > 12) throw new InvalidOperationException("Support input controller list exceeds bounded capture.");
            return new object[] { c, Controller, c.beards, c.beards.beards, c.beards.activeBeards, c.allBeards,
                c.diggers, c.diggers.diggers, c.diggers.activeDiggers, c.allDiggers, pods, c.stats, c.rebirthTime, c.totalPlaytime,
                c.machine, c.timeMachineController, c.bloodMagicController, c.NGUController, c.challenges, c.allChallenges,
                c.buttons, c.buttons.beards, c.buttons.diggers }
                .Concat(c.beards.beards.Cast<object>()).Concat(c.diggers.diggers.Cast<object>())
                .Concat(pods == null ? new object[0] : pods.SelectMany(x => new object[] { x, x == null ? null : x.diggerLevelInput })).ToArray();
        }

        private static SupportContext CaptureSupportContext()
        {
            var state = CaptureSupportState(); var c = Character; var ctrl = c.allDiggers; var beard = c.allBeards;
            if (ctrl == null || beard == null || !ReferenceEquals(ctrl.character, c) || !ReferenceEquals(beard.character, c)
                || c.inventory == null || c.buttons?.diggers == null || c.buttons.beards == null || c.stats == null
                || c.rebirthTime == null || c.totalPlaytime == null || c.machine == null || c.timeMachineController == null
                || c.bloodMagicController == null || c.NGUController == null || c.allChallenges?.timeMachineChallenge == null
                || c.challenges?.timeMachineChallenge == null || c.challenges.trollChallenge == null
                || c.diggers.diggers.Count != 12 || c.beards.beards.Count != 7
                || ctrl.baseGPSDrain == null || ctrl.gpsGrowthRate == null || ctrl.consumesGPS == null || ctrl.startingBoost == null || ctrl.boostPerLevel == null
                || ctrl.baseGPSDrain.Count < 12 || ctrl.gpsGrowthRate.Count < 12 || ctrl.consumesGPS.Count < 12 || ctrl.startingBoost.Count < 12 || ctrl.boostPerLevel.Count < 12)
                throw new InvalidOperationException("Support controllers/tables unavailable.");
            var s = new SupportContext { BeardsAvailable = c.buttons.beards.interactable, DiggersAvailable = c.buttons.diggers.interactable,
                BeardsDisabled = c.beards.disabled, CurrentBeards = state.Selection.Beards,
                BeardSlots = c.buttons.beards.interactable ? beard.capBeards() : 0,
                DiggerSlots = c.buttons.diggers.interactable ? ctrl.maxDiggerSlots() : 0,
                UnlockedBeards = Enumerable.Range(0, 7).Where(i => i != 6 || c.challenges.trollChallenge.curCompletions >= 7).ToArray(),
                DiggerLevelBonus = ctrl.totalLevelBonus(),
                BeardTemporary = new[] { beard.tempNGUBonus(true), beard.tempGoldBonus(true), beard.tempAdventureBonus(true), beard.tempLootBonus(true) },
                BeardPermanent = new[] { beard.permNGUBonus(), beard.permGoldBonus(), beard.permAdventureBonus(), beard.permLootBonus() },
                BeardEffective = new[] { beard.nguBonus(), beard.goldBonus(), beard.adventureBonus(), beard.lootBonus() },
                BeardWTemporary = beard.tempWandoosBonus(true), BeardWPermanent = beard.permWandoosBonus(), BeardWEffective = beard.wandoosBonus(),
                BeardStatTemporary = beard.tempStatBonus(true), BeardStatPermanent = beard.permStatBonus(), BeardStatEffective = beard.statBonus() };
            var diggerNames = PresetDefinitions.DiggerNames;
            s.Diggers = Enumerable.Range(0, 12).Select(i => new SupportDigger { Id = i,
                Name = diggerNames[i],
                Maximum = c.diggers.diggers[i].maxLevel, Level = c.diggers.diggers[i].curLevel,
                BaseDrain = ctrl.baseGPSDrain[i], Growth = ctrl.gpsGrowthRate[i], ConsumesGold = ctrl.consumesGPS[i],
                StartBonus = ctrl.startingBoost[i], PerLevel = ctrl.boostPerLevel[i] }).ToArray();
            s.CurrentDiggers = s.Diggers.Where(x => state.Selection.Diggers.Any(y => y.Id == x.Id)).ToArray();
            if (s.Diggers.Any(x => x.Maximum < 0 || x.Level < 0 || x.Level > x.Maximum || !SupportProjection.Finite(x.StartBonus)
                || !SupportProjection.Finite(x.PerLevel) || x.BaseDrain < 0 || !SupportProjection.Finite(x.BaseDrain) || x.Growth < 1 || !SupportProjection.Finite(x.Growth))
                || s.BeardSlots < 0 || s.DiggerSlots < 0 || !SupportProjection.Finite(s.DiggerLevelBonus) || s.DiggerLevelBonus < 1)
                throw new InvalidOperationException("Invalid support levels, purchased bounds or numeric tables.");
            // Disabled beards can retain dormant native flags/lists. Capture them; exact evaluation still refuses activation.
            if (s.CurrentBeards.Length > s.BeardSlots || s.CurrentBeards.Any(x => !s.UnlockedBeards.Contains(x))
                || s.CurrentDiggers.Length > s.DiggerSlots || s.CurrentDiggers.Any(x => x.Level <= 0)
                || s.BeardTemporary.Concat(s.BeardPermanent).Concat(s.BeardEffective).Any(x => !SupportProjection.Finite(x) || x < 1)
                || new[] { (double)s.BeardWTemporary, s.BeardWPermanent, s.BeardWEffective, s.BeardStatTemporary, s.BeardStatPermanent, s.BeardStatEffective }
                    .Any(x => !SupportProjection.Finite(x) || x < 1))
                throw new InvalidOperationException("Invalid current support slots, unlocks or beard effects.");
            var baseGold = c.machine.realBaseGold; var boss = c.machineBossMulti(); var fills = c.timeMachineController.barFillsPerSecond();
            var speed = c.timeMachineController.speedGoldMultiBonus(); var gold = c.timeMachineController.goldMultiBonus();
            var blood = c.bloodMagicController.goldBonus(); var ngu = c.NGUController.timeMachineBonus();
            var challenge = c.allChallenges.timeMachineChallenge.totalGPSbonus(); var blocked = c.challenges.timeMachineChallenge.inChallenge;
            s.GoldActiveGross = SupportProjection.ForwardGrossGold(baseGold, boss, fills, speed, gold, blood,
                (float)SupportProjection.BeardBonus(s, new[] { 6 }, 6), ngu, challenge, blocked);
            s.GoldInactiveGross = SupportProjection.ForwardGrossGold(baseGold, boss, fills, speed, gold, blood,
                (float)SupportProjection.BeardBonus(s, new int[0], 6), ngu, challenge, blocked);
            s.GrossGold = c.grossGoldPerSecond();
            var drain = s.CurrentDiggers.OrderBy(x => x.Id).Sum(x => x.Drain(x.Level));
            // A valid native observation may overspend; only the desired selection must be affordable.
            if (!SupportProjection.Finite(s.GrossGold) || s.GrossGold < 0 || !SupportProjection.Finite(drain)
                || !SupportProjection.Finite(s.GoldActiveGross) || !SupportProjection.Finite(s.GoldInactiveGross))
                throw new InvalidOperationException("Invalid captured support income/drain.");
            RequireSupportParity(SupportProjection.GrossGold(s, s.CurrentBeards), s.GrossGold, "current gross GPS");
            // Preserve the unchanged native Double result after checking the forward Single-factor boundary.
            if (s.CurrentBeards.Contains(6)) s.GoldActiveGross = s.GrossGold; else s.GoldInactiveGross = s.GrossGold;
            RequireSupportParity(drain, ctrl.totalGPSDrain(), "current digger drain");
            var native = CaptureSupportDiggerEffects();
            for (var i = 0; i < native.Length; i++) RequireSupportParity(SupportProjection.DiggerBonus(s, s.CurrentSelection, i), native[i], "digger effect " + i);
            s.DiggerEffective = native;
            return s;
        }

        private static double[] CaptureSupportDiggerEffects()
        {
            var d = Character.allDiggers;
            // Daycare's parameterless getter adds gear/challenges. Only its pure digger overload belongs here.
            var singles = new[] { d.totalDropChanceBonus(), d.totalWandoosSpeedBonus(), 1f, d.totalAdventureBonus(),
                d.totalEnergyNGUBonus(), d.totalMagicNGUBonus(), d.totalEnergyBeardBonus(), d.totalMagicBeardBonus(), d.totalPPBonus(),
                d.totalDaycareBonus(0, false), d.totalBloodBonus(), d.totalEXPBonus() };
            var effects = singles.Select(x => (double)x).ToArray();
            effects[2] = d.totalStatBonus();
            return effects;
        }

        private static void RequireSupportParity(double expected, double actual, string name)
        {
            // Desktop CLR may retain one extra Single ULP across native getter boundaries. Never invert a total.
            if (!SupportProjection.Finite(expected) || !SupportProjection.Finite(actual)
                || Math.Abs(expected - actual) > Math.Max(Math.Abs(expected), Math.Abs(actual)) * 1e-6)
                throw new InvalidOperationException("Native support parity failed: " + name + ".");
        }

        private static SupportEvaluation PreflightExactSupport(SupportSelection selection, double budgetFraction, bool enforceBudget,
            SupportDiggerLevel[] inactiveLevels = null)
        {
            if (reference?._activeSetupGrant != null && !reference.ValidateExecutionGrant(reference._activeSetupGrant, reference._activeSetupGrant.Domains)
                || Controller == null || Controller.midDrag)
                throw new InvalidOperationException("Support is locked or inventory drag is active.");
            if (Character == null || !ReferenceEquals(Controller, Character.inventoryController) || !ReferenceEquals(Controller.character, Character))
                throw new InvalidOperationException("Support inventory controller belongs to a different game context.");
            var context = CaptureSupportContext();
            var result = SupportProjection.Evaluate(context, selection, budgetFraction, enforceBudget);
            if (!result.Feasible) throw new InvalidOperationException(result.Reason);
            inactiveLevels = inactiveLevels ?? new SupportDiggerLevel[0];
            if (inactiveLevels.Length > 12 || inactiveLevels.Select(x => x.Id).Distinct().Count() != inactiveLevels.Length)
                throw new InvalidOperationException("Inactive digger input IDs must be unique and bounded.");
            if (inactiveLevels.Any(x => selection.Diggers.Any(y => y.Id == x.Id)))
                throw new InvalidOperationException("Inactive digger input conflicts with a selected digger ID.");
            if (inactiveLevels.Any(x => x.Level < 0 || !context.Diggers.Any(y => y.Id == x.Id && x.Level <= y.Maximum)))
                throw new InvalidOperationException("Inactive digger input exceeds exact zero-to-purchased level bounds.");
            if (inactiveLevels.Length > 0 && !context.DiggersAvailable)
                throw new InvalidOperationException("Inactive digger inputs are unavailable.");
            var levels = selection.Diggers.Concat(inactiveLevels).ToArray();
            if (levels.Length > 0)
            {
                if (Character.allDiggers.pods == null || Character.allDiggers.pods.Count > 12)
                    throw new InvalidOperationException("Exact digger input controller list is unavailable or unbounded.");
                var pod = Character.allDiggers.pods?.FirstOrDefault(x => x != null && x.diggerLevelInput != null && ReferenceEquals(x.character, Character));
                if (pod == null || pod.id < 0 || pod.id >= context.Diggers.Length)
                    throw new InvalidOperationException("Exact digger input controller is unavailable.");
                foreach (var d in levels)
                    if (long.Parse(d.Level.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) != d.Level)
                        throw new InvalidOperationException("Exact digger input is not representable.");
            }
            return result;
        }

        private bool SupportMutationContextMatches(SupportMutation receipt)
        {
            try
            {
                if (receipt == null || !ReferenceEquals(receipt, _supportMutation)) return false;
                var objects = SupportObjects(); var c = Character;
                return objects.Length == receipt.Objects.Length && !objects.Where((x, i) => !ReferenceEquals(x, receipt.Objects[i])).Any()
                    && ReferenceEquals(c.allDiggers.character, c) && ReferenceEquals(c.allBeards.character, c) && ReferenceEquals(Controller.character, c)
                    && c.stats.rebirthNumber == receipt.Rebirth && c.rebirthTime.totalseconds >= receipt.Elapsed && c.totalPlaytime.totalseconds >= receipt.Total
                    && Math.Abs((c.totalPlaytime.totalseconds - c.rebirthTime.totalseconds) - (receipt.Total - receipt.Elapsed)) <= .001;
            }
            catch { return false; }
        }

        private bool SupportMutationStillOwned(SupportMutation receipt)
        {
            try { return SupportMutationContextMatches(receipt) && !receipt.Interfered && receipt.After != null && ObserveSupportState().StableKey == receipt.After.StableKey; }
            catch { return false; }
        }

        private bool SupportPodDraftStillOwned(SupportMutation receipt, SupportPodDraft draft, int id, params string[] allowedText)
        {
            if (draft.Owned && SupportMutationContextMatches(receipt) && ReferenceEquals(draft.Pod.character, Character) && draft.Pod.id == id
                && allowedText.Contains(draft.Input.text)) return true;
            draft.Owned = false; receipt.Interfered = true;
            return false;
        }

        private void RestoreSupportPodDrafts(SupportMutation receipt)
        {
            Exception failure = null;
            foreach (var draft in receipt.PodDrafts)
            {
                if (!draft.Owned || !SupportMutationContextMatches(receipt) || receipt.Authorized != null && !receipt.Authorized()) continue;
                try
                {
                    if (SupportPodDraftStillOwned(receipt, draft, draft.Id, draft.ExpectedText))
                    {
                        draft.Input.text = draft.Text;
                        draft.ExpectedText = draft.Text;
                        SupportPodDraftStillOwned(receipt, draft, draft.Id, draft.ExpectedText);
                    }
                }
                catch (Exception error) { draft.Owned = false; receipt.Interfered = true; failure = failure ?? error; }
            }
            if (failure != null) throw new InvalidOperationException("Support input draft restoration failed.", failure);
        }

        private void WriteSupport(SupportMutation receipt, Action write, int beardId = -1, int diggerId = -1, int levelId = -1, bool clearDiggers = false, long? exactLevel = null)
        {
            RequireSupportAuthority(receipt);
            if (!SupportMutationStillOwned(receipt)) throw new InvalidOperationException("Support changed outside the synchronous transaction.");
            foreach (var draft in receipt.PodDrafts)
                if (!SupportPodDraftStillOwned(receipt, draft, draft.Id, draft.ExpectedText))
                    throw new InvalidOperationException("Support input draft changed outside the synchronous transaction.");
            var before = receipt.After;
            if (levelId >= 0) receipt.TouchedLevelIds.Add(levelId);
            try { write(); }
            finally
            {
                try
                {
                    var after = ObserveSupportState();
                    if (clearDiggers && (after.Selection.Diggers.Length != 0 || after.DiggerFlags.Length != 0)
                        || after.Selection.Beards.Distinct().Count() != after.Selection.Beards.Length
                        || after.Selection.Diggers.Select(x => x.Id).Distinct().Count() != after.Selection.Diggers.Length
                        || exactLevel.HasValue && after.Levels.Any(x => x.Id == levelId && x.Level != exactLevel.Value && !before.Levels.Any(y => y.Id == x.Id && y.Level == x.Level))
                        || before.Levels.Where(x => x.Id != levelId).Any(x => !after.Levels.Any(y => x.Id == y.Id && x.Level == y.Level))
                        || !before.Selection.Beards.Where(x => x != beardId).SequenceEqual(after.Selection.Beards.Where(x => x != beardId))
                        || !before.BeardFlags.Except(new[] { beardId }).SequenceEqual(after.BeardFlags.Except(new[] { beardId }))
                        || !clearDiggers && (!before.DiggerFlags.Except(new[] { diggerId }).SequenceEqual(after.DiggerFlags.Except(new[] { diggerId }))
                            || !before.Selection.Diggers.Where(x => x.Id != diggerId).Select(x => x.Id).SequenceEqual(after.Selection.Diggers.Where(x => x.Id != diggerId).Select(x => x.Id)))
                        || !clearDiggers && !before.Selection.Diggers.Where(x => x.Id != diggerId && x.Id != levelId)
                            .Select(x => x.Id + ":" + x.Level).SequenceEqual(after.Selection.Diggers.Where(x => x.Id != diggerId && x.Id != levelId).Select(x => x.Id + ":" + x.Level)))
                        receipt.Interfered = true;
                    receipt.After = after;
                    foreach (var draft in receipt.PodDrafts)
                    {
                        var nativeText = before.Levels.Concat(after.Levels).Where(x => x.Id == draft.Id)
                            .Select(x => x.Level.ToString(CultureInfo.CurrentCulture)).Concat(new[] { draft.ExpectedText }).ToArray();
                        if (SupportPodDraftStillOwned(receipt, draft, draft.Id, nativeText))
                            draft.ExpectedText = draft.Input.text;
                    }
                    if (!SupportMutationStillOwned(receipt)) receipt.Interfered = true;
                }
                catch { receipt.Interfered = true; }
            }
            if (receipt.Interfered) throw new InvalidOperationException("Unexpected foreign support mutation during native write.");
            RequireSupportAuthority(receipt);
        }

        private static void RequireSupportAuthority(SupportMutation receipt)
        {
            if (receipt.Authorized == null || !receipt.Authorized())
                throw new InvalidOperationException("Selected setup support footprint is no longer authorized.");
        }

        private void SetExactSupportDiggerLevel(SupportMutation receipt, SupportDiggerLevel level)
        {
            var c = Character;
            if (c.diggers.diggers[level.Id].active) throw new InvalidOperationException("Digger became active before exact input.");
            var pod = c.allDiggers.pods.First(x => x != null && x.diggerLevelInput != null && ReferenceEquals(x.character, c));
            var draft = receipt.PodDrafts.Single(x => ReferenceEquals(x.Pod, pod));
            var previousId = pod.id; var previousText = pod.diggerLevelInput.text;
            var previousLevel = c.diggers.diggers[previousId].curLevel.ToString(CultureInfo.CurrentCulture);
            var borrowedLevel = c.diggers.diggers[level.Id].curLevel.ToString(CultureInfo.CurrentCulture);
            WriteSupport(receipt, () => {
                Exception failure = null;
                try
                {
                    RequireSupportAuthority(receipt);
                    pod.setID(level.Id);
                    if (!SupportPodDraftStillOwned(receipt, draft, level.Id, previousText, borrowedLevel))
                        throw new InvalidOperationException("Support input draft changed during native controller selection.");
                    var text = level.Level.ToString(CultureInfo.InvariantCulture);
                    RequireSupportAuthority(receipt);
                    pod.diggerLevelInput.text = text;
                    if (!SupportPodDraftStillOwned(receipt, draft, level.Id, text))
                        throw new InvalidOperationException("Support input draft changed before native submission.");
                    RequireSupportAuthority(receipt);
                    pod.setInputField();
                }
                catch (Exception error) { failure = error; throw; }
                finally
                {
                    try
                    {
                        if ((receipt.Authorized == null || receipt.Authorized()) && SupportPodDraftStillOwned(receipt, draft, level.Id, previousText, borrowedLevel, level.Level.ToString(CultureInfo.InvariantCulture)))
                        {
                            Exception restoreFailure = null;
                            try { RequireSupportAuthority(receipt); pod.setID(previousId); }
                            catch (Exception error) { restoreFailure = error; throw; }
                            finally
                            {
                                try
                                {
                                    if ((receipt.Authorized == null || receipt.Authorized()) && SupportPodDraftStillOwned(receipt, draft, previousId, previousText, borrowedLevel, previousLevel,
                                        level.Level.ToString(CultureInfo.InvariantCulture), c.diggers.diggers[previousId].curLevel.ToString(CultureInfo.CurrentCulture)))
                                        pod.diggerLevelInput.text = previousText;
                                }
                                catch { if (restoreFailure == null) throw; }
                            }
                        }
                    }
                    catch { if (failure == null) throw; }
                }
            }, levelId: level.Id, exactLevel: level.Level);
            if (c.diggers.diggers[level.Id].curLevel != level.Level || c.diggers.diggers[level.Id].active)
                throw new InvalidOperationException("Native input rounded, activated or rejected exact digger level " + level.Id + ".");
        }

        private SupportMutation ApplyExactSupport(SupportSelection selection, double budgetFraction, bool enforceBudget,
            SupportDiggerLevel[] inactiveLevels = null)
        {
            inactiveLevels = inactiveLevels == null ? new SupportDiggerLevel[0] : (SupportDiggerLevel[])inactiveLevels.Clone();
            var expected = PreflightExactSupport(selection, budgetFraction, enforceBudget, inactiveLevels);
            var c = Character;
            var receipt = new SupportMutation { Before = CaptureSupportState(), Objects = SupportObjects(), Rebirth = c.stats.rebirthNumber,
                Elapsed = c.rebirthTime.totalseconds, Total = c.totalPlaytime.totalseconds, Authorized = _selectedSetupSupportAuthorized };
            // Clear and final refresh touch every visible pod, not only the borrowed input controller.
            receipt.PodDrafts = (c.allDiggers.pods ?? new List<GoldDiggerUIController>()).Where(x => x != null && x.diggerLevelInput != null)
                .Select(x => new SupportPodDraft { Pod = x, Input = x.diggerLevelInput, Id = x.id,
                    Text = x.diggerLevelInput.text, ExpectedText = x.diggerLevelInput.text }).ToArray();
            receipt.After = receipt.Before; _supportMutation = receipt;
            Exception failure = null;
            try
            {
                // Gold deactivation also clears diggers. Clear first so no old selection is restored or re-budgeted.
                WriteSupport(receipt, () => c.allDiggers.clearAllActiveDiggers(), clearDiggers: true);
                foreach (var id in c.beards.activeBeards.Except(selection.Beards).ToArray())
                    WriteSupport(receipt, () => c.allBeards.deactivateBeard(id), beardId: id);
                foreach (var id in selection.Beards.Except(c.beards.activeBeards).ToArray())
                    WriteSupport(receipt, () => c.allBeards.activateBeard(id), beardId: id);
                WriteSupport(receipt, () => c.allBeards.refreshMenu());
                foreach (var d in selection.Diggers.Concat(inactiveLevels)) SetExactSupportDiggerLevel(receipt, d);
                // Check every exact level and fresh funding BEFORE the first native activation.
                var funded = SupportProjection.Evaluate(CaptureSupportContext(), selection, budgetFraction, enforceBudget);
                if (!funded.Feasible) throw new InvalidOperationException(funded.Reason);
                foreach (var d in selection.Diggers)
                {
                    if (c.diggers.diggers[d.Id].active) throw new InvalidOperationException("Digger became active before exact funding.");
                    WriteSupport(receipt, () => c.allDiggers.activateDigger(d.Id), diggerId: d.Id);
                }
                WriteSupport(receipt, () => c.allDiggers.refreshMenu());
                if (CaptureSupportState().Selection.StableKey != selection.StableKey
                    || inactiveLevels.Any(x => c.diggers.diggers[x.Id].active || c.diggers.diggers[x.Id].curLevel != x.Level))
                    throw new InvalidOperationException("Game rejected exact support selection.");
                var observed = CaptureSupportContext();
                RequireSupportParity(expected.GrossGold, observed.GrossGold, "applied gross GPS");
                RequireSupportParity(expected.Drain, c.allDiggers.totalGPSDrain(), "applied drain");
                foreach (var id in new[] { 0, 1, 3, 4, 5, 6 })
                    RequireSupportParity(SupportProjection.BeardBonus(observed, selection.Beards, id),
                        id == 0 ? expected.BeardStat : id == 1 ? expected.BeardLoot : id == 3 ? expected.BeardNGU : id == 4 ? expected.BeardW : id == 5 ? expected.BeardAdv : expected.BeardGold, "applied beard " + id);
                var effects = new[] { expected.DiggerLoot, expected.DiggerW, expected.DiggerStat, expected.DiggerAdv, expected.DiggerEnergyNGU, expected.DiggerMagicNGU,
                    expected.DiggerEnergyBeard, expected.DiggerMagicBeard, expected.DiggerPP, expected.DiggerDaycare, expected.DiggerBlood, expected.DiggerEXP };
                for (var id = 0; id < 12; id++)
                    RequireSupportParity(SupportProjection.DiggerBonus(observed, selection, id), effects[id], "applied digger " + id);
                if (!SupportMutationStillOwned(receipt)) throw new InvalidOperationException("Support changed during final verification.");
                receipt.Completed = true;
                return receipt;
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                try
                {
                    RestoreSupportPodDrafts(receipt);
                    if (failure == null && !SupportMutationStillOwned(receipt))
                        throw new InvalidOperationException("Support changed during input draft restoration.");
                }
                catch { receipt.Completed = false; if (failure == null) throw; }
                finally { InvalidateCustomBeardRequirements(); }
            }
        }
    }
}
