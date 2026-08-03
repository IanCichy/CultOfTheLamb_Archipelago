using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using I2.Loc;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Marks shop slots that are Archipelago checks: the AP logo in place of the item's own art,
/// and the scouted item name appended to the buy prompt.
///
/// Why it matters: a tarot shop slot looks identical whether or not buying it sends a check,
/// so without this the player has to remember which of the 14 cards are locations. Multiworld
/// players also want to know *what* a check pays out before spending on it.
///
/// Two entry points, because a shop and a connection can happen in either order:
///   - ShopSlotDisplayPatch.OnShopInitialised - walked into a shop while connected.
///   - the sweep in Register() - connected while already standing in one.
/// Both enqueue the shop; Tick() does the work over the following frames. Deferring is not
/// tidiness: InitTarotShop runs inside shopKeeperManager.Start(), and decorating from there
/// found no card art at all, because the visuals aren't in place that early.
///
/// Item names come from a single pre-scout on connect. Scouting is async and the buy prompt is
/// rebuilt synchronously every frame, so there's no opportunity to fetch on demand; until the
/// scout lands, slots show the icon but the vanilla prompt.
/// </summary>
internal class ShopIconService : IService
{
    private readonly ArchipelagoSession session;
    private readonly Dictionary<string, long> cardToCheckId;

    // location id -> what the multiworld put there. Written from the scout continuation (a
    // background thread) and read from the UI patches (the main thread), so it's swapped
    // wholesale rather than mutated in place - a reference assignment is atomic, an Add is not.
    private Dictionary<long, ScoutedCheck> scoutedItems = new();

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

    // The panel's own header and flavour line, in the style of a real card's.
    private const string CardTitle = "The Multiworld's Binding";
    private const string CardLore = "Weakness begs exploitation.";

    internal ShopIconService(ArchipelagoSession session, Dictionary<string, long> cardToCheckId)
    {
        this.session = session;
        this.cardToCheckId = cardToCheckId ?? new Dictionary<string, long>();
    }

    public void Register()
    {
        ShopSlotDisplayPatch.OnShopInitialised += HandleShopInitialised;
        ShopSlotDisplayPatch.OnLabelBuilt += HandleLabelBuilt;
        ShopSlotDisplayPatch.OnTarotDisplayBuilt += HandleTarotDisplayBuilt;
        ShopSlotDisplayPatch.SlotIsSpent = SlotIsSpent;

        ScoutShopLocations();
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

    /// <summary>
    /// One scout for every mapped shop location. HintCreationPolicy is left at the default (no
    /// hint): this is a UI convenience, and burning the player's hint points to render a label
    /// - let alone broadcasting hints to the whole multiworld on connect - would be hostile.
    /// </summary>
    private void ScoutShopLocations()
    {
        var ids = cardToCheckId.Values.Distinct().ToArray();
        if (ids.Length == 0) return;

        session.Locations.ScoutLocationsAsync(ids).ContinueWith(task =>
        {
            if (task.IsFaulted || task.Result == null)
            {
                Log.LogWarning("[AP] Could not scout shop locations - buy prompts stay vanilla: "
                    + task.Exception?.GetBaseException().Message);
                return;
            }

            var names = new Dictionary<long, ScoutedCheck>();
            foreach (var entry in task.Result)
            {
                var item = entry.Value;
                names[entry.Key] = new ScoutedCheck
                {
                    ItemName = item.ItemName,
                    // Alias falls back to the slot name, so this is never empty.
                    PlayerName = item.Player.Alias,
                    Game = item.ItemGame,
                    ForLocalPlayer = item.Player.Slot == session.ConnectionInfo.Slot,
                };
            }

            scoutedItems = names;
            Log.LogInfo($"[AP] Scouted {names.Count} shop location(s) for buy prompts.");
        });
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
    ///
    /// A tarot slot draws in two layers: a plain SpriteRenderer holding the card back, and a
    /// Spine skeleton on top painting the card's face. Replacing only the sprite leaves the
    /// face covering the logo, so the skeleton has to go too.
    ///
    /// Only ever runs on slots that are open AP checks, which are always tarot cards - so this
    /// can't strip an animation off an ordinary stall. Buying a card destroys its slot outright
    /// (Interaction_BuyItem.Activate), so nothing has to turn these back on mid-session.
    /// </summary>
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
    /// The renderer actually drawing the thing for sale.
    ///
    /// `enabled` is the whole trick, and it cost two attempts to find. A tarot slot carries a
    /// SpriteRenderer on the slot object itself - the one InventoryItemDisplay.SetImage writes
    /// to, and the obvious thing to reach for - but it is *disabled*: it exists for item stalls,
    /// and tarot slots draw through an enabled child instead. Writing to it succeeds silently
    /// and changes nothing on screen, which is exactly what the first two builds did.
    ///
    /// Among what's left, the biggest sprite is the item on sale; shadows, highlights and price
    /// pips are all smaller. Press F1 in a shop (DebugActions.DumpShopSlots) to see the field.
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
    ///
    /// Replacing rather than appending, because the card name is actively misleading: the slot
    /// is a location, and what buying it produces is whatever the multiworld put there. The
    /// card itself comes from the item pool, on its own schedule.
    ///
    /// Rebuilt through the game's own format string and cost formatter so it stays localised
    /// and keeps the vanilla "for &lt;icon&gt; N" shape. Runs from the Label getter, which the game
    /// polls while the player stands near a slot, so it stays cheap.
    /// </summary>
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
    ///
    /// This panel, not the buy prompt, is where the check's details belong: it's the surface
    /// with room for them, and it's already the thing a player reads before deciding to spend.
    /// </summary>
    private void HandleTarotDisplayBuilt(UITarotDisplay display, TarotCards.Card card)
    {
        if (!cardToCheckId.TryGetValue(card.ToString(), out var checkId)) return;

        ShopSlotDisplayPatch.SetTarotDisplayText(
            display, CardTitle, CardLore, DescribeCheck(checkId));
    }

    /// <summary>What this check holds and who it's for, in the panel's body.</summary>
    private string DescribeCheck(long checkId)
    {
        // Before the scout lands - one server round trip after connecting - there's nothing to
        // name, and saying so beats naming the wrong thing.
        if (!scoutedItems.TryGetValue(checkId, out var scouted))
        {
            return "An Archipelago check.\nAsking the server what it holds...";
        }

        var location = LocationName(checkId);

        return scouted.ForLocalPlayer
            ? $"Sends <b>{scouted.ItemName}</b> to you.\n<i>{location}</i>"
            : $"Sends <b>{scouted.ItemName}</b> to <b>{scouted.PlayerName}</b>, "
                + $"playing {scouted.Game}.\n<i>{location}</i>";
    }

    private string LocationName(long checkId)
    {
        try
        {
            var name = session.Locations.GetLocationNameFromId(checkId, "Cult of the Lamb");
            return string.IsNullOrEmpty(name) ? $"Check {checkId}" : name;
        }
        catch
        {
            return $"Check {checkId}";
        }
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

    /// <summary>What the multiworld put at one shop location.</summary>
    private class ScoutedCheck
    {
        internal string ItemName;
        internal string PlayerName;
        internal string Game;
        internal bool ForLocalPlayer;
    }
}
