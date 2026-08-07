# Sprint Roadmap

**Written to survive a context reset.** Every sprint below records the hook points already
researched, so no session should have to re-derive them. Deep game-internals findings live in
`DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md` (code) and `.../wiki/` (names, lore, locations) —
read those before grepping.

One feature per sprint, and a sprint isn't done until it's **implemented, generating, and
verified in-game**.

## Target play session — read this before prioritising anything

**A default seed should run 4–6 hours**, with the goal set to N Bishops, N Witnesses, or the
vanilla final boss (Narinder).

Content which *enriches* a base-game run is worth more than content which *extends* it.
Woolhaven roughly doubles a seed to 12+ hours, so it stays behind `include_woolhaven` and is
deliberately last. The `_P2` post-game re-clears have the same problem.

---

## Where things stand

**121 locations** in a default all-options-on seed (147 with Woolhaven). Working end-to-end
and verified in real play:

| System | Locations | Items |
|---|---|---|
| Bishops, minibosses, Witnesses | 20 | — |
| Sermon upgrades | 32 (38 w/ DLC) | 32 (38 w/ DLC), 3 progressive chains |
| Tarot cards | 19 (5 region-gated) | one per non-starting card |
| Tarot shop purchases | 16 | — |
| Weapon families | 5 (6 w/ DLC) | one per non-starting family |
| Curse families | 4 | one per non-starting family |
| Follower milestones | 20 | — |
| Snail shrines | 5 | — |
| Region access | — | 3 progressive |
| Filler + traps | — | 42, weighted pool |

Location counts are what a default seed *creates*: cards and equipment families the player
starts with get neither a check nor an item, so `location_table`'s 131 base-game entries
become 121.

Also done and **no longer on this roadmap**: the AP logo in shop slots (`ShopIconService`) and
the connect UI (F5 panel). Both were once separate sprints.

### Sphere structure

Twelve progression items in 121 locations — but only three of them gate anything other than
themselves. Sprint 0d's nine weapon and curse items each unlock exactly one location (their
own), so `Progressive Bishop's Domain` is still the only item whose arrival changes what the
rest of the seed can reach. Sermon upgrades and tarot cards remain `useful`.

Two mechanisms keep the fill from burying a door key at the end of a grind:

- **Depth bands.** `rules.set_depth_rules` splits each "Cult"-resident block into four
  reachability bands (0/1/2/3 copies required) and marks the deepest `EXCLUDED`, so it stays
  reachable and checkable but can never hold anything important.
- **Real logic where we have it.** Cards tied to one crusade region live in that region
  (`REGION_TAROT_CARDS`); post-game cards aren't created at all under a goal that doesn't
  reach the post-game (`POSTGAME_TAROT_CARDS`, `goal_reaches_postgame`); and each weapon and
  curse check is gated on its own item (`set_equipment_rules`), so those nine skip the bands
  entirely.

Measured on a default seed: sphere 1 is 27 of 121 (was ~71 before any of this), excluded is 20
(17%), and door keys land on boss, shop and region-gated card locations rather than deep in a
grind block. Sprint 0d's nine locations added nothing to sphere 1 — every one of them is
behind its own item.

Bands are still an approximation. **The cure is more real gates — Sprint 1 below.**

Card difficulty data lives in `docs/tarot-difficulty.md`; `TAROT_TIERS` orders the remaining
Cult-resident cards so the bands mean something instead of keying off the game's enum order.

---

## Ordering principle: leverage, not content size

An earlier draft ordered by "how much does this add to a seed". Three pieces of work multiply
everything after them, and any sprint done before them pays full price and then gets reworked.

### Execution order — authoritative

Section numbers below are **stable identifiers, not the running order** (they'd churn every time
priorities move, and older notes reference them). This list is the order:

| # | Sprint | Why here |
|---|---|---|
| ~~1~~ | ~~**0b** — verify in-game~~ | ✅ done 2026-08-06 |
| ~~2~~ | ~~**0e step one** — dump the upgrade trees~~ | ✅ done 2026-08-06 — DI tree is 69 upgrades / 5 tiers, thresholds 0/4/10/20/25; **no tech→building cycle**, so 0e and 7 can gate in one seed |
| ~~3~~ | ~~**0d** — weapon and curse pools~~ | ✅ done 2026-08-06 — 7 weapons + 5 curses, filter-not-revoke. **Did not prove 0b**: it found the boundary where 0b doesn't apply |
| 4 | **0e** — Divine Inspiration tree | Biggest sphere source available; gates Sprint 7 |
| 5+ | 0c, 1, 2, 2b, 3, 3b, 4, 5, 7a, 7, 7b, 8, 9, 10, 11, 12 | as listed below |

