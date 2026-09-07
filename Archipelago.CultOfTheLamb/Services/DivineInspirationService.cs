using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Makes the Divine Inspiration tree, the buildings-and-rituals tree at the Shrine, an
/// Archipelago system.
///
/// Checks are **sequential**, so the Nth upgrade unlocked in that tree is the Nth check. That's
/// the only shape that works in all three active modes, because in checks_and_techs the player
/// never picks anything and a per-upgrade location would fire for whatever the multiworld handed
/// over rather than for something the player did. It also sidesteps the tree's prerequisites,
/// 11 of the 69 of which sit behind external systems, since the player unlocks things in whatever
/// order the game allows. The count comes from the tree's own <c>NumUnlockedUpgrades()</c> rather
/// than a tally kept here, so it survives reconnects and catches up unlocks made while
/// disconnected.
/// </summary>
internal class DivineInspirationService : IService
{
    internal const int ModeOff = 0;
    internal const int ModeChecksOnly = 1;
    internal const int ModeChecksAndPoints = 2;
    internal const int ModeChecksAndTechs = 3;
    internal const int ModeCurated = 4;

    private readonly ArchipelagoSession session;
    private readonly int mode;
    private readonly long locationBaseId;
    private readonly int locationCount;

    /// <summary>
    /// AP item name -> every upgrade it grants at once.
    ///
    /// One entry per item in checks_and_techs, where each list holds a single upgrade. In
    /// curated_checks the same map carries both the single-upgrade items and the bundles, since
    /// "grant all of these" covers both.
    /// </summary>
    private readonly Dictionary<string, List<UpgradeSystem.Type>> itemNameToUpgrades;

    /// <summary>
    /// curated_checks only. AP item name -> upgrades in tier order, where the Nth copy received
    /// grants the Nth entry rather than all of them.
    /// </summary>
    private readonly Dictionary<string, List<UpgradeSystem.Type>> progressiveUpgrades;

    /// <summary>Upgrades handed over on connect, with neither a check nor an item.</summary>
    private readonly List<UpgradeSystem.Type> freeUpgrades;

    /// <summary>The item that carries one ability point in checks_and_points.</summary>
    private readonly string pointItemName;

    /// <summary>Tree layout axis, independent of <see cref="mode"/>. See DivineInspirationShuffle.</summary>
    private readonly int shuffleMode;
    private readonly int shuffleSeed;

    /// <summary>
    /// Most Devotion one point may cost, for reporting only. EconomyService owns the value, so
    /// this reads it back off the patch rather than keeping a second copy that could disagree.
    /// </summary>
    private static int DevotionCap => DivineInspirationPatch.DevotionCap;

    internal DivineInspirationService(
        ArchipelagoSession session,
        int mode,
        long locationBaseId,
        int locationCount,
        Dictionary<string, List<UpgradeSystem.Type>> itemNameToUpgrades,
        string pointItemName,
        int shuffleMode = 0,
        int shuffleSeed = 0,
        Dictionary<string, List<UpgradeSystem.Type>> progressiveUpgrades = null,
        List<UpgradeSystem.Type> freeUpgrades = null)
    {
        this.session = session;
        this.mode = mode;
        this.locationBaseId = locationBaseId;
        this.locationCount = locationCount;
        this.itemNameToUpgrades =
            itemNameToUpgrades ?? new Dictionary<string, List<UpgradeSystem.Type>>();
        this.pointItemName = pointItemName;
        this.shuffleMode = shuffleMode;
        this.shuffleSeed = shuffleSeed;
        this.progressiveUpgrades =
            progressiveUpgrades ?? new Dictionary<string, List<UpgradeSystem.Type>>();
        this.freeUpgrades = freeUpgrades ?? new List<UpgradeSystem.Type>();
    }

    /// <summary>Per-connection tier counter for the progressive families. See ProgressiveGrant.</summary>
    private readonly ProgressiveGrant progressive = new();

    /// <summary>Applies one copy of a progressive item. The Nth copy grants the Nth tier.</summary>
    private void ApplyProgressive(string itemName, List<UpgradeSystem.Type> tiers)
    {
        if (!progressive.TryTake(itemName, tiers.Count, out var tierIndex)) return;

        // Already-unlocked is the normal case on a replay. UnlockAbility is a set Add, so it
        // no-ops and only the count matters.
        var tier = tiers[tierIndex];
        UpgradeSystem.UnlockAbility(tier);
        Log.LogInfo($"[AP] Divine Inspiration '{itemName}' unlocked {tier} "
            + $"(tier {tierIndex + 1} of {tiers.Count}).");
    }

