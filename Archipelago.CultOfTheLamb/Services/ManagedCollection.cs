using System;
using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The game-side collection a <see cref="ManagedCollection{T}"/> manages. Three operations is
/// all the state machine needs, which is what lets the game's very different storage shapes -
/// a List of enums, a List of ints, a system with its own methods - plug in as small adapters.
/// </summary>
internal interface IManagedBacking<T> where T : struct, Enum
{
    /// <summary>
    /// False when there's no save to read. Callers treat it as "try again next tick" - writing
    /// into a save that isn't loaded is how debt lands on the wrong file.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Puts an entry back. Returns false if it was already there.</summary>
    bool Add(T value);

    /// <summary>Takes an entry out. Returns false if it wasn't there.</summary>
    bool Remove(T value);
}

/// <summary>
/// Holds part of a game collection outside the save, so Archipelago can hand it out instead.
/// Fleeces, follower forms, doctrines, structures and outfits are all this shape.
///
/// **The gate problem.** Every route the game has to offer you something first checks you don't
/// already own it, so unlocking an Archipelago grant *for real* closes that gate and strands the
/// check riding on it. Grants therefore live in <see cref="granted"/> and are never written to
/// the game's collection; anything that genuinely needs to see them gets them lent back.
///
/// **The symmetry problem.** Emptying the collection edits real save data, so everything taken
/// is put back on disconnect.
///
/// Where a system has a single reward method to intercept - as sermons do - prefer a Harmony
/// prefix there. This class is for collections that can only be managed after the fact.
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
    /// What Archipelago has handed over this session. The game's own collection deliberately
    /// never learns about it - see the class summary.
    /// </summary>
    private readonly HashSet<T> granted = new();

    /// <summary>
    /// Which save <see cref="revoked"/> was taken from. The player can load a different save
    /// without reconnecting, and entries owed to one save must never be written into another.
    /// </summary>
    private int saveSlot;

    /// <summary>
    /// Set whenever the debt changes, cleared once <see cref="Tick"/> has written it out.
    /// Batched through the tick because the connect-time item replay grants dozens in a row,
    /// and each would otherwise rewrite the whole file.
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
    /// Hands everything back. The caller queues this onto the main thread: teardown arrives on
    /// the websocket thread, and the game's collections are plain Lists the main thread
    /// iterates.
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
    /// <see cref="Begin"/> establishes it once, but GameManager.Awake re-seeds fifteen default
    /// tarot cards whenever the collection is empty (GameManager.cs:175) - exactly the state we
    /// leave a fresh save in. A sweep rather than a hook on Awake or OnLoadComplete, because
    /// those are ordering-sensitive and neither covers anything else that writes to the
    /// collection.
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
    /// granted. Both, because grants only ever live in memory - quitting to the desktop runs no
    /// teardown, so without this the player loses everything the multiworld gave them.
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

        // `granted` deliberately survives: it's connection state, and the item replay that would
        // rebuild it runs only on connect, not on a save load. That's also why the seed's
        // starting entries aren't re-granted here - they're still in it.
        saveSlot = SaveSlot.Current;
        revoked.UnionWith(ManagedCollectionStore.Owed<T>(collectionKey, saveSlot, legacyKey));
        RevokeManaged();
    }

    /// <summary>
    /// Puts the player's entries back: both the ones taken at connect and the ones Archipelago
    /// granted. Grants have to be included - they only ever lived in memory, so leaving them out
    /// would mean disconnecting silently took away everything the multiworld handed over.
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
    /// Pays back whatever the loaded save is owed, for the player who crashes and then
    /// uninstalls - nothing else would ever repay them. Runs from the plugin's poll while
    /// *disconnected*, so it only fires on the interrupted paths. Static because no session,
    /// and so no collection instance, exists then.
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
