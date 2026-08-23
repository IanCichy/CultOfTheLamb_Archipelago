using Archipelago.CultOfTheLamb.Console;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.CultOfTheLamb.Services;
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
    // Keep in step with manifest.json, the csproj VersionPrefix, and MOD_VERSION in the apworld's
    // worlds/cult_of_the_lamb/__init__.py - the last of those is what a tester's log compares against.
    public const string PluginVersion = "0.9.0";

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

        // Manual, because its target is a compiler-generated iterator that PatchAll can't reach by
        // attribute - and because a resolution failure there should cost a warning, not the plugin.
        BaseUpgradeGuardPatch.ApplyRoutineFinalizer(harmony);

        CreateConfigurations();
        DebugCommands.Init(Config);

        Instance = this;
        AP = new ArchipelagoClient();

        // The panel reads and writes the config entries itself, so the entered values live in
        // exactly one place rather than being copied between the form, a set of statics and the
        // config on every connect.
        connectPanel = new ArchipelagoConnectPanel(
            AP, ServerNameEntry, PortEntry, SlotNameEntry, PasswordEntry);
        connectPanel.AttachTo(gameObject.AddComponent<ApPanelHost>());

        ArchipelagoHudIndicator.IsConnected = () => AP.IsConnected;

        MenuButtonPatch.OnArchipelagoButtonPressed += () => connectPanel.Open();
        DebugCommands.OnConnectKeyPressed += () => connectPanel.Toggle();
        DebugCommands.OnDebugKeyPressed += () => DebugActions.DumpState(AP);
        DebugCommands.OnQuestGuideKeyPressed += () => DebugActions.DumpQuestGuide(AP);
        DebugCommands.OnCompleteBishopsKeyPressed += () => DebugActions.CompleteBishopsAndOpenGateway(AP);
        AP.OnClientDisconnect += AP_OnClientDisconnect;
        ArchipelagoConsoleCommand.OnArchipelagoCommandCalled += ArchipelagoConsoleCommand_OnArchipelagoCommandCalled;
        ArchipelagoConsoleCommand.OnArchipelagoDisconnectCommandCalled += () => AP.Disconnect();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    public void Update()
    {
        DebugCommands.Update();
        AP?.ItemLogic?.ProcessQueue();

        // Work handed over from the websocket thread - teardown reaching into save data.
        MainThreadQueue.Drain();

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
            AP?.BroomService?.Tick();

            // Runs a Temple upgrade that was held back because the base wasn't live. Not gated on
            // a session: the upgrade is already in the save by then, so it has to complete whether
            // or not Archipelago is still connected.
            BaseUpgradeGuardPatch.Tick();

            // Re-places the base podiums after a scene load and lights one whose family has just
            // arrived. Same once-a-second budget: a few dictionary reads in the steady state.
            AP?.EquipmentDisplayService?.Tick();

            // Takes back any managed card the game has put into the collection since the last
            // tick - GameManager.Awake re-seeds fifteen of them whenever it finds the list
            // empty - and re-revokes from scratch if a different save has been loaded.
            AP?.TarotService?.Tick();

            // Adds the objective checklist once a save is loaded, then keeps its counters
            // current. Cheap in a steady state: it re-registers a term only when the composed
            // line actually changed.
            AP?.QuestGuideService?.Tick();

            // While disconnected instead: hands back anything a session that ended in a crash
            // or an alt-F4 never got the chance to return. Nothing to find after a clean
            // disconnect, so this is quiet unless something actually went wrong.
            if (AP == null || !AP.IsConnected)
            {
                ManagedCollection<TarotCards.Card>.SettleIfOwed(
                    TarotCollectionBacking.Key,
                    new TarotCollectionBacking(),
                    TarotCollectionBacking.Noun,
                    TarotCollectionBacking.LegacyKey);

                // Same idea for the objective guide. Its objectives live in the save file, so a
                // crash or alt-F4 mid-session would otherwise leave Archipelago lines in a
                // vanilla quest log permanently. Once per loaded save, not once a second.
                QuestGuideService.SweepLoadedSaveOnce();
            }
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

        // An explicit connect supersedes a pending retry - otherwise the retry loop would keep
        // dialling the *old* details underneath the ones just typed in.
        AP.StopReconnecting();

        StartCoroutine(AP.ConnectRoutine($"{url}:{port}", slot, password));
    }

    private void AP_OnClientDisconnect(string reason)
    {
        Log.LogWarning($"Archipelago client was disconnected from the server: {reason}");

        if (!isReconnecting && AP.Reconnecting)
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

        // Weapons and curses are the only randomized system the game can't show natively, so the
        // mod puts a row of crusade podiums in the base for them. Position is configurable because
        // where they look right is a judgement call - Ctrl+F6 prints your current coordinates.
        PedestalsEnabled = Config.Bind("Displays", "EquipmentPedestals", true,
            "Show weapon and curse podiums in the base.");
        PedestalOriginX = Config.Bind("Displays", "PedestalOriginX", -16.64f,
            "World X of the first weapon podium.");
        PedestalOriginY = Config.Bind("Displays", "PedestalOriginY", -28.52f,
            "World Y of the podium line.");
        PedestalSpacing = Config.Bind("Displays", "PedestalSpacing", 2f,
            "Distance between podiums.");
        PedestalPoolGap = Config.Bind("Displays", "PedestalPoolGap", 9f,
            "Extra space between the last weapon and the first curse.");
    }

    internal static ConfigEntry<bool> PedestalsEnabled { get; private set; }
    internal static ConfigEntry<float> PedestalOriginX { get; private set; }
    internal static ConfigEntry<float> PedestalOriginY { get; private set; }
    internal static ConfigEntry<float> PedestalSpacing { get; private set; }
    internal static ConfigEntry<float> PedestalPoolGap { get; private set; }
}
