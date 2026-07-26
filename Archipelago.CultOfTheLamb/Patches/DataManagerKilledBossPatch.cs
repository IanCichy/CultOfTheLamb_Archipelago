using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Catches miniboss and Witness kills. DataManager.AddKilledBoss(string) is the *only* write
/// site for DataManager.KilledBosses in the whole assembly, it's public, and it's a clean
/// method boundary - so one patch here covers all 12 minibosses, all 4 Witnesses, and their
/// post-game "_P2" variants uniformly, with the identifying string handed to us as the
/// argument. See DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md §3a.
///
/// This is a separate system from InteractionMonsterHeartPatch: that one covers the four
/// Bishops (tracked as FollowerLocation values in BossesCompleted), this one covers everything
/// else in a region (tracked as strings in KilledBosses). The two never overlap.
///
/// Note the game suppresses AddKilledBoss entirely while DungeonSandboxManager.Active
/// (Endless mode), so sandbox kills correctly send nothing without us checking for it.
/// </summary>
[HarmonyPatch(typeof(DataManager))]
internal static class DataManagerKilledBossPatch
{
    /// <summary>
    /// Fires with the internal boss key (e.g. "Boss Mama Worm", "Boss Beholder 1",
    /// "Boss Beholder 1_P2"), and only for a *newly* recorded kill.
    /// </summary>
    internal static event Action<string> OnBossKillRecorded;

    // AddKilledBoss dedups internally, so it's called on every re-kill but only mutates the
    // list the first time. Capture "was this new?" in the prefix so subscribers see genuine
    // first kills rather than re-runs of already-cleared content.
    [HarmonyPatch(nameof(DataManager.AddKilledBoss))]
    [HarmonyPrefix]
    private static void AddKilledBoss_Prefix(DataManager __instance, string BossSkin, out bool __state)
    {
        __state = __instance?.KilledBosses != null && !__instance.KilledBosses.Contains(BossSkin);
    }

    [HarmonyPatch(nameof(DataManager.AddKilledBoss))]
    [HarmonyPostfix]
    private static void AddKilledBoss_Postfix(string BossSkin, bool __state)
    {
        if (!__state) return;

        Log.LogInfo($"[AP] Boss kill recorded: \"{BossSkin}\"");
        OnBossKillRecorded?.Invoke(BossSkin);
    }
}
