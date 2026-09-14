"""Generation tests for the Divine Inspiration tree (Sprint 0e).

The block is 69 sequential locations whose *items* change per mode while the locations don't.
That asymmetry is the thing worth defending, and it's what these assert:

- every mode creates the same 69 locations (or none, when off);
- the item pool matches the mode exactly - one name many times, or many names once, or nothing;
- the items are *not* progression and carry no rules, because the checks fire on filling the
  Devotion meter and nothing this world hands out makes that faster.

That last one is the easy thing to get wrong. An earlier version gated "Divine Inspiration N" on
holding N items, which was true when the check fired on *spending* a point and became a lie the
moment it moved to *earning* one. A stale rule there would be invisible in a spoiler and would
poison every other player's sphere math.

The shuffle axis is asserted to be *logic-neutral*: it rearranges tiers in-game, and because the
tier gate is a count rather than a prerequisite chain it must not move a single item or location.
"""

import unittest

from BaseClasses import LocationProgressType
from test.bases import WorldTestBase

from ..items import (
    DI_CURATED_BUNDLES, DI_CURATED_ITEM_NAMES, DI_CURATED_PROGRESSIVE, DI_CURATED_SINGLES,
    DI_FREE_UPGRADES, DI_INTERNAL_BY_DISPLAY, DI_POINT, DIVINE_INSPIRATION, ap_item_name,
)
from ..locations import DIVINE_INSPIRATION_COUNT
from ..options import DivineInspirationChecks
from ..rules import PROGRESSIVE_CULT

# PROGRESSIVE_CULT comes from rules.py rather than being rebuilt here: the whole point of these
# tests is that the rule and the pool name the same item, which a local copy would paper over.
PROGRESSIVE_SHRINE_FLAME = ap_item_name("DivineInspiration", "Progressive Shrine Flame")
PROGRESSIVE_RESOURCE_PRODUCTION = ap_item_name("DivineInspiration", "Resource Production")
REFINING = ap_item_name("DivineInspiration", "Refining")


class DITestBase(WorldTestBase):
    game = "Cult of the Lamb"

    @property
    def location_names(self):
        return {location.name for location in self.multiworld.get_locations(1)}

    @property
    def item_names(self):
        return [item.name for item in self.multiworld.itempool]

    @property
    def di_locations(self):
        return sorted(n for n in self.location_names if n.startswith("Divine Inspiration "))

    def assert_locations_exist(self):
        self.assertEqual(len(self.di_locations), DIVINE_INSPIRATION_COUNT)
        for n in (1, DIVINE_INSPIRATION_COUNT):
            self.assertIn(f"Divine Inspiration {n}", self.location_names)

    def assert_items_are_not_gates(self, item_names):
        """No Divine Inspiration location may depend on a Divine Inspiration item.

        The checks come from filling the Devotion meter, which no item affects, so any such
        dependency is a leftover from the old spend-based trigger and a lie to the fill.

        Probed by stripping every one of these items out of an otherwise-complete state: if a
        location still can't be reached, something is gating on them.
        """
        state = self.multiworld.get_all_state()
        for name in item_names:
            for _ in range(sum(1 for i in self.multiworld.itempool if i.name == name)):
                state.remove(self.world.create_item(name))

        for n in (1, 2, 35, DIVINE_INSPIRATION_COUNT):
            location = self.multiworld.get_location(f"Divine Inspiration {n}", 1)
            self.assertTrue(
                location.can_reach(state),
                f"Divine Inspiration {n} needs a Divine Inspiration item - the checks fire on "
                "filling the Devotion meter, which no item affects",
            )

    def assert_items_are_useful_not_progression(self, item_names):
        """Only rule-referenced items should be progression; none of these are referenced."""
        for name in set(item_names):
            item = self.world.create_item(name)
            self.assertFalse(
                item.advancement,
                f"{name} is marked progression but no rule references it",
            )


class TestOff(DITestBase):
    options = {"divine_inspiration_mode": "off"}

    def test_no_block_at_all(self):
        self.assertFalse(self.di_locations)
        self.assertIsNone(self.world.divine_inspiration_gives_items)
        self.assertFalse([n for n in self.item_names if n == DI_POINT])
        for upgrade in DIVINE_INSPIRATION:
            self.assertNotIn(upgrade.item_name, self.item_names)


