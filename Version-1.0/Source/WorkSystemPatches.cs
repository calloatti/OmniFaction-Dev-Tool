using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.DistributionSystem;
using Timberborn.TemplateSystem;
using Timberborn.WorkSystem;

namespace Calloatti.OmniFactionDevTool
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
    public static bool Prefix(WorkplaceAssigner __instance)
    {
      var workplacesList = __instance._priorityOrderedWorkplaces._workplaces.Values;
      if (workplacesList == null || workplacesList.Count == 0) return false;

      // Use a HashSet for O(1) removal. Even when empty, we must continue so understaffed
      // workplaces can still be staffed by migrating compatible workers from lower-priority
      // workplaces (vanilla ReassignWorkersToHigherPriorityWorkplaces); the fill loop below
      // simply no-ops when there are no unemployed workers.
      HashSet<Worker> availableUnemployed = new HashSet<Worker>(__instance._unemployedWorkers._unemployed);

      for (int i = 0; i < workplacesList.Count; i++)
      {
        WorkplacePriority understaffedWpPriority = workplacesList[i];
        Workplace understaffedWorkplace = understaffedWpPriority.Workplace;
        if (!understaffedWorkplace.Understaffed) continue;

        string workplaceFaction = FactionAssignmentHelper.GetWorkplaceFaction(understaffedWorkplace);
        int numNeeded = understaffedWorkplace.DesiredWorkers - understaffedWorkplace.NumberOfAssignedWorkers;

        // Try to assign compatible unemployed workers
        while (numNeeded > 0 && availableUnemployed.Count > 0)
        {
          Worker selectedWorker = null;
          foreach (Worker worker in availableUnemployed)
          {
            if (FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(worker), workplaceFaction))
            {
              selectedWorker = worker;
              break;
            }
          }
          if (selectedWorker == null) break;

          availableUnemployed.Remove(selectedWorker);
          understaffedWorkplace.AssignWorker(selectedWorker);
          numNeeded--;
        }

        if (!understaffedWorkplace.Understaffed) continue;

        // Reassignment from lower-priority workplaces
        WorkplacePriority lowestPriorityStaffedWorkplace = null;
        for (int num = workplacesList.Count - 1; num > i; num--)
        {
          WorkplacePriority staffedWp = workplacesList[num];
          if (staffedWp.Workplace.NumberOfAssignedWorkers > 0 && understaffedWpPriority.Priority > staffedWp.Priority)
          {
            var assignedWorkers = staffedWp.Workplace.AssignedWorkers;
            for (int w = assignedWorkers.Count - 1; w >= 0; w--)
            {
              Worker worker = assignedWorkers[w];
              if (FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(worker), workplaceFaction))
              {
                lowestPriorityStaffedWorkplace = staffedWp;
                break;
              }
            }
            if (lowestPriorityStaffedWorkplace != null) break;
          }
        }

        if (lowestPriorityStaffedWorkplace != null)
        {
          var assignedWorkers = lowestPriorityStaffedWorkplace.Workplace.AssignedWorkers;
          int idx = assignedWorkers.Count - 1;
          while (idx >= 0)
          {
            Worker worker = assignedWorkers[idx];
            if (FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(worker), workplaceFaction))
            {
              lowestPriorityStaffedWorkplace.Workplace.UnassignWorker(worker);
              understaffedWorkplace.AssignWorker(worker);
              if (!understaffedWorkplace.Understaffed) break;
            }
            idx--;
          }
        }
      }

      return false;
    }
  }
}