using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Raises an event when a boss is defeated.
/// </summary>
/// <remarks>
/// Interaction_MonsterHeart is the real "boss defeated" completion hook (DcplIdx 3). It fires a
/// public OnHeartTaken event right after the game records the kill
/// (DataManager.Instance.BossesCompleted.Add(...)), so we subscribe per-instance rather than
/// patching the coroutine that raises it, which has no clean method boundary to postfix.
/// </remarks>
[HarmonyPatch(typeof(Interaction_MonsterHeart))]
internal static class InteractionMonsterHeartPatch
{
    // Fires with the FollowerLocation (region/dungeon slot) the kill happened in
    internal static event Action<FollowerLocation> OnBossDefeated;

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start_Postfix(Interaction_MonsterHeart __instance)
    {
        __instance.OnHeartTaken += () => OnBossDefeated?.Invoke(PlayerFarming.Location);
    }
}
