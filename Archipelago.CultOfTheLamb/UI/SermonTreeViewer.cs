using System;
using HarmonyLib;
using I2.Loc;
using Lamb.UI;
using src.Extensions;
using TMPro;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Opens the game's own sermon upgrade tree as a read-only viewer.
///
/// Randomizing sermons costs the player the only routine way to see this tree: SermonUpgradePatch
/// replaces SermonController.PlayerUpgrade, which was the sole call site of
/// UIManager.ShowPlayerUpgradeTree. This gives it back, with the real art, connectors and - the
/// reason it isn't an IMGUI panel - the game's own controller cursor.
///
/// We instantiate the prefab rather than calling ShowPlayerUpgradeTree, and that one decision
/// removes most of the danger: ShowPlayerUpgradeTree is what sets `revealType` on a Seasons save,
/// and OnShowCompleted unlocks an upgrade for free when it's set. Going through the inherited
/// Show(bool) leaves revealType at Count, so that branch is unreachable with no patch at all.
/// </summary>
internal static class SermonTreeViewer
{
    /// <summary>
    /// Our instance, while it's up. The cancel patch compares against this by reference so it can
    /// never touch a tree the game opened for its own reasons.
    /// </summary>
    internal static UIUpgradePlayerTreeMenuController Viewer { get; private set; }

    /// <summary>
    /// False at the main menu and anywhere UIManager has released the base assets, which is why
    /// the IMGUI fallback panel still exists.
    /// </summary>
    internal static bool IsAvailable =>
        MonoSingleton<UIManager>.Instance != null
        && MonoSingleton<UIManager>.Instance.UpgradePlayerTreeMenuTemplate != null;

    /// <summary>
    /// <paramref name="onClosed"/> runs when the viewer is dismissed - and also when it can't be
    /// opened at all, so a caller that hid itself to make room always gets to come back.
    /// </summary>
    internal static void Open(Action onClosed)
    {
        if (Viewer != null)
        {
            Log.LogWarning("[AP] The sermon tree viewer is already open.");

            // Still hand back control. A caller that hid itself to make room - the altar entry -
            // has no other way to come back, and leaving it hidden strands the player in a paused
            // scene with no menu.
            onClosed?.Invoke();
            return;
        }

        if (!IsAvailable)
        {
            Log.LogWarning("[AP] The sermon tree prefab isn't loaded - can't open the viewer here.");
            UIManager.PlayAudio("event:/ui/negative_feedback");
            onClosed?.Invoke();
            return;
        }

        UIUpgradePlayerTreeMenuController viewer;
        try
        {
            viewer = MonoSingleton<UIManager>.Instance.UpgradePlayerTreeMenuTemplate
                .Instantiate<UIUpgradePlayerTreeMenuController>();
        }
        catch (Exception e)
        {
            Log.LogError($"[AP] Could not open the sermon tree viewer: {e}");
            onClosed?.Invoke();
            return;
        }

        // Before anything that can raise a callback: Awake has already run inside Instantiate, and
        // the cancel patch needs this set the moment the menu can receive input.
        Viewer = viewer;
        loggedLabel = false;

        Neuter(viewer);
        HideTierDividers(viewer);
        AddDescriptions(viewer);

        // Awake deactivates this so the vanilla flow can't be escaped. We're read-only, so the
        // player must be able to leave.
        if (viewer.disableBackPrompt != null) viewer.disableBackPrompt.SetActive(true);

        // OnShowStarted re-subscribes every node's confirm handler, after the setup above - so do
        // it again once the menu is up. The descriptions are re-applied for a related reason:
        // Destroy is deferred to end of frame, so a node's Localize can still rewrite the label
        // from its own term when the node activates during the show.
        viewer.OnShow += () =>
        {
            Neuter(viewer);
            AddDescriptions(viewer);
        };

        // OnHidden rather than OnCancel: the subclass's empty OnCancelButtonInput never sets
        // _didCancel, so OnCancel never fires. Hide always reaches this.
        viewer.OnHidden += () =>
        {
            Viewer = null;
            onClosed?.Invoke();
        };

        // The inherited Show(bool). Never Show(UpgradeSystem.Type) - see the class comment.
        viewer.Show(false);

        // Gives us the vanilla pause and timescale restore. A no-op if something else is still the
        // current menu, which is why callers hide themselves first.
        MonoSingleton<UIManager>.Instance.SetMenuInstance(viewer, 0f, false);
    }

