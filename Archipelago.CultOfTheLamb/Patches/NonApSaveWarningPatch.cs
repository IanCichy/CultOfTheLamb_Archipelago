using System;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.MainMenu;

namespace Archipelago.CultOfTheLamb.Patches;

// Asks before loading a save that has never used AP
[HarmonyPatch(typeof(LoadMenu), "OnTryLoadSaveSlot")]
internal static class NonApSaveWarningPatch
{
    // Lets the re-entry from OnConfirm through once
    private static int confirmedSlot = -1;

    [HarmonyPrefix]
    private static bool Prefix(LoadMenu __instance, int slotIndex)
    {
        if (confirmedSlot == slotIndex)
        {
            confirmedSlot = -1;
            return true;
        }

        try
        {
            if (!SaveAndLoad.SaveExist(slotIndex) || AppliedItemStore.HasHistoryFor(slotIndex))
            {
                return true;
            }

            var window = __instance.Push(MonoSingleton<UIManager>.Instance.ConfirmationWindowTemplate);
            window.Configure(
                "***WARNING***",
                "Archipelago is meant to be played on a dedicated AP / new save. "
                    + "Connect to this non-AP save at your own risk. Load it anyway?");
            window.OnConfirm += () =>
            {
                confirmedSlot = slotIndex;
                AccessTools.Method(typeof(LoadMenu), "OnTryLoadSaveSlot")
                    .Invoke(__instance, new object[] { slotIndex });
            };

            Log.LogInfo($"[AP] Save slot {slotIndex} has no Archipelago history, showing warning.");
            return false;
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not show the non-Archipelago save warning, loading anyway: {e.Message}");
            return true;
        }
    }
}