class TestChecksOnly(DITestBase):
    """The player keeps their own ability points, so this adds checks and no items - and
    therefore no logical length. The depth bands are all this block gets."""
    options = {"divine_inspiration_mode": "checks_only"}

    def test_locations_without_items(self):
        self.assert_locations_exist()
        self.assertIsNone(self.world.divine_inspiration_gives_items)
        self.assertNotIn(DI_POINT, self.item_names)
        for upgrade in DIVINE_INSPIRATION:
            self.assertNotIn(upgrade.item_name, self.item_names)


class TestChecksAndPoints(DITestBase):
    """One item name, one copy per location."""
    options = {"divine_inspiration_mode": "checks_and_points"}

    def test_point_items(self):
        self.assert_locations_exist()
        self.assertEqual(self.world.divine_inspiration_gives_items, [DI_POINT])
        self.assertEqual(
            sum(1 for n in self.item_names if n == DI_POINT), DIVINE_INSPIRATION_COUNT)

        # The techs themselves are the player's choice in this mode, so none are items.
        for upgrade in DIVINE_INSPIRATION:
            self.assertNotIn(upgrade.item_name, self.item_names)

    def test_points_do_not_gate_the_checks(self):
        self.assert_items_are_not_gates([DI_POINT])
        self.assert_items_are_useful_not_progression([DI_POINT])


class TestChecksAndTechs(DITestBase):
    """Many item names, one copy each - and the only mode where the multiworld knows which
    upgrades the player actually holds."""
    options = {"divine_inspiration_mode": "checks_and_techs"}

    def test_tech_items(self):
        self.assert_locations_exist()
        pool = self.item_names
        self.assertNotIn(DI_POINT, pool)

        for upgrade in DIVINE_INSPIRATION:
            self.assertEqual(sum(1 for n in pool if n == upgrade.item_name), 1,
                             f"{upgrade.item_name} should appear exactly once")

    def test_techs_do_not_gate_the_checks(self):
        names = [u.item_name for u in DIVINE_INSPIRATION]
        self.assert_items_are_not_gates(names)
        self.assert_items_are_useful_not_progression(names)


class TestShuffleIsLogicNeutral(DITestBase):
    """The shuffle rearranges tiers client-side. Because the tier gate is a count and not a
    prerequisite chain, it must not change what Archipelago places or requires."""
    options = {"divine_inspiration_mode": "checks_and_techs",
               "divine_inspiration_shuffle": "true_random"}

    def test_same_shape_as_unshuffled(self):
        self.assert_locations_exist()
        for upgrade in DIVINE_INSPIRATION:
            self.assertIn(upgrade.item_name, self.item_names)

    def test_rules_unchanged(self):
        self.assert_items_are_not_gates([u.item_name for u in DIVINE_INSPIRATION])


class TestAllUnlockedRegions(DITestBase):
    """With no region gating there are no Progressive Bishop's Domain items, so set_depth_rules
    never runs and this block carries no rules at all. Everything must still generate."""
    options = {"divine_inspiration_mode": "checks_and_techs",
               "region_access_order": "all_unlocked"}

    def test_generates_without_bands(self):
        self.assert_locations_exist()
        self.assert_items_are_not_gates([u.item_name for u in DIVINE_INSPIRATION])


