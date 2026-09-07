using System;
using I2.Loc;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Registers localization terms at runtime so mod-authored text can be shown by game UI that
/// only accepts an I2 key.
/// </summary>
/// <remarks>
/// Several of the game's display paths take a term key rather than a string:
/// NotificationCentre.PlayGenericNotification(locKey, flair), ObjectivesData.GroupId, and
/// Objectives_Custom.Text (which looks up "Objectives/Custom/{CustomQuestType}"). I2 returns
/// null for an unregistered term and does not fall back to the key
/// (LocalizationManager.cs:1019), so raw English renders blank.
///
/// Two callers with different key schemes: ApNotification hashes the message text into a key
/// under its own prefix, QuestGuideService uses fixed keys the game computes itself. Hence the
/// explicit-key API.
/// </remarks>
internal static class I2Terms
{
    // Whether I2 is far enough up to accept a term. False early in startup and briefly during
    // some scene loads, so callers should treat it as "not yet" and retry
    internal static bool Ready =>
        LocalizationManager.Sources != null
        && LocalizationManager.Sources.Count > 0
        && LocalizationManager.Sources[0] != null;

    // Points key at text in every language slot, creating the term if it doesn't exist yet.
    // Re-registering an existing key overwrites the translation, which is how a live progress
    // counter updates. False if I2 wasn't ready or the term couldn't be created
    internal static bool Register(string key, string text)
    {
        if (string.IsNullOrEmpty(key) || !Ready)
        {
            return false;
        }

        var source = LocalizationManager.Sources[0];

        // SaveSource is false so we don't write our terms into the game's shipped localization asset.
        var termData = source.GetTermData(key) ?? source.AddTerm(key, eTermType.Text, SaveSource: false);
        if (termData == null)
        {
            return false;
        }

        // Every slot gets the same string. We have no translations, and leaving the other
        // languages null would render blank for anyone not playing in English.
        for (var i = 0; i < termData.Languages.Length; i++)
        {
            termData.SetTranslation(i, text);
        }

        return true;
    }

    /// <summary>
    /// Reads a term straight back out of I2. Only used for diagnostics, comparing this against
    /// what was registered is the one thing that distinguishes "the term never landed" from a
    /// UI fault, and both look identical in game (blank text).
    /// </summary>
    internal static string Read(string key)
    {
        try
        {
            return LocalizationManager.GetTranslation(key, true, 0, true, false, null, null, true);
        }
        catch (Exception e)
        {
            return $"<lookup threw: {e.Message}>";
        }
    }
}
