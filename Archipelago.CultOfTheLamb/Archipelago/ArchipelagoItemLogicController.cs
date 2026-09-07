using Archipelago.CultOfTheLamb.Services;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using System.Collections.Concurrent;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Receives items from the AP server and queues them for main-thread processing.
/// Unity/game API calls (granting a follower, unlocking a doctrine, etc.) must happen
/// on the main thread, so ItemReceived only enqueues ArchipelagoPlugin.Update() drains
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
    private readonly DivineInspirationService divineInspirationService;
    private readonly ConcurrentQueue<PendingItem> pendingItemIds = new();

    internal ArchipelagoItemLogicController(
        ArchipelagoSession session,
        RegionUnlockService regionUnlockService,
        SermonService sermonService,
        TarotService tarotService,
        EquipmentPoolService weaponPoolService,
        EquipmentPoolService cursePoolService,
        DivineInspirationService divineInspirationService)
    {
        this.divineInspirationService = divineInspirationService;
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
        // lands *before* this subscription exists so reacting only to the live event drops
        // the entire backlog. That silently breaks any reconnect (previously-unlocked regions
        // stay locked) and breaks connecting to a seed already in progress.
        //
        // Draining is safe against a concurrent live event, because both paths consume the same
        // underlying queue via DequeueItem(), so an item goes to exactly one of them.
        storeKey = AppliedItemStore.BuildKey(session.RoomState?.Seed, session.ConnectionInfo.Slot);
        appliedCount = AppliedItemStore.Get(storeKey);
        replaysRemaining = appliedCount;

        var backlog = 0;
        while (session.Items.Any())
        {
            pendingItemIds.Enqueue(Capture(session.Items.DequeueItem()));
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
        pendingItemIds.Enqueue(Capture(helper.DequeueItem()));
    }

    /// <summary>
    /// Everything we need off the library's item DTO, taken here so nothing downstream depends on
    /// its exact shape.
    /// </summary>
    /// <remarks>
    /// The sender is read at dequeue time. An item's origin is on the packet, and by the time the
    /// main thread processes the queue there is nothing left to ask.
    ///
    /// Null when the item came from our own world, so the popup reads "Received X" rather than
    /// "Received X from yourself". That happened four times in one test run, since this world's
    /// own points and cards land on its own locations.
    /// </remarks>
    private PendingItem Capture(ItemInfo item)
    {
        var fromSomeoneElse = item.Player != null
            && item.Player.Slot != session.ConnectionInfo.Slot;

        // Read off the flags rather than matched by name. "Dissent Trap" is the only one today,
        // but more are planned and a hardcoded list would quietly stop being true.
        var isTrap = item.Flags.HasFlag(ItemFlags.Trap);

        return new PendingItem(
            item.ItemId, fromSomeoneElse ? item.Player.Alias : null, isTrap);
    }

    private readonly struct PendingItem
    {
        internal readonly long ItemId;

        // Who found it, or null when it was our own world
        internal readonly string SenderName;

        internal readonly bool IsTrap;

        internal PendingItem(long itemId, string senderName, bool isTrap)
        {
            ItemId = itemId;
            SenderName = senderName;
            IsTrap = isTrap;
        }
    }

    // Call once per frame from the main thread (see ArchipelagoPlugin.Update)
    public void ProcessQueue()
    {
        while (pendingItemIds.TryDequeue(out var item))
        {
            ApplyItem(item.ItemId, item.SenderName, item.IsTrap);
        }
    }

    // Identifies this save+seed+slot in AppliedItemStore
    private string storeKey;

    // How many items have actually been granted to this save
    private int appliedCount;

    /// <summary>
    /// How many queued items are a replay of ones this save already got.
    /// </summary>
    /// <remarks>
    /// The server resends the full history on connect and we have to drain it. Re-granting stacks
    /// anything non-idempotent, Inventory.AddItem above all, which made reconnect-spamming an
    /// infinite resource generator.
    /// </remarks>
    private int replaysRemaining;

    /// <summary>
    /// Turns a received AP item into an actual game effect.
    /// </summary>
    /// <remarks>
    /// Replayed items are not skipped wholesale. The services that reset on Register (regions,
    /// sermons, equipment) need the replay to rebuild their state, and all of them are idempotent.
    /// Only the stacking grants are suppressed, meaning filler and Follower Level Up.
    /// </remarks>
    private void ApplyItem(long itemId, string senderName, bool isTrap)
    {
        var itemName = session.Items.GetItemName(itemId);

        var isReplay = replaysRemaining > 0;
        if (isReplay)
        {
            replaysRemaining--;
        }

        if (!isReplay)
        {
            Log.LogInfo($"[AP] Received item: {itemName} (id {itemId})"
                + (senderName == null ? string.Empty : $" from {senderName}"));
            appliedCount++;
            AppliedItemStore.Set(storeKey, appliedCount);

            // Sends were announced but receives weren't, so an incoming item was only
            // visible in the AP terminal unless the game happened to show its own banner
            // (resources do, an unlocked upgrade doesn't). Announce every genuinely new
            // item. Replays are deliberately silent, since the player already saw them.
            //
            // Named sender when there is one, so this reads as the mirror of the sent popup.
            var from = senderName == null
                ? string.Empty
                : $" from {ApColors.Tint(senderName, ApColors.YellowHex)}";

            // Green glow = incoming, red = a trap landing on you. The one case where the colour
            // is genuinely load-bearing rather than decorative. A trap is worth noticing before
            // you work out why the game just got harder.
            var glow = isTrap ? ApColors.Red : ApColors.Green;

            ApNotification.Show(
                $"Received {itemName}{from}", NotificationBase.Flair.Positive, glow);
        }

        // --- idempotent, always applied (including on replay) ---

        if (itemId == CultOfTheLambIds.ProgressiveRegionAccessItemId)
        {
            regionUnlockService?.UnlockNextRegion();
            return;
        }

        // Matched by name, not id. The item -> upgrade mapping comes from slot data, which is
        // keyed by name, and that indirection is what keeps the two sides from drifting when
        // upgrades get added or reordered.
        if (sermonService != null && sermonService.TryApplyItem(itemName))
        {
            return;
        }

        // Idempotent too, since granting a card is a set Add, and it has to replay, because
        // TarotService empties the collection on connect and the item history is what rebuilds
        // it.
        if (tarotService != null && tarotService.TryApplyItem(itemName))
        {
            return;
        }

        // Idempotent for the same reason, and it has to replay. The granted set lives only in
        // memory, so the item history is the only thing that rebuilds it on connect.
        if (weaponPoolService != null && weaponPoolService.TryApplyItem(itemName))
        {
            return;
        }

        if (cursePoolService != null && cursePoolService.TryApplyItem(itemName))
        {
            return;
        }

        // Handles its own replay rule rather than sitting on either side of the line. Granting a
        // tech is a set Add and must replay, but granting an ability point is a counter and
        // must not.
        if (divineInspirationService != null
            && divineInspirationService.TryApplyItem(itemName, isReplay))
        {
            return;
        }

        // --- non-idempotent, suppressed on replay ---

        if (isReplay)
        {
            Log.LogInfo($"[AP] Already granted to this save, not re-granting: {itemName}");
            return;
        }

        if (FillerService.TryApplyItem(itemName))
        {
            return;
        }

        // Loud rather than silent. An unhandled item is one the player earned and didn't get,
        // and filler is about half of a seed, so a quiet drop here is the most likely way this
        // mod feels broken while looking fine.
        Log.LogWarning($"[AP] No handler for item '{itemName}' (id {itemId}) - nothing granted.");
    }
}
