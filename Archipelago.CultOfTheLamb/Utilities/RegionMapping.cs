using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Region name (matches worlds/cult_of_the_lamb's REGION_NAMES / slot data "regionOrder"
/// strings) &lt;-&gt; the game's own FollowerLocation enum values for each region's home-base door
/// and Bishop-kill completion slot. See DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md section 3 for how
/// these were confirmed (each Enemy*Boss class's own
/// BossesCompleted.Contains(FollowerLocation.Dungeon1_N) check).
/// </summary>
internal static class RegionMapping
{
    /// <summary>Region name -> its dungeon. Game knowledge, so it isn't sent in slot data.</summary>
    internal static readonly Dictionary<string, FollowerLocation> RegionToDungeonLocation = new()
    {
        { "Darkwood", FollowerLocation.Dungeon1_1 },
        { "Anura", FollowerLocation.Dungeon1_2 },
        { "Anchordeep", FollowerLocation.Dungeon1_3 },
        { "Silk Cradle", FollowerLocation.Dungeon1_4 },
    };

    /// <summary>
    /// Bishop kill slot -> AP location id, filled from slot data at connect.
    ///
    /// The ids used to be hardcoded here as `3_051_000 + N`, which made reordering locations.py
    /// silently repoint the four checks the goal depends on. Empty until Populate runs; every
    /// reader is on a connected path.
    /// </summary>
    internal static Dictionary<FollowerLocation, long> BishopLocationToCheckId { get; private set; }
        = new();

    internal static void Populate(IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<FollowerLocation, long>();

        // Keyed by FollowerLocation member name ("Dungeon1_1") rather than region name, because
        // that's what the kill event hands us and it can't be reordered the way a list can.
        foreach (var entry in SlotData.ParseIdMap(slotData, "bishopLocations"))
        {
            if (SlotData.TryParseEnum<FollowerLocation>(entry.Key, "bishopLocations", out var loc))
            {
                result[loc] = entry.Value;
            }
        }

        BishopLocationToCheckId = result;
        Log.LogInfo($"[AP] Bishop checks: {result.Count} location(s) mapped.");
    }

    internal static void Clear() => BishopLocationToCheckId = new Dictionary<FollowerLocation, long>();
}
