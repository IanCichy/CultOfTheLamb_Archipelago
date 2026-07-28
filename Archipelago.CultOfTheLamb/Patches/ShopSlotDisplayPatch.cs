using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// The two hooks needed to make a shop slot *look* like an Archipelago check.
///
/// They're paired here because they're one concern - slot presentation - split across two
/// classes only because the game splits it that way: shopKeeperManager decides what each slot
/// holds and shows, Interaction_BuyItem writes the prompt text.
///
/// Nothing here touches AP state; both just re-raise as events so ShopIconService stays the
/// only place that knows about locations. Same shape as ShopPurchasePatch.
/// </summary>
[HarmonyPatch]
internal static class ShopSlotDisplayPatch
{
    /// <summary>Raised once per shop after its slots have been filled in.</summary>
    internal static event Action<shopKeeperManager> OnShopInitialised;

    /// <summary>
    /// Raised after a slot rebuilds its prompt. Handlers add to it via AppendToLabel.
    /// </summary>
    internal static event Action<Interaction_BuyItem> OnLabelBuilt;

    // Interaction.label, the field behind the Label property. Resolved once - FieldRefAccess
    // does the reflection at construction, so the accessor itself is a direct field access.
    private static readonly AccessTools.FieldRef<Interaction, string> LabelField =
        AccessTools.FieldRefAccess<Interaction, string>("label");

    /// <summary>
    /// Overwrites a slot's prompt.
    ///
    /// Writes the field rather than the property because Interaction.Label's *getter* calls
    /// GetLabel() before returning (Interaction.cs:128-143). Reading it from a GetLabel
    /// postfix - the only place a handler runs - re-enters GetLabel and recurses until the
    /// stack runs out. Assigning through the property is safe; reading it is not, so touching
    /// the field keeps the whole hazard in one place.
    /// </summary>
    internal static void ReplaceLabel(Interaction_BuyItem buyItem, string label)
    {
        if (buyItem == null || string.IsNullOrEmpty(label)) return;

        LabelField(buyItem) = label;
    }

    /// <summary>
    /// Answers "is this card still an unclaimed AP location?". Set by ShopIconService while
    /// connected; null the rest of the time, which leaves every patch here inert.
    /// </summary>
    internal static Func<TarotCards.Card, bool> IsOpenCheck;

    // Set only for the duration of InitTarotShop, so the TrinketUnlocked override below can't
    // leak into the tarot menu, the collection screen, or anything else that asks.
    private static bool initialisingTarotShop;

    // InitTarotShop is the only initialiser the tarot hub shops run (shopKeeperManager.Start
    // branches on the TarotCardShop flag). Private, which Harmony doesn't care about.
    [HarmonyPatch(typeof(shopKeeperManager), "InitTarotShop")]
    [HarmonyPrefix]
    private static void InitTarotShop_Prefix() => initialisingTarotShop = true;

    [HarmonyPatch(typeof(shopKeeperManager), "InitTarotShop")]
    [HarmonyPostfix]
    private static void InitTarotShop_Postfix(shopKeeperManager __instance)
    {
        initialisingTarotShop = false;
        OnShopInitialised?.Invoke(__instance);
    }

