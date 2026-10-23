using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NGUInjector.Managers;

namespace NGUInjector
{
    internal partial class Main
    {
        internal enum ItemActionKind { Merge, Boost, Cube, Consume }

        internal bool IsRetainedActionEquipment(Equipment item)
        {
            if (item == null || item.id == 0) return false;
            Func<ih[], bool> contains = map => map != null && map.Any(x => ReferenceEquals(x.equipment, item));
            if (contains(_executionBaseline?.Gear) || contains(_selectedSetup?.Gear)
                || contains(_fightPreparationGear) || contains(_fightPreparationSetup?.Gear) || contains(_manualFightPreparationRequest?.Gear)
                || _executionClaims.Any(x => !x.Ended && contains(x.Setup?.Gear))
                || contains(_inlineStageSelection?.Baseline?.Gear) || contains(_focusSetupSelection?.Desired?.Gear)
                || contains(_manualAdventureBaseline?.Gear) || contains(_delegatedManualRoute?.Gear)
                || _globalPresetApplyingPolicy?.Items?.Any(x => ReferenceEquals(x, item)) == true
                || RetainedExecutionPresetPolicies().Any(x => x.Items != null && x.Items.Any(y => ReferenceEquals(y, item)))
                || _manualAdventureBaselinePolicy?.Items?.Any(x => ReferenceEquals(x, item)) == true
                || _combatPrepRun?.Capture?.Gear?.Values.Any(contains) == true
                || _combatPrepRun?.Forecast?.Gear?.Values.Any(contains) == true
                || _combatPrepPreview?.Capture?.Gear?.Values.Any(contains) == true) return true;
            var preview = _loadoutPresetPreview;
            if (preview?.Capture?.Instances != null && preview.Plan != null)
            {
                var candidates = (preview.Plan.Candidates ?? new PresetCandidate[0]).Concat(preview.Plan.Specialists ?? new PresetCandidate[0]);
                if (candidates.SelectMany(x => x.Items ?? new PresetItem[0]).Any(x => x.Id != 0
                    && preview.Capture.Instances.TryGetValue(x.SourceSlot, out var instance) && ReferenceEquals(instance, item))) return true;
            }
            return false;
        }