    /// <summary>
    /// Makes every node unclickable.
    ///
    /// This is not optional. UIPlayerUpgradeUnlockOverlayController.IsAvailable returns true
    /// unconditionally, so unlike the Divine Inspiration tree the sermon tree has no currency gate
    /// at all - opening it and holding confirm unlocks any available node for free, which would
    /// hand out an upgrade the randomizer never placed.
    /// </summary>
    private static void Neuter(UIUpgradePlayerTreeMenuController viewer)
    {
        foreach (var node in viewer.GetComponentsInChildren<UpgradeTreeNode>(includeInactive: true))
        {
            if (node == null) continue;

            // The real block: the only route from the node's onClick to DoUnlock, and it also
            // covers Button.OnSubmit, which MMButton doesn't route through Confirmable.
            node.OnUpgradeNodeSelected = null;

            // Makes it read as a viewer rather than a dead button - both the pad and the mouse
            // confirm paths go through MMButton.TryPerformConfirmAction, which this denies.
            //
            // Deliberately not Interactable: UpgradeMenuCursor only considers interactable nodes
            // and dereferences the nearest one unguarded, so a fully non-interactable tree throws
            // on the first stick flick.
            if (node.Button != null) node.Button.Confirmable = false;
        }
    }

    private static readonly AccessTools.FieldRef<UpgradeTreeNode, TextMeshProUGUI> NodeTitle =
        AccessTools.FieldRefAccess<UpgradeTreeNode, TextMeshProUGUI>("_title");

    private static readonly AccessTools.FieldRef<UpgradeTreeNode, Localize> NodeLocalize =
        AccessTools.FieldRefAccess<UpgradeTreeNode, Localize>("_localize");

    /// <summary>
    /// Puts each upgrade's description under its name.
    ///
    /// Vanilla only ever shows the name here - the description lives in the hold-to-confirm overlay,
    /// which a read-only viewer never opens. Since the label is only visible on the focused node,
    /// folding the description into it costs no space and answers "what does this actually do".
    ///
    /// The Localize component has to go first: it rewrites the text from
    /// "UpgradeSystem/{upgrade}/Name" on enable and on any language change, so setting .text alone
    /// reverts. Same reason MenuButtonPatch.SetLabel destroys it.
    /// </summary>
    private static void AddDescriptions(UIUpgradePlayerTreeMenuController viewer)
    {
        foreach (var node in viewer.GetComponentsInChildren<UpgradeTreeNode>(includeInactive: true))
        {
            if (node == null) continue;

            var title = NodeTitle(node);
            if (title == null) continue;

            string name, description;
            try
            {
                name = UpgradeSystem.GetLocalizedName(node.Upgrade);
                description = UpgradeSystem.GetLocalizedDescription(node.Upgrade);
            }
            catch (Exception)
            {
                // I2 throws for a term it doesn't have; the vanilla label is still fine.
                continue;
            }

            if (string.IsNullOrEmpty(description)) continue;

            var localize = NodeLocalize(node);
            if (localize != null) UnityEngine.Object.Destroy(localize);

            // The label is authored as a single line for a short name, so a description would be
            // truncated away invisibly - which looks exactly like the text never being set.
            title.enableWordWrapping = true;
            title.overflowMode = TextOverflowModes.Overflow;

            var rect = title.rectTransform;
            if (rect != null && rect.sizeDelta.x < MinLabelWidth)
            {
                rect.sizeDelta = new Vector2(MinLabelWidth, MinLabelHeight);
            }

            title.text = $"{name}\n<size=80%>{description}</size>";

            if (!loggedLabel)
            {
                loggedLabel = true;
                Log.LogInfo($"[AP] Sermon label sample: rect {rect?.sizeDelta}, "
                    + $"overflow {title.overflowMode}, wrap {title.enableWordWrapping}, "
                    + $"text '{title.text.Replace("\n", " | ")}'");
            }
        }
    }

    // The authored label is sized for a name; a description needs room to wrap into.
    private const float MinLabelWidth = 340f;
    private const float MinLabelHeight = 150f;

    /// <summary>One sample line per open, so the log shows whether the text landed without spamming.</summary>
    private static bool loggedLabel;

    /// <summary>
    /// Drops the "N / M" tier rules across the tree.
    ///
    /// They describe spending upgrades to open the next row, which can't happen here - Archipelago
    /// grants sermon upgrades outright and ignores tiers entirely, so the thresholds are a
    /// progression the player can't act on. Same reasoning as DivineInspirationTierReveal.
    ///
    /// Done on our instance rather than by patching NumRequiredNodesForTier, because that reader
    /// also decides node state for the *real* sermon tree the Flock ritual and sermon rewards open.
    /// </summary>
    private static void HideTierDividers(UIUpgradePlayerTreeMenuController viewer)
    {
        foreach (var divider in viewer.GetComponentsInChildren<TierLockIcon>(includeInactive: true))
        {
            if (divider != null) divider.gameObject.SetActive(false);
        }
    }
}
