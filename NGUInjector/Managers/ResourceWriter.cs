using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace NGUInjector.Managers
{
    internal enum ResourceTarget
    {
        EnergyNgu, MagicNgu, AdvancedTraining, Augment, AugmentUpgrade,
        TimeMachineEnergy, TimeMachineMagic, WandoosEnergy, WandoosMagic,
        BloodRitual, WishEnergy, WishMagic, WishRes3, Hack, BasicAttack, BasicDefense
    }

    internal enum MutationDisposition
    {
        RejectedUnchanged, AppliedVerified, ModelAppliedRenderFailed, KnownPartial, ForeignConflict, UnknownOutcome
    }

    internal sealed class ResourceMutationResult
    {
        internal MutationDisposition Disposition;
        internal int AffectedLanes, ModelWrites, VerifiedLanes;
        internal string Reason;
        internal Exception Error;
        internal KeyValuePair<ResourceTarget, int>[] PresentationTargets = new KeyValuePair<ResourceTarget, int>[0];
        internal bool Verified => Disposition == MutationDisposition.AppliedVerified
            || Disposition == MutationDisposition.ModelAppliedRenderFailed;
    }

    internal sealed class ResourceMutationException : InvalidOperationException
    {
        internal readonly ResourceMutationResult Result;
        internal ResourceMutationException(ResourceMutationResult result) : base(result.Reason, result.Error) { Result = result; }
    }

    // Application evidence only. Feature goals, phase history and deadlines live with their controllers.
    internal sealed class ResourceApplication
    {
        // Evidence belongs to the native model, not a feature receipt. A new application
        // or successor cannot acknowledge an uncertain write by forgetting the receipt.
        private sealed class LaneEvidence
        {
            internal readonly ResourceMutationResult[] Unresolved = new ResourceMutationResult[3];
            internal object Stats, RebirthClock, TotalClock;
            internal long Rebirth;
            internal double Elapsed, Total;
            internal bool EpochKnown;
        }
        private static readonly ConditionalWeakTable<object, LaneEvidence> Evidence = new ConditionalWeakTable<object, LaneEvidence>();
        private Character character;
        private LaneEvidence epoch;
        private bool presentationPending;
        private KeyValuePair<ResourceTarget, int>[] presentationTargets;
        internal ResourceMutationResult Last { get; private set; }
        internal bool PresentationPending => presentationPending;
        internal int BlockedLanes
        {
            get
            {
                return ResourceWriter.Execute(() => Main.Character == null ? 0
                    : GetEvidence(Main.Character).Unresolved.Select((result, lane) => result == null ? 0 : 1 << lane)
                        .Aggregate(0, (mask, lane) => mask | lane));
            }
        }

        private static LaneEvidence GetEvidence(Character model, bool recording = false)
        {
            var evidence = Evidence.GetValue(model, _ => new LaneEvidence());
            if (model.stats == null || model.rebirthTime == null || model.totalPlaytime == null)
            {
                if (recording) evidence.EpochKnown = false;
                return evidence;
            }
            var rebirth = model.stats.rebirthNumber;
            var elapsed = model.rebirthTime.totalseconds; var total = model.totalPlaytime.totalseconds;
            if (rebirth < 0 || !(elapsed >= 0 && elapsed <= double.MaxValue)
                || !(total >= elapsed && total <= double.MaxValue))
            {
                if (recording) evidence.EpochKnown = false;
                return evidence;
            }
            // A successor/row edit is not an epoch. Only valid native save/rebirth
            // markers can retire old uncertainty; unavailable markers keep it quarantined.
            if (evidence.EpochKnown && (!ReferenceEquals(evidence.Stats, model.stats)
                || !ReferenceEquals(evidence.RebirthClock, model.rebirthTime) || !ReferenceEquals(evidence.TotalClock, model.totalPlaytime)
                || evidence.Rebirth != rebirth || elapsed + .1d < evidence.Elapsed || total + .1d < evidence.Total
                || Math.Abs(total - elapsed - (evidence.Total - evidence.Elapsed)) > 1d))
            {
                Evidence.Remove(model);
                evidence = Evidence.GetValue(model, _ => new LaneEvidence());
            }
            evidence.Stats = model.stats; evidence.RebirthClock = model.rebirthTime; evidence.TotalClock = model.totalPlaytime;
            evidence.Rebirth = rebirth; evidence.Elapsed = elapsed; evidence.Total = total; evidence.EpochKnown = true;
            return evidence;
        }

        internal ResourceMutationResult Apply(Dictionary<ResourceTarget, long[]> desired, bool energy, bool magic,
            bool res3 = false, Func<bool> authorized = null, Action render = null)
        {
            return ResourceWriter.Execute(() => ApplyCore(desired, energy, magic, res3, authorized, render));
        }

        internal void RecordUnresolved(Character model, ResourceMutationResult result)
        {
            if (model == null || result == null || result.AffectedLanes == 0
                || result.Disposition != MutationDisposition.ForeignConflict && result.Disposition != MutationDisposition.UnknownOutcome)
                throw new ArgumentException("Unresolved resource evidence requires an observed affected lane.");
            ResourceWriter.Execute(() => {
                var evidence = GetEvidence(model, true);
                for (var lane = 0; lane < 3; lane++)
                    if ((result.AffectedLanes & (1 << lane)) != 0) evidence.Unresolved[lane] = result;
                Last = result;
            });
        }

        internal bool Reauthorize(Character model, int lanes, Func<bool> reviewed)
        {
            return ResourceWriter.Execute(() => {
                if (!ReferenceEquals(model, Main.Character) || lanes < 0 || (lanes & ~7) != 0 || reviewed == null || !reviewed()) return false;
                var evidence = GetEvidence(model);
                if (!reviewed()) return false;
                for (var lane = 0; lane < 3; lane++) if ((lanes & (1 << lane)) != 0) evidence.Unresolved[lane] = null;
                return true;
            });
        }

        private ResourceMutationResult ApplyCore(Dictionary<ResourceTarget, long[]> desired, bool energy, bool magic,
            bool res3, Func<bool> authorized, Action render)
        {
            if (!ReferenceEquals(character, Main.Character)) { character = Main.Character; Last = null; presentationPending = false; presentationTargets = null; }
            if (character == null || desired == null)
                return Last = ResourceWriter.Reconcile(desired, energy, magic, res3, render, PresentationPending, authorized, presentationTargets);
            var evidence = GetEvidence(character);
            if (!ReferenceEquals(epoch, evidence))
            {
                epoch = evidence; Last = null; presentationPending = false; presentationTargets = null;
            }
            var requested = (energy ? 1 : 0) | (magic ? 2 : 0) | (res3 ? 4 : 0);
            var blocked = 0;
            for (var lane = 0; lane < 3; lane++)
                if (evidence.Unresolved[lane] != null) blocked |= 1 << lane;
            var overlap = blocked & requested;
            // A caller-supplied renderer can touch every requested lane. Do not run it
            // across a yielded lane; the native per-target renderer has an exact footprint.
            var healthy = desired.Where(x => (blocked & (1 << ResourceWriter.Lane(x.Key))) == 0)
                .ToDictionary(x => x.Key, x => x.Value);
            Last = ResourceWriter.Reconcile(healthy, energy && (blocked & 1) == 0,
                magic && (blocked & 2) == 0, res3 && (blocked & 4) == 0,
                overlap == 0 ? render : null, PresentationPending, authorized,
                presentationTargets);
            if ((Last.Disposition == MutationDisposition.ForeignConflict || Last.Disposition == MutationDisposition.UnknownOutcome) && Last.AffectedLanes != 0)
                RecordUnresolved(character, Last);
            if (Last.Disposition != MutationDisposition.RejectedUnchanged)
            {
                presentationPending = Last.Disposition == MutationDisposition.ModelAppliedRenderFailed;
                presentationTargets = presentationPending ? Last.PresentationTargets : null;
            }
            if (Last.Verified || Last.Disposition == MutationDisposition.ForeignConflict)
                Last.VerifiedLanes = requested & ~blocked & ~Last.AffectedLanes;
            if (overlap != 0)
            {
                var prior = Enumerable.Range(0, 3).Where(lane => (overlap & (1 << lane)) != 0)
                    .Select(lane => evidence.Unresolved[lane]).ToArray();
                Last = new ResourceMutationResult {
                    Disposition = prior.Any(x => x.Disposition == MutationDisposition.UnknownOutcome)
                        || Last.Disposition == MutationDisposition.UnknownOutcome ? MutationDisposition.UnknownOutcome : MutationDisposition.ForeignConflict,
                    AffectedLanes = Last.AffectedLanes | overlap, ModelWrites = Last.ModelWrites,
                    VerifiedLanes = Last.VerifiedLanes, Error = Last.Error ?? prior[0].Error,
                    PresentationTargets = Last.PresentationTargets,
                    Reason = "Unresolved resource authority in lanes " + overlap + "; independent lanes were evaluated separately. " + prior[0].Reason
                };
            }
            return Last;
        }
    }

    // Concrete resource-domain intent. Missing targets are uncontrolled; present zero
    // arrays are controlled-empty. Producers resolve mathematics, never native writes.
    internal sealed class ResourceSetupIntent
    {
        internal string Owner;
        internal Dictionary<ResourceTarget, long[]> Desired;
        internal bool Energy, Magic, Res3;
        internal Func<bool> Authorized;
        internal Action Render;
    }

    internal sealed class ResourceSetupExecutor
    {
        internal readonly ResourceApplication Application = new ResourceApplication();
        internal ResourceSetupIntent Current { get; private set; }

        internal ResourceMutationResult Reconcile(ResourceSetupIntent intent)
        {
            if (intent == null || string.IsNullOrEmpty(intent.Owner) || intent.Authorized == null)
                throw new ArgumentException("Resource setup requires current owner and authority.");
            // Retain current desired intent on partial/rejected application, not historical
            // return state. Detach producer buffers before any authority/native callback.
            Current = new ResourceSetupIntent { Owner = intent.Owner, Energy = intent.Energy,
                Magic = intent.Magic, Res3 = intent.Res3, Authorized = intent.Authorized, Render = intent.Render,
                Desired = intent.Desired?.ToDictionary(x => x.Key, x => x.Value?.ToArray()) };
            return Application.Apply(Current.Desired, Current.Energy, Current.Magic, Current.Res3, Current.Authorized, Current.Render);
        }
    }

    // All injector resource mutations share this reentrant lock. Callers retain lane/owner policy
    // and must run on the game thread; serialization does not make Unity rendering thread-safe.
    internal static class ResourceWriter
    {
        private static readonly object Gate = new object();

        internal static string AllocatedResourceKey()
        {
            lock (Gate)
            {
                var c = Main.Character;
                var allocations = CaptureAllocations(c);
                if (CaptureIdle(c).Any(x => x < 0) || allocations.Values.Where(x => x != null).Any(x => x.Any(v => v < 0)))
                    throw new InvalidOperationException("Invalid resource accounting during a coupled setup transition.");
                return string.Join("|", allocations.Select(x => x.Key + ":"
                    + (x.Value == null ? "null" : string.Join(",", x.Value))));
            }
        }

        // Missing targets are uncontrolled. A present all-zero target is controlled empty.
        // Arrays are detached observations; no native collection becomes write authority.
        internal static Dictionary<ResourceTarget, long[]> EmptyDesiredMap(bool energy, bool magic, bool res3 = false)
        {
            lock (Gate)
                return CaptureAllocations(Main.Character).Where(x => x.Value != null
                    && x.Key != ResourceTarget.BasicAttack && x.Key != ResourceTarget.BasicDefense
                    && (Lane(x.Key) == 0 ? energy : Lane(x.Key) == 1 ? magic : res3))
                    .ToDictionary(x => x.Key, x => new long[x.Value.Length]);
        }

        internal static ResourceMutationResult Reconcile(Dictionary<ResourceTarget, long[]> desired,
            bool energy, bool magic, bool res3, Action render = null, bool retryPresentation = false, Func<bool> authorized = null,
            KeyValuePair<ResourceTarget, int>[] presentationTargets = null)
        {
            lock (Gate)
            {
                var result = new ResourceMutationResult();
                var c = Main.Character;
                object[] context;
                Dictionary<ResourceTarget, long[]> expected;
                long?[] idle;
                try
                {
                    if (desired == null) throw new ArgumentNullException(nameof(desired));
                    desired = desired.ToDictionary(x => x.Key, x => x.Value?.ToArray());
                    expected = CaptureAllocations(c); idle = CaptureIdle(c); context = CaptureResourceContext(c);
                    foreach (var pair in desired)
                    {
                        var lane = Lane(pair.Key);
                        if (!(lane == 0 ? energy : lane == 1 ? magic : res3))
                            throw new InvalidOperationException("Desired resource map claims an unmanaged lane.");
                        if (pair.Value == null || !expected.ContainsKey(pair.Key) || expected[pair.Key] == null
                            || expected[pair.Key].Length != pair.Value.Length || pair.Value.Any(x => x < 0))
                            throw new InvalidOperationException("Desired resource target is missing, negative or changed shape: " + pair.Key);
                    }
                    // Validate every lane's final accounting before changing any destination.
                    for (var lane = 0; lane < 3; lane++)
                    {
                        var changes = desired.Where(x => Lane(x.Key) == lane).ToArray();
                        if (changes.Length == 0) continue;
                        if (!idle[lane].HasValue || idle[lane] < 0 || expected.Any(x => Lane(x.Key) == lane && x.Value != null && x.Value.Any(v => v < 0)))
                            throw new InvalidOperationException("Negative or absent native resource accounting.");
                        decimal available = idle[lane].Value;
                        decimal released = available;
                        foreach (var pair in changes)
                            for (var i = 0; i < pair.Value.Length; i++)
                            {
                                if (expected[pair.Key][i] > pair.Value[i]) released += (decimal)expected[pair.Key][i] - pair.Value[i];
                                available += (decimal)expected[pair.Key][i] - pair.Value[i];
                            }
                        if (available < 0 || available > long.MaxValue || released > long.MaxValue)
                            throw new InvalidOperationException("Insufficient resources or idle overflow for exact desired map.");
                        idle[lane] = (long)available;
                    }
                    if (authorized != null && !authorized()) throw new InvalidOperationException("Resource authority changed before application.");
                }
                catch (Exception error)
                {
                    result.Disposition = MutationDisposition.RejectedUnchanged; result.Reason = error.Message; result.Error = error;
                    return result;
                }
                try
                {
                    var changed = new List<KeyValuePair<ResourceTarget, int>>();
                    // No native callbacks run between release and funding. Never recall a whole
                    // lane or replay an additive transfer after a presentation exception.
                    foreach (var release in new[] { true, false })
                        foreach (var pair in desired)
                            for (var i = 0; i < pair.Value.Length; i++)
                            {
                                var current = expected[pair.Key][i]; var wanted = pair.Value[i];
                                if (wanted == current || (wanted < current) != release) continue;
                                var lane = Lane(pair.Key);
                                var nextIdle = checked(GetIdle(c, lane) + (current - wanted));
                                SetAmount(c, pair.Key, i, wanted); SetIdle(c, lane, nextIdle);
                                expected[pair.Key][i] = wanted;
                                result.ModelWrites++;
                                changed.Add(new KeyValuePair<ResourceTarget, int>(pair.Key, i));
                            }
                    if (result.ModelWrites > 0 || retryPresentation)
                    {
                        if (render != null)
                        {
                            try { render(); }
                            catch (Exception error) { result.Error = error; result.Reason = error.Message; }
                        }
                        else
                        {
                            var pending = new List<KeyValuePair<ResourceTarget, int>>();
                            foreach (var target in changed.Concat(presentationTargets ?? new KeyValuePair<ResourceTarget, int>[0]).Distinct())
                            {
                                if (!desired.ContainsKey(target.Key)) continue;
                                if (!ReferenceEquals(Main.Character, c) || authorized != null && !authorized()) break;
                                var callbackContext = CaptureResourceContext(c);
                                var callbackMap = CaptureAllocations(c);
                                if (callbackContext.Length != context.Length || callbackContext.Where((item, i) => !ReferenceEquals(item, context[i])).Any()
                                    || expected.Any(pair => pair.Value == null ? callbackMap[pair.Key] != null
                                        : callbackMap[pair.Key] == null || !pair.Value.SequenceEqual(callbackMap[pair.Key]))
                                    || !idle.SequenceEqual(CaptureIdle(c))) break;
                                try { Refresh(c, target.Key, target.Value); }
                                catch (Exception error)
                                {
                                    pending.Add(target); result.Error = result.Error ?? error; result.Reason = result.Error.Message;
                                }
                            }
                            result.PresentationTargets = pending.ToArray();
                        }
                    }
                    var observedContext = CaptureResourceContext(c);
                    if (!ReferenceEquals(Main.Character, c) || context.Length != observedContext.Length
                        || context.Where((x, i) => !ReferenceEquals(x, observedContext[i])).Any() || authorized != null && !authorized())
                    {
                        result.Disposition = MutationDisposition.UnknownOutcome;
                        result.AffectedLanes = (energy ? 1 : 0) | (magic ? 2 : 0) | (res3 ? 4 : 0);
                        result.Reason = "Resource epoch, entry or permissions changed during application. " + result.Reason;
                        return result;
                    }
                    var observed = CaptureAllocations(c); var observedIdle = CaptureIdle(c);
                    foreach (var pair in expected)
                        if (pair.Value == null ? observed[pair.Key] != null : observed[pair.Key] == null || !pair.Value.SequenceEqual(observed[pair.Key]))
                            result.AffectedLanes |= 1 << Lane(pair.Key);
                    for (var lane = 0; lane < 3; lane++)
                        if (idle[lane] != observedIdle[lane]) result.AffectedLanes |= 1 << lane;
                    result.Disposition = result.AffectedLanes != 0 ? MutationDisposition.ForeignConflict
                        : result.Error != null ? MutationDisposition.ModelAppliedRenderFailed : MutationDisposition.AppliedVerified;
                    if (result.Disposition == MutationDisposition.ForeignConflict)
                        result.Reason = "Foreign resource mutation during presentation; affected lanes were preserved. " + result.Reason;
                }
                catch (Exception error)
                {
                    result.Disposition = MutationDisposition.UnknownOutcome; result.Error = error; result.Reason = error.Message;
                    result.AffectedLanes = (energy ? 1 : 0) | (magic ? 2 : 0) | (res3 ? 4 : 0);
                }
                return result;
            }
        }

        internal static long Reclaimable(bool energy)
        {
            lock (Gate)
            {
                var c = Main.Character; var total = (decimal)GetIdle(c, energy ? 0 : 1);
                if (total < 0) throw new InvalidOperationException("Negative idle resource accounting.");
                foreach (var pair in CaptureAllocations(c))
                {
                    if (pair.Value == null || Lane(pair.Key) != (energy ? 0 : 1)) continue;
                    foreach (var amount in pair.Value)
                    {
                        if (amount < 0) throw new InvalidOperationException("Negative native resource allocation.");
                        if (pair.Key != ResourceTarget.BasicAttack && pair.Key != ResourceTarget.BasicDefense) total += amount;
                    }
                }
                return checked((long)total);
            }
        }

        internal static void Execute(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (Gate) action();
        }

        internal static T Execute<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (Gate) return action();
        }

        internal static void Transfer(ResourceTarget target, int index, long amount, bool refresh = true)
        {
            lock (Gate)
            {
                if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
                var c = Main.Character;
                var lane = Lane(target);
                var current = GetAmount(c, target, index);
                var idle = GetIdle(c, lane);
                if (current < 0 || idle < 0) throw new InvalidOperationException("Negative native resource accounting.");
                if (amount > idle) throw new InvalidOperationException("Insufficient idle resources for exact transfer.");
                var allocated = checked(current + amount);
                var remaining = checked(idle - amount);
                if (amount == 0) return;
                SetAmount(c, target, index, allocated);
                SetIdle(c, lane, remaining);
                // Rendering may throw. Never roll back only one side or hide that failure.
                if (refresh) Refresh(c, target, index);
            }
        }

        internal static long RecallTarget(ResourceTarget target, int index)
        {
            lock (Gate)
            {
                var c = Main.Character;
                var lane = Lane(target);
                var amount = GetAmount(c, target, index);
                var idle = GetIdle(c, lane);
                if (amount < 0 || idle < 0) throw new InvalidOperationException("Negative native resource accounting.");
                var returned = checked(idle + amount);
                if (amount == 0) return 0;
                SetAmount(c, target, index, 0);
                SetIdle(c, lane, returned);
                Refresh(c, target, index);
                return amount;
            }
        }

        internal static bool RecallOwnedTarget(ResourceTarget target, int index, long expected)
        {
            lock (Gate)
            {
                if (expected < 0) throw new ArgumentOutOfRangeException(nameof(expected));
                Lane(target);
                if (GetAmount(Main.Character, target, index) != expected) return false;
                RecallTarget(target, index);
                return true;
            }
        }

        internal static void Recall(bool energy)
        {
            RecallNative(energy ? 0 : 1, false);
        }

        internal static void RecallRes3()
        {
            RecallNative(2, false);
        }

        internal static void RecallAllEnergy()
        {
            RecallNative(0, true);
        }

        private static void RecallNative(int lane, bool includeBasic)
        {
            lock (Gate)
            {
                var c = Main.Character;
                var expected = CaptureAllocations(c);
                var expectedIdle = CaptureIdle(c);
                if (!expectedIdle[lane].HasValue || expectedIdle.Any(x => x.HasValue && x.Value < 0))
                    throw new InvalidOperationException("Missing or negative native resource pool before recall.");
                foreach (var target in expected)
                {
                    var amounts = target.Value;
                    if (amounts == null) continue;
                    if (amounts.Any(x => x < 0)) throw new InvalidOperationException("Negative native resource allocation before recall.");
                    if (Lane(target.Key) != lane || !includeBasic
                        && (target.Key == ResourceTarget.BasicAttack || target.Key == ResourceTarget.BasicDefense)) continue;
                    foreach (var amount in amounts) expectedIdle[lane] = checked(expectedIdle[lane].Value + amount);
                    // This clears only a detached expected snapshot, never native allocations.
                    Array.Clear(amounts, 0, amounts.Length);
                }

                if (lane == 2) c.removeAllRes3();
                else if (lane == 1) c.removeMostMagic();
                else if (includeBasic) c.removeAllEnergy();
                else c.removeMostEnergy();

                // Conservation alone would accept a rejected or partial recall. Require the exact
                // post-map too, including retained basic training and every unowned destination.
                var name = lane == 0 ? "energy" : lane == 1 ? "magic" : "R3";
                if (!ReferenceEquals(Main.Character, c))
                    throw new InvalidOperationException("Native " + name + " recall changed character context.");
                var observed = CaptureAllocations(c);
                foreach (var target in expected)
                {
                    var actual = observed[target.Key];
                    if (target.Value == null ? actual != null : actual == null || !target.Value.SequenceEqual(actual))
                        throw new InvalidOperationException("Native " + name + " recall was rejected, incomplete, or changed an unowned destination: " + target.Key + ".");
                }
                if (!expectedIdle.SequenceEqual(CaptureIdle(c)))
                    throw new InvalidOperationException("Native " + name + " recall did not conserve exact idle resources or changed an unowned pool.");
            }
        }

        private static long?[] CaptureIdle(Character c)
        {
            return new long?[] { c.idleEnergy, c.magic?.idleMagic, c.res3?.idleRes3 };
        }

        internal static object[] CaptureResourceContext(Character c)
        {
            return new object[] { c, c.NGU, c.NGU?.skills, c.NGU?.magicSkills, c.magic, c.res3,
                c.advancedTraining, c.advancedTraining?.energy, c.augments, c.augments?.augs, c.machine,
                c.wandoos98, c.bloodMagic, c.bloodMagic?.ritual, c.wishes, c.wishes?.wishes,
                c.hacks, c.hacks?.hacks, c.training, c.training?.attackEnergy, c.training?.defenseEnergy }
                .Concat(c.NGU?.skills?.Cast<object>() ?? Enumerable.Empty<object>())
                .Concat(c.NGU?.magicSkills?.Cast<object>() ?? Enumerable.Empty<object>())
                .Concat(c.augments?.augs?.Cast<object>() ?? Enumerable.Empty<object>())
                .Concat(c.bloodMagic?.ritual?.Cast<object>() ?? Enumerable.Empty<object>())
                .Concat(c.wishes?.wishes?.Cast<object>() ?? Enumerable.Empty<object>())
                .Concat(c.hacks?.hacks?.Cast<object>() ?? Enumerable.Empty<object>()).ToArray();
        }

        internal static Dictionary<ResourceTarget, long[]> CaptureAllocations(Character c)
        {
            // Native removeMost includes all wishes, even when the activity is locked. Capture
            // backing collections, not just visible/unlocked bars. Missing models stay absent.
            return new Dictionary<ResourceTarget, long[]>
            {
                { ResourceTarget.EnergyNgu, c.NGU?.skills?.Select(x => x.energy).ToArray() },
                { ResourceTarget.MagicNgu, c.NGU?.magicSkills?.Select(x => x.magic).ToArray() },
                { ResourceTarget.AdvancedTraining, c.advancedTraining?.energy?.ToArray() },
                { ResourceTarget.Augment, c.augments?.augs?.Select(x => x.augEnergy).ToArray() },
                { ResourceTarget.AugmentUpgrade, c.augments?.augs?.Select(x => x.upgradeEnergy).ToArray() },
                { ResourceTarget.TimeMachineEnergy, c.machine == null ? null : new[] { c.machine.speedEnergy } },
                { ResourceTarget.TimeMachineMagic, c.machine == null ? null : new[] { c.machine.goldMultiMagic } },
                { ResourceTarget.WandoosEnergy, c.wandoos98 == null ? null : new[] { c.wandoos98.wandoosEnergy } },
                { ResourceTarget.WandoosMagic, c.wandoos98 == null ? null : new[] { c.wandoos98.wandoosMagic } },
                { ResourceTarget.BloodRitual, c.bloodMagic?.ritual?.Select(x => x.magic).ToArray() },
                { ResourceTarget.WishEnergy, c.wishes?.wishes?.Select(x => x.energy).ToArray() },
                { ResourceTarget.WishMagic, c.wishes?.wishes?.Select(x => x.magic).ToArray() },
                { ResourceTarget.WishRes3, c.wishes?.wishes?.Select(x => x.res3).ToArray() },
                { ResourceTarget.Hack, c.hacks?.hacks?.Select(x => x.res3).ToArray() },
                { ResourceTarget.BasicAttack, c.training?.attackEnergy?.ToArray() },
                { ResourceTarget.BasicDefense, c.training?.defenseEnergy?.ToArray() }
            };
        }

        // Intentionally unconditional, matching the native gear-swap boundary. The caller must
        // authorize all affected lanes before using this operation (basic training is retained).
        internal static void RecallForGearSwap()
        {
            Execute(() => { Recall(true); Recall(false); RecallRes3(); });
        }

        // These native operations are serialized, not resource-policy decisions. They do not
        // recall anything implicitly; callers must authorize the fruit/ritual lane beforehand.
        internal static void ActivateFruit(int index)
        {
            lock (Gate)
            {
                var c = Main.Character;
                if (index < 0 || index >= c.yggdrasil.fruits.Count) throw new ArgumentOutOfRangeException(nameof(index));
                var controller = c.yggdrasilController;
                controller.changePage(index / 9);
                controller.fruits[index % 9].activate(index);
            }
        }

        internal static void CapBloodRituals()
        {
            lock (Gate)
            {
                var c = Main.Character;
                for (var i = c.bloodMagic.ritual.Count - 1; i >= 0; i--)
                    c.bloodMagicController.bloodMagics[i].cap();
            }
        }

        internal static int Lane(ResourceTarget target)
        {
            switch (target)
            {
                case ResourceTarget.EnergyNgu: case ResourceTarget.AdvancedTraining:
                case ResourceTarget.Augment: case ResourceTarget.AugmentUpgrade:
                case ResourceTarget.TimeMachineEnergy: case ResourceTarget.WandoosEnergy:
                case ResourceTarget.WishEnergy: case ResourceTarget.BasicAttack: case ResourceTarget.BasicDefense:
                    return 0;
                case ResourceTarget.MagicNgu: case ResourceTarget.TimeMachineMagic:
                case ResourceTarget.WandoosMagic: case ResourceTarget.BloodRitual: case ResourceTarget.WishMagic:
                    return 1;
                case ResourceTarget.WishRes3: case ResourceTarget.Hack:
                    return 2;
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        private static long GetIdle(Character c, int lane)
        {
            return lane == 0 ? c.idleEnergy : lane == 1 ? c.magic.idleMagic : c.res3.idleRes3;
        }

        private static void SetIdle(Character c, int lane, long amount)
        {
            if (lane == 0) c.idleEnergy = amount;
            else if (lane == 1) c.magic.idleMagic = amount;
            else c.res3.idleRes3 = amount;
        }

        private static long GetAmount(Character c, ResourceTarget target, int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            switch (target)
            {
                case ResourceTarget.EnergyNgu: return c.NGU.skills[index].energy;
                case ResourceTarget.MagicNgu: return c.NGU.magicSkills[index].magic;
                case ResourceTarget.AdvancedTraining: return c.advancedTraining.energy[index];
                case ResourceTarget.Augment: return c.augments.augs[index].augEnergy;
                case ResourceTarget.AugmentUpgrade: return c.augments.augs[index].upgradeEnergy;
                case ResourceTarget.BloodRitual: return c.bloodMagic.ritual[index].magic;
                case ResourceTarget.WishEnergy: return c.wishes.wishes[index].energy;
                case ResourceTarget.WishMagic: return c.wishes.wishes[index].magic;
                case ResourceTarget.WishRes3: return c.wishes.wishes[index].res3;
                case ResourceTarget.Hack: return c.hacks.hacks[index].res3;
                case ResourceTarget.BasicAttack: return c.training.attackEnergy[index];
                case ResourceTarget.BasicDefense: return c.training.defenseEnergy[index];
            }
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            switch (target)
            {
                case ResourceTarget.TimeMachineEnergy: return c.machine.speedEnergy;
                case ResourceTarget.TimeMachineMagic: return c.machine.goldMultiMagic;
                case ResourceTarget.WandoosEnergy: return c.wandoos98.wandoosEnergy;
                case ResourceTarget.WandoosMagic: return c.wandoos98.wandoosMagic;
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        private static void SetAmount(Character c, ResourceTarget target, int index, long amount)
        {
            switch (target)
            {
                case ResourceTarget.EnergyNgu: c.NGU.skills[index].energy = amount; break;
                case ResourceTarget.MagicNgu: c.NGU.magicSkills[index].magic = amount; break;
                case ResourceTarget.AdvancedTraining: c.advancedTraining.energy[index] = amount; break;
                case ResourceTarget.Augment: c.augments.augs[index].augEnergy = amount; break;
                case ResourceTarget.AugmentUpgrade: c.augments.augs[index].upgradeEnergy = amount; break;
                case ResourceTarget.TimeMachineEnergy: c.machine.speedEnergy = amount; break;
                case ResourceTarget.TimeMachineMagic: c.machine.goldMultiMagic = amount; break;
                case ResourceTarget.WandoosEnergy: c.wandoos98.wandoosEnergy = amount; break;
                case ResourceTarget.WandoosMagic: c.wandoos98.wandoosMagic = amount; break;
                case ResourceTarget.BloodRitual: c.bloodMagic.ritual[index].magic = amount; break;
                case ResourceTarget.WishEnergy: c.wishes.wishes[index].energy = amount; break;
                case ResourceTarget.WishMagic: c.wishes.wishes[index].magic = amount; break;
                case ResourceTarget.WishRes3: c.wishes.wishes[index].res3 = amount; break;
                case ResourceTarget.Hack: c.hacks.hacks[index].res3 = amount; break;
                case ResourceTarget.BasicAttack: c.training.attackEnergy[index] = amount; break;
                case ResourceTarget.BasicDefense: c.training.defenseEnergy[index] = amount; break;
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        private static void Refresh(Character c, ResourceTarget target, int index)
        {
            switch (target)
            {
                case ResourceTarget.EnergyNgu: c.NGUController.NGU[index].updateText(); break;
                case ResourceTarget.MagicNgu: c.NGUController.NGUMagic[index].updateText(); break;
                case ResourceTarget.AdvancedTraining:
                    switch (index)
                    {
                        case 0: c.advancedTrainingController.defense.updateText(); break;
                        case 1: c.advancedTrainingController.attack.updateText(); break;
                        case 2: c.advancedTrainingController.block.updateText(); break;
                        case 3: c.advancedTrainingController.wandoosEnergy.updateText(); break;
                        case 4: c.advancedTrainingController.wandoosMagic.updateText(); break;
                        default: throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    break;
                case ResourceTarget.Augment: c.augmentsController.augments[index].updateAugTexts(); break;
                case ResourceTarget.AugmentUpgrade: c.augmentsController.augments[index].updateUpgradeTexts(); break;
                case ResourceTarget.TimeMachineEnergy: c.timeMachineController.updateSpeedText(); break;
                case ResourceTarget.TimeMachineMagic: c.timeMachineController.updateGoldMultiText(); break;
                case ResourceTarget.WandoosEnergy: case ResourceTarget.WandoosMagic: c.wandoos98Controller.updateText(); break;
                case ResourceTarget.BloodRitual: c.bloodMagicController.bloodMagics[index].updateBloodMagicText(); break;
                case ResourceTarget.WishEnergy: case ResourceTarget.WishMagic: case ResourceTarget.WishRes3:
                    c.wishesController.updateMenu(); break;
                case ResourceTarget.Hack: c.hacksController.refreshMenu(); break;
                case ResourceTarget.BasicAttack: c.allOffenseController.trains[index].updateText(); break;
                case ResourceTarget.BasicDefense: c.allDefenseController.trains[index].updateText(); break;
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }
    }
}
