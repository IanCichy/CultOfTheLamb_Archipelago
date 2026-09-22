# Cult of the Lamb

## Where is the options page?

The [player options page for this game](../player-options) has every option with a description,
and can export a config file for you. The presets in the dropdown at the top are a faster start
than reading all of it.

## What does randomization do to this game?

Vanilla Cult of the Lamb hands you things on a schedule: a weapon family per run until you own
them all, a sermon upgrade every time the Temple bar fills, a tarot card when you clear a room.
Archipelago takes that schedule away and gives it to the multiworld.

The four crusade regions are the backbone. Normally they open in a fixed order as you kill
Bishops; here each one is gated behind a **Progressive Bishop's Domain** item, and which region
that opens is decided per seed. That is this world's only progression item, so it is what decides
whether your seed has a shape or is one long sphere.

Everything else is optional and switched on per block in your YAML. With all of them on, and the
Woolhaven DLC included, the world has 261 locations.

## What is the goal of Cult of the Lamb?

You pick one of three, and how many of it you need:

- **Bishops** — defeat 1 to 4 of Leshy, Heket, Kallamar and Shamura.
- **Witnesses** — defeat 1 to 4 of Agares, Bathin, Astaroth and Allocer. Each Witness only
  becomes fightable after its own region's Bishop is dead, so this is the longer of the two.
- **Narinder** — beat the game. The Gateway only opens once all four Bishops are down, so this
  always needs every region.

## What are location checks in Cult of the Lamb?

Every block here is optional except the bosses.

| Block | Checks | Notes |
|---|---|---|
| Divine Inspiration | 69 | Filling the Devotion meter, not spending the point |
| Tarot cards | 41 | 28 base, 47 with Woolhaven, minus the shop cards |
| Sermon upgrades | 38 | 32 without Woolhaven |
| Buildings | 25 | First construction of each curated building |
| Follower milestones | 20 | Your first 20 recruited Followers, ever-recruited |
| Tarot shop slots | 16 | Four hubs, a fixed set of cards each |
| Minibosses | 12 | Three per region |
| Broom levels | 10 | Chore XP, trickles in as you sweep |
| Weapon families | 7 | Equipping one for the first time |
| Curse families | 5 | Same |
| Snail Shrines | 5 | One Shell offering each |
| Bishops | 4 | Always present |
| Witnesses | 4 | Always present |

The two big ones are worth understanding before you set your options:

**Divine Inspiration** is the buildings-and-rituals tree at the Shrine, not the Temple sermon
tree. The check fires when you *fill* the Devotion meter — the Nth point you earn is the Nth
check. What you spend it on is your business. Vanilla's cost curve climbs to 465 Devotion and
stays there, which puts all 69 points at about 24,000 Devotion, so the `divine_inspiration_devotion_cap`
option exists to flatten that tail into something a normal seed can actually reach.

**Sermon upgrades** work the same way: filling the Temple bar sends a check instead of opening
the pick-an-upgrade screen, and `sermon_xp_cap` flattens that curve.

## Which items can be in another player's world?

Anything this world hands out:

- **Progressive Bishop's Domain** — the only progression item, and the only thing gating you.
- **Weapon and curse families** — Sword, Axe, Dagger, Hammer, Gauntlets, Blunderbuss, and the
  Flail with Woolhaven; Flaming Shot, Touch of Turua, Divine Blast, Ichor Thrown, Death's Sweep.
- **Sermon upgrades** — Hearts of the Faithful, Might of the Devout, the weapon affixes, the
  curse packs, the Heavy Attack masteries.
- **Tarot cards.**
- **Divine Inspiration** — either ability points or the upgrades themselves, depending on mode.
- **Filler** — resource bundles (Construction, Larder, Ritual, Artisan, Treasury) and Follower
  Level Up.
- **Dissent Trap** — the one trap, drains 5 cult faith.

## What does another world's item look like in Cult of the Lamb?

The same notification the game uses for anything else, coloured by what it is. There is no
separate item model in the world — checks fire off things you were doing anyway, so you find out
by the popup rather than by walking into a chest.

## When the player receives an item, what happens?

It applies immediately and a notification names the item and who sent it. Weapon and curse
families start appearing on podiums and in choice rooms from then on. Sermon upgrades and Divine
Inspiration unlocks are applied to your save. Resource bundles land in your stores.

If you were offline when it was sent, you get it on reconnect.

## Tracking your progress

Turn on `archipelago_objective_guide` (it is on by default) and the pause menu's Quests tab grows
an Archipelago group: your goal, your region access, and one live line per active check block.
There is no external tracker for this game yet, so this is how you know what the seed still wants
from you.

## Is there a DeathLink?

Not yet.
