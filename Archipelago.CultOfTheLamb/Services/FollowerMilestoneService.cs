using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Sends a check for each of the first N Followers recruited.
/// </summary>
/// <remarks>
/// FollowerManager.OnFollowerAdded is the single funnel and a public static event, so no Harmony
/// patch is needed. FollowerRecruit.OnRecruitFinalised is the wrong hook: it fires mid animation,
/// before the Follower is in the list, so the count sticks one behind.
///
/// The count is derived from save state rather than tallied in session, so it survives reconnects
/// and catches up recruits made while disconnected. It counts Followers ever recruited rather
/// than the current flock, so a plague or a sacrifice spree can't make a passed milestone
/// unreachable. Every check up to the current count is re-sent each time.
/// </remarks>
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
        SendChecksUpTo(CountEverRecruited());
        Log.LogInfo($"[AP] Follower milestones active: {locationCount} location(s) "
            + $"from id {locationBaseId}.");
    }

    public void Unregister()
    {
        FollowerManager.OnFollowerAdded -= HandleFollowerAdded;
        highestSent = 0;
    }

    private void HandleFollowerAdded(int followerId)
    {
        SendChecksUpTo(CountEverRecruited());
    }

    // Backstop for the paths that write Followers directly instead of going through
    // AddFollower, so OnFollowerAdded never fires. CheatConsole does this, save migration may
    internal void Tick()
    {
        var recruited = CountEverRecruited();
        if (recruited > highestSent)
        {
            SendChecksUpTo(recruited);
        }
    }

    // Living flock plus the dead. Followers_Recruit is excluded because it is the queue of
    // Followers not yet indoctrinated, and indoctrination is what makes one count. Counting
    // the dead keeps a plague or a sacrifice spree from making a passed milestone unreachable
    private static int CountEverRecruited()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            return 0;
        }

        var living = dataManager.Followers?.Count ?? 0;
        var dead = dataManager.Followers_Dead?.Count ?? 0;
        return living + dead;
    }

    // Highest milestone already sent, so polling stays quiet when nothing changed
    private int highestSent;

    private void SendChecksUpTo(int recruited)
    {
        if (recruited <= 0)
        {
            return;
        }

        var highest = recruited < locationCount ? recruited : locationCount;
        if (highest <= highestSent)
        {
            return;
        }

        var checkIds = new long[highest];
        for (var i = 0; i < highest; i++)
        {
            checkIds[i] = locationBaseId + i;
        }

        highestSent = highest;
        Log.LogInfo($"[AP] {recruited} Follower(s) ever recruited "
            + $"(living {DataManager.Instance?.Followers?.Count ?? 0}, "
            + $"dead {DataManager.Instance?.Followers_Dead?.Count ?? 0}) "
            + $"- sending milestone checks 1-{highest}.");

        // The full 1..N range every time: CheckSender drops whatever the server
        // already has, so this can stay a blunt re-derivation without re-announcing old
        // milestones on connect.
        CheckSender.Send(session, checkIds);
    }
}
