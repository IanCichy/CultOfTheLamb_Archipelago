using Archipelago.CultOfTheLamb.Console;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.CultOfTheLamb.Services;
using Archipelago.CultOfTheLamb.UI;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// BepInEx entry point, and the mod's only Update loop. Services do not run their own, because
/// the client's queues have to be drained on the Unity main thread, so all per-frame and once-a-
/// second work is driven from here rather than scattered across MonoBehaviours with undefined
/// ordering.
/// </summary>
[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
public class ArchipelagoPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "io.github.iancichy.archipelago-cultofthelamb";
    public const string PluginName = "Archipelago.CultOfTheLamb";
    // Keep in step with manifest.json, the csproj VersionPrefix, and MOD_VERSION in
    // worlds/cult_of_the_lamb/__init__.py
    public const string PluginVersion = "0.9.5";

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

        // Cleanup needs to be an event subscription, not a patch
        Services.SaveSlotCleanup.Register();

        // Manual: its target is a compiler-generated iterator PatchAll cannot reach by
        // attribute, and a miss should cost a warning rather than the plugin.
        BaseUpgradeGuardPatch.ApplyRoutineFinalizer(harmony);

        // Same reason: its target lives inside a coroutine and a miss should not cost the plugin.
        OnboardingCompatibilityPatches.BossChestFragments.Apply(harmony);

        CreateConfigurations();
        DebugCommands.Init(Config);

        Instance = this;
        AP = new ArchipelagoClient();

        // The panel reads and writes the config entries directly, so the entered values live in
        // one place rather than being copied between the form, statics and the config.
        connectPanel = new ArchipelagoConnectPanel(
            AP, ServerNameEntry, PortEntry, SlotNameEntry, PasswordEntry);
        connectPanel.AttachTo(gameObject.AddComponent<ApPanelHost>());

        ArchipelagoHudIndicator.IsConnected = () => AP.IsConnected;

        MenuButtonPatch.OnArchipelagoButtonPressed += () => connectPanel.Open();
        DebugCommands.OnDebugKeyPressed += () => DebugActions.DumpState(AP);
#if AP_DEBUG_KEYS
        DebugCommands.OnConnectKeyPressed += () => connectPanel.Toggle();
        DebugCommands.OnQuestGuideKeyPressed += () => DebugActions.DumpQuestGuide(AP);
        DebugCommands.OnCompleteBishopsKeyPressed += () => DebugActions.CompleteBishopsAndOpenGateway(AP);
        DebugCommands.OnDeathLinkKeyPressed += () => DebugActions.SimulateDeathLink(AP);
#endif
        AP.OnClientDisconnect += AP_OnClientDisconnect;
        ArchipelagoConsoleCommand.OnArchipelagoCommandCalled += ArchipelagoConsoleCommand_OnArchipelagoCommandCalled;
        ArchipelagoConsoleCommand.OnArchipelagoDisconnectCommandCalled += () => AP.Disconnect();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    public void Update()
    {
        DebugCommands.Update();

#if AP_DEBUG_KEYS
        GodMode.Tick();
#endif

        AP?.ItemLogic?.ProcessQueue();

        // Work handed over from the websocket thread, such as teardown reaching into save data.
        MainThreadQueue.Drain();

        // Unthrottled and cheap: returns immediately unless a shop is waiting to be marked.
        AP?.ShopIconService?.Tick();

        // Notifications raised while the HUD was hidden wait here until the game will show them.
        ApNotification.Flush();

        // Re-attaches itself after each scene load, since the HUD is rebuilt with the scene.
        ArchipelagoHudIndicator.EnsureExists();

        // Polled rather than evented: recruit events fire before the Follower data lands, and
        // Snail Shrines are five save booleans with no event at all.
        followerPollTimer += Time.unscaledDeltaTime;
        if (followerPollTimer >= FollowerPollIntervalSeconds)
        {
            followerPollTimer = 0f;
            AP?.FollowerMilestoneService?.Tick();
            AP?.SnailShrineService?.Tick();
            AP?.BroomService?.Tick();
            AP?.GoalService?.Tick();
            AP?.DeathLinkService?.Tick();

            // Hands over the free Divine Inspiration upgrades once the tutorial ends
            AP?.DivineInspirationService?.Tick();

            // Not gated on a session: a held-back Temple upgrade is already in the save, so it
            // has to complete whether or not Archipelago is still connected.
            BaseUpgradeGuardPatch.Tick();

            // A stalled opening is in the save either way, so this is not gated on a session.
            OnboardingCompatibilityPatches.Tick();

            // Re-places the base podiums after a scene load and lights newly arrived families.
            AP?.EquipmentDisplayService?.Tick();

            // GameManager.Awake re-seeds fifteen cards whenever it finds the collection empty,
            // so managed ones have to be taken back out again.
            AP?.TarotService?.Tick();

            // Re-registers a term only when the composed line changed, so this is cheap.
            AP?.QuestGuideService?.Tick();

            // While disconnected instead, to hand back anything a session that ended in a crash
            // or an alt-F4 never returned. Quiet unless something went wrong.
            if (AP == null || !AP.IsConnected)
            {
                ManagedCollection<TarotCards.Card>.SettleIfOwed(
                    TarotCollectionBacking.Key,
                    new TarotCollectionBacking(),
                    TarotCollectionBacking.Noun,
                    TarotCollectionBacking.LegacyKey);

                // Its objectives live in the save file, so a crash would otherwise leave
                // Archipelago lines in a vanilla quest log permanently.
                QuestGuideService.SweepLoadedSaveOnce();
            }
        }
    }

    private const float FollowerPollIntervalSeconds = 1f;
    private float followerPollTimer;

    // The single place a connection starts, whether from the panel, a keybind or the console.
    private void ArchipelagoConsoleCommand_OnArchipelagoCommandCalled(
        string url, int port, string slot, string password)
    {
        Log.LogDebug($"Connecting to {url}:{port} as {slot}");

        // An explicit connect supersedes a pending retry. Otherwise the retry loop would keep
        // dialling the old details underneath the ones just typed in.
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

        // Weapons and curses are the only randomized system the game cannot show natively, so
        // the mod puts crusade podiums in the base. Ctrl+F6 prints your current coordinates.
        PedestalsEnabled = Config.Bind("Displays", "EquipmentPedestals", true,
            "Show weapon and curse podiums in the base.");
        // Each display anchors itself. Measuring them off the end of the weapon row made the
        // layout move with the seed, shifting when Woolhaven added a seventh weapon or when
        // weapons were not randomized at all.
        WeaponOriginX = Config.Bind("Displays", "WeaponOriginX", -16.64f,
            "World X of the first weapon podium.");
        WeaponOriginY = Config.Bind("Displays", "WeaponOriginY", -28.52f,
            "World Y of the weapon row.");
        CurseOriginX = Config.Bind("Displays", "CurseOriginX", 6.36f,
            "World X of the first curse podium.");
        CurseOriginY = Config.Bind("Displays", "CurseOriginY", -28.52f,
            "World Y of the curse row.");
        BookOriginX = Config.Bind("Displays", "BookOriginX", 0.86f,
            "World X of the collection book.");
        BookOriginY = Config.Bind("Displays", "BookOriginY", -28.52f,
            "World Y of the collection book.");
        PedestalSpacing = Config.Bind("Displays", "PedestalSpacing", 2f,
            "Distance between podiums within a row.");

        // Its own toggle rather than riding on EquipmentPedestals: the podiums report what
        // Archipelago granted, while this is the game's own collection UI, useful in any seed.
        CollectionBookEnabled = Config.Bind("Displays", "CollectionBook", true,
            "Show the relic and tarot collection book in the base.");
    }

    internal static ConfigEntry<bool> PedestalsEnabled { get; private set; }
    internal static ConfigEntry<float> WeaponOriginX { get; private set; }
    internal static ConfigEntry<float> WeaponOriginY { get; private set; }
    internal static ConfigEntry<float> CurseOriginX { get; private set; }
    internal static ConfigEntry<float> CurseOriginY { get; private set; }
    internal static ConfigEntry<float> BookOriginX { get; private set; }
    internal static ConfigEntry<float> BookOriginY { get; private set; }
    internal static ConfigEntry<float> PedestalSpacing { get; private set; }
    internal static ConfigEntry<bool> CollectionBookEnabled { get; private set; }
}
