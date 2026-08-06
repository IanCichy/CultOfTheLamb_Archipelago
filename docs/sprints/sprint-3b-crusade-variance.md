# Sprint 3b — Crusade content variance

**Status: researched, not started.** Every hook point below is verified against the decompile
at `DecompiledGamesViaDnSpy/Cotl/Assembly-CSharp/`. Line numbers are from that export.

## Why

Every seed plays the same four crusade regions with the same enemy rosters. `region_access_order`
shuffles *which door opens when*, never what is behind it. Meanwhile `README.md:59` records that
the sphere structure is still an approximation because there are no real crusade-side gates, and
`README.md:11-18` sets the standard: content that **enriches** a base run beats content that
**extends** it.

Per-seed variance inside the four regions is the largest remaining enrichment that adds no
locations, no items, no logic, and no run time.

## Two corrections to the obvious framing

The feature was originally scoped as "use Purgatory to put Woolhaven wolves in early regions".
Both halves of that turned out to be wrong in useful ways.

**Wolves are not Purgatory-exclusive.** They are the Woolhaven **Major DLC** dungeon enemies for
`FollowerLocation.Dungeon1_5` / `Dungeon1_6` — `EnemySwordsmanWolf`, `EnemyWolfGuardian` (+
`_Axe`/`_Staff`/`_Sword`), `EnemyWolfGuardianMiniboss`, `EnemyWolfTurret`, `EnemyWolfBoss`,
`WolfMinibossScientistEnemy`, `WarriorTrioManager`. **There is no per-enemy "allowed here" check
anywhere in the assembly.** Gating is only:

1. which biome scene's asset list names them,
2. DLC ownership — `GameManager.AuthenticateMajorDLC()` (`GameManager.cs:371`) →
   `SteamApps.BIsSubscribedApp(3840050)` → `DataManager.Instance.MAJOR_DLC`, checked at the
   world-map/door level (`BiomeBaseManager.cs:1353`),
3. `GameManager.Layer2` (`GameManager.cs:1669`), which explicitly excludes DLC dungeons.

So putting a wolf in Darkwood is mechanically trivial. The hard part is knowing its asset key.

**Purgatory's value is not its enemies — it is `DungeonSandboxManager`.** Purgatory (internally
"Endless Mode" / "Dungeon Sandbox"; `Purgatory` appears in code only as a UI string and a
`TutorialTopic` value) is the shipped, QA'd proof that mid-run biome repointing is safe, and its
private `dungeons[]` array is a complete biome→asset table for every biome in one object. It is
the **harvest source**, not the feature.

---

## The generation chain

```
MapConfig → MapGenerator.GetMap() → Map of Node[]
  → MapManager.EnterNode(Node)                     per floor
    → BiomeGenerator (one per biome SCENE)         random-walks a grid of BiomeRoom
      → BiomeRoom.Activate() → GenerateRoom.Generate(seed, N,E,S,W)
        → IslandPiece[] chunks
          → IslandPiece.InitIsland() instantiates ONE Encounter prefab (Addressables key)
            → that prefab holds the actual enemy GameObjects
```

### `Addr_StartPieces` is the encounter pool, not `Addr_IslandPieces`

The single most load-bearing fact here, and the naming actively misleads.

`GenerateRoom.GetRandomEncounterIsland()` (`GenerateRoom.cs:1636-1676`) iterates
**`this.StartPieces`** looking for unused `Encounters`. `IslandPieces` (`Addr_IslandPieces`) feeds
`GetIslandListByDirection` — the connective path terrain, no encounters.

Confirmed from the other side by `DungeonSandboxManager.SetDungeonType` (`:125`):

```csharp
this.roomGenerator.Addr_StartPieces = this.GetIslandPieces(location);   // reads dungeon.Addr_IslandPieces
```

The Purgatory table's field *named* `Addr_IslandPieces` is each biome's **encounter-piece** list,
and `roomGenerator.Addr_IslandPieces` is never touched at all. Consequences: any hook that wants
encounters targets `InitializeStartPrefabs` (`:2878`), not `InitializeIslandPrefabs` (`:2890`);
and `DungeonSandboxManager.dungeons[].Addr_IslandPieces` is exactly the biome→encounter-roster
table this sprint needs harvested.

### Encounter selection — `MMRoomGeneration/IslandPiece.cs`

