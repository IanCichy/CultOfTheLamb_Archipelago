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


class IncludeWoolhaven(Toggle):
    """Include content from the paid Woolhaven DLC (the game's only major gameplay DLC).

    Enable this ONLY if you own Woolhaven - the client cannot grant DLC content you don't
    own, so a seed generated with this on and played without the DLC will be unbeatable.
    Affects the sermon upgrades (6 extra), and later the tarot/fleece/doctrine pools."""
    display_name = "Include Woolhaven DLC"
    default = False


class RandomizeSermonUpgrades(Toggle):
    """Randomize the Temple sermon upgrades (Hearts of the Faithful, Might of the Devout,
    the weapon affixes and curse packs, the Heavy Attack masteries...).

    When enabled, filling the sermon bar sends a check instead of opening the upgrade-choice
    screen, and the upgrades themselves arrive as Archipelago items. 32 upgrades, or 38 with
    Include Woolhaven DLC."""
    display_name = "Randomize Sermon Upgrades"
    default = True


class FollowerMilestoneChecks(Toggle):
    """Send a check for each of your first 20 recruited Followers.

    Counted as Followers ever recruited, not current flock size, so losing Followers can't
    make a milestone you already passed unreachable."""
    display_name = "Follower Milestone Checks"
    default = True


class TrapPercentage(Range):
    """What percentage of the filler items in your seed are traps instead.

    0 disables traps entirely. Traps replace filler only - they never take the place of a
    real item, so raising this can't make a seed harder to complete, only more annoying."""
    display_name = "Trap Percentage"
    range_start = 0
    range_end = 50
    default = 5


@dataclass
class CultOfTheLambOptions(PerGameCommonOptions):
    goal: Goal
    required_count: RequiredCount
    randomize_region_access: RandomizeRegionAccess
    include_woolhaven: IncludeWoolhaven
    randomize_sermon_upgrades: RandomizeSermonUpgrades
    follower_milestone_checks: FollowerMilestoneChecks
    trap_percentage: TrapPercentage
