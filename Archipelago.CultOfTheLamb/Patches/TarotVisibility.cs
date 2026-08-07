using System;
using System.Collections.Generic;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Shows the player's Archipelago cards to the two parts of the game that should see them -
/// the in-run draw pool and the collection screen - while keeping them out of
/// PlayerFoundTrinkets everywhere else (see ManagedCollection for why).
///
/// Both work by lending the cards to the list for one call and taking them straight back out.
/// Lending rather than adjusting the result matters for the draw pool: GetUnusedFoundTrinkets
/// filters on fleece, corruption pairing, season, relic scale and the resurrect ability, and a
/// re-implementation would drift from the game's within a patch or two.
///
/// Other readers are left alone. Completion percentage, GetTrinketsUnlocked and the
/// ALL_TAROTS_UNLOCKED achievement under-report while connected and correct themselves on
/// disconnect. Lending to them isn't worth it - the achievement path writes a permanent unlock,
/// which is the one thing this class exists to prevent.
/// </summary>
internal static class TarotVisibility
{
    /// <summary>
    /// The cards Archipelago has granted. Set by TarotService while connected; null the rest
    /// of the time, which leaves the game entirely alone.
    /// </summary>
    internal static Func<IEnumerable<TarotCards.Card>> GrantedCards;

    /// <summary>
    /// Adds the granted cards, returning exactly the ones it added so <see cref="Take"/> removes
    /// those and nothing else. Null when there was nothing to lend, which is the common case.
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
    /// The in-run draw pool - what a pedestal or shrine offers mid-crusade. The one place where
    /// getting this wrong is a gameplay bug: miss it and Archipelago's cards never show up.
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
    /// The collection screen. It asks TarotCards.IsUnlocked which entries to show as owned, so
    /// lending for the length of the build is enough to light up Archipelago's cards.
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
