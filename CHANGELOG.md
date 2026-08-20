# Changelog

## Unreleased
- **Sermon tree viewer** ("Archipelago" on the Temple Altar menu): opens the game's own sermon
  upgrade tree as a read-only view, with the real node art, the real layout, and full controller
  support. Randomizing sermons replaces `SermonController.PlayerUpgrade`, which was the game's
  only routine way into that tree — so until now there was no way at all to see which sermon
  upgrades you held. Nothing can be unlocked from it, and each node also shows what the upgrade
  does. The tier thresholds are hidden, since Archipelago grants upgrades outright and ignores
  them.
- **Weapon and curse podiums in the base**: a line of the game's own crusade podiums — every
  weapon, a gap, then every curse — lit for the families Archipelago has granted and locked for
  the ones it hasn't. These are the only randomized system the game can't show natively; the
  weapon and curse wheels are in-run only, and there is no collection screen for them. Teleport
  curses are included on a Woolhaven seed, since that family arrives through a sermon upgrade
  rather than the curse pool. Position is configurable under `[Displays]` and they can be turned
  off entirely. Purely decorative — nothing is written to save data.
- **Archipelago objective guide** (`archipelago_objective_guide`, on by default): an
  Archipelago checklist in the game's own quest log — win condition, region access, and one
  live-progress line per active check block. Guidance only; it creates no locations and no
  items. `objective_guide_pinning` controls how much of it sits on the on-screen tracker.
- **Vanilla follower quests trimmed** (`vanilla_follower_quests`, default `thin_trickle`):
  most of the game's ~87 built-in follower quests are taken out of rotation, keeping the
  ritual quests, the crusade collection quests and the follower story chains. A trim rather
  than a wipe because turning a quest in is the game's main follower-loyalty-XP source.
- Ctrl+F9 dumps the objective guide (per-line I2 read-back, every Archipelago objective in
  the save) and then sweeps and rebuilds it.
- First real Harmony patch and working gameplay hooks: `RegionUnlockService` force-opens
  regions via `DataManager.Instance.UnlockedDungeonDoor`; `LocationCheckService` sends real
  checks for the 4 base Bishop kills via a patch on `Interaction_MonsterHeart`.
- Region *locking* (`Patches/BaseDungeonDoorPatch.cs`): unlocking alone left every other
  region openable, since vanilla opens doors on the follower-count requirement without ever
  checking the unlocked-door save state. Now blocked at both the interaction and the
  physical-collider route.
- Fixed the AP handshake announcing the mod version (0.1.0) as the Archipelago protocol
  version, which the server rejected with `IncompatibleVersion`.
- Python world redesigned: single `Progressive Bishop's Domain` region-access item (one of
  four regions free per-seed, order randomized), 20 real per-region locations (3 named
  minibosses + Bishop + Witness), two-track X/4 goal (Bishops or Witnesses).
- Region/Bishop/miniboss mapping fully confirmed against the decompiled source (not just
  wiki-inferred).

## 0.1.0 - Initial scaffold
- BepInEx 5 plugin skeleton: connection lifecycle, reconnection, item receive queue.
- Archipelago Python world: regions (Anura/Darkwood/Anchordeep/Silk Cradle), starter item/
  location tables, options, rules. Generation verified end-to-end against a real
  Archipelago checkout.
- No gameplay hooks yet - see docs/architecture.md for what's real vs. placeholder.
