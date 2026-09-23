using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using I2.Loc;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Marks shop slots that are Archipelago checks, with the AP logo on the slot and the item's name
/// in the buy prompt. Without it, you can't tell whether buying a slot sends a check.
/// </summary>
/// <remarks>
/// A shop can load before or after connecting, so there are two ways in: the shop opening while
/// connected, or connecting while standing in one. Both queue the shop and Tick does the work over
/// the next few frames, since the card art doesn't exist yet when the shop starts.
///
/// Item names come from one scout on connect. Scouting is async and the prompt is rebuilt every
/// frame, so there's no chance to look them up on the spot.
/// </remarks>
internal class ShopIconService : IService
{
    private readonly ArchipelagoSession session;
    private readonly Dictionary<string, long> cardToCheckId;

    // What the multiworld put at each location. Shared with CheckNotifier rather than scouted
    // twice. See ScoutCache.
    private readonly ScoutCache scouts;

    // Everything we've changed about a shop, so a disconnect can put it back rather than
    // leaving AP logos sitting in a now-vanilla shop.
    private readonly List<SlotEdit> edits = new();

    // Shops waiting to be decorated, with how many frames we've tried. A shop leaves the queue
    // once every eligible slot is marked, or once it's clear nothing is coming.
    private readonly List<PendingShop> pending = new();

    // ~half a second at 60fps. Long enough for a shop's visuals to finish spawning, short
    // enough that an artless slot doesn't get retried all session.
    private const int MaxDecorateAttempts = 30;

    // The buy prompt names the card, not its contents. The item is already spelled out on the
    // panel floating above the slot, and repeating it makes for a long one-line prompt.
    private const string CardName = "AP Tarot";

    // Shown until the scout lands, and only until then. Once we know what a slot holds, the
    // item's own name is the card's name. Doubles as the loading state, which is why it reads
    // like flavour rather than like an error.
    private const string PendingTitle = "The Multiworld's Binding";
    private const string PendingLore = "Weakness begs exploitation.";

    internal ShopIconService(
        ArchipelagoSession session, Dictionary<string, long> cardToCheckId, ScoutCache scouts)
    {
        this.session = session;
        this.cardToCheckId = cardToCheckId ?? new Dictionary<string, long>();
        this.scouts = scouts;
    }

    public void Register()
    {
        ShopSlotDisplayPatch.OnShopInitialised += HandleShopInitialised;
        ShopSlotDisplayPatch.OnLabelBuilt += HandleLabelBuilt;
        ShopSlotDisplayPatch.OnTarotDisplayBuilt += HandleTarotDisplayBuilt;
        ShopSlotDisplayPatch.SlotIsSpent = SlotIsSpent;

        foreach (var manager in UnityEngine.Object.FindObjectsOfType<shopKeeperManager>())
        {
            Enqueue(manager);
        }

        Log.LogInfo($"[AP] Shop slot icons active: {cardToCheckId.Count} slot(s) mapped.");
    }

    public void Unregister()
    {
        ShopSlotDisplayPatch.OnShopInitialised -= HandleShopInitialised;
        ShopSlotDisplayPatch.OnLabelBuilt -= HandleLabelBuilt;
        ShopSlotDisplayPatch.OnTarotDisplayBuilt -= HandleTarotDisplayBuilt;
        ShopSlotDisplayPatch.SlotIsSpent = null;

        pending.Clear();
        RestoreSwappedSprites();
    }

