using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Archipelago.MultiClient.Net;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>How much of the guide is pinned to the on-screen tracker. Matches
/// worlds/cult_of_the_lamb/options.py ObjectiveGuidePinning.</summary>
internal enum GuideHudMode
{
    Off = 0,
    GoalOnly = 1,
    Everything = 2,
}

/// <summary>
/// One line of the Archipelago checklist. What it says, and how to work out its progress.
///
/// Pure data plus two funcs. All the ObjectiveManager and I2 mechanics live on the service, so
/// adding a line is only ever a matter of writing a probe and a sentence.
/// </summary>
internal sealed class QuestGuideEntry
{
    // Objectives.CustomQuestTypes value, cast from an int well past the enum's 148 real
    // members. Stable per entry forever, because it is the identity written into the player's
    // save and the tail of the I2 term key
    internal Objectives.CustomQuestTypes QuestType { get; }

    // The term Objectives_Custom.Text will look up. Derived rather than passed in, because it
    // has to match exactly what the game computes, $"Objectives/Custom/{CustomQuestType}",
    // where an unnamed enum value ToString()s to its number. Otherwise the line renders blank
    internal string TermKey => "Objectives/Custom/" + (int)QuestType;

    // Which of the two groups this line belongs to (pinned, or log-only)
    internal string GroupId { get; }

    // Short name for logs. Never shown to the player
    internal string DebugName { get; }

    // Re-derived on every refresh. Current may exceed Target, and the service clamps
    internal Func<(int Current, int Target)> Progress { get; }

    // (current, target) -> the line the player reads. Must contain no braces
    internal Func<int, int, string> BuildText { get; }

    internal QuestGuideEntry(
        int questTypeId,
        string groupId,
        string debugName,
        Func<(int Current, int Target)> progress,
        Func<int, int, string> buildText)
    {
        QuestType = (Objectives.CustomQuestTypes)questTypeId;
        GroupId = groupId;
        DebugName = debugName;
        Progress = progress;
        BuildText = buildText;
    }
}

/// <summary>
/// Puts an Archipelago checklist into the game's own quest log: the win condition, region access,
/// and a progress line for each check block this seed turned on.
/// </summary>
/// <remarks>
/// The normal quest log is full of follower errands and never mentions the win condition or which
/// blocks are on.
///
/// Read only. Every line shows check state the mod already tracks. Nothing here sends a check or
/// changes the seed.
///
/// Uses Objectives_Custom added straight to ObjectiveManager (DcplIdx 5a). A new objective class
/// wouldn't save, since the game's saved list only knows its own types. Objectives_Custom stores
/// its quest type as an int, so an out of range value saves fine and marks the line as ours.
/// </remarks>
internal class QuestGuideService : IService
{
    // Every group we own starts with this. Teardown sweeps by prefix rather than by the two
    // exact ids, so a group renamed in a future version still gets cleaned out of old saves
    internal const string GroupPrefix = "Archipelago/Objectives/";

    internal const string GoalGroupId = GroupPrefix + "GroupTitles/Goal";
    internal const string ChecklistGroupId = GroupPrefix + "GroupTitles/Checklist";

    private const string GoalGroupTitle = "Archipelago - Goal";
    private const string ChecklistGroupTitle = "Archipelago - Checklist";

    // Entry ids. Append only, because an id is the save-persisted identity of a line, so reusing
    // one for a different meaning would relabel a line in an existing save.
    private const int IdGoal = 9001;
    private const int IdRegions = 9002;
    private const int IdFollowers = 9003;
    private const int IdSermons = 9004;
    private const int IdDivineInspiration = 9005;
    private const int IdBuildings = 9006;
    private const int IdBroom = 9007;
    private const int IdSnailShrines = 9008;
    private const int IdTarotCards = 9009;
    private const int IdTarotShop = 9010;

    private readonly ArchipelagoSession session;
    private readonly GuideHudMode hudMode;
    private readonly List<QuestGuideEntry> entries;

    // Everything believed about the loaded save, dropped when it changes
    private void ForgetSave()
    {
        added = false;
        completed.Clear();
        lastProgress.Clear();
        registeredOnce.Clear();
    }

    // Entries already reported complete, so CompleteCustomObjective runs once each
    private readonly HashSet<int> completed = new();