        private sealed class InventoryLeafAction : FiniteAction
        {
            internal ItemActionKind Kind;
            internal ih Target;
            internal ih[] Donors;
            internal string BeforeMaterial;
            internal float CubePower, CubeToughness;
            internal string ExpectedObservation;
            internal string ExpectedTargetMaterial;
            internal float ExpectedCubePower, ExpectedCubeToughness;
            internal ExecutionGrant Grant;
            private ih[] Inventory() => Character.inventory.GetConvertedInventory().ToArray();
            private ih FindTarget() => Character.inventory.GetConvertedEquips().Concat(Inventory())
                .Concat(Character.inventory.macguffins.Select((x, i) => x.GetInventoryHelper(Controller.globalMacguffinID(i))))
                .FirstOrDefault(x => x.slot == Target.slot);
            internal override bool Prerequisites()
            {
                if (Controller.midDrag || Observe() != ExpectedObservation) return false;
                if (Kind != ItemActionKind.Cube && !ReferenceEquals(FindTarget()?.equipment, Target.equipment)) return false;
                if (Kind == ItemActionKind.Consume && reference.IsRetainedActionEquipment(Target.equipment)) return false;
                return Donors.All(x => !reference.IsRetainedActionEquipment(x.equipment));
            }
            internal override string Observe() => string.Join(";", Inventory().Select(x => x.slot + ":" + LoadoutPresetPrimitiveKey(x.equipment)))
                + "|" + (Kind == ItemActionKind.Cube ? ActionNumber(Character.inventory.cubePower) + ":" + ActionNumber(Character.inventory.cubeToughness)
                    : LoadoutPresetPrimitiveKey(FindTarget()?.equipment));
            internal override void Invoke()
            {
                var inventory = Character.inventory; var first = inventory.item1; var second = inventory.item2;
                var equipped = Target != null && (Target.slot < 0 || Target.slot >= 10000);
                var wantedFirst = equipped ? Target.slot : Donors.Length == 0 ? 0 : Donors[0].slot;
                var wantedSecond = equipped ? Donors[0].slot : Target?.slot ?? 0;
                try
                {
                    if (Kind == ItemActionKind.Merge || Kind == ItemActionKind.Boost)
                    {
                        inventory.item1 = wantedFirst; inventory.item2 = wantedSecond;
                        if (!reference.ValidateExecutionGrant(Grant, Footprint)) throw new InvalidOperationException("Native item input authority changed.");
                        switch (Target.slot)
                        {
                            case -1: Controller.swapHead(); break;
                            case -2: Controller.swapChest(); break;
                            case -3: Controller.swapLegs(); break;
                            case -4: Controller.swapBoots(); break;
                            case -5: Controller.swapWeapon(); break;
                            case -6: Controller.swapWeapon2(); break;
                            default:
                                if (!equipped) Controller.swapItems();
                                else if (Target.equipment.isMacGuffin()) Controller.swapMacguffin();
                                else Controller.swapAcc();
                                break;
                        }
                    }
                    else if (Kind == ItemActionKind.Cube) Controller.infinityCubeBoost(Donors[0].slot);
                    else
                    {
                        // The item controller is resolved immediately before this leaf;
                        // no stale slot captured before preceding merges is consumed.
                        var method = typeof(ItemController).GetMethod("consumeItem", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (method == null) throw new MissingMethodException("ItemController.consumeItem");
                        method.Invoke(Controller.inventory[Target.slot % 60], null);
                    }
                }
                finally
                {
                    if (reference.ValidateExecutionGrant(Grant, Footprint) && ReferenceEquals(Character.inventory, inventory)
                        && inventory.item1 == wantedFirst && inventory.item2 == wantedSecond)
                    { inventory.item1 = first; inventory.item2 = second; }
                }
            }
            internal override MutationDisposition Classify(string before, string after, Exception error)
            {
                var remaining = Inventory();
                var consumed = Donors.Count(x => !remaining.Any(y => ReferenceEquals(y.equipment, x.equipment)));
                var target = Kind == ItemActionKind.Cube ? null : FindTarget();
                var committed = Kind == ItemActionKind.Consume ? !ReferenceEquals(target?.equipment, Target.equipment)
                    : Kind == ItemActionKind.Cube ? consumed == 1 && Character.inventory.cubePower == ExpectedCubePower
                        && Character.inventory.cubeToughness == ExpectedCubeToughness
                    : consumed == 1 && ReferenceEquals(target?.equipment, Target.equipment)
                        && LoadoutPresetPrimitiveKey(Target.equipment) == ExpectedTargetMaterial;
                if (committed) return error == null ? MutationDisposition.AppliedVerified : MutationDisposition.ModelAppliedRenderFailed;
                return before == after && error == null ? MutationDisposition.RejectedUnchanged : MutationDisposition.UnknownOutcome;
            }
        }

