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
/// The collection is emptied on connect, including the cards the game normally starts you with
/// - a randomizer where a sixth of the pool is already in hand isn't randomizing those. The
/// seed's own starting cards are granted straight back, so "what you begin with" is the seed's
/// decision rather than the game's.
///
/// Emptying it edits real save data, so it is strictly symmetrical: everything taken is put
/// back on disconnect. Someone who tries the mod and stops should get their save exactly as it
/// was, not one missing sixty cards. RegionUnlockService sets the same precedent for regions.
///
/// Card identity comes from slot data, keyed by AP item name, because display names are
/// nothing like the TarotCards.Card enum names ("The Burning Dead" is Skull) and hardcoding
/// either side would let the two drift silently.
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

    /// <summary>Every card this seed owns, whether or not its check lives on the card.</summary>
    private readonly HashSet<TarotCards.Card> managedCards;

    /// <summary>Cards the seed says the player begins with. No check, no item.</summary>
    private readonly HashSet<TarotCards.Card> startingCards;

    /// <summary>
    /// What we took off the player, so it can be handed back. Only holds cards that were
    /// genuinely unlocked before we touched them.
    /// </summary>
    private readonly List<TarotCards.Card> revoked = new();

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
        managedCards = new HashSet<TarotCards.Card>(this.itemNameToCard.Values);
    }

    public void Register()
    {
        RevokeManagedCards();
        GrantStartingCards();

        TarotUnlockPatch.Decide = Decide;
        Log.LogInfo($"[AP] Tarot cards active: {itemNameToCard.Count} managed, "
            + $"{startingCards.Count} granted at start.");
    }

    public void Unregister()
    {
        TarotUnlockPatch.Decide = null;
        RestoreRevokedCards();
    }

    /// <summary>
    /// What should happen when the game tries to unlock a card. See TarotUnlockPatch.
    /// </summary>
    private TarotUnlockPatch.UnlockDecision Decide(TarotCards.Card card)
    {
        // Not part of this seed - co-op cards, or Woolhaven ones without the DLC option. The
        // game keeps them entirely, because nothing here would ever grant them back.
        if (!managedCards.Contains(card)) return TarotUnlockPatch.UnlockDecision.Allow;

        // A shop slot already sent its own check for this purchase.
        if (ShopSlotDisplayPatch.ConsumeSuppressedUnlock(card))
        {
            return TarotUnlockPatch.UnlockDecision.Swallow;
        }

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
    /// UnlockTrinket is a Contains-then-Add, so granting twice is a no-op. That matters
    /// because the collection is rebuilt from the item history on every connect - we just
    /// emptied it.
    /// </summary>
    internal bool TryApplyItem(string itemName)
    {
        if (itemName == null || !itemNameToCard.TryGetValue(itemName, out var card)) return false;

        Grant(card);

        // No longer ours to give back - the player owns it through Archipelago now.
        revoked.Remove(card);

        Log.LogInfo($"[AP] Tarot card '{itemName}' ({card}) unlocked.");
        return true;
    }

    /// <summary>
    /// Unlocks a card for real, through the game's own method so it raises the game's
    /// card-unlocked alert. Wrapped so our own grant isn't mistaken for the player earning it
    /// and turned straight back into a check.
    /// </summary>
    private static void Grant(TarotCards.Card card) =>
        TarotUnlockPatch.WhileGranting(() => TarotCards.UnlockTrinket(card));

    /// <summary>
    /// Empties the collection of every card this seed manages.
    ///
    /// Only managed cards are touched: co-op cards, and Woolhaven cards in a non-DLC seed, are
    /// left exactly as the player had them.
    /// </summary>
    private void RevokeManagedCards()
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null)
        {
            Log.LogWarning("[AP] No save loaded - tarot cards can't be revoked yet.");
            return;
        }

        revoked.Clear();

        foreach (var card in managedCards)
        {
            if (found.Remove(card)) revoked.Add(card);
        }

        Log.LogInfo($"[AP] Revoked {revoked.Count} tarot card(s) - they come from the multiworld "
            + "now. They're returned if you disconnect.");
    }

    private void GrantStartingCards()
    {
        foreach (var card in startingCards)
        {
            Grant(card);

            // Granted rather than kept, so it isn't owed back on disconnect - the player would
            // have had it either way.
            revoked.Remove(card);
        }
    }

    private void RestoreRevokedCards()
    {
        if (revoked.Count == 0) return;

        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null)
        {
            // The save is gone (quit to menu), so there's nothing to put them back into - and
            // nothing was written to disk, so the save still has them.
            revoked.Clear();
            return;
        }

        foreach (var card in revoked)
        {
            if (!found.Contains(card)) found.Add(card);
        }

        Log.LogInfo($"[AP] Returned {revoked.Count} tarot card(s) to the save.");
        revoked.Clear();
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
            if (TryParseCard(entry.Key, entry.Key, out var card))
            {
                result[card] = entry.Value.ToObject<long>();
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
}
