using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check when a Tarot Card is bought from a hub shop
/// </summary>
/// <remarks>
/// Each hub sells the same named cards every time, so each purchase is a fixed location, 14
/// across the four hubs. Keyed by enum name ("The Burning Dead" is Skull).
///
/// A card bought while disconnected is lost for good. The game saves shop purchases, but tarot
/// purchases skip that save, so the only trace is the card in PlayerFoundTrinkets, and that looks
/// the same as an AP grant.
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
