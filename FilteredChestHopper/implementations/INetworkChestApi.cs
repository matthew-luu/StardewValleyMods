using StardewValley;
using StardewValley.Inventories;

namespace NetworkChest
{
    public interface INetworkChestApi
    {
        bool TryGetInventory(
            StardewValley.Object obj,
            out Inventory inventory
        );

        Item AddItem(
            StardewValley.Object obj,
            Item item
        );
    }
}