class TestSlotData(DITestBase):
    options = {"divine_inspiration_mode": "checks_and_techs"}

    def test_agrees_with_locations(self):
        slot_data = self.world.fill_slot_data()

        self.assertEqual(slot_data["divineInspirationLocationCount"], DIVINE_INSPIRATION_COUNT)
        self.assertEqual(
            slot_data["divineInspirationLocationBaseId"],
            self.multiworld.get_location("Divine Inspiration 1", 1).address,
        )

        # Contiguity is the contract: the client turns a count of unlocks into base + N - 1.
        last = self.multiworld.get_location(
            f"Divine Inspiration {DIVINE_INSPIRATION_COUNT}", 1).address
        self.assertEqual(
            last, slot_data["divineInspirationLocationBaseId"] + DIVINE_INSPIRATION_COUNT - 1)

    def test_upgrade_mapping_is_complete(self):
        mapping = self.world.fill_slot_data()["divineInspirationUpgrades"]
        self.assertEqual(len(mapping), DIVINE_INSPIRATION_COUNT)
        for upgrade in DIVINE_INSPIRATION:
            self.assertEqual(mapping[upgrade.item_name], upgrade.internal)

    def test_thresholds(self):
        """Read off a live F4 dump, not guessed - a wrong threshold would silently mis-shape
        every shuffled tree the client builds."""
        self.assertEqual(
            self.world.fill_slot_data()["divineInspirationTierThresholds"], [0, 4, 10, 20, 25])

    def test_devotion_cap_reaches_slot_data(self):
        """70, lowered from 100 after a play session reached only 22 of the 69 points."""
        self.assertEqual(self.world.fill_slot_data()["divineInspirationDevotionCap"], 70)


class TestDevotionCapOff(DITestBase):
    """0 means "leave the game's economy alone" - the client skips the clamp entirely."""
    options = {"divine_inspiration_devotion_cap": 0}

    def test_zero_passes_through(self):
        self.assertEqual(self.world.fill_slot_data()["divineInspirationDevotionCap"], 0)


# ---------------------------------------------------------------------------
# curated_checks
# ---------------------------------------------------------------------------
#
# The regrouped block: the same 69 upgrades as 38 items, a shorter location list, five upgrades
# free from the start, and, uniquely in this world, a Divine Inspiration item that is
# progression and does gate its own block.


class TestCuratedTablesCoverEverything(unittest.TestCase):
    """No world needed: this is the table itself, and it's the test that catches a typo.

    A misspelled internal name wouldn't fail generation - it would produce an upgrade that
    silently never unlocks, discovered in game, hours in.
    """

    def test_every_upgrade_claimed_exactly_once(self):
        claimed = (
            list(DI_FREE_UPGRADES)
            + [name for g in DI_CURATED_PROGRESSIVE for name in g.upgrades]
            + [name for g in DI_CURATED_BUNDLES for name in g.upgrades]
            + [DI_INTERNAL_BY_DISPLAY[d] for d in DI_CURATED_SINGLES]
        )
        self.assertEqual(len(claimed), len(set(claimed)), "an upgrade is claimed twice")
        self.assertEqual(set(claimed), {u.internal for u in DIVINE_INSPIRATION})
        self.assertEqual(len(claimed), DIVINE_INSPIRATION_COUNT)

    def test_block_may_be_shorter_than_its_own_item_count(self):
        """The floor is deliberately well below the item count.

        An earlier version pinned range_start to the item count, on the assumption that a block
        can't have fewer locations than the items belonging to it. That's false: items go into
        the world's general pool, not onto their own block, and this world has ~77 location-only
        checks (buildings, followers, broom, snail shrines, bosses) that otherwise just absorb
        filler. A short block trades that filler for real unlocks.
        """
        self.assertLess(DivineInspirationChecks.range_start, len(DI_CURATED_ITEM_NAMES))

    def test_progressive_families_contribute_one_item_per_tier(self):
        for group in DI_CURATED_PROGRESSIVE:
            self.assertEqual(
                sum(1 for n in DI_CURATED_ITEM_NAMES if n == group.item_name),
                len(group.upgrades))

    def test_singles_reuse_existing_item_names(self):
        """They deliberately share DIVINE_INSPIRATION's ids rather than minting new ones."""
        for display in DI_CURATED_SINGLES:
            self.assertIn(display, DI_INTERNAL_BY_DISPLAY)

    def test_bundle_names_do_not_collide_with_single_upgrade_names(self):
        """A collision would silently overwrite the earlier item_table entry."""
        existing = set(DI_INTERNAL_BY_DISPLAY)
        for group in DI_CURATED_PROGRESSIVE + DI_CURATED_BUNDLES:
            self.assertNotIn(group.display, existing, f"{group.display} collides")