| Fact | Cite |
|---|---|
| `Encounters.ObjectList[].GameObjectPath` is already the friendly `Assets/....prefab` Addressables key | `:440`, `:453` |
| `Probability` is **ignored** by `InitIsland` — it picks `list[Seed.Next(0, list.Count)]`, uniform. Only `GetRandomWeightedIndex` (`:175`) honours it, and that path serves sprite shapes | `:267` |
| `AvailableOnLayer()` returns true immediately when `GameManager.DungeonUseAllLayers` | `:456-462` |
| `InitIsland` is a **compiler-generated iterator** — body patching would need `AccessTools.EnumeratorMoveNext` | `:205` |
| A spawned encounter's `SpriteShapeController`s are re-skinned with the **host** room's `DecorationList.SpriteShapeMaterial` — foreign encounters partly re-theme themselves for free | `:218-234` |

There is **no GUID→key resolution problem for encounters**. `GameObjectPath` is the string
`InstantiateAsync` consumes. That is what makes this whole sprint tractable.

### Determinism and the free event hooks

Generation is fully seeded: `BiomeGenerator.Seed` ← `DataManager.RandomSeed` → `BiomeRoom.Seed` →
`GenerateRoom.RandomSeed` → `IslandPiece.InitIsland(Seed)`.

`BiomeGenerator` exposes **public static events** — `OnBiomeGenerated`, `OnBiomeChangeRoom`,
`OnBiomeLeftRoom`, `OnRoomActive`, `OnBiomeEnteredCombatRoom` (`BiomeGenerator.cs:81-106`) —
subscribable with **no Harmony patch**. `EnemyEncounterChanceEvents.cs:14,40` is the game's own
worked example, including the `Random.InitState(BiomeGenerator.Instance.CurrentRoom.Seed)` idiom
for reproducible per-room randomness. Copy it.

### The `Enemy` enum is descriptive only

`Enemy.cs` is banded by biome — 100s Darkwood, 200s Anura, 300s Anchordeep, 400s Silk Cradle,
500s NG+/Woolhaven. It is a serialized field on the prefab (`UnitObject.EnemyType`, `:20`) used
for kill tracking (`DataManager.EnemiesKilled`), miniboss bookkeeping, and exactly one blacklist
(`EnemyEncounterChanceEvents.cs:210`). **Nothing selects a spawn from it.** Useful as a
classifier, useless as a lever.

---

## Purgatory as the harvest source

`DungeonSandboxManager.dungeons[]` (`:364`) is a private `Dungeon[]` of a private nested struct,
serialized in the "Dungeon Sandbox" scene. Per biome it holds `Location`, `Addr_Decorations`,
`Addr_IslandPieces`, `MiniBossRooms`, `LeaderRooms`, `EntranceRoom`, `TempleDoorRoom`,
`DisplayName`, three `BiomeLightingSettings`, `BiomeMusicPath`, `BiomeAtmosPath`. Reachable via
`AccessTools.Field(typeof(DungeonSandboxManager), "dungeons")`; the struct's fields are public,
so `GetFields(Public|Instance)` enumerates them.

`SetDungeonType(FollowerLocation)` (`:123`) moves **eleven coupled fields** atomically:
`roomGenerator.Addr_StartPieces`, `roomGenerator.Addr_DecorationSetList`, `PlayerFarming.Location`,
`biomeGenerator.DungeonLocation`, `BossRoomPath`, `LeaderRoomPath`, `EntranceRoomPath`,
`BossDoorRoomPath`, `DisplayName`, `biomeMusicPath`, `biomeAtmosPath` — plus `LightingManager`
settings and `DungeonLocationManager._location`. **That list is the spec** for any future "swap
the biome of a normal crusade" work.

`MapManager.EnterNode:167-186` is the Purgatory-only per-floor biome branch. Normal crusades have
no equivalent, because biome *is* the loaded Unity scene.

---

## Tiered options, ranked

| Tier | Mechanism | Verdict |
|---|---|---|
| — | Harvest/dump debug commands | **build first** — hard prerequisite for 1′ |
| 5 | `DungeonUseAllLayers` early | **build first** — XS effort, very low risk |
| 6 | Per-region generation-parameter shuffle | **build first**, two knobs only |
| 1′ | Encounter-key interception at `Addressables_wrapper.InstantiateAsync` | **build second** — the actual feature, and the wolf vector |
| 2 | Island-piece injection | hold as 1′'s geometry fallback only |
| 3 | Post-spawn enemy substitution | **drop** |
| 4 | Door-level biome shuffle | **drop** |

### Tier 1′ — the recommended substitution hook

