# Cult of the Lamb - Archipelago

A [Cult of the Lamb](https://store.steampowered.com/app/1313140/Cult_of_the_Lamb/) mod for the
[Archipelago](https://archipelago.gg) multiworld randomizer. Region access, weapons, curses,
sermon upgrades, Tarot cards, Divine Inspiration, buildings, Followers and more all become
Archipelago items and checks.

**Status:** first public beta (`v0.9.0-beta.1`). Playable end to end - connecting, sending
checks, receiving items - but still beta software; see [Known issues](#known-issues).

## Requirements

- **Cult of the Lamb**, base game. The Woolhaven DLC is optional - only enable
  `include_woolhaven` in your YAML if you actually own it (see [YAML options](#yaml-options)).
- **[BepInEx 5](https://github.com/BepInEx/BepInEx/releases/latest)** for Cult of the Lamb. If
  you install via r2modman/Thunderstore (recommended), this is pulled in for you.
- **An Archipelago install**, version `0.6.6` or newer, to generate a seed and host or connect
  to a server. Get it from [archipelago.gg](https://archipelago.gg/).

## Installation

### Quick install (from a GitHub release)

1. Install [r2modman](https://thunderstore.io/c/cult-of-the-lamb/p/ebkr/r2modman/) or an
   equivalent mod manager, and create a profile for Cult of the Lamb.
2. Grab the mod zip from the
   [latest release](https://github.com/IanCichy/CultOfTheLamb_Archipelago/releases/latest) and
   import it via r2modman's **Settings -> Import local mod**. This pulls in the correct BepInEx
   pack version automatically.
3. Grab `cult_of_the_lamb.apworld` from the same release and drop it into your Archipelago
   install's `custom_worlds\` folder (not `lib\worlds\` - that's for worlds bundled with
   Archipelago itself).
4. Launch Cult of the Lamb **through r2modman**, using the profile from step 1.

### Manual install (from a source checkout)

1. Clone this repo.
2. Build the C# mod - see [Building the C# mod](#building-the-c-mod) below - and copy the
   output DLLs into your BepInEx `plugins\` folder.
3. Build the apworld with `py -3.12 build_apworld.py` and drop the resulting
   `cult_of_the_lamb.apworld` into `custom_worlds\`.

## Playing

1. **Generate a seed** in Archipelago the normal way, using a YAML for this world (see
   [YAML options](#yaml-options)), and start or join a server.
2. **Load a save file first** - the pause menu's Archipelago entry is the one that works;
   connecting from the main menu writes nowhere to connect *into*, since there's no save loaded
   yet. This is also why the mod expects a **dedicated save**: it revokes and re-grants weapons,
   curses, sermon upgrades and Tarot cards as items come in, which will fight a vanilla
   playthrough on the same file. Start a fresh cult for your seed.
3. Open the pause menu and pick **Archipelago**, fill in your server, port, slot name and
   password, and hit **Connect**. The same entry exists on the main menu if you'd rather fill in
   the details before loading a save - it just won't let you connect until one is.
4. Play. Whatever you've randomized fires checks as you earn it in game - killing a Bishop,
   filling the sermon bar, drawing a Tarot card, finishing a building, offering a Snail Shrine
   shell, and so on, depending on which check blocks your YAML turned on. Items from the
   multiworld apply automatically, no menu required.
5. If `archipelago_objective_guide` is on (the default), the pause menu's Quests tab keeps a
   running Archipelago checklist - win condition, region access, and live progress per active
   check block - since the game itself gives you no way to tell what a seed wants from you.
6. The Temple Altar's **AP** entry opens a read-only viewer of the sermon upgrade tree, and the
   base's crusade podiums light up to show which weapon and curse families you've been granted -
   both useful since Archipelago is the only way into either without the game's own menus.

See [docs/check-economy.md](https://github.com/IanCichy/CultOfTheLamb_Archipelago/blob/main/docs/check-economy.md) for the full shape of the seed: every check
source, what it pays into, and what rerolls per-seed versus per-run.

## YAML options

`cult_of_the_lamb_quickstart.yaml` (in this repo) sets the handful of options that most change
the experience and leaves everything else at its default - the fastest way to a sane first seed.

`cult_of_the_lamb_example.yaml` (also in this repo) is the full annotated option list, one block
per system (goal, region access, weapons/curses, Divine Inspiration, sermons, Tarot, buildings,
pacing, filler), each with what it does and why the default is what it is.

Archipelago's own Launcher (**Generate Template Options**) also produces a template with every
option at its real default once the apworld is installed - the option best kept in sync with
whatever Archipelago version you're generating with.

## Verifying the install

Check `BepInEx/LogOutput.log` after connecting. A working install shows:

```
Archipelago.CultOfTheLamb v0.9.0 loaded.
...
[AP] Connected!
[AP] Versions: client 0.9.0, apworld 0.9.0
```

The two versions should match - a `MISMATCH` warning means your mod build and your apworld
build are out of step and one needs updating. If you instead see a line about **"Developer
debug keys are compiled into this build"**, you have a dev build rather than a release build;
get the release zip from the [releases page](https://github.com/IanCichy/CultOfTheLamb_Archipelago/releases)
instead.

## Known issues

- This is a first beta. If something looks wrong, it probably hasn't been seen yet rather than
  being a known, accepted issue - see [Reporting issues](#reporting-issues).

## Reporting issues

Open a [GitHub issue](https://github.com/IanCichy/CultOfTheLamb_Archipelago/issues) with your
`LogOutput.log` and, if it's seed-specific, your YAML. Reports from real multiworld sessions are
the most valuable thing this beta can get - "the checklist said one thing and the server said
another" or "this item never arrived" matter more than they might seem to.

## AI disclosure

- **This implementation is AI-assisted.** Most of the Python world was
  written by an LLM working in a directed loop with me.
- **No AI art.** The mod icon and all visuals are my own work or the game's own assets.
- **Game-API knowledge is verified against the source decompiled game DLLs**, cited by file:line

## Credits

Built by Ian Cichy.

## Third-party software

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net) -
  MIT
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) - MIT
- [BepInEx](https://github.com/BepInEx/BepInEx) - LGPL-2.1
- [HarmonyLib](https://github.com/pardeike/Harmony) - MIT

---

# Development

## Project Layout
- `Archipelago.CultOfTheLamb/` - c# client mod.
- `worlds/cult_of_the_lamb/` - Archipelago Python world.
- `docs/` - architecture notes and sprint docs.
- `lib/` - drop-in folder for third-party DLLs not on NuGet (e.g. COTL_API.dll).

## Building the C# Mod
1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases/latest) (x64) into your
   Cult of the Lamb install if you haven't already.
2. Copy `Archipelago.CultOfTheLamb/Directory.Build.props.default` to
   `Directory.Build.props.user` and point `GameFolder` at your local install
   (gitignored - this stays local).
3. Open `Archipelago.CultOfTheLamb.sln` and build. The post-build step copies the plugin
   into `<GameFolder>/BepInEx/plugins/Archipelago.CultOfTheLamb` automatically.

## Working on the AP World
`worlds/cult_of_the_lamb/` is a standard Archipelago world package. To test generation
against a full Archipelago checkout: copy (or symlink) the folder into that checkout's
`worlds/` directory, then run `Generate.py` with a matching player YAML. To package for
distribution: `py -3.12 build_apworld.py`.

Tests live in `worlds/cult_of_the_lamb/test/` and need that same checkout, since they run on
Archipelago's own `WorldTestBase`:

```
python -m unittest discover -s worlds/cult_of_the_lamb/test -t .
```

They cover the option matrix, and the invariant worth defending is that **every card the
seed manages has exactly one location** - a card with none can never be earned, and a card
with two pays twice for one action. Both failures have shipped before.

See [docs/architecture.md](https://github.com/IanCichy/CultOfTheLamb_Archipelago/blob/main/docs/architecture.md) for how the C# client and the Python world
fit together.

## License
MIT - see [LICENSE](LICENSE).
