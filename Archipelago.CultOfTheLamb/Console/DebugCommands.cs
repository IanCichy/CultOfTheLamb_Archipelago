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

    internal static void Init(ConfigFile config)
    {
        debugKey = config.Bind(
            "Debug",
            "DumpStateKey",
            new KeyboardShortcut(KeyCode.F9),
            "Dumps current Archipelago client state to the BepInEx log.");
    }

    internal static void Update()
    {
        if (debugKey != null && debugKey.Value.IsDown())
        {
            OnDebugKeyPressed?.Invoke();
        }
    }

    internal static event System.Action OnDebugKeyPressed;
}
