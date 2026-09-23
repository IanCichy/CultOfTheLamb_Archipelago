# Cult of the Lamb Archipelago

A [Cult of the Lamb](https://store.steampowered.com/app/1313140/Cult_of_the_Lamb/) Archipelago mod. Nearly everything in the game can become
an Archipelago item or check: region access, weapons, curses, sermon upgrades, tarot cards,
divine inspiration, buildings, followers, and more.

>[!IMPORTANT]
>Find the latest release of the mod [here](https://github.com/IanCichy/CultOfTheLamb_Archipelago/releases/latest)

New to Archipelago? Find more [info](https://archipelago.gg)

## Requirements

1. **Cult of the Lamb**, base game.
   - The Woolhaven DLC is optional
2. **BepInEx 5** for Cult of the Lamb. If you install via r2modman (recommended), this is
   pulled in for you.

3. **An Archipelago install**, version `0.6.6` or newer, to generate a seed and host. Get it from [archipelago.gg](https://archipelago.gg/).

## Installation

1. Install [r2modman](https://thunderstore.io/c/cult-of-the-lamb/p/ebkr/r2modman/)
   and create a profile for Cult of the Lamb.

2. Find **Archipelago_CultOfTheLamb** in the profile's online mod list and install it. The
   correct BepInEx pack comes with it.

3. Only if you're generating the seed yourself: download `cult_of_the_lamb.apworld` from the
   [latest release](https://github.com/IanCichy/CultOfTheLamb_Archipelago/releases/latest) and
   drop it into your Archipelago install:

        Archipelago\custom_worlds\cult_of_the_lamb.apworld

## Playing

### 1. Get your YAML
Download one of the `.yaml` files from the latest release on GitHub

- `cult_of_the_lamb_quickstart.yaml` has a handful of options. Start here if you don't want to
  be overwhelmed.
- `cult_of_the_lamb_example.yaml` has every option, each explained in a comment above it.

Open it and change `name: PlayerName` to the name you want in the multiworld. Everyone in a
multiworld needs a different one.

### 2. Generate a seed
   Put your YAML in `Archipelago\Players\`, then open Archipelago and click Generate

   This produces a `.archipelago` file. Upload it at
   [archipelago.gg/uploads](https://archipelago.gg/uploads) to have it hosted for you, which
   gives you the server address and port to connect to. You can also host it yourself from the
   Archipelago launcher.

### 3. Load the game and connect

> [!CAUTION]
> Use a save dedicated to Archipelago. With `randomize_tarot_cards` on, connecting removes the
> cards this seed manages from your collection and hands them back as multiworld items. They
> come back when you disconnect, but a card you unlock while disconnected sends no check and
> is taken back. Adding the mod to an existing game has not been tested.
>
> Saves that have received Archipelago items show an AP icon on the save select screen.

   - Launch Cult of the Lamb through **r2modman** (click *start modded*). Launching from Steam
     runs the game without the mod, and none of the Archipelago menus will appear.
   - Start a new game, or load a save you have already used for Archipelago

   - Open the pause menu and click **Archipelago**, which opens a new dialogue box. Fill in your server (e.g. `archipelago.gg:38281`), slot name (the name from your `.yaml` file) and
   password (leave blank if none). Click **Connect**.
      - The Archipelago dialogue box will tell you it's connected to a server
      - If it doesn't connect, the box says why. A wrong slot name is the usual cause, and it
        has to match the `name:` in your YAML exactly

>[!TIP]
>There is an AP icon in the upper-left of the screen. When the icon is in full color that confirms that you're connected to an AP world. The icon will be washed out if you aren't connected.

## Features

### What's Included
- Region access randomized
- Weapon and curse families randomized
- Sermon upgrades randomized
- Tarot cards randomized
- Divine Inspiration randomized with multiple options
- Checks for every Bishop, miniboss and Witness
- Toggleable checks: follower recruitment, snail shrine, tarot shop, construction, sweeping
- Multiple goal options (Bishops, Witnesses, or Narinder)
- Woolhaven DLC support (only enable `include_woolhaven` in your YAML if you actually own it)
- Resource bundle filler items
- One trap so far, with a percentage knob
- In-game ways to view your progress

### What's **NOT** Included
- Death Link
- Multiplayer / co-op

>[!NOTE]
>**There are several in-game ways to track your progress**
>  - See your Archipelago checklist with the **Quests tab** in the pause menu
>     - Tracks win condition, region access, and live progress per active check block
>     - Will only show up if `archipelago_objective_guide` is enabled
>
>   - See your unlocked tarot cards at the **relic and tarot book** at the south end of the base
>     - This will always show up even if `randomize_tarot_cards` is not enabled
>
>   - See what weapons and curses you have unlocked with the **podiums** at the south end of the base
>     - Podiums light up to show which weapon and curse families you've been granted
>     - These will only show up if you have `randomize_weapons` and/or `randomize_curses` enabled
>
>   - See the sermons you have unlocked in the **AP** entry at the Temple altar
>     - This menu will always show up even if `randomize_sermon_upgrades` is not enabled

## Verifying the install

Check `BepInEx/LogOutput.log` after connecting. A working install shows:

```
Archipelago.CultOfTheLamb v0.9.0 loaded.
...
[AP] Connected!
[AP] Versions: client 0.9.0, apworld 0.9.0
```

The two versions should match. A `MISMATCH` warning means your mod and your apworld are out of
step, which usually means the apworld in `custom_worlds\` is older than the mod. If you instead see
a line about **"Developer debug keys are compiled into this build"**, you have a dev build rather
than a released one. Reinstall the mod through your mod manager.

## Reporting issues

This is a first beta and nothing here has been through a real multiworld yet. If something looks
wrong, assume it hasn't been seen rather than that it's known and accepted.

**Press F9 in game first.** That writes the mod's state to the log, and a popup confirms it worked.
A log with that in it answers most of what a bug report would otherwise take a conversation to
sort out.

Open a [GitHub issue](https://github.com/IanCichy/CultOfTheLamb_Archipelago/issues) with your
`LogOutput.log` and, if it's seed-specific, your YAML. Reports from real multiworld sessions are
the most valuable thing this beta can get - "the checklist said one thing and the server said
another" or "this item never arrived" matter more than they might seem to.

## AI disclosure

This mod is AI-assisted. See [AIdisclosure.md](AIdisclosure.md) for details.

## Credits

Built by Ian Cichy

Not affiliated with or endorsed by Massive Monster or Devolver Digital. Cult of the Lamb is
their trademark.

## Third-party software

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) - MIT
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) - MIT
- [BepInEx](https://github.com/BepInEx/BepInEx) - LGPL-2.1
- [HarmonyLib](https://github.com/pardeike/Harmony) - MIT
