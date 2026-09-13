using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// What the multiworld put at each of this slot's locations, so the client can name the item
/// instead of just the check
/// </summary>
/// <remarks>
/// One scout for everything on connect. Scouting per check would make the popup async for
/// something wanted in the same frame. No hints are created, since spending the player's hint
/// points on labels would be rude.
///
/// Callers have to handle a miss. A check done before the scout comes back just shows the
/// location name.
/// </remarks>
internal class ScoutCache
{
    private readonly ArchipelagoSession session;
    private readonly string game;

    // Written from the scout continuation (a background thread) and read from UI and check
    // paths (the main thread), so it's swapped wholesale rather than mutated in place. A
    // reference assignment is atomic, an Add is not.
    private Dictionary<long, ScoutedCheck> scouted = new();

    internal ScoutCache(ArchipelagoSession session, string game)
    {
        this.session = session;
        this.game = game;
    }

    /// <summary>What the multiworld put at one location.</summary>
    internal class ScoutedCheck
    {
        internal string ItemName;
        internal string PlayerName;
        internal string Game;
        internal bool ForLocalPlayer;
    }

    internal bool TryGet(long locationId, out ScoutedCheck check) =>
        scouted.TryGetValue(locationId, out check);

    /// <summary>
    /// Scouts everything this slot still has outstanding.
    ///
    /// Already-checked locations are skipped. Their items are gone, so naming them would cost a
    /// bigger round trip for data nothing reads.
    /// </summary>
    internal void ScoutAll()
    {
        long[] ids;
        try
        {
            ids = session.Locations.AllMissingLocations?.ToArray() ?? Array.Empty<long>();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't list locations to scout: {e.Message}");
            return;
        }

        Scout(ids);
    }

    // Scouts a specific set, merging the result over anything already known
    internal void Scout(IReadOnlyCollection<long> locationIds)
    {
        if (locationIds == null || locationIds.Count == 0)
        {
            return;
        }

        var ids = locationIds.Distinct().ToArray();

        session.Locations.ScoutLocationsAsync(ids).ContinueWith(task =>
        {
            if (task.IsFaulted || task.Result == null)
            {
                Log.LogWarning("[AP] Could not scout locations - notifications and shop panels "
                    + "will name the check instead of the item: "
                    + task.Exception?.GetBaseException().Message);
                return;
            }

            // Built off the current map so a later partial scout doesn't drop earlier results.
            var merged = new Dictionary<long, ScoutedCheck>(scouted);

            foreach (var entry in task.Result)
            {
                var item = entry.Value;
                merged[entry.Key] = new ScoutedCheck
                {
                    ItemName = item.ItemName,
                    // Alias falls back to the slot name, so this is never empty.
                    PlayerName = item.Player.Alias,
                    Game = item.ItemGame,
                    ForLocalPlayer = item.Player.Slot == session.ConnectionInfo.Slot,
                };
            }

            scouted = merged;
            Log.LogInfo($"[AP] Scouted {merged.Count} location(s) for item names.");
        });
    }

    /// <summary>
    /// The location's own name. Falls back to the raw id rather than throwing, since the lookup
    /// needs the datapackage and a missing name is no reason to lose a notification.
    /// </summary>
    internal string LocationName(long locationId)
    {
        try
        {
            var name = session.Locations.GetLocationNameFromId(locationId, game);
            return string.IsNullOrEmpty(name) ? $"check {locationId}" : name;
        }
        catch
        {
            return $"check {locationId}";
        }
    }
}
