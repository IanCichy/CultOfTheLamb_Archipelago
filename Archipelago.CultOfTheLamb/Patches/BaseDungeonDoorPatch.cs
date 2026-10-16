using HarmonyLib;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// Holds AP-locked region doors shut. 
[HarmonyPatch(typeof(Interaction_BaseDungeonDoor))]
internal static class BaseDungeonDoorPatch
{
    private const string RegionKeyItem = "Progressive Bishop's Domain";
    private const float ExplanationCooldownSeconds = 8f;
    private static float nextExplanation;

    // Strips AP-locked regions out of the save set before the door reads it
    [HarmonyPatch("OnEnableInteraction")]
    [HarmonyPrefix]
    private static void OnEnableInteraction_Prefix(Interaction_BaseDungeonDoor __instance)
    {
        if (!RegionLockState.IsLockedByArchipelago(__instance.Location))
        {
            return;
        }

        if (DataManager.Instance == null)
        {
            return;
        }

        if (DataManager.Instance.UnlockedDungeonDoor.Remove(__instance.Location))
        {
            Log.LogInfo($"[AP] Re-locked {__instance.Location} (not yet unlocked by Archipelago)");
        }
    }

    // Returns false to skip the original, so the open ritual never starts.
    [HarmonyPatch("OnInteract")]
    [HarmonyPrefix]
    private static bool OnInteract_Prefix(Interaction_BaseDungeonDoor __instance)
    {
        if (!RegionLockState.IsLockedByArchipelago(__instance.Location))
        {
            return true;
        }

        Log.LogInfo($"[AP] Blocked opening {__instance.Location} - locked by Archipelago.");

        // Replicate the default game cue for an unmet requirement
        AudioManager.Instance?.PlayOneShot("event:/ui/negative_feedback", __instance.transform.position);

        // Throttled since the interact button can be mashed
        if (Time.unscaledTime >= nextExplanation)
        {
            nextExplanation = Time.unscaledTime + ExplanationCooldownSeconds;
            ApNotification.Show(
                "The AP Gods have sealed this door. Await a "
                + $"{RegionKeyItem} from the multiworld.",
                NotificationBase.Flair.None, ApColors.Yellow);
        }

        return false;
    }

    // The default label shows the follower cost and makes a sealed door look like it just needs
    // more followers. Postfix to show our text.
    [HarmonyPatch(nameof(Interaction_BaseDungeonDoor.GetLabel))]
    [HarmonyPostfix]
    private static void GetLabel_Postfix(Interaction_BaseDungeonDoor __instance)
    {
        if (!RegionLockState.IsLockedByArchipelago(__instance.Location))
        {
            return;
        }

        __instance.Label = $"Sealed by Archipelago | needs {RegionKeyItem}";
    }
}
