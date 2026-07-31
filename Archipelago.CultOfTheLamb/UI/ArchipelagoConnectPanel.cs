using System;
using Archipelago.CultOfTheLamb.Console;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// The connection form: server, port, slot, password, and a Connect button.
///
/// Replaces editing a config file and restarting the game, which is what connecting used to
/// require. That was fine for the two of us and a hard stop for anyone else.
///
/// Drawn with IMGUI rather than the game's own UI. It looks like a mod, not like Cult of the
/// Lamb - deliberately, for now: a native form means repurposing a game prefab's private
/// serialized fields, and doing that badly takes the game's UI down with it. The entry points
/// in the pause and main menus are native, so the form behind them can be upgraded later
/// without players having to learn a new place to look.
/// </summary>
internal class ArchipelagoConnectPanel
{
    private readonly ArchipelagoClient client;

    // The panel is the only thing that reads or writes these, so the form *is* the persistence
    // layer - there's no third copy of the values to keep in step, and the defaults live once,
    // in CreateConfigurations.
    private readonly ConfigEntry<string> serverEntry;
    private readonly ConfigEntry<int> portEntry;
    private readonly ConfigEntry<string> slotEntry;
    private readonly ConfigEntry<string> passwordEntry;

    internal bool IsOpen { get; private set; }

    private string server;
    private string port;
    private string slot;
    private string password;

    // Whether *we* froze the player, so closing can't un-freeze something else - a cutscene, a
    // shop purchase - that happened to start while the panel was open.
    private bool frozePlayer;

    private Rect window = new(60f, 60f, 460f, 0f);
    private GUIStyle labelStyle;
    private GUIStyle statusStyle;

    // IMGUI's default font is unreadably small on anything modern. Everything is authored at
    // this nominal size and scaled to the actual screen, so layout maths stays in one space.
    private const float DesignHeight = 1080f;

    internal ArchipelagoConnectPanel(
        ArchipelagoClient client,
        ConfigEntry<string> serverEntry,
        ConfigEntry<int> portEntry,
        ConfigEntry<string> slotEntry,
        ConfigEntry<string> passwordEntry)
    {
        this.client = client;
        this.serverEntry = serverEntry;
        this.portEntry = portEntry;
        this.slotEntry = slotEntry;
        this.passwordEntry = passwordEntry;

        server = serverEntry.Value;
        port = portEntry.Value.ToString();
        slot = slotEntry.Value;
        password = passwordEntry.Value;
    }

