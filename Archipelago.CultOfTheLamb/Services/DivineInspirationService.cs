using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.CultOfTheLamb.Patches;
using Archipelago.MultiClient.Net;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Makes the Divine Inspiration tree - the buildings-and-rituals tree at the Shrine - an
/// Archipelago system.
///
/// Checks are **sequential**: the Nth upgrade unlocked in that tree is the Nth check. That's the
/// only shape that works in all three active modes, because in checks_and_techs the player never
/// picks anything and a per-upgrade location would fire for whatever the multiworld handed over
/// rather than for something the player did. It also sidesteps the tree's prerequisites - 11 of
/// the 69 sit behind external systems - since the player just unlocks things in whatever order
/// the game allows.
///
/// The count comes from the tree's own <c>NumUnlockedUpgrades()</c> rather than a tally kept
/// here, so it survives reconnects and catches up unlocks made while disconnected.
/// </summary>
internal class DivineInspirationService : IService
{
    internal const int ModeOff = 0;
    internal const int ModeChecksOnly = 1;
    internal const int ModeChecksAndPoints = 2;
    internal const int ModeChecksAndTechs = 3;

    private readonly ArchipelagoSession session;
    private readonly int mode;
    private readonly long locationBaseId;
    private readonly int locationCount;

    /// <summary>AP item name -> upgrade, for checks_and_techs.</summary>
    private readonly Dictionary<string, UpgradeSystem.Type> itemNameToUpgrade;

    /// <summary>The item that carries one ability point in checks_and_points.</summary>
    private readonly string pointItemName;

    /// <summary>Tree layout axis, independent of <see cref="mode"/>. See DivineInspirationShuffle.</summary>
    private readonly int shuffleMode;
    private readonly int shuffleSeed;

    /// <summary>Most Devotion one point may cost. 0 leaves the game's curve alone.</summary>
    private readonly int devotionCap;

    internal DivineInspirationService(
        ArchipelagoSession session,
        int mode,
        long locationBaseId,
        int locationCount,
        Dictionary<string, UpgradeSystem.Type> itemNameToUpgrade,
        string pointItemName,
        int shuffleMode = 0,
        int shuffleSeed = 0,
        int devotionCap = 0)
    {
        this.devotionCap = devotionCap;
        this.session = session;
        this.mode = mode;
        this.locationBaseId = locationBaseId;
        this.locationCount = locationCount;
        this.itemNameToUpgrade = itemNameToUpgrade ?? new Dictionary<string, UpgradeSystem.Type>();
        this.pointItemName = pointItemName;
        this.shuffleMode = shuffleMode;
        this.shuffleSeed = shuffleSeed;
    }

    public void Register()
    {
        DivineInspirationPatch.PointEarned = OnPointEarned;
        DivineInspirationPatch.ResetWithholdLog();
        DivineInspirationPatch.DevotionCap = devotionCap;

        // Both granting modes take the point away: in checks_and_points it comes back as an
        // item, in checks_and_techs it never exists because Archipelago grants the upgrade.
        DivineInspirationPatch.WithholdPoints =
            mode == ModeChecksAndPoints || mode == ModeChecksAndTechs;

        DivineInspirationShuffle.Apply(shuffleMode, shuffleSeed);

        // Catch up meter fills from before this connect, or from while disconnected.
        SendChecksUpTo(EarnedCount());

        Log.LogInfo($"[AP] Divine Inspiration active: mode {ModeName}, {locationCount} "
            + $"location(s) from id {locationBaseId}, {EarnedCount()} point(s) earned so far."
            + (DivineInspirationPatch.WithholdPoints ? " Ability points withheld." : string.Empty)
            + (devotionCap > 0 ? $" Devotion capped at {devotionCap}." : string.Empty));
    }

    public void Unregister()
    {
        DivineInspirationPatch.PointEarned = null;
        DivineInspirationPatch.WithholdPoints = false;
        DivineInspirationPatch.DevotionCap = 0;

        // The shuffle edits a ScriptableObject, which lives for the whole process - without
        // this, disconnecting would leave the tree rearranged until the game restarts.
        // Queued because teardown arrives on the websocket thread and this touches Unity
        // objects, the same reason TarotService queues its restore.
        MainThreadQueue.Enqueue(DivineInspirationShuffle.Restore);
    }

    private string ModeName => mode switch
    {
        ModeChecksOnly => "checks_only",
        ModeChecksAndPoints => "checks_and_points",
        ModeChecksAndTechs => "checks_and_techs",
        _ => "off",
    };

    /// <summary>
    /// How many ability points the player has ever earned from the Devotion meter.
    ///
    /// <c>DataManager.Level</c> is incremented once per fill in PlayerFarming.GetXP and is never
    /// decremented - spending points moves <c>AbilityPoints</c>, not this. That makes it a
    /// monotonic, save-backed count, the same shape as the sermon system's
    /// Doctrine_PlayerUpgrade_Level, so a reconnect or a session played offline catches up
    /// without tracking anything ourselves.
    ///
    /// It counts *meter fills only*. Points the multiworld hands over go straight into
    /// AbilityPoints without touching Level, which is exactly right - a received point isn't
    /// something the player earned.
    /// </summary>
    private static int EarnedCount() => DataManager.Instance?.Level ?? 0;

    private void OnPointEarned() => SendChecksUpTo(EarnedCount());

    /// <summary>
    /// Sends every check up to <paramref name="count"/>, not just the newest.
    ///
    /// Deliberately idempotent - CheckSender drops anything the server already has - so a
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
    /// replaying it is a no-op. A *point* is not - it's a counter - so points are only granted
    /// for genuinely new items, which is why this reports whether it consumed a replay.
    /// </summary>
    internal bool TryApplyItem(string itemName, bool isReplay)
    {
        if (itemName == null) return false;

        if (mode == ModeChecksAndTechs && itemNameToUpgrade.TryGetValue(itemName, out var upgrade))
        {
            // Prerequisites are not enforced by UnlockAbility (a bare Contains-then-Add), so
            // out-of-order grants are safe - which they have to be, since the multiworld hands
            // these over in whatever order it likes.
            if (UpgradeSystem.UnlockAbility(upgrade))
            {
                Log.LogInfo($"[AP] Divine Inspiration '{itemName}' ({upgrade}) unlocked.");
            }
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

    /// <summary>AP item name -> upgrade, from "divineInspirationUpgrades".</summary>
    internal static Dictionary<string, UpgradeSystem.Type> ParseUpgrades(
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new Dictionary<string, UpgradeSystem.Type>();

        if (!slotData.TryGetValue("divineInspirationUpgrades", out var raw)
            || raw is not JObject mapping)
        {
            return result;
        }

        foreach (var entry in mapping)
        {
            var internalName = entry.Value?.ToString();
            if (string.IsNullOrEmpty(internalName)) continue;

            // Dropped with a warning rather than thrown: a name this build doesn't have means
            // the mod and the game disagree, and losing one upgrade beats losing the session.
            if (!Enum.IsDefined(typeof(UpgradeSystem.Type), internalName))
            {
                Log.LogWarning("[AP] Slot data names an upgrade this game doesn't have: "
                    + $"'{internalName}' - skipping '{entry.Key}'.");
                continue;
            }

            result[entry.Key] =
                (UpgradeSystem.Type)Enum.Parse(typeof(UpgradeSystem.Type), internalName);
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
            + $"{(devotionCap > 0 ? $" (capped at {devotionCap})" : " (uncapped)")}, "
            + $"currently {DataManager.Instance?.XP ?? 0}."
            + $"\n  Unlocked: {string.Join(", ", unlocked)}";
    }
}
