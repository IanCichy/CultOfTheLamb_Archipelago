using Archipelago.CultOfTheLamb.UI;
using HarmonyLib;
using Lamb.UI;
using TMPro;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Lets the sermon tree be dismissed, but only the copy SermonTreeViewer opened.
///
/// UIUpgradePlayerTreeMenuController.OnCancelButtonInput is an empty override on purpose. The
/// vanilla flows that open this tree hand out a reward and must not be escaped. Restoring cancel
/// for everyone would let a player back out of the Hearts of the Faithful ritual and forfeit it.
/// It could also soft-lock the game waiting for a pick that can no longer happen, since
/// RitualFlockOfTheFaithful blocks on the menu closing. So the scope is the viewer instance
/// itself, by reference, rather than an "AP is browsing" flag. A flag left set by an exception or
/// a scene change would reopen exactly that hole, while a stale instance reference simply never
/// matches again.
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

    private static readonly AccessTools.FieldRef<UpgradeTreeNodeInfoCard, TextMeshProUGUI> CardName =
        AccessTools.FieldRefAccess<UpgradeTreeNodeInfoCard, TextMeshProUGUI>("_nodeNameText");

    /// <summary>
    /// Adds the upgrade's description under its name on the focused node.
    /// </summary>
    /// <remarks>
    /// The card is the label the player actually sees. UpgradeTreeNodeInfoCard.Configure writes
    /// GetLocalizedName into it on every selection. UpgradeTreeNode's own `_title` is a different
    /// object that isn't what renders here, which is why setting that one changed nothing.
    ///
    /// A postfix rather than pre-setting the text, because Configure re-writes it on every
    /// selection, so appending afterwards is the only thing that survives. Vanilla puts the
    /// description in the hold-to-confirm overlay, which a read-only viewer never opens, so
    /// without this there is nowhere to learn what an upgrade does.
    /// </remarks>
    [HarmonyPatch(typeof(UpgradeTreeNodeInfoCard), nameof(UpgradeTreeNodeInfoCard.Configure))]
    [HarmonyPostfix]
    private static void InfoCard_Configure_Postfix(UpgradeTreeNodeInfoCard __instance, UpgradeTreeNode node)
    {
        // Only our viewer's card. The same component serves the Divine Inspiration and Woolhaven
        // trees, which are the game's own and should read as the game wrote them.
        if (SermonTreeViewer.Viewer == null || node == null) return;
        if (__instance.GetComponentInParent<UIUpgradePlayerTreeMenuController>() != SermonTreeViewer.Viewer)
        {
            return;
        }

        var label = CardName(__instance);
        if (label == null) return;

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

        if (string.IsNullOrEmpty(description)) return;

        // The card is authored for a single short name, so a description needs somewhere to go.
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;

        label.text = $"{label.text}\n<size=75%>{description}</size>";
    }
}