**Why 0d and 0e were promoted out of the 7s** (2026-08-06): their old position was topical
adjacency to structures, not dependency. Neither depends on 0c, 1, 2, 3, 4 or 5. Two arguments
that moved them:

- ~~**0d is a better proof of 0b than fleeces.**~~ **Wrong, and 0d disproved it on contact.**
  The argument was that `GameManager.cs:109` refills an emptied weapon pool exactly like the
  tarot re-seed, so 0d would exercise the `Tick()` sweep. It refills it *far* more aggressively
  than that — the intro ladder hands the weapon back every run — which means revoking is the
  wrong mechanism here, not a mechanism needing a better sweep. 0d never instantiated
  `ManagedCollection`. Sprint 2 (fleeces) is the next real test of the seam.
- **0e may beat Sprint 1 on Sprint 1's own argument.** Sprint 1 is early because it takes the
  world from 3 progression items to 7 and makes spheres form naturally. `checks_and_techs` does
  that harder, with a count-based tier rule that is the game's own gating logic rather than an
  approximation of it.

Known cost of this order: **0c slips**, so 0d/0e get playtested in seeds that still have the
mismatched filler names. Annoying, not blocking. And **Sprint 2 loses its special claim** to its
position once 0d has proved the abstraction — it becomes an ordinary collection sprint.

## Sprint 0a — Sphere/exclusion framework ✅ done

Bands plus `LocationProgressType.EXCLUDED` on the deep tail. Every later block of locations
plugs into this instead of retrofitting it N times. See `rules.py` and `test/test_rules.py`.

## Sprint 0b — Shared managed-collection service

**The highest-leverage remaining work.** Every remaining collection sprint is the same shape —
fleeces, follower forms, doctrines, structures, outfits — and each must answer the same three
questions:

1. **Does an Archipelago grant close the gate its check rides on?** For tarot this cost a
   seed-breaking blocker. Confirmed identical shapes elsewhere: the broom outfit's
   `!UnlockedClothing.Contains(Special_4)` guard (`Interaction_Poop:433`), fleeces reading
   `UnlockedFleeces.Contains`, doctrines reading `GetUnlocked`.
2. **Does it edit real save data**, and so need revoke / restore / persist-across-crash?
3. **Named locations or sequential?**

Question 3 already has three different answers in-tree, which is the tell that it's an unmade
decision rather than three considered ones:

| System | Pattern | Revoke? |
|---|---|---|
| Sermons | sequential — `"Sermon Upgrade N"` (`locations.py:57-68`) | no |
| Tarot cards | named, withheld, shadow set (`TarotService.granted`) | yes — plus store and sweep |
| Tarot shop slots | location-state override on `TrinketUnlocked` | n/a |

Extract from `TarotService` while it's fresh: revoke/restore, `RevokedCardStore` persistence,
`MainThreadQueue` teardown, and the `Tick()` sweep that re-establishes the invariant.

### ✅ Done — extracted and verified in-game (2026-08-06)

Four new files, and `TarotService` drops from ~460 lines to ~240 by delegating:

| File | What it holds |
|---|---|
| `Utilities/SaveSlot.cs` | `SaveSlot.Current`, the DLC-slot fold. A fact about the game's saving, not about tarot. |
| `Utilities/ManagedCollectionStore.cs` | `RevokedCardStore` generalised over any enum, keyed `{collection}.save{N}`. |
| `Services/ManagedCollection.cs` | The state machine: `Begin`/`End`/`Grant`/`Tick`, debt persistence, save-switch handling, `SettleIfOwed`. |
| `Services/TarotCollectionBacking.cs` | Tarot's adapter onto `PlayerFoundTrinkets`. |

`IManagedBacking<T>` is the seam, and it is deliberately **two methods plus an availability
flag** — `Add`, `Remove`, `IsAvailable`. That's everything the sweep needs, which is what lets
the game's very different storage shapes plug in without special-casing: tarot's `List<Card>`,
fleeces' `List<int>` with no unlock API, doctrines' `DoctrineUpgradeSystem` methods.

`MainThreadQueue` needed no extraction — it was already general.

**Migration hazard, handled:** tarot's store rows were bare `saveN`. The new key is
`tarot.saveN`, so `Owed`/`Settle` take an optional `legacyKey` and fall back to the old row.
Without it, a player who updates mid-session is owed cards under a key nothing reads. Delete
that fallback at release.

**Verified in-game on a fresh save, seed `31646554891775684647`:** store writes on connect with
the namespaced key; `Revoked 15` / `62 managed, 8 granted at start`; granted cards visible on the
collection screen; **crash recovery** (`SettleIfOwed` returned 15 after a quit-to-desktop);
**the `Tick()` sweep**; and a clean disconnect restoring all 15 and settling the debt. Plus the
full multiworld loop — miniboss → check → item → region unlock.

