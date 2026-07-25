namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Item/location ids matching worlds/cult_of_the_lamb/items.py and locations.py's
/// deterministic offset schemes (offset=3_050_000 for items, 3_051_000 + dict-enumeration-
/// index for locations). Same "hardcode ids that both sides compute the same way" pattern
/// Archipelago.RiskOfRain2 uses. TODO: revisit with a real AP datapackage name-to-id lookup
/// (Archipelago.MultiClient.Net has one) so these can't silently drift out of sync with the
/// Python side if locations.py's dict order ever changes.
/// </summary>
internal static class CultOfTheLambIds
{
    internal const long ProgressiveRegionAccessItemId = 3_050_001;

    internal const long DarkwoodLeshyLocationId = 3_051_003;
    internal const long AnuraHeketLocationId = 3_051_008;
    internal const long AnchordeepKallamarLocationId = 3_051_013;
    internal const long SilkCradleShamuraLocationId = 3_051_018;
}
