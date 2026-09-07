using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Turns "the game just unlocked a tarot card" into an Archipelago check, and stops the game
/// handing the card over.
/// </summary>
/// <remarks>
/// Every route to a permanent card unlock ends at TarotCards.UnlockTrinket (the reveal menu:
/// crusade finds, shop purchases, challenge rewards) or DataManager.UnlockTrinket (world-placed
/// cards). Intercepting the outcome covers all 85 unlock conditions without knowing any of them.
/// </remarks>
[HarmonyPatch]
internal static class TarotUnlockPatch
{
    /// <summary>
    /// Answers what to do about a card the game is trying to unlock. Set by TarotService while
    /// connected, and null the rest of the time, which leaves the game entirely alone.
    /// </summary>
    internal static Func<TarotCards.Card, UnlockDecision> Decide;

    internal enum UnlockDecision
    {
        // Not ours, so vanilla behaviour stands
        Allow,

        // Ours, and earning it is a check. Send it, and don't grant the card
        SendCheck,

        // Ours, but already accounted for, so swallow it without sending anything
        Swallow,
    }

    [HarmonyPatch(typeof(TarotCards), nameof(TarotCards.UnlockTrinket))]
    [HarmonyPrefix]
    private static bool TarotCards_UnlockTrinket_Prefix(TarotCards.Card card, ref bool __result)
        => Intercept(card, ref __result);

    [HarmonyPatch(typeof(DataManager), nameof(DataManager.UnlockTrinket))]
    [HarmonyPrefix]
    private static bool DataManager_UnlockTrinket_Prefix(TarotCards.Card card, ref bool __result)
        => Intercept(card, ref __result);

    // Applies Decide to one unlock attempt. Returns false to skip the original, which is what
    // withholds the card
    private static bool Intercept(TarotCards.Card card, ref bool __result)
    {
        if (Decide == null)
        {
            return true;
        }

        switch (Decide(card))
        {
            case UnlockDecision.SendCheck:
                Log.LogInfo($"[AP] Earned tarot card {card} - sending its check. The card itself "
                    + "comes from the multiworld.");
                __result = false;
                return false;

            case UnlockDecision.Swallow:
                __result = false;
                return false;

            default:
                return true;
        }
    }
}
