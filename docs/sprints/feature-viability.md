# Feature Viability Plan

Goal: prove each proposed feature is *technically possible* with a minimal test before
building it properly. Ordered by (value x confidence) / effort.

Legend for **API status**:
- **PROVEN** — already working in our mod, verified in-game
- **CONFIRMED** — exact API found in the decompile or a working third-party mod, not yet run by us
- **UNKNOWN** — needs a research pass before we can estimate

---

## Already proven in-game (Sprint 1, done)

| Feature | AP role | API | Status |
|---|---|---|---|
| Region lock/unlock | Item (`Progressive Bishop's Domain`) | `DataManager.UnlockedDungeonDoor` + `Interaction_BaseDungeonDoor` patches | PROVEN |
| Bishop kills | Location (4) | `Interaction_MonsterHeart.OnHeartTaken` + `PlayerFarming.Location` | PROVEN |
| Goal / victory | — | `StatusUpdatePacket{ClientGoal}` | Built, untested |

---

## 1. Resources as filler items — LOWEST RISK, do first

The filler pool: seeds, plants, currency, building materials, food.

- **AP role**: filler items (the bulk of the pool).
- **API**: `Inventory.AddItem(InventoryItem.ITEM_TYPE, qty)` — **CONFIRMED** (used by both
  williambsm and firebirdjsb).
- **Risk**: very low. The enum is large and already exists; no new UI or hooks.
- **Test**: bind a debug key that calls `Inventory.AddItem` for 3-4 different ITEM_TYPEs and
  confirm they land in the inventory. ~30 minutes.
- **Note**: this also unblocks *every* other feature, since filler is what fills a pool once
  real items run out.

## 2. Tarot cards — locations AND items

The design the player asked for, and the standard AP shop pattern.

- **AP role**: two-sided.
  - Buying/collecting a tarot card slot = a **location** (sends a check).
  - A tarot card unlock = an **item** (received from any world; only then is it usable).
- **API**:
  - Types `TarotCards.TarotCard` and `TarotCards.Card` — **CONFIRMED** to exist
    (`CheatConsole.RunTrinket` has an overload for each).
  - `CheatConsole.AllTrinkets()` / `ImplementedTrinkets()` / `EnableTarot()` exist —
    semantics **UNKNOWN**.
  - The per-card *unlock* API and the shop slot's data source are **UNKNOWN**.
  - Candidate hook files: `Lamb/UI/Tarot/Interaction_TarotCardUnlock.cs`,
    `Interaction_TarotForge.cs`, `Interaction_Tarot.cs`, `Interaction_TarotCard.cs`.
- **Risk**: medium. Two unknowns (unlock API, shop slot rendering).
- **Why it may be *easier* than crusade minibosses**: shops are fixed, identifiable
  locations. Crusade minibosses still can't be disambiguated (`FollowerLocation` only tells
  us the region, not which encounter) - which is exactly why 15 of our 20 locations don't
  send checks yet. Tarot shops may be the better location source.
- **Test**: (a) read the 4 candidate files for the unlock API; (b) debug-key call it to
  unlock one specific card and confirm it appears; (c) patch the shop interaction and log
  when a card slot is bought.

## 3. Sermon upgrade tree — the big location/item source

~35 upgrades across 6 tiers + Woolhaven, with a real prerequisite graph (see the wiki dump
the player pasted: Hearts of the Faithful, Might of the Devout I-VII, weapon families
Bane/Vampiric/Necromantic/Zealous/Merciless/Godly, Curse families, the 6 weapon Masteries,
Relic unlocks).

