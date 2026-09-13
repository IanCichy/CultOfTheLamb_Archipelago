using Archipelago.CultOfTheLamb.UI;
using HarmonyLib;
using Lamb.UI;
using TMPro;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Lets the player close the sermon tree, but only the copy SermonTreeViewer opened
/// </summary>
/// <remarks>
/// The game blocks cancel on this menu on purpose. Its normal uses hand out a reward, and backing
/// out of Hearts of the Faithful would lose it or leave the ritual waiting forever.
///
/// So this checks for our exact menu object rather than an "AP is browsing" flag. A flag left on
/// after an error would reopen that hole, but an old object reference never matches again.
/// </remarks>
[HarmonyPatch(typeof(UIUpgradePlayerTreeMenuController))]
internal static class SermonTreeViewerPatch
{
    [HarmonyPatch(nameof(UIUpgradePlayerTreeMenuController.OnCancelButtonInput))]
    [HarmonyPrefix]
    private static bool OnCancelButtonInput_Prefix(UIUpgradePlayerTreeMenuController __instance)
    {
        if (!ReferenceEquals(__instance, SermonTreeViewer.Viewer))
        {
            return true;
        }

        // The same guards the base class uses. Cancelling mid-fade would call Hide before the show
        // coroutine finishes and leave the menu up with navigation still locked.
        if (__instance.IsShowing || __instance.IsHiding)
        {
            return false;
        }

        if (__instance.CanvasGroup == null || !__instance.CanvasGroup.interactable)
        {
            return false;
        }

        __instance.Hide(false);
        return false;
    }

    private static readonly AccessTools.FieldRef<UpgradeTreeNodeInfoCard, TextMeshProUGUI> CardName =
        AccessTools.FieldRefAccess<UpgradeTreeNodeInfoCard, TextMeshProUGUI>("_nodeNameText");

    /// <summary>
    /// Adds the upgrade's description under its name on the selected node
    /// </summary>
    /// <remarks>
    /// The info card is the label you actually see. UpgradeTreeNode has its own _title, but that
    /// isn't what's drawn here.
    ///
    /// A postfix, because Configure rewrites the text on every selection. The game normally shows
    /// the description in the hold-to-confirm popup, which a read-only viewer never opens.
    /// </remarks>
    [HarmonyPatch(typeof(UpgradeTreeNodeInfoCard), nameof(UpgradeTreeNodeInfoCard.Configure))]
    [HarmonyPostfix]
    private static void InfoCard_Configure_Postfix(UpgradeTreeNodeInfoCard __instance, UpgradeTreeNode node)
    {
        // Only our viewer's card. The same component serves the Divine Inspiration and Woolhaven
        // trees, which are the game's own and should read as the game wrote them.
        if (SermonTreeViewer.Viewer == null || node == null)
        {
            return;
        }

        if (__instance.GetComponentInParent<UIUpgradePlayerTreeMenuController>() != SermonTreeViewer.Viewer)
        {
            return;
        }

        var label = CardName(__instance);
        if (label == null)
        {
            return;
        }

        string description;
        try
        {
            description = UpgradeSystem.GetLocalizedDescription(node.Upgrade);
        }
        catch (System.Exception)
        {
            // I2 throws for a term it doesn't have. The name alone is still correct.
            return;
        }

        if (string.IsNullOrEmpty(description))
        {
            return;
        }

        // The card is authored for a single short name, so a description needs somewhere to go.
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;

        label.text = $"{label.text}\n<size=75%>{description}</size>";
    }
}
