using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Maps the game's internal miniboss/Witness kill keys to AP location ids. The key is what
/// DataManager.KilledBosses stores: MiniBossController.name, which is also the boss's
/// follower-skin name - the equivalence that makes these strings recoverable from code at all
/// (see AI_INDEX.md §3a).
///
/// These are not the display names players see; those live in I2 as
/// MiniBossController.DisplayName. The alignment below was confirmed in-game by dumping a live
/// Darkwood boss room - list position matches the wiki roster order exactly - and the Darkwood
/// row was verified end-to-end against the server.
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
