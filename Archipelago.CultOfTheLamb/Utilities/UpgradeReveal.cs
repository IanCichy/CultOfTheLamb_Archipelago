using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Plays the unlock-reveal for upgrades Archipelago granted, exactly once each.
/// </summary>
/// <remarks>
/// The reveal is also where the game applies an upgrade's effect, such as an extra heart. The
/// game's own routine replays its whole pending list each time it starts, so granting several
/// upgrades at once applied the early ones repeatedly.
///
/// Persisted per save, because a lost reveal is a lost effect: UnlockAbility has already recorded
/// the upgrade by the time we claim it, and PLAYER_HEARTS_LEVEL can't be re-derived since the
/// Hearts of the Faithful ritual increments the same field (RitualHeartsOfTheFaithful.cs:131).
/// </remarks>
internal static class UpgradeReveal
{
    // Namespaces this save's pending reveals in the shared store
    private const string Collection = "reveals";

    private static readonly List<UpgradeSystem.Type> Claimed = new();

    // Which save Claimed belongs to, so loading another one can't replay its reveals
    private static int loadedForSlot = -1;

    /// <summary>
    /// Takes over the reveal for an upgrade we just granted.
    /// </summary>
    /// <remarks>
    /// Removed from the game's pending list so its routine can't play it a second time the next
    /// time something else starts it.
    /// </remarks>
    internal static void Claim(UpgradeSystem.Type upgrade)
    {
        SyncToLoadedSave();
        UpgradeSystem.UnlocksToReveal?.Remove(upgrade);

        if (Claimed.Contains(upgrade))
        {
            return;
        }

        Claimed.Add(upgrade);
        Persist();
    }

    /// <summary>
    /// Reveals one claimed upgrade per call. ProcessQueue calls it every frame.
    /// </summary>
    internal static void FlushClaimed()
    {
        SyncToLoadedSave();

        if (Claimed.Count == 0)
        {
            return;
        }

        var manager = GameManager.GetInstance();

        // The same lookup the Heart tiers make unguarded (UpgradeSystem.cs:918), so it has to be
        // this and not PlayerFarming.Instance, which survives the player object being deactivated.
        if (manager == null || UnityEngine.Object.FindObjectOfType<HealthPlayer>() == null)
        {
            return;
        }

        var upgrade = Claimed[0];
        Claimed.RemoveAt(0);

        // Dropped from the record before the coroutine starts, so a reveal that throws is lost
        // rather than retried on every load from here on.
        Persist();
        manager.StartCoroutine(UpgradeSystem.OnUnlockAbility(upgrade));
    }

    /// <summary>
    /// Drops a deleted save's pending reveals.
    /// </summary>
    internal static void ForgetSlot(int slot)
    {
        ManagedCollectionStore.Settle(Collection, slot);

        if (slot == loadedForSlot)
        {
            Claimed.Clear();
        }
    }

    // Reloads the pending list whenever the loaded save changes. That is what lets a quit mid-drain
    // resume the next time that save loads, and what keeps the leftovers off a different save.
    private static void SyncToLoadedSave()
    {
        var slot = SaveSlot.Current;
        if (slot == loadedForSlot)
        {
            return;
        }

        loadedForSlot = slot;
        Claimed.Clear();
        Claimed.AddRange(ManagedCollectionStore.Owed<UpgradeSystem.Type>(Collection, slot));

        if (Claimed.Count > 0)
        {
            Log.LogInfo($"[AP] Save {slot} has {Claimed.Count} upgrade reveal(s) owed from an "
                + "earlier session. Applying them now.");
        }
    }

    private static void Persist()
    {
        if (Claimed.Count == 0)
        {
            ManagedCollectionStore.Settle(Collection, loadedForSlot);
            return;
        }

        ManagedCollectionStore.Owe(Collection, loadedForSlot, Claimed);
    }
}
