using System.Collections.Generic;
using I2.Loc;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Shows an in-game popup with arbitrary text.
///
/// NotificationCentre.PlayGenericNotification(locKey, flair) takes an **I2 localization key,
/// not display text**, and I2 returns null for an unregistered term with no fallback
/// (LocalizationManager.cs:1019) - so passing raw English produces a *blank* popup. Both
/// reference mods (williambsm/COTL.Archipelago, firebirdjsb/cheat-menu-cotl) pass raw English
/// and are almost certainly showing empty notifications.
///
/// The fix is to register the term at runtime first, then pass the key. Registration uses
/// SaveSource: false so we never write into the game's shipped localization asset, and the
/// translation is set for *every* language slot so the popup still reads correctly if the
/// player isn't playing in English.
/// </summary>
internal static class ApNotification
{
    private const string TermPrefix = "Archipelago/Runtime/";

    // Terms we've already registered this process. I2 lookups are dictionary-backed, but
    // AddTerm does more work than a lookup, and this also keeps the key stable per message.
    private static readonly Dictionary<string, string> registeredTerms = new();

    /// <summary>
    /// Shows <paramref name="text"/> as a game notification. Returns false if the game's
    /// notification/localization systems aren't up yet (e.g. called from the main menu).
    /// </summary>
    internal static bool Show(string text, NotificationBase.Flair flair = NotificationBase.Flair.None)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var notificationCentre = NotificationCentre.Instance;
        if (notificationCentre == null)
        {
            Log.LogWarning($"[AP] NotificationCentre not ready; dropping notification: {text}");
            return false;
        }

        var key = RegisterTerm(text);
        if (key == null)
        {
            Log.LogWarning($"[AP] Could not register localization term; dropping notification: {text}");
            return false;
        }

        // NotificationCentre dedupes by key within a frame, which is why the key is derived
        // from the message text rather than being a single shared constant.
        notificationCentre.PlayGenericNotification(key, flair);
        return true;
    }

    private static string RegisterTerm(string text)
    {
        if (registeredTerms.TryGetValue(text, out var existingKey)) return existingKey;

        if (LocalizationManager.Sources == null || LocalizationManager.Sources.Count == 0)
        {
            return null;
        }

        var source = LocalizationManager.Sources[0];
        if (source == null) return null;

        var key = TermPrefix + text.GetHashCode().ToString("X8");

        var termData = source.GetTermData(key) ?? source.AddTerm(key, eTermType.Text, SaveSource: false);
        if (termData == null) return null;

        for (var i = 0; i < termData.Languages.Length; i++)
        {
            termData.SetTranslation(i, text);
        }

        registeredTerms[text] = key;
        return key;
    }
}
