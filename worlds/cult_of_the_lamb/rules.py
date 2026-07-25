from typing import TYPE_CHECKING

from worlds.generic.Rules import set_rule

from .items import PROGRESSIVE_REGION_ACCESS

if TYPE_CHECKING:
    from . import CultOfTheLambWorld

# Region -> Bishop / Witness location names, confirmed real (see locations.py header).
BISHOP_LOCATIONS = {
    "Darkwood": "Darkwood - Leshy",
    "Anura": "Anura - Heket",
    "Anchordeep": "Anchordeep - Kallamar",
    "Silk Cradle": "Silk Cradle - Shamura",
}
WITNESS_LOCATIONS = {
    "Darkwood": "Darkwood - Witness Agares",
    "Anura": "Anura - Witness Bathin",
    "Anchordeep": "Anchordeep - Witness Astaroth",
    "Silk Cradle": "Silk Cradle - Witness Allocer",
}


def set_rules(world: "CultOfTheLambWorld") -> None:
    player = world.player
    multiworld = world.multiworld

    # world.region_order[0] is free (set in generate_early); each region after that needs
    # one more copy of the progressive access item than the one before it.
    if world.options.randomize_region_access:
        for i, region_name in enumerate(world.region_order[1:], start=1):
            entrance = multiworld.get_entrance(f"Cult -> {region_name}", player)
            set_rule(entrance, lambda state, count=i: state.has(PROGRESSIVE_REGION_ACCESS, player, count))

    # Reaching a Bishop/Witness location implies being equipped to beat them (the standard
    # AP assumption that "can reach" == "can complete"), so victory is defined by reachable
    # count rather than a separate synthetic "defeated" item.
    track = BISHOP_LOCATIONS if world.options.goal == "bishops" else WITNESS_LOCATIONS
    required = world.options.required_count.value
    multiworld.completion_condition[player] = lambda state: sum(
        1 for location_name in track.values() if state.can_reach_location(location_name, player)
    ) >= required
