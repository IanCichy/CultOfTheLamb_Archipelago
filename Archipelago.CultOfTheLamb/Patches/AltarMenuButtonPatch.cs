using System;
using Archipelago.CultOfTheLamb.UI;
using HarmonyLib;
using Lamb.UI;
using Lamb.UI.AltarMenu;
using TMPro;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Adds an Archipelago entry to the Temple Altar menu that opens the sermon tree viewer.
/// </summary>
/// <remarks>
/// The altar is where a player already goes to look at what the Crown has given them, so the
/// sermon tree belongs here rather than behind a debug key or only in the pause menu.
/// </remarks>
[HarmonyPatch(typeof(UIAltarMenuController))]
internal static class AltarMenuButtonPatch
{
    private const string ButtonName = "ArchipelagoSermonsButton";

    /// <summary>
    /// The entry's label, abbreviated to fit the button.
    /// </summary>
    /// <remarks>
    /// The label inherits its width from the cloned button, and the game's own labels top out at
    /// eight characters, so TMP wrapped "Archipelago" mid word. Widening isn't an option either:
    /// the icons sit about 130px apart and the full word needs closer to 190, which overlaps
    /// Crown and Rituals.
    /// </remarks>
    private const string ButtonLabel = "AP";

    private const string ButtonDescription = "View the sermon upgrades Archipelago has granted.";

    // Which entry OnShowStarted re-focuses on. 1 is Player Upgrades, our neighbour
    private const int PlayerUpgradesIndex = 1;

    private static readonly AccessTools.FieldRef<UIAltarMenuController, MMButton> PlayerUpgradesButton =
        AccessTools.FieldRefAccess<UIAltarMenuController, MMButton>("_playerUpgradesButton");

    private static readonly AccessTools.FieldRef<UIAltarMenuController, TextMeshProUGUI> Description =
        AccessTools.FieldRefAccess<UIAltarMenuController, TextMeshProUGUI>("_description");

    private static readonly AccessTools.FieldRef<int> DefaultIndex =
        AccessTools.StaticFieldRefAccess<int>(
            AccessTools.Field(typeof(UIAltarMenuController), "_defaultIndex"));

    // Start is where every vanilla button gets its onClick and OnSelected
    [HarmonyPatch(nameof(UIAltarMenuController.Start))]
    [HarmonyPostfix]
    private static void Start_Postfix(UIAltarMenuController __instance)
    {
        // A throw in a Start postfix leaves the menu half-wired, which is far worse than a
        // missing button. Same reasoning as MenuButtonPatch.TryAddButton.
        try
        {
            AddButton(__instance);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not add the Archipelago entry to the altar menu: {e}");
        }
    }

    private static void AddButton(UIAltarMenuController menu)
    {
        // No dead entry when the tree prefab isn't loaded, since the viewer would have nothing
        // to show.
        if (!SermonTreeViewer.IsAvailable)
        {
            return;
        }

        var donor = PlayerUpgradesButton(menu);
        if (donor == null)
        {
            return;
        }

        var parent = donor.transform.parent;
        if (parent == null)
        {
            return;
        }

        // Start can run again on a rebuilt menu, and a second entry would be worse than none.
        if (parent.Find(ButtonName) != null)
        {
            return;
        }

        var clone = UnityEngine.Object.Instantiate(donor.gameObject, parent);
        clone.name = ButtonName;
        clone.SetActive(true);
        clone.transform.SetSiblingIndex(donor.transform.GetSiblingIndex() + 1);

        var button = clone.GetComponent<MMButton>();
        if (button == null)
        {
            return;
        }

        // The clone carries the donor's listeners, which would open the player upgrades screen.
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnClicked(menu));

        // OnSelected is assigned at runtime rather than serialized, so the clone starts with none.
        button.OnSelected = () =>
        {
            var description = Description(menu);
            if (description != null)
            {
                description.text = ButtonDescription;
            }
        };

        MenuButtonPatch.SetLabel(clone, ButtonLabel);

        Log.LogInfo("[AP] Added the Archipelago entry to the Temple Altar menu.");
    }

    /// <summary>
    /// Set between the click and the viewer closing. Without it a second click before the menu
    /// finishes hiding would stack another OnHidden handler, and the extra Open call is refused,
    /// leaving a hide with nothing to reopen the altar.
    /// </summary>
    private static bool opening;

    /// <summary>
    /// Mirrors Interaction_TempleAltar.DoCultUpgrade. Hide the altar, open the next screen, and
    /// bring the altar back when it closes.
    /// </summary>
    private static void OnClicked(UIAltarMenuController menu)
    {
        if (opening)
        {
            return;
        }

        opening = true;

        var altar = Interaction_TempleAltar.Instance;

        // Wait for OnHidden instead of opening right away. The altar is still UIManager's current
        // menu until then, and SetMenuInstance quietly does nothing while it is.
        //
        // The handler removes itself, because OnHidden is never cleared and the altar menu gets
        // reused. Left attached, it would open the viewer on the next normal altar close too, and
        // since closing the viewer reopens the altar, the two would keep bouncing off each other.
        Action openViewer = null;
        openViewer = () =>
        {
            menu.OnHidden -= openViewer;
            SermonTreeViewer.Open(() => ReopenAltar(altar));
        };
        menu.OnHidden += openViewer;

        MonoSingleton<UIManager>.Instance.ForceBlockMenus = false;

        // Without this, OnInteract's `if (!Activated)` guard silently drops the reopen and leaves
        // the player stranded in a frozen scene.
        if (altar != null)
        {
            altar.Activated = false;
        }

        // Hide, not Cancel. Cancelling raises the altar's DoCancel, which unpauses the sim,
        // resets the camera and releases the followers. The world should stay held while we're
        // on top.
        menu.Hide(false);

        // A deliberate, unreverted write to vanilla static state. Every later altar open focuses
        // Player Upgrades, not just the one after ours. That's the intent, since our entry sits
        // directly below it, but it does outlive this interaction.
        DefaultIndex() = PlayerUpgradesIndex;
    }

    /// <summary>
    /// Reopens the altar one frame after the viewer closes, never from inside OnHidden
    /// </summary>
    /// <remarks>
    /// Two things still run after OnHidden. The tree's OnHideCompleted kills every tween, which
    /// would stop the altar's opening animation. And UIManager only clears its current menu in its
    /// own hide handler, which runs after ours, so reopening early does nothing and the game stays
    /// unpaused.
    /// </remarks>
    private static void ReopenAltar(Interaction_TempleAltar altar)
    {
        // Released here rather than in OpenAltarNow so every path out of this method clears it.
        // Missing one leaves the entry dead for the session, and worse, the altar is already
        // hidden by now with the world paused, so nothing would put the player back in control.
        opening = false;

        if (altar == null)
        {
            return;
        }

        var plugin = ArchipelagoPlugin.Instance;
        if (plugin == null)
        {
            OpenAltarNow(altar);
            return;
        }

        plugin.StartCoroutine(ReopenNextFrame(altar));
    }

    private static System.Collections.IEnumerator ReopenNextFrame(Interaction_TempleAltar altar)
    {
        yield return null;
        OpenAltarNow(altar);
    }

    private static void OpenAltarNow(Interaction_TempleAltar altar)
    {
        if (altar == null)
        {
            return;
        }

        var state = altar.state != null ? altar.state : PlayerFarming.Instance?.state;
        if (state == null)
        {
            Log.LogWarning("[AP] No state machine to reopen the altar with.");
            return;
        }

        altar.OnInteract(state);
    }
}
