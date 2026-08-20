using Archipelago.CultOfTheLamb.UI;
using HarmonyLib;
using Lamb.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Lets the sermon tree be dismissed - but only the copy SermonTreeViewer opened.
///
/// UIUpgradePlayerTreeMenuController.OnCancelButtonInput is an empty override, deliberately: the
/// vanilla flows that open this tree hand out a reward and must not be escaped. Restoring cancel
/// for everyone would let a player back out of the Hearts of the Faithful ritual and forfeit it,
/// or - since RitualFlockOfTheFaithful blocks on the menu closing - soft-lock waiting for a pick
/// that can no longer happen.
///
/// So the scope is the viewer instance itself, by reference, rather than an "AP is browsing" flag.
/// A flag left set by an exception or a scene change would reopen exactly the hole above; a stale
/// instance reference simply never matches again.
/// </summary>
[HarmonyPatch(typeof(UIUpgradePlayerTreeMenuController))]
internal static class SermonTreeViewerPatch
{
    [HarmonyPatch(nameof(UIUpgradePlayerTreeMenuController.OnCancelButtonInput))]
    [HarmonyPrefix]
    private static bool OnCancelButtonInput_Prefix(UIUpgradePlayerTreeMenuController __instance)
    {
        if (!ReferenceEquals(__instance, SermonTreeViewer.Viewer)) return true;

        // The same guards the base class uses. Cancelling mid-fade would call Hide before the show
        // coroutine finishes and leave the menu up with navigation still locked.
        if (__instance.IsShowing || __instance.IsHiding) return false;
        if (__instance.CanvasGroup == null || !__instance.CanvasGroup.interactable) return false;

        __instance.Hide(false);
        return false;
    }
}
