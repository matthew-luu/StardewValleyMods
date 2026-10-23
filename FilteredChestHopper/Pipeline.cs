using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Internal;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Objects;

namespace FilteredChestHopper
{
    internal class Pipeline
    {
        public List<Chest> Hoppers = new List<Chest>();
        //location
        internal GameLocation Location;

        public Pipeline(Chest originHopper)
        {
            Location = originHopper.Location;

            originHopper.modData[Mod.ModDataFlag] = "1";

            Hoppers.Add(originHopper);

            CheckSideHoppers(new Vector2(1, 0), originHopper);
            CheckSideHoppers(new Vector2(-1, 0), originHopper);

            Hoppers.Sort(new ChestLeftToRight());
        }

        //Checks adjacent hoppers for expansion
        private void CheckSideHoppers(Vector2 direction, Chest hopper)
        {
            //check for hopper in direction
            Chest chest = Mod.GetChestAt(Location, hopper.TileLocation + direction);
            if (chest == null || !Mod.TryGetHopper(chest, out hopper))
            {
                return;
            }

            ExpandPipeline(hopper);

            CheckSideHoppers(direction, hopper);
        }

        internal void ExpandPipeline(Chest hopper)
        {
            //Expand Pipeline
            Hoppers.Add(hopper);
            hopper.modData[Mod.ModDataFlag] = "1";
        }

        //Attempt to output with this hopper as a filter

        public void AttemptTransfer(Mod mod)
        {
            // Each hopper takes input from above and sends output below.
            var inputObjects = new List<StardewValley.Object>();
            var outputObjects = new List<StardewValley.Object[]>();

            foreach (Chest hopper in Hoppers)
            {
                var inputPosition =
                    hopper.TileLocation - new Vector2(0, 1);
                var input = GetObjectAt(inputPosition);

                if (input != null)
                    inputObjects.Add(input);

                var outputPosition =
                    hopper.TileLocation + new Vector2(0, 1);
                var output = GetObjectAt(outputPosition);

                if (output != null)
                {
                    outputObjects.Add(new StardewValley.Object[]
                    {
                hopper, output
                    });
                }
            }

            foreach (var inputObject in inputObjects)
            {
                IList<Item> sourceItems;
                Chest sourceChest = inputObject as Chest;

                Inventory sourceNetworkInventory =
                    GetNetworkInventory(mod, inputObject);

                if (sourceChest != null)
                {
                    sourceChest.clearNulls();
                    sourceItems = sourceChest.GetItemsForPlayer(
                        sourceChest.owner.Value);
                }
                else if (sourceNetworkInventory != null)
                {
                    sourceItems = sourceNetworkInventory;

                }
                else
                {
                    continue;
                }

                foreach (var outputPair in outputObjects)
                {
                    Chest filterHopper = (Chest)outputPair[0];
                    StardewValley.Object destination = outputPair[1];

                    var filterItems = filterHopper.GetItemsForPlayer(
                        Game1.player.UniqueMultiplayerID);

                    filterItems.RemoveEmptySlots();

                    for (int i = sourceItems.Count - 1; i >= 0; i--)
                    {
                        Item item = sourceItems[i];

                        if (item == null)
                            continue;

                        bool match = filterItems.Count == 0;
                        int filterCount = 0;

                        for (int j = filterItems.Count - 1; j >= 0; j--)
                        {
                            Item filter = filterItems[j];

                            if (filter != null
                                && filter.QualifiedItemId == item.QualifiedItemId
                                && GetItemsFlavourID(filter) == GetItemsFlavourID(item)
                                && (!mod.Config.CompareQuality
                                    || filter.Quality == item.Quality))
                            {
                                match = true;

                                if (mod.Config.CompareQuantity)
                                {
                                    filterCount = filter.Stack == 1
                                        ? 0
                                        : filter.Stack;
                                }

                                break;
                            }
                        }

                        if (!match)
                            continue;

                        int amountToMove = item.Stack;

                        if (filterCount > 0)
                        {
                            int currentAmount = 0;

                            Inventory destinationInventory =
                                GetNetworkInventory(mod, destination);

                            if (destinationInventory != null)
                            {
                                foreach (Item existing in destinationInventory)
                                {
                                    if (existing != null && existing.canStackWith(item))
                                        currentAmount += existing.Stack;
                                }
                            }
                            else if (destination is Chest destinationChest)
                            {
                                foreach (Item existing in
                                    destinationChest.GetItemsForPlayer(
                                        Game1.player.UniqueMultiplayerID))
                                {
                                    if (existing != null && existing.canStackWith(item))
                                        currentAmount += existing.Stack;
                                }
                            }
                            else
                            {
                                continue;
                            }

                            amountToMove = Math.Min(
                                item.Stack,
                                filterCount - currentAmount);

                            if (amountToMove <= 0)
                                continue;
                        }

                        Item transferItem = CreateTransferItem(item, amountToMove);

                        if (transferItem == null)
                        {
                            continue;
                        }

                        int moved = DepositItem(
                            mod, destination, transferItem, amountToMove);

                        if (moved <= 0)
                            continue;

                        if (moved >= item.Stack)
                        {
                            sourceItems.RemoveAt(i);
                        }
                        else
                        {
                            item.Stack -= moved;
                        }
                    }
                }
            }
        }

