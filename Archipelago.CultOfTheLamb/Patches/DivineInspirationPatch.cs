using System;
using HarmonyLib;
using Lamb.UI.Assets;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Owns the Divine Inspiration point economy: reports when the player fills the Devotion meter,
/// optionally withholds the point it would have paid, and can cap how much Devotion a point
/// costs.
///
/// The award is patched at <c>UpgradeSystem.AbilityPoints</c>'s **setter** rather than at
/// PlayerFarming's <c>++</c> (`PlayerFarming.cs:2024`). That `++` is the only non-debug award
/// site today, but the setter catches every path including any the game adds later, and it's
/// where the "new upgrade point" notification fires from.
///
/// Everything here is inert unless a session sets it, so the game is untouched when
/// disconnected.
/// </summary>
internal static class DivineInspirationPatch
{
    /// <summary>
    /// Fired when the player fills the Devotion meter - **before** any withholding, because
    /// filling it is the thing that earns the check whether or not they get to keep the point.
    /// </summary>
    internal static Action PointEarned;

    /// <summary>
    /// True while Archipelago owns the point supply. Set by DivineInspirationService in
    /// checks_and_points and checks_and_techs.
    /// </summary>
    internal static bool WithholdPoints;

    /// <summary>
    /// Set while the service is granting a point itself, so its own write isn't withheld - and,
    /// just as importantly, isn't mistaken for a meter fill. A point arriving from the
    /// multiworld must not pay out a check; that would double-count every point in the seed.
    /// </summary>
    private static bool granting;

    /// <summary>Adds <paramref name="count"/> points past the withholding.</summary>
    internal static void GrantPoints(int count)
    {
        if (count <= 0) return;

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

    /// <summary>
    /// Logged once per session the first time an award is actually swallowed. Withholding is
    /// otherwise completely silent - nothing appears in the log and the player just doesn't get
    /// a point - which makes "is this working?" unanswerable from a log without it.
    /// </summary>
    private static bool loggedFirstWithhold;

    /// <summary>Lets the next session report its own first withhold.</summary>
    internal static void ResetWithholdLog() => loggedFirstWithhold = false;

    /// <summary>
    /// Reports the meter fill, then swallows the point if this seed is withholding.
    ///
    /// Spending still has to work - the player converts granted points into upgrades - so a
    /// decrease always passes through, as does a write the service made itself.
    /// </summary>
    [HarmonyPatch(typeof(UpgradeSystem), nameof(UpgradeSystem.AbilityPoints),
        MethodType.Setter)]
    internal static class AbilityPointSetter
    {
        [HarmonyPrefix]
        private static bool Prefix(int value)
        {
            // Our own grant: not a meter fill, and never withheld.
            if (granting) return true;

            var current = UpgradeSystem.AbilityPoints;
            if (value <= current) return true;

            PointEarned?.Invoke();

            if (!WithholdPoints) return true;

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
    ///
    /// The game's curve runs 1, 13, 29 ... up to 465 and then stays there
    /// (`DataManager.TargetXP`, 41 entries, clamped by index), which totals roughly 24,000
    /// Devotion for all 69 points - a completionist number, not a one-seed number. Capping the
    /// tail keeps the early curve intact while making the whole tree reachable in a normal run.
    ///
    /// Applied as a postfix on purpose: `AllUnlockedMultiplier` triples the cost inside
    /// GetTargetXP once nothing is left to unlock, so clamping afterwards neutralises that
    /// cliff for free.
    /// </summary>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.GetTargetXP))]
    internal static class DevotionCost
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            if (DevotionCap > 0) __result = Mathf.Min(__result, DevotionCap);
        }
    }

    /// <summary>The Divine Inspiration tree, or null before GameManager exists.</summary>
    internal static UpgradeTreeConfiguration Tree =>
        GameManager.GetInstance()?.UpgradeTreeConfiguration;
}
