using System;
using HarmonyLib;
using Lamb.UI.Assets;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Owns the Divine Inspiration point economy. It reports when the player fills the Devotion
/// meter, optionally withholds the point it would have paid, and can cap how much Devotion a
/// point costs.
/// </summary>
/// <remarks>
/// Patched at <c>UpgradeSystem.AbilityPoints</c>'s setter rather than PlayerFarming's
/// <c>++</c> (`PlayerFarming.cs:2024`). That `++` is the only non-debug award site today, but
/// the setter catches every path and is where the "new upgrade point" notification fires from.
///
/// Inert unless a session sets it.
/// </remarks>
internal static class DivineInspirationPatch
{
    // Fired when the player fills the Devotion meter, *before* any withholding. Filling it is
    // what earns the check, whether or not they keep the point
    internal static Action PointEarned;

    // True while Archipelago owns the point supply. Set by DivineInspirationService in
    // checks_and_points and checks_and_techs
    internal static bool WithholdPoints;

    // Set while the service is granting a point itself, so its own write is neither withheld nor
    // mistaken for a meter fill. A point from the multiworld paying out a check would
    // double-count every point in the seed
    private static bool granting;

    // Adds count points past the withholding
    internal static void GrantPoints(int count)
    {
        if (count <= 0)
        {
            return;
        }

        granting = true;
        try
        {
            UpgradeSystem.AbilityPoints += count;
        }
        finally
        {
            granting = false;
        }
    }

    // Logged once per session the first time an award is swallowed. Withholding is otherwise
    // completely silent, which makes "is this working?" unanswerable from a log
    private static bool loggedFirstWithhold;

    // Lets the next session report its own first withhold
    internal static void ResetWithholdLog()
    {
        loggedFirstWithhold = false;
    }

    /// <summary>
    /// Reports the meter fill, then swallows the point if this seed is withholding.
    ///
    /// Spending still has to work, since the player converts granted points into upgrades, so a
    /// decrease always passes through, as does a write the service made itself.
    /// </summary>
    [HarmonyPatch(typeof(UpgradeSystem), nameof(UpgradeSystem.AbilityPoints),
        MethodType.Setter)]
    internal static class AbilityPointSetter
    {
        [HarmonyPrefix]
        private static bool Prefix(int value)
        {
            // Our own grant is not a meter fill, and is never withheld.
            if (granting)
            {
                return true;
            }

            var current = UpgradeSystem.AbilityPoints;
            if (value <= current)
            {
                return true;
            }

            PointEarned?.Invoke();

            if (!WithholdPoints)
            {
                return true;
            }

            if (!loggedFirstWithhold)
            {
                loggedFirstWithhold = true;
                Log.LogInfo($"[AP] Withheld a Divine Inspiration ability point ({current} -> "
                    + $"{value} suppressed). Points come from the multiworld this seed; this is "
                    + "logged once per session.");
            }

            return false;
        }
    }

    /// <summary>
    /// Most Devotion a single ability point may cost. 0 leaves the game's own curve alone.
    /// </summary>
    internal static int DevotionCap;

    /// <summary>
    /// Caps what the next point costs.
    /// </summary>
    /// <remarks>
    /// The curve runs 1, 13, 29 ... to 465 and holds there (`DataManager.TargetXP`, 41 entries,
    /// clamped by index), roughly 24,000 Devotion for all 69 points. Capping the tail leaves the
    /// early curve intact.
    ///
    /// A postfix, not a prefix: `AllUnlockedMultiplier` triples the cost inside GetTargetXP once
    /// nothing is left to unlock, so clamping afterwards also neutralises that cliff.
    /// </remarks>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.GetTargetXP))]
    internal static class DevotionCost
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            if (DevotionCap > 0)
            {
                __result = Mathf.Min(__result, DevotionCap);
            }
        }
    }

    // The Divine Inspiration tree, or null before GameManager exists
    internal static UpgradeTreeConfiguration Tree =>
        GameManager.GetInstance()?.UpgradeTreeConfiguration;
}
