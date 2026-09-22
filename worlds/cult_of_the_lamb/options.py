from dataclasses import dataclass

from .locations import DIVINE_INSPIRATION_COUNT
from Options import Choice, PerGameCommonOptions, Range, Toggle


class Goal(Choice):
    """
    Bishops: Defeat Required Count of the four Bishops (Leshy, Heket, Kallamar, Shamura).
    Witnesses: Defeat Required Count of the four Witnesses (Agares, Bathin, Astaroth,
    Allocer). Each Witness only becomes fightable after its region's Bishop is defeated.
    Narinder: Beat the game. The Gateway only opens once all four Bishops are dead, so this is
    the longest goal and always needs every region. Required Count does not apply to it.
    """
    display_name = "Goal"
    option_bishops = 0
    option_witnesses = 1
    option_narinder = 2
    default = 0


class RequiredCount(Range):
    """How many of the Goal's four encounters must be defeated to win.

    Ignored when the Goal is Narinder: there is only one of him.
    """
    display_name = "Required Count"
    range_start = 1
    range_end = 4
    default = 4


class ArchipelagoObjectiveGuide(Toggle):
    """Add an Archipelago checklist to the in-game quest log.

    The game never tells you what the seed wants from you. This adds an Archipelago group to
    the pause menu's Quests tab: your goal, your region access, and one line per active check
    type, each ticking itself off as you finish it.

    Display only. It adds no checks and no items, so turning it off changes nothing about the
    seed. Progress comes from what the server recorded for your slot, so two save files on one
    slot share one count.
    """
    display_name = "Archipelago Objective Guide"
    default = True


class ObjectiveGuidePinning(Choice):
    """How much of the Archipelago checklist is pinned to the on-screen tracker.

    The tracker only holds three quest groups at a time, so pinning everything can push the
    quest you're actually doing off screen. The full list is always in the pause menu.

    off: nothing on screen.
    goal_only: your goal and region access, one or two lines.
    everything: the whole checklist, which uses two of your three tracker slots.

    Ignored when Archipelago Objective Guide is off.
    """
    display_name = "Objective Guide Pinning"
    option_off = 0
    option_goal_only = 1
    option_everything = 2
    default = 1


class VanillaFollowerQuests(Choice):
    """How much of the game's own follower-quest table stays in rotation.

    Followers walk over and offer one of the game's ~87 built-in quests: cook three great
    meals, dress someone in a fancy suit, murder a specific follower at night. Most is busywork
    that pulls against what the multiworld wants from you. But handing quests in is also the
    main source of follower loyalty XP, so cutting all of them slows levelling down.

    unchanged: every quest stays in rotation.

    thin_trickle: keep the ritual quests, the crusade collection quests and the follower story
      chains (Sozo, the lovers, the rivalries); drop the rest. Followers still level up.

    story_only: story chains only. Followers still wander over, and say "oh, never mind" when
      they have nothing to give.

    none: no follower quests at all. The quietest option, and the slowest for levelling.
    """
    display_name = "Vanilla Follower Quests"
    option_unchanged = 0
    option_thin_trickle = 1
    option_story_only = 2
    option_none = 3
    default = 1


