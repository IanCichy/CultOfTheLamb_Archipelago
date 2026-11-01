using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Stops the tutorial from locking the base door behind a ritual the player cannot perform yet.
///
/// While the BonesAndRitual objective group exists, Onboarding tops the player up to the
/// Bonfire's bone cost and then locks the door with "Perform any Ritual"
/// (Onboarding.cs:102-130). That assumes holding the bones means owning the Bonfire, which is
/// true in vanilla but not here: a Ritual Bundle can arrive from the multiworld first, while the
/// player still has to leave the base to find the first Commandment Stone.
/// </summary>
[HarmonyPatch(typeof(BaseGoopDoor), nameof(BaseGoopDoor.BlockGoopDoor))]
internal static class RitualDoorLockPatch
{
    private const string RitualLockLabel = "Objectives/Custom/PerformAnyRitual";

    private static bool logged;

    // A proxy for "any ritual is unlocked", reading these two unlocks directly. The game's own
    // SermonsAndRituals.CheckUnlocked cannot answer this: it reads UnlockedSermonsAndRituals,
    // whose enum has no fire-pit or brainwashing member.
    private static bool CanPerformARitual =>
        UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Ritual_FirePit)
        || UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Ritual_Brainwashing);

    // True on a throw locks the door as vanilla would, which is the lock this patch avoids. Still
    // better than throwing out of BlockGoopDoor and breaking every caller, including load.
    [HarmonyPrefix]
    private static bool Prefix(string loc)
    {
        try
        {
            return Decide(loc);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't tell whether the base door lock is the tutorial's, "
                + $"so the game's own locking stands: {e}");
            return true;
        }
    }

    private static bool Decide(string loc)
    {
        var data = DataManager.Instance;
        if (data == null || CanPerformARitual)
        {
            return true;
        }

        // Onboarding.Start asks for the lock by name. BaseGoopDoor.Start re-applies a saved one
        // with an empty label.
        var requested = loc == RitualLockLabel;
        var restored = string.IsNullOrEmpty(loc)
            && data.BaseGoopDoorLocked
            && data.BaseGoopDoorLoc == RitualLockLabel;

        if (!requested && !restored)
        {
            return true;
        }

        if (!logged)
        {
            logged = true;
            Log.LogInfo("[AP] Kept the base door open: the tutorial asked for a ritual before any "
                + "ritual is unlocked. This is logged once per session.");
        }

        // The lock is saved, so a save that already stuck needs it cleared as well
        if (data.BaseGoopDoorLocked)
        {
            data.BaseGoopDoorLoc = "";
            BaseGoopDoor.UnblockGoopDoor();
        }

        return false;
    }
}
