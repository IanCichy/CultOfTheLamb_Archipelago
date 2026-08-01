using Archipelago.CultOfTheLamb.Console;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.CultOfTheLamb.UI;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Archipelago.CultOfTheLamb;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
public class ArchipelagoPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "io.github.iancichy.archipelago-cultofthelamb";
    public const string PluginAuthor = "IanCichy";
    public const string PluginName = "Archipelago.CultOfTheLamb";
    public const string PluginVersion = "0.1.0";

    internal static ArchipelagoPlugin Instance { get; private set; }

    public static ConfigEntry<string> SlotNameEntry { get; set; }
    public static ConfigEntry<string> ServerNameEntry { get; set; }
    public static ConfigEntry<int> PortEntry { get; set; }
    public static ConfigEntry<string> PasswordEntry { get; set; }

    private ArchipelagoClient AP;
    private ArchipelagoConnectPanel connectPanel;
    private Harmony harmony;
    private bool isReconnecting;

    public void Awake()
    {
        Log.Init(Logger);

        harmony = new Harmony(PluginGUID);
        harmony.PatchAll();

        CreateConfigurations();
        DebugCommands.Init(Config);

        Instance = this;
        AP = new ArchipelagoClient();

        // The panel reads and writes the config entries itself, so the entered values live in
        // exactly one place rather than being copied between the form, a set of statics and the
        // config on every connect.
        connectPanel = new ArchipelagoConnectPanel(
            AP, ServerNameEntry, PortEntry, SlotNameEntry, PasswordEntry);
        connectPanel.AttachTo(gameObject.AddComponent<ArchipelagoConnectPanelHost>());

        ArchipelagoHudIndicator.IsConnected = () => AP.IsConnected;

        MenuButtonPatch.OnArchipelagoButtonPressed += () => connectPanel.Open();
        DebugCommands.OnConnectKeyPressed += () => connectPanel.Toggle();
        DebugCommands.OnDebugKeyPressed += () => DebugActions.DumpState(AP);
        AP.OnClientDisconnect += AP_OnClientDisconnect;
        ArchipelagoConsoleCommand.OnArchipelagoCommandCalled += ArchipelagoConsoleCommand_OnArchipelagoCommandCalled;
        ArchipelagoConsoleCommand.OnArchipelagoDisconnectCommandCalled += () => AP.Disconnect();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    public void Update()
    {
        DebugCommands.Update();
        AP?.ItemLogic?.ProcessQueue();

        // Unthrottled, and cheap: it returns immediately unless a shop is waiting to be marked,
        // which only happens for a few frames after walking into one.
        AP?.ShopIconService?.Tick();

        // Notifications raised while the HUD was hidden - during a cutscene or a menu - wait
        // here until the game is willing to show them.
        ApNotification.Flush();

        // Re-attaches itself after each scene load, since the HUD is rebuilt with the scene.
        ArchipelagoHudIndicator.EnsureExists();

        // Throttled polling for the two systems derived from save state: Follower counts
        // (recruit events fire before the data lands) and Snail Shrines (five save booleans
        // with no event at all). Once a second is far more often than either can change, and
        // both checks are a handful of field reads.
        followerPollTimer += Time.unscaledDeltaTime;
        if (followerPollTimer >= FollowerPollIntervalSeconds)
        {
            followerPollTimer = 0f;
            AP?.FollowerMilestoneService?.Tick();
            AP?.SnailShrineService?.Tick();
        }
    }

    private const float FollowerPollIntervalSeconds = 1f;
    private float followerPollTimer;

    /// <summary>
    /// The single place a connection starts, whatever asked for it - the panel, a keybind, or a
    /// console command. ArchipelagoConsoleCommand exists to be exactly this seam.
    /// </summary>
    private void ArchipelagoConsoleCommand_OnArchipelagoCommandCalled(string url, int port, string slot, string password)
    {
        Log.LogDebug($"Connecting to {url}:{port} as {slot}");
        StartCoroutine(AP.ConnectRoutine($"{url}:{port}", slot, password));
    }

    private void AP_OnClientDisconnect(string reason)
    {
        Log.LogWarning($"Archipelago client was disconnected from the server: {reason}");

        if (!isReconnecting && AP.reconnecting)
        {
            isReconnecting = true;
            StartCoroutine(ReconnectAndReset());
        }
    }

    private System.Collections.IEnumerator ReconnectAndReset()
    {
        yield return StartCoroutine(AP.AttemptReconnection());
        isReconnecting = false;
    }

    private void CreateConfigurations()
    {
        SlotNameEntry = Config.Bind("Archipelago", "SlotName", "", "Change the default slot name");
        ServerNameEntry = Config.Bind("Archipelago", "ServerName", "archipelago.gg", "Change the default server name");
        PortEntry = Config.Bind("Archipelago", "Port", 38281, "Change the default port");
        PasswordEntry = Config.Bind("Archipelago", "Password", "", "Change the default password");
    }
}
