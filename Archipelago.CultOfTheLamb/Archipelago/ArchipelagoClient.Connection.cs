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

        // TODO: parse remaining Cult of the Lamb-specific slot data here (goal,
        // requiredCount, victory condition) once LocationCheckService covers enough of the
        // location table to check a real goal against. See
        // ArchipelagoClient.RiskOfRain2's equivalent for the pattern - this project's
        // README links to it as a reference.

        ConnectedPlayerName = session.Players.GetPlayerName(session.ConnectionInfo.Slot);

        session.MessageLog.OnMessageReceived += Session_OnMessageReceived;
        session.Socket.SocketClosed += Session_SocketClosed;
        session.Socket.ErrorReceived += Socket_ErrorReceived;
        ArchipelagoConsoleCommand.OnArchipelagoReconnectCommandCalled += ArchipelagoConsoleCommand_OnArchipelagoReconnectCommandCalled;

        LocationCheckService = new LocationCheckService(session);
        LocationCheckService.Register();
        RegionUnlockService = new RegionUnlockService(regionOrder);
        RegionUnlockService.Register();

        ItemLogic = new ArchipelagoItemLogicController(session, RegionUnlockService);
        ItemLogic.Register();
    }

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
