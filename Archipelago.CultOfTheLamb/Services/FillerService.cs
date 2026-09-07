using System.Collections.Generic;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Applies filler and trap items. These are roughly half of a seed's items, so "received an
/// item and nothing happened" is the most common way the mod can feel broken. Every entry in
/// items.py's filler pool needs a real effect here.
///
/// Item names must match worlds/cult_of_the_lamb/items.py exactly. Matching by name rather
/// than id keeps this readable and survives id changes, at the cost of breaking silently if a
/// name is edited on one side only. That is why anything unmatched is logged loudly rather than
/// ignored.
/// </summary>
internal static class FillerService
{
    // Resource bundles, keyed by AP item name, each granting several stacks at once. Themed
    // mixes rather than one resource each, since filler is about half of a seed and a small
    // pile of something you already hold stops reading as a reward. Quantities are generous:
    // the per-stack cap is 9999 (Inventory.cs:266) and BLACK_GOLD is exempt from it
    private static readonly Dictionary<string, (InventoryItem.ITEM_TYPE Type, int Quantity)[]> Bundles = new()
    {
        ["Construction Bundle"] = new[]
        {
            (InventoryItem.ITEM_TYPE.LOG, 50),
            (InventoryItem.ITEM_TYPE.STONE, 50),
        },
        ["Larder Bundle"] = new[]
        {
            (InventoryItem.ITEM_TYPE.BERRY, 30),
            (InventoryItem.ITEM_TYPE.BEETROOT, 20),
            (InventoryItem.ITEM_TYPE.CAULIFLOWER, 20),
            (InventoryItem.ITEM_TYPE.PUMPKIN, 20),
        },
        ["Ritual Bundle"] = new[]
        {
            (InventoryItem.ITEM_TYPE.BONE, 40),
            (InventoryItem.ITEM_TYPE.GOLD_NUGGET, 30),
        },
        ["Artisan Bundle"] = new[]
        {
            (InventoryItem.ITEM_TYPE.LOG_REFINED, 25),
            (InventoryItem.ITEM_TYPE.STONE_REFINED, 25),
        },
        ["Treasury Bundle"] = new[]
        {
            (InventoryItem.ITEM_TYPE.BLACK_GOLD, 300),
            (InventoryItem.ITEM_TYPE.GOLD_REFINED, 10),
        },
    };

    // Faith removed by a Dissent Trap
    private const float DissentTrapFaith = -5f;

    // Levels granted per Follower Level Up item. See ApplyFollowerLevelUp
    private const int FollowerLevelsPerItem = 3;

    // Returns false if the name isn't a filler or trap item, so the caller can keep looking
    internal static bool TryApplyItem(string itemName)
    {
        if (itemName == null)
        {
            return false;
        }

        if (Bundles.TryGetValue(itemName, out var bundle))
        {
            var granted = new List<string>();
            foreach (var (type, quantity) in bundle)
            {
                // forceNormalInventory is true because Inventory.AddItem otherwise routes into
                // the *dungeon* inventory whenever BiomeGenerator.Instance exists
                // (Inventory.cs:251), which would silently lose the items when the crusade ends.
                Inventory.AddItem(type, quantity, forceNormalInventory: true);
                granted.Add($"+{quantity} {type}");
            }

            Log.LogInfo($"[AP] Filler '{itemName}': {string.Join(", ", granted)}");
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

    // Three levels. A Follower contributes sermon points equal to their level, capped at 10
    // (FollowerInfo.cs:653), so this compounds into faster sermons. Re-picks between each
    // level, targeting the highest Follower still under the cap, so it spreads across the
    // flock once the current best tops out
    private static void ApplyFollowerLevelUp()
    {
        var followers = DataManager.Instance?.Followers;
        if (followers == null || followers.Count == 0)
        {
            Log.LogInfo("[AP] Filler 'Follower Level Up': no Followers yet - nothing to level.");
            return;
        }

        var levelled = new List<string>();
        FollowerInfo last = null;

        for (var i = 0; i < FollowerLevelsPerItem; i++)
        {
            FollowerInfo best = null;
            foreach (var follower in followers)
            {
                if (follower == null || follower.XPLevel >= MaxUsefulFollowerLevel)
                {
                    continue;
                }

                if (best == null || follower.XPLevel > best.XPLevel)
                {
                    best = follower;
                }
            }

            if (best == null)
            {
                break;
            }

            best.XPLevel++;
            levelled.Add($"{best.Name} -> {best.XPLevel}");
            last = best;
        }

        if (levelled.Count == 0)
        {
            Log.LogInfo("[AP] Filler 'Follower Level Up': every Follower is already at the "
                + $"level cap ({MaxUsefulFollowerLevel}).");
            return;
        }

        Log.LogInfo($"[AP] Filler 'Follower Level Up': {string.Join(", ", levelled)}.");

        // Names someone rather than just counting. "Aya reached level 5" is the part that lands,
        // and a bare count reads like a receipt. The log above still has every pick.
        ApNotification.Show(
            levelled.Count == 1
                ? $"Archipelago: {last.Name} reached level {last.XPLevel}"
                : $"Archipelago: {levelled.Count} Follower levels - {last.Name} reached level {last.XPLevel}",
            NotificationBase.Flair.Positive);
    }

    // Sermon-point contribution is Mathf.Clamp(XPLevel, 1, 10), so levels past 10 are dead
    // weight
    private const int MaxUsefulFollowerLevel = 10;

    // Drains cult faith. GetFaith is the single choke point for all faith change
    // (CultFaithManager.cs:224) and handles the clamping and the notification itself
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
