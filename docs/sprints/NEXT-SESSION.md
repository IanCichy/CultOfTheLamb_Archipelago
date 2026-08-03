# Tarot cards — what was built, and what's still open

Record of the tarot work. Supersedes the handoff this file used to hold: the blocker it was
written to warn about is fixed, and the mechanism it recommended isn't the one that shipped.

## Loading context

1. **`cotl-decompile-lookup` skill** → `DecompiledGamesViaDnSpy/Cotl/AI_INDEX.md` (verified
   findings with file:line cites) and `wiki/`. **Read AI_INDEX.md before grepping.**
2. **`cotl-build-deploy-validate` skill** → every path, the r2modman profile, the apworld
   pipeline, the known traps.
3. `docs/architecture.md` for how the C# client and Python world fit together.

## The design

Every card the seed manages is an Archipelago item, and earning one is a check. You never
get a card by playing; playing is how you send checks.

**The invariant everything serves: no managed card is ever in
`DataManager.Instance.PlayerFoundTrinkets`.**

That's because every route the game has to offer a card first checks you don't already own
it — `Interaction_MysticShop.cs:493`/`:716`, `spiderShop.cs:173`,
`Interaction_DepositFollowerPlant.cs:60`, and `TarotCards.UnlockTrinket` itself. Letting an
Archipelago card become a real unlock closes that gate permanently and strands the check
riding on it. That was the original blocker: `Tarot Card - Favourable Winds` held a
`Progressive Bishop's Domain` in a seed where the card was already owned, so the fourth
region was unreachable.

Archipelago's cards live in `TarotService.granted` instead, and `TarotVisibility` lends them
back to the only two readers that need them.

### Why not sequential locations

The earlier handoff recommended replacing named locations with "Tarot Card 1..N" and called
it immune by construction. It isn't. The gates above are ownership-keyed, so every card the
multiworld grants still destroys one unlock opportunity — a card-heavy seed runs out anyway,
just later and harder to diagnose. The `SermonService` precedent doesn't transfer either:
sermon upgrades are repeatable purchases, whereas card unlocks are mostly one-shot,
fixed-identity world events (`Shrines.cs:381-382` Sun/Moon, `RitualWedding.cs:263` Lovers2,
`Interaction_TarotCardUnlock.CardOverride`). It would also have invalidated every seed.

## The pieces

| Where | What it does |
|---|---|
| `Patches/TarotUnlockPatch.cs` | Prefixes the two methods every unlock route ends at — `TarotCards.UnlockTrinket` and `DataManager.UnlockTrinket` — and asks `TarotService.Decide` for `Allow` / `SendCheck` / `Swallow`. Hooking the outcome instead of 85 unlock conditions is what makes this cheap. |
| `Services/TarotService.cs` | The policy and the state: `managedCards`, `startingCards`, `revoked`, `granted`. Revokes on connect, grants the seed's starting cards, restores on disconnect. |
| `Patches/TarotVisibility.cs` | Lends `granted` into the collection for the length of one call: `GetUnusedFoundTrinkets` (the run draw pool — the one that matters) and `UITarotCardsMenuController.OnShowStarted` (the collection screen). |
| `Patches/ShopSlotDisplayPatch.cs` | Decides which shop slots exist by overriding `DataManager.TrinketUnlocked` *scoped to `InitTarotShop`*, answering from the location's state rather than the card's. Also arms the unlock suppression so a purchase can't pay twice. |
| `Services/TarotShopService.cs` | Sends the check on purchase. |
| `Services/ShopIconService.cs` | Puts the AP logo and the scouted item name on marked slots. |
| `Utilities/RevokedCardStore.cs` | Sidecar file recording what a save is owed. Keyed by save slot alone, unlike `AppliedItemStore`'s save+seed+slot key. |
| `Utilities/MainThreadQueue.cs` | Teardown arrives on the websocket thread; save data may only be touched from Unity's. |

`TarotService.Tick()` re-establishes the invariant once a second, because `GameManager.Awake`
re-seeds 15 default cards whenever it finds the collection empty (`GameManager.cs:175`) —
exactly the state a fresh save is left in — and because loading a different save brings in a
whole collection nobody revoked.

## Traps worth remembering

- **`SaveAndLoad.SAVE_SLOT` is not stable within a session.** `MakeBaseGameBackUpSave` adds
  10, saves, and puts it back (`:307`); `Saving` can subtract 10 for good (`:183`). The +10
  is the Woolhaven variant of the *same* playthrough. `TarotService.CurrentSaveId` folds it
  back; comparing the raw value reads a false save switch.
- **The game replaces the list object**, at `GameManager.cs:177` and on MessagePack load, so
  `PlayerFoundTrinkets` must be re-read each tick rather than cached.
- **Display names are nothing like enum names** — "The Burning Dead" is `Skull`, "Ambrosia"
  is `Potion`. They come from the F4 dump (`BepInEx/ap_unlockable_names.txt`), never guessed.
  `Neptune’s Curse` really does use U+2019 and is the only card that does.
- **Notifications are queued** (`ApNotification.Flush()` from the plugin's Update): the game
  hard-suppresses them while the HUD is hidden or `NotificationsEnabled` is false, and
  `PlayGenericNotification` returns silently — which is why checks during cutscenes showed
  nothing and logged nothing.
- **In-run draws are not unlocks.** They read `GetUnusedFoundTrinkets` and never touch
  `UnlockTrinket`, so they correctly send nothing.

## Deliberate carve-outs

- Co-op cards (5) are never pooled — they can't be earned solo, and nothing would grant them
  back.
- Woolhaven cards (19) are fully in the pool, items and locations, when `include_woolhaven`
  is on; excluded from both sides and left vanilla when it's off.
- With `tarot_shop_checks` off, the 16 shop cards are handed back to the game entirely. Left
  managed they'd have no location at all, and the slot would take gold forever without
  selling out.
- Buying a card never grants it. The shop sends its check; the card comes from the pool.
- Completion % and `ALL_TAROTS_UNLOCKED` under-report while connected, and both self-correct
  on disconnect. Lending to the achievement path would write a permanent unlock.

## Still open

| Finding | Severity |
|---|---|
| Reveal cutscene plays for cards you don't receive — both the shop flow and `UICardManagerCard.UnlockCard()` animate before calling the unlock | Medium (polish) |
| 238 `NullReferenceException`s in `PlayerFarming.Update()` during crusade room transitions, **zero Archipelago frames in any stack**. Probably vanilla; needs a mod-off comparison | Unknown |
| Unbounded notification queue (`ApNotification.cs:89`) — turn notifications off and the whole session's backlog fires at once | Low |
| Unguarded `entry.Value.ToObject<long>()` (`TarotService.cs:237`) kills the connect instead of skipping one card | Low |
| Shop/card double-send is safe only by construction — worth an explicit comment at `TarotService.cs:95` | Low |
| Dead main-menu connect path (`MenuButtonPatch.MainMenu_Start_Postfix`) — only the pause-menu button ever worked | Low |
| Run-trader purchases give nothing that run | Design call |
| Filler names don't match effects ("Gold Tithe" grants Gold Nuggets) | Low |
| Sermon/follower/snail blocks all sit in `Cult`, so depth comes from `set_depth_rules` rather than real reachability | Watch |

## Debug keys

`F5` connect panel · `F1` dump shop slots + renderers · `F2` owned sermon upgrades ·
`F3` fill sermon bar · `F4` dump all name tables to file · `F6` resources · `F7` an upgrade ·
`F8` a tarot card · `F9` AP + boss + miniboss + snail state · `F10` a fleece · `F11` notifications
