using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// What the gods do to you, and what they do for you.
/// </summary>
/// <remarks>
/// Every entry is one call the game already makes somewhere, so none of this is new mechanics.
/// Each returns null when there is nothing to act on, and the roller tries another.
/// </remarks>
internal static class PlagueTable
{
    // A quarter of the cult, never none, never more than this. The game's own follower effects
    // use a sixth to a quarter, and a full cult tops out around 25.
    private const float CultShare = 0.25f;
    private const int MostFollowers = 7;

    // Same share of the farm. A blizzard withers each ineligible plot at 35%, so this is gentler.
    private const float CropShare = 0.25f;

    // Tax takes a share of what you are carrying, so it stings a rich player as much as a poor
    // one. Alms is flat, because a mercy that scales with wealth rewards hoarding.
    private const float TaxShare = 0.10f;
    private const int AlmsGold = 100;

    // BLACK_GOLD despite the name: the spendable gold the HUD counts (HUD_Manager.cs:747) and
    // shops charge in. GOLD_NUGGET and GOLD_REFINED are crafting materials.
    private const InventoryItem.ITEM_TYPE Gold = InventoryItem.ITEM_TYPE.BLACK_GOLD;

    internal static readonly IReadOnlyList<Plague> Curses = new[]
    {
        new Plague("Pestilence", false, () =>
            Afflict(Thought.Ill, n => $"a sickness takes {n} of your flock")),

        new Plague("Famine", false, () =>
            Afflict(Thought.BecomeStarving, n => $"hunger takes {n} of your flock")),

        new Plague("Drunken Stupor", false, Drunkenness),

        new Plague("Locusts", false, Wither),

        new Plague("Tax", false, TakeGold),
    };

    // Rare, so each one is worth the wait. Divine Favour and Inspired Sermon are the strongest
    // things the gods can hand an Archipelago run: both fill a meter whose completion is a check.
    internal static readonly IReadOnlyList<Plague> Mercies = new[]
    {
        new Plague("Divine Favour", false, () =>
            Manipulate(WorldManipulatorManager.Manipulations.FillDevotion,
                "the Devotion meter fills at once")),

        new Plague("Inspired Sermon", false, () =>
            Manipulate(WorldManipulatorManager.Manipulations.FillSermon,
                "the sermon bar fills at once")),

        new Plague("Miracle", false, () =>
            Manipulate(WorldManipulatorManager.Manipulations.InstantlyBuildStructures,
                "every unfinished building raises itself")),

        new Plague("Alms", false, GiveGold),
    };

    /// <summary>How many of a group a plague touches.</summary>
    internal static int Share(int total, float fraction, int cap) =>
        total <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(total * fraction), 1, cap);

    // The game's own follower curse. It skips locked, already-cursed and immune followers itself,
    // so the count is a request rather than a promise.
    private static string Afflict(Thought thought, Func<int, string> line)
    {
        var count = Share(FollowerBrain.AllBrains?.Count ?? 0, CultShare, MostFollowers);
        if (count <= 0)
        {
            return null;
        }

        FollowerManager.GiveFollowersRandomCurse(count, thought);
        return line(count);
    }

    // MakeDrunk rather than the game's BefuddledFollowers, which needs a Pub built and always
    // takes half the cult. This needs neither.
    private static string Drunkenness()
    {
        var brains = FollowerBrain.AllBrains;
        if (brains == null || brains.Count == 0)
        {
            return null;
        }

        var count = Share(brains.Count, CultShare, MostFollowers);
        var touched = 0;

        foreach (var brain in brains.OrderBy(_ => UnityEngine.Random.value).Take(count))
        {
            if (brain == null)
            {
                continue;
            }

            brain.MakeDrunk();
            touched++;
        }

        return touched > 0 ? $"{touched} of your flock can no longer stand straight" : null;
    }

    // SetWithered no-ops on an already withered plot, on seed type 160 and on fertilizer 187, and
    // emits its own smoke at base, so this only has to pick which plots to call it on.
    private static string Wither()
    {
        var plots = StructureManager.GetAllStructuresOfType<Structures_FarmerPlot>();
        if (plots == null || plots.Count == 0)
        {
            return null;
        }

        var count = Share(plots.Count, CropShare, plots.Count);
        var touched = 0;

        foreach (var plot in plots.OrderBy(_ => UnityEngine.Random.value).Take(count))
        {
            if (plot == null)
            {
                continue;
            }

            plot.SetWithered();
            touched++;
        }

        return touched > 0 ? $"locusts wither {touched} of your crops" : null;
    }

    private static string TakeGold()
    {
        var held = Inventory.GetItemQuantity(Gold);
        var taken = Share(held, TaxShare, held);
        if (taken <= 0)
        {
            return null;
        }

        Inventory.ChangeItemQuantity(Gold, -taken);
        return $"the gods take {taken} gold";
    }

    private static string GiveGold()
    {
        Inventory.ChangeItemQuantity(Gold, AlmsGold);
        return $"the gods leave {AlmsGold} gold";
    }

    // Asks the game whether the effect is possible here before firing it. Dungeon effects
    // dereference BiomeGenerator.Instance and throw at base, and the filters are the only honest
    // answer to "can this run right now".
    private static string Manipulate(WorldManipulatorManager.Manipulations effect, string line)
    {
        var allowed =
            WorldManipulatorManager.GetPossibleBasePositiveManipulations().Contains(effect) ||
            WorldManipulatorManager.GetPossibleBaseNegativeManipulations().Contains(effect);

        if (!allowed)
        {
            return null;
        }

        // Never twitch: true shows a "Thanks Twitch chat!" banner and writes to the save's
        // notification history.
        WorldManipulatorManager.TriggerManipulation(effect, 0f, false);
        return line;
    }
}
