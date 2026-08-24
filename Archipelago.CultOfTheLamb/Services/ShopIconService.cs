using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using I2.Loc;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Marks shop slots that are Archipelago checks: the AP logo in place of the item's own art, and
/// the scouted item name appended to the buy prompt. Without it, a slot looks identical whether or
/// not buying it sends a check.
///
/// Two entry points, because a shop and a connection can happen in either order:
/// ShopSlotDisplayPatch.OnShopInitialised (walked into a shop while connected) and the sweep in
/// Register() (connected while already standing in one). Both enqueue the shop and Tick() does the
/// work over following frames, since InitTarotShop runs inside shopKeeperManager.Start() before
/// any card art exists. Item names come from a single pre-scout on connect: scouting is async and
/// the buy prompt is rebuilt every frame, so there's no chance to fetch on demand.
/// </summary>
internal class ShopIconService : IService
{
    private readonly ArchipelagoSession session;
    private readonly Dictionary<string, long> cardToCheckId;

    // What the multiworld put at each location. Shared with CheckNotifier rather than scouted
    // twice - see ScoutCache.
    private readonly ScoutCache scouts;

    // Everything we've changed about a shop, so a disconnect can put it back rather than
    // leaving AP logos sitting in a now-vanilla shop.
    private readonly List<SlotEdit> edits = new();

    // Shops waiting to be decorated, with how many frames we've tried. A shop leaves the queue
    // once every eligible slot is marked, or once it's clear nothing is coming.
    private readonly List<PendingShop> pending = new();

    // ~half a second at 60fps. Long enough for a shop's visuals to finish spawning, short
    // enough that a genuinely artless slot doesn't get retried all session.
    private const int MaxDecorateAttempts = 30;

    // The buy prompt names the card, not its contents: the item is already spelled out on the
    // panel floating above the slot, and repeating it makes for a very long one-line prompt.
    private const string CardName = "AP Tarot";

    // Shown until the scout lands, and only until then - once we know what a slot holds, the
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

    /// <summary>Called every frame from the plugin's Update; does nothing once shops settle.</summary>
    internal void Tick()
    {
        if (pending.Count == 0) return;

        for (var i = pending.Count - 1; i >= 0; i--)
        {
            var entry = pending[i];
            entry.Attempts++;

            if (entry.Manager == null || Decorate(entry.Manager))
            {
                pending.RemoveAt(i);
                continue;
            }

            if (entry.Attempts < MaxDecorateAttempts) continue;

            Log.LogWarning($"[AP] Gave up marking shop '{entry.Manager.name}' after "
                + $"{entry.Attempts} frames - press F1 in this shop to dump what its slots hold.");
            pending.RemoveAt(i);
        }
    }

    private void Enqueue(shopKeeperManager manager)
    {
        if (manager == null || manager.itemSlots == null) return;
        if (pending.Any(p => p.Manager == manager)) return;

        pending.Add(new PendingShop { Manager = manager });
    }

    private void HandleShopInitialised(shopKeeperManager manager) => Enqueue(manager);

    /// <summary>
    /// Marks every eligible slot in a shop. Returns true once there's nothing left to do, which
    /// is what takes the shop off the retry queue.
    /// </summary>
    private bool Decorate(shopKeeperManager manager)
    {
        if (manager == null || manager.itemSlots == null) return true;

        var considered = 0;
        var skipped = 0;
        var outstanding = 0;

        foreach (var slot in manager.itemSlots)
        {
            // Slots for cards the player already owns get hidden by InitTarotShop rather than
            // removed, so inactive ones are still in the array.
            if (slot == null || !slot.activeInHierarchy) continue;

            var buyItem = slot.GetComponent<Interaction_BuyItem>();
            if (buyItem == null) continue;

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

            if (!ApplyIcon(slot)) outstanding++;
        }

        if (outstanding > 0) return false;

        if (considered > 0)
        {
            Log.LogInfo($"[AP] Shop '{manager.name}' ({manager.Location}): {considered} live "
                + $"slot(s), {skipped} not an open AP check.");
        }

        return true;
    }

    /// <summary>
    /// Turns one slot into the Archipelago tarot card. Returns false if there's nothing to mark
    /// *yet* - the caller retries for a few frames before giving up.
    ///
    /// Two steps, because a slot draws its card in two layers: swap the sprite underneath, and
    /// switch off the Spine skeleton painting a specific card's face over it. Doing only the
    /// first leaves the vanilla face covering our card entirely.
    /// </summary>
    private bool ApplyIcon(GameObject slot)
    {
        var renderer = FindArtRenderer(slot);
        if (renderer == null) return false;

        if (edits.Any(e => e.Renderer == renderer)) return true;

        var original = renderer.sprite;

        var card = ApAssets.TarotCardSprite(original.bounds);
        if (card == null) return true;

        renderer.sprite = card;
        edits.Add(new SlotEdit { Renderer = renderer, Original = original });

        var hiddenFaces = HideSpineArt(slot);

        Log.LogInfo($"[AP] Marked shop slot '{slot.name}': replaced '{original.name}' on "
            + $"'{renderer.gameObject.name}', hid {hiddenFaces} skeleton renderer(s).");
        return true;
    }

