from typing import Any, Dict, List

from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld, World

from .items import (
    SERMON_ITEM_OFFSET, SERMON_ITEM_UPGRADES, CultOfTheLambItem, PROGRESSIVE_REGION_ACCESS,
    create_item, filler_table, item_table, offset, sermon_item_counts,
)
from .locations import location_name_to_id
from .options import CultOfTheLambOptions
from .regions import REGION_NAMES, create_regions
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
        self.region_order = self.random.sample(REGION_NAMES, len(REGION_NAMES))

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> CultOfTheLambItem:
        return create_item(name, self.player)

    def create_items(self) -> None:
        item_pool: List[CultOfTheLambItem] = []

        if self.options.randomize_region_access:
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
        return self.random.choice(filler_table)

    def fill_slot_data(self) -> Dict[str, Any]:
        return {
            "goal": self.options.goal.value,
            "requiredCount": self.options.required_count.value,
            "randomizeRegionAccess": bool(self.options.randomize_region_access.value),
            # Tells the C# client which region to force-open at start, and the order the
            # remaining three unlock in as Progressive Bishop's Domain copies arrive.
            "regionOrder": self.region_order,

            "includeWoolhaven": bool(self.options.include_woolhaven.value),
            "randomizeSermonUpgrades": bool(self.options.randomize_sermon_upgrades.value),
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
