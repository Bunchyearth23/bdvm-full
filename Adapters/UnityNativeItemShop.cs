using System;
using System.Collections.Generic;
using System.Linq;
using DV.InventorySystem;
using DV.Shops;
using DV.ThingTypes;
using UnityEngine;

namespace BDVM.Adapters;

/// <summary>Projects the vanilla item catalog and performs local-authority direct-to-inventory purchases.</summary>
internal static class UnityNativeItemShop
{
    internal sealed class Listing
    {
        public string Id = "";
        public string Name = "";
        public double Price;
        public int Stock;
    }

    public static Listing[] ReadCatalog()
    {
        var controller = GlobalShopController.Instance;
        if (controller == null || controller.globalShopList == null) return Array.Empty<Listing>();
        return controller.globalShopList.Where(shop => shop != null && shop.scanItemResourceModules != null)
            .SelectMany(shop => shop.scanItemResourceModules)
            .Where(module => module != null && module.sellingItemSpec != null)
            .Select(module => controller.GetShopItemData(module.sellingItemSpec.ItemPrefabName))
            .Where(data => data != null && data.item != null && !data.unavailableDueToGameMode && data.ItemsInStock > 0)
            .GroupBy(data => data.item.ItemPrefabName, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(data => data.item.ItemPrefabName, StringComparer.Ordinal)
            .Take(512)
            .Select(data => new Listing { Id = data.item.ItemPrefabName, Name = data.item.ItemPrefabName, Price = data.basePrice, Stock = data.ItemsInStock })
            .ToArray();
    }

    public static void Purchase(string itemId, string actorId, string localPlayerId, string correlationId)
    {
        if (!string.Equals(actorId, localPlayerId, StringComparison.Ordinal))
            throw new InvalidOperationException("Direct item delivery is currently available to the local player only.");
        if (string.IsNullOrWhiteSpace(itemId) || itemId.Length > 256 || string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 96)
            throw new ArgumentException("Invalid item purchase request.");
        var controller = GlobalShopController.Instance ?? throw new InvalidOperationException("The game item catalog is unavailable.");
        var data = controller.GetShopItemData(itemId);
        if (data == null || data.item == null || data.unavailableDueToGameMode || data.ItemsInStock <= 0)
            throw new InvalidOperationException("This game catalog item is unavailable or out of stock.");
        if (!controller.globalShopList.Any(shop => shop != null && shop.scanItemResourceModules != null &&
            shop.scanItemResourceModules.Any(module => module != null && module.sellingItemSpec == data.item)))
            throw new InvalidOperationException("This item is not sold by the active game catalog.");
        var inventory = Inventory.Instance ?? throw new InvalidOperationException("The player inventory is unavailable.");
        var prefab = Resources.Load<GameObject>(itemId);
        if (prefab == null || prefab.GetComponent<InventoryItemSpec>() == null)
            throw new InvalidOperationException("The catalog item prefab is unavailable: " + itemId);
        var price = data.basePrice;
        if (double.IsNaN(price) || double.IsInfinity(price) || price < 0 || inventory.PlayerMoney < price)
            throw new InvalidOperationException("The authoritative wallet cannot cover this item.");
        var item = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var spec = item.GetComponent<InventoryItemSpec>();
            spec.BelongsToPlayer = true;
            var restocker = item.GetComponent<ShopRestocker>();
            if (restocker != null) restocker.restockOnItemDestroyed = false;
            item.SetActive(true);
            if (!inventory.CanAddItem(item)) throw new InvalidOperationException("There is no free inventory slot for this item.");
            if (price > 0 && !inventory.RemoveMoney(price)) throw new InvalidOperationException("The authoritative wallet debit was refused.");
            try
            {
                inventory.AddItemToInventory(item, true);
                data.purchasedItems++;
            }
            catch
            {
                if (price > 0) inventory.AddMoney(price);
                throw;
            }
        }
        catch
        {
            if (item != null && item.transform.parent == null) UnityEngine.Object.Destroy(item);
            throw;
        }
    }
}
