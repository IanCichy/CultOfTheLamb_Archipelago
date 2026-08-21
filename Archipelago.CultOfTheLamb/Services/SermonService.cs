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

    /// <summary>Which tier of each progressive sermon chain comes next. See ProgressiveGrant.</summary>
    private readonly ProgressiveGrant progressive = new();

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

        SendChecksUpTo(EarnedCount());
    }

    /// <summary>
    /// How many sermon upgrades this save has taken, ever. Monotonic, and vanilla keeps counting
    /// while we're disconnected - Unregister hands the pick-an-upgrade flow back, and
    /// SermonController increments this on its own.
    /// </summary>
    private static int EarnedCount() => DataManager.Instance?.Doctrine_PlayerUpgrade_Level ?? 0;

    /// <summary>
    /// Pays for every sermon the save says was taken, so ones earned while disconnected still
    /// land. Same shape as DivineInspirationService, and free to repeat because CheckSender
    /// filters against what the server already has.
    ///
    /// One knock-on worth knowing: a sermon taken offline also hands out a real vanilla upgrade,
    /// since the choice screen isn't suppressed once Unregister runs. Catch-up pays the check on
    /// top, so the player keeps that upgrade for free. A leaked reward beats a lost check, and the
    /// alternative - withholding the check - would strand an item for someone else.
    /// </summary>
    private void SendChecksUpTo(int level)
    {
        if (level < 1) return;

        var highest = System.Math.Min(level, locationCount);
        var pending = new List<long>();
        for (var i = 1; i <= highest; i++) pending.Add(locationBaseId + (i - 1));

        Log.LogInfo($"[AP] Save records {level} sermon upgrade(s) taken - sending any of the "
            + $"first {highest} check(s) that haven't landed yet.");
        CheckSender.Send(session, pending);
    }

    public void Unregister()
    {
        SermonUpgradePatch.OnSermonUpgradeEarned -= HandleSermonUpgradeEarned;
        // Hand the vanilla pick-an-upgrade flow back, so a disconnected save is playable.
        SermonUpgradePatch.Active = false;
        progressive.Clear();
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

        if (!progressive.TryTake(itemName, upgrades.Count, out var tierIndex)) return true;

        if (!SlotData.TryParseEnum<UpgradeSystem.Type>(
                upgrades[tierIndex], itemName, out var upgrade))
        {
            return true;
        }

        // instant: true plays the game's own unlock-reveal, so an AP grant feels like a
        // normal unlock. Effects apply live - verified in-game.
        var granted = UpgradeSystem.UnlockAbility(upgrade, instant: true);
        Log.LogInfo($"[AP] Sermon item '{itemName}' -> {upgrade} "
            + $"(tier {tierIndex + 1}/{upgrades.Count}), UnlockAbility returned {granted}");

        return true;
    }
}