        internal bool RunInventoryLeaf(ItemActionKind kind, ih requested)
        {
            if (Controller.midDrag) return false;
            var inventory = Character.inventory.GetConvertedInventory().ToArray();
            var target = kind == ItemActionKind.Cube ? null : Character.inventory.GetConvertedEquips().Concat(inventory)
                .Concat(Character.inventory.macguffins.Select((x, i) => x.GetInventoryHelper(Controller.globalMacguffinID(i))))
                .FirstOrDefault(x => x.slot == requested.slot && ReferenceEquals(x.equipment, requested.equipment));
            if (kind != ItemActionKind.Cube && target == null) return false;
            if (target != null && target.slot >= Controller.curSpaces() && target.slot < 10000) return false;
            var donors = inventory.Where(x => x.id > 0 && x.equipment.removable
                && (kind == ItemActionKind.Merge ? x.id == target.id && !ReferenceEquals(x.equipment, target.equipment)
                    && (target.equipment.isMacGuffin() || x.level < 100 && target.level < 100)
                    && (!x.equipment.isBoost() || x.level + target.level <= 100 && !Character.inventory.itemList.itemMaxxed[x.id])
                : kind == ItemActionKind.Boost ? x.equipment.isBoost() && !ReferenceEquals(x.equipment, target.equipment)
                : kind == ItemActionKind.Cube ? x.equipment.isBoost() && x.slot < Controller.curSpaces() : false)).ToArray();
            if (donors.Any(x => IsRetainedActionEquipment(x.equipment)) || kind == ItemActionKind.Consume && IsRetainedActionEquipment(target.equipment)) return false;
            if (kind != ItemActionKind.Consume && donors.Length == 0) return false;
            var source = donors.FirstOrDefault();
            var expected = target == null ? null : (Equipment)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target.equipment, null);
            if (kind == ItemActionKind.Merge) expected.mergeItem(source.equipment);
            if (kind == ItemActionKind.Boost)
            {
                source = donors.FirstOrDefault(item => {
                    var copy = (Equipment)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target.equipment, null);
                    return copy.boostEquip(item.equipment, Character.allItemList.boostBonus());
                });
                if (source == null) return false;
                expected.boostEquip(source.equipment, Character.allItemList.boostBonus());
            }
            if (kind == ItemActionKind.Merge && IsRetainedActionEquipment(target.equipment)
                && Controller.checkItemTransform(expected) > 0) return false;
            donors = source == null ? new ih[0] : new[] { source };
            var action = new InventoryLeafAction { Kind = kind, Target = target, Donors = donors,
                ChangesEquipmentMaterial = kind == ItemActionKind.Merge || kind == ItemActionKind.Boost,
                ExpectedTargetMaterial = expected == null ? null : LoadoutPresetPrimitiveKey(expected),
                Footprint = ExecutionDomain.FiniteAction | ExecutionDomain.Inventory | ExecutionDomain.Gear
                    | (kind == ItemActionKind.Consume ? ExecutionDomain.NativeSettings : 0),
                BeforeMaterial = target == null ? null : LoadoutPresetPrimitiveKey(target.equipment),
                CubePower = Character.inventory.cubePower, CubeToughness = Character.inventory.cubeToughness };
            action.ExpectedEquipmentMaterial = item => target != null && ReferenceEquals(item, target.equipment) ? action.ExpectedTargetMaterial : null;
            if (kind == ItemActionKind.Cube) PredictNativeCubeLeaf(action, source.equipment);
            var page = kind == ItemActionKind.Consume ? Controller.inventory[0].id / 60 : 0;
            if (kind == ItemActionKind.Consume)
            {
                var boundary = ResolveExecutionGrant("inventory", "inventory-page", ExecutionDomain.NativeSettings | ExecutionDomain.Inventory, () => !Controller.midDrag);
                if (!ValidateExecutionGrant(boundary, boundary.Domains)) return false;
                Controller.changePage(target.slot / 60);
                if (!ValidateExecutionGrant(boundary, boundary.Domains)) return false;
            }
            action.ExpectedObservation = action.Observe();
            var id = ActionIdentity("item-" + kind + ":" + (target?.id ?? 0), "inventory", JsonFiles.ComputeRevision(System.Text.Encoding.UTF8.GetBytes(action.ExpectedObservation)));
            action.Identity = id;
            var reviseInline = CaptureInlineMaterialRevision(); var reviseFocus = CaptureFocusMaterialRevision(); var reviseCombat = CaptureCombatPrepMaterialRevision();
            try
            {
                action.Grant = ResolveExecutionGrant(id.Owner, id.Acquisition, action.Footprint, () => !Controller.midDrag);
                var result = RunFiniteAction(action, action.Grant);
                if (result.Verified)
                {
                    reviseInline?.Invoke(); reviseFocus?.Invoke(); reviseCombat?.Invoke();
                    foreach (var policy in RetainedExecutionPresetPolicies().Where(x => x.Items != null && x.Material != null))
                    {
                        // Only this leaf's exact surviving target can revise acquired material.
                        var changed = false;
                        for (var i = 0; i < policy.Items.Length; i++)
                            if (target != null && ReferenceEquals(policy.Items[i], target.equipment) && policy.Material[i] == action.BeforeMaterial)
                            { policy.Material[i] = action.ExpectedTargetMaterial; changed = true; }
                        if (changed) ApproveExecutionPolicyMaterialRevision(policy);
                    }
                }
                return result.Verified;
            }
            finally
            {
                if (kind == ItemActionKind.Consume && Controller.inventory[0].id / 60 == target.slot / 60)
                {
                    var boundary = ResolveExecutionGrant("inventory", "inventory-page-return", ExecutionDomain.NativeSettings | ExecutionDomain.Inventory, () => !Controller.midDrag);
                    if (ValidateExecutionGrant(boundary, boundary.Domains))
                    {
                        Controller.changePage(page);
                        ValidateExecutionGrant(boundary, boundary.Domains);
                    }
                }
            }
        }

