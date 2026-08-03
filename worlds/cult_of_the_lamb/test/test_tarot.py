"""Generation tests for the tarot card system.

The invariant worth defending is *every card this seed manages has exactly one location*.
Both ways of breaking it have already shipped once:

- a card with no location at all, which the client withholds while sending nothing - the
  shop-slot gold sink when ``tarot_shop_checks`` was off;
- a card whose location was created in one place and dropped in another, which sends a check
  the multiworld never made - starting cards left in ``tarotCardLocations``.

Neither is visible in a spoiler log, so they're asserted here instead.
"""

from test.bases import WorldTestBase

from ..items import POSTGAME_TAROT_CARDS, REGION_TAROT_CARDS, TAROT_CARDS
from ..locations import TAROT_SHOP_CARDS, TAROT_SHOP_HUBS


def shop_card_internals():
    return {internal for cards in TAROT_SHOP_CARDS.values() for _, internal in cards}


def shop_location_by_internal():
    """Card enum name -> the shop-slot location name it would have, if that block is on."""
    return {
        internal: f"{TAROT_SHOP_HUBS[region]} - {display}"
        for region, cards in TAROT_SHOP_CARDS.items()
        for display, internal in cards
    }


class TarotTestBase(WorldTestBase):
    game = "Cult of the Lamb"

    # `self.world` is set for us by WorldTestBase.world_setup.

    @property
    def location_names(self):
        return {location.name for location in self.multiworld.get_locations(1)}

    def assert_one_location_per_managed_card(self):
        """Every card in slot data resolves to exactly one location that really exists.

        A managed card is one the client will withhold. If it has no location the player can
        never be paid for earning it; if it has two, one action pays twice.

        Both sides are checked against the locations the multiworld actually created, not
        against slot data. ``tarotShopLocations`` is built unconditionally - the client gates
        on the ``tarotShopChecks`` flag instead - so trusting it here would have let the
        gold-sink bug through.
        """
        slot_data = self.world.fill_slot_data()
        names = self.location_names
        starting = {card.internal for card in self.world.starting_tarot_cards}
        shop_locations = shop_location_by_internal()

        for display, internal in slot_data["tarotCards"].items():
            if internal in starting:
                # Deliberately locationless: granted at connect, so there is nothing to earn.
                self.assertNotIn(
                    internal, slot_data["tarotCardLocations"], f"{display} starts owned"
                )
                continue

            has_card_location = f"Tarot Card - {display}" in names
            has_shop_location = shop_locations.get(internal) in names

            self.assertTrue(
                has_card_location or has_shop_location,
                f"{display} ({internal}) is managed but has no location - the client will "
                f"withhold it and send nothing",
            )
            self.assertFalse(
                has_card_location and has_shop_location,
                f"{display} ({internal}) has both a card and a shop location",
            )

    def assert_slot_data_ids_exist(self):
        """Nothing the client will act on points at a location the multiworld didn't create.

        ``tarotShopLocations`` is only included when shop checks are on, because it's built
        unconditionally and the client decides whether to use it from the separate
        ``tarotShopChecks`` flag (ArchipelagoClient.Connection.cs:248).
        """
        slot_data = self.world.fill_slot_data()
        real = {
            self.world.location_name_to_id[name]
            for name in self.location_names
            if name in self.world.location_name_to_id
        }

        keys = ["tarotCardLocations"]
        if slot_data["tarotShopChecks"]:
            keys.append("tarotShopLocations")

        for key in keys:
            self.assertEqual(
                set(slot_data[key].values()) - real,
                set(),
                f"{key} points at locations that don't exist in this seed",
            )


