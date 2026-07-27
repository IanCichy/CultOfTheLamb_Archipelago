using System.Collections.Generic;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Shows an in-game popup when checks are sent.
///
/// Half of what makes a multiworld feel alive is watching your checks go out, and the game
/// gives no feedback of its own for it - receiving items at least produces the game's own
/// pickup banners, but sending was entirely silent.
///
/// Batches deliberately: milestone catch-up can send a dozen checks in one call (and does, on
/// every reconnect), and a dozen stacked popups would bury the screen. One line naming a
/// single check, or a count when there are several.
/// </summary>
internal static class CheckNotifier
{
    private const string Game = "Cult of the Lamb";

    internal static void Announce(ArchipelagoSession session, IReadOnlyList<long> checkIds)
    {
        if (session == null || checkIds == null || checkIds.Count == 0) return;

        if (checkIds.Count == 1)
        {
            var name = NameFor(session, checkIds[0]);
            ApNotification.Show($"Archipelago: sent {name}", NotificationBase.Flair.Positive);
            return;
        }

        ApNotification.Show($"Archipelago: sent {checkIds.Count} checks",
            NotificationBase.Flair.Positive);
    }

    /// <summary>
    /// Falls back to the raw id rather than throwing: the name lookup needs the datapackage,
    /// and a missing name is not a reason to lose the notification entirely.
    /// </summary>
    private static string NameFor(ArchipelagoSession session, long checkId)
    {
        try
        {
            var name = session.Locations.GetLocationNameFromId(checkId, Game);
            return string.IsNullOrEmpty(name) ? $"check {checkId}" : name;
        }
        catch
        {
            return $"check {checkId}";
        }
    }
}
