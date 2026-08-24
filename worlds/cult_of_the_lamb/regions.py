from typing import TYPE_CHECKING

from BaseClasses import Region

from .items import TAROT_CARDS
from .locations import (
    DIVINE_INSPIRATION_COUNT, CultOfTheLambLocation, get_locations_for_region, location_name_to_id,
)

if TYPE_CHECKING:
    from . import CultOfTheLambWorld

# The four base regions. Which one is free at seed start (and the unlock order of the
# other three) is randomized per-seed in CultOfTheLambWorld.generate_early - this list is
# just used to build the region graph, not to imply any fixed order.
REGION_NAMES = ["Darkwood", "Anura", "Anchordeep", "Silk Cradle"]

# Its home-base door demands sacrificing a Follower to open, so starting a seed here is a
# rough opening move - the randomized_safe_start option exists to avoid exactly that.
SACRIFICE_GATED_REGION = "Silk Cradle"


def create_regions(world: "CultOfTheLambWorld") -> None:
    """Builds this seed's region graph and hangs each enabled block's locations off it."""
    player = world.player
    multiworld = world.multiworld

    include_dlc = bool(world.options.include_woolhaven)

    menu = Region("Menu", player, multiworld)
    cult = Region("Cult", player, multiworld)
    multiworld.regions.append(menu)
    multiworld.regions.append(cult)
    menu.connect(cult)

    # Two reasons a card's check can't exist, and location_table carries every card either way -
    # it's a static, positionally-indexed table, so entries are filtered by name here rather than
    # removed from it, and by name rather than in get_locations_for_region, which keys off
    # category and DLC. Applies to both blocks below, since a starting card can be region-tied.
    #
    # Already held: a starting card can never be earned again.
    #
    # Not managed at all: pick_tarot_cards drops the game's 15 default cards (nothing in the game
    # can re-unlock one) and, with shop checks off, the shop cards. Those stay with the game, so
    # they get no item - and without this they'd still get a location, which is strictly worse
    # than the bug it replaced: an unreachable check with no item to pair against it.
    managed = {card.display for card in world.tarot_cards}
    begins_with = {card.display for card in world.starting_tarot_cards}
    starting = {
        f"Tarot Card - {card.display}" for card in TAROT_CARDS
        if card.display not in managed or card.display in begins_with
    }

    starting |= {f"Weapon - {w.display}" for w in world.starting_weapons}
    starting |= {f"Curse - {c.display}" for c in world.starting_curses}

    # curated_checks shortens the Divine Inspiration block, so the tail of it isn't in this seed.
    # Dropped by name through the same filter rather than by trimming location_table, because
    # those ids are positional and append-only - removing a name would repoint every id after it.
    starting |= {
        f"Divine Inspiration {n}"
        for n in range(world.divine_inspiration_location_count + 1, DIVINE_INSPIRATION_COUNT + 1)
    }

    # Each block is only added when its option is on, so a seed that isn't randomizing one
    # doesn't carry unreachable locations for it.
    cult_categories = set()
    if world.options.randomize_sermon_upgrades:
        cult_categories.add("Sermon")
    if world.options.follower_milestone_checks:
        cult_categories.add("Follower")
    if world.options.snail_shrine_checks:
        cult_categories.add("Snail")
    if world.options.randomize_tarot_cards:
        cult_categories.add("TarotCard")
    if world.options.randomize_weapons:
        cult_categories.add("Weapon")
    if world.options.randomize_curses:
        cult_categories.add("Curse")
    if world.divine_inspiration_enabled:
        cult_categories.add("DivineInspiration")
    if world.options.building_checks:
        cult_categories.add("Building")
    if world.options.broom_checks:
        cult_categories.add("Broom")
    if cult_categories:
        # Weapon/curse families the player begins with are dropped for the same reason as
        # starting tarot cards: you can't earn what you already have. Their names are added
        # to `starting` above the tarot ones, so one filter covers all three blocks.
        add_locations(cult, [
            name for name in get_locations_for_region(
                "Cult", include_dlc, categories=cult_categories)
            if name not in starting
        ], player)

    # Boss checks always; Tarot shop checks only when that option is on.
    region_categories = {"Miniboss", "Bishop", "Witness"}
    if world.options.tarot_shop_checks:
        region_categories.add("TarotShop")
    if world.options.randomize_tarot_cards:
        # Cards whose earn condition is locked to one region - knucklebones opponents you
        # have to meet there, Helob's follower shop, the Pilgrim's Passage fisherman. Real
        # logic, so they skip the depth bands entirely.
        region_categories.add("TarotCardRegion")

    for region_name in REGION_NAMES:
        region = Region(region_name, player, multiworld)
        # Same starting-card exclusion as Cult above: a region-tied card the player begins
        # with can't be earned there either.
        add_locations(region, [
            name for name in get_locations_for_region(
                region_name, include_dlc, categories=region_categories)
            if name not in starting
        ], player)
        multiworld.regions.append(region)
        cult.connect(region, f"Cult -> {region_name}")


def add_locations(region: Region, location_names, player: int) -> None:
    """Attaches locations to a region by name, looking each id up in `location_table`."""
    for location_name in location_names:
        region.locations.append(CultOfTheLambLocation(
            player, location_name, location_name_to_id[location_name], region))
