import logging
from typing import Any, Dict, List, Optional, Tuple

from Options import OptionError
from worlds.AutoWorld import WebWorld, World

from .items import (
    BROOM_LEVEL_COUNT, BUILDINGS,
    CURSES, DI_CURATED_BUNDLES, DI_CURATED_ITEM_NAMES, DI_CURATED_PROGRESSIVE,
    DI_EARLY_ITEM_NAMES,
    DI_CURATED_SINGLES, DI_FREE_UPGRADES, DI_INTERNAL_BY_DISPLAY,
    DI_POINT, DI_TIER_THRESHOLDS, DIVINE_INSPIRATION,
    SERMON_ITEM_UPGRADES, TAROT_CARDS, WEAPONS, CultOfTheLambItem, EquipmentData,
    PROGRESSIVE_REGION_ACCESS, TarotCardData, create_item, item_table,
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
    CultOfTheLambOptions, DivineInspirationChecks, DivineInspirationMode, FAST_BUILD_MINUTES,
    LegendaryWeapons, ObjectiveGuidePinning, RegionAccessOrder, StartingCurses,
    StartingTarotPool, StartingWeapons,
)
from .regions import REGION_NAMES, SACRIFICE_GATED_REGION, create_regions
from .rules import set_rules

# Sent in slot data and logged by the client next to its own version, so a player's log says which
# apworld built the seed. It is not enforced. A mismatch is something to notice while reading a
# log, not a reason to refuse a connection. Keep in step with ArchipelagoPlugin.PluginVersion.
MOD_VERSION = "0.9.1"


class CultOfTheLambWeb(WebWorld):
    theme = "dirt"
    bug_report_page = "https://github.com/IanCichy/CultOfTheLamb_Archipelago/issues"

    # Starting points on the options page, so a new player doesn't have to read 28 options to get
    # a seed. Each one has been generated and checked; keep it that way when editing, because a
    # preset that fails generation only surfaces when someone picks it.
    #
    # None of these turn Woolhaven on. A preset is a blind pick from a dropdown, and the DLC is
    # the one option that makes a seed unbeatable for the player who guesses wrong.
    options_presets = {
        # Vanilla region order and no traps, but every randomizer left on: the point of a first
        # seed is trading items with the room, so trimming those would make a duller game rather
        # than a gentler one. Broom is the one block cut, being the grindiest. The whole checklist
        # is pinned on screen, which costs two of the game's three tracker slots but is the
        # fastest way to learn what a seed wants from you.
        "First Seed": {
            "goal": "bishops",
            "required_count": 4,
            "region_access_order": "vanilla_order",
            "objective_guide_pinning": "everything",
            "divine_inspiration_mode": "checks_and_points",
            "divine_inspiration_devotion_cap": 70,
            "sermon_xp_cap": 20,
            "randomize_sermon_upgrades": True,
            "randomize_weapons": True,
            "randomize_curses": True,
            "randomize_tarot_cards": True,
            "vanilla_follower_quests": "thin_trickle",
            "broom_checks": False,
            "trap_percentage": 0,
            "fast_build": True,
        },
        # ~130 locations rather than the default 218. Tarot and broom go, and curated_checks is
        # the only way to shorten Divine Inspiration, which is the biggest block by far.
        "Short Session": {
            "goal": "bishops",
            "required_count": 2,
            "region_access_order": "randomized_safe_start",
            "divine_inspiration_mode": "curated_checks",
            "divine_inspiration_checks": 20,
            "divine_inspiration_devotion_cap": 50,
            "sermon_xp_cap": 12,
            "randomize_tarot_cards": False,
            "tarot_shop_checks": False,
            "broom_checks": False,
            "fast_build": True,
        },
        # Every block on, traps left at the default 5%, and the caps loosened rather than
        # removed. Uncapped is roughly 24,000 Devotion and ~150 sermons, which is a different
        # hobby.
        "Long Haul": {
            "goal": "narinder",
            "region_access_order": "randomized",
            "divine_inspiration_mode": "checks_and_points",
            "divine_inspiration_devotion_cap": 150,
            "sermon_xp_cap": 40,
            "randomize_sermon_upgrades": True,
            "randomize_tarot_cards": True,
            "randomize_weapons": True,
            "randomize_curses": True,
            "building_checks": True,
            "broom_checks": True,
            "snail_shrine_checks": True,
            "follower_milestone_checks": True,
            "tarot_shop_checks": True,
            "fast_build": True,
        },
        # The tree unlocks itself in a shuffled order, you start with three random cards, and
        # one filler in five is a trap. Also the longest goal short of Narinder: four Witnesses means
        # beating all four Bishops first.
        "Chaos": {
            "goal": "witnesses",
            "required_count": 4,
            "region_access_order": "randomized",
            "divine_inspiration_mode": "checks_and_techs",
            "divine_inspiration_shuffle": "true_random",
            "divine_inspiration_devotion_cap": 70,
            "sermon_xp_cap": 20,
            "starting_tarot_cards": 3,
            "starting_tarot_pool": "any",
            "trap_percentage": 20,
            "fast_build": True,
        },
    }


