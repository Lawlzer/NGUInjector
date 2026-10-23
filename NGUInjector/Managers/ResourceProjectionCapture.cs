using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUInjector.Managers
{
    internal static class ResourceProjectionCapture
    {
        internal static readonly specType[] SpecTypes = Enum.GetNames(typeof(ResourceSpec))
            .Select(x => (specType)Enum.Parse(typeof(specType), x)).ToArray();

        internal static ResourceProjection Create(Character c, bool energy, bool capacity)
        {
            var perks = c.adventureController.itopod;
            return new ResourceProjection { Energy = energy, Capacity = capacity,
                BaseCap = energy ? c.capEnergy : c.magic.capMagic, BasePower = energy ? c.energyPower : c.magic.magicPower,
                CapLimit = c.hardCap(), PowerLimit = c.hardCapPowBar(),
                Itopod = capacity ? energy ? perks.totalEnergyCapBonus() : perks.totalMagicCapBonus() : energy ? perks.totalEnergyPowerBonus() : perks.totalMagicPowerBonus(),
                Macguffin = c.inventory.macguffinBonuses[capacity ? energy ? 1 : 3 : energy ? 0 : 2],
                Quirk = capacity ? energy ? c.beastQuestPerkController.totalEnergyCapBonus() : c.beastQuestPerkController.totalMagicCapBonus()
                    : energy ? c.beastQuestPerkController.totalEnergyPowerBonus() : c.beastQuestPerkController.totalMagicPowerBonus(),
                Wish = capacity ? energy ? c.wishesController.totalEnergyCapBonus() : c.wishesController.totalMagicCapBonus()
                    : energy ? c.wishesController.totalEnergyPowerBonus() : c.wishesController.totalMagicPowerBonus(),
                Potion1 = (energy ? c.arbitrary.energyPotion1Time.totalseconds : c.arbitrary.magicPotion1Time.totalseconds) > 0 ? c.allArbitrary.potionModifier() : 1f,
                Potion2 = (energy ? c.arbitrary.energyPotion2InUse : c.arbitrary.magicPotion2InUse) ? c.allArbitrary.potionModifier() : 1f };
        }

        internal static float[] GetBonuses(IEnumerable<ih> gear)
        {
            var controller = Main.Controller;
            var items = gear.ToDictionary(x => x.slot);
            var slots = items.Keys.ToArray();
            return ResourceProjection.EquipmentBonuses(slots, (i, stat) => {
                var item = items[slots[i]];
                return item.id == 0 ? 0f : controller.equipSpecBonus(SpecTypes[stat], item.equipment);
            }, i => controller.weapon2Factor(), nativePrefix: true);
        }
    }
}
