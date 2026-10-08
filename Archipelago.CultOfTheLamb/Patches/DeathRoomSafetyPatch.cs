using HarmonyLib;
using MMTools;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Sends the player home when they die somewhere the room the death screen wants does not exist.
/// </summary>
/// <remarks>
/// Both rooms fade to black and then dereference a manager that only a generated crusade puts in
/// the scene, so the NRE lands inside the transition callback and the fade never completes.
/// </remarks>
internal static class DeathRoomSafetyPatch
{
    // Checks the scene, not the location: FollowerLocation does not identify one, and both rooms
    // sit inactive until Play switches them on, so the inactive overload is required.
    private static bool PlayOrGoHome<T>(string room) where T : Object
    {
        if (Object.FindObjectOfType<T>(true) != null)
        {
            return true;
        }

        Time.timeScale = 1f;
        Log.LogInfo($"[AP] Died where there is no {room}. Returning to base instead.");
        GameManager.ToShip("Base Biome 1", 2f, MMTransition.Effect.BlackFade);
        return false;
    }

    [HarmonyPatch(typeof(DeathCatRoomManager), nameof(DeathCatRoomManager.Play))]
    internal static class DeathCatRoom
    {
        [HarmonyPrefix]
        private static bool Prefix() => PlayOrGoHome<DeathCatRoomManager>("Death Cat room");
    }

    // Reached before the Death Cat room, and gated on save flags and dungeon progress only.
    [HarmonyPatch(typeof(MysticShopKeeperManager), nameof(MysticShopKeeperManager.Play))]
    internal static class MysticShop
    {
        [HarmonyPrefix]
        private static bool Prefix() =>
            PlayOrGoHome<MysticShopKeeperManager>("Mystic Shop keeper room");
    }
}
