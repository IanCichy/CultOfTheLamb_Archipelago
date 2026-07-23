from typing import TYPE_CHECKING

from worlds.generic.Rules import set_rule

if TYPE_CHECKING:
    from . import CultOfTheLambWorld

# Darkwood is the game's tutorial region and is always reachable (confirmed via the actual
# story sequence - see DecompiledGamesViaDnSpy/Cotl/wiki/bishops_regions_and_dlc.md) - it's
# also the only always-accessible location the fill algorithm has to place the *first*
# progression item, so it must stay ungated or generation deadlocks (nothing reachable at
# game start to hold it).
REGION_ACCESS_ITEMS = {
    "Anura": "Anura Access",
    "Anchordeep": "Anchordeep Access",
    "Silk Cradle": "Silk Cradle Access",
}

# Reaching a bishop's location implies being equipped to beat them (the standard AP
# assumption that "can reach" == "can complete"), so victory is defined by reachability
# rather than a separate synthetic "defeated" item.
BISHOP_LOCATIONS = [
    "Darkwood - Leshy",
    "Anura - Heket",
    "Anchordeep - Kallamar",
    "Silk Cradle - Shamura",
]


def set_rules(world: "CultOfTheLambWorld") -> None:
    player = world.player
    multiworld = world.multiworld

    if world.options.randomize_region_access:
        for region_name, item_name in REGION_ACCESS_ITEMS.items():
            entrance = multiworld.get_entrance(f"Cult -> {region_name}", player)
            set_rule(entrance, lambda state, item=item_name: state.has(item, player))

    multiworld.completion_condition[player] = lambda state: all(
        state.can_reach_location(location_name, player) for location_name in BISHOP_LOCATIONS
    )
