using System;
using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Makes the tarot collection an Archipelago system. Earning a card sends a check, and cards
/// arrive as items.
/// </summary>
/// <remarks>
/// The collection is emptied on connect, including the cards the game normally starts you with.
/// The seed's starting cards are then given straight back.
///
/// Cards are matched through slot data, keyed by AP item name, since display names don't match the
/// game's names ("The Burning Dead" is Skull). ManagedCollection does the revoke, restore and
/// sweep work. This class reads slot data, decides unlocks and wires up the patches.
///
/// A card earned while disconnected sends no check, and the connect sweep takes it back out of
/// the collection. It isn't lost: it goes into the revoked set with everything else we took, so
/// Restore hands it back on disconnect. What's missing is the check, and telling an offline card
/// apart from a granted one would need a new saved record, which can't live in Register because
/// that runs before any items are applied.
/// </remarks>
internal class TarotService : IService
{
    private readonly ArchipelagoSession session;

    // AP item name -> card, for every card this seed manages
    private readonly Dictionary<string, TarotCards.Card> itemNameToCard;

    // Card -> the check that earning it sends. Cards sold in shops are absent, their check
    // lives on the shop slot instead
    private readonly Dictionary<TarotCards.Card, long> cardToCheckId;

    // Cards the seed says the player begins with. No check, no item
    private readonly HashSet<TarotCards.Card> startingCards;

    private readonly ManagedCollection<TarotCards.Card> collection;

    internal TarotService(
        ArchipelagoSession session,
        Dictionary<string, TarotCards.Card> itemNameToCard,
        Dictionary<TarotCards.Card, long> cardToCheckId,
        HashSet<TarotCards.Card> startingCards)
    {
        this.session = session;
        this.itemNameToCard = itemNameToCard ?? new Dictionary<string, TarotCards.Card>();
        this.cardToCheckId = cardToCheckId ?? new Dictionary<TarotCards.Card, long>();
        this.startingCards = startingCards ?? new HashSet<TarotCards.Card>();

        collection = new ManagedCollection<TarotCards.Card>(
            TarotCollectionBacking.Key,
            new TarotCollectionBacking(),
            this.itemNameToCard.Values,
            TarotCollectionBacking.Noun,
            TarotCollectionBacking.LegacyKey);
    }

    public void Register()
    {
        collection.Begin();
        GrantStartingCards();

        TarotUnlockPatch.Decide = Decide;
        TarotVisibility.GrantedCards = () => collection.Granted;
        Log.LogInfo($"[AP] Tarot cards active: {itemNameToCard.Count} managed, "
            + $"{startingCards.Count} granted at start.");
    }

    public void Unregister()
    {
        // Reference writes, so they're safe from any thread, and they stop new work starting
        // while the restore is in flight.
        TarotUnlockPatch.Decide = null;
        TarotVisibility.GrantedCards = null;

        // Unregister runs on the websocket thread, by way of Session_SocketClosed and
        // TeardownSession, and PlayerFoundTrinkets is a plain List the main thread iterates.
        // Touching it from here can throw mid-enumeration or undo a lend that's part-way through.
        //
        // If a reconnect beats the drain, the new session's sweep takes back out whatever
        // this puts in, and the store is what makes that safe either way.
        MainThreadQueue.Enqueue(collection.End);
    }

    // Sweeps the collection back to the invariant. See ManagedCollection.Tick
    internal void Tick()
    {
        collection.Tick();
    }

    // What should happen when the game tries to unlock a card. See TarotUnlockPatch
    private TarotUnlockPatch.UnlockDecision Decide(TarotCards.Card card)
    {
        // Not part of this seed, meaning co-op cards or Woolhaven ones without the DLC option.
        // The game keeps them entirely, because nothing here would ever grant them back.
        if (!collection.IsManaged(card))
        {
            return TarotUnlockPatch.UnlockDecision.Allow;
        }

        // A shop slot already sent its own check for this purchase.
        if (ShopSlotDisplayPatch.ConsumeSuppressedUnlock(card))
        {
            return TarotUnlockPatch.UnlockDecision.Swallow;
        }

        // Sent even when the card is already in `granted`. That's the point of holding
        // Archipelago's cards outside the collection. The trigger stays available, so finding
        // a card the multiworld already gave you still pays its check.
        if (cardToCheckId.TryGetValue(card, out var checkId))
        {
            CheckSender.Send(session, checkId);
            return TarotUnlockPatch.UnlockDecision.SendCheck;
        }

        // Ours, but its check lives on a shop slot rather than on the card, so the card is
        // still withheld (it comes from the pool) and nothing is sent. Every card has exactly
        // one location, and for these that location is the shop.
        return TarotUnlockPatch.UnlockDecision.Swallow;
    }

    // Returns false so the caller can keep looking. Granting is a set Add, so this replays
    // safely, which it must: the collection is rebuilt from the item history on every connect
    internal bool TryApplyItem(string itemName)
    {
        if (itemName == null || !itemNameToCard.TryGetValue(itemName, out var card))
        {
            return false;
        }

        // No game-side alert is raised. ArchipelagoItemLogicController already announces every
        // item received, and the game's card-unlocked alert would badge a card its own
        // collection doesn't contain.
        collection.Grant(card);

        Log.LogInfo($"[AP] Tarot card '{itemName}' ({card}) unlocked.");
        return true;
    }

    private void GrantStartingCards()
    {
        foreach (var card in startingCards)
        {
            collection.Grant(card);
        }
    }

    // AP item name -> card. Cards whose enum name this build doesn't recognise are dropped
    // with a warning
    internal static Dictionary<string, TarotCards.Card> ParseCards(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<string, TarotCards.Card>();

        if (!slotData.TryGetValue("tarotCards", out var raw) || raw is not JObject mapping)
        {
            return result;
        }

        foreach (var entry in mapping)
        {
            if (TryParseCard(entry.Value?.ToString(), entry.Key, out var card))
            {
                result[entry.Key] = card;
            }
        }

        return result;
    }

    // Card -> location id, from "tarotCardLocations" (keyed by enum name)
    internal static Dictionary<TarotCards.Card, long> ParseCardLocations(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<TarotCards.Card, long>();

        if (!slotData.TryGetValue("tarotCardLocations", out var raw) || raw is not JObject mapping)
        {
            return result;
        }

        foreach (var entry in mapping)
        {
            if (TryParseCard(entry.Key, entry.Key, out var card)
                && TryParseLocationId(entry.Value, entry.Key, out var locationId))
            {
                result[card] = locationId;
            }
        }

        return result;
    }

    // Cards granted at seed start, from "startingTarotCards" (enum names)
    internal static HashSet<TarotCards.Card> ParseStartingCards(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new HashSet<TarotCards.Card>();

        if (!slotData.TryGetValue("startingTarotCards", out var raw) || raw is not JArray names)
        {
            return result;
        }

        foreach (var name in names)
        {
            if (TryParseCard(name?.ToString(), name?.ToString(), out var card))
            {
                result.Add(card);
            }
        }

        return result;
    }

    private static bool TryParseCard(string internalName, string context, out TarotCards.Card card)
    {
        return SlotData.TryParseEnum(internalName, context, out card);
    }

    // Malformed entries skip their own card rather than throwing. This runs during connect,
    // so a throw would cost the whole session
    private static bool TryParseLocationId(JToken value, string context, out long locationId)
    {
        locationId = 0;

        try
        {
            locationId = value.ToObject<long>();
            return true;
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Slot data has a non-numeric location id for '{context}': "
                + $"{e.Message} - skipping it.");
            return false;
        }
    }
}
