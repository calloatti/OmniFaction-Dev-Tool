# OmniFaction Dev Tool — Version 1.1 Port Notes

## Target
- **Game version:** 1.1.x — decompiled source at `C:\Users\calloatti\source\repos\timberborn-decompiled-1.1.1.1-cfb778f-xsw`
- **Game DLLs:** `C:\Program Files (x86)\Steam\steamapps\common\timberborn_experimental\Timberborn_Data\Managed` (set as `TimberbornPath` in the csproj)
- **Manifest:** `MinimumGameVersion` bumped to `1.1.0.0` in `manifest.json`.
- The `Source/` and content folders are otherwise identical to `Version-1.0/` until the porting fixes below are applied.

## In-Progress Feature: Per-Faction Population Counter
The design doc lives at the repo root: **`../population-counter.md`** — read it before touching these files. **Implemented in this fork:** `FactionPopulationService.cs` (Bindito DI singleton) + `PopulationCounterPatches.cs` (Harmony postfix on `PopulationCounter.Sample`). The service registers with the `EventBus` in `ILoadableSingleton.Load()` and relies on queued event delivery at `EventBus.PostLoad()` (no `ShowPrimaryUIEvent` re-seed). The events it subscribes to that live in **publicized** assemblies require `DoNotPublicize` items in the csproj (see the csproj comment and root `AGENTS.md` Known Pitfalls) — `Contaminable.ContaminationChanged` and `BlockableObject.ObjectBlocked/ObjectUnblocked` do not, as their assemblies are unpublicized.

## Feature changes that must also land in `Version-1.0`
This fork has feature changes that `Version-1.0` does not have yet: the **beaver faction-workplace restriction** (WorkSystemPatches.cs) and the **beaver faction-dwelling restriction** (DwellingSystemPatches.cs). `Version-1.0/AGENTS.md` tracks the back-port checklist — read it before touching `Version-1.0/Source/`.

