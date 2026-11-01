using System;
using System.Collections;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Turns "the sermon bar filled" into an Archipelago check instead of an upgrade you pick.
///
/// SermonController.PlayerUpgrade() is the whole reward step: Disciple Point, tree menu, choice,
/// then the level increment. Replacing it wholesale decouples earning an upgrade from receiving
/// one, and cannot hang the sermon, because every caller clears xp around the call
/// (SermonController.cs:97-99, :175-176, :370-373) rather than looping until an unlock is spent.
///
/// The level counter doubles as the location index. It is incremented once per fill and
/// persisted, so it survives reconnects and sermons given while disconnected.
/// </summary>
[HarmonyPatch(typeof(SermonController))]
internal static class SermonUpgradePatch
{
    // Most sermon XP one Temple upgrade may need, in tenths, which is the unit the game's own
    // bar counts in. 0 leaves the curve alone
    internal static int XpCapTenths;

    /// <summary>
    /// Caps what the next Temple upgrade costs.
    ///
    /// PlayerUpgradeTargetXP climbs 0.3, 0.4, 1.1 and on to 10.0 across 14 entries, then clamps,
    /// so the last 24 of the 38 upgrades all sit at the ceiling: about 298 XP in total, or
    /// roughly 150 sermons at a 20-strong flock, and a sermon is a once-a-day ritual.
    ///
    /// Filtered to PlayerUpgrade. The other eight sermon categories share this method, five of
    /// them on a far steeper curve, and this world randomizes none of them yet.
    /// </summary>
    [HarmonyPatch(typeof(DoctrineUpgradeSystem),
        nameof(DoctrineUpgradeSystem.GetXPTargetBySermon))]
    internal static class SermonXpTarget
    {
        [HarmonyPostfix]
        private static void Postfix(SermonCategory sermonCategory, ref float __result)
        {
            if (XpCapTenths <= 0 || sermonCategory != SermonCategory.PlayerUpgrade)
            {
                return;
            }

            __result = UnityEngine.Mathf.Min(__result, XpCapTenths / 10f);
        }
    }

    // Set by SermonService while a session is randomizing sermons. When false the vanilla
    // pick-an-upgrade flow runs untouched, so a disconnected or non-sermon seed plays normally
    internal static bool Active { get; set; }

    // Fires with the 1-based index of the sermon upgrade just earned
    internal static event Action<int> OnSermonUpgradeEarned;

    [HarmonyPatch(nameof(SermonController.PlayerUpgrade))]
    [HarmonyPrefix]
    private static bool PlayerUpgrade_Prefix(ref IEnumerator __result)
    {
        try
        {
            if (!Active)
            {
                return true;
            }

            // False skips the original, so no tree menu, no Disciple Point and no pick.
            __result = SkipUpgradeChoice();
            return false;
        }
        catch (Exception e)
        {
            // True gives them an upgrade the multiworld didn't send, which beats false replacing
            // the sermon reward with nothing.
            Log.LogWarning($"[AP] Couldn't suppress the sermon upgrade choice, so the vanilla "
                + $"screen runs: {e}");
            return true;
        }
    }

    private static IEnumerator SkipUpgradeChoice()
    {
        // This runs as the game's own coroutine, where a throw would abandon the sermon partway
        // through instead of surfacing anywhere useful. Swallowing it costs one check.
        try
        {
            RecordUpgrade();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Sermon upgrade earned but recording it failed, so no check "
                + $"was sent: {e}");
        }

        yield break;
    }

    private static void RecordUpgrade()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            Log.LogWarning("[AP] Sermon upgrade earned but DataManager is null - check not sent.");
            return;
        }

        // Mirrors SermonController.cs:449. XP targets are indexed by this level, so letting it
        // drift would desync the sermon economy even though the UI was skipped.
        dataManager.Doctrine_PlayerUpgrade_Level++;
        var level = dataManager.Doctrine_PlayerUpgrade_Level;

        Log.LogInfo($"[AP] Sermon upgrade #{level} earned (choice screen suppressed).");
        OnSermonUpgradeEarned?.Invoke(level);
    }
}
