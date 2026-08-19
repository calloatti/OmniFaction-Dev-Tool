using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.DistributionSystem;
using Timberborn.TemplateSystem;
using Timberborn.WorkSystem;

namespace Calloatti.OmniFaction
{
  public static class FactionAssignmentHelper
  {
    public static string GetFactionID(BaseComponent component)
    {
      if (component == null) return "Common";

      // 1. Check entity cache (O(1))
      string cached = OmniFactionService.GetCachedFaction(component.GameObject);
      if (!string.IsNullOrEmpty(cached)) return cached;

      // 2. Template cache for buildings (from FactionBlueprintCache)
      TemplateSpec templateSpec = component.GetComponent<TemplateSpec>();
      if (templateSpec != null && !string.IsNullOrEmpty(templateSpec.TemplateName))
      {
        if (FactionBlueprintCache.TemplateToFactionId.TryGetValue(templateSpec.TemplateName, out string factionId))
          return factionId;
      }

      // 3. Fallback: substring match on entity name (only for uncached entities, e.g., during creation)
      string nameToCheck = templateSpec != null && !string.IsNullOrEmpty(templateSpec.TemplateName)
          ? templateSpec.TemplateName
          : component.Name;
      foreach (string knownFactionId in FactionNeedCache.FactionAllowedNeeds.Keys)
      {
        if (!string.IsNullOrEmpty(knownFactionId)
            && nameToCheck.IndexOf(knownFactionId, StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return knownFactionId;
        }
      }

      return "Common";
    }

    public static bool CanWorkAt(string workerFaction, string workplaceFaction)
    {
      return workplaceFaction == "Common" || workerFaction == "Common" || workerFaction == workplaceFaction;
    }

    // District Crossings link two districts and must be staffable by any faction, regardless of
    // the crossing's own factioned template. Treating them as "Common" lets CanWorkAt's wildcard
    // admit workers of every faction.
    public static string GetWorkplaceFaction(Workplace workplace)
    {
      if (workplace.GetComponent<DistrictCrossing>() != null) return "Common";
      return GetFactionID(workplace);
    }

    public static bool CanResideAt(string residentFaction, string dwellingFaction)
    {
      return CanWorkAt(residentFaction, dwellingFaction);
    }
  }

  [HarmonyPatch(typeof(WorkplaceAssigner), "Assign")]
  public static class Patch_WorkplaceAssigner_Assign
  {
    // Reused across ticks to avoid per-update allocation. Assign runs single-threaded on the
    // work-tick, so clearing + refilling the shared sets is safe (no re-entrancy).
    private static readonly HashSet<Worker> _availableUnemployed = new HashSet<Worker>();
    private static readonly HashSet<string> _servicedFactions = new HashSet<string>();

    public static bool Prefix(WorkplaceAssigner __instance)
    {
      var workplacesList = __instance._priorityOrderedWorkplaces._workplaces.Values;
      if (workplacesList == null || workplacesList.Count == 0) return false;

      // Vanilla cadence, extended per faction: DistrictWorkplaceAssigner.Tick calls Assign once
      // per tick per worker-type, and vanilla Assign fully staffs exactly ONE workplace (the
      // highest-priority understaffed one) per call. We service ONE workplace per faction per
      // tick instead — each faction gets its own turn, so no faction starves on multi-faction
      // maps, while still spreading staffing across ticks instead of one big burst.
      _availableUnemployed.Clear();
      _availableUnemployed.UnionWith(__instance._unemployedWorkers._unemployed);
      _servicedFactions.Clear();

      for (int i = 0; i < workplacesList.Count; i++)
      {
        WorkplacePriority understaffedWpPriority = workplacesList[i];
        Workplace understaffedWorkplace = understaffedWpPriority.Workplace;
        if (!understaffedWorkplace.Understaffed) continue;

        string workplaceFaction = FactionAssignmentHelper.GetWorkplaceFaction(understaffedWorkplace);
        if (!_servicedFactions.Add(workplaceFaction)) continue;

        int numNeeded = understaffedWorkplace.DesiredWorkers - understaffedWorkplace.NumberOfAssignedWorkers;

        // Fill from compatible unemployed workers (vanilla AssignStalestUnemployed, faction-filtered).
        while (numNeeded > 0 && _availableUnemployed.Count > 0)
        {
          Worker selectedWorker = null;
          foreach (Worker worker in _availableUnemployed)
          {
            if (FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(worker), workplaceFaction))
            {
              selectedWorker = worker;
              break;
            }
          }
          if (selectedWorker == null) break;

          _availableUnemployed.Remove(selectedWorker);
          understaffedWorkplace.AssignWorker(selectedWorker);
          numNeeded--;
        }

        if (understaffedWorkplace.Understaffed)
        {
          // Migrate from lower-priority staffed workplaces (vanilla
          // ReassignWorkersToHigherPriorityWorkplaces, faction-filtered). Scans every lower-priority
          // source — skipping ones without compatible workers — so multi-faction setups still find
          // a source when the single lowest-priority workplace holds the wrong faction.
          for (int num = workplacesList.Count - 1; num > i; num--)
          {
            WorkplacePriority staffedWp = workplacesList[num];
            if (staffedWp.Workplace.NumberOfAssignedWorkers <= 0
                || understaffedWpPriority.Priority <= staffedWp.Priority) continue;
            var assignedWorkers = staffedWp.Workplace.AssignedWorkers;
            for (int w = assignedWorkers.Count - 1; w >= 0; w--)
            {
              Worker worker = assignedWorkers[w];
              if (FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(worker), workplaceFaction))
              {
                staffedWp.Workplace.UnassignWorker(worker);
                understaffedWorkplace.AssignWorker(worker);
                if (!understaffedWorkplace.Understaffed) break;
              }
            }
            if (!understaffedWorkplace.Understaffed) break;
          }
        }

        // One workplace per faction per tick: keep scanning so every faction gets its turn.
        // If this workplace is still understaffed after an exhaustive fill+migrate pass, no
        // compatible workers exist — its faction's turn is spent; next tick retries.
      }

      return false;
    }
  }
}