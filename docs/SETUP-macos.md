# Development setup (macOS, Apple Silicon)

The Valheim modding wiki and the JötunnModStub README both assume Windows. This records
what was actually done on this machine, and why each step differs.

Verified on: macOS 27, arm64, Valheim 1.0 on Unity 6000.0.75f1, Jötunn 2.27.1,
BepInEx 5.4.23.5 (Thunderstore pack `5.4.2350`).

## Toolchain

```sh
brew install dotnet     # .NET SDK 10.0.400, no sudo, installs to /opt/homebrew
```

Visual Studio for Mac is discontinued — use Rider or VS Code + C# Dev Kit if you want an
IDE. Neither is required; `dotnet build` is enough.

No .NET Framework 4.8 targeting pack is needed. The project targets `net48`, but every
reference is a direct `HintPath` to a game or BepInEx assembly, so the SDK never asks for
a targeting pack.

## Game paths

macOS puts the managed assemblies inside the app bundle, not in a `Valheim_Data` folder:

```
~/Library/Application Support/Steam/steamapps/common/Valheim/
├── valheim.app/Contents/Resources/Data/Managed/   <- the real assemblies
├── Valheim_Data/Managed/                          <- our shim (see below)
├── BepInEx/
└── start_game_bepinex.sh
```

Note the wiki's `<Choose>` snippet suggests `.../Valheim/Contents/MacOS` for macOS. That
path does not exist. Jötunn's own `Paths.props` has the same stale default, which is why
`Environment.props` overrides `VALHEIM_INSTALL` explicitly.

## The Valheim_Data shim

`JotunnLibRefsCorlib.props` hardcodes `$(VALHEIM_INSTALL)\Valheim_Data\Managed\...` for
roughly ninety references, and `JotunnBuildTask` looks for a literal `Valheim_Data`
folder. Rather than fork all of that, we present the Windows layout to the build:

```sh
V="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
REAL="$V/valheim.app/Contents/Resources/Data/Managed"
mkdir -p "$V/Valheim_Data/Managed"
for f in "$REAL"/*.dll; do ln -sf "$f" "$V/Valheim_Data/Managed/$(basename "$f")"; done
```

Each assembly is symlinked **individually**, rather than symlinking the `Managed`
directory itself. This is deliberate: Jötunn's publicizer writes `publicized_assemblies/`
next to the assemblies it reads, and a directory symlink would put that output inside
`valheim.app`, invalidating the bundle's code signature. With per-file links, the
generated folder lands in our shim directory and the bundle is never written to.

MSBuild resolves the `\` separators in those HintPaths correctly on macOS, so no other
changes were needed.

Check the signature is intact after any change to the game folder:

```sh
codesign --verify --strict "$V/valheim.app"
```

## Publicized assemblies

`DoPrebuild.props` is set to `true`, so Jötunn regenerates the publicized assemblies on
every build. **Rerun a build after each Valheim update** — the publicized DLLs are derived
from the game's, and stale ones cause confusing compile errors.

## Environment.props

Gitignored, so it is not in the repo. Recreate it at the solution root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <VALHEIM_INSTALL>$(HOME)/Library/Application Support/Steam/steamapps/common/Valheim</VALHEIM_INSTALL>
    <VALHEIM_MANAGED>$(VALHEIM_INSTALL)/valheim.app/Contents/Resources/Data/Managed</VALHEIM_MANAGED>
    <BEPINEX_PATH>$(VALHEIM_INSTALL)/BepInEx</BEPINEX_PATH>
    <MOD_DEPLOYPATH>$(BEPINEX_PATH)/plugins</MOD_DEPLOYPATH>
  </PropertyGroup>
</Project>
```

Jötunn's `Paths.props` imports this from `$(SolutionDir)`, so **build the solution, not
the project** — `dotnet build Kukolony/Kukolony.csproj` leaves `SolutionDir` undefined and
the file is silently skipped.

## BepInEx on Apple Silicon

Two things make this work that usually don't:

**Entitlements.** Valheim ships hardened-runtime with
`com.apple.security.cs.allow-dyld-environment-variables` and
`com.apple.security.cs.disable-library-validation` both set. Those are exactly what
`DYLD_INSERT_LIBRARIES` injection needs. Without them BepInEx could not load at all.

**Architecture.** `valheim.app` is a universal binary and runs **arm64 natively** — it is
not a Rosetta x64 build. The doorstop shipped in BepInExPack_Valheim 5.4.2350 is still
Doorstop 4.4.0, x86_64-only, so it silently fails to inject. Fixed by dropping in the
universal build from Doorstop 4.5.0:

```sh
# from https://github.com/NeighTools/UnityDoorstop/releases -> doorstop_macos_release_4.5.0.zip
cp universal/libdoorstop.dylib "$V/doorstop_libs/libdoorstop_x64.dylib"
```

The name stays `libdoorstop_x64.dylib` because `start_game_bepinex.sh` derives the name
from `file` output, which reports a universal binary as 64-bit and picks `x64`. The
original is kept alongside as `libdoorstop_x64.dylib.orig-4.4.0-x86_64`. Doorstop 4.5.0's
env-var interface is a superset of 4.4.0's, so BepInEx's preloader is unaffected.

Also set in the launcher:

```sh
executable_name="valheim.app"    # was valheim.x86_64
```

**A pack update will overwrite both changes.** Re-apply them after updating BepInEx. In
particular, retain the universal Doorstop binary while upgrading the BepInEx `core/`
assemblies. BepInEx 5.4.23.5 adds a macOS arm64 guard around the preloader console/runtime
fixes required by Valheim 1.0, so upgrade the core rather than rolling it back.

## Launching

Set Valheim's Steam launch options to:

```
"/Users/kuku/Library/Application Support/Steam/steamapps/common/Valheim/start_game_bepinex.sh" %command%
```

Logs go to `BepInEx/LogOutput.log`. A successful load shows the plugin listed by GUID.

## Build

```sh
dotnet build Kukolony.sln -c Debug
```

Debug builds copy `Kukolony.dll` and `.pdb` straight to `BepInEx/plugins/Kukolony/`.
Release builds produce a Thunderstore-ready zip instead.

Jötunn must also be installed as a mod for the plugin to load — `Jotunn.dll` goes in
`BepInEx/plugins/`, from the Thunderstore package `ValheimModding-Jotunn`.
