using System;
using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The game-side collection a <see cref="ManagedCollection{T}"/> manages.
///
/// Three operations, because three is all the state machine needs - and keeping it to three is
/// what lets the game's wildly different storage shapes plug in unchanged. Tarot's
/// PlayerFoundTrinkets is a List of the enum; fleeces' UnlockedFleeces is a plain List of int
/// with no unlock API at all; doctrines go through DoctrineUpgradeSystem's own methods. Each of
/// those is a small adapter over the same three calls rather than a special case in the sweep.
/// </summary>
internal interface IManagedBacking<T> where T : struct, Enum
{
    /// <summary>
    /// False when there is no save to read - the player is at the main menu, or a load is in
    /// flight. Every caller treats this as "try again next tick" rather than as an error,
    /// because writing into a save that isn't loaded is how debt lands on the wrong file.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Puts an entry back. Returns false if it was already there.</summary>
    bool Add(T value);

    /// <summary>Takes an entry out. Returns false if it wasn't there.</summary>
    bool Remove(T value);
}

/// <summary>
/// Holds part of a game collection outside the save, so Archipelago can hand it out instead.
///
/// Extracted from TarotService, which paid for every lesson in here the hard way. Fleeces,
/// follower forms, doctrines, structures and outfits are all the same shape, and each has the
/// same two problems tarot hit:
///
/// **The gate problem.** Every route the game has to offer you something first checks you don't
/// already own it - the mystic shop, the spider shop's Arrows, UnlockTrinket itself, the broom
/// outfit's `!UnlockedClothing.Contains(Special_4)`. Unlocking an Archipelago grant *for real*
/// closes that gate, and the check riding on it can never fire again. So grants are held in
/// <see cref="granted"/> and deliberately never written to the game's collection; the collection
/// stays empty of managed entries and every unlock trigger stays live for the whole seed.
/// Anything that genuinely needs to see them gets them lent back (TarotVisibility's job).
///
/// **The symmetry problem.** Emptying the collection edits real save data, so it has to be
/// strictly reversible: everything taken is put back on disconnect. Someone who tries the mod
/// and stops should get their save exactly as it was, not one missing sixty cards.
///
/// Where a system instead has a *single reward method* to intercept - as sermons do with
/// SermonController.PlayerUpgrade - prefer a Harmony prefix there. Suppressing the reward at
/// source needs none of this machinery. This class is for collections that can only be managed
/// after the fact.
/// </summary>
internal class ManagedCollection<T> where T : struct, Enum
{
    private readonly string collectionKey;
    private readonly IManagedBacking<T> backing;
    private readonly string legacyKey;
    private readonly string noun;

    /// <summary>Every entry this seed owns, whether or not its check lives on the entry.</summary>
    private readonly HashSet<T> managed;

    /// <summary>
    /// What we took off the player, so it can be handed back. Only holds entries that were
    /// genuinely unlocked before we touched them.
    /// </summary>
    private readonly HashSet<T> revoked = new();

    /// <summary>
    /// What Archipelago has handed over this session. This is the player's real collection as
    /// far as Archipelago is concerned; the game's own collection deliberately never learns
    /// about it (see the class summary).
    /// </summary>
    private readonly HashSet<T> granted = new();

    /// <summary>
    /// Which save <see cref="revoked"/> was taken from. The player can load a different save
    /// without reconnecting, and entries owed to one save must never be written into another.
    /// </summary>
    private int saveSlot;

    /// <summary>
    /// Set whenever the debt changes, cleared once <see cref="Tick"/> has written it out.
    ///
    /// The debt is written from the tick rather than at the moment it changes because the item
    /// replay grants dozens of entries in a row on connect, and each one would otherwise
    /// rewrite the whole file.
    /// </summary>
    private bool debtDirty;

    /// <param name="collectionKey">Namespaces this collection's rows in the store.</param>
    /// <param name="noun">What one entry is called, for log lines the player reads.</param>
    /// <param name="legacyKey">See <see cref="ManagedCollectionStore.Owed{T}"/>.</param>
    internal ManagedCollection(
        string collectionKey,
        IManagedBacking<T> backing,
        IEnumerable<T> managed,
        string noun,
        string legacyKey = null)
    {
        this.collectionKey = collectionKey;
        this.backing = backing;
        this.legacyKey = legacyKey;
        this.noun = noun;
        this.managed = new HashSet<T>(managed);
    }

