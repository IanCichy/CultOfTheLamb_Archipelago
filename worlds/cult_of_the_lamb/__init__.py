from typing import Any, Dict, List, Tuple

from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld, World

from .items import (
    CURSES, SERMON_ITEM_OFFSET, SERMON_ITEM_UPGRADES, WEAPONS, CultOfTheLambItem,
    EquipmentData, PROGRESSIVE_REGION_ACCESS, TarotCardData, create_item, filler_table,
    item_table, offset, poolable_equipment, poolable_tarot_cards, sermon_item_counts,
    trap_table, weighted_filler_names,
)
from .locations import (
    FOLLOWER_MILESTONE_COUNT, SNAIL_SHRINE_COUNT, TAROT_SHOP_CARDS, TAROT_SHOP_HUBS,
    location_name_to_id,
    location_table,
)
from .options import (
    CultOfTheLambOptions, LegendaryWeapons, RegionAccessOrder, StartingTarotPool,
)
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
        "Curses": {name for name, data in item_table.items() if data.category == "Curse"},
        "Tarot Cards": {name for name, data in item_table.items() if data.category == "Tarot"},
        "Relics": {name for name, data in item_table.items() if data.category == "Relic"},
        "Sermon Upgrades": {name for name, data in item_table.items() if data.category == "Sermon"},
    }

    web = CultOfTheLambWeb()

    # The region that's free from seed start, followed by the other three in the order
    # they unlock via Progressive Bishop's Domain. Set once in generate_early so
    # create_regions/set_rules/fill_slot_data all agree on the same per-seed order.
    region_order: List[str]

    # The Tarot Cards this seed hands out, and the subset the player begins with. Starting
    # cards get neither a check nor an item - you can't earn a card you already have - so
    # create_regions, create_items and fill_slot_data all have to agree on the same pick,
    # which is why it happens once here.
    tarot_cards: List[TarotCardData]
    starting_tarot_cards: List[TarotCardData]

    # Same shape for the weapon and curse families: the seed's set, and the subset the player
    # begins with. create_regions, set_rules, create_items and fill_slot_data all have to
    # agree on the pick, so it's made once here.
    weapons: List[EquipmentData]
    starting_weapons: List[EquipmentData]
    curses: List[EquipmentData]
    starting_curses: List[EquipmentData]

    def generate_early(self) -> None:
        self.region_order = self.build_region_order()
        # Expand once rather than per filler item - this is sampled dozens of times per seed.
        self.weighted_filler = weighted_filler_names()
        self.tarot_cards, self.starting_tarot_cards = self.pick_tarot_cards()
        self.weapons, self.starting_weapons = self.pick_equipment(
            WEAPONS, self.options.randomize_weapons, self.options.starting_weapons.value)
        self.curses, self.starting_curses = self.pick_equipment(
            CURSES, self.options.randomize_curses, self.options.starting_curses.value)
        self.legendary_weapon_chance = self.pick_legendary_chance()

    def pick_legendary_chance(self) -> float:
        """How often a weapon offer is upgraded to its family's Legendary, 0 to 1.

        Forced to 0 without Woolhaven: Legendaries are DLC content, and a seed that offered
        them to a player who doesn't own it would hand out weapons the client can't resolve.
        """
        if not self.options.include_woolhaven:
            return 0.0

        return {
            LegendaryWeapons.option_off: 0.0,
            LegendaryWeapons.option_rare: 0.1,
            LegendaryWeapons.option_common: 0.25,
            LegendaryWeapons.option_always: 1.0,
        }[self.options.legendary_weapons.value]

    def pick_equipment(
        self, families: List[EquipmentData], enabled, starting_count: int
    ) -> Tuple[List[EquipmentData], List[EquipmentData]]:
        """A weapon/curse family set, and which of them the player begins holding.

        Clamped rather than an error, matching pick_tarot_cards: asking for 7 starting weapons
        in a seed without Woolhaven means "all of them", which is the obvious reading.
        """
        if not enabled:
            return [], []

        pool = poolable_equipment(families, bool(self.options.include_woolhaven))
        count = min(starting_count, len(pool))
        return pool, self.random.sample(pool, count)

    def pick_tarot_cards(self) -> Tuple[List[TarotCardData], List[TarotCardData]]:
        """The seed's card set, and which of them the player starts holding."""
        if not self.options.randomize_tarot_cards:
            return [], []

        cards = poolable_tarot_cards(
            bool(self.options.include_woolhaven), self.goal_reaches_postgame
        )

        # A shop card's only location is its shop slot, so with shop checks off it has none:
        # locations.py excludes it from the "Tarot Card - X" block unconditionally, and
        # regions.py only creates the slot locations when the option is on. Left managed, the
        # client would withhold the card and send nothing, so the slot could be bought over
        # and over for gold and never sell out. Handing them back to the game instead means
        # no item, no location, and buying one works exactly as it does in vanilla.
        if not self.options.tarot_shop_checks:
            shop_cards = {
                internal
                for cards_in_hub in TAROT_SHOP_CARDS.values()
                for _, internal in cards_in_hub
            }
            cards = [c for c in cards if c.internal not in shop_cards]

        if self.options.starting_tarot_pool == StartingTarotPool.option_vanilla_defaults:
            candidates = [c for c in cards if c.default]
        else:
            candidates = list(cards)

        # Clamped rather than an error: asking for 20 of the game's 15 defaults is a
        # reasonable thing to type, and giving all 15 is the obvious reading of it.
        count = min(self.options.starting_tarot_cards.value, len(candidates))
        starting = self.random.sample(candidates, count)

        return cards, starting

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
    def goal_reaches_postgame(self) -> bool:
        """Whether finishing this seed takes the player past the vanilla final boss.

        Nothing does yet: both goals are Bishops or Witnesses, which end before The One Who
        Waits. Written as a goal check rather than a constant so that adding a Narinder or
        Woolhaven goal (roadmap Sprints 1 and 11) switches the post-game tarot cards on by
        itself, instead of leaving a second thing to remember.
        """
        return False

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

        # One item per card the player doesn't already have. No cap and no competition with
        # filler: each of these has its own location - unlocking that card in game - so the
        # pool grows and shrinks with the location count rather than eating into it.
        starting = {c.display for c in self.starting_tarot_cards}
        for card in self.tarot_cards:
            if card.display not in starting:
                item_pool.append(self.create_item(card.display))

        # One item per family the player doesn't begin with, matching the locations
        # regions.py created one-for-one.
        for families, begun in ((self.weapons, self.starting_weapons),
                                (self.curses, self.starting_curses)):
            held = {e.display for e in begun}
            for family in families:
                if family.display not in held:
                    item_pool.append(self.create_item(family.display))

        # Count the locations actually created rather than the whole table: options can
        # disable whole blocks (sermons, cards, DLC content), and padding to the table size
        # would overfill the pool and fail generation.
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

            "randomizeTarotCards": bool(self.options.randomize_tarot_cards.value),
            # AP name -> TarotCards.Card enum name for every card this seed manages. Sent
            # rather than hardcoded client-side for the same reason as sermonUpgrades: display
            # names are nothing like enum names, and the two drifting would be silent.
            #
            # This is also the set the client revokes on connect and the set it watches for
            # unlocks, so both sides agree on exactly which cards Archipelago owns.
            "tarotCards": {card.display: card.internal for card in self.tarot_cards},
            # Granted back immediately after the revoke, so the player starts with these.
            "startingTarotCards": [card.internal for card in self.starting_tarot_cards],
            # "Tarot Card - <name>" ids, so the client can turn an unlock into a check.
            #
            # Two exclusions, both of which must agree with what regions.py created: shop cards,
            # whose check is the slot itself, and starting cards, whose unlock the player can
            # still trigger (their card was never written into the game's collection) but whose
            # location was never made. Leaving them out makes the client swallow the unlock.
            "tarotCardLocations": {
                card.internal: location_name_to_id[f"Tarot Card - {card.display}"]
                for card in self.tarot_cards
                if card not in self.starting_tarot_cards
                and f"Tarot Card - {card.display}" in location_name_to_id
            },

            "randomizeWeapons": bool(self.options.randomize_weapons.value),
            "randomizeCurses": bool(self.options.randomize_curses.value),
            # Chance a weapon offer is upgraded to its family's Legendary. Already resolved to
            # 0 without Woolhaven, so the client needs no DLC check of its own.
            "legendaryWeaponChance": self.legendary_weapon_chance,
            # AP item name -> EquipmentType enum name, for every family this seed manages.
            # This is the set the client filters on: a podium may only offer a family whose
            # item has arrived, and any variant of it (a Bane Axe rides along with the Axe).
            "weaponItems": {w.display: w.internal for w in self.weapons},
            "curseItems": {c.display: c.internal for c in self.curses},
            # Granted from the start, so they have no item and no location.
            "startingWeapons": [w.internal for w in self.starting_weapons],
            "startingCurses": [c.internal for c in self.starting_curses],
            # EquipmentType enum name -> location id, keyed by enum name because that's what
            # the client reads off PlayerWeapon.SetWeapon / PlayerSpells.SetSpell. Starting
            # families are absent, matching the locations regions.py declined to create.
            "weaponLocations": {
                w.internal: location_name_to_id[f"Weapon - {w.display}"]
                for w in self.weapons if w not in self.starting_weapons
            },
            "curseLocations": {
                c.internal: location_name_to_id[f"Curse - {c.display}"]
                for c in self.curses if c not in self.starting_curses
            },

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
