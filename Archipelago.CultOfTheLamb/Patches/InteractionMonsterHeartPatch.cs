using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Raises an event when a Bishop's heart is taken, which is the real "boss defeated" signal
/// (DcplIdx 3).
///
/// The heart exposes a public OnHeartTaken event, raised on the line immediately after the game
/// records the kill with DataManager.BossesCompleted.Add. Subscribing per instance in Start is
/// cheaper than patching the method that raises it, because that method is a coroutine and has
/// no clean boundary to postfix.
/// </summary>
[HarmonyPatch(typeof(Interaction_MonsterHeart))]
internal static class InteractionMonsterHeartPatch
{
    // Fires with the FollowerLocation the kill happened in, which is how the region is identified
    internal static event Action<FollowerLocation> OnBossDefeated;

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start_Postfix(Interaction_MonsterHeart __instance)
    {
        // Failing to subscribe costs this one heart its check rather than the scene it is
        // spawning into, so it is caught separately from the handler below.
        try
        {
            __instance.OnHeartTaken += ReportDefeat;
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't hook a boss heart, so that kill won't send its "
                + $"check: {e}");
        }
    }

    private static void ReportDefeat()
    {
        // Runs inside the heart's own coroutine. The reward is already handed over by this
        // point, so a throw would only stop the heart destroying itself.
        try
        {
            OnBossDefeated?.Invoke(PlayerFarming.Location);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] A boss was defeated but reporting it failed: {e}");
        }
    }
}