    // Called every frame from the plugin's Update. Does nothing once shops settle
    internal void Tick()
    {
        if (pending.Count == 0)
        {
            return;
        }

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var entry = pending[i];
            entry.Attempts++;

            if (entry.Manager == null || Decorate(entry.Manager))
            {
                pending.RemoveAt(i);
                continue;
            }

            if (entry.Attempts < MaxDecorateAttempts)
            {
                continue;
            }

            Log.LogWarning($"[AP] Gave up marking shop '{entry.Manager.name}' after "
                + $"{entry.Attempts} frames. Please attach this log to a bug report.");
            pending.RemoveAt(i);
        }
    }

    private void Enqueue(shopKeeperManager manager)
    {
        if (manager == null || manager.itemSlots == null)
        {
            return;
        }

        if (pending.Any(p => p.Manager == manager))
        {
            return;
        }

        pending.Add(new PendingShop { Manager = manager });
    }

    private void HandleShopInitialised(shopKeeperManager manager)
    {
        Enqueue(manager);
    }

    // Marks every eligible slot in a shop. Returns true once there's nothing left to do, which
    // takes the shop off the retry queue
    private bool Decorate(shopKeeperManager manager)
    {
        if (manager == null || manager.itemSlots == null)
        {
            return true;
        }

        var considered = 0;
        var skipped = 0;
        var outstanding = 0;

        foreach (var slot in manager.itemSlots)
        {
            // Slots for cards the player already owns get hidden by InitTarotShop rather than
            // removed, so inactive ones are still in the array.
            if (slot == null || !slot.activeInHierarchy)
            {
                continue;
            }

            var buyItem = slot.GetComponent<Interaction_BuyItem>();
            if (buyItem == null)
            {
                continue;
            }

            considered++;

            if (!TryGetCheckId(buyItem, out var checkId))
            {
                skipped++;
                continue;
            }

            // A location the server already has is no longer a check, even if the slot is still
            // standing there (an AP-granted card unlocks without the shop noticing).
            if (session.Locations.AllLocationsChecked.Contains(checkId))
            {
                skipped++;
                continue;
            }

            if (!ApplyIcon(slot))
            {
                outstanding++;
            }
        }

        if (outstanding > 0)
        {
            return false;
        }

        if (considered > 0)
        {
            Log.LogInfo($"[AP] Shop '{manager.name}' ({manager.Location}): {considered} live "
                + $"slot(s), {skipped} not an open AP check.");
        }

        return true;
    }

    // Returns false if there's nothing to mark yet, and the caller retries for a few frames.
    // Two steps, because a slot draws its card in two layers: swap the sprite underneath, then
    // switch off the Spine skeleton painting a card's face over it. The first alone leaves the
    // vanilla face covering ours
    private bool ApplyIcon(GameObject slot)
    {
        var renderer = FindArtRenderer(slot);
        if (renderer == null)
        {
            return false;
        }

        if (edits.Any(e => e.Renderer == renderer))
        {
            return true;
        }

        var original = renderer.sprite;

        var card = ApAssets.TarotCardSprite(original.bounds);
        if (card == null)
        {
            return true;
        }

        renderer.sprite = card;
        edits.Add(new SlotEdit { Renderer = renderer, Original = original });

        var hiddenFaces = HideSpineArt(slot);

        Log.LogInfo($"[AP] Marked shop slot '{slot.name}': replaced '{original.name}' on "
            + $"'{renderer.gameObject.name}', hid {hiddenFaces} skeleton renderer(s).");
        return true;
    }

    // A plain SpriteRenderer holds the card back and a Spine skeleton on top paints its face,
    // so replacing the sprite alone leaves the face covering the logo.
    //
    // Only runs on slots that are open AP checks, which are always tarot cards, so it can't
    // strip an animation off an ordinary stall. Buying a card destroys its slot outright
    // (Interaction_BuyItem.Activate), so nothing turns these back on mid-session
    private int HideSpineArt(GameObject slot)
    {
        var hidden = 0;

        foreach (var renderer in slot.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer is SpriteRenderer || !renderer.enabled)
            {
                continue;
            }

            renderer.enabled = false;
            edits.Add(new SlotEdit { Hidden = renderer });
            hidden++;
        }

        return hidden;
    }

    // The renderer drawing the item for sale, found by checking enabled. A tarot slot's own
    // SpriteRenderer, the one InventoryItemDisplay.SetImage writes to, is disabled and draws
    // nothing, so writing to it does nothing. Of the rest, the biggest sprite is the item, since
    // shadows, highlights and price pips are smaller. F1 in a shop dumps it
    private static SpriteRenderer FindArtRenderer(GameObject slot)
    {
        SpriteRenderer best = null;
        var bestArea = 0f;

        foreach (var candidate in slot.GetComponentsInChildren<SpriteRenderer>(includeInactive: false))
        {
            if (!candidate.enabled || candidate.sprite == null)
            {
                continue;
            }

            var size = candidate.sprite.bounds.size;

            // In world units, so a child scaled down doesn't get picked over the real art
            // just because its source sprite has more pixels.
            var scale = candidate.transform.lossyScale;
            var area = Mathf.Abs(size.x * scale.x) * Mathf.Abs(size.y * scale.y);

            if (area <= bestArea)
            {
                continue;
            }

            bestArea = area;
            best = candidate;
        }

        return best;
    }

    private void RestoreSwappedSprites()
    {
        foreach (var entry in edits)
        {
            // Shops get torn down with their scene, so most of these are gone by now.
            if (entry.Renderer != null)
            {
                entry.Renderer.sprite = entry.Original;
            }

            if (entry.Hidden != null)
            {
                entry.Hidden.enabled = true;
            }
        }

        edits.Clear();
    }

    // Names the Archipelago item rather than the tarot card. Replacing rather than appending,
    // because the slot is a location and what buying it produces is whatever the multiworld put
    // there. Rebuilt through the game's own format string and cost formatter so it stays
    // localised. Runs from the Label getter, which the game polls, so it stays cheap
    private void HandleLabelBuilt(Interaction_BuyItem buyItem)
    {
        if (buyItem == null)
        {
            return;
        }

        if (!TryGetCheckId(buyItem, out var checkId))
        {
            return;
        }

        var entry = buyItem.itemForSale;
        var cost = CostFormatter.FormatCost(entry.costType, buyItem.GetCost(), true, false);

        ShopSlotDisplayPatch.ReplaceLabel(
            buyItem, string.Format(ScriptLocalization.UI_ItemSelector_Context.Buy, CardName, cost));
    }

    // Rewrites the panel over a slot, which normally describes the tarot card. None of that is
    // what buying the slot does anymore.
    //
    // This panel is where the check's details go, since it has room and it's what you read before
    // buying. The item takes the card name spot, who it's for goes in the flavour line, and the
    // location goes in the body
    private void HandleTarotDisplayBuilt(UITarotDisplay display, TarotCards.Card card)
    {
        if (!cardToCheckId.TryGetValue(card.ToString(), out var checkId))
        {
            return;
        }

        // Before the scout lands, one server round trip after connecting, there's nothing to
        // name. The vanilla-styled header stands in rather than an empty card.
        if (scouts == null || !scouts.TryGet(checkId, out var scouted))
        {
            ShopSlotDisplayPatch.SetTarotDisplayText(
                display, PendingTitle, PendingLore, "Asking the server what this holds...");
            return;
        }

        // Every one of these comes from another world and is arbitrary text, so a '<' in any of
        // them would be read as a tag. The location name is the worst of the three, since it
        // sits inside our own <i> pair.
        var recipient = scouted.ForLocalPlayer
            ? "~ for you ~"
            : $"~ for {ApColors.Tint(scouted.PlayerName, ApColors.YellowHex)} "
                + $"({ApColors.Sanitize(scouted.Game)}) ~";

        ShopSlotDisplayPatch.SetTarotDisplayText(
            display,
            ApColors.Sanitize(scouted.ItemName),
            recipient,
            $"<i>{ApColors.Sanitize(scouts.LocationName(checkId))}</i>");
    }

    // Whether a card's shop slot has been spent, for the TrinketUnlocked override. True once
    // its check is sent, false while it's still there to buy, and null for cards this seed
    // doesn't map at all, which are left entirely to the game
    private bool? SlotIsSpent(TarotCards.Card card)
    {
        if (!cardToCheckId.TryGetValue(card.ToString(), out var checkId))
        {
            return null;
        }

        return session.Locations.AllLocationsChecked.Contains(checkId);
    }

    private bool TryGetCheckId(Interaction_BuyItem buyItem, out long checkId)
    {
        checkId = 0;

        // customItemForSale slots are built at runtime and Activate() bails on them, so they
        // can never send a check, and marking one would be a lie. Matches ShopPurchasePatch.
        if (buyItem.customItemForSale)
        {
            return false;
        }

        var entry = buyItem.itemForSale;
        if (entry == null || !entry.TarotCard)
        {
            return false;
        }

        return cardToCheckId.TryGetValue(entry.Card.ToString(), out checkId);
    }

    /// <summary>
    /// One change to undo on disconnect. Either a sprite we replaced, or a renderer we switched
    /// off.
    /// </summary>
    private class SlotEdit
    {
        internal SpriteRenderer Renderer;
        internal Sprite Original;
        internal Renderer Hidden;
    }

    private class PendingShop
    {
        internal shopKeeperManager Manager;
        internal int Attempts;
    }

}
