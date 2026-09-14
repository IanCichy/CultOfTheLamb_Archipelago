"""Generation tests for the building and broom check blocks.

The invariant worth defending for buildings is that **no entry can be permanently unbuildable**.
Ten Divine Inspiration upgrades sit behind external systems - PleasureSystem, TailorSystem,
DiscipleSystem - and PleasureSystem in particular comes from a doctrine branch, so a player who
picks Work or Faith can never build Drinkhouse, Mating Tent, Nursery or Drum Circle. A check on
one of those is dead for the entire seed, and nothing in a spoiler log would show it.

That's asserted by name here rather than by inspection, because the failure is silent and the
temptation to add "just one more building" to the list is permanent.
"""

from test.bases import WorldTestBase

from ..items import BROOM_LEVEL_COUNT, BUILDINGS, DIVINE_INSPIRATION

# Divine Inspiration upgrades gated behind systems the player may never unlock, and the
# structures they gate. Read from the F4 prerequisite dump.
EXTERNALLY_GATED_STRUCTURES = {
    "PUB", "PUB_2", "MATING_TENT", "HATCHERY_2", "DRUM_CIRCLE", "DAYCARE",
    "SHRINE",              # Re-Indoctrination Stone, behind PleasureSystem
    "TAILOR",              # behind TailorSystem
    "SHRINE_DISCIPLE_BOOST", "SHRINE_DISCIPLE_COLLECTION",  # behind DiscipleSystem
}


class BuildingTestBase(WorldTestBase):
    game = "Cult of the Lamb"

    @property
    def location_names(self):
        return {location.name for location in self.multiworld.get_locations(1)}


class TestBuildingsOn(BuildingTestBase):
    options = {"building_checks": True}

    def test_every_building_has_a_location(self):
        names = self.location_names
        for building in BUILDINGS:
            self.assertIn(f"Build - {building.display}", names)

    def test_none_are_externally_gated(self):
        """The one that actually matters - see the module docstring."""
        for building in BUILDINGS:
            self.assertNotIn(
                building.internal, EXTERNALLY_GATED_STRUCTURES,
                f"{building.display} is gated behind a system the player may never unlock, so "
                "its check could be dead for the whole seed",
            )

    def test_tiers_are_real(self):
        """Each building's tier must match its unlocking upgrade, since locations.py sorts by it
        and the depth bands read that order. A wrong tier puts a late building in an early band."""
        di_tiers = {u.display: u.tier for u in DIVINE_INSPIRATION}

        # Display names line up between the two tables for most entries. Check the ones that do,
        # which is enough to catch a systematically wrong column.
        checked = 0
        for building in BUILDINGS:
            if building.display in di_tiers:
                self.assertEqual(
                    building.tier, di_tiers[building.display],
                    f"{building.display} is tier {building.tier} here but "
                    f"{di_tiers[building.display]} in the Divine Inspiration table",
                )
                checked += 1

        self.assertGreater(checked, 5, "name alignment broke; this test stopped checking anything")

    def test_no_upgrade_tiers_except_shelter(self):
        """Upper tiers make better progressive items than checks. Shelter is the one deliberate
        exception, asked for by name."""
        for building in BUILDINGS:
            if building.display == "Shelter":
                continue
            self.assertFalse(
                building.internal.endswith(("_2", "_3", "_II", "_III")),
                f"{building.display} is an upgrade tier",
            )

    def test_slot_data_matches_locations(self):
        slot_data = self.world.fill_slot_data()
        names = self.location_names

        by_internal = {b.internal: b for b in BUILDINGS}
        self.assertEqual(len(slot_data["buildingLocations"]), len(BUILDINGS))

        for internal, location_id in slot_data["buildingLocations"].items():
            location = f"Build - {by_internal[internal].display}"
            self.assertIn(location, names)
            self.assertEqual(
                location_id, self.multiworld.get_location(location, 1).address)


class TestBuildingsOff(BuildingTestBase):
    options = {"building_checks": False}

    def test_no_locations(self):
        self.assertFalse([n for n in self.location_names if n.startswith("Build - ")])


class TestBroomOn(BuildingTestBase):
    options = {"broom_checks": True}

    def test_locations_and_contiguity(self):
        names = self.location_names
        for n in range(1, BROOM_LEVEL_COUNT + 1):
            self.assertIn(f"Broom Level {n}", names)

        slot_data = self.world.fill_slot_data()
        self.assertEqual(slot_data["broomLocationCount"], BROOM_LEVEL_COUNT)
        self.assertEqual(
            slot_data["broomLocationBaseId"],
            self.multiworld.get_location("Broom Level 1", 1).address,
        )

        # Contiguity is the contract: the client turns ChoreXPLevel into base + N - 1.
        last = self.multiworld.get_location(f"Broom Level {BROOM_LEVEL_COUNT}", 1).address
        self.assertEqual(last, slot_data["broomLocationBaseId"] + BROOM_LEVEL_COUNT - 1)


class TestBroomOff(BuildingTestBase):
    options = {"broom_checks": False}

    def test_no_locations(self):
        self.assertFalse([n for n in self.location_names if n.startswith("Broom Level ")])


class TestBothOffWithEverythingElseOn(BuildingTestBase):
    """Turning these two off must not disturb the rest of the seed."""
    options = {"building_checks": False, "broom_checks": False}

    def test_generates(self):
        self.assertFalse([n for n in self.location_names if n.startswith("Build - ")])
        self.assertFalse([n for n in self.location_names if n.startswith("Broom Level ")])
        self.assertIn("Darkwood - Leshy", self.location_names)
