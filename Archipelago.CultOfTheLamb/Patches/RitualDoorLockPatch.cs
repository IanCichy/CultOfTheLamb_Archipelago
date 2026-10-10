using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Stops the tutorial from locking the base door behind a ritual the player can't perform yet.
/// </summary>
/// <remarks>
/// Onboarding.Start locks the door with "Perform any Ritual" once the player holds the Bonfire's
/// bone cost (Onboarding.cs:103-130). Vanilla bones don't drop until the Bonfire is unlocked, but
/// a Ritual Bundle arrives earlier, while the player still has to leave to find the first
/// Commandment Stone.
/// </remarks>
[HarmonyPatch(typeof(BaseGoopDoor), nameof(BaseGoopDoor.BlockGoopDoor))]
internal static class RitualDoorLockPatch
{
    private const string RitualLockLabel = "Objectives/Custom/PerformAnyRitual";

    private static bool logged;

    // The same test the altar uses to show its Rituals button
    private static bool CanPerformARitual =>
        UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Ritual_FirePit)
        || UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Ritual_Brainwashing);

    [HarmonyPrefix]
    private static bool Prefix(string loc)
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
