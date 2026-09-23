using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The seed's pacing caps. That is how much Devotion an ability point costs, how much XP a Temple
/// upgrade costs, and how long a structure takes to build.
/// </summary>
/// <remarks>
/// The game's curves are built for completion over dozens of hours, while an Archipelago seed
/// wants those blocks finishable in one. All three are the same shape, one postfix clamping one
/// public static, so they live together.
///
/// Not tied to whether the matching block is randomized. These are quality of life, not
/// randomization settings, so a seed with sermon randomization off can still cap sermon XP.
/// </remarks>
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
        if (devotionCap > 0)
        {
            applied.Add($"Devotion {devotionCap}");
        }

        if (sermonXpCapTenths > 0)
        {
            applied.Add($"sermon XP {sermonXpCapTenths / 10f:0.#}");
        }

        if (buildTimeCap > 0)
        {
            applied.Add($"build progress {buildTimeCap}");
        }

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
