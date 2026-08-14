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
    private static ConfigEntry<KeyboardShortcut> questGuideKey;
    private static ConfigEntry<KeyboardShortcut> completeBishopsKey;

    internal static void Init(ConfigFile config)
    {
        debugKey = Bind(config, "DumpStateKey", KeyCode.F9,
            "Dumps Archipelago client state, the game's boss-kill records, and every "
            + "MiniBossController in the current scene (internal name -> display name) to the log.");

        // Ctrl+F9 rather than a spare function key: F1-F11 are taken and F12 is Steam's
        // screenshot binding. BepInEx's KeyboardShortcut requires unlisted modifiers to be up,
        // so this doesn't also fire the plain-F9 dump above.
        questGuideKey = Bind(config, "DumpQuestGuideKey", KeyCode.F9,
            "Dumps the Archipelago objective guide - each line's I2 term, intended text and "
            + "read-back, plus every Archipelago objective sitting in the save - then sweeps "
            + "and rebuilds it.",
            KeyCode.LeftControl);

        // Kept on its original config name so existing setups don't lose their binding, even
        // though it now opens the panel rather than connecting outright. The panel is also
        // reachable from the pause and main menus, so this is a shortcut rather than the only
        // way in.
        connectKey = Bind(config, "ConnectKey", KeyCode.F5,
            "Opens (or closes) the Archipelago connection panel.");

        // Ctrl+F2: every plain function key is already taken, and this needs the Archipelago
        // client to re-check the goal, so it goes through an event like the two above.
        completeBishopsKey = Bind(config, "CompleteBishopsKey", KeyCode.F2,
            "Records all four Bishops as beaten and breaks every chain on the Gateway door, so "
            + "the Narinder goal can be tested without a full playthrough. Does not send the "
            + "Bishop location checks.",
            KeyCode.LeftControl);

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
        ConfigFile config, string name, KeyCode key, string description, params KeyCode[] modifiers) =>
        config.Bind("Debug", name, new KeyboardShortcut(key, modifiers), description);

    private static void BindFeatureKey(
        ConfigFile config, string name, KeyCode key, string description, Action handler,
        params KeyCode[] modifiers)
    {
        bindings.Add((Bind(config, name, key, description, modifiers), handler));
    }

    internal static void Update()
    {
        // The event-based keys need the plugin's Archipelago reference, which isn't available
        // when bindings is built, so they can't live in that list yet. They go through the same
        // guard, because Ctrl+F9 rebuilds the objective guide and writes save lists.
        Fire(debugKey, () => OnDebugKeyPressed?.Invoke());
        Fire(connectKey, () => OnConnectKeyPressed?.Invoke());
        Fire(questGuideKey, () => OnQuestGuideKeyPressed?.Invoke());
        Fire(completeBishopsKey, () => OnCompleteBishopsKeyPressed?.Invoke());

        foreach (var (key, handler) in bindings)
        {
            Fire(key, handler);
        }
    }

    /// <summary>
    /// Runs a keybind's handler if it was pressed.
    ///
    /// A debug keybind must never take the game down with it - these call into game APIs that
    /// may not be initialized depending on where the player is (main menu, mid-crusade, etc).
    /// </summary>
    private static void Fire(ConfigEntry<KeyboardShortcut> key, Action handler)
    {
        if (key == null || !key.Value.IsDown()) return;

        try
        {
            handler();
        }
        catch (Exception e)
        {
            Log.LogError($"[AP] Debug key '{key.Definition.Key}' threw: {e}");
        }
    }

    internal static event Action OnDebugKeyPressed;
    internal static event Action OnConnectKeyPressed;

    /// <summary>
    /// Like OnDebugKeyPressed, an event rather than a BindFeatureKey handler because its body
    /// needs the ArchipelagoClient, which lives on the plugin.
    /// </summary>
    internal static event Action OnQuestGuideKeyPressed;

    /// <summary>Ctrl+F2. Needs the ArchipelagoClient to re-check the goal after writing.</summary>
    internal static event Action OnCompleteBishopsKeyPressed;
}
