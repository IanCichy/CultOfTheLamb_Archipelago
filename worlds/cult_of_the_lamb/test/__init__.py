from test.bases import WorldTestBase


class CultOfTheLambTestBase(WorldTestBase):
    """Shared base for this world's tests.

    Carries the two accessors every test file otherwise re-declares. The four pre-existing files
    still have their own copies; new tests should use this.
    """
    game = "Cult of the Lamb"

    @property
    def location_names(self):
        return {location.name for location in self.multiworld.get_locations(1)}

    @property
    def item_names(self):
        return [item.name for item in self.multiworld.itempool]
