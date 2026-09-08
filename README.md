# Kukolony

A colony management system for Valheim. Recruit NPC villagers, give them a work post, and
they keep the base running — hauling, loading smelters, cooking, crafting, repairing —
whether or not you are standing there watching.

Built with [Jötunn](https://github.com/Valheim-Modding/Jotunn) on BepInEx. Early
development; nothing is playable yet.

## Documentation

Read in this order:

| Doc | What it covers |
|---|---|
| [What the mod is](docs/README.md) | Scope, milestones, design principles |
| [Off-screen simulation](docs/off-screen-simulation.md) | How zone and instance lifetime work, and how we extend them |
| [Multiplayer](docs/multiplayer.md) | ZDO ownership arbitration and what it forces on job code |
| [API notes](docs/api-notes.md) | The game and Jötunn calls we actually use |
| [Modding basics](docs/modding-basics.md) | BepInEx/Harmony/Jötunn conventions and traps |
| [Codebase rules](docs/code-style.md) | Layout, state, ownership, patching conventions |
| [Post-mortem](docs/predecessor-postmortem.md) | The 2024 predecessor: what to keep, what not to repeat |
| [macOS setup](docs/SETUP-macos.md) | Toolchain, game paths, BepInEx on Apple Silicon |

## Build

```sh
dotnet build Kukolony.sln -c Debug
```

Debug builds deploy `Kukolony.dll` straight to `BepInEx/plugins/Kukolony/`. Release builds
produce a Thunderstore-ready zip.

Build the **solution**, not the project — Jötunn's `Paths.props` imports
`$(SolutionDir)Environment.props`, and building the `.csproj` directly leaves
`SolutionDir` undefined so your machine paths are silently skipped.

`Environment.props` is gitignored; its contents are in
[docs/SETUP-macos.md](docs/SETUP-macos.md).

## Status

Environment is set up and the plugin loads in-game. No gameplay yet.

The next open question is a blocking one: whether a dedicated server instantiates
GameObjects at all. It decides whether server-owned idle colonies are viable. See the
unverified section in [docs/multiplayer.md](docs/multiplayer.md).

## Credits

Scaffolded from [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub).
