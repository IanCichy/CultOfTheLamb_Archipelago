"""Universal Tracker rebuilds the same seed from slot data.

UT works out logic by re-running generation on the player's machine, and the server stores only
item placements. Four of generate_early's choices come from self.random - region order, starting
tarot, starting weapons and starting curses - so left to roll again UT gets different answers and
shows the wrong free region.

These tests run generation twice with different seeds, feed the first run's slot data to the
second as UT would, and assert the second matches the first.
"""
import unittest

from test.general import gen_steps, setup_multiworld
from worlds.AutoWorld import call_all

from .. import CultOfTheLambWorld

GAME = "Cult of the Lamb"

# Everything that makes generate_early roll something, turned on at once.
ROLLING_OPTIONS = {
    "region_access_order": "randomized_safe_start",
    "include_woolhaven": True,
    "randomize_tarot_cards": True,
    "starting_tarot_pool": "any",
    "starting_tarot_cards": 6,
    "randomize_weapons": True,
    "starting_weapons": 2,
    "randomize_curses": True,
    "starting_curses": 2,
    "divine_inspiration_mode": "curated_checks",
    "legendary_weapons": "rare",
}


def build(options, seed, passthrough=None):
    """Generate one solo world, optionally as Universal Tracker would."""
    multiworld = setup_multiworld(CultOfTheLambWorld, steps=(), seed=seed, options=options)
    if passthrough is not None:
        # Exactly what UT does once interpret_slot_data returns something truthy.
        multiworld.re_gen_passthrough = {GAME: passthrough}
    for step in gen_steps:
        call_all(multiworld, step)
    return multiworld


def world_of(multiworld):
    return multiworld.worlds[1]


class TestPassthroughRebuildsTheSeed(unittest.TestCase):
    """A second generation on a different seed reproduces the first, given its slot data."""

    def setUp(self):
        self.original = build(ROLLING_OPTIONS, seed=1)
        self.slot_data = world_of(self.original).fill_slot_data()
        # A different seed, so anything still rolling would disagree.
        self.tracker = build(ROLLING_OPTIONS, seed=999, passthrough=self.slot_data)

    def test_region_order(self):
        self.assertEqual(world_of(self.tracker).region_order,
                         world_of(self.original).region_order)

    def test_free_region_is_the_same(self):
        """The symptom players saw: UT named a region they could not enter."""
        self.assertEqual(world_of(self.tracker).region_order[0],
                         world_of(self.original).region_order[0])

    def test_starting_picks(self):
        for attribute in ("starting_tarot_cards", "starting_weapons", "starting_curses"):
            with self.subTest(attribute):
                self.assertEqual(
                    [e.internal for e in getattr(world_of(self.tracker), attribute)],
                    [e.internal for e in getattr(world_of(self.original), attribute)])

    def test_managed_pools(self):
        for attribute in ("tarot_cards", "weapons", "curses"):
            with self.subTest(attribute):
                self.assertEqual(
                    [e.internal for e in getattr(world_of(self.tracker), attribute)],
                    [e.internal for e in getattr(world_of(self.original), attribute)])

    def test_locations_match(self):
        """Starting picks remove locations, so a wrong roll changes the location list too."""
        self.assertEqual(
            {location.name for location in self.tracker.get_locations(1)},
            {location.name for location in self.original.get_locations(1)})

    def test_reachability_matches(self):
        """The free region is open with no items and the next one is not."""
        order = world_of(self.original).region_order
        state = self.tracker.get_all_state()
        empty = self.tracker.state
        self.assertTrue(self.tracker.get_region(order[0], 1).can_reach(empty))
        self.assertFalse(self.tracker.get_region(order[1], 1).can_reach(empty))
        self.assertTrue(self.tracker.get_region(order[1], 1).can_reach(state))

    def test_legendary_chance(self):
        self.assertEqual(world_of(self.tracker).legendary_weapon_chance,
                         world_of(self.original).legendary_weapon_chance)


class TestPassthroughAllUnlocked(unittest.TestCase):
    """all_unlocked has no access items, so regions_are_gated has to survive the round trip."""

    def test_no_gating_either_side(self):
        options = dict(ROLLING_OPTIONS, region_access_order="all_unlocked")
        original = build(options, seed=2)
        tracker = build(options, seed=888, passthrough=world_of(original).fill_slot_data())

        self.assertFalse(world_of(tracker).regions_are_gated)
        self.assertEqual(world_of(tracker).region_order, world_of(original).region_order)
        self.assertEqual({location.name for location in tracker.get_locations(1)},
                         {location.name for location in original.get_locations(1)})


class TestPassthroughVanillaOrder(unittest.TestCase):
    """vanilla_order never rolled an order, but it still has to come back gated."""

    def test_gated_and_identical(self):
        options = dict(ROLLING_OPTIONS, region_access_order="vanilla_order")
        original = build(options, seed=3)
        tracker = build(options, seed=777, passthrough=world_of(original).fill_slot_data())

        self.assertTrue(world_of(tracker).regions_are_gated)
        self.assertEqual(world_of(tracker).region_order, world_of(original).region_order)


class TestWithoutPassthroughStillRolls(unittest.TestCase):
    """Regression guard: the normal path must be untouched.

    If this ever passes trivially, the passthrough branch has swallowed real generation.
    """

    def test_two_seeds_differ(self):
        options = dict(ROLLING_OPTIONS, region_access_order="randomized")
        orders = {tuple(world_of(build(options, seed=seed)).region_order)
                  for seed in range(1, 12)}
        self.assertGreater(len(orders), 1,
                           "region order stopped varying between seeds")
