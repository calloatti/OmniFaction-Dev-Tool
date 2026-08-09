using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.TemplateSystem;
using Timberborn.WorkSystem;

namespace Calloatti.OmniFactionDevTool
{
  public static class FactionAssignmentHelper
  {
    public static string GetFactionID(BaseComponent component)
    {
      TemplateSpec templateSpec = component.GetComponent<TemplateSpec>();

      // 1. Try to resolve via the global template cache (Best for buildings)
      if (templateSpec != null && !string.IsNullOrEmpty(templateSpec.TemplateName))
      {
        if (FactionBlueprintCache.TemplateToFactionLocKey.TryGetValue(templateSpec.TemplateName, out string locKey))
        {
          if (locKey.IndexOf("Folktails", StringComparison.OrdinalIgnoreCase) >= 0) return "Folktails";
          if (locKey.IndexOf("IronTeeth", StringComparison.OrdinalIgnoreCase) >= 0) return "IronTeeth";
          return locKey;
        }
      }

      // 2. Fallback to direct name matching
      string nameToCheck = templateSpec != null && !string.IsNullOrEmpty(templateSpec.TemplateName)
          ? templateSpec.TemplateName
          : component.Name;

      if (nameToCheck.IndexOf("Folktails", StringComparison.OrdinalIgnoreCase) >= 0) return "Folktails";
      if (nameToCheck.IndexOf("IronTeeth", StringComparison.OrdinalIgnoreCase) >= 0) return "IronTeeth";

      return "Common";
    }

    // Safely allows Folktails or IronTeeth to work at shared "Common" buildings
    public static bool CanWorkAt(string workerFaction, string workplaceFaction)
    {
      return workplaceFaction == "Common" || workerFaction == "Common" || workerFaction == workplaceFaction;
    }

    // Same compatibility rule for dwellings: a factioned beaver may only move into its own
    // faction's dwelling, "Common" dwellings accept anyone, and "Common" beavers (shared vanilla
    // templates) may live anywhere.
    public static bool CanResideAt(string residentFaction, string dwellingFaction)
    {
      return CanWorkAt(residentFaction, dwellingFaction);
    }
  }

  [HarmonyPatch(typeof(WorkplaceAssigner), "Assign")]
  public static class Patch_WorkplaceAssigner_Assign
  {
    // Faction-restrict workplace assignment for BOTH beavers and bots: a factioned worker can only
    // be assigned to its own faction's workplace ("Common" workplaces accept anyone, "Common"
    // workers - e.g. shared vanilla templates - can work anywhere). Restricting beavers keeps a
    // faction's workforce confined to that faction's buildings, which the per-faction population
    // counter relies on.
    public static bool Prefix(WorkplaceAssigner __instance)
    {
      var workplacesList = __instance._priorityOrderedWorkplaces._workplaces.Values;
      if (workplacesList == null || workplacesList.Count == 0) return false;

      bool anyUnemployed = __instance._unemployedWorkers.AnyUnemployed;

      // FIX: Create a local copy of the unemployed set so we can remove workers as we assign them.
      // This prevents the loop from throwing multiple assignment commands at the exact same bot in a single tick.
      List<Worker> availableUnemployed = new List<Worker>(__instance._unemployedWorkers._unemployed);

      // Iterate through all understaffed workplaces from highest priority to lowest
      for (int i = 0; i < workplacesList.Count; i++)
      {
        WorkplacePriority understaffedWpPriority = workplacesList[i];
        Workplace understaffedWorkplace = understaffedWpPriority.Workplace;

        if (!understaffedWorkplace.Understaffed) continue;

        string workplaceFaction = FactionAssignmentHelper.GetFactionID(understaffedWorkplace);

        // 1. Try to assign available unemployed workers first
        if (anyUnemployed)
        {
          int numNeeded = understaffedWorkplace.DesiredWorkers - understaffedWorkplace.NumberOfAssignedWorkers;
          bool assignedAny = false;

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

            if (selectedWorker != null)
            {
              understaffedWorkplace.AssignWorker(selectedWorker);

              // FIX: Remove the worker from our local tracking list so they aren't picked again
              availableUnemployed.Remove(selectedWorker);

              assignedAny = true;
              numNeeded--;
            }
            else
            {
              break; // No more compatible unemployed workers for THIS workplace
            }
          }

          if (assignedAny)
          {
            return false; // Successfully processed this tick, exit to allow other systems to run
          }
        }

        // 2. If no unemployed workers were compatible, try reassigning from a lower priority staffed workplace
        WorkplacePriority lowestPriorityStaffedWorkplace = null;
        for (int num = workplacesList.Count - 1; num > i; num--)
        {
          WorkplacePriority staffedWp = workplacesList[num];
          if (staffedWp.Workplace.NumberOfAssignedWorkers > 0 && understaffedWpPriority.Priority > staffedWp.Priority)
          {
            var assignedWorkers = staffedWp.Workplace.AssignedWorkers;

            // Search backwards to safely identify a compatible worker
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

        // If a compatible lower-priority workplace was found, migrate as many workers as possible
        if (lowestPriorityStaffedWorkplace != null)
        {
          var assignedWorkers = lowestPriorityStaffedWorkplace.Workplace.AssignedWorkers;
          int num = assignedWorkers.Count - 1;

          while (num >= 0)
          {
            Worker worker = assignedWorkers[num];
            if (FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(worker), workplaceFaction))
            {
              lowestPriorityStaffedWorkplace.Workplace.UnassignWorker(worker);
              understaffedWorkplace.AssignWorker(worker);

              if (!understaffedWorkplace.Understaffed)
              {
                break;
              }
            }
            num--;
          }
          return false;
        }
      }

      return false; // Skip vanilla execution entirely as we handled the full assignment logic
    }
  }
}