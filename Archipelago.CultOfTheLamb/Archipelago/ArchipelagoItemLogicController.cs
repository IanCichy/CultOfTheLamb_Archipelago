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
    private readonly TarotService tarotService;
    private readonly EquipmentPoolService weaponPoolService;
    private readonly EquipmentPoolService cursePoolService;
    private readonly ConcurrentQueue<long> pendingItemIds = new();

    internal ArchipelagoItemLogicController(
        ArchipelagoSession session,
        RegionUnlockService regionUnlockService,
        SermonService sermonService,
        TarotService tarotService,
        EquipmentPoolService weaponPoolService,
        EquipmentPoolService cursePoolService)
    {
        this.session = session;
        this.regionUnlockService = regionUnlockService;
        this.sermonService = sermonService;
        this.tarotService = tarotService;
        this.weaponPoolService = weaponPoolService;
        this.cursePoolService = cursePoolService;
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
        storeKey = AppliedItemStore.BuildKey(session.RoomState?.Seed, session.ConnectionInfo.Slot);
        appliedCount = AppliedItemStore.Get(storeKey);
        replaysRemaining = appliedCount;

        var backlog = 0;
        while (session.Items.Any())
        {
            pendingItemIds.Enqueue(session.Items.DequeueItem().ItemId);
            backlog++;
        }

        if (backlog > 0)
        {
            var toGrant = backlog - appliedCount;
            Log.LogInfo($"[AP] {backlog} item(s) received on this slot; {appliedCount} already "
                + $"applied to this save, so {(toGrant > 0 ? toGrant : 0)} will be granted. "
                + $"[{storeKey}]");
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

    /// <summary>Identifies this save+seed+slot in AppliedItemStore.</summary>
    private string storeKey;

    /// <summary>How many items have actually been granted to this save.</summary>
    private int appliedCount;

    /// <summary>
    /// How many queued items are a replay of ones this save already got. The server resends
    /// the full history on connect and we must drain it, but re-granting stacks anything
    /// non-idempotent - Inventory.AddItem in particular, which made reconnect-spamming an
    /// infinite resource generator.
    /// </summary>
    private int replaysRemaining;

    /// <summary>
    /// Turns a received AP item into an actual game effect.
    ///
    /// Replayed items are NOT skipped wholesale: the services that reset on Register - regions,
    /// sermons, equipment - need the replay to rebuild their state, and all of them are
    /// idempotent. Only the stacking grants (filler, Follower Level Up) are suppressed.
    /// </summary>
    private void ApplyItem(long itemId)
    {
        var itemName = session.Items.GetItemName(itemId);

        var isReplay = replaysRemaining > 0;
        if (isReplay) replaysRemaining--;

        if (!isReplay)
        {
            Log.LogInfo($"[AP] Received item: {itemName} (id {itemId})");
            appliedCount++;
            AppliedItemStore.Set(storeKey, appliedCount);

            // Sends were announced but receives weren't, so an incoming item was only
            // visible in the AP terminal unless the game happened to show its own banner
            // (resources do; an unlocked upgrade doesn't). Announce every genuinely new
            // item - replays are deliberately silent, since the player already saw them.
            ApNotification.Show($"Archipelago: received {itemName}",
                NotificationBase.Flair.Positive);
        }

        // --- idempotent, always applied (including on replay) ---

        if (itemId == CultOfTheLambIds.ProgressiveRegionAccessItemId)
        {
            regionUnlockService?.UnlockNextRegion();
            return;
        }

        // Matched by name, not id: the item -> upgrade mapping comes from slot data, which is
        // keyed by name, and that indirection is what keeps the two sides from drifting when
        // upgrades get added or reordered.
        if (sermonService != null && sermonService.TryApplyItem(itemName)) return;

        // Idempotent too - granting a card is a set Add - and it has to replay, because
        // TarotService empties the collection on connect and the item history is what rebuilds
        // it.
        if (tarotService != null && tarotService.TryApplyItem(itemName)) return;

        // Idempotent for the same reason, and it has to replay: the granted set lives only in
        // memory, so the item history is the only thing that rebuilds it on connect.
        if (weaponPoolService != null && weaponPoolService.TryApplyItem(itemName)) return;
        if (cursePoolService != null && cursePoolService.TryApplyItem(itemName)) return;

        // --- non-idempotent, suppressed on replay ---

        if (isReplay)
        {
            Log.LogInfo($"[AP] Already granted to this save, not re-granting: {itemName}");
            return;
        }

        if (FillerService.TryApplyItem(itemName)) return;

        // Loud rather than silent: an unhandled item is one the player earned and didn't get,
        // and filler is ~half of a seed - a quiet drop here is the most likely way this mod
        // feels broken while looking fine.
        Log.LogWarning($"[AP] No handler for item '{itemName}' (id {itemId}) - nothing granted.");
    }
}