    internal void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    internal void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        if (host != null) host.enabled = true;
        FreezePlayer();
        SuspendGameUi();
    }

    internal void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (host != null) host.enabled = false;
        UnfreezePlayer();
        RestoreGameUi();
    }

    /// <summary>
    /// The component whose OnGUI draws this. Disabled whenever the panel is closed, so Unity's
    /// IMGUI dispatch costs nothing for the ~99% of a session the panel isn't up.
    /// </summary>
    internal void AttachTo(ArchipelagoConnectPanelHost host)
    {
        this.host = host;
        host.Panel = this;
        host.enabled = IsOpen;
    }

    private ArchipelagoConnectPanelHost host;

    /// <summary>Called from the host's OnGUI.</summary>
    internal void Draw()
    {
        if (!IsOpen) return;

        var scale = Screen.height / DesignHeight;
        var previousMatrix = GUI.matrix;

        // Scaling the whole matrix rather than each font size keeps hit-testing correct: mouse
        // positions run through the same transform the drawing does.
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        window = GUILayout.Window(GetHashCode(), window, DrawContents, "Archipelago");

        GUI.matrix = previousMatrix;
    }

    private void DrawContents(int id)
    {
        EnsureStyles();

        GUILayout.Space(4f);
        GUILayout.Label(StatusText(), statusStyle);
        GUILayout.Space(8f);

        server = Field("Server", server);
        port = Field("Port", port);
        slot = Field("Slot name", slot);
        password = Field("Password", password, secret: true);

        GUILayout.Space(10f);

        var connecting = IsBusy();
        var canConnect = !connecting && CanConnectHere() && slot.Trim().Length > 0;

        GUILayout.BeginHorizontal();

        GUI.enabled = canConnect;
        if (GUILayout.Button(connecting ? "Connecting..." : "Connect", GUILayout.Height(34f)))
        {
            Save();
            ArchipelagoConsoleCommand.Connect(server.Trim(), ParsePort(), slot.Trim(), password);
        }

        GUI.enabled = client.IsConnected;
        if (GUILayout.Button("Disconnect", GUILayout.Height(34f)))
        {
            ArchipelagoConsoleCommand.Disconnect();
        }

        GUI.enabled = true;
        if (GUILayout.Button("Close", GUILayout.Height(34f)))
        {
            Close();
        }

        GUILayout.EndHorizontal();

        if (!CanConnectHere())
        {
            GUILayout.Space(6f);
            GUILayout.Label(
                "Load a save first - Archipelago writes unlocks into save data, so there has to "
                + "be a save to write into. Your details are kept.",
                labelStyle);
        }

        GUILayout.Space(4f);

        // Only the title bar drags, so the fields underneath stay clickable.
        GUI.DragWindow(new Rect(0f, 0f, window.width, 24f));
    }

    private string Field(string label, string value, bool secret = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(110f));

        var result = secret
            ? GUILayout.PasswordField(value ?? string.Empty, '*', GUILayout.Height(26f))
            : GUILayout.TextField(value ?? string.Empty, GUILayout.Height(26f));

        GUILayout.EndHorizontal();
        return result;
    }

    /// <summary>
    /// An attempt is in flight, whether the player started it or a dropped socket did. Ordered
    /// ahead of LastError everywhere it's used, so a retry reads as "still trying" rather than
    /// flickering the previous failure between attempts.
    /// </summary>
    private bool IsBusy() => client.Connecting || client.reconnecting;

    private string StatusText()
    {
        if (client.IsConnected) return $"Connected as {ArchipelagoClient.ConnectedPlayerName}.";
        if (IsBusy()) return "Connecting...";

        return client.LastError == null ? "Not connected." : $"Not connected. {client.LastError}";
    }

    /// <summary>
    /// Connecting replays the whole item history into save state and unlocks regions through
    /// DataManager, so there has to be a loaded save underneath. At the main menu there isn't -
    /// the form still works, it just can't finish the job.
    /// </summary>
    private static bool CanConnectHere() =>
        DataManager.Instance != null && PlayerFarming.Instance != null;

    /// <summary>
    /// Remembers what was typed, so a returning player doesn't retype it - and so a typo worth
    /// correcting is still there when the panel is reopened. Saved on the attempt rather than on
    /// success for exactly that reason.
    /// </summary>
    private void Save()
    {
        serverEntry.Value = server.Trim();
        portEntry.Value = ParsePort();
        slotEntry.Value = slot.Trim();
        passwordEntry.Value = password;
    }

    /// <summary>Falls back to the last saved port rather than a literal, so the default lives once.</summary>
    private int ParsePort() => int.TryParse(port, out var parsed) ? parsed : portEntry.Value;

    /// <summary>
    /// Stops the lamb reacting to typing. Without it, entering a slot name walks the player
    /// across the room and can trigger interactions.
    /// </summary>
    private void FreezePlayer()
    {
        if (PlayerFarming.Instance == null) return;

        try
        {
            PlayerFarming.SetStateForAllPlayers(StateMachine.State.InActive, false, null);
            frozePlayer = true;
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not freeze the player for the connect panel: {e.Message}");
        }
    }

    private void UnfreezePlayer()
    {
        if (!frozePlayer) return;
        frozePlayer = false;

        if (PlayerFarming.Instance == null) return;

        try
        {
            PlayerFarming.SetStateForAllPlayers(StateMachine.State.Idle, false, null);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not restore the player after the connect panel: {e.Message}");
        }
    }

    /// <summary>
    /// Switches off Unity's EventSystem while the panel is up.
    ///
    /// IMGUI and the game's UI take input through completely separate paths, so a click inside
    /// this window *also* lands on whatever menu button sits behind it - the panel is usually
    /// opened from the pause menu, which is exactly where that would happen. Turning the
    /// EventSystem off makes the menu behind inert until the panel closes, and stops arrow keys
    /// walking its selection while you type.
    /// </summary>
    private void SuspendGameUi()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null || !eventSystem.enabled) return;

        eventSystem.enabled = false;
        suspendedEventSystem = eventSystem;
    }

    private void RestoreGameUi()
    {
        if (suspendedEventSystem == null) return;

        suspendedEventSystem.enabled = true;
        suspendedEventSystem = null;
    }

    private EventSystem suspendedEventSystem;

    private void EnsureStyles()
    {
        if (labelStyle != null) return;

        labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
        statusStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold };
    }
}
