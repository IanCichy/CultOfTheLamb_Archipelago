using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Static view of which regions Archipelago currently allows, so Harmony patches (which are
/// static and have no reference to the session) can consult it.
/// </summary>
/// <remarks>
/// RegionUnlockService owns the writes. The patches only read.
///
/// Needed because unlocking alone isn't enough to gate regions. The vanilla flow in
/// Interaction_BaseDungeonDoor.OnInteract() opens a door purely on the follower-count requirement
/// (HaveFollowers), never consulting UnlockedDungeonDoor. Without an explicit block the player can
/// open an AP-locked region's door normally.
/// </remarks>
internal static class RegionLockState
{
    private static readonly HashSet<FollowerLocation> unlocked = new();

    /// <summary>
    /// Whether an AP session is actually managing regions.
    /// </summary>
    /// <remarks>
    /// Locking is only enforced while this is true. Otherwise a disconnected or vanilla session
    /// would have every door permanently locked.
    /// </remarks>
    internal static bool Active { get; set; }

    internal static void Reset()
    {
        unlocked.Clear();
        Active = false;
    }

    internal static void MarkUnlocked(FollowerLocation location) => unlocked.Add(location);

    /// <summary>
    /// True for the 4 base-game Bishop regions, the only ones AP gates.
    /// </summary>
    /// <remarks>
    /// Asks the region table, not the check-id map. Which dungeons exist is game knowledge and is
    /// true before a connection, where the id map is seed data and empty until one.
    /// </remarks>
    internal static bool IsManaged(FollowerLocation location) =>
        RegionMapping.RegionToDungeonLocation.ContainsValue(location);

    internal static bool IsUnlocked(FollowerLocation location) => unlocked.Contains(location);

    /// <summary>True when AP is actively holding this door shut.</summary>
    internal static bool IsLockedByArchipelago(FollowerLocation location) =>
        Active && IsManaged(location) && !IsUnlocked(location);
}
