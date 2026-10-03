using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Remembers how many received items have already been applied to a given save.
/// </summary>
/// <remarks>
/// The key includes the save slot as well as the AP seed and slot, since "already applied" is a
/// property of the save file rather than the client install.
///
/// Known limitation: reloading an earlier autosave of the same slot leaves the count ahead of
/// what that save received, so those items are skipped.
/// </remarks>
internal static class AppliedItemStore
{
    private static string StorePath =>
        Path.Combine(Paths.ConfigPath, "archipelago_applied_items.txt");

    internal static int Get(string key)
    {
        return ReadAll().TryGetValue(key, out var count) ? count : 0;
    }

    internal static void Set(string key, int count)
    {
        var entries = ReadAll();
        entries[key] = count;
        WriteAll(entries);
    }

    private static void WriteAll(Dictionary<string, int> entries)
    {
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
            // Losing the count means duplicate grants on the next reconnect. That is bad, but it
            // is never worth taking the session down for.
            Log.LogWarning($"[AP] Could not write {StorePath}: {e.Message}");
        }
    }

    // Identity of this playthrough: which save, on which seed, as which slot.
    internal static string BuildKey(string seed, int apSlot)
    {
        return $"save{SaveSlot.Current}:{seed ?? "noseed"}:{apSlot}";
    }

    // Drops every row for a slot, so a deleted save doesn't leave a badge and an applied count
    // for whatever is created there next.
    internal static void ForgetSlot(int rawSaveSlot)
    {
        var prefix = $"save{Fold(rawSaveSlot)}:";
        var entries = ReadAll();
        var dropped = new List<string>();

        foreach (var key in entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                dropped.Add(key);
            }
        }

        if (dropped.Count == 0)
        {
            return;
        }

        foreach (var key in dropped)
        {
            entries.Remove(key);
        }

        WriteAll(entries);
        Log.LogInfo($"[AP] Save slot {rawSaveSlot} deleted - dropped {dropped.Count} applied-item "
            + "record(s) so a new save there starts clean.");
    }

    private static int Fold(int rawSaveSlot) => rawSaveSlot >= 10 ? rawSaveSlot - 10 : rawSaveSlot;

    // Whether this save has ever received an AP item, without loading it. Takes a raw slot and
    // folds Woolhaven's slot+10 down like SaveSlot.Current
    internal static bool HasHistoryFor(int rawSaveSlot)
    {
        var prefix = $"save{Fold(rawSaveSlot)}:";

        foreach (var key in CachedKeys())
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Re-read only when the store file changes, since the menu asks once per row
    private static string[] cachedKeys;
    private static DateTime cachedStamp;

    private static string[] CachedKeys()
    {
        try
        {
            var stamp = File.Exists(StorePath) ? File.GetLastWriteTimeUtc(StorePath) : DateTime.MinValue;
            if (cachedKeys != null && stamp == cachedStamp)
            {
                return cachedKeys;
            }

            cachedStamp = stamp;
            cachedKeys = new List<string>(ReadAll().Keys).ToArray();
            return cachedKeys;
        }
        catch (Exception)
        {
            return cachedKeys ?? new string[0];
        }
    }

    private static Dictionary<string, int> ReadAll()
    {
        var result = new Dictionary<string, int>();
        try
        {
            if (!File.Exists(StorePath))
            {
                return result;
            }

            foreach (var line in File.ReadAllLines(StorePath))
            {
                var split = line.LastIndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                if (int.TryParse(line.Substring(split + 1), out var count))
                {
                    result[line.Substring(0, split)] = count;
                }
            }
        }
        catch (Exception e)
        {
            Log.LogWarning($"[AP] Could not read {StorePath}: {e.Message}");
        }
        return result;
    }
}
