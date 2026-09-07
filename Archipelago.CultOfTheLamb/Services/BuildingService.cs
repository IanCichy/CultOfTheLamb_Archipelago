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
/// Named locations rather than sequential, unlike most blocks here. Which building you put up is
/// a real choice, so "Build - Kitchen" says more than "Building 12".
///
/// The set comes from slot data, so the client never needs its own opinion about which of the
/// game's 332 structures are interesting, and the two sides can't drift.
/// </remarks>
internal class BuildingService : IService
{
    private readonly ArchipelagoSession session;

    /// <summary>Structure -> the check its first construction sends.</summary>
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

    public void Unregister() => StructureBuildPatch.Built = null;

    private void OnBuilt(StructureBrain.TYPES type)
    {
        if (!structureToCheckId.TryGetValue(type, out var checkId)) return;

        Log.LogInfo($"[AP] Built {type} - sending check {checkId}.");
        CheckSender.Send(session, checkId);
    }

    /// <summary>
    /// Sends a check for every managed structure already standing.
    ///
    /// The game keeps a first-built record only for *decorations*
    /// (DataManager.DecorationTypesBuilt), and none of these are decorations, so "have you ever
    /// built one" has to be answered by looking at what exists right now. That means a building
    /// demolished before connecting is missed, which is the right way round: CheckSender makes
    /// re-sends free, so the check lands whenever one is standing at any future connect.
    /// </summary>
    private void SendChecksForExisting()
    {
        var pending = new List<long>();

        foreach (var pair in structureToCheckId)
        {
            // Guarded per structure. This runs during connect, and one bad type shouldn't cost
            // the rest of the catch-up.
            try
            {
                var standing = StructureManager.GetAllStructuresOfType(pair.Key);
                if (standing != null && standing.Count > 0) pending.Add(pair.Value);
            }
            catch (Exception e)
            {
                Log.LogWarning($"[AP] Couldn't count existing {pair.Key}: {e.Message}");
            }
        }

        CheckSender.Send(session, pending);
    }

    /// <summary>
    /// StructureBrain.TYPES name -> location id, from "buildingLocations". Names this build of
    /// the game doesn't recognise are dropped with a warning, because losing one check beats
    /// losing the connection.
    /// </summary>
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
            if (!SlotData.TryParseEnum<StructureBrain.TYPES>(
                    entry.Key, "buildingLocations", out var structure))
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

    /// <summary>What F9 prints.</summary>
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
