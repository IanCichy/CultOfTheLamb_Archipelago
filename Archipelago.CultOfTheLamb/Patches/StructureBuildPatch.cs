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

    // Longest any structure may take to build, in game-minutes. 0 leaves it alone
    internal static int BuildTimeCap;

    /// <summary>
    /// Caps how long a structure takes.
    ///
    /// Vanilla runs from 10 game-minutes to 9000 for the late Temple tiers, with most buildings
    /// at 30, 300 or 600. Capping at 30, the cost of a Sleeping Bag, makes everything quick.
    /// Pure quality of life, and it matters more here than in vanilla because building is now a
    /// check.
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
