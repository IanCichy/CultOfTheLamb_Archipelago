using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Raises an event when Narinder is defeated.
/// </summary>
/// <remarks>
/// OnDie is where the game records the defeat (EnemyDeathCatBoss.cs:1140 sets DeathCatBeaten), and
/// it runs before the kill-or-spare choice, so it fires for both endings.
///
/// No clone guard needed: DeathCatClone is its own UnitObject subclass rather than a subclass of
/// this boss, so the fakes killed during the fight never reach here.
/// </remarks>
[HarmonyPatch(typeof(EnemyDeathCatBoss), nameof(EnemyDeathCatBoss.OnDie))]
internal static class EnemyDeathCatBossPatch
{
    internal static event Action OnNarinderDefeated;

    private static void Postfix()
    {
        OnNarinderDefeated?.Invoke();
    }
}
