from dataclasses import dataclass

from .locations import DIVINE_INSPIRATION_COUNT
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


class ArchipelagoObjectiveGuide(Toggle):
    """Add an Archipelago checklist to the in-game quest log.

    Cult of the Lamb tells you nothing about the seed you're in. The quest log fills up with
    follower errands and never mentions the win condition, how many followers the multiworld
    wants from you, or which blocks of checks are even switched on - so a new player has no
    way to tell what to aim for, and the game's own quests actively pull against it.

    This adds a persistent Archipelago group to the pause menu's Quests tab: the win
    condition, region access, and one line per active check block, each showing live
    progress. Lines tick themselves off as you finish them.

    Pure guidance. It creates no locations and no items, and finishing a line is a display
    state rather than a check - turning this off changes nothing about the seed itself.

    One caveat worth knowing: progress is counted from what the server has recorded for your
    slot, so if you play two save files against one slot the checklist shows the slot's total
    rather than that save's.
    """
    display_name = "Archipelago Objective Guide"
    default = True


class ObjectiveGuidePinning(Choice):
    """How much of the Archipelago checklist is pinned to the on-screen tracker.

    The tracker only shows three quest groups at a time and drops the oldest past that, so
    pinning the whole checklist costs you sight of the quest you're actually doing. The full
    list is always in the pause menu's Quests tab no matter what this is set to.

    off: nothing on screen. Pause menu only.
    goal_only: the win condition and region access - one or two lines.
    everything: pin the checklist too. Spends two of your three tracker slots.

    Ignored when Archipelago Objective Guide is off.
    """
    display_name = "Objective Guide Pinning"
    option_off = 0
    option_goal_only = 1
    option_everything = 2
    default = 1


class VanillaFollowerQuests(Choice):
    """How much of the game's own follower-quest table stays in rotation.

    Followers periodically walk over and offer one of the game's ~87 built-in quests - cook
    three great meals, dress someone in a fancy suit, murder a specific follower at night.
    Most are busywork that pulls against whatever the multiworld wants from you, and a new
    player can't tell the difference between the two.

    They aren't pure noise, though. Turning a quest in is the game's main source of follower
    loyalty XP, so removing all of them slows follower levelling down. That's why the default
    trims rather than wipes.

    unchanged: vanilla. Every quest stays in rotation.

    thin_trickle: keep the ritual quests, the crusade collection quests, and every follower
      story chain (Sozo, the lovers, the rivalries); drop the rest. Followers still level up,
      and what they ask for is either something you were going to do anyway or an actual
      story beat.

    story_only: keep the story chains and nothing else. Followers still wander over
      sometimes, and get an "oh, never mind" line when there's nothing to give.

    none: no follower quests at all, story chains included. The quietest option, and the
      harshest on follower levelling.
    """
    display_name = "Vanilla Follower Quests"
    option_unchanged = 0
    option_thin_trickle = 1
    option_story_only = 2
    option_none = 3
    default = 1


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


class SermonXpCap(Range):
    """The most sermon XP one Temple upgrade is allowed to need, in tenths.

    Tenths because that's what the game's own bar counts in - it renders `12/30`, so a cap of 30
    means the bar never asks for more than that.

    Vanilla's curve is 0.3, 0.4, 1.1 ... climbing to 10.0 and then staying there, and there are
    38 sermon upgrades - so the last 25 all sit at the ceiling and the block totals roughly 298
    XP. One sermon gives about `followers / 10`, so at a 20-strong flock that's ~150 sermons.
    Since a sermon is a once-a-day ritual rather than a trickle, that's far worse than the
    Divine Inspiration grind.

    | Cap | Total XP | ~sermons at 20 followers |
    |-----|----------|--------------------------|
    | 0 (off) | 298 | ~150 |
    | 50 | 163 | ~80 |
    | 30 | 102 | ~51 |
    | 20 | 70 | ~35 |

    Only affects the Temple upgrade sermon - the five doctrine categories share the same method
    but a much steeper curve, and this world doesn't randomize them yet, so speeding them up
    would change content you didn't ask to change."""
    display_name = "Sermon XP Cap"
    range_start = 0
    range_end = 100
    default = 20


class BuildTimeCap(Range):
    """The longest any structure may take to build, in game-minutes. 0 leaves the game alone.

    Vanilla ranges from 10 minutes to **9000** for the late Temple tiers, with the common
    buildings at 30, 300 or 600. A Sleeping Bag is 30, so the default makes everything build as
    fast as one - two hits and done.

    Pure quality of life. It changes no logic and no checks; it only stops the cult-management
    half of the game being mostly waiting, which matters more here than in vanilla because
    building is now a check."""
    display_name = "Build Time Cap"
    range_start = 0
    range_end = 9000
    default = 30


