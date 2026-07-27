from typing import Dict, List, NamedTuple, Optional, Tuple

from BaseClasses import Item, ItemClassification

# Placeholder id range - not yet checked against the AP world registry for collisions.
# Pick a real range before publishing (see docs/architecture.md).
offset = 3_050_000


class ItemData(NamedTuple):
    code: Optional[int]
    classification: ItemClassification
    category: str
    # Woolhaven (the game's only major gameplay DLC) content. Excluded from the pool unless
    # the Include Woolhaven DLC option is on - see options.py for why that matters.
    dlc: bool = False


class CultOfTheLambItem(Item):
    game: str = "Cult of the Lamb"


# Region/Bishop names, weapon names, Tarot Card names, and Relic names below are real,
# cross-checked against DecompiledGamesViaDnSpy/Cotl/wiki/. Doctrine and (base-game, non-DLC)
# Structure names are still placeholders - the wiki page for Doctrines wasn't sourced yet,
# and StructureBrain.cs's full base-game enum hasn't been read (see AI_INDEX.md open
# questions). Don't treat "Doctrine Unlock"/"Structure Unlock" as anything but stand-ins.

# Which of the 4 regions is free at seed start (and the order the other 3 unlock in) is
# randomized per-seed - see regions.REGION_NAMES/CultOfTheLambWorld.region_order and
# rules.py. So instead of 3 separately-named "X Access" items, there's a single progressive
# item: the Nth copy received opens the Nth-still-locked region in that seed's random order.
PROGRESSIVE_REGION_ACCESS = "Progressive Bishop's Domain"

# The Temple sermon upgrades, in the wiki's tier order. Display names are the real in-game
# names; the second element is the UpgradeSystem.Type the C# client passes to
# UpgradeSystem.UnlockAbility(). Order here defines the item ids (SERMON_ITEM_OFFSET + index)
# and must stay append-only - the client hardcodes the same offsets.
#
# The three numbered chains below are progressive; everything else is its own named item.
#
# Out-of-order delivery wouldn't *break* anything - UpgradeSystem.UnlockAbility ignores
# prerequisites entirely (verified: it's a bare Contains-then-Add), and the client grants
# upgrades directly rather than through the tree UI. The reason to make these progressive is
# pacing: Might of the Devout sets your starting weapon level, so receiving VI before I is a
# power spike arriving out of sequence. Hearts and Fervour are additive, so their tiers are
# interchangeable and progressive is just tidier.
#
# The distinctly-named sequences (the five curse packs) stay individual - each adds three
# *different* curses, and "Curse of the Beguiler" reads far better in someone else's
# multiworld than "Progressive Curse Pack".
SERMON_ITEM_OFFSET = 400

# (display name, UpgradeSystem.Type, is_woolhaven_dlc)
SERMON_UPGRADES = [
    # Tier 1
    ("Hearts of the Faithful", "PUpgrade_Heart_1", False),
    ("Bane Weapons", "PUpgrade_WeaponPoison", False),
    ("Curse of the Horde", "PUpgrade_CursePack2", False),
    # Tier 2
    ("Might of the Devout I", "PUpgrade_StartingWeapon_1", False),
    ("Kudaai's Blessing", "PUpgrade_ResummonWeapon", False),
    ("Vampiric Weapons", "PUpgrade_WeaponHeal", False),
    ("Curse of the Occultist", "PUpgrade_CursePack5", False),
    ("Fervour of the Righteous I", "PUpgrade_Ammo_1", False),
    # Tier 3
    ("Hearts of the Faithful II", "PUpgrade_Heart_2", False),
    ("Weapon Mastery", "PUpgrade_HeavyAttacks", False),
    ("Necromantic Weapons", "PUpgrade_WeaponNecromancy", False),
    ("Curse of the Tundra", "PUpgrade_CursePack1", False),
    # Tier 4
    ("Might of the Devout II", "PUpgrade_StartingWeapon_2", False),
    ("Zealous Weapons", "PUpgrade_WeaponFervor", False),
    ("Curse of the Necromancer", "PUpgrade_CursePack4", False),
    ("Fervour of the Righteous II", "PUpgrade_Ammo_2", False),
    # Tier 5
    ("Might of the Devout III", "PUpgrade_StartingWeapon_3", False),
    ("Merciless Weapons", "PUpgrade_WeaponCritHit", False),
    ("Curse of the Beguiler", "PUpgrade_CursePack3", False),
    # Tier 6
    ("Might of the Devout IV", "PUpgrade_StartingWeapon_4", False),
    ("Eyes of the Lost Relics", "Relic_Pack1", False),
    ("Blessings of the Relics", "Relics_Blessed_1", False),
    ("Damnation of the Relics", "Relics_Dammed_1", False),
    ("Sword Mastery", "PUpgrade_HA_Sword", False),
    ("Axe Mastery", "PUpgrade_HA_Axe", False),
    ("Dagger Mastery", "PUpgrade_HA_Dagger", False),
    ("Gauntlets Mastery", "PUpgrade_HA_Gauntlets", False),
    ("Hammer Mastery", "PUpgrade_HA_Hammer", False),
    ("Blunderbuss Mastery", "PUpgrade_HA_Blunderbuss", False),
    ("Godly Weapons", "PUpgrade_WeaponGodly", False),
    ("Might of the Devout V", "PUpgrade_StartingWeapon_5", False),
    ("Might of the Devout VI", "PUpgrade_StartingWeapon_6", False),
    # Woolhaven DLC tier
    ("Might of the Devout VII", "PUpgrade_StartingWeapon_7", True),
    ("Relics of the Freezing", "Relics_Ice", True),
    ("Relics of the Burning", "Relics_Fire", True),
    ("Flail Mastery", "PUpgrade_HA_Chain", True),
    ("Burning Curses", "Curses_Fire", True),
    ("Teleport Curses", "Curses_Teleport", True),
]

