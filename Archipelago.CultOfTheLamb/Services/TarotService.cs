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
/// Cards Archipelago hands over are held here rather than unlocked in the game. That looks
/// roundabout, but it is what keeps card locations reachable: every route the game has to
/// offer a card first checks you don't already own it - the mystic shop, the spider shop's
/// Arrows, the follower plant's Joker, UnlockTrinket itself. Unlocking an Archipelago card
/// for real would close that gate, and the check riding on it could never fire again. Leaving
/// the collection empty of managed cards keeps every unlock trigger live for the whole seed.
/// TarotVisibility lends them back to the two places that genuinely need to see them.
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
    private readonly HashSet<TarotCards.Card> revoked = new();

    /// <summary>
    /// Which save <see cref="revoked"/> was taken from. The player can load a different save
    /// without reconnecting, and cards owed to one save must never be written into another.
    /// </summary>
    private int saveSlot;

    /// <summary>
    /// Which save is loaded, with the Woolhaven variant folded onto its base slot.
    ///
    /// SaveAndLoad.SAVE_SLOT is not stable within a session. The game keeps a DLC save at
    /// slot+10 beside a base-game backup at slot, and moves SAVE_SLOT between the two while
    /// writing: MakeBaseGameBackUpSave adds 10, saves, and puts it back (SaveAndLoad.cs:307),
    /// while Saving can subtract 10 for good (:183). Comparing the raw value would read those
    /// as the player loading a different save - during which this would hand the debt to the
    /// wrong key and clear Archipelago's cards with no item replay left to rebuild them.
    /// </summary>
    internal static int CurrentSaveId =>
        SaveAndLoad.SAVE_SLOT >= 10 ? SaveAndLoad.SAVE_SLOT - 10 : SaveAndLoad.SAVE_SLOT;

    /// <summary>
    /// Cards Archipelago has handed over this session. This is the player's real collection
    /// as far as Archipelago is concerned; the game's own collection deliberately never
    /// learns about them (see the class summary).
    /// </summary>
    private readonly HashSet<TarotCards.Card> granted = new();

    /// <summary>
    /// Set whenever the debt changes, cleared once <see cref="Tick"/> has written it out.
    ///
    /// The debt is written from the tick rather than at the moment it changes because the
    /// item replay grants dozens of cards in a row on connect, and each one would otherwise
    /// rewrite the whole file.
    /// </summary>
    private bool debtDirty;

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
        saveSlot = CurrentSaveId;

        // Anything an earlier session took and never gave back is still owed. Folded in
        // before revoking so a session that ended in a crash doesn't cost the player cards.
        revoked.UnionWith(RevokedCardStore.Owed(saveSlot));

        RevokeManagedCards();
        GrantStartingCards();

        TarotUnlockPatch.Decide = Decide;
        TarotVisibility.GrantedCards = () => granted;
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
        MainThreadQueue.Enqueue(RestoreCards);
    }

    /// <summary>
    /// Keeps the invariant true: no card this seed manages is ever in the game's collection.
    ///
    /// Register establishes it once, but plenty of things break it afterwards.
    /// GameManager.Awake re-seeds fifteen default cards whenever the collection is empty
    /// (GameManager.cs:175) - which is exactly the state we leave a fresh save in - so
    /// quitting to the menu and loading again silently hands them back as real unlocks,
    /// closing their gates and stranding their checks. Loading a *different* save is worse:
    /// none of its collection was ever revoked.
    ///
    /// A sweep rather than a hook on Awake or SaveAndLoad.OnLoadComplete, because those are
    /// ordering-sensitive - Awake's re-seed runs after a load-complete revoke and undoes it -
    /// and neither covers anything else that writes to the collection. This is self-healing
    /// whatever put the card there.
    ///
    /// Safe against the lending patches: Lend and Take both complete inside one synchronous
    /// call on this same thread, so a tick can never catch a loan in progress.
    /// </summary>
    internal void Tick()
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null) return;

        if (CurrentSaveId != saveSlot)
        {
            SwitchToLoadedSave();
            return;
        }

        List<TarotCards.Card> reappeared = null;

        foreach (var card in managedCards)
        {
            if (!found.Remove(card)) continue;

            // Already accounted for - the game handed back something we'd taken, or
            // something Archipelago had granted. Owed either way, just not newly.
            if (revoked.Contains(card) || granted.Contains(card)) continue;

            (reappeared ??= new List<TarotCards.Card>()).Add(card);
        }

        if (reappeared != null)
        {
            revoked.UnionWith(reappeared);
            debtDirty = true;
            Log.LogInfo($"[AP] The game put {reappeared.Count} managed tarot card(s) back into "
                + "the collection - taken out again so their checks stay reachable.");
        }

        if (debtDirty) PersistDebt();
    }

    /// <summary>
    /// Records everything the loaded save is owed: what we took off it, and what Archipelago
    /// has handed over.
    ///
    /// Both, because a granted card is only ever held in memory and lent - it is never
    /// written to the save. Quitting to the desktop runs no teardown (the plugin has no
    /// OnApplicationQuit) and a crash runs less than that, so without recording them here the
    /// player loses every card the multiworld gave them.
    /// </summary>
    private void PersistDebt()
    {
        var owed = new HashSet<TarotCards.Card>(revoked);
        owed.UnionWith(granted);

        RevokedCardStore.Owe(saveSlot, owed);
        debtDirty = false;
    }

    /// <summary>
    /// A different save is loaded than the one we took cards from. That save is no longer in
    /// memory, so its debt can only be handed to the store; the new one then gets the same
    /// treatment the old one got at connect.
    /// </summary>
    private void SwitchToLoadedSave()
    {
        Log.LogInfo($"[AP] Save slot changed ({saveSlot} -> {CurrentSaveId}). "
            + $"{revoked.Count} tarot card(s) stay owed to the old save.");

        // Only what we took off the old save. Archipelago's cards were never in it.
        RevokedCardStore.Owe(saveSlot, revoked);
        revoked.Clear();

        // `granted` deliberately survives. It's connection state, never written to a save and
        // only ever lent, so carrying it across is safe - and clearing it would strand the
        // player with no cards at all, because the item replay that would rebuild it runs
        // only on connect (ArchipelagoItemLogicController.Register), not on a save load.
        saveSlot = CurrentSaveId;
        revoked.UnionWith(RevokedCardStore.Owed(saveSlot));
        RevokeManagedCards();
        GrantStartingCards();
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

        Grant(card);

        Log.LogInfo($"[AP] Tarot card '{itemName}' ({card}) unlocked.");
        return true;
    }

    /// <summary>
    /// Hands a card to the player - into our own set rather than the game's collection, so
    /// the game keeps offering it and the check riding on it stays reachable.
    ///
    /// No game-side alert is raised. ArchipelagoItemLogicController already announces every
    /// item received, and the game's card-unlocked alert would badge a card its own
    /// collection doesn't contain.
    /// </summary>
    private void Grant(TarotCards.Card card)
    {
        if (granted.Add(card)) debtDirty = true;
    }

    /// <summary>
    /// Empties the collection of every card this seed manages.
    ///
    /// Only managed cards are touched: co-op cards, and Woolhaven cards in a non-DLC seed, are
    /// left exactly as the player had them.
    ///
    /// Adds to <see cref="revoked"/> rather than replacing it, because the debt may already
    /// carry cards an interrupted session never handed back.
    /// </summary>
    private void RevokeManagedCards()
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null)
        {
            Log.LogWarning("[AP] No save loaded - tarot cards can't be revoked yet.");
            return;
        }

        var taken = 0;
        foreach (var card in managedCards)
        {
            if (!found.Remove(card)) continue;
            revoked.Add(card);
            taken++;
        }

        // Recorded now rather than at disconnect. The game autosaves throughout, so from this
        // point the save on disk is already missing these cards, and the store is the only
        // thing that still knows they're owed if the process dies.
        PersistDebt();

        Log.LogInfo($"[AP] Revoked {taken} tarot card(s) - they come from the multiworld now. "
            + "They're returned if you disconnect.");
    }

    private void GrantStartingCards()
    {
        foreach (var card in startingCards) Grant(card);
    }

    /// <summary>
    /// Puts the player's cards back into the save: both the ones taken at connect and the
    /// ones Archipelago granted during the session.
    ///
    /// Granted cards have to be included. They were never written to the collection - they
    /// only ever lived in <see cref="granted"/> - so leaving them out would mean
    /// disconnecting silently took away every card the multiworld handed over.
    /// </summary>
    private void RestoreCards()
    {
        var owed = new HashSet<TarotCards.Card>(revoked);
        owed.UnionWith(granted);

        revoked.Clear();
        granted.Clear();

        if (owed.Count == 0)
        {
            RevokedCardStore.Settle(saveSlot);
            return;
        }

        var found = DataManager.Instance?.PlayerFoundTrinkets;

        // Either no save is loaded (quit to the menu before disconnecting) or a different one
        // is. Writing into it would be worse than waiting: the store keeps the debt, and the
        // next connect on the right save pays it.
        if (found == null || CurrentSaveId != saveSlot)
        {
            RevokedCardStore.Owe(saveSlot, owed);
            Log.LogWarning($"[AP] Save {saveSlot} isn't loaded, so its {owed.Count} tarot "
                + "card(s) couldn't be returned yet. They're recorded, and come back the next "
                + "time you connect on that save.");
            return;
        }

        foreach (var card in owed)
        {
            if (!found.Contains(card)) found.Add(card);
        }

        // Only once they're actually back in the collection.
        RevokedCardStore.Settle(saveSlot);

        Log.LogInfo($"[AP] Returned {owed.Count} tarot card(s) to the save.");
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
