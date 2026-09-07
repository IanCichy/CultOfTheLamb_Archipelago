using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check the first time the player builds each of a curated set of structures.
/// </summary>
/// <remarks>
/// Named locations rather than sequential. "Build - Kitchen" > "Building 12".
/// </remarks>
internal class BuildingService : IService
{
    private readonly ArchipelagoSession session;

    // Structure list to keep track of which have been built and which haven't. The value is the location id to send.
    private readonly Dictionary<StructureBrain.TYPES, long> structureToCheckId;

    internal BuildingService(
        ArchipelagoSession session, Dictionary<StructureBrain.TYPES, long> structureToCheckId)
    {
        this.session = session;
        this.structureToCheckId = structureToCheckId
            ?? new Dictionary<StructureBrain.TYPES, long>();
    }

    public void Register()
    {
        StructureBuildPatch.Built = OnBuilt;
        SendChecksForExisting();
        Log.LogInfo($"[AP] Building checks active: {structureToCheckId.Count} structure(s) mapped.");
    }

    public void Unregister()
    {
        StructureBuildPatch.Built = null;
    }

    private void OnBuilt(StructureBrain.TYPES type)
    {
        if (!structureToCheckId.TryGetValue(type, out var checkId))
        {
            return;
        }

        Log.LogInfo($"[AP] Built {type} - sending check {checkId}.");
        CheckSender.Send(session, checkId);
    }

    // DataManager.DecorationTypesBuilt records first build for decorations only
    // This reads what is standing right now in base. A building demolished
    // before connecting is missed until one is standing
    private void SendChecksForExisting()
    {
        var pending = new List<long>();

        foreach (var pair in structureToCheckId)
        {
            // Guarded per structure. This runs during connect, and one bad type wont tank
            // the rest of the catch-up.
            try
            {
                var standing = StructureManager.GetAllStructuresOfType(pair.Key);
                if (standing != null && standing.Count > 0)
                {
                    pending.Add(pair.Value);
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"[AP] Couldn't count existing {pair.Key}: {e.Message}");
            }
        }

        CheckSender.Send(session, pending);
    }

    // StructureBrain.TYPES name -> location id, from buildingLocations. Unrecognised names are dropped with a warning
    internal static Dictionary<StructureBrain.TYPES, long> ParseLocations(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<StructureBrain.TYPES, long>();

        if (!slotData.TryGetValue("buildingLocations", out var raw) || raw is not JObject mapping)
        {
            Log.LogWarning("[AP] Building checks are on but slot data has no buildingLocations "
                + "mapping - no building will send a check.");
            return result;
        }

        foreach (var entry in mapping)
        {
            if (!SlotData.TryParseEnum<StructureBrain.TYPES>(entry.Key, "buildingLocations", out var structure))
            {
                continue;
            }

            try
            {
                result[structure] = entry.Value.ToObject<long>();
            }
            catch (Exception e)
            {
                Log.LogWarning("[AP] Slot data has a non-numeric location id for structure "
                    + $"'{entry.Key}': {e.Message} - skipping it.");
            }
        }

        return result;
    }

    // For debugging, F9 prints
    internal string DescribeState()
    {
        var standing = structureToCheckId.Keys
            .Where(type =>
            {
                try { return StructureManager.GetAllStructuresOfType(type)?.Count > 0; }
                catch { return false; }
            })
            .ToList();

        return $"Buildings: {standing.Count}/{structureToCheckId.Count} built."
            + $"\n  Standing: {string.Join(", ", standing)}";
    }
}
