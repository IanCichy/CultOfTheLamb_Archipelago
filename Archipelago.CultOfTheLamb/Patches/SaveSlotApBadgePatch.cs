using System;
using HarmonyLib;
using Lamb.UI.MainMenu;
using UnityEngine;
using UnityEngine.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Badges the save select rows that Archipelago has played on
/// </summary>
/// <remarks>
/// AP saves must stay connected, so players need to tell them apart before loading one. The game
/// already badges a beaten save, and this is the same idea.
///
/// Reads AppliedItemStore instead of the save, since the menu has no save loaded.
///
/// The badge clones the game's completion badge to keep its layout. Same trick as MenuButtonPatch.
/// </remarks>
[HarmonyPatch]
internal static class SaveSlotApBadgePatch
{
    private const string BadgeName = "ArchipelagoSaveBadge";

    // Moves the clone clear of the completion badge, so a beaten AP save shows both
    private static readonly Vector3 Offset = new(-46f, 0f, 0f);

    private const float ScaleUp = 1.4f;

    private static readonly AccessTools.FieldRef<SaveSlotButtonBase, GameObject> CompletionBadge =
        AccessTools.FieldRefAccess<SaveSlotButtonBase, GameObject>("_completionBadge");

    [HarmonyPatch(typeof(SaveSlotButton_Load), "SetupOccupiedSlot")]
    [HarmonyPostfix]
    private static void Load_Postfix(SaveSlotButtonBase __instance)
    {
        Apply(__instance);
    }

    [HarmonyPatch(typeof(SaveSlotButton_BaseGame), "SetupOccupiedSlot")]
    [HarmonyPostfix]
    private static void BaseGame_Postfix(SaveSlotButtonBase __instance)
    {
        Apply(__instance);
    }

    // A throw here would leave the save menu half built
    private static void Apply(SaveSlotButtonBase slot)
    {
        try
        {
            Show(slot, AppliedItemStore.HasHistoryFor(slot.SaveIndex));
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not badge save slot {slot?.SaveIndex}: {e.Message}");
        }
    }

    private static void Show(SaveSlotButtonBase slot, bool isArchipelago)
    {
        var donor = CompletionBadge(slot);
        if (donor == null)
        {
            return;
        }

        // Search the donor's parent, where the clone goes. slot.transform only finds direct
        // children, so it would miss the badge and spawn a new one every time
        var parent = donor.transform.parent;
        var existing = parent == null ? null : parent.Find(BadgeName);
        if (existing != null)
        {
            existing.gameObject.SetActive(isArchipelago);
            return;
        }

        if (!isArchipelago)
        {
            return;
        }

        var badge = UnityEngine.Object.Instantiate(donor, parent);
        badge.name = BadgeName;
        badge.transform.localPosition = donor.transform.localPosition + Offset;

        // Bigger than the crow, since our pale ring is hard to see at its size
        badge.transform.localScale = donor.transform.localScale * ScaleUp;

        // In case the prefab carries a faded group we can't see
        foreach (var group in badge.GetComponentsInChildren<CanvasGroup>(includeInactive: true))
        {
            group.alpha = 1f;
        }

        if (!SwapSprite(badge))
        {
            UnityEngine.Object.Destroy(badge);
            return;
        }

        badge.SetActive(true);
    }

    // Replaces every image on the clone, or it still looks like the beaten badge. False when our
    // sprite is missing, so the caller drops the clone
    private static bool SwapSprite(GameObject badge)
    {
        var sprite = ApAssets.IconSprite();
        if (sprite == null)
        {
            return false;
        }

        var swapped = false;

        foreach (var image in badge.GetComponentsInChildren<Image>(includeInactive: true))
        {
            image.sprite = sprite;
            image.color = Color.white;
            swapped = true;
        }

        foreach (var renderer in badge.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
        {
            renderer.sprite = sprite;
            renderer.color = Color.white;
            swapped = true;
        }

        return swapped;
    }
}
