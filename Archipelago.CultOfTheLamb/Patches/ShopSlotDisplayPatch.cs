using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Everything needed to turn a tarot shop slot into an Archipelago check: which slots the shop
/// puts out, what they look like, what they say, and what buying one grants. They live together
/// because the game splits that one concern across shopKeeperManager, Interaction_BuyItem,
/// UITarotDisplay and TarotCards.
///
/// The hooks re-raise as events or ask through delegates, so ShopIconService stays the only
/// place that knows about locations - this file knows about slots.
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
    /// Overwrites a slot's prompt. Writes the field, not the property: Interaction.Label's
    /// getter calls GetLabel() (Interaction.cs:128-143), so reading it from a GetLabel postfix -
    /// the only place a handler runs - recurses until the stack runs out.
    /// </summary>
    internal static void ReplaceLabel(Interaction_BuyItem buyItem, string label)
    {
        if (buyItem == null || string.IsNullOrEmpty(label)) return;

        LabelField(buyItem) = label;
    }

    /// <summary>
    /// Answers "has this card's shop slot been spent?" - true if its check is already sent,
    /// false if it's still there to buy, and null for cards that aren't AP locations at all,
    /// which the game then answers for itself. Set by ShopIconService while connected; null the
    /// rest of the time, which leaves every patch here inert.
    /// </summary>
    internal static Func<TarotCards.Card, bool?> SlotIsSpent;

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
    /// Decides which tarot slots a shop puts out, by answering the one question it asks.
    ///
    /// InitTarotShop shows a slot iff the player doesn't own its card - the only state a tarot
    /// purchase writes. Once these slots became AP checks that answer was wrong both ways: a
    /// card granted by the multiworld made the slot vanish and stranded its location, and a
    /// sent check left the slot buyable again on every visit for nothing.
    ///
    /// So the answer comes from the location's state instead. Overriding this one call rather
    /// than rebuilding slots afterwards keeps the game's own initialisation - cost, quantity,
    /// prefab wiring, sold-out signs.
    /// </summary>
    [HarmonyPatch(typeof(DataManager), nameof(DataManager.TrinketUnlocked))]
    [HarmonyPrefix]
    private static bool TrinketUnlocked_Prefix(TarotCards.Card card, ref bool __result)
    {
        if (!initialisingTarotShop || SlotIsSpent == null) return true;

        var spent = SlotIsSpent(card);
        if (!spent.HasValue) return true;

        __result = spent.Value;
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
    /// The panel that floats over a shop slot. LocalizeText rather than Play, because it writes
    /// the three text fields and catches the re-fill on a language change too. No scoping
    /// needed: UITarotDisplay is only ever spawned by TarotCardDisplay on the slot itself.
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
    /// Marks the next unlock of <paramref name="card"/> as already paid for. An AP slot is a
    /// location, not a purchase, so without this one action would pay out twice.
    /// </summary>
    internal static void SuppressNextUnlock(TarotCards.Card card)
    {
        suppressedUnlocks[card] = Time.unscaledTime + SuppressionWindowSeconds;
    }

    /// <summary>
    /// Whether this unlock was a shop purchase we've already accounted for, clearing the mark as
    /// it answers. A stale entry counts as no suppression, so the failure mode is a card earned
    /// normally rather than one silently swallowed.
    /// </summary>
    internal static bool ConsumeSuppressedUnlock(TarotCards.Card card)
    {
        if (!suppressedUnlocks.TryGetValue(card, out var expiresAt)) return false;

        suppressedUnlocks.Remove(card);

        if (Time.unscaledTime <= expiresAt) return true;

        Log.LogInfo($"[AP] Stale unlock suppression for {card} - treating it as earned normally.");
        return false;
    }
}
