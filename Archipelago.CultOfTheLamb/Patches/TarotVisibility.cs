using System;
using System.Collections.Generic;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Shows your Archipelago cards to the in-run draw pool and the collection screen while keeping
/// them out of PlayerFoundTrinkets everywhere else. See ManagedCollection for why.
///
/// Both patches lend the cards to the list for one call and take them back after. Lending rather
/// than editing the result keeps the game's own filtering intact.
///
/// Every other reader is left alone deliberately. Completion percentage and the all-tarots
/// achievement read low while connected and correct themselves on disconnect, and that
/// achievement writes a permanent unlock, which is the thing this class exists to prevent.
/// </summary>
internal static class TarotVisibility
{
    // The cards Archipelago has granted. Set by TarotService while connected, and null the rest
    // of the time, which leaves the game entirely alone
    internal static Func<IEnumerable<TarotCards.Card>> GrantedCards;

    // Returns exactly the cards it added, so Take removes those and nothing else, or null when
    // there was nothing to lend. Catching here rather than in the prefixes means a partial
    // failure still hands back whatever did get added: a card left on loan is a permanent unlock
    // in the save.
    private static List<TarotCards.Card> Lend()
    {
        List<TarotCards.Card> lent = null;

        try
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
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't lend the Archipelago cards to the game's list, so "
                + $"some may not show up: {e}");
        }

        return lent;
    }

    private static void Take(List<TarotCards.Card> lent)
    {
        if (lent == null)
        {
            return;
        }

        // Logged as an error, not a warning: anything still on loan here is now a permanent
        // unlock in the player's save.
        try
        {
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
        catch (Exception e)
        {
            Log.LogError($"[AP] Couldn't take back {lent.Count} lent card(s), so they may now "
                + $"count as permanently found: {e}");
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

        // A finalizer rather than a postfix, so it still runs if the original throws. Cards
        // left on loan would silently become permanent unlocks.
        [HarmonyFinalizer]
        private static void Finalizer(List<TarotCards.Card> __state)
        {
            Take(__state);
        }
    }

    /// <summary>
    /// The collection screen, which reads PlayerFoundTrinkets directly and through
    /// TarotCards.IsUnlocked, so lending for the length of the build lights up our cards.
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
