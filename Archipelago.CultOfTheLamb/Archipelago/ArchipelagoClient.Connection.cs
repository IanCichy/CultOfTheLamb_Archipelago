using Archipelago.CultOfTheLamb.Console;
using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Which retry the reconnect loop is on, or 0 when it isn't running. Shown in the panel so a
    /// player can tell "still trying" from "wedged" - the loop is unbounded, so without a visible
    /// count a long backoff is indistinguishable from nothing happening.
    /// </summary>
    public int ReconnectAttempt { get; private set; }

    /// <summary>
    /// The last attempt reached the server and was turned away, rather than failing to reach it.
    /// The distinction is the reconnect loop's stopping condition: an unreachable server is worth
    /// retrying forever, a refused login never is.
    /// </summary>
    private bool loginRefused;

    /// <summary>Asks the reconnect loop to stop at its next opportunity. See StopReconnecting.</summary>
    private bool cancelReconnect;

    /// <summary>
    /// Connects without blocking the game.
    /// </summary>
    /// <remarks>
    /// The synchronous version of this froze the main thread for as long as the login took,
    /// which was tolerable when connecting meant pressing a key you already knew worked. Behind
    /// a form where people mistype addresses, it's a multi-second hang with nothing on screen.
    ///
    /// Drive it with StartCoroutine from a MonoBehaviour.
    /// </remarks>
    public IEnumerator ConnectRoutine(string url, string slotName, string password = null)
    {
        // A second attempt spawns its own thread and tears down the session the first is still
        // building. Bail before the last* fields are written - AttemptReconnection replays those.
        if (Connecting)
        {
            Log.LogWarning("[AP] A connection attempt is already in flight - ignoring this one.");
            yield break;
        }

        LastServerUrl = url;
        LastSlotName = slotName;
        LastPassword = password;

        if (IsConnected)
        {
            Log.LogInfo("[AP] Reusing existing Archipelago session.");
            yield break;
        }

        Log.LogInfo($"[AP] Attempting to connect to Archipelago at {url}.");
        Connecting = true;
        LastError = null;
        loginRefused = false;

        // Here rather than in ConnectToServer, which runs on the connect thread below: teardown
        // reaches into save data and Unity objects, so it has to stay on the main thread.
        TeardownSession();

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
    /// Processes a login result. Must run on the Unity main thread: it constructs every service,
    /// and those register Harmony hooks and touch Unity APIs as they start.
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

            // Not `session = null`: a refused login leaves a *connected* socket, so that would
            // orphan it and its polling loop. Safe this early - the handlers attach on the
            // success path below, and no service has registered yet.
            TeardownSession(disconnect: true);

            // Only a real ConnectionRefused packet stops the reconnect loop. TryConnectAndLogin
            // reports an unreachable server as a LoginFailure too - "Connection timed out" is one
            // - so treating every failure as a refusal made the loop give up on the one case it
            // exists for. ErrorCodes is populated only from the server's packet; a failure
            // manufactured from an exception leaves it empty.
            loginRefused = failureResult.ErrorCodes is { Length: > 0 };

            // The server's own wording is the useful part - "Slot not found", a password
            // mismatch, an incompatible version - so pass it through rather than flattening
            // every refusal into one generic message.
            LastError = failureResult.Errors.Length > 0
                ? string.Join(" ", failureResult.Errors)
                : "The server refused the connection.";

            // Same as Fail(). The panel happens to work without it because it derives from
            // Connecting and LastError, but a refusal that doesn't raise this is a trap for the
            // next thing that subscribes.
            OnClientDisconnect?.Invoke(LastError);
            return;
        }

        var successResult = (LoginSuccessful)result;
        Log.LogInfo("[AP] Connected!");

        // Before any service is constructed: both mappings are empty until this runs, and the
        // services below read them as soon as they register.
        BossKeyMapping.Populate(successResult.SlotData);
        RegionMapping.Populate(successResult.SlotData);

        // Both halves in one line, because the pair is what matters and a tester's log is how we
        // find out they're mismatched. Not enforced - during alpha we may ship a mismatched pair
        // knowingly, and refusing the connection would be worse than saying so.
        var worldVersion = SlotData.GetString(successResult.SlotData, "worldVersion") ?? "unknown";
        Log.LogInfo($"[AP] Versions: client {ArchipelagoPlugin.PluginVersion}, apworld "
            + $"{worldVersion}." + (worldVersion != ArchipelagoPlugin.PluginVersion
                ? "  <-- MISMATCH: these should match; update whichever is older."
                : string.Empty));

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
        Log.LogInfo(goal == GoalService.GoalNarinder
            ? "[AP] Goal: narinder (required count does not apply)."
            : $"[AP] Goal: {(goal == GoalService.GoalWitnesses ? "witnesses" : "bishops")}, "
                + $"required: {requiredCount}");

        ConnectedPlayerName = session.Players.GetPlayerName(session.ConnectionInfo.Slot);

        session.MessageLog.OnMessageReceived += Session_OnMessageReceived;
        session.Socket.SocketClosed += Session_SocketClosed;
        session.Socket.ErrorReceived += Socket_ErrorReceived;
        ArchipelagoConsoleCommand.OnArchipelagoReconnectCommandCalled += ArchipelagoConsoleCommand_OnArchipelagoReconnectCommandCalled;

        // Before any service, because both the shop panels and the sent-check popups read it.
        // The scout is async, so this only starts the round trip - every reader falls back to
        // the location name until it lands.
        Scouts = new ScoutCache(session, "Cult of the Lamb");
        Scouts.ScoutAll();
        CheckNotifier.Scouts = Scouts;

        // Unconditional: the pacing caps are quality of life, not randomizer settings, so they
        // apply whether or not the matching block is being randomized this seed.
        EconomyService = new EconomyService(
            (int)SlotData.GetLong(successResult.SlotData, "divineInspirationDevotionCap"),
            (int)SlotData.GetLong(successResult.SlotData, "sermonXpCap"),
            (int)SlotData.GetLong(successResult.SlotData, "buildTimeCap"));
        EconomyService.Register();

        LocationCheckService = new LocationCheckService(session);
        LocationCheckService.Register();
        RegionUnlockService = new RegionUnlockService(regionOrder);
        RegionUnlockService.Register();
        GoalService = new GoalService(session, goal, requiredCount);
        GoalService.Register();

        // Sermon randomization is optional per seed; when it's off we leave the vanilla
        // pick-an-upgrade flow completely untouched rather than registering an inert service.
        if (SlotData.GetBool(successResult.SlotData, "randomizeSermonUpgrades"))
        {
            SermonService = new SermonService(
                session,
                ParseSermonUpgrades(successResult.SlotData),
                SlotData.GetLong(successResult.SlotData, "sermonLocationBaseId"),
                (int)SlotData.GetLong(successResult.SlotData, "sermonLocationCount"));
            SermonService.Register();
        }

        if (SlotData.GetBool(successResult.SlotData, "followerMilestoneChecks"))
        {
            FollowerMilestoneService = new FollowerMilestoneService(
                session,
                SlotData.GetLong(successResult.SlotData, "followerLocationBaseId"),
                (int)SlotData.GetLong(successResult.SlotData, "followerLocationCount"));
            FollowerMilestoneService.Register();
        }

        if (SlotData.GetBool(successResult.SlotData, "tarotShopChecks"))
        {
            var tarotShopLocations = ParseTarotShopLocations(successResult.SlotData);

            TarotShopService = new TarotShopService(session, tarotShopLocations);
            TarotShopService.Register();

            // Same mapping, different job: TarotShopService sends the check, ShopIconService
            // makes the slot look like one beforehand.
            ShopIconService = new ShopIconService(session, tarotShopLocations, Scouts);
            ShopIconService.Register();
        }

        if (SlotData.GetBool(successResult.SlotData, "snailShrineChecks"))
        {
            SnailShrineService = new SnailShrineService(
                session,
                SlotData.GetLong(successResult.SlotData, "snailLocationBaseId"),
                (int)SlotData.GetLong(successResult.SlotData, "snailLocationCount"));
            SnailShrineService.Register();
        }

        // Before ItemLogic: registering empties the collection, and ItemLogic's backlog drain
        // immediately replays whatever the player has already been sent back into it.
        if (SlotData.GetBool(successResult.SlotData, "randomizeTarotCards"))
        {
            TarotService = new TarotService(
                session,
                TarotService.ParseCards(successResult.SlotData),
                TarotService.ParseCardLocations(successResult.SlotData),
                TarotService.ParseStartingCards(successResult.SlotData));
            TarotService.Register();
        }

        // Also before ItemLogic, for the same reason: the backlog drain is what rebuilds the
        // granted set, and until it runs the player has been granted nothing.
        // The Legendary option works on its own, so the weapon service also registers when
        // weapons aren't being randomized at all - with nothing managed, it only rolls those.
        var randomizeWeapons = SlotData.GetBool(successResult.SlotData, "randomizeWeapons");
        var legendaryChance = SlotData.GetFloat(successResult.SlotData, "legendaryWeaponChance");

        if (randomizeWeapons || legendaryChance > 0f)
        {
            WeaponPoolService = BuildEquipmentService(
                successResult.SlotData, weapons: true, randomizeWeapons, legendaryChance);
            WeaponPoolService.Register();
        }

        if (SlotData.GetBool(successResult.SlotData, "randomizeCurses"))
        {
            CursePoolService = BuildEquipmentService(successResult.SlotData, weapons: false);
            CursePoolService.Register();
        }

        // After both pools, since it reads what each one manages. Registered whenever either
        // exists - it's the only way to see weapon and curse grants in game.
        if (WeaponPoolService != null || CursePoolService != null)
        {
            EquipmentDisplayService = new EquipmentDisplayService(
                WeaponPoolService, CursePoolService,
                ArchipelagoPlugin.PedestalsEnabled, ArchipelagoPlugin.PedestalOriginX,
                ArchipelagoPlugin.PedestalOriginY, ArchipelagoPlugin.PedestalSpacing,
                ArchipelagoPlugin.PedestalPoolGap,
                // The Teleport curses only exist with Woolhaven - their sermon upgrade sits
                // behind Major_DLC_Sermon_Packs - so without it there's nothing to display.
                SlotData.GetBool(successResult.SlotData, "includeWoolhaven"));
            EquipmentDisplayService.Register();
        }

        if (SlotData.GetBool(successResult.SlotData, "buildingChecks"))
        {
            BuildingService = new BuildingService(
                session, BuildingService.ParseLocations(successResult.SlotData));
            BuildingService.Register();
        }

        if (SlotData.GetBool(successResult.SlotData, "broomChecks"))
        {
            BroomService = new BroomService(
                session,
                SlotData.GetLong(successResult.SlotData, "broomLocationBaseId"),
                (int)SlotData.GetLong(successResult.SlotData, "broomLocationCount"));
            BroomService.Register();
        }

        var diMode = (int)SlotData.GetLong(successResult.SlotData, "divineInspirationMode");
        if (diMode != DivineInspirationService.ModeOff)
        {
            DivineInspirationService = new DivineInspirationService(
                session,
                diMode,
                SlotData.GetLong(successResult.SlotData, "divineInspirationLocationBaseId"),
                (int)SlotData.GetLong(successResult.SlotData, "divineInspirationLocationCount"),
                DivineInspirationService.ParseUpgrades(successResult.SlotData),
                SlotData.GetString(successResult.SlotData, "divineInspirationPointItem"),
                (int)SlotData.GetLong(successResult.SlotData, "divineInspirationShuffle"),
                (int)SlotData.GetLong(successResult.SlotData, "divineInspirationShuffleSeed"),
                DivineInspirationService.ParseProgressive(successResult.SlotData),
                DivineInspirationService.ParseFreeUpgrades(successResult.SlotData));
            DivineInspirationService.Register();
        }

        // Guidance only - creates no location and sends no check. It reads the same slot-data
        // keys the services above were gated on, so it can never advertise a block that isn't
        // actually running this seed.
        if (SlotData.GetBool(successResult.SlotData, "objectiveGuide"))
        {
            QuestGuideService = new QuestGuideService(
                session,
                (GuideHudMode)(int)SlotData.GetLong(successResult.SlotData, "objectiveGuidePinning"),
                goal,
                requiredCount,
                regionOrder,
                successResult.SlotData);
            QuestGuideService.Register();
        }

        var questTrimMode = (int)SlotData.GetLong(successResult.SlotData, "vanillaFollowerQuests");
        if (questTrimMode != QuestTrimService.Unchanged)
        {
            QuestTrimService = new QuestTrimService(questTrimMode);
            QuestTrimService.Register();
        }

        ItemLogic = new ArchipelagoItemLogicController(
            session, RegionUnlockService, SermonService, TarotService,
            WeaponPoolService, CursePoolService, DivineInspirationService);
        ItemLogic.Register();
    }

    /// <summary>
    /// One EquipmentPoolService, weapons or curses. The two halves read four differently-named
    /// slot-data keys and are otherwise identical, so the naming lives here rather than being
    /// duplicated at both call sites.
    /// </summary>
    private EquipmentPoolService BuildEquipmentService(
        IReadOnlyDictionary<string, object> slotData,
        bool weapons,
        bool randomizing = true,
        float legendaryChance = 0f)
    {
        var prefix = weapons ? "weapon" : "curse";
        var starting = weapons ? "startingWeapons" : "startingCurses";

        return new EquipmentPoolService(
            session,
            weapons,
            EquipmentPoolService.ParseFamilies(slotData, prefix + "Items"),
            EquipmentPoolService.ParseLocations(slotData, prefix + "Locations"),
            EquipmentPoolService.ParseStarting(slotData, starting),
            randomizing,
            legendaryChance);
    }

    /// <summary>
    /// Sermon item name -> the UpgradeSystem.Type names it unlocks, in order (see
    /// worlds/cult_of_the_lamb/__init__.py's fill_slot_data). Comes through as a JObject of
    /// JArrays, since Newtonsoft is the library's serializer - not native .NET collections.
    /// </summary>
    private static Dictionary<string, List<string>> ParseSermonUpgrades(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = SlotData.ParseNameLists(slotData, "sermonUpgrades");

        if (result.Count == 0)
        {
            Log.LogWarning("[AP] Sermon randomization is on but slot data has no sermonUpgrades "
                + "mapping - sermon items won't grant anything.");
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
        WeaponPoolService?.Unregister();
        WeaponPoolService = null;
        CursePoolService?.Unregister();
        CursePoolService = null;
        EquipmentDisplayService?.Unregister();
        EquipmentDisplayService = null;
        DivineInspirationService?.Unregister();
        DivineInspirationService = null;
        BuildingService?.Unregister();
        BuildingService = null;
        BroomService?.Unregister();
        BroomService = null;
        QuestGuideService?.Unregister();
        QuestGuideService = null;
        QuestTrimService?.Unregister();
        QuestTrimService = null;
        EconomyService?.Unregister();
        EconomyService = null;
        CheckNotifier.Scouts = null;
        Scouts = null;
        ItemLogic?.Unregister();
        ItemLogic = null;

        // Seed-specific, so they must not survive into the next connection.
        BossKeyMapping.Clear();
        RegionMapping.Clear();

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
        // First, and outside the null-session guard: "disconnect" while a retry is pending means
        // stop retrying, and at that moment there is no session to tear down.
        StopReconnecting();

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
        Reconnecting = true;
        Session_SocketClosed(message);
    }

    /// <summary>
    /// The socket dropped, on the MultiClient websocket thread. The whole body is deferred, not
    /// just the teardown: TeardownSession writes to save-data lists that Update() is iterating,
    /// and OnClientDisconnect reaches StartCoroutine, which Unity refuses off the main thread.
    /// </summary>
    private void Session_SocketClosed(string reason)
    {
        // Deferring opens a race: a fast reconnect can replace the session before this runs, and
        // tearing that one down would drop a live connection. So only fire while the session this
        // was raised for is still current.
        var closed = session;

        // Every drop that reaches this handler is unplanned, so every one of them should retry.
        // Previously only Socket_ErrorReceived set this, which meant a *clean* close - a host
        // restarting their server, the usual case - dropped the session with no reconnect at all.
        // A user-initiated disconnect can't get here: TeardownSession unsubscribes this handler
        // before it calls DisconnectAsync.
        Reconnecting = true;

        MainThreadQueue.Enqueue(() =>
        {
            if (!ReferenceEquals(session, closed))
            {
                Log.LogDebug($"[AP] Ignoring a socket close superseded by a newer attempt: {reason}");
                return;
            }

            TeardownSession();

            // Dropped rather than asked to stop, so this is genuinely the last error. No need to
            // suppress it while reconnecting: "an attempt is in flight" outranks it wherever the
            // two are displayed together.
            LastError = reason;
            OnClientDisconnect?.Invoke(reason);
        });
    }

    /// <summary>
    /// Retries a dropped connection until it succeeds, the server refuses it, or the player stops
    /// it.
    /// </summary>
    /// <remarks>
    /// Unbounded on purpose: nothing queues the checks earned while the socket is down - every
    /// service re-derives what it owes from the game's own save state at connect - so the only
    /// thing between a dropped socket and a caught-up multiworld is getting the socket back. A
    /// five-attempt cap gave up after about fifteen seconds, which loses to the ordinary case of a
    /// host restarting their server.
    ///
    /// The trade is log noise and idle sockets against an unattended recovery, so the delay backs
    /// off: a server down for an hour costs two attempts a minute rather than twenty.
    /// </remarks>
    public IEnumerator AttemptReconnection()
    {
        Log.LogDebug("Attempting to reconnect!");
        cancelReconnect = false;
        ReconnectAttempt = 0;

        while (!cancelReconnect)
        {
            ReconnectAttempt++;
            yield return new WaitForSeconds(ReconnectDelay(ReconnectAttempt));

            // Checked again after the wait, not just at the top: the backoff is long enough that
            // the player acting inside it is the expected case rather than a race.
            if (cancelReconnect) break;

            // Quiet after the first few. This can run for hours, and a line every thirty seconds
            // buries whatever the player opened the log to find.
            var attemptLine = $"[AP] Reconnection attempt #{ReconnectAttempt}";
            if (ReconnectAttempt <= 5 || ReconnectAttempt % 10 == 0) Log.LogInfo(attemptLine);
            else Log.LogDebug(attemptLine);

            // Same routine the panel's Connect button uses - one code path for "talk to the
            // server", so a fix to either can't drift away from the other.
            yield return ConnectRoutine(LastServerUrl, LastSlotName, LastPassword);

            if (IsConnected)
            {
                Log.LogInfo($"[AP] Reconnected to Archipelago after {ReconnectAttempt} attempt(s).");
                break;
            }

            // Unreachable is worth retrying; refused isn't. Looping forever against a server
            // that's answering would also spam the host's console, not just ours.
            if (loginRefused)
            {
                Log.LogError($"[AP] The server refused the reconnection, so giving up: {LastError}");
                Dispose();
                break;
            }
        }

        if (cancelReconnect) Log.LogInfo("[AP] Stopped trying to reconnect.");

        Reconnecting = false;
        ReconnectAttempt = 0;
    }

    /// <summary>3s, doubling to a 30s ceiling: 3, 6, 12, 24, 30, 30...</summary>
    private static float ReconnectDelay(int attempt) =>
        Mathf.Min(3f * Mathf.Pow(2f, attempt - 1), 30f);

    /// <summary>
    /// Ends the retry loop. Since the loop is otherwise unbounded, this is the only way out of it
    /// besides connecting or being refused - both connecting manually and disconnecting go
    /// through here, which is what keeps the panel usable while a retry is pending.
    ///
    /// `reconnecting` drops immediately rather than when the coroutine notices, so the UI responds
    /// to the click instead of to the end of the current backoff.
    /// </summary>
    public void StopReconnecting()
    {
        if (!Reconnecting) return;

        cancelReconnect = true;
        Reconnecting = false;
    }

    private void Session_OnMessageReceived(LogMessage message)
    {
        Log.LogInfo($"[AP] {message}");
    }

    private void ArchipelagoConsoleCommand_OnArchipelagoReconnectCommandCalled()
    {
        Reconnecting = true;
        Dispose();
        OnClientDisconnect?.Invoke("Manual reconnect requested.");
    }
}
