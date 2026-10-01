using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Plays the unlock-reveal for upgrades Archipelago granted, exactly once each.
/// </summary>
/// <remarks>
/// The reveal is also where the game applies an upgrade's effect, such as an extra heart. The
/// game's own routine replays its whole pending list each time it starts, so granting several
/// upgrades at once applied the early ones repeatedly.
/// </remarks>
internal static class UpgradeReveal
{
    private static readonly List<UpgradeSystem.Type> Claimed = new();

    /// <summary>
    /// Takes over the reveal for an upgrade we just granted.
    /// </summary>
    /// <remarks>
    /// Removed from the game's pending list so its routine can't play it a second time the next
    /// time something else starts it.
    /// </remarks>
    internal static void Claim(UpgradeSystem.Type upgrade)
    {
        UpgradeSystem.UnlocksToReveal?.Remove(upgrade);

        if (!Claimed.Contains(upgrade))
        {
            Claimed.Add(upgrade);
        }
    }

    /// <summary>
    /// Reveals one claimed upgrade per call. ProcessQueue calls it every frame.
    /// </summary>
    internal static void FlushClaimed()
    {
        if (Claimed.Count == 0)
        {
            return;
        }

        var manager = GameManager.GetInstance();

        // The same lookup the Heart tiers make unguarded (UpgradeSystem.cs:918), so it has to be
        // this and not PlayerFarming.Instance, which survives the player object being deactivated.
        // We dequeue before starting the coroutine, so a throw there loses the upgrade outright.
        if (manager == null || UnityEngine.Object.FindObjectOfType<HealthPlayer>() == null)
        {
            return;
        }

        var upgrade = Claimed[0];
        Claimed.RemoveAt(0);
        manager.StartCoroutine(UpgradeSystem.OnUnlockAbility(upgrade));
    }
}
