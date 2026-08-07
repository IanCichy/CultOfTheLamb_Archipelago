using System.Collections.Generic;
using I2.Loc;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Shows an in-game popup with arbitrary text.
///
/// NotificationCentre.PlayGenericNotification(locKey, flair) takes an **I2 localization key, not
/// display text**, and I2 returns null for an unregistered term with no fallback
/// (LocalizationManager.cs:1019) - so passing raw English produces a *blank* popup.
///
/// So the term is registered at runtime first, with SaveSource: false to avoid writing into the
/// game's shipped asset, and translated into every language slot.
/// </summary>
internal static class ApNotification
{
    // Internal because it is also how NotificationStylePatch tells our popups from the game's:
    // Configure only ever sees the loc key, never the display text.
    internal const string TermPrefix = "Archipelago/Runtime/";

    // Terms we've already registered this process. I2 lookups are dictionary-backed, but
    // AddTerm does more work than a lookup, and this also keeps the key stable per message.
    private static readonly Dictionary<string, string> registeredTerms = new();

    /// <summary>
    /// Shows <paramref name="text"/> as a game notification, or holds it until the game is
    /// willing to show one.
    ///
    /// Holding matters more than it sounds. The game suppresses notifications outright while
    /// the HUD is hidden or NotificationsEnabled is off - cutscenes, full-screen menus, the
    /// follower recruitment flow - and PlayGenericNotification just returns silently in that
    /// state. Those are exactly the moments checks fire: recruiting a follower, killing a
    /// boss, finishing a ritual. Showing immediately meant the player saw nothing for most of
    /// the checks that matter, with nothing in the log to say so.
    /// </summary>
    internal static void Show(string text, NotificationBase.Flair flair = NotificationBase.Flair.None)
    {
        if (string.IsNullOrEmpty(text)) return;

        pending.Enqueue(new PendingNotification { Text = text, Flair = flair });
        Flush();
    }

    /// <summary>Called each frame from the plugin; does nothing once the queue drains.</summary>
    internal static void Flush()
    {
        while (pending.Count > 0 && CanShowNow())
        {
            var next = pending.Peek();

            var key = RegisterTerm(next.Text);
            if (key == null)
            {
                // Localization isn't up yet. Leave it queued rather than dropping it - this is
                // a "not yet", the same as a hidden HUD.
                return;
            }

            pending.Dequeue();

            // NotificationCentre dedupes by key within a frame, which is why the key is derived
            // from the message text rather than being a single shared constant.
            NotificationCentre.Instance.PlayGenericNotification(key, next.Flair);
        }
    }

    /// <summary>
    /// Whether the game would actually display one right now. All three conditions make
    /// PlayGenericNotification a no-op, and it reports none of them.
    /// </summary>
    private static bool CanShowNow()
    {
        if (NotificationCentre.Instance == null) return false;
        if (!NotificationCentre.NotificationsEnabled) return false;

        var hud = HUD_Manager.Instance;
        return hud == null || !hud.Hidden;
    }

    private struct PendingNotification
    {
        internal string Text;
        internal NotificationBase.Flair Flair;
    }

    private static readonly Queue<PendingNotification> pending = new();

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
