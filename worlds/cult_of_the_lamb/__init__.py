from typing import Any, Dict, List

from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld, World

from .items import CultOfTheLambItem, create_item, filler_table, item_table
from .locations import location_name_to_id, location_table
from .options import CultOfTheLambOptions
from .regions import create_regions
from .rules import REGION_ACCESS_ITEMS, set_rules


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
        "Regions": set(REGION_ACCESS_ITEMS.values()),
        "Weapons": {name for name, data in item_table.items() if data.category == "Weapon"},
        "Tarot Cards": {name for name, data in item_table.items() if data.category == "Tarot"},
        "Relics": {name for name, data in item_table.items() if data.category == "Relic"},
    }

    web = CultOfTheLambWeb()

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> CultOfTheLambItem:
        return create_item(name, self.player)

    def create_items(self) -> None:
        item_pool: List[CultOfTheLambItem] = []

        if self.options.randomize_region_access:
            for item_name in REGION_ACCESS_ITEMS.values():
                item_pool.append(self.create_item(item_name))

        remaining = len(location_table) - len(item_pool)
        fillable_names = [name for name in item_table if name not in REGION_ACCESS_ITEMS.values()]
        for _ in range(remaining):
            item_pool.append(self.create_item(self.random.choice(fillable_names)))

        self.multiworld.itempool += item_pool

    def set_rules(self) -> None:
        set_rules(self)

    def get_filler_item_name(self) -> str:
        return self.random.choice(filler_table)

    def fill_slot_data(self) -> Dict[str, Any]:
        return {
            "goal": self.options.goal.value,
            "randomizeRegionAccess": bool(self.options.randomize_region_access.value),
        }
