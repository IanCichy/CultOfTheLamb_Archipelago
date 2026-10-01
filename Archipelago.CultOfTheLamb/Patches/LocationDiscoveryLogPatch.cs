#if AP_DEBUG_KEYS
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Debug only. Records what unlocks each hub, which the decompile cannot answer.
/// </summary>
/// <remarks>
/// The reveals are wired in scene data, so there is no C# call site to read. This logs what was
/// discovered and where the player was standing.
/// </remarks>
[HarmonyPatch(typeof(DataManager), nameof(DataManager.DiscoverLocation))]
internal static class LocationDiscoveryLogPatch
{
    private static void Postfix(FollowerLocation location, bool __result)
    {
        // False means it was already known, which is the uninteresting case
        if (!__result)
        {
            return;
        }

        Log.LogInfo($"[AP] Debug: DISCOVERED {location} while the player was in "
            + $"{PlayerFarming.Location}, day {TimeManager.CurrentDay}.");
    }
}

/// <summary>
/// Debug only. Reports when a one-off NPC room is armed for the run about to start.
/// </summary>
/// <remarks>
/// These are the rooms that reveal the hubs. Logged at run setup, so the log shows which run
/// armed which room.
/// </remarks>
[HarmonyPatch(typeof(DataManager), nameof(DataManager.SetNewRun))]
internal static class SpecialRoomArmedLogPatch
{
    private static void Postfix(FollowerLocation location)
    {
        var d = DataManager.Instance;
        if (d == null)
        {
            return;
        }

        var armed = "";
        if (d.ShowSpecialSozoRoom) armed += " Sozo";
        if (d.ShowSpecialPlimboRoom) armed += " Plimbo";
        if (d.ShowSpecialLighthouseKeeperRoom) armed += " LighthouseKeeper";
        if (d.ShowSpecialFishermanRoom) armed += " Fisherman";
        if (d.ShowSpecialMidasRoom) armed += " Midas";

        Log.LogInfo(armed.Length == 0
            ? $"[AP] Debug: no special NPC room armed for the {location} run."
            : $"[AP] Debug: special NPC room(s) armed for the {location} run:{armed}.");
    }
}
#endif
