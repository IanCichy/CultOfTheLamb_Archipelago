# Sprint Roadmap

**Written to survive a context reset.** Every sprint below records the hook points already
researched, so no session should have to re-derive them. Deep game-internals findings live in
`DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md` (code) and `.../wiki/` (names, lore, locations) —
read those before grepping.

One feature per sprint, and a sprint isn't done until it's **implemented, generating, and
verified in-game**. The Sprint 2 pattern worked: research everything first, ship one debug
build that exercises the feature, then test.

---

## Where things stand

97 locations, ~2–3 spheres. Working end-to-end and verified in a real playthrough:

| System | Locations | Items |
|---|---|---|
| Bishops, minibosses, Witnesses | 20 | — |
| Sermon upgrades | 38 | 30 (3 progressive chains) |
| Follower milestones | 20 | — |
| Tarot shop purchases | 14 | — |
| Snail shrines | 5 | — |
| Region access | — | 3 progressive |
| Filler + traps | — | weighted pool, real effects |

**The structural problem**: `Progressive Bishop's Domain` is the world's *only* progression
item, so sphere depth is capped at 2–3 no matter how many locations get added. Sprints 3 and 4
exist to fix that; everything after is content.

---

## Sprint 3 — Endgame & Woolhaven regions

**Why first**: the only remaining work that adds *late-game structure* rather than more
sphere-1 locations. Everything else piles into the early game.

Scope (~30 locations):
- **The Gateway / Narinder** — `EnemyDeathCatBoss`, uses `PlayerFarming.Location` generically
  rather than a fixed `Dungeon1_N`. `DataManager.DeathCatBeaten` is the state flag.
- **The 16 `_P2` post-game re-clears** — `AddKilledBoss` **already fires for these** and
  `LocationCheckService` currently logs and skips them (`BossKeyMapping.IsPostGameVariant`).
  Only locations are missing. Gate behind Narinder.
- **Woolhaven** — Marchosias (`EnemyWolfBoss` ↔ `Dungeon1_5`/`Boss_Wolf`) and Yngya
  (`EnemyYngyaBoss` ↔ `Dungeon1_6`/`Boss_Yngya`; her defeat adds *both* to `BossesCompleted`).
  Plus their Dungeon5/6 minibosses, which use separate `DLCDungeon5MiniBossIndex` /
  `DLCDungeon6MiniBossIndex` counters. Behind `include_woolhaven`.

Needs new regions in `regions.py` with real gates: Gateway requires 4 Bishops, `_P2` requires
Narinder, Woolhaven requires DLC + post-game.

**Acceptance**: seeds show 4+ spheres; killing a `_P2` boss sends a check.

---

## Sprint 4 — Crown abilities as progression

**Why**: 14 items, and Grapple Hook / Fishing Rod / Special Key are *traversal* — the only
genuine logic gates the game has. This is the highest-leverage change for sphere depth, and
the first time `rules.py` gets to express something other than region access.

- Enum: `CrownAbilities.TYPE` (14 values, listed in `AI_INDEX.md`).
- Bought at the Temple with Monster Hearts via
  `UIPlayerUpgradesMenuController.UpgradeItemSelected` → `UpgradeSystem.UnlockAbility`, with a
  `Cost.CanAfford()` guard right there — so both the check and the gate live in one method.
- Display names via `CrownAbilities.LocalisedName(TYPE)`.

Open design question: which abilities actually gate which content. Needs a pass through the
region/dungeon requirements before writing rules — don't guess.

---

## Sprint 5 — Tarot cards (full system)

**Biggest content add available.** Fully researched; nothing left to discover.

- `DataManager.AllTrinkets` is the real pool: **85 cards**.
- Realistically randomizable **~46**: minus 19 Woolhaven (`MajorDLCCards`), 5 co-op
  (`CoopCards`), 15 already unlocked at start (`DefaultCards`).
- Grant: `TarotCards.UnlockTrinket(Card)` — proven, and it queues the game's own
  card-unlocked alert for free.
- Display names are already dumped (F4). They differ wildly from enum names — "The Burning
  Dead" is `Skull`, "The Path" is `MovementSpeed`. **Never guess these.**

Design decision still open: what the *locations* are. The 14 shop purchases already exist;
the other ~30 need a source (first-find in a crusade is the obvious one, but see the softlock
note in the Sprint 2 doc about decoupling find-events from grants).

**This sprint is finalization + testing, not research.**

---

## Sprint 6 — Doctrines

~48 base (6 categories × 8, ~10 levels each), plus 15 Winter/Woolhaven and 11 story-granted
`Special_*` which are **not** player-chosen and should be excluded.

- `DoctrineUpgradeSystem.UnlockAbility(DoctrineType)` / `GetUnlocked` / `UnlockedUpgrades`.
- `GetSermonReward(SermonCategory, level, firstChoice)` gives the two options offered per
  level — the tier structure maps cleanly to progressive items per category.
- Full enum listed in `AI_INDEX.md` §4a.

---

## Sprint 7 — Fleeces

Small and already proven (F10 grants one live).

