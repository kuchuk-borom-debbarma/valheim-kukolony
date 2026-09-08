# Modding basics

Conventions this project follows, and the Valheim-specific traps behind them. Distilled
from the [Valheim modding wiki](https://github.com/Valheim-Modding/Wiki/wiki) —
*Creating Your First Mod*, *Best Practices*, and *Advanced Practices and Tools* — plus
what we verified ourselves. The wiki is Windows-centric; see `SETUP-macos.md` for the
substitutions.

## The stack

**BepInEx 5** is the mod loader. A plugin is a class deriving from `BaseUnityPlugin`,
tagged with `[BepInPlugin(guid, name, version)]`, whose `Awake()` runs at load.

**HarmonyX** — not stock Harmony 2 — does the patching. Behaviour differs in places, so
prefer the [HarmonyX docs](https://github.com/BepInEx/HarmonyX/wiki) when the two
disagree.

**Jötunn** wraps the tedious parts: registering creatures, items, pieces, recipes,
localisation, config sync. `[BepInDependency(Jotunn.Main.ModGuid)]` guarantees load order.

## Patching

```csharp
[HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
public static class Patch_Player_UseStamina
{
    private static bool Prefix() => false;   // false skips the original method
}
```

A `Prefix` runs before the original and can suppress it by returning `false`; a `Postfix`
runs after. `Harmony.PatchAll(Assembly.GetExecutingAssembly())` in `Awake` picks up every
annotated class.

**Never call `UnpatchAll()` with no argument.** It unpatches *every mod in the process*,
not just yours, which shuts other mods down mid-run and can corrupt saves. If you must
unpatch, pass your own harmony id: `UnpatchAll("com.kuku.kukolony")`. There is also no
reason to unpatch on shutdown — do not do it in `OnDestroy`.

## Private members

We publicize the game assemblies (`DoPrebuild.props` → `true`), so private and protected
members are directly callable. `Properties/IgnoreAccessModifiers.cs` carries the
`SecurityPermission(SkipVerification)` attribute that makes the Mono JIT skip the
visibility check at runtime.

This matters for us: `BaseAI.MoveTo` is `protected`, and we call it directly.

The alternative — `AccessTools.Method(typeof(X), "Y").Invoke(...)` — is only worth it for
one-off access in a mod that does not publicize.

## Logging

Use Jötunn's logger (or a BepInEx `ManualLogSource`), never `Debug.Log` or `ZLog.Log`.
Those are indistinguishable from vanilla output in `LogOutput.log`, which makes a bug
report useless.

```csharp
Jotunn.Logger.LogInfo("...");    // normal, user-facing
Jotunn.Logger.LogDebug("...");   // hidden by default in the Valheim BepInEx pack
Jotunn.Logger.LogWarning("..."); // needs attention, e.g. "update your config"
Jotunn.Logger.LogError("...");   // something broke and the mod will misbehave
```

Prefixed output looks like `[Info : Kukolony.Kukolony] ...` — that is how we confirmed the
plugin loads.

## Configuration

Use BepInEx's `Config.Bind` rather than a bespoke file — users already know it, and the
in-game Configuration Manager picks it up for free.

```csharp
Config.SaveOnConfigSet = false;                       // don't write once per Bind
var entry = Config.Bind("1 - General", "Key", true, "description");
Config.Save();
Config.SaveOnConfigSet = true;
```

Two wiki notes worth keeping:

- **Sections sort alphabetically.** Prefix them (`1 - General`, `2 - Advanced`) to control
  the order shown to users.
- **`FileSystemWatcher` for live reload fires multiple events per save** and is unreliable
  on Linux. Debounce with a timestamp (~1s) if we add live reload.

For multiplayer, config must be server-authoritative — Jötunn's `SynchronizationManager`,
see `multiplayer.md`.

## The Unity null trap

**Do not use `?.`, `??`, or `?[]` on anything deriving from `UnityEngine.Object`.**

Unity overloads `==` so a destroyed object compares equal to null. The C# null-propagation
operators bypass that overload, so a destroyed object passes as non-null and you get a
`NullReferenceException` from inside `GetComponentFastPath`.

```csharp
// wrong - destroyed GameObject slips through
if (item.GetComponent<ItemDrop>()?.m_itemData is { } data) { }

// right
if (item.TryGetComponent(out ItemDrop drop) && drop.m_itemData is { } data) { }
```

Use `TryGetComponent`, or an explicit `if (obj != null)` / `if ((bool)obj)`. This one is
easy to get away with for a long time and then fail in the field.

## Dependencies

```csharp
[BepInDependency(Jotunn.Main.ModGuid)]                 // load after Jotunn
[BepInIncompatibility("com.example.conflicting")]      // refuse to load alongside
```

Incompatibility is worth declaring only for genuinely game-breaking overlap — two mods
registering the same prefab, for instance. Do not use it defensively.

## After a game update

1. Rebuild. `DoPrebuild.props` is `true`, so Jötunn regenerates the publicized assemblies
   from the new game DLLs. Stale publicized assemblies produce confusing compile errors.
2. Re-apply the macOS BepInEx tweaks if the pack itself updated — see `SETUP-macos.md`.
3. Re-decompile `.reference/assembly_valheim.decompiled.cs` if any documented behaviour
   looks wrong. Everything in `api-notes.md` is version-specific.
