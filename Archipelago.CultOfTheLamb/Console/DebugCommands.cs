using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Console;

/// <summary>
/// Keybind-driven debug helpers. RoR2 has a native dev console (RoR2.Console) that the
/// RiskOfRain2 mod hooks with [ConCommand]; Cult of the Lamb doesn't expose an equivalent
/// out of the box (TODO: confirm - COTL_API may add one), so this uses BepInEx keybinds
/// instead. The bodies live in DebugActions; ArchipelagoPlugin does the wiring.
///
/// The feature keys (F6-F11) exist to prove out candidate AP features in one debug build
/// rather than one build/test cycle per feature - see docs/sprints/sprint-2-feature-slice.md.
/// They're developer tooling, not player-facing, and should be removed or gated once the
/// features they test are real.
/// </summary>
internal static class DebugCommands
{
    private static readonly List<(ConfigEntry<KeyboardShortcut> Key, Action Handler)> bindings = new();

    private static ConfigEntry<KeyboardShortcut> debugKey;
    private static ConfigEntry<KeyboardShortcut> connectKey;

    internal static void Init(ConfigFile config)
    {
        debugKey = Bind(config, "DumpStateKey", KeyCode.F9,
            "Dumps Archipelago client state, the game's boss-kill records, and every "
            + "MiniBossController in the current scene (internal name -> display name) to the log.");

        // Kept on its original config name so existing setups don't lose their binding, even
        // though it now opens the panel rather than connecting outright. The panel is also
        // reachable from the pause and main menus, so this is a shortcut rather than the only
        // way in.
        connectKey = Bind(config, "ConnectKey", KeyCode.F5,
            "Opens (or closes) the Archipelago connection panel.");

        BindFeatureKey(config, "ListSermonUpgradesKey", KeyCode.F2,
            "Lists the sermon upgrades you own to the log. Does NOT open the game's upgrade "
            + "tree - that menu can't be dismissed without picking an upgrade, which would "
            + "hand out one the randomizer never granted.",
            DebugActions.ListOwnedSermonUpgrades);

        BindFeatureKey(config, "FillSermonBarKey", KeyCode.F3,
            "Fills the sermon XP bar so the next sermon at the Temple immediately pays out "
            + "(tests sermon checks without grinding real sermons).",
            DebugActions.FillSermonBar);

        BindFeatureKey(config, "DumpNamesKey", KeyCode.F4,
            "Writes the internal-name -> display-name table for upgrades, tarot, fleeces, "
            + "crown abilities and doctrines to BepInEx/ap_unlockable_names.txt.",
            DebugActions.DumpUnlockableNames);

        BindFeatureKey(config, "GiveResourcesKey", KeyCode.F6,
            "Grants a few resource items (tests filler-item grants).",
            DebugActions.GiveResources);

        BindFeatureKey(config, "UnlockSermonKey", KeyCode.F7,
            "Unlocks one sermon/ability upgrade (tests sermon-upgrade item grants).",
            DebugActions.UnlockSampleSermon);

        BindFeatureKey(config, "UnlockTarotKey", KeyCode.F8,
            "Unlocks one tarot card (tests tarot item grants).",
            DebugActions.UnlockSampleTarot);

        BindFeatureKey(config, "UnlockFleeceKey", KeyCode.F10,
            "Unlocks one fleece (tests fleece item grants).",
            DebugActions.UnlockSampleFleece);

        BindFeatureKey(config, "ShowNotificationKey", KeyCode.F11,
            "Shows sample Archipelago notifications (tests the notification pipeline).",
            DebugActions.ShowSampleNotification);

        // F1, not F12: Steam binds F12 to screenshots by default, and a debug key that also
        // fires the overlay is a confusing thing to hand someone testing.
        BindFeatureKey(config, "DumpShopSlotsKey", KeyCode.F1,
            "Dumps every shop in the current scene and the renderer behind each of its slots "
            + "(checks which one ShopIconService should be replacing with the AP logo).",
            DebugActions.DumpShopSlots);
    }

    private static ConfigEntry<KeyboardShortcut> Bind(
        ConfigFile config, string name, KeyCode key, string description) =>
        config.Bind("Debug", name, new KeyboardShortcut(key), description);

    private static void BindFeatureKey(
        ConfigFile config, string name, KeyCode key, string description, Action handler)
    {
        bindings.Add((Bind(config, name, key, description), handler));
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

        foreach (var (key, handler) in bindings)
        {
            if (!key.Value.IsDown()) continue;

            // A debug keybind must never take the game down with it - these call into game
            // APIs that may not be initialized depending on where the player is (main menu,
            // mid-crusade, etc).
            try
            {
                handler();
            }
            catch (Exception e)
            {
                Log.LogError($"[AP] Debug key '{key.Definition.Key}' threw: {e}");
            }
        }
    }

    internal static event Action OnDebugKeyPressed;
    internal static event Action OnConnectKeyPressed;
}
