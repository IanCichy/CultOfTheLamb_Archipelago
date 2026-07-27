using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check for each of the first N Followers recruited.
///
/// FollowerManager.OnFollowerAdded is the single funnel: however a Follower is acquired, they
/// only join the flock by being indoctrinated at the base, and that event fires immediately
/// after DataManager.Followers.Add (FollowerManager.cs:1236). It's a public static event, so
/// unlike most of this mod's hooks no Harmony patch is needed.
///
/// The FollowerRecruit.OnRecruitFinalised event was tried first and is the wrong hook - it
/// fires mid-recruit-animation, before the Follower is in the list, which showed up in testing
/// as the count sticking one behind.
///
/// The count is derived from save state rather than tallied in-session, so it stays correct
/// across reconnects and across Followers recruited while disconnected. It counts Followers
/// *ever* recruited (living + dead) rather than the current flock: a plague, a sacrifice
/// spree, or a Ritual can shrink the flock, and a milestone you already passed must not
/// become unreachable again.
///
/// Every check up to the current count is sent each time rather than just the newest one.
/// That's deliberately idempotent - the server ignores repeats - and it means a missed event
/// or a save edited outside the mod still self-corrects on the next recruitment.
/// </summary>
internal class FollowerMilestoneService : IService
{
    private readonly ArchipelagoSession session;
    private readonly long locationBaseId;
    private readonly int locationCount;

    internal FollowerMilestoneService(ArchipelagoSession session, long locationBaseId, int locationCount)
    {
        this.session = session;
        this.locationBaseId = locationBaseId;
        this.locationCount = locationCount;
    }

    public void Register()
    {
        FollowerManager.OnFollowerAdded += HandleFollowerAdded;
        // Catch up on anything recruited before this connect.
        SendChecksUpTo(CountEverRecruited());
        Log.LogInfo($"[AP] Follower milestones active: {locationCount} location(s) "
            + $"from id {locationBaseId}.");
    }

    public void Unregister()
    {
        FollowerManager.OnFollowerAdded -= HandleFollowerAdded;
        highestSent = 0;
    }

    private void HandleFollowerAdded(int followerId) => SendChecksUpTo(CountEverRecruited());

    /// <summary>
    /// Backstop re-check, called on a throttle from the plugin's Update.
    ///
    /// OnFollowerAdded should catch everything - it fires straight after Followers.Add - but a
    /// few paths write the list directly without going through AddFollower (CheatConsole does,
    /// and save migration might). The poll is three list-count reads and stays silent unless
    /// the number actually moved, so it's cheap insurance against a silently-missed milestone.
    /// </summary>
    internal void Tick()
    {
        var recruited = CountEverRecruited();
        if (recruited > highestSent) SendChecksUpTo(recruited);
    }

    /// <summary>
    /// Counts Followers ever *indoctrinated* - the living flock plus the dead.
    ///
    /// Deliberately excludes Followers_Recruit: however you acquire a Follower, they must be
    /// indoctrinated at the base to actually join, and that list is the queue of ones who
    /// haven't been yet. Including the dead is what stops a plague or a sacrifice spree from
    /// making an already-passed milestone unreachable.
    /// </summary>
    private static int CountEverRecruited()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null) return 0;

        var living = dataManager.Followers?.Count ?? 0;
        var dead = dataManager.Followers_Dead?.Count ?? 0;
        return living + dead;
    }

    /// <summary>Highest milestone already sent, so polling stays quiet when nothing changed.</summary>
    private int highestSent;

    private void SendChecksUpTo(int recruited)
    {
        if (recruited <= 0) return;

        var highest = recruited < locationCount ? recruited : locationCount;
        if (highest <= highestSent) return;

        var checkIds = new long[highest];
        for (var i = 0; i < highest; i++)
        {
            checkIds[i] = locationBaseId + i;
        }

        // Announce only what's actually new. The full 1..N range is re-sent every time for
        // idempotency, but the player has already seen the earlier ones.
        var newlySent = new long[highest - highestSent];
        for (var i = 0; i < newlySent.Length; i++)
        {
            newlySent[i] = locationBaseId + highestSent + i;
        }

        highestSent = highest;
        Log.LogInfo($"[AP] {recruited} Follower(s) ever recruited "
            + $"(living {DataManager.Instance?.Followers?.Count ?? 0}, "
            + $"dead {DataManager.Instance?.Followers_Dead?.Count ?? 0}) "
            + $"- sending milestone checks 1-{highest}.");
        session.Locations.CompleteLocationChecks(checkIds);
        CheckNotifier.Announce(session, newlySent);
    }
}
