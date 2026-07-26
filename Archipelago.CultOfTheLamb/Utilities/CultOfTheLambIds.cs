namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Item/location ids matching worlds/cult_of_the_lamb/items.py and locations.py's
/// deterministic offset schemes (offset=3_050_000 for items, 3_051_000 + dict-enumeration-
/// index for locations). Same "hardcode ids that both sides compute the same way" pattern
/// Archipelago.RiskOfRain2 uses. TODO: revisit with a real AP datapackage name-to-id lookup
/// (Archipelago.MultiClient.Net has one) so these can't silently drift out of sync with the
/// Python side if locations.py's dict order ever changes.
///
/// The "+ N" offsets below are written out explicitly so the enumeration index each id
/// depends on is visible at a glance - that index IS the contract with locations.py, and it
/// is the thing that silently breaks if that dict is ever reordered.
/// </summary>
internal static class CultOfTheLambIds
{
    internal const long ProgressiveRegionAccessItemId = 3_050_001;

    private const long LocationIdBase = 3_051_000;

    // Darkwood (Leshy)
    internal const long DarkwoodAmdusiasLocationId = LocationIdBase + 0;
    internal const long DarkwoodValefarLocationId = LocationIdBase + 1;
    internal const long DarkwoodBarbatosLocationId = LocationIdBase + 2;
    internal const long DarkwoodLeshyLocationId = LocationIdBase + 3;
    internal const long DarkwoodWitnessAgaresLocationId = LocationIdBase + 4;

    // Anura (Heket)
    internal const long AnuraGusionLocationId = LocationIdBase + 5;
    internal const long AnuraEligosLocationId = LocationIdBase + 6;
    internal const long AnuraZeparLocationId = LocationIdBase + 7;
    internal const long AnuraHeketLocationId = LocationIdBase + 8;
    internal const long AnuraWitnessBathinLocationId = LocationIdBase + 9;

    // Anchordeep (Kallamar)
    internal const long AnchordeepSaleosLocationId = LocationIdBase + 10;
    internal const long AnchordeepHaborymLocationId = LocationIdBase + 11;
    internal const long AnchordeepBaalzebubLocationId = LocationIdBase + 12;
    internal const long AnchordeepKallamarLocationId = LocationIdBase + 13;
    internal const long AnchordeepWitnessAstarothLocationId = LocationIdBase + 14;

    // Silk Cradle (Shamura)
    internal const long SilkCradleFocalorLocationId = LocationIdBase + 15;
    internal const long SilkCradleVepharLocationId = LocationIdBase + 16;
    internal const long SilkCradleHaurasLocationId = LocationIdBase + 17;
    internal const long SilkCradleShamuraLocationId = LocationIdBase + 18;
    internal const long SilkCradleWitnessAllocerLocationId = LocationIdBase + 19;
}
