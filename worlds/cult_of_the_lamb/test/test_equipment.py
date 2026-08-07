"""Generation tests for the weapon and curse family systems (Sprint 0d).

Two invariants, both invisible in a spoiler log:

- *every managed family has exactly one location, and every starting family has none*. The
  client filters what the game may offer, so a family with no location is one the player is
  handed and never paid for; a starting family with a location is a check that can never fire,
  because it's already equipped before the seed begins.
- *every family location is gated on its own item*. That's what stops the fill treating twelve
  checks as free sphere-1 real estate. It is also the one rule here that ``set_depth_rules``
  would silently destroy if these categories were ever added to it - ``set_rule`` overwrites.

The option axes are independent by design, so the combinations are enumerated rather than
spot-checked.
"""

from test.bases import WorldTestBase

from ..items import CURSES, WEAPONS


class EquipmentTestBase(WorldTestBase):
    game = "Cult of the Lamb"

    @property
    def location_names(self):
        return {location.name for location in self.multiworld.get_locations(1)}

    @property
    def item_names(self):
        return [item.name for item in self.multiworld.itempool]

    def assert_families(self, prefix, families, seed_families, starting, enabled):
        """One location and one item per managed family; none for a starting one."""
        names = self.location_names
        held = {e.display for e in starting}

        if not enabled:
            self.assertEqual(seed_families, [], f"{prefix} disabled but families were picked")
            for family in families:
                self.assertNotIn(f"{prefix} - {family.display}", names)
            return

        pool_items = self.item_names

        for family in seed_families:
            location = f"{prefix} - {family.display}"

            if family.display in held:
                self.assertNotIn(location, names,
                                 f"{location} exists but {family.display} is a starting family")
                self.assertNotIn(family.display, pool_items,
                                 f"{family.display} is in the pool but is a starting family")
                continue

            self.assertIn(location, names, f"{location} was never created")
            self.assertIn(family.display, pool_items, f"{family.display} was never pooled")

    def assert_gated_on_own_item(self, prefix, seed_families, starting):
        """Reaching a family's check requires holding that family's item.

        Checked by collecting everything *except* that item and confirming the location is
        still unreachable - which catches a missing rule, where an all-but-one state would
        wrongly reach it.
        """
        held = {e.display for e in starting}

        for family in seed_families:
            if family.display in held:
                continue

            state = self.multiworld.get_all_state()
            state.remove(self.world.create_item(family.display))

            location = self.multiworld.get_location(f"{prefix} - {family.display}", 1)
            self.assertFalse(
                location.can_reach(state),
                f"{location.name} is reachable without {family.display} - its rule is missing",
            )

    def assert_all(self):
        world = self.world
        self.assert_families("Weapon", WEAPONS, world.weapons, world.starting_weapons,
                             bool(world.options.randomize_weapons))
        self.assert_families("Curse", CURSES, world.curses, world.starting_curses,
                             bool(world.options.randomize_curses))

        # No assertion that the fill avoided placing a family's item on its own location:
        # WorldTestBase doesn't run the fill, and the rule above is what guarantees it anyway.
        if world.options.randomize_weapons:
            self.assert_gated_on_own_item("Weapon", world.weapons, world.starting_weapons)
        if world.options.randomize_curses:
            self.assert_gated_on_own_item("Curse", world.curses, world.starting_curses)


class TestBothOn(EquipmentTestBase):
    options = {"randomize_weapons": True, "randomize_curses": True}

    def test_families(self):
        self.assert_all()


class TestWeaponsOnly(EquipmentTestBase):
    options = {"randomize_weapons": True, "randomize_curses": False}

    def test_families(self):
        self.assert_all()


class TestCursesOnly(EquipmentTestBase):
    options = {"randomize_weapons": False, "randomize_curses": True}

    def test_families(self):
        self.assert_all()


class TestBothOff(EquipmentTestBase):
    options = {"randomize_weapons": False, "randomize_curses": False}

    def test_families(self):
        self.assert_all()


class TestWoolhaven(EquipmentTestBase):
    """The Flail is the only DLC family, and the only one whose presence is conditional."""
    options = {"include_woolhaven": True}

    def test_flail_is_in(self):
        self.assert_all()
        self.assertIn("Battler's Bludgeon", {w.display for w in self.world.weapons})


class TestNoWoolhaven(EquipmentTestBase):
    options = {"include_woolhaven": False}

    def test_flail_is_out(self):
        self.assert_all()
        self.assertNotIn("Battler's Bludgeon", {w.display for w in self.world.weapons})
        self.assertNotIn("Weapon - Battler's Bludgeon", self.location_names)


