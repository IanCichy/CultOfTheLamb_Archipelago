using Archipelago.CultOfTheLamb.Console;
using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Connection, slot data parsing, and reconnection. This layer is protocol-level and
/// mostly game-agnostic; game-specific reactions to slot data live in ArchipelagoPlugin
/// and the Services it wires up.
/// </summary>
public partial class ArchipelagoClient
{
    public void Connect(string url, string slotName, string password = null)
    {
        lastServerUrl = url;
        lastSlotName = slotName;
        lastPassword = password;

        if (IsConnected)
        {
            Log.LogInfo("[AP] Reusing existing Archipelago session.");
            return;
        }

        Log.LogInfo($"[AP] Attempting to connect to Archipelago at {url}.");

        var result = ConnectToServer(url, slotName, password);
        if (result == null)
        {
            OnClientDisconnect?.Invoke("Failed to create session.");
            return;
        }

        ProcessLoginResult(result);
    }

    /// <summary>
    /// Network-only connection: creates session and attempts login.
    /// Safe to call from any thread (no Unity API calls).
    /// Returns null if session creation fails.
    /// </summary>
    private LoginResult ConnectToServer(string url, string slotName, string password)
    {
        TeardownSession();

        try
        {
            session = ArchipelagoSessionFactory.CreateSession(url);
        }
        catch (Exception e)
        {
            Log.LogWarning($"Failed to create session: {e.Message}");
            return null;
        }

        // NOTE: this Version is the **Archipelago network protocol version** we claim to
        // speak - NOT this mod's version. The server rejects the login with
        // 'IncompatibleVersion' if it's below its minimum supported client version, so it
        // has to track the AP releases we support (currently generating/hosting on 0.6.6).
        // Don't "helpfully" sync this to manifest.json's mod version.
        return session.TryConnectAndLogin(
            "Cult of the Lamb",
            slotName,
            ItemsHandlingFlags.AllItems,
            new Version(0, 6, 6),
            password: password);
    }

