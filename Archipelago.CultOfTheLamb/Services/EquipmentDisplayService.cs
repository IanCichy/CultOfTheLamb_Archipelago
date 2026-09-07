using System.Collections.Generic;
using Archipelago.CultOfTheLamb.UI;
using BepInEx.Configuration;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The base displays. A row of podiums per equipment pool showing which families Archipelago has
/// granted, and the collection book between them.
/// </summary>
/// <remarks>
/// The only randomized system with no native screen anywhere in the game. The weapon and curse
/// wheels are in-run only and read the transient pool, and there is no PlayerFoundWeapons to
/// mirror how PlayerFoundTrinkets backs the tarot collection, so there is nothing to unhide.
///
/// Nothing is written to save data: no StructureBrain, no StructureManager entry. The podiums
/// are respawned per scene load and destroyed on disconnect, so a save that stops using this
/// mod is unchanged.
/// </remarks>
internal class EquipmentDisplayService : IService
{
    private readonly EquipmentPoolService weapons;
    private readonly EquipmentPoolService curses;

    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<bool> bookEnabled;
    private readonly ConfigEntry<float> spacing;

    // Three independent anchors, not one origin the rest are measured from. Chained, the curse
    // row and the book moved whenever the weapon row changed length: two units for a Woolhaven
    // seventh weapon, twenty-three with weapons not randomized at all, so the same base laid
    // itself out differently per seed
    private readonly ConfigEntry<float> weaponX;
    private readonly ConfigEntry<float> weaponY;
    private readonly ConfigEntry<float> curseX;
    private readonly ConfigEntry<float> curseY;
    private readonly ConfigEntry<float> bookX;
    private readonly ConfigEntry<float> bookY;

    // The one curse family with no default variant. All three arrive with the Curses_Teleport
    // sermon upgrade, which is Woolhaven-only, so they reach the player as a sermon item rather
    // than a curse-pool item. Leaving them off the row would hide a family they can earn
    private readonly bool showTeleport;

    // Last known state of the Teleport podium, which isn't in either pool
    private bool teleportShown;

    // What each podium is showing, so a re-tint only happens when something changed
    private readonly Dictionary<EquipmentType, bool> shown = new();

    private bool placed;

    // What the config said when we placed, so a mid-session toggle is noticed
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

    // Whether the Teleport curse family has been earned, via its sermon upgrade
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

    // Polled rather than driven by a scene-load event. The base is rebuilt on every load and
    // this re-places itself afterwards without needing to know which path rebuilt it
    internal void Tick()
    {
        // Either display alone is reason enough to run. The book is useful in a seed that
        // randomizes no equipment at all, so it isn't gated on the podiums being on.
        if (!enabled.Value && !bookEnabled.Value)
        {
            if (placed)
            {
                Unregister();
            }

            return;
        }

        if (!InBase())
        {
            // The podiums belong to the base scene and are gone with it, so drop our references
            // and the next visit rebuilds rather than re-tinting destroyed objects.
            if (placed)
            {
                Unregister();
            }

            // Out here is the only place a lectern exists to copy. One stands beside every tarot
            // shop and vendor, and the base has none.
            if (bookEnabled.Value)
            {
                CollectionBook.TryAdoptFromScene();
            }

            return;
        }

        // A toggle flipped since we placed. Tearing down here rather than letting Refresh() run is
        // what makes turning one display off remove it. The early-out above only fires
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
        if (PlayerFarming.Instance == null || DataManager.Instance == null)
        {
            return false;
        }

        var location = PlayerFarming.Location;
        return location == FollowerLocation.Base || location == FollowerLocation.Church;
    }

    // Weapon row, curse row, book, each at its own anchor. Only a pool the seed randomizes is
    // placed, since an empty one would be a line of locked podiums for something never in play
    private void Place()
    {
        var curseOrigin = new Vector3(curseX.Value, curseY.Value, 0f);

        if (enabled.Value)
        {
            PlaceGroup(weapons, new Vector3(weaponX.Value, weaponY.Value, 0f));
        }

        var curseCount = enabled.Value ? PlaceGroup(curses, curseOrigin) : 0;

        // The pool check keeps a seed that randomizes no equipment from getting a lone Teleport
        // podium. This service registers unconditionally for the book's sake, where it used to
        // exist only when a pool did.
        if (enabled.Value && showTeleport && (weapons != null || curses != null))
        {
            // On the end of the curse row rather than in its own cluster. It is a curse family,
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

    // Stands the book at its own anchor, defaulting to the gap between the two rows. Its own
    // coordinates keep it present when no equipment is randomized at all
    private void PlaceBook()
    {
        if (!bookEnabled.Value)
        {
            return;
        }

        var position = new Vector3(bookX.Value, bookY.Value, 0f);

        if (CollectionBook.Spawn(position) != null)
        {
            Log.LogInfo($"[AP] Collection book placed at ({position.x:0.##}, {position.y:0.##}).");
        }
    }

    // Returns how many podiums it placed, so the caller knows where the next group starts
    private int PlaceGroup(EquipmentPoolService pool, Vector3 origin)
    {
        if (pool == null)
        {
            return 0;
        }

        var families = pool.Managed;
        if (families == null || families.Count == 0)
        {
            return 0;
        }

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

    // Re-lights a podium when its family arrives mid-session. Only touches ones whose state
    // actually changed
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
        if (pool == null)
        {
            return;
        }

        foreach (var family in pool.Managed)
        {
            var received = pool.IsGranted(family);
            if (shown.TryGetValue(family, out var was) && was == received)
            {
                continue;
            }

            shown[family] = received;
            EquipmentPedestal.SetReceived(family, received);
        }
    }
}
