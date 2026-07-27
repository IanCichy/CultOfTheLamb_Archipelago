using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Remembers how many received items have already been applied to a given save.
///
/// The server replays a slot's entire item history on every connect. That's correct AP
/// behaviour and the client must drain it, or items received while disconnected are lost.
/// But re-applying that history is only harmless for *idempotent* grants: unlocking a region
/// or an upgrade twice is a no-op, while Inventory.AddItem stacks. Before this existed,
/// reconnecting re-granted every resource ever received, so spamming F5 was an infinite
/// resource generator.
///
/// Keyed by **save slot** as well as AP seed and slot, because "already applied" is a
/// property of the save file, not of the client install. Loading a different save must start
/// from zero rather than skipping items that save never received.
///
/// Kept in a sidecar file next to the BepInEx config rather than inside the game save:
/// DataManager is MessagePack-serialized with fixed [Key(N)] attributes, so a mod can't add a
/// field without breaking the save format.
///
/// Known limitation: reloading an *earlier* autosave of the same slot leaves the count ahead
/// of what that save actually received, so those items are skipped. Imperfect, but far better
/// than unbounded duplication, and it matches how most AP clients behave.
/// </summary>
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
            // Losing the count means duplicate grants next reconnect - bad, but never worth
            // taking the session down for.
            Log.LogWarning($"[AP] Could not write {StorePath}: {e.Message}");
        }
    }

    /// <summary>
    /// Identity of "this playthrough": which save, on which seed, as which slot. SAVE_SLOT is
    /// read at connect time, by which point a save is loaded.
    /// </summary>
    internal static string BuildKey(string seed, int apSlot)
    {
        return $"save{SaveAndLoad.SAVE_SLOT}:{seed ?? "noseed"}:{apSlot}";
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
