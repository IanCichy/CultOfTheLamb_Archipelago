from typing import TYPE_CHECKING

from BaseClasses import LocationProgressType
from worlds.generic.Rules import add_rule, set_rule

from .items import (
    BUILDINGS, DI_GATE_ITEM, PROGRESSIVE_REGION_ACCESS, TAROT_CARDS,
)
from .locations import SNAIL_SHRINE_COUNT, location_table
from .regions import REGION_NAMES

# The progressive item the curated Divine Inspiration block gates on, and how many copies exist.
# Both come off the table rather than being restated: a rule naming an item that doesn't exist is
# silently never satisfiable, and a retiered family would otherwise leave the count stale and
# quietly under-gate the block.
PROGRESSIVE_CULT = DI_GATE_ITEM.item_name
_DI_CULT_TIERS = len(DI_GATE_ITEM.upgrades)

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

    # world.region_order[0] is free, set in generate_early. Each region after that needs one
    # more copy of the progressive access item than the one before it.
    if world.regions_are_gated:
        for i, region_name in enumerate(world.region_order[1:], start=1):
            entrance = multiworld.get_entrance(f"Cult -> {region_name}", player)
            set_rule(entrance,
                     lambda state, count=i: state.has(PROGRESSIVE_REGION_ACCESS, player, count))

        if world.options.randomize_sermon_upgrades:
            set_depth_rules(world, "Sermon")
        if world.options.follower_milestone_checks:
            set_depth_rules(world, "Follower")
        if world.options.randomize_tarot_cards:
            set_depth_rules(world, "TarotCard")

        # Bands in every mode. The checks fire on filling the Devotion meter, which is pure
        # play time. No item this world hands out makes it fill faster, so there is nothing to
        # write a real rule against, in any mode.
        #
        # exclude_tail=False, uniquely. Two reasons, and the first is a hard constraint:
        #
        # - At 69 locations the deepest band is 18 of them, and this block brings exactly as
        #   many items as it has locations (69 points, or 69 techs), so it contributes no
        #   filler of its own to pay for them. Excluding the tail overdraws the seed's filler
        #   and generation fails outright with "not enough filler items for excluded
        #   locations".
        # - Nothing can be blocked down there anyway. The deepest band already requires every
        #   Progressive Bishop's Domain copy, so an item placed at "Divine Inspiration 65" can't
        #   gate anything the player hasn't already reached. It would only be tedious rather
        #   than unwinnable, and the band rule alone keeps it out of the early spheres.
        if world.divine_inspiration_enabled and not world.divine_inspiration_is_curated:
            set_depth_rules(world, "DivineInspiration", exclude_tail=False)

        # Where the player picks the techs, the multiworld can't know which buildings exist, so
        # they're banded by real tier (BUG-010). The modes that hand out techs get a real rule.
        if world.options.building_checks and not world.building_tech_requirements:
            set_building_depth_rules(world)

        if world.options.broom_checks:
            set_depth_rules(world, "Broom")

    # Not gated on regions_are_gated: the rule asks for the Bishops, so it holds in an
    # all_unlocked seed too.
    if world.goal_reaches_all_bishops:
        set_woolhaven_rules(world)

    # Outside the gated block, since a tech item is a real requirement in an all_unlocked seed too.
    set_building_tech_rules(world)

    # Tarot shops need only their own region, which the region entrance already says. Finding the
    # hub costs runs, not an item, so anything stricter would be a guess.
    set_unmodelled_card_rules(world)

    if world.options.snail_shrine_checks:
        set_snail_shrine_rules(world)

    # Outside the block above for the same reason as the equipment rules below: curated_checks
    # gates on Progressive Cult, which exists in every curated seed regardless of how region
    # access is set, so unlike a depth band it stays true in an all_unlocked seed.
    if world.divine_inspiration_is_curated:
        set_curated_di_rules(world)

    # Outside the `regions_are_gated` block above on purpose, and not using
    # set_depth_rules: these have real logic rather than an approximated band, so they hold up
    # in an all_unlocked seed too. set_rule overwrites rather than composes, so a location can
    # only have one of the two, and the item requirement is the true one.
    if world.options.randomize_weapons:
        set_equipment_rules(world, "Weapon", world.weapons, world.starting_weapons)
    if world.options.randomize_curses:
        set_equipment_rules(world, "Curse", world.curses, world.starting_curses)


    # Reaching a Bishop/Witness location implies being equipped to beat them (the standard
    # AP assumption that "can reach" == "can complete"), so victory is defined by reachable
    # count rather than a separate synthetic "defeated" item.
    if world.options.goal == "narinder":
        # Narinder has no location of his own, because a check that only fires at the win
        # condition pays nothing. The Gateway opens only after all four Bishops are dead, so
        # requiring every Bishop location is the same statement, and it already implies every
        # region.
        multiworld.completion_condition[player] = lambda state: all(
            state.can_reach_location(location_name, player)
            for location_name in BISHOP_LOCATIONS.values()
        )
        return

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

