"""death_link is a Choice so it can grow modes, but group YAMLs write it as a Toggle.

`death_link: true` is what every core DeathLink option takes, and a Choice that only knows its
own names fails generation on it. The aliases are what keep those YAMLs working.
"""

import unittest

from ..options import DeathLink
from . import CultOfTheLambTestBase


class TestDeathLinkAcceptsToggleValues(unittest.TestCase):
    def test_booleans(self):
        self.assertEqual(DeathLink.from_any(True).value, DeathLink.option_classic)
        self.assertEqual(DeathLink.from_any(False).value, DeathLink.option_off)

    def test_names(self):
        self.assertEqual(DeathLink.from_any("true").value, DeathLink.option_classic)
        self.assertEqual(DeathLink.from_any("classic").value, DeathLink.option_classic)
        self.assertEqual(DeathLink.from_any("off").value, DeathLink.option_off)


class TestDeathLinkTrueGenerates(CultOfTheLambTestBase):
    options = {"death_link": True}

    def test_slot_data_says_classic(self):
        slot_data = self.multiworld.worlds[1].fill_slot_data()
        self.assertEqual(slot_data["deathLink"], DeathLink.option_classic)
