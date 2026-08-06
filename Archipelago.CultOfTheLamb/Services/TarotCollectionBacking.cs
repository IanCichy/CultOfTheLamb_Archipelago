using System;

namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The tarot collection, as a <see cref="ManagedCollection{T}"/> backing.
///
/// DataManager.Instance.PlayerFoundTrinkets is read through the property on every call rather
/// than captured once: the instance is replaced across save loads, and a stale reference would
/// have the sweep tidying a collection nothing is reading any more.
///
/// Stateless, so the plugin can build one while disconnected for
/// <see cref="ManagedCollection{T}.SettleIfOwed"/>.
/// </summary>
internal class TarotCollectionBacking : IManagedBacking<TarotCards.Card>
{
    /// <summary>Namespaces this collection's rows in the store.</summary>
    internal const string Key = "tarot";

    /// <summary>
    /// What tarot wrote before the store was generalised: a bare "saveN" with no collection
    /// prefix. Kept so a player who updates mid-session is still handed their cards back.
    /// </summary>
    internal const string LegacyKey = "save";

    internal const string Noun = "tarot card";

    public bool IsAvailable => DataManager.Instance?.PlayerFoundTrinkets != null;

    public bool Contains(TarotCards.Card value) =>
        DataManager.Instance?.PlayerFoundTrinkets?.Contains(value) == true;

    public bool Add(TarotCards.Card value)
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null || found.Contains(value)) return false;

        found.Add(value);
        return true;
    }

    public bool Remove(TarotCards.Card value) =>
        DataManager.Instance?.PlayerFoundTrinkets?.Remove(value) == true;
}
