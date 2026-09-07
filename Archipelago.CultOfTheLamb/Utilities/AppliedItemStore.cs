using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Remembers how many received items have already been applied to a given save.
/// </summary>
/// <remarks>
/// The server replays a slot's entire item history on every connect, and the client must drain
/// it or lose items received while disconnected. Re-applying it is only harmless for idempotent
/// grants: Inventory.AddItem stacks, so before this existed spamming F5 was an infinite resource
/// generator.
///
/// The key includes the save slot as well as the AP seed and slot, because "already applied" is a
/// property of the save file, not of the client install. It lives in a sidecar file rather than
/// the game save, because DataManager is MessagePack-serialized with fixed [Key(N)] attributes.
///
/// One known limitation. Reloading an earlier autosave of the same slot leaves the count ahead
/// of what that save received, so those items are skipped. That is better than unbounded
/// duplication, and it matches how most AP clients behave.
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

        try
        {
            var lines = new List<string>();
            foreach (var entry in entries) lines.Add($"{entry.Key}={entry.Value}");
            File.WriteAllLines(StorePath, lines.ToArray());
        }
        catch (Exception e)
        {
            // Losing the count means duplicate grants on the next reconnect. That is bad, but it
            // is never worth taking the session down for.
            Log.LogWarning($"[AP] Could not write {StorePath}: {e.Message}");
        }
    }

    /// <summary>
    /// Identity of "this playthrough": which save, on which seed, as which slot.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="SaveSlot.Current"/> rather than the raw SAVE_SLOT: the game parks a
    /// Woolhaven save at slot+10 and moves SAVE_SLOT between the two while writing, so the raw
    /// value can key the same save two different ways within one session. A key that misses reads
    /// the count as 0 and re-applies the entire item history.
    /// </remarks>
    /// <param name="seed">The multiworld's seed name, or "noseed" when there isn't one.</param>
    /// <param name="apSlot">The Archipelago slot this client is playing.</param>
    /// <returns>The store key for the loaded save on this seed and slot.</returns>
    internal static string BuildKey(string seed, int apSlot)
    {
        return $"save{SaveSlot.Current}:{seed ?? "noseed"}:{apSlot}";
    }

    private static Dictionary<string, int> ReadAll()
    {
        var result = new Dictionary<string, int>();
        try
        {
            if (!File.Exists(StorePath)) return result;

            foreach (var line in File.ReadAllLines(StorePath))
            {
                var split = line.LastIndexOf('=');
                if (split <= 0) continue;
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