`Addressables_wrapper.InstantiateAsync(object key, Transform parent, bool instantiateInWorldSpace,
Action<AsyncOperationHandle<GameObject>> callback)` (`Addressables_wrapper.cs:42-50`) is a plain
public static with `key` typed `object`. This overload has **exactly four call sites** in the
entire assembly:

| Call site | Instantiates |
|---|---|
| `IslandPiece.cs:273` | **encounters** ← ours |
| `BiomeGenerator.cs:1792` | custom rooms (miniboss / leader / entrance) |
| `PlayerRelic.cs:2231` | relic-summoned friendly |
| `PlayerRelic.cs:2261` | relic-summoned enemy |

A `[HarmonyPrefix]` taking `ref object key` that rewrites **only keys present in the harvested
encounter set** self-scopes by construction — the other three call sites cannot collide, because
their keys aren't in the set.

Why this beats rewriting `IslandPiece.Encounters` on the loaded prefabs:

- **No shared-Addressable mutation.** No pristine snapshot, no restore-on-disconnect, no
  idempotency window, no leak into Purgatory or into a biome that shares a piece prefab. That
  entire class of bug never exists.
- **Vanilla's anti-repeat logic stays intact.** `SetEncounterAsUsed(originalKey)` and
  `CurrentEncounter = originalKey` both run *before* the instantiate (`IslandPiece.cs:270-273`),
  so `BiomeGenerator.UsedEncounters` keeps operating on the pristine roster. You inherit vanilla's
  *variety structure* and substitute only the *content* — strictly better than rewriting the
  roster, which would collapse dedupe onto the substituted keys.
- **No iterator patching** anywhere in the design.

Selection should hash `(shuffleSeed, DungeonLocation, CurrentDungeonFloor, CurrentRoom.Seed,
originalKey, roomLocalCounter)`. Use **FNV-1a over UTF-8 bytes, not `string.GetHashCode`** — the
latter is not stable across .NET runtimes and would silently make the shuffle non-reproducible
between two players on the same seed. Reset `roomLocalCounter` on `OnBiomeChangeRoom`.

Pools should be built once at connect, per region, starting from that region's own pristine keys:
keep native keys weighted heavily (light ≈ 20 % foreign, standard ≈ 40 % — beyond that the regions
stop having identity); constrain candidates to encounters harvested from pieces with the same
connector signature (the cheapest available footprint proxy); exclude `NewGamePlus` keys unless
`GameManager.Layer2`; wolves only under the DLC gate below.

### Why drop tier 3 (post-spawn enemy substitution)

Destroying `UnitObject`s breaks live references held by `MiniBossController.EnemiesToTrack`,
`Health`'s static team lists, `SpiderNest`, `EnemyPetSummoner`, `EnemyBlueJellySpawner` — and it
races `EnemyEncounterChanceEvents` on the *same* `OnBiomeChangeRoom` event with undefined
ordering. You would also have to match archetype by hand or the encounter's spacing becomes
nonsense.

An Encounter prefab is authored as a **coherent group**: spacing, ranged/melee mix, shield
eligibility. Swapping the whole prefab preserves all of it for less work. Keep tier 3 in reserve
only as the fallback if harvest shows foreign encounters don't fit host island footprints.

### Why drop tier 4 (door-level biome shuffle)

`Interaction_BaseDungeonDoor.Location` (`:834`) is load-bearing for at least ten systems,
including this mod's own `RegionLockState` / `BaseDungeonDoorPatch`, `GetFloor(Location)` →
`GetDungeonLayer` / `DungeonCompleted` / `CachePreviousRun`, `GameManager.NewRun(_,_,Location)` →
`PlayerFarming.Location`, `Health.InitHP`'s bonus table, `DataManager.SaveKeyPieceFromLocation`,
`RegionMapping`'s Bishop-kill→check mapping, and the boss/miniboss room paths.

Moving **only** `SceneName` (`:837`) desyncs `Location` from `BiomeGenerator.DungeonLocation`,
which the loaded scene sets itself. You would stand in Anchordeep with
`PlayerFarming.Location == Dungeon1_1`: wrong HP table, Leshy's boss door rendered in Anchordeep
terrain, wrong key piece saved, and the Bishop-kill check resolving against a region you were
never in.

Purgatory gets away with this **only** because `SetDungeonType` moves all eleven coupled fields at
once. If this is ever wanted, the design is "apply a `SetDungeonType` equivalent on crusade scene
load" — its own sprint, with its own research.

---

