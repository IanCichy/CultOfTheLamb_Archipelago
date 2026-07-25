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

## After the slice (ordered, from `feature-viability.md`)

1. Sermon upgrades as ~35 locations + ~35 items — **the biggest single win**; roughly
   triples the location count from one system. Open design question: AP may grant e.g.
   "Sword Mastery" before "Weapon Mastery" — either mirror the prereq graph in `rules.py`
   or make them progressive items.
2. **Miniboss + Witness checks** — fills in the 15 locations that already exist in
   `locations.py` but send nothing. Now that per-boss names are known to exist (see "Known
   gaps"), this is probably the cheapest large win, not a blocker.
3. Tarot cards as locations + items. Shops are *fixed, identifiable* interactions, so this
   is still a good additional location source.
3. AP logo in shop slots. Texture is ready at
   `Archipelago.CultOfTheLamb/Assets/ap_icon.png` (converted from WebP — Unity's
   `Texture2D.LoadImage` only decodes PNG/JPG, so the original would have silently failed).
   Use `session.Locations.ScoutLocationsAsync()` for the remote item name/player.
4. Connect UI — build a plain BepInEx IMGUI panel. **Do not** try to reuse the cult-naming
   dialog: `CheatConsole.NameCult()` just sets `DataManager.Instance.OnboardedCultName =
   false` to re-trigger onboarding; it is not a reusable text input.
5. Pacing multipliers as `options.py` YAML settings (not AP items).

## Standing constraints

- Don't add `Co-Authored-By` or "Generated with Claude Code" to commits/PRs.
- Don't commit until the user has reviewed the diff and approved.
- Item/location ids in `Utilities/CultOfTheLambIds.cs` are hardcoded to mirror the Python
  side's offset scheme. They **will silently drift** if `items.py`/`locations.py` dict order
  changes. Adding a lot of items/locations is a good moment to replace this with a real AP
  datapackage name→id lookup.
