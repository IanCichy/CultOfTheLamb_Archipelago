using System;
using UnityEngine;

using UnityEngine.UI;

namespace Archipelago.CultOfTheLamb.UI;

/// <summary>
/// An Archipelago logo in the top left, full colour while connected and dimmed while not. Without
/// it, a dropped connection looks fine until a check doesn't land.
/// </summary>
/// <remarks>
/// Not clickable, since something catching clicks over the play area could eat attacks. It has
/// its own canvas so it can sit in the screen corner, which means it doesn't hide with the HUD by
/// itself, so that's copied from HUD_Manager.Hidden.
/// </remarks>
internal class ArchipelagoHudIndicator : MonoBehaviour
{
    // Answers whether Archipelago is connected. Set by the plugin
    internal static Func<bool> IsConnected;

    private const string ObjectName = "ArchipelagoStatusIcon";

    // Inset from the top-left corner, and size, in canvas units. Tucked above and left of the
    // faith ring, which starts a little further in. These are the two numbers to change if it
    // sits wrong. y is negative because the anchor is the top edge.
    private static readonly Vector2 Inset = new(34f, -34f);
    private static readonly Vector2 Size = new(44f, 44f);

    private static readonly Color ConnectedTint = Color.white;
    private static readonly Color DisconnectedTint = new(1f, 1f, 1f, 0.3f);

    // Above the game's own UI. The HUD sits at 0, so anything positive clears it without
    // fighting the pause menu, which renders on a much higher layer of its own.
    private const int SortingOrder = 100;

    private static GameObject root;

    private Image image;

    // Builds the indicator once and keeps it alive across scenes. Cheap enough to call every
    // frame, since the common case is one null check. Polled rather than hooked to a HUD
    // method, because the HUD is rebuilt per scene
    internal static void EnsureExists()
    {
        if (root != null)
        {
            return;
        }

        var icon = ApAssets.IconSprite();
        if (icon == null)
        {
            return;
        }

        // No GraphicRaycaster. Nothing here is interactive, and without one this canvas can't
        // intercept a click even in principle.
        root = new GameObject("ArchipelagoHud", typeof(Canvas));
        UnityEngine.Object.DontDestroyOnLoad(root);

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        var go = new GameObject(ObjectName, typeof(RectTransform));
        go.transform.SetParent(root.transform, worldPositionStays: false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = Size;
        rect.anchoredPosition = Inset;

        var image = go.AddComponent<Image>();
        image.sprite = icon;
        image.preserveAspect = true;
        image.raycastTarget = false;

        go.AddComponent<ArchipelagoHudIndicator>().image = image;

        Log.LogInfo("[AP] Added the connection indicator to the screen.");
    }

    private void Update()
    {
        if (image == null)
        {
            return;
        }

        image.color = IsConnected != null && IsConnected() ? ConnectedTint : DisconnectedTint;

        // Mirrors the HUD, so it vanishes for cutscenes and full-screen menus along with
        // everything else rather than floating over them.
        var hud = HUD_Manager.Instance;
        var visible = hud == null || !hud.Hidden;
        if (image.enabled != visible)
        {
            image.enabled = visible;
        }
    }
}
