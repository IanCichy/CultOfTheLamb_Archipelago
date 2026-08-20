using System.Collections.Generic;
using Archipelago.CultOfTheLamb.UI;
using BepInEx.Configuration;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// A line of podiums in the base - every weapon, a gap, then every curse - showing which families
/// Archipelago has granted.
///
/// These are the only randomized system with no native screen anywhere in the game. The weapon and
/// curse wheels are in-run only and read the transient pool, and there is no PlayerFoundWeapons to
/// mirror how PlayerFoundTrinkets backs the tarot collection. So unlike sermons or tarot there is
/// nothing to unhide, and the display has to be built.
///
/// Nothing is written to save data: no StructureBrain, no StructureManager entry. The podiums are
/// respawned per scene load and destroyed on disconnect, so a save that stops using this mod is
/// unchanged.
/// </summary>
internal class EquipmentDisplayService : IService
{
    private readonly EquipmentPoolService weapons;
    private readonly EquipmentPoolService curses;

    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<float> originX;
    private readonly ConfigEntry<float> originY;
    private readonly ConfigEntry<float> spacing;
    private readonly ConfigEntry<float> poolGap;

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

    internal EquipmentDisplayService(
        EquipmentPoolService weapons,
        EquipmentPoolService curses,
        ConfigEntry<bool> enabled,
        ConfigEntry<float> originX,
        ConfigEntry<float> originY,
        ConfigEntry<float> spacing,
        ConfigEntry<float> poolGap,
        bool showTeleport)
    {
        this.weapons = weapons;
        this.curses = curses;
        this.enabled = enabled;
        this.originX = originX;
        this.originY = originY;
        this.spacing = spacing;
        this.poolGap = poolGap;
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
        if (!enabled.Value)
        {
            if (placed) Unregister();
            return;
        }

        if (!InBase())
        {
            // The podiums belong to the base scene and are gone with it; drop our references so
            // the next visit rebuilds rather than re-tinting destroyed objects.
            if (placed) Unregister();
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
    /// One line: every weapon, a gap, then every curse. The gap is what separates the two systems
    /// visually - a single unbroken run of twelve podiums reads as one undifferentiated set.
    ///
    /// Only a pool the seed actually randomizes is placed at all; an empty one would just be a
    /// line of locked podiums for something never in play.
    /// </summary>
    private void Place()
    {
        var origin = new Vector3(originX.Value, originY.Value, 0f);

        var placedCount = PlaceGroup(weapons, origin);
        if (placedCount > 0)
        {
            origin += new Vector3(placedCount * spacing.Value + poolGap.Value, 0f, 0f);
        }

        var curseCount = PlaceGroup(curses, origin);

        if (showTeleport)
        {
            // On the end of the curse group rather than in its own cluster - it is a curse family,
            // it just doesn't arrive through the curse pool.
            teleportShown = TeleportReceived();
            EquipmentPedestal.Spawn(
                EquipmentType.Teleport,
                origin + new Vector3(curseCount * spacing.Value, 0f, 0f),
                teleportShown);
        }

        placed = true;
        Log.LogInfo($"[AP] Equipment displays placed - {EquipmentPedestal.Count} podium(s).");
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
