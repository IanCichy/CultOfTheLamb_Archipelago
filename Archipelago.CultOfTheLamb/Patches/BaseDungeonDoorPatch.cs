using System;
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
        // A throw here would propagate into the door's own enable path and leave it half set up.
        // Note the fallback is weak: a failed re-lock leaves Unlocked true, and OpenDoor then
        // opens the physical collider, so the OnInteract guard below never runs.
        try
        {
            ReLock(__instance);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't re-lock a region door, so a stale save entry may let "
                + $"the player walk through: {e}");
        }
    }

    private static void ReLock(Interaction_BaseDungeonDoor door)
    {
        if (!RegionLockState.IsLockedByArchipelago(door.Location))
        {
            return;
        }

        if (DataManager.Instance == null)
        {
            return;
        }

        if (DataManager.Instance.UnlockedDungeonDoor.Remove(door.Location))
        {
            Log.LogInfo($"[AP] Re-locked {door.Location} (not yet unlocked by Archipelago)");
        }
    }

    // Returns false to skip the original, so the open ritual never starts.
    [HarmonyPatch("OnInteract")]
    [HarmonyPrefix]
    private static bool OnInteract_Prefix(Interaction_BaseDungeonDoor __instance)
    {
        try
        {
            return Allow(__instance);
        }
        catch (Exception e)
        {
            // True can open a region Archipelago still has locked, which is a logic hole. False
            // would make the door permanently uninteractable, which is worse.
            Log.LogWarning($"[AP] Couldn't tell whether a region door is locked, so the vanilla "
                + $"interaction stands: {e}");
            return true;
        }
    }

    private static bool Allow(Interaction_BaseDungeonDoor __instance)
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

    // The default label shows the follower cost, which makes a sealed door look like it just
    // needs more followers. A door with SacrificeFollowerLevel set reads differently again.
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
