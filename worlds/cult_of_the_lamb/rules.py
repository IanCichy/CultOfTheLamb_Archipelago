from typing import TYPE_CHECKING

from worlds.generic.Rules import set_rule

from .items import PROGRESSIVE_REGION_ACCESS
from .locations import location_table

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

        if world.options.randomize_sermon_upgrades:
            set_depth_rules(world, "Sermon")
        if world.options.follower_milestone_checks:
            set_depth_rules(world, "Follower")

    # Reaching a Bishop/Witness location implies being equipped to beat them (the standard
    # AP assumption that "can reach" == "can complete"), so victory is defined by reachable
    # count rather than a separate synthetic "defeated" item.
    track = BISHOP_LOCATIONS if world.options.goal == "bishops" else WITNESS_LOCATIONS
    required = world.options.required_count.value
    multiworld.completion_condition[player] = lambda state: sum(
        1 for location_name in track.values() if state.can_reach_location(location_name, player)
    ) >= required


def set_depth_rules(world: "CultOfTheLambWorld", category: str) -> None:
    """Spread a repeatable check block across spheres instead of leaving it all reachable.

    Sermon and follower-milestone locations live in "Cult", which is free from seed start, so
    without this every one of them is sphere 1 and the fill is free to bury a region-unlock
    item at "Sermon Upgrade 38". That generates a *valid* seed and a miserable one: the only
    route to your fourth region would be filling the sermon bar 38 times, with nothing else
    to do in between, because the entire seed is a single sphere.

    Requiring progressive region access approximates the real constraint. Deep sermon levels
    and a 20-strong flock both need hours of play and opened regions - the game just doesn't
    express that in a way Archipelago can see, so we say it explicitly.

    Split into thirds, capped at 2 of the 3 available copies: requiring all 3 would leave the
    last copy with nowhere late to go, which needlessly constrains the fill.

    Only called when region access is randomized - otherwise no Progressive Bishop's Domain
    items exist and these rules would make the deeper two thirds unreachable.
    """
    player = world.player
    include_dlc = bool(world.options.include_woolhaven)

    locations = [
        name for name, data in location_table.items()
        if data.category == category and (include_dlc or not data.dlc)
    ]
    if not locations:
        return

    third = max(1, len(locations) // 3)

    for index, location_name in enumerate(locations):
        required = min(index // third, 2)
        if required == 0:
            continue

        location = world.multiworld.get_location(location_name, player)
        set_rule(
            location,
            lambda state, count=required: state.has(PROGRESSIVE_REGION_ACCESS, player, count),
        )
