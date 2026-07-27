using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Applies filler and trap items. These are roughly half of a seed's items, so "received an
/// item and nothing happened" is the most common way the mod can feel broken - every entry in
/// items.py's filler pool needs a real effect here.
///
/// Item names must match worlds/cult_of_the_lamb/items.py exactly. Matching by name rather
/// than id keeps this readable and survives id changes, at the cost of breaking silently if a
/// name is edited on one side only - so anything unmatched is logged loudly rather than
/// ignored.
/// </summary>
internal static class FillerService
{
    /// <summary>Resource grants, keyed by AP item name.</summary>
    private static readonly Dictionary<string, (InventoryItem.ITEM_TYPE Type, int Quantity)> ResourceGrants = new()
    {
        { "Bundle of Lumber", (InventoryItem.ITEM_TYPE.LOG, 15) },
        { "Pile of Stone", (InventoryItem.ITEM_TYPE.STONE, 15) },
        { "Basket of Berries", (InventoryItem.ITEM_TYPE.BERRY, 10) },
        { "Bag of Bones", (InventoryItem.ITEM_TYPE.BONE, 12) },
        { "Gold Tithe", (InventoryItem.ITEM_TYPE.GOLD_NUGGET, 5) },
        { "Fervour", (InventoryItem.ITEM_TYPE.BLACK_GOLD, 10) },
    };

    /// <summary>Faith removed by a Dissent Trap.</summary>
    private const float DissentTrapFaith = -5f;

    /// <summary>
    /// Returns false if the name isn't a filler/trap item, so the caller can keep looking.
    /// </summary>
    internal static bool TryApplyItem(string itemName)
    {
        if (itemName == null) return false;

        if (ResourceGrants.TryGetValue(itemName, out var grant))
        {
            // forceNormalInventory: true because Inventory.AddItem otherwise routes into the
            // *dungeon* inventory whenever BiomeGenerator.Instance exists (Inventory.cs:251),
            // which would silently lose the items when the crusade ends.
            Inventory.AddItem(grant.Type, grant.Quantity, forceNormalInventory: true);
            Log.LogInfo($"[AP] Filler '{itemName}': +{grant.Quantity} {grant.Type}");
            return true;
        }

        switch (itemName)
        {
            case "Follower Level Up":
                ApplyFollowerLevelUp();
                return true;

            case "Dissent Trap":
                ApplyDissentTrap();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Levels up one Follower. Each Follower contributes sermon points equal to their level
    /// (capped at 10 - FollowerInfo.cs:653), so this compounds into faster sermons, which is
    /// what makes it worth more than a resource drop.
    ///
    /// Targets the highest-level Follower still under the cap rather than the lowest: the
    /// point is to accelerate sermons, and levels above 10 contribute nothing.
    /// </summary>
    private static void ApplyFollowerLevelUp()
    {
        var followers = DataManager.Instance?.Followers;
        if (followers == null || followers.Count == 0)
        {
            Log.LogInfo("[AP] Filler 'Follower Level Up': no Followers yet - nothing to level.");
            return;
        }

        FollowerInfo best = null;
        foreach (var follower in followers)
        {
            if (follower == null || follower.XPLevel >= MaxUsefulFollowerLevel) continue;
            if (best == null || follower.XPLevel > best.XPLevel) best = follower;
        }

        if (best == null)
        {
            Log.LogInfo("[AP] Filler 'Follower Level Up': every Follower is already at the "
                + $"level cap ({MaxUsefulFollowerLevel}).");
            return;
        }

        best.XPLevel++;
        Log.LogInfo($"[AP] Filler 'Follower Level Up': {best.Name} is now level {best.XPLevel}.");
        ApNotification.Show($"Archipelago: {best.Name} reached level {best.XPLevel}",
            NotificationBase.Flair.Positive);
    }

    /// <summary>
    /// Sermon-point contribution is Mathf.Clamp(XPLevel, 1, 10), so levels past 10 are dead
    /// weight for the thing this item exists to accelerate.
    /// </summary>
    private const int MaxUsefulFollowerLevel = 10;

    /// <summary>
    /// Drains cult faith. GetFaith is the single choke point for all faith change
    /// (CultFaithManager.cs:224) and handles the clamping and the notification itself, so
    /// going through it keeps the trap consistent with every other faith source.
    /// </summary>
    private static void ApplyDissentTrap()
    {
        if (CultFaithManager.Instance == null && DataManager.Instance == null)
        {
            Log.LogInfo("[AP] Trap 'Dissent Trap': cult not loaded - skipping.");
            return;
        }

        CultFaithManager.GetFaith(DissentTrapFaith, DissentTrapFaith, true,
            NotificationBase.Flair.Negative);
        Log.LogInfo($"[AP] Trap 'Dissent Trap': {DissentTrapFaith} faith.");
    }
}
