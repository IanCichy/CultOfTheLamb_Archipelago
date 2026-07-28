using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Loads the mod's own textures out of the assembly.
///
/// Assets ship as *embedded resources* rather than loose files next to the DLL: r2modman
/// flattens plugin folders in ways that make relative paths unreliable, and a missing file at
/// runtime would be a silent visual bug rather than a build error. Embedding makes the DLL
/// self-contained.
///
/// PNG, not the WebP the AP logo is normally distributed as - Unity's Texture2D.LoadImage only
/// decodes PNG and JPG.
/// </summary>
internal static class ApAssets
{
    // Default logical name for an EmbeddedResource: RootNamespace + folder path + filename.
    private const string CardResourceName = "Archipelago.CultOfTheLamb.Assets.APTarotCard.png";

    private static Texture2D cardTexture;
    private static bool cardLoadAttempted;

    // Sprites are built per size and pivot so a card can stand exactly where the one it replaced
    // did. Keyed on hundredths of a world unit, finer than any visible difference.
    private static readonly Dictionary<string, Sprite> cardSprites = new();

    /// <summary>
    /// The Archipelago tarot card, sized and anchored to stand exactly where a piece of art with
    /// the given bounds was standing.
    ///
    /// Both halves matter. It fits *inside* the bounds keeping its aspect ratio, so it can't
    /// come out wider than the card it replaces. And its pivot is chosen so its centre lands on
    /// the original's centre - a sprite's pivot is its anchor to the transform, so a replacement
    /// with a naive centred pivot inherits the original's anchor point and, for art anchored at
    /// its base, ends up sunk into the shop counter.
    ///
    /// Returns null if the texture couldn't be loaded; callers should leave the original art
    /// alone rather than blanking a slot.
    /// </summary>
    internal static Sprite TarotCardSprite(Bounds original)
    {
        var texture = CardTexture();
        if (texture == null) return null;

        // Height the card would need to be exactly as wide as the space it has to fit.
        var heightAtFullWidth = original.size.x > 0f
            ? original.size.x * texture.height / texture.width
            : 0f;

        var height = original.size.y > 0f && heightAtFullWidth > 0f
            ? Mathf.Min(original.size.y, heightAtFullWidth)
            : Mathf.Max(original.size.y, heightAtFullWidth);
        if (height <= 0f) height = 1f;

        var width = height * texture.width / texture.height;

        // A sprite's local bounds sit centred at size * (0.5 - pivot) from the transform, so
        // this inverts that to land the centre where the original's was.
        var pivot = new Vector2(
            0.5f - original.center.x / width,
            0.5f - original.center.y / height);

        return CardSprite(texture, height, pivot);
    }

    private static Sprite CardSprite(Texture2D texture, float worldHeight, Vector2 pivot)
    {
        var key = $"{Mathf.RoundToInt(worldHeight * 100f)}"
            + $"_{Mathf.RoundToInt(pivot.x * 100f)}_{Mathf.RoundToInt(pivot.y * 100f)}";
        if (cardSprites.TryGetValue(key, out var cached) && cached != null) return cached;

        // pixelsPerUnit is what sets a sprite's world size: height / PPU = units tall.
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            pivot,
            texture.height / worldHeight);
        sprite.name = $"ArchipelagoTarot_{key}";
        sprite.hideFlags = HideFlags.HideAndDontSave;

        cardSprites[key] = sprite;
        return sprite;
    }

    private static Texture2D CardTexture()
    {
        if (cardLoadAttempted) return cardTexture;
        cardLoadAttempted = true;

        var bytes = ReadResource(CardResourceName);
        if (bytes == null) return null;

        // The 2x2 size is a placeholder - LoadImage resizes the texture to the decoded image.
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            // Without this the texture is a scene object and Unity destroys it on scene load,
            // leaving every sprite built from it rendering as nothing.
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (!texture.LoadImage(bytes))
        {
            Log.LogWarning($"[AP] Could not decode {CardResourceName} - shop slots keep their "
                + "vanilla art.");
            Object.Destroy(texture);
            return null;
        }

        Log.LogInfo($"[AP] Loaded AP tarot card ({texture.width}x{texture.height}).");
        cardTexture = texture;
        return cardTexture;
    }

    private static byte[] ReadResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            // Almost always a build problem (the EmbeddedResource entry in the csproj), so name
            // what *is* embedded to make the mismatch obvious.
            Log.LogWarning($"[AP] Embedded resource '{name}' not found. Available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
