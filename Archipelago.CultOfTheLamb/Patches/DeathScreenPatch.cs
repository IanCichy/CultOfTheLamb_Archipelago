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

    // The node loop reads MapManager.Instance unguarded, but only once past the first room.
    // Matching that exactly leaves every other death on the vanilla path, which matters because
    // the loop is also what starts the lost-item penalty.
    private static void SkipLevelNodesOutsideACrusade(ref int levels)
    {
        if (levels <= 0 || MapManager.Instance != null)
        {
            return;
        }

        var visited = DataManager.Instance?.dungeonVisitedRooms?.Count ?? 0;
        if (visited >= 2)
        {
            levels = 0;
        }
    }
}
