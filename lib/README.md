# lib/

Drop third-party DLLs here that aren't published to NuGet.

- `COTL_API.dll` (optional) — Cult of the Lamb's community modding API
  ([xhayper/COTL_API](https://github.com/xhayper/COTL_API)). Not required to build; only
  needed once we start using its `Custom*` systems (structures, tarot cards, follower
  commands, etc.) instead of raw Harmony patches. Build it from a local clone or grab it
  from [Thunderstore](https://cult-of-the-lamb.thunderstore.io/package/xhayper/COTL_API/).
