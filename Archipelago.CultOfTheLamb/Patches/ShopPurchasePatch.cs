using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Catches purchases from any hub shop.
///
/// Interaction_BuyItem is the universal shop-slot class - it drives the Tarot Card shops, the
/// Decoration shops and the plain item stalls alike, which is why every one of them shows the
/// same "Buy &lt;name&gt; for &lt;cost&gt;" prompt. One patch therefore covers every shop in the game.
///
/// Activate() is the purchase-completed step (it raises the interaction's own OnItemBought and
/// then plays the bought effects). Patching it rather than subscribing to that event because
/// the event is per-instance: subscribing would mean finding and hooking every slot object as
/// it spawns, whereas a postfix catches them all with no lifecycle tracking.
///
/// The BuyEntry handed back says what was bought - BuyEntry.TarotCard plus BuyEntry.Card for
/// cards, BuyEntry.Decoration plus decorationToBuy for decorations - so subscribers can filter
/// to the kinds they care about.
/// </summary>
[HarmonyPatch(typeof(Interaction_BuyItem))]
internal static class ShopPurchasePatch
{
    /// <summary>Fires with the purchased entry. Decorations and plain items come through too.</summary>
    internal static event Action<BuyEntry> OnItemPurchased;

    [HarmonyPatch("Activate")]
    [HarmonyPostfix]
    private static void Activate_Postfix(Interaction_BuyItem __instance)
    {
        // customItemForSale entries are built at runtime rather than configured on the prefab,
        // and Activate() bails out early for them - nothing stable to key a location off.
        if (__instance == null || __instance.customItemForSale) return;

        var entry = __instance.itemForSale;
        if (entry == null) return;

        Log.LogInfo($"[AP] Shop purchase: tarot={entry.TarotCard} card={entry.Card} "
            + $"decoration={entry.Decoration} item={entry.itemToBuy} "
            + $"cost={entry.itemCost} {entry.costType}");

        OnItemPurchased?.Invoke(entry);
    }
}
