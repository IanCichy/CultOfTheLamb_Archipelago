using System;
using System.Collections.Generic;
using UnityEngine;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Shows an in-game popup with arbitrary text.
/// </summary>
/// <remarks>
/// NotificationCentre.PlayGenericNotification(locKey, flair) takes an **I2 localization key, not
/// display text**, and I2 returns null for an unregistered term with no fallback
/// (LocalizationManager.cs:1019) - so passing raw English produces a *blank* popup.
///
/// So the term is registered at runtime first - see I2Terms, which does the same job for the
/// objective guide's quest lines.
/// </remarks>
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
    /// </summary>
    /// <param name="text">The line the player reads.</param>
    /// <param name="flair">The game's own notification styling to use.</param>
    /// <param name="glow">
    /// What the popup's flair should glow. Null keeps the default AP green. See ApColors - the
    /// glow carries the *direction* of the event, which is what's readable from the corner of
    /// the eye mid-crusade, while the wording carries the detail.
    /// </param>
    /// <remarks>
    /// Holding matters more than it sounds: the game suppresses notifications outright while the
    /// HUD is hidden or NotificationsEnabled is off - cutscenes, full-screen menus, the follower
    /// recruitment flow - and PlayGenericNotification just returns silently. Those are exactly the
    /// moments checks fire, so showing immediately meant the player saw nothing for most of the
    /// checks that matter, with nothing in the log to say so.
    /// </remarks>
    internal static void Show(
        string text,
        NotificationBase.Flair flair = NotificationBase.Flair.None,
        Color32? glow = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        pending.Enqueue(new PendingNotification { Text = text, Flair = flair, Glow = glow });
        Flush();
    }

    /// <summary>
    /// Loc key -> the glow that message asked for. NotificationGeneric.Configure is handed only
    /// the key, so this is how the colour reaches the patch.
    ///
    /// Bounded by the number of distinct messages a session produces, and each entry is a key
    /// and a colour - so it's left to grow rather than evicted, the same as registeredTerms.
    /// </summary>
    private static readonly Dictionary<string, Color32> keyGlows = new();

    /// <summary>The glow registered for a key, or null if it wasn't ours or didn't ask.</summary>
    internal static Color32? GlowFor(string key) =>
        key != null && keyGlows.TryGetValue(key, out var colour) ? colour : null;

    /// <summary>
    /// Called each frame from the plugin; does nothing once the queue drains.
    ///
    /// **One per frame**, not a drain loop. Several messages routinely queue together - a check
    /// sent and the item it paid out arrive within a frame of each other, and the HUD is hidden
    /// through cutscenes like follower recruitment so a backlog builds - and firing them all
    /// into NotificationCentre in the same frame means they collide instead of queueing on
    /// screen.
    /// </summary>
    internal static void Flush()
    {
        if (pending.Count == 0) return;

        if (!CanShowNow())
        {
            LogDeferralOnce();
            return;
        }

        var next = pending.Peek();

        var key = RegisterTerm(next.Text);
        if (key == null)
        {
            // Localization isn't up yet. Leave it queued rather than dropping it - this is a
            // "not yet", the same as a hidden HUD.
            return;
        }

        pending.Dequeue();

        // Recorded before the popup is asked for, since Configure runs inside that call.
        if (next.Glow.HasValue) keyGlows[key] = next.Glow.Value;

        // The single most useful line for diagnosing "I never saw that popup": it reads the
        // term straight back out of I2 after registering it. If this logs an empty or mangled
        // translation, the popup is being drawn blank and the fault is term registration, not
        // anything upstream. Diagnosing this by inference cost three separate attempts.
        var readBack = I2Terms.Read(key);
        var matches = string.Equals(readBack, next.Text, StringComparison.Ordinal);
        Log.LogInfo($"[AP] Notification -> \"{Oneline(next.Text)}\" (key {key}, "
            + $"{pending.Count} still queued). I2 read-back "
            + (matches ? "OK." : $"MISMATCH: \"{Oneline(readBack)}\"."));

        deferralLogged = false;
        NotificationCentre.Instance.PlayGenericNotification(key, next.Flair);
    }

    private static bool deferralLogged;

    /// <summary>
    /// Says why the queue is stuck, once per stall rather than once per frame - all three of
    /// these conditions make PlayGenericNotification a silent no-op, and the game reports none
    /// of them.
    /// </summary>
    private static void LogDeferralOnce()
    {
        if (deferralLogged) return;
        deferralLogged = true;

        string reason;
        if (NotificationCentre.Instance == null) reason = "no NotificationCentre yet";
        else if (!NotificationCentre.NotificationsEnabled) reason = "notifications are disabled";
        else reason = "the HUD is hidden";

        Log.LogInfo($"[AP] Holding {pending.Count} notification(s) - {reason}. They'll show "
            + "when the game is willing.");
    }

    /// <summary>Messages are multi-line now, and a wrapped log line is hard to grep.</summary>
    private static string Oneline(string text) =>
        text == null ? "<null>" : text.Replace("\n", " | ");

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
        internal Color32? Glow;
    }

    private static readonly Queue<PendingNotification> pending = new();

    /// <summary>
    /// The key for a message, registering it with I2 the first time it's seen. Null means
    /// localization isn't up yet - a "not yet", not a failure.
    ///
    /// The key is derived from the text so the same message reuses one term, and lives under
    /// TermPrefix so NotificationStylePatch can tell our popups from the game's.
    /// </summary>
    private static string RegisterTerm(string text)
    {
        if (registeredTerms.TryGetValue(text, out var existingKey)) return existingKey;

        var key = TermPrefix + text.GetHashCode().ToString("X8");
        if (!I2Terms.Register(key, text)) return null;

        registeredTerms[text] = key;
        return key;
    }
}
