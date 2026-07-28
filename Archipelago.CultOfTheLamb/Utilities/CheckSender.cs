using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Sends location checks, skipping the ones the server already has.
///
/// Every service that derives its checks from save state - Followers, Snail Shrines, and
/// anything added later - necessarily re-derives them from scratch on each connect, because
/// that's what makes offerings and recruits made while disconnected get caught up. The side
/// effect is that reconnecting re-sends checks the player sent sessions ago. Harmless to the
/// server, which ignores duplicates, but the player got a popup for each one all over again.
///
/// Filtering here rather than in the services keeps each one free to re-derive as bluntly as
/// it likes, and means catch-up logic never has to reason about what the server already knows.
///
/// This is not the same thing as a service's own "already sent" flags: those cover repeat polls
/// within one session, this covers everything from previous ones.
/// </summary>
internal static class CheckSender
{
    /// <summary>
    /// Sends <paramref name="checkIds"/> and announces the ones that were genuinely new.
    /// Safe to hand the full re-derived set on every connect.
    /// </summary>
    internal static void Send(ArchipelagoSession session, IReadOnlyList<long> checkIds)
    {
        if (session == null || checkIds == null || checkIds.Count == 0) return;

        // Populated from the Connected packet's checked_locations, so it's already accurate by
        // the time services register.
        var alreadyChecked = session.Locations.AllLocationsChecked;
        var pending = checkIds.Where(id => !alreadyChecked.Contains(id)).ToArray();

        if (pending.Length == 0)
        {
            Log.LogInfo($"[AP] {checkIds.Count} check(s) already recorded by the server - "
                + "not resending.");
            return;
        }

        session.Locations.CompleteLocationChecks(pending);
        CheckNotifier.Announce(session, pending);
    }

    internal static void Send(ArchipelagoSession session, long checkId) =>
        Send(session, new[] { checkId });
}
