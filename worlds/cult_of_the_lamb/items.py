from typing import Dict, NamedTuple, Optional

from BaseClasses import Item, ItemClassification

# Placeholder id range - not yet checked against the AP world registry for collisions.
# Pick a real range before publishing (see docs/architecture.md).
offset = 3_050_000


class ItemData(NamedTuple):
    code: Optional[int]
    classification: ItemClassification
    category: str


class CultOfTheLambItem(Item):
    game: str = "Cult of the Lamb"


# Region/Bishop names, weapon names, Tarot Card names, and Relic names below are real,
# cross-checked against DecompiledGamesViaDnSpy/Cotl/wiki/. Doctrine and (base-game, non-DLC)
# Structure names are still placeholders - the wiki page for Doctrines wasn't sourced yet,
# and StructureBrain.cs's full base-game enum hasn't been read (see AI_INDEX.md open
# questions). Don't treat "Doctrine Unlock"/"Structure Unlock" as anything but stand-ins.
item_table: Dict[str, ItemData] = {
    # Progression: region access, mirrors RoR2's per-environment unlock items.
    # Darkwood has no access item - see rules.py for why (it's the always-reachable tutorial
    # region, and generation needs at least one free region to bootstrap the fill).
    "Anura Access": ItemData(offset + 1, ItemClassification.progression, "Region"),
    "Anchordeep Access": ItemData(offset + 3, ItemClassification.progression, "Region"),
    "Silk Cradle Access": ItemData(offset + 4, ItemClassification.progression, "Region"),

    # Useful-but-not-required unlocks. "Doctrine Unlock"/"Structure Unlock" are still
    # generic placeholders (see module docstring above); Weapons/Tarot/Relics are real names.
    "Doctrine Unlock": ItemData(offset + 100, ItemClassification.useful, "Doctrine"),
    "Structure Unlock": ItemData(offset + 101, ItemClassification.useful, "Structure"),

    # Base weapons, one per category (Swords/Axes/Hammers/Gauntlets/Daggers).
    "Crusader's Blade": ItemData(offset + 110, ItemClassification.useful, "Weapon"),
    "Apostate's Cleaver": ItemData(offset + 111, ItemClassification.useful, "Weapon"),
    "Warmaker's Hammer": ItemData(offset + 112, ItemClassification.useful, "Weapon"),
    "Tempest's Gauntlets": ItemData(offset + 113, ItemClassification.useful, "Weapon"),
    "Traitor's Razor": ItemData(offset + 114, ItemClassification.useful, "Weapon"),

    # Tarot Cards (85 exist in-game; this is a small representative sample).
    "The Hearts I": ItemData(offset + 120, ItemClassification.useful, "Tarot"),
    "The Lovers I": ItemData(offset + 121, ItemClassification.useful, "Tarot"),
    "Weeping Moon": ItemData(offset + 122, ItemClassification.useful, "Tarot"),
    "Nature's Boon": ItemData(offset + 123, ItemClassification.useful, "Tarot"),
    "Fortune's Blessing": ItemData(offset + 124, ItemClassification.useful, "Tarot"),

    # Relics (single-carry combat items from the free Relics of the Old Faith update).
    "Beads of the Anchorite": ItemData(offset + 130, ItemClassification.useful, "Relic"),
    "Clauneck's Mirror": ItemData(offset + 131, ItemClassification.useful, "Relic"),

    # Filler.
    "Gold Tithe": ItemData(offset + 200, ItemClassification.filler, "Filler"),
    "Fervour": ItemData(offset + 201, ItemClassification.filler, "Filler"),

    # Trap - placeholder, needs a real negative-effect hook before it's usable.
    "Dissent Trap": ItemData(offset + 300, ItemClassification.trap, "Trap"),
}

filler_table = [name for name, data in item_table.items() if data.category == "Filler"]

item_pool_weights: Dict[str, int] = {
    "Gold Tithe": 10,
    "Fervour": 10,
}


def create_item(name: str, player: int) -> CultOfTheLambItem:
    data = item_table[name]
    return CultOfTheLambItem(name, data.classification, data.code, player)
