using System;
using System.Collections.Generic;
using Bindito.Core;
using Timberborn.BaseComponentSystem;
using Timberborn.BeaverContaminationSystem;
using Timberborn.Beavers;
using Timberborn.BlockingSystem;
using Timberborn.BlockSystem;
using Timberborn.Bots;
using Timberborn.Characters;
using Timberborn.DwellingSystem;
using Timberborn.GameDistricts;
using Timberborn.Population;
using Timberborn.SingletonSystem;
using Timberborn.WorkSystem;

namespace Calloatti.OmniFactionDevTool
{
  [Context("Game")]
  public class FactionPopulationConfigurator : Configurator
  {
    protected override void Configure()
    {
      Bind<FactionPopulationService>().AsSingleton();
    }
  }

  // Event-driven, per-faction copy of the game's population statistics, consumed exclusively by
  // PopulationCounterPatches. Mirrors the vanilla incremental model (BeaverPopulation +
  // DwellerCounter + WorkplaceWorkerCounter + WorkRefuserRegistry + contamination registry) but
  // keys every tally by faction, so a Population Counter placed from a faction's blueprints only
  // ever reads its own faction's numbers.
  //
  // Registers with the EventBus in Load() (like BeaverPopulation and the global statistics
  // providers) so the CharacterCreatedEvent / EnteredFinishedStateEvent posted during map load are
  // queued and delivered to it when EventBus.PostLoad() drains them — by then the entity Load()
  // phase has restored districts, needs, and contamination, so the initial per-entity state read
  // here is correct for save-loaded entities too. No ShowPrimaryUIEvent re-seed is needed.
  public class FactionPopulationService : ILoadableSingleton, IDisposable
  {
    public static FactionPopulationService Instance { get; private set; }

    private readonly EventBus _eventBus;
    private readonly Dictionary<string, FactionTally> _globalTallies = new Dictionary<string, FactionTally>();
    private readonly Dictionary<DistrictCenter, Dictionary<string, FactionTally>> _districtTallies = new Dictionary<DistrictCenter, Dictionary<string, FactionTally>>();
    private readonly Dictionary<Character, CharacterRecord> _characters = new Dictionary<Character, CharacterRecord>();
    private readonly Dictionary<Dwelling, BuildingRecord> _dwellings = new Dictionary<Dwelling, BuildingRecord>();
    private readonly Dictionary<Workplace, BuildingRecord> _workplaces = new Dictionary<Workplace, BuildingRecord>();
    private readonly HashSet<BlockObject> _wiredBuildings = new HashSet<BlockObject>();

    [Inject]
    public FactionPopulationService(EventBus eventBus)
    {
      _eventBus = eventBus;
    }

    public void Load()
    {
      Instance = this;
      _eventBus.Register(this);
    }

    public void Dispose()
    {
      _eventBus.Unregister(this);
      Instance = null;
    }

    // ---- Query API (consumed by PopulationCounterPatches) ----

    public PopulationData GetGlobal(string faction)
    {
      PopulationData populationData = new PopulationData();
      if (_globalTallies.TryGetValue(faction, out FactionTally tally))
      {
        FillPopulationData(populationData, tally);
      }
      return populationData;
    }

    public PopulationData GetDistrict(DistrictCenter districtCenter, string faction)
    {
      PopulationData populationData = new PopulationData();
      if (districtCenter != null
          && _districtTallies.TryGetValue(districtCenter, out Dictionary<string, FactionTally> perFaction)
          && perFaction.TryGetValue(faction, out FactionTally tally))
      {
        FillPopulationData(populationData, tally);
      }
      return populationData;
    }

    // ---- Character tracking ----

