using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Reports every finished building.
///
/// The game's own OnBuildComplete is a parameterless Action, so it says something finished but
/// not what. Build() is patched instead because it knows the structure type.
///
/// Simple buildings and multi-stage projects use two separate classes, Structures_BuildSite and
/// Structures_BuildSiteProject. Patching only the first would leave every multi-stage building
/// silent.
///
/// Decorations are not filtered here. BuildingService only reports structures named in the
/// buildingLocations map the seed sends it, drawn from the apworld's 25 buildings, and filtering
/// in two places is two places to get it wrong.
/// </summary>
internal static class StructureBuildPatch
{
    // Set by BuildingService while connected. Null leaves the game untouched
    internal static Action<StructureBrain.TYPES> Built;

    [HarmonyPatch(typeof(Structures_BuildSite), nameof(Structures_BuildSite.Build))]
    internal static class BuildSite
    {
        [HarmonyPostfix]
        private static void Postfix(Structures_BuildSite __instance)
        {
            Report(__instance?.Data);
        }
    }

    [HarmonyPatch(typeof(Structures_BuildSiteProject), "Build")]
    internal static class BuildSiteProject
    {
        [HarmonyPostfix]
        private static void Postfix(Structures_BuildSiteProject __instance)
        {
            Report(__instance?.Data);
        }
    }

    // Guarded here rather than in each postfix, since both call sites want the same thing: a
    // failed send shouldn't leave a building half finished in the player's base.
    private static void Report(StructuresData data)
    {
        if (data == null)
        {
            return;
        }

        try
        {
            Built?.Invoke(data.ToBuildType);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] A building finished but reporting it failed: {e}");
        }
    }

    // Ceiling for BuildDurationGameMinutes. 0 leaves it alone
    internal static int BuildTimeCap;

    /// <summary>
    /// Caps how much build progress a structure needs.
    ///
    /// Despite the name, BuildDurationGameMinutes returns a progress total, not a duration.
    /// Interaction_PlayerBuild adds a flat 5 per hammer swing, while FollowerTask_Build adds
    /// elapsed game time scaled by the Follower's ProductivityMultiplier, which is where the
    /// misleading name comes from.
    ///
    /// The method ends in two defaults, 120 for AESTHETIC types and 300 for everything else not
    /// named in its switch. Only 4 of the apworld's buildings have an explicit value (Temple 120,
    /// Sleeping Bag, Body Pit and Farm Plot 30); the other 21 take the 300 default.
    ///
    /// So capping at 30, which is six hammer swings, flattens most building checks rather than a
    /// rare deep tier. Pure quality of life, and it matters more here than in vanilla because
    /// building is now a check.
    /// </summary>
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.BuildDurationGameMinutes))]
    internal static class BuildDuration
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            if (BuildTimeCap > 0)
            {
                __result = UnityEngine.Mathf.Min(__result, BuildTimeCap);
            }
        }
    }
}