## Tier 5 — layer-gate unlock

`Interaction_BaseDungeonDoor.GetFloor` (`:770-805`) already does:

```csharp
bool flag = num2 >= 4 || DataManager.Instance.DungeonCompleted(Location, false);
...
GameManager.DungeonUseAllLayers = flag;
if (flag) GameManager.CurrentDungeonLayer = 4;
```

So on *that* path the flag and layer 4 always travel together. But the combination
`DungeonUseAllLayers == true` at a **lower** layer is still vanilla-reachable, via
`Inteaction_DoorRoomDoor.cs:148-149` (sets the flag, then `NextDungeonLayer(num2)` without forcing
4) and `BiomeGenerator.cs:226-231` (sets the flag with `StartingLayer = 3`).

The flag has only three consumers:

| Consumer | Effect |
|---|---|
| `IslandPiece.AvailableOnLayer` (`:458`) | all layer-gated encounters become eligible ← the point |
| `EquipmentManager.cs:225` | re-enables the `TeleportToBoss` / `RandomTeleport` relics |
| `MiniBossManager.cs:29` | `ForcedIndex = 3` instead of 2 when `CurrentDungeonLayer == 3` |

Setting it early is therefore cheap and near-free in risk — but **verify layer 1 + flag in-game**
rather than assuming it, since that specific pairing isn't one vanilla produces on the door path.

**Do not touch `CurrentDungeonLayer`.** It drives `MapConfig.DungeonLength` (`:49`) → run length,
`MapGenerator.cs:677`, `DecorationPercentageSelector.cs:20`, `DungeonLayerActivator`,
`Interaction_TempleBossDoor.cs:231`, and the `DataManager.DungeonLayer1..5` save flags. **Layer
flag is variance; layer number is progression.**

Hook: `[HarmonyPostfix]` on `Interaction_BaseDungeonDoor.GetFloor` — public static, plain, called
from exactly one place (`OnTriggerEnter2D`, `:763`) immediately before `GameManager.NewRun`.

---

## Tier 6 — generation-parameter shuffle, two knobs only

Hook: `[HarmonyPrefix]` on `GenerateRoom.Generate(int, ConnectionTypes×4)` (`:763`, public,
plain). Apply **per room generator** — `GenerateRoom.Instance` is reassigned per room in
`OnEnable` (`:614`), so a once-per-scene application would be wrong.

| Knob | Cite | Clamp | Why |
|---|---|---|---|
| `GenerateRoom.EncounterWillBeEnemyOrResource` | `:2982` | **0.35 – 0.70** | below 0.35 the crusade is a resource walk; above 0.70 it's a slog that eats the 4–6 h budget |
| `BiomeGenerator.HumanoidHealthMultiplier` | `:3566` | **0.85 – 1.25** | read by ~16 enemy classes in their own `Start()` (`EnemyArcher:19`, `EnemySwordsman:31`, `EnemySwordsmanWolf:33`, …), so set it on `OnBiomeGenerated` — before any enemy spawns |

**Rejected knobs:** `NumberOfRooms` (changes run length — direct violation of the 4–6 h
constraint) and `CreateOnlyOneEncounterRoom` (binary, strictly removes content).
`CreateRandomExtraPaths` (`:2978`) is mild and optional; leave it for later.

---

## Harvest tooling

Three debug commands in `Console/DebugActions.cs`, registered in `Console/DebugCommands.cs` via
`BindFeatureKey`. F1–F4, F6–F8, F10, F11 are bound and F5 is the connect panel, so use F9 plus
modified shortcuts. `Newtonsoft.Json 13.0.3` is already a `PackageReference` and already ships
beside the DLL (`Archipelago.CultOfTheLamb.csproj:20,44`) — the existing dumps are tab-separated
`.txt`, but this table is nested, so JSON earns its keep here.

**`DumpBiomeContent()`** — run inside a crusade. Reads `GenerateRoom.Instance` and
`BiomeGenerator.Instance`:

- context: `DungeonLocation`, `CurrentDungeonLayer`, `DungeonUseAllLayers`, `GameManager.Layer2`,
  `NumberOfRooms`, `HumanoidHealthMultiplier`, and the three `GenerateRoom` knobs;
- per piece in `StartPieces` / `IslandPieces` / `ResourcePieces`: `name`, connector counts per
  direction (`GetConnectorsDirection(...)` — the footprint proxy), each `Encounters.ObjectList`
  entry's `GameObjectPath` / `Probability` / `NewGamePlus` / `LayerOne..LayerFour`, and the
  `SpriteShapes` / `SpriteShapes2` paths.

