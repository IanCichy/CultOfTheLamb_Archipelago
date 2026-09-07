using UnityEngine;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// The Archipelago palette, as both <see cref="Color32"/> (for notification glow tints) and hex
/// strings (for TMP <c>&lt;color=#…&gt;</c> tags in message text).
/// </summary>
/// <remarks>
/// Both forms live here so they can't drift apart.
///
/// The glow tint and the inline tags are independent surfaces. The flair tint recolours the glow
/// graphics, not the text label. A popup can therefore carry direction in its glow and item
/// classification in its wording at the same time.
/// </remarks>
internal static class ApColors
{
    /// <summary>Traps.</summary>
    internal const string RedHex = "#c97682";

    /// <summary>Other players' names, warnings, catch-up batches.</summary>
    internal const string YellowHex = "#eee391";

    /// <summary>Outgoing checks, and 'useful' items.</summary>
    internal const string BlueHex = "#767ebd";

    /// <summary>Incoming items, and items destined for you.</summary>
    internal const string GreenHex = "#75c275";

    /// <summary>Progression. The checks that actually move a seed forward.</summary>
    internal const string PinkHex = "#ca94c2";

    /// <summary>Filler.</summary>
    internal const string OrangeHex = "#d9a07d";

    internal static readonly Color32 Red = new(0xC9, 0x76, 0x82, 0xFF);
    internal static readonly Color32 Yellow = new(0xEE, 0xE3, 0x91, 0xFF);
    internal static readonly Color32 Blue = new(0x76, 0x7E, 0xBD, 0xFF);
    internal static readonly Color32 Green = new(0x75, 0xC2, 0x75, 0xFF);
    internal static readonly Color32 Pink = new(0xCA, 0x94, 0xC2, 0xFF);
    internal static readonly Color32 Orange = new(0xD9, 0xA0, 0x7D, 0xFF);

    /// <summary>Wraps <paramref name="text"/> in a TMP colour tag.</summary>
    /// <param name="text">The message text to tint.</param>
    /// <param name="hex">One of the hex constants on this class, such as <see cref="PinkHex"/>.</param>
    internal static string Tint(string text, string hex) => $"<color={hex}>{text}</color>";
}
