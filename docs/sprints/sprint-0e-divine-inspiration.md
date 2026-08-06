# Sprint 7a — Divine Inspiration tree

**Status: researched, not started.** Every hook point below was read directly from the
decompile on 2026-08-05; citations are in `DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md` §4-ii.

The design goal is **implement every option and let the YAML pick**. Two axes that work
independently, so a seed can run any combination of the two — e.g. "checks and techs" with
"random except tier 1".

---

## Why this sprint is worth its size

The tree is the only system in the game that is simultaneously:

- a large block of **locations** (one per upgrade),
- a large block of **items** (the same upgrades, or the points that buy them),
- and an existing **gate on other content** — `StructuresData.GetUnlocked(TYPES)` maps every
  buildable structure to an `UpgradeSystem.Type`, so the tree already decides what you can build.

That last point is why this is numbered ahead of Sprint 7. Structures checks land on top of
tree logic, not beside it.

It also produces spheres almost for free — see "Tier gating is a count" below.

---

## The three trees

All three are `UpgradeTreeConfiguration` ScriptableObjects on **public fields** of `GameManager`
(`GameManager.cs:1978-1984`), so the whole graph is reachable at runtime without reflection:

| Field | Tree |
|---|---|
| `UpgradeTreeConfiguration` | **Divine Inspiration** — this sprint |
| `UpgradePlayerConfiguration` | the player/sermon tree — already AP-randomized |
| `DLCUpgradeTreeConfiguration` | Woolhaven — defer to Sprint 11 |

They share a class. Anything built here works on all three, which is worth remembering before
writing anything tree-specific.

---

## Tier gating is a **count**, not a prerequisite

The single most useful fact in this sprint. Each node has **two independent gates**
(`UpgradeTreeNode.cs:296`):

1. **Tier gate** — `NumUnlockedUpgrades() >= NumRequiredNodesForTier(nodeTier)`, where that is a
   cumulative sum of `TreeTierConfig.NumRequiredToUnlock` across every tier ≤ N. It counts
   upgrades unlocked **anywhere in the tree**, not specific ones.
2. **Prerequisite gate** — the node's own parents, from `_allUpgradesRequiringUpgrade`
   (`RequiresUpgrade { Type Upgrade; List<Type> Children; }`) — the tree's DAG.

So tier access is *"have you bought K things"*, not *"have you bought these things"*. That maps
straight onto an AP progressive-count rule with **no graph traversal**:

```
Tier N reachable  <=>  count(tree upgrades received) >= K_N
```

`TreeTier` is `Tier1`…`Tier6` (`UpgradeTreeNode.cs:710`), so this is six clean spheres. The
prerequisite DAG is a second, finer axis that can be added later without redoing the first.

**This is a better sphere source than the depth bands in `rules.py`** — it is the game's own
rule, not an approximation of one.

---

## Axis A — tree layout (`divine_inspiration_shuffle`)

| Value | Behaviour |
|---|---|
| `true_random` | Reassign every upgrade's tier, preserving the prerequisite DAG. Tier 1 and tier 2 of the same structure may land in the same tier, but never inverted. |
| `random_except_first` | As above, but `TierConfigurations[0].AllUpgradesInTier` is frozen — the starting bed and the rest of row 1 stay put. |
| `default` | No change. |

"Never a tier before the next" **is** "respect the DAG, ignore the tiers" — a topological-order-
preserving shuffle. The DAG is already sitting in `_allUpgradesRequiringUpgrade`, so the
constraint is directly expressible rather than something to reconstruct.

All three fields are `[SerializeField]` private on the ScriptableObject — writable with Harmony's
`Traverse`.

### The hazard that will cost a day if missed

`UpgradeTreeNode._upgrade` and `.requiresUpgrade` (`UpgradeTreeNode.cs:625`, `:668`) are
**separate serialized fields on the prefab node components**, gathered by the menu as `_treeNodes`
(`UIUpgradeTreeMenuBase.cs:728`). A shuffle must rewrite **both** the configuration and the node
components. Rewrite only the config and the drawn tree silently disagrees with the logic — you
see one upgrade and buy another.

---

## Axis B — Archipelago interaction (`divine_inspiration_mode`)

| Value | Behaviour | Cost |
|---|---|---|
| `checks_only` | Normal DI. Unlocking sends a check. Player keeps the point and picks normally. | Subscribe to one event. No patches. |
| `checks_and_points` | Normal DI, but the point is withheld. DI points arrive as AP items, capped at the tree's upgrade count. Player still chooses what to spend on. | Setter patch + item |
| `checks_and_techs` | Normal DI, point withheld, points never exist. AP grants techs directly. | Setter patch + `UnlockAbility` |
| `off` | No interaction. | — |

### Choke points

- **Point award** — `UpgradeSystem.AbilityPoints` is a static property with a setter
  (`UpgradeSystem.cs:31-50`). **Patch the setter**, not `PlayerFarming.GetXP`. The `++` at
  `PlayerFarming.cs:2024` is the only non-debug award site today, but the setter catches every
  path including any the game adds later. `DisciplePoints` (`:56`) is the parallel currency and
  needs the same decision.
- **Locations** — `UpgradeSystem.OnAbilityUnlocked`, a `public static Action<UpgradeSystem.Type>`
  (`:2422`). Subscribe; no Harmony patch at all. `OnUpgradeUnlocked` (`:77`) is a second event
  in the same place if a richer signature is wanted.
