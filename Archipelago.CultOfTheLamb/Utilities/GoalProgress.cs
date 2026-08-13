namespace Archipelago.CultOfTheLamb;

/// <summary>
/// How far along the seed's win condition is, counted from the game's own save state.
///
/// Shared by GoalService (which reports victory to the server) and QuestGuideService (which
/// shows the same number as a quest line). Two readers meant two chances to disagree about
/// what "defeated" means - particularly around the "_P2" post-game Witness re-fights - so the
/// counting lives in one place.
///
/// Save state rather than a session tally, deliberately: BossesCompleted and KilledBosses are
/// both already written by the time our handlers run, so these are correct after a reconnect
/// and for progress made before ever connecting. See AI_INDEX.md §3 and §3a.
/// </summary>
internal static class GoalProgress
{
    /// <summary>How many of the four base-game Bishops have been killed.</summary>
    internal static int CountDefeatedBishops()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null || dataManager.BossesCompleted == null) return 0;

        var count = 0;
        foreach (var bishopLocation in RegionMapping.BishopLocationToCheckId.Keys)
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

    /// <summary>Progress toward whichever track this seed's Goal option picked.</summary>
    internal static int CountForGoal(int goal) =>
        goal == Services.GoalService.GoalWitnesses ? CountDefeatedWitnesses() : CountDefeatedBishops();
}
