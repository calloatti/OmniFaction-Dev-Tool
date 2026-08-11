using Bindito.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Timberborn.AssetSystem;
using Timberborn.BaseComponentSystem;
using Timberborn.BeaverContaminationSystem;
using Timberborn.Beavers;
using Timberborn.BlockingSystem;
using Timberborn.BlockSystem;
using Timberborn.Bots;
using Timberborn.BottomBarSystem;
using Timberborn.Buildings;
using Timberborn.Characters;
using Timberborn.DwellingSystem;
using Timberborn.GameDistricts;
using Timberborn.GameFactionSystem;
using Timberborn.GameSceneLoading;
using Timberborn.Goods;
using Timberborn.InputSystem;
using Timberborn.NewGameConfigurationSystem;
using Timberborn.Population;
using Timberborn.SceneLoading;
using Timberborn.SimpleOutputBuildings;
using Timberborn.SingletonSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using Timberborn.UILayoutSystem;
using Timberborn.WorkSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace Calloatti.OmniFaction
{
  [Context("Game")]
  public class OmniFactionConfigurator : Configurator
  {
    protected override void Configure()
    {
      Bind<OmniFactionService>().AsSingleton();
      Bind<FactionToolFilter>().AsSingleton();
      Bind<FactionFilterButton>().AsSingleton();
      MultiBind<BottomBarModule>().ToProvider<FactionFilterModuleProvider>().AsSingleton();
    }

    private class FactionFilterModuleProvider : IProvider<BottomBarModule>
    {
      private readonly FactionFilterButton _factionFilterButton;

      public FactionFilterModuleProvider(FactionFilterButton factionFilterButton)
      {
        _factionFilterButton = factionFilterButton;
      }

      public BottomBarModule Get()
      {
        BottomBarModule.Builder builder = new BottomBarModule.Builder();
        builder.AddRightSectionElement(_factionFilterButton);
        return builder.Build();
      }
    }
  }

  public class OmniFactionService : ILoadableSingleton, IDisposable
  {
    public static OmniFactionService Instance { get; private set; }
    public static bool StartupComplete { get; private set; }
    public static string CurrentFaction => _factionService?.Current?.Id;

    private static FactionService _factionService;
    private static DistrictCenterRegistry _districtCenterRegistry;

    // Entity faction cache (keyed by instance ID)
    private static readonly Dictionary<int, string> _entityFactionCache = new Dictionary<int, string>();

    private readonly EventBus _eventBus;
    private readonly BeaverFactory _beaverFactory;
    private readonly ISceneLoader _sceneLoader;
    private readonly GameModeSpecService _gameModeSpecService;
    private readonly IAssetLoader _assetLoader;

    // Tool-button faction backgrounds (no color, just sprites)
    private readonly Dictionary<ITool, (VisualElement Background, Sprite NormalSprite, Sprite HotSprite)> _customToolBackgrounds
        = new Dictionary<ITool, (VisualElement, Sprite, Sprite)>();

    // Cached faction sprites
    private readonly Dictionary<string, (Sprite Normal, Sprite Hot)> _factionSprites = new Dictionary<string, (Sprite, Sprite)>();

    // Tallies (unchanged)
    private readonly Dictionary<string, FactionTally> _globalTallies = new Dictionary<string, FactionTally>();
    private readonly Dictionary<DistrictCenter, Dictionary<string, FactionTally>> _districtTallies = new Dictionary<DistrictCenter, Dictionary<string, FactionTally>>();
    private readonly Dictionary<Character, CharacterRecord> _characters = new Dictionary<Character, CharacterRecord>();
    private readonly Dictionary<Dwelling, BuildingRecord> _dwellings = new Dictionary<Dwelling, BuildingRecord>();
    private readonly Dictionary<Workplace, BuildingRecord> _workplaces = new Dictionary<Workplace, BuildingRecord>();
    private readonly HashSet<BlockObject> _wiredBuildings = new HashSet<BlockObject>();

    [Inject]
    public OmniFactionService(EventBus eventBus, BeaverFactory beaverFactory, ISceneLoader sceneLoader, GameModeSpecService gameModeSpecService, FactionService factionService, DistrictCenterRegistry districtCenterRegistry, IAssetLoader assetLoader)
    {
      _eventBus = eventBus;
      _beaverFactory = beaverFactory;
      _sceneLoader = sceneLoader;
      _gameModeSpecService = gameModeSpecService;
      _factionService = factionService;
      _districtCenterRegistry = districtCenterRegistry;
      _assetLoader = assetLoader;
    }

    public void Load()
    {
      Instance = this;
      StartupComplete = false;
      _eventBus.Register(this);
      _entityFactionCache.Clear();

      LoadFactionSprites();

      // Process any pending tool buttons that were created before the service was ready.
      FactionBackgroundQueue.ProcessPending();
    }

    public void Dispose()
    {
      _eventBus.Unregister(this);
      Instance = null;
      _entityFactionCache.Clear();
    }

    // ---- Load pre-tinted sprites per faction ----
    private void LoadFactionSprites()
    {
      // Get all distinct faction IDs in a stable order
      var factionIds = FactionBlueprintCache.TemplateToFactionId.Values.Distinct().ToList();

      string[] fallbackIndices = { "00", "01", "02" };
      int fallbackIndex = 0;

      foreach (string factionId in factionIds)
      {
        string normalPath;
        string hotPath;

        if (string.Equals(factionId, "Folktails", StringComparison.OrdinalIgnoreCase))
        {
          normalPath = "Sprites/BottomBar/subbutton-bg-folktails-normal";
          hotPath = "Sprites/BottomBar/subbutton-bg-folktails-hot";
        }
        else if (string.Equals(factionId, "IronTeeth", StringComparison.OrdinalIgnoreCase))
        {
          normalPath = "Sprites/BottomBar/subbutton-bg-ironteeth-normal";
          hotPath = "Sprites/BottomBar/subbutton-bg-ironteeth-hot";
        }
        else
        {
          string index = fallbackIndices[fallbackIndex % fallbackIndices.Length];
          fallbackIndex++;
          normalPath = $"Sprites/BottomBar/subbutton-bg-{index}-normal";
          hotPath = $"Sprites/BottomBar/subbutton-bg-{index}-hot";
        }

        Sprite normalSprite = _assetLoader.Load<Sprite>(normalPath);
        Sprite hotSprite = _assetLoader.Load<Sprite>(hotPath);

        _factionSprites[factionId] = (normalSprite, hotSprite);
      }
    }

    // ---- Public API for getting sprites (optional) ----
    public (Sprite Normal, Sprite Hot) GetFactionSprites(string factionId)
    {
      if (string.IsNullOrEmpty(factionId) || !_factionSprites.TryGetValue(factionId, out var sprites))
        return (null, null);
      return sprites;
    }

    // ---- Tool-button faction backgrounds ----
    public bool TryApplyFactionBackground(ToolButton toolButton, string templateName)
    {
      if (toolButton == null || string.IsNullOrEmpty(templateName))
        return false;

      if (!FactionBlueprintCache.TemplateToFactionId.TryGetValue(templateName, out string factionId))
        return false;

      VisualElement background = toolButton.Root?.Q<VisualElement>("Background");
      if (background == null)
        return false;

      if (!_factionSprites.TryGetValue(factionId, out var sprites))
        return false;

      // Apply normal sprite (no tint)
      background.style.backgroundImage = new StyleBackground(sprites.Normal);
      background.style.unityBackgroundImageTintColor = StyleKeyword.Null;

      _customToolBackgrounds[toolButton.Tool] = (background, sprites.Normal, sprites.Hot);

      return true;
    }

    [OnEvent]
    public void OnToolEntered(ToolEnteredEvent e)
    {
      if (_customToolBackgrounds.TryGetValue(e.Tool, out var entry))
      {
        var (background, normalSprite, hotSprite) = entry;
        background.style.backgroundImage = new StyleBackground(hotSprite ?? normalSprite);
        background.style.unityBackgroundImageTintColor = StyleKeyword.Null;
      }
    }

    [OnEvent]
    public void OnToolExited(ToolExitedEvent e)
    {
      if (_customToolBackgrounds.TryGetValue(e.Tool, out var entry))
      {
        var (background, normalSprite, hotSprite) = entry;
        background.style.backgroundImage = new StyleBackground(normalSprite);
        background.style.unityBackgroundImageTintColor = StyleKeyword.Null;
      }
    }

    // ---- Entity faction cache (unchanged) ----
    public static void SetFactionForEntity(GameObject entity, string faction)
    {
      if (entity == null || string.IsNullOrEmpty(faction)) return;
      int id = entity.GetInstanceID();
      _entityFactionCache[id] = faction;
    }

    public static void RemoveFactionForEntity(GameObject entity)
    {
      if (entity == null) return;
      _entityFactionCache.Remove(entity.GetInstanceID());
    }

    public static string GetCachedFaction(GameObject entity)
    {
      if (entity == null) return null;
      _entityFactionCache.TryGetValue(entity.GetInstanceID(), out string faction);
      return faction;
    }

    // ---- Find nearest district faction (unchanged) ----
    public static string FindNearestDistrictFaction(Vector3 position)
    {
      if (_districtCenterRegistry == null) return null;
      DistrictCenter nearest = null;
      float nearestDist = float.PositiveInfinity;
      foreach (DistrictCenter dc in _districtCenterRegistry.FinishedDistrictCenters)
      {
        if (dc == null) continue;
        float dist = Vector3.Distance(position, dc.Transform.position);
        if (dist < nearestDist)
        {
          nearestDist = dist;
          nearest = dc;
        }
      }
      if (nearest == null) return null;
      string faction = FactionAssignmentHelper.GetFactionID(nearest);
      return faction == "Common" ? null : faction;
    }

    // ---- Query API (unchanged) ----
    public PopulationData GetGlobal(string faction)
    {
      PopulationData populationData = new PopulationData();
      if (_globalTallies.TryGetValue(faction, out FactionTally tally))
        FillPopulationData(populationData, tally);
      return populationData;
    }

    public PopulationData GetDistrict(DistrictCenter districtCenter, string faction)
    {
      PopulationData populationData = new PopulationData();
      if (districtCenter != null
          && _districtTallies.TryGetValue(districtCenter, out Dictionary<string, FactionTally> perFaction)
          && perFaction.TryGetValue(faction, out FactionTally tally))
        FillPopulationData(populationData, tally);
      return populationData;
    }

    // ---- Character tracking (unchanged) ----
    [OnEvent]
    public void OnCharacterCreated(CharacterCreatedEvent characterCreatedEvent)
    {
      Character character = characterCreatedEvent.Character;
      if (_characters.ContainsKey(character)) return;

      string faction = FactionAssignmentHelper.GetFactionID(character);
      SetFactionForEntity(character.GameObject, faction);

      CharacterRecord record = new CharacterRecord { Faction = faction };
      _characters.Add(character, record);

      Citizen citizen = character.GetComponent<Citizen>();
      if (citizen != null)
      {
        record.District = citizen.AssignedDistrict;
        citizen.ChangedAssignedDistrict += OnChangedAssignedDistrict;
      }

      Worker worker = character.GetComponent<Worker>();
      if (worker != null)
      {
        WorkRefuser workRefuser = character.GetComponent<WorkRefuser>();
        if (workRefuser != null) workRefuser.RefusesWorkChanged += OnRefusesWorkChanged;
      }

      Contaminable contaminable = character.GetComponent<Contaminable>();
      if (contaminable != null) contaminable.ContaminationChanged += OnContaminationChanged;

      record.Contrib = ComputeCharacterContribution(character);
      ApplyCharacterDelta(record, record.Contrib);
    }

    [OnEvent]
    public void OnCharacterKilled(CharacterKilledEvent characterKilledEvent)
    {
      Character character = characterKilledEvent.Character;
      if (!_characters.TryGetValue(character, out CharacterRecord record)) return;
      _characters.Remove(character);
      RemoveFactionForEntity(character.GameObject);

      Citizen citizen = character.GetComponent<Citizen>();
      if (citizen != null) citizen.ChangedAssignedDistrict -= OnChangedAssignedDistrict;
      Worker worker = character.GetComponent<Worker>();
      if (worker != null)
      {
        WorkRefuser workRefuser = character.GetComponent<WorkRefuser>();
        if (workRefuser != null) workRefuser.RefusesWorkChanged -= OnRefusesWorkChanged;
      }
      Contaminable contaminable = character.GetComponent<Contaminable>();
      if (contaminable != null) contaminable.ContaminationChanged -= OnContaminationChanged;

      ApplyCharacterDelta(record, -record.Contrib);
    }

    // ---- Building tracking (unchanged) ----
    [OnEvent]
    public void OnEnteredFinishedState(EnteredFinishedStateEvent enteredFinishedStateEvent)
    {
      TrySpawnFactionStartingBeavers(enteredFinishedStateEvent.BlockObject);
      RegisterBuilding(enteredFinishedStateEvent.BlockObject);
    }

    [OnEvent]
    public void OnExitedFinishedState(ExitedFinishedStateEvent exitedFinishedStateEvent)
    {
      UnregisterBuilding(exitedFinishedStateEvent.BlockObject);
    }

    [OnEvent]
    public void OnShowPrimaryUI(ShowPrimaryUIEvent showPrimaryUIEvent)
    {
      StartupComplete = true;
    }

    private void RegisterBuilding(BlockObject blockObject)
    {
      string faction = FactionAssignmentHelper.GetFactionID(blockObject);
      SetFactionForEntity(blockObject.GameObject, faction);

      if (_wiredBuildings.Add(blockObject))
      {
        DistrictBuilding districtBuilding = blockObject.GetComponent<DistrictBuilding>();
        if (districtBuilding != null) districtBuilding.ReassignedDistrict += OnBuildingReassignedDistrict;
      }

      Dwelling dwelling = blockObject.GetComponent<Dwelling>();
      if (dwelling != null && !_dwellings.ContainsKey(dwelling))
      {
        BuildingRecord record = new BuildingRecord { Faction = faction };
        record.District = GetDistrict(blockObject);
        record.Contrib = ComputeDwellingContribution(dwelling);
        _dwellings.Add(dwelling, record);
        dwelling.NumberOfDwellersChanged += OnNumberOfDwellersChanged;
        BlockableObject dwellingBlockableObject = blockObject.GetComponent<BlockableObject>();
        if (dwellingBlockableObject != null)
        {
          dwellingBlockableObject.ObjectBlocked += OnDwellingBlockChanged;
          dwellingBlockableObject.ObjectUnblocked += OnDwellingBlockChanged;
        }
        ApplyBuildingDelta(record, record.Contrib);
      }

      Workplace workplace = blockObject.GetComponent<Workplace>();
      if (workplace != null && !_workplaces.ContainsKey(workplace))
      {
        BuildingRecord record = new BuildingRecord { Faction = faction };
        record.District = GetDistrict(blockObject);
        record.Contrib = ComputeWorkplaceContribution(workplace);
        _workplaces.Add(workplace, record);
        workplace.WorkerAssigned += OnWorkerAssigned;
        workplace.WorkerUnassigned += OnWorkerUnassigned;
        workplace.DesiredWorkersChanged += OnDesiredWorkersChanged;
        WorkplaceWorkerType workplaceWorkerType = workplace.GetComponent<WorkplaceWorkerType>();
        if (workplaceWorkerType != null) workplaceWorkerType.WorkerTypeChanged += OnWorkerTypeChanged;
        BlockableObject workplaceBlockableObject = blockObject.GetComponent<BlockableObject>();
        if (workplaceBlockableObject != null)
        {
          workplaceBlockableObject.ObjectBlocked += OnWorkplaceBlockChanged;
          workplaceBlockableObject.ObjectUnblocked += OnWorkplaceBlockChanged;
        }
        ApplyBuildingDelta(record, record.Contrib);
      }
    }

    private void UnregisterBuilding(BlockObject blockObject)
    {
      RemoveFactionForEntity(blockObject.GameObject);

      Dwelling dwelling = blockObject.GetComponent<Dwelling>();
      if (dwelling != null && _dwellings.TryGetValue(dwelling, out BuildingRecord dwellingRecord))
      {
        dwelling.NumberOfDwellersChanged -= OnNumberOfDwellersChanged;
        BlockableObject dwellingBlockableObject = blockObject.GetComponent<BlockableObject>();
        if (dwellingBlockableObject != null)
        {
          dwellingBlockableObject.ObjectBlocked -= OnDwellingBlockChanged;
          dwellingBlockableObject.ObjectUnblocked -= OnDwellingBlockChanged;
        }
        _dwellings.Remove(dwelling);
        ApplyBuildingDelta(dwellingRecord, -dwellingRecord.Contrib);
      }

      Workplace workplace = blockObject.GetComponent<Workplace>();
      if (workplace != null && _workplaces.TryGetValue(workplace, out BuildingRecord workplaceRecord))
      {
        workplace.WorkerAssigned -= OnWorkerAssigned;
        workplace.WorkerUnassigned -= OnWorkerUnassigned;
        workplace.DesiredWorkersChanged -= OnDesiredWorkersChanged;
        WorkplaceWorkerType workplaceWorkerType = workplace.GetComponent<WorkplaceWorkerType>();
        if (workplaceWorkerType != null) workplaceWorkerType.WorkerTypeChanged -= OnWorkerTypeChanged;
        BlockableObject workplaceBlockableObject = blockObject.GetComponent<BlockableObject>();
        if (workplaceBlockableObject != null)
        {
          workplaceBlockableObject.ObjectBlocked -= OnWorkplaceBlockChanged;
          workplaceBlockableObject.ObjectUnblocked -= OnWorkplaceBlockChanged;
        }
        _workplaces.Remove(workplace);
        ApplyBuildingDelta(workplaceRecord, -workplaceRecord.Contrib);
      }

      if (_wiredBuildings.Remove(blockObject))
      {
        DistrictBuilding districtBuilding = blockObject.GetComponent<DistrictBuilding>();
        if (districtBuilding != null) districtBuilding.ReassignedDistrict -= OnBuildingReassignedDistrict;
      }
    }

    // ---- DC-faction starting population (unchanged) ----
    private void TrySpawnFactionStartingBeavers(BlockObject blockObject)
    {
      if (!StartupComplete) return;
      if (blockObject.GetComponent<DistrictCenter>() == null) return;

      string faction = FactionAssignmentHelper.GetFactionID(blockObject);
      if (faction == "Common") return;
      if (HasFactionPopulation(faction)) return;

      SpawnFactionStartingBeavers(blockObject, faction);
    }

    private void SpawnFactionStartingBeavers(BlockObject blockObject, string faction)
    {
      BuildingAccessible buildingAccessible = blockObject.GetComponent<BuildingAccessible>();
      if (buildingAccessible == null) return;
      Vector3? unblockedSingleAccess = buildingAccessible.Accessible.UnblockedSingleAccess;
      if (!unblockedSingleAccess.HasValue) return;

      GameModeSpec gameMode = GetCurrentGameMode();
      Vector3 spawnPosition = unblockedSingleAccess.GetValueOrDefault();

      StartingBeaverSpawn.PendingFaction = faction;
      try
      {
        SpawnBeavers(spawnPosition, adults: true, gameMode.StartingAdults, gameMode.AdultAgeProgress);
        SpawnBeavers(spawnPosition, adults: false, gameMode.StartingChildren, gameMode.ChildAgeProgress);
      }
      finally
      {
        StartingBeaverSpawn.PendingFaction = null;
      }

      GiveStartingInventory(blockObject, gameMode);
    }

    private void SpawnBeavers(Vector3 position, bool adults, int numberOfBeavers, MinMaxSpec<float> lifeStageProgressRange)
    {
      float num = ((numberOfBeavers > 1) ? ((lifeStageProgressRange.Max - lifeStageProgressRange.Min) / (float)(numberOfBeavers - 1)) : 0f);
      for (int i = 0; i < numberOfBeavers; i++)
      {
        float lifeStageProgress = lifeStageProgressRange.Min + num * (float)i;
        if (adults)
          _beaverFactory.CreateAdult(position, lifeStageProgress);
        else
          _beaverFactory.CreateChild(position, lifeStageProgress);
      }
    }

    private void GiveStartingInventory(BlockObject blockObject, GameModeSpec gameMode)
    {
      SimpleOutputInventory simpleOutputInventory = blockObject.GetComponent<SimpleOutputInventory>();
      if (simpleOutputInventory == null) return;
      simpleOutputInventory.Inventory.GiveIgnoringCapacity(new GoodAmount("Berries", gameMode.StartingFood));
      simpleOutputInventory.Inventory.GiveIgnoringCapacity(new GoodAmount("Water", gameMode.StartingWater));
    }

    private GameModeSpec GetCurrentGameMode()
    {
      GameSceneParameters sceneParameters;
      if (_sceneLoader.TryGetSceneParameters<GameSceneParameters>(out sceneParameters) && sceneParameters.NewGame)
        return sceneParameters.NewGameConfiguration.GameMode;
      return _gameModeSpecService.GetDefaultSpec();
    }

    private bool HasFactionPopulation(string faction)
    {
      return _globalTallies.TryGetValue(faction, out FactionTally tally)
          && tally.Adults + tally.Children + tally.Bots > 0;
    }

    // ---- Event handlers (unchanged) ----
    private void OnChangedAssignedDistrict(object sender, ChangeAssignedDistrictEventArgs e)
    {
      Citizen citizen = (Citizen)sender;
      if (_characters.TryGetValue(citizen.GetComponent<Character>(), out CharacterRecord record))
        MoveCharacterDistrict(record, e.CurrentDistrict);
    }

    private void OnRefusesWorkChanged(object sender, EventArgs e)
    {
      WorkRefuser workRefuser = (WorkRefuser)sender;
      if (_characters.TryGetValue(workRefuser.GetComponent<Character>(), out CharacterRecord record))
        RefreshCharacter(workRefuser.GetComponent<Character>(), record);
    }

    private void OnContaminationChanged(object sender, EventArgs e)
    {
      Contaminable contaminable = (Contaminable)sender;
      if (_characters.TryGetValue(contaminable.GetComponent<Character>(), out CharacterRecord record))
        RefreshCharacter(contaminable.GetComponent<Character>(), record);
    }

    private void OnNumberOfDwellersChanged(object sender, EventArgs e)
    {
      RefreshDwelling((Dwelling)sender);
    }

    private void OnDwellingBlockChanged(object sender, EventArgs e)
    {
      RefreshDwelling(((BlockableObject)sender).GetComponent<Dwelling>());
    }

    private void OnWorkerAssigned(object sender, WorkerChangedEventArgs e)
    {
      RefreshWorkplace((Workplace)sender);
    }

    private void OnWorkerUnassigned(object sender, WorkerChangedEventArgs e)
    {
      RefreshWorkplace((Workplace)sender);
    }

    private void OnDesiredWorkersChanged(object sender, EventArgs e)
    {
      RefreshWorkplace((Workplace)sender);
    }

    private void OnWorkerTypeChanged(object sender, WorkerTypeChangedEventArgs e)
    {
      RefreshWorkplace(((WorkplaceWorkerType)sender).GetComponent<Workplace>());
    }

    private void OnWorkplaceBlockChanged(object sender, EventArgs e)
    {
      RefreshWorkplace(((BlockableObject)sender).GetComponent<Workplace>());
    }

    private void OnBuildingReassignedDistrict(object sender, EventArgs e)
    {
      DistrictBuilding districtBuilding = (DistrictBuilding)sender;
      DistrictCenter newDistrict = districtBuilding.District;

      Dwelling dwelling = districtBuilding.GetComponent<Dwelling>();
      if (dwelling != null && _dwellings.TryGetValue(dwelling, out BuildingRecord dwellingRecord))
        MoveBuildingDistrict(dwellingRecord, newDistrict);

      Workplace workplace = districtBuilding.GetComponent<Workplace>();
      if (workplace != null && _workplaces.TryGetValue(workplace, out BuildingRecord workplaceRecord))
        MoveBuildingDistrict(workplaceRecord, newDistrict);
    }

    // ---- Contribution computation (unchanged) ----
    private static CharacterContrib ComputeCharacterContribution(Character character)
    {
      int adults = 0, children = 0, bots = 0;
      int beaverWorkforceEmployable = 0, beaverWorkforceUnemployable = 0;
      int botWorkforceEmployable = 0, botWorkforceUnemployable = 0;

      if (character.GetComponent<Beaver>() != null)
      {
        if (character.HasComponent<ChildSpec>()) children = 1;
        else adults = 1;
        Worker worker = character.GetComponent<Worker>();
        if (worker != null)
        {
          if (IsRefusingWork(character)) beaverWorkforceUnemployable = 1;
          else beaverWorkforceEmployable = 1;
        }
      }
      else if (character.GetComponent<Bot>() != null)
      {
        bots = 1;
        Worker worker = character.GetComponent<Worker>();
        if (worker != null)
        {
          if (IsRefusingWork(character)) botWorkforceUnemployable = 1;
          else botWorkforceEmployable = 1;
        }
      }

      int contaminatedAdults = 0, contaminatedChildren = 0;
      Contaminable contaminable = character.GetComponent<Contaminable>();
      if (contaminable != null && contaminable.IsContaminated)
      {
        if (children > 0) contaminatedChildren = 1;
        else if (adults > 0) contaminatedAdults = 1;
      }

      return new CharacterContrib(adults, children, bots, beaverWorkforceEmployable, beaverWorkforceUnemployable,
          botWorkforceEmployable, botWorkforceUnemployable, contaminatedAdults, contaminatedChildren);
    }

    private static BuildingContrib ComputeDwellingContribution(Dwelling dwelling)
    {
      BlockableObject blockableObject = dwelling.GetComponent<BlockableObject>();
      if (blockableObject != null && !blockableObject.IsUnblocked)
        return new BuildingContrib(0, 0, 0, 0, 0, 0);
      int occupied = dwelling.NumberOfDwellers;
      return new BuildingContrib(occupied, Math.Max(0, dwelling.MaxBeavers - occupied), 0, 0, 0, 0);
    }

    private static BuildingContrib ComputeWorkplaceContribution(Workplace workplace)
    {
      BlockableObject blockableObject = workplace.GetComponent<BlockableObject>();
      if (blockableObject != null && !blockableObject.IsUnblocked)
        return new BuildingContrib(0, 0, 0, 0, 0, 0);
      int occupied = workplace.NumberOfAssignedWorkers;
      int free = Math.Max(0, workplace.DesiredWorkers - occupied);
      WorkplaceWorkerType workplaceWorkerType = workplace.GetComponent<WorkplaceWorkerType>();
      bool isBeaverWorkplace = workplaceWorkerType == null || workplaceWorkerType.WorkerType == "Beaver";
      if (isBeaverWorkplace)
        return new BuildingContrib(0, 0, occupied, free, 0, 0);
      return new BuildingContrib(0, 0, 0, 0, occupied, free);
    }

    private static bool IsRefusingWork(Character character)
    {
      WorkRefuser workRefuser = character.GetComponent<WorkRefuser>();
      return workRefuser != null && workRefuser.RefusesWork;
    }

    private static DistrictCenter GetDistrict(BlockObject blockObject)
    {
      DistrictBuilding districtBuilding = blockObject.GetComponent<DistrictBuilding>();
      return districtBuilding != null ? districtBuilding.District : null;
    }

    private void RefreshCharacter(Character character, CharacterRecord record)
    {
      CharacterContrib newContrib = ComputeCharacterContribution(character);
      ApplyCharacterDelta(record, newContrib - record.Contrib);
      record.Contrib = newContrib;
    }

    private void RefreshDwelling(Dwelling dwelling)
    {
      if (_dwellings.TryGetValue(dwelling, out BuildingRecord record))
      {
        BuildingContrib newContrib = ComputeDwellingContribution(dwelling);
        ApplyBuildingDelta(record, newContrib - record.Contrib);
        record.Contrib = newContrib;
      }
    }

    private void RefreshWorkplace(Workplace workplace)
    {
      if (_workplaces.TryGetValue(workplace, out BuildingRecord record))
      {
        BuildingContrib newContrib = ComputeWorkplaceContribution(workplace);
        ApplyBuildingDelta(record, newContrib - record.Contrib);
        record.Contrib = newContrib;
      }
    }

    private void MoveCharacterDistrict(CharacterRecord record, DistrictCenter newDistrict)
    {
      if (record.District == newDistrict) return;
      DistrictCenter previousDistrict = record.District;
      record.District = newDistrict;
      if (previousDistrict != null) GetOrCreateDistrictTally(previousDistrict, record.Faction).Subtract(record.Contrib);
      if (newDistrict != null) GetOrCreateDistrictTally(newDistrict, record.Faction).Add(record.Contrib);
    }

    private void MoveBuildingDistrict(BuildingRecord record, DistrictCenter newDistrict)
    {
      if (record.District == newDistrict) return;
      DistrictCenter previousDistrict = record.District;
      record.District = newDistrict;
      if (previousDistrict != null) GetOrCreateDistrictTally(previousDistrict, record.Faction).Subtract(record.Contrib);
      if (newDistrict != null) GetOrCreateDistrictTally(newDistrict, record.Faction).Add(record.Contrib);
    }

    private void ApplyCharacterDelta(CharacterRecord record, CharacterContrib delta)
    {
      GetOrCreateGlobalTally(record.Faction).Add(delta);
      if (record.District != null) GetOrCreateDistrictTally(record.District, record.Faction).Add(delta);
    }

    private void ApplyBuildingDelta(BuildingRecord record, BuildingContrib delta)
    {
      GetOrCreateGlobalTally(record.Faction).Add(delta);
      if (record.District != null) GetOrCreateDistrictTally(record.District, record.Faction).Add(delta);
    }

    private FactionTally GetOrCreateGlobalTally(string faction)
    {
      if (!_globalTallies.TryGetValue(faction, out FactionTally tally))
      {
        tally = new FactionTally();
        _globalTallies.Add(faction, tally);
      }
      return tally;
    }

    private FactionTally GetOrCreateDistrictTally(DistrictCenter district, string faction)
    {
      if (!_districtTallies.TryGetValue(district, out Dictionary<string, FactionTally> perFaction))
      {
        perFaction = new Dictionary<string, FactionTally>();
        _districtTallies.Add(district, perFaction);
      }
      if (!perFaction.TryGetValue(faction, out FactionTally tally))
      {
        tally = new FactionTally();
        perFaction.Add(faction, tally);
      }
      return tally;
    }

    private static void FillPopulationData(PopulationData populationData, FactionTally tally)
    {
      int occupiedBeds = tally.OccupiedBeds;
      populationData.Update(
          tally.Adults,
          tally.Children,
          tally.Bots,
          new WorkforceData(tally.BeaverWorkforceEmployable, tally.BeaverWorkforceUnemployable),
          new WorkforceData(tally.BotWorkforceEmployable, tally.BotWorkforceUnemployable),
          new BedData(occupiedBeds, tally.FreeBeds, tally.Adults + tally.Children - occupiedBeds),
          new WorkplaceData(tally.BeaverOccupiedWorkslots, tally.BeaverFreeWorkslots, Math.Max(0, tally.BeaverWorkforceEmployable - tally.BeaverOccupiedWorkslots)),
          new WorkplaceData(tally.BotOccupiedWorkslots, tally.BotFreeWorkslots, Math.Max(0, tally.BotWorkforceEmployable - tally.BotOccupiedWorkslots)),
          new ContaminationData(tally.ContaminatedAdults, tally.ContaminatedChildren));
    }

    // ---- Inner classes (unchanged) ----
    private sealed class CharacterRecord
    {
      public string Faction;
      public DistrictCenter District;
      public CharacterContrib Contrib;
    }

    private sealed class BuildingRecord
    {
      public string Faction;
      public DistrictCenter District;
      public BuildingContrib Contrib;
    }

    private sealed class FactionTally
    {
      public int Adults;
      public int Children;
      public int Bots;
      public int BeaverWorkforceEmployable;
      public int BeaverWorkforceUnemployable;
      public int BotWorkforceEmployable;
      public int BotWorkforceUnemployable;
      public int OccupiedBeds;
      public int FreeBeds;
      public int BeaverOccupiedWorkslots;
      public int BeaverFreeWorkslots;
      public int BotOccupiedWorkslots;
      public int BotFreeWorkslots;
      public int ContaminatedAdults;
      public int ContaminatedChildren;

      public void Add(CharacterContrib contribution)
      {
        Adults += contribution.Adults;
        Children += contribution.Children;
        Bots += contribution.Bots;
        BeaverWorkforceEmployable += contribution.BeaverWorkforceEmployable;
        BeaverWorkforceUnemployable += contribution.BeaverWorkforceUnemployable;
        BotWorkforceEmployable += contribution.BotWorkforceEmployable;
        BotWorkforceUnemployable += contribution.BotWorkforceUnemployable;
        ContaminatedAdults += contribution.ContaminatedAdults;
        ContaminatedChildren += contribution.ContaminatedChildren;
      }

      public void Subtract(CharacterContrib contribution)
      {
        Adults -= contribution.Adults;
        Children -= contribution.Children;
        Bots -= contribution.Bots;
        BeaverWorkforceEmployable -= contribution.BeaverWorkforceEmployable;
        BeaverWorkforceUnemployable -= contribution.BeaverWorkforceUnemployable;
        BotWorkforceEmployable -= contribution.BotWorkforceEmployable;
        BotWorkforceUnemployable -= contribution.BotWorkforceUnemployable;
        ContaminatedAdults -= contribution.ContaminatedAdults;
        ContaminatedChildren -= contribution.ContaminatedChildren;
      }

      public void Add(BuildingContrib contribution)
      {
        OccupiedBeds += contribution.OccupiedBeds;
        FreeBeds += contribution.FreeBeds;
        BeaverOccupiedWorkslots += contribution.BeaverOccupiedWorkslots;
        BeaverFreeWorkslots += contribution.BeaverFreeWorkslots;
        BotOccupiedWorkslots += contribution.BotOccupiedWorkslots;
        BotFreeWorkslots += contribution.BotFreeWorkslots;
      }

      public void Subtract(BuildingContrib contribution)
      {
        OccupiedBeds -= contribution.OccupiedBeds;
        FreeBeds -= contribution.FreeBeds;
        BeaverOccupiedWorkslots -= contribution.BeaverOccupiedWorkslots;
        BeaverFreeWorkslots -= contribution.BeaverFreeWorkslots;
        BotOccupiedWorkslots -= contribution.BotOccupiedWorkslots;
        BotFreeWorkslots -= contribution.BotFreeWorkslots;
      }
    }

    private readonly struct CharacterContrib
    {
      public readonly int Adults;
      public readonly int Children;
      public readonly int Bots;
      public readonly int BeaverWorkforceEmployable;
      public readonly int BeaverWorkforceUnemployable;
      public readonly int BotWorkforceEmployable;
      public readonly int BotWorkforceUnemployable;
      public readonly int ContaminatedAdults;
      public readonly int ContaminatedChildren;

      public CharacterContrib(int adults, int children, int bots, int beaverWorkforceEmployable, int beaverWorkforceUnemployable, int botWorkforceEmployable, int botWorkforceUnemployable, int contaminatedAdults, int contaminatedChildren)
      {
        Adults = adults;
        Children = children;
        Bots = bots;
        BeaverWorkforceEmployable = beaverWorkforceEmployable;
        BeaverWorkforceUnemployable = beaverWorkforceUnemployable;
        BotWorkforceEmployable = botWorkforceEmployable;
        BotWorkforceUnemployable = botWorkforceUnemployable;
        ContaminatedAdults = contaminatedAdults;
        ContaminatedChildren = contaminatedChildren;
      }

      public static CharacterContrib operator +(CharacterContrib a, CharacterContrib b)
      {
        return new CharacterContrib(
            a.Adults + b.Adults,
            a.Children + b.Children,
            a.Bots + b.Bots,
            a.BeaverWorkforceEmployable + b.BeaverWorkforceEmployable,
            a.BeaverWorkforceUnemployable + b.BeaverWorkforceUnemployable,
            a.BotWorkforceEmployable + b.BotWorkforceEmployable,
            a.BotWorkforceUnemployable + b.BotWorkforceUnemployable,
            a.ContaminatedAdults + b.ContaminatedAdults,
            a.ContaminatedChildren + b.ContaminatedChildren);
      }

      public static CharacterContrib operator -(CharacterContrib value)
      {
        return new CharacterContrib(
            -value.Adults,
            -value.Children,
            -value.Bots,
            -value.BeaverWorkforceEmployable,
            -value.BeaverWorkforceUnemployable,
            -value.BotWorkforceEmployable,
            -value.BotWorkforceUnemployable,
            -value.ContaminatedAdults,
            -value.ContaminatedChildren);
      }

      public static CharacterContrib operator -(CharacterContrib a, CharacterContrib b)
      {
        return a + -b;
      }
    }

    private readonly struct BuildingContrib
    {
      public readonly int OccupiedBeds;
      public readonly int FreeBeds;
      public readonly int BeaverOccupiedWorkslots;
      public readonly int BeaverFreeWorkslots;
      public readonly int BotOccupiedWorkslots;
      public readonly int BotFreeWorkslots;

      public BuildingContrib(int occupiedBeds, int freeBeds, int beaverOccupiedWorkslots, int beaverFreeWorkslots, int botOccupiedWorkslots, int botFreeWorkslots)
      {
        OccupiedBeds = occupiedBeds;
        FreeBeds = freeBeds;
        BeaverOccupiedWorkslots = beaverOccupiedWorkslots;
        BeaverFreeWorkslots = beaverFreeWorkslots;
        BotOccupiedWorkslots = botOccupiedWorkslots;
        BotFreeWorkslots = botFreeWorkslots;
      }

      public static BuildingContrib operator +(BuildingContrib a, BuildingContrib b)
      {
        return new BuildingContrib(
            a.OccupiedBeds + b.OccupiedBeds,
            a.FreeBeds + b.FreeBeds,
            a.BeaverOccupiedWorkslots + b.BeaverOccupiedWorkslots,
            a.BeaverFreeWorkslots + b.BeaverFreeWorkslots,
            a.BotOccupiedWorkslots + b.BotOccupiedWorkslots,
            a.BotFreeWorkslots + b.BotFreeWorkslots);
      }

      public static BuildingContrib operator -(BuildingContrib value)
      {
        return new BuildingContrib(
            -value.OccupiedBeds,
            -value.FreeBeds,
            -value.BeaverOccupiedWorkslots,
            -value.BeaverFreeWorkslots,
            -value.BotOccupiedWorkslots,
            -value.BotFreeWorkslots);
      }

      public static BuildingContrib operator -(BuildingContrib a, BuildingContrib b)
      {
        return a + -b;
      }
    }
  }
}