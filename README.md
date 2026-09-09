# Kukolony

Kukolony now centres a placed Colony Hearth: it owns live-radius registered structures,
members and typed concrete job queues. The retired work-post and JSON-job model is not
loaded on startup. See [colonies](docs/colonies.md), [structure registry](docs/structure-registry.md),
and [job catalog](docs/job-catalog.md).

A colony management system for Valheim. Recruit NPC villagers, give them a work post, and
they keep the base running — hauling, loading smelters, cooking, crafting, repairing —
whether or not you are standing there watching.

Built with [Jötunn](https://github.com/Valheim-Modding/Jotunn) on BepInEx. Early
development; nothing is playable yet.

## Documentation

Read in this order:

| Doc | What it covers |
|---|---|
| [Current features](features.md) | What is playable today, how to set it up, and what is still missing |
| [What the mod is](docs/README.md) | Scope, milestones, design principles |
| [Off-screen simulation](docs/off-screen-simulation.md) | How zone and instance lifetime work, and how we extend them |
| [Multiplayer](docs/multiplayer.md) | ZDO ownership arbitration and what it forces on job code |
| [Jobs](docs/jobs.md) | The job engine, target claims, JSON definitions, and the work post panel |
| [Colonies](docs/colonies.md) | What a colony owns, why it has no radius, and how that makes far members reachable |
| [API notes](docs/api-notes.md) | The game and Jötunn calls we actually use |
| [NPC design research](docs/npc-design.md) | Player-model villagers, appearance, and the job architecture |
| [Modding basics](docs/modding-basics.md) | BepInEx/Harmony/Jötunn conventions and traps |
| [Codebase rules](docs/code-style.md) | Layout, state, ownership, patching conventions |
| [Automated testing](docs/automated-testing.md) | How the mod tests itself, with no human in the loop |
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

Villagers have a persistent identity and inventory, can be organised from a colony hearth,
assigned to work posts, and haul dropped items into a chosen container. The colony and work
post panels, JSON-backed job definitions, target claims, and off-screen keep-alive are all
implemented. See [current features](features.md) for the player-facing summary and known
limits.

The core workflow is verified by the mod testing itself — see
[docs/automated-testing.md](docs/automated-testing.md).

## Credits

Scaffolded from [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub).