class RegionAccessOrder(Choice):
    """The order the four crusade regions unlock in, and whether they're gated at all.

    vanilla_order: the game's own order (Darkwood, Anura, Anchordeep, Silk Cradle), still
      gated behind Progressive Bishop's Domain items. Just the familiar sequence.

    randomized: any region can be the free starting one, and the other three unlock in a
      random per-seed order.

    randomized_safe_start: as randomized, but never Silk Cradle first. Getting through it
      costs you a Follower, which is brutal as a seed's opening move, before you have a flock
      to spare.

    all_unlocked: every region open from the start, with no Progressive Bishop's Domain items
      in the pool. That's this world's only progression item, so seeds become a single
      sphere."""
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
    """Include content from the paid Woolhaven DLC.

    Enable this ONLY if you own Woolhaven. The mod can't grant content you don't own, so a seed
    made with this on and played without the DLC is unbeatable. Adds 6 sermon upgrades, 19
    tarot cards and the Flail weapon family. Fleeces and doctrines come later."""
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

    Tenths because that's what the game's bar counts in: it renders `12/30`.

    The default is tuned for a seed of roughly six hours, which puts the sermon block at about
    35 sermons. Raise it for a longer game: 30 is about 51 sermons and 50 about 80. Set 0 to
    leave the game's own curve alone, which runs to about 150 at a 20-strong flock.

    Those counts assume a low-level flock. A sermon pays more as Followers level up, so treat
    them as the slow end.

    Only affects the Temple upgrade sermon, not the five doctrines."""
    display_name = "Sermon XP Cap"
    range_start = 0
    range_end = 100
    default = 20


# Ceiling for StructuresData.BuildDurationGameMinutes, which despite the name is a build
# progress total rather than a duration: a player hammer swing adds 5, while an assigned
# Follower accrues it in game time. 30 is six swings, the same as a Bed or a Farm Plot.
FAST_BUILD_MINUTES = 30


class FastBuild(Toggle):
    """Cap how much build progress any structure needs at 30.

    A hammer swing is worth 5, so that's six swings, the same as a Bed or a Farm Plot. Most
    buildings need 10 to 180 already, so for those this only shaves off a few seconds. What it
    actually changes is the 300 to 600 tier: Temple III and IV, the shrine upgrades and the
    repairables.

    A ceiling, not a floor. Anything already cheaper than 30 keeps its own cost.

    Quality of life only. It changes no checks and no logic, it just speeds up building."""
    display_name = "Fast Build"
    default = True


class BuildingChecks(Toggle):
    """Send a check the first time you construct each of 25 curated buildings.

    Temple, Sleeping Bags, Missionary, Tabernacle, Offering Statue, Confession Booth, Kitchen
    and so on. No decorations. Upgrade tiers are excluded except Shelter.

    Only the first build of each counts, tracked per seed rather than per save, so demolishing
    and rebuilding won't pay twice. Connecting on a save that already has buildings standing
    sends the whole backlog at once."""
    display_name = "Building Checks"
    default = True


class BroomChecks(Toggle):
    """Send a check for each of the 10 broom levels.

    Sweeping raises your chore level, which makes cleaning faster. The ten levels cost 3, 5, 10,
    20, 30, 50, 75, 100, 150 and 200 chore XP, so they trickle in across a run."""
    display_name = "Broom Checks"
    default = True


class TrapPercentage(Range):
    """What percentage of the filler items in your seed are traps instead.

    Only one trap exists so far: Dissent Trap, which drains 5 cult faith.

    0 disables traps entirely. Traps replace filler only and they never take the place of a
    real item, so raising this can't make a seed harder to complete, only more annoying."""
    display_name = "Trap Percentage"
    range_start = 0
    range_end = 50
    default = 5


class TarotShopChecks(Toggle):
    """Send a check for each Tarot Card bought from a hub shop.

    Every hub (Pilgrim's Passage, Spore Grotto, Smuggler's Sanctuary, Midas's Cave) sells a
    fixed set of cards, four per hub and 16 in total."""
    display_name = "Tarot Shop Checks"
    default = True


class SnailShrineChecks(Toggle):
    """Send a check for each of the 5 Snail Shrines you make a Shell offering at."""
    display_name = "Snail Shrine Checks"
    default = True


class RandomizeTarotCards(Toggle):
    """Randomize the Tarot Card collection.

    The cards this seed manages are taken out of your collection on connect and arrive back as
    Archipelago items. Unlocking one in game, a crusade find or a challenge reward, sends a
    check. Shop cards are checked at the shop slot instead, and the 15 the game starts you with
    are left alone. 28 cards, or 47 with Include Woolhaven DLC.
    Turning Tarot Shop Checks off drops those to 12 and 31, since the shop cards go back to the
    game."""
    display_name = "Randomize Tarot Cards"
    default = True


class StartingTarotCards(Range):
    """How many extra Tarot Cards to start with, on top of the 15 the game gives you.

    These have neither a check nor an item, so each one you add removes one of each."""
    display_name = "Starting Tarot Cards"
    range_start = 0
    range_end = 20
    default = 0


class StartingTarotPool(Choice):
    """Which cards the starting ones are drawn from.

    vanilla_defaults: the 15 the game normally starts you with. You keep those anyway, so this
      is the "no extra cards" setting and Starting Tarot Cards has no effect alongside it.

    any: any randomizable card, Woolhaven ones included if that option is on. More variance.
      It can hand you something excellent on day one, or three cards you can't use yet."""
    display_name = "Starting Tarot Pool"
    option_vanilla_defaults = 0
    option_any = 1
    default = 0


class RandomizeWeapons(Toggle):
    """Randomize which weapon families you can find.

    Vanilla drip-feeds the Sword, Axe, Dagger, Hammer, Gauntlets and Blunderbuss: the first
    floor of a run hands you one you don't own yet, with Gauntlets, Hammer and Blunderbuss also
    gated on Bishops you have beaten. With this on, podiums, chests and choice rooms only offer
    families the multiworld has granted you, and equipping one for the first time sends a check.
    6 weapons, or 7 with Include Woolhaven DLC (the Flail)."""
    display_name = "Randomize Weapons"
    default = True


class StartingWeapons(Range):
    """How many weapon families you begin the seed with."""
    display_name = "Starting Weapons"
    range_start = 1
    range_end = 7
    default = 1


