"""Tests for depth bands and the excluded tail.

The failure these defend against isn't an invalid seed - it's a legal, miserable one. With
`Progressive Bishop's Domain` as the world's only progression item, the fill is free to put
the copy that opens your fourth region behind "Sermon Upgrade 32", and the whole seed becomes
one grind with nothing else to do.
"""

from BaseClasses import ItemClassification, LocationProgressType
from test.bases import WorldTestBase
from worlds.AutoWorld import call_all

from ..items import PROGRESSIVE_REGION_ACCESS
from ..locations import location_table
from ..regions import REGION_NAMES

# Blocks that live in "Cult" and so need bands imposed on them; everything else is gated by
# the region graph and gets its depth from real logic.
BANDED_CATEGORIES = ("Sermon", "Follower", "Snail", "TarotCard")


class RulesTestBase(WorldTestBase):
    game = "Cult of the Lamb"

    def fill(self):
        """Actually place items. `world_setup` stops before the fill, so without this every
        `location.item` is None and a placement assertion passes vacuously."""
        from Fill import distribute_items_restrictive

        distribute_items_restrictive(self.multiworld)
        call_all(self.multiworld, "post_fill")

    @property
    def banded_locations(self):
        return [
            location
            for location in self.multiworld.get_locations(1)
            if location.name in location_table
            and location_table[location.name].category in BANDED_CATEGORIES
        ]


class TestDepthBands(RulesTestBase):
    options = {
        "randomize_tarot_cards": 1,
        "randomize_sermon_upgrades": 1,
        "follower_milestone_checks": 1,
        "snail_shrine_checks": 1,
        "region_access_order": 1,  # randomized, so progression items exist
    }

    def test_progression_never_lands_in_an_excluded_location(self):
        """The whole point. Excluded locations take filler and traps only."""
        self.fill()

        for location in self.multiworld.get_locations(1):
            if location.progress_type != LocationProgressType.EXCLUDED:
                continue
            if location.item is None:
                continue
            self.assertNotEqual(
                location.item.classification & ItemClassification.progression,
                ItemClassification.progression,
                f"{location.item.name} is progression and landed on excluded "
                f"{location.name}",
            )

    def test_every_door_key_is_placed_outside_the_deep_tail(self):
        self.fill()

        keys = [
            location
            for location in self.multiworld.get_locations(1)
            if location.item is not None
            and location.item.name == PROGRESSIVE_REGION_ACCESS
        ]
        self.assertEqual(len(keys), len(REGION_NAMES) - 1)

        for location in keys:
            self.assertNotEqual(
                location.progress_type,
                LocationProgressType.EXCLUDED,
                f"a region key landed on excluded {location.name}",
            )

    def test_tarot_cards_are_banded(self):
        """45 unrestricted sphere-1 locations was the single biggest contributor."""
        tarot = [
            location
            for location in self.banded_locations
            if location_table[location.name].category == "TarotCard"
        ]
        self.assertGreater(len(tarot), 0)

        excluded = [
            location
            for location in tarot
            if location.progress_type == LocationProgressType.EXCLUDED
        ]
        self.assertGreater(
            len(excluded), 0, "tarot cards were never added to set_depth_rules"
        )

    def test_exclusion_stays_a_minority(self):
        """Excluded locations can't hold useful items either, and this game's own items are
        almost all useful - so the deep tail must stay a slice, not a half."""
        everything = self.multiworld.get_locations(1)
        excluded = [
            location
            for location in everything
            if location.progress_type == LocationProgressType.EXCLUDED
        ]

        self.assertGreater(len(excluded), 0)
        self.assertLess(
            len(excluded) / len(everything),
            0.30,
            "too much of the seed is filler-only",
        )

    def test_every_banded_block_still_has_reachable_shallow_locations(self):
        """A band split that put everything in the deep end would technically pass the
        exclusion tests while making the block pointless."""
        for category in BANDED_CATEGORIES:
            block = [
                location
                for location in self.banded_locations
                if location_table[location.name].category == category
            ]
            if not block:
                continue

            shallow = [
                location
                for location in block
                if location.progress_type != LocationProgressType.EXCLUDED
            ]
            self.assertGreater(
                len(shallow), 0, f"every {category} location ended up excluded"
            )


class TestNoGatingMeansNoBands(RulesTestBase):
    """`all_unlocked` creates no progression items at all, so bands would make the deeper
    ones genuinely unreachable. set_depth_rules must stay skipped entirely."""

    options = {
        "randomize_tarot_cards": 1,
        "randomize_sermon_upgrades": 1,
        "region_access_order": 3,  # all_unlocked
    }

    def test_no_progression_items_exist(self):
        self.assertEqual(
            [
                item
                for item in self.multiworld.itempool
                if item.name == PROGRESSIVE_REGION_ACCESS
            ],
            [],
        )

    def test_nothing_is_excluded(self):
        excluded = [
            location.name
            for location in self.multiworld.get_locations(1)
            if location.progress_type == LocationProgressType.EXCLUDED
        ]
        self.assertEqual(excluded, [])
