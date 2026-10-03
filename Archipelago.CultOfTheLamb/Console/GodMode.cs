#if AP_DEBUG_KEYS
using HarmonyLib;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Console;

/// <summary>
/// Developer god mode. No damage taken, and a multiple of the usual damage dealt.
/// </summary>
/// <remarks>
/// Invulnerability is the game's own Health.GodMode field, which lives on the player's Health
/// component and never reaches the save.
/// </remarks>
internal static class GodMode
{
    private const float DamageMultiplier = 5f;

    private static bool active;

    internal static void Toggle()
    {
        active = !active;
        Apply();

        Log.LogInfo($"[AP] Debug: god mode {(active ? "ON" : "OFF")} - no damage taken, "
            + $"{DamageMultiplier}x damage dealt.");

        ApNotification.Show(
            active
                ? $"Archipelago: god mode ON ({DamageMultiplier}x damage)"
                : "Archipelago: god mode OFF",
            active ? NotificationBase.Flair.Positive : NotificationBase.Flair.None);
    }

    // Every frame from ArchipelagoPlugin.Update: the player's Health component is rebuilt entering
    // a crusade and returning to base, so setting the flag once wears off at the next load.
    internal static void Tick()
    {
        if (active)
        {
            Apply();
        }
    }

    private static void Apply()
    {
        var player = PlayerFarming.Instance;
        if (player == null || player.health == null)
        {
            return;
        }

        player.health.GodMode = active ? Health.CheatMode.God : Health.CheatMode.None;
    }

    [HarmonyPatch(typeof(Health), nameof(Health.DealDamage))]
    internal static class OutgoingDamage
    {
        [HarmonyPrefix]
        private static void Prefix(Health __instance, ref float Damage, GameObject Attacker)
        {
            // isPlayer skips our own health, which the GodMode flag already covers
            if (!active || Damage <= 0f || Attacker == null || __instance.isPlayer)
            {
                return;
            }

            if (DealtByPlayer(Attacker))
            {
                Damage *= DamageMultiplier;
            }
        }

        // Spells and projectiles name themselves as the attacker, so the owner is resolved the way
        // DealDamage does it (Health.cs:618-626)
        private static bool DealtByPlayer(GameObject attacker)
        {
            if (attacker.GetComponent<PlayerFarming>() != null)
            {
                return true;
            }

            var owner = Health.GetSpellOwner(attacker);
            return owner != null && owner.GetComponent<PlayerFarming>() != null;
        }
    }
}
#endif
