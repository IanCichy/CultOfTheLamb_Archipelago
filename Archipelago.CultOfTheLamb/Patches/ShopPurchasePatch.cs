using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Catches purchases from the slot shops shopKeeperManager drives, covering tarot, decorations
/// and plain stalls alike. Not every shop: the Mystic Shop, spider shop, seed shop and others are
/// independent classes that take payment themselves.
///
/// Activate() is the purchase-completed step, patched rather than subscribing to its own
/// OnItemBought, which is per-instance and would mean hooking every slot object as it spawns. The
/// BuyEntry handed back says what was bought, so subscribers can filter to the kinds they want.
/// </summary>
[HarmonyPatch(typeof(Interaction_BuyItem))]
internal static class ShopPurchasePatch
{
    // Fires with the purchased entry. Decorations and plain items come through too
    internal static event Action<BuyEntry> OnItemPurchased;

    [HarmonyPatch("Activate")]
    [HarmonyPostfix]
    private static void Activate_Postfix(Interaction_BuyItem __instance)
    {
        // customItemForSale entries are built at runtime rather than configured on the prefab,
        // and Activate() bails out early for them, so there is nothing stable to key a location
        // off.
        if (__instance == null || __instance.customItemForSale)
        {
            return;
        }

        var entry = __instance.itemForSale;
        if (entry == null)
        {
            return;
        }

        Log.LogInfo($"[AP] Shop purchase: tarot={entry.TarotCard} card={entry.Card} "
            + $"decoration={entry.Decoration} item={entry.itemToBuy} "
            + $"cost={entry.itemCost} {entry.costType}");

        // Activate() runs 0.25s after the purchase, from DelayOnActivate, so a subscriber that
        // throws cannot unwind it. Caught anyway so a failed send stays a logged warning.
        try
        {
            OnItemPurchased?.Invoke(entry);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] A shop purchase completed but reporting it failed: {e}");
        }
    }
}
