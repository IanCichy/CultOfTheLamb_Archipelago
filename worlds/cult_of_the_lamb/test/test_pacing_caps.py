"""The three pacing caps: Devotion per ability point, sermon XP per Temple upgrade, and build
time per structure.

They exist because the game's curves are built for completion across dozens of hours while a
seed wants those blocks finishable in one, and all three are the same shape - a client-side
postfix clamping one public static.

The property worth defending is that they are **quality of life, not randomizer settings**: a
seed with sermon randomization off must still be able to cap sermon XP. That's easy to break by
folding a cap into the service that happens to care about it, so it's asserted here.
"""

from test.bases import WorldTestBase


class CapTestBase(WorldTestBase):
    game = "Cult of the Lamb"

    def caps(self):
        data = self.world.fill_slot_data()
        return (
            data["divineInspirationDevotionCap"],
            data["sermonXpCap"],
            data["buildTimeCap"],
        )


class TestDefaults(CapTestBase):
    options = {}

    def test_defaults(self):
        devotion, sermon, build = self.caps()
        self.assertEqual(devotion, 70)
        self.assertEqual(sermon, 20)   # tenths, so 2.0 XP, the unit the game's bar counts in
        self.assertEqual(build, 30)    # game-minutes, the same as a Sleeping Bag


class TestAllOff(CapTestBase):
    """0 means "leave the game's own economy alone" for every one of them."""
    options = {
        "divine_inspiration_devotion_cap": 0,
        "sermon_xp_cap": 0,
        "build_time_cap": 0,
    }

    def test_zero_passes_through(self):
        self.assertEqual(self.caps(), (0, 0, 0))


class TestCapsApplyWithBlocksDisabled(CapTestBase):
    """The one that matters. Turning a block's randomization off must not disable its cap -
    they're separate concerns, and the client registers EconomyService unconditionally."""
    options = {
        "randomize_sermon_upgrades": False,
        "divine_inspiration_mode": "off",
        "building_checks": False,
        "sermon_xp_cap": 25,
        "divine_inspiration_devotion_cap": 55,
        "build_time_cap": 45,
    }

    def test_caps_still_sent(self):
        self.assertEqual(self.caps(), (55, 25, 45))

    def test_the_blocks_really_are_off(self):
        names = {location.name for location in self.multiworld.get_locations(1)}
        self.assertFalse([n for n in names if n.startswith("Sermon Upgrade ")])
        self.assertFalse([n for n in names if n.startswith("Divine Inspiration ")])
        self.assertFalse([n for n in names if n.startswith("Build - ")])


class TestExtremes(CapTestBase):
    """Both ends of each range generate - a cap tighter than the first entry of a curve is legal,
    just very fast."""
    options = {
        "divine_inspiration_devotion_cap": 465,
        "sermon_xp_cap": 100,
        "build_time_cap": 9000,
    }

    def test_maximums(self):
        self.assertEqual(self.caps(), (465, 100, 9000))
