using Archipelago.CultOfTheLamb.Console;
using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    /// <summary>
    /// Whether an attempt is in flight. This is the only thing about the connection that
    /// *isn't* already answerable: IsConnected is computed from the socket and so can't go
    /// stale, LastError says whether the last attempt failed, and a UI derives everything it
    /// shows from those three. Tracking a parallel status enum alongside them only creates a
    /// second answer to "am I connected" that agrees by convention.
    /// </summary>
    public bool Connecting { get; private set; }

    /// <summary>Why the last attempt failed, in words a player can act on. Null when fine.</summary>
    public string LastError { get; private set; }

    /// <summary>
    /// Connects without blocking the game.
    ///
    /// The synchronous version of this froze the main thread for as long as the login took,
    /// which was tolerable when connecting meant pressing a key you already knew worked. Behind
    /// a form where people mistype addresses, it's a multi-second hang with nothing on screen.
    ///
    /// Drive it with StartCoroutine from a MonoBehaviour.
    /// </summary>
    public IEnumerator ConnectRoutine(string url, string slotName, string password = null)
    {
        lastServerUrl = url;
        lastSlotName = slotName;
        lastPassword = password;

        if (IsConnected)
        {
            Log.LogInfo("[AP] Reusing existing Archipelago session.");
            yield break;
        }

        Log.LogInfo($"[AP] Attempting to connect to Archipelago at {url}.");
        Connecting = true;
        LastError = null;

        LoginResult result = null;
        Exception thrown = null;
        var finished = new StrongBox<bool>();

        // ConnectToServer touches no Unity API, which is what makes this safe to run off the
        // main thread; ProcessLoginResult below very much does, so it stays on it.
        //
        // A dedicated thread rather than Task.Run: this parks on a blocking socket call for as
        // long as the timeout, and tying up a thread-pool worker for seconds is what pools are
        // worst at. IsBackground so a hung connect to a mistyped address - now the expected
        // failure, not a dev typo - can't keep the process alive at quit.
        new Thread(() =>
        {
            try
            {
                result = ConnectToServer(url, slotName, password);
            }
            catch (Exception e)
            {
                thrown = e;
            }
            Volatile.Write(ref finished.Value, true);
        })
        {
            IsBackground = true,
            Name = "Archipelago connect",
        }.Start();

        // The coroutine only ever polls this, never blocks on it, so a synchronisation
        // primitive would be doing nothing - a write the main thread is guaranteed to observe
        // is the whole requirement. Boxed because a captured local can't be declared volatile,
        // and kept local rather than a field so two overlapping connects can't share it.
        while (!Volatile.Read(ref finished.Value))
        {
            yield return null;
        }

        Connecting = false;

        if (thrown != null)
        {
            Fail($"Could not reach {url}: {thrown.Message}");
            yield break;
        }

        if (result == null)
        {
            Fail($"Could not reach {url}.");
            yield break;
        }

        ProcessLoginResult(result);
    }

    private void Fail(string reason)
    {
        LastError = reason;
        Connecting = false;
        Log.LogWarning($"[AP] {reason}");
        OnClientDisconnect?.Invoke(reason);
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

            // The server's own wording is the useful part - "Slot not found", a password
            // mismatch, an incompatible version - so pass it through rather than flattening
            // every refusal into one generic message.
            LastError = failureResult.Errors.Length > 0
                ? string.Join(" ", failureResult.Errors)
                : "The server refused the connection.";
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

        // Before ItemLogic: registering empties the collection, and ItemLogic's backlog drain
        // immediately replays whatever the player has already been sent back into it.
        if (GetBool(successResult.SlotData, "randomizeTarotCards"))
        {
            TarotService = new TarotService(
                session,
                TarotService.ParseCards(successResult.SlotData),
                TarotService.ParseCardLocations(successResult.SlotData),
                TarotService.ParseStartingCards(successResult.SlotData));
            TarotService.Register();
        }

        ItemLogic = new ArchipelagoItemLogicController(
            session, RegionUnlockService, SermonService, TarotService);
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
            // Skip a malformed entry rather than throwing. This runs during connect, so an
            // exception costs the whole session instead of the one shop check we can't read.
            try
            {
                result[entry.Key] = entry.Value.ToObject<long>();
            }
            catch (Exception e)
            {
                Log.LogWarning("[AP] Slot data has a non-numeric location id for tarot shop "
                    + $"slot '{entry.Key}': {e.Message} - that slot won't send a check.");
            }
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
        TarotService?.Unregister();
        TarotService = null;
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
        // Asked for, so there's nothing to report - this is what distinguishes a clean
        // disconnect from a failed one.
        LastError = null;
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

        // Dropped rather than asked to stop, so this is genuinely the last error. No need to
        // suppress it while reconnecting: "an attempt is in flight" outranks it wherever the
        // two are displayed together.
        LastError = reason;
        OnClientDisconnect?.Invoke(reason);
    }

    public IEnumerator AttemptReconnection()
    {
        Log.LogDebug("Attempting to reconnect!");

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            Log.LogInfo($"[AP] Reconnection attempt #{attempt}");
            yield return new WaitForSeconds(3f);

            // Same routine the panel's Connect button uses - one code path for "talk to the
            // server", so a fix to either can't drift away from the other.
            yield return ConnectRoutine(lastServerUrl, lastSlotName, lastPassword);

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
        LastError = "Lost the connection and could not get it back after 5 attempts.";
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
