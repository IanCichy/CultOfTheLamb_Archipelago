using HarmonyLib;
using MMTools;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Sends the player home when they die somewhere the Death Cat room does not exist.
/// </summary>
/// <remarks>
/// Vanilla never kills you outside a crusade, so the game has no route back. DeathCatRoomManager
/// .Play fades to black, then dereferences FindObjectOfType&lt;DeathCatRoomManager&gt;(), which is
/// null at base, and its routine deactivates the biome generator besides. Death Link can kill you
/// anywhere, so the mod has to supply the route.
///
/// ToShip is that route, and it is the game's own: the cheat console's Return To Base calls it,
/// and so does coming home from a crusade. It resets the run, saves, cleans up the characters and
/// transitions, which between them clear the death screen, the game-over music and the locked
/// input. Reviving by hand instead meant replicating all of that, and every piece missed left the
/// player alive but unable to move.
///
/// Decided on the player's location, not on whether the room object exists. Play transitions
/// first and resolves Instance inside its own callback, so FindObjectOfType is null here even on
/// a crusade death, and testing for it hijacked every death in the game.
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
