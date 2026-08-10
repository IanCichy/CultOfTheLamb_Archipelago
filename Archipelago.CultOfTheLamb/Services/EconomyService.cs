using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The seed's pacing caps: how much Devotion an ability point costs, how much XP a Temple
/// upgrade costs, and how long a structure takes to build.
///
/// All three exist because the game's curves are built for completion over dozens of hours,
/// while an Archipelago seed wants those same blocks finishable in one. Each is the same shape -
/// a postfix clamping one public static - so they live together rather than being scattered
/// across the services that happen to care about them.
///
/// Deliberately **not** tied to whether the matching block is randomized. These are quality of
/// life, not randomizer settings: a seed with sermon randomization off should still be able to
/// cap sermon XP, and a seed with Divine Inspiration off should still cap Devotion. So this
/// registers unconditionally and reads the caps straight from slot data.
/// </summary>
internal class EconomyService : IService
{
    private readonly int devotionCap;
    private readonly int sermonXpCapTenths;
    private readonly int buildTimeCap;

    internal EconomyService(int devotionCap, int sermonXpCapTenths, int buildTimeCap)
    {
        this.devotionCap = devotionCap;
        this.sermonXpCapTenths = sermonXpCapTenths;
        this.buildTimeCap = buildTimeCap;
    }

    public void Register()
    {
        DivineInspirationPatch.DevotionCap = devotionCap;
        SermonUpgradePatch.XpCapTenths = sermonXpCapTenths;
        StructureBuildPatch.BuildTimeCap = buildTimeCap;

        var applied = new List<string>();
        if (devotionCap > 0) applied.Add($"Devotion {devotionCap}");
        if (sermonXpCapTenths > 0) applied.Add($"sermon XP {sermonXpCapTenths / 10f:0.#}");
        if (buildTimeCap > 0) applied.Add($"build time {buildTimeCap}m");

        Log.LogInfo(applied.Count == 0
            ? "[AP] No pacing caps this seed - the game's own curves apply."
            : $"[AP] Pacing caps: {string.Join(", ", applied)}.");
    }

    public void Unregister()
    {
        DivineInspirationPatch.DevotionCap = 0;
        SermonUpgradePatch.XpCapTenths = 0;
        StructureBuildPatch.BuildTimeCap = 0;
    }
}
