using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

// Stop Steam Achievements from unlocking if a player doesn't want to get them
// for essentially free. The game itself has no way to turn them off, so this patch is the only way to do it.
internal static class AchievementSuppression
{
    internal static bool BlockAchievements { get; private set; }

    // Set while a session's own setting applies. Without one, a save Archipelago has played is
    // held back anyway: the game re-checks every achievement on each scene load
    // (GameManager.cs:262, :280), so loading in before connecting would unlock them.
    private static bool sessionDecided;

    // Unlock attempts blocked this session, shown in the F9 dump. The game re-asks for every
    // achievement it already counts as earned on each scene load, so this climbs steadily and
    // only the first block is logged.
    internal static int HeldBack { get; private set; }

    internal static void Apply(bool allowAchievements)
    {
        BlockAchievements = !allowAchievements;
        sessionDecided = true;
        HeldBack = 0;

        Log.LogInfo(BlockAchievements
            ? "[AP] Achievements are held back for this seed. Set allow_achievements in your "
                + "YAML if you want them."
            : "[AP] Achievements are enabled for this seed.");
    }

    internal static void Reset()
    {
        BlockAchievements = false;
        sessionDecided = false;
    }

    internal static bool ShouldHold() =>
        sessionDecided ? BlockAchievements : AppliedItemStore.HasHistoryFor(SaveSlot.Current);

    // Every achievement should pass through here
    [HarmonyPatch(typeof(AchievementsWrapper), nameof(AchievementsWrapper.UnlockAchievement))]
    internal static class AchievementsWrapperUnlockPatch
    {
        // No parameters on purpose: naming the game's Achievement type would tie this to the
        // assembly we compile against and break if the game changes it.
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try
            {
                if (!ShouldHold())
                {
                    return true;
                }

                if (HeldBack++ == 0)
                {
                    Log.LogInfo("[AP] Held back an achievement. Later ones this session are held quietly, F9 shows the count.");
                }

                return false;
            }
            catch (Exception e)
            {
                Log.LogWarning($"[AP] Couldn't tell whether to hold back an achievement, so the "
                    + $"game's own behaviour stands: {e.Message}");
                return true;
            }
        }
    }
}
