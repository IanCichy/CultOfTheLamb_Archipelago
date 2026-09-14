using Archipelago.CultOfTheLamb.Console;
using BepInEx.Configuration;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// The connection form, meaning server, port, slot, password, and a Connect button.
/// </summary>
/// <remarks>
/// Drawn with IMGUI rather than the game's own UI, so it looks like a mod. A native form means
/// repurposing a prefab's private serialized fields, and doing that badly takes the game's UI
/// down with it. The menu entry points are native, so the form behind them can be upgraded
/// later without moving where players look.
/// </remarks>
internal class ArchipelagoConnectPanel : ApPanelBase
{
    private readonly ArchipelagoClient client;

    // The panel is the only thing that reads or writes these, so the form is the persistence
    // layer. There's no third copy of the values to keep in step, and the defaults live once,
    // in CreateConfigurations.
    private readonly ConfigEntry<string> serverEntry;
    private readonly ConfigEntry<int> portEntry;
    private readonly ConfigEntry<string> slotEntry;
    private readonly ConfigEntry<string> passwordEntry;

    private string server;
    private string port;
    private string slot;
    private string password;

    // Anything stable and unlikely to collide will do. GetHashCode varies per run and per
    // instance for no benefit.
    protected override int WindowId => 0x0AA7E1;

    protected override string Title => "Archipelago";

    private GUIStyle labelStyle;
    private GUIStyle statusStyle;

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

    protected override void DrawContents(int id)
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

        // Read once, because the button's enabled state and the line explaining it must agree,
        // and IMGUI needs the Layout and Repaint passes to agree with each other too.
        var canConnectHere = HasLoadedSave();

        // Not "is a retry pending". The retry loop is unbounded, so gating Connect
        // on it would disable the button for as long as the server stayed down. Only an attempt
        // actually in flight blocks a new one, and connecting manually cancels the loop.
        var connecting = client.Connecting;
        var canConnect = !connecting && canConnectHere && slot.Trim().Length > 0;

        GUILayout.BeginHorizontal();

        GUI.enabled = canConnect;
        if (GUILayout.Button(connecting ? "Connecting..." : "Connect", GUILayout.Height(34f)))
        {
            Save();
            ArchipelagoConsoleCommand.Connect(server.Trim(), ParsePort(), slot.Trim(), password);
        }

        // Enabled while retrying too, where it means "stop retrying". That is the only way to
        // end an unbounded loop from the UI.
        GUI.enabled = client.IsConnected || client.Reconnecting;
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

        if (!canConnectHere)
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

    // Both in-flight states are reported ahead of LastError, so a retry reads as "still trying"
    // rather than flickering the previous failure between attempts. The retry line names the
    // attempt number, or a thirty-second backoff looks the same as a wedged client
    private string StatusText()
    {
        if (client.IsConnected)
        {
            return $"Connected as {ArchipelagoClient.ConnectedPlayerName}.";
        }

        if (client.Connecting)
        {
            return "Connecting...";
        }

        if (client.Reconnecting)
        {
            return $"Lost the connection - retrying (attempt {client.ReconnectAttempt}). "
                + "Connect retries now; Disconnect stops.";
        }

        return client.LastError == null ? "Not connected." : $"Not connected. {client.LastError}";
    }

    // Writes what was typed back into the config entries. Saved on the attempt rather than on
    // success, so a typo worth correcting is still there when the panel is reopened
    private void Save()
    {
        serverEntry.Value = server.Trim();
        portEntry.Value = ParsePort();
        slotEntry.Value = slot.Trim();
        passwordEntry.Value = password;
    }

    // The port as typed. Falls back to the last saved port rather than a literal, so the
    // default lives once
    private int ParsePort() => int.TryParse(port, out var parsed) ? parsed : portEntry.Value;

    private void EnsureStyles()
    {
        if (labelStyle != null)
        {
            return;
        }

        labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
        statusStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold };
    }
}
