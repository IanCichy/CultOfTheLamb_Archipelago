using Archipelago.CultOfTheLamb.Console;
using Archipelago.CultOfTheLamb.UI;
using BepInEx;
using BepInEx.Configuration;

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

    internal static string apServerUri = "archipelago.gg";
    internal static int apServerPort = 38281;
    internal static string apSlotName = "";
    internal static string apPassword;

    private ArchipelagoClient AP;
    private bool isReconnecting;

    public void Awake()
    {
        Log.Init(Logger);

        CreateConfigurations();
        DebugCommands.Init(Config);

        apSlotName = SlotNameEntry.Value;
        apServerUri = ServerNameEntry.Value;
        apServerPort = PortEntry.Value;
        apPassword = PasswordEntry.Value;

        Instance = this;
        AP = new ArchipelagoClient();

        ArchipelagoConnectButtonController.OnConnectClick += OnClick_ConnectToArchipelago;
        AP.OnClientDisconnect += AP_OnClientDisconnect;
        ArchipelagoConsoleCommand.OnArchipelagoCommandCalled += ArchipelagoConsoleCommand_OnArchipelagoCommandCalled;
        ArchipelagoConsoleCommand.OnArchipelagoDisconnectCommandCalled += () => AP.Disconnect();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    public void Update()
    {
        DebugCommands.Update();
        AP?.ItemLogic?.ProcessQueue();
    }

    private void OnClick_ConnectToArchipelago()
    {
        if (AP.IsConnected)
        {
            AP.Disconnect();
            return;
        }

        var url = $"{apServerUri}:{apServerPort}";
        Log.LogDebug($"Connecting to {url} as {apSlotName}");
        AP.Connect(url, apSlotName, apPassword);
        SlotNameEntry.Value = apSlotName;
    }

    private void ArchipelagoConsoleCommand_OnArchipelagoCommandCalled(string url, int port, string slot, string password)
    {
        AP.Connect($"{url}:{port}", slot, password);
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
