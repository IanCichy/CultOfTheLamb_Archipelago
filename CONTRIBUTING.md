# Contributing

## Reporting a bug

**Press F9 in game first, then send the log.** F9 dumps the mod's state — what it thinks is
connected, which checks it has sent, which cards it manages — and a log containing one usually
answers everything that would otherwise take a conversation.

The log is at `<r2modman profile>/BepInEx/LogOutput.log`, and it is **wiped every time the game
launches**. Grab it before restarting.

Then open a [bug report](../../issues/new?template=bug_report.yml). The form asks for the version
and the log because those two are what make a report actionable.

### The mismatch that causes most confusing bugs

The **mod DLL and the apworld are a versioned pair.** The client prints both on connect:

```
[AP] Versions: client 0.9.0, apworld 0.9.0
```

If those differ, item names and location ids can disagree between the two halves, and the symptom
is silent — items simply stop applying, with no error. Check that line before assuming anything
else is wrong.

## Building the client

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases/latest) (x64) into your Cult of
   the Lamb install.
2. Copy `Archipelago.CultOfTheLamb/Directory.Build.props.default` to `Directory.Build.props.user`
   and point `GameFolder` at your install. **This file is gitignored** and stays local — it's the
   step most people miss.
3. `dotnet build Archipelago.CultOfTheLamb.sln --configuration Release`

The post-build step stages the package and zips it to `bin/zip/`.

**Close the game before building.** A running game holds the DLL open, so the copy fails — but the
*build* still succeeds, which makes it easy to think you deployed when you didn't. Check the
deployed file's timestamp.

## Working on the apworld

`worlds/cult_of_the_lamb/` is a standard Archipelago world package. To run the tests you need a
full Archipelago checkout, because they run on Archipelago's own `WorldTestBase`:

```
rm -rf   "$AP/worlds/cult_of_the_lamb"
cp -r    worlds/cult_of_the_lamb "$AP/worlds/cult_of_the_lamb"
cd "$AP" && py -3.12 -m unittest discover -s worlds/cult_of_the_lamb/test -t .
```

**The copy is not optional.** The checkout holds a *copy*, not a symlink, so running the tests
without syncing first passes against whatever code was there before. That has produced false green
runs more than once, and nothing catches it for you — there is no CI. If you work on this often,
replacing the copy with a directory symlink removes the failure mode entirely.

To package: `py -3.12 build_apworld.py`.

## What this mod is trying to be

Worth knowing before proposing a feature.

Cult of the Lamb is not a good randomizer, and this mod stopped trying to be one. Shuffling the
game aggressively makes it worse — the skill tree stops being a progression you shape and becomes
a slot machine. The aim is a good **crusade → base → crusade loop**. Randomization stays
available, never defaulted.

Anything that improves that loop beats anything that merely adds more content to shuffle.

## Conventions

- **Comments explain why, not what.** Keep `<summary>` to a sentence or two and put the longer
  explanation in `<remarks>`; long-form design rationale belongs in a design doc, not the source.
- **Never change a YAML option's name** once shipped — it silently breaks everyone's config.
- **Item and location ids are append-only.** Ids are positional, so inserting a row mid-table
  repoints everything after it.
- Slot data is the contract between the two halves. Prefer sending a value from the world over
  hardcoding it in the client, so drift fails loudly instead of silently.
