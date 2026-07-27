from typing import Any, Dict, List

from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld, World

from .items import (
    SERMON_ITEM_OFFSET, SERMON_ITEM_UPGRADES, CultOfTheLambItem, PROGRESSIVE_REGION_ACCESS,
    create_item, filler_table, item_table, offset, sermon_item_counts, trap_table,
    weighted_filler_names,
)
from .locations import (
    FOLLOWER_MILESTONE_COUNT, SNAIL_SHRINE_COUNT, TAROT_SHOP_CARDS, TAROT_SHOP_HUBS,
    location_name_to_id,
    location_table,
)
from .options import CultOfTheLambOptions, RegionAccessOrder
from .regions import REGION_NAMES, SACRIFICE_GATED_REGION, create_regions
from .rules import set_rules


class CultOfTheLambWeb(WebWorld):
    tutorials = [Tutorial(
        "Multiworld Setup Guide",
        "A guide to setting up the Cult of the Lamb integration for Archipelago multiworld games.",
        "English",
        "setup_en.md",
        "setup/en",
        ["IanCichy"]
    )]


class CultOfTheLambWorld(World):
    """
    Build a cult, manage your flock, and fight your way through corrupted lands to defeat
    the four Bishops of the Old Faith - and whatever waits beyond them.
    """
    game = "Cult of the Lamb"
    options_dataclass = CultOfTheLambOptions
    options: CultOfTheLambOptions
    topology_present = True

    item_name_to_id = {name: data.code for name, data in item_table.items()}
    location_name_to_id = location_name_to_id
    item_name_groups = {
        "Weapons": {name for name, data in item_table.items() if data.category == "Weapon"},
        "Tarot Cards": {name for name, data in item_table.items() if data.category == "Tarot"},
        "Relics": {name for name, data in item_table.items() if data.category == "Relic"},
        "Sermon Upgrades": {name for name, data in item_table.items() if data.category == "Sermon"},
    }

    web = CultOfTheLambWeb()

    # The region that's free from seed start, followed by the other three in the order
    # they unlock via Progressive Bishop's Domain. Set once in generate_early so
    # create_regions/set_rules/fill_slot_data all agree on the same per-seed order.
    region_order: List[str]

    def generate_early(self) -> None:
        self.region_order = self.build_region_order()
        # Expand once rather than per filler item - this is sampled dozens of times per seed.
        self.weighted_filler = weighted_filler_names()

    def build_region_order(self) -> List[str]:
        """The unlock order for this seed. Index 0 is free; the rest gate behind one more
        Progressive Bishop's Domain each (see rules.set_rules).

        REGION_NAMES is already in the game's own order, so vanilla_order is just a copy.
        """
        order = self.options.region_access_order

        if order in (RegionAccessOrder.option_vanilla_order,
                     RegionAccessOrder.option_all_unlocked):
            return list(REGION_NAMES)

        if order == RegionAccessOrder.option_randomized_safe_start:
            # Silk Cradle's door costs a Follower sacrifice to open, which is a punishing
            # opening move before there's a flock to spare - so shuffle until it isn't first.
            # Rejection sampling rather than picking-then-shuffling keeps every other ordering
            # equally likely.
            while True:
                shuffled = self.random.sample(REGION_NAMES, len(REGION_NAMES))
                if shuffled[0] != SACRIFICE_GATED_REGION:
                    return shuffled

        return self.random.sample(REGION_NAMES, len(REGION_NAMES))

    @property
    def regions_are_gated(self) -> bool:
        """False only for all_unlocked, where no access items exist at all."""
        return self.options.region_access_order != RegionAccessOrder.option_all_unlocked

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> CultOfTheLambItem:
        return create_item(name, self.player)

    def create_items(self) -> None:
        item_pool: List[CultOfTheLambItem] = []

        if self.regions_are_gated:
            # One fewer copy than there are regions - the first region in region_order is
            # always free, so only the remaining N-1 need to be unlocked.
            for _ in range(len(REGION_NAMES) - 1):
                item_pool.append(self.create_item(PROGRESSIVE_REGION_ACCESS))

        if self.options.randomize_sermon_upgrades:
            counts = sermon_item_counts(bool(self.options.include_woolhaven))
            for name, count in counts.items():
                for _ in range(count):
                    item_pool.append(self.create_item(name))

        # Count the locations actually created rather than the whole table: options can
        # disable whole blocks (sermons, DLC content), and padding to the table size would
        # overfill the pool and fail generation.
        remaining = len(self.multiworld.get_unfilled_locations(self.player)) - len(item_pool)
        for _ in range(remaining):
            item_pool.append(self.create_item(self.get_filler_item_name()))

        self.multiworld.itempool += item_pool

    def set_rules(self) -> None:
        set_rules(self)

    def get_filler_item_name(self) -> str:
        """Weighted filler, with traps mixed in per the Trap Percentage option.

        Rolled per item rather than by carving an exact slice off the pool, so the trap count
        varies naturally between seeds instead of being identical every time.
        """
        if self.options.trap_percentage.value > 0 and trap_table:
            if self.random.randint(1, 100) <= self.options.trap_percentage.value:
                return self.random.choice(trap_table)
        return self.random.choice(self.weighted_filler)

    def fill_slot_data(self) -> Dict[str, Any]:
        return {
            "goal": self.options.goal.value,
            "requiredCount": self.options.required_count.value,
            "randomizeRegionAccess": self.regions_are_gated,
            # Tells the C# client which region to force-open at start, and the order the
            # remaining three unlock in as Progressive Bishop's Domain copies arrive.
            "regionOrder": self.region_order,

            "includeWoolhaven": bool(self.options.include_woolhaven.value),
            "randomizeSermonUpgrades": bool(self.options.randomize_sermon_upgrades.value),
            # "Sermon Upgrade N" location ids are contiguous from here, so the client can
            # turn DataManager.Doctrine_PlayerUpgrade_Level into a check id directly.
            "sermonLocationBaseId": location_name_to_id["Sermon Upgrade 1"],
            "sermonLocationCount": sum(
                1 for name, data in location_table.items()
                if data.category == "Sermon" and (self.options.include_woolhaven or not data.dlc)
            ),

            "followerMilestoneChecks": bool(self.options.follower_milestone_checks.value),
            # "Followers Recruited N" ids are contiguous from here, so the client turns a
            # recruit count straight into a check id.
            "followerLocationBaseId": location_name_to_id["Followers Recruited 1"],
            "followerLocationCount": FOLLOWER_MILESTONE_COUNT,

            "snailShrineChecks": bool(self.options.snail_shrine_checks.value),
            # ShellsGifted_0.._4 map to contiguous ids from here.
            "snailLocationBaseId": location_name_to_id["Snail Shrine 1"],
            "snailLocationCount": SNAIL_SHRINE_COUNT,

            "tarotShopChecks": bool(self.options.tarot_shop_checks.value),
            # TarotCards.Card enum name -> location id. Keyed by enum name because that's
            # what the client can read off a BuyEntry; display names differ completely
            # ("The Burning Dead" is Skull) and would be useless to match on.
            "tarotShopLocations": {
                internal: location_name_to_id[f"{TAROT_SHOP_HUBS[region]} - {display}"]
                for region, cards in TAROT_SHOP_CARDS.items()
                for display, internal in cards
            },
            # Sermon item name -> the UpgradeSystem.Type names it unlocks, in order. A
            # single-entry list is a standalone upgrade; a longer one is a progressive chain
            # where the Nth copy received unlocks the Nth entry. Sending the mapping instead
            # of hardcoding it client-side means adding or reordering upgrades can't
            # silently desync the two sides the way the hardcoded location ids in
            # CultOfTheLambIds.cs can.
            "sermonUpgrades": {
                name: [
                    internal for internal, dlc in tiers
                    if self.options.include_woolhaven or not dlc
                ]
                for name, tiers in SERMON_ITEM_UPGRADES.items()
            },
        }
