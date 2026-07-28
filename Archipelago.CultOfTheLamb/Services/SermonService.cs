using System;
using System.Collections.Generic;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Both halves of sermon randomization:
///
///  - Location: filling the sermon bar sends "Sermon Upgrade N" (see SermonUpgradePatch).
///  - Item: a received sermon item calls UpgradeSystem.UnlockAbility for the upgrade it maps to.
///
/// The item -> UpgradeSystem.Type mapping arrives in slot data rather than being hardcoded
/// here, so adding or reordering upgrades on the Python side can't silently desync the two
/// halves. Each entry is an ordered list: one element is a standalone upgrade, several make a
/// progressive chain whose Nth copy grants the Nth tier.
/// </summary>
internal class SermonService : IService
{
    private readonly ArchipelagoSession session;
    private readonly Dictionary<string, List<string>> itemToUpgrades;
    private readonly long locationBaseId;
    private readonly int locationCount;

    /// <summary>How many copies of each sermon item we've already applied, for chain order.</summary>
    private readonly Dictionary<string, int> grantedCounts = new();

    internal SermonService(
        ArchipelagoSession session,
        Dictionary<string, List<string>> itemToUpgrades,
        long locationBaseId,
        int locationCount)
    {
        this.session = session;
        this.itemToUpgrades = itemToUpgrades ?? new Dictionary<string, List<string>>();
        this.locationBaseId = locationBaseId;
        this.locationCount = locationCount;
    }

    public void Register()
    {
        SermonUpgradePatch.OnSermonUpgradeEarned += HandleSermonUpgradeEarned;
        SermonUpgradePatch.Active = true;
        Log.LogInfo($"[AP] Sermon randomization active: {itemToUpgrades.Count} item(s), "
            + $"{locationCount} location(s) from id {locationBaseId}.");
    }

    public void Unregister()
    {
        SermonUpgradePatch.OnSermonUpgradeEarned -= HandleSermonUpgradeEarned;
        // Hand the vanilla pick-an-upgrade flow back, so a disconnected save is playable.
        SermonUpgradePatch.Active = false;
        grantedCounts.Clear();
    }

    private void HandleSermonUpgradeEarned(int level)
    {
        if (level < 1 || level > locationCount)
        {
            // Past the last location the seed has. The vanilla game would be handing out
            // blue hearts by now; we just stop sending. Not an error.
            Log.LogInfo($"[AP] Sermon upgrade #{level} is beyond this seed's "
                + $"{locationCount} sermon location(s) - nothing to send.");
            return;
        }

        var checkId = locationBaseId + (level - 1);
        Log.LogInfo($"[AP] Sermon upgrade #{level}, sending check {checkId}");
        CheckSender.Send(session, checkId);
    }

    /// <summary>
    /// Applies a received sermon item. Returns false if the item isn't a sermon item, so the
    /// caller can go on trying other handlers.
    /// </summary>
    internal bool TryApplyItem(string itemName)
    {
        if (itemName == null || !itemToUpgrades.TryGetValue(itemName, out var upgrades)) return false;

        grantedCounts.TryGetValue(itemName, out var alreadyGranted);
        if (alreadyGranted >= upgrades.Count)
        {
            Log.LogWarning($"[AP] Received more copies of '{itemName}' than it has tiers "
                + $"({upgrades.Count}) - ignoring the extra.");
            return true;
        }

        var internalName = upgrades[alreadyGranted];
        grantedCounts[itemName] = alreadyGranted + 1;

        if (!Enum.IsDefined(typeof(UpgradeSystem.Type), internalName))
        {
            // Slot data naming an upgrade this build of the game doesn't have - most likely a
            // seed generated against a newer apworld than the installed client.
            Log.LogError($"[AP] Slot data names unknown upgrade '{internalName}' for item "
                + $"'{itemName}' - this game build can't grant it.");
            return true;
        }

        var upgrade = (UpgradeSystem.Type)Enum.Parse(typeof(UpgradeSystem.Type), internalName);

        // instant: true plays the game's own unlock-reveal, so an AP grant feels like a
        // normal unlock. Effects apply live - verified in-game.
        var granted = UpgradeSystem.UnlockAbility(upgrade, instant: true);
        Log.LogInfo($"[AP] Sermon item '{itemName}' -> {upgrade} "
            + $"(tier {alreadyGranted + 1}/{upgrades.Count}), UnlockAbility returned {granted}");

        return true;
    }
}
