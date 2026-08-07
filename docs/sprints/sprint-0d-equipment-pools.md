# Sprint 0d — Weapon and curse pools

**Status: implemented.** Every hook point below was read directly from the decompile on
2026-08-06; citations are in `DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md` §4-i.

Answers the question that started this: *"is it possible to restrict a player's pool of
weapons and curses, and make those unlocks? The player doesn't normally unlock them though."*

**They do unlock them.** See "The introductory ladder" below — that is the single most
important finding in this sprint, and it changes the whole design.

---

## The introductory ladder is the game's own unlock mechanism

`DataManager.GetRandomWeaponInPool` (`DataManager.cs:2765-2796`) opens with a hardcoded
ladder. On the first floor of any run after your first, before it rolls anything random, it
checks whether you own each weapon in a fixed order and hands over the first one you don't:

| Order | Weapon | Extra condition |
|---|---|---|
| 1 | Axe | — |
| 2 | Dagger | — |
| 3 | Hammer | `BossesCompleted.Count >= 2` |
| 4 | Gauntlet | `BossesCompleted.Count >= 1` |
| 5 | Blunderbuss | `BossesCompleted.Count >= 3` |
| 6 | Chain (Flail) | `PlayerFarming.Location == Dungeon1_5` (Woolhaven) |

`GetRandomCurseInPool` (`:2967-2982`) has the same shape for Tentacles, EnemyBlast,
ProjectileAOE and MegaSlash.

So weapons *are* progression in the vanilla game — just deterministic, unskippable
progression that nobody thinks of as an unlock because it can't be missed. That is exactly
the kind of thing a randomizer wants.

It also means the naive approach is actively wrong. Emptying `WeaponPool` the way
`TarotService` empties `PlayerFoundTrinkets` doesn't withhold a weapon: the ladder notices
it's missing and force-feeds it back in the first room of the very next run. You would be
fighting the game every crusade.

---

## Design: filter the selection, never touch the save

**`WeaponPool` and `CursePool` are never written to.** This sprint does not use
`ManagedCollection` at all, and that is a deliberate finding rather than an oversight — the
0b machinery exists for collections whose *membership* is the thing being withheld. Here
membership is the player's real progress and the thing being withheld is what the game is
willing to *offer*.

Not touching save data buys a lot for free:

- No revoke, no restore, no sidecar store, no crash recovery, no disconnect symmetry.
- No `[Key(N)]` serialization concerns.
- Three pieces of count-sensitive game code keep seeing honest numbers:
  - `Interaction_Chest.cs:329` spawns *two* weapon podiums instead of one only when
    `WeaponPool.Count > 2 && CursePool.Count > 2`.
  - `BiomeGenerator.cs:1459` only spawns a weapon room when
    `WeaponPool.Count + CursePool.Count > 3`.
  - `AccessibilitySettings.cs:126` hides the Force Weapon setting when `WeaponPool.Count == 1`.

  A revoke-based design would have silently turned off the weapon room and halved chest
  rewards for the first several hours of every seed. That is the kind of bug that reads as
  "this randomizer feels empty" and never gets diagnosed.
- **It works on an existing save.** Nothing has to be a fresh file.

Instead, four postfixes filter what the game is allowed to pick:

| Patch | Site |
|---|---|
| `DataManager.GetRandomWeaponInPool` | every podium, chest and choice room (`Interaction_WeaponSelectionPodium.cs:403/414`, `Interaction_WeaponChoiceChest.cs:71`, `Interaction_WeaponChoice.cs:32`) |
| `DataManager.GetRandomCurseInPool` | the same three, curse side (`:504`, `:143`, `:83`) |
| `FoundItemPickUp.GetWeapon` | the only site that indexes `WeaponPool` directly (`:118`) |
| `FoundItemPickUp.GetCurse` | ditto (`:148/151`) |

### The substitution rule

A postfix rather than a prefix, because `GetRandomWeaponInPool` carries a lot of behaviour
worth keeping: legendary gating, the Fervour lockout, `ForcedStartingWeapon`, the Cowboy
fleece's blunderbuss-only branch, the accessibility Force Weapon setting. Replacing it
wholesale would mean reimplementing all of that.

Given the game's own pick, `EquipmentPoolFilter`:

1. If the pick's primary type is granted, keep it. Variants ride along with their primary,
   so a Bane Axe still appears once the Axe is granted.