- **AP role**: ideal two-sided fit.
  - Each sermon upgrade node = a **location** (you perform the sermon, it sends a check).
  - Each upgrade = an **item** (you don't choose it - AP grants it).
- **Why this is the most valuable target**: it's the single biggest well-defined,
  individually-named, non-procedural progression set in the game. ~35 locations + ~35 items
  from one system, versus 20 total today.
- **API**: `CheatConsole.UnlockAllSermons()` **CONFIRMED to exist**; the underlying
  per-upgrade enum/API is **UNKNOWN**. `UpgradeSystem.UnlockAbility(UpgradeSystem.PrimaryRitual1)`
  appears in firebirdjsb's doctrine code, so an `UpgradeSystem` with per-ability unlocks
  very likely exists - needs confirming.
- **Risk**: medium. High payoff.
- **Test**: find `UpgradeSystem`, confirm a per-upgrade enum + `UnlockAbility`/`GetUnlocked`
  pair, then debug-key unlock a single named upgrade (e.g. Weapon Mastery) and verify it
  takes effect in a crusade.
- **Design note**: the prerequisite graph matters for AP logic - if AP grants "Sword
  Mastery" before "Weapon Mastery", does the game handle it? Either mirror the prereqs as AP
  rules in `rules.py`, or make them progressive items. Needs a decision.

## 4. Fleeces — items

- **AP role**: items (unlocks).
- **API**: `CheatConsole.GiveGhostFleeces()` **CONFIRMED to exist**;
  `Data/Serialization/DungeonCompletedFleecesFormatter.cs` implies fleeces are tracked in
  save data as a set. Per-fleece unlock API **UNKNOWN**.
- **Risk**: low-medium. Small, well-bounded set (~15 fleeces incl. Woolhaven).
- **Test**: find the fleece enum + unlock call, debug-key one, confirm it's selectable.

## 5. AP logo in shops — presentation, high "feels real" value

The standard AP pattern: a shop slot shows the AP logo plus the remote item's name and
owning player.

- **Pieces needed**:
  1. Texture load — `Assets/ap_icon.png` **DONE** (converted from the WebP the player
     provided; Unity's `Texture2D.LoadImage` only decodes PNG/JPG, so the original WebP
     would have silently failed).
  2. Shop slot icon swap — **UNKNOWN**, needs the shop UI class.
  3. Remote item name/player — `session.Locations.ScoutLocationsAsync()` (standard AP client
     API, **CONFIRMED** available in MultiClient.Net).
- **Risk**: medium; depends entirely on how moddable the shop UI is.
- **Dependency**: only meaningful once shops are actual AP locations (feature 2).

## 6. In-game notifications for AP events

- **API**: `NotificationCentre.Instance.PlayGenericNotification(locKey, Flair)` — **CONFIRMED**,
  with `Flair` = None/Positive/Negative/Winter for styling.
- **Trap already found**: it takes an **I2 localization key, not display text**. I2's
  `GetTranslation` returns `null` for unregistered terms with no fallback
  (`LocalizationManager.cs:1019`), so passing raw English renders a **blank popup**. Both
  reference mods do exactly this and are likely showing empty notifications. Fix: register
  the string as a term at runtime via `LocalizationManager.Sources[0].AddTerm(key,
  eTermType.Text, SaveSource:false)` + `SetTranslation(...)` before showing it.
- **Other gotchas**: dedupes by key within a frame; every notification is appended to
  `DataManager`'s notification history (would pollute the player's log if spammed).
- **Risk**: low, now that the trap is known.

## 7. Connect UI

- Currently F5 keybind + BepInEx config file only.
- **Investigated and rejected**: reusing the cult-naming dialog. `CheatConsole.NameCult()`
  is just `DataManager.Instance.OnboardedCultName = false` - it re-triggers the *onboarding*
  naming sequence, not a reusable text-input dialog.
- **Recommended**: a plain BepInEx IMGUI (`OnGUI`) panel for host/port/slot/password. That's
  what most BepInEx mods do, it's fully under our control, and firebirdjsb's cheat menu
  proves IMGUI overlays work fine in this game. Skip trying to reuse game UI.
- **Risk**: low, but it's real work.

## 8. Pacing / QoL options (faith gen 2x/4x/6x, etc.)

- **AP role**: **not** items or locations - these are `options.py` YAML settings, applied
  client-side from slot data.
- **Why they matter**: an AP run touching this much of the game is long. Multipliers on
  faith/devotion/resource generation make seeds tractable and testing far faster.
- **API**: **UNKNOWN** which fields to scale - likely `CultFaithManager` and/or
  `DataManager` rate fields.
- **Risk**: low-medium. Also directly useful to *us* for testing.

---

## Suggested order

1. **Resources filler** (proves item-granting broadly, lowest risk, unblocks pools)
2. **Sermon upgrades** (biggest payoff; ~35 locations + items from one system)
3. **Tarot cards** (locations + items; likely better location source than minibosses)
4. **Notifications** (cheap now that the I2 trap is known; makes everything legible)
5. **AP logo in shops** (after 3 makes shops real locations)
6. **Connect UI** (needed before anyone else can play it)
7. **QoL multipliers** (helps testing, do opportunistically)

**One research pass covers most unknowns**: `UpgradeSystem` (sermons), the tarot unlock API,
the fleece enum, and the shop UI class. Worth doing as a single focused session against the
decompile, then a single debug-keybind build that exercises all of them at once - the player
can then test every feature in one sitting rather than one build per feature.