class TestTarotDefaults(TarotTestBase):
    options = {"randomize_tarot_cards": 1, "tarot_shop_checks": 1}

    def test_one_location_per_managed_card(self):
        self.assert_one_location_per_managed_card()

    def test_postgame_cards_are_left_to_the_game(self):
        """With a Bishops goal these sit past the win condition. A location the player can't
        reach fails generation outright, so they're dropped rather than gated."""
        managed = set(self.world.fill_slot_data()["tarotCards"].values())
        self.assertEqual(
            managed & POSTGAME_TAROT_CARDS,
            set(),
            "post-game cards must not be managed unless the goal reaches the post-game",
        )
        self.assertFalse(self.world.goal_reaches_postgame)

    def test_region_cards_live_in_their_region(self):
        """Real logic instead of an approximated depth band: you cannot meet the knucklebones
        opponents, or Helob, without that region open."""
        for internal, region in REGION_TAROT_CARDS.items():
            card = next(c for c in TAROT_CARDS if c.internal == internal)
            name = f"Tarot Card - {card.display}"
            if name not in self.location_names:
                continue  # drawn as a starting card this seed
            self.assertEqual(
                self.multiworld.get_location(name, 1).parent_region.name,
                region,
                f"{card.display} should be gated behind {region}",
            )

    def test_slot_data_ids_exist(self):
        self.assert_slot_data_ids_exist()

    def test_starting_cards_have_no_item(self):
        starting = {card.display for card in self.world.starting_tarot_cards}
        received = [
            item.name for item in self.multiworld.itempool if item.name in starting
        ]
        self.assertEqual(received, [], "starting cards must not also be in the item pool")


class TestTarotShopChecksOff(TarotTestBase):
    """The gold sink: shop cards kept no location when their slots stopped being checks."""

    options = {"randomize_tarot_cards": 1, "tarot_shop_checks": 0}

    def test_one_location_per_managed_card(self):
        self.assert_one_location_per_managed_card()

    def test_slot_data_ids_exist(self):
        self.assert_slot_data_ids_exist()

    def test_shop_cards_are_left_to_the_game(self):
        managed = set(self.world.fill_slot_data()["tarotCards"].values())
        self.assertEqual(
            managed & shop_card_internals(),
            set(),
            "with shop checks off, shop cards must be unmanaged - otherwise buying one "
            "costs gold, grants nothing and sends nothing, repeatably",
        )

    def test_no_shop_locations_exist(self):
        hubs = tuple(TAROT_SHOP_HUBS.values())
        self.assertEqual(
            [name for name in self.location_names if name.startswith(hubs)], []
        )


class TestTarotWithWoolhaven(TarotTestBase):
    options = {
        "randomize_tarot_cards": 1,
        "tarot_shop_checks": 1,
        "include_woolhaven": 1,
    }

    def test_one_location_per_managed_card(self):
        self.assert_one_location_per_managed_card()

    def test_slot_data_ids_exist(self):
        self.assert_slot_data_ids_exist()


class TestTarotStartingPoolAny(TarotTestBase):
    """`any` can draw a shop card as a starting card, which used to strand its shop slot."""

    options = {
        "randomize_tarot_cards": 1,
        "tarot_shop_checks": 1,
        "starting_tarot_pool": 1,
        "starting_tarot_cards": 12,
    }

    def test_one_location_per_managed_card(self):
        self.assert_one_location_per_managed_card()

    def test_slot_data_ids_exist(self):
        self.assert_slot_data_ids_exist()


class TestTarotStartingCountClamped(TarotTestBase):
    """Asking for more starting cards than the pool holds is clamped, not an error.

    The option itself allows up to 20, but ``vanilla_defaults`` only has the game's 15, so
    the top of the range has to fall back to "all of them".
    """

    options = {
        "randomize_tarot_cards": 1,
        "starting_tarot_cards": 20,
        "starting_tarot_pool": 0,  # vanilla_defaults
    }

    def test_clamped_to_the_candidate_pool(self):
        starting = self.world.starting_tarot_cards
        defaults = [card for card in self.world.tarot_cards if card.default]

        self.assertEqual(len(starting), len(defaults))
        self.assertEqual(len(starting), len({card.internal for card in starting}))

    def test_one_location_per_managed_card(self):
        self.assert_one_location_per_managed_card()


class TestTarotDisabled(TarotTestBase):
    options = {"randomize_tarot_cards": 0}

    def test_nothing_is_managed(self):
        slot_data = self.world.fill_slot_data()
        self.assertEqual(slot_data["tarotCards"], {})
        self.assertEqual(slot_data["tarotCardLocations"], {})
        self.assertEqual(
            [name for name in self.location_names if name.startswith("Tarot Card - ")], []
        )
