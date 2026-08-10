using System.Collections.Generic;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Shows an in-game popup when checks are sent.
///
/// Half of what makes a multiworld feel alive is watching your checks go out, and the game gives
/// no feedback of its own for it - receiving items at least produces the game's own pickup
/// banners, but sending was entirely silent.
///
/// The popup leads with the **item and who gets it**, not the location. The location is the
/// thing the player just did and already knows about; the item is the part they can't see.
/// It still gets a second line, since "which check was that" is a fair question.
///
/// Batches deliberately: milestone catch-up can send a dozen checks in one call (and does, on
/// every reconnect), and a dozen stacked popups would bury the screen.
/// </summary>
internal static class CheckNotifier
{
    private const string Game = "Cult of the Lamb";

    /// <summary>
    /// What the multiworld put at each location. Set by the client on connect and nulled on
    /// teardown; when it's null or hasn't landed yet, the popups name the location instead.
    /// </summary>
    internal static ScoutCache Scouts;

    internal static void Announce(ArchipelagoSession session, IReadOnlyList<long> checkIds)
    {
        if (session == null || checkIds == null || checkIds.Count == 0) return;

        if (checkIds.Count == 1)
        {
            // Blue glow = outgoing. Direction is the thing worth reading from the corner of the
            // eye mid-crusade; the wording carries everything else.
            ApNotification.Show(
                Describe(session, checkIds[0]), NotificationBase.Flair.Positive, ApColors.Blue);
            return;
        }

        // Yellow for a batch, matching the catch-up/warning slot in the palette - a burst of
        // checks is almost always a reconnect catching up rather than something you just did.
        ApNotification.Show($"Sent {checkIds.Count} checks to the multiworld",
            NotificationBase.Flair.Positive, ApColors.Yellow);
    }

    /// <summary>
    /// One check, as two lines: what went where, then which check it was.
    ///
    /// Falls back to the location alone whenever the scout hasn't answered - there's a round
    /// trip between connecting and the cache filling, and a check completed in that window still
    /// deserves a popup.
    /// </summary>
    private static string Describe(ArchipelagoSession session, long checkId)
    {
        var location = LocationName(session, checkId);

        if (Scouts == null || !Scouts.TryGet(checkId, out var scouted))
        {
            return $"Sent {location}";
        }

        var headline = scouted.ForLocalPlayer
            ? $"Sent yourself {scouted.ItemName}"
            : $"Sent {scouted.ItemName} to "
                + ApColors.Tint(scouted.PlayerName, ApColors.YellowHex);

        return $"{headline}\n{location}";
    }

    /// <summary>
    /// Falls back to the raw id rather than throwing: the name lookup needs the datapackage,
    /// and a missing name is not a reason to lose the notification entirely.
    /// </summary>
    private static string LocationName(ArchipelagoSession session, long checkId)
    {
        if (Scouts != null) return Scouts.LocationName(checkId);

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
