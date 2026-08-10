using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Reports every finished building.
///
/// <c>Structures_BuildSite.OnBuildComplete</c> looks like the hook and isn't: it's a
/// per-instance <c>Action</c> with no arguments, so it says *that* something finished, not
/// *what*. <c>Build()</c> has <c>Data.ToBuildType</c> right there.
///
/// **Two classes need patching.** Simple structures go through
/// <see cref="Structures_BuildSite"/>; multi-stage ones go through
/// <see cref="Structures_BuildSiteProject"/>, whose <c>Build()</c> is private. Patch only the
/// first and every project building silently never fires.
///
/// Decorations are not filtered here. Both originals branch on
/// <c>StructuresData.GetCategory(...) == AESTHETIC</c>, but the service works from an explicit
/// list of 25 buildings, so anything not on it is ignored anyway - and filtering twice would
/// just be a second place to get the category wrong.
/// </summary>
internal static class StructureBuildPatch
{
    /// <summary>Set by BuildingService while connected; null leaves the game untouched.</summary>
    internal static Action<StructureBrain.TYPES> Built;

    [HarmonyPatch(typeof(Structures_BuildSite), nameof(Structures_BuildSite.Build))]
    internal static class BuildSite
    {
        [HarmonyPostfix]
        private static void Postfix(Structures_BuildSite __instance) => Report(__instance?.Data);
    }

    [HarmonyPatch(typeof(Structures_BuildSiteProject), "Build")]
    internal static class BuildSiteProject
    {
        [HarmonyPostfix]
        private static void Postfix(Structures_BuildSiteProject __instance) =>
            Report(__instance?.Data);
    }

    private static void Report(StructuresData data)
    {
        if (data == null) return;
        Built?.Invoke(data.ToBuildType);
    }

    /// <summary>Longest any structure may take to build, in game-minutes. 0 leaves it alone.</summary>
    internal static int BuildTimeCap;

    /// <summary>
    /// Caps how long a structure takes.
    ///
    /// Vanilla runs from 10 game-minutes to 9000 for the late Temple tiers, with most buildings
    /// at 30, 300 or 600. Capping at 30 - a Sleeping Bag - makes everything quick. Pure quality
    /// of life, and it matters more here than in vanilla because building is now a check.
    /// </summary>
    [HarmonyPatch(typeof(StructuresData), nameof(StructuresData.BuildDurationGameMinutes))]
    internal static class BuildDuration
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            if (BuildTimeCap > 0) __result = UnityEngine.Mathf.Min(__result, BuildTimeCap);
        }
    }
}