    /// <summary>
    /// Processes a login result. Must be called on the Unity main thread if it ends up
    /// touching any Unity APIs (UI updates, etc.) - currently it doesn't, but game-specific
    /// slot data handling added later probably will.
    /// </summary>
    private void ProcessLoginResult(LoginResult result)
    {
        if (!result.Successful)
        {
            var failureResult = (LoginFailure)result;
            foreach (var err in failureResult.Errors)
            {
                Log.LogError($"[AP] {err}");
            }
            session = null;
            return;
        }

        var successResult = (LoginSuccessful)result;
        Log.LogInfo("[AP] Connected!");

        cachedSlotData = new Dictionary<string, object>(successResult.SlotData);

        // "regionOrder": which region is free at start, followed by the unlock order of
        // the other 3 - set in worlds/cult_of_the_lamb/__init__.py's generate_early() and
        // sent via fill_slot_data(). Comes through as a JArray (Newtonsoft.Json, the
        // library's underlying serializer), not a native List<string>.
        var regionOrder = new List<string>();
        if (successResult.SlotData.TryGetValue("regionOrder", out var regionOrderObj)
            && regionOrderObj is JArray regionOrderArray)
        {
            regionOrder = regionOrderArray.ToObject<List<string>>();
        }
        else
        {
            Log.LogWarning("[AP] No regionOrder in slot data - region unlocking won't work this session.");
        }

        // Win condition, from worlds/cult_of_the_lamb/options.py (Goal / RequiredCount).
        var goal = GoalService.GoalBishops;
        var requiredCount = 4;
        if (successResult.SlotData.TryGetValue("goal", out var goalObj))
        {
            goal = Convert.ToInt32(goalObj);
        }
        if (successResult.SlotData.TryGetValue("requiredCount", out var requiredCountObj))
        {
            requiredCount = Convert.ToInt32(requiredCountObj);
        }
        Log.LogInfo($"[AP] Goal: {(goal == GoalService.GoalWitnesses ? "witnesses" : "bishops")}, required: {requiredCount}");

        ConnectedPlayerName = session.Players.GetPlayerName(session.ConnectionInfo.Slot);

        session.MessageLog.OnMessageReceived += Session_OnMessageReceived;
        session.Socket.SocketClosed += Session_SocketClosed;
        session.Socket.ErrorReceived += Socket_ErrorReceived;
        ArchipelagoConsoleCommand.OnArchipelagoReconnectCommandCalled += ArchipelagoConsoleCommand_OnArchipelagoReconnectCommandCalled;

        LocationCheckService = new LocationCheckService(session);
        LocationCheckService.Register();
        RegionUnlockService = new RegionUnlockService(regionOrder);
        RegionUnlockService.Register();
        GoalService = new GoalService(session, goal, requiredCount);
        GoalService.Register();

        // Sermon randomization is optional per seed; when it's off we leave the vanilla
        // pick-an-upgrade flow completely untouched rather than registering an inert service.
        if (GetBool(successResult.SlotData, "randomizeSermonUpgrades"))
        {
            SermonService = new SermonService(
                session,
                ParseSermonUpgrades(successResult.SlotData),
                GetLong(successResult.SlotData, "sermonLocationBaseId"),
                (int)GetLong(successResult.SlotData, "sermonLocationCount"));
            SermonService.Register();
        }

        if (GetBool(successResult.SlotData, "followerMilestoneChecks"))
        {
            FollowerMilestoneService = new FollowerMilestoneService(
                session,
                GetLong(successResult.SlotData, "followerLocationBaseId"),
                (int)GetLong(successResult.SlotData, "followerLocationCount"));
            FollowerMilestoneService.Register();
        }

        if (GetBool(successResult.SlotData, "tarotShopChecks"))
        {
            var tarotShopLocations = ParseTarotShopLocations(successResult.SlotData);

            TarotShopService = new TarotShopService(session, tarotShopLocations);
            TarotShopService.Register();

            // Same mapping, different job: TarotShopService sends the check, ShopIconService
            // makes the slot look like one beforehand.
            ShopIconService = new ShopIconService(session, tarotShopLocations);
            ShopIconService.Register();
        }

        if (GetBool(successResult.SlotData, "snailShrineChecks"))
        {
            SnailShrineService = new SnailShrineService(
                session,
                GetLong(successResult.SlotData, "snailLocationBaseId"),
                (int)GetLong(successResult.SlotData, "snailLocationCount"));
            SnailShrineService.Register();
        }

        ItemLogic = new ArchipelagoItemLogicController(session, RegionUnlockService, SermonService);
        ItemLogic.Register();
    }

    /// <summary>
    /// Sermon item name -> the UpgradeSystem.Type names it unlocks, in order (see
    /// worlds/cult_of_the_lamb/__init__.py's fill_slot_data). Comes through as a JObject of
    /// JArrays, since Newtonsoft is the library's serializer - not native .NET collections.
    /// </summary>
    private static Dictionary<string, List<string>> ParseSermonUpgrades(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<string, List<string>>();

        if (!slotData.TryGetValue("sermonUpgrades", out var raw) || raw is not JObject mapping)
        {
            Log.LogWarning("[AP] Sermon randomization is on but slot data has no sermonUpgrades "
                + "mapping - sermon items won't grant anything.");
            return result;
        }

        foreach (var entry in mapping)
        {
            if (entry.Value is JArray upgrades)
            {
                result[entry.Key] = upgrades.ToObject<List<string>>();
            }
        }

        return result;
    }

    /// <summary>
    /// TarotCards.Card enum name -> location id. Keyed by enum name because that's what a
    /// BuyEntry exposes; display names differ completely ("The Burning Dead" is Skull).
    /// </summary>
    private static Dictionary<string, long> ParseTarotShopLocations(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<string, long>();

        if (!slotData.TryGetValue("tarotShopLocations", out var raw) || raw is not JObject mapping)
        {
            Log.LogWarning("[AP] Tarot shop checks are on but slot data has no "
                + "tarotShopLocations mapping - shop purchases won't send checks.");
            return result;
        }

        foreach (var entry in mapping)
        {
            result[entry.Key] = entry.Value.ToObject<long>();
        }

        return result;
    }

