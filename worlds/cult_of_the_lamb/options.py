from dataclasses import dataclass

from Options import Choice, PerGameCommonOptions, Range, Toggle


class Goal(Choice):
    """
    Bishops: Defeat Required Count of the four Bishops (Leshy, Heket, Kallamar, Shamura).
    Witnesses: Defeat Required Count of the four Witnesses (Agares, Bathin, Astaroth,
    Allocer) - each Witness only becomes fightable after its region's Bishop is defeated.
    """
    display_name = "Goal"
    option_bishops = 0
    option_witnesses = 1
    default = 0


class RequiredCount(Range):
    """How many of the Goal's four encounters must be defeated to win."""
    display_name = "Required Count"
    range_start = 1
    range_end = 4
    default = 4


class RandomizeRegionAccess(Toggle):
    """If enabled, one of Darkwood/Anura/Anchordeep/Silk Cradle is randomly free from the
    start each seed, and the other three require the Progressive Bishop's Domain item (one
    more copy each, in a random per-seed order) before they can be visited."""
    display_name = "Randomize Region Access"
    default = True


@dataclass
class CultOfTheLambOptions(PerGameCommonOptions):
    goal: Goal
    required_count: RequiredCount
    randomize_region_access: RandomizeRegionAccess
