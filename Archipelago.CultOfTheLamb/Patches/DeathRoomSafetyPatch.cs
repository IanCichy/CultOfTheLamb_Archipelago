using HarmonyLib;
using MMTools;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Sends the player home when they die somewhere the room the death screen wants does not exist.
///
/// After a death the game can route you into one of two special rooms. Both are built as part of
/// a crusade, so dying anywhere else leaves the game fading to black towards a room that was
/// never loaded. It throws partway through the fade, which leaves a black screen with no way out.
/// </summary>
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
        // Falling through reinstates the vanilla black screen for one death, which beats
        // throwing inside the death flow and breaking every death.
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try
            {
                if (RoomIsInScene<DeathCatRoomManager>())
                {
                    return true;
                }

                GoHome("Death Cat room");
                return false;
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"[AP] Couldn't check for the Death Cat room, so the game's own "
                    + $"handling stands: {e}");
                return true;
            }
        }
    }

    // Checked before the Death Cat room, and its condition is story progress only
    // (ForeshadowedMysticShop, OnboardedMysticShop, the Dungeon1_4 layer and
    // RemovedStoryMomentsActive), with no test for where the player is. So any death anywhere
    // can be routed here.
    [HarmonyPatch(typeof(MysticShopKeeperManager), nameof(MysticShopKeeperManager.Play))]
    internal static class MysticShop
    {
        // Same fallback: the vanilla outcome beats breaking the death flow outright.
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try
            {
                if (RoomIsInScene<MysticShopKeeperManager>())
                {
                    return true;
                }

                StubInstance();
                GoHome("Mystic Shop keeper room");
                return false;
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"[AP] Couldn't check for the Mystic Shop keeper room, so the "
                    + $"game's own handling stands: {e}");
                return true;
            }
        }

        // Skipping the room is not enough on its own. The line straight after Play() reads
        // MysticShopKeeperManager.Instance.transform, so a null Instance throws there instead.
        // The stand-in is created switched off, so none of the real room's startup code runs.
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
