using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Maps the game's internal miniboss/Witness kill keys to AP location ids.
///
/// The key is the string the game stores in DataManager.KilledBosses - which is
/// MiniBossController.name (the Unity GameObject name), and is *also* that boss's
/// follower-skin name. That equivalence is why these strings are recoverable from code at
/// all: they're declared verbatim in UIFollowerFormsMenuController's per-region
/// DarkwoodOrder/AnuraOrder/AnchordeepOrder/SilkCradleOrder arrays. See
/// DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md §3a for the full derivation and citations.
///
/// Internal names are NOT the display names players see (the wiki's Amdusias, Valefar, ...);
/// those live in I2 as MiniBossController.DisplayName.
///
/// The internal -> display alignment below was confirmed in-game by dumping a live Darkwood
/// boss room: Boss Mama Worm = Amdusias, Boss Mama Maggot = Valefar, Boss Burrow Worm =
/// Barbatos, Boss Beholder 1 = Witness Agares - i.e. list position matches the wiki roster
/// order exactly. The other three regions are built the same way. The Darkwood row was also
/// verified end-to-end: killing that miniboss sent check 3051000 and the server resolved it
/// to "Darkwood - Amdusias".
/// </summary>
internal static class BossKeyMapping
{
    /// <summary>Suffix the game appends when re-killing a boss in post-game (Layer2) mode.</summary>
    internal const string PostGameSuffix = "_P2";

    internal static readonly Dictionary<string, long> BossKeyToCheckId = new()
    {
        // Darkwood
        { "Boss Mama Worm", CultOfTheLambIds.DarkwoodAmdusiasLocationId },
        { "Boss Mama Maggot", CultOfTheLambIds.DarkwoodValefarLocationId },
        { "Boss Burrow Worm", CultOfTheLambIds.DarkwoodBarbatosLocationId },
        { "Boss Beholder 1", CultOfTheLambIds.DarkwoodWitnessAgaresLocationId },

        // Anura
        { "Boss Flying Burp Frog", CultOfTheLambIds.AnuraGusionLocationId },
        { "Boss Egg Hopper", CultOfTheLambIds.AnuraEligosLocationId },
        { "Boss Mortar Hopper", CultOfTheLambIds.AnuraZeparLocationId },
        { "Boss Beholder 2", CultOfTheLambIds.AnuraWitnessBathinLocationId },

        // Anchordeep
        { "Boss Spiker", CultOfTheLambIds.AnchordeepSaleosLocationId },
        { "Boss Charger", CultOfTheLambIds.AnchordeepHaborymLocationId },
        { "Boss Scuttle Turret", CultOfTheLambIds.AnchordeepBaalzebubLocationId },
        { "Boss Beholder 3", CultOfTheLambIds.AnchordeepWitnessAstarothLocationId },

        // Silk Cradle
        { "Boss Spider Jump", CultOfTheLambIds.SilkCradleFocalorLocationId },
        { "Boss Millipede Poisoner", CultOfTheLambIds.SilkCradleVepharLocationId },
        { "Boss Scorpion", CultOfTheLambIds.SilkCradleHaurasLocationId },
        { "Boss Beholder 4", CultOfTheLambIds.SilkCradleWitnessAllocerLocationId },
    };

    /// <summary>
    /// The four Witness (Beholder) kill keys, in Darkwood/Anura/Anchordeep/Silk Cradle order.
    /// Confirmed by DataManager.cs:740-743, where BeatenWitnessDungeon1..4 are computed as
    /// CheckKilledBosses("Boss Beholder 1".."4").
    /// </summary>
    internal static readonly string[] WitnessKeys =
    {
        "Boss Beholder 1",
        "Boss Beholder 2",
        "Boss Beholder 3",
        "Boss Beholder 4",
    };

    internal static bool IsPostGameVariant(string bossKey) =>
        bossKey != null && bossKey.EndsWith(PostGameSuffix);
}
