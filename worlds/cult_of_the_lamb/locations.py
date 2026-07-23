from typing import Dict, List, NamedTuple

from BaseClasses import Location

# Must not overlap worlds/cult_of_the_lamb/items.py's offset range.
location_offset = 3_051_000


class CultOfTheLambLocation(Location):
    game: str = "Cult of the Lamb"


class LocationData(NamedTuple):
    region: str
    category: str


# Starter set: a handful of locations per region so regions.py/rules.py have something
# real to attach to. "Follower Rescue N" are still generic placeholders (crusade encounters
# are procedurally generated per-run, so there's no fixed "rescue spot" to name - TODO
# replace with whatever check-worthy events actually make sense for a randomized dungeon,
# e.g. per-run milestones rather than named locations). Bishop and Witness fights ARE real,
# named, one-time story beats confirmed via DecompiledGamesViaDnSpy/Cotl/wiki/ - safe to
# treat as fixed locations. See docs/architecture.md.
location_table: Dict[str, LocationData] = {
    # Region -> Bishop -> Witness (post-Bishop miniboss), confirmed via the game's actual
    # story/boss sequence (DecompiledGamesViaDnSpy/Cotl/wiki/bishops_regions_and_dlc.md):
    # Darkwood/Leshy/Agares -> Anura/Heket/Bathin -> Anchordeep/Kallamar/Astaroth ->
    # Silk Cradle/Shamura/Allocer.
    "Darkwood - Follower Rescue 1": LocationData("Darkwood", "Follower"),
    "Darkwood - Follower Rescue 2": LocationData("Darkwood", "Follower"),
    "Darkwood - Leshy": LocationData("Darkwood", "Bishop"),
    "Darkwood - Witness Agares": LocationData("Darkwood", "Witness"),

    "Anura - Follower Rescue 1": LocationData("Anura", "Follower"),
    "Anura - Follower Rescue 2": LocationData("Anura", "Follower"),
    "Anura - Heket": LocationData("Anura", "Bishop"),
    "Anura - Witness Bathin": LocationData("Anura", "Witness"),

    "Anchordeep - Follower Rescue 1": LocationData("Anchordeep", "Follower"),
    "Anchordeep - Follower Rescue 2": LocationData("Anchordeep", "Follower"),
    "Anchordeep - Kallamar": LocationData("Anchordeep", "Bishop"),
    "Anchordeep - Witness Astaroth": LocationData("Anchordeep", "Witness"),

    "Silk Cradle - Follower Rescue 1": LocationData("Silk Cradle", "Follower"),
    "Silk Cradle - Follower Rescue 2": LocationData("Silk Cradle", "Follower"),
    "Silk Cradle - Shamura": LocationData("Silk Cradle", "Bishop"),
    "Silk Cradle - Witness Allocer": LocationData("Silk Cradle", "Witness"),
}

location_name_to_id: Dict[str, int] = {
    name: location_offset + i for i, name in enumerate(location_table)
}


def get_locations_for_region(region: str) -> List[str]:
    return [name for name, data in location_table.items() if data.region == region]
