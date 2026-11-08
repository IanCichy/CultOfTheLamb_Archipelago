using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Turns a tarot shop slot into an Archipelago check: which slots the shop puts out, what they
/// look like, what they say, and what buying one grants. One file because the game splits that
/// concern across shopKeeperManager, Interaction_BuyItem, UITarotDisplay and TarotCards.
/// Everything here re-raises as an event or asks through a delegate, so this file knows about
/// slots and ShopIconService stays the only place that knows about locations.
/// </summary>
[HarmonyPatch]
internal static class ShopSlotDisplayPatch
{
    // Raised once per shop after its slots have been filled in
    internal static event Action<shopKeeperManager> OnShopInitialised;

    // Raised after a slot rebuilds its prompt. Handlers rewrite it via ReplaceLabel
    internal static event Action<Interaction_BuyItem> OnLabelBuilt;

    private static readonly AccessTools.FieldRef<Interaction, string> LabelField =
        AccessTools.FieldRefAccess<Interaction, string>("label");

    // Writes the backing field directly. Interaction.Label's getter calls GetLabel()
    // (Interaction.cs:136), and handlers only ever run from a GetLabel postfix, so reading the
    // property from one would recurse.
    internal static void ReplaceLabel(Interaction_BuyItem buyItem, string label)
    {
        if (buyItem == null || string.IsNullOrEmpty(label))
        {
            return;
        }

        LabelField(buyItem) = label;
    }

    // Has this card's slot been spent? Null means it is not an AP location, so the game
    // answers for itself. Set by ShopIconService while connected, null otherwise, which leaves
    // this whole file inert.
    internal static Func<TarotCards.Card, bool?> SlotIsSpent;

    // Scopes the TrinketUnlocked override below to InitTarotShop, so it cannot leak into the
    // tarot menu or the collection screen.
    private static bool initialisingTarotShop;

    // The only initialiser a tarot shop runs: shopKeeperManager.Start branches on TarotCardShop.
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
        // A subscriber failing must not abandon the shop the game just finished building.
        try
        {
            OnShopInitialised?.Invoke(__instance);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] A tarot shop opened but marking its slots failed: {e}");
        }
    }

    // A finalizer, not a postfix, because it also runs when InitTarotShop throws. One escaped
    // exception would otherwise leave TrinketUnlocked overridden for the rest of the session.
    [HarmonyPatch(typeof(shopKeeperManager), "InitTarotShop")]
    [HarmonyFinalizer]
    private static void InitTarotShop_Finalizer()
    {
        initialisingTarotShop = false;
    }

    // InitTarotShop shows a slot only if you don't own the card (shopKeeperManager.cs:362).
    // Once slots are checks that breaks both ways: a card arriving from the multiworld hides the
    // slot and strands its check, and a slot you already bought comes back. Answering from check
    // state instead fixes both and leaves price, stock and the sold out sign alone.
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.TrinketUnlocked))]
    [HarmonyPrefix]
    private static bool TrinketUnlocked_Prefix(TarotCards.Card card, ref bool __result)
    {
        if (!initialisingTarotShop || SlotIsSpent == null)
        {
            return true;
        }

        bool? spent;
        try
        {
            spent = SlotIsSpent(card);
        }
        catch (Exception e)
        {
            // Falls back to the game's own answer. A wrong-looking slot beats throwing out
            // of InitTarotShop and leaving the shop empty.
            Log.LogWarning($"[AP] Couldn't read the check state for tarot slot {card}, so the "
                + $"shop decides for itself: {e}");
            return true;
        }

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
        // Mid-build of the prompt the player is about to read, so a throw would cost the
        // whole prompt rather than just the AP wording.
        try
        {
            OnLabelBuilt?.Invoke(__instance);
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't rewrite a shop slot label: {e}");
        }
    }

    // Raised once the floating card panel fills in. Handlers rewrite it through
    // SetTarotDisplayText.
    internal static event Action<UITarotDisplay, TarotCards.Card> OnTarotDisplayBuilt;

    private static readonly AccessTools.FieldRef<UITarotDisplay, TarotCards.Card> DisplayCardField =
        AccessTools.FieldRefAccess<UITarotDisplay, TarotCards.Card>("tarotCard");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayTitleField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("title");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayLoreField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("loreText");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayDescriptionField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("descriptionText");

    // LocalizeText rather than Play: Play calls it, and it is also subscribed to
    // OnLocalizeEvent, so this catches the language-change refill too. Needs no scoping because
    // TarotCardDisplay is the only thing that spawns a UITarotDisplay.
    [HarmonyPatch(typeof(UITarotDisplay), "LocalizeText")]
    [HarmonyPostfix]
    private static void LocalizeText_Postfix(UITarotDisplay __instance)
    {
        // The field refs resolve by name, so a drifted game build lands here. A wrong-looking
        // panel beats one that throws every time it opens.
        try
        {
            OnTarotDisplayBuilt?.Invoke(__instance, DisplayCardField(__instance));
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Couldn't rewrite the floating tarot panel: {e}");
        }
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

    // Spans the purchase sequence, which runs about 1.5 seconds from the buy to the unlock
    // (Interaction_BuyItem.cs:346 through UITarotCardsMenuController.cs:52). Deliberately far
    // longer than measured, since the cost of overshooting is only a stale entry.
    private const float SuppressionWindowSeconds = 15f;

    // Marks the next unlock of this card as already paid for. An AP slot is a location, not a
    // purchase, so without this one action pays out twice.
    internal static void SuppressNextUnlock(TarotCards.Card card)
    {
        suppressedUnlocks[card] = Time.unscaledTime + SuppressionWindowSeconds;
    }

    // Whether this unlock was an already-accounted shop purchase, clearing the mark as it
    // answers. A stale entry reads as no suppression, so the failure lands on a card counted as
    // earned normally rather than one silently swallowed.
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