**The sweep is invisible to the log and the store, and that cost three inconclusive attempts.**
`Tick()` skips cards already in `revoked`/`granted` before building its `reappeared` list, so its
log line only fires for cards it hasn't seen — which is never, in the normal re-seed case. The
store is `revoked ∪ granted` either way. Nothing observable distinguishes "swept" from "never
ran". Fixed by adding a tarot dump to **F9** (`DebugActions.DumpTarotState`) which prints
`PlayerFoundTrinkets` and an explicit `Invariant OK` / `INVARIANT BROKEN` verdict. Use that to
verify any future managed collection rather than reading the log.

**One design decision this run proved necessary rather than merely prudent:** `GameManager.cs:175`
re-seeds with `PlayerFoundTrinkets = new List<...>` — a **replacement**, not an `Add`.
`TarotCollectionBacking` reads the property on every call rather than caching the list, so the
sweep follows the new object. A cached reference would have swept a detached list forever while
the real collection filled with permanent unlocks, with every log line looking identical.
**Any future `IManagedBacking<T>` must read its collection through the property each call.**

**Audit done — sermons are clean.** The worry was that `SermonService` might not withhold the
upgrade, so filling the bar would both grant it *and* consume a check. It doesn't happen, for
two independent reasons, and both are worth copying rather than re-deriving:

- **The grant is suppressed at the source.** `SermonUpgradePatch.PlayerUpgrade_Prefix`
  (`:40-46`) returns `false`, replacing `SermonController.PlayerUpgrade()` wholesale — no
  Disciple Point, no tree menu, no pick. The replacement coroutine only increments
  `Doctrine_PlayerUpgrade_Level` and fires the check. The upgrade arrives *solely* as an AP item.
- **The counts can't drift.** Locations (`locations.py:68`) and item copies
  (`items.py:sermon_item_counts`) are both derived from `SERMON_UPGRADES` under the same DLC
  predicate: 38 entries, 32 base + 6 Woolhaven, exactly 1:1 either way. Progressive chains group
  entries under one name but preserve the count.

The general lesson for this sprint: **a prefix that replaces the whole reward step is cleaner
than withholding after the fact.** Tarot needed a shadow set, a revoke store and a sweep
precisely because it couldn't intercept there. Where a system has a single reward method,
patch that instead of reaching for the managed-collection machinery.

## Sprint 0d — Weapon and curse pools ✅ implemented 2026-08-06

Full design: `sprint-0d-equipment-pools.md`. Seven weapon families and five curse families,
two independent toggles plus a starting count each.

**The player already unlocks these** — it just doesn't feel like it, because the unlock ladder in
`GetRandomWeaponInPool` (`:2765-2796`) is automatic and front-loaded. A fresh save starts with
**Sword only** and **Fireball only**; the ladder then hands over Axe, Dagger, Gauntlet (≥1
Bishop), Hammer (≥2), Blunderbuss (≥3) and Chain (in `Dungeon1_5`) on a fixed schedule. This
sprint makes that schedule Archipelago's.

**It does not use Sprint 0b's machinery, and that's the finding.** Three things this sprint
proved, each of which contradicts what was written here before it was built:

- **Revoking doesn't work at all.** The plan was to empty `WeaponPool` the way `TarotService`
  empties `PlayerFoundTrinkets`. The ladder defeats that outright — it notices the missing weapon
  and force-feeds it back on the first floor of the next run. This isn't a `Tick()` sweep problem
  to solve; it's the wrong mechanism. **`ManagedCollection` is for collections whose membership
  is what's withheld. Here membership is the player's real progress, and what's withheld is what
  the game is willing to *offer*.** So: four postfixes filtering the selection, and the save is
  never written to.
- **`OnWeaponUnlocked` is not the location hook.** All four pickup sites `Add` to the list
  directly and bypass `DataManager.AddWeapon`, so the event never fires for them — it only covers
  the blacksmith's legendaries, Ratau's sword and the `Awake` seed. Checks fire on
  `PlayerWeapon.SetWeapon` / `PlayerSpells.SetSpell` instead, which is also the only hook that
  works on an established save, where every weapon is already in the pool.
- **Emptying the pool would have quietly deleted content.** `Interaction_Chest.cs:329` needs both
  pools above 2 to spawn its second weapon podium, `BiomeGenerator.cs:1459` needs them to sum
  above 3 to spawn a weapon room at all, and `AccessibilitySettings.cs:126` hides Force Weapon at
  a pool of 1. None of that is visible in a log; it reads as "this randomizer feels empty".

So **0d did not prove 0b on a second shape** — it found the boundary of where 0b applies. The
next real test of that abstraction is Sprint 2 (fleeces), which is back to being the cheap one.

