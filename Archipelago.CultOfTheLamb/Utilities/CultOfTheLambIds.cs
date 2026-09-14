namespace Archipelago.CultOfTheLamb;

/// <summary>
/// The one id still hardcoded on this side
/// </summary>
/// <remarks>
/// Boss location ids come through slot data (see BossKeyMapping and RegionMapping).
///
/// This one stays because it's used to recognise a received item before any seed data exists.
/// Everything else is matched by name, so don't add more here. Send them in slot data instead.
/// </remarks>
internal static class CultOfTheLambIds
{
    internal const long ProgressiveRegionAccessItemId = 3_050_001;
}
