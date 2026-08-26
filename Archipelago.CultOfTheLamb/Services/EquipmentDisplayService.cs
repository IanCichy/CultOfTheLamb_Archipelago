using System.Collections.Generic;
using Archipelago.CultOfTheLamb.UI;
using BepInEx.Configuration;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The base displays: a row of podiums per equipment pool showing which families Archipelago has
/// granted, and the collection book between them.
/// </summary>
/// <remarks>
/// These are the only randomized system with no native screen anywhere in the game: the weapon and
/// curse wheels are in-run only and read the transient pool, and there is no PlayerFoundWeapons to
/// mirror how PlayerFoundTrinkets backs the tarot collection. So unlike sermons or tarot there is
/// nothing to unhide, and the display has to be built.
///
/// Nothing is written to save data - no StructureBrain, no StructureManager entry. The podiums are
/// respawned per scene load and destroyed on disconnect, so a save that stops using this mod is
/// unchanged.
/// </remarks>
internal class EquipmentDisplayService : IService
{
    private readonly EquipmentPoolService weapons;
    private readonly EquipmentPoolService curses;

    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<bool> bookEnabled;
    private readonly ConfigEntry<float> spacing;

    /// <summary>
    /// Where each display starts, independently of the others.
    /// </summary>
    /// <remarks>
    /// Three anchors rather than one origin the rest are measured from. Chained, the curse row and
    /// the book moved whenever the weapon row changed length - two units for a Woolhaven seventh
    /// weapon, twenty-three when weapons weren't randomized at all - so the same base laid itself
    /// out differently per seed. Anchored, switching any system off leaves the other two put.
    /// </remarks>
    private readonly ConfigEntry<float> weaponX;
    private readonly ConfigEntry<float> weaponY;
    private readonly ConfigEntry<float> curseX;
    private readonly ConfigEntry<float> curseY;
    private readonly ConfigEntry<float> bookX;
    private readonly ConfigEntry<float> bookY;

    /// <summary>
    /// Whether to show a podium for the Teleport curses.
    ///
    /// They're the one curse family with no default variant - all three arrive with the
    /// Curses_Teleport sermon upgrade, which is Woolhaven-only. That still reaches the player
    /// through Archipelago, just as a sermon item rather than a curse-pool item, so leaving it off
    /// the row would hide a family they can genuinely earn.
    /// </summary>
    private readonly bool showTeleport;

    /// <summary>Last known state of the Teleport podium; it isn't in either pool.</summary>
    private bool teleportShown;

    /// <summary>What each podium is showing, so a re-tint only happens when something changed.</summary>
    private readonly Dictionary<EquipmentType, bool> shown = new();

    private bool placed;

    /// <summary>What the config said when we placed, so a mid-session toggle is noticed.</summary>
    private bool pedestalsPlaced;
    private bool bookPlaced;

    internal EquipmentDisplayService(
        EquipmentPoolService weapons,
        EquipmentPoolService curses,
        ConfigEntry<bool> enabled,
        ConfigEntry<bool> bookEnabled,
        ConfigEntry<float> spacing,
        ConfigEntry<float> weaponX,
        ConfigEntry<float> weaponY,
        ConfigEntry<float> curseX,
        ConfigEntry<float> curseY,
        ConfigEntry<float> bookX,
        ConfigEntry<float> bookY,
        bool showTeleport)
    {
        this.weapons = weapons;
        this.curses = curses;
        this.enabled = enabled;
        this.bookEnabled = bookEnabled;
        this.spacing = spacing;
        this.weaponX = weaponX;
        this.weaponY = weaponY;
        this.curseX = curseX;
        this.curseY = curseY;
        this.bookX = bookX;
        this.bookY = bookY;
        this.showTeleport = showTeleport;
    }