    private static bool GetBool(IReadOnlyDictionary<string, object> slotData, string key) =>
        slotData.TryGetValue(key, out var value) && Convert.ToBoolean(value);

    private static long GetLong(IReadOnlyDictionary<string, object> slotData, string key) =>
        slotData.TryGetValue(key, out var value) ? Convert.ToInt64(value) : 0L;

    /// <summary>
    /// Unsubscribes session-level events and nulls the session.
    /// Optionally disconnects the socket if still connected.
    /// </summary>
    private void TeardownSession(bool disconnect = false)
    {
        if (session == null) return;

        session.MessageLog.OnMessageReceived -= Session_OnMessageReceived;
        session.Socket.SocketClosed -= Session_SocketClosed;
        session.Socket.ErrorReceived -= Socket_ErrorReceived;
        ArchipelagoConsoleCommand.OnArchipelagoReconnectCommandCalled -= ArchipelagoConsoleCommand_OnArchipelagoReconnectCommandCalled;

        LocationCheckService?.Unregister();
        LocationCheckService = null;
        RegionUnlockService?.Unregister();
        RegionUnlockService = null;
        GoalService?.Unregister();
        GoalService = null;
        SermonService?.Unregister();
        SermonService = null;
        FollowerMilestoneService?.Unregister();
        FollowerMilestoneService = null;
        TarotShopService?.Unregister();
        TarotShopService = null;
        ShopIconService?.Unregister();
        ShopIconService = null;
        SnailShrineService?.Unregister();
        SnailShrineService = null;
        ItemLogic?.Unregister();
        ItemLogic = null;

        if (disconnect && session.Socket.Connected)
        {
            session.Socket.DisconnectAsync();
        }

        session = null;
    }

    public void Dispose()
    {
        TeardownSession(disconnect: true);
    }

    /// <summary>
    /// Intentional disconnect initiated by the user (e.g. console command).
    /// </summary>
    public void Disconnect()
    {
        if (session == null) return;
        Dispose();
        OnClientDisconnect?.Invoke("Disconnected.");
    }

    private void Socket_ErrorReceived(Exception e, string message)
    {
        Log.LogDebug($"Error received: {e}, message: {message}");
        reconnecting = true;
        Session_SocketClosed(message);
    }

    private void Session_SocketClosed(string reason)
    {
        TeardownSession();
        OnClientDisconnect?.Invoke(reason);
    }

    public IEnumerator<WaitForSeconds> AttemptReconnection()
    {
        Log.LogDebug("Attempting to reconnect!");

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            Log.LogInfo($"[AP] Reconnection attempt #{attempt}");
            yield return new WaitForSeconds(3f);

            LoginResult loginResult = null;
            using (var connectSignal = new ManualResetEventSlim(false))
            {
                new Thread(() =>
                {
                    try
                    {
                        loginResult = ConnectToServer(lastServerUrl, lastSlotName, lastPassword);
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"Reconnection attempt {attempt} failed: {ex.Message}");
                    }
                    connectSignal.Set();
                }).Start();

                while (!connectSignal.IsSet)
                    yield return new WaitForSeconds(0.25f);
            }

            if (loginResult != null)
            {
                ProcessLoginResult(loginResult);
            }

            if (IsConnected)
            {
                Log.LogInfo("[AP] Reconnected to Archipelago.");
                reconnecting = false;
                yield break;
            }
        }

        Log.LogError("[AP] Failed to reconnect after 5 attempts.");
        Dispose();
        reconnecting = false;
    }

    private void Session_OnMessageReceived(LogMessage message)
    {
        Log.LogInfo($"[AP] {message}");
    }

    private void ArchipelagoConsoleCommand_OnArchipelagoReconnectCommandCalled()
    {
        reconnecting = true;
        Dispose();
        OnClientDisconnect?.Invoke("Manual reconnect requested.");
    }
}
