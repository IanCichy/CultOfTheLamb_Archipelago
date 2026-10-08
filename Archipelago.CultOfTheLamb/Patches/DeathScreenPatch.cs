using System;
using HarmonyLib;
using Lamb.UI.DeathScreen;
using Map;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Reports a real player death.
/// </summary>
/// <remarks>
/// The death screen, not HealthPlayer.OnPlayerDied, which also fires on a co-op knockdown and on
/// resurrections from The Deal, a Pyre or a Monolith. None of those reach a death screen, so
/// hooking here excludes them without enumerating them.
///
/// Only the three-argument Show: the two-argument overload delegates to it.
/// </remarks>
[HarmonyPatch(typeof(UIDeathScreenOverlayController), nameof(UIDeathScreenOverlayController.Show),
    typeof(UIDeathScreenOverlayController.Results), typeof(int), typeof(bool))]
internal static class DeathScreenPatch
{
    /// <summary>Raised when the player dies for real.</summary>
    internal static Action OnPlayerKilled;

    [HarmonyPrefix]
    private static void Prefix(UIDeathScreenOverlayController.Results result, ref int levels)
    {
        SkipLevelNodesOutsideACrusade(ref levels);

        if (result != UIDeathScreenOverlayController.Results.Killed)
        {
            return;
        }

        try
        {
            OnPlayerKilled?.Invoke();
        }
        catch (Exception e)
        {
            // Never take the death screen down with us
            Log.LogWarning($"[AP] Death notification failed: {e.Message}");
        }
    }

    // The node loop reads MapManager.Instance unguarded, so any death outside a crusade threw
    // once the last one had reached a second room. levels is read nowhere else.
    private static void SkipLevelNodesOutsideACrusade(ref int levels)
    {
        if (levels > 0 && MapManager.Instance == null)
        {
            levels = 0;
        }
    }
}