    /// <summary>
    /// Turns off the Spine skeletons under a slot, and reports how many.
    /// </summary>
    /// <remarks>
    /// A tarot slot draws in two layers: a plain SpriteRenderer holding the card back, and a
    /// Spine skeleton on top painting the card's face. Replacing only the sprite leaves the
    /// face covering the logo, so the skeleton has to go too.
    ///
    /// Only ever runs on slots that are open AP checks, which are always tarot cards, so this can't
    /// strip an animation off an ordinary stall - and buying a card destroys its slot outright
    /// (Interaction_BuyItem.Activate), so nothing has to turn these back on mid-session.
    /// </remarks>
    private int HideSpineArt(GameObject slot)
    {
        var hidden = 0;

        foreach (var renderer in slot.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            if (renderer is SpriteRenderer || !renderer.enabled) continue;

            renderer.enabled = false;
            edits.Add(new SlotEdit { Hidden = renderer });
            hidden++;
        }

        return hidden;
    }

    /// <summary>
    /// The renderer actually drawing the thing for sale. `enabled` is the whole trick: a tarot
    /// slot's own SpriteRenderer - the obvious one, that InventoryItemDisplay.SetImage writes to
    /// - is *disabled* and draws nothing, so writing to it fails silently. Among what's left the
    /// biggest sprite is the item; shadows, highlights and price pips are smaller. F1 in a shop
    /// dumps the field.
    /// </summary>
    private static SpriteRenderer FindArtRenderer(GameObject slot)
    {
        SpriteRenderer best = null;
        var bestArea = 0f;

        foreach (var candidate in slot.GetComponentsInChildren<SpriteRenderer>(includeInactive: false))
        {
            if (!candidate.enabled || candidate.sprite == null) continue;

            var size = candidate.sprite.bounds.size;

            // In world units, so a child scaled down doesn't get picked over the real art
            // just because its source sprite has more pixels.
            var scale = candidate.transform.lossyScale;
            var area = Mathf.Abs(size.x * scale.x) * Mathf.Abs(size.y * scale.y);

            if (area <= bestArea) continue;

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
            if (entry.Renderer != null) entry.Renderer.sprite = entry.Original;
            if (entry.Hidden != null) entry.Hidden.enabled = true;
        }

        edits.Clear();
    }

    /// <summary>
    /// Rewrites the buy prompt to name the Archipelago item rather than the tarot card.
    /// </summary>
    /// <remarks>
    /// Replacing rather than appending, because the card name is actively misleading: the slot is a
    /// location, and what buying it produces is whatever the multiworld put there - the card itself
    /// comes from the item pool on its own schedule.
    ///
    /// Rebuilt through the game's own format string and cost formatter so it stays localised and
    /// keeps the vanilla "for &lt;icon&gt; N" shape. Runs from the Label getter, which the game polls
    /// while the player stands near a slot, so it stays cheap.
    /// </remarks>
    private void HandleLabelBuilt(Interaction_BuyItem buyItem)
    {
        if (buyItem == null) return;
        if (!TryGetCheckId(buyItem, out var checkId)) return;

        var entry = buyItem.itemForSale;
        var cost = CostFormatter.FormatCost(entry.costType, buyItem.GetCost(), true, false);

        ShopSlotDisplayPatch.ReplaceLabel(
            buyItem, string.Format(ScriptLocalization.UI_ItemSelector_Context.Buy, CardName, cost));
    }

    /// <summary>
    /// Rewrites the panel that floats over a slot, which otherwise describes the tarot card -
    /// its name, its lore, and the effect it grants - none of which is what buying the slot
    /// does any more.
    /// </summary>
    /// <remarks>
    /// This panel, not the buy prompt, is where the check's details belong: it's the surface with
    /// room for them, and it's already the thing a player reads before deciding to spend.
    ///
    /// The item takes the card's name slot, because it's the biggest text on the panel and it's
    /// what the player is deciding about. Who it's for goes in the flavour line, and the location
    /// goes in the body - it's the slot they're standing on, so it's the least surprising there.
    /// </remarks>
    private void HandleTarotDisplayBuilt(UITarotDisplay display, TarotCards.Card card)
    {
        if (!cardToCheckId.TryGetValue(card.ToString(), out var checkId)) return;

        // Before the scout lands - one server round trip after connecting - there's nothing to
        // name. The vanilla-styled header stands in rather than an empty card.
        if (scouts == null || !scouts.TryGet(checkId, out var scouted))
        {
            ShopSlotDisplayPatch.SetTarotDisplayText(
                display, PendingTitle, PendingLore, "Asking the server what this holds...");
            return;
        }

        var recipient = scouted.ForLocalPlayer
            ? "~ for you ~"
            : $"~ for {ApColors.Tint(scouted.PlayerName, ApColors.YellowHex)} "
                + $"({scouted.Game}) ~";

        ShopSlotDisplayPatch.SetTarotDisplayText(
            display, scouted.ItemName, recipient, $"<i>{scouts.LocationName(checkId)}</i>");
    }

    /// <summary>
    /// Whether a card's shop slot has been spent, for the TrinketUnlocked override: true once
    /// its check is sent, false while it's still there to buy, and null for cards this seed
    /// doesn't map at all - those are left entirely to the game.
    /// </summary>
    private bool? SlotIsSpent(TarotCards.Card card)
    {
        if (!cardToCheckId.TryGetValue(card.ToString(), out var checkId)) return null;

        return session.Locations.AllLocationsChecked.Contains(checkId);
    }

    private bool TryGetCheckId(Interaction_BuyItem buyItem, out long checkId)
    {
        checkId = 0;

        // customItemForSale slots are built at runtime and Activate() bails on them, so they
        // can never send a check - marking one would be a lie. Matches ShopPurchasePatch.
        if (buyItem.customItemForSale) return false;

        var entry = buyItem.itemForSale;
        if (entry == null || !entry.TarotCard) return false;

        return cardToCheckId.TryGetValue(entry.Card.ToString(), out checkId);
    }

    /// <summary>
    /// One change to undo on disconnect: a sprite we replaced, or a renderer we switched off.
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
