using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Miniboss/Witness kill key -> AP location id, filled from slot data at connect.
///
/// The key is what DataManager.KilledBosses stores: MiniBossController.name, which is also the
/// boss's follower-skin name - the equivalence that makes these strings recoverable from code at
/// all (see AI_INDEX.md section 3a). These are not the display names players see; those live in
/// I2 as MiniBossController.DisplayName.
///
/// The ids used to be written out here as `3_051_000 + N`, where N was the row's position in
/// locations.py's dict - so reordering that dict silently repointed every boss check, and these
/// are the goal-critical ones. The world sends them now, like every other block does. The key
/// strings stay here because they're game knowledge, not seed data.
/// </summary>
internal static class BossKeyMapping
{
    /// <summary>Suffix the game appends when re-killing a boss in post-game (Layer2) mode.</summary>
    internal const string PostGameSuffix = "_P2";

    /// <summary>Empty until Populate runs at connect. Only read from connected code paths.</summary>
    internal static Dictionary<string, long> BossKeyToCheckId { get; private set; } = new();

    internal static void Populate(IReadOnlyDictionary<string, object> slotData)
    {
        BossKeyToCheckId = SlotData.ParseIdMap(slotData, "bossKeyLocations");
        Log.LogInfo($"[AP] Boss checks: {BossKeyToCheckId.Count} miniboss/Witness location(s) mapped.");
    }

    internal static void Clear() => BossKeyToCheckId = new Dictionary<string, long>();

    /// <summary>
    /// The four Witness (Beholder) kill keys, in Darkwood/Anura/Anchordeep/Silk Cradle order.
    /// Confirmed by DataManager.cs:740-743, where BeatenWitnessDungeon1..4 are computed as
    /// CheckKilledBosses("Boss Beholder 1".."4"). Game knowledge, so it isn't sent.
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