2. Otherwise, prefer a granted primary the player doesn't own yet. **This is what preserves
   the ceremony**: receiving *Apostate's Cleaver* means the next run's first room hands you
   the Axe with the game's own unlock animation and jingle, exactly as vanilla does — because
   the ladder was already going to offer something there, and we redirect it to the weapon
   Archipelago actually granted.
3. Otherwise, a random granted entry that's really in the pool.
4. Failing everything, Sword / Fireball.

Steps 3 and 4 exist because `GetRandomWeaponInPool` ends in `list[0]` on a `Count <= 1` list
(`:2816`, `:2985`) — an empty pool is an `IndexOutOfRangeException`, not a graceful
degradation. We never empty it, and `starting_weapons` / `starting_curses` have
`range_start = 1`, so the grant set is non-empty by construction. The fallback is belt and
braces.

### Primary type from the enum value

`EquipmentType` is banded: Sword 0, Axe 100, Hammer 200, Dagger 300, Gauntlet 400,
Blunderbuss 450, Shield 460, Chain 470, Tentacles 500, EnemyBlast 600, ProjectileAOE 700,
Fireball 800, MegaSlash 900, Teleport 1000, Barrier 1100. Every variant sits contiguously
above its base, so "largest base value ≤ this one" is exact.

The game itself uses `EquipmentManager.GetWeaponData(t).PrimaryEquipmentType` (`:2856`), but
that reads a ScriptableObject and returns null often enough that the game null-checks it on
every use. The arithmetic needs no assets loaded and can't return null.

---

## Checks fire on *use*, not on pool entry

The obvious location hook is the four sites that add to the pool
(`Interaction_WeaponItem.cs:211/249`, `Interaction_WeaponSelectionPodium.cs:864/930`). It's
the wrong one, for two reasons:

- Those sites `Add` to the list **directly**, bypassing `DataManager.AddWeapon` — so
  `DataManager.OnWeaponUnlocked` does *not* fire on normal pickup. The event only covers the
  blacksmith's legendaries, Ratau's sword, and the `GameManager.Awake` seed. (Earlier research
  notes named that event as the hook; they were wrong.)
- More importantly, on an existing save the pool already contains every weapon, so "first
  time it enters the pool" is an event that already happened and can never happen again.
  Every check would be stranded.

So the check fires the first time a weapon is **equipped** in this seed:

| Patch | Site |
|---|---|
| `PlayerWeapon.SetWeapon(EquipmentType, int)` | `PlayerWeapon.cs:1441` |
| `PlayerSpells.SetSpell(EquipmentType, int)` | `PlayerSpells.cs:61` |

Both are the single funnel every route ends at — podium, chest, choice room, found item
(`FoundItemPickUp.cs:322` calls straight into `SetWeapon`). One patch each, no coroutine
state machines to reach into.

Sent-once is tracked in memory per session and re-derived from the AP location history on
reconnect, so it needs nothing persisted.

---

## Scope: primary types only

Seven weapons and five curses. The `_Poison` / `_Critical` / `_Healing` / `_Fervour` /
`_Godly` / `_Nercomancy` / `_Legendary` variants are **not** in scope — they are unlocked by
sermon upgrades (`UpgradeSystem.cs:984-990`) and the blacksmith
(`Interaction_BlacksmithNPC.cs:300-360`), which are Sprint 2's and a future sprint's
business respectively. Randomizing them here would double-gate the same content.

| `EquipmentType` | Item name |
|---|---|
| `Sword` | Crusader's Blade |
| `Axe` | Apostate's Cleaver |
| `Hammer` | Warmaker's Hammer |
| `Gauntlet` | Tempest's Gauntlets |
| `Dagger` | Traitor's Razor |
| `Blunderbuss` | Mayhem's Cannon |
| `Chain` | Battler's Bludgeon (Woolhaven) |

| `EquipmentType` | Item name |
|---|---|
| `Fireball` | Flaming Shot |
| `Tentacles` | Touch of Turua |
| `EnemyBlast` | Divine Blast |
| `ProjectileAOE` | Ichor Thrown |
| `MegaSlash` | Death's Sweep |

Names are the real base-tier in-game ones from `wiki/extracted_text/Weapons` and `/Curses`,
not guesses. The first five weapon ids already existed in `items.py` as placeholders and are
reused unchanged, so nothing repoints.

**Deliberately excluded:**

- `Sword_Ratau` — a one-off story unlock from `GraveyardNPC.cs:68`, and
  `Interaction_Knucklebones.cs:59` reads it as a flag. Gating it would reach into Ratau's
  questline for one check.
