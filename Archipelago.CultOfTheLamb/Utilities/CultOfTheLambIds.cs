namespace Archipelago.CultOfTheLamb;

/// <summary>
/// Item/location ids matching items.py and locations.py's offset schemes (3_050_000 for items,
/// 3_051_000 + dict-enumeration-index for locations). The "+ N" offsets are written out so that
/// index is visible at a glance - it IS the contract with locations.py, and reordering that dict
/// silently repoints everything after the change.
///
/// TODO: replace with a real AP datapackage name-to-id lookup so the two can't drift.
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
