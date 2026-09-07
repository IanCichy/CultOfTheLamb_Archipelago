using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Raises an event when a boss is defeated.
/// </summary>
/// <remarks>
/// Interaction_MonsterHeart is the real "boss defeated" completion hook. See DcplIdx 3.
/// It fires a public OnHeartTaken event right
/// after the game records the kill (DataManager.Instance.BossesCompleted.Add(...)), so we
/// subscribe to that event per-instance instead of patching the coroutine that raises it, which
/// has no clean single method boundary to postfix against.
/// </remarks>
[HarmonyPatch(typeof(Interaction_MonsterHeart))]
internal static class InteractionMonsterHeartPatch
{
    /// <summary>Fires with the FollowerLocation (region/dungeon slot) the kill happened in.</summary>
    internal static event Action<FollowerLocation> OnBossDefeated;

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    private static void Start_Postfix(Interaction_MonsterHeart __instance)
    {
        __instance.OnHeartTaken += () => OnBossDefeated?.Invoke(PlayerFarming.Location);
    }
}
