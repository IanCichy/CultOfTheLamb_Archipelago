"""Achievement toggle.

Default off. A seed that hands you content you did not earn should not also hand you the
achievements for it. Asserted on the wire value rather than the option object, since the client
matches this key by string.
"""

from test.bases import WorldTestBase


class AchievementTestBase(WorldTestBase):
    game = "Cult of the Lamb"


class TestAllowAchievements(AchievementTestBase):
    options = {}

    def test_held_back_by_default(self):
        self.assertEqual(self.multiworld.worlds[1].fill_slot_data()["allowAchievements"], 0)

    def test_creates_no_locations_or_items(self):
        # Purely a client behaviour, so it must not disturb the pool either way.
        self.assertEqual(
            [item.name for item in self.multiworld.itempool if "chievement" in item.name], [])
        self.assertEqual(
            [loc.name for loc in self.multiworld.get_locations(1) if "chievement" in loc.name],
            [])


class TestAllowAchievementsOn(AchievementTestBase):
    options = {"allow_achievements": True}

    def test_allowed_when_asked_for(self):
        self.assertEqual(self.multiworld.worlds[1].fill_slot_data()["allowAchievements"], 1)
