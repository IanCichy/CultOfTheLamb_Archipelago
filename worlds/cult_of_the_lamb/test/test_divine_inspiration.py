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

from test.bases import WorldTestBase

from ..items import DI_POINT, DIVINE_INSPIRATION
from ..locations import DIVINE_INSPIRATION_COUNT


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
            self.assertNotIn(upgrade.display, self.item_names)


class TestChecksOnly(DITestBase):
    """The player keeps their own ability points, so this adds checks and no items - and
    therefore no logical length. The depth bands are all this block gets."""
    options = {"divine_inspiration_mode": "checks_only"}

    def test_locations_without_items(self):
        self.assert_locations_exist()
        self.assertIsNone(self.world.divine_inspiration_gives_items)
        self.assertNotIn(DI_POINT, self.item_names)
        for upgrade in DIVINE_INSPIRATION:
            self.assertNotIn(upgrade.display, self.item_names)


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
            self.assertNotIn(upgrade.display, self.item_names)

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
            self.assertEqual(sum(1 for n in pool if n == upgrade.display), 1,
                             f"{upgrade.display} should appear exactly once")

    def test_techs_do_not_gate_the_checks(self):
        names = [u.display for u in DIVINE_INSPIRATION]
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
            self.assertIn(upgrade.display, self.item_names)

    def test_rules_unchanged(self):
        self.assert_items_are_not_gates([u.display for u in DIVINE_INSPIRATION])


class TestAllUnlockedRegions(DITestBase):
    """With no region gating there are no Progressive Bishop's Domain items, so set_depth_rules
    never runs and this block carries no rules at all. Everything must still generate."""
    options = {"divine_inspiration_mode": "checks_and_techs",
               "region_access_order": "all_unlocked"}

    def test_generates_without_bands(self):
        self.assert_locations_exist()
        self.assert_items_are_not_gates([u.display for u in DIVINE_INSPIRATION])


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
            self.assertEqual(mapping[upgrade.display], upgrade.internal)

    def test_thresholds(self):
        """Read off a live F4 dump, not guessed - a wrong threshold would silently mis-shape
        every shuffled tree the client builds."""
        self.assertEqual(
            self.world.fill_slot_data()["divineInspirationTierThresholds"], [0, 4, 10, 20, 25])

    def test_devotion_cap_reaches_slot_data(self):
        self.assertEqual(self.world.fill_slot_data()["divineInspirationDevotionCap"], 100)


class TestDevotionCapOff(DITestBase):
    """0 means "leave the game's economy alone" - the client skips the clamp entirely."""
    options = {"divine_inspiration_devotion_cap": 0}

    def test_zero_passes_through(self):
        self.assertEqual(self.world.fill_slot_data()["divineInspirationDevotionCap"], 0)
