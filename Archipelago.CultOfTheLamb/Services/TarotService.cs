using System;
using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Makes the tarot collection an Archipelago system: earning a card sends a check, and cards
/// arrive as items.
///
/// The collection is emptied on connect, including the cards the game normally starts you with -
/// a randomizer where a sixth of the pool is already in hand isn't randomizing those. The seed's
/// own starting cards are granted straight back.
///
/// Card identity comes from slot data, keyed by AP item name, because display names are nothing
/// like the TarotCards.Card enum names ("The Burning Dead" is Skull) and hardcoding either side
/// would let the two drift silently.
///
/// The revoke/restore/persist/sweep machinery lives in <see cref="ManagedCollection{T}"/>, which
/// also explains why grants are held outside the game's collection. What stays here is what is
/// genuinely about tarot: slot-data parsing, the unlock decision, and the patch wiring.
/// </summary>
internal class TarotService : IService
{
    private readonly ArchipelagoSession session;

    /// <summary>AP item name -> card, for every card this seed manages.</summary>
    private readonly Dictionary<string, TarotCards.Card> itemNameToCard;

    /// <summary>
    /// Card -> the check that earning it sends. Not every managed card is here: the ones sold
    /// in shops have their check on the shop slot instead.
    /// </summary>
    private readonly Dictionary<TarotCards.Card, long> cardToCheckId;

    /// <summary>Cards the seed says the player begins with. No check, no item.</summary>
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

        // Unregister runs on the websocket thread - Session_SocketClosed -> TeardownSession -
        // and PlayerFoundTrinkets is a plain List the main thread iterates. Touching it from
        // here can throw mid-enumeration or undo a lend that's part-way through.
        //
        // If a reconnect beats the drain, the new session's sweep takes back out whatever
        // this puts in, and the store is what makes that safe either way.
        MainThreadQueue.Enqueue(collection.End);
    }

    /// <summary>Sweeps the collection back to the invariant. See ManagedCollection.Tick.</summary>
    internal void Tick() => collection.Tick();

    /// <summary>
    /// What should happen when the game tries to unlock a card. See TarotUnlockPatch.
    /// </summary>
    private TarotUnlockPatch.UnlockDecision Decide(TarotCards.Card card)
    {
        // Not part of this seed - co-op cards, or Woolhaven ones without the DLC option. The
        // game keeps them entirely, because nothing here would ever grant them back.
        if (!collection.IsManaged(card)) return TarotUnlockPatch.UnlockDecision.Allow;

        // A shop slot already sent its own check for this purchase.
        if (ShopSlotDisplayPatch.ConsumeSuppressedUnlock(card))
        {
            return TarotUnlockPatch.UnlockDecision.Swallow;
        }

        // Sent even when the card is already in `granted`. That's the point of holding
        // Archipelago's cards outside the collection: the trigger stays available, so finding
        // a card the multiworld already gave you still pays its check.
        if (cardToCheckId.TryGetValue(card, out var checkId))
        {
            CheckSender.Send(session, checkId);
            return TarotUnlockPatch.UnlockDecision.SendCheck;
        }

        // Ours, but its check lives on a shop slot rather than on the card - so the card is
        // still withheld (it comes from the pool) and nothing is sent. Every card has exactly
        // one location, and for these that location is the shop.
        return TarotUnlockPatch.UnlockDecision.Swallow;
    }

    /// <summary>
    /// Grants a card if the item is one. Returns false so the caller can keep looking.
    ///
    /// Safe to replay alongside region and sermon items rather than being suppressed:
    /// granting is a set Add, so granting twice is a no-op. That matters because the
    /// collection is rebuilt from the item history on every connect - we just emptied it.
    /// </summary>
    internal bool TryApplyItem(string itemName)
    {
        if (itemName == null || !itemNameToCard.TryGetValue(itemName, out var card)) return false;

        // No game-side alert is raised. ArchipelagoItemLogicController already announces every
        // item received, and the game's card-unlocked alert would badge a card its own
        // collection doesn't contain.
        collection.Grant(card);

        Log.LogInfo($"[AP] Tarot card '{itemName}' ({card}) unlocked.");
        return true;
    }

    private void GrantStartingCards()
    {
        foreach (var card in startingCards) collection.Grant(card);
    }

    /// <summary>
    /// AP item name -> card. Cards whose enum name this build of the game doesn't recognise are
    /// dropped with a warning rather than throwing: that means the mod and the game disagree
    /// about the card list, and losing one card beats losing the connection.
    /// </summary>
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

    /// <summary>Card -> location id, from "tarotCardLocations" (keyed by enum name).</summary>
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

    /// <summary>Cards granted at seed start, from "startingTarotCards" (enum names).</summary>
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
            if (TryParseCard(name?.ToString(), name?.ToString(), out var card)) result.Add(card);
        }

        return result;
    }

    private static bool TryParseCard(string internalName, string context, out TarotCards.Card card)
    {
        card = default;
        if (string.IsNullOrEmpty(internalName)) return false;

        if (!Enum.IsDefined(typeof(TarotCards.Card), internalName))
        {
            Log.LogWarning("[AP] Slot data names a tarot card this game doesn't have: "
                + $"'{internalName}' - skipping '{context}'.");
            return false;
        }

        card = (TarotCards.Card)Enum.Parse(typeof(TarotCards.Card), internalName);
        return true;
    }

    /// <summary>
    /// Reads one location id out of slot data. Malformed entries skip their own card rather
    /// than throwing: this runs during connect, so an exception here costs the whole session
    /// instead of the one check we couldn't parse.
    /// </summary>
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
