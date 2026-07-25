using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Holds AP-locked region doors shut. Two patches are needed because the vanilla door has
/// two independent ways to become passable:
///
/// 1. OnEnableInteraction() sets Unlocked from DataManager.UnlockedDungeonDoor, and
///    OpenDoor() disables the blocking collider when Unlocked - so a stale/auto-added entry
///    would let the player simply walk through.
/// 2. OnInteract() starts the open ritual based only on HaveFollowers (the follower-count
///    requirement) - it never consults UnlockedDungeonDoor at all, so meeting the follower
///    cost opens an AP-locked region.
/// </summary>
[HarmonyPatch(typeof(Interaction_BaseDungeonDoor))]
internal static class BaseDungeonDoorPatch
{
    /// <summary>
    /// Strip AP-locked regions out of the save's unlocked-door set before the door reads it,
    /// so Unlocked evaluates false and the blocking collider stays on.
    /// </summary>
    [HarmonyPatch("OnEnableInteraction")]
    [HarmonyPrefix]
    private static void OnEnableInteraction_Prefix(Interaction_BaseDungeonDoor __instance)
    {
        if (!RegionLockState.IsLockedByArchipelago(__instance.Location)) return;
        if (DataManager.Instance == null) return;

        if (DataManager.Instance.UnlockedDungeonDoor.Remove(__instance.Location))
        {
            Log.LogInfo($"[AP] Re-locked {__instance.Location} (not yet unlocked by Archipelago)");
        }
    }

    /// <summary>Refuse the open-the-door interaction entirely while AP has it locked.</summary>
    [HarmonyPatch("OnInteract")]
    [HarmonyPrefix]
    private static bool OnInteract_Prefix(Interaction_BaseDungeonDoor __instance)
    {
        if (!RegionLockState.IsLockedByArchipelago(__instance.Location)) return true;

        Log.LogInfo($"[AP] Blocked opening {__instance.Location} - locked by Archipelago.");
        // Same negative-feedback cue vanilla uses when the follower requirement isn't met,
        // so it reads as "not yet" rather than as a broken interaction.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayOneShot("event:/ui/negative_feedback", __instance.transform.position);
        }
        return false; // skip the original - don't start the ritual
    }
}
