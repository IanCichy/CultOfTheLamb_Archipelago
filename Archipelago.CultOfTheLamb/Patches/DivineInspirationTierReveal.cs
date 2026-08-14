using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Services;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.Assets;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Opens every tier of the Divine Inspiration tree in curated_checks, where Archipelago grants the
/// upgrades and there are no ability points to spend on opening a row.
///
/// Patches the reader, not the data: zeroing the serialized _numRequiredToUnlock changes nothing
/// on screen, because TierLockIcon and UpgradeTreeNode read through their own config reference.
///
/// Filtered by tree in both patches. NumRequiredNodesForTier is the tier-unlock test, not just the
/// lock icon, and GameManager holds three of these configs - unfiltered, this opens the player and
/// Woolhaven trees too.
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
        if (!ReferenceEquals(__instance, DivineInspirationPatch.Tree)) return;

        // Keyed on instance id, not name: reading .name marshals a string on every UI paint.
        if (reported.Add(__instance.GetInstanceID()))
        {
            Log.LogInfo($"[AP] Tier reveal: clearing tier gates on '{__instance.name}'.");
        }

        __result = 0;
    }

    /// <summary>
    /// Hides the divider itself, not just its padlock: TierLockIcon.Configure answers an open tier
    /// by hiding the lock and filling the divider lines in, leaving a rule across a boundary that
    /// no longer exists.
    /// </summary>
    [HarmonyPatch(typeof(TierLockIcon), nameof(TierLockIcon.Configure))]
    internal static class HideDivider
    {
        // The icon's own serialized tree reference, which is what says the icon belongs to. Read
        // through FieldRef rather than a `____config` parameter: Harmony strips three underscores
        // as its prefix, so a field named _config needs four, and getting that wrong throws at
        // patch time and takes the rest of PatchAll with it.
        private static readonly AccessTools.FieldRef<TierLockIcon, UpgradeTreeConfiguration> Config =
            AccessTools.FieldRefAccess<TierLockIcon, UpgradeTreeConfiguration>("_config");

        private static void Postfix(TierLockIcon __instance)
        {
            if (!active || !ReferenceEquals(Config(__instance), DivineInspirationPatch.Tree)) return;

            __instance.gameObject.SetActive(false);
        }
    }
}
