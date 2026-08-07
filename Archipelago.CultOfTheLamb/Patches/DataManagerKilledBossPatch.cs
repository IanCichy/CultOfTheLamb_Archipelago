using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Catches miniboss and Witness kills. DataManager.AddKilledBoss(string) is the only write site
/// for KilledBosses in the whole assembly, so one patch covers all 12 minibosses, all 4
/// Witnesses and their post-game "_P2" variants (AI_INDEX.md §3a).
///
/// Separate from InteractionMonsterHeartPatch, which covers the four Bishops - those are
/// FollowerLocation values in BossesCompleted, and the two never overlap.
///
/// The game suppresses AddKilledBoss while DungeonSandboxManager.Active, so Endless-mode kills
/// correctly send nothing without us checking for it.
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