# Divine Inspiration tier -> domain copies a building check needs. Five tiers but only three
# copies, so the top two share the deepest band.
_BUILDING_TIER_COPIES = {1: 0, 2: 1, 3: 2, 4: 3, 5: 3}

# Cards whose region is only the first of several gates, none of them items. Excluded rather than
# ruled. Internal names, matched against TAROT_CARDS.
_UNMODELLED_REGION_CARDS = {"NeptunesCurse"}

# The only region whose dungeons have the snails that drop Shells.
SHELL_REGION = "Anura"


def set_equipment_rules(world: "CultOfTheLambWorld", prefix, families, starting) -> None:
    """Gate each weapon/curse check on the item that unlocks it.

    The client never writes to WeaponPool or CursePool. It filters what the game is allowed to
    offer. So until Archipelago hands over the Axe, no podium in the game will ever put one
    down, and "Weapon - Apostate's Cleaver" genuinely cannot be checked. That is a real
    constraint, not a pacing approximation, which is why these skip the depth bands.

    It also self-locks: the fill can't put Apostate's Cleaver on its own location, because
    reaching that location requires already holding it. Archipelago handles that natively.

    Starting families are skipped, because regions.py never created their locations.
    """
    player = world.player
    already = {e.display for e in starting}

    for family in families:
        if family.display in already:
            continue
        set_rule(
            # The location keeps the game's own name. The rule names the AP item, which carries
            # its category prefix. They are deliberately different strings.
            world.multiworld.get_location(f"{prefix} - {family.display}", player),
            lambda state, name=family.item_name: state.has(name, player),
        )


