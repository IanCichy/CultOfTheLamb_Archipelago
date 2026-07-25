using BepInEx.Configuration;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Console;

/// <summary>
/// Keybind-driven debug helpers. RoR2 has a native dev console (RoR2.Console) that the
/// RiskOfRain2 mod hooks with [ConCommand]; Cult of the Lamb doesn't expose an equivalent
/// out of the box (TODO: confirm - COTL_API may add one), so this uses a BepInEx keybind
/// instead. Wire real debug behavior into OnDebugKeyPressed once there's something worth
/// inspecting (received item queue, region unlock state, etc.).
/// </summary>
internal static class DebugCommands
{
    private static ConfigEntry<KeyboardShortcut> debugKey;
    private static ConfigEntry<KeyboardShortcut> connectKey;

    internal static void Init(ConfigFile config)
    {
        debugKey = config.Bind(
            "Debug",
            "DumpStateKey",
            new KeyboardShortcut(KeyCode.F9),
            "Dumps current Archipelago client state to the BepInEx log.");

        // There's no in-game UI to connect yet (ArchipelagoConnectButtonController is a
        // stub) and no native dev console to type a command into, so this keybind - using
        // the SlotName/ServerName/Port/Password values from the BepInEx config file - is
        // the only way to connect/disconnect for testing right now.
        connectKey = config.Bind(
            "Debug",
            "ConnectKey",
            new KeyboardShortcut(KeyCode.F5),
            "Connects to (or disconnects from) Archipelago using the SlotName/ServerName/"
            + "Port/Password values below.");
    }

    internal static void Update()
    {
        if (debugKey != null && debugKey.Value.IsDown())
        {
            OnDebugKeyPressed?.Invoke();
        }

        if (connectKey != null && connectKey.Value.IsDown())
        {
            OnConnectKeyPressed?.Invoke();
        }
    }

    internal static event System.Action OnDebugKeyPressed;
    internal static event System.Action OnConnectKeyPressed;
}
