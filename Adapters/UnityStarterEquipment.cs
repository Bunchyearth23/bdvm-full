using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Items;
using DV.ThingTypes;
using HarmonyLib;
using UnityEngine;

namespace BDVM;

/// <summary>
/// Adds the steam operating tools to vanilla solo starting inventory. Multiplayer
/// supplies the same tools through its persistent, host-authoritative player profile.
/// </summary>
internal static class UnityStarterEquipment
{
    private const string AuthoritativeMultiplayerInventoryKey = "Multiplayer_AuthoritativeInventory";
    private static readonly string[] StarterPrefabs = { "shovel", "lighter", "Oiler" };
    private static bool installed;
    private static Action<string>? log;

    public static void Install(Harmony harmony, Action<string> log)
    {
        if (harmony == null) throw new ArgumentNullException(nameof(harmony));
        if (log == null) throw new ArgumentNullException(nameof(log));
        if (installed) return;

        var target = AccessTools.Method(typeof(StartingItemsController), "GetStartingItemsPrefabs", new[] { typeof(SaveGameData) });
        var postfix = AccessTools.Method(typeof(UnityStarterEquipment), nameof(AddStarterItems));
        if (target == null || postfix == null)
            throw new MissingMethodException("Vanilla starting item catalog hook is unavailable.");

        UnityStarterEquipment.log = log;
        harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        installed = true;
        log("[correlation=starter-equipment] Solo starting inventory grant installed; Multiplayer remains host-authoritative.");
    }

    private static void AddStarterItems(SaveGameData data, ref List<StartingItems.StartingItem> __result)
    {
        if (data == null || data.GetBool(AuthoritativeMultiplayerInventoryKey) == true || __result == null)
            return;

        var existing = new HashSet<string>(__result.Select(item => item.ItemPrefabName), StringComparer.Ordinal);
        foreach (var prefabName in StarterPrefabs)
        {
            if (!existing.Add(prefabName)) continue;

            var prefab = Resources.Load<GameObject>(prefabName);
            var spec = prefab == null ? null : prefab.GetComponent<InventoryItemSpec>();
            if (spec == null)
            {
                log?.Invoke("[correlation=starter-equipment] Starter item skipped because its prefab is unavailable: " + prefabName);
                continue;
            }

            __result.Add(new StartingItems.StartingItem
            {
                item = spec,
                // Let vanilla's inventory allocator choose an available slot.
                preferredRelativeSlot = 0,
                backpackPriority = false,
                allowDuplicates = false
            });
        }
    }
}
