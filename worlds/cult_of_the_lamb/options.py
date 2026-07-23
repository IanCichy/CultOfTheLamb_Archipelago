from dataclasses import dataclass

from Options import Choice, PerGameCommonOptions, Toggle


class Goal(Choice):
    """
    All Bishops: Defeat Leshy, Heket, Kallamar, and Shamura.
    The One Who Waits: Defeat all four Bishops, then Narinder (The One Who Waits).
    """
    display_name = "Goal"
    option_all_bishops = 0
    option_the_one_who_waits = 1
    default = 1


class RandomizeRegionAccess(Toggle):
    """If enabled, Anura, Anchordeep, and Silk Cradle each require their matching Access
    item before they can be visited. Darkwood is always open - it's the game's own
    starting/tutorial region."""
    display_name = "Randomize Region Access"
    default = True


@dataclass
class CultOfTheLambOptions(PerGameCommonOptions):
    goal: Goal
    randomize_region_access: RandomizeRegionAccess