    /// <summary>
    /// Keeps a shop slot on the shelf when its card is already unlocked but its AP location
    /// hasn't been sent.
    ///
    /// InitTarotShop hides any slot whose card the player owns - it calls AlreadyBought() and
    /// drops a sold-out sign - which is right for vanilla and wrong for a randomizer: the card
    /// arrives from the item pool whenever the multiworld decides, and the moment it does, the
    /// location behind that slot becomes permanently unreachable. That's a seed-breaking
    /// softlock, not a cosmetic problem.
    ///
    /// Lying to that one call rather than rebuilding the slot afterwards means the game's own
    /// initialisation runs normally - cost, quantity, prefab wiring - instead of us
    /// reconstructing it from outside and getting some detail wrong.
    /// </summary>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.TrinketUnlocked))]
    [HarmonyPrefix]
    private static bool TrinketUnlocked_Prefix(TarotCards.Card card, ref bool __result)
    {
        if (!initialisingTarotShop || IsOpenCheck == null || !IsOpenCheck(card)) return true;

        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(Interaction_BuyItem), "GetLabel")]
    [HarmonyPostfix]
    private static void GetLabel_Postfix(Interaction_BuyItem __instance)
    {
        OnLabelBuilt?.Invoke(__instance);
    }

    /// <summary>
    /// Raised after the floating card panel fills itself in, with the card it's describing.
    /// Handlers rewrite it through SetTarotDisplayText.
    /// </summary>
    internal static event Action<UITarotDisplay, TarotCards.Card> OnTarotDisplayBuilt;

    private static readonly AccessTools.FieldRef<UITarotDisplay, TarotCards.Card> DisplayCardField =
        AccessTools.FieldRefAccess<UITarotDisplay, TarotCards.Card>("tarotCard");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayTitleField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("title");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayLoreField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("loreText");
    private static readonly AccessTools.FieldRef<UITarotDisplay, TMP_Text> DisplayDescriptionField =
        AccessTools.FieldRefAccess<UITarotDisplay, TMP_Text>("descriptionText");

    /// <summary>
    /// The panel that floats over a shop slot while the player stands at it.
    ///
    /// Patching LocalizeText rather than Play catches both the initial fill and the re-fill on
    /// a language change, and it's the method that actually writes the three text fields.
    ///
    /// No scoping needed here, unlike the TrinketUnlocked override: UITarotDisplay is spawned
    /// from exactly one place, TarotCardDisplay, which sits on the shop slot itself. The card
    /// collection and reveal menus use TarotInfoCard instead and are untouched.
    /// </summary>
    [HarmonyPatch(typeof(UITarotDisplay), "LocalizeText")]
    [HarmonyPostfix]
    private static void LocalizeText_Postfix(UITarotDisplay __instance)
    {
        OnTarotDisplayBuilt?.Invoke(__instance, DisplayCardField(__instance));
    }

    /// <summary>Overwrites the floating panel's three lines. Null leaves a line as it was.</summary>
    internal static void SetTarotDisplayText(
        UITarotDisplay display, string title, string lore, string description)
    {
        if (display == null) return;

        var titleText = DisplayTitleField(display);
        if (titleText != null && title != null) titleText.text = title;

        var loreText = DisplayLoreField(display);
        if (loreText != null && lore != null) loreText.text = lore;

        var descriptionText = DisplayDescriptionField(display);
        if (descriptionText != null && description != null) descriptionText.text = description;
    }

    // Cards whose next unlock belongs to the multiworld, with the time each entry goes stale.
    private static readonly Dictionary<TarotCards.Card, float> suppressedUnlocks = new();

    // Generous, because the window it has to cover is the purchase cutscene: the card flies to
    // the player, the reveal menu opens, and only then does the unlock fire - several seconds
    // after the purchase that armed this.
    private const float SuppressionWindowSeconds = 15f;

    /// <summary>
    /// Marks the next unlock of <paramref name="card"/> as belonging to Archipelago, so the
    /// game doesn't also hand it over locally.
    /// </summary>
    internal static void SuppressNextUnlock(TarotCards.Card card)
    {
        suppressedUnlocks[card] = Time.unscaledTime + SuppressionWindowSeconds;
    }

    /// <summary>
    /// Stops a bought shop slot from granting its card.
    ///
    /// An AP-marked slot is a *location*, not a purchase: the card itself is an item in the
    /// pool and arrives whenever the multiworld sends it. Granting it here as well would make
    /// the pool item redundant and let a player buy their way past the randomizer.
    ///
    /// Armed per card and time-boxed rather than left on: the same method is how an AP item
    /// grant will unlock a card (Sprint 3), and a blanket suppression would block that too.
    /// An entry that goes stale - the cutscene interrupted, say - is treated as no suppression
    /// at all, so the failure mode is a card granted normally rather than one lost for good.
    /// </summary>
    [HarmonyPatch(typeof(TarotCards), nameof(TarotCards.UnlockTrinket))]
    [HarmonyPrefix]
    private static bool UnlockTrinket_Prefix(TarotCards.Card card, ref bool __result)
    {
        if (!suppressedUnlocks.TryGetValue(card, out var expiresAt)) return true;

        suppressedUnlocks.Remove(card);
        if (Time.unscaledTime > expiresAt)
        {
            Log.LogInfo($"[AP] Stale unlock suppression for {card} - letting the game grant it.");
            return true;
        }

        Log.LogInfo($"[AP] Bought {card} as an Archipelago check - not granting the card locally.");
        __result = false;
        return false;
    }
}
