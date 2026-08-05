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
| `TemplateCollectionSystemPatches.cs` | `TemplateCollectionService.Load` postfix that **deduplicates** blueprints by both object reference and `TemplateSpec.TemplateName`, keeping the first loaded copy, AND builds `FactionBlueprintCache.TemplateToFactionLocKey` (TemplateName → faction `DisplayNameLocKey`) from faction/collection specs. `LabeledEntity.DisplayName` postfix appends a faction suffix to display names. |
| `TemplateSystemPatches.cs` | `TemplateNameMapper.TryAddTemplate` prefix that forces `throwIfDuplicated = false` to bypass duplicate-name exceptions. |
| `BeaversPatches.cs` | `BeaverFactory.Load` prefix — collects **every** `AdultSpec`/`ChildSpec` blueprint into `Patch_BeaverFactory_Load.AllAdultTemplates`/`AllChildTemplates` and caches each. `BeaverFactory.Create*` prefixes (`CreateAdult`, `CreateChild`, `CreateNewbornAdult`, `CreateAdultFromChild`) **round-robin a factioned template** into `_adultTemplate`/`_childTemplate` before the original runs, so `BeaverAdult.Folktails` / `BeaverAdult.IronTeeth` beavers coexist. `BeaverTextureSetter.Start` prefix — **applies the entity's own faction fur texture** (first element of that faction's `Textures`/`ChildTextures`, matched from the GameObject name), falling back to round-robin for unsuffixed names; the 1-5 texture variants are role-applied later by the game. |
| `BotsPatches.cs` | `BotFactory.Load` prefix — collects **every** `BotSpec.Blueprint`, caches each, and stores them in `Patch_BotFactory_Load.AllBotTemplates`. `Manufactory.IncreaseProductionProgress` prefix/postfix tracks `ActiveManufactory`. `BotFactory.Create(Vector3, Quaternion)` prefix — **spawns the bot matching the producing building's faction** (via `ActiveManufactory` + `FactionAssignmentHelper`), falling back to **round-robin** through `AllBotTemplates` for dev-tool spawns (works for any number of factions). |
| `NeedManagerPatches.cs` | `FactionNeedCache.FactionAllowedNeeds` (FactionId → set of Need Ids) built in a `NeedVerifier.Load` postfix — each faction's set is `faction.NeedCollectionIds` **unioned with `NeedCollection.Common`** (Hunger/Thirst/Sleep/Injury, etc.) so faction-scoped filtering never strips survival needs. `NeedManager.GetNeeds` postfix **filters needs to the entity's own faction** by matching `GameObject.name` against faction Ids, so a Folktails bot/beaver never gets IronTeeth needs (e.g. `Energy`) and vice-versa — this is what stops cross-faction animation crashes like the `Charging` `KeyNotFoundException`. Unsuffixed names ("BeaverAdult"/"BeaverChild") match no faction → unfiltered (intended). |
| `WorkSystemPatches.cs` | `FactionAssignmentHelper.GetFactionID` (TemplateName → `FactionBlueprintCache` locKey, with name fallback) and `CanWorkAt` ("Common" wildcard). `WorkplaceAssigner.AssignStalestUnemployed` / `ReassignWorkersToHigherPriorityWorkplaces` prefixes **restrict bot workplace assignment to the bot's own faction** (beavers are exempt — no faction variants). |
| `TemplateAttachmentPatches.cs` | `TemplateAttachments.GetOrCreateAttachment` prefix — rewrites other-faction names in attachment IDs to the entity's faction, returns cached attachments, and **creates a dummy inactive attachment instead of crashing** when the id isn't in the spec. |
| `ModularShaftsPatches.cs` | `ShaftFrameFactory.Load` / `ShaftModelFactory.Load` prefixes — re-create the shaft root and part GameObjects from the first `ModularShaftPartsSpec` instead of relying on `GetSingle<T>()`. |
| `GetSingleFactoryPatches.cs` | Three more `GetSingle<T>` crash-sites hardened the same way: `BlockOccupationLayerFactory.Load` (`BlockOccupierSpec`), `RecoveredGoodStackFactory.Load` (`RecoveredGoodStackSpec`, incl. its `Blocks.Single()`), and `PlaneSpawner.Awake` (`PlaneSpec`, re-reads `SpawnPointName` + `FindChildTransform`). |
| `PlantingUIPatches.cs` | `PlantingToolButtonFactory.GetPlanterBuildingName` prefix — uses `FirstOrDefault()` on `PlanterBuildingSpec` and resolves the display name via `ILoc`. |
| `ConditionalLoading.txt` | **Reference/scratch only** — an unrelated conditional-blueprint-loading example. Not compiled; do not treat it as part of the mod. |

## Content Blueprints (`Characters/` and `TemplateCollections/` folders at mod root)

The mod ships four **factioned beaver blueprints** plus two **template-collection overrides** to give new beavers faction-specific needs and fur. These live at the mod root (`Version-1.0/Characters/Beaver/...`, `Version-1.0/TemplateCollections/...`) — NOT under a `Blueprints/` folder.

