using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Console;

/// <summary>
/// Keybind-driven debug helpers. Cult of the Lamb has no dev console, so these are BepInEx
/// keybinds. The bodies live in DebugActions and ArchipelagoPlugin does the wiring.
/// </summary>
/// <remarks>
/// **A normal build has exactly one key. F9 dumps state to the log.** Every other binding
/// is behind the AP_DEBUG_KEYS compile constant, set only in the gitignored
/// Directory.Build.props.user, so a clean checkout, a tester's build and any release build can't
/// press them. The DebugActions bodies still compile in. It's the bindings that are gated, so
/// the accurate claim is "unreachable", not "absent".
///
/// One key on purpose. The entire instruction a tester needs is "press F9 and send the log", every
/// extra binding is something to press by accident and report as a bug, and connecting doesn't need
/// a key at all since the panel is on both menus.
///
/// That matters because six of the gated keys write real state and three reach the server. Ctrl+F2
/// could report a false victory on a Bishops seed, F3 pays out a sermon check, and F8 unlocks a
/// tarot card that TarotService then sends. In a shared multiworld those corrupt other people's
/// games, and testers press keys without reading what they do.
/// </remarks>
internal static class DebugCommands
{
    private static readonly List<(ConfigEntry<KeyboardShortcut> Key, Action Handler)> bindings = new();

    private static ConfigEntry<KeyboardShortcut> debugKey;

#if AP_DEBUG_KEYS
    private static ConfigEntry<KeyboardShortcut> connectKey;
    private static ConfigEntry<KeyboardShortcut> questGuideKey;
    private static ConfigEntry<KeyboardShortcut> completeBishopsKey;
#endif

    internal static void Init(ConfigFile config)
    {
        // ---- the only key in a normal build ----

        // The whole bug-reporting flow is "press F9, send LogOutput.log", so this has to survive
        // in every build or an alpha loses its main diagnostic.
        debugKey = Bind(config, "DumpStateKey", KeyCode.F9,
            "Dumps Archipelago client state, the game's boss-kill records, and every "
            + "MiniBossController in the current scene (internal name -> display name) to the log.");

#if AP_DEBUG_KEYS
        // ---- developer only, compiled out of every other build ----

        // A shortcut, not the only way in. The panel is on the main menu and the pause menu, so
        // players never need this. Kept on its original config name so existing setups don't lose
        // their binding.
        connectKey = Bind(config, "ConnectKey", KeyCode.F5,
            "Opens (or closes) the Archipelago connection panel.");

        // Ctrl+F9 rather than a spare function key, because F1 through F11 are taken and F12 is
        // Steam's screenshot binding. BepInEx's KeyboardShortcut requires unlisted modifiers to
        // be up, so this doesn't also fire the plain-F9 dump above.
        questGuideKey = Bind(config, "DumpQuestGuideKey", KeyCode.F9,
            "Dumps the Archipelago objective guide - each line's I2 term, intended text and "
            + "read-back, plus every Archipelago objective sitting in the save - then sweeps "
            + "and rebuilds it.",
            KeyCode.LeftControl);

        completeBishopsKey = Bind(config, "CompleteBishopsKey", KeyCode.F2,
            "Records all four Bishops as beaten and breaks every chain on the Gateway door, so "
            + "the Narinder goal can be tested without a full playthrough. Refuses on any other "
            + "goal, where it would just be a way to win instantly.",
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

        // A shortcut, not the only way in. The viewer is on the Temple Altar menu. Useful because
        // this reaches it from anywhere, including away from the Temple.
        BindFeatureKey(config, "SermonTreeViewerKey", KeyCode.F3,
            "Opens the game's own sermon upgrade tree as a read-only viewer.",
            () => UI.SermonTreeViewer.Open(null),
            KeyCode.LeftControl);

        BindFeatureKey(config, "DumpNamesKey", KeyCode.F4,
            "Writes the internal-name -> display-name table for upgrades, tarot, fleeces, "
            + "crown abilities and doctrines to BepInEx/ap_unlockable_names.txt.",
            DebugActions.DumpUnlockableNames);

        // Scratch harness for the in-world pedestal placement pass. Ctrl+F6 so it doesn't also
        // fire the plain-F6 resource grant.
        BindFeatureKey(config, "SpawnPedestalsKey", KeyCode.F6,
            "Spawns a test row of weapon pedestals in front of the player; press again to clear.",
            DebugActions.SpawnEquipmentPedestals,
            KeyCode.LeftControl);

        BindFeatureKey(config, "GiveResourcesKey", KeyCode.F6,
            "Grants a few resource items (tests filler-item grants).",
            DebugActions.GiveResources);

        // Ctrl+F7 so it doesn't also fire the plain-F7 sermon unlock. Meant to be pressed next to
        // a real podium mid-crusade.
        BindFeatureKey(config, "DumpPodiumsKey", KeyCode.F7,
            "Dumps every weapon podium in the loaded scene - hierarchy, renderers and lit state.",
            DebugActions.DumpPodiumsInScene,
            KeyCode.LeftControl);

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

        Log.LogWarning("[AP] Developer debug keys are compiled into this build. Do not hand it "
            + "to anyone - several of these write save state and send real checks.");
#endif
    }

    private static ConfigEntry<KeyboardShortcut> Bind(
        ConfigFile config, string name, KeyCode key, string description, params KeyCode[] modifiers) =>
        config.Bind("Debug", name, new KeyboardShortcut(key, modifiers), description);

#if AP_DEBUG_KEYS
    private static void BindFeatureKey(
        ConfigFile config, string name, KeyCode key, string description, Action handler,
        params KeyCode[] modifiers)
    {
        bindings.Add((Bind(config, name, key, description, modifiers), handler));
    }
#endif

    internal static void Update()
    {
        // The event-based keys need the plugin's Archipelago reference, which isn't available
        // when bindings is built, so they can't live in that list.
        Fire(debugKey, () => OnDebugKeyPressed?.Invoke());

#if AP_DEBUG_KEYS
        Fire(connectKey, () => OnConnectKeyPressed?.Invoke());
        Fire(questGuideKey, () => OnQuestGuideKeyPressed?.Invoke());
        Fire(completeBishopsKey, () => OnCompleteBishopsKeyPressed?.Invoke());
#endif

        foreach (var (key, handler) in bindings)
        {
            Fire(key, handler);
        }
    }

    /// <summary>
    /// Runs a keybind's handler if it was pressed.
    ///
    /// A debug keybind must never take the game down with it. These call into game APIs that
    /// may not be initialized depending on where the player is, such as the main menu or
    /// mid-crusade.
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
    /// needs the ArchipelagoClient, which lives on the plugin. Never raised without AP_DEBUG_KEYS.
    /// </summary>
    internal static event Action OnQuestGuideKeyPressed;

    /// <summary>Ctrl+F2. Needs the ArchipelagoClient to re-check the goal after writing.</summary>
    internal static event Action OnCompleteBishopsKeyPressed;
}
