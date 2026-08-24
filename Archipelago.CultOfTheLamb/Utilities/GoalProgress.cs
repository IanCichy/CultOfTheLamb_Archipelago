namespace Archipelago.CultOfTheLamb;

/// <summary>
/// How far along the seed's win condition is, counted from the game's own save state.
/// </summary>
/// <remarks>
/// Shared by GoalService (which reports victory to the server) and QuestGuideService (which
/// shows the same number as a quest line). Two readers meant two chances to disagree about
/// what "defeated" means - particularly around the "_P2" post-game Witness re-fights - so the
/// counting lives in one place.
///
/// Save state rather than a session tally, deliberately: BossesCompleted and KilledBosses are
/// both already written by the time our handlers run, so these are correct after a reconnect
/// and for progress made before ever connecting. See AI_INDEX.md §3 and §3a.
/// </remarks>
internal static class GoalProgress
{
    /// <summary>How many of the four base-game Bishops have been killed.</summary>
    internal static int CountDefeatedBishops()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null || dataManager.BossesCompleted == null) return 0;

        // The region table rather than the check-id map: counting kills doesn't depend on any id,
        // and this stays correct even before a connection has filled the seed's mappings in.
        var count = 0;
        foreach (var bishopLocation in RegionMapping.RegionToDungeonLocation.Values)
        {
            if (dataManager.BossesCompleted.Contains(bishopLocation)) count++;
        }
        return count;
    }

    /// <summary>
    /// Counts the base-game Witnesses only, deliberately ignoring the "_P2" post-game
    /// re-fights - a Purged-run kill shouldn't count toward a goal the player hasn't met in
    /// the base run. Reads KilledBosses directly rather than DataManager's
    /// BeatenWitnessDungeon1..4 booleans, which are only refreshed at specific points.
    /// </summary>
    internal static int CountDefeatedWitnesses()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null || dataManager.KilledBosses == null) return 0;

        var count = 0;
        foreach (var witnessKey in BossKeyMapping.WitnessKeys)
        {
            if (dataManager.KilledBosses.Contains(witnessKey)) count++;
        }
        return count;
    }

    /// <summary>
    /// Whether Narinder has been beaten on this save.
    ///
    /// DeathCatBeaten is per-save and written only by EnemyDeathCatBoss.OnDie - except on New
    /// Game+ entry, which force-sets it without a fight (MMBiomeGeneration/BiomeGenerator.cs:234).
    /// Accepted rather than worked around: starting NG+ on an Archipelago save is already outside
    /// the save policy, and this is the only save-backed signal there is.
    /// </summary>
    internal static bool NarinderDefeated() => DataManager.Instance?.DeathCatBeaten ?? false;

    /// <summary>How many of this goal's encounters are done. Narinder is 0 or 1 of 1.</summary>
    internal static int CountForGoal(int goal) => goal switch
    {
        Services.GoalService.GoalWitnesses => CountDefeatedWitnesses(),
        Services.GoalService.GoalNarinder => NarinderDefeated() ? 1 : 0,
        _ => CountDefeatedBishops(),
    };

    /// <summary>How many this goal needs, or 0 to use the seed's Required Count.</summary>
    internal static int FixedTargetForGoal(int goal) =>
        goal == Services.GoalService.GoalNarinder ? 1 : 0;
}