    /// <summary>What Archipelago has handed over. Lent to the places that need to see it.</summary>
    internal IReadOnlyCollection<T> Granted => granted;

    internal bool IsManaged(T value) => managed.Contains(value);

    /// <summary>
    /// Takes every managed entry out of the save and starts the session's bookkeeping.
    ///
    /// Only managed entries are touched: co-op cards, and DLC entries in a non-DLC seed, are
    /// left exactly as the player had them.
    /// </summary>
    internal void Begin()
    {
        saveSlot = SaveSlot.Current;

        // Anything an earlier session took and never gave back is still owed. Folded in before
        // revoking so a session that ended in a crash doesn't cost the player anything.
        revoked.UnionWith(ManagedCollectionStore.Owed<T>(collectionKey, saveSlot, legacyKey));

        RevokeManaged();
    }

    /// <summary>
    /// Hands everything back. Queued onto the main thread by the caller rather than run here:
    /// teardown arrives on the websocket thread (Session_SocketClosed -> TeardownSession) and
    /// the game's collections are plain Lists the main thread iterates, so touching them from
    /// there can throw mid-enumeration or undo a lend that's part-way through.
    /// </summary>
    internal void End() => Restore();

    /// <summary>
    /// Hands an entry to the player - into our own set rather than the game's collection, so
    /// the game keeps offering it and the check riding on it stays reachable.
    ///
    /// Safe to replay: granting is a set Add, so granting twice is a no-op. That matters
    /// because the collection is rebuilt from the item history on every connect.
    /// </summary>
    internal void Grant(T value)
    {
        if (granted.Add(value)) debtDirty = true;
    }

    /// <summary>
    /// Keeps the invariant true: no entry this seed manages is ever in the game's collection.
    ///
    /// <see cref="Begin"/> establishes it once, but plenty of things break it afterwards.
    /// GameManager.Awake re-seeds fifteen default tarot cards whenever the collection is empty
    /// (GameManager.cs:175) - which is exactly the state we leave a fresh save in - so quitting
    /// to the menu and loading again silently hands them back as real unlocks, closing their
    /// gates and stranding their checks. Loading a *different* save is worse: none of its
    /// collection was ever revoked.
    ///
    /// A sweep rather than a hook on Awake or SaveAndLoad.OnLoadComplete, because those are
    /// ordering-sensitive - Awake's re-seed runs after a load-complete revoke and undoes it -
    /// and neither covers anything else that writes to the collection. This is self-healing
    /// whatever put the entry there.
    ///
    /// Safe against the lending patches: a lend and its matching take both complete inside one
    /// synchronous call on this same thread, so a tick can never catch a loan in progress.
    /// </summary>
    internal void Tick()
    {
        if (!backing.IsAvailable) return;

        if (SaveSlot.Current != saveSlot)
        {
            SwitchToLoadedSave();
            return;
        }

        List<T> reappeared = null;

        foreach (var value in managed)
        {
            if (!backing.Remove(value)) continue;

            // Already accounted for - the game handed back something we'd taken, or something
            // Archipelago had granted. Owed either way, just not newly.
            if (revoked.Contains(value) || granted.Contains(value)) continue;

            (reappeared ??= new List<T>()).Add(value);
        }

        if (reappeared != null)
        {
            revoked.UnionWith(reappeared);
            debtDirty = true;
            Log.LogInfo($"[AP] The game put {reappeared.Count} managed {noun}(s) back into the "
                + "collection - taken out again so their checks stay reachable.");
        }

        if (debtDirty) PersistDebt();
    }

    private void RevokeManaged()
    {
        if (!backing.IsAvailable)
        {
            Log.LogWarning($"[AP] No save loaded - {noun}s can't be revoked yet.");
            return;
        }

        var taken = 0;
        foreach (var value in managed)
        {
            if (!backing.Remove(value)) continue;
            revoked.Add(value);
            taken++;
        }

        // Recorded now rather than at disconnect. The game autosaves throughout, so from this
        // point the save on disk is already missing these, and the store is the only thing that
        // still knows they're owed if the process dies.
        PersistDebt();

        Log.LogInfo($"[AP] Revoked {taken} {noun}(s) - they come from the multiworld now. "
            + "They're returned if you disconnect.");
    }

