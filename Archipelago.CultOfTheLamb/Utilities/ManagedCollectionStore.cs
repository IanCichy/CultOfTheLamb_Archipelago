using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Remembers what Archipelago took out of a save and hasn't given back yet
/// </summary>
/// <remarks>
/// Memory alone isn't enough. The game autosaves constantly, so the save on disk is missing those
/// entries all session, and a crash would lose them for good.
///
/// Keyed by collection and save slot only. Owing a save its cards back holds no matter which seed
/// or AP slot you're on.
///
/// A separate file rather than the game save, since the save format can't take new fields.
/// Entries are stored by name, so a game update that reorders the enum can't hand back the wrong
/// cards.
/// </remarks>
internal static class ManagedCollectionStore
{
    private static string StorePath =>
        Path.Combine(Paths.ConfigPath, "archipelago_revoked_cards.txt");

    private static string KeyFor(string collection, int saveSlot) => $"{collection}.save{saveSlot}";

    // Records what this save is owed from one collection, replacing its previous entry
    internal static void Owe<T>(string collection, int saveSlot, IEnumerable<T> values)
        where T : struct, Enum
    {
        var names = new List<string>();
        foreach (var value in values)
        {
            names.Add(value.ToString());
        }

        Write(KeyFor(collection, saveSlot), string.Join(",", names.ToArray()));
    }

    /// <summary>
    /// What this save is still owed from an earlier session. Names this game version doesn't know
    /// are skipped with a warning, since losing one card beats failing to return the rest.
    /// </summary>
    /// <remarks>
    /// legacyKey is the old key tarot used before this store was shared. Without it, a player who
    /// updated mid-session would be owed cards under a key nothing reads. Safe to remove once no
    /// such file can exist.
    /// </remarks>
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

        if (string.IsNullOrEmpty(joined))
        {
            return result;
        }

        foreach (var name in joined.Split(','))
        {
            if (name.Length == 0)
            {
                continue;
            }

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

    // Called once the entries are back in the save
    internal static void Settle(string collection, int saveSlot, string legacyKey = null)
    {
        Write(KeyFor(collection, saveSlot), null);
        if (legacyKey != null)
        {
            Write($"{legacyKey}{saveSlot}", null);
        }
    }

    private static void Write(string key, string value)
    {
        var entries = ReadAll();

        if (value == null)
        {
            entries.Remove(key);
        }
        else
        {
            entries[key] = value;
        }

        try
        {
            var lines = new List<string>();
            foreach (var entry in entries)
            {
                lines.Add($"{entry.Key}={entry.Value}");
            }

            File.WriteAllLines(StorePath, lines.ToArray());
        }
        catch (Exception e)
        {
            // Losing the record means a crash could cost the player their cards. That is bad, but
            // never worth taking the session down for. Same call as AppliedItemStore makes.
            Log.LogWarning($"[AP] Could not write {StorePath}: {e.Message}");
        }
    }

    private static Dictionary<string, string> ReadAll()
    {
        var result = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(StorePath))
            {
                return result;
            }

            foreach (var line in File.ReadAllLines(StorePath))
            {
                var split = line.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

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
