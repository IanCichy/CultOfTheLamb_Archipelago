namespace Archipelago.CultOfTheLamb;

// Decides whether connecting the loaded save to a seed deserves a warning first.
internal static class SaveOwnership
{
    // Past this many dungeon entries a save with no Archipelago history was likely played without it.
    // The counter starts at -1 and gains one per entry (DataManager.cs:4508, :686), and Quick Start sets it to 1 (:448).
    private const int PlayedDungeonRuns = 2;

    // A warning for the player, or null when the save is fine to connect.
    // `key` is this session's AppliedItemStore key
    internal static string Warning(string key)
    {
        var history = AppliedItemStore.KeysFor(SaveSlot.Current);

        if (history.Count > 0)
        {
            return history.Contains(key)
                ? null
                : "This save was played on a different Archipelago seed or slot. Connecting to it "
                    + "risks breaking this seed's run. Make sure you want to do this.";
        }

        var data = DataManager.Instance;
        var bishops = data.BossesCompleted?.Count ?? 0;

        if (bishops == 0 && data.dungeonRun <= PlayedDungeonRuns)
        {
            return null;
        }

        return "This save has never used Archipelago and has already been played "
            + $"({data.dungeonRun} crusades, {bishops} Bishop(s) beaten). Archipelago is meant "
            + "for a new save, and connecting adds every item from this seed to it.";
    }
}