    [OnEvent]
    public void OnCharacterCreated(CharacterCreatedEvent characterCreatedEvent)
    {
      Character character = characterCreatedEvent.Character;
      if (_characters.ContainsKey(character)) return;

      CharacterRecord record = new CharacterRecord(FactionAssignmentHelper.GetFactionID(character));
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

    // ---- Building tracking ----

    [OnEvent]
    public void OnEnteredFinishedState(EnteredFinishedStateEvent enteredFinishedStateEvent)
    {
      RegisterBuilding(enteredFinishedStateEvent.BlockObject);
    }

    [OnEvent]
    public void OnExitedFinishedState(ExitedFinishedStateEvent exitedFinishedStateEvent)
    {
      UnregisterBuilding(exitedFinishedStateEvent.BlockObject);
    }

    private void RegisterBuilding(BlockObject blockObject)
    {
      if (_wiredBuildings.Add(blockObject))
      {
        DistrictBuilding districtBuilding = blockObject.GetComponent<DistrictBuilding>();
        if (districtBuilding != null) districtBuilding.ReassignedDistrict += OnBuildingReassignedDistrict;
      }

      Dwelling dwelling = blockObject.GetComponent<Dwelling>();
      if (dwelling != null && !_dwellings.ContainsKey(dwelling))
      {
        BuildingRecord record = new BuildingRecord(FactionAssignmentHelper.GetFactionID(blockObject));
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
        BuildingRecord record = new BuildingRecord(FactionAssignmentHelper.GetFactionID(blockObject));
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

    // ---- C# event handlers ----

    private void OnChangedAssignedDistrict(object sender, ChangeAssignedDistrictEventArgs e)
    {
      Citizen citizen = (Citizen)sender;
      if (_characters.TryGetValue(citizen.GetComponent<Character>(), out CharacterRecord record))
      {
        MoveCharacterDistrict(record, e.CurrentDistrict);
      }
    }

    private void OnRefusesWorkChanged(object sender, EventArgs e)
    {
      WorkRefuser workRefuser = (WorkRefuser)sender;
      if (_characters.TryGetValue(workRefuser.GetComponent<Character>(), out CharacterRecord record))
      {
        RefreshCharacter(workRefuser.GetComponent<Character>(), record);
      }
    }

    private void OnContaminationChanged(object sender, EventArgs e)
    {
      Contaminable contaminable = (Contaminable)sender;
      if (_characters.TryGetValue(contaminable.GetComponent<Character>(), out CharacterRecord record))
      {
        RefreshCharacter(contaminable.GetComponent<Character>(), record);
      }
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
      {
        MoveBuildingDistrict(dwellingRecord, newDistrict);
      }

      Workplace workplace = districtBuilding.GetComponent<Workplace>();
      if (workplace != null && _workplaces.TryGetValue(workplace, out BuildingRecord workplaceRecord))
      {
        MoveBuildingDistrict(workplaceRecord, newDistrict);
      }
    }

    // ---- Contribution computation ----

    // A character is either a beaver (adult or child) or a bot. Workforce contribution follows the
    // vanilla WorkRefuserRegistry split (worker refusing work vs not), and contamination mirrors the
    // contamination registry. Dweller employment is intentionally NOT tracked here — the vanilla
    // WorkplaceData derives Unemployed from workplace occupancy (Employable - OccupiedWorkslots).
    private static CharacterContrib ComputeCharacterContribution(Character character)
    {
      int adults = 0;
      int children = 0;
      int bots = 0;
      int beaverWorkforceEmployable = 0;
      int beaverWorkforceUnemployable = 0;
      int botWorkforceEmployable = 0;
      int botWorkforceUnemployable = 0;

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

      int contaminatedAdults = 0;
      int contaminatedChildren = 0;
      Contaminable contaminable = character.GetComponent<Contaminable>();
      if (contaminable != null && contaminable.IsContaminated)
      {
        if (children > 0) contaminatedChildren = 1;
        else if (adults > 0) contaminatedAdults = 1;
      }

      return new CharacterContrib(adults, children, bots, beaverWorkforceEmployable, beaverWorkforceUnemployable, botWorkforceEmployable, botWorkforceUnemployable, contaminatedAdults, contaminatedChildren);
    }

    // Mirrors vanilla DwellerCounter: blocked dwellings report (0, 0), otherwise
    // (NumberOfDwellers, MaxBeavers - NumberOfDwellers).
    private static BuildingContrib ComputeDwellingContribution(Dwelling dwelling)
    {
      BlockableObject blockableObject = dwelling.GetComponent<BlockableObject>();
      if (blockableObject != null && !blockableObject.IsUnblocked) return new BuildingContrib(0, 0, 0, 0, 0, 0);
      int occupied = dwelling.NumberOfDwellers;
      return new BuildingContrib(occupied, Math.Max(0, dwelling.MaxBeavers - occupied), 0, 0, 0, 0);
    }

    // Mirrors vanilla WorkplaceWorkerCounter: blocked workplaces report (0, 0), otherwise
    // (NumberOfAssignedWorkers, max(DesiredWorkers - assigned, 0)) bucketed by the workplace's
    // WorkerType (a workplace has a single worker type).
    private static BuildingContrib ComputeWorkplaceContribution(Workplace workplace)
    {
      BlockableObject blockableObject = workplace.GetComponent<BlockableObject>();
      if (blockableObject != null && !blockableObject.IsUnblocked) return new BuildingContrib(0, 0, 0, 0, 0, 0);
      int occupied = workplace.NumberOfAssignedWorkers;
      int free = Math.Max(0, workplace.DesiredWorkers - occupied);
      WorkplaceWorkerType workplaceWorkerType = workplace.GetComponent<WorkplaceWorkerType>();
      bool isBeaverWorkplace = workplaceWorkerType == null || workplaceWorkerType.WorkerType == "Beaver";
      if (isBeaverWorkplace) return new BuildingContrib(0, 0, occupied, free, 0, 0);
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

    // Folds a faction tally into PopulationData using the exact field layout and derived-number
    // formulas the vanilla PopulationDataCollector uses (Homeless = adults + children - occupied
    // beds; Unemployed = employable workforce - occupied workslots), just for one faction.
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

    private sealed class CharacterRecord
    {
      public readonly string Faction;
      public DistrictCenter District;
      public CharacterContrib Contrib;

      public CharacterRecord(string faction)
      {
        Faction = faction;
      }
    }

    private sealed class BuildingRecord
    {
      public readonly string Faction;
      public DistrictCenter District;
      public BuildingContrib Contrib;

      public BuildingRecord(string faction)
      {
        Faction = faction;
      }
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
        return new CharacterContrib(a.Adults + b.Adults, a.Children + b.Children, a.Bots + b.Bots, a.BeaverWorkforceEmployable + b.BeaverWorkforceEmployable, a.BeaverWorkforceUnemployable + b.BeaverWorkforceUnemployable, a.BotWorkforceEmployable + b.BotWorkforceEmployable, a.BotWorkforceUnemployable + b.BotWorkforceUnemployable, a.ContaminatedAdults + b.ContaminatedAdults, a.ContaminatedChildren + b.ContaminatedChildren);
      }

      public static CharacterContrib operator -(CharacterContrib value)
      {
        return new CharacterContrib(-value.Adults, -value.Children, -value.Bots, -value.BeaverWorkforceEmployable, -value.BeaverWorkforceUnemployable, -value.BotWorkforceEmployable, -value.BotWorkforceUnemployable, -value.ContaminatedAdults, -value.ContaminatedChildren);
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
        return new BuildingContrib(a.OccupiedBeds + b.OccupiedBeds, a.FreeBeds + b.FreeBeds, a.BeaverOccupiedWorkslots + b.BeaverOccupiedWorkslots, a.BeaverFreeWorkslots + b.BeaverFreeWorkslots, a.BotOccupiedWorkslots + b.BotOccupiedWorkslots, a.BotFreeWorkslots + b.BotFreeWorkslots);
      }

      public static BuildingContrib operator -(BuildingContrib value)
      {
        return new BuildingContrib(-value.OccupiedBeds, -value.FreeBeds, -value.BeaverOccupiedWorkslots, -value.BeaverFreeWorkslots, -value.BotOccupiedWorkslots, -value.BotFreeWorkslots);
      }

      public static BuildingContrib operator -(BuildingContrib a, BuildingContrib b)
      {
        return a + -b;
      }
    }
  }
}