    /// <summary>Whether the Teleport curse family has been earned, via its sermon upgrade.</summary>
    private static bool TeleportReceived() =>
        UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Curses_Teleport);

    public void Register()
    {
    }

    public void Unregister()
    {
        EquipmentPedestal.Clear();
        CollectionBook.Clear();
        shown.Clear();
        placed = false;
    }

    /// <summary>
    /// Polled rather than driven by a scene-load event, for the same reason
    /// ArchipelagoHudIndicator.EnsureExists is: the base is rebuilt on every load and this
    /// re-places itself afterwards without needing to know which path rebuilt it.
    /// </summary>
    internal void Tick()
    {
        // Either display alone is reason enough to run: the book is useful in a seed that
        // randomizes no equipment at all, so it isn't gated on the podiums being on.
        if (!enabled.Value && !bookEnabled.Value)
        {
            if (placed) Unregister();
            return;
        }

        if (!InBase())
        {
            // The podiums belong to the base scene and are gone with it; drop our references so
            // the next visit rebuilds rather than re-tinting destroyed objects.
            if (placed) Unregister();

            // Out here is the only place a lectern exists to copy - one stands beside every tarot
            // shop and vendor, and the base has none.
            if (bookEnabled.Value) CollectionBook.TryAdoptFromScene();
            return;
        }

        // A toggle flipped since we placed. Tearing down here rather than letting Refresh() run is
        // what makes turning one display off actually remove it: the early-out above only fires
        // when *both* are off, so without this, unticking the podiums while the book is on left
        // them standing with no way to place them again.
        if (placed && (pedestalsPlaced != enabled.Value || bookPlaced != bookEnabled.Value))
        {
            Unregister();
            return;
        }

        if (!placed)
        {
            Place();
            return;
        }

        Refresh();
    }

    private static bool InBase()
    {
        if (PlayerFarming.Instance == null || DataManager.Instance == null) return false;

        var location = PlayerFarming.Location;
        return location == FollowerLocation.Base || location == FollowerLocation.Church;
    }

    /// <summary>
    /// Puts each display at its own anchor: the weapon row, the curse row, and the book between
    /// them. Only a pool the seed actually randomizes is placed at all, since an empty one would
    /// just be a line of locked podiums for something never in play.
    /// </summary>
    private void Place()
    {
        var curseOrigin = new Vector3(curseX.Value, curseY.Value, 0f);

        if (enabled.Value) PlaceGroup(weapons, new Vector3(weaponX.Value, weaponY.Value, 0f));

        var curseCount = enabled.Value ? PlaceGroup(curses, curseOrigin) : 0;

        // The pool check keeps a seed that randomizes no equipment from getting a lone Teleport
        // podium: this service registers unconditionally for the book's sake, where it used to
        // exist only when a pool did.
        if (enabled.Value && showTeleport && (weapons != null || curses != null))
        {
            // On the end of the curse row rather than in its own cluster - it is a curse family,
            // it just doesn't arrive through the curse pool.
            teleportShown = TeleportReceived();
            EquipmentPedestal.Spawn(
                EquipmentType.Teleport,
                curseOrigin + new Vector3(curseCount * spacing.Value, 0f, 0f),
                teleportShown);
        }

        PlaceBook();

        placed = true;
        pedestalsPlaced = enabled.Value;
        bookPlaced = bookEnabled.Value;
        Log.LogInfo($"[AP] Equipment displays placed - {EquipmentPedestal.Count} podium(s), "
            + $"{CollectionBook.Count} book(s).");
    }

    /// <summary>
    /// Stands the collection book at its own anchor, defaulting to the gap between the two rows.
    /// </summary>
    /// <remarks>
    /// Its own coordinates rather than a position derived from the podium rows. Derived, it moved
    /// whenever the weapon row changed length, and a weapons-off seed left it marooned off the side
    /// of the base - an independent anchor is also what keeps it present when no equipment is
    /// randomized at all.
    /// </remarks>
    private void PlaceBook()
    {
        if (!bookEnabled.Value) return;

        var position = new Vector3(bookX.Value, bookY.Value, 0f);

        if (CollectionBook.Spawn(position) != null)
        {
            Log.LogInfo($"[AP] Collection book placed at ({position.x:0.##}, {position.y:0.##}).");
        }
    }

    /// <summary>Returns how many podiums it placed, so the caller knows where the next group starts.</summary>
    private int PlaceGroup(EquipmentPoolService pool, Vector3 origin)
    {
        if (pool == null) return 0;

        var families = pool.Managed;
        if (families == null || families.Count == 0) return 0;

        var index = 0;
        foreach (var family in families)
        {
            var received = pool.IsGranted(family);
            EquipmentPedestal.Spawn(
                family, origin + new Vector3(index * spacing.Value, 0f, 0f), received);

            shown[family] = received;
            index++;
        }

        return index;
    }

    /// <summary>
    /// Re-lights a podium when its family arrives mid-session. Only touches ones whose state
    /// actually changed, so the common case is a handful of dictionary reads.
    /// </summary>
    private void Refresh()
    {
        RefreshPool(weapons);
        RefreshPool(curses);

        // Teleport is in neither pool, so it needs its own comparison.
        if (showTeleport)
        {
            var received = TeleportReceived();
            if (received != teleportShown)
            {
                teleportShown = received;
                EquipmentPedestal.SetReceived(EquipmentType.Teleport, received);
            }
        }
    }

    private void RefreshPool(EquipmentPoolService pool)
    {
        if (pool == null) return;

        foreach (var family in pool.Managed)
        {
            var received = pool.IsGranted(family);
            if (shown.TryGetValue(family, out var was) && was == received) continue;

            shown[family] = received;
            EquipmentPedestal.SetReceived(family, received);
        }
    }
}
