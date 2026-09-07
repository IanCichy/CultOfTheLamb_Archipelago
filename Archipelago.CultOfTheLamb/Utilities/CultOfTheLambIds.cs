namespace Archipelago.CultOfTheLamb;

/// <summary>
/// The one id still hardcoded on this side.
/// </summary>
/// <remarks>
/// The twenty boss/miniboss/Witness location ids used to live here as `3_051_000 + N`, N being
/// the row's position in locations.py's dict, so reordering that dict silently repointed every
/// boss check. They come through slot data now (see BossKeyMapping and RegionMapping).
///
/// This one stays because it is an item id used to recognise a received item before any
/// seed-specific mapping exists. Items are matched by name everywhere else, so if this ever
/// needs company, send it instead of adding to it.
/// </remarks>
internal static class CultOfTheLambIds
{
    internal const long ProgressiveRegionAccessItemId = 3_050_001;
}
