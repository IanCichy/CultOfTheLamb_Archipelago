"""Item names carry their category, and every name the client is told about really exists.

Two guards, both defending against the same failure: a rename applied at some call sites and not
others. Item names are dict keys in slot data and the C# client matches received items against
them, so a mismatch doesn't raise anything - the item simply stops applying, in game, silently.

The prefixes exist because a player in another game who receives "Strength from Within" or
"Kitchen" has no way to tell what they've found. Locations have always read this way, so items
now match them.
"""

import unittest

from test.bases import WorldTestBase

from ..items import CATEGORY_PREFIXES, DI_POINT, PROGRESSIVE_REGION_ACCESS, item_table

# Slot-data maps whose *keys* are Archipelago item names. Everything else in slot data is keyed by
# the game's own internal names, which deliberately never carry a prefix.
ITEM_KEYED_SLOT_DATA = (
    "tarotCards",
    "weaponItems",
    "curseItems",
    "sermonUpgrades",
    "divineInspirationUpgrades",
    "divineInspirationBundles",
    "divineInspirationProgressive",
)


class TestItemNamePrefixes(unittest.TestCase):
    """A table test - no world needed."""

    def test_every_prefixable_item_carries_its_prefix(self):
        for name, data in item_table.items():
            prefix = CATEGORY_PREFIXES.get(data.category)
            if prefix is None:
                continue
            self.assertTrue(
                name.startswith(f"{prefix} - "),
                f"{name!r} is category {data.category} but doesn't start with {prefix!r} - "
                "the rename was applied to some construction sites and not this one",
            )

    def test_unprefixed_items_stay_unprefixed(self):
        """Filler, traps and the region item say what they are already."""
        for name in (DI_POINT, PROGRESSIVE_REGION_ACCESS, "Bundle of Lumber", "Dissent Trap"):
            self.assertIn(name, item_table)
            self.assertNotIn(" - ", name)

    def test_no_double_prefix(self):
        """A name built by applying the helper twice, e.g. "Tarot Card - Tarot Card - X"."""
        for name in item_table:
            for prefix in CATEGORY_PREFIXES.values():
                self.assertNotIn(
                    f"{prefix} - {prefix} - ", name, f"{name!r} has a doubled prefix")


class TestSlotDataItemNames(WorldTestBase):
    """Every item name the client is handed has to be a real item.

    This is the join that breaks quietly: the client looks up received item names in these maps,
    so a key the pool never produces means that item silently does nothing.
    """
    game = "Cult of the Lamb"
    options = {
        "divine_inspiration_mode": "curated_checks",
        "randomize_weapons": True,
        "randomize_curses": True,
        "randomize_sermon_upgrades": True,
        "randomize_tarot_cards": True,
        "include_woolhaven": True,
    }

    def test_keys_are_real_item_names(self):
        slot_data = self.world.fill_slot_data()

        for key in ITEM_KEYED_SLOT_DATA:
            self.assertIn(key, slot_data, f"{key} is missing from slot data")
            for item_name in slot_data[key]:
                self.assertIn(
                    item_name, item_table,
                    f"slot data {key!r} names {item_name!r}, which is not an item - the client "
                    "will never match it against anything it receives",
                )


class TestSlotDataItemNamesOtherModes(TestSlotDataItemNames):
    """The same join, for the mode that uses the per-upgrade item table rather than the bundles."""
    options = dict(TestSlotDataItemNames.options, divine_inspiration_mode="checks_and_techs")
