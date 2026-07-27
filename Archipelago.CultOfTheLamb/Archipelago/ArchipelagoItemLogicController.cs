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
    private readonly SermonService sermonService;
    private readonly ConcurrentQueue<long> pendingItemIds = new();

    internal ArchipelagoItemLogicController(
        ArchipelagoSession session,
        RegionUnlockService regionUnlockService,
        SermonService sermonService)
    {
        this.session = session;
        this.regionUnlockService = regionUnlockService;
        this.sermonService = sermonService;
    }

    public void Register()
    {
        session.Items.ItemReceived += Items_ItemReceived;

        // The server replays every item the slot has ever received as part of login, and that
        // lands *before* this subscription exists - so reacting only to the live event drops
        // the entire backlog. That silently breaks any reconnect (previously-unlocked regions
        // stay locked) and breaks connecting to a seed already in progress.
        //
        // Draining is safe against a concurrent live event: both paths consume the same
        // underlying queue via DequeueItem(), so an item goes to exactly one of them.
        var backlog = 0;
        while (session.Items.Any())
        {
            pendingItemIds.Enqueue(session.Items.DequeueItem().ItemId);
            backlog++;
        }

        if (backlog > 0)
        {
            Log.LogInfo($"[AP] Replaying {backlog} previously-received item(s) from this slot.");
        }
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
        var itemName = session.Items.GetItemName(itemId);
        Log.LogInfo($"[AP] Received item: {itemName} (id {itemId})");

        if (itemId == CultOfTheLambIds.ProgressiveRegionAccessItemId)
        {
            regionUnlockService?.UnlockNextRegion();
            return;
        }

        // Sermon upgrades are matched by name, not id: the item -> upgrade mapping comes from
        // slot data, which is keyed by name, and that indirection is what keeps the two sides
        // from drifting when upgrades get added or reordered.
        if (sermonService != null && sermonService.TryApplyItem(itemName)) return;

        if (FillerService.TryApplyItem(itemName)) return;

        // Loud rather than silent: an unhandled item is an item the player earned and didn't
        // get, and filler is ~half of a seed - a quiet drop here is the most likely way this
        // mod feels broken while looking fine.
        Log.LogWarning($"[AP] No handler for item '{itemName}' (id {itemId}) - nothing granted.");
    }
}
