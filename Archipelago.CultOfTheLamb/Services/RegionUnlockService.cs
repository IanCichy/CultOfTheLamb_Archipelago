using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Force-opens dungeon regions by writing directly to
/// DataManager.Instance.UnlockedDungeonDoor - see DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md
/// §3 ("RESOLVED - the region unlock mechanism"). Region 0 in regionOrder is free from
/// connect; each further region opens as a "Progressive Bishop's Domain" copy arrives
/// (see worlds/cult_of_the_lamb/rules.py for the matching Python-side logic).
/// </summary>
internal class RegionUnlockService : IService
{
    private readonly List<string> regionOrder;
    private int unlockedCount;

    internal RegionUnlockService(List<string> regionOrder)
    {
        this.regionOrder = regionOrder;
    }

    public void Register()
    {
        unlockedCount = 0;
        RegionLockState.Reset();
        if (regionOrder == null || regionOrder.Count == 0)
        {
            Log.LogWarning("[AP] RegionUnlockService: no regionOrder in slot data, nothing to unlock.");
            return;
        }
        // Enforce locking only once we know the seed's region order - otherwise the door
        // patches would hold every managed region shut with no way to open them.
        RegionLockState.Active = true;
        Log.LogInfo($"[AP] Region order this seed: {string.Join(" -> ", regionOrder.ToArray())} "
            + "(first is free, the rest open in that order).");
        UnlockRegion(regionOrder[0]);
        unlockedCount = 1;
    }

    public void Unregister()
    {
        // Stop enforcing locks on disconnect so the save is playable as vanilla again.
        RegionLockState.Reset();
    }

    /// <summary>Call when a Progressive Bishop's Domain item is received.</summary>
    internal void UnlockNextRegion()
    {
        if (regionOrder == null || unlockedCount >= regionOrder.Count)
        {
            Log.LogWarning("[AP] RegionUnlockService: received a region-access item but all regions are already unlocked.");
            return;
        }
        UnlockRegion(regionOrder[unlockedCount]);
        unlockedCount++;
    }

    private void UnlockRegion(string regionName)
    {
        if (!RegionMapping.RegionToDungeonLocation.TryGetValue(regionName, out var location))
        {
            Log.LogWarning($"[AP] RegionUnlockService: unknown region name '{regionName}'.");
            return;
        }

        // TODO: guard with SaveAndLoad.Loaded (confirmed in AI_INDEX.md §9) once a save can
        // reliably be assumed loaded at this point - for now this assumes Register()/
        // UnlockNextRegion() only get called while a save is active.
        if (DataManager.Instance == null)
        {
            Log.LogWarning($"[AP] RegionUnlockService: DataManager.Instance is null, can't unlock {regionName} yet.");
            return;
        }

        // Record it first: the door patches read RegionLockState, and marking it unlocked
        // is what stops them from stripping the entry back out again.
        RegionLockState.MarkUnlocked(location);

        if (!DataManager.Instance.UnlockedDungeonDoor.Contains(location))
        {
            DataManager.Instance.UnlockedDungeonDoor.Add(location);
            Log.LogInfo($"[AP] Unlocked region: {regionName} ({location})");
        }
        else
        {
            // Log this case too. Staying silent when the door was already in the save made
            // "did that item actually do anything?" impossible to answer from the log.
            Log.LogInfo($"[AP] Region {regionName} ({location}) was already open in the save "
                + "- now also unlocked by Archipelago.");
        }
    }
}
