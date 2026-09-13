using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Miniboss and Witness kill key to AP location id, filled from slot data at connect
/// </summary>
/// <remarks>
/// The key is what DataManager.KilledBosses stores, MiniBossController.name, which is also the
/// boss's follower skin name (DcplIdx 3a). These aren't the names players see.
///
/// The location ids come from the world in slot data. Only the key strings live here, since
/// they're game knowledge, not seed data.
/// </remarks>
internal static class BossKeyMapping
{
    // Suffix the game appends when re-killing a boss in post-game (Layer2) mode
    internal const string PostGameSuffix = "_P2";

    // Empty until Populate runs at connect. Only read from connected code paths
    internal static Dictionary<string, long> BossKeyToCheckId { get; private set; } = new();

    internal static void Populate(IReadOnlyDictionary<string, object> slotData)
    {
        BossKeyToCheckId = SlotData.ParseIdMap(slotData, "bossKeyLocations");
        Log.LogInfo($"[AP] Boss checks: {BossKeyToCheckId.Count} miniboss/Witness location(s) mapped.");
    }

    internal static void Clear()
    {
        BossKeyToCheckId = new Dictionary<string, long>();
    }

    // The four Witness (Beholder) kill keys, in Darkwood/Anura/Anchordeep/Silk Cradle order.
    // Confirmed by DataManager.cs:740-743, where BeatenWitnessDungeon1..4 are computed as
    // CheckKilledBosses("Boss Beholder 1".."4"). Game knowledge, so it isn't sent
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
