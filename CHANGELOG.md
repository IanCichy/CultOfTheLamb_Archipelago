# Changelog

## 0.9.0 - First public beta

The first build published for anyone else to play. Everything below `0.1.0` was
pre-release development, so this section covers the whole mod rather than one increment.

### Randomized systems
- **Region access.** The four Pathway doors become a single `Progressive Bishop's Domain` item:
  one region is free per seed and the other three unlock in a randomized order. Doors are locked
  at both the interaction and the physical-collider route, since vanilla opens them on the
  follower-count requirement alone and never consults the unlocked-door save state.
- **Weapon and curse families**, **sermon upgrades**, **Tarot cards** and **Divine Inspiration**
  all become two-sided systems - each sends checks as you earn it and receives its own unlocks
  from the multiworld, so you still play the system but no longer choose what it gives you.
- **Faucet-only check sources**, each toggleable: Bishops, minibosses and Witnesses; follower
  recruitment milestones; Snail Shrine offerings; Tarot shop slots; construction; sweeping.
- **Goal**: beat N of the four Bishops, N of the four Witnesses, or Narinder.
- Woolhaven DLC content is supported and gated behind `include_woolhaven`.
- Filler, resources and traps pad the pool, with `trap_percentage` as the counterweight.

### In-game support
- **Sermon tree viewer** ("Archipelago" on the Temple Altar menu): opens the game's own sermon
  upgrade tree as a read-only view, with the real node art, the real layout, and full controller
  support. Randomizing sermons replaces `SermonController.PlayerUpgrade`, which was the game's
  only routine way into that tree - so without this there is no way at all to see which sermon
  upgrades you hold. Nothing can be unlocked from it, and each node shows what the upgrade does.
  The tier thresholds are hidden, since Archipelago grants upgrades outright and ignores them.
- **Weapon and curse podiums in the base**: a line of the game's own crusade podiums - every
  weapon, a gap, then every curse - lit for the families Archipelago has granted and locked for
  the ones it hasn't. These are the only randomized system the game can't show natively; the
  weapon and curse wheels are in-run only, and there is no collection screen for them. Teleport
  curses are included on a Woolhaven seed, since that family arrives through a sermon upgrade
  rather than the curse pool. Position is configurable under `[Displays]` and they can be turned
  off entirely. Purely decorative - nothing is written to save data.
- **Archipelago objective guide** (`archipelago_objective_guide`, on by default): an Archipelago
  checklist in the game's own quest log - win condition, region access, and one live-progress
  line per active check block. Guidance only; it creates no locations and no items.
  `objective_guide_pinning` controls how much of it sits on the on-screen tracker.
- **Vanilla follower quests trimmed** (`vanilla_follower_quests`, default `thin_trickle`): most
  of the game's ~87 built-in follower quests are taken out of rotation, keeping the ritual
  quests, the crusade collection quests and the follower story chains. A trim rather than a wipe
  because turning a quest in is the game's main follower-loyalty-XP source.

### Fixes in the run-up to the beta
- **Fixed a hard lock in the base-upgrade flow.** Granting a base-tier upgrade (e.g. from a
  multiworld item) while the base wasn't loaded, or out of order, could throw mid-routine and
  leave the player permanently uncontrollable - menus blocked, character gone, no recovery short
  of a hard exit. The upgrade is now deferred until the base is safely live, a downgrade is
  refused and repaired to the correct tier instead of silently rebuilding it lower, and a
  finalizer restores the game's own state if the routine fails for any other reason.
- **Fixed the game's 15 default tarot cards becoming permanently unreachable locations.**
  Cult of the Lamb can never re-unlock a card you start with, so treating them like any other
  tarot check left dead locations in every seed that the fill could still place progression items
  on - stranding other players' items behind a check that could never fire. Default cards are now
  excluded entirely: no item, no location, you simply keep them as in vanilla. This shrinks the
  Tarot pool by the 15 default cards, so a seed has fewer locations than it used to -
  **seeds generated before this update are incompatible.**
- **Missed checks are now re-derived on every connect**, not just some. Bishop, miniboss, Witness
  and sermon checks previously only sent from live events, so anything earned while disconnected
  was gone for good; they now re-scan save state at connect and send whatever hasn't landed yet.
  Re-sending is free and needs nothing persisted.
- **Reconnection no longer gives up.** A dropped connection used to stop retrying after ~15
  seconds; it now retries indefinitely with backoff (3s up to a 30s ceiling), so an unattended
  client recovers on its own once the server comes back.
- **Check sends moved off the main thread**, removing a hitch when several checks catch up at
  once on connect.
- Ships an `archipelago.json` manifest inside the `.apworld`, required by newer Archipelago
  versions to load the world at all.

## 0.1.0 - Initial scaffold
- BepInEx 5 plugin skeleton: connection lifecycle, reconnection, item receive queue.
- Archipelago Python world: regions (Anura/Darkwood/Anchordeep/Silk Cradle), starter item/
  location tables, options, rules. Generation verified end-to-end against a real
  Archipelago checkout.
- No gameplay hooks yet - item and location tables only.
