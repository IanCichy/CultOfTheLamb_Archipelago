using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The effects a received death brings in chaos mode, and the die that picks between them.
/// </summary>
/// <remarks>
/// The die is the game's own Fifty Fifty Gamble relic VFX. It decides its own outcome internally
/// (VFX_Dice.cs:52-57), so rather than patch it we subscribe to OnDiceRolled and let the roll
/// choose. That keeps the Rabbit's Foot tarot card's 70/30 bias in play, which is a reason to
/// want the card rather than a bug.
///
/// If the die cannot be spawned the effect still lands, just without the show.
/// </remarks>
internal static class ChaosPlagues
{
    // BLACK_GOLD despite the name: it is the spendable gold the HUD counts (HUD_Manager.cs:747)
    // and shops charge in. GOLD_NUGGET and GOLD_REFINED are crafting materials.
    private const InventoryItem.ITEM_TYPE Gold = InventoryItem.ITEM_TYPE.BLACK_GOLD;

    // How often the gods are merciful. The die itself is even, so the roll is steered to this.
    private const float MercyChance = 0.05f;

    // One plague is in flight. Nothing in the game stops a second bounce starting mid-roll.
    private static bool rolling;

    /// <summary>
    /// Rolls for a plague and applies whichever way it lands.
    /// </summary>
    internal static void Visit(string who)
    {
        if (rolling)
        {
            Log.LogInfo("[AP] Chaos: a plague is already being rolled for, ignoring this one.");
            return;
        }

        rolling = true;

        // Decided here, then the die is steered to show it, so its green flash never disagrees
        // with what actually happens.
        var merciful = UnityEngine.Random.value < MercyChance;

        if (!TryRollDice(merciful, blessed => Resolve(who, blessed)))
        {
            Resolve(who, merciful);
        }
    }

    private static void Resolve(string who, bool blessed)
    {
        rolling = false;
        ClearDiceBlur();

        try
        {
            if (blessed)
            {
                Bless(who);
            }
            else
            {
                Curse(who);
            }
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Chaos effect failed: {e.Message}");
        }
    }

    private static void Bless(string who) => Visit(who, PlagueTable.Mercies, blessed: true);

    private static void Curse(string who) => Visit(who, PlagueTable.Curses, blessed: false);

    // Tries entries at random until one reports it did something. A famine with no followers or
    // locusts with nothing planted returns null, and spending the death on it would look broken.
    private static void Visit(string who, IReadOnlyList<Plague> table, bool blessed)
    {
        var inCrusade = GameManager.IsDungeon(PlayerFarming.Location);

        foreach (var plague in table.Where(p => inCrusade || !p.CrusadeOnly)
                                    .OrderBy(_ => UnityEngine.Random.value))
        {
            var line = plague.Apply();
            if (line == null)
            {
                continue;
            }

            Log.LogInfo($"[AP] Chaos: {(blessed ? "blessed" : "cursed")} - {plague.Name}. {line}.");
            ApNotification.Show(
                $"{who} died, and {line}.",
                blessed ? NotificationBase.Flair.Positive : NotificationBase.Flair.Negative,
                blessed ? ApColors.Green : ApColors.Red);
            return;
        }

        Log.LogInfo($"[AP] Chaos: nothing in the {(blessed ? "mercy" : "plague")} table could act "
            + "here, so this death passes.");
    }

    // VFX_Dice decides its own outcome from the first Random.Range(0, 10) it draws
    // (VFX_Dice.cs:52-57), so seeding Unity's generator with a state whose next draw lands on the
    // wanted side makes the die show the result we already chose. Rewound rather than restored
    // afterwards: the die draws again for the spin, and letting that run from the same state is
    // harmless.
    private static void SteerNextRoll(bool merciful, PlayerFarming player)
    {
        // Rabbit's Foot adds 2 before the >= 5 test, so the threshold moves with it
        var threshold = TrinketManager.HasTrinket(TarotCards.Card.RabbitFoot, player) ? 3 : 5;
        var saved = UnityEngine.Random.state;

        for (var seed = 0; seed < 2000; seed++)
        {
            UnityEngine.Random.InitState(seed);
            if (UnityEngine.Random.Range(0, 10) >= threshold == merciful)
            {
                UnityEngine.Random.InitState(seed);
                return;
            }
        }

        // Nothing found, so let it roll on its own rather than leaving the generator seeded
        UnityEngine.Random.state = saved;
        Log.LogWarning("[AP] Chaos: could not steer the die, falling back to its own odds.");
    }

    // Puts the depth of field back where the game keeps it. DepthOfFieldTween stops any routine
    // already running (BiomeConstants.cs), so this cancels the die's rather than fighting it.
    // Same values GameManager.OnConversationEnd uses, with the far plane it picks per location.
    private static void ClearDiceBlur()
    {
        try
        {
            var far = GameManager.IsDungeon(PlayerFarming.Location) ? 0f : 200f;
            BiomeConstants.Instance?.DepthOfFieldTween(0.15f, 8.7f, 26f, 1f, far);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Chaos: could not clear the dice blur: {e.Message}");
        }
    }

    /// <summary>
    /// Spawns the Fifty Fifty Gamble die on the player and reports which way it landed.
    /// </summary>
    /// <returns>False if the die could not be spawned, so the caller can decide for itself.</returns>
    private static bool TryRollDice(bool merciful, Action<bool> rolled)
    {
        try
        {
            var player = PlayerFarming.Instance;
            if (player == null)
            {
                return false;
            }

            SteerNextRoll(merciful, player);

            var relic = EquipmentManager.GetRelicData(RelicType.FiftyFiftyGamble);
            var sequence = relic?.VFXData?.PlayNewSequence(
                player.transform, new[] { player.transform });

            var dice = sequence?.ImpactVFXObjects?
                .Select(o => o == null ? null : o.GetComponent<VFX_Dice>())
                .FirstOrDefault(d => d != null);

            if (dice == null)
            {
                return false;
            }

            // No PlayVFX here: the VFXSequence constructor already played every impact object
            // (VFXSequence.cs:63), so the die is mid-roll by now. Calling it again rolled a
            // second time, which showed up as two effects per death that could disagree.
            // Unsubscribes itself. The die is pooled, so the same VFX_Dice comes back with the
            // previous subscription still attached, and every roll fired every handler ever
            // added: one effect, then two, then three.
            Action<bool> handler = null;
            handler = blessed =>
            {
                dice.OnDiceRolled -= handler;
                rolled(blessed);
            };

            dice.OnDiceRolled += handler;

            // Cancel the focus pull the die just started. In vanilla the relic moves the camera
            // onto the player at the same time, so the blur has a subject; we spawn the die
            // without that, leaving the whole screen soft and nothing in focus.
            ClearDiceBlur();
            return true;
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Chaos: could not roll the die, deciding without it: {e.Message}");
            return false;
        }
    }
}