        private static void PredictNativeCubeLeaf(InventoryLeafAction action, Equipment source)
        {
            var previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.state = Character.boostState;
                var power = action.CubePower; var toughness = action.CubeToughness;
                var divisor = (Character.adventure.itopod.perkLevel[26] >= 1 ? 50f : 100f) / Character.wishesController.totalBoostRatioDivider();
                var bonus = Character.allItemList.boostBonus(); var recycle = Character.totalRecycleBonus();
                for (var remaining = 0; remaining < 40; remaining++)
                {
                    if (source.type == part.atkBoost) power += source.capAttack * bonus / divisor;
                    else if (source.type == part.defBoost) toughness += source.capDefense * bonus / divisor;
                    else if (source.type == part.specBoost)
                    { power += source.spec1Cap * bonus / (divisor * 2f); toughness += source.spec1Cap * bonus / (divisor * 2f); }
                    else throw new InvalidOperationException("Native cube source is not a boost.");
                    var roll = UnityEngine.Random.value;
                    if (!Character.settings.autoboostRecycledBoosts || !(roll < recycle) || source.id == 1 || source.id == 14 || source.id == 27)
                    { action.ExpectedCubePower = power; action.ExpectedCubeToughness = toughness; return; }
                    source = Controller.itemInfo.genLoot(source.id - 1, true);
                }
                throw new InvalidOperationException("Native cube recycling exceeded its bounded boost tiers.");
            }
            finally { UnityEngine.Random.state = previous; }
        }

        internal void RequestBoostTransform(int desired)
        {
            if (Character.settings.autoTransform == desired) return;
            var grant = ResolveExecutionGrant("boost-transform", "desired:" + desired, ExecutionDomain.NativeSettings, () => !Controller.midDrag);
            if (!VerifyExecutionPrerequisites(grant, grant.Domains, () => desired >= 0 && desired <= 3)) return;
            switch (desired)
            {
                case 0: Controller.selectAutoNoneTransform(); break;
                case 1: Controller.selectAutoPowerTransform(); break;
                case 2: Controller.selectAutoToughTransform(); break;
                case 3: Controller.selectAutoSpecialTransform(); break;
            }
            ValidateExecutionGrant(grant, grant.Domains);
        }

        internal void RequestUnlockBoost(ih target)
        {
            if (target == null || target.level != 100 || target.equipment.removable || IsRetainedActionEquipment(target.equipment)) return;
            var grant = ResolveExecutionGrant("boost-unlock", "slot:" + target.slot, ExecutionDomain.Inventory | ExecutionDomain.NativeSettings,
                () => !Controller.midDrag && target.slot >= 0 && target.slot < Controller.curSpaces()
                    && ReferenceEquals(Character.inventory.inventory[target.slot], target.equipment));
            if (ValidateExecutionGrant(grant, grant.Domains)) target.equipment.removable = true;
        }

        internal void RequestItemFilter(int id)
        {
            if (Character.inventory.itemList.itemFiltered[id]) return;
            var grant = ResolveExecutionGrant("item-filter", "item:" + id, ExecutionDomain.Inventory | ExecutionDomain.NativeSettings,
                () => Character.arbitrary.lootFilter);
            if (ValidateExecutionGrant(grant, grant.Domains)) Character.inventory.itemList.itemFiltered[id] = true;
        }
    }
}
