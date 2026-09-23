using UnityEngine;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// The Archipelago palette, as both <see cref="Color32"/> (for notification glow tints) and hex
/// strings (for TMP <c>&lt;color=#…&gt;</c> tags in message text).
/// </summary>
/// <remarks>
/// Both forms live here so they can't drift apart.
///
/// The glow tint and the inline tags are independent surfaces. The glow tint recolours the glow
/// graphics, not the text label. A popup can therefore carry direction in its glow and a player's
/// name in its wording at the same time.
/// </remarks>
internal static class ApColors
{
    // Traps
    internal const string RedHex = "#c97682";

    // Other players names, warnings, catch-up batches
    internal const string YellowHex = "#eee391";

    // Outgoing checks
    internal const string BlueHex = "#767ebd";

    // Incoming items
    internal const string GreenHex = "#75c275";

    // Unused so far. Kept because the palette is the Archipelago one and should stay whole
    internal const string PinkHex = "#ca94c2";
    internal const string OrangeHex = "#d9a07d";

    internal static readonly Color32 Red = new(0xC9, 0x76, 0x82, 0xFF);
    internal static readonly Color32 Yellow = new(0xEE, 0xE3, 0x91, 0xFF);
    internal static readonly Color32 Blue = new(0x76, 0x7E, 0xBD, 0xFF);
    internal static readonly Color32 Green = new(0x75, 0xC2, 0x75, 0xFF);
    internal static readonly Color32 Pink = new(0xCA, 0x94, 0xC2, 0xFF);
    internal static readonly Color32 Orange = new(0xD9, 0xA0, 0x7D, 0xFF);

    // Wraps text in a TMP colour tag.
    internal static string Tint(string text, string hex) =>
        $"<color={hex}>{Sanitize(text)}</color>";

    // Player, item and location names all come from the multiworld and are arbitrary text. TMP
    // starts reading a tag at '<' wherever it appears, so a name containing "</color>" would
    // close our tag early and leak its formatting into the rest of the popup. Swapping in a
    // single angle quote is the only thing that reliably stops the parser: anything that leaves
    // the '<' in place, a zero width space included, still opens a tag.
    internal static string Sanitize(string text) =>
        string.IsNullOrEmpty(text) || text.IndexOf('<') < 0 ? text : text.Replace('<', '\u2039');
}
