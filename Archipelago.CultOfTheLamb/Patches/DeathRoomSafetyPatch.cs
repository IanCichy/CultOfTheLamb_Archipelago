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
    private static bool RoomIsInScene<T>() where T : Object =>
        Object.FindObjectOfType<T>(true) != null;

    private static void GoHome(string room)
    {
        Time.timeScale = 1f;
        Log.LogInfo($"[AP] Died where there is no {room}. Returning to base instead.");
        GameManager.ToShip("Base Biome 1", 2f, MMTransition.Effect.BlackFade);
    }

    [HarmonyPatch(typeof(DeathCatRoomManager), nameof(DeathCatRoomManager.Play))]
    internal static class DeathCatRoom
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (RoomIsInScene<DeathCatRoomManager>())
            {
                return true;
            }

            GoHome("Death Cat room");
            return false;
        }
    }

    // Reached before the Death Cat room, and gated on save flags and dungeon progress only.
    [HarmonyPatch(typeof(MysticShopKeeperManager), nameof(MysticShopKeeperManager.Play))]
    internal static class MysticShop
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (RoomIsInScene<MysticShopKeeperManager>())
            {
                return true;
            }

            StubInstance();
            GoHome("Mystic Shop keeper room");
            return false;
        }

        // The death screen reads Instance on the line after Play, so skipping Play alone only
        // moves the throw. Built inactive, so neither OnEnable nor OnDisable ever runs on it.
        private static void StubInstance()
        {
            if (MysticShopKeeperManager.Instance != null)
            {
                return;
            }

            var stub = new GameObject("AP_MysticShopKeeperStub");
            stub.SetActive(false);
            MysticShopKeeperManager.Instance = stub.AddComponent<MysticShopKeeperManager>();
        }
    }
}
