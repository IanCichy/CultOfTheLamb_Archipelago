# Changelog

## Unreleased
- First real Harmony patch and working gameplay hooks: `RegionUnlockService` force-opens
  regions via `DataManager.Instance.UnlockedDungeonDoor`; `LocationCheckService` sends real
  checks for the 4 base Bishop kills via a patch on `Interaction_MonsterHeart`.
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
