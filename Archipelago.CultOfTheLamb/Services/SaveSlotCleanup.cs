using System;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Drops our per-save records when the game deletes a save.
/// </summary>
/// <remarks>
/// Without this, a new save made in the same slot inherits the old one's Archipelago badge and
/// applied-item count.
/// </remarks>
internal static class SaveSlotCleanup
{
    private static bool subscribed;

    internal static void Register()
    {
        if (subscribed)
        {
            return;
        }

        SaveAndLoad.OnSaveSlotDeleted += OnDeleted;
        subscribed = true;
    }

    private static void OnDeleted(int saveSlot)
    {
        try
        {
            // Only clean up once the whole row is empty. Activating Woolhaven snapshots the save
            // to slot+10 (SaveAndLoad.MakeBaseGameBackUpSave), and falling back to that snapshot
            // migrates it down and deletes the +10 slot (SaveAndLoad.cs:183-196). Our keys fold
            // +10 into the base slot, so acting on that delete would forget the save just written.
            var slot = saveSlot >= 10 ? saveSlot - 10 : saveSlot;
            if (SaveAndLoad.SaveExist(slot) || SaveAndLoad.SaveExist(slot + 10))
            {
                return;
            }

            AppliedItemStore.ForgetSlot(slot);
            ManagedCollectionStore.Settle(
                TarotCollectionBacking.Key, slot, TarotCollectionBacking.LegacyKey);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not clear save slot {saveSlot}'s records: {e.Message}");
        }
    }
}
