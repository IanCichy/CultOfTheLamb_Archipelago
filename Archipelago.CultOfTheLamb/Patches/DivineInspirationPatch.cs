using System;
using HarmonyLib;
using Lamb.UI.Assets;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Controls how Divine Inspiration points are earned. Reports when the Devotion meter fills, can
/// hold back the point it would have given, and can cap what a point costs.
/// </summary>
/// <remarks>
/// Patches the UpgradeSystem.AbilityPoints setter instead of the one place PlayerFarming adds a
/// point, since the setter catches every path and is where the "new point" popup comes from.
///
/// Does nothing unless a session turns it on.
/// </remarks>
internal static class DivineInspirationPatch
{
    // Fired when the player fills the Devotion meter, before any withholding. Filling it is
    // what earns the check, whether or not they keep the point
    internal static Action PointEarned;

    // True while Archipelago owns the point supply. Set by DivineInspirationService in every
    // granting mode: checks_and_points, checks_and_techs and curated_checks
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

    // Logged once per session, the first time a point is held back. Otherwise withholding is
    // silent, and there'd be no way to tell from the log whether it's working
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

    // Most Devotion a single ability point may cost. 0 leaves the game's own curve alone
    internal static int DevotionCap;

    /// <summary>
    /// Caps what the next point costs
    /// </summary>
    /// <remarks>
    /// The cost climbs from 1 to 465 and stays there, about 24,000 Devotion for all 69 points.
    /// Capping only the top keeps the early curve the same.
    ///
    /// A postfix, so it also catches the game tripling the cost once there's nothing left to
    /// unlock.
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