- **Grant** — `UpgradeSystem.UnlockAbility(Type, instant)` (`:703`). Prerequisites are **not**
  enforced (a bare `Contains`-then-`Add`), so out-of-order grants are safe.

### `checks_and_points` has a logic caveat — decide before building

The player still chooses *which* tech a point buys, so **AP can never know which techs they
hold**. DI points are progressive items; techs are player choice. That is fine in isolation, but
it means nothing else in logic may depend on a *specific* tech — which directly conflicts with
gating structure checks on `Building_*` upgrades (Sprint 7).

`checks_and_techs` does not have this problem: AP knows exactly what it granted.

**Recommendation:** if `divine_inspiration_mode = checks_and_points`, structure checks must fall
back to count-based bands rather than per-upgrade logic. Encode that as an option interaction in
`rules.py`, or forbid the combination in `generate_early()`.

---

## Cross-system coupling

- `UpgradeSystem.GetStructureTypeFromUpgrade(Type)` (`:330`) is the upgrade→structure map;
  `StructuresData.GetUnlocked(TYPES)` is the inverse. **This is what makes structure logic
  writable at all** — it is ready-made, not something to hand-maintain.
- `UpgradeSystem.PlayerHasRequiredBuildings(Type)` (`:2055`) / `GetRequiredBuilding(Type)`
  (`:2073`): some upgrades require *built structures*. Combined with the above that is a
  potential **tech → building → tech cycle**. Check the dump for a real one before gating both
  systems in one seed; if one exists, one side has to be excluded from logic.

---

## ✅ Dump done (2026-08-06) — the numbers

Press **F4** at a loaded save; writes `BepInEx/ap_unlockable_names.txt`.

| Tree | Upgrades | Tiers | Cumulative thresholds |
|---|---|---|---|
| `UpgradeTreeConfiguration` (Divine Inspiration) | **69** | **5** | **0, 4, 10, 20, 25** |
| `UpgradePlayerConfiguration` (sermon) | 38 | 6 | 0, 2, 5, 8, 12, 16 |
| `DLCUpgradeTreeConfiguration` (Woolhaven) | 23 | 3 | 0, 0, 0 |

So the sphere rule is:

```
Tier N reachable  <=>  received >= [0, 4, 10, 20, 25][N] Divine Inspiration upgrades
```

Only **25 of the 69** upgrades are ever needed to open every tier — the rest are breadth.

Three corrections to assumptions made before the dump:

- **The DI tree has 5 tiers, not 6.** `UpgradeTreeNode.TreeTier` declares `Tier1`–`Tier6`, but only
  the *sermon* tree uses all six. Writing the rule off the enum would have produced a phantom tier.
- **Woolhaven has no count gating** — every `NumRequiredToUnlock` is 0, so it is purely
  prerequisite-driven. A count-based sphere rule is meaningless there; if Sprint 11 ever
  randomizes it, that tree needs the DAG instead.
- Tier centrals are `Building_Temple`, `Building_Temple2`, `Economy_Refinery`, `Temple_III`,
  `Temple_IV` — all `RequiresCentralTier=True`.

### No tech → building → tech cycle. Both systems can gate in one seed.

62 upgrades unlock a structure; 11 upgrades require a built structure. Every one of those points
strictly *upward* — `Building_Temple2` needs `TEMPLE` (unlocked by `Building_Temple`), `Temple_III`
needs `TEMPLE_II` (unlocked by `Building_Temple2`), `Economy_MineII` needs `BLOODSTONE_MINE`
(unlocked by `Economy_Mine`), and so on. Zero direct cycles, and because the relation is a strict
partial order over tiers, no transitive cycle is possible either.

**This removes the one hazard that could have forced Sprint 0e and Sprint 7 apart.** Structure
checks may be gated on `Building_*` upgrades in the same seed that randomizes the tree — subject
only to the `checks_and_points` caveat above, which is a different problem.

## Original blocker note (resolved, kept for context)

`_allUpgrades`, tier membership, the prerequisite edges, and the `NumRequiredToUnlock` values
that *define the spheres* are serialized Unity asset data. They are not in the assembly, so
exact counts are unknown until dumped.

This is much lighter than Sprint 3b's blocker — one ScriptableObject on a public field, no
addressables harvesting. A debug command walks
`GameManager.GetInstance().UpgradeTreeConfiguration` and prints:

- `_allUpgrades` (count and contents)
- each `TreeTierConfig`: `Tier`, `CentralNode`, `RequiresCentralTier`, `NumRequiredToUnlock`,
  `AllUpgradesInTier`
- `_allUpgradesRequiringUpgrade` as edge pairs
- the same for `UpgradePlayerConfiguration` and `DLCUpgradeTreeConfiguration`

**Step one, and everything else is designable the moment it exists.** Run it for all three trees
in one pass, and check the structure-cycle question at the same time.

---

## Acceptance

- All four `divine_inspiration_mode` values generate and play.
- All three `divine_inspiration_shuffle` values generate, and the drawn tree matches the logic
  in every case (the node-component hazard above).
- Both axes set independently in one seed, including `checks_and_techs` + `random_except_first`.
- Tier N locations are provably in sphere ≥ N.
