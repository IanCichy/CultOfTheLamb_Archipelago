using System;
using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The game-side collection a <see cref="ManagedCollection{T}"/> manages.
/// </summary>
/// <typeparam name="T">The game enum stored in the collection, such as TarotCards.Card.</typeparam>
/// <remarks>
/// Three operations is all the state machine needs, which lets very different storage shapes
/// plug in as small adapters: a List of enums, a List of ints, a system with its own methods.
/// </remarks>
internal interface IManagedBacking<T> where T : struct, Enum
{
    // False when there's no save to read. Callers treat it as "try again next tick", because
    // writing into a save that isn't loaded is how debt lands on the wrong file
    bool IsAvailable { get; }

    // Puts an entry back. Returns false if it was already there
    bool Add(T value);

    // Takes an entry out. Returns false if it wasn't there
    bool Remove(T value);
}

/// <summary>
/// Holds part of a game collection outside the save, so Archipelago can hand it out instead.
/// </summary>
/// <typeparam name="T">The game enum stored in the collection, such as TarotCards.Card.</typeparam>
/// <remarks>
/// Fleeces, follower forms, doctrines, structures and outfits are all this shape.
///
/// The gate problem: every route the game has to offer you something first checks you don't
/// already own it, so unlocking an Archipelago grant for real closes that gate and strands the
/// check riding on it. Grants live in <c>granted</c> and are never written to the game's
/// collection, and anything that needs to see them gets them lent back.
///
/// The symmetry problem: emptying the collection edits real save data, so everything taken is
/// put back on disconnect.
///
/// Where a system has a single reward method to intercept, as sermons do, prefer a Harmony
/// prefix there. This is for collections that can only be managed after the fact.
/// </remarks>
internal class ManagedCollection<T> where T : struct, Enum
{
    private readonly string collectionKey;
    private readonly IManagedBacking<T> backing;
    private readonly string legacyKey;
    private readonly string noun;

    // Every entry this seed owns, whether or not its check lives on the entry
    private readonly HashSet<T> managed;

    // What we took off the player, so it can be handed back. Only entries that were genuinely
    // unlocked before we touched them
    private readonly HashSet<T> revoked = new();

    // What Archipelago has handed over this session. The game's own collection never learns
    // about it. See the class summary
    private readonly HashSet<T> granted = new();

    // Which save `revoked` was taken from. The player can load a different save without
    // reconnecting, and entries owed to one save must never be written into another
    private int saveSlot;

    // Set whenever the debt changes, cleared once Tick has written it out. Batched because the
    // connect-time item replay grants dozens in a row, each otherwise rewriting the whole file
    private bool debtDirty;

    // noun is what one entry is called, for log lines the player reads.
    // legacyKey is explained on ManagedCollectionStore.Owed
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

    // What Archipelago has handed over. Lent to the places that need to see it
    internal IReadOnlyCollection<T> Granted => granted;

    internal bool IsManaged(T value) => managed.Contains(value);

    // Takes every managed entry out of the save and starts the session's bookkeeping. Co-op
    // cards and DLC entries in a non-DLC seed are left exactly as the player had them
    internal void Begin()
    {
        saveSlot = SaveSlot.Current;

        // Anything an earlier session took and never gave back is still owed. Folded in before
        // revoking so a session that ended in a crash doesn't cost the player anything.
        revoked.UnionWith(ManagedCollectionStore.Owed<T>(collectionKey, saveSlot, legacyKey));

        RevokeManaged();
    }

    // The caller queues this onto the main thread: teardown arrives on the websocket thread and
    // the game's collections are plain Lists the main thread iterates
    internal void End()
    {
        Restore();
    }

    // Into our own set rather than the game's collection, so the game keeps offering the entry
    // and the check riding on it stays reachable. Replays safely, since it is a set Add
    internal void Grant(T value)
    {
        if (granted.Add(value))
        {
            debtDirty = true;
        }
    }