class TestCuratedChecks(DITestBase):
    options = {"divine_inspiration_mode": "curated_checks"}

    def test_block_is_shortened_to_the_option(self):
        self.assertEqual(len(self.di_locations), DivineInspirationChecks.default)
        self.assertIn("Divine Inspiration 1", self.location_names)
        self.assertIn(f"Divine Inspiration {DivineInspirationChecks.default}",
                      self.location_names)
        # The tail beyond the seed's count must not exist, or it would be unreachable.
        self.assertNotIn(f"Divine Inspiration {DIVINE_INSPIRATION_COUNT}", self.location_names)

    def test_pool_is_the_curated_items(self):
        pool = self.item_names
        self.assertNotIn(DI_POINT, pool)

        for name in set(DI_CURATED_ITEM_NAMES):
            expected = sum(1 for n in DI_CURATED_ITEM_NAMES if n == name)
            self.assertEqual(sum(1 for n in pool if n == name), expected,
                             f"{name} should appear {expected} time(s)")

    def test_free_upgrades_are_not_items(self):
        """They're granted on connect, so an item for one would be a wasted check."""
        free_displays = {
            u.item_name for u in DIVINE_INSPIRATION if u.internal in DI_FREE_UPGRADES}
        for display in free_displays:
            self.assertNotIn(display, self.item_names)

    def test_progressive_cult_is_progression(self):
        """rules.py names it. An item named by a rule that isn't progression is ignored by the
        fill's state sweep, which would quietly make those locations unreachable."""
        self.assertTrue(self.world.create_item(PROGRESSIVE_CULT).advancement)

    def test_shrine_flame_is_not_progression(self):
        """No rule names it, so it stays useful like the rest of the block."""
        self.assertFalse(self.world.create_item(PROGRESSIVE_SHRINE_FLAME).advancement)

    def test_deeper_checks_need_progressive_cult(self):
        """The rate-limiter on this whole block can't legally be the last thing you find."""
        state = self.multiworld.get_all_state()
        for _ in range(3):
            state.remove(self.world.create_item(PROGRESSIVE_CULT))

        first = self.multiworld.get_location("Divine Inspiration 1", 1)
        self.assertTrue(first.can_reach(state), "the opening checks must not be gated")

        last = self.multiworld.get_location(
            f"Divine Inspiration {DivineInspirationChecks.default}", 1)
        self.assertFalse(
            last.can_reach(state),
            "the deepest checks should require Progressive Cult - without that rule the item "
            "governing this block's Devotion rate can be placed last",
        )

    def test_no_excluded_tail(self):
        """The block brings ~10 filler; excluding the deepest quarter of 48 would overdraw it."""
        for n in range(1, DivineInspirationChecks.default + 1):
            location = self.multiworld.get_location(f"Divine Inspiration {n}", 1)
            self.assertNotEqual(location.progress_type, LocationProgressType.EXCLUDED)

    def test_slot_data_round_trips_the_tables(self):
        slot_data = self.world.fill_slot_data()

        self.assertEqual(slot_data["divineInspirationLocationCount"],
                         DivineInspirationChecks.default)
        self.assertEqual(sorted(slot_data["divineInspirationFreeUpgrades"]),
                         sorted(DI_FREE_UPGRADES))

        bundles = slot_data["divineInspirationBundles"]
        self.assertEqual(len(bundles), len(DI_CURATED_BUNDLES))
        for group in DI_CURATED_BUNDLES:
            self.assertEqual(bundles[group.item_name], list(group.upgrades))

        progressive = slot_data["divineInspirationProgressive"]
        self.assertEqual(len(progressive), len(DI_CURATED_PROGRESSIVE))
        for group in DI_CURATED_PROGRESSIVE:
            self.assertEqual(progressive[group.item_name], list(group.upgrades))

        singles = slot_data["divineInspirationUpgrades"]
        self.assertEqual(len(singles), len(DI_CURATED_SINGLES))
        for display in DI_CURATED_SINGLES:
            self.assertEqual(
                singles[ap_item_name("DivineInspiration", display)],
                DI_INTERNAL_BY_DISPLAY[display])

        # Every upgrade the client could be asked to grant, across all three maps plus the free
        # list, is still exactly the 69.
        reachable = (
            set(slot_data["divineInspirationFreeUpgrades"])
            | {n for names in bundles.values() for n in names}
            | {n for names in progressive.values() for n in names}
            | set(singles.values())
        )
        self.assertEqual(reachable, {u.internal for u in DIVINE_INSPIRATION})


