Include ..\AGENTS.md

# OmniFaction Dev Tool — Mod-Specific Agent Instructions

## Identity
- **Mod ID:** `Calloatti.OmniFactionDevTool`
- **Assembly:** `OmniFactionDevTool`
- **Namespace:** `Calloatti.OmniFactionDevTool`
- **Framework:** Harmony only (no Bindito DI, no UI, no config)
- **Min Game Version:** 1.0.12.5 — uses `timberborn-decompiled-1.0.*`
- **Entry Point:** `ModStarter.cs` — `IModStarter`, `Harmony.PatchAll()`, Harmony ID `"com.mod.unlockallfactionbuildings"`

## What This Mod Does
Developer tool (not for normal play) that unlocks buildings, goods, materials, and needs across **all factions** on a single map for faction-testing. It does this entirely through Harmony patches that make several game services tolerant of the multi-faction/multi-spec layouts the dev map it ships with relies on.

## Source Architecture (`Version-1.0/Source/`)

| File | Role |
|---|---|
| `ModStarter.cs` | `IModStarter` entry point — creates a Harmony instance and `PatchAll()`. |
| `GameFactionSystemPatches.cs` | Five patches on `Faction*CollectionIdsProvider` (`Template`, `Good`, `Material`, `Need`) that **aggregate** collection IDs across all `FactionSpec`s in `FactionService` instead of only the current faction; plus a `FactionNeedService.GetBeaverOrBotNeedById` prefix that safely falls back across beaver/bot need lists. |
| `TemplateCollectionSystemPatches.cs` | `TemplateCollectionService.Load` postfix that **deduplicates** blueprints by both object reference and `TemplateSpec.TemplateName`, keeping the first loaded copy. |
| `TemplateSystemPatches.cs` | `TemplateNameMapper.TryAddTemplate` prefix that forces `throwIfDuplicated = false` to bypass duplicate-name exceptions. |
| `BeaversPatches.cs` | `BeaverFactory.Load` prefix — replaces the `GetSingle<T>()` logic with `FirstOrDefault()` for adult/child specs and caches the instances. `BeaverTextureSetter.Start` prefix — **round-robins the default fur texture per faction** (first element of `Textures`/`ChildTextures`), so Folktails-brown and IronTeeth-gray beavers alternate; the 1-5 texture variants are role-applied later by the game. |
| `BotsPatches.cs` | `BotFactory.Load` prefix — collects **every** `BotSpec.Blueprint`, caches each, and stores them in `Patch_BotFactory_Load.AllBotTemplates`. `BotFactory.Create(Vector3, Quaternion)` prefix — **round-robins** `_botTemplate` through `AllBotTemplates` so all faction bot types spawn (works for any number of factions). |
| `ModularShaftsPatches.cs` | `ShaftFrameFactory.Load` / `ShaftModelFactory.Load` prefixes — re-create the shaft root and part GameObjects from the first `ModularShaftPartsSpec` instead of relying on `GetSingle<T>()`. |
| `GetSingleFactoryPatches.cs` | Three more `GetSingle<T>` crash-sites hardened the same way: `BlockOccupationLayerFactory.Load` (`BlockOccupierSpec`), `RecoveredGoodStackFactory.Load` (`RecoveredGoodStackSpec`, incl. its `Blocks.Single()`), and `PlaneSpawner.Awake` (`PlaneSpec`, re-reads `SpawnPointName` + `FindChildTransform`). |
| `PlantingUIPatches.cs` | `PlantingToolButtonFactory.GetPlanterBuildingName` prefix — uses `FirstOrDefault()` on `PlanterBuildingSpec` and resolves the display name via `ILoc`. |
| `ConditionalLoading.txt` | **Reference/scratch only** — an unrelated conditional-blueprint-loading example. Not compiled; do not treat it as part of the mod. |

## Core Principle
Every patch exists to survive the **duplicated specs / templates** that appear when all factions are loaded into one map. The vanilla code calls `GetSingle<T>()` or throws on duplicate template names; this mod's patches replace those failures with safe `FirstOrDefault()` dedup/handling and **fall back to the original method** (return `true`) whenever the expected data is missing. Do not break that fallback contract.

## Known Pitfalls & Lessons Learned
- **No reflection — rely on publicized assemblies.** All game fields/methods/private setters this mod touches live in assemblies publicized via Krafs.Publicizer (see `Publicize` includes in the mod `.csproj` and in `CommonModSettings.props`), so patches access `_factionSpecService`, `_adultTemplate`, `_botTemplate`, `ShaftFrameFactory.Instantiate`, `TemplateCollectionService.AllTemplates` (private set), etc. **directly** — no `AccessTools`/`TargetMethod` reflection. If the game renames a field, the build fails loudly instead of the patch silently no-oping at runtime.
- The four `Faction*CollectionIdsProvider` postfixes all funnel through one shared helper `FactionCollectionIdsAggregator.CombineWithAllFactions` with a per-provider `Func<FactionSpec, ImmutableArray<string>>` selector.
- **Prefix patches return `bool`** — `false` skips the original, `true` falls back. After assigning template fields (directly, thanks to publicizer), always call `templateInstantiator.CacheInstance(...)`.
- **Dedup keeps the first loaded version** of a template name (`TemplateCollectionSystemPatches.cs`) — the intended behavior is "first wins". Because the collection-provider aggregation seeds the **current faction's** IDs first, the current faction's templates win duplicate-name clashes — do not reorder `FactionCollectionIdsAggregator` or that preference silently flips.
- **Localization:** not used — this is a dev tool with no user-facing UI; do not add localization unless explicitly requested.
- **`ConditionalLoading.txt`** is scratch reference code, namespaced `MyCustomMod`, and is **not** in the build. Ignore it unless it is wired into the `.csproj`.

## Build & Deploy
- Build via `dotnet build` in `Version-1.0/` (project `OmniFaction Dev Tool.csproj`).
- Pre/post build scripts (`prebuild.ps1`/`postbuild.ps1`) handle assembly copying.
- `CommonModSettings.props` defines Timberborn game DLL references and publicizer configuration. The mod's own `.csproj` adds `Publicize` includes for `Timberborn.GameFactionSystem`, `Timberborn.Beavers`, `Timberborn.Bots`, `Timberborn.BlockObstacles`, `Timberborn.RecoveredGoodSystem`, `Timberborn.WonderPlanes`, `Timberborn.ModularShafts`, and `Timberborn.TemplateCollectionSystem`.