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


class RegionAccessOrder(Choice):
    """The order the four crusade regions unlock in, and whether they're gated at all.

    vanilla_order: the game's own order (Darkwood, Anura, Anchordeep, Silk Cradle). Still
      gated - each after the first needs another Progressive Bishop's Domain - so the region
      checks stay meaningful, they just arrive in the familiar sequence.

    randomized: any region can be the free starting one, and the other three unlock in a
      random per-seed order.

    randomized_safe_start: as randomized, but Silk Cradle is never the free starting region.
      Its door demands sacrificing a Follower to open, which is brutal as a seed's opening
      move before you have a flock to spare.

    all_unlocked: no gating at all - every region open from the start, and no Progressive
      Bishop's Domain items in the pool. Note this removes the only progression item the
      world currently has, so seeds become a single sphere."""
    display_name = "Region Access Order"
    option_vanilla_order = 0
    option_randomized = 1
    option_randomized_safe_start = 2
    option_all_unlocked = 3
    default = 2
    # Keeps YAMLs written against the old Toggle working.
    alias_true = 1
    alias_false = 3


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


class TarotShopChecks(Toggle):
    """Send a check for each Tarot Card bought from a hub shop.

    Every hub (Pilgrim's Passage, Spore Grotto, Smuggler's Sanctuary, Midas's Cave) sells a
    fixed set of named cards - 14 in total. Because the hubs are reached through their
    region's progression, these spread across spheres rather than all being available at
    once."""
    display_name = "Tarot Shop Checks"
    default = True


class SnailShrineChecks(Toggle):
    """Send a check for each of the 5 Snail Shrines you make a Shell offering at.

    Lighting all five is what unlocks the Snail Follower form in the base game."""
    display_name = "Snail Shrine Checks"
    default = True


class RandomizeTarotCards(Toggle):
    """Randomize the Tarot Card collection.

    Your collection is emptied on connect and every card becomes both a check and an item:
    unlocking one in-game - a crusade find, a shop, a challenge reward - sends a check, and the
    cards themselves arrive from Archipelago. 61 cards, or 80 with Include Woolhaven DLC.

    This adds no logical length: no card is ever required for the goal, so these are extra
    checks along the way rather than extra hours.

    Your cards are handed back if you disconnect."""
    display_name = "Randomize Tarot Cards"
    default = True


class StartingTarotCards(Range):
    """How many Tarot Cards to start with, replacing the 15 the game normally gives you.

    These are yours from the start, so they have neither a check nor an item - each one you add
    removes one of each. 0 means starting with an empty collection."""
    display_name = "Starting Tarot Cards"
    range_start = 0
    range_end = 20
    default = 8


class StartingTarotPool(Choice):
    """Which cards the starting ones are drawn from.

    vanilla_defaults: the 15 the game normally starts you with. They exist so an early deck
      isn't full of situational cards, which is why this is the default.

    any: any randomizable card, including Woolhaven ones if that option is on. More variance -
      it can hand you something excellent on day one, or three cards you can't use yet."""
    display_name = "Starting Tarot Pool"
    option_vanilla_defaults = 0
    option_any = 1
    default = 0


class RandomizeWeapons(Toggle):
    """Randomize which weapon families you can find.

    Vanilla hands out the Axe, Dagger, Hammer, Gauntlets, Blunderbuss and Flail on a fixed
    schedule - the first floor of each run gives you the next one you don't own. With this on,
    that schedule is Archipelago's instead: podiums, chests and choice rooms only ever offer
    families the multiworld has granted you, and equipping one for the first time sends a
    check. 6 weapons, or 7 with Include Woolhaven DLC (the Flail).

    Your existing weapons are never taken away - the game's save data isn't touched at all, so
    this is safe to enable on a save you've already played. It only changes what gets offered."""
    display_name = "Randomize Weapons"
    default = True


class StartingWeapons(Range):
    """How many weapon families you begin the seed with.

    These have neither a check nor an item, so each one you add removes one of each. Can't be
    0: with nothing to offer, every weapon podium in the game would have nothing to put on it."""
    display_name = "Starting Weapons"
    range_start = 1
    range_end = 7
    default = 1


class LegendaryWeapons(Choice):
    """Let Legendary weapons turn up on ordinary weapon podiums.

    Requires the Woolhaven DLC - Legendaries are its content, normally earned through the
    Blacksmith's Broken Hammer questline and its job boards. **This option is forced off
    without Include Woolhaven DLC**, since the client can't hand you content you don't own.

    A Legendary can only appear for a family you already hold, so this makes the weapons you
    have better rather than handing you one you can't otherwise use.

    Nothing is written to your save and no check or item is involved - the Legendary is simply
    offered in place of a normal weapon. Your Blacksmith questline is untouched and still
    unlocks them properly, which is deliberate: writing them into the weapon pool is what
    would break the Legendary plinths and job-board objectives.

    off: vanilla. Legendaries only from the Blacksmith.
    rare: roughly 1 weapon offer in 10.
    common: roughly 1 in 4.
    always: every weapon offered is the Legendary of its family. A power fantasy, not a
      balanced seed."""
    display_name = "Legendary Weapons"
    option_off = 0
    option_rare = 1
    option_common = 2
    option_always = 3
    default = 0


class RandomizeCurses(Toggle):
    """Randomize which curse families you can find.

    The curse-side counterpart to Randomize Weapons, covering Flaming Shot, Touch of Turua,
    Divine Blast, Ichor Thrown and Death's Sweep. Independent of it - either, both or neither.

    The Teleport and Barrier curses aren't included here: they're unlocked by sermon upgrades,
    which Randomize Sermon Upgrades already covers."""
    display_name = "Randomize Curses"
    default = True


class StartingCurses(Range):
    """How many curse families you begin the seed with.

    As Starting Weapons - no check and no item for these, and 1 is the floor because the game
    always needs something to put on a curse podium."""
    display_name = "Starting Curses"
    range_start = 1
    range_end = 5
    default = 1


@dataclass
class CultOfTheLambOptions(PerGameCommonOptions):
    goal: Goal
    required_count: RequiredCount
    region_access_order: RegionAccessOrder
    include_woolhaven: IncludeWoolhaven
    randomize_sermon_upgrades: RandomizeSermonUpgrades
    follower_milestone_checks: FollowerMilestoneChecks
    tarot_shop_checks: TarotShopChecks
    snail_shrine_checks: SnailShrineChecks
    randomize_tarot_cards: RandomizeTarotCards
    starting_tarot_cards: StartingTarotCards
    starting_tarot_pool: StartingTarotPool
    randomize_weapons: RandomizeWeapons
    starting_weapons: StartingWeapons
    legendary_weapons: LegendaryWeapons
    randomize_curses: RandomizeCurses
    starting_curses: StartingCurses
    trap_percentage: TrapPercentage
