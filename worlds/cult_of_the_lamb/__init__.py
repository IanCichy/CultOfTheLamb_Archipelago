from typing import Any, Dict, List, Optional, Tuple

from BaseClasses import Tutorial
from Options import OptionError
from worlds.AutoWorld import WebWorld, World

from .items import (
    BROOM_LEVEL_COUNT, BUILDINGS,
    CURSES, DI_CURATED_BUNDLES, DI_CURATED_ITEM_NAMES, DI_CURATED_PROGRESSIVE,
    DI_EARLY_ITEM_NAMES,
    DI_CURATED_SINGLES, DI_FREE_UPGRADES, DI_INTERNAL_BY_DISPLAY,
    DI_POINT, DI_TIER_THRESHOLDS, DIVINE_INSPIRATION, SERMON_ITEM_OFFSET,
    SERMON_ITEM_UPGRADES, WEAPONS, CultOfTheLambItem, EquipmentData,
    PROGRESSIVE_REGION_ACCESS, TarotCardData, create_item, filler_table, item_table, offset,
    ap_item_name, poolable_equipment, poolable_tarot_cards, sermon_item_counts,
    sermon_item_name, trap_table,
    weighted_filler_names,
)
from .locations import (
    BISHOP_DUNGEON_LOCATIONS, DIVINE_INSPIRATION_COUNT, FOLLOWER_MILESTONE_COUNT,
    MINIBOSS_AND_WITNESS_KEYS, SNAIL_SHRINE_COUNT, TAROT_SHOP_CARDS,
    TAROT_SHOP_HUBS,
    location_name_to_id,
    location_table,
)
from .options import (
    CultOfTheLambOptions, DivineInspirationMode, LegendaryWeapons, RegionAccessOrder,
    StartingTarotPool,
)
from .regions import REGION_NAMES, SACRIFICE_GATED_REGION, create_regions
from .rules import set_rules

