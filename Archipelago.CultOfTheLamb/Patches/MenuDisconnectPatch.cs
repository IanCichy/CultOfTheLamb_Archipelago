using System;
using Archipelago.CultOfTheLamb.Console;
using HarmonyLib;
using Lamb.UI.MainMenu;

namespace Archipelago.CultOfTheLamb.Patches;

// Ends the session on the way back to the main menu, so a connection can never follow the
// player into a different save.
[HarmonyPatch(typeof(MainMenu), "Start")]
internal static class MenuDisconnectPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            var client = ArchipelagoPlugin.Instance?.Client;
            if (client == null)
            {
                return;
            }

            // A warning belongs to the save just left. Left up, its Connect anyway would run at the
            // menu, past the panel's loaded-save check.
            client.DismissSaveWarning();

            // A retry loop too, or it reconnects at the menu or inside the next save loaded.
            if (!client.IsConnected && !client.Reconnecting)
            {
                return;
            }

            Log.LogInfo("[AP] Back at the main menu, so the session is being closed. Reconnect "
                + "after loading a save.");

            // Through the console command's event rather than the client directly, which is the
            // same path the pause-menu button takes.
            ArchipelagoConsoleCommand.Disconnect();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't close the session on returning to the menu, so it "
                + $"may still be connected: {e.Message}");
        }
    }
}
