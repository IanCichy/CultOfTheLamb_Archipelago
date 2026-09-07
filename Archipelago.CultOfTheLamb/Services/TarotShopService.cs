using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check when a Tarot Card is bought from a hub shop.
/// </summary>
/// <remarks>
/// Every hub sells a fixed, named set of cards rather than randomised stock, so each purchase is
/// a stable location, 14 across the four hubs. Keyed by enum name, which is what a BuyEntry
/// exposes ("The Burning Dead" is Skull).
///
/// No catch-up is possible, and a card bought while disconnected is lost for good. There is
/// nothing in save data to re-derive from: DataManager.Shops persists BuyEntry.Bought, but the
/// tarot branch of Interaction_BuyItem.Activate spawns a TarotCustomTarget and returns before
/// any Bought = true / UpdateShop call, so a tarot slot leaves no trace. The card landing in
/// PlayerFoundTrinkets is the only evidence, and that can't be told apart from an AP grant.
/// </remarks>
internal class TarotShopService : IService
{
    private readonly ArchipelagoSession session;
    private readonly Dictionary<string, long> cardToCheckId;

    internal TarotShopService(ArchipelagoSession session, Dictionary<string, long> cardToCheckId)
    {
        this.session = session;
        this.cardToCheckId = cardToCheckId ?? new Dictionary<string, long>();
    }

    public void Register()
    {
        ShopPurchasePatch.OnItemPurchased += HandleItemPurchased;
        Log.LogInfo($"[AP] Tarot shop checks active: {cardToCheckId.Count} card(s) mapped.");
    }

    public void Unregister()
    {
        ShopPurchasePatch.OnItemPurchased -= HandleItemPurchased;
    }

    private void HandleItemPurchased(BuyEntry entry)
    {
        // Decorations and plain items flow through the same patch. Only cards are locations.
        if (entry == null || !entry.TarotCard)
        {
            return;
        }

        var cardName = entry.Card.ToString();
        if (!cardToCheckId.TryGetValue(cardName, out var checkId))
        {
            // A card sold somewhere we haven't catalogued. Worth surfacing rather than
            // dropping, because it is a location we could be offering and aren't.
            Log.LogInfo($"[AP] Bought tarot card '{cardName}' with no mapped AP location - skipping.");
            return;
        }

        // Before the check, because the purchase cutscene is already running. The card unlock
        // lands a couple of seconds from now, and this has to be armed before it does.
        ShopSlotDisplayPatch.SuppressNextUnlock(entry.Card);

        Log.LogInfo($"[AP] Bought tarot card '{cardName}', sending check {checkId}");
        CheckSender.Send(session, checkId);
    }
}
