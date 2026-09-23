# Cult of the Lamb

Cult of the Lamb is two games at once: a roguelike crusade through four regions, and a base
builder where your Followers' Devotion and your sermons pay for upgrades. Archipelago randomizes
both halves.

This is a first beta. It has been generated and played solo, but not yet through a real
multiworld, so expect rough edges and report them. Use a save dedicated to Archipelago, and only
turn on `include_woolhaven` if you own the DLC.

## Where is the options page?

The [player options page for this game](../player-options) has every option with a description,
and can export a config file for you. The presets in the dropdown at the top are a faster start
than reading all of it.

## What does randomization do to this game?

Vanilla Cult of the Lamb hands you things on a schedule: a weapon family per run until you own
them all, a sermon upgrade every time the Temple bar fills, a tarot card from a shop or a
challenge. Archipelago takes that schedule away and gives it to the multiworld.

The four crusade regions are the backbone. Normally they open in a fixed order as you kill
Bishops. Here one region is open from the start and the other three are each gated behind a
**Progressive Bishop's Domain** item, in a per-seed order. That item is the main thing shaping a
seed. Weapon and curse families gate their own checks too, since a podium will not offer a family
the multiworld has not sent you.

Everything else is optional and switched on per block in your YAML. With all of them on, and the
Woolhaven DLC included, a seed has 244 locations. A default seed without the DLC has 218.

## What is the goal of Cult of the Lamb when randomized?

You pick one of three. The first two also let you choose how many you need:

- **Bishops**: defeat 1 to 4 of Leshy, Heket, Kallamar and Shamura.
- **Witnesses**: defeat 1 to 4 of Agares, Bathin, Astaroth and Allocer. Each Witness only becomes
  fightable after its own region's Bishop is dead, so this is the longer of the two.
- **Narinder**: beat the game. The Gateway only opens once all four Bishops are down, so this
  always needs every region, and the count above does not apply.

## What are location checks in Cult of the Lamb?

Every block here is optional except the bosses.

| Block | Checks | Notes |
|---|---|---|
| Divine Inspiration | 69 | Filling the Devotion meter, not spending the point |
| Sermon upgrades | 38 | 32 without Woolhaven |
| Tarot cards | 31 | 12 without Woolhaven. The 16 shop cards are the row below |
| Buildings | 25 | First construction of each curated building |
| Follower milestones | 20 | Your first 20 recruited Followers, ever-recruited |
| Tarot shop slots | 16 | Four hubs, a fixed set of cards each |
| Minibosses | 12 | Three per region |
| Broom levels | 10 | Chore XP, trickles in as you sweep |
| Weapon families | 6 | 5 without Woolhaven. The family you start with has no check |
| Snail Shrines | 5 | One Shell offering each |
| Curse families | 4 | Five families; the one you start with has no check |
| Bishops | 4 | Always present |
| Witnesses | 4 | Always present |

The two big ones are worth understanding before you set your options:

**Divine Inspiration** is the buildings-and-rituals tree at the Shrine, not the Temple sermon
tree. The check fires when you *fill* the Devotion meter, so the Nth point you earn is the Nth
check. What you spend it on is your business. Vanilla's cost curve climbs to 465 Devotion and
stays there, which puts all 69 points at about 24,000 Devotion, so the `divine_inspiration_devotion_cap`
option exists to flatten that tail into something a normal seed can actually reach.

In `curated_checks` mode the block can be anywhere from 5 checks to the full 69, and the
upgrades arrive bundled so a building and all its tiers come together.

**Sermon upgrades** work the same way: filling the Temple bar sends a check instead of opening
the pick-an-upgrade screen, and `sermon_xp_cap` flattens that curve.

## Which items can be in another player's world?

Anything this world hands out:

- **Progressive Bishop's Domain**: opens your regions, and is the main thing shaping a seed.
- **Weapon and curse families**: Crusader's Blade, Apostate's Cleaver, Traitor's Razor,
  Warmaker's Hammer, Tempest's Gauntlets, Mayhem's Cannon, and Battler's Bludgeon with Woolhaven;
  Flaming Shot, Touch of Turua, Divine Blast, Ichor Thrown, Death's Sweep.
- **Sermon upgrades**: Hearts of the Faithful, Might of the Devout, the weapon affixes, the curse
  packs, the Heavy Attack masteries.
- **Tarot cards.**
- **Divine Inspiration**: either ability points or the upgrades themselves, depending on mode.
- **Filler**: resource bundles (Construction, Larder, Ritual, Artisan, Treasury) and Follower
  Level Up.
- **Dissent Trap**: the one trap, drains 5 cult faith.

## What does another world's item look like in Cult of the Lamb?

The same notification the game uses for anything else, coloured by what it is. There is no
separate item model in the world. Checks fire off things you were doing anyway, so you find out
by the popup rather than by walking into a chest.

## When the player receives an item, what happens?

It applies immediately and a notification names the item and who sent it. Weapon and curse
families start appearing on podiums and in choice rooms from then on. Sermon upgrades and Divine
Inspiration unlocks are applied to your save. Resource bundles land in your stores.

If you were offline when it was sent, you get it on reconnect. Checks you earn offline are sent
too, with one exception: with `randomize_tarot_cards` on, a card unlocked while disconnected
sends no check and is taken back on reconnect, so earn cards while connected.

## Tracking your progress

Turn on `archipelago_objective_guide` (it is on by default) and the pause menu's Quests tab grows
an Archipelago group: your goal, your region access, and one live line per active check block.
There is no external tracker for this game yet, so this is how you know what the seed still
wants. The base shows more: your tarot book, your weapon and curse podiums, and your sermon tree
at the Temple altar.

## Is there a DeathLink?

Not yet.