class CultOfTheLambWorld(World):
    """
    Build a cult and crusade through four corrupted regions. Archipelago gates those regions and
    scatters the weapons, curses, sermons, tarot cards and Divine Inspiration upgrades across the
    multiworld.
    """
    game = "Cult of the Lamb"
    options_dataclass = CultOfTheLambOptions
    options: CultOfTheLambOptions
    topology_present = True

    # Universal Tracker re-runs generation locally, and the server doesn't store the per-seed
    # rolls. This flag and interpret_slot_data below let it rebuild them from slot data instead.
    ut_can_gen_without_yaml = True

    item_name_to_id = {name: data.code for name, data in item_table.items()}
    location_name_to_id = location_name_to_id
    item_name_groups = {
        "Weapons": {name for name, data in item_table.items() if data.category == "Weapon"},
        "Curses": {name for name, data in item_table.items() if data.category == "Curse"},
        "Tarot Cards": {name for name, data in item_table.items() if data.category == "Tarot"},
        "Relics": {name for name, data in item_table.items() if data.category == "Relic"},
        "Sermon Upgrades": {
            name for name, data in item_table.items() if data.category == "Sermon"
        },
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
    # cards get neither a check nor an item, because you can't earn a card you already have.
    # create_regions, create_items and fill_slot_data all have to agree on the same pick, which
    # is why it happens once here.
    tarot_cards: List[TarotCardData]
    starting_tarot_cards: List[TarotCardData]

    # Same shape for the weapon and curse families: the seed's set, and the subset the player
    # begins with. create_regions, set_rules, create_items and fill_slot_data all have to
    # agree on the pick, so it's made once here.
    weapons: List[EquipmentData]
    starting_weapons: List[EquipmentData]
    curses: List[EquipmentData]
    starting_curses: List[EquipmentData]

    @staticmethod
    def interpret_slot_data(slot_data: Dict[str, Any]) -> Dict[str, Any]:
        """Universal Tracker hook. The return value lands in re_gen_passthrough[game]."""
        return slot_data

    def generate_early(self) -> None:
        """Per-seed choices every later step reads: region order, starting equipment and cards."""
        passthrough = getattr(self.multiworld, "re_gen_passthrough", {})
        if self.game in passthrough:
            self.load_from_slot_data(passthrough[self.game])
            return

        self.warn_about_ignored_options()
        self.region_order = self.build_region_order()
        # Expand once rather than per filler item. This is sampled dozens of times per seed.
        self.weighted_filler = weighted_filler_names()
        self.tarot_cards, self.starting_tarot_cards = self.pick_tarot_cards()
        self.weapons, self.starting_weapons = self.pick_equipment(
            WEAPONS, self.options.randomize_weapons, self.options.starting_weapons.value)
        self.curses, self.starting_curses = self.pick_equipment(
            CURSES, self.options.randomize_curses, self.options.starting_curses.value)
        self.legendary_weapon_chance = self.pick_legendary_chance()

    def load_from_slot_data(self, sd: Dict[str, Any]) -> None:
        """Rebuild generate_early's per-seed choices from slot data, for Universal Tracker.

        Options first, because regions_are_gated and divine_inspiration_is_curated read them.
        No warn_about_ignored_options: under UT it would repeat the player's warnings on their
        machine for a seed they cannot change.
        """
        opts = self.options
        opts.goal.value = sd["goal"]
        opts.required_count.value = sd["requiredCount"]
        opts.include_woolhaven.value = int(sd["includeWoolhaven"])
        opts.randomize_sermon_upgrades.value = int(sd["randomizeSermonUpgrades"])
        opts.follower_milestone_checks.value = int(sd["followerMilestoneChecks"])
        opts.snail_shrine_checks.value = int(sd["snailShrineChecks"])
        opts.randomize_tarot_cards.value = int(sd["randomizeTarotCards"])
        opts.tarot_shop_checks.value = int(sd["tarotShopChecks"])
        opts.randomize_weapons.value = int(sd["randomizeWeapons"])
        opts.randomize_curses.value = int(sd["randomizeCurses"])
        opts.building_checks.value = int(sd["buildingChecks"])
        opts.broom_checks.value = int(sd["broomChecks"])
        opts.divine_inspiration_mode.value = sd["divineInspirationMode"]
        opts.divine_inspiration_checks.value = sd["divineInspirationLocationCount"]

        # Only regions_are_gated reads this now, and it just asks whether the seed is
        # all_unlocked, so the exact flavour of randomization need not survive the round trip.
        opts.region_access_order.value = (
            RegionAccessOrder.option_vanilla_order if sd["randomizeRegionAccess"]
            else RegionAccessOrder.option_all_unlocked
        )

        self.region_order = list(sd["regionOrder"])
        self.weighted_filler = weighted_filler_names()
        self.legendary_weapon_chance = sd["legendaryWeaponChance"]

        # Slot-data order, not table order: the starting picks are a random sample whose order
        # the wire already preserves.
        cards = {c.internal: c for c in TAROT_CARDS}
        self.tarot_cards = [cards[i] for i in sd["tarotCards"].values()]
        self.starting_tarot_cards = [cards[i] for i in sd["startingTarotCards"]]

        self.weapons, self.starting_weapons = self._equipment_from_slot_data(
            WEAPONS, sd["weaponItems"], sd["startingWeapons"])
        self.curses, self.starting_curses = self._equipment_from_slot_data(
            CURSES, sd["curseItems"], sd["startingCurses"])

    @staticmethod
    def _equipment_from_slot_data(
        table: List[EquipmentData], managed: Dict[str, str], starting: List[str],
    ) -> Tuple[List[EquipmentData], List[EquipmentData]]:
        """Families this seed manages, and the subset it starts you with, in the seed's order."""
        by_internal = {e.internal: e for e in table}
        return (
            [by_internal[i] for i in managed.values()],
            [by_internal[i] for i in starting],
        )

    def warn_about_ignored_options(self) -> None:
        """Log the options this YAML set that the rest of its own settings switch off.

        Several options here are only read when another one is in a particular state, and the
        rest of the world resolves that by quietly doing nothing. That is the right behaviour -
        erroring would break YAMLs that generate perfectly well today - but silence means a
        player who asked for 5 starting tarot cards and got 0 has no way to find out why.

        Only fires where the value differs from the option's own default, so leaving an
        irrelevant option alone costs nobody a warning. Warnings, never errors: every one of
        these still generates a valid seed.
        """
        opts = self.options
        ignored: List[str] = []

        if opts.starting_tarot_cards.value > 0:
            if not opts.randomize_tarot_cards:
                ignored.append(
                    "starting_tarot_cards is set, but randomize_tarot_cards is off, so this "
                    "seed manages no cards to start you with"
                )
            elif opts.starting_tarot_pool == StartingTarotPool.option_vanilla_defaults:
                ignored.append(
                    "starting_tarot_cards is set, but starting_tarot_pool is vanilla_defaults, "
                    "which draws from the 15 cards you already begin with. Set "
                    "starting_tarot_pool to 'any' to actually get extra cards"
                )

        if opts.starting_weapons.value > StartingWeapons.range_start and not opts.randomize_weapons:
            ignored.append(
                "starting_weapons is set, but randomize_weapons is off, so the game hands out "
                "weapons on its own schedule and there is nothing to start you with"
            )

        if opts.starting_curses.value > StartingCurses.range_start and not opts.randomize_curses:
            ignored.append(
                "starting_curses is set, but randomize_curses is off, so the game hands out "
                "curses on its own schedule and there is nothing to start you with"
            )

        # The one that catches people out most: divine_inspiration_checks sits in the example
        # YAML looking like the main length control, and four of the five modes ignore it.
        if (opts.divine_inspiration_checks.value != DivineInspirationChecks.default
                and not self.divine_inspiration_is_curated):
            ignored.append(
                f"divine_inspiration_checks is set to "
                f"{opts.divine_inspiration_checks.value}, but divine_inspiration_mode is "
                f"{opts.divine_inspiration_mode.current_key}, which always uses all "
                f"{DIVINE_INSPIRATION_COUNT}. Only curated_checks reads this option"
            )

        if opts.legendary_weapons != LegendaryWeapons.option_off and not opts.include_woolhaven:
            ignored.append(
                "legendary_weapons is set, but Legendary weapons are Woolhaven content and "
                "include_woolhaven is off, so it has been forced off"
            )

        if not opts.archipelago_objective_guide and opts.objective_guide_pinning != ObjectiveGuidePinning.default:
            ignored.append(
                "objective_guide_pinning is set, but archipelago_objective_guide is off, so "
                "there is no checklist to pin"
            )

        for message in ignored:
            logging.warning("Cult of the Lamb (%s): %s.", self.player_name, message)

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
        # card. The three functions that would hand over an arbitrary unfound card, namely
        # GiveNewTrinket, UnlockRandomTrinket and UnlockTrinkets, have no callers at all. A
        # crusade card pickup draws from GetUnusedFoundTrinkets, meaning cards you already own,
        # so it is a run buff rather than an unlock. The one thing that does re-add a default is
        # GameManager.Awake, which writes straight into PlayerFoundTrinkets when it finds the list
        # empty. That bypasses UnlockTrinket, so no check fires, and the client's sweep removes it
        # again a second later.
        #
        # Handed back to the game for the same reasons as the shop cards above. They get no item
        # and no location, are not revoked, and the player keeps them as they would in
        # vanilla.
        cards = [c for c in cards if not c.default]

        if self.options.starting_tarot_pool == StartingTarotPool.option_vanilla_defaults:
            # Empty by construction now that defaults aren't managed, so this asks for no extra
            # starting cards, which is the honest answer, since all 15 are already in hand.
            candidates = [c for c in cards if c.default]
        else:
            candidates = list(cards)

        # Clamped rather than an error, so a count set alongside the vanilla_defaults pool
        # is ignored instead of failing generation. warn_about_ignored_options says so at
        # the time, since the candidate list is empty in that branch.
        count = min(self.options.starting_tarot_cards.value, len(candidates))
        starting = self.random.sample(candidates, count)

        return cards, starting

    def build_region_order(self) -> List[str]:
        """The unlock order for this seed. Index 0 is free. The rest gate behind one more
        Progressive Bishop's Domain each (see rules.set_rules).

        REGION_NAMES is already in the game's own order, so vanilla_order is just a copy.
        """
        order = self.options.region_access_order

        if order in (RegionAccessOrder.option_vanilla_order,
                     RegionAccessOrder.option_all_unlocked):
            return list(REGION_NAMES)

        if order == RegionAccessOrder.option_randomized_safe_start:
            # Silk Cradle's door costs a Follower sacrifice to open, which is a punishing
            # opening move before there's a flock to spare. So shuffle until it isn't first.
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
    def divine_inspiration_grants_upgrades(self) -> bool:
        """Whether Archipelago hands out the upgrades themselves rather than ability points.

        Both granting modes need the same five tier-1 upgrades free at the start. Without a bed,
        a farm plot or the Temple a save cannot function, and the Temple is worse than
        inconvenient: sermons happen there, so a seed that buried it could leave the whole sermon
        block unreachable with nothing in rules.py to notice.
        """
        return self.options.divine_inspiration_mode in (
            DivineInspirationMode.option_checks_and_techs,
            DivineInspirationMode.option_curated_checks,
        )

    @property
    def divine_inspiration_location_count(self) -> int:
        """How many checks this block creates.

        Only curated_checks makes this an option. Every other mode uses all 69, which is what
        keeps the option a pure addition rather than a change to existing seeds.
        """
        if self.divine_inspiration_is_curated:
            return self.options.divine_inspiration_checks.value
        return DIVINE_INSPIRATION_COUNT

    @property
    def divine_inspiration_gives_items(self) -> Optional[List[str]]:
        """The item names that buy Divine Inspiration unlocks, or None if this mode has none.

        A one-element list means many copies of one item, as in checks_and_points. A longer one is
        the pool verbatim, one entry per copy, so curated_checks repeats a progressive name once
        per tier. create_items builds the pool from this same answer so the two can't disagree.
        """
        mode = self.options.divine_inspiration_mode

        if mode == DivineInspirationMode.option_checks_and_points:
            return [DI_POINT]
        if mode == DivineInspirationMode.option_checks_and_techs:
            return [u.item_name for u in DIVINE_INSPIRATION
                    if u.internal not in DI_FREE_UPGRADES]
        if mode == DivineInspirationMode.option_curated_checks:
            return list(DI_CURATED_ITEM_NAMES)

        # off and checks_only: the player keeps their own ability points.
        return None

    @property
    def goal_reaches_postgame(self) -> bool:
        """Whether finishing this seed takes the player past the vanilla final boss.

        Still nothing, Narinder included: the Mystic Cellar and the corrupted set open *after*
        Narinder dies, so they sit past that win condition just as they sit past the Bishops one,
        and switching them on would strand other players' items behind content the winner has no
        reason to play. This is waiting on a goal that genuinely requires post-game content.
        """
        return False

    @property
    def regions_are_gated(self) -> bool:
        """False only for all_unlocked, where no access items exist at all."""
        return self.options.region_access_order != RegionAccessOrder.option_all_unlocked

    def create_regions(self) -> None:
        """Builds Menu -> Cult -> the four crusade regions, and hangs this seed's locations off
        them. Blocks whose option is off contribute no locations at all."""
        create_regions(self)

    def create_item(self, name: str) -> CultOfTheLambItem:
        """One item by name, with the classification `item_table` records for it."""
        return create_item(name, self.player)

    def create_items(self) -> None:
        """Fills the item pool, then pads with filler and traps to exactly match the location
        count. Every block that adds locations adds its items here."""
        item_pool: List[CultOfTheLambItem] = []

        if self.regions_are_gated:
            # One fewer copy than there are regions. The first region in region_order is always
            # free, so only the remaining N-1 need to be unlocked.
            for _ in range(len(REGION_NAMES) - 1):
                item_pool.append(self.create_item(PROGRESSIVE_REGION_ACCESS))

        if self.options.randomize_sermon_upgrades:
            counts = sermon_item_counts(bool(self.options.include_woolhaven))
            for name, count in counts.items():
                for _ in range(count):
                    item_pool.append(self.create_item(sermon_item_name(name)))

        # One item per card the player doesn't already have. No cap and no competition with
        # filler, because each of these has its own location, which is unlocking that card in
        # game. The pool grows and shrinks with the location count rather than eating into it.
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
        # base economy has no floor. These two are pinned to sphere 1 rather than left to land
        # wherever. distribute_early_items has a non-advancement branch (Fill.py:441), so neither
        # needs to be progression to qualify.
        #
        # Not LocationProgressType.PRIORITY. Priority locations are filled from the
        # progression pool only (Fill.py:524), so marking checks priority would push these out
        # rather than pull them early. Keep this list short. Overfilling sphere 1 logs "Ran out
        # of early locations" and silently falls back to a normal fill.
        if self.divine_inspiration_is_curated:
            for name in DI_EARLY_ITEM_NAMES:
                self.multiworld.local_early_items[self.player][name] = 1

        # Same treatment, and for a sharper reason: the three Progressive Bishop's Domain copies
        # are the widest gate here, so where they land decides whether the seed is paced or a wait.
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
        # negative number is empty. Without this the over-full pool ships and AP fails
        # much later with "Unplaced Items remaining in itempool", naming nothing the player set.
        #
        # curated_checks is how you get here. It always contributes all 38 of its items while
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
        """Access rules and the completion condition. See rules.py for why most blocks get
        reachability bands rather than real item requirements."""
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
        """The wire contract with the C# client: every id map and option the mod reads at connect.
        Keys here are matched by literal string on the client side, so renaming one breaks it."""
        return {
            "worldVersion": MOD_VERSION,

            "goal": self.options.goal.value,
            "requiredCount": self.options.required_count.value,

            # Guidance only. No location, no item, no rule. The client renders these as an
            # objective group in the game's own quest log, reading each line's progress back
            # out of the keys already in this dict. Independent of the trim
            # below, because wanting a quieter game and wanting a checklist aren't the same wish.
            "objectiveGuide": bool(self.options.archipelago_objective_guide.value),
            "objectiveGuidePinning": self.options.objective_guide_pinning.value,
            # How much of the game's own follower-quest table the client leaves in rotation.
            # Vanilla quests are the main follower-loyalty-XP source, so the default trims
            # rather than wipes. See options.py.
            "vanillaFollowerQuests": self.options.vanilla_follower_quests.value,

            # Boss check ids, so the client stops hardcoding them. Keyed by the game's own
            # identifier. That is MiniBossController.name for minibosses and Witnesses, and the
            # FollowerLocation member name for Bishops, which is what the client reads off each
            # kill.
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
            # Most Devotion one ability point may cost. 0 leaves the game's curve alone. The
            # client clamps DataManager.GetTargetXP to this.
            "divineInspirationDevotionCap":
                self.options.divine_inspiration_devotion_cap.value,
            # "Divine Inspiration N" ids are contiguous from here, so the client turns a count
            # of tree unlocks straight into a check id. Same shape as the sermon block.
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
                list(DI_FREE_UPGRADES) if self.divine_inspiration_grants_upgrades else []
            ),
            # The item that carries one ability point in checks_and_points.
            "divineInspirationPointItem": DI_POINT,
            # Cumulative unlocks per tier, so a shuffle can rebuild the tree without the client
            # having to re-derive thresholds it would only get wrong.
            "divineInspirationTierThresholds": DI_TIER_THRESHOLDS,
            # Deterministic per seed so a reconnect rebuilds the identical tree.
            "divineInspirationShuffleSeed": self.random.getrandbits(31),

            # Pacing caps. Independent of whether the matching block is randomized, because
            # they're quality of life rather than randomization settings, so a seed with sermons off
            # still gets the sermon cap. All three are the same shape client-side. Each is one
            # postfix clamping a single public static.
            "sermonXpCap": self.options.sermon_xp_cap.value,
            # A toggle player-side, a number on the wire: the client clamps build times to
            # this, and 0 means leave the game's own times alone.
            "buildTimeCap": FAST_BUILD_MINUTES if self.options.fast_build else 0,

            "buildingChecks": bool(self.options.building_checks.value),
            # StructureBrain.TYPES name -> location id. Keyed by enum name because that's what
            # the client reads off Structures_BuildSite.Data.ToBuildType. Display names are ours,
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
            # what the client can read off a BuyEntry. Display names differ completely, since
            # "The Burning Dead" is Skull, and would be useless to match on.
            "tarotShopLocations": {
                internal: location_name_to_id[f"{TAROT_SHOP_HUBS[region]} - {display}"]
                for region, cards in TAROT_SHOP_CARDS.items()
                for display, internal in cards
            },
            # Sermon item name -> the UpgradeSystem.Type names it unlocks, in order. A
            # single-entry list is a standalone upgrade. A longer one is a progressive chain
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
