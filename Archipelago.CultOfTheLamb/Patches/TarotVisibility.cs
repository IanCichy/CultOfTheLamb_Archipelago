using System;
using System.Collections.Generic;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Shows your Archipelago cards to the in-run draw pool and the collection screen, while keeping
/// them out of PlayerFoundTrinkets everywhere else. See ManagedCollection for why.
/// </summary>
/// <remarks>
/// Both lend the cards to the list for one call, then take them back. Lending instead of editing
/// the result means the draw pool keeps all of the game's own filtering.
///
/// Everything else that reads the list is left alone. Completion percentage and the all-tarots
/// achievement read low while connected and fix themselves on disconnect. That achievement writes
/// a permanent unlock, which is exactly what this class is here to stop.
/// </remarks>
internal static class TarotVisibility
{
    // The cards Archipelago has granted. Set by TarotService while connected, and null the rest
    // of the time, which leaves the game entirely alone
    internal static Func<IEnumerable<TarotCards.Card>> GrantedCards;

    // Adds the granted cards, returning exactly the ones it added so Take removes
    // those and nothing else. Null when there was nothing to lend, which is the common case
    private static List<TarotCards.Card> Lend()
    {
        var granted = GrantedCards?.Invoke();
        if (granted == null)
        {
            return null;
        }

        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null)
        {
            return null;
        }

        List<TarotCards.Card> lent = null;

        foreach (var card in granted)
        {
            // Already there means it isn't ours to take back out again.
            if (found.Contains(card))
            {
                continue;
            }

            (lent ??= new List<TarotCards.Card>()).Add(card);
            found.Add(card);
        }

        return lent;
    }

    private static void Take(List<TarotCards.Card> lent)
    {
        if (lent == null)
        {
            return;
        }

        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null)
        {
            return;
        }

        foreach (var card in lent)
        {
            found.Remove(card);
        }
    }

    /// <summary>
    /// The in-run draw pool, meaning what a pedestal or shrine offers mid-crusade. This is the
    /// one place where getting it wrong is a gameplay bug. Miss it and Archipelago's cards
    /// never show up.
    /// </summary>
    [HarmonyPatch(typeof(TarotCards), nameof(TarotCards.GetUnusedFoundTrinkets))]
    internal static class DrawPool
    {
        [HarmonyPrefix]
        private static void Prefix(out List<TarotCards.Card> __state)
        {
            __state = Lend();
        }

        // A finalizer rather than a postfix, because it runs even if the original throws.
        // Cards left on loan would silently become permanently owned, which is the exact
        // failure this whole class exists to avoid.
        [HarmonyFinalizer]
        private static void Finalizer(List<TarotCards.Card> __state)
        {
            Take(__state);
        }
    }

    /// <summary>
    /// The collection screen. It asks TarotCards.IsUnlocked which entries to show as owned, so
    /// lending for the length of the build is enough to light up Archipelago's cards.
    /// </summary>
    [HarmonyPatch(typeof(UITarotCardsMenuController), "OnShowStarted")]
    internal static class CollectionScreen
    {
        [HarmonyPrefix]
        private static void Prefix(out List<TarotCards.Card> __state)
        {
            __state = Lend();
        }

        [HarmonyFinalizer]
        private static void Finalizer(List<TarotCards.Card> __state)
        {
            Take(__state);
        }
    }
}
