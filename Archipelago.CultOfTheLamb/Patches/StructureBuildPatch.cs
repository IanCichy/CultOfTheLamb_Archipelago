using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Reports every finished building
/// </summary>
/// <remarks>
/// OnBuildComplete says something finished but not what, so we patch Build(), which knows the
/// building type.
///
/// Simple buildings and multi-stage ones use two different classes. Patch only the first and the
/// multi-stage buildings never report.
///
/// Decorations aren't filtered here. The service already works from a list of 25 buildings, and
/// filtering in two places is two places to get it wrong.
/// </remarks>
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

    private static void Report(StructuresData data)
    {
        if (data == null)
        {
            return;
        }

        Built?.Invoke(data.ToBuildType);
    }

    // Ceiling for BuildDurationGameMinutes. 0 leaves it alone
    internal static int BuildTimeCap;

    /// <summary>
    /// Caps how much build progress a structure needs.
    ///
    /// Despite the name, BuildDurationGameMinutes returns a progress total, not a duration.
    /// Interaction_PlayerBuild adds 5 per hammer swing; FollowerTask_Build adds game time
    /// scaled by the Follower's productivity, which is where the name comes from.
    ///
    /// Vanilla values are 10, 15, 20, 30, 60, 120, 180, then 300 for Temple III and IV and 600
    /// for the shrine upgrades and repairables. The two Temple extensions are 6000 and 9000,
    /// but those are follower building projects rather than something hammered out.
    ///
    /// Capping at 30 is six hammer swings. Most buildings only lose a few seconds to it; the
    /// 300 and 600 tier is what it really flattens. Pure quality of life, and it matters more
    /// here than in vanilla because building is now a check.
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