    // Keeps the invariant true: no entry this seed manages is ever in the game's collection.
    // Begin establishes it once, but GameManager.Awake re-seeds fifteen default tarot cards
    // whenever the collection is empty (GameManager.cs:175), which is exactly the state we
    // leave a fresh save in. A sweep rather than a hook on Awake or OnLoadComplete, which are
    // ordering-sensitive and miss anything else that writes to the collection
    internal void Tick()
    {
        if (!backing.IsAvailable)
        {
            return;
        }

        if (SaveSlot.Current != saveSlot)
        {
            SwitchToLoadedSave();
            return;
        }

        List<T> reappeared = null;

        foreach (var value in managed)
        {
            if (!backing.Remove(value))
            {
                continue;
            }

            // Already accounted for. The game handed back something we'd taken, or something
            // Archipelago had granted. Owed either way, just not newly.
            if (revoked.Contains(value) || granted.Contains(value))
            {
                continue;
            } (reappeared ??= new List<T>()).Add(value);
        }

        if (reappeared != null)
        {
            revoked.UnionWith(reappeared);
            debtDirty = true;
            Log.LogInfo($"[AP] The game put {reappeared.Count} managed {noun}(s) back into the "
                + "collection - taken out again so their checks stay reachable.");
        }

        if (debtDirty)
        {
            PersistDebt();
        }
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
            if (!backing.Remove(value))
            {
                continue;
            }

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

    // What we took off the save, and what Archipelago granted. Grants are included because they
    // only ever live in memory, and quitting to the desktop runs no teardown
    private void PersistDebt()
    {
        var owed = new HashSet<T>(revoked);
        owed.UnionWith(granted);

        ManagedCollectionStore.Owe(collectionKey, saveSlot, owed);
        debtDirty = false;
    }

    // A different save is loaded than the one we took from. That save is no longer in memory,
    // so its debt goes to the store and the new one gets the same treatment at connect
    private void SwitchToLoadedSave()
    {
        Log.LogInfo($"[AP] Save slot changed ({saveSlot} -> {SaveSlot.Current}). "
            + $"{revoked.Count} {noun}(s) stay owed to the old save.");

        // Only what we took off the old save. Archipelago's grants were never in it.
        ManagedCollectionStore.Owe(collectionKey, saveSlot, revoked);
        revoked.Clear();

        // `granted` deliberately survives. It's connection state, and the item replay that would
        // rebuild it runs only on connect, not on a save load. That's also why the seed's
        // starting entries aren't re-granted here, because they're still in it.
        saveSlot = SaveSlot.Current;
        revoked.UnionWith(ManagedCollectionStore.Owed<T>(collectionKey, saveSlot, legacyKey));
        RevokeManaged();
    }

    // Both the entries taken at connect and the ones Archipelago granted. Grants must be
    // included or disconnecting silently takes away everything the multiworld handed over
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
        // is. Writing into it would be worse than waiting, because the store keeps the debt and
        // the next connect on the right save pays it.
        if (!backing.IsAvailable || SaveSlot.Current != saveSlot)
        {
            ManagedCollectionStore.Owe(collectionKey, saveSlot, owed);
            Log.LogWarning($"[AP] Save {saveSlot} isn't loaded, so its {owed.Count} {noun}(s) "
                + "couldn't be returned yet. They're recorded, and come back the next time you "
                + "connect on that save.");
            return;
        }

        foreach (var value in owed)
        {
            backing.Add(value);
        }

        // Only once they're actually back in the collection.
        ManagedCollectionStore.Settle(collectionKey, saveSlot, legacyKey);

        Log.LogInfo($"[AP] Returned {owed.Count} {noun}(s) to the save.");
    }

    // Pays back whatever the loaded save is owed, for the player who crashes and then
    // uninstalls. Runs from the plugin's poll while *disconnected*, so it fires only on the
    // interrupted paths. Static because no session, and so no collection instance, exists then
    internal static void SettleIfOwed(
        string collectionKey,
        IManagedBacking<T> backing,
        string noun,
        string legacyKey = null)
    {
        if (!backing.IsAvailable)
        {
            return;
        }

        var saveSlot = SaveSlot.Current;
        var owed = ManagedCollectionStore.Owed<T>(collectionKey, saveSlot, legacyKey);
        if (owed.Count == 0)
        {
            return;
        }

        foreach (var value in owed)
        {
            backing.Add(value);
        }

        ManagedCollectionStore.Settle(collectionKey, saveSlot, legacyKey);
        Log.LogInfo($"[AP] Returned {owed.Count} {noun}(s) an interrupted session still owed "
            + "this save.");
    }
}