        private Inventory GetNetworkInventory(
            Mod mod,
            StardewValley.Object obj)
        {
            if (mod.NetworkChestApi == null)
            {
                return null;
            }

            bool found = mod.NetworkChestApi.TryGetInventory(
                obj, out var inventory);

            return found ? inventory : null;
        }

        public class ChestLeftToRight : Comparer<Chest>
        {
            public override int Compare(Chest x, Chest y)
            {
                return x.TileLocation.X.CompareTo(y.TileLocation.X);
            }
        }

        public string GetItemsFlavourID(Item item)
        {
            foreach (string contextTag in item.GetContextTags())
            {
                if(contextTag.Contains("preserve_sheet_index_"))
                {
                    return contextTag.Replace("preserve_sheet_index_", ""); 
                }
            }
            return null;
        }

        //Everything past this I adapted (stole) from CJB Item Spawner, thanks CJB

        /// <summary>Get flavored variants of a base item (like Blueberry Wine for Blueberry), if any.</summary>
        private FilteredItem GetFlavoredObjectVariant(ObjectDataDefinition objectDataDefinition, StardewValley.Object newItem, StardewValley.Object processedItem, IItemDataDefinition itemType)
        {
            string id = processedItem.ItemId;

            switch(newItem.itemId.Value)
            {
                case "348":
                    return this.TryCreate(itemType.Identifier, $"348/{id}", _ => objectDataDefinition.CreateFlavoredWine(processedItem));
                case "344":
                    return this.TryCreate(itemType.Identifier, $"344/{id}", _ => objectDataDefinition.CreateFlavoredJelly(processedItem));
                case "350":
                    return this.TryCreate(itemType.Identifier, $"350/{id}", _ => objectDataDefinition.CreateFlavoredJuice(processedItem));
                case "342":
                    return this.TryCreate(itemType.Identifier, $"342/{id}", _ => objectDataDefinition.CreateFlavoredPickle(processedItem));
                case "340":
                    return this.TryCreate(itemType.Identifier, $"340/{id}", _ => objectDataDefinition.CreateFlavoredHoney(processedItem));
                case "812":
                    return this.TryCreate(itemType.Identifier, $"812/{id}", _ => objectDataDefinition.CreateFlavoredRoe(processedItem));
                case "447":
                    return this.TryCreate(itemType.Identifier, $"447/{id}", _ => objectDataDefinition.CreateFlavoredAgedRoe(processedItem));
            }
            return null;
        }


        /// <summary>Create a searchable item if valid.</summary>
        /// <param name="type">The item type.</param>
        /// <param name="key">The locally unique item key.</param>
        /// <param name="createItem">Create an item instance.</param>
        private FilteredItem TryCreate(string type, string key, Func<FilteredItem, Item> createItem)
        {
            try
            {
                FilteredItem item = new FilteredItem(type, key, createItem);
                item.Item.getDescription(); // force-load item data, so it crashes here if it's invalid

                if (item.Item.Name is null or "Error Item")
                    return null;

                return item;
            }
            catch
            {
                return null; // if some item data is invalid, just don't include it
            }
        }

