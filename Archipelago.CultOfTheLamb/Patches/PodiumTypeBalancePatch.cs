using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Stops a crusade room asking for two podiums of a kind the seed can only fill once
/// </summary>
/// <remarks>
/// Interaction_WeaponChoice rerolls up to 50 times until its offer differs from the other
/// podium. With one granted curse that never happens, and the fallback leaves a weapon in a
/// podium still marked as a curse. That podium shows 0 damage and 0 speed and equips into the
/// curse slot.
///
/// Weapons and curses share one TypeOfWeapon field, so they can never match. A room with one of
/// each always passes on the first try, so we set that mix before anything rolls.
///
/// Only choice rooms and chests compare against neighbours, so the crusade's opening podiums are
/// left alone. So is the Cursed Crusade fleece, which turns every podium into a curse whatever we
/// set. The room just loses a pedestal.
/// </remarks>
[HarmonyPatch(typeof(Interaction_WeaponSelectionPodium),
    nameof(Interaction_WeaponSelectionPodium.OnEnableInteraction))]
internal static class PodiumTypeBalancePatch
{
    // The podiums Interaction_WeaponChoice compares against. Set on the prefab
    private static readonly AccessTools.FieldRef<Interaction_WeaponSelectionPodium,
        Interaction_WeaponSelectionPodium[]> OtherOptions =
        AccessTools.FieldRefAccess<Interaction_WeaponSelectionPodium,
            Interaction_WeaponSelectionPodium[]>("otherWeaponOptions");

    // Chests use their own static list of every chest instead
    private static readonly FieldInfo ChestOptions =
        AccessTools.Field(typeof(Interaction_WeaponChoiceChest), "otherWeaponOptions");

    // Only warn once about a room we can't fill
    private static bool warnedUnfillable;

    [HarmonyPostfix]
    private static void Postfix(Interaction_WeaponSelectionPodium __instance)
    {
        try
        {
            Decide(__instance);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not balance the podiums in this room: {e.Message}");
        }
    }

    // Runs for every podium and decides for the whole room, setting a neighbour's Type before its
    // own coin flip. Has to run each time, since only the last podium sees every Type resolved
    private static void Decide(Interaction_WeaponSelectionPodium self)
    {
        if (EquipmentPoolPatch.WeaponOfferCount == null
            && EquipmentPoolPatch.CurseOfferCount == null)
        {
            return;
        }

        if (!(self is Interaction_WeaponChoice) && !(self is Interaction_WeaponChoiceChest))
        {
            return;
        }

        // The game forces Weapon here afterwards anyway
        if (PlayerFarming.Location == FollowerLocation.IntroDungeon)
        {
            return;
        }

        if (DataManager.Instance == null)
        {
            return;
        }

        var members = Members(self);
        if (members.Count < 2)
        {
            return;
        }

        var weapons = EquipmentPoolPatch.WeaponOfferCount?.Invoke() ?? int.MaxValue;
        var curses = EquipmentPoolPatch.CurseOfferCount?.Invoke() ?? int.MaxValue;

        // Curse podiums get deleted while curses are locked
        if (!DataManager.Instance.EnabledSpells)
        {
            curses = 0;
        }

        var onWeapons = Seated(members, Interaction_WeaponSelectionPodium.Types.Weapon);
        var onCurses = Seated(members, Interaction_WeaponSelectionPodium.Types.Curse);

#if AP_DEBUG_KEYS
        // Every podium in the room prints this, so debug builds only
        Log.LogInfo($"[AP] Crusade room: {members.Count} podium(s), the game wants {onWeapons} "
            + $"on weapons and {onCurses} on curses. This seed can offer {Describe(weapons)} "
            + $"weapon and {Describe(curses)} curse choices.");
#endif

        // Weapons can fill as many podiums as there are different weapons, and the same for curses
        if (onWeapons <= weapons && onCurses <= curses)
        {
            return;
        }

        Rebalance(members, weapons, curses);
    }

