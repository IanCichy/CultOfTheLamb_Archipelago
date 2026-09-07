using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Static view of which regions Archipelago currently allows, so Harmony patches (which are
/// static and have no reference to the session) can consult it.
/// </summary>
/// <remarks>
/// RegionUnlockService owns the writes. The patches only read.
///
/// Unlocking alone isn't enough to gate regions. Interaction_BaseDungeonDoor.OnInteract() opens
/// a door purely on the follower-count requirement (HaveFollowers), never consulting
/// UnlockedDungeonDoor, so without an explicit block the player opens an AP-locked door.
/// </remarks>
internal static class RegionLockState
{
    private static readonly HashSet<FollowerLocation> unlocked = new();

    /// <summary>
    /// Whether an AP session is actually managing regions.
    /// </summary>
    /// <remarks>
    /// Locking is enforced only while this is true, or a disconnected session would have every
    /// door permanently locked.
    /// </remarks>
    internal static bool Active { get; set; }

    internal static void Reset()
    {
        unlocked.Clear();
        Active = false;
    }

    internal static void MarkUnlocked(FollowerLocation location)
    {
        unlocked.Add(location);
    }

    /// <summary>
    /// True for the 4 base-game Bishop regions, the only ones AP gates.
    /// </summary>
    /// <remarks>
    /// Asks the region table, not the check-id map. Which dungeons exist is game knowledge, true
    /// before a connection, where the id map is seed data and empty until one.
    /// </remarks>
    internal static bool IsManaged(FollowerLocation location) =>
        RegionMapping.RegionToDungeonLocation.ContainsValue(location);

    internal static bool IsUnlocked(FollowerLocation location) => unlocked.Contains(location);

    // True when AP is actively holding this door shut
    internal static bool IsLockedByArchipelago(FollowerLocation location) =>
        Active && IsManaged(location) && !IsUnlocked(location);
}
