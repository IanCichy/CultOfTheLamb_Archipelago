using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check when a Tarot Card is bought from a hub shop.
///
/// Every hub sells a fixed, named set of cards rather than randomised stock, so each purchase
/// is a stable location. 14 across the four hubs.
///
/// The card enum name -> location id mapping comes from slot data. It's keyed by *enum* name
/// because that's what a BuyEntry exposes, and because the display names are nothing like the
/// internal ones - "The Burning Dead" is Skull, "The Path" is MovementSpeed - so matching on
/// display text would be both fragile and localisation-dependent.
///
/// There's no catch-up pass on connect: unlike Follower counts or the sermon level, the game
/// records a purchase only as BuyEntry.Bought on the shop prefab, which isn't reachable unless
/// the player is standing in that hub. Cards bought while disconnected are therefore missed
/// until the shop is revisited. Acceptable - the alternative is scanning every hub scene.
/// </summary>
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
        // Decorations and plain items flow through the same patch; only cards are locations.
        if (entry == null || !entry.TarotCard) return;

        var cardName = entry.Card.ToString();
        if (!cardToCheckId.TryGetValue(cardName, out var checkId))
        {
            // A card sold somewhere we haven't catalogued. Worth surfacing rather than
            // dropping - it's a location we could be offering and aren't.
            Log.LogInfo($"[AP] Bought tarot card '{cardName}' with no mapped AP location - skipping.");
            return;
        }

        Log.LogInfo($"[AP] Bought tarot card '{cardName}', sending check {checkId}");
        session.Locations.CompleteLocationChecks(checkId);
        CheckNotifier.Announce(session, new[] { checkId });
    }
}
