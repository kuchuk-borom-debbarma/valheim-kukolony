# Kukolony

Kukolony is a pre-release colony-management mod for Valheim. A placed Colony Hearth owns
named registered structures, villagers, concrete job configurations, presets, and each
villager's ordered looping queue. Work posts, bed assignments, and JSON job graphs have
been removed.

The implemented catalog includes hauling loose items, container transfers, fireplace
fueling, smelters and charcoal kilns, cooking stations, fermenters, and beehives. Registered
structures must be placed, network-backed, and inside the colony's live radius to be used.
Missing or out-of-radius records remain visible for repair or removal.

## Build

Development uses Jötunn and the pinned
`denikson-BepInExPack_Valheim-5.4.2350` runtime. Machine paths live in the ignored
`Environment.props`. Build the solution so Jötunn receives `$(SolutionDir)` and the DLL
is deployed to Valheim:

```sh
dotnet build Kukolony.sln -c Debug
```

Do not build the project file directly. On this workstation both compilation references and
the game's Doorstop/BepInEx runtime come from
`/Users/kuku/Downloads/denikson-BepInExPack_Valheim-5.4.2350/BepInExPack_Valheim`.

## Automated acceptance

```sh
./scripts/run-colony-acceptance.sh
```

The script builds the solution, verifies the installed Doorstop matches the pinned pack,
launches Valheim through Steam twice for save/relaunch persistence, launches it again for
UI capture, checks the reports and expected PNGs, and copies evidence to
`~/Desktop/kukolony`. Debug features are config-gated and off by default.

Start with [current features](features.md), [colonies](docs/colonies.md),
[structure registry](docs/structure-registry.md), [jobs](docs/jobs.md),
[job catalog](docs/job-catalog.md), and [automated testing](docs/automated-testing.md).
