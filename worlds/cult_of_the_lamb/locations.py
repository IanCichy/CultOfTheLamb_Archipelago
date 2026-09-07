from typing import Dict, List, NamedTuple, Optional, Set

from BaseClasses import Location

from .items import (
    BROOM_LEVEL_COUNT, BUILDINGS, CURSES, DIVINE_INSPIRATION, SERMON_UPGRADES, TAROT_CARDS,
    WEAPONS, location_offset, tarot_tier,
)


class CultOfTheLambLocation(Location):
    game: str = "Cult of the Lamb"


class LocationData(NamedTuple):
    region: str
    category: str
    # Only created when the Include Woolhaven DLC option is on (see options.py).
    dlc: bool = False


# Each region path is 4 chunks (3 regular crusades against a named miniboss, then the
# Bishop crusade) plus a 5th bonus chunk (the Witness, a miniboss fight that becomes
# available after the Bishop is defeated). All names and the region/Bishop/Witness
# groupings are real. They were confirmed independently via the decompiled FollowerLocation enum's
# Dungeon{tier}_{region} pattern, the wiki's per-region boss rosters, and williambsm's
# COTL.Archipelago prototype's own Check enum.
#
# Witnesses are part of the free "Relics of the Old Faith" update, not the paid Woolhaven
# DLC, so they're included unconditionally rather than behind a DLC option.
location_table: Dict[str, LocationData] = {
    "Darkwood - Amdusias": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Valefar": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Barbatos": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Leshy": LocationData("Darkwood", "Bishop"),
    "Darkwood - Witness Agares": LocationData("Darkwood", "Witness"),

    "Anura - Gusion": LocationData("Anura", "Miniboss"),
    "Anura - Eligos": LocationData("Anura", "Miniboss"),
    "Anura - Zepar": LocationData("Anura", "Miniboss"),
    "Anura - Heket": LocationData("Anura", "Bishop"),
    "Anura - Witness Bathin": LocationData("Anura", "Witness"),

    "Anchordeep - Saleos": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Haborym": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Baalzebub": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Kallamar": LocationData("Anchordeep", "Bishop"),
    "Anchordeep - Witness Astaroth": LocationData("Anchordeep", "Witness"),

    "Silk Cradle - Focalor": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Vephar": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Hauras": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Shamura": LocationData("Silk Cradle", "Bishop"),
    "Silk Cradle - Witness Allocer": LocationData("Silk Cradle", "Witness"),
}

# The game's own identifier for each boss encounter, sent through slot data so the client doesn't
# have to hardcode these location ids.
#
# Reordering the dict above silently repoints every boss check, and these are the goal-critical
# ones. That is why they travel as slot data rather than as `3_051_000 + N` positional ids.
#
# Minibosses and Witnesses are keyed by MiniBossController.name, which is what
# DataManager.KilledBosses stores. Confirmed in game by dumping a live boss room. See
# DcplIdx 3a.
MINIBOSS_AND_WITNESS_KEYS: Dict[str, str] = {
    "Darkwood - Amdusias": "Boss Mama Worm",
    "Darkwood - Valefar": "Boss Mama Maggot",
    "Darkwood - Barbatos": "Boss Burrow Worm",
    "Darkwood - Witness Agares": "Boss Beholder 1",

    "Anura - Gusion": "Boss Flying Burp Frog",
    "Anura - Eligos": "Boss Egg Hopper",
    "Anura - Zepar": "Boss Mortar Hopper",
    "Anura - Witness Bathin": "Boss Beholder 2",

    "Anchordeep - Saleos": "Boss Spiker",
    "Anchordeep - Haborym": "Boss Charger",
    "Anchordeep - Baalzebub": "Boss Scuttle Turret",
    "Anchordeep - Witness Astaroth": "Boss Beholder 3",

    "Silk Cradle - Focalor": "Boss Spider Jump",
    "Silk Cradle - Vephar": "Boss Millipede Poisoner",
    "Silk Cradle - Hauras": "Boss Scorpion",
    "Silk Cradle - Witness Allocer": "Boss Beholder 4",
}

# Bishops are recorded differently: they go into DataManager.BossesCompleted as a FollowerLocation
# rather than into KilledBosses as a string, so they're keyed by that enum's member name.
BISHOP_DUNGEON_LOCATIONS: Dict[str, str] = {
    "Darkwood - Leshy": "Dungeon1_1",
    "Anura - Heket": "Dungeon1_2",
    "Anchordeep - Kallamar": "Dungeon1_3",
    "Silk Cradle - Shamura": "Dungeon1_4",
}

