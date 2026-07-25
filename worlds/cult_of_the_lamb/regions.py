from typing import TYPE_CHECKING

from BaseClasses import Region

from .locations import CultOfTheLambLocation, get_locations_for_region, location_name_to_id

if TYPE_CHECKING:
    from . import CultOfTheLambWorld

# The four base regions. Which one is free at seed start (and the unlock order of the
# other three) is randomized per-seed in CultOfTheLambWorld.generate_early - this list is
# just used to build the region graph, not to imply any fixed order.
REGION_NAMES = ["Darkwood", "Anura", "Anchordeep", "Silk Cradle"]


def create_regions(world: "CultOfTheLambWorld") -> None:
    player = world.player
    multiworld = world.multiworld

    menu = Region("Menu", player, multiworld)
    cult = Region("Cult", player, multiworld)
    multiworld.regions.append(menu)
    multiworld.regions.append(cult)
    menu.connect(cult)

    for region_name in REGION_NAMES:
        region = Region(region_name, player, multiworld)
        for location_name in get_locations_for_region(region_name):
            location = CultOfTheLambLocation(
                player, location_name, location_name_to_id[location_name], region)
            region.locations.append(location)
        multiworld.regions.append(region)
        cult.connect(region, f"Cult -> {region_name}")
