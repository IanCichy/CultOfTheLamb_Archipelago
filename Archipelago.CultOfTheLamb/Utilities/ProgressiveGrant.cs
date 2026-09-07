using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Tracks which tier of a progressive item the next copy should grant.
/// </summary>
/// <remarks>
/// A progressive item arrives several times and grants a different thing each time. The Nth copy
/// gives the Nth tier, so it needs a count that survives replays.
///
/// Counted, not read off the game. "Grant the first tier not already held" looks more idempotent
/// and is wrong: every item replays on reconnect, so a held copy skips past its own tier and
/// grants the next, turning N copies into 2N tiers.
///
/// The count is safe because an instance lives on a service rebuilt on every connect, so a
/// replay refills it from zero rather than adding to it. This must never be static or persisted.
/// </remarks>
internal class ProgressiveGrant
{
    private readonly Dictionary<string, int> granted = new();

    /// <summary>
    /// Claims the next tier for <paramref name="itemName"/>.
    ///
    /// Returns false, having warned, when more copies arrive than the family has tiers, which
    /// means the pool and the tier table disagree, not that the player did anything wrong.
    /// </summary>
    internal bool TryTake(string itemName, int tierCount, out int tierIndex)
    {
        granted.TryGetValue(itemName, out tierIndex);

        if (tierIndex >= tierCount)
        {
            Log.LogWarning($"[AP] Received more copies of '{itemName}' than it has tiers "
                + $"({tierCount}) - ignoring the extra.");
            return false;
        }

        granted[itemName] = tierIndex + 1;
        return true;
    }

    // Forgets everything, for a service that outlives one connection
    internal void Clear()
    {
        granted.Clear();
    }
}
