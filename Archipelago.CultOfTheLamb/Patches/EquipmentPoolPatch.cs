using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Restricts what weapons and curses the game may offer, and reports what the player equipped.
///
/// The save data is never touched - the opposite of TarotService, because emptying WeaponPool
/// doesn't withhold anything: GetRandomWeaponInPool opens with a ladder that hands over the
/// first weapon you *don't* own on the first floor of every run, so a revoked weapon is
/// force-fed straight back. Leaving the pool alone also keeps three count-sensitive checks
/// honest (Interaction_Chest's second podium, BiomeGenerator's weapon room,
/// AccessibilitySettings' Force Weapon control), and means an established save works.
///
/// All four hooks are null unless a session sets them, so the game is untouched when
/// disconnected or when the options are off.
/// </summary>
internal static class EquipmentPoolPatch
{
    /// <summary>
    /// Given the weapon the game chose, the one it's allowed to hand over. Set by
    /// EquipmentPoolService while connected with weapon randomization on.
    /// </summary>
    internal static Func<EquipmentType, EquipmentType> SubstituteWeapon;

    /// <summary>The curse-side counterpart.</summary>
    internal static Func<EquipmentType, EquipmentType> SubstituteCurse;

    /// <summary>Called when a weapon is equipped, which is what sends its check.</summary>
    internal static Action<EquipmentType> WeaponEquipped;

    /// <summary>The curse-side counterpart.</summary>
    internal static Action<EquipmentType> CurseEquipped;

    /// <summary>
    /// Every weapon podium, chest and choice room in the game.
    ///
    /// A postfix rather than a prefix because the original carries a lot worth keeping -
    /// legendary gating, the Fervour lockout, ForcedStartingWeapon, the Cowboy fleece branch,
    /// the accessibility Force Weapon setting.
    /// </summary>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.GetRandomWeaponInPool))]
    internal static class WeaponSelection
    {
        [HarmonyPostfix]
        private static void Postfix(ref EquipmentType __result)
        {
            var substitute = SubstituteWeapon;
            if (substitute != null) __result = substitute(__result);
        }
    }

    /// <summary>The same three interactions, curse side.</summary>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.GetRandomCurseInPool))]
    internal static class CurseSelection
    {
        [HarmonyPostfix]
        private static void Postfix(ref EquipmentType __result)
        {
            var substitute = SubstituteCurse;
            if (substitute != null) __result = substitute(__result);
        }
    }

    /// <summary>
    /// The one site that indexes WeaponPool directly rather than going through
    /// GetRandomWeaponInPool (FoundItemPickUp.cs:118). The sprite has to be reassigned too,
    /// since GetWeapon sets it from TypeOfWeapon before returning.
    /// </summary>
    [HarmonyPatch(typeof(FoundItemPickUp), "GetWeapon")]
    internal static class FoundWeapon
    {
        [HarmonyPostfix]
        private static void Postfix(FoundItemPickUp __instance)
        {
            var substitute = SubstituteWeapon;
            if (substitute == null) return;

            var chosen = substitute(__instance.TypeOfWeapon);
            if (chosen == __instance.TypeOfWeapon) return;

            __instance.TypeOfWeapon = chosen;
            RefreshSprite(__instance, chosen);
        }
    }

    /// <summary>The curse-side counterpart (FoundItemPickUp.cs:148).</summary>
    [HarmonyPatch(typeof(FoundItemPickUp), "GetCurse")]
    internal static class FoundCurse
    {
        [HarmonyPostfix]
        private static void Postfix(FoundItemPickUp __instance)
        {
            var substitute = SubstituteCurse;
            if (substitute == null) return;

            var chosen = substitute(__instance.TypeOfCurse);
            if (chosen == __instance.TypeOfCurse) return;

            __instance.TypeOfCurse = chosen;
            RefreshSprite(__instance, chosen);
        }
    }

    /// <summary>
    /// Swallows a missing sprite rather than throwing - GetEquipmentData reads a
    /// ScriptableObject, and a wrong icon beats an exception inside a pickup's setup.
    /// </summary>
    private static void RefreshSprite(FoundItemPickUp pickup, EquipmentType type)
    {
        try
        {
            var sprite = EquipmentManager.GetEquipmentData(type)?.WorldSprite;
            if (sprite != null && pickup.itemSprite != null) pickup.itemSprite.sprite = sprite;
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't set the pickup sprite for {type}: {e.Message}");
        }
    }

    /// <summary>
    /// Equipping a weapon - the funnel every route ends at, including FoundItemPickUp.
    ///
    /// This sends the check rather than the four sites that add to WeaponPool, which are wrong
    /// twice over: they bypass DataManager.AddWeapon so OnWeaponUnlocked never fires for them,
    /// and on an established save the pool already holds every weapon, so "first added to the
    /// pool" can never happen again.
    /// </summary>
    [HarmonyPatch(typeof(PlayerWeapon), nameof(PlayerWeapon.SetWeapon))]
    internal static class WeaponEquip
    {
        [HarmonyPostfix]
        private static void Postfix(EquipmentType weaponType) => WeaponEquipped?.Invoke(weaponType);
    }

    /// <summary>The curse-side counterpart.</summary>
    [HarmonyPatch(typeof(PlayerSpells), nameof(PlayerSpells.SetSpell))]
    internal static class CurseEquip
    {
        [HarmonyPostfix]
        private static void Postfix(EquipmentType Spell) => CurseEquipped?.Invoke(Spell);
    }
}
