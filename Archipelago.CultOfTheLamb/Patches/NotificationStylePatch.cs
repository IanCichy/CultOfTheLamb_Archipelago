using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Archipelago.CultOfTheLamb.Patches;

/// <summary>
/// Makes Archipelago's own popups hold twice as long and glow Archipelago green, so a check
/// firing reads as a multiworld event rather than as something the game did.
///
/// Both changes land here rather than at the call site because
/// NotificationCentre.PlayGenericNotification is fire-and-forget - it starts a coroutine that
/// waits for the HUD to be willing to show anything before spawning the popup, so there is no
/// instance to configure at the moment we ask for one. Configure is the first point the object
/// exists, and it is also the only place the loc key and the instance are in scope together.
/// </summary>
[HarmonyPatch]
internal static class NotificationStylePatch
{
    /// <summary>
    /// Vanilla NotificationGeneric._onScreenDuration is 3s, which isn't long enough to read a
    /// check name mid-crusade. The game itself overrides this the same way for Winter flair,
    /// at 10s - so this sits comfortably inside what the game already does to itself.
    /// </summary>
    private const float OnScreenSeconds = 8f;

    private static readonly Color ApGreen = new Color32(0x64, 0xC8, 0x64, 0xFF);

    /// <summary>
    /// Notifications are pooled - NotificationBase.Hide ends in ObjectPool.Recycle - so the
    /// instance we tint gets reused for the game's own popups later. Without the original
    /// colour to put back, a vanilla "building complete" would inherit our green from whichever
    /// Archipelago message used that instance last.
    /// </summary>
    private static readonly Dictionary<Graphic, Color> originalColours = new();

    private static readonly AccessTools.FieldRef<NotificationBase, GameObject> PositiveFlair =
        AccessTools.FieldRefAccess<NotificationBase, GameObject>("_positiveFlair");

    private static bool loggedFlairContents;

    [HarmonyPatch(typeof(NotificationGeneric), nameof(NotificationGeneric.Configure),
        new[] { typeof(string), typeof(NotificationBase.Flair) })]
    [HarmonyPostfix]
    private static void Configure_Postfix(NotificationGeneric __instance, string locKey)
    {
        // Every Archipelago message is registered under this prefix and nothing else uses it,
        // so it's an exact test - and the only one available, since Configure is handed the loc
        // key rather than the display text.
        var mine = locKey != null
            && locKey.StartsWith(ApNotification.TermPrefix, StringComparison.Ordinal);

        // A postfix specifically: NotificationBase.Configure resets _overrideScreenDuration to
        // -1f as its first statement, so setting the override any earlier would be thrown away.
        // Non-AP popups are left on that reset value, which is the vanilla 3s path.
        if (mine) __instance.SetOverrideShowDuration(OnScreenSeconds);

        Recolour(__instance, mine);
    }

    /// <summary>
    /// Recolours the flair behind a Positive notification, or puts the original colours back.
    /// The restore half is the point of the cache - see originalColours.
    /// </summary>
    private static void Recolour(NotificationBase notification, bool mine)
    {
        GameObject flair;
        try
        {
            flair = PositiveFlair(notification);
        }
        catch (Exception e)
        {
            // A prefab variant without the field would otherwise throw once per notification.
            Log.LogWarning($"[AP] Couldn't reach the notification's positive flair: {e.Message}");
            return;
        }

        if (flair == null) return;

        // Notifications are spawned with SpawnUI into the notification container, so the glow is
        // uGUI (Image, or a TMP label) rather than a SpriteRenderer. Inactive included because
        // Configure toggles the flair on for Positive only, and the ordering isn't guaranteed.
        var graphics = flair.GetComponentsInChildren<Graphic>(includeInactive: true);

        // The one thing the decompile can't tell us is what _positiveFlair actually contains -
        // it's a serialized prefab reference. Logged once so the first run says what we hit
        // rather than leaving it to a second round of guessing.
        if (mine && !loggedFlairContents)
        {
            loggedFlairContents = true;
            Log.LogInfo($"[AP] Notification positive flair '{flair.name}' has {graphics.Length} "
                + "graphic(s): "
                + string.Join(", ", Array.ConvertAll(graphics, g => $"{g.name}:{g.GetType().Name}")));
        }

        foreach (var graphic in graphics)
        {
            if (graphic == null) continue;

            if (!originalColours.ContainsKey(graphic)) originalColours[graphic] = graphic.color;

            graphic.color = mine ? ApGreen : originalColours[graphic];
        }
    }
}