class TestMaximumStarting(EquipmentTestBase):
    """Clamped, not an error: 7 starting weapons without Woolhaven means all 6 of them.

    The interesting consequence is that the whole block collapses to nothing - no locations,
    no items - which is a shape create_items has to survive.
    """
    options = {
        "starting_weapons": 7,
        "starting_curses": 5,
        "include_woolhaven": False,
    }

    def test_everything_is_starting(self):
        self.assert_all()
        self.assertEqual(len(self.world.starting_weapons), len(self.world.weapons))
        self.assertEqual(len(self.world.starting_curses), len(self.world.curses))
        self.assertFalse([n for n in self.location_names if n.startswith("Weapon - ")])
        self.assertFalse([n for n in self.location_names if n.startswith("Curse - ")])


class TestMinimumStarting(EquipmentTestBase):
    """1 is the floor on purpose - with nothing granted, every weapon podium in the game
    would have nothing to put on it, and the game's own selection ends in ``list[0]``."""
    options = {"starting_weapons": 1, "starting_curses": 1}

    def test_one_each(self):
        self.assert_all()
        self.assertEqual(len(self.world.starting_weapons), 1)
        self.assertEqual(len(self.world.starting_curses), 1)


class TestAllUnlockedRegions(EquipmentTestBase):
    """The family rules live outside the ``regions_are_gated`` branch, so they must still
    apply in a seed with no region gating at all - unlike the depth bands."""
    options = {"region_access_order": "all_unlocked"}

    def test_still_gated(self):
        self.assert_all()


class TestLegendaryWithoutWoolhaven(EquipmentTestBase):
    """The one hard rule on Legendaries: they're DLC content, so the option is forced off
    without it however loudly the YAML asks. Otherwise the client would be told to offer
    weapons the player doesn't own."""
    options = {"legendary_weapons": "always", "include_woolhaven": False}

    def test_forced_off(self):
        self.assertEqual(self.world.legendary_weapon_chance, 0.0)
        self.assertEqual(self.world.fill_slot_data()["legendaryWeaponChance"], 0.0)


class TestLegendaryWithWoolhaven(EquipmentTestBase):
    options = {"legendary_weapons": "common", "include_woolhaven": True}

    def test_chance_reaches_slot_data(self):
        self.assertGreater(self.world.legendary_weapon_chance, 0.0)
        self.assertEqual(
            self.world.fill_slot_data()["legendaryWeaponChance"],
            self.world.legendary_weapon_chance,
        )

    def test_adds_no_items_or_locations(self):
        """Legendaries are a gameplay modifier, not a randomization axis - no check, no item,
        so turning this on must not change the seed's shape at all."""
        self.assertFalse([n for n in self.location_names if "Legendary" in n])
        self.assertFalse([n for n in self.item_names if "Legendary" in n])


class TestLegendaryWithoutWeaponRandomization(EquipmentTestBase):
    """The two options are independent: Legendaries work with randomize_weapons off, which is
    why the client registers the weapon service for either one."""
    options = {
        "legendary_weapons": "rare",
        "include_woolhaven": True,
        "randomize_weapons": False,
    }

    def test_generates(self):
        self.assert_all()
        self.assertGreater(self.world.legendary_weapon_chance, 0.0)
        self.assertEqual(self.world.weapons, [])


class TestSlotData(EquipmentTestBase):
    """Slot data has to agree with the locations that were actually created.

    The client sends a check for any family in ``weaponLocations``/``curseLocations``, so an
    id in there without a matching location is a check the multiworld never made.
    """
    options = {"randomize_weapons": True, "randomize_curses": True}

    def test_locations_match(self):
        slot_data = self.world.fill_slot_data()
        names = self.location_names

        by_internal = {w.internal: w for w in WEAPONS + CURSES}

        for key, prefix in (("weaponLocations", "Weapon"), ("curseLocations", "Curse")):
            for internal, location_id in slot_data[key].items():
                family = by_internal[internal]
                location = f"{prefix} - {family.display}"
                self.assertIn(location, names, f"{key} names {location}, which doesn't exist")
                self.assertEqual(
                    location_id, self.multiworld.get_location(location, 1).address,
                    f"{key} has the wrong id for {location}",
                )

    def test_starting_families_have_no_location(self):
        slot_data = self.world.fill_slot_data()

        for key, families in (("startingWeapons", self.world.starting_weapons),
                              ("startingCurses", self.world.starting_curses)):
            self.assertEqual(set(slot_data[key]), {f.internal for f in families})

        for key, starting in (("weaponLocations", self.world.starting_weapons),
                              ("curseLocations", self.world.starting_curses)):
            for family in starting:
                self.assertNotIn(family.internal, slot_data[key],
                                 f"{family.display} is a starting family but has a check id")
