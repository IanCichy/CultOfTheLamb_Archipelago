using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Reads values out of the slot data the server sends at connect.
///
/// Newtonsoft is the client library's serializer, so anything nested arrives as
/// <see cref="JObject"/> / <see cref="JArray"/> rather than native .NET collections, and every
/// scalar arrives boxed. Both facts were being rediscovered in a handful of services.
///
/// **Malformed entries are skipped, not thrown.** This all runs during connect, where an
/// exception costs the whole session rather than the one value that was wrong.
/// </summary>
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

    /// <summary>The values of a name -> location-id mapping, ignoring the names.</summary>
    internal static List<long> ParseIdValues(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new List<long>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JObject mapping) return result;

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

    /// <summary>A name -> list-of-names mapping, e.g. an item to the upgrades it grants.</summary>
    internal static Dictionary<string, List<string>> ParseNameLists(
        IReadOnlyDictionary<string, object> slotData, string key)
    {
        var result = new Dictionary<string, List<string>>();

        if (!slotData.TryGetValue(key, out var raw) || raw is not JObject mapping) return result;

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
    /// Resolves an enum member the server named, or warns and returns false.
    ///
    /// A name this build of the game doesn't have means the mod and the game disagree - most
    /// likely a seed generated against a newer apworld than the installed client. Losing one
    /// upgrade beats losing the session.
    /// </summary>
    internal static bool TryParseEnum<T>(string name, string owner, out T value) where T : struct
    {
        value = default;

        if (string.IsNullOrEmpty(name)) return false;

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