- `Characters/Beaver/BeaverAdult.Folktails.blueprint.json`, `BeaverAdult.IronTeeth.blueprint.json`, `BeaverChild.Folktails.blueprint.json`, `BeaverChild.IronTeeth.blueprint.json` — exact copies of the vanilla `BeaverAdult.blueprint.json`/`BeaverChild.blueprint.json` with **only** `TemplateSpec.TemplateName` suffixed (e.g. `BeaverAdult.Folktails`). The spawned `GameObject.name` then embeds the faction, which is what the `NeedManager.GetNeeds` filter and the `BeaverTextureSetter` faction-match rely on. `WellbeingTierService` matches tiers by component, not template name, so the renames are safe.
- **Regenerating:** copy from `timberborn-decompiled-1.0.13.1-b769e88-sw\Blueprints\Characters\Beaver\Beaver{Adult,Child}.blueprint.json` and replace `"TemplateName": "Beaver{Adult,Child}",` → `"TemplateName": "Beaver{Adult,Child}.<Faction>",`. Nothing else in the file changes — both factions' hats/attachments and the shared model are already in the vanilla template.
- `TemplateCollections/TemplateCollection.Characters.{Folktails,IronTeeth}.blueprint.json` — overrides that use **`Blueprints#append`** to add the factioned beavers to the per-faction character collection. **Do not overwrite the `Blueprints` array wholesale** — the shared `BeaverAdult.blueprint`/`BeaverChild.blueprint` must stay loadable because **existing saves reference those template names**; replacing the list deletes all beavers on save load. The `#append` keyword (`Timberborn.SerializationSystem.cs` `JsonKeywords`) merges the array items into the vanilla list.
- Because `PickTemplate` filters the round-robin pool to `IsFactioned` templates, new beavers always spawn from the factioned copies; the shared templates only exist for save compatibility.
- Deploying the folders is handled outside the build (copy `Characters/` and `TemplateCollections/` into the deployed mod folder); the pre/post build scripts copy the compiled `bin` output only.

## Core Principle
Every patch exists to survive the **duplicated specs / templates** that appear when all factions are loaded into one map. The vanilla code calls `GetSingle<T>()` or throws on duplicate template names; this mod's patches replace those failures with safe `FirstOrDefault()` dedup/handling and **fall back to the original method** (return `true`) whenever the expected data is missing. Do not break that fallback contract.

## Known Pitfalls & Lessons Learned
- **No reflection — rely on publicized assemblies.** All game fields/methods/private setters this mod touches live in assemblies publicized via Krafs.Publicizer (see `Publicize` includes in the mod `.csproj` and in `CommonModSettings.props`), so patches access `_factionSpecService`, `_adultTemplate`, `_botTemplate`, `ShaftFrameFactory.Instantiate`, `TemplateCollectionService.AllTemplates` (private set), `FactionSpec.DisplayNameLocKey`, etc. **directly** — no `AccessTools`/`Traverse`/`TargetMethod` reflection. If the game renames a field, the build fails loudly instead of the patch silently no-oping at runtime.
- `Timberborn.FactionSystem` is publicized specifically to expose `FactionSpec.DisplayNameLocKey` (a `private` property) for the `FactionBlueprintCache` build — do not remove it from the `.csproj`.
- The four `Faction*CollectionIdsProvider` postfixes all funnel through one shared helper `FactionCollectionIdsAggregator.CombineWithAllFactions` with a per-provider `Func<FactionSpec, ImmutableArray<string>>` selector.
- **Prefix patches return `bool`** — `false` skips the original, `true` falls back. After assigning template fields (directly, thanks to publicizer), always call `templateInstantiator.CacheInstance(...)`.
- **Dedup keeps the first loaded version** of a template name (`TemplateCollectionSystemPatches.cs`) — the intended behavior is "first wins". Because the collection-provider aggregation seeds the **current faction's** IDs first, the current faction's templates win duplicate-name clashes — do not reorder `FactionCollectionIdsAggregator` or that preference silently flips.
- **Faction matching is by string substrings** (`IndexOf("Folktails"/"IronTeeth")`). Bot/beaver template names embed the faction (`Bot.Folktails`), `GameObject.name` carries it at `Awake` time (instantiation sets `name = blueprint.Name` before `NeedManager.Awake`), and buildings' `TemplateSpec.TemplateName` resolves through `FactionBlueprintCache`. `FactionNeedCache`/`FactionBlueprintCache` are populated in `ILoadableSingleton.Load` postfixes (`NeedVerifier`, `TemplateCollectionService`), so they are ready before any entity spawns.
- **Localization:** not used — this is a dev tool with no user-facing UI; do not add localization unless explicitly requested.
- **`ConditionalLoading.txt`** is scratch reference code, namespaced `MyCustomMod`, and is **not** in the build. Ignore it unless it is wired into the `.csproj`.

## Build & Deploy
- Build via `dotnet build` in `Version-1.0/` (project `OmniFaction Dev Tool.csproj`).
- Pre/post build scripts (`prebuild.ps1`/`postbuild.ps1`) handle assembly copying.
- `CommonModSettings.props` defines Timberborn game DLL references and publicizer configuration. The mod's own `.csproj` adds `Publicize` includes for `Timberborn.GameFactionSystem`, `Timberborn.FactionSystem`, `Timberborn.Beavers`, `Timberborn.Bots`, `Timberborn.BlockObstacles`, `Timberborn.RecoveredGoodSystem`, `Timberborn.WonderPlanes`, `Timberborn.ModularShafts`, `Timberborn.TemplateCollectionSystem`, `Timberborn.TemplateAttachmentSystem`, and `Timberborn.WorkSystem`.