class BuildingChecks(Toggle):
    """Send a check the first time you construct each of 25 curated buildings.

    Temple, Sleeping Bags, Missionary, Tabernacle, Offering Statue, Confession Booth, Kitchen and
    so on - the buildings that mark real progress in the cult, not the 200-odd decorations.
    Upgrade tiers are excluded except Shelter, since a second Healing Bay isn't a milestone.

    Only the first construction of each counts, and it's tracked per seed rather than per save,
    so demolishing and rebuilding won't pay twice.

    These interact with Divine Inspiration - every building is gated behind a DI upgrade - but
    only loosely: Archipelago can't know which upgrades you chose to buy, so these get depth
    bands ordered by the tier of the upgrade that unlocks them rather than real item logic."""
    display_name = "Building Checks"
    default = True


class BroomChecks(Toggle):
    """Send a check for each of the 10 broom levels.

    Sweeping raises your chore level, which makes cleaning faster. Ten levels, costing 3, 5, 10,
    20, 30, 50, 75, 100, 150 and 200 chore XP - a slow background trickle rather than something
    you grind, so these spread naturally across a run."""
    display_name = "Broom Checks"
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

    No check and no item - the Legendary is simply offered in place of a normal weapon, and the
    Blacksmith's Broken Hammer questline still unlocks them properly. Claiming one from a plinth
    goes through `LegendaryWeaponsJobBoardCompleted`, which this never touches.

    One side effect, verified in play: **picking a substituted Legendary up does add it to your
    weapon pool permanently**, because the game's own pickup code records whatever you collect
    (`Interaction_WeaponSelectionPodium.cs:866`). The client doesn't write that - it hands the
    game a weapon and the game writes it. Consequences are small: its Woolhaven plinth displays
    the weapon early (you still can't claim it), and collecting all seven this way would pop the
    ALL_LEGENDARY_WEAPONS achievement without the questlines. It also persists after you stop
    using the mod, which the project's save policy puts out of scope.

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


class DivineInspirationMode(Choice):
    """How Archipelago interacts with the Divine Inspiration tree - the buildings-and-rituals
    tree you open at the Shrine, not the Temple sermon tree.

    69 upgrades across 5 tiers, and 69 checks. **The check fires when you fill the Devotion
    meter** - the Nth ability point you earn is the Nth check - not when you spend it. Earning
    the point is the thing you did; spending it is a menu click. Same shape as the sermon bar.

    See Divine Inspiration Devotion Cap: without it, all 69 points cost ~24,000 Devotion and
    most of this block is out of reach in a normal seed.

    off: no interaction at all. The tree behaves exactly as vanilla.

    checks_only: filling the meter sends a check and you keep the point, exactly as in vanilla.
      69 checks, no items.

    checks_and_points: filling the meter sends a check and the point is taken. Points arrive
      from Archipelago instead, and you spend them on whatever you like. 69 points go out as
      checks and 69 come back as items, so the totals match vanilla - they just arrive in
      bursts, on the multiworld's schedule rather than one-for-one.

    checks_and_techs: the point is taken and never comes back; Archipelago grants the upgrades
      directly. The only mode where the multiworld knows exactly which upgrades you hold, which
      matters for anything later that wants to gate on a specific building. The cost is that you
      never choose anything - the tree unlocks itself.

    curated_checks: as checks_and_techs, but the 69 upgrades are regrouped into 38 items - a
      building and all of its tiers arrive together, so "Missionary Network" is one item rather
      than three. Five tier-1 upgrades (Temple, Farm Plot, Sleeping Bags, Body Pit, Farming
      Bundle) are free from the start, because without them the cult cannot function at all. The
      block is also shorter: see Divine Inspiration Checks.

      Two exceptions stay tiered, because both multiply **Devotion** - the resource that fills the
      meter every check in this block counts. Progressive Cult raises the Shrine from 50 to 175
      Devotion and 4 to 10 simultaneous prayers; Progressive Shrine Flame adds up to +60% pray
      speed. Bundling either would let a single item triple the throughput of the whole block.

    checks_and_points is the default because it keeps the part of the tree that's actually a
    decision: which upgrade you want next.

    Only curated_checks adds logical length: the deeper checks require Progressive Cult copies,
    which is what stops the thing that governs your Devotion rate from turning up last. Every
    other mode spreads across spheres via the depth bands, like the sermon block, because nothing
    they hand out makes the meter fill faster.

    Independent of Divine Inspiration Shuffle - any combination is a legal seed."""
    display_name = "Divine Inspiration Mode"
    option_off = 0
    option_checks_only = 1
    option_checks_and_points = 2
    option_checks_and_techs = 3
    option_curated_checks = 4
    default = 2


class DivineInspirationDevotionCap(Range):
    """The most Devotion a single ability point is allowed to cost.

    Vanilla's cost curve runs 1, 13, 29, 45 ... and climbs to 465, where it stays. Earning all
    69 points therefore costs roughly **24,000 Devotion** - a completionist number, not
    something you finish in one seed. Since every one of the 69 checks is a filled meter, that
    would leave most of the block unreachable in practice.

    Capping the cost keeps the early curve exactly as the game wrote it and only flattens the
    expensive tail:

    | Cap | Devotion for all 69 |
    |-----|---------------------|
    | 0 (off) | ~24,000 |
    | 200 | ~12,300 |
    | 150 | ~9,400 |
    | 100 | ~6,500 |
    | 70  | ~4,600 |
    | 65  | ~4,300 |

    70 is the default, set from a real play session: at a cap of 100 a full evening reached 22 of
    the 69 points, which would have made the block a three-session grind.

    Set 0 to leave the game's economy completely alone - fine if you want a very long seed, or
    if you're not using Divine Inspiration checks at all.

    Side benefit: the game triples the cost of every point once nothing is left to unlock
    (`AllUnlockedMultiplier`). The cap is applied after that multiplier, so it flattens that
    cliff too."""
    display_name = "Divine Inspiration Devotion Cap"
    range_start = 0
    range_end = 465
    default = 70


class DivineInspirationChecks(Range):
    """How many Divine Inspiration checks the block has, in curated_checks mode.

    **Read only when Divine Inspiration Mode is curated_checks.** Every other mode always uses all
    69, unchanged.

    69 is too many: a full evening of real play reached 22 of them at a Devotion cap of 100, and
    the cap is 70 now - roughly 30% cheaper per point - so 30 is about one session.

    The block may be shorter than its own item count. Those items simply go somewhere else in the
    seed - this world has ~77 locations that carry no items of their own (buildings, followers,
    broom, snail shrines, bosses) and normally just absorb filler. A short block trades that
    filler for real unlocks, so the tree pays out for playing the game rather than for grinding
    the Devotion meter.

    The only real limit is global: a world can't hold more items than it has locations. Turning
    most other check blocks off *and* setting this very low can overfill the pool and fail
    generation - the same arithmetic every AP world is subject to."""
    display_name = "Divine Inspiration Checks"
    range_start = 5
    range_end = DIVINE_INSPIRATION_COUNT
    default = 30


class DivineInspirationShuffle(Choice):
    """Rearranges which tier each Divine Inspiration upgrade sits in.

    Purely a layout change - it moves upgrades between rows of the tree without changing what
    Archipelago checks or grants, because the tier gate is a *count* ("have you bought 10
    things") rather than a prerequisite chain. That's why this is safe to combine with any
    Divine Inspiration Mode.

    default: the game's own layout.

    random_except_first: tier 1 is frozen - the Temple, the starting bed, the rest of row 1 stay
      put - and the other four tiers are reshuffled. The safer choice: an unchanged opening.

    true_random: every upgrade is reassigned a tier, so Crypt III can turn up in row 1 and
      Sleeping Bags in row 5. The wild one - fine, but a very different opening.

    Either way the five central nodes (Temple, Cult II, Refinery, Cult III, Cult IV) stay in
    their own tier. Each must be bought to open the next tier, so moving one would make that
    tier permanently unopenable."""
    display_name = "Divine Inspiration Shuffle"
    option_default = 0
    option_random_except_first = 1
    option_true_random = 2
    default = 0


@dataclass
class CultOfTheLambOptions(PerGameCommonOptions):
    goal: Goal
    required_count: RequiredCount
    archipelago_objective_guide: ArchipelagoObjectiveGuide
    objective_guide_pinning: ObjectiveGuidePinning
    vanilla_follower_quests: VanillaFollowerQuests
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
    divine_inspiration_mode: DivineInspirationMode
    divine_inspiration_checks: DivineInspirationChecks
    divine_inspiration_shuffle: DivineInspirationShuffle
    divine_inspiration_devotion_cap: DivineInspirationDevotionCap
    sermon_xp_cap: SermonXpCap
    build_time_cap: BuildTimeCap
    building_checks: BuildingChecks
    broom_checks: BroomChecks
    trap_percentage: TrapPercentage
