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
2. BepInEx 5 for Cult of the Lamb. If
  you install via r2modman (recommended), this is pulled in for you.

3. **An Archipelago install**, version `0.6.6` or newer, to generate a seed and host. Get it from [archipelago.gg](https://archipelago.gg/).

## Installation

1. Install [r2modman](https://thunderstore.io/c/cult-of-the-lamb/p/ebkr/r2modman/) 
   and create a profile for Cult of the Lamb.

2. Find **Archipelago_CultOfTheLamb** in the profile's online mod list and install it. The
   correct BepInEx pack comes with it.

3. Download `cult_of_the_lamb.apworld` from the
   [latest release](https://github.com/IanCichy/CultOfTheLamb_Archipelago/releases/latest) and
   drop it into your Archipelago install:
        
        Archipelago\custom_worlds\cult_of_the_lamb.apworld




## Playing


### 1. Generate the YAML
Download one of the `.yaml` files from the latest release on GitHub

`cult_of_the_lamb_quickstart.yaml` provides only a handful of options to customize your experience
 - This is a great place to start if you don't want to be overwhelmed by options


`cult_of_the_lamb_example.yaml` provides all options to customize your experience


### 2. Generate a seed
   Open up Archipelago and click Generate

   This produces a `.archipelago` file. Upload it to the Archipelago server, or host it locally




### 3. Load the game and connect
   - Launch Cult of the Lamb through **r2modman** (click *start modded*)
   - Start a new game or load a previous Archipelago save

  > [!CAUTION]
> It is recommended to use a dedicated AP save. Adding the mod to an existing vanilla game
> has not been tested. Proceed at your own risk.        
>There is currently no way to denote which save has been played on an Archipelago server previously. Make sure you remember which slot you're saving your game in.
   - Open the pause menu and click **Archipelago**, which opens a new dialogue box. Fill in your server (e.g. `archipelago.gg:38281`), slot name (the name from your `.yaml` file) and
   password (leave blank if none). Click **Connect**.
      - The Archipelago dialouge box will tell you it's connected to a server



>[!TIP]
>There is an AP icon in the upper-left of the screen. When the icon is in full color that confirms that you're connected to an AP world. The icon will be washed out if you aren't connected.






## Features

### What's Included
- Region access randomized
- Weapon and curse families randomized
- Sermon upgrades randomized
- Tarot cards randomized
- Divine inspiration randomized with multiple options
- Toggleable checks: bishops, minibosses, witnesses, follower recruitment, snail shrine, tarot
  shop, construction, sweeping
- Multiple goal options (Bishops, Witnesses, or Narinder)
- Woolhaven DLC support (only enable `include_woolhaven` in your YAML if you actually own it)
- Resources bundle filler items
- Traps 
- In-game ways to view your progress

### What's **NOT** Included
- Death Link
- Multiplayer / co-op


>[!NOTE]
>**There are several in-game ways to track your progress**
>  - See your Archipelago checklist with the **Quests tab** in the pause menu
>     - Tracks win condition, region access, and live progress per active
>   check block 
>     - Will only show up if `archipelago_objective_guide` is enabled
>  
>   - See your unlocked tarot cards at the **tarot card podium** at the south-end of the base 
>     - This will always show up even if `randomize_tarot` is not enabled
>   
>   - See what weapons and curses you have unlocked with the **podiums** at the south-end of the base
>     - Podiums light up to show which weapon and curse families
>     you've been granted
>     - These will only show up if you have `randomize_weapons` and/or `randomize_curses` enabled
>   
>   - See the sermons you have unlocked in the **Archipelago menu in the temple**
>     - This menu will always show up even if `randomize_sermons` is not enabled

## Verifying the install

Check `BepInEx/LogOutput.log` after connecting. A working install shows:

```
Archipelago.CultOfTheLamb v0.9.0 loaded.
...
[AP] Connected!
[AP] Versions: client 0.9.0, apworld 0.9.0
```

The two versions should match - a `MISMATCH` warning means your mod build and your apworld are
out of step, and usually that the apworld in `custom_worlds\` is older than the mod. If you
instead see a line about **"Developer debug keys are compiled into this build"**, you have a dev
build rather than a released one; reinstall the mod through your mod manager.

## Known issues

- This is a first beta. If something looks wrong, it probably hasn't been seen yet rather than
  being a known, accepted issue - see [Reporting issues](#reporting-issues).

## Reporting issues

Open a [GitHub issue](https://github.com/IanCichy/CultOfTheLamb_Archipelago/issues) with your
`LogOutput.log` and, if it's seed-specific, your YAML. Reports from real multiworld sessions are
the most valuable thing this beta can get - "the checklist said one thing and the server said
another" or "this item never arrived" matter more than they might seem to.

## Credits

Built by Ian Cichy

## Third-party software

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) -
  MIT
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) - MIT
- [BepInEx](https://github.com/BepInEx/BepInEx) - LGPL-2.1
- [HarmonyLib](https://github.com/pardeike/Harmony) - MIT


