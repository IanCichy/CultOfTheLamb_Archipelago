using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Raises an event when Narinder is defeated, which completes a Narinder-goal seed.
///
/// OnDie is the hook because it sets DeathCatBeaten unconditionally
/// (EnemyDeathCatBoss.cs:1140), so any defeat reaches here. The kill-or-spare choice comes later,
/// in DeathCatController.cs:1026-1034, so it cannot change whether this fires.
///
/// No clone guard is needed. DeathCatClone is a sibling UnitObject subclass rather than a
/// subclass of this boss, so the fakes killed during the fight never reach this patch.
/// </summary>
[HarmonyPatch(typeof(EnemyDeathCatBoss), nameof(EnemyDeathCatBoss.OnDie))]
internal static class EnemyDeathCatBossPatch
{
    internal static event Action OnNarinderDefeated;

    private static void Postfix()
    {
        // Raised from inside OnDie, which has already started the death sequence coroutine. A
        // throw would strand the fight part way through it.
        try
        {
            OnNarinderDefeated?.Invoke();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Narinder was defeated but reporting it failed: {e}");
        }
    }
}
