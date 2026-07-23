using Archipelago.MultiClient.Net;
using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Gates access to dungeon regions (Anura, Darkwood, Anchordeep, Silk Cradle, ...) behind
/// AP items, mirroring StageBlockerService from the RoR2 mod. Cult of the Lamb has no
/// multiplayer, so unlike StageBlockerService there's no cross-client sync layer needed -
/// this is purely local state.
/// TODO: identify how the game exposes "which region can I travel to" (crusade/dungeon
/// selection screen) so we can block/unblock entries here.
/// </summary>
internal class RegionUnlockService : IService
{
    private readonly ArchipelagoSession session;
    private readonly HashSet<string> unlockedRegions = new();

    public RegionUnlockService(ArchipelagoSession session)
    {
        this.session = session;
    }

    public void Register()
    {
        // TODO: hook the region/dungeon-select UI so locked regions are hidden or
        // rejected until the matching AP item has been received.
    }

    public void Unregister()
    {
    }

    public bool IsRegionUnlocked(string regionName) => unlockedRegions.Contains(regionName);

    public void UnlockRegion(string regionName)
    {
        unlockedRegions.Add(regionName);
    }
}
