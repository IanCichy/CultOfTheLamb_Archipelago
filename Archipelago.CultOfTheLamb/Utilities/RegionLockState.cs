using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Static view of which regions Archipelago currently allows, so Harmony patches (which are
/// static and have no reference to the session) can consult it. RegionUnlockService owns the
/// writes; the patches only read.
///
/// Needed because unlocking alone isn't enough to gate regions: the vanilla flow in
/// Interaction_BaseDungeonDoor.OnInteract() opens a door purely on the follower-count
/// requirement (HaveFollowers), never consulting UnlockedDungeonDoor - so without an
/// explicit block the player can open an AP-locked region's door normally.
/// </summary>
internal static class RegionLockState
{
    private static readonly HashSet<FollowerLocation> unlocked = new();

    /// <summary>
    /// Only enforce locking while an AP session is actually managing regions - otherwise a
    /// disconnected/vanilla session would have every door permanently locked.
    /// </summary>
    internal static bool Active { get; set; }

    internal static void Reset()
    {
        unlocked.Clear();
        Active = false;
    }

    internal static void MarkUnlocked(FollowerLocation location) => unlocked.Add(location);

    /// <summary>
    /// True for the 4 base-game Bishop regions - the only ones AP gates.
    ///
    /// Asks the region table, not the check-id map: which dungeons exist is game knowledge and is
    /// true before a connection, where the id map is seed data and empty until one.
    /// </summary>
    internal static bool IsManaged(FollowerLocation location) =>
        RegionMapping.RegionToDungeonLocation.ContainsValue(location);

    internal static bool IsUnlocked(FollowerLocation location) => unlocked.Contains(location);

    /// <summary>True when AP is actively holding this door shut.</summary>
    internal static bool IsLockedByArchipelago(FollowerLocation location) =>
        Active && IsManaged(location) && !IsUnlocked(location);
}