def set_depth_rules(
    world: "CultOfTheLambWorld", category: str, exclude_tail: bool = True
) -> None:
    """Spread a repeatable check block across spheres, and keep progression out of its tail.

    These blocks all live in "Cult", which is free from seed start, so without this every one
    of them is sphere 1 and the fill is free to bury a region-unlock item at "Sermon Upgrade
    32" or "Tarot Card - <something you unlock post-game>". That generates a *valid* seed and a
    miserable one. The only route to your fourth region is filling the sermon bar 32 times, with
    nothing else to do in between.

    Two mechanisms, doing two different jobs:

    **Reachability bands** approximate the real constraint. Deep sermon levels and a 20-strong
    flock both need hours of play and opened regions, and the game doesn't express that in a way
    Archipelago can see. Split into `_BANDS` bands requiring 0..N copies.

    The deepest band requires *all* the copies, deliberately. The last copy having nowhere late
    to go is the goal. Capping it short of the total is what made "your fourth door is behind
    Sermon Upgrade 32" a legal seed.

    **Exclusion** does the job the bands can't. A band says a location is *unreachable*, which is
    a lie. You genuinely can reach sermon 30 with one region open, it just takes hours, and that
    lie propagates into every other player's sphere math. So the deepest band is also marked
    EXCLUDED, which says the true thing. It is reachable and still worth checking, but nothing
    important goes here. Progression lands on the region-gated boss and shop locations instead,
    which are real gameplay rather than grind.

    Excluded locations take filler and traps only, so they cost the fill some room for *useful*
    items too, and this game's own items are almost entirely useful. One band in four is
    comfortable. Excluding half the block would leave the deep tail paying nothing but Bundles
    of Lumber.

    Only called when region access is randomized. Otherwise no Progressive Bishop's Domain items
    exist and every band past the first would be genuinely unreachable.
    """
    player = world.player
    include_dlc = bool(world.options.include_woolhaven)

    # Taken in location_table order, because that order is what defines "deeper", then
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

    # Base-game locations are banded on the base-game count alone, so turning Woolhaven on doesn't
    # make them shallower. DLC ones keep their place in the whole list, as before.
    base_count = sum(1 for name in locations if not location_table[name].dlc)
    base_band = max(1, base_count // _BANDS)
    full_band = max(1, len(locations) // _BANDS)
    base_index = 0

    for index, location_name in enumerate(locations):
        if location_table[location_name].dlc:
            required = min(index // full_band, _MAX_REGION_COPIES)
        else:
            required = min(base_index // base_band, _MAX_REGION_COPIES)
            base_index += 1
        location = world.multiworld.get_location(location_name, player)

        if required > 0:
            set_rule(
                location,
                lambda state, count=required: state.has(
                    PROGRESSIVE_REGION_ACCESS, player, count
                ),
            )

        if required == _MAX_REGION_COPIES and exclude_tail:
            location.progress_type = LocationProgressType.EXCLUDED


def set_curated_di_rules(world: "CultOfTheLambWorld") -> None:
    """Gate the deeper Divine Inspiration checks on Progressive Cult, in curated_checks only.

    A real item requirement in place of an approximated band, and the same trade
    set_equipment_rules already makes: every check here is a filled Devotion meter, and each Cult
    tier raises both the Shrine's capacity and how many Followers can pray, so the Cult tier held
    is roughly the rate at which this block pays out. Without it, the item governing that rate can
    legally be the last thing found.

    This is still soft logic, since a patient player can grind the deep checks at Cult I. It is
    aimed at something real rather than at a guess about elapsed time.

    No excluded tail: the block brings no filler of its own at these lengths, so excluding the
    deepest quarter overdraws the seed and generation fails outright.
    """
    player = world.player
    created = {location.name for location in world.multiworld.get_locations(player)}

    locations = [
        name
        for n in range(1, world.divine_inspiration_location_count + 1)
        if (name := f"Divine Inspiration {n}") in created
    ]
    if not locations:
        return

    # Quarters, so the requirement tracks the block's length rather than a fixed cutoff. The
    # length is a YAML range and can be anything from 5 to 69.
    band_size = max(1, len(locations) // 4)

    for index, location_name in enumerate(locations):
        required = min(index // band_size, _DI_CULT_TIERS)
        if required == 0:
            continue

        set_rule(
            world.multiworld.get_location(location_name, player),
            lambda state, count=required: state.has(PROGRESSIVE_CULT, player, count),
        )


def set_woolhaven_rules(world: "CultOfTheLambWorld") -> None:
    """Puts Woolhaven's tarot cards behind all four Bishops, which is what reaching it takes.

    With no rule, the fill put a domain item on a card that needed it (BUG-012). Asks for the
    Bishop locations, which carry their own gates, and uses add_rule to keep the depth band.
    """
    player = world.player

    for name, data in location_table.items():
        if not data.dlc or data.category != "TarotCard":
            continue
        try:
            location = world.multiworld.get_location(name, player)
        except KeyError:
            # Not in this seed: a starting card, or tarot randomization is off.
            continue
        add_rule(
            location,
            lambda state: all(
                state.can_reach_location(bishop, player)
                for bishop in BISHOP_LOCATIONS.values()
            ))


def set_building_depth_rules(world: "CultOfTheLambWorld") -> None:
    """Band the building checks by the tier the game gates them on.

    Equal quarters put Missionary, a tier 2 building, in the free band (BUG-010). Still pacing, not
    logic: the real rule would name the unlocking item, which most modes don't have.
    """
    player = world.player
    created = {location.name for location in world.multiworld.get_locations(player)}

    for building in BUILDINGS:
        location_name = f"Build - {building.display}"
        if location_name not in created:
            continue

        required = _BUILDING_TIER_COPIES.get(building.tier, _MAX_REGION_COPIES)
        location = world.multiworld.get_location(location_name, player)

        if required > 0:
            set_rule(
                location,
                lambda state, count=required: state.has(
                    PROGRESSIVE_REGION_ACCESS, player, count
                ),
            )

        # The deepest band is excluded rather than called unreachable, as in set_depth_rules.
        if required == _MAX_REGION_COPIES:
            location.progress_type = LocationProgressType.EXCLUDED


def set_building_tech_rules(world: "CultOfTheLambWorld") -> None:
    """Gate each building check on the item that unlocks its structure.

    Only in checks_and_techs and curated_checks. The table is the type StructuresData.GetUnlocked
    checks before the structure can be placed.
    """
    player = world.player
    created = {location.name for location in world.multiworld.get_locations(player)}

    for location_name, (item, count) in world.building_tech_requirements.items():
        if location_name not in created:
            continue
        set_rule(
            world.multiworld.get_location(location_name, player),
            lambda state, item=item, count=count: state.has(item, player, count),
        )


def set_snail_shrine_rules(world: "CultOfTheLambWorld") -> None:
    """Snail Shrine N is the Nth shrine lit, in any order, so it needs N shrines you can reach.

    Shells only come from Anura's snails, and there is one shrine at the Lonely Shack plus one in
    each region's hub. With Anura open that is two shrines, and each other open region adds one.
    """
    player = world.player
    created = {location.name for location in world.multiworld.get_locations(player)}
    others = [region for region in REGION_NAMES if region != SHELL_REGION]

    for n in range(1, SNAIL_SHRINE_COUNT + 1):
        name = f"Snail Shrine {n}"
        if name not in created:
            continue
        set_rule(
            world.multiworld.get_location(name, player),
            lambda state, extra=max(0, n - 2): state.can_reach_region(SHELL_REGION, player)
            and sum(state.can_reach_region(region, player) for region in others) >= extra,
        )


def set_unmodelled_card_rules(world: "CultOfTheLambWorld") -> None:
    """Keep progression off region-tied cards whose region is only the first gate.

    Neptune's Curse needs Darkwood and then a 25-Crystal quest before the fisherman appears
    (HubShoreManager.cs:94). The region rule stays, but the card holds nothing important.
    """
    player = world.player
    created = {location.name for location in world.multiworld.get_locations(player)}

    for card in TAROT_CARDS:
        if card.internal not in _UNMODELLED_REGION_CARDS:
            continue

        # Not created for a starting card, or with tarot randomization off.
        location_name = f"Tarot Card - {card.display}"
        if location_name not in created:
            continue

        location = world.multiworld.get_location(location_name, player)
        location.progress_type = LocationProgressType.EXCLUDED
