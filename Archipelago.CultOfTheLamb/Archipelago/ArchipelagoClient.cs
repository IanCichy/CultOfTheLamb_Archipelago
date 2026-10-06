using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using System;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Core connection state and services
/// </summary>
/// <remarks>
/// Session, login and reconnect live in the ArchipelagoClient.Connection.cs partial.
/// Receiving items from the server is ArchipelagoItemLogicController.
/// </remarks>
public partial class ArchipelagoClient : IDisposable
{
    private ArchipelagoSession session;

    public delegate void ClientDisconnected(string reason);
    public event ClientDisconnected OnClientDisconnect;

    // A retry is pending or in flight. Written only by the reconnect machinery in
    // ArchipelagoClient.Connection. An outside setter could desync it from the cancel flag and
    // the live coroutine, and the UI reads it to decide whether its buttons do anything
    public bool Reconnecting { get; private set; }

    public static string ConnectedPlayerName { get; private set; }

    // What the last connection attempt actually used, which is what a retry replays and what the
    // F9 dump reports. Set only by ConnectRoutine, because an outside writer would send the retry
    // loop at different details than the ones the player is looking at.
    public string LastServerUrl { get; private set; }
    public string LastSlotName { get; private set; }
    public string LastPassword { get; private set; }
    public bool IsConnected => session != null && session.Socket.Connected;

    // ------------------------------------------------------------------ services

    // Core services that are always on
    internal GoalService GoalService { get; private set; }
    internal LocationCheckService LocationCheckService { get; private set; }
    internal RegionUnlockService RegionUnlockService { get; private set; }

    // Progression services for core game loops
    internal DivineInspirationService DivineInspirationService { get; private set; }
    internal SermonService SermonService { get; private set; }
    internal FollowerMilestoneService FollowerMilestoneService { get; private set; }
    internal BuildingService BuildingService { get; private set; }
    internal TarotShopService TarotShopService { get; private set; }
    internal TarotService TarotService { get; private set; }

    // Weapon and Curse services (randomize_weapons / randomize_curses) are independent
    // options and can be on alone, together, or off
    internal EquipmentPoolService WeaponPoolService { get; private set; }
    internal EquipmentPoolService CursePoolService { get; private set; }
    internal EquipmentDisplayService EquipmentDisplayService { get; private set; }

    // Misc game services for small things
    internal BroomService BroomService { get; private set; }
    internal SnailShrineService SnailShrineService { get; private set; }

    // Qualified: Archipelago.MultiClient.Net has a DeathLinkService too
    internal Services.DeathLinkService DeathLinkService { get; private set; }
    internal ShopIconService ShopIconService { get; private set; }

    // Quest Services:
    // The in game Archipelago checklist. Guidance only sends no checks
    internal QuestGuideService QuestGuideService { get; private set; }
    // Takes most of the games own follower quests out of rotation
    internal QuestTrimService QuestTrimService { get; private set; }

    public ArchipelagoItemLogicController ItemLogic { get; private set; }

    // What the multiworld put at each location. Shared by the shop panels and the sent check popups
    internal ScoutCache Scouts { get; private set; }

    // The seed's pacing caps. Always registers : see EconomyService
    internal EconomyService EconomyService { get; private set; }
}
