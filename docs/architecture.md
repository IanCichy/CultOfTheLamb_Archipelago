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
- `LocationCheckService` — **wired up.** Subscribes to `Patches/InteractionMonsterHeartPatch`'s
  `OnBossDefeated` event and sends a check for the 4 base Bishops (`Utilities/RegionMapping.cs`
  maps `FollowerLocation` → AP location id). The 3 minibosses and the Witness fight per
  region aren't disambiguated from `FollowerLocation` alone yet (it only identifies the
  region, not which specific encounter) — those location table entries have no send-check
  hook until a per-encounter identifier is found.
- `RegionUnlockService` — **wired up.** Writes directly to
  `DataManager.Instance.UnlockedDungeonDoor` (see
  `DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md` §3 - this is the real, confirmed unlock state,
  found by reading `Interaction_BaseDungeonDoor`). Region 0 in the seed's `regionOrder`
  unlocks on connect; each further region unlocks as a `Progressive Bishop's Domain` copy
  arrives, routed through `ArchipelagoItemLogicController.ApplyItem`.
- `Patches/InteractionMonsterHeartPatch.cs` — the one Harmony patch so far. Postfixes
  `Interaction_MonsterHeart.Start()` to subscribe to that instance's public `OnHeartTaken`
  event (fires right after the game itself records a boss kill) rather than patching the
  coroutine that raises it, which has no clean method boundary to hook.
- Known fragility: item/location ids in `Utilities/CultOfTheLambIds.cs` are hardcoded to
  match `items.py`/`locations.py`'s deterministic offset scheme (same pattern RoR2 uses) -
  they'll silently drift if either Python file's dict order changes. TODO: replace with a
  real AP datapackage name→id lookup.

## World Design: Region Access, Locations, Goal

**Region access (`items.py`, `rules.py`, `regions.py`)**: which of the four regions is free
at seed start is randomized per-seed (`CultOfTheLambWorld.generate_early` shuffles
`REGION_NAMES` into `world.region_order`), not hardcoded to Darkwood. The other three are
gated behind a single progressive item, `PROGRESSIVE_REGION_ACCESS` ("Progressive Bishop's
Domain") — the Nth copy received opens the Nth-still-locked region in that seed's random
order. This is the standard AP "Progressive X" pattern (fixed order decided once at
generation, not runtime randomness) rather than 3 separately-named "X Access" items.
`fill_slot_data()` sends `regionOrder` so the C# client knows which physical region to
force-open at each step.

**Locations (`locations.py`)**: each region path is 4 chunks - 3 regular crusades against a
named miniboss, then the Bishop crusade - plus a 5th bonus chunk, the Witness (a miniboss
fight that unlocks after the Bishop is defeated; part of the free "Relics of the Old Faith"
update, not the paid Woolhaven DLC, so it's included unconditionally). 5 checks × 4 regions
= 20 real, named locations - no more generic "Follower Rescue N" placeholders. All names and
groupings (which minibosses/Witness belong to which region) are confirmed independently
three ways: the decompiled `FollowerLocation` enum's `Dungeon{tier}_{region}` pattern, the
wiki's per-region boss rosters, and williambsm/COTL.Archipelago's own `Check` enum (see
`DecompiledGamesViaDnSpy/Cotl/wiki/bishops_regions_and_dlc.md`).

**Goal (`options.py`, `rules.py`)**: two-track threshold goal instead of a single binary
victory condition. `Goal` picks a track (`bishops` or `witnesses`); `RequiredCount` (1-4)
is how many of that track's four encounters must be reachable to win. `state.can_reach_location`
counting stays the "reaching implies completing" pattern from the original scaffold - no
synthetic "defeated" item needed.

## What's Real vs. Placeholder
Real, high-confidence facts, confirmed via `DecompiledGamesViaDnSpy/Cotl/wiki/`: the four
regions and their Bishops/minibosses/Witnesses (see Locations above); the five base weapon
names, five sample Tarot Card names, and two Relic names in `items.py`.

Still explicitly placeholder / needs verification before it's meaningful:
- Doctrine names (the wiki's Doctrines page wasn't sourced yet - see
  `wiki/cult_management.md`'s note on this) and base-game (non-DLC) Structure names
  (`StructureBrain.cs`'s full enum hasn't been read - DLC-specific structure names *are*
  known, see `AI_INDEX.md` §4, just not used in `items.py` yet since there's no Woolhaven
  option to gate them behind).
- All Harmony patch targets / COTL_API hook points in the C# services — nothing has been
  decompiled yet. Next step is dnSpy against
  `Cult of the Lamb_Data/Managed/Assembly-CSharp.dll` to find real hook points for: follower
  conversion, bishop/miniboss/Witness defeat, region-select gate (needed to force-open an
  arbitrary region, not just observe Darkwood), doctrine unlocks.
- The AP item id range (`offset = 3_050_000` in `items.py`) is arbitrary and unchecked
  against the AP world registry for collisions — fine for local testing, needs a real
  reservation before this is shared with other players.

## Generation Verified
This world has been run through the real Archipelago generator (`Generate.py`) end-to-end,
across multiple option combinations (both goal tracks, several `required_count` values,
`randomize_region_access` on/off), and produces valid seeds. Design bugs caught and fixed
along the way:
1. **Circular access deadlock**: originally all 4 regions required an access item, so
   nothing was reachable at game start to hold the very first progression item. Fixed by
   leaving one region (now randomized per-seed) ungated.
2. **Wrong tutorial region**: an early version hardcoded Anura as the ungated region with
   three of the four Bishop↔region pairings scrambled (only Anura↔Heket was right by
   chance). Fixed after wiki research confirmed the real story order, then generalized to
   "randomly pick one of the four" per the current design above.
3. **Invalid event-item pattern**: originally modeled "defeat this Bishop" as a `code=None`
   event item placed at a real (networked) location — AP requires event items' locations to
   *also* have `address=None`. Fixed by making Bishop/Witness fights ordinary checked
   locations and defining victory via `state.can_reach_location(...)` counting instead (the
   standard "reaching implies completing" AP pattern).

## COTL_API (optional future dependency)
Not referenced yet. `xhayper/COTL_API` provides `Custom*` systems (structures, tarot cards,
follower commands, objectives, rituals) that are safer integration points than raw Harmony
patches against private game internals. Not on NuGet — see `lib/README.md` for how to add
it once we actually need one of its systems.