- `PlayerFleeceManager.FleeceType`; **no unlock API** — `DataManager.UnlockedFleeces` is a
  plain `List<int>` of enum values. Guard with `Contains` first.
- 12 base, 15 Woolhaven (`WoolhavenPackFleeces`), 5 cosmetic-DLC (exclude).
- Locations: the fleece-purchase interactions (`Interaction_PurchasableFleece`).

---

## Sprint 8 — Follower forms

~50 skins, and `DataManager.SetFollowerSkinUnlocked(string)` is a single static choke point —
the same system boss skins use (see `AI_INDEX.md` §3a). `UIFollowerFormsMenuController` has
per-region ordered arrays naming every one.

Hub Follower Form booths sell them by region category, so shop purchases are natural
locations — and `Interaction_BuyItem` already covers those, no new hook needed.

---

## Sprint 9 — Achievements (toggleable)

48 enumerable via `AchievementsWrapper.Achievements`, queried with `UnlockedAchievement()`,
hookable at `UnlockAchievement()`.

~28 are event-shaped and make good checks. The ~12 "collect all X" ones are effectively
goal-tier and the `*_NODAMAGE` ones are skill-gated — both groups want their own YAML toggle
rather than being forced on everyone.

---

## Sprint 10 — Structures & buildings

`Structures_BuildSite.OnBuildComplete` is a public `Action`, `StructureBrain.TYPES` says which
structure. Needs curating: the enum includes scenery (`TREE`, `ROCK`, `POOP`) alongside real
buildables, and overlaps the Divine Inspiration tree — avoid double-counting.

---

## Sprint 11 — Milestones & side activities

Cheap, plentiful, and good "something to do while stuck" checks:
- Each ritual performed (~38, enumerable)
- Fish species (`FISH_ALL_TYPES` implies an enumerable set), Knucklebones opponents, recipes
- `DataManager.itemsCleaned` — the broom/cleaning counter, single field, 6 increment sites
- Time/economy milestones (day 10/25/50, 666 gold, temple/shrine fully upgraded)
- Golden statue converter at Midas's Cave — exactly 4 uses
- Donation well unlock

Caution: AP treats "reachable" as "achievable", so keep grind milestones shallow.

---

## Sprint 12 — AP logo in shop slots

Presentation, deferred deliberately — it's the only remaining feature with real technical risk.

- `Interaction_BuyItem` drives every shop; `BuyEntry` says what a slot holds.
- Icon path: `InventoryItemDisplay.SetImage(...)` for item/decoration slots. **Tarot slots
  render card art, not item icons** — likely a different path, needs checking.
- Texture ready at `Assets/ap_icon.png` (converted from WebP; Unity's `Texture2D.LoadImage`
  only decodes PNG/JPG).
- `session.Locations.ScoutLocationsAsync()` for the remote item name — **async against a
  synchronous UI**, so either pre-scout on connect or show a placeholder and update.

---

## Sprint 13 — Connect UI

Plain BepInEx IMGUI panel. **Do not** try to reuse the cult-naming dialog:
`CheatConsole.NameCult()` just sets `OnboardedCultName = false` to re-trigger onboarding; it
is not a reusable text input.

Also fold in a real in-game view of owned sermon upgrades — currently F2, log-only, because
the game's own tree menu **cannot be opened safely** (`UIUpgradePlayerTreeMenuController`
overrides `OnCancelButtonInput()` with an empty body; the only exit is buying something).

---

## Sprint 14 — Pacing & QoL options

YAML settings, not AP items:
- Faith rate — `CultFaithManager.GetFaith(Delta, ...)` is the single choke point. **Don't
  scale the stored value**, it's clamped to `[0, 85]`.
- Devotion/doctrine XP — `DoctrineUpgradeSystem.Get/SetXPBySermon`, targets in
  `DataManager.DoctrineTargetXP` / `PlayerUpgradeTargetXP`.

---

## Sprint 15 — Release prep

- **Gate the granting debug keys** (F6/F7/F8/F10) behind a config toggle defaulting to off —
  handing yourself items is cheating in a multiworld.
- Replace the hardcoded id scheme in `Utilities/CultOfTheLambIds.cs` with a real AP
  datapackage name→id lookup. Sermons already avoid this by sending the mapping through
  `fill_slot_data` — prefer that pattern everywhere.
- Pick a real AP item/location id range (currently a placeholder, unchecked for collisions).
- README, setup guide, Thunderstore packaging.

---

## Known issues / tech debt

- **Filler names don't match effects**: "Fervour" grants Coins (`BLACK_GOLD`), "Gold Tithe"
  grants Gold Nuggets. Leftover placeholder names with real effects attached. Renaming now is
  cheap; after seeds exist it isn't.
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
- **Co-op is untested.** Every hook reads shared save state so it *should* work, but
  `TarotCards.CoopCards` are in `AllTrinkets` and would need excluding from solo seeds.

---

## Standing constraints

- No `Co-Authored-By` or "Generated with Claude Code" in commits/PRs.
- Don't commit until the diff has been reviewed and approved.
- The repo has a ruleset wanting PRs on `main`; pushes currently bypass it. Worth settling
  whether direct-to-main or PR-per-sprint is the intent.
