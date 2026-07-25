using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Region name (matches worlds/cult_of_the_lamb's REGION_NAMES / slot data "regionOrder"
/// strings) <-> the game's own FollowerLocation enum values for each region's home-base
/// door and Bishop-kill completion slot. See
/// DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md §3 for how these were confirmed (each
/// Enemy*Boss class's own BossesCompleted.Contains(FollowerLocation.Dungeon1_N) check).
/// </summary>
internal static class RegionMapping
{
    internal static readonly Dictionary<string, FollowerLocation> RegionToDungeonLocation = new()
    {
        { "Darkwood", FollowerLocation.Dungeon1_1 },
        { "Anura", FollowerLocation.Dungeon1_2 },
        { "Anchordeep", FollowerLocation.Dungeon1_3 },
        { "Silk Cradle", FollowerLocation.Dungeon1_4 },
    };

    internal static readonly Dictionary<FollowerLocation, long> BishopLocationToCheckId = new()
    {
        { FollowerLocation.Dungeon1_1, CultOfTheLambIds.DarkwoodLeshyLocationId },
        { FollowerLocation.Dungeon1_2, CultOfTheLambIds.AnuraHeketLocationId },
        { FollowerLocation.Dungeon1_3, CultOfTheLambIds.AnchordeepKallamarLocationId },
        { FollowerLocation.Dungeon1_4, CultOfTheLambIds.SilkCradleShamuraLocationId },
    };
}
