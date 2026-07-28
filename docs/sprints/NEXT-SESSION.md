# Start here — handoff for a fresh session

Paste this file's path into a new session and say **"read this and start Sprint 3."**

---

## 1. Load context in this order

1. **`docs/sprints/README.md`** — the sprint roadmap. Read the "Target play session" section
   first; it's the constraint that decides everything else.
2. **`cotl-decompile-lookup` skill** → points at
   `C:\Users\IanCi\Repos\DecompiledGamesViaDnSpy\Cotl\AI_INDEX.md` (verified code findings with
   file:line cites) and `wiki/` (real in-game names, hub/shop inventories).
   **Read AI_INDEX.md before grepping** — most answers are already there.
3. **`cotl-build-deploy-validate` skill** → every path, the r2modman profile, the apworld
   pipeline, and the known traps.
4. `docs/architecture.md` for how the C# client and Python world fit together.

## 2. Repo facts

- Repo: `C:\Users\IanCi\Repos\cult_of_the_lamb_archipelago`
  (GitHub: IanCichy/CultOfTheLamb_Archipelago). Work on `main`.
- **Commit only after the user reviews the diff.** No `Co-Authored-By`, no "Generated with
  Claude Code" in commit messages.
- The repo ruleset asks for PRs on `main`; pushes currently bypass it. Unsettled.

## 3. What already works (don't rebuild it)

97 locations, verified in a real playthrough — bosses/minibosses/Witnesses (20), sermon
upgrades (38 locations / 30 items), Follower milestones (20), Tarot shop purchases (14), Snail
Shrines (5), region access (3 progressive items), and a weighted filler pool with real effects
plus traps.

Client patterns already established — copy these rather than inventing new ones:

| Need | Pattern |
|---|---|
| Boss kills | Postfix `DataManager.AddKilledBoss(string)` — the only write site |
| Shop purchases | Postfix `Interaction_BuyItem.Activate()` — universal across all shops |
| Follower joins | `FollowerManager.OnFollowerAdded` — public static event, no patch |
| Save-state flags | Poll on a throttle from `ArchipelagoPlugin.Update` (see SnailShrineService) |
| Item → game effect | Add to `ArchipelagoItemLogicController.ApplyItem`, or a `TryApplyItem` service |
| Mapping Python ↔ C# | **Send it through `fill_slot_data`.** Don't hardcode ids client-side |
| Popups | `ApNotification.Show(...)` — handles the I2 term registration trap |
| Check sent | `CheckNotifier.Announce(session, ids)` — batches, so catch-up bursts don't spam |

## 4. Hard-won traps — do not rediscover these

- **Internal names are never display names.** `Boss Mama Worm` is Amdusias, `Skull` is "The
  Burning Dead", `LOG` is "Lumber", `SOUL` is "Devotion". Guessing has been wrong every single
  time. **Press F4 in-game** to dump the real name tables to
  `BepInEx/ap_unlockable_names.txt`, and read from that.
- **The wiki is sometimes stale.** It had the curse-pack numbering wrong (4 of 5) and missed
  entire upgrade tiers. Prefer the game's own data (F4, `UpgradePlayerConfiguration.AllUpgrades`).
- **Never open `UIUpgradePlayerTreeMenuController`.** It overrides `OnCancelButtonInput()` with
  an empty body — it cannot be dismissed, and the only exit hands out a free upgrade.
- **Non-idempotent grants must not replay.** The server resends the full item history every
  connect; `AppliedItemStore` tracks what a save already got. Resources stack, unlocks don't.
- **Locations don't create spheres, gates do.** 97 locations still yields 2–3 spheres because
  region access is the only progression item.
- **`Interaction_ShopKeeper` is dead placeholder code** (joke dialogue about bananas).
- The project compiles against `CultOfTheLamb.GameLibs 1.5.15.979`. If a restore fails,
  Windows Defender may quarantine `Rewired_Core.dll` out of the NuGet cache as a false positive.

## 5. Debug keys (need a game restart to register)

`F5` connect/disconnect · `F2` list owned sermon upgrades · `F3` fill the sermon bar ·
`F4` dump all name tables to file · `F6` resources · `F7` an upgrade · `F8` a tarot card ·
`F9` AP + boss + miniboss + snail state · `F10` a fleece · `F11` notifications

## 6. Sprint 3 — Tarot cards

Everything needed is researched; this is **finalization and testing, not discovery**.

- Pool: `DataManager.AllTrinkets` = 85 cards. Randomizable ~46 after excluding 19 Woolhaven
  (`MajorDLCCards`), 5 co-op (`CoopCards`), 15 starting (`DefaultCards`).
- Grant: `TarotCards.UnlockTrinket(Card)` — proven, and raises the game's own unlock alert.
- Names: already in the F4 dump. Do not guess.

**The one real design decision**: what the other ~30 locations are, beyond the 14 shop
purchases that already exist. First-find in a crusade is the obvious source, but the
find-event and the grant must be **decoupled** — otherwise a card AP gives you early makes its
own location permanently unreachable. Sprint 2's doc covers this trap in the sermon context;
the same logic applies.

Suggested shape: mirror the sermon approach — sequential locations ("Tarot Card 1..N" as you
find cards) with the *named cards* as items. That sidesteps the softlock entirely.

**Acceptance**: seed generates with tarot locations and items; finding a card in a crusade
sends a check; receiving a card item unlocks it and shows a notification.

## 7. Known issues worth fixing opportunistically

- Filler names don't match effects: "Fervour" grants Coins, "Gold Tithe" grants Gold Nuggets.
  Cheap to rename **now**, expensive once seeds exist.
- Snail shrine region-scoping unresolved — all 5 sit in `Cult`. F9 showed `ShrineNumber=1` in
  *Ratau's Home*, not a hub, contradicting the "one per hub" assumption. Needs F9 dumps from
  the four hubs before changing anything.
- `Region locking active: True` logs twice per connect — `ProcessLoginResult` appears to run
  twice. Harmless, not understood.
- Co-op untested. `TarotCards.CoopCards` are in `AllTrinkets` and should be excluded from
  solo seeds — **relevant to Sprint 3.**