    public void Register()
    {
        DivineInspirationPatch.PointEarned = OnPointEarned;
        DivineInspirationPatch.ResetWithholdLog();

        // Every granting mode takes the point away. In checks_and_points it comes back as an
        // item, and in the two tech modes it never exists because Archipelago grants the upgrade.
        DivineInspirationPatch.WithholdPoints =
            mode == ModeChecksAndPoints || mode == ModeChecksAndTechs || mode == ModeCurated;

        DivineInspirationShuffle.Apply(shuffleMode, shuffleSeed);

        DivineInspirationTierReveal.Apply(mode);

        GrantFreeUpgrades();

        SendChecksUpTo(EarnedCount());

        Log.LogInfo($"[AP] Divine Inspiration active: mode {ModeName}, {locationCount} "
            + $"location(s) from id {locationBaseId}, {EarnedCount()} point(s) earned so far."
            + (DivineInspirationPatch.WithholdPoints ? " Ability points withheld." : string.Empty)
            + (DevotionCap > 0 ? $" Devotion capped at {DevotionCap}." : string.Empty));
    }

    public void Unregister()
    {
        DivineInspirationPatch.PointEarned = null;
        DivineInspirationPatch.WithholdPoints = false;

        // The shuffle edits a ScriptableObject, which lives for the whole process. Without
        // this, disconnecting would leave the tree rearranged until the game restarts.
        // Queued because teardown arrives on the websocket thread and this touches Unity
        // objects, the same reason TarotService queues its restore.
        MainThreadQueue.Enqueue(DivineInspirationShuffle.Restore);

        DivineInspirationTierReveal.Restore();
    }

    private string ModeName => mode switch
    {
        ModeChecksOnly => "checks_only",
        ModeChecksAndPoints => "checks_and_points",
        ModeChecksAndTechs => "checks_and_techs",
        ModeCurated => "curated_checks",
        _ => "off",
    };

    /// <summary>
    /// Unlocks the upgrades this seed hands over for free, in curated_checks.
    ///
    /// Without them a fresh save can't unlock a bed, a farm plot or the Temple, so the cult can't
    /// function at all, because there is no first move. Idempotent, since UnlockAbility is a set
    /// Add, so reconnecting simply re-asserts them.
    /// </summary>
    private void GrantFreeUpgrades()
    {
        if (freeUpgrades.Count == 0) return;

        // Same guard as RegionUnlockService. At the main menu there is no save to write into.
        // Warned rather than thrown, because the connection itself is still perfectly good.
        if (DataManager.Instance == null)
        {
            Log.LogWarning($"[AP] No save loaded, so the {freeUpgrades.Count} free Divine "
                + "Inspiration upgrade(s) can't be granted yet. Connect at a loaded save.");
            return;
        }

        var granted = 0;
        foreach (var upgrade in freeUpgrades)
        {
            if (UpgradeSystem.UnlockAbility(upgrade)) granted++;
        }

        Log.LogInfo($"[AP] Divine Inspiration: {granted} free upgrade(s) unlocked "
            + $"({freeUpgrades.Count - granted} already held).");
    }

    /// <summary>
    /// How many ability points the player has ever earned from the Devotion meter.
    /// </summary>
    /// <remarks>
    /// <c>DataManager.Level</c> is incremented once per fill in PlayerFarming.GetXP and is never
    /// decremented, because spending points moves <c>AbilityPoints</c> rather than this. That
    /// makes it a monotonic, save-backed count, the same shape as the sermon system's
    /// Doctrine_PlayerUpgrade_Level, so a reconnect or a session played offline catches up
    /// without tracking anything ourselves.
    ///
    /// It counts *meter fills only*. Points the multiworld hands over go straight into
    /// AbilityPoints without touching Level, which is exactly right, since a received point isn't
    /// something the player earned.
    /// </remarks>
    private static int EarnedCount() => DataManager.Instance?.Level ?? 0;

    private void OnPointEarned() => SendChecksUpTo(EarnedCount());

    /// <summary>
    /// Sends every check up to <paramref name="count"/>, not just the newest.
    ///
    /// Deliberately idempotent, since CheckSender drops anything the server already has, so a
    /// missed event, a save edited outside the mod, or fills made while disconnected all
    /// self-correct on the next fill.
    /// </summary>
    private void SendChecksUpTo(int count)
    {
        if (count <= 0) return;

        var capped = Math.Min(count, locationCount);
        var ids = new long[capped];
        for (var i = 0; i < capped; i++) ids[i] = locationBaseId + i;

        CheckSender.Send(session, ids);
    }

