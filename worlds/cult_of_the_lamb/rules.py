from typing import TYPE_CHECKING

from BaseClasses import LocationProgressType
from worlds.generic.Rules import set_rule

from .items import PROGRESSIVE_REGION_ACCESS
from .locations import location_table
from .regions import REGION_NAMES

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
    if world.regions_are_gated:
        for i, region_name in enumerate(world.region_order[1:], start=1):
            entrance = multiworld.get_entrance(f"Cult -> {region_name}", player)
            set_rule(entrance, lambda state, count=i: state.has(PROGRESSIVE_REGION_ACCESS, player, count))

        if world.options.randomize_sermon_upgrades:
            set_depth_rules(world, "Sermon")
        if world.options.follower_milestone_checks:
            set_depth_rules(world, "Follower")
        if world.options.snail_shrine_checks:
            set_depth_rules(world, "Snail")
        if world.options.randomize_tarot_cards:
            set_depth_rules(world, "TarotCard")

    # Deliberately outside the `regions_are_gated` block above, and deliberately not using
    # set_depth_rules: these have real logic rather than an approximated band, so they hold up
    # in an all_unlocked seed too. set_rule overwrites rather than composes, so a location can
    # only have one of the two - and the item requirement is the true one.
    if world.options.randomize_weapons:
        set_equipment_rules(world, "Weapon", world.weapons, world.starting_weapons)
    if world.options.randomize_curses:
        set_equipment_rules(world, "Curse", world.curses, world.starting_curses)

    # Reaching a Bishop/Witness location implies being equipped to beat them (the standard
    # AP assumption that "can reach" == "can complete"), so victory is defined by reachable
    # count rather than a separate synthetic "defeated" item.
    track = BISHOP_LOCATIONS if world.options.goal == "bishops" else WITNESS_LOCATIONS
    required = world.options.required_count.value
    multiworld.completion_condition[player] = lambda state: sum(
        1 for location_name in track.values() if state.can_reach_location(location_name, player)
    ) >= required


# Progressive Bishop's Domain copies in a gated seed: one per region after the free first.
# The deepest band requires all of them, which is what stops the last copy being buried.
_MAX_REGION_COPIES = len(REGION_NAMES) - 1

# Bands per repeatable block: 0, 1, 2 ... _MAX_REGION_COPIES copies required.
_BANDS = _MAX_REGION_COPIES + 1


def set_equipment_rules(world: "CultOfTheLambWorld", prefix, families, starting) -> None:
    """Gate each weapon/curse check on the item that unlocks it.

    The client never writes to WeaponPool/CursePool - it filters what the game is allowed to
    offer. So until Archipelago hands over the Axe, no podium in the game will ever put one
    down, and "Weapon - Apostate's Cleaver" genuinely cannot be checked. That is a real
    constraint, not a pacing approximation, which is why these skip the depth bands.

    It also self-locks: the fill can't put Apostate's Cleaver on its own location, because
    reaching that location requires already holding it. Archipelago handles that natively.

    Starting families are skipped - regions.py never created their locations.
    """
    player = world.player
    already = {e.display for e in starting}

    for family in families:
        if family.display in already:
            continue
        set_rule(
            world.multiworld.get_location(f"{prefix} - {family.display}", player),
            lambda state, name=family.display: state.has(name, player),
        )


def set_depth_rules(world: "CultOfTheLambWorld", category: str) -> None:
    """Spread a repeatable check block across spheres, and keep progression out of its tail.

    These blocks all live in "Cult", which is free from seed start, so without this every one
    of them is sphere 1 and the fill is free to bury a region-unlock item at "Sermon Upgrade
    32" or "Tarot Card - <something you unlock post-game>". That generates a *valid* seed and
    a miserable one: the only route to your fourth region is filling the sermon bar 32 times,
    with nothing else to do in between.

    Two mechanisms, doing two different jobs:

    **Reachability bands** approximate the real constraint - deep sermon levels and a
    20-strong flock both need hours of play and opened regions, and the game doesn't express
    that in a way Archipelago can see. Split into `_BANDS` bands requiring 0..N copies.

    The deepest band requires *all* the copies, deliberately. An earlier version capped this
    one short of the total, reasoning that requiring all of them would leave the last copy
    with nowhere late to go. That's backwards: the last copy having nowhere late to go is the
    goal. Capping it is what made "your fourth door is behind Sermon Upgrade 32" a legal seed.

    **Exclusion** does the job the bands can't. A band says a location is *unreachable*, which
    is a lie - you genuinely can reach sermon 30 with one region open, it just takes hours -
    and that lie propagates into every other player's sphere math. So the deepest band is also
    marked EXCLUDED, which says the true thing: reachable, still worth checking, but nothing
    important goes here. Progression lands on the region-gated boss and shop locations instead,
    which are real gameplay rather than grind.

    Excluded locations take filler and traps only, so they cost the fill some room for *useful*
    items too - and this game's own items are almost entirely useful. One band in four is
    comfortable; excluding half the block would leave the deep tail paying nothing but Bundles
    of Lumber.

    Only called when region access is randomized - otherwise no Progressive Bishop's Domain
    items exist and every band past the first would be genuinely unreachable.
    """
    player = world.player
    include_dlc = bool(world.options.include_woolhaven)

    # Taken in location_table order, because that order is what defines "deeper" - but
    # filtered to what this seed actually created. Tarot is the first category where those
    # differ: create_regions drops the starting cards' locations, since a card you begin with
    # can never be earned.
    created = {location.name for location in world.multiworld.get_locations(player)}

    locations = [
        name for name, data in location_table.items()
        if data.category == category
        and (include_dlc or not data.dlc)
        and name in created
    ]
    if not locations:
        return

    band_size = max(1, len(locations) // _BANDS)

    for index, location_name in enumerate(locations):
        required = min(index // band_size, _MAX_REGION_COPIES)
        location = world.multiworld.get_location(location_name, player)

        if required > 0:
            set_rule(
                location,
                lambda state, count=required: state.has(
                    PROGRESSIVE_REGION_ACCESS, player, count
                ),
            )

        if required == _MAX_REGION_COPIES:
            location.progress_type = LocationProgressType.EXCLUDED
