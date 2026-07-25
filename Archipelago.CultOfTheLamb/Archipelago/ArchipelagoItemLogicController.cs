using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using System.Collections.Concurrent;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Receives items from the AP server and queues them for main-thread processing.
/// Unity/game API calls (granting a follower, unlocking a doctrine, etc.) must happen
/// on the main thread, so ItemReceived only enqueues - ArchipelagoPlugin.Update() drains
/// the queue via ProcessQueue().
/// </summary>
public partial class ArchipelagoItemLogicController : IService
{
    private readonly ArchipelagoSession session;
    private readonly RegionUnlockService regionUnlockService;
    private readonly ConcurrentQueue<long> pendingItemIds = new();

    internal ArchipelagoItemLogicController(ArchipelagoSession session, RegionUnlockService regionUnlockService)
    {
        this.session = session;
        this.regionUnlockService = regionUnlockService;
    }

    public void Register()
    {
        session.Items.ItemReceived += Items_ItemReceived;
    }

    public void Unregister()
    {
        session.Items.ItemReceived -= Items_ItemReceived;
    }

    private void Items_ItemReceived(ReceivedItemsHelper helper)
    {
        // Match RiskOfRain2's pattern: let type inference pick up whatever DequeueItem()
        // returns rather than naming it explicitly, and only pull the ItemId field back out
        // - avoids depending on the exact shape/namespace of the library's item DTO.
        var newItem = helper.DequeueItem();
        pendingItemIds.Enqueue(newItem.ItemId);
    }

    /// <summary>Call once per frame from the main thread (see ArchipelagoPlugin.Update).</summary>
    public void ProcessQueue()
    {
        while (pendingItemIds.TryDequeue(out var itemId))
        {
            ApplyItem(itemId);
        }
    }

    /// <summary>
    /// Turns a received AP item into an actual game effect. Only region access is wired up
    /// so far - the rest of items.py (weapons/tarot/relics/doctrines/filler/traps) still
    /// needs real game-side hooks. See RegionUnlockService and
    /// DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md for what's confirmed so far.
    /// </summary>
    private void ApplyItem(long itemId)
    {
        Log.LogInfo($"[AP] Received item: {session.Items.GetItemName(itemId)} (id {itemId})");

        if (itemId == CultOfTheLambIds.ProgressiveRegionAccessItemId)
        {
            regionUnlockService?.UnlockNextRegion();
        }
    }
}
