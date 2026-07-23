# Architecture Notes

## Project Layout
- **C# mod**: `Archipelago.CultOfTheLamb/` — BepInEx 5 (Mono) plugin, client-side.
- **Python AP world**: `worlds/cult_of_the_lamb/` — Archipelago server-side world.
- Modeled after [ror2_archipelago_enhanced](https://github.com/IanCichy/ror2_archipelago_enhanced)
  (Services/-layer architecture, partial-class ArchipelagoClient, IService pattern). See that
  repo for a mature reference implementation of the same patterns.

## Build Target Decisions
- **TargetFramework: net472**, not netstandard2.1 (unlike the RoR2 mod). Matches
  [xhayper/COTL_API](https://github.com/xhayper/COTL_API), the community modding API for
  this game — chosen for ecosystem consistency, not because it's technically required.
  netstandard2.1 would very likely also work (Cult of the Lamb runs Unity 2021.3 LTS, same
  branch as RoR2, whose Mono build supports netstandard2.1). Revisit if net472 ever causes
  friction.
- **Not net5.0+/net10.0**: hard constraint, not a preference. BepInEx 5 loads the plugin DLL
  directly into the game's embedded Mono CLR (confirmed Mono, not IL2CPP: game install has
  `Assembly-CSharp.dll` + `MonoBleedingEdge/`, no `GameAssembly.dll`). Modern .NET
  (net5.0–net10.0) targets a different BCL (CoreCLR) that old embedded Mono can't resolve.
  BepInEx 6 supports modern .NET, but only via IL2CPP interop — a different mod
  architecture, moot here.
- **Game assembly reference**: `CultOfTheLamb.GameLibs` NuGet package from
  `nuget.bepinex.dev` (see `nuget.config`), not a local `Assembly-CSharp.dll` path. This is
  what COTL_API itself uses. Local `GameFolder` (via `Directory.Build.props.user`) is only
  needed for deploying the built plugin to `BepInEx/plugins`, not for compiling.

## No Multiplayer Sync Layer
The RoR2 mod has a `Network/` folder full of R2API `NetworkMessage` types because Risk of
Rain 2 is co-op and every client needs synced check/objective state. **Cult of the Lamb is
single-player only** — there is no equivalent need, so this project has no `Network/`
folder and no R2API-style dependency. If that ever changes, look at
`Archipelago.RiskOfRain2/Network/` for the pattern.

## Services Layer (IService pattern)
Same convention as RoR2: each cross-cutting concern is a class implementing
`Interfaces/IService.cs` (`Register()` / `Unregister()`), constructed and torn down by
`ArchipelagoClient` on connect/disconnect.
- `LocationCheckService` — reports AP location checks when in-game events happen. **Not
  wired to any real game hook yet** (TODO).
- `RegionUnlockService` — gates dungeon regions (Darkwood, Anura, Anchordeep, Silk Cradle)
  behind AP items, analogous to RoR2's `StageBlockerService`. **Not wired to the actual
  dungeon-select UI yet** (TODO).

## What's Real vs. Placeholder (as of initial scaffold)
Real, high-confidence facts baked into this scaffold, confirmed via
`DecompiledGamesViaDnSpy/Cotl/wiki/bishops_regions_and_dlc.md`:
- The four dungeon regions and their Bishops, in actual story order: Darkwood/Leshy →
  Anura/Heket → Anchordeep/Kallamar → Silk Cradle/Shamura.
- Darkwood is the tutorial region and is always reachable from the start — this isn't just
  flavor, it's load-bearing: the Archipelago fill algorithm needs at least one
  always-accessible location to place the first progression item into, or generation
  deadlocks. Verified by actually running `Generate.py` against this world (see below).
  (An earlier version of this scaffold incorrectly used Anura as the free region, with three
  of the four Bishop↔region pairings scrambled — fixed after the wiki research above
  confirmed the real story order.)

Also real now (added after wiki research, see `wiki/combat_items.md`): the five base
weapon names, five sample Tarot Card names, and two Relic names in `items.py`.

Still explicitly placeholder / needs verification before it's meaningful:
- Doctrine names (the wiki's Doctrines page wasn't sourced yet - see
  `wiki/cult_management.md`'s note on this) and base-game (non-DLC) Structure names
  (`StructureBrain.cs`'s full enum hasn't been read - DLC-specific structure names *are*
  known, see `AI_INDEX.md` §4, just not used in `items.py` yet since there's no Woolhaven
  option to gate them behind).
- "Follower Rescue N" locations - still a generic stand-in; crusades are procedurally
  generated per run, so there's no fixed rescue spot to name the way there is for the
  (real, one-time, named) Bishop and Witness fights.
- All Harmony patch targets / COTL_API hook points in the C# services — nothing has been
  decompiled yet. Next step is dnSpy against
  `Cult of the Lamb_Data/Managed/Assembly-CSharp.dll` to find real hook points for: follower
  conversion, bishop defeat, dungeon/crusade selection, doctrine unlocks.
- The AP item id range (`offset = 3_050_000` in `items.py`) is arbitrary and unchecked
  against the AP world registry for collisions — fine for local testing, needs a real
  reservation before this is shared with other players.

## Generation Verified
This world has been run through the real Archipelago generator (`Generate.py`) end-to-end
and produces a valid seed. That process caught two real design bugs before they shipped:
1. **Circular access deadlock**: originally all 4 regions required an access item, so
   nothing was reachable at game start to hold the very first progression item. Fixed by
   leaving Anura ungated (see above).
1a. **Wrong tutorial region**: originally used Anura as the ungated region with three of the
   four Bishop↔region pairings scrambled (only Anura↔Heket was right by chance). Fixed to
   Darkwood after wiki research confirmed the real story order (see above).
2. **Invalid event-item pattern**: originally modeled "defeat this Bishop" as a `code=None`
   event item placed at a real (networked) location — AP requires event items' locations to
   *also* have `address=None`. Fixed by making Bishop fights ordinary checked locations and
   defining victory via `state.can_reach_location(...)` on the four Bishop locations
   instead (the standard "reaching implies completing" AP pattern).

## COTL_API (optional future dependency)
Not referenced yet. `xhayper/COTL_API` provides `Custom*` systems (structures, tarot cards,
follower commands, objectives, rituals) that are safer integration points than raw Harmony
patches against private game internals. Not on NuGet — see `lib/README.md` for how to add
it once we actually need one of its systems.
