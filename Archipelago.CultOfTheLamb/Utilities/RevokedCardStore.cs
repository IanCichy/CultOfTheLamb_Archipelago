using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Remembers which tarot cards Archipelago has taken out of a given save and not yet given
/// back.
///
/// TarotService empties the save's collection on connect and promises to put it back on
/// disconnect. Holding that promise only in memory isn't enough: the game autosaves
/// constantly, so the file on disk is missing those cards for the whole session, and a crash,
/// an alt-F4, or a quit to the main menu before disconnecting would lose them permanently.
/// This is real player data - sixty cards someone earned before they ever installed the mod.
///
/// Keyed by **save slot only**, unlike AppliedItemStore's save+seed+slot key. "Already
/// applied" is a property of one playthrough of one seed; "we owe this save its cards back"
/// is a property of the save alone, and stays true across a different seed, a different AP
/// slot, or a reinstall.
///
/// Same sidecar-file approach as AppliedItemStore, and for the same reason: DataManager is
/// MessagePack-serialized with fixed [Key(N)] attributes, so a mod can't add a field to the
/// save without breaking its format.
/// </summary>
internal static class RevokedCardStore
{
    private static string StorePath =>
        Path.Combine(Paths.ConfigPath, "archipelago_revoked_cards.txt");

    private static string KeyFor(int saveSlot) => $"save{saveSlot}";

    /// <summary>Records what this save is owed, replacing any previous entry for it.</summary>
    internal static void Owe(int saveSlot, IEnumerable<TarotCards.Card> cards)
    {
        var names = new List<string>();
        foreach (var card in cards) names.Add(card.ToString());

        Write(KeyFor(saveSlot), string.Join(",", names.ToArray()));
    }

    /// <summary>
    /// What this save is still owed from an earlier session. Cards whose names this build of
    /// the game doesn't recognise are dropped with a warning rather than throwing - the store
    /// outlives game updates, and losing one card beats failing to return the other fifty-nine.
    /// </summary>
    internal static List<TarotCards.Card> Owed(int saveSlot)
    {
        var result = new List<TarotCards.Card>();

        if (!ReadAll().TryGetValue(KeyFor(saveSlot), out var joined)) return result;
        if (string.IsNullOrEmpty(joined)) return result;

        foreach (var name in joined.Split(','))
        {
            if (name.Length == 0) continue;

            if (!Enum.IsDefined(typeof(TarotCards.Card), name))
            {
                Log.LogWarning($"[AP] {StorePath} names a tarot card this game doesn't have: "
                    + $"'{name}' - skipping it.");
                continue;
            }

            result.Add((TarotCards.Card)Enum.Parse(typeof(TarotCards.Card), name));
        }

        return result;
    }

    /// <summary>Called once the cards are actually back in the save.</summary>
    internal static void Settle(int saveSlot) => Write(KeyFor(saveSlot), null);

    /// <summary>
    /// Pays back whatever the loaded save is owed, for the case where nothing else will.
    ///
    /// TarotService repays on disconnect and folds any leftover debt in on the next connect,
    /// which covers a player who keeps using the mod. It doesn't cover the one who crashes
    /// and then uninstalls: their save would stay short those cards forever. So this runs
    /// while *disconnected*, from the plugin's own poll - after a clean disconnect there's no
    /// entry to find, so it only ever fires on the interrupted paths.
    ///
    /// Takes the save id from the caller because folding the DLC slot variant onto its base
    /// slot is TarotService's rule, and the two must agree on the key.
    /// </summary>
    internal static void SettleIfOwed(int saveSlot)
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null) return;

        var owed = Owed(saveSlot);
        if (owed.Count == 0) return;

        foreach (var card in owed)
        {
            if (!found.Contains(card)) found.Add(card);
        }

        Settle(saveSlot);
        Log.LogInfo($"[AP] Returned {owed.Count} tarot card(s) an interrupted session still "
            + "owed this save.");
    }

    private static void Write(string key, string value)
    {
        var entries = ReadAll();

        if (value == null) entries.Remove(key);
        else entries[key] = value;

        try
        {
            var lines = new List<string>();
            foreach (var entry in entries) lines.Add($"{entry.Key}={entry.Value}");
            File.WriteAllLines(StorePath, lines.ToArray());
        }
        catch (Exception e)
        {
            // Losing the record means a crash could cost the player their cards - bad, but
            // never worth taking the session down for. Same call as AppliedItemStore makes.
            Log.LogWarning($"[AP] Could not write {StorePath}: {e.Message}");
        }
    }

    private static Dictionary<string, string> ReadAll()
    {
        var result = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(StorePath)) return result;

            foreach (var line in File.ReadAllLines(StorePath))
            {
                var split = line.IndexOf('=');
                if (split <= 0) continue;
                result[line.Substring(0, split)] = line.Substring(split + 1);
            }
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not read {StorePath}: {e.Message}");
        }
        return result;
    }
}