    /// <summary>
    /// Applies a Divine Inspiration item. Returns false so the caller can keep looking.
    ///
    /// Both granting modes are idempotent in the way that matters. A tech is a set Add, so
    /// replaying it is a no-op. A *point* is not, because it's a counter, so points are only
    /// granted for genuinely new items, which is why this reports whether it consumed a replay.
    /// </summary>
    internal bool TryApplyItem(string itemName, bool isReplay)
    {
        if (itemName == null) return false;

        var grantsTechs = mode == ModeChecksAndTechs || mode == ModeCurated;

        if (grantsTechs && itemNameToUpgrades.TryGetValue(itemName, out var upgrades))
        {
            // Prerequisites are not enforced by UnlockAbility (a bare Contains-then-Add), so
            // out-of-order grants are safe, which they have to be, since the multiworld hands
            // these over in whatever order it likes. That is also what lets a bundle unlock a
            // building and all of its tiers in one go.
            var unlocked = new List<UpgradeSystem.Type>();
            foreach (var upgrade in upgrades)
            {
                if (UpgradeSystem.UnlockAbility(upgrade)) unlocked.Add(upgrade);
            }

            if (unlocked.Count > 0)
            {
                Log.LogInfo($"[AP] Divine Inspiration '{itemName}' unlocked "
                    + $"{string.Join(", ", unlocked)}.");
            }
            return true;
        }

        if (grantsTechs && progressiveUpgrades.TryGetValue(itemName, out var tiers))
        {
            ApplyProgressive(itemName, tiers);
            return true;
        }

        if (mode == ModeChecksAndPoints && itemName == pointItemName)
        {
            // Re-granting on every connect would stack the counter, so replays are dropped -
            // the points from the original grant are already in the save.
            if (!isReplay)
            {
                DivineInspirationPatch.GrantPoints(1);
                Log.LogInfo("[AP] Divine Inspiration point granted.");
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// AP item name -> the upgrades it grants, from "divineInspirationUpgrades" (one each) merged
    /// with "divineInspirationBundles" (several each).
    ///
    /// Merged into one map because both mean the same thing to the caller, "grant all of these",
    /// and a single-upgrade item is just a bundle of one.
    /// </summary>
    internal static Dictionary<string, List<UpgradeSystem.Type>> ParseUpgrades(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<string, List<UpgradeSystem.Type>>();

        if (slotData.TryGetValue("divineInspirationUpgrades", out var raw)
            && raw is JObject singles)
        {
            foreach (var entry in singles)
            {
                if (SlotData.TryParseEnum<UpgradeSystem.Type>(
                        entry.Value?.ToString(), entry.Key, out var upgrade))
                {
                    result[entry.Key] = new List<UpgradeSystem.Type> { upgrade };
                }
            }
        }

        foreach (var entry in ParseUpgradeLists(slotData, "divineInspirationBundles"))
        {
            result[entry.Key] = entry.Value;
        }

        return result;
    }

    /// <summary>
    /// AP item name -> upgrades in tier order, from "divineInspirationProgressive". Empty outside
    /// curated_checks.
    /// </summary>
    internal static Dictionary<string, List<UpgradeSystem.Type>> ParseProgressive(
        IReadOnlyDictionary<string, object> slotData) =>
        ParseUpgradeLists(slotData, "divineInspirationProgressive");

    /// <summary>Upgrades granted on connect, from "divineInspirationFreeUpgrades".</summary>
    internal static List<UpgradeSystem.Type> ParseFreeUpgrades(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new List<UpgradeSystem.Type>();

        if (!slotData.TryGetValue("divineInspirationFreeUpgrades", out var raw)
            || raw is not JArray names)
        {
            return result;
        }

        foreach (var name in names)
        {
            if (SlotData.TryParseEnum<UpgradeSystem.Type>(
                    name?.ToString(), "free starting set", out var upgrade))
            {
                result.Add(upgrade);
            }
        }

        return result;
    }

    private static Dictionary<string, List<UpgradeSystem.Type>> ParseUpgradeLists(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new Dictionary<string, List<UpgradeSystem.Type>>();

        foreach (var entry in SlotData.ParseNameLists(slotData, key))
        {
            var upgrades = new List<UpgradeSystem.Type>();
            foreach (var name in entry.Value)
            {
                if (SlotData.TryParseEnum<UpgradeSystem.Type>(name, entry.Key, out var upgrade))
                {
                    upgrades.Add(upgrade);
                }
            }

            if (upgrades.Count > 0) result[entry.Key] = upgrades;
        }

        return result;
    }

    /// <summary>What F9 prints for this tree.</summary>
    internal string DescribeState()
    {
        var tree = DivineInspirationPatch.Tree;
        if (tree == null) return $"Divine Inspiration: mode {ModeName}, tree unavailable.";

        var unlocked = tree.AllUpgrades.Where(UpgradeSystem.GetUnlocked).ToList();
        var nextCost = DataManager.GetTargetXP(
            Math.Min(EarnedCount(), DataManager.TargetXP.Count - 1));

        return $"Divine Inspiration: mode {ModeName}, {EarnedCount()} point(s) earned "
            + $"(= checks owed), {unlocked.Count}/{tree.AllUpgrades.Count} unlocked, "
            + $"{UpgradeSystem.AbilityPoints} held"
            + $"{(DivineInspirationPatch.WithholdPoints ? " (awards withheld)" : string.Empty)}."
            + $"\n  Next point costs {nextCost} Devotion"
            + $"{(DevotionCap > 0 ? $" (capped at {DevotionCap})" : " (uncapped)")}, "
            + $"currently {DataManager.Instance?.XP ?? 0}."
            + $"\n  Unlocked: {string.Join(", ", unlocked)}";
    }
}
