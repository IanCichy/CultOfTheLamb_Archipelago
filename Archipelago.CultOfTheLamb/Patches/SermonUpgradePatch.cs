using System;
using System.Collections;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Turns "the sermon bar filled" into an Archipelago check instead of an upgrade you pick.
///
/// SermonController.PlayerUpgrade() is the whole reward step - Disciple Point, tree menu, choice,
/// then the level increment - so replacing it wholesale is what decouples earning an upgrade from
/// receiving one.
///
/// Suppressing the pick can't hang the sermon: the caller resets xp to 0 immediately after
/// (SermonController.cs:97-99) rather than looping until an unlock is consumed.
///
/// The level counter doubles as the location index - incremented once per fill and persisted, so
/// it survives reconnects and sermons given while disconnected.
/// </summary>
[HarmonyPatch(typeof(SermonController))]
internal static class SermonUpgradePatch
{
    /// <summary>
    /// Set by SermonService while a session is randomizing sermons. When false the vanilla
    /// pick-an-upgrade flow runs untouched, so a disconnected or non-sermon seed plays normally.
    /// </summary>
    internal static bool Active { get; set; }

    /// <summary>Fires with the 1-based index of the sermon upgrade just earned.</summary>
    internal static event Action<int> OnSermonUpgradeEarned;

    [HarmonyPatch(nameof(SermonController.PlayerUpgrade))]
    [HarmonyPrefix]
    private static bool PlayerUpgrade_Prefix(ref IEnumerator __result)
    {
        if (!Active) return true;

        __result = SkipUpgradeChoice();
        return false; // skip the original - no tree menu, no Disciple Point, no pick
    }

    private static IEnumerator SkipUpgradeChoice()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            Log.LogWarning("[AP] Sermon upgrade earned but DataManager is null - check not sent.");
            yield break;
        }

        // Mirror what the original does at the end of PlayerUpgrade(). The game reads this
        // elsewhere (XP targets are indexed by it), so letting it drift would slowly desync
        // the sermon economy even though we skipped the UI.
        dataManager.Doctrine_PlayerUpgrade_Level++;
        var level = dataManager.Doctrine_PlayerUpgrade_Level;

        Log.LogInfo($"[AP] Sermon upgrade #{level} earned (choice screen suppressed).");
        OnSermonUpgradeEarned?.Invoke(level);
        yield break;
    }
}
