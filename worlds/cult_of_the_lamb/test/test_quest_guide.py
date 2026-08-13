"""The Archipelago objective guide and the vanilla follower-quest trim.

Both are client-side presentation, which is exactly why they're asserted here. The guide is a
read-only view over check state the client already derives - it creates no locations and no
items - and nothing in a spoiler log would show a guidance option quietly growing a location
block. The two options are also independent axes that a later refactor could easily couple
("only trim when the guide is on"), which would be wrong: wanting a quieter game and wanting a
checklist are separate wishes.
"""

import unittest

from test.bases import WorldTestBase

from ..items import item_table
from ..locations import location_table


class GuideTestBase(WorldTestBase):
    game = "Cult of the Lamb"

    def guide(self):
        data = self.world.fill_slot_data()
        return (
            data["objectiveGuide"],
            data["objectiveGuidePinning"],
            data["vanillaFollowerQuests"],
        )

    @property
    def location_names(self):
        return {location.name for location in self.multiworld.get_locations(1)}


class TestDefaults(GuideTestBase):
    options = {}

    def test_defaults(self):
        # Guide on, win condition pinned only, vanilla quests trimmed to a thin trickle.
        self.assertEqual(self.guide(), (True, 1, 1))


class TestIndependentAxes(GuideTestBase):
    """The one that matters. The trim is not a sub-setting of the guide."""
    options = {
        "archipelago_objective_guide": False,
        "vanilla_follower_quests": "none",
    }

    def test_trim_survives_guide_off(self):
        self.assertEqual(self.guide(), (False, 1, 3))


class TestGuideWithVanillaQuestsIntact(GuideTestBase):
    """And the other direction: a checklist alongside the game's own quests is a legal seed."""
    options = {
        "archipelago_objective_guide": True,
        "vanilla_follower_quests": "unchanged",
    }

    def test_both_sent(self):
        guide, _, trim = self.guide()
        self.assertTrue(guide)
        self.assertEqual(trim, 0)


class TestPinningPassesThrough(GuideTestBase):
    options = {"objective_guide_pinning": "everything"}

    def test_value(self):
        self.assertEqual(self.guide()[1], 2)


class TestGuideCreatesNothing(unittest.TestCase):
    """Decision on record: the guide is guidance, not content. Quest-as-check was deliberately
    deferred to a later sprint, so if either table ever grows a guide entry, it shows up here
    rather than in someone's seed.

    A plain TestCase: both tables are module-level, so generating a multiworld to read them would
    be two full seeds' work for two dict scans."""

    def test_no_guide_locations(self):
        offenders = [
            name for name in location_table
            if "Objective" in name or "Guide" in name or name.startswith("Quest ")
        ]
        self.assertFalse(offenders)

    def test_no_guide_items(self):
        offenders = [name for name in item_table if "Objective" in name or "Guide" in name]
        self.assertFalse(offenders)


class TestGuideDoesNotChangeTheSeed(GuideTestBase):
    """The location set must be identical with the guide on and off.

    Regenerated inline rather than compared against a hardcoded count, which would rot the next
    time a check block is added. The same seed is reused on purpose: generate_early picks the
    region order and the starting tarot/weapon/curse sets randomly, and those genuinely do
    change which locations exist - so a fresh seed would fail for the wrong reason.
    """
    options = {"archipelago_objective_guide": True}

    def test_same_locations(self):
        with_guide = self.location_names

        self.options = {**self.options, "archipelago_objective_guide": False}
        self.world_setup(self.multiworld.seed)

        self.assertEqual(with_guide, self.location_names)
