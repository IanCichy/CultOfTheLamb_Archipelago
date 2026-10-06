using HarmonyLib;
using MMTools;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Sends the player home when they die somewhere the Death Cat room does not exist.
/// </summary>
/// <remarks>
/// Vanilla never kills you outside a crusade, so Play fades to black and then dereferences a
/// DeathCatRoomManager that only exists where a crusade generated one. Death Link can kill you
/// anywhere.
///
/// ToShip is the game's own way home, used by the cheat console and by finishing a crusade. It
/// resets the run, saves and transitions, which clears the death screen, the game-over music and
/// the input lock together.
///
/// Decided on location, not on whether the room object exists: Play transitions before resolving
/// it, so a lookup here is null even mid-crusade and would hijack every death in the game.
/// </remarks>
[HarmonyPatch(typeof(DeathCatRoomManager), nameof(DeathCatRoomManager.Play))]
internal static class DeathCatRoomSafetyPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (GameManager.IsDungeon(PlayerFarming.Location))
        {
            return true;
        }

        // Play sets this inside the callback that would have thrown
        Time.timeScale = 1f;

        Log.LogInfo("[AP] Died outside a crusade, where there is no Death Cat room. Returning to "
            + "base instead.");

        GameManager.ToShip("Base Biome 1", 2f, MMTransition.Effect.BlackFade);
        return false;
    }
}
