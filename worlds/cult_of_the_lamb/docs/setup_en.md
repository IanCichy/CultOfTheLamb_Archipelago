# Cult of the Lamb Setup Guide

## Required Software

- [Cult of the Lamb](https://store.steampowered.com/app/1313140/Cult_of_the_Lamb/). The Woolhaven
  DLC is optional.
- [r2modman](https://thunderstore.io/c/cult-of-the-lamb/p/ebkr/r2modman/), or another mod manager
  that installs BepInEx 5 packs.
- An [Archipelago](https://archipelago.gg/) install, version `0.6.6` or newer, if you want to
  generate seeds yourself.

## Installation

### The apworld

Download `cult_of_the_lamb.apworld` from the
[latest release](https://github.com/IanCichy/CultOfTheLamb_Archipelago/releases/latest) and put it
in your Archipelago install:

    Archipelago\custom_worlds\cult_of_the_lamb.apworld

Anyone generating a seed with Cult of the Lamb in it needs this file. Players who are only joining
a game someone else generated do not.

### The mod

1. Install r2modman and create a profile for Cult of the Lamb.
2. Find **Archipelago_CultOfTheLamb** in the profile's online mod list and install it. The BepInEx
   pack it needs comes with it.
3. Launch the game from r2modman with **Start modded**. Launching from Steam runs the game without
   the mod.

## Configuring your YAML

Your YAML file is what tells the generator how you want your game randomized. Two are attached to
each release:

- `cult_of_the_lamb_quickstart.yaml` has a handful of options. Start here if you don't want to read
  through everything.
- `cult_of_the_lamb_example.yaml` has every option, with each one explained in a comment above it.

Put your finished file in `Archipelago\Players\` before generating, or hand it to whoever is
generating for your group.

A few options are worth knowing about before your first seed:

- `include_woolhaven` only works if you own the Woolhaven DLC. Turning it on without the DLC puts
  items in the seed that you can never reach.
- `goal` picks what ends your game: beating a number of Bishops, a number of Witnesses, or
  Narinder.
- `region_access_order` controls how the four Bishop regions open up. The randomized options are
  where most of the variety comes from.

## Joining a multiworld game

1. Start the game through r2modman.
2. Start a new game, or load a save you have already been using for Archipelago.
3. Open the pause menu and click **Archipelago**. Fill in the server address (something like
   `archipelago.gg:38281`), your slot name from your YAML, and the password if the room has one.
   The main menu has the same panel if you want to type your details in early, but
   connecting needs a save loaded.
4. Click **Connect**. The dialogue box tells you when you're connected, and the AP icon in the
   upper left of the screen turns to full color.

Use a save dedicated to Archipelago. Adding the mod to an existing vanilla playthrough has not
been tested, and with `randomize_tarot_cards` on, connecting removes this seed's cards from your
collection until the multiworld hands them back. Saves that have received Archipelago items are
marked with an AP icon on the save select screen, so they're easy to tell apart.

If the connection drops, the mod keeps trying on its own and sends your progress once it's back.
Tarot cards are the exception: one unlocked while disconnected sends no check and is taken back
on reconnect.

## Tracking your progress in game

- The **Quests tab** in the pause menu holds an Archipelago checklist with your goal, your region
  access, and your progress on each kind of check that's turned on.
- The **relic and tarot book** at the south end of your base shows the tarot cards you've unlocked.
- The **podiums** at the south end of your base light up for the weapon and curse families you've
  been sent.
- The **AP** entry at the Temple altar shows your sermon upgrade tree.

## Troubleshooting

**Check that the mod loaded.** Open `BepInEx/LogOutput.log` in your r2modman profile folder. A
working install has these lines in it:

    Archipelago.CultOfTheLamb v0.9.0 loaded.
    ...
    [AP] Connected!
    [AP] Versions: client 0.9.0, apworld 0.9.0

**The two versions should match.** A `MISMATCH` warning means the apworld in `custom_worlds\` is a
different version from the mod, and it's usually the apworld that's out of date.

**If you see a line about developer debug keys**, you have a development build rather than a
release. Reinstall the mod through your mod manager.

**Before reporting a bug, press F9 in game.** That writes the mod's state to the log, and a popup
confirms it worked. A log with that in it answers most of the questions a bug report would
otherwise need a conversation to sort out. Open an issue on
[GitHub](https://github.com/IanCichy/CultOfTheLamb_Archipelago/issues) with the log attached, and
your YAML as well if the problem looks specific to your seed.

You can also ask in the Cult of the Lamb thread on the
[Archipelago Discord](https://discord.gg/archipelago).
