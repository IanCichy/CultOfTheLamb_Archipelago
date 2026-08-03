from typing import Dict, List, NamedTuple, Optional, Set

from BaseClasses import Location

from .items import SERMON_UPGRADES, TAROT_CARDS, tarot_tier

# Must not overlap worlds/cult_of_the_lamb/items.py's offset range.
location_offset = 3_051_000


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
# groupings are real - confirmed independently via the decompiled FollowerLocation enum's
# Dungeon{tier}_{region} pattern, the wiki's per-region boss rosters, and williambsm's
# COTL.Archipelago prototype's own Check enum. See
# DecompiledGamesViaDnSpy/Cotl/wiki/bishops_regions_and_dlc.md.
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

# Sermon upgrade checks. These are deliberately *sequential* rather than named after specific
# upgrades: filling the sermon bar is one repeatable event, and which upgrade you'd have
# picked is exactly what Archipelago is randomizing away. So the Nth fill is the Nth check,
# and the named upgrades are the items (see items.py SERMON_UPGRADES).
#
# The last 6 exist only with the Woolhaven DLC, because without it there are only 32 upgrades
# to earn and the bar stops paying out - so those checks would be unreachable.
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

# Tarot Card shop purchases. Every hub has a shop selling a fixed, named set of cards - not
# randomised stock - so each purchase is a stable, identifiable check.
#
# These live in the *paired crusade region* rather than "Cult" on purpose: each hub is reached
# through its region's progression (Midas's Cave opens after the golden tree in Silk Cradle,
# Pilgrim's Passage's shops need the Lighthouse lit), so putting them here makes them gate
# naturally instead of all landing in sphere 1 the way the Cult-region blocks do.
#
# Display names differ wildly from TarotCards.Card enum names - "The Burning Dead" is Skull,
# "The Path" is MovementSpeed - so the enum name is carried alongside for the client.
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

# Snail shrines - one per hub, each accepting a single Shell offering. The game tracks them
# as DataManager.ShellsGifted_0.._4, and lighting all five unlocks the Snail Follower form.
#
# Kept in "Cult" rather than region-gated because which ShrineNumber sits in which hub isn't
# known yet - the index is a serialized field on the prefab, not something the decompile
# exposes. Depth rules apply to them so they still spread across spheres.
SNAIL_SHRINE_COUNT = 5

for _n in range(1, SNAIL_SHRINE_COUNT + 1):
    location_table[f"Snail Shrine {_n}"] = LocationData("Cult", "Snail")


for _region, _cards in TAROT_SHOP_CARDS.items():
    for _display, _internal in _cards:
        location_table[f"{TAROT_SHOP_HUBS[_region]} - {_display}"] = \
            LocationData(_region, "TarotShop")

# Unlocking a Tarot Card, however you did it. Every route the game has - finding one in a
# crusade, a shop purchase, a challenge reward like Ratau's - ends at the same two unlock
# methods, so the client sends these without knowing or caring which condition fired.
#
# "Cult" rather than a crusade region because the earning condition of each card isn't known
# without tracing all 85, and Cult is always reachable, so nothing here can become unreachable.
# Same reasoning as the Snail Shrines above.
#
# Some cards are only earnable through content a given player may never reach (post-game,
# Woolhaven). That costs a completionist a check and can never block the goal - no card is
# progression, and the game is beatable with none of them.
#
# Cards sold in the hub shops are skipped: their shop slot is already the check for earning
# that card, and buying it is the only way the game unlocks it. Giving them a second location
# here would pay twice for one card - and the second one could never fire anyway, since the
# client withholds the unlock on a shop purchase.
#
# Post-game cards get no location at all. Their checks would sit past the win condition of a
# Bishops or Witnesses goal, and an unreachable location fails generation rather than just
# making a slow seed. They come back when a goal that reaches the post-game exists.
#
# Cards tied to one crusade region live in that region under a separate category, so they get
# real logic from the region graph instead of an approximated depth band - and so
# set_depth_rules leaves them alone.
#
# The rest are sorted by how hard they are to earn, because the band a card lands in is its
# position in this table. Left in the game's own enum order the bands would be meaningless.
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
