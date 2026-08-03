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

**112 locations** in a default all-options-on seed. Working end-to-end and verified in real
play:

| System | Locations | Items |
|---|---|---|
| Bishops, minibosses, Witnesses | 20 | — |
| Sermon upgrades | 32 (38 w/ DLC) | 30 (3 progressive chains) |
| Tarot cards | 27 (5 region-gated) | one per non-starting card |
| Tarot shop purchases | 16 | — |
| Follower milestones | 20 | — |
| Snail shrines | 5 | — |
| Region access | — | 3 progressive |
| Filler + traps | — | weighted pool |

Also done and **no longer on this roadmap**: the AP logo in shop slots (`ShopIconService`) and
the connect UI (F5 panel). Both were once separate sprints.

### Sphere structure

`Progressive Bishop's Domain` is still the world's **only** progression item — sermon upgrades
and tarot cards are both `useful`. Three progression items in 112 locations.

Two mechanisms keep the fill from burying a door key at the end of a grind:

- **Depth bands.** `rules.set_depth_rules` splits each "Cult"-resident block into four
  reachability bands (0/1/2/3 copies required) and marks the deepest `EXCLUDED`, so it stays
  reachable and checkable but can never hold anything important.
- **Real logic where we have it.** Cards tied to one crusade region live in that region
  (`REGION_TAROT_CARDS`), and post-game cards aren't created at all under a goal that doesn't
  reach the post-game (`POSTGAME_TAROT_CARDS`, `goal_reaches_postgame`).

Measured over three seeds: sphere 1 is 27 of 112 (was ~71 before any of this), excluded is 20
(18%), and door keys land on boss, shop and region-gated card locations rather than deep in a
grind block.

Bands are still an approximation. **The cure is more real gates — Sprint 1 below.**

Card difficulty data lives in `docs/tarot-difficulty.md`; `TAROT_TIERS` orders the remaining
Cult-resident cards so the bands mean something instead of keying off the game's enum order.

---

## Ordering principle: leverage, not content size

An earlier draft ordered by "how much does this add to a seed". Three pieces of work multiply
everything after them, and any sprint done before them pays full price and then gets reworked.

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

**Audit while you're here:** sermons are sequential, but if `SermonService` doesn't withhold
the upgrade on purchase then buying one both grants it *and* consumes a check, while AP's own
sermon items shrink the pool of unbought upgrades from the other side. With 32 locations and
~30 items that's the tarot arithmetic again. May already be a live bug.

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

## Sprint 7 — Structures and buildings

`Structures_BuildSite.OnBuildComplete` is a public `Action`; `StructureBrain.TYPES` says which
structure. Needs curating — the enum includes scenery (`TREE`, `ROCK`, `POOP`) alongside real
buildables, and overlaps the Divine Inspiration tree.

Also the **strongest artificial gate available**: withholding what the player can *build* gates
the whole cult-management loop, the way tarot withholding gates combat variety.

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

- No `Co-Authored-By` or "Generated with Claude Code" in commits/PRs.
- Don't commit until the diff has been reviewed and approved.
- The repo has a ruleset wanting PRs on `main`; pushes currently bypass it. Worth settling
  whether direct-to-main or PR-per-sprint is the intent.
