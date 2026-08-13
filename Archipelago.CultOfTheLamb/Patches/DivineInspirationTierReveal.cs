using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Services;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.Assets;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Opens every tier of the Divine Inspiration tree at once, so curated_checks doesn't draw lock
/// lines across a tree the player can't interact with. In that mode Archipelago grants the
/// upgrades and there are no ability points to spend on opening a row.
///
/// **Patches the reader, not the data.** Zeroing the serialized <c>_numRequiredToUnlock</c> on
/// GameManager's tree reports success and changes nothing on screen: <c>TierLockIcon</c> and
/// <c>UpgradeTreeNode</c> read through their own serialized config reference, which needn't be
/// that instance. Patching the method catches every caller whichever asset it holds, and mutates
/// no shipped data - so there is nothing to restore beyond a flag.
///
/// Purely cosmetic: this feeds the lock icon and the per-node Locked state, neither of which
/// gates what the client can grant.
/// </summary>
[HarmonyPatch(typeof(UpgradeTreeConfiguration),
    nameof(UpgradeTreeConfiguration.NumRequiredNodesForTier))]
internal static class DivineInspirationTierReveal
{
    private static bool active;

    /// <summary>Instance ids already logged, so the log gets one line each rather than one a frame.</summary>
    private static readonly HashSet<int> reported = new();

    /// <summary>Mirrors DivineInspirationShuffle.Apply - the mode decides, not the caller.</summary>
    internal static void Apply(int mode)
    {
        active = mode == DivineInspirationService.ModeCurated;
        if (!active) return;

        Log.LogInfo("[AP] Divine Inspiration tier gates suppressed - the whole tree is visible, "
            + "since there are no points to spend on opening it.");
    }

    internal static void Restore()
    {
        active = false;
        reported.Clear();
    }

    private static void Postfix(UpgradeTreeConfiguration __instance, ref int __result)
    {
        if (!active || __result == 0) return;

        // Deliberately not filtered to GameManager's Divine Inspiration tree - that filter is
        // what the field-mutation attempt effectively had, and it silently did nothing. The log
        // records which asset each caller holds, so there's a name to filter on if this ever
        // reveals a tree it shouldn't. Keyed on the instance id rather than the name, because
        // reading .name marshals a string across the native boundary on every UI paint.
        if (__instance != null && reported.Add(__instance.GetInstanceID()))
        {
            Log.LogInfo($"[AP] Tier reveal: clearing tier gates on upgrade tree "
                + $"'{__instance.name}' (is GameManager's Divine Inspiration tree: "
                + $"{ReferenceEquals(__instance, DivineInspirationPatch.Tree)}).");
        }

        __result = 0;
    }

    /// <summary>
    /// Hides the tier divider itself, not just its padlock.
    ///
    /// Clearing the gate above is only half of it: TierLockIcon.Configure responds to an open
    /// tier by hiding the lock and *filling the divider lines in* (TierLockIcon.cs:50-52), so a
    /// revealed tree still gets a white rule drawn across it per tier. Those lines mark a
    /// boundary that no longer exists here, so the whole icon goes - which is what the method's
    /// own early-out does when a tier is behind you.
    /// </summary>
    [HarmonyPatch(typeof(TierLockIcon), nameof(TierLockIcon.Configure))]
    internal static class HideDivider
    {
        private static void Postfix(TierLockIcon __instance)
        {
            if (active) __instance.gameObject.SetActive(false);
        }
    }
}