class TestCuratedEarlyItems(DITestBase):
    """Resource Production and Refining are pinned to sphere 1.

    Without passive lumber and stone, and without the only source of planks and bricks, the base
    economy has no floor - so these two aren't allowed to arrive late.
    """
    options = {"divine_inspiration_mode": "curated_checks"}

    def test_declared_early(self):
        early = self.multiworld.local_early_items[1]
        self.assertEqual(early.get(PROGRESSIVE_RESOURCE_PRODUCTION), 1)
        self.assertEqual(early.get(REFINING), 1)


class TestCuratedShortestBlock(DITestBase):
    """5 checks, 38 items: the extreme case, where nearly every unlock is found elsewhere.

    The quartile rules still have to divide sensibly at this length - band_size floors to 1 - and
    the full item set still has to be pooled.
    """
    options = {"divine_inspiration_mode": "curated_checks",
               "divine_inspiration_checks": DivineInspirationChecks.range_start}

    def test_short_block_still_pools_every_item(self):
        self.assertEqual(len(self.di_locations), DivineInspirationChecks.range_start)

        pool = self.item_names
        for name in set(DI_CURATED_ITEM_NAMES):
            expected = sum(1 for n in DI_CURATED_ITEM_NAMES if n == name)
            self.assertEqual(sum(1 for n in pool if n == name), expected,
                             f"{name} should still appear {expected} time(s) in a 5-check block")

    def test_cult_gate_still_divides(self):
        state = self.multiworld.get_all_state()
        for _ in range(3):
            state.remove(self.world.create_item(PROGRESSIVE_CULT))

        first = self.multiworld.get_location("Divine Inspiration 1", 1)
        last = self.multiworld.get_location(
            f"Divine Inspiration {DivineInspirationChecks.range_start}", 1)
        self.assertTrue(first.can_reach(state))
        self.assertFalse(last.can_reach(state))


class TestCuratedLongestBlock(DITestBase):
    options = {"divine_inspiration_mode": "curated_checks",
               "divine_inspiration_checks": DivineInspirationChecks.range_end}

    def test_full_length_block_still_generates(self):
        self.assertEqual(len(self.di_locations), DIVINE_INSPIRATION_COUNT)


class TestCuratedRulesSurviveAllUnlocked(DITestBase):
    """The Cult gate is not a depth band and must not be skipped with the bands.

    Depth bands only apply when region access is randomized, because otherwise no Progressive
    Bishop's Domain items exist and every band past the first would be genuinely unreachable.
    Progressive Cult has no such dependency - it exists in every curated seed - so the rule holds
    here too. This was wrong in the first implementation, which nested the call inside the banded
    block and silently dropped the gate for all_unlocked seeds.
    """
    options = {"divine_inspiration_mode": "curated_checks",
               "region_access_order": "all_unlocked"}

    def test_gate_still_applies(self):
        state = self.multiworld.get_all_state()
        for _ in range(3):
            state.remove(self.world.create_item(PROGRESSIVE_CULT))

        last = self.multiworld.get_location(
            f"Divine Inspiration {DivineInspirationChecks.default}", 1)
        self.assertFalse(last.can_reach(state),
                         "the Cult gate was dropped along with the depth bands")


class TestOtherModesIgnoreTheCheckCount(DITestBase):
    """The pure-addition guard: the new option must not shorten an existing mode's block."""
    options = {"divine_inspiration_mode": "checks_and_techs",
               "divine_inspiration_checks": 38}

    def test_still_sixty_nine(self):
        self.assert_locations_exist()
        self.assertEqual(
            self.world.fill_slot_data()["divineInspirationLocationCount"],
            DIVINE_INSPIRATION_COUNT)
        # And the curated-only slot data stays empty rather than confusing the client.
        slot_data = self.world.fill_slot_data()
        self.assertEqual(slot_data["divineInspirationBundles"], {})
        self.assertEqual(slot_data["divineInspirationProgressive"], {})
        self.assertEqual(slot_data["divineInspirationFreeUpgrades"], [])
