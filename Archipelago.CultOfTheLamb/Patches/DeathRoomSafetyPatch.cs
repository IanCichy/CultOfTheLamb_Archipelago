using HarmonyLib;
using MMTools;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Sends the player home when they die somewhere the room the death screen wants does not exist.
/// </summary>
/// <remarks>
/// After a death the game can send you to one of two special rooms. Both are built as part of a
/// crusade, so dying anywhere else leaves the game fading to black towards a room that was never
/// loaded. It crashes partway through the fade, which is a black screen you cannot escape.
/// </remarks>
internal static class DeathRoomSafetyPatch
{
    // Ask whether the room is actually loaded rather than guessing from where the player is,
    // which does not reliably tell you whether you are on a crusade. Both rooms sit switched off
    // until the game plays them, so the search has to include switched-off objects.
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

    // The game checks this room before the Death Cat one, and decides purely on story progress,
    // never on where you are. So any death at all can be sent here.
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

        // Skipping the room is not enough on its own: the game reaches for it again immediately
        // afterwards and crashes when it is missing. So leave an empty stand-in for it to find.
        // It is created switched off, so none of the real room's own startup code runs.
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