- `Teleport` and `Barrier` — curse families that enter the pool through sermon upgrades
  (`Curses_Teleport`, and the Barrier pack), not through the ladder. Already randomized as
  sermon items.
- `Shield` — **discarded content**. It has a full enum band (460-467), but *Conviction's Guard*
  was cut before release and nothing can grant it.

---

## Logic: each check is gated on its own item

`Weapon - Apostate's Cleaver` requires the item *Apostate's Cleaver*, because the check
cannot fire until Archipelago grants the weapon and the filter lets it appear. That makes
these twelve items **progression**, unlike the sermon and tarot items — a rule genuinely
references them, which is the bar for that classification.

They get real logic, so they skip `set_depth_rules` entirely (same treatment as
`TarotCardRegion`). Note `set_depth_rules` calls `set_rule`, which *overwrites* rather than
composes — running both would silently drop the item requirement.

Starting weapons and curses get neither a check nor an item, matching `starting_tarot_cards`.

---

## Options

Five, and every combination is valid:

| Option | Values | Default |
|---|---|---|
| `randomize_weapons` | toggle | on |
| `randomize_curses` | toggle | on |
| `starting_weapons` | 1–7 | 1 |
| `starting_curses` | 1–5 | 1 |
| `legendary_weapons` | `off` / `rare` / `common` / `always` | `off` |

`range_start = 1` on the two counts is load-bearing, not politeness: zero granted primaries
would make the substitution rule fall through to its emergency fallback on every pick.

Woolhaven's Flail follows `include_woolhaven` like everything else.

### `legendary_weapons`

Offers a family's Legendary in place of a normal weapon of that family, at 0 / 10% / 25% / 100%.
It is a gameplay modifier, not a randomization axis — **no check, no item, no change to the
seed's shape.** The roll happens before every other rule in `Substitute`, so it applies to picks
that would otherwise pass straight through, and it's gated on the family already being granted:
this improves the weapons you have rather than handing you one you can't otherwise use.

**Woolhaven-only, and resolved server-side.** `pick_legendary_chance` returns 0 without
`include_woolhaven`, so the client needs no DLC check of its own. Legendaries are DLC content:
`CompletionCalculator.CalculateDLC` counts `BlacksmithShopFixed` and
`CompletedBlacksmithJobBoard`, and the shop is a `DLCRebuildableShop`.

Independent of `randomize_weapons` — the client registers the weapon service for either option,
and with nothing managed it only performs the Legendary roll.

**Why this is not "add them to `WeaponPool`"**, despite that being the obvious reading (the game
even has a localization key named `Notifications/LegendaryWeaponAddedToPool`):

- **It wouldn't work.** `GetRandomWeaponInPool` (`:2820-2847`) strips any Legendary whose
  `Blacksmith_Legendary_<X>` upgrade isn't unlocked, so a pool write alone changes nothing —
  you'd need a second permanent save write per weapon, into the same upgrade tree Sprint 0e
  wants to randomize. Substituting from a postfix runs *after* that filtering and needs neither.
- **It would break Woolhaven's own Legendary questline.** `Interaction_LegendaryWeaponPlinth`'s
  `canShowWeapon` (`:19`) and `Objectives_LegendaryWeaponRun` (`:17`) both read
  `WeaponPool.Contains(legendary)`, so writing them in makes the plinths show as already claimed
  and the job-board objectives read as complete.

Equipping a Legendary sends its *family's* check (`FamilyOf` maps `Sword_Legendary` to `Sword`),
which is the intended behaviour — it's still using that family.

---

## Acceptance

- [x] Both toggles generate independently, in all four combinations.
- [x] `starting_weapons`/`starting_curses` at both ends of their range generate.
- [x] Weapon and curse items are progression and their checks are gated on them.
- [x] `legendary_weapons` is forced to 0 without Woolhaven, adds no items or locations, and
      generates with `randomize_weapons` off.
- [ ] In-game: with `legendary_weapons: always` and Woolhaven, every podium offers the
      Legendary of a granted family — and the Blacksmith plinths still show them unclaimed.
- [ ] In-game: with one starting weapon, every podium and chest offers only that weapon.
- [ ] In-game: receiving a weapon item makes the next run's first room hand it over with the
      normal unlock ceremony, and equipping it sends the check.
- [ ] In-game: `WeaponPool`/`CursePool` are byte-identical before and after a session
      (nothing is ever written) — F9 dumps both.
