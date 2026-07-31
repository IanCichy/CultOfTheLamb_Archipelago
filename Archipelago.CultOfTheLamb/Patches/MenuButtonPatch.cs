using System;
using HarmonyLib;
using I2.Loc;
using Lamb.UI.MainMenu;
using Lamb.UI.PauseMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Puts an "Archipelago" entry in the pause menu and the main menu.
///
/// Both menus wire their buttons the same way in their own Start() - a serialized MMButton per
/// entry, each given an onClick listener - so the cheapest correct way to add one is to clone a
/// button that already exists. A clone inherits the prefab's styling, layout and hover
/// behaviour, none of which we could reproduce by hand without an asset pipeline.
///
/// The pause menu is the one that matters: it's the only entry point guaranteed to be at a
/// loaded save, which is the only place connecting can actually finish (see the panel's
/// CanConnectHere). The main menu one is a convenience for entering details early.
/// </summary>
[HarmonyPatch]
internal static class MenuButtonPatch
{
    /// <summary>Raised when the player picks Archipelago from either menu.</summary>
    internal static event Action OnArchipelagoButtonPressed;

    private const string ButtonName = "ArchipelagoButton";
    private const string ButtonLabel = "Archipelago";

    private static readonly AccessTools.FieldRef<UIPauseMenuController, MMButton> TwitchButtonField =
        AccessTools.FieldRefAccess<UIPauseMenuController, MMButton>("_twitchSettingsButton");

    private static readonly AccessTools.FieldRef<MainMenu, Button> SettingsButtonField =
        AccessTools.FieldRefAccess<MainMenu, Button>("_settingsButton");

    /// <summary>
    /// Twitch Settings is the donor on purpose: it's the closest thing the game already has to
    /// what we're adding - a connection to an outside service - so it both looks right and sits
    /// in the right part of the list.
    /// </summary>
    [HarmonyPatch(typeof(UIPauseMenuController), "Start")]
    [HarmonyPostfix]
    private static void PauseMenu_Start_Postfix(UIPauseMenuController __instance)
    {
        TryAddButton(TwitchButtonField(__instance), "pause menu");
    }

    [HarmonyPatch(typeof(MainMenu), "Start")]
    [HarmonyPostfix]
    private static void MainMenu_Start_Postfix(MainMenu __instance)
    {
        TryAddButton(SettingsButtonField(__instance), "main menu");
    }

    /// <summary>
    /// Never lets a UI failure take a menu down with it: a thrown exception in a Start postfix
    /// leaves the menu half-initialised, which is a far worse outcome than a missing button.
    /// </summary>
    private static void TryAddButton(Selectable donor, string where)
    {
        try
        {
            AddButton(donor, where);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not add the Archipelago button to the {where}: {e}");
        }
    }

    private static void AddButton(Selectable donor, string where)
    {
        if (donor == null)
        {
            Log.LogWarning($"[AP] No donor button found for the {where} - skipping.");
            return;
        }

        var parent = donor.transform.parent;
        if (parent == null) return;

        // Start can run again on a menu that was rebuilt; a second button would be worse than
        // none.
        if (parent.Find(ButtonName) != null) return;

        var clone = UnityEngine.Object.Instantiate(donor.gameObject, parent);
        clone.name = ButtonName;
        clone.SetActive(true);

        // Directly below the donor, so it reads as belonging to the same group.
        clone.transform.SetSiblingIndex(donor.transform.GetSiblingIndex() + 1);

        var button = clone.GetComponent<Button>();
        if (button == null)
        {
            Log.LogWarning($"[AP] Cloned {where} button has no Button component - skipping.");
            UnityEngine.Object.Destroy(clone);
            return;
        }

        // The clone carries the donor's listeners, which would open Twitch settings.
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnArchipelagoButtonPressed?.Invoke());

        SetLabel(clone, ButtonLabel);
        SetIcon(clone);

        Log.LogInfo($"[AP] Added the Archipelago button to the {where}.");
    }

    /// <summary>
    /// Swaps the donor's glyph for the AP logo.
    ///
    /// Matched on the existing sprite's *name* rather than by object name or child index: a
    /// menu button is several Images deep - ribbon, glyph, highlight - and replacing the wrong
    /// one wipes the button's background. The name is the only thing that identifies the glyph
    /// unambiguously, and if the prefab ever changes we get a warning instead of a mangled
    /// button. Buttons with no glyph (the main menu's) are left alone.
    /// </summary>
    private static void SetIcon(GameObject button)
    {
        var icon = ApAssets.IconSprite();
        if (icon == null) return;

        var replaced = 0;
        foreach (var image in button.GetComponentsInChildren<Image>(true))
        {
            if (image.sprite == null) continue;
            if (image.sprite.name.IndexOf("twitch", StringComparison.OrdinalIgnoreCase) < 0) continue;

            image.sprite = icon;
            // The AP logo isn't the same shape as the glyph it replaces, and the slot it sits
            // in was sized for that one.
            image.preserveAspect = true;
            replaced++;
        }

        if (replaced == 0)
        {
            Log.LogInfo("[AP] No donor glyph on the Archipelago button - leaving it text-only.");
        }
    }

    /// <summary>
    /// Names the button, and stops the game renaming it back.
    ///
    /// Every menu label carries an I2 Localize component that rewrites its text from a term on
    /// enable and on any language change. Setting .text alone works until the first of those,
    /// then silently reverts - so the components have to go first.
    /// </summary>
    private static void SetLabel(GameObject button, string label)
    {
        foreach (var localize in button.GetComponentsInChildren<Localize>(true))
        {
            UnityEngine.Object.Destroy(localize);
        }

        foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
        {
            text.text = label;
        }
    }
}
