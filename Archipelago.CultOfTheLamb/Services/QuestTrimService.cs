using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// Takes most of the game's built-in follower quests out of rotation, leaving a curated slice.
/// </summary>
/// <remarks>
/// Followers periodically walk over and offer one of ~87 hardcoded quests, such as cook three great
/// meals, dress someone in a fancy suit, murder a specific follower at night. Most are busywork
/// that pulls against whatever the multiworld actually wants, and a new player can't tell the
/// two apart. But turning a quest in is also the game's main source of follower loyalty XP
/// (FollowerBrain.AddAdoration(AdorationActions.Quest)), so wiping them all starves follower
/// levelling. Hence a trim with modes rather than a switch.
///
/// The lever is Quests.RemovedQuests, the developers' own kill switch. It is a private static
/// List&lt;int&gt; of indices into Quests.QuestsAll, and the very first filter inside
/// Quests.GetQuest's eligibility loop. See DcplIdx 5a.
/// </remarks>
internal class QuestTrimService : IService
{
    // Matches worlds/cult_of_the_lamb/options.py VanillaFollowerQuests.
    internal const int Unchanged = 0;
    internal const int ThinTrickle = 1;
    internal const int StoryOnly = 2;
    internal const int None = 3;

    private readonly int mode;

    /// <summary>
    /// The game's own RemovedQuests contents from before we touched it. Non-null means we have
    /// edited the list and owe a restore.
    /// </summary>
    private List<int> originalRemoved;

    private int keptCount;

    internal QuestTrimService(int mode)
    {
        this.mode = mode;
    }

    public void Register()
    {
        var questsAll = Traverse.Create(typeof(Quests)).Field("QuestsAll").GetValue<List<ObjectivesData>>();
        var removed = Traverse.Create(typeof(Quests)).Field("RemovedQuests").GetValue<List<int>>();
        var storyQuests = Traverse.Create(typeof(Quests)).Field("StoryQuests").GetValue<List<int>>();

        if (questsAll == null || removed == null)
        {
            // Leave the game exactly as it is rather than half-trimming it. A renamed field in
            // a game patch lands here, and a quieter quest log is not worth a broken one.
            Log.LogWarning("[AP] Could not reach Quests.QuestsAll / Quests.RemovedQuests - "
                + "vanilla follower quests are unchanged this session.");
            return;
        }

        originalRemoved = new List<int>(removed);

        var keep = BuildKeepSet(questsAll, storyQuests);
        keptCount = keep.Count;

        // Mutate the existing list rather than replacing it. That is cheaper, and it can't be defeated
        // by anything holding a cached reference to the old list.
        for (var i = 0; i < questsAll.Count; i++)
        {
            if (!keep.Contains(i) && !removed.Contains(i)) removed.Add(i);
        }

        Log.LogInfo($"[AP] Vanilla follower quests trimmed ({ModeName(mode)}): "
            + $"{keptCount} of {questsAll.Count} kept, RemovedQuests now holds {removed.Count} "
            + $"(was {originalRemoved.Count}).");
    }

    public void Unregister()
    {
        if (originalRemoved == null) return;

        // Mandatory, not tidiness. Quests is a static class living for the whole process, so
        // without this a disconnected session keeps playing with a trimmed quest table until
        // the game is restarted.
        var removed = Traverse.Create(typeof(Quests)).Field("RemovedQuests").GetValue<List<int>>();
        if (removed != null)
        {
            removed.Clear();
            removed.AddRange(originalRemoved);
            Log.LogInfo($"[AP] Vanilla follower quests restored ({removed.Count} removed "
                + "by the game's own defaults).");
        }

        originalRemoved = null;
    }

    /// <summary>
    /// Which quest indices survive.
    ///
    /// Predicate-based, never a hardcoded index list. QuestsAll is a literal inline table, so a
    /// game patch inserting one entry would shift every index after it and silently start
    /// keeping the wrong quests. Types and the game's own StoryQuests list both survive that.
    /// </summary>
    private HashSet<int> BuildKeepSet(List<ObjectivesData> questsAll, List<int> storyQuests)
    {
        var keep = new HashSet<int>();
        if (mode == None) return keep;

        // Story chains (Sozo, the lovers, the rivalries) survive everything but `none`. They
        // have to be kept explicitly. A new chain spawns through GetNewStory -> GetQuest with
        // the story entry as targetQuest, and that path checks list.Contains(targetQuest) -
        // exactly what RemovedQuests nulls out.
        if (storyQuests != null) keep.UnionWith(storyQuests);

        if (mode == StoryOnly) return keep;

        // ThinTrickle. Rituals and crusade collections are the two families that read as "go do
        // the thing you were going to do anyway", and Objectives_CollectItem is the only family
        // that pays out an item on turn-in (interaction_FollowerInteraction.cs:618).
        for (var i = 0; i < questsAll.Count; i++)
        {
            if (questsAll[i] is Objectives_PerformRitual or Objectives_CollectItem) keep.Add(i);
        }

        return keep;
    }

    private static string ModeName(int value) => value switch
    {
        Unchanged => "unchanged",
        ThinTrickle => "thin trickle",
        StoryOnly => "story only",
        None => "none",
        _ => $"unknown ({value})",
    };

    internal string DescribeState()
    {
        if (originalRemoved == null)
        {
            return $"Quest trim: mode {ModeName(mode)}, but the game's quest table could not be "
                + "reached - vanilla quests are unchanged.";
        }

        var removed = Traverse.Create(typeof(Quests)).Field("RemovedQuests").GetValue<List<int>>();
        var questsAll = Traverse.Create(typeof(Quests)).Field("QuestsAll").GetValue<List<ObjectivesData>>();

        var breakdown = questsAll == null
            ? "quest table unreadable"
            : string.Join(", ", questsAll
                .Where(q => q != null)
                .GroupBy(q => q.GetType().Name)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}"));

        return $"Quest trim: mode {ModeName(mode)}. QuestsAll={questsAll?.Count ?? -1}, "
            + $"kept={keptCount}, "
            + $"RemovedQuests={removed?.Count ?? -1} (was {originalRemoved.Count}). "
            + $"Table: {breakdown}.";
    }
}
