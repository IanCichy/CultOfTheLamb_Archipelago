# Changelog

## 0.9.1

### Fixed

- Beating Narinder now reports the goal.
- Max health no longer climbs past the game's ceiling when a batch of items arrives on connect.
- Universal Tracker shows the right starting region.
- The sermon XP cap is logged as the number you set, not the game's internal value.
- Deleting a save no longer leaves its Archipelago badge on the empty slot.

## 0.9.0: First public beta

The first public release, so this covers everything the mod does. Later releases will list what
changed.

### Part of the multiworld

- **Region access.** One region is free at seed start, and the other three each unlock through
  a Progressive Bishop's Domain item. Pick vanilla order, randomized, randomized with a safe
  start, or everything open.
- **Weapons and curses.** You're only offered the families you've been sent, and equipping a family
  for the first time sends a check.
- **Sermon upgrades.** Filling the sermon bar sends a check, and the upgrades arrive as items.
- **Tarot cards.** Unlocking one sends a check, and the cards arrive as items. Shop cards are
  checked at the shop slot instead, and the 15 the game starts you with are left alone.
- **Divine Inspiration.** Several modes, from checks only up to the multiworld handing out the
  upgrades, with an optional shuffle of which tier each upgrade sits in.
- **More checks.** Bishops, minibosses and Witnesses are always checks. Follower milestones, Snail
  Shrine offerings, tarot shop slots, buildings and sweeping can each be turned on or off.
- **Goals.** Beat a number of Bishops, a number of Witnesses, or Narinder.
- **Filler**, and one trap so far, with `trap_percentage` to control how often it shows up.
- **Woolhaven DLC** content behind `include_woolhaven`, including optional Legendary weapon offers.

### Pacing

- Optional caps on how much Devotion a Divine Inspiration point costs and how much XP a sermon
  upgrade costs, plus a Fast Build toggle that caps build progress at six hammer swings.
- Most of the game's follower quests can be taken out of rotation, so errands don't crowd out
  the crusades.
- Choose how many weapons and curses you start with, and how many tarot cards if you set
  `starting_tarot_pool` to `any`.

### In game

- **Connect from the pause menu.** Click Archipelago and fill in your server, slot and
  password. The main menu has the same panel so you can type your details in early, but
  connecting itself needs a save loaded.
- **An AP icon in the corner** shows whether you're connected.
- **AP saves are marked** on the save select screen once they've received an Archipelago item.
- **A popup for every check you send**, naming the item and who it's for. A batch sent after
  a reconnect shows a single count instead.
- **Shop slots that are checks** show the AP logo and the item they hold.
- **An Archipelago checklist in the quest log** with your goal, region access and progress on each
  active check type.
- **The sermon upgrade tree** can be viewed from the Temple altar, since randomizing sermons
  removes the game's own way to see it.
- **Displays in the base** for your weapon and curse families and your tarot collection.
- **Press F9** to write the mod's state to the log for bug reports. A popup confirms it worked.

### Connection

- Reconnects by itself if the connection drops, and keeps trying until the server is back.
- Progress made while disconnected is sent when you reconnect, for bosses, sermons, followers,
  buildings, sweeping, Snail Shrines and Divine Inspiration. Tarot cards are the exception:
  with `randomize_tarot_cards` on, a card unlocked while disconnected sends no check and is
  taken back on reconnect, so earn cards while connected.
- Warns in the log if the mod and apworld versions don't match.