# Sent in slot data and logged by the client next to its own version, so a tester's log says which
# apworld built the seed. Not enforced - a mismatch is something to notice while reading a log, not
# a reason to refuse a connection. Keep in step with ArchipelagoPlugin.PluginVersion.
MOD_VERSION = "0.8.0"


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
        "Divine Inspiration": {
            name for name, data in item_table.items() if data.category == "DivineInspiration"
        },
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

        # The game has no way to give a default card back once it's been taken, so managing one
        # would create a location nobody can ever check.
        #
        # Every route to a permanent unlock ends at TarotCards.UnlockTrinket, and the only things
        # that reach it are scripted pickups with a CardOverride (Moon, Sun, Lovers2, Joker), the
        # Mystic Shop's own eight MysticCards, and the hub shop slots. None of those is a default
        # card. The three functions that would hand over an arbitrary unfound card -
        # GiveNewTrinket, UnlockRandomTrinket, UnlockTrinkets - have no callers at all, and a
        # crusade card pickup draws from GetUnusedFoundTrinkets, i.e. cards you already own, so it
        # is a run buff rather than an unlock. The one thing that does re-add a default is
        # GameManager.Awake, which writes straight into PlayerFoundTrinkets when it finds the list
        # empty - bypassing UnlockTrinket, so no check fires, and the client's sweep removes it
        # again a second later.
        #
        # Handed back to the game for the same reasons as the shop cards above: no item, no
        # location, not revoked, and the player simply keeps them as they would in vanilla.
        cards = [c for c in cards if not c.default]

        if self.options.starting_tarot_pool == StartingTarotPool.option_vanilla_defaults:
            # Empty by construction now that defaults aren't managed, so this asks for no extra
            # starting cards - which is the honest answer, since all 15 are already in hand.
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
    def divine_inspiration_enabled(self) -> bool:
        """Whether this seed creates the 69 Divine Inspiration locations."""
        return self.options.divine_inspiration_mode != DivineInspirationMode.option_off

    @property
    def divine_inspiration_is_curated(self) -> bool:
        """Whether this seed uses the regrouped 38-item block rather than one item per upgrade."""
        return (self.options.divine_inspiration_mode
                == DivineInspirationMode.option_curated_checks)

    @property
    def divine_inspiration_location_count(self) -> int:
        """How many checks this block creates.

        Only curated_checks makes this an option; every other mode uses all 69, which is what
        keeps the option a pure addition rather than a change to existing seeds.
        """
        if self.divine_inspiration_is_curated:
            return self.options.divine_inspiration_checks.value
        return DIVINE_INSPIRATION_COUNT

    @property
    def divine_inspiration_gives_items(self) -> Optional[List[str]]:
        """The item names that buy Divine Inspiration unlocks, or None if this mode has none.

        A one-element list means many copies of one item (checks_and_points); a longer one is the
        pool verbatim, one entry per copy - so curated_checks repeats a progressive name once per
        tier. create_items builds the pool from this same answer so the two can't disagree.
        """
        mode = self.options.divine_inspiration_mode

        if mode == DivineInspirationMode.option_checks_and_points:
            return [DI_POINT]
        if mode == DivineInspirationMode.option_checks_and_techs:
            return [u.item_name for u in DIVINE_INSPIRATION]
        if mode == DivineInspirationMode.option_curated_checks:
            return list(DI_CURATED_ITEM_NAMES)

        # off and checks_only: the player keeps their own ability points.
        return None

    @property
    def goal_reaches_postgame(self) -> bool:
        """Whether finishing this seed takes the player past the vanilla final boss.

        Still nothing, Narinder included - an earlier version of this docstring expected that
        goal to flip it. It doesn't: the Mystic Cellar and the corrupted set open *after*
        Narinder dies, so they sit past that win condition just as they sit past the Bishops
        one. Switching them on would strand other players' items behind content the winner has
        no reason to play.

        A goal that genuinely requires post-game content - Woolhaven, roadmap Sprint 11 - is
        what this is waiting for.
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
                    item_pool.append(self.create_item(sermon_item_name(name)))

        # One item per card the player doesn't already have. No cap and no competition with
        # filler: each of these has its own location - unlocking that card in game - so the
        # pool grows and shrinks with the location count rather than eating into it.
        starting = {c.display for c in self.starting_tarot_cards}
        for card in self.tarot_cards:
            if card.display not in starting:
                item_pool.append(self.create_item(card.item_name))

        # One item per family the player doesn't begin with, matching the locations
        # regions.py created one-for-one.
        for families, begun in ((self.weapons, self.starting_weapons),
                                (self.curses, self.starting_curses)):
            held = {e.display for e in begun}
            for family in families:
                if family.display not in held:
                    item_pool.append(self.create_item(family.item_name))

        # One item per Divine Inspiration location, so spending them all is exactly enough to
        # clear the block and the pool never has to compete with filler for room.
        di_items = self.divine_inspiration_gives_items
        if di_items is not None:
            if len(di_items) == 1:
                for _ in range(self.divine_inspiration_location_count):
                    item_pool.append(self.create_item(di_items[0]))
            else:
                for name in di_items:
                    item_pool.append(self.create_item(name))

        # Without passive lumber and stone, and without the only source of planks and bricks, the
        # base economy has no floor - so these two are pinned to sphere 1 rather than left to land
        # wherever. distribute_early_items has a non-advancement branch (Fill.py:441), so neither
        # needs to be progression to qualify.
        #
        # Deliberately not LocationProgressType.PRIORITY: priority locations are filled from the
        # progression pool only (Fill.py:524), so marking checks priority would push these out
        # rather than pull them early. Keep this list short - overfilling sphere 1 logs "Ran out
        # of early locations" and silently falls back to a normal fill.
        if self.divine_inspiration_is_curated:
            for name in DI_EARLY_ITEM_NAMES:
                self.multiworld.local_early_items[self.player][name] = 1

        # Same treatment, and for a sharper reason: the four Bishops' domains are the only thing
        # gating this world, so where their keys land decides whether the seed is paced or a wait.
        # Left to the fill they are three items among the whole multiworld's locations, and a seed
        # that put all three in other players' games opened the second domain at sphere 37 of 64 -
        # hours of one biome before anything new. Pinning one copy here guarantees a second domain
        # early without touching the other two, which stay in the pool and keep the seed a seed.
        #
        # Only the first copy: pinning more would spend the sphere-1 budget the comment above
        # warns about, and would flatten the pacing this is trying to protect.
        if self.regions_are_gated:
            self.multiworld.local_early_items[self.player][PROGRESSIVE_REGION_ACCESS] = 1

        # Count the locations actually created rather than the whole table: options can
        # disable whole blocks (sermons, cards, DLC content), and padding to the table size
        # would overfill the pool and fail generation.
        remaining = len(self.multiworld.get_unfilled_locations(self.player)) - len(item_pool)

        # A negative `remaining` means more items than places to put them, and `range()` of a
        # negative number is simply empty - so without this the over-full pool ships and AP fails
        # much later with "Unplaced Items remaining in itempool", naming nothing the player set.
        #
        # curated_checks is how you get here: it always contributes all 38 of its items while
        # divine_inspiration_checks decides how many locations the block has, so a low count plus
        # other blocks switched off runs out of room.
        if remaining < 0:
            raise OptionError(
                f"This Cult of the Lamb seed has {-remaining} more item(s) than locations to put "
                f"them in, so it can't be generated. The usual cause is "
                f"divine_inspiration_checks ({self.options.divine_inspiration_checks.value}) "
                f"being low while other check blocks are switched off - the curated Divine "
                f"Inspiration block always contributes "
                f"{len(DI_CURATED_ITEM_NAMES)} items regardless of how many checks it has. "
                f"Raise divine_inspiration_checks, or turn another check block back on."
            )

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
            "worldVersion": MOD_VERSION,

            "goal": self.options.goal.value,
            "requiredCount": self.options.required_count.value,

            # Guidance only - no location, no item, no rule. The client renders these as an
            # objective group in the game's own quest log, reading each line's progress back
            # out of the keys already in this dict. Deliberately independent of the trim
            # below: wanting a quieter game and wanting a checklist aren't the same wish.
            "objectiveGuide": bool(self.options.archipelago_objective_guide.value),
            "objectiveGuidePinning": self.options.objective_guide_pinning.value,
            # How much of the game's own follower-quest table the client leaves in rotation.
            # Vanilla quests are the main follower-loyalty-XP source, so the default trims
            # rather than wipes - see options.py.
            "vanillaFollowerQuests": self.options.vanilla_follower_quests.value,

            # Boss check ids, so the client stops hardcoding them. Keyed by the game's own
            # identifier: MiniBossController.name for minibosses and Witnesses, the FollowerLocation
            # member name for Bishops - which is what the client reads off each kill.
            "bossKeyLocations": {
                key: location_name_to_id[name]
                for name, key in MINIBOSS_AND_WITNESS_KEYS.items()
            },
            "bishopLocations": {
                dungeon: location_name_to_id[name]
                for name, dungeon in BISHOP_DUNGEON_LOCATIONS.items()
            },

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
            "tarotCards": {card.item_name: card.internal for card in self.tarot_cards},
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
            "weaponItems": {w.item_name: w.internal for w in self.weapons},
            "curseItems": {c.item_name: c.internal for c in self.curses},
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

            "divineInspirationMode": self.options.divine_inspiration_mode.value,
            "divineInspirationShuffle": self.options.divine_inspiration_shuffle.value,
            # Most Devotion one ability point may cost; 0 leaves the game's curve alone. The
            # client clamps DataManager.GetTargetXP to this.
            "divineInspirationDevotionCap":
                self.options.divine_inspiration_devotion_cap.value,
            # "Divine Inspiration N" ids are contiguous from here, so the client turns a count
            # of tree unlocks straight into a check id - same shape as the sermon block.
            "divineInspirationLocationBaseId": location_name_to_id["Divine Inspiration 1"],
            "divineInspirationLocationCount": self.divine_inspiration_location_count,
            # AP item name -> UpgradeSystem.Type, for checks_and_techs and for curated_checks'
            # single-upgrade items. Sent rather than hardcoded client-side for the same reason as
            # sermonUpgrades: these are ScriptableObject data, and the two sides drifting would
            # be silent.
            "divineInspirationUpgrades": (
                {ap_item_name("DivineInspiration", d): DI_INTERNAL_BY_DISPLAY[d]
                 for d in DI_CURATED_SINGLES}
                if self.divine_inspiration_is_curated
                else {u.item_name: u.internal for u in DIVINE_INSPIRATION}
            ),
            # curated_checks only, and empty otherwise. Bundles grant every upgrade at once;
            # progressives grant the Nth on the Nth copy, so the client has to keep a count.
            "divineInspirationBundles": (
                {g.item_name: list(g.upgrades) for g in DI_CURATED_BUNDLES}
                if self.divine_inspiration_is_curated else {}
            ),
            "divineInspirationProgressive": (
                {g.item_name: list(g.upgrades) for g in DI_CURATED_PROGRESSIVE}
                if self.divine_inspiration_is_curated else {}
            ),
            # Unlocked on connect, with neither a check nor an item. Without these a fresh save
            # can't build a bed, a farm plot or the Temple.
            "divineInspirationFreeUpgrades": (
                list(DI_FREE_UPGRADES) if self.divine_inspiration_is_curated else []
            ),
            # The item that carries one ability point in checks_and_points.
            "divineInspirationPointItem": DI_POINT,
            # Cumulative unlocks per tier, so a shuffle can rebuild the tree without the client
            # having to re-derive thresholds it would only get wrong.
            "divineInspirationTierThresholds": DI_TIER_THRESHOLDS,
            # Deterministic per seed so a reconnect rebuilds the identical tree.
            "divineInspirationShuffleSeed": self.random.getrandbits(31),

            # Pacing caps. Independent of whether the matching block is randomized - they're
            # quality of life, not randomizer settings, so a seed with sermons off still gets
            # the sermon cap. All three are the same shape client-side: one postfix clamping a
            # single public static.
            "sermonXpCap": self.options.sermon_xp_cap.value,
            "buildTimeCap": self.options.build_time_cap.value,

            "buildingChecks": bool(self.options.building_checks.value),
            # StructureBrain.TYPES name -> location id. Keyed by enum name because that's what
            # the client reads off Structures_BuildSite.Data.ToBuildType; display names are ours,
            # not the game's.
            "buildingLocations": {
                b.internal: location_name_to_id[f"Build - {b.display}"] for b in BUILDINGS
            },

            "broomChecks": bool(self.options.broom_checks.value),
            # "Broom Level N" ids are contiguous from here, so the client turns
            # DataManager.ChoreXPLevel straight into a check id.
            "broomLocationBaseId": location_name_to_id["Broom Level 1"],
            "broomLocationCount": BROOM_LEVEL_COUNT,

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
                sermon_item_name(name): [
                    internal for internal, dlc in tiers
                    if self.options.include_woolhaven or not dlc
                ]
                for name, tiers in SERMON_ITEM_UPGRADES.items()
            },
        }
