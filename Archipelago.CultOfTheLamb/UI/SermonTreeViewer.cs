using System;
using HarmonyLib;
using I2.Loc;
using Lamb.UI;
using src.Extensions;
using TMPro;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// Opens the game's own sermon upgrade tree as a read-only viewer
/// </summary>
/// <remarks>
/// Randomizing sermons takes away the only normal way to see this tree, so this gives it back
/// with the game's real art and controller cursor.
///
/// It creates the prefab directly instead of calling ShowPlayerUpgradeTree. On a Seasons save that
/// call sets a reveal mode that unlocks an upgrade for free when the menu opens.
/// </remarks>
internal static class SermonTreeViewer
{
    // Our instance, while it's up. The cancel patch compares against this by reference so it can
    // never touch a tree the game opened for its own reasons
    internal static UIUpgradePlayerTreeMenuController Viewer { get; private set; }

    // False at the main menu and anywhere UIManager has released the base assets, which is why
    // the IMGUI fallback panel still exists
    internal static bool IsAvailable =>
        MonoSingleton<UIManager>.Instance != null
        && MonoSingleton<UIManager>.Instance.UpgradePlayerTreeMenuTemplate != null;

    // onClosed runs when the viewer is dismissed, and also when it can't be
    // opened at all, so a caller that hid itself to make room always gets to come back
    internal static void Open(Action onClosed)
    {
        if (Viewer != null)
        {
            Log.LogWarning("[AP] The sermon tree viewer is already open.");

            // Still hand back control. A caller that hid itself to make room, such as the altar
            // entry, has no other way to come back, and leaving it hidden strands the player in a
            // paused scene with no menu.
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

        // Before anything that can raise a callback. Awake has already run inside Instantiate,
        // and the cancel patch needs this set the moment the menu can receive input.
        Viewer = viewer;

        Neuter(viewer);
        HideTierDividers(viewer);

        // Awake deactivates this so the vanilla flow can't be escaped. We're read-only, so the
        // player must be able to leave.
        if (viewer.disableBackPrompt != null)
        {
            viewer.disableBackPrompt.SetActive(true);
        }

        // OnShowStarted re-subscribes every node's confirm handler, after the setup above, so do
        // it again once the menu is up.
        viewer.OnShow += () => Neuter(viewer);

        // OnHidden rather than OnCancel, because the subclass's empty OnCancelButtonInput never
        // sets _didCancel, so OnCancel never fires. Hide always reaches this.
        viewer.OnHidden += () =>
        {
            Viewer = null;
            onClosed?.Invoke();
        };

        // The inherited Show(bool). Never Show(UpgradeSystem.Type). See the class comment.
        viewer.Show(false);

        // Gives us the vanilla pause and timescale restore. A no-op if something else is still the
        // current menu, which is why callers hide themselves first.
        MonoSingleton<UIManager>.Instance.SetMenuInstance(viewer, 0f, false);
    }

    // Makes every node unclickable.
    //
    // This is required. UIPlayerUpgradeUnlockOverlayController.IsAvailable always returns true, so
    // unlike the Divine Inspiration tree, the sermon tree has no currency gate. Opening it and
    // holding confirm unlocks any available node for free, handing out an upgrade the multiworld
    // never sent
    private static void Neuter(UIUpgradePlayerTreeMenuController viewer)
    {
        foreach (var node in viewer.GetComponentsInChildren<UpgradeTreeNode>(includeInactive: true))
        {
            if (node == null)
            {
                continue;
            }

            // The real block. This is the only route from the node's onClick to DoUnlock, and it
            // also covers Button.OnSubmit, which MMButton doesn't route through Confirmable.
            node.OnUpgradeNodeSelected = null;

            // Makes it read as a viewer rather than a dead button. Both the pad and the mouse
            // confirm paths go through MMButton.TryPerformConfirmAction, which this denies.
            //
            // Not Interactable. UpgradeMenuCursor only considers interactable nodes
            // and dereferences the nearest one unguarded, so a fully non-interactable tree throws
            // on the first stick flick.
            if (node.Button != null)
            {
                node.Button.Confirmable = false;
            }
        }
    }


    // Drops the "N / M" tier rules across the tree.
    //
    // They describe spending upgrades to open the next row, which can't happen here. Archipelago
    // grants sermon upgrades outright and ignores tiers, so the thresholds are a progression the
    // player can't act on.
    //
    // Done on our instance rather than by patching NumRequiredNodesForTier, since that reader
    // also decides node state for the real sermon tree the Flock ritual opens
    private static void HideTierDividers(UIUpgradePlayerTreeMenuController viewer)
    {
        foreach (var divider in viewer.GetComponentsInChildren<TierLockIcon>(includeInactive: true))
        {
            if (divider != null)
            {
                divider.gameObject.SetActive(false);
            }
        }
    }
}
