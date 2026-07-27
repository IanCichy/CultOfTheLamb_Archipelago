from typing import Dict, List, NamedTuple, Optional, Set

from BaseClasses import Location

from .items import SERMON_UPGRADES

# Must not overlap worlds/cult_of_the_lamb/items.py's offset range.
location_offset = 3_051_000


class CultOfTheLambLocation(Location):
    game: str = "Cult of the Lamb"


class LocationData(NamedTuple):
    region: str
    category: str
    # Only created when the Include Woolhaven DLC option is on (see options.py).
    dlc: bool = False


# Each region path is 4 chunks (3 regular crusades against a named miniboss, then the
# Bishop crusade) plus a 5th bonus chunk (the Witness, a miniboss fight that becomes
# available after the Bishop is defeated). All names and the region/Bishop/Witness
# groupings are real - confirmed independently via the decompiled FollowerLocation enum's
# Dungeon{tier}_{region} pattern, the wiki's per-region boss rosters, and williambsm's
# COTL.Archipelago prototype's own Check enum. See
# DecompiledGamesViaDnSpy/Cotl/wiki/bishops_regions_and_dlc.md.
# Witnesses are part of the free "Relics of the Old Faith" update, not the paid Woolhaven
# DLC, so they're included unconditionally rather than behind a DLC option.
location_table: Dict[str, LocationData] = {
    "Darkwood - Amdusias": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Valefar": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Barbatos": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Leshy": LocationData("Darkwood", "Bishop"),
    "Darkwood - Witness Agares": LocationData("Darkwood", "Witness"),

    "Anura - Gusion": LocationData("Anura", "Miniboss"),
    "Anura - Eligos": LocationData("Anura", "Miniboss"),
    "Anura - Zepar": LocationData("Anura", "Miniboss"),
    "Anura - Heket": LocationData("Anura", "Bishop"),
    "Anura - Witness Bathin": LocationData("Anura", "Witness"),

    "Anchordeep - Saleos": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Haborym": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Baalzebub": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Kallamar": LocationData("Anchordeep", "Bishop"),
    "Anchordeep - Witness Astaroth": LocationData("Anchordeep", "Witness"),

    "Silk Cradle - Focalor": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Vephar": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Hauras": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Shamura": LocationData("Silk Cradle", "Bishop"),
    "Silk Cradle - Witness Allocer": LocationData("Silk Cradle", "Witness"),
}

# Sermon upgrade checks. These are deliberately *sequential* rather than named after specific
# upgrades: filling the sermon bar is one repeatable event, and which upgrade you'd have
# picked is exactly what Archipelago is randomizing away. So the Nth fill is the Nth check,
# and the named upgrades are the items (see items.py SERMON_UPGRADES).
#
# The last 6 exist only with the Woolhaven DLC, because without it there are only 32 upgrades
# to earn and the bar stops paying out - so those checks would be unreachable.
#
# They live in "Cult" (the home base) rather than a dungeon region: sermons are given at the
# Temple, so they're gated by follower count and time, not by which regions are unlocked.
for _i, (_name, _internal, _dlc) in enumerate(SERMON_UPGRADES):
    location_table[f"Sermon Upgrade {_i + 1}"] = LocationData("Cult", "Sermon", _dlc)

# Follower recruitment milestones. Counted as "ever recruited" (living + dead) rather than
# current flock size, so a plague or a sacrifice spree can't make an already-passed milestone
# unreachable again.
FOLLOWER_MILESTONE_COUNT = 20

for _n in range(1, FOLLOWER_MILESTONE_COUNT + 1):
    location_table[f"Followers Recruited {_n}"] = LocationData("Cult", "Follower")

# Append-only: ids come from enumeration order, and the C# client hardcodes the same
# offsets (see Utilities/CultOfTheLambIds.cs). Reordering this dict silently repoints every
# id after the change.
location_name_to_id: Dict[str, int] = {
    name: location_offset + i for i, name in enumerate(location_table)
}


def get_locations_for_region(
    region: str, include_dlc: bool = True, categories: Optional[Set[str]] = None
) -> List[str]:
    """Locations in a region, optionally narrowed to specific categories.

    The category filter exists because "Cult" holds several independently-toggleable blocks
    (sermon upgrades, follower milestones), and a disabled block must not create locations -
    unreachable ones would fail generation.
    """
    return [
        name for name, data in location_table.items()
        if data.region == region
        and (include_dlc or not data.dlc)
        and (categories is None or data.category in categories)
    ]
