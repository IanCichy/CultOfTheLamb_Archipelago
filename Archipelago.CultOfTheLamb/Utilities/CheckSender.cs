using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Sends location checks, skipping the ones the server already has.
/// </summary>
/// <remarks>
/// Services that derive checks from save state re-derive them on every connect, which is what
/// catches up progress made while disconnected - but it also re-sends checks from sessions ago,
/// popping a notification for each. Filtering here leaves each service free to re-derive as
/// bluntly as it likes.
///
/// Not the same as a service's own "already sent" flags: those cover repeat polls within one
/// session, this covers everything from previous ones.
/// </remarks>
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

        // Async because the synchronous overload parks the caller on a Task.Wait until the socket
        // sender dequeues, and most calls here are on the Unity main thread - a connect alone makes
        // seven of them back to back while every service catches up.
        //
        // The faulted continuation is the point: dropping the Task would turn a send failure into
        // an unobserved exception, which trades a frame hitch for a check that silently never
        // lands. Logging off the main thread is fine, as it is everywhere else in this class's
        // callers.
        session.Locations.CompleteLocationChecksAsync(pending).ContinueWith(
            task => Log.LogError($"[AP] Failed to send {pending.Length} check(s): "
                + $"{task.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);

        CheckNotifier.Announce(session, pending);
    }

    internal static void Send(ArchipelagoSession session, long checkId) =>
        Send(session, new[] { checkId });
}
