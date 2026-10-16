using System;
using HarmonyLib;
using Lamb.UI.Assets;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// Controls how Divine Inspiration points are earned. Patches the AbilityPoints setter rather
/// than where PlayerFarming adds a point, since the setter catches every path.
internal static class DivineInspirationPatch
{
    // Filling the meter is what earns the check, whether or not the point is kept.
    internal static Action PointEarned;

    // True while Archipelago owns the point supply, in every granting mode.
    internal static bool WithholdPoints;

    // Without this, a point from the multiworld would pay out a check and double-count.
    private static bool granting;

    private static bool loggedTutorialPoint;

    internal static int CheckCount;

    // Withholding is otherwise silent, so there would be no way to tell it from a bug.
    private static bool loggedFirstWithhold;

    internal static void ResetWithholdLog()
    {
        loggedFirstWithhold = false;
    }

    internal static int DevotionCap;

    // The tutorial needs a Temple ritual to end and the Temple costs a point, so one must get
    // through. Gated on the Temple, not CollectDivineInspiration, which any upgrade completes.
    private static bool TutorialNeedsAPoint =>
        TutorialState.InTutorial
        && UpgradeSystem.AbilityPoints < 1
        && !UpgradeSystem.GetUnlocked(UpgradeSystem.Type.Building_Temple);

    internal static void GrantPoints(int count)
    {
        if (count <= 0)
        {
            return;
        }

        granting = true;
        try
        {
            UpgradeSystem.AbilityPoints += count;
        }
        finally
        {
            granting = false;
        }
    }

    /// Reports the meter fill, then swallows the point if this seed withholds. Decreases always
    /// pass through, or the player could not spend granted points.
    [HarmonyPatch(typeof(UpgradeSystem), nameof(UpgradeSystem.AbilityPoints),
        MethodType.Setter)]
    internal static class AbilityPointSetter
    {
        [HarmonyPrefix]
        private static bool Prefix(int value)
        {
            if (granting)
            {
                return true;
            }

            var current = UpgradeSystem.AbilityPoints;
            if (value <= current)
            {
                return true;
            }

            PointEarned?.Invoke();

            if (!WithholdPoints)
            {
                return true;
            }

            if (TutorialNeedsAPoint)
            {
                if (!loggedTutorialPoint)
                {
                    loggedTutorialPoint = true;
                    Log.LogInfo("[AP] Letting one Divine Inspiration point through: the tutorial "
                        + "needs an upgrade unlocked to continue. Withholding resumes after that.");
                }

                return true;
            }

            if (!loggedFirstWithhold)
            {
                loggedFirstWithhold = true;
                Log.LogInfo($"[AP] Withheld a Divine Inspiration ability point ({current} -> "
                    + $"{value} suppressed). Points come from the multiworld this seed; this is "
                    + "logged once per session.");
            }

            return false;
        }
    }

    /// Caps what the next point costs. Vanilla climbs to 465, roughly 24,000 Devotion for the
    /// whole tree, so capping only the top leaves the early curve alone.
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.GetTargetXP))]
    internal static class DevotionCost
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            if (DevotionCap > 0)
            {
                __result = Mathf.Min(__result, DevotionCap);
            }
        }
    }


    // True while the player still has DI related checks to send
    internal static bool ChecksRemain
    {
        get
        {
            var data = DataManager.Instance;
            return CheckCount > 0 && data != null && data.Level < CheckCount;
        }
    }

    /// Keeps the meter fillable while Archipelago still has checks to earn, since the multiworld
    /// can finish the tree before you have sent them all. Not while a point is held, or the
    /// Shrine opens the tree instead of paying out (BuildingShrine.cs:444-456).
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.HasUnlockAvailable))]
    internal static class KeepMeterFilling
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (__result || !ChecksRemain || UpgradeSystem.AbilityPoints > 0)
            {
                return;
            }

            __result = true;
        }
    }

    internal static UpgradeTreeConfiguration Tree =>
        GameManager.GetInstance()?.UpgradeTreeConfiguration;
}
