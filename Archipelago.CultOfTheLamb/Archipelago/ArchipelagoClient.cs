using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using System;
using System.Collections.Generic;

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

    public string lastServerUrl { get; set; }
    public string lastSlotName { get; set; }
    public string lastPassword { get; set; }
    public bool IsConnected => session != null && session.Socket.Connected;

    internal LocationCheckService LocationCheckService { get; private set; }
    internal RegionUnlockService RegionUnlockService { get; private set; }
    internal GoalService GoalService { get; private set; }
    internal SermonService SermonService { get; private set; }
    internal FollowerMilestoneService FollowerMilestoneService { get; private set; }
    internal TarotShopService TarotShopService { get; private set; }
    internal ShopIconService ShopIconService { get; private set; }
    internal SnailShrineService SnailShrineService { get; private set; }

    public ArchipelagoItemLogicController ItemLogic;

    private ArchipelagoSession session;
    public bool reconnecting { get; set; } = false;
    public static string ConnectedPlayerName;

    // Cached slot data (survives reconnection so we don't need to re-derive it)
    private Dictionary<string, object> cachedSlotData;

    public ArchipelagoClient()
    {
    }
}
