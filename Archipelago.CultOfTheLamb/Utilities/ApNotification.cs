using System;
using System.Collections.Generic;
using UnityEngine;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Shows an in-game popup with arbitrary text.
/// </summary>
/// <remarks>
/// NotificationCentre.PlayGenericNotification(locKey, flair) takes an I2 localization key, not
/// display text, and I2 returns null for an unregistered term with no fallback
/// (LocalizationManager.cs:1019), so raw English produces a blank popup.
///
/// The term is registered at runtime first. See I2Terms.
/// </remarks>
internal static class ApNotification
{
    // Internal because it is also how NotificationStylePatch tells our popups from the game's.
    // Configure only ever sees the loc key, never the display text.
    internal const string TermPrefix = "Archipelago/Runtime/";

    // Terms we've already registered this process. I2 lookups are dictionary-backed, but
    // AddTerm does more work than a lookup, and this also keeps the key stable per message.
    private static readonly Dictionary<string, string> registeredTerms = new();

    // Shows text as a game notification, or holds it until the game is willing to show one.
    // glow is the flair colour, null keeping the default AP green. See ApColors.
    //
    // The game suppresses notifications outright while the HUD is hidden or NotificationsEnabled
    // is off, covering cutscenes, full-screen menus and follower recruitment, and
    // PlayGenericNotification just returns silently. Those are exactly the moments checks fire
    internal static void Show(
        string text,
        NotificationBase.Flair flair = NotificationBase.Flair.None,
        Color32? glow = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        pending.Enqueue(new PendingNotification { Text = text, Flair = flair, Glow = glow });
        Flush();
    }

    // Loc key -> the glow that message asked for. NotificationGeneric.Configure is handed only
    // the key, so this is how the colour reaches the patch. Bounded by the number of distinct
    // messages a session produces, so it is left to grow rather than evicted
    private static readonly Dictionary<string, Color32> keyGlows = new();

    // The glow registered for a key, or null if it wasn't ours or didn't ask
    internal static Color32? GlowFor(string key) =>
        key != null && keyGlows.TryGetValue(key, out var colour) ? colour : null;

    // One per frame, not a drain loop. Messages routinely queue together, and firing them all
    // into NotificationCentre in the same frame makes them collide instead of queueing on
    // screen
    internal static void Flush()
    {
        if (pending.Count == 0)
        {
            return;
        }

        if (!CanShowNow())
        {
            LogDeferralOnce();
            return;
        }

        var next = pending.Peek();

        var key = RegisterTerm(next.Text);
        if (key == null)
        {
            // Localization isn't up yet. Leave it queued rather than dropping it, because this
            // is a "not yet", the same as a hidden HUD.
            return;
        }

        pending.Dequeue();

        // Recorded before the popup is asked for, since Configure runs inside that call.
        if (next.Glow.HasValue)
        {
            keyGlows[key] = next.Glow.Value;
        }

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

    // Why the queue is stuck, once per stall rather than once per frame. All three conditions
    // make PlayGenericNotification a silent no-op and the game reports none of them
    private static void LogDeferralOnce()
    {
        if (deferralLogged)
        {
            return;
        }

        deferralLogged = true;

        string reason;
        if (NotificationCentre.Instance == null)
        {
            reason = "no NotificationCentre yet";
        }
        else if (!NotificationCentre.NotificationsEnabled)
        {
            reason = "notifications are disabled";
        }
        else
        {
            reason = "the HUD is hidden";
        }

        Log.LogInfo($"[AP] Holding {pending.Count} notification(s) - {reason}. They'll show "
            + "when the game is willing.");
    }

    // Messages are multi-line now, and a wrapped log line is hard to grep
    private static string Oneline(string text) =>
        text == null ? "<null>" : text.Replace("\n", " | ");

    // Whether the game would display one right now
    private static bool CanShowNow()
    {
        if (NotificationCentre.Instance == null)
        {
            return false;
        }

        if (!NotificationCentre.NotificationsEnabled)
        {
            return false;
        }

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

    // Registers the message with I2 the first time it's seen. Null means localization isn't up
    // yet, which is a "not yet" rather than a failure. The key is derived from the text so the
    // same message reuses one term, under TermPrefix so NotificationStylePatch can spot ours
    private static string RegisterTerm(string text)
    {
        if (registeredTerms.TryGetValue(text, out var existingKey))
        {
            return existingKey;
        }

        var key = TermPrefix + text.GetHashCode().ToString("X8");
        if (!I2Terms.Register(key, text))
        {
            return null;
        }

        registeredTerms[text] = key;
        return key;
    }
}
