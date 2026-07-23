# Cult of the Lamb - Archipelago

A [Cult of the Lamb](https://store.steampowered.com/app/1313140/Cult_of_the_Lamb/) mod for
[Archipelago](https://archipelago.gg) multiworld randomizer. Modeled after the
architecture of [ror2_archipelago_enhanced](https://github.com/IanCichy/ror2_archipelago_enhanced)
(Services-layer C# client, partial-class connection handling, IService pattern).

## Status: early scaffold

This is not playable yet. What exists:
- A buildable BepInEx 5 plugin skeleton (connects to an AP server, receives items, but
  doesn't yet act on them or report any real location checks).
- An Archipelago Python world (`worlds/cult_of_the_lamb/`) that generates valid seeds today
  - verified by actually running it through `Generate.py` - but with placeholder item/
  location names beyond the real region and Bishop names.
- No Harmony patches or COTL_API hooks into the actual game yet. Next step is decompiling
  `Assembly-CSharp.dll` with dnSpy to find real hook points.

See [docs/architecture.md](docs/architecture.md) for the reasoning behind build target
choices, what's verified vs. placeholder, and the two generation bugs already caught and
fixed.

## Project Layout
- `Archipelago.CultOfTheLamb/` - BepInEx 5 (Mono) client mod.
- `worlds/cult_of_the_lamb/` - Archipelago Python world.
- `docs/` - architecture notes and sprint docs.
- `lib/` - drop-in folder for third-party DLLs not on NuGet (e.g. COTL_API.dll).

## Building the C# Mod
1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases/latest) (x64) into your
   Cult of the Lamb install if you haven't already.
2. Copy `Archipelago.CultOfTheLamb/Directory.Build.props.default` to
   `Directory.Build.props.user` and point `GameFolder` at your local install
   (gitignored - this stays local).
3. Open `Archipelago.CultOfTheLamb.sln` and build. The post-build step copies the plugin
   into `<GameFolder>/BepInEx/plugins/Archipelago.CultOfTheLamb` automatically.

## Working on the AP World
`worlds/cult_of_the_lamb/` is a standard Archipelago world package. To test generation
against a full Archipelago checkout: copy (or symlink) the folder into that checkout's
`worlds/` directory, then run `Generate.py` with a matching player YAML. To package for
distribution: `cd worlds && zip -r ../cult_of_the_lamb.apworld cult_of_the_lamb/`.

## License
MIT - see [LICENSE](LICENSE).
