using System;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check for each broom level earned by sweeping
/// </summary>
/// <remarks>
/// <c>DataManager.ChoreXPLevel</c> is save persisted, so the Nth level is the Nth check.
///
/// The counter is incremented in two places (PlayerChoreXPBarController:62 and :234 solo and co-op)
/// checking it catches sweeping done while disconnected and will re-send any checks that were missed.
/// </remarks>
internal class BroomService : IService
{
    private readonly ArchipelagoSession session;
    private readonly long locationBaseId;
    private readonly int locationCount;

    // Highest level reported
    private int sentUpTo;

    internal BroomService(ArchipelagoSession session, long locationBaseId, int locationCount)
    {
        this.session = session;
        this.locationBaseId = locationBaseId;
        this.locationCount = locationCount;
    }

    public void Register()
    {
        Tick();
        Log.LogInfo($"[AP] Broom checks active: {locationCount} location(s) from id "
            + $"{locationBaseId}, level {CurrentLevel()} already reached.");
    }

    public void Unregister()
    {
        sentUpTo = 0;
    }

    private static int CurrentLevel() => DataManager.Instance?.ChoreXPLevel ?? 0;

    internal void Tick()
    {
        var level = Math.Min(CurrentLevel(), locationCount);
        if (level <= sentUpTo)
        {
            return;
        }

        // Send everything up to the current level
        var ids = new long[level];
        for (var i = 0; i < level; i++)
        {
            ids[i] = locationBaseId + i;
        }

        sentUpTo = level;
        Log.LogInfo($"[AP] Broom level {level} reached, sending up to check {locationBaseId + level - 1}.");
        CheckSender.Send(session, ids);
    }

    // For debugging, F9 prints
    internal string DescribeState() =>
        $"Broom: level {CurrentLevel()}/{locationCount}, {DataManager.Instance?.ChoreXP ?? 0f} chore XP toward the next.";
}
