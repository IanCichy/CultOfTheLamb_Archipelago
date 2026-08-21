using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using System;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Core connection state and cross-cutting fields. Split into partials by concern:
/// see ArchipelagoClient.Connection.cs for session/login/reconnect,
/// ArchipelagoClient.Items.cs for receiving items from the server.
/// </summary>
public partial class ArchipelagoClient : IDisposable
{
    public delegate void ClientDisconnected(string reason);
    public event ClientDisconnected OnClientDisconnect;

    // What the last connection attempt actually used, which is what a retry replays and what the
    // F9 dump reports. Set only by ConnectRoutine - an outside writer would send the retry loop
    // at different details than the ones the player is looking at.
    public string LastServerUrl { get; private set; }
    public string LastSlotName { get; private set; }
    public string LastPassword { get; private set; }
    public bool IsConnected => session != null && session.Socket.Connected;

    internal LocationCheckService LocationCheckService { get; private set; }
    internal RegionUnlockService RegionUnlockService { get; private set; }
    internal GoalService GoalService { get; private set; }
    internal SermonService SermonService { get; private set; }
    internal FollowerMilestoneService FollowerMilestoneService { get; private set; }
    internal TarotShopService TarotShopService { get; private set; }
    internal ShopIconService ShopIconService { get; private set; }
    internal TarotService TarotService { get; private set; }
    internal SnailShrineService SnailShrineService { get; private set; }

    // One each, because randomize_weapons and randomize_curses are independent options and
    // either can be on alone. Null when its option is off.
    internal EquipmentPoolService WeaponPoolService { get; private set; }
    internal EquipmentPoolService CursePoolService { get; private set; }
    internal EquipmentDisplayService EquipmentDisplayService { get; private set; }

    internal DivineInspirationService DivineInspirationService { get; private set; }
    /// <summary>What the multiworld put at each location. Shared by the shop panels and the
    /// sent-check popups, so the scout happens once.</summary>
    internal ScoutCache Scouts { get; private set; }

    /// <summary>The seed's pacing caps. Always registers - see EconomyService.</summary>
    internal EconomyService EconomyService { get; private set; }

    internal BuildingService BuildingService { get; private set; }
    internal BroomService BroomService { get; private set; }

    /// <summary>The in-game Archipelago checklist. Guidance only - sends no checks.</summary>
    internal QuestGuideService QuestGuideService { get; private set; }

    /// <summary>Takes most of the game's own follower quests out of rotation.</summary>
    internal QuestTrimService QuestTrimService { get; private set; }

    public ArchipelagoItemLogicController ItemLogic { get; private set; }

    private ArchipelagoSession session;

    /// <summary>
    /// A retry is pending or in flight. Written only by the reconnect machinery in
    /// ArchipelagoClient.Connection - an outside setter could desync it from the cancel flag and
    /// the live coroutine, and the UI reads it to decide whether its buttons do anything.
    /// </summary>
    public bool Reconnecting { get; private set; }

    public static string ConnectedPlayerName { get; private set; }
}
