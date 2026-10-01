"""Item links with replacement ask the link's group world for filler.

A group world never runs generate_early. weighted_filler used to be set there, so a seed where
two Cult of the Lamb players shared a link with replacement failed to generate (GitHub issue #6).
"""
import unittest

from test.general import setup_multiworld

from .. import CultOfTheLambWorld

GAME = "Cult of the Lamb"


class TestGroupWorldCanMakeFiller(unittest.TestCase):
    """The narrow regression: a group world must be able to produce filler."""

    def test_create_group_world_makes_filler(self):
        multiworld = setup_multiworld(CultOfTheLambWorld, steps=())
        group = CultOfTheLambWorld.create_group(multiworld, multiworld.players + 1, {1})

        # This is what MultiWorld.link_items calls for a link with replacement.
        item = group.create_filler()

        self.assertIsNotNone(item)
        self.assertIn(item.name, CultOfTheLambWorld.weighted_filler)

    def test_weighted_filler_is_on_the_class(self):
        """Not an instance attribute, or a group world would never see it."""
        self.assertIn("weighted_filler", vars(CultOfTheLambWorld))
        self.assertTrue(CultOfTheLambWorld.weighted_filler)

    def test_every_weighted_name_is_a_real_item(self):
        for name in set(CultOfTheLambWorld.weighted_filler):
            with self.subTest(name):
                self.assertIn(name, CultOfTheLambWorld.item_name_to_id)


class TestLinkedSeedGenerates(unittest.TestCase):
    """Two Cult of the Lamb players sharing a link with replacement must still generate."""

    def test_two_players_linked_with_replacement(self):
        options = {
            "item_links": [{
                "name": "ItemLinkTest",
                "item_pool": ["Everything"],
                # None means "replace with filler", which is the path that asks the group world
                "replacement_item": None,
                "link_replacement": True,
            }],
        }
        multiworld = setup_multiworld([CultOfTheLambWorld, CultOfTheLambWorld], options=options)

        self.assertTrue(multiworld.itempool)
