using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Remembers what Archipelago has taken out of a given save and not yet given back. Holding
/// that promise in memory alone isn't enough: the game autosaves constantly, so the file on disk
/// is missing those entries for the whole session, and a crash or an alt-F4 would lose real
/// player data permanently.
///
/// Keyed by **collection and save slot only**, unlike AppliedItemStore's save+seed+slot key -
/// "we owe this save its cards back" is a property of the save alone, true across a different
/// seed, a different AP slot, or a reinstall.
///
/// Sidecar file rather than the game save, because DataManager is MessagePack-serialized with
/// fixed [Key(N)] attributes. Entries are stored as enum *names*: a name survives the game
/// reordering an enum between updates, where a shifted ordinal would silently hand back the
/// wrong cards.
/// </summary>
internal static class ManagedCollectionStore
{
    private static string StorePath =>
        Path.Combine(Paths.ConfigPath, "archipelago_revoked_cards.txt");

    private static string KeyFor(string collection, int saveSlot) => $"{collection}.save{saveSlot}";

    /// <summary>Records what this save is owed from one collection, replacing its previous entry.</summary>
    internal static void Owe<T>(string collection, int saveSlot, IEnumerable<T> values)
        where T : struct, Enum
    {
        var names = new List<string>();
        foreach (var value in values) names.Add(value.ToString());

        Write(KeyFor(collection, saveSlot), string.Join(",", names.ToArray()));
    }

    /// <summary>
    /// What this save is still owed from an earlier session. Entries whose names this build of
    /// the game doesn't recognise are dropped with a warning rather than throwing - the store
    /// outlives game updates, and losing one card beats failing to return the other fifty-nine.
    ///
    /// <paramref name="legacyKey"/> is an older, un-namespaced key to fall back to when the
    /// namespaced one is absent. Tarot shipped before this store was generalised and wrote a
    /// bare "saveN", so without the fallback a player who updated mid-session would be owed
    /// cards under a key nothing reads any more. Safe to delete once no such file can exist.
    /// </summary>
    internal static List<T> Owed<T>(string collection, int saveSlot, string legacyKey = null)
        where T : struct, Enum
    {
        var result = new List<T>();
        var entries = ReadAll();

        if (!entries.TryGetValue(KeyFor(collection, saveSlot), out var joined)
            && (legacyKey == null || !entries.TryGetValue($"{legacyKey}{saveSlot}", out joined)))
        {
            return result;
        }

        if (string.IsNullOrEmpty(joined)) return result;

        foreach (var name in joined.Split(','))
        {
            if (name.Length == 0) continue;

            if (!Enum.IsDefined(typeof(T), name))
            {
                Log.LogWarning($"[AP] {StorePath} names a {typeof(T).Name} this game doesn't "
                    + $"have: '{name}' - skipping it.");
                continue;
            }

            result.Add((T)Enum.Parse(typeof(T), name));
        }

        return result;
    }

    /// <summary>Called once the entries are actually back in the save.</summary>
    internal static void Settle(string collection, int saveSlot, string legacyKey = null)
    {
        Write(KeyFor(collection, saveSlot), null);
        if (legacyKey != null) Write($"{legacyKey}{saveSlot}", null);
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