class LegendaryWeapons(Choice):
    """Let Legendary weapons turn up on ordinary weapon podiums.

    Requires the Woolhaven DLC, so it is forced off without Include Woolhaven DLC.

    One side effect: picking one up adds it to your weapon pool permanently, because the game
    records whatever you collect. Collecting all seven this way pops the all-Legendary
    achievement without doing the questlines, and it stays in your save after you stop using
    the mod.

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
    Divine Blast, Ichor Thrown and Death's Sweep.

    Teleport curses aren't here. They come from the Woolhaven sermon upgrade Teleporting
    Curses rather than crusade drops."""
    display_name = "Randomize Curses"
    default = True


class StartingCurses(Range):
    """How many curse families you begin the seed with."""
    display_name = "Starting Curses"
    range_start = 1
    range_end = 5
    default = 1


class DivineInspirationMode(Choice):
    """How Archipelago interacts with the Divine Inspiration tree, the buildings-and-rituals
    tree you open at the Shrine rather than the Temple sermon tree.

    69 upgrades and 69 checks. The check fires when you fill the Devotion meter, not when you
    spend the point. Same shape as the sermon bar.

    See Divine Inspiration Devotion Cap: without it, all 69 points cost ~24,000 Devotion and
    most of this block is out of reach in a normal seed.

    off: no interaction at all. The tree behaves exactly as vanilla.

    checks_only: filling the meter sends a check and you keep the point, as in vanilla. 69
      checks, no items.

    checks_and_points: filling the meter sends a check and the point is taken. Points come back
      from Archipelago instead, and you spend them on whatever you like. Same totals as vanilla,
      just on the multiworld's schedule.

    checks_and_techs: the point never comes back, and Archipelago grants the upgrades directly.
      You never choose anything, since the tree unlocks itself.

    curated_checks: as checks_and_techs, but the 69 upgrades are grouped into 38 items, so a
      building and all of its tiers arrive together and "Missionary Network" is one item rather
      than three. Five tier-1 upgrades (Temple, Farm Plot, Sleeping Bags, Body Pit, Farming
      Bundle) are free from the start, since the cult can't function without them. Progressive
      Cult and Progressive Shrine Flame stay tiered, because both multiply Devotion. This mode
      also uses Divine Inspiration Checks to shorten the block.

    Any combination with Divine Inspiration Shuffle is a legal seed."""
    display_name = "Divine Inspiration Mode"
    option_off = 0
    option_checks_only = 1
    option_checks_and_points = 2
    option_checks_and_techs = 3
    option_curated_checks = 4
    default = 2


class DivineInspirationDevotionCap(Range):
    """The most Devotion a single ability point is allowed to cost.

    Vanilla's curve runs 1, 13, 29, 45 ... up to 465 and stays there, so all 69 points cost
    about 24,000 Devotion. Every check in this block is a filled meter, so without a cap most of
    the block is out of reach.

    The default is tuned for a seed of roughly six hours, bringing all 69 points down to about
    4,600 Devotion. Raise it for a longer game: 100 is about 6,500, 150 about 9,400, and 200
    about 12,300. Set 0 to leave the game's own curve alone.

    Capping only flattens the expensive tail. The early curve stays exactly as the game wrote
    it."""
    display_name = "Divine Inspiration Devotion Cap"
    range_start = 0
    range_end = 465
    default = 70


class DivineInspirationChecks(Range):
    """How many Divine Inspiration checks the block has, in curated_checks mode.

    Only read when Divine Inspiration Mode is curated_checks. Every other mode uses all 69.

    69 is a lot. At the default Devotion cap, 30 is about one session.

    The block can be shorter than its own item count. The extra items just go elsewhere in the
    seed, onto locations that would otherwise hold filler.

    Setting this very low while also turning most other check blocks off can leave more items
    than locations, which fails generation."""
    display_name = "Divine Inspiration Checks"
    range_start = 5
    range_end = DIVINE_INSPIRATION_COUNT
    default = 30


class DivineInspirationShuffle(Choice):
    """Rearranges which tier each Divine Inspiration upgrade sits in.

    A layout change only. It moves upgrades between rows without changing what Archipelago
    checks or grants, so it's safe with any Divine Inspiration Mode.

    default: the game's own layout.

    random_except_first: tier 1 stays put and the other four are reshuffled. The safer choice,
      since your opening is unchanged.

    true_random: every upgrade is reassigned a tier, so Crypt III can turn up in row 1 and
      Sleeping Bags in row 5. A very different opening.

    Either way the five central nodes (Temple, Cult II, Refinery, Cult III, Cult IV) stay in
    their own tier, since each one has to be bought to open the next."""
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
    fast_build: FastBuild
    building_checks: BuildingChecks
    broom_checks: BroomChecks
    trap_percentage: TrapPercentage
