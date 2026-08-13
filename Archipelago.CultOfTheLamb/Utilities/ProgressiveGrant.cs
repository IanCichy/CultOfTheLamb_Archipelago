using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Tracks which tier of a progressive item the next copy should grant.
///
/// A progressive item arrives several times and grants a different thing each time - the Nth copy
/// gives the Nth tier. That needs a count, and the count has to be right in the face of replays.
///
/// **Counted, not read off the game.** Deciding from game state instead - "grant the first tier
/// not already held" - looks more idempotent and is actively wrong: every item replays on
/// reconnect, so a held copy would skip past its own tier and grant the next, turning N copies
/// into 2N tiers.
///
/// The count is safe precisely because an instance of this lives on a service that is rebuilt on
/// every connect. A replay refills it from zero and arrives at the same answer instead of adding
/// to it, so **this must never be static or persisted**.
/// </summary>
internal class ProgressiveGrant
{
    private readonly Dictionary<string, int> granted = new();

    /// <summary>
    /// Claims the next tier for <paramref name="itemName"/>.
    ///
    /// Returns false, having warned, when more copies arrive than the family has tiers - which
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

    /// <summary>Forgets everything, for a service that outlives one connection.</summary>
    internal void Clear() => granted.Clear();
}
