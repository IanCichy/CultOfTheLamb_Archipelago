using System;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Turns "the game just unlocked a tarot card" into an Archipelago check, and stops the game
/// handing the card over.
///
/// Every reachable route to a permanent unlock ends at TarotCards.UnlockTrinket (the reveal
/// menu, so crusade finds, shop purchases and challenge rewards) or DataManager.UnlockTrinket
/// (world-placed cards). Intercepting the outcome covers all 85 unlock conditions without
/// knowing any of them.
///
/// Two other places write PlayerFoundTrinkets and would slip past both patches.
/// TarotCards.UnlockRandomTrinket has no callers anywhere in the assembly, so it cannot fire
/// today, but a caller appearing there after a game update would leak a card silently rather
/// than failing loudly. GameManager.Awake (:175-194) replaces the whole list when it finds it
/// empty, which TarotService.Tick is what handles.
/// </summary>
[HarmonyPatch]
internal static class TarotUnlockPatch
{
    // Answers what to do about a card the game is trying to unlock. Set by TarotService while
    // connected, and null the rest of the time, which leaves the game entirely alone
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

        UnlockDecision decision;
        try
        {
            decision = Decide(card);
        }
        catch (Exception e)
        {
            // True lets the game unlock the card the way it always did, so the player is up a
            // card and down a check. Returning false instead would withhold a card nobody is
            // going to send, and this runs on every route to an unlock.
            Log.LogWarning($"[AP] Couldn't decide what to do about tarot card {card}, so the "
                + $"game keeps it and no check is sent: {e}");
            return true;
        }

        switch (decision)
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