        private StardewValley.Object GetObjectAt(Vector2 position)
        {
            return Location.objects.TryGetValue(position, out var obj)
                ? obj
                : null;
        }

        private int DepositItem(
            Mod mod,
            StardewValley.Object destination,
            Item sourceItem,
            int requestedAmount)
        {
            if (requestedAmount <= 0)
                return 0;

            Item transfer = sourceItem.getOne();
            transfer.Stack = requestedAmount;

            if (mod.NetworkChestApi != null &&
                mod.NetworkChestApi.TryGetInventory(destination, out _))
            {
                int originalAmount = transfer.Stack;

                Item remaining = mod.NetworkChestApi.AddItem(
                    destination,
                    transfer
                );

                return remaining == null
                    ? originalAmount
                    : originalAmount - remaining.Stack;
            }

            if (destination is Chest chest)
            {
                int originalAmount = transfer.Stack;
                Item remaining = chest.addItem(transfer);

                return remaining == null
                    ? originalAmount
                    : originalAmount - remaining.Stack;
            }

            return 0;
        }

        private Item CreateTransferItem(Item item, int amount)
        {
            // Preserve existing support for the newer flavored item types.
            Item newFlavoredItem = TryCreateNewFlavoredItem(item, amount);

            if (newFlavoredItem != null)
                return newFlavoredItem;

            // Keep the existing exact-ID behavior for all other items.
            try
            {
                Item exactItem = ItemRegistry.Create(
                    item.QualifiedItemId,
                    amount,
                    item.Quality);

                if (exactItem != null
                    && exactItem.QualifiedItemId == item.QualifiedItemId)
                {
                    exactItem.Stack = amount;
                    return exactItem;
                }
            }
            catch
            {
                // Fall back to flavored-item reconstruction below.
            }

            string flavorId = GetItemsFlavourID(item);

            if (!string.IsNullOrEmpty(flavorId)
                && item is StardewValley.Object objectItem)
            {
                var processedItem = new StardewValley.Object(flavorId, 1);

                var definition = (ObjectDataDefinition)
                    ItemRegistry.GetTypeDefinition(ItemRegistry.type_object);

                var itemType = ItemRegistry.ItemTypes.Find(
                    candidate => candidate.Identifier == item.TypeDefinitionId);

                if (itemType != null)
                {
                    var variant = GetFlavoredObjectVariant(
                        definition,
                        objectItem,
                        processedItem,
                        itemType);

                    if (variant != null)
                    {
                        Item result = variant.CreateItem();
                        result.Quality = item.Quality;
                        result.Stack = amount;
                        return result;
                    }
                }
            }

            Item fallback = ItemRegistry.Create(
                item.QualifiedItemId,
                amount,
                item.Quality);

            fallback.Stack = amount;
            return fallback;
        }

        private Item TryCreateNewFlavoredItem(Item item, int amount)
        {
            if (item is not StardewValley.Object objectItem)
                return null;

            StardewValley.Object.PreserveType? preserveType = objectItem.preserve.Value;

            if (preserveType == null)
                return null;

            if (preserveType != StardewValley.Object.PreserveType.SmokedFish
                && preserveType != StardewValley.Object.PreserveType.DriedMushroom)
            {
                return null;
            }

            string flavorId = objectItem.preservedParentSheetIndex.Value.ToString();

            if (string.IsNullOrEmpty(flavorId) || flavorId == "0")
                return null;

            try
            {
                var definition = (ObjectDataDefinition)
                    ItemRegistry.GetTypeDefinition(ItemRegistry.type_object);

                var ingredient = ItemRegistry.Create<StardewValley.Object>(
                    flavorId.StartsWith("(O)", StringComparison.Ordinal)
                        ? flavorId
                        : $"(O){flavorId}");

                Item result = definition.CreateFlavoredItem(preserveType.Value, ingredient);

                if (result == null)
                    return null;

                result.Quality = item.Quality;
                result.Stack = amount;
                return result;
            }
            catch
            {
                return null;
            }
        }

    }
}