    private int lastSaveSlot = -1;
    private bool added;
    private bool refreshFallbackLogged;

    internal QuestGuideService(
        ArchipelagoSession session,
        GuideHudMode hudMode,
        int goal,
        int requiredCount,
        IReadOnlyList<string> regionOrder,
        IReadOnlyDictionary<string, object> slotData)
    {
        this.session = session;
        this.hudMode = hudMode;
        entries = BuildEntries(goal, requiredCount, regionOrder, slotData);
    }

    public void Register()
    {
        Log.LogInfo($"[AP] Objective guide active: {entries.Count} line(s), HUD mode {hudMode}.");
        // Everything else is idempotent and lives in Tick, so connecting from the main menu
        // (no DataManager yet) is a no-op that heals itself once a save loads.
        Tick();
    }

    public void Unregister()
    {
        ForgetSave();
        lastSaveSlot = -1;
        lastSweptSlot = -1;

        // Teardown arrives on the websocket thread (Session_SocketClosed), and this writes save
        // lists the main thread is iterating.
        MainThreadQueue.Enqueue(SweepAll);
    }

    internal void Tick()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null || GameManager.GetInstance() == null)
        {
            // Between saves. Drop the "already added" belief so the next loaded save rebuilds.
            added = false;
            return;
        }

        if (SaveSlot.Current != lastSaveSlot)
        {
            lastSaveSlot = SaveSlot.Current;
            ForgetSave();
        }

        // I2 comes up after the plugin does, so treat this as "not yet" and retry next tick.
        if (!I2Terms.Ready)
        {
            return;
        }

        RefreshCheckedSnapshot();

        // Both are const, and Register loops every language slot, so they go through the same
        // short-circuit as the lines rather than being rewritten every second.
        RegisterOnce(GoalGroupId, GoalGroupTitle);
        RegisterOnce(ChecklistGroupId, ChecklistGroupTitle);

        var textChanged = false;
        List<QuestGuideEntry> satisfied = null;

        foreach (var entry in entries)
        {
            var (current, target) = Probe(entry);
            if (target > 0 && current >= target)
            {
                (satisfied ??= new List<QuestGuideEntry>()).Add(entry);
            }

            // Compare the numbers before composing the string. In a steady state a tick is then
            // a couple of int comparisons per line rather than ten string builds.
            if (lastProgress.TryGetValue(entry.TermKey, out var seen)
                && seen.Current == current && seen.Target == target)
            {
                continue;
            }
            lastProgress[entry.TermKey] = (current, target);

            if (!I2Terms.Register(entry.TermKey, entry.BuildText(current, target)))
            {
                continue;
            }

            textChanged = true;
        }

        if (!added)
        {
            EnsureObjectivesAdded(dataManager);
        }

        // Nothing of ours is tracked when pinning is off, so there is no HUD line to repaint.
        // The pause-menu log rebuilds from DataManager on open either way.
        if (textChanged && hudMode != GuideHudMode.Off)
        {
            RefreshTrackedText(dataManager);
        }

        if (satisfied != null)
        {
            CompleteSatisfiedEntries(satisfied);
        }
    }

    // The server's checked location set as a hash set, refreshed once per tick.
    //
    // AllLocationsChecked is scanned linearly, and the checklist asks about ~200 ids. Doing that
    // every second is wasteful when hashing once is cheap
    private readonly HashSet<long> checkedSnapshot = new();

    // Progress last rendered per line, so an unchanged tick composes no strings
    private readonly Dictionary<string, (int Current, int Target)> lastProgress = new();

    // Terms already registered whose text never changes
    private readonly HashSet<string> registeredOnce = new();

    private void RegisterOnce(string key, string text)
    {
        if (registeredOnce.Contains(key))
        {
            return;
        }

        if (I2Terms.Register(key, text))
        {
            registeredOnce.Add(key);
        }
    }

    private void RefreshCheckedSnapshot()
    {
        checkedSnapshot.Clear();

        var alreadyChecked = session?.Locations?.AllLocationsChecked;
        if (alreadyChecked != null)
        {
            checkedSnapshot.UnionWith(alreadyChecked);
        }
    }

    // A probe that throws must not take the tick, and therefore the guide, with it
    private (int Current, int Target) Probe(QuestGuideEntry entry)
    {
        try
        {
            var (current, target) = entry.Progress();
            return (Math.Max(0, Math.Min(current, target)), target);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Objective guide could not read progress for {entry.DebugName}: {e.Message}");
            return (0, 0);
        }
    }

    // ------------------------------------------------------------------ adding

    private void EnsureObjectivesAdded(DataManager dataManager)
    {
        // Our objectives are save-persisted, so a reload or a reconnect already has them.
        var present = new HashSet<Objectives.CustomQuestTypes>();
        foreach (var objective in AllOurObjectives(dataManager))
        {
            if (objective is Objectives_Custom custom)
            {
                present.Add(custom.CustomQuestType);
            }
        }

        // Goal group first. ObjectiveManager.Add auto-tracks the first group when nothing else
        // is tracked (:399-402), and the win condition is what we'd want that slot spent on.
        // OrderByDescending is a stable sort, so order within each group is preserved.
        foreach (var entry in entries.OrderByDescending(e => e.GroupId == GoalGroupId))
        {
            if (present.Contains(entry.QuestType))
            {
                continue;
            }

            Add(entry);
        }

        added = true;

        // Add's free auto-track is not the tracking we want past the first group, so state it
        // explicitly. Done once, at add time. If the player later pins three other things and
        // evicts ours, that is their call, and they can re-pin from the quest log.
        if (hudMode != GuideHudMode.Everything)
        {
            Untrack(dataManager, ChecklistGroupId);
        }

        if (hudMode == GuideHudMode.Off)
        {
            Untrack(dataManager, GoalGroupId);
        }

        Log.LogInfo($"[AP] Objective guide added to save slot {lastSaveSlot} "
            + $"({entries.Count - present.Count} new line(s), {present.Count} already present).");
    }

    private void Add(QuestGuideEntry entry)
    {
        var objective = new Objectives_Custom(
            entry.GroupId, entry.QuestType, targetFollowerID: -1, questExpireDuration: -1f);

        // Index, Follower and TargetFollowerID all stay at their -1 defaults, and all three
        // matter (DcplIdx 5a):
        //   Index: TailorMenu_Assign.cs:152 compares Index == 53 against every live objective.
        //   Follower: ObjectivesData.Complete() spawns a follower turn-in when it isn't -1.
        //   TargetFollowerID: Objectives_Custom.Text runs the string through string.Format when it
        //   isn't -1, so a stray brace would throw inside a UI paint.
        objective.FailLocked = true;
        objective.AutoRemoveQuestOnceComplete = false;

        ObjectiveManager.Add(objective, autoTrack: hudMode != GuideHudMode.Off && entry.GroupId == GoalGroupId);
    }

    private static void Untrack(DataManager dataManager, string groupId)
    {
        var uniqueId = UniqueGroupIdFor(dataManager, groupId);
        if (uniqueId != null)
        {
            ObjectiveManager.UntrackGroup(uniqueId);
        }
    }

    // The UniqueGroupID the game assigned our group. ObjectiveManager.Add picks it (reusing
    // whatever an existing objective with the same GroupId already has), so it is read back
    // rather than chosen
    private static string UniqueGroupIdFor(DataManager dataManager, string groupId) =>
        AllOurObjectives(dataManager)
            .FirstOrDefault(o => o.GroupId == groupId && !string.IsNullOrEmpty(o.UniqueGroupID))
            ?.UniqueGroupID;

    // ------------------------------------------------------------------ refreshing

    // ObjectiveManager.OnObjectiveUpdated's backing field. UIObjective subscribes to that event
    // and re-reads ObjectivesData.Text when it fires for its own objective, which is exactly
    // the refresh a changed counter needs
    private static readonly FieldInfo ObjectiveUpdatedEvent =
        AccessTools.Field(typeof(ObjectiveManager), "OnObjectiveUpdated");

    // Repaints the on-screen tracker after a counter moved. Only the HUD needs this, since the
    // pause menu log rebuilds from DataManager every time it opens.
    //
    // Not ObjectiveManager.UpdateObjective, the obvious choice. That runs TryComplete(), and
    // Objectives_Custom.CheckComplete() passes straight away while ResultFollowerID ==
    // TargetFollowerID == -1, so every line would tick itself off the moment its text changed
    private void RefreshTrackedText(DataManager dataManager)
    {
        // Only when the field itself is missing, i.e. a game update renamed it. LocalizeAll is
        // language-change-sized work, so it must not be the answer to an ordinary tick.
        if (ObjectiveUpdatedEvent == null)
        {
            if (!refreshFallbackLogged)
            {
                refreshFallbackLogged = true;
                Log.LogWarning("[AP] Could not reach ObjectiveManager.OnObjectiveUpdated - "
                    + "falling back to a full re-localize for objective guide refreshes.");
            }

            LocalizationManager.LocalizeAll(true);
            return;
        }

        // A null handler means no UIObjective is enabled, so there is nothing on screen to
        // repaint. It subscribes in OnEnable and drops out in OnDisable. The I2 term is already
        // current by now, and Objectives_Custom.Text reads it live, so the next one to show is
        // right without help.
        var handler = ObjectiveUpdatedEvent.GetValue(null) as ObjectiveManager.ObjectiveUpdated;
        if (handler == null)
        {
            return;
        }

        foreach (var objective in AllOurObjectives(dataManager))
        {
            handler(objective);
        }
    }

    // ------------------------------------------------------------------ completing

    private void CompleteSatisfiedEntries(IEnumerable<QuestGuideEntry> satisfied)
    {
        foreach (var entry in satisfied)
        {
            if (!completed.Add((int)entry.QuestType))
            {
                continue;
            }

            // Sets ResultFollowerID to -1, which equals our TargetFollowerID, so CheckComplete
            // passes and the game plays its own tick-box animation for us.
            ObjectiveManager.CompleteCustomObjective(entry.QuestType);
            Log.LogInfo($"[AP] Objective guide line complete: {entry.DebugName}.");
        }
    }

    // ------------------------------------------------------------------ teardown

    // Save slot the idle sweep has already cleaned. See SweepLoadedSaveOnce
    private static int lastSweptSlot = -1;

    // The disconnected idle sweep, run once per loaded save rather than every tick.
    //
    // SweepAll is several full list scans, one of them over CompletedObjectivesHistory, which
    // grows without bound. A player who never connects would otherwise pay that every second
    // forever to keep finding nothing. Once per save still covers the case the sweep exists
    // for, since a crash mid-session is cleaned when that save is next loaded
    internal static void SweepLoadedSaveOnce()
    {
        if (DataManager.Instance == null || GameManager.GetInstance() == null)
        {
            // Between saves, so sweep again when one is loaded.
            lastSweptSlot = -1;
            return;
        }

        if (SaveSlot.Current == lastSweptSlot)
        {
            return;
        }

        lastSweptSlot = SaveSlot.Current;
        SweepAll();
    }

    // Removes every trace of the guide from the loaded save.
    //
    // DataManager.Objectives and friends are save-persisted, so without this a player who
    // disconnects, or crashes, is left with Archipelago lines in a vanilla quest log forever.
    // Static and self-contained so the plugin can also run it while disconnected, the same way
    // ManagedCollection.SettleIfOwed cleans up after a session that never got to end cleanly
    internal static void SweepAll()
    {
        var dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            return;
        }

        var uniqueIds = AllOurObjectives(dataManager)
            .Select(o => o.UniqueGroupID)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        // Untrack first. UIObjectivesController.OnObjectiveRemoved has an empty body
        // (:315-317), so UntrackGroup is the only thing that takes the group off screen.
        foreach (var uniqueId in uniqueIds)
        {
            ObjectiveManager.UntrackGroup(uniqueId, ignoreQueue: true);
        }

        var removed = 0;
        removed += RemoveOurs(dataManager.Objectives);
        removed += RemoveOurs(dataManager.CompletedObjectives);
        removed += RemoveOurs(dataManager.FailedObjectives);
        removed += RemoveOurs(dataManager.DungeonObjectives);
        removed += RemoveOursFinalized(dataManager.CompletedObjectivesHistory);
        removed += RemoveOursFinalized(dataManager.FailedObjectivesHistory);

        // ObjectivesData.Complete() writes into CompletedObjectivesHistory, and the tracked-id
        // list keeps a UniqueGroupID whose objectives are now gone.
        dataManager.TrackedObjectiveGroupIDs?.RemoveAll(uniqueIds.Contains);

        if (removed > 0)
        {
            Log.LogInfo($"[AP] Swept {removed} Archipelago objective(s) out of the save.");
        }
    }

    private static bool IsOurs(string groupId) =>
        groupId != null && groupId.StartsWith(GroupPrefix, StringComparison.Ordinal);

    private static int RemoveOurs(List<ObjectivesData> list) =>
        list?.RemoveAll(o => o != null && IsOurs(o.GroupId)) ?? 0;

    private static int RemoveOursFinalized(List<ObjectivesDataFinalized> list) =>
        list?.RemoveAll(o => o != null && IsOurs(o.GroupId)) ?? 0;

    // Live and completed guide objectives. History lists hold a different type
    private static IEnumerable<ObjectivesData> AllOurObjectives(DataManager dataManager) =>
        (dataManager.Objectives ?? Enumerable.Empty<ObjectivesData>())
        .Concat(dataManager.CompletedObjectives ?? Enumerable.Empty<ObjectivesData>())
        .Concat(dataManager.FailedObjectives ?? Enumerable.Empty<ObjectivesData>())
        .Where(o => o != null && IsOurs(o.GroupId));

    // ------------------------------------------------------------------ the lines

    // Which lines this seed gets. Every entry is gated on the same slot-data key that decides
    // whether the matching service registers at all, so the guide can never advertise a block
    // that isn't running
    private List<QuestGuideEntry> BuildEntries(
        int goal,
        int requiredCount,
        IReadOnlyList<string> regionOrder,
        IReadOnlyDictionary<string, object> slotData)
    {
        var result = new List<QuestGuideEntry>();

        if (goal == GoalService.GoalNarinder)
        {
            // One encounter, so Required Count doesn't apply and "1 of 1 down" reads badly.
            result.Add(new QuestGuideEntry(
                IdGoal, GoalGroupId, "goal",
                () => (GoalProgress.CountForGoal(goal), 1),
                (current, _) => current > 0
                    ? "Defeat Narinder, The One Who Waits. Done."
                    : "Defeat Narinder, The One Who Waits."));
        }
        else
        {
            var goalNoun = goal == GoalService.GoalWitnesses ? "Witnesses" : "Bishops";
            result.Add(new QuestGuideEntry(
                IdGoal, GoalGroupId, "goal",
                () => (GoalProgress.CountForGoal(goal), requiredCount),
                (current, target) => target >= 4
                    ? $"Defeat all four {goalNoun} - {current} of {target}"
                    : $"Defeat any {target} of the four {goalNoun} - {current} of {target}"));
        }

        if (SlotData.GetBool(slotData, "randomizeRegionAccess"))
        {
            result.Add(new QuestGuideEntry(
                IdRegions, GoalGroupId, "regions",
                () => (CountUnlockedRegions(), RegionMapping.RegionToDungeonLocation.Count),
                (current, target) => DescribeRegions(regionOrder, current, target)));
        }

        AddCheckBlock(result, slotData, IdFollowers, "followers",
            "followerMilestoneChecks", "followerLocationBaseId", "followerLocationCount",
            (current, target) => $"Recruit followers to the cult - {current} of {target}");

        AddCheckBlock(result, slotData, IdSermons, "sermons",
            "randomizeSermonUpgrades", "sermonLocationBaseId", "sermonLocationCount",
            (current, target) => $"Earn sermon upgrades at the Temple - {current} of {target}");

        // Divine Inspiration is a Choice, not a Toggle, and mode 0 is off.
        if ((int)SlotData.GetLong(slotData, "divineInspirationMode") != DivineInspirationService.ModeOff)
        {
            AddSequentialBlock(result, slotData, IdDivineInspiration, "divine inspiration",
                "divineInspirationLocationBaseId", "divineInspirationLocationCount",
                (current, target) => $"Fill the Devotion meter for Divine Inspiration - {current} of {target}");
        }

        if (SlotData.GetBool(slotData, "buildingChecks"))
        {
            // Check derived rather than save derived. The save has no record of a
            // non-decoration building, and asking the scene costs ~25 GetAllStructuresOfType
            // queries, far too much at 1 Hz. See BuildingService.DescribeState.
            var buildingIds = SlotData.ParseIdValues(slotData, "buildingLocations");
            AddIdList(result, IdBuildings, "buildings", buildingIds,
                (current, target) => $"Construct the buildings this seed tracks - {current} of {target}");
        }

        AddCheckBlock(result, slotData, IdBroom, "broom",
            "broomChecks", "broomLocationBaseId", "broomLocationCount",
            (current, target) => $"Upgrade the broom by sweeping - {current} of {target}");

        AddCheckBlock(result, slotData, IdSnailShrines, "snail shrines",
            "snailShrineChecks", "snailLocationBaseId", "snailLocationCount",
            (current, target) => $"Offer Shells at the Snail Shrines - {current} of {target}");

        if (SlotData.GetBool(slotData, "randomizeTarotCards"))
        {
            // Save state is unusable here by design. TarotService empties PlayerFoundTrinkets
            // of every managed card, so the game's collection is the inverse of the answer.
            AddIdList(result, IdTarotCards, "tarot cards",
                SlotData.ParseIdValues(slotData, "tarotCardLocations"),
                (current, target) => $"Find tarot cards on crusade - {current} of {target}");
        }

        if (SlotData.GetBool(slotData, "tarotShopChecks"))
        {
            // Not save-derivable at all. The record is BuyEntry.Bought on the shop prefab,
            // unreachable unless the player is standing in that hub.
            AddIdList(result, IdTarotShop, "tarot shop",
                SlotData.ParseIdValues(slotData, "tarotShopLocations"),
                (current, target) => $"Buy tarot cards from the shopkeepers - {current} of {target}");
        }

        // Weapons and curses are absent. Their check fires on whatever the player
        // happened to pick up, which is not something anyone can aim at.

        AssertTextIsSafe(result);
        return result;
    }

    // Braces would reach string.Format inside a UI paint and newlines break the tracker's
    // per-line strikethrough. Both are author errors, so they're caught at construction with a
    // loud log rather than left to surface as a blank or broken line in game
    private static void AssertTextIsSafe(IEnumerable<QuestGuideEntry> built)
    {
        foreach (var entry in built)
        {
            // Both ends, because some lines word their finished state differently and the zero
            // state alone never reaches that branch
            foreach (var pair in new[] { new[] { 0, 1 }, new[] { 1, 1 } })
            {
                string sample;
                try
                {
                    sample = entry.BuildText(pair[0], pair[1]);
                }
                catch (Exception e)
                {
                    Log.LogError($"[AP] Objective guide line '{entry.DebugName}' threw while "
                        + $"building its text: {e.Message}");
                    continue;
                }

                if (sample == null)
                {
                    continue;
                }

                if (sample.IndexOf('{') >= 0 || sample.IndexOf('}') >= 0
                    || sample.IndexOf('\n') >= 0)
                {
                    Log.LogError($"[AP] Objective guide line '{entry.DebugName}' contains a "
                        + $"brace or newline and will not render correctly: \"{sample}\"");
                }
            }
        }
    }

    private void AddCheckBlock(
        List<QuestGuideEntry> target,
        IReadOnlyDictionary<string, object> slotData,
        int id,
        string debugName,
        string toggleKey,
        string baseIdKey,
        string countKey,
        Func<int, int, string> buildText)
    {
        if (!SlotData.GetBool(slotData, toggleKey))
        {
            return;
        }

        AddSequentialBlock(target, slotData, id, debugName, baseIdKey, countKey, buildText);
    }

    private void AddSequentialBlock(
        List<QuestGuideEntry> target,
        IReadOnlyDictionary<string, object> slotData,
        int id,
        string debugName,
        string baseIdKey,
        string countKey,
        Func<int, int, string> buildText)
    {
        var baseId = SlotData.GetLong(slotData, baseIdKey);
        var count = (int)SlotData.GetLong(slotData, countKey);
        if (count <= 0)
        {
            return;
        }

        var ids = Enumerable.Range(0, count).Select(i => baseId + i).ToList();
        AddIdList(target, id, debugName, ids, buildText);
    }

    private void AddIdList(
        List<QuestGuideEntry> target,
        int id,
        string debugName,
        IReadOnlyList<long> ids,
        Func<int, int, string> buildText)
    {
        if (ids == null || ids.Count == 0)
        {
            return;
        }

        target.Add(new QuestGuideEntry(id, ChecklistGroupId, debugName, () => CountChecked(ids), buildText));
    }

    // How many of a block's locations the server has recorded.
    //
    // Counting the server's record rather than the save means a player running two saves
    // against one slot sees the slot's progress rather than that save's, which is the right
    // answer to "how much of this seed is done"
    private (int Current, int Target) CountChecked(IReadOnlyList<long> ids)
    {
        // A plain loop rather than ids.Count(predicate): the predicate closes over a mutable
        // field, so the compiler cannot cache the delegate and each block allocates one per tick.
        var found = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            if (checkedSnapshot.Contains(ids[i]))
            {
                found++;
            }
        }

        return (found, ids.Count);
    }

    private static int CountUnlockedRegions() =>
        RegionMapping.RegionToDungeonLocation.Values.Count(RegionLockState.IsUnlocked);

    private static string DescribeRegions(IReadOnlyList<string> regionOrder, int current, int target)
    {
        var line = $"Open the Bishops' domains - {current} of {target}";

        var next = regionOrder?.FirstOrDefault(name =>
            RegionMapping.RegionToDungeonLocation.TryGetValue(name, out var location)
            && !RegionLockState.IsUnlocked(location));

        return next == null ? line : $"{line} - next: {next}";
    }

    // ------------------------------------------------------------------ diagnostics

    // Included in the F9 state dump, and the whole of the Ctrl+F9 dump.
    //
    // The I2 read-back per line is the point. A term that never registered and a UI fault look
    // identical in game (a blank line), and only this tells them apart
    internal string DescribeState()
    {
        var dataManager = DataManager.Instance;
        RefreshCheckedSnapshot();

        var lines = new List<string>
        {
            $"Objective guide: {entries.Count} line(s), HUD mode {hudMode}, added={added}, "
            + $"save slot {lastSaveSlot}.",
        };

        foreach (var entry in entries)
        {
            var (current, target) = Probe(entry);
            var intended = entry.BuildText(current, target);
            var readBack = I2Terms.Read(entry.TermKey);
            var matches = string.Equals(readBack, intended, StringComparison.Ordinal);

            lines.Add($"  {(int)entry.QuestType} [{ShortGroup(entry.GroupId)}] {current}/{target} "
                + $"\"{intended}\" - I2 read-back "
                + (matches ? "OK" : $"MISMATCH: \"{readBack}\""));
        }

        if (dataManager == null)
        {
            lines.Add("  No DataManager loaded - nothing in a save to report.");
            return string.Join("\n", lines);
        }

        lines.Add($"  In save: {CountOurs(dataManager.Objectives)} active, "
            + $"{CountOurs(dataManager.CompletedObjectives)} completed, "
            + $"{CountOurs(dataManager.FailedObjectives)} failed, "
            + $"{CountOursFinalized(dataManager.CompletedObjectivesHistory)} completed-history, "
            + $"{CountOursFinalized(dataManager.FailedObjectivesHistory)} failed-history.");

        var tracked = dataManager.TrackedObjectiveGroupIDs ?? new List<string>();
        var ourTracked = AllOurObjectives(dataManager)
            .Select(o => o.UniqueGroupID)
            .Distinct()
            .Where(tracked.Contains)
            .ToList();
        lines.Add($"  Tracked group id(s) of ours: "
            + (ourTracked.Count == 0 ? "none" : string.Join(", ", ourTracked)));

        return string.Join("\n", lines);
    }

    // Sweeps, then forces a rebuild on the next tick, so one session can test add, sweep and re-add
    // without reconnecting
    internal void ForceRebuild()
    {
        SweepAll();
        ForgetSave();
        Tick();
    }

    private static string ShortGroup(string groupId) =>
        groupId == GoalGroupId ? "Goal" : "Checklist";

    private static int CountOurs(List<ObjectivesData> list) =>
        list?.Count(o => o != null && IsOurs(o.GroupId)) ?? 0;

    private static int CountOursFinalized(List<ObjectivesDataFinalized> list) =>
        list?.Count(o => o != null && IsOurs(o.GroupId)) ?? 0;
}