    /// <summary>
    /// Records everything the loaded save is owed: what we took off it, and what Archipelago
    /// has handed over.
    ///
    /// Both, because a granted entry is only ever held in memory and lent - it is never written
    /// to the save. Quitting to the desktop runs no teardown (the plugin has no
    /// OnApplicationQuit) and a crash runs less than that, so without recording them here the
    /// player loses everything the multiworld gave them.
    /// </summary>
    private void PersistDebt()
    {
        var owed = new HashSet<T>(revoked);
        owed.UnionWith(granted);

        ManagedCollectionStore.Owe(collectionKey, saveSlot, owed);
        debtDirty = false;
    }

    /// <summary>
    /// A different save is loaded than the one we took from. That save is no longer in memory,
    /// so its debt can only be handed to the store; the new one then gets the same treatment
    /// the old one got at connect.
    /// </summary>
    private void SwitchToLoadedSave()
    {
        Log.LogInfo($"[AP] Save slot changed ({saveSlot} -> {SaveSlot.Current}). "
            + $"{revoked.Count} {noun}(s) stay owed to the old save.");

        // Only what we took off the old save. Archipelago's grants were never in it.
        ManagedCollectionStore.Owe(collectionKey, saveSlot, revoked);
        revoked.Clear();

        // `granted` deliberately survives. It's connection state, never written to a save and
        // only ever lent, so carrying it across is safe - and clearing it would strand the
        // player with nothing at all, because the item replay that would rebuild it runs only
        // on connect (ArchipelagoItemLogicController.Register), not on a save load.
        //
        // It surviving is also why nothing re-grants the seed's starting entries here: they
        // went into `granted` at connect and are still in it. TarotService.SwitchToLoadedSave
        // used to call GrantStartingCards() at this point, which was already a no-op.
        saveSlot = SaveSlot.Current;
        revoked.UnionWith(ManagedCollectionStore.Owed<T>(collectionKey, saveSlot, legacyKey));
        RevokeManaged();
    }

    /// <summary>
    /// Puts the player's entries back into the save: both the ones taken at connect and the
    /// ones Archipelago granted during the session.
    ///
    /// Granted entries have to be included. They were never written to the collection - they
    /// only ever lived in <see cref="granted"/> - so leaving them out would mean disconnecting
    /// silently took away everything the multiworld handed over.
    /// </summary>
    private void Restore()
    {
        var owed = new HashSet<T>(revoked);
        owed.UnionWith(granted);

        revoked.Clear();
        granted.Clear();

        if (owed.Count == 0)
        {
            ManagedCollectionStore.Settle(collectionKey, saveSlot, legacyKey);
            return;
        }

        // Either no save is loaded (quit to the menu before disconnecting) or a different one
        // is. Writing into it would be worse than waiting: the store keeps the debt, and the
        // next connect on the right save pays it.
        if (!backing.IsAvailable || SaveSlot.Current != saveSlot)
        {
            ManagedCollectionStore.Owe(collectionKey, saveSlot, owed);
            Log.LogWarning($"[AP] Save {saveSlot} isn't loaded, so its {owed.Count} {noun}(s) "
                + "couldn't be returned yet. They're recorded, and come back the next time you "
                + "connect on that save.");
            return;
        }

        foreach (var value in owed) backing.Add(value);

        // Only once they're actually back in the collection.
        ManagedCollectionStore.Settle(collectionKey, saveSlot, legacyKey);

        Log.LogInfo($"[AP] Returned {owed.Count} {noun}(s) to the save.");
    }

    /// <summary>
    /// Pays back whatever the loaded save is owed, for the case where nothing else will.
    ///
    /// A live collection repays on disconnect and folds any leftover debt in on the next
    /// connect, which covers a player who keeps using the mod. It doesn't cover the one who
    /// crashes and then uninstalls: their save would stay short forever. So this runs while
    /// *disconnected*, from the plugin's own poll - after a clean disconnect there's no entry
    /// to find, so it only ever fires on the interrupted paths.
    ///
    /// Static because at that point no session, and so no collection instance, exists.
    /// </summary>
    internal static void SettleIfOwed(
        string collectionKey,
        IManagedBacking<T> backing,
        string noun,
        string legacyKey = null)
    {
        if (!backing.IsAvailable) return;

        var saveSlot = SaveSlot.Current;
        var owed = ManagedCollectionStore.Owed<T>(collectionKey, saveSlot, legacyKey);
        if (owed.Count == 0) return;

        foreach (var value in owed) backing.Add(value);

        ManagedCollectionStore.Settle(collectionKey, saveSlot, legacyKey);
        Log.LogInfo($"[AP] Returned {owed.Count} {noun}(s) an interrupted session still owed "
            + "this save.");
    }
}
