"""Tests for the win conditions, and for the Narinder goal in particular.

The Narinder goal is shaped differently from the other two: it is one encounter rather than N
of four, so `required_count` must not touch it, and the Gateway only opens once every Bishop
is dead - which is the whole of its logic. A completion condition that quietly ignored either
of those would still generate, and would still look right in a spoiler header.
"""

from BaseClasses import CollectionState

from ..items import PROGRESSIVE_REGION_ACCESS
from ..rules import BISHOP_LOCATIONS, WITNESS_LOCATIONS
from . import CultOfTheLambTestBase


class GoalTestBase(CultOfTheLambTestBase):
    def empty_state(self):
        """A state holding nothing, so `completion_condition` is judged on its own terms."""
        state = CollectionState(self.multiworld)
        state.update_reachable_regions(1)
        return state

    @property
    def completion(self):
        return self.multiworld.completion_condition[1]


class TestNarinderGoal(GoalTestBase):
    options = {
        "goal": 2,  # narinder
        "region_access_order": 1,  # randomized, so access items exist to gate on
    }

    def test_not_complete_with_nothing(self):
        self.assertFalse(self.completion(self.empty_state()))

    def test_needs_every_region(self):
        """All four Bishops, so every Progressive Bishop's Domain copy. One short must fail."""
        state = self.empty_state()

        # One fewer than the three copies a gated seed carries.
        for _ in range(len(BISHOP_LOCATIONS) - 2):
            state.collect(self.get_item_by_name(PROGRESSIVE_REGION_ACCESS), prevent_sweep=True)
        state.update_reachable_regions(1)
        self.assertFalse(
            self.completion(state), "the Gateway must not open with a region still shut"
        )

    def test_complete_with_all_regions(self):
        state = self.empty_state()
        for _ in range(len(BISHOP_LOCATIONS) - 1):
            state.collect(self.get_item_by_name(PROGRESSIVE_REGION_ACCESS), prevent_sweep=True)
        state.update_reachable_regions(1)
        self.assertTrue(self.completion(state))

    def test_all_bishop_locations_exist(self):
        """The condition names these directly, and a rule naming a location that doesn't exist
        would raise rather than quietly pass - but only at completion time."""
        for name in BISHOP_LOCATIONS.values():
            self.assertIn(name, self.location_names)


class TestNarinderIgnoresRequiredCount(GoalTestBase):
    """`required_count` applies to the other two goals only. At 1 a Bishops seed would be won
    by one Bishop; Narinder must still want all four."""

    options = {
        "goal": 2,
        "required_count": 1,
        "region_access_order": 1,
    }

    def test_one_region_is_not_enough(self):
        state = self.empty_state()
        state.collect(self.get_item_by_name(PROGRESSIVE_REGION_ACCESS), prevent_sweep=True)
        state.update_reachable_regions(1)
        self.assertFalse(self.completion(state))

    def test_all_regions_still_required(self):
        state = self.empty_state()
        for _ in range(len(BISHOP_LOCATIONS) - 1):
            state.collect(self.get_item_by_name(PROGRESSIVE_REGION_ACCESS), prevent_sweep=True)
        state.update_reachable_regions(1)
        self.assertTrue(self.completion(state))


class TestNarinderUngated(GoalTestBase):
    """all_unlocked has no access items at all, so the condition has to hold without them -
    otherwise a narinder seed on that setting would be unwinnable."""

    options = {"goal": 2, "region_access_order": 3}

    def test_complete_immediately(self):
        self.assertTrue(self.completion(self.empty_state()))


class TestNarinderVanillaOrder(GoalTestBase):
    """vanilla_order is still gated - only the sequence is fixed - so this behaves like the
    randomized case rather than like all_unlocked."""

    options = {"goal": 2, "region_access_order": 0}

    def test_not_complete_with_nothing(self):
        self.assertFalse(self.completion(self.empty_state()))

    def test_complete_with_all_regions(self):
        state = self.empty_state()
        for _ in range(len(BISHOP_LOCATIONS) - 1):
            state.collect(self.get_item_by_name(PROGRESSIVE_REGION_ACCESS), prevent_sweep=True)
        state.update_reachable_regions(1)
        self.assertTrue(self.completion(state))


class TestNarinderSlotData(GoalTestBase):
    options = {"goal": 2}

    def test_goal_value_reaches_the_client(self):
        """The client switches on this int; GoalService.GoalNarinder is 2."""
        self.assertEqual(self.world.fill_slot_data()["goal"], 2)

    def test_no_narinder_location(self):
        """He is the win condition, not a check - a location that only fires at victory pays
        nothing, and the four Bishop locations are the logic anchor instead."""
        self.assertEqual(
            [name for name in self.location_names if "Narinder" in name], []
        )


class TestBishopsGoalUnchanged(GoalTestBase):
    options = {"goal": 0, "required_count": 2, "region_access_order": 1}

    def test_two_bishops_is_enough(self):
        state = self.empty_state()
        state.collect(self.get_item_by_name(PROGRESSIVE_REGION_ACCESS), prevent_sweep=True)
        state.update_reachable_regions(1)
        self.assertTrue(
            self.completion(state), "two regions open should satisfy a required_count of 2"
        )


class TestWitnessesGoalUnchanged(GoalTestBase):
    options = {"goal": 1, "required_count": 4, "region_access_order": 1}

    def test_needs_all_four(self):
        self.assertFalse(self.completion(self.empty_state()))

    def test_witness_locations_exist(self):
        for name in WITNESS_LOCATIONS.values():
            self.assertIn(name, self.location_names)
