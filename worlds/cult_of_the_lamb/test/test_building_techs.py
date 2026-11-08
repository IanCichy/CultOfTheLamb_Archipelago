"""Tests for building checks gated on the tech that unlocks each building.

The failure this defends against is the tracker saying "in logic" for a building the player
cannot place yet, because the rule asked for region progress rather than for the tech.
"""

from test.bases import WorldTestBase

from ..items import BUILDINGS, DI_FREE_UPGRADES, di_tech_item


class BuildingTechTestBase(WorldTestBase):
    game = "Cult of the Lamb"

    def tech_for(self, display):
        upgrade = next(b.upgrade for b in BUILDINGS if b.display == display)
        return di_tech_item(upgrade, self.curated)


class _TechGateMixin:
    """Shared by both modes that hand out techs as items."""

    def test_every_gated_building_has_a_tech_item(self):
        world = self.multiworld.worlds[1]
        gated = {b.display for b in BUILDINGS if b.upgrade not in DI_FREE_UPGRADES}
        self.assertEqual(
            set(world.building_tech_requirements),
            {f"Build - {name}" for name in gated})

    def test_tech_items_are_progression(self):
        world = self.multiworld.worlds[1]
        for item in self.multiworld.itempool:
            if item.name in world.building_tech_items:
                self.assertTrue(item.advancement, f"{item.name} is not progression")

    def test_kitchen_needs_its_tech_and_nothing_else(self):
        location = "Build - Kitchen"
        item, count = self.tech_for("Kitchen")
        self.assertFalse(self.can_reach_location(location))
        self.collect(self.get_items_by_name(item)[:count])
        self.assertTrue(self.can_reach_location(location))

    def test_tier_1_buildings_are_free(self):
        self.assertTrue(self.can_reach_location("Build - Farm Plot"))


class TestChecksAndTechs(_TechGateMixin, BuildingTechTestBase):
    curated = False
    options = {
        "building_checks": True,
        "divine_inspiration_mode": "checks_and_techs",
        "region_access_order": "all_unlocked",
    }


class TestCurated(_TechGateMixin, BuildingTechTestBase):
    curated = True
    options = {
        "building_checks": True,
        "divine_inspiration_mode": "curated_checks",
        "region_access_order": "all_unlocked",
    }

    def test_bundle_member_needs_the_bundle(self):
        """Janitor Station is in the Sanitation bundle, not an item of its own."""
        item, _ = self.tech_for("Janitor Station")
        self.assertEqual(item, self.tech_for("Outhouse")[0])


class TestPlayerPicksTechs(BuildingTechTestBase):
    """checks_only: no tech items exist, so the depth bands stay and nothing is reclassified."""
    curated = False
    options = {"building_checks": True, "divine_inspiration_mode": "checks_only"}

    def test_no_tech_rules(self):
        self.assertEqual(self.multiworld.worlds[1].building_tech_requirements, {})