    // Gives out weapon and curse podiums only as far as each can be filled. A podium that already
    // rolled stays as it is. Ties go to Weapon, since weapon podiums are never deleted
    private static void Rebalance(
        List<Interaction_WeaponSelectionPodium> members,
        int weapons,
        int curses)
    {
        var weaponRoom = weapons;
        var curseRoom = curses;

        foreach (var podium in members.Where(Rolled))
        {
            if (podium.Type == Interaction_WeaponSelectionPodium.Types.Curse)
            {
                curseRoom--;
            }
            else
            {
                weaponRoom--;
            }
        }

        foreach (var podium in members.Where(p => !Rolled(p)))
        {
            if (podium.Type == Interaction_WeaponSelectionPodium.Types.Weapon && weaponRoom > 0)
            {
                weaponRoom--;
            }
            else if (podium.Type == Interaction_WeaponSelectionPodium.Types.Curse && curseRoom > 0)
            {
                curseRoom--;
            }
            else if (weaponRoom > 0)
            {
                weaponRoom--;
                Assign(podium, Interaction_WeaponSelectionPodium.Types.Weapon);
            }
            else if (curseRoom > 0)
            {
                curseRoom--;
                Assign(podium, Interaction_WeaponSelectionPodium.Types.Curse);
            }
            else if (!warnedUnfillable)
            {
                warnedUnfillable = true;
                Log.LogWarning($"[AP] This room wants {members.Count} podiums but the seed can "
                    + $"only offer {Describe(weapons)} weapon and {Describe(curses)} curse "
                    + "choices, so one of them may still come out wrong. Logged once.");
            }
        }
    }

    // Every podium competing with this one for a different offer
    private static List<Interaction_WeaponSelectionPodium> Members(
        Interaction_WeaponSelectionPodium self)
    {
        var found = new List<Interaction_WeaponSelectionPodium> { self };

        if (self is Interaction_WeaponChoiceChest)
        {
            // This list has every chest awake anywhere, so keep the ones still to roll
            if (ChestOptions?.GetValue(null) is List<Interaction_WeaponSelectionPodium> chests)
            {
                found.AddRange(chests.Where(p => p != null && p != self
                    && p.gameObject.activeInHierarchy && !Rolled(p)));
            }
        }
        else
        {
            var others = OtherOptions(self);
            if (others != null)
            {
                found.AddRange(others.Where(p => p != null && p != self));
            }
        }

        // Relics use a different table, and the legendary plinth has no curse path
        return found
            .Where(p => p.Type != Interaction_WeaponSelectionPodium.Types.Relic
                && p.TypeOfRelic == RelicType.None
                && !(p is Interaction_LegendaryWeaponSelectionPodium))
            .OrderBy(p => p.GetInstanceID())
            .ToList();
    }

    // Whether the podium has already picked its offer
    private static bool Rolled(Interaction_WeaponSelectionPodium podium)
        => podium.TypeOfWeapon != EquipmentType.None;

    private static int Seated(
        List<Interaction_WeaponSelectionPodium> members,
        Interaction_WeaponSelectionPodium.Types side)
        => members.Count(p => p.Type == side);

    private static void Assign(
        Interaction_WeaponSelectionPodium podium,
        Interaction_WeaponSelectionPodium.Types side)
    {
        if (side == Interaction_WeaponSelectionPodium.Types.Curse
            && !DataManager.Instance.EnabledSpells)
        {
            return;
        }

        if (podium.Type == side)
        {
            return;
        }

        var was = podium.Type;
        podium.Type = side;

        Log.LogInfo($"[AP] Podium '{podium.name}' switched from {was} to {side}, so the room "
            + "asks for one of each instead of two the seed cannot tell apart.");
    }

    private static string Describe(int howMany)
        => howMany == int.MaxValue ? "any number of" : $"{howMany}";
}