BASE_SERMON_COUNT = sum(1 for _, _, dlc in SERMON_UPGRADES if not dlc)

# Display-name prefixes that collapse into one progressive item. Order within a chain is
# taken from SERMON_UPGRADES above, so the Nth copy received unlocks the Nth tier.
PROGRESSIVE_SERMON_CHAINS = {
    "Might of the Devout": "Progressive Might of the Devout",
    "Hearts of the Faithful": "Progressive Hearts of the Faithful",
    "Fervour of the Righteous": "Progressive Fervour of the Righteous",
}


def _chain_for(display_name: str):
    """The progressive item a given upgrade belongs to, or None if it stands alone."""
    for prefix, item_name in PROGRESSIVE_SERMON_CHAINS.items():
        # Guard against a future upgrade whose name merely starts the same way: every real
        # chain member is "<prefix> <roman numeral>", never the bare prefix plus a word.
        if display_name == prefix or display_name.startswith(prefix + " "):
            return item_name
    return None


def build_sermon_items() -> Dict[str, List[Tuple[str, bool]]]:
    """Maps sermon AP item name -> ordered [(UpgradeSystem.Type name, is_dlc), ...].

    A single-element list is a standalone upgrade; a longer one is a progressive chain whose
    Nth copy unlocks the Nth entry. Uniform shape so the client needs only one code path.

    The per-tier DLC flag has to survive into the chain, because Might of the Devout is a
    base-game chain with a DLC-only final tier: the item belongs in every seed, but it needs
    6 copies without Woolhaven and 7 with it.
    """
    items: Dict[str, List[Tuple[str, bool]]] = {}
    for display_name, internal, dlc in SERMON_UPGRADES:
        item_name = _chain_for(display_name) or display_name
        items.setdefault(item_name, []).append((internal, dlc))
    return items


SERMON_ITEM_UPGRADES = build_sermon_items()


def sermon_item_counts(include_dlc: bool) -> Dict[str, int]:
    """How many copies of each sermon item this seed's pool needs. Zero means excluded."""
    return {
        name: sum(1 for _, dlc in tiers if include_dlc or not dlc)
        for name, tiers in SERMON_ITEM_UPGRADES.items()
    }

item_table: Dict[str, ItemData] = {
    PROGRESSIVE_REGION_ACCESS: ItemData(offset + 1, ItemClassification.progression, "Region"),

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

    # Filler. Ids 200/201 kept for the two original placeholder names so older seeds don't
    # repoint; everything from 202 is new.
    "Gold Tithe": ItemData(offset + 200, ItemClassification.filler, "Filler"),
    "Fervour": ItemData(offset + 201, ItemClassification.filler, "Filler"),
    "Bundle of Lumber": ItemData(offset + 202, ItemClassification.filler, "Filler"),
    "Pile of Stone": ItemData(offset + 203, ItemClassification.filler, "Filler"),
    "Basket of Berries": ItemData(offset + 204, ItemClassification.filler, "Filler"),
    "Bag of Bones": ItemData(offset + 205, ItemClassification.filler, "Filler"),
    "Follower Level Up": ItemData(offset + 206, ItemClassification.filler, "Filler"),

    # Trap.
    "Dissent Trap": ItemData(offset + 300, ItemClassification.trap, "Trap"),
}

# Sermon upgrades are 'useful', not 'progression': nothing in rules.py requires them, and an
# item should only be progression if some rule actually references it - over-marking bloats
# the progression pool and constrains fill for no benefit.
#
# Ids follow SERMON_ITEM_UPGRADES' insertion order, which follows SERMON_UPGRADES' tier
# order, so this stays stable as long as that list is only appended to.
item_table.update({
    name: ItemData(
        offset + SERMON_ITEM_OFFSET + i,
        ItemClassification.useful,
        "Sermon",
        all(dlc for _, dlc in tiers),
    )
    for i, (name, tiers) in enumerate(SERMON_ITEM_UPGRADES.items())
})

filler_table = [name for name, data in item_table.items() if data.category == "Filler"]
trap_table = [name for name, data in item_table.items() if data.category == "Trap"]

# Relative frequency in the filler pool. Filler is roughly half of a seed's items right now,
# so an even split would make the common case feel repetitive - resources are deliberately
# the bulk, with Follower Level Up rarer because it compounds (each level permanently raises
# that Follower's sermon-point contribution, so it accelerates every later sermon).
#
# Any Filler-category item missing from this dict falls back to weight 1 rather than being
# dropped, so adding an item and forgetting to weight it can't silently remove it from seeds.
item_pool_weights: Dict[str, int] = {
    "Bundle of Lumber": 10,
    "Pile of Stone": 10,
    "Basket of Berries": 8,
    "Bag of Bones": 8,
    "Gold Tithe": 8,
    "Fervour": 6,
    "Follower Level Up": 4,
}


def weighted_filler_names() -> List[str]:
    """filler_table expanded by weight, for random.choice to sample from."""
    expanded: List[str] = []
    for name in filler_table:
        expanded.extend([name] * item_pool_weights.get(name, 1))
    return expanded


def create_item(name: str, player: int) -> CultOfTheLambItem:
    data = item_table[name]
    return CultOfTheLambItem(name, data.classification, data.code, player)