# Every boss row above must exist in location_table, or its check can never be sent. A typo would
# otherwise surface in game, hours in, as a boss that pays nothing.
assert set(MINIBOSS_AND_WITNESS_KEYS) | set(BISHOP_DUNGEON_LOCATIONS) == {
    name for name, data in location_table.items()
    if data.category in ("Miniboss", "Bishop", "Witness")
}, "the boss key tables and location_table's boss rows have drifted apart"

# Sermon upgrade checks. These are deliberately *sequential* rather than named after specific
# upgrades: filling the sermon bar is one repeatable event, and which upgrade you'd have
# picked is exactly what Archipelago is randomizing away. So the Nth fill is the Nth check,
# and the named upgrades are the items (see items.py SERMON_UPGRADES).
#
# The last 6 exist only with the Woolhaven DLC, because without it there are only 32 upgrades
# to earn and the bar stops paying out, so those checks would be unreachable.
#
# They live in "Cult" (the home base) rather than a dungeon region: sermons are given at the
# Temple, so they're gated by follower count and time, not by which regions are unlocked.
for _i, (_name, _internal, _dlc) in enumerate(SERMON_UPGRADES):
    location_table[f"Sermon Upgrade {_i + 1}"] = LocationData("Cult", "Sermon", _dlc)

# Follower recruitment milestones. Counted as "ever recruited" (living + dead) rather than
# current flock size, so a plague or a sacrifice spree can't make an already-passed milestone
# unreachable again.
FOLLOWER_MILESTONE_COUNT = 20

for _n in range(1, FOLLOWER_MILESTONE_COUNT + 1):
    location_table[f"Followers Recruited {_n}"] = LocationData("Cult", "Follower")

# Tarot Card shop purchases. Every hub has a shop selling a fixed, named set of cards rather than
# randomised stock, so each purchase is a stable, identifiable check.
#
# These live in the *paired crusade region* rather than "Cult" on purpose: each hub is reached
# through its region's progression (Midas's Cave opens after the golden tree in Silk Cradle,
# Pilgrim's Passage's shops need the Lighthouse lit), so putting them here makes them gate
# naturally instead of all landing in sphere 1 the way the Cult-region blocks do.
#
# Display names differ wildly from TarotCards.Card enum names. "The Burning Dead" is Skull and
# "The Path" is MovementSpeed, so the enum name is carried alongside for the client.
# (display name, TarotCards.Card enum name)
TAROT_SHOP_CARDS = {
    "Darkwood": [            # Pilgrim's Passage
        ("Hands of Rage", "HandsOfRage"),
        ("Nature's Boon", "NaturesGift"),
        ("All Seeing Sun", "Sun"),
        ("Retribution", "BombOnDamaged"),
    ],
    "Anura": [               # Spore Grotto
        ("Blazing Trail", "DamageOnRoll"),
        ("Weeping Moon", "Moon"),
        ("Kin of Turua", "TentacleOnDamaged"),
        ("The Path", "MovementSpeed"),
    ],
    "Anchordeep": [          # Smuggler's Sanctuary
        ("The Bomb", "BombOnRoll"),
        ("Ichor Lingered", "GoopOnRoll"),
        ("Soul Snatcher", "HealChance"),
        ("Wraith's Will", "WalkThroughBlocks"),
    ],
    "Silk Cradle": [         # Midas's Cave
        ("The Burning Dead", "Skull"),
        ("The Deal", "TheDeal"),
        ("Ichor Earned", "GoopOnDamaged"),
        ("Godly Moment", "InvincibilityPerRoom"),
    ],
}

TAROT_SHOP_HUBS = {
    "Darkwood": "Pilgrim's Passage",
    "Anura": "Spore Grotto",
    "Anchordeep": "Smuggler's Sanctuary",
    "Silk Cradle": "Midas's Cave",
}

# Snail shrines, one per hub, each accepting a single Shell offering. The game tracks them
# as DataManager.ShellsGifted_0.._4, and lighting all five unlocks the Snail Follower form.
#
# Kept in "Cult" rather than region-gated because which ShrineNumber sits in which hub isn't
# known yet. The index is a serialized field on the prefab, not something the decompile
# exposes. Depth rules apply to them so they still spread across spheres.
SNAIL_SHRINE_COUNT = 5

for _n in range(1, SNAIL_SHRINE_COUNT + 1):
    location_table[f"Snail Shrine {_n}"] = LocationData("Cult", "Snail")


for _region, _cards in TAROT_SHOP_CARDS.items():
    for _display, _internal in _cards:
        location_table[f"{TAROT_SHOP_HUBS[_region]} - {_display}"] = \
            LocationData(_region, "TarotShop")