Remaining hazard, now unreachable but worth keeping written down: **a zero-length pool crashes** —
both getters end with `if (list.Count <= 1) return list[0];`. `starting_weapons` and
`starting_curses` have `range_start = 1`, so the granted set can't be empty.

**Relics are already randomized** and need no work: they unlock in packs keyed to
`UpgradeSystem.Type`, five of which are already sermon items ("Eyes of the Lost Relics",
"Blessings of the Relics", "Damnation of the Relics", "Relics of the Freezing", "Relics of the
Burning"). Going per-relic (84 in `RelicType`, with `CoopRelics`/`MajorDLCRelics` as ready-made
exclusions) is a refinement, not a new capability.

Note a **shipped game bug** worth not tripping over: `DataManager.cs:2818-2822`, the
`Blacksmith_Legendary_Sword` branch removes `Hammer_Legendary` (copy-paste of the line above), so
`Sword_Legendary` is never filtered for a missing upgrade.

## Sprint 0e — Divine Inspiration tree (researched, not started)

Full design and every verified hook point: `sprint-0e-divine-inspiration.md`. **Two independent
YAML axes, every option implemented** — tree layout (`true_random` / `random_except_first` /
`default`) crossed with AP interaction (`checks_only` / `checks_and_points` / `checks_and_techs`
/ `off`). Any combination is a legal seed.

Numbered ahead of Sprint 7 because `StructuresData.GetUnlocked(TYPES)` maps every buildable to
an `UpgradeSystem.Type` — **the tree already gates what you can build**, so structure checks land
on top of tree logic rather than beside it.

Headline findings:

- **Tier gating is a cumulative count, not a prerequisite** (`UpgradeTreeNode.cs:296`). A node
  needs `NumUnlockedUpgrades() >= NumRequiredNodesForTier(tier)` — upgrades unlocked *anywhere*.
  That is `Tier N reachable <=> count >= K_N`, six clean spheres with no graph traversal, and a
  better sphere source than the depth bands because it is the game's own rule.
- The whole graph is one `UpgradeTreeConfiguration` ScriptableObject on a **public** `GameManager`
  field (`:1978-1984`) — and there are **three** trees sharing the class, one of which is the
  sermon tree we already randomize.
- `UpgradeSystem.AbilityPoints` is a static property with a **setter** — patch that, not
  `PlayerFarming.GetXP`, to catch every award path.
- `UpgradeSystem.OnAbilityUnlocked` is a public static `Action<Type>`: `checks_only` costs one
  event subscription and no Harmony patch.
- **`checks_and_points` can't support per-upgrade logic** — the player still picks what a point
  buys, so AP never knows which techs they hold. It conflicts with gating structure checks on
  `Building_*`. Resolve as an option interaction or forbid the combination.

Blocked on a runtime dump of the ScriptableObject (contents are Unity asset data, not in the
assembly) — but it is one object on a public field, so the debug command is step one and cheap.
## Sprint 0c — Filler and traps via `WorldManipulatorManager`

The game ships a curated, developer-balanced chaos system. Use it instead of hand-rolling.

```csharp
WorldManipulatorManager.TriggerManipulation(Manipulations m, float delay = 0f, bool twitch = false)  // :17
WorldManipulatorManager.GetLocalisation(Manipulations m)   // :832 — display name, free
WorldManipulatorManager.GetNotification(Manipulations m)   // :838 — notification text, free
```

~60 effects in the `Manipulations` enum (`:1689`), and the numeric bands *are* the taxonomy:

| Band | Kind | Examples |
|---|---|---|
| 0–99 | crusade, positive | `GainRandomHeart`, `HealHearts`, `GainTarot`, `InvincibleForTime`, `FreezeEnemies`, `NextChestGold` |
| 100–199 | crusade, negative | `TakeDamage`, `SpawnBombs`, `LoseAllSpecialHearts`, `LoseRelic`, `NoSpecialAttacks`, `IncreasedBossesHealth`, `NoHeartDrops` |
| 200–299 | base, positive | `GainFaith`, `FillDevotion`, `FillSermon`, `FollowerInstantlyLevelled`, `CropsInstantlyFertilised`, `ClearAllWaste`, `FreeRitual` |
| 300+ | base, negative | `AllFollowersPoopOrVomit`, `BreakAllBeds`, `SleepFollowers`, `ToiletsInstantlyFull`, `MealsVanish`, `FollowerLosesLevel`, `KillRandomFollower` |

Crucially it also ships **context filters** — `GetPossibleDungeonPositiveManipulations()` (`:844`),
`...DungeonNegative...` (`:886`), `...BasePositive...` (`:934`), `...BaseNegative...` (`:1122`) —
answering "is this valid right now?". That's the tedious half of trap design, already solved.

