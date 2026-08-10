using System;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check for each broom level earned by sweeping.
///
/// <c>DataManager.ChoreXPLevel</c> is monotonic and save-persisted - the same shape as the sermon
/// and Divine Inspiration counters - so the Nth level is the Nth check.
///
/// Polled rather than patched. The counter is incremented in two places
/// (PlayerChoreXPBarController:62 and :234, the solo and co-op paths), but it's save state, so
/// reading it also catches sweeping done while disconnected and re-derives correctly after a
/// reload. Same reasoning as SnailShrineService.
/// </summary>
internal class BroomService : IService
{
    private readonly ArchipelagoSession session;
    private readonly long locationBaseId;
    private readonly int locationCount;

    /// <summary>Highest level reported, so a steady-state poll stays silent.</summary>
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

    public void Unregister() => sentUpTo = 0;

    private static int CurrentLevel() => DataManager.Instance?.ChoreXPLevel ?? 0;

    /// <summary>Called on a throttle from the plugin's Update.</summary>
    internal void Tick()
    {
        var level = Math.Min(CurrentLevel(), locationCount);
        if (level <= sentUpTo) return;

        // Everything up to the current level, not just the newest - idempotent via CheckSender,
        // so a missed poll or a reload self-corrects.
        var ids = new long[level];
        for (var i = 0; i < level; i++) ids[i] = locationBaseId + i;

        sentUpTo = level;
        Log.LogInfo($"[AP] Broom level {level} reached, sending up to check "
            + $"{locationBaseId + level - 1}.");
        CheckSender.Send(session, ids);
    }

    /// <summary>What F9 prints.</summary>
    internal string DescribeState() =>
        $"Broom: level {CurrentLevel()}/{locationCount}, {DataManager.Instance?.ChoreXP ?? 0f} "
        + "chore XP toward the next.";
}
