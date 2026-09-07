using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Reports every finished building.
/// </summary>
/// <remarks>
/// <c>Structures_BuildSite.OnBuildComplete</c> is a per-instance <c>Action</c> with no
/// arguments, so it reports that something finished but not what. <c>Build()</c> has
/// <c>Data.ToBuildType</c> right there.
///
/// Two classes need patching: simple structures go through <c>Structures_BuildSite</c>,
/// multi-stage ones through <c>Structures_BuildSiteProject</c>, whose <c>Build()</c> is
/// private. Patch only the first and every project building silently never fires.
///
/// Decorations are not filtered here. Both originals branch on
/// <c>StructuresData.GetCategory(...) == AESTHETIC</c>, but the service works from an explicit
/// list of 25 buildings, so filtering twice would be a second place to get it wrong.
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