Why early: filler is roughly half of every seed, so this improves every seed regardless of what
content lands. It also fixes the "filler names don't match effects" debt below at the root —
impossible once the name comes from `GetLocalisation` on the effect actually fired.

Notes: resolve context at apply time and **queue** a mismatched effect for the next run rather
than dropping it (`ApNotification` can say it's pending). `AllFollowersPoopOrVomit` feeds
`itemsCleaned`, so a mess trap genuinely helps the broom checks — keep that tension. Decide
whether `TarotCards.Card.ImmuneToTraps` ("The Intangible") suppresses AP traps; recommend yes.

## Sprint 1 — Crown abilities + Narinder goal

**Moved up from late.** The only work that turns a 2–3 sphere world into a real one, and it
restructures a seed rather than lengthening it.

`Abilities_GrappleHook`, `Abilities_FishingRod`, `Abilities_SpecialKey`, `Abilities_Hunting`
are the only genuine traversal logic the game has, and they gate real world objects
(`Interaction_Grapple.cs`, `PickUpFishingRod.cs`). As progression items the world goes from 3
to 7, and spheres form naturally instead of being asserted by bands.

- Enum `CrownAbilities.TYPE`; bought at the Temple with Monster Hearts via
  `UIPlayerUpgradesMenuController.UpgradeItemSelected` → `UpgradeSystem.UnlockAbility`, with a
  `Cost.CanAfford()` guard in the same method — check and gate in one place.
- Display names via `CrownAbilities.LocalisedName(TYPE)`.

**Narinder as a goal option** — the vanilla ending and a natural 4–6 hour target.
`EnemyDeathCatBoss` is Narinder; `DataManager.DeathCatBeaten` is the flag. The goal, not a
gateway into post-game.

## Sprint 2 — Fleeces

Placed here not because fleeces matter most, but because it's the **smallest** collection and
therefore the cheapest proof that Sprint 0b's abstraction is right, before betting doctrines
(~48) and forms (~50) on it. Already proven live via F10.

- `PlayerFleeceManager.FleeceType`; **no unlock API** — `DataManager.UnlockedFleeces` is a plain
  `List<int>` of enum values. Guard with `Contains` first.
- 12 base, 15 Woolhaven (`WoolhavenPackFleeces`), 5 cosmetic-DLC (exclude).
- Locations: the fleece-purchase interactions (`Interaction_PurchasableFleece`).

## Sprint 2b — Milestones and side activities (was Sprint 6)

**Moved up from tenth.** Best value-per-effort on the board, and it exercises Sprint 0b's
service on a *second* shape — polling counters rather than a withheld collection — before
doctrines (~48) and forms (~50) commit to that abstraction. Sprint 2 proves the collection
path; this proves everything else routes through it too.

Shrunk: Sprint 0c already took the interesting half. What's left is **one polling service** in
the shape of `SnailShrineService.Tick()` over plain `int` fields on `DataManager` — no new
Harmony patches at all.

The best is `DataManager.KillsInGame` (`:4539`): incremented at exactly one site
(`Health.cs:1435`), **never reset**, so it's a lifetime count — and already shown to the player
on the Doctrine Cult page (`Lamb/UI/DoctrineCultPage.cs:32`), which makes it genuinely
targetable. Kill 50 / 100 / 250 / 500 / 1000.

Others in the same shape: `FishCaughtTotal` (`:6579`), `TotalBodiesHarvested` (`:4659`),
`sacrificesCompleted` (`:9190`), `weddingsPerformed` (`:8988`), `MissionariesCompleted`
(`:4671`), `ResurrectRitualCount` (`:8658`), `PuzzleRoomsCompleted` (`:5103`),
`NPCRescueRoomsCompleted` (`:8874`), `TotalFirefliesCaught`/`Squirrels`/`Birds`
(`:4647`/`:4651`/`:4655`). Per-enemy-type via `GetEnemiesKilled(Enemy)` (`:3551`).

**Broom**: `itemsCleaned` (`:9016`), `itemsCleanedNeeded = 200` (`:9019`), six increment sites
(`Interaction_DaycareClean:52`, `Interaction_Poop:368`, `Interaction_Outhouse:181`,
`Interaction_IceBlock:319`, `Interaction_IceSculpture:139`, `Vomit:184`). Milestones at
25/50/100/200. The vanilla >200 reward is `FollowerClothingType.Special_4` gated on
`TailorEnabled` — **that guard is the tarot blocker's exact shape**, so any outfit work applies
the Sprint 0b lesson from the start.

These belong in the deep/excluded bands by construction: AP treats reachable as achievable, so
a grind milestone must never hold someone's key item.

## Sprint 3 — Passive buffs (hearts)

Pure items, no locations — enriches the pool for every sprint after, and scales a run's
difficulty curve rather than lengthening the seed.

Run-start health is **not** computed at run start; `HealthPlayer.cs:158-171` reads it out of
persistent save fields. So "start every run with one more black heart" is a single float
increment, no hook required:

| Field (`DataManager.cs:10272-10312`) | Item |
|---|---|
| `PLAYER_BLACK_HEARTS` | Progressive Black Heart |
| `PLAYER_BLUE_HEARTS` | Progressive Blue Heart |
| `PLAYER_SPIRIT_TOTAL_HEARTS` | Progressive Spirit Heart |
| `PLAYER_FIRE_HEARTS` / `PLAYER_ICE_HEARTS` | elemental variants |
| `PLAYER_HEARTS_LEVEL` | red heart capacity |

`PLAYER_REMOVED_HEARTS` is the same mechanism in reverse and makes a real trap. These edit save
data, so they go through Sprint 0b's service.

## Sprint 3b — Crusade content variance (researched, not started)

Every seed plays the same four regions with the same enemy rosters. `region_access_order`
shuffles which door opens, never what is behind it. Pure enrichment — no new locations, no new
items, no added run time — which is exactly what the 4–6 h target asks for.

Full design and every verified hook point: `sprint-3b-crusade-variance.md`. Headline findings:

- **`Addr_StartPieces`, not `Addr_IslandPieces`, is the encounter pool** (`GenerateRoom.cs:1636`).
  The naming actively misleads and this fact is load-bearing for everything else.
- `Addressables_wrapper.InstantiateAsync`'s 4-arg overload (`:42`) is a **self-scoping**
  substitution hook — four call sites in the whole assembly, one of which is the encounter spawn,
  and membership in a harvested key set is the entire scoping mechanism. No shared-asset mutation,
  no iterator patching.
- **Woolhaven wolves are DLC-dungeon enemies, not Purgatory-exclusive.** There is no per-enemy
  "allowed here" check anywhere in the game.
- `DungeonSandboxManager.dungeons[]` is a complete biome→asset table for every biome, and
  `SetDungeonType` (`:123`) is the shipped proof that mid-run biome repointing is safe.

Blocked on a runtime asset dump — the encounter keys are serialized in Unity assets, not in the
assembly. The debug commands to produce it are step one, and they ship alongside two knobs that
need no asset knowledge at all (`DungeonUseAllLayers` early, per-region generation parameters).

## Sprint 4 — Doctrines

~48 base (6 categories × 8, ~10 levels each). Excludes the 15 Winter/Woolhaven entries and the
11 story-granted `Special_*`, which aren't player-chosen.

- `DoctrineUpgradeSystem.UnlockAbility(DoctrineType)` / `GetUnlocked` / `UnlockedUpgrades`.
- `GetSermonReward(SermonCategory, level, firstChoice)` gives the two options offered per level
  — that tier structure maps cleanly onto progressive items per category.
- Full enum in `AI_INDEX.md` §4a.

## Sprint 5 — Follower forms

~50 skins, with `DataManager.SetFollowerSkinUnlocked(string)` as a single static choke point —
the same system boss skins use (`AI_INDEX.md` §3a). `UIFollowerFormsMenuController` holds
per-region ordered arrays naming every one. Hub Follower Form booths sell them by region
category, so purchases are natural locations and `Interaction_BuyItem` already covers those.

## Sprint 6 — Milestones and side activities

**Moved up — now Sprint 2b, above.** Kept as a stub so older notes referring to "Sprint 6"
still resolve.


## Sprint 7 — Structures and buildings (researched, not started)

**Corrected hook**: `Structures_BuildSite.OnBuildComplete` is a **per-instance `Action` with no
arguments** — it says *that* something finished, not *what*. Use a Harmony postfix on
**`Structures_BuildSite.Build()`** (`:107`), where `this.Data.ToBuildType` gives the type. There
are **two** classes needing the patch: `Structures_BuildSite` and `Structures_BuildSiteProject`
(`:87`); miss the second and multi-stage projects never fire.

The decor filter already exists: `StructuresData.GetCategory(type) != StructureBrain.Categories.AESTHETIC`,
which is what both `Build()` methods already branch on. 17 categories total, so finer slicing is
available for free.

Counts, from `StructuresData.AllStructures` (`:9208`) — the curated buildable list, **not** the
raw 370-member `TYPES` enum:

| Set | Count |
|---|---|
| `AllStructures` | 332 |
| `DECORATION_*` | 207 |
| non-decoration | 125 |
| minus `TILE_*` flooring | −14 |
| minus Woolhaven/DLC | ~−25 |
| **base-game real buildings** | **~85** |

85 would nearly double a 121-location seed — the "extends rather than enriches" trap. But ~35 of
those are `_2`/`_II`/`_3` tiers of something (`BED`, `SHRINE`/`_II`/`_III`/`_IV`, `TEMPLE`,
`MISSIONARY`, `DEMON_SUMMONER`, plus a dozen simple pairs). **Collapse each family to a check on
its first tier and it lands at ~45–50** — a real sprint that doesn't bloat the seed. The upper
tiers make better progressive items than checks anyway.

First-build tracking exists **only for decorations**: `DataManager.HasBuiltDecoration` /
`SetBuiltDecoration` over `DecorationTypesBuilt` (`:6647`), a save-persisted `List<int>` — same
bare-list shape Sprint 0b's backing handles. Non-decor buildings have no such record, so AP keeps
its own set with catch-up derived from `StructureManager.GetAllStructuresOfType(...)`.

Also the **strongest artificial gate available**: withholding what the player can *build* gates
the whole cult-management loop, the way tarot withholding gates combat variety.


## Sprint 7a — Divine Inspiration tree

**Moved up — now Sprint 0e, above.** Kept as a stub so older notes referring to "Sprint 7a" still resolve.

## Sprint 7b — Weapon and curse pools

**Moved up — now Sprint 0d, above.** Kept as a stub so older notes referring to "Sprint 7b" still resolve.

## Sprint 8 — Shiny (levelled) tarot cards

The first item that makes *already-received* cards better — a different kind of reward from
another card. Level is rolled in `TarotCards.DrawRandomCard` (`:647-664`):

```csharp
if (DataManager.Instance.dungeonRun >= 5)
    while (Random.Range(0f, 1f) < 0.275f * DataManager.Instance.GetLuckMultiplier()) num++;
num = Mathf.Min(num, TarotCards.GetMaxTarotCardLevel(card));
```

Use a **postfix on `DrawRandomCard`** raising the returned level, still clamped by
`GetMaxTarotCardLevel`. Do *not* raise `DataManager.LuckMultiplier` (`:3564`) — it's shared with
`Interaction_CoinGamble`, `Interaction_BloodSacrafice` and `DropMultipleLootOnDeath`, so it
would silently buff gambling and loot too.

Mind the `dungeonRun >= 5` gate: levelled cards can't appear at all in the first four runs, so
an early copy does nothing unless the postfix bypasses it.

## Sprint 9 — DeathLink

Follows 0c because it needs the same "defer until the player is in the right context" machinery.

Detection is easy: `HealthPlayer.OnPlayerDied` is a public event (`Demon.cs:59`, `:124`), and
`PlayerFarming.Instance.health.OnDie` is the same one level down (`BountyRoomController.cs:26`).

Receiving needs a decision: in a crusade, death is meaningful and ends the run — that's what
other DeathLink games mean. In the base the player can't die at all, so **queue it for the next
run** rather than dropping it silently. With Sprint 3 landed, `PLAYER_REMOVED_HEARTS` is a
plausible lesser penalty for the base context.

Standard shape: `deathlink` YAML toggle → slot data → MultiClient.Net's `DeathLinkService`.
Guard the receive path against re-sending our own death.

## Sprint 10 — Achievements (toggleable)

48 enumerable via `AchievementsWrapper.Achievements`, queried with `UnlockedAchievement()`,
hookable at `UnlockAchievement()`. ~28 are event-shaped and make good checks. The ~12
"collect all X" ones are goal-tier and the `*_NODAMAGE` ones are skill-gated — both want their
own YAML toggle rather than being forced on everyone.

## Sprint 11 — Woolhaven and post-game (optional, behind the DLC toggle)

**Deliberately last.** Roughly doubles a seed to 12+ hours, so it's opt-in content.

Scope (~30 locations):
- **The Gateway / Narinder** — `EnemyDeathCatBoss`, uses `PlayerFarming.Location` generically
  rather than a fixed `Dungeon1_N`. `DataManager.DeathCatBeaten` is the state flag.
- **The 16 `_P2` post-game re-clears** — `AddKilledBoss` **already fires for these** and
  `LocationCheckService` currently logs and skips them (`BossKeyMapping.IsPostGameVariant`).
  Only locations are missing. Gate behind Narinder.
- **Woolhaven** — Marchosias (`EnemyWolfBoss` ↔ `Dungeon1_5`/`Boss_Wolf`) and Yngya
  (`EnemyYngyaBoss` ↔ `Dungeon1_6`/`Boss_Yngya`; her defeat adds *both* to `BossesCompleted`).
  Plus their Dungeon5/6 minibosses, which use separate `DLCDungeon5MiniBossIndex` /
  `DLCDungeon6MiniBossIndex` counters.

Needs new regions in `regions.py` with real gates: Gateway requires 4 Bishops, `_P2` requires
Narinder, Woolhaven requires DLC + post-game.

**Acceptance**: seeds show 4+ spheres; killing a `_P2` boss sends a check.

## Sprint 12 — Release prep

- **Gate the granting debug keys** (F6/F7/F8/F10) behind a config toggle defaulting to off —
  handing yourself items is cheating in a multiworld.
- Replace the hardcoded id scheme in `Utilities/CultOfTheLambIds.cs` with a real AP datapackage
  name→id lookup. It currently hardcodes ids 0–19 (bosses, minibosses, witnesses) and relies on
  those staying first in `location_table`. Sermons and tarot already avoid this by sending their
  mapping through `fill_slot_data` — prefer that pattern everywhere.
- Pick a real AP item/location id range (currently a placeholder, unchecked for collisions).
- README, setup guide, Thunderstore packaging.

---

## Pacing options (not sprints)

YAML settings rather than AP items.

**Faith** — `CultFaithManager.GetFaith(float Delta, float DeltaDisplay, bool Animate, ...)`
(`CultFaithManager.cs:224`) is the single choke point for **all** faith change. A Harmony prefix
scaling `Delta`/`DeltaDisplay` gives clean 2x/4x/6x pacing. **Don't scale the stored value** —
it's clamped to `[0, 85]`.

**Devotion / doctrine XP** — `DoctrineUpgradeSystem.Get/SetXPBySermon`, targets in
`DataManager.DoctrineTargetXP` / `PlayerUpgradeTargetXP`.

This is also the lever for "level followers faster". **Follower XP does not exist**:
`FollowerBrain.GetXP(float Delta)` is an empty method body, and follower levels are set directly
by the follower interaction (`interaction_FollowerInteraction.cs:1441`), which spends Devotion.
There is no XP curve to multiply.

Probably does more for the 4–6 hour target than any location change, since it compresses the
cult-management half of the game without removing it.

---

## Known issues / tech debt

- **Filler names don't match effects**: "Fervour" grants Coins (`BLACK_GOLD`), "Gold Tithe"
  grants Gold Nuggets. Leftover placeholder names with real effects attached. **Sprint 0c fixes
  this at the root** by taking names from `GetLocalisation`.
- **Snail shrine region-scoping unresolved.** All 5 sit in `Cult` (always reachable). The
  assumption was that 4 hide behind hub access, but F9 showed `ShrineNumber=1` in *Ratau's
  Home* — so they may not be one-per-hub at all. Needs F9 dumps from the four hubs before
  changing anything.
- **`Region locking active: True` prints twice per connect** — `ProcessLoginResult` appears to
  run twice. Harmless (idempotent) but not understood.
- **Tarot shop purchases have no catch-up on connect** — the game records them as
  `BuyEntry.Bought` on the shop prefab, unreachable unless standing in that hub.
- **Reloading an earlier autosave** leaves `AppliedItemStore`'s count ahead of what that save
  received, so those items are skipped. Better than duplication; matches most AP clients.
  `AppliedItemStore.BuildKey` also uses raw `SaveAndLoad.SAVE_SLOT`, which is unstable within a
  session — see `TarotService.CurrentSaveId` for the fold that fixes it.
- **Tarot reveal cutscene plays for cards you don't receive** — both the shop flow and
  `UICardManagerCard.UnlockCard()` animate before calling the unlock.
- **238 `NullReferenceException`s** in `PlayerFarming.Update()` during crusade room transitions,
  with zero Archipelago frames in any stack. Probably vanilla; needs a mod-off comparison.
- **Dead main-menu connect path** — `MenuButtonPatch.MainMenu_Start_Postfix` and
  `SettingsButtonField`. Only the pause-menu button ever worked.
- **Unbounded notification queue** (`ApNotification.cs:89`) — turning notifications off
  accumulates the whole session's backlog, which then fires in one frame.
- **Co-op is untested.** Every hook reads shared save state so it *should* work.
  `TarotCards.CoopCards` are already excluded from solo seeds.

---

## Standing constraints

- **Implement every option and let the YAML pick.** Where a feature has several defensible
  behaviours, ship them all as YAML values rather than choosing one — and where two option axes
  are genuinely independent, let any combination be a legal seed. Sprint 7a is the worked
  example: tree layout crossed with AP interaction, 3 × 4 combinations, all valid. The cost of
  the extra branches is usually small next to the cost of guessing which behaviour a player
  wants; the real work is making sure `rules.py` models each combination honestly, and saying so
  explicitly when one combination has to be forbidden.
- No `Co-Authored-By` or "Generated with Claude Code" in commits/PRs.
- Don't commit until the diff has been reviewed and approved.
- The repo has a ruleset wanting PRs on `main`; pushes currently bypass it. Worth settling
  whether direct-to-main or PR-per-sprint is the intent.
