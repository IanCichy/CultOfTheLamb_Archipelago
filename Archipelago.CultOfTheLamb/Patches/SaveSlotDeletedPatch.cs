using Archipelago.CultOfTheLamb.Services;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Drops our per-save records when the game deletes a save.
/// </summary>
/// <remarks>
/// Without this, a new save made in the same slot inherits the old one's Archipelago badge and
/// applied-item count.
/// </remarks>
[HarmonyPatch(typeof(SaveAndLoad), nameof(SaveAndLoad.DeleteSaveSlot))]
internal static class SaveSlotDeletedPatch
{
    private static void Postfix(int saveSlot)
    {
        AppliedItemStore.ForgetSlot(saveSlot);

        // Woolhaven saves use slot+10, folded here the way SaveSlot.Current does
        var slot = saveSlot >= 10 ? saveSlot - 10 : saveSlot;
        ManagedCollectionStore.Settle(
            TarotCollectionBacking.Key, slot, TarotCollectionBacking.LegacyKey);
    }
}
