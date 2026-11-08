using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Services;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.Assets;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Opens every tier of the Divine Inspiration tree in curated_checks, where Archipelago grants
/// the upgrades and there are no ability points to spend on opening a row.
///
/// Patches the reader rather than the data because the data is not reachable:
/// NumRequiredToUnlock is a getter-only property over a private field, inside a private
/// _tierConfigurations list.
///
/// Both patches filter by tree. NumRequiredNodesForTier is the tier-unlock test rather than just
/// the lock icon, and GameManager holds three of these configs (UpgradeTreeConfiguration,
/// UpgradePlayerConfiguration, DLCUpgradeTreeConfiguration), so unfiltered this would open the
/// player and Woolhaven trees too.
/// </summary>
[HarmonyPatch(typeof(UpgradeTreeConfiguration),
    nameof(UpgradeTreeConfiguration.NumRequiredNodesForTier))]
internal static class DivineInspirationTierReveal
{
    private static bool active;

    // Instance ids already logged, so the log gets one line each rather than one a frame
    private static readonly HashSet<int> reported = new();

    // Mirrors DivineInspirationShuffle.Apply. The mode decides, not the caller
    internal static void Apply(int mode)
    {
        active = mode == DivineInspirationService.ModeCurated;
        if (!active)
        {
            return;
        }

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
        if (!active || __result == 0)
        {
            return;
        }

        if (!ReferenceEquals(__instance, DivineInspirationPatch.Tree))
        {
            return;
        }

        // Keyed on instance id, not name. Reading .name marshals a string on every UI paint.
        if (reported.Add(__instance.GetInstanceID()))
        {
            Log.LogInfo($"[AP] Tier reveal: clearing tier gates on '{__instance.name}'.");
        }

        __result = 0;
    }

    /// <summary>
    /// Hides the divider, not just its padlock. Given an open tier, TierLockIcon.Configure hides
    /// the lock and fills both divider lines in, which would leave a rule drawn across a boundary
    /// that no longer exists.
    /// </summary>
    [HarmonyPatch(typeof(TierLockIcon), nameof(TierLockIcon.Configure))]
    internal static class HideDivider
    {
        // The icon's own tree reference, which is what it belongs to. Read through FieldRef
        // rather than a `____config` parameter, because Harmony strips three underscores as its
        // prefix so a field named _config needs four, and getting that wrong throws at patch time.
        private static readonly AccessTools.FieldRef<TierLockIcon, UpgradeTreeConfiguration> Config =
            AccessTools.FieldRefAccess<TierLockIcon, UpgradeTreeConfiguration>("_config");

        // Resolved by field name, so a rename would throw on every icon the tree paints and
        // make the menu unusable. A visible divider is the harmless outcome.
        private static void Postfix(TierLockIcon __instance)
        {
            try
            {
                if (!active || !ReferenceEquals(Config(__instance), DivineInspirationPatch.Tree))
                {
                    return;
                }

                __instance.gameObject.SetActive(false);
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"[AP] Couldn't hide a tier divider on the Divine Inspiration "
                    + $"tree: {e}");
            }
        }
    }
}
