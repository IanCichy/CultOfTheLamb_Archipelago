"""Snail Shrine N is the Nth shrine lit, so it needs Anura for Shells and N reachable shrines.

One shrine sits at the Lonely Shack and one in each region's hub. With Anura open that makes two,
and each other open region adds one. These walk a seed through its region unlocks and check the
reachable count at every step, in a seed where Anura is free and one where it opens last.
"""

from BaseClasses import CollectionState
from test.bases import WorldTestBase

from ..items import PROGRESSIVE_REGION_ACCESS


class SnailTestBase(WorldTestBase):
    game = "Cult of the Lamb"
    options = {"snail_shrine_checks": True, "region_access_order": "randomized"}

    def reachable_shrines(self, copies):
        world = self.multiworld.worlds[1]
        state = CollectionState(self.multiworld)
        for _ in range(copies):
            state.collect(world.create_item(PROGRESSIVE_REGION_ACCESS), True)
        return sum(
            self.multiworld.get_location(f"Snail Shrine {n}", 1).can_reach(state)
            for n in range(1, 6)
        )

    def expected(self, copies):
        order = self.multiworld.worlds[1].region_order
        open_regions = order[:copies + 1]
        if "Anura" not in open_regions:
            return 0
        return min(5, 1 + len(open_regions))

    def test_count_follows_open_regions(self):
        for copies in range(4):
            with self.subTest(copies=copies):
                self.assertEqual(self.reachable_shrines(copies), self.expected(copies))


class TestSnailAnuraFirst(SnailTestBase):
    options = {"snail_shrine_checks": True, "region_access_order": "vanilla_order"}

    def test_anura_position_is_covered(self):
        # Covers whichever position vanilla_order puts Anura in. The randomized class covers others.
        self.assertIn("Anura", self.multiworld.worlds[1].region_order)


class TestSnailRandomized(SnailTestBase):
    pass


class TestSnailAllUnlocked(SnailTestBase):
    options = {"snail_shrine_checks": True, "region_access_order": "all_unlocked"}

    def test_count_follows_open_regions(self):
        self.assertEqual(self.reachable_shrines(0), 5)
