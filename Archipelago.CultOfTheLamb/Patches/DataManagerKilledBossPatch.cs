using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Catches miniboss and Witness kills. DataManager.AddKilledBoss is the only place anything is
/// added to KilledBosses in the whole assembly, so this one patch covers every miniboss, every
/// Witness and their post-game "_P2" variants (DcplIdx 3a).
///
/// Separate from InteractionMonsterHeartPatch, which covers the four Bishops. Those are
/// FollowerLocation values in BossesCompleted rather than strings in KilledBosses, so the two
/// lists never overlap.
///
/// Every caller guards on !DungeonSandboxManager.Active, so Endless-mode kills never reach here
/// and correctly send nothing without this patch testing for it.
/// </summary>
[HarmonyPatch(typeof(DataManager))]
internal static class DataManagerKilledBossPatch
{
    // Fires with the internal boss key (e.g. "Boss Mama Worm", "Boss Beholder 1",
    // "Boss Beholder 1_P2"), and only for a newly recorded kill
    internal static event Action<string> OnBossKillRecorded;

    // AddKilledBoss dedups internally, so it's called on every re-kill but only mutates the
    // list the first time. Capture "was this new?" in the prefix so subscribers see genuine
    // first kills rather than re-runs of already-cleared content.
    [HarmonyPatch(nameof(DataManager.AddKilledBoss))]
    [HarmonyPrefix]
    private static void AddKilledBoss_Prefix(DataManager __instance, string BossSkin, out bool __state)
    {
        // False on a failure, which only costs the check. This sits in front of the game's own
        // kill bookkeeping, so throwing would mean the kill is never recorded at all.
        __state = false;
        try
        {
            __state = __instance?.KilledBosses != null && !__instance.KilledBosses.Contains(BossSkin);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't tell whether \"{BossSkin}\" was a first kill, so no "
                + $"check will be sent for it: {e}");
        }
    }

    [HarmonyPatch(nameof(DataManager.AddKilledBoss))]
    [HarmonyPostfix]
    private static void AddKilledBoss_Postfix(string BossSkin, bool __state)
    {
        if (!__state)
        {
            return;
        }

        Log.LogInfo($"[AP] Boss kill recorded: \"{BossSkin}\"");

        // Subscribers send the check, and this runs inside the boss death. A send that throws
        // shouldn't leave the fight unresolved.
        try
        {
            OnBossKillRecorded?.Invoke(BossSkin);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't report the kill of \"{BossSkin}\": {e}");
        }
    }
}
