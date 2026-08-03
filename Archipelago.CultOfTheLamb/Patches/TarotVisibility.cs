using System;
using System.Collections.Generic;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Shows the player's Archipelago cards to the two parts of the game that should see them,
/// while keeping them out of the collection everywhere else.
///
/// TarotService never writes a granted card into DataManager.Instance.PlayerFoundTrinkets,
/// because every route the game has to offer a card first checks you don't already own it - a
/// real unlock would close that gate and strand the check riding on it. The cost is that the
/// places which genuinely need the player's full collection read the same list: the in-run
/// draw pool, and the collection screen.
///
/// Both are handled by lending the cards to PlayerFoundTrinkets for the length of one call and
/// taking them straight back out. Lending rather than adjusting the result matters for the
/// draw pool in particular: GetUnusedFoundTrinkets filters on fleece, corruption pairing,
/// season, relic scale and the resurrect ability, and a re-implementation of that condition
/// would drift from the game's within a patch or two.
///
/// Other readers are deliberately left alone. Completion percentage (CompletionCalculator),
/// TarotCards.GetTrinketsUnlocked and the ALL_TAROTS_UNLOCKED achievement all count the
/// collection directly, so while connected they under-report by however many cards
/// Archipelago has granted, and the achievement can't fire. Both correct themselves the
/// moment the cards go back on disconnect. Lending to them isn't worth it: the achievement
/// path writes a permanent unlock, which is the one thing this class exists to prevent.
/// </summary>
internal static class TarotVisibility
{
    /// <summary>
    /// The cards Archipelago has granted. Set by TarotService while connected; null the rest
    /// of the time, which leaves the game entirely alone.
    /// </summary>
    internal static Func<IEnumerable<TarotCards.Card>> GrantedCards;

    /// <summary>
    /// Adds the granted cards to the collection, returning exactly the ones it added so
    /// <see cref="Take"/> removes those and nothing else. Null when there was nothing to lend,
    /// which is the common case - the game calls these methods whether we're connected or not.
    /// </summary>
    private static List<TarotCards.Card> Lend()
    {
        var granted = GrantedCards?.Invoke();
        if (granted == null) return null;

        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null) return null;

        List<TarotCards.Card> lent = null;

        foreach (var card in granted)
        {
            // Already there means it isn't ours to take back out again.
            if (found.Contains(card)) continue;

            (lent ??= new List<TarotCards.Card>()).Add(card);
            found.Add(card);
        }

        return lent;
    }

    private static void Take(List<TarotCards.Card> lent)
    {
        if (lent == null) return;

        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null) return;

        foreach (var card in lent) found.Remove(card);
    }

    /// <summary>
    /// The in-run draw pool - what a tarot pedestal or a shrine offers you mid-crusade. The
    /// one place where getting this wrong is a gameplay bug rather than a cosmetic one: miss
    /// it and Archipelago's cards never show up in a run at all.
    /// </summary>
    [HarmonyPatch(typeof(TarotCards), nameof(TarotCards.GetUnusedFoundTrinkets))]
    internal static class DrawPool
    {
        [HarmonyPrefix]
        private static void Prefix(out List<TarotCards.Card> __state) => __state = Lend();

        // A finalizer rather than a postfix, because it runs even if the original throws.
        // Cards left on loan would silently become permanently owned, which is the exact
        // failure this whole class exists to avoid.
        [HarmonyFinalizer]
        private static void Finalizer(List<TarotCards.Card> __state) => Take(__state);
    }

    /// <summary>
    /// The tarot collection screen. It builds an entry for every card in the game and asks
    /// TarotCards.IsUnlocked which ones to show as owned (TarotCardItem_Unlocked.Configure),
    /// so lending for the length of the build is enough to light up Archipelago's cards.
    /// </summary>
    [HarmonyPatch(typeof(UITarotCardsMenuController), "OnShowStarted")]
    internal static class CollectionScreen
    {
        [HarmonyPrefix]
        private static void Prefix(out List<TarotCards.Card> __state) => __state = Lend();

        [HarmonyFinalizer]
        private static void Finalizer(List<TarotCards.Card> __state) => Take(__state);
    }
}
