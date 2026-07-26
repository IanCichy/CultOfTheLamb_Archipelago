# Sprint 2 — Prove the remaining features with one debug build

**Written to survive a context reset.** A fresh session should be able to execute this
without re-deriving anything. Read the "Cold start" section first.

---

## Cold start (read this first if you have no context)

1. **Repo**: `C:\Users\IanCi\Repos\cult_of_the_lamb_archipelago`
   (GitHub: https://github.com/IanCichy/CultOfTheLamb_Archipelago). Commit directly to
   `main`, but **only after the user reviews the diff and approves**.
2. **Game internals knowledge**: invoke the `cotl-decompile-lookup` skill. It points at
   `C:\Users\IanCi\Repos\DecompiledGamesViaDnSpy\Cotl\AI_INDEX.md` (curated, verified
   findings with file:line cites) and `wiki/` (game names/lore). **Read AI_INDEX.md before
   grepping** — most of what you need is already there.
3. **Build/deploy/test**: invoke the `cotl-build-deploy-validate` skill. It has every path,
   the r2modman profile, the apworld pipeline, and the known traps.
4. **What already works**: see `docs/architecture.md` and `feature-viability.md` (same
   folder as this file).

## Where Sprint 1 left off

**Proven working in-game** (verified in a real session, not assumed):
- Region locking + unlocking via `DataManager.UnlockedDungeonDoor` and two
  `Interaction_BaseDungeonDoor` patches.
- Bishop-kill location checks via `Interaction_MonsterHeart.OnHeartTaken`.
- Full loop confirmed: kill Bishop → check sent → item received → next region unlocked.

**Built but never tested**: `Services/GoalService.cs` (victory reporting). It was awaiting
review when Sprint 1 ended — verify it's committed, and test it.

**Known gaps**:
- 15 of 20 locations send no checks (3 minibosses + Witness per region). **This is very
  likely solvable — an earlier claim that it was blocked was wrong.** `PlayerFarming.Location`
  only gives the region, but the game tracks bosses individually by *name*:
  - `DataManager.KilledBosses` is a `List<string>`; `CheckKilledBosses(name)` is just
    `KilledBosses.Contains(name)` (`DataManager.cs:2676`).
  - `FirstMiniBossIntro.cs:26` identifies a miniboss as
    `GetComponentInParent<MiniBossController>().name` — so **`MiniBossController.name` is
    the per-encounter identifier**, and `MiniBossController` is the class to hook.
  - Witnesses are tracked as `"Boss Beholder 1".."4"` (+ `_P2` variants), feeding
    `DataManager.BeatenWitnessDungeon1..4` (`DataManager.cs:740-743`) — so Witnesses are
    individually detectable too.
  - Approach: find what writes to `KilledBosses`, patch it (or diff the list on kill), and
    map the names to our location table. Confirm the actual miniboss name strings first —
    only the Beholder/Witness names are verified so far.
- Every item type except `Progressive Bishop's Domain` is inert.
- No in-game connect UI (F5 keybind + BepInEx config only).

---

## The slice: one research pass, then one debug build

**Rationale**: each feature is individually cheap to *test* but expensive to *build*. So
resolve all the API unknowns in one pass, then ship a single build with debug keybinds that
exercises every feature at once. The user tests them all in one sitting instead of doing a
build/test cycle per feature.

### Step 1 — Research pass (decompile only, no code)

Resolve these four unknowns. Record findings in `AI_INDEX.md` **as you go** (that file is
the durable memory; this plan is not).

| # | Unknown | Where to look | What we need |
|---|---|---|---|
| 1 | Sermon upgrade system | `UpgradeSystem` (referenced as `UpgradeSystem.UnlockAbility(UpgradeSystem.PrimaryRitual1)`); `CheatConsole.UnlockAllSermons()` (line ~1311) | The per-upgrade enum + `UnlockAbility`/`GetUnlocked` pair, and whether prerequisites are enforced |
| 2 | Tarot unlock API | `Lamb/UI/Tarot/Interaction_TarotCardUnlock.cs`, `Interaction_TarotForge.cs`, `Interaction_Tarot.cs`, `Interaction_TarotCard.cs`; types `TarotCards.TarotCard` / `TarotCards.Card` | How a single card is unlocked, and where a shop slot gets its card data |
| 3 | Fleece unlock API | `Data/Serialization/DungeonCompletedFleecesFormatter.cs`; `CheatConsole.GiveGhostFleeces()` (~line 899) | The fleece enum + per-fleece unlock call |
| 4 | Faith/devotion rate fields | `CultFaithManager`, `DataManager` | Which fields to scale for 2x/4x/6x pacing options |
| 5 | **Miniboss/Witness identity** (promoted — this unblocks 15 locations) | `MiniBossController`, `FirstMiniBossIntro.cs`, whatever writes `DataManager.KilledBosses` | The exact name strings for the 12 minibosses, and the best hook to catch a kill with its name |

### Step 2 — One debug build

Extend `Console/DebugCommands.cs` (already has the F5 connect / F9 dump-state pattern —
follow it). Add keybinds, each logging clearly to `[AP]`:

| Key | Action | Proves |
|---|---|---|
| F6 | `Inventory.AddItem` a few different `ITEM_TYPE`s | Resource filler items (lowest risk; API already confirmed by two other mods) |
| F7 | Unlock one named sermon upgrade (e.g. Weapon Mastery) | Sermon items — the biggest payoff feature |
| F8 | Unlock one named tarot card | Tarot items |
| F10 | Unlock one fleece | Fleece items |
| F11 | Show an AP notification | Notification pipeline (**see trap below**) |

**Notification trap — already diagnosed, don't rediscover it**:
`NotificationCentre.PlayGenericNotification(locKey, Flair)` takes an **I2 localization key,
not display text**. I2 returns `null` for unregistered terms with no fallback
(`LocalizationManager.cs:1019`) → **blank popup**. Both reference mods
(williambsm/COTL.Archipelago, firebirdjsb/cheat-menu-cotl) pass raw English and are likely
showing empty notifications. Fix: register the term at runtime first —
`LocalizationManager.Sources[0].AddTerm(key, eTermType.Text, SaveSource: false)` then
`TermData.SetTranslation(langIndex, text)` — then pass that key. `Flair` is
`None`/`Positive`/`Negative`/`Winter`. It dedupes by key within a frame, and every
notification is appended to `DataManager`'s notification history.

### Step 3 — User tests, then we decide

Whatever proves out becomes real AP items/locations in `worlds/cult_of_the_lamb/`. Whatever
doesn't gets dropped or rethought before we invest in it.

---

## After the slice

### Done
- ~~Miniboss + Witness checks~~ — shipped. `DataManager.AddKilledBoss(string)` is the single
  write site for `KilledBosses` and covers all 12 minibosses, 4 Witnesses and their `_P2`
  post-game variants. Verified end-to-end in-game. See `AI_INDEX.md` §3a.
- ~~Sermon upgrades~~ (Python side) — 38 upgrades, 32 base + 6 Woolhaven, verified against
  the game's own `UpgradePlayerConfiguration.AllUpgrades`. **C# side still to do**: grant on
  item receive, send check on sermon-bar fill, suppress the upgrade-choice screen.

### Next
1. **Tarot cards** — 85 in `DataManager.AllTrinkets` (46 realistically randomizable: minus 19
   Woolhaven, 5 co-op, 15 unlocked at game start). `TarotCards.UnlockTrinket(Card)` grants and
   raises the game's own alert for free. Blocked only on display names (F4 dump has them).
2. **AP logo in shop slots / marketplace.** Texture ready at `Assets/ap_icon.png` (converted
   from WebP — Unity's `Texture2D.LoadImage` only decodes PNG/JPG). Use
   `session.Locations.ScoutLocationsAsync()` for the remote item name/player. Real vendors
   exist: `MarketPlaceWeapons/Clothes/Chef/Animal/Cat/Spider`, `MarketplaceBlacksmith`,
   `MarketPlacePostGame`, plus `AnimalMarketplaceManager`. **Highest technical risk of
   anything remaining — worth prototyping early rather than late.**
3. **Follower recruitment milestones (1..20).** Cheapest check source left:
   `FollowerRecruit.OnFollowerRecruited` / `OnRecruitFinalised` are **public static events**,
   so no Harmony patch is needed. Must be high-water-mark ("recruited N total"), not current
   count — followers die, and a current-count check would become unreachable after a plague.
4. **Structure-built checks.** `Structures_BuildSite.OnBuildComplete` is a public `Action`
   and `StructureBrain.TYPES` says which structure. Needs curating: the enum includes scenery
   (`TREE`, `ROCK`, `POOP`) alongside real buildables.
5. **Doctrines** — ~48 base (6 categories x 8, ~10 levels each). `DoctrineUpgradeSystem`
   has the full unlock API; `GetSermonReward(category, level, firstChoice)` gives the two
   options offered per level.
6. **Achievements** — 48 enumerable via `AchievementsWrapper.Achievements`, queryable with
   `UnlockedAchievement()`. ~28 are event-shaped and make good checks; the ~12 "collect all
   X" ones are effectively goal-tier and the `*_NODAMAGE` ones are skill-gated, so both
   groups want a YAML toggle.
7. **Crown abilities** — 14, bought with Monster Hearts via
   `UIPlayerUpgradesMenuController.UpgradeItemSelected` -> `UnlockAbility`. Grapple Hook /
   Fishing Rod / Special Key are traversal, so these are the best `progression` candidates
   in the game.
8. **Fleeces** — 12 base / 15 Woolhaven. Proven working (there is no unlock API;
   `DataManager.UnlockedFleeces` is a plain `List<int>` of enum values).
9. Connect UI — plain BepInEx IMGUI panel. **Do not** try to reuse the cult-naming dialog:
   `CheatConsole.NameCult()` just sets `DataManager.Instance.OnboardedCultName = false` to
   re-trigger onboarding; it is not a reusable text input.
10. Pacing multipliers as `options.py` YAML settings (not AP items). `CultFaithManager.GetFaith`
    is the single choke point for faith; don't scale the stored value, it's clamped to [0, 85].

### Researched but rejected
- **"Complete path 1/2/3/4" per region** — redundant with the miniboss checks.
  `MiniBossManager` picks the encounter *by* dungeon layer (`:52`) and advances the layer *on*
  its death (`:327-330`), so layer N completing and miniboss N dying are the same event.
  Paired locations that can never be reached independently add no routing freedom.

### Open questions
- **"Broom upgrades"** (user request) — no such system found. The only matches in the whole
  assembly are two audio events (`broom_away_spin`, `broom_in_spin`); "sweep"/"cleaning" only
  turn up combat sweep attacks and follower cleaning tasks. Needs clarifying before it can be
  scoped.
- Which marketplace shape is wanted: purchases-as-checks (easy) vs AP items in shop slots
  (the item #2 above).

## Standing constraints

- Don't add `Co-Authored-By` or "Generated with Claude Code" to commits/PRs.
- Don't commit until the user has reviewed the diff and approved.
- Item/location ids in `Utilities/CultOfTheLambIds.cs` are hardcoded to mirror the Python
  side's offset scheme. They **will silently drift** if `items.py`/`locations.py` dict order
  changes. Adding a lot of items/locations is a good moment to replace this with a real AP
  datapackage name→id lookup. (The sermon feature avoids this entirely by sending the
  name→`UpgradeSystem.Type` mapping through `fill_slot_data` instead — prefer that pattern.)
- The project compiles against `CultOfTheLamb.GameLibs 1.4.6.596-r.0`, which is **older than
  the installed game**: e.g. `GameManager.DLCUpgradeTreeConfiguration` exists at runtime but
  not at compile time (reached by reflection in `DebugActions`). Bumping to `1.5.15.979-r.0`
  currently fails because Windows Defender blocks reading `Rewired_Core.dll` out of the NuGet
  cache as a false positive; needs a Defender exclusion before the bump can land.
