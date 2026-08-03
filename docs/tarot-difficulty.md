# Tarot card acquisition difficulty

How hard each card is to earn, and what the world does about it. Player-supplied — the mod
deliberately never learns the game's ~85 unlock conditions (`TarotUnlockPatch` hooks the
*outcome* precisely because finding them all was infeasible), so this file is the source of
truth and the code trusts it.

Encoded in `worlds/cult_of_the_lamb/items.py` as `POSTGAME_TAROT_CARDS`, `REGION_TAROT_CARDS`
and `TAROT_TIERS`.

**Why it matters.** Card locations used to be banded by their position in the game's internal
enum, which has nothing to do with difficulty. A card needing the post-game could sit in the
shallowest band and advertise sphere-1 reachability to every other player in the multiworld.

---

## Post-game (18) — not created unless the goal reaches the post-game

Both sources are strictly after The One Who Waits. With a Bishops or Witnesses goal these
checks would sit past the win condition, and a location the player can't reach **fails
generation** under Full accessibility rather than merely making a slow seed. So they get no
item and no location, and the game keeps those cards entirely — the same treatment co-op
cards get.

`goal_reaches_postgame` currently returns `False` for both goals. Adding a Narinder or
Woolhaven goal (roadmap Sprints 1 and 11) switches these on by itself.

**Mystic Cellar** — only opens after the vanilla final boss:

| Card | Internal |
|---|---|
| The Reaching Tree | `AdventureMapFreedom` |
| Spirit Salvager | `Recycle` |
| Blind Wrath | `StrikeBack` |
| The Vanquisher | `SurpriseAttack` |
| Usurper Blessed | `BossHeal` |
| Temptation's Fate | `Sin` |
| Favourable Winds | `ExtraMove` |
| The Labyrinth | `ShuffleNode` |

**Corrupted set** — die in a post-game crusade, then interact with the Goat Statue (which also
unlocks corrupted relics and the Goat Fleece):

| Card | Internal |
|---|---|
| Order of Purity | `NoCorruption` |
| Reckoning | `CorruptedBombsAndHealth` |
| Mortal Oath | `CorruptedHeavy` |
| Blighted Core | `CorruptedTradeOff` |
| The Loathed | `CorruptedBlackHeartForRelic` |
| The Hoarder | `CorruptedHealForRelic` |
| Renegade Victorious | `CorruptedFullCorruption` |
| Poison Taster | `CorruptedPoisonCoins` |
| Sullied Oil | `CorruptedRelicCharge` |
| Ichor Drained | `CorruptedGoopyTrail` |

## Region-tied (5) — real logic, not a depth band

Their checks live in the crusade region that gates them, so the region graph does the work.
The knucklebones cards share one shape: you **meet** the opponent during a crusade in that
region, after which they move to Ratau's house and you play them there. Meeting them is the
gate, so the region is what matters — not where the game is finally played.

| Card | Internal | Region | How |
|---|---|---|---|
| Mithridatism | `PoisonImmune` | Anura | beat Flinky at knucklebones |
| Strength from Within | `BlackSoulAutoRecharge` | Silk Cradle | beat Shroomy at knucklebones |
| Strength from Without | `BlackSoulOnDamage` | Anchordeep | beat that region's knucklebones opponent |
| Fervour's Host | `Arrows` | Silk Cradle | buy followers from Helob 3× during crusades |
| Neptune's Curse | `NeptunesCurse` | Darkwood | from the fisherman in Pilgrim's Passage |

Note: `Arrows` matches `spiderShop.cs:173`, which gates on `followerShopUses > 3`. Helob is
the spider — the code name and the in-game name agree once you know that.

## Tiers (7) — order the depth bands

Everything else defaults to mid. These only decide which band a Cult-resident card lands in.

| Tier | Card | Internal | Why |
|---|---|---|---|
| early | The Hearts II | `Hearts2` | easy, first run or two |
| early | Ambrosia | `Potion` | |
| mid | The Hearts III | `Hearts3` | |
| mid | The Lovers II | `Lovers2` | wedding or fight-pit ritual (`RitualWedding.cs:263`) |
| late | The Collector | `MoreRelics` | |
| late | The Intangible | `ImmuneToTraps` | heavy RNG |
| late | Consecrated Oil of Renewal | `DecreaseRelicCharge` | |

---

## Effect on a seed

| | before | after |
|---|---|---|
| Total locations | 130 | 112 |
| Sphere-1 locations | 32 | 27 |
| Excluded (filler-only) | 25 (19%) | 20 (18%) |
| Card locations | 37 | 19, of which 5 region-gated |

Door keys now land on boss, shop and region-gated card locations rather than deep in a grind
block.

## Open

- The 19 Woolhaven cards have no tiers. They only appear in DLC seeds, and Woolhaven is the
  last sprint, so they band by enum order for now.
- Nothing here is verified against the decompile — unlock conditions are exactly what the
  design avoids needing to know. A wrong `late` costs a check; a wrong `early` on a genuinely
  late card puts the original blocker back. Prefer leaving a card unlisted (mid) over guessing.