Guard on `GenerateRoom.Instance == null` and bail with a log — touching `.StartPieces` forces
`InitializeStartPrefabs()` → `WaitForCompletion()` synchronous Addressables loads (`:2914-2933`).
Inside a live crusade they're already resident, so it's cheap; outside one it is not.

**`DumpPurgatoryDungeons()`** — run in Purgatory. Reflects `dungeons[]` per the recipe above; for
each `Addr_IslandPieces` entry, `Addressables.LoadAssetAsync<GameObject>(r).WaitForCompletion()`
→ `GetComponent<IslandPiece>()` → dump its encounter keys and connector signature → `Release`.
**Wrap each entry in try/catch** — a throw here on a non-DLC install is itself the answer to
whether `Dungeon1_5`/`1_6` are in the array.

**`DumpLiveRoom()`** — the verification instrument, not a harvest. Logs
`GenerateRoom.Instance.Pieces[i].CurrentEncounter` (the game reads this itself at
`HealthPlayer.cs:221`, so it's known-good) plus every `Team2` `UnitObject`'s `EnemyType` and world
position. This is what proves geometry fit in slice 2.

Output merges into one `ap_biome_content.json` under `Paths.BepInExRootPath`, keyed by
`FollowerLocation`, so four separate crusades accumulate into one file. Commit the result to
`docs/` as reference data.

---

## Decisions to record now

### Balance — `Health.InitHP`

`Health.cs:392-466` applies a flat per-biome HP bonus keyed on `PlayerFarming.Location` — the
**host** biome:

| Biome | Bonus | Plus, once that biome's boss is beaten |
|---|---|---|
| `Dungeon1_1` | +0 | +4 |
| `Dungeon1_2` | +2 | +4 |
| `Dungeon1_3` | +4 | +4 |
| `Dungeon1_4` | +6 | +4 |
| `Dungeon1_5` / `1_6` | +3 | +5 |

So a Silk Cradle enemy in Darkwood gets +0 instead of +6 — *under*-tuned, an annoyance, not a
wall. A wolf in Darkwood loses its +3. The genuinely risky direction is the reverse: a Darkwood
mob in Silk Cradle picking up +6 and becoming spongy trash.

**Ship with no compensation and measure.** If it reads badly, add an opt-in `Health.InitHP`
postfix that uses `Enemy`-enum band mismatch as the "was this substituted" test (vanilla rooms are
almost entirely native-band, so the condition is self-selecting) and honours the existing
`Health.IgnoreLocationHPBuff` escape hatch. **Reject** scaling `DataManager.EnemyHealthMultiplier`
or `DifficultyManager.GetEnemyHealthMultiplier()` — both are global and hit bosses.

Note that tier 6's `HumanoidHealthMultiplier` stacks multiplicatively on top of `InitHP`, in each
enemy's own `Start()`. A wolf in Darkwood inherits Darkwood's randomized humanoid scalar. Fine,
but log it during testing so surprises are attributable.

### DLC safety

```csharp
bool wolvesAllowed =
    slotData.crusadeWolfEnemies
    && DataManager.Instance != null && DataManager.Instance.MAJOR_DLC
    && GameManager.AuthenticateMajorDLC();
```

`MAJOR_DLC` is a **save** flag (set `DataManager.cs:4356`, cleared `:3803` and
`GameManager.cs:536`), so it can read true on a save opened without the DLC — which is exactly why
`AuthenticateMajorDLC()` must be ANDed in.

Then the real gate: a **catalog existence probe on every key in every pool**, not just wolf keys —
`Addressables.LoadResourceLocationsAsync(key, typeof(GameObject))`, `WaitForCompletion()`, check
`Status == Succeeded && Result.Count > 0`, `Release`. Drop failures permanently for the session and
log once. Probing everything also catches game-patch drift and a stale `ap_biome_content.json`,
which are the same failure with the same consequence.

### Soft-lock risk — the one that matters

If a substituted key is invalid, Unity Addressables can throw **synchronously** out of
`InstantiateAsync`, killing `InitIsland`'s coroutine so its `completeCallback` never fires;
`GenerateRoom.DisableIslands` never completes and `BiomeGenerator.ChangeRoomRoutine` (`:2007`) is
itself blocked on `while (!CurrentRoom.generateRoom.GeneratedDecorations)`. Unrecoverable without
alt-F4.

Three mitigations, **all three shipped in the same PR as any substitution**:

1. catalog probe at connect (above);
2. hard allowlist — pools contain only harvested keys, never constructed ones;
3. a watchdog on `OnBiomeChangeRoom` that checks `CurrentRoom.generateRoom.GeneratedDecorations`
   after ~15 s and, if still false, logs loudly, notifies via `ApNotification`, and disables the
   service for the rest of the session.

Plus a BepInEx config kill switch (`Config.Bind("Crusade", "VarianceOverride", …)`) independent of
slot data. Non-negotiable: a player must be able to escape a bad seed without regenerating it.

Secondary failure: an encounter lands off-island → enemies unreachable → the room never clears
(`BiomeGenerator.cs:2521` waits on the room's `UnitObject`s). Mitigated by the connector-signature
constraint. The game ships its own escape at `BiomeGenerator.cs:2505` (`TurnEnemyIntoCritter`) —
instrument whether it fires, since that's the empirical signal.

### Seeding — send the seed, not the table

Slot data carries a 32-bit `crusadeShuffleSeed` drawn from `self.random` (AP's per-slot seeded
RNG, so it's reproducible from AP seed + slot and survives regeneration) plus the intensity enums.
The client combines that with its **locally harvested** key table.

This deliberately inverts the rule at `__init__.py:225-229` ("send mappings through slot data"),
and the reason matters: location and item ids are **Archipelago-owned**, so slot data is their
canonical home. Addressables keys are **game-owned** — they drift with every game patch and differ
between DLC and non-DLC installs. Putting them in the apworld would bake a silently-staling
snapshot of game assets into the Python package, which is the exact class of bug the original rule
exists to prevent.

This is only safe because **no location is gated on which enemies appear.** A client whose table
differs (different game version, DLC vs. not) gets a different mix, never an unbeatable seed. That
is the load-bearing justification; if this feature ever gates a check, the rule flips back.

### Options sketch

- `crusade_variance` — Choice: `off` / `light` / `standard`, default `light`.
- `crusade_wolf_enemies` — Toggle, default off; forced false when `include_woolhaven` is false.

Both cosmetic. No `rules.py`, `regions.py`, or `locations.py` change; no new locations or items.

---

## Sequencing

1. **Slice 1** — harvest commands + tier 5 + tier 6 + a `CrusadeVarianceService : IService`
   (copy `RegionUnlockService`'s slot-data + static-flag shape, and its `Unregister()` contract of
   restoring vanilla state on disconnect) + the `crusade_variance` option + kill switch. Ships
   felt variance with zero asset-key knowledge.
2. **Between sprints** — four crusades, one per region, plus one Purgatory run, to produce
   `ap_biome_content.json`. This is where the open questions below get answered by evidence rather
   than by reasoning.
3. **Slice 2** — tier 1′ + watchdog + catalog probe + wolf gate + `crusade_wolf_enemies`.
4. **Only if slice 2 shows misfit** — revisit tier 2 (inject the foreign `IslandPiece` *with* its
   encounter, so geometry travels together). Not before.

Slice 1 acceptance: four crusades on one seed vs. the same four with variance off show measurably
different encounter mixes; the log shows encounters whose `LayerOne == false` appearing on layer 1;
**run length unchanged**; no stuck rooms; `ap_biome_content.json` covers all four regions.

## Open questions

Serialized in Unity assets, not in the assembly — not answerable by reading code:

- The literal encounter prefab keys per biome.
- Whether `DungeonSandboxManager.dungeons[]` contains `Dungeon1_5` / `Dungeon1_6` entries. This
  decides whether wolves can appear in Purgatory at all, and whether the Purgatory dump alone is
  enough to source them.
- Whether foreign-biome encounters geometrically fit host island pieces.

## Relationship to other sprints

- **Sprint 0c** (traps via `WorldManipulatorManager`) is the other in-run system. No overlap:
  0c fires discrete effects, this changes what generates. `DungeonModifier.cs` — the game's own
  per-floor run-variance ScriptableObjects — is unclaimed by both and is a candidate for whichever
  lands second.
- **Sprint 11** (Woolhaven) owns Woolhaven *as regions*. This sprint borrows its *enemies* into the
  base game, behind the same `include_woolhaven` gate, and does not depend on Sprint 11 landing.
- Does **not** contradict the rejection at `sprint-2-feature-slice.md:156-160` ("Complete path N"
  locations, rejected as redundant with miniboss checks) — this proposal adds no locations.
