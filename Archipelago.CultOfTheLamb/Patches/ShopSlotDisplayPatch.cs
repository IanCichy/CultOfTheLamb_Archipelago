using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Everything needed to turn a tarot shop slot into an Archipelago check. That means which
/// slots the shop puts out, what they look like, what they say, and what buying one grants. They
/// live together because the game splits that one concern across shopKeeperManager,
/// Interaction_BuyItem, UITarotDisplay and TarotCards.
///
/// The hooks re-raise as events or ask through delegates, so ShopIconService stays the only
/// place that knows about locations. This file knows about slots.
/// </summary>
[HarmonyPatch]
internal static class ShopSlotDisplayPatch
{
    // Raised once per shop after its slots have been filled in
    internal static event Action<shopKeeperManager> OnShopInitialised;

    // Raised after a slot rebuilds its prompt. Handlers add to it via AppendToLabel
    internal static event Action<Interaction_BuyItem> OnLabelBuilt;

    // Interaction.label, the field behind the Label property. Resolved once, because
    // FieldRefAccess does the reflection at construction, so the accessor is a direct field
    // access.
    private static readonly AccessTools.FieldRef<Interaction, string> LabelField =
        AccessTools.FieldRefAccess<Interaction, string>("label");

    // Overwrites a slot's prompt. Writes the field, not the property, because Interaction.Label's
    // getter calls GetLabel() (Interaction.cs:128-143). Reading it from a GetLabel postfix, which
    // is the only place a handler runs, recurses until the stack runs out
    internal static void ReplaceLabel(Interaction_BuyItem buyItem, string label)
    {
        if (buyItem == null || string.IsNullOrEmpty(label))
        {
            return;
        }

        LabelField(buyItem) = label;
    }

    // Answers "has this card's shop slot been spent?". True if its check is already sent,
    // false if it's still there to buy, and null for cards that aren't AP locations at all,
    // which the game then answers for itself. Set by ShopIconService while connected, and null
    // the rest of the time, which leaves every patch here inert
    internal static Func<TarotCards.Card, bool?> SlotIsSpent;

    // Set only for the duration of InitTarotShop, so the TrinketUnlocked override below can't
    // leak into the tarot menu, the collection screen, or anything else that asks.
    private static bool initialisingTarotShop;

    // InitTarotShop is the only initialiser the tarot hub shops run (shopKeeperManager.Start
    // branches on the TarotCardShop flag). Private, which Harmony doesn't care about.
    [HarmonyPatch(typeof(shopKeeperManager), "InitTarotShop")]
    [HarmonyPrefix]
    private static void InitTarotShop_Prefix()
    {
        initialisingTarotShop = true;
    }

    [HarmonyPatch(typeof(shopKeeperManager), "InitTarotShop")]
    [HarmonyPostfix]
    private static void InitTarotShop_Postfix(shopKeeperManager __instance)
    {
        OnShopInitialised?.Invoke(__instance);
    }

    // Clears the flag even when InitTarotShop throws, which a postfix wouldn't. Without this a
    // single exception leaves TrinketUnlocked overridden for the rest of the session, answering
    // location state to every caller that asks, meaning the tarot menu, the collection screen,
    // the lot
    [HarmonyPatch(typeof(shopKeeperManager), "InitTarotShop")]
    [HarmonyFinalizer]
    private static void InitTarotShop_Finalizer()
    {
        initialisingTarotShop = false;
    }

    // Decides which tarot slots a shop shows, by answering the one question it asks
    //
    // The shop only shows a slot if you don't own the card. That breaks once slots are checks: a
    // card from the multiworld hides the slot and strands its check, and a slot you already bought
    // keeps coming back.
    //
    // So we answer from the check's state instead. Overriding this one call keeps everything else
    // the game sets up, like price, stock and the sold out sign
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.TrinketUnlocked))]
    [HarmonyPrefix]
    private static bool TrinketUnlocked_Prefix(TarotCards.Card card, ref bool __result)
    {
        if (!initialisingTarotShop || SlotIsSpent == null)
        {
            return true;
        }

        var spent = SlotIsSpent(card);
        if (!spent.HasValue)
        {
            return true;
        }

        __result = spent.Value;
        return false;
    }

    [HarmonyPatch(typeof(Interaction_BuyItem), "GetLabel")]
    [HarmonyPostfix]
    private static void GetLabel_Postfix(Interaction_BuyItem __instance)
    {
        OnLabelBuilt?.Invoke(__instance);
    }

    // Raised after the floating card panel fills itself in, with the card it's describing.
    // Handlers rewrite it through SetTarotDisplayText
    internal static event Action<UITarotDisplay, TarotCards.Card> OnTarotDisplayBuilt;

    private static readonly AccessTools.FieldRef<UITarotDisplay, TarotCards.Card> DisplayCardField =
        AccessTools.FieldRefAccess<UITarotDisplay, TarotCards.Card>("tarotCard");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayTitleField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("title");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayLoreField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("loreText");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayDescriptionField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("descriptionText");

    // The panel that floats over a shop slot. LocalizeText rather than Play, because it writes
    // the three text fields and catches the re-fill on a language change too. No scoping is
    // needed, since UITarotDisplay is only ever spawned by TarotCardDisplay on the slot itself
    [HarmonyPatch(typeof(UITarotDisplay), "LocalizeText")]
    [HarmonyPostfix]
    private static void LocalizeText_Postfix(UITarotDisplay __instance)
    {
        OnTarotDisplayBuilt?.Invoke(__instance, DisplayCardField(__instance));
    }

    // Overwrites the floating panel's three lines. Null leaves a line as it was
    internal static void SetTarotDisplayText(
        UITarotDisplay display, string title, string lore, string description)
    {
        if (display == null)
        {
            return;
        }

        var titleText = DisplayTitleField(display);
        if (titleText != null && title != null)
        {
            titleText.text = title;
        }

        var loreText = DisplayLoreField(display);
        if (loreText != null && lore != null)
        {
            loreText.text = lore;
        }

        var descriptionText = DisplayDescriptionField(display);
        if (descriptionText != null && description != null)
        {
            descriptionText.text = description;
        }
    }

    // Cards whose next unlock belongs to the multiworld, with the time each entry goes stale.
    private static readonly Dictionary<TarotCards.Card, float> suppressedUnlocks = new();

    // Generous, because the window it has to cover is the purchase cutscene. The card flies to
    // the player, the reveal menu opens, and only then does the unlock fire, several seconds
    // after the purchase that armed this.
    private const float SuppressionWindowSeconds = 15f;

    // Marks the next unlock of card as already paid for. An AP slot is a
    // location, not a purchase, so without this one action would pay out twice
    internal static void SuppressNextUnlock(TarotCards.Card card)
    {
        suppressedUnlocks[card] = Time.unscaledTime + SuppressionWindowSeconds;
    }

    // Whether this unlock was a shop purchase we've already accounted for, clearing the mark as
    // it answers. A stale entry counts as no suppression, so the failure mode is a card earned
    // normally rather than one silently swallowed
    internal static bool ConsumeSuppressedUnlock(TarotCards.Card card)
    {
        if (!suppressedUnlocks.TryGetValue(card, out var expiresAt))
        {
            return false;
        }

        suppressedUnlocks.Remove(card);

        if (Time.unscaledTime <= expiresAt)
        {
            return true;
        }

        Log.LogInfo($"[AP] Stale unlock suppression for {card} - treating it as earned normally.");
        return false;
    }
}