## Porting fixes applied (one at a time, verify build after each)
- [x] Create this file and record findings.
- [x] Fix `BeaverTextureSetter` patch target: `Start()` → `InitializeEntity()` (BeaversPatches.cs).
- [x] Fix `BotFactory.Create` overload: `(Vector3, Quaternion)` → `(Vector3, Quaternion, object)` (BotsPatches.cs).
- [x] Fix `RecoveredGoodStackFactory` patch: `_recoveredGoodStackTemplate` is now a `Blueprint` (GetSingleFactoryPatches.cs).
- [x] Disambiguate `BeaverFactory.CreateChild` patch target (1.1 added a private `CreateChild` overload) (BeaversPatches.cs).
- [x] Regenerate factioned beaver blueprints from the 1.1 vanilla blueprint (Characters/Beaver/*.blueprint.json).
- [x] Update root `AGENTS.md` for the 1.1 fork.
- [x] Implement per-faction Population Counter per `../population-counter.md` (`FactionPopulationService.cs` + `PopulationCounterPatches.cs`) — **1.1 only**; mirror to `Version-1.0/Source/` is tracked by the `Version-1.0/AGENTS.md` back-port checklist.

## Game API drift 1.0.13.1 → 1.1.1.1 (compile / patch-target breaking)

### 1. `BeaverTextureSetter` — entry point renamed
- 1.0: `BeaverTextureSetter : BaseComponent, IStartableComponent` — `public void Start()`
- 1.1: `BeaverTextureSetter : BaseComponent, IInitializableEntity` — `public void InitializeEntity()`
- Fix: change `[HarmonyPatch(typeof(BeaverTextureSetter), nameof(BeaverTextureSetter.Start))]` to `nameof(BeaverTextureSetter.InitializeEntity)`.
- Prefix logic unchanged (reads `CharacterMaterialModifier`, `Child`, `_factionService._factionSpecService`, `FactionSpec.Textures/ChildTextures`).

### 2. `BotFactory.Create` — production overload gained an init component
- 1.0: `public Bot Create(Vector3 position, Quaternion rotation)`
- 1.1: `public void Create(Vector3 position, Quaternion rotation, object initComponent)`; `Create(Vector3)` delegates to it.
- Bot production now flows `BotManufactory.OnProductionFinished` (subscribes `_manufactory.ProductionFinished` in `IInitializableEntity`) → `_botFactory.Create(pos, rot, CharacterBirthInit)`. The `Manufactory.ProductionFinished` event is still raised synchronously inside `IncreaseProductionProgress`, so `Patch_Manufactory_IncreaseProductionProgress.ActiveManufactory` tracking still works unchanged.
- Fix: patch target overload list must be `new[] { typeof(Vector3), typeof(Quaternion), typeof(object) }`.

### 3. `RecoveredGoodStackFactory._recoveredGoodStackTemplate` — type changed to Blueprint
- 1.0: `private BlockObjectSpec _recoveredGoodStackTemplate;` and `Load` did `GetSingle<RecoveredGoodStackSpec>().GetSpec<BlockObjectSpec>()`.
- 1.1: `private Blueprint _recoveredGoodStackTemplate;` and `Load` does `GetSingle<RecoveredGoodStackSpec>().Blueprint`, then `_recoveredGoodStackTemplate.GetSpec<BlockObjectSpec>().Blocks.Single()`.
- Fix: assign `__instance._recoveredGoodStackTemplate = recoveredGoodStackSpec.Blueprint;` and `__instance.GoodStackBlockSpec = recoveredGoodStackSpec.Blueprint.GetSpec<BlockObjectSpec>().Blocks.FirstOrDefault();`.

### 4. `BeaverFactory.CreateChild` — now ambiguous
- 1.0: single `public Beaver CreateChild(Vector3 position, float childhoodProgress)`.
- 1.1: `public void CreateChild(Vector3, float)` plus a new private overload `private void CreateChild(EntitySetup.Builder, Vector3, float)`.
- Fix: `[HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.CreateChild), new[] { typeof(Vector3), typeof(float) })]`.

## Content blueprint drift (factioned beaver copies)
- 1.1 vanilla `Beaver{Adult,Child}.blueprint.json` changed vs 1.0:
  - `CharacterBirthNotifierSpec` → `CharacterBirthSpec` (NotificationLocKey unchanged).
  - `DeadStatusSpec`: `DeadStatusLocKey` split into `DiedOldAgeStatusLocKey`, `DiedTragicallyStatusLocKey`, `DiedTragicallyAlertLocKey`.
  - Animator: new Jumping/Punching/Stepping states; `Massaging` now `Looped: true`.
  - New attachments `ScavengerHat.Beaver.Folktails` / `ScavengerHat.Beaver.IronTeeth`.
- The four factioned copies under `Characters/Beaver/` were **regenerated** from the **1.1** vanilla blueprint (they previously carried 1.0 content, e.g. `CharacterBirthNotifierSpec`). Only `TemplateSpec.TemplateName` was suffixed (`BeaverAdult.Folktails`, `BeaverAdult.IronTeeth`, `BeaverChild.Folktails`, `BeaverChild.IronTeeth`); nothing else changes — verified via `git diff --no-index` that only the `TemplateName` line differs from the 1.1 vanilla blueprint. If they are ever regenerated again, repeat that procedure from `timberborn-decompiled-1.1.1.1-cfb778f-xsw\Blueprints\Characters\Beaver\Beaver{Adult,Child}.blueprint.json`.

## Verified unchanged between 1.0 and 1.1 (no edit needed)
- `BeaverFactory.Load`, `CreateAdult`, `CreateNewbornAdult`, `CreateAdultFromChild`; `NewbornSpawner.SpawnAdult/SpawnChild`.
- `Manufactory.IncreaseProductionProgress`; bot creation flow (see item 2).
- `FactionSystem`: `FactionSpec` (Id, Textures/ChildTextures, Material/Template/Need/Good CollectionIds, private `DisplayNameLocKey`), `FactionSpecService.Factions`, `FactionService._factionSpecService`/`Current`.
- `GameFactionSystem`: the four `Faction*CollectionIdsProvider` getters, `FactionNeedService.GetBeaverOrBotNeedById/GetBeaverNeeds/GetBotNeeds`, `NeedVerifier.Load` (`_factionSpecService`, `_specService`).
- `TemplateSystem`: `TemplateNameMapper.TryAddTemplate`, `TemplateService.GetAll<T>/GetSingle<T>`, `TemplateSpec.TemplateName`.
- `TemplateCollectionSystem`: `TemplateCollectionService.Load`/`AllTemplates`/`_specService`, `TemplateCollectionSpec.Blueprints`.
- `TemplateAttachmentSystem`: `TemplateAttachments.GetOrCreateAttachment`, `_attachmentCache`, `_templateAttachmentsSpec`.
- `NeedSystem`: `NeedManager.GetNeeds` (private). `NeedSpecs`, `NeedCollectionSystem` unchanged.
- `BlockOccupationLayerFactory` (`_blockOccupierTemplate` still `BlockObjectSpec`), `PlaneSpawner` (`_planeTemplate` still `Blueprint`).
- `ModularShafts`: `ShaftFrameFactory.Load` (`_root`, `_shaftBase/_shaftLowerFrame/_shaftSupport/_shaftFrame`, private `Instantiate(GameObject, Transform)`), `ShaftModelFactory.Load` (`_modularShaftPartsSpec`).
- `EntitySystem`: `LabeledEntity.get_DisplayName`, `_displayName`, `_loc`.
- `Wellbeing`: `WellbeingService.AppliedNeeds` (private static), `WellbeingTrackerRegistrar`. `WellbeingUI`: `PopulationWellbeingBox.UpdateCounters`/`_counters`/`_appliedCount`, `PopulationWellbeingCounter.UpdateValues`.
- `WorkSystem`: `WorkplaceAssigner.Assign`, `PriorityOrderedWorkplaces._workplaces` (SortedList), `UnemployedWorkers._unemployed`, all `Workplace` members used.
- `PlantingUI`: `PlantingToolButtonFactory.GetPlanterBuildingName` (`_templateService`, `_loc`).
- `ModManagerScene`: `IModStarter`, `IModEnvironment` (same namespaces). `CharacterMaterialModifier.SetTexture(int, Texture)`.
- `BlueprintSystem`: `Blueprint.GetSpec<T>`, `ComponentSpec.Blueprint`, `ISpecService.GetBlueprint/GetSpecs<T>`.
