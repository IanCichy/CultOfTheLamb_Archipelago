using System;
using System.Collections;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Turns "the sermon bar filled" into an Archipelago check instead of an upgrade you pick.
///
/// SermonController.PlayerUpgrade() is the whole reward step: it grants a Disciple Point,
/// opens the player upgrade tree, waits for a choice, then increments
/// DataManager.Doctrine_PlayerUpgrade_Level. Replacing it wholesale is what decouples
/// "earned an upgrade" (the location) from "received an upgrade" (the item) - which is the
/// core of randomizing this system at all.
///
/// Suppressing the pick can't hang the sermon: PlayerUpgrade() is invoked once per bar fill
/// and the caller resets xp to 0 immediately after (SermonController.cs:97-99), rather than
/// looping until an unlock is consumed. UpgradePlayerConfiguration.HasUnlockAvailable(true)
/// staying true just means sermons keep paying out, which is exactly what we want - the
/// vanilla game would otherwise start handing out blue hearts once the tree is exhausted.
///
/// The level counter doubles as our location index: it's incremented once per fill and
/// persisted in the save, so it survives reconnects and stays correct even if the player
/// gave sermons while disconnected.
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
