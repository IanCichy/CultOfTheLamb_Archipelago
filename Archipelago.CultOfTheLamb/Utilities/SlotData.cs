using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Reads values out of the slot data the server sends at connect
/// </summary>
/// <remarks>
/// Newtonsoft is the client library's serializer, so nested values arrive as JObject and JArray,
/// and every scalar arrives boxed.
///
/// Bad entries are skipped, not thrown. This all runs during connect, where an exception costs the
/// whole session instead of one value.
/// </remarks>
internal static class SlotData
{
    internal static bool GetBool(IReadOnlyDictionary<string, object> slotData, string key) =>
        slotData.TryGetValue(key, out var value) && Convert.ToBoolean(value);

    internal static long GetLong(IReadOnlyDictionary<string, object> slotData, string key) =>
        slotData.TryGetValue(key, out var value) ? Convert.ToInt64(value) : 0L;

    internal static float GetFloat(IReadOnlyDictionary<string, object> slotData, string key) =>
        slotData.TryGetValue(key, out var value) ? Convert.ToSingle(value) : 0f;

    internal static string GetString(IReadOnlyDictionary<string, object> slotData, string key) =>
        slotData.TryGetValue(key, out var value) ? value?.ToString() : null;

    // The values of a name -> location-id mapping, ignoring the names
    internal static List<long> ParseIdValues(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new List<long>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JObject mapping)
        {
            return result;
        }

        foreach (var entry in mapping)
        {
            try
            {
                result.Add(entry.Value.ToObject<long>());
            }
            catch (Exception)
            {
                // Already warned about by whichever service owns this mapping.
            }
        }

        return result;
    }

    // A name -> list-of-names mapping, e.g. an item to the upgrades it grants
    internal static Dictionary<string, List<string>> ParseNameLists(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new Dictionary<string, List<string>>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JObject mapping)
        {
            return result;
        }

        foreach (var entry in mapping)
        {
            if (entry.Value is JArray names)
            {
                result[entry.Key] = names.ToObject<List<string>>();
            }
        }

        return result;
    }

    /// <summary>
    /// A name -> location-id mapping, keeping the names. ParseIdValues throws the keys away;
    /// several blocks need them to decide *which* thing a given id belongs to.
    /// </summary>
    internal static Dictionary<string, long> ParseIdMap(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new Dictionary<string, long>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JObject mapping)
        {
            Log.LogWarning($"[AP] Slot data has no '{key}' mapping.");
            return result;
        }

        foreach (var entry in mapping)
        {
            try
            {
                result[entry.Key] = entry.Value.ToObject<long>();
            }
            catch (Exception e)
            {
                Log.LogWarning($"[AP] '{key}' has a non-numeric id for '{entry.Key}': "
                    + $"{e.Message} - skipping it.");
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves an enum member the server named, or warns and returns false.
    ///
    /// A name this build of the game doesn't have means the mod and the game disagree. Most
    /// likely a seed generated against a newer apworld than the installed client. Losing one
    /// upgrade beats losing the session.
    /// </summary>
    internal static bool TryParseEnum<T>(string name, string owner, out T value) where T : struct
    {
        value = default;

        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (!Enum.IsDefined(typeof(T), name))
        {
            Log.LogWarning($"[AP] Slot data names a {typeof(T).Name} this game doesn't have: "
                + $"'{name}' - skipping it for '{owner}'.");
            return false;
        }

        value = (T)Enum.Parse(typeof(T), name);
        return true;
    }
}
