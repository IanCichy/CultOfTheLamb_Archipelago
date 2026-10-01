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
            AppliedItemStore.ForgetSlot(saveSlot);

            // Woolhaven saves use slot+10, folded here the way SaveSlot.Current does
            var slot = saveSlot >= 10 ? saveSlot - 10 : saveSlot;
            ManagedCollectionStore.Settle(
                TarotCollectionBacking.Key, slot, TarotCollectionBacking.LegacyKey);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not clear save slot {saveSlot}'s records: {e.Message}");
        }
    }
}
