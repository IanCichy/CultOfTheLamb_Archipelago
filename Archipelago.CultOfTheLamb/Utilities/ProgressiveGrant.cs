using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Tracks which tier of a progressive item the next copy should grant
/// </summary>
/// <remarks>
/// A progressive item arrives several times, and the Nth copy gives the Nth tier.
///
/// It counts copies instead of checking which tiers you hold. Every item replays on reconnect, so
/// "give the first tier not held" would skip ahead and turn N copies into 2N tiers.
///
/// The count is safe because it lives on a service rebuilt every connect, so a replay starts from
/// zero. Never make it static or save it.
/// </remarks>
internal class ProgressiveGrant
{
    private readonly Dictionary<string, int> granted = new();

    // Claims the next tier for itemName.
    //
    // Returns false, having warned, when more copies arrive than the family has tiers, which
    // means the pool and the tier table disagree, not that the player did anything wrong
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
