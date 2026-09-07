namespace Archipelago.CultOfTheLamb.Services;

/// <summary>
/// The tarot collection, as a <see cref="ManagedCollection{T}"/> backing.
/// </summary>
/// <remarks>
/// DataManager.Instance.PlayerFoundTrinkets is read through the property on every call, never
/// captured. The instance is replaced across save loads, and a stale reference would have the
/// sweep tidying a collection nothing reads any more.
///
/// Stateless, so the plugin can build one while disconnected for
/// <see cref="ManagedCollection{T}.SettleIfOwed"/>.
/// </remarks>
internal class TarotCollectionBacking : IManagedBacking<TarotCards.Card>
{
    // Namespaces this collection's rows in the store
    internal const string Key = "tarot";

    // What tarot wrote before the store was generalised, a bare "saveN" with no collection
    // prefix. Kept so a player who updates mid-session is still handed their cards back
    internal const string LegacyKey = "save";

    internal const string Noun = "tarot card";

    public bool IsAvailable => DataManager.Instance?.PlayerFoundTrinkets != null;

    public bool Contains(TarotCards.Card value) =>
        DataManager.Instance?.PlayerFoundTrinkets?.Contains(value) == true;

    public bool Add(TarotCards.Card value)
    {
        var found = DataManager.Instance?.PlayerFoundTrinkets;
        if (found == null || found.Contains(value))
        {
            return false;
        }

        found.Add(value);
        return true;
    }

    public bool Remove(TarotCards.Card value) =>
        DataManager.Instance?.PlayerFoundTrinkets?.Remove(value) == true;
}