# Unlocking a Tarot Card, however you did it. Every route ends at the same two unlock methods,
# so the client sends these without knowing which condition fired.
#
# "Cult" rather than a crusade region, because each card's earning condition would mean tracing
# all 85, and Cult is always reachable so nothing here can become unreachable.
#
# Three exclusions:
#   1. Shop cards. Their slot is already their check, so a second location would pay twice.
#   2. Post-game cards. Their checks would sit past a Bishops or Witnesses win condition, and an
#      unreachable location fails generation outright.
#   3. Region-tied cards. They live in their region under a separate category, so they get real
#      logic from the region graph and set_depth_rules leaves them alone.
#
# The rest are sorted by how hard they are to earn, because a card's band is its position in
# this table. Left in the game's enum order the bands would be meaningless.
_SHOP_CARD_NAMES = {_display for _cards in TAROT_SHOP_CARDS.values() for _display, _ in _cards}

_card_locations = [
    _card for _card in TAROT_CARDS
    if not _card.coop and not _card.postgame and _card.display not in _SHOP_CARD_NAMES
]

for _card in sorted(_card_locations, key=tarot_tier):
    location_table[f"Tarot Card - {_card.display}"] = LocationData(
        _card.region or "Cult",
        "TarotCardRegion" if _card.region else "TarotCard",
        _card.dlc,
    )

# Equipping each weapon family and each curse family for the first time this seed.
#
# "First equipped", not "first added to the pool", and the difference matters: the pool is
# real save data that this world never writes to, so on an established save it already
# contains every weapon and a pool-entry check could never fire again. Equipping is something
# the player does fresh every seed.
#
# They live in "Cult" but never get depth bands. rules.py gates each one on its own item, which
# is real logic rather than an approximation, and set_depth_rules would overwrite it.
for _weapon in WEAPONS:
    location_table[f"Weapon - {_weapon.display}"] = LocationData("Cult", "Weapon", _weapon.dlc)

for _curse in CURSES:
    location_table[f"Curse - {_curse.display}"] = LocationData("Cult", "Curse", _curse.dlc)

# Divine Inspiration unlocks. These are sequential, so the Nth upgrade unlocked in that tree is
# the Nth check, rather than one named location per upgrade. That shape is the only one that
# works in all four divine_inspiration_mode values. In checks_and_techs the player never picks
# anything (Archipelago grants the techs), so a per-upgrade location would fire for whatever the
# multiworld happened to hand over rather than for anything the player did.
#
# It also sidesteps the tree's prerequisites entirely: 11 of the 69 sit behind external systems
# (PleasureSystem, TailorSystem, DiscipleSystem, System_PlayerTent), which would each need real
# logic if the locations were named. Sequentially, the player just unlocks 69 things in whatever
# order the game allows.
#
# Same reasoning as "Sermon Upgrade N", which this deliberately mirrors.
DIVINE_INSPIRATION_COUNT = len(DIVINE_INSPIRATION)

for _n in range(1, DIVINE_INSPIRATION_COUNT + 1):
    location_table[f"Divine Inspiration {_n}"] = LocationData("Cult", "DivineInspiration")

# First construction of each curated building. Named rather than sequential, unlike most blocks
# here. Which building you put up is a real choice, so "Build - Kitchen" is a more meaningful
# check than "Building 12". The client maps Data.ToBuildType straight to an id.
#
# Sorted by the tier of the Divine Inspiration upgrade that unlocks each one, because that order
# is what the depth bands read: tier-1 buildings land in early bands where they're genuinely
# available, tier-5 ones land deep. Without it the bands would key off list order and mean
# nothing.
for _building in sorted(BUILDINGS, key=lambda b: b.tier):
    location_table[f"Build - {_building.display}"] = LocationData("Cult", "Building")

# Broom levels, from sweeping. DataManager.ChoreXPLevel is monotonic and save-persisted, so this
# is the same sequential shape as the sermon block.
for _n in range(1, BROOM_LEVEL_COUNT + 1):
    location_table[f"Broom Level {_n}"] = LocationData("Cult", "Broom")

# Append-only: ids come from enumeration order, and the C# client hardcodes the same
# offsets (see Utilities/CultOfTheLambIds.cs). Reordering this dict silently repoints every
# id after the change.
location_name_to_id: Dict[str, int] = {
    name: location_offset + i for i, name in enumerate(location_table)
}


def get_locations_for_region(
    region: str, include_dlc: bool = True, categories: Optional[Set[str]] = None
) -> List[str]:
    """Locations in a region, optionally narrowed to specific categories.

    The category filter exists because "Cult" holds several independently-toggleable blocks
    (sermon upgrades, follower milestones), and a disabled block must not create locations -
    unreachable ones would fail generation.
    """
    return [
        name for name, data in location_table.items()
        if data.region == region
        and (include_dlc or not data.dlc)
        and (categories is None or data.category in categories)
    ]
