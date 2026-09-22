using System.Collections.Generic;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Shows an in-game popup when checks are sent.
/// </summary>
/// <remarks>
/// The game gives no feedback of its own for an outgoing check. Receiving items at least
/// produces the game's own pickup banners, but sending was entirely silent.
///
/// The popup leads with the item and who gets it, not the location: the location is what the
/// player just did and already knows. The location still gets a second line.
///
/// Batched, because milestone catch-up can send a dozen checks in one call, and does on every
/// reconnect.
/// </remarks>
internal static class CheckNotifier
{
    private const string Game = "Cult of the Lamb";

    // What the multiworld put at each location. Null before it lands, and the popups then name
    // the location instead
    internal static ScoutCache Scouts;

    internal static void Announce(ArchipelagoSession session, IReadOnlyList<long> checkIds)
    {
        if (session == null || checkIds == null || checkIds.Count == 0)
        {
            return;
        }

        if (checkIds.Count == 1)
        {
            // A blue glow means outgoing. Direction is the thing worth reading from the corner
            // of the eye mid-crusade, and the wording carries everything else.
            ApNotification.Show(
                Describe(session, checkIds[0]), NotificationBase.Flair.Positive, ApColors.Blue);
            return;
        }

        // Yellow for a batch, matching the catch-up and warning slot in the palette. A burst of
        // checks is almost always a reconnect catching up rather than something you just did.
        ApNotification.Show($"Sent {checkIds.Count} checks",
            NotificationBase.Flair.Positive, ApColors.Yellow);
    }

    // One check, as two lines: what went where, then which check it was. Falls back to the
    // location alone whenever the scout hasn't answered yet
    private static string Describe(ArchipelagoSession session, long checkId)
    {
        var location = LocationName(session, checkId);

        if (Scouts == null || !Scouts.TryGet(checkId, out var scouted))
        {
            return $"Checked {location}";
        }

        var headline = scouted.ForLocalPlayer
            ? $"Sent yourself {scouted.ItemName}"
            : $"Sent {scouted.ItemName} to "
                + ApColors.Tint(scouted.PlayerName, ApColors.YellowHex);

        return $"{headline}\n{location}";
    }

    // The lookup needs the datapackage, so it can fail. Says so in words rather than throwing
    private static string LocationName(ArchipelagoSession session, long checkId)
    {
        if (Scouts != null)
        {
            return Scouts.LocationName(checkId);
        }

        try
        {
            var name = session.Locations.GetLocationNameFromId(checkId, Game);
            return string.IsNullOrEmpty(name) ? "an Archipelago check" : name;
        }
        catch
        {
            return "an Archipelago check";
        }
    }
}
