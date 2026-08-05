using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.Bots;
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
  }

  [HarmonyPatch(typeof(WorkplaceAssigner), "AssignStalestUnemployed")]
  public static class Patch_WorkplaceAssigner_AssignStalestUnemployed
  {
    public static bool Prefix(WorkplaceAssigner __instance, Workplace workplace)
    {
      int num = workplace.DesiredWorkers - workplace.NumberOfAssignedWorkers;
      HashSet<Worker> unemployedSet = __instance._unemployedWorkers._unemployed;
      string workplaceFaction = FactionAssignmentHelper.GetFactionID(workplace);

      while (num > 0 && unemployedSet.Count > 0)
      {
        Worker selectedWorker = null;
        foreach (Worker worker in unemployedSet)
        {
          // Beavers do not have faction variants, so filtering only applies to Bots
          bool isBot = worker.GetComponent<Bot>() != null;
          if (!isBot || FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(worker), workplaceFaction))
          {
            selectedWorker = worker;
            break;
          }
        }

        if (selectedWorker != null)
        {
          workplace.AssignWorker(selectedWorker);
          num--;
        }
        else
        {
          break;
        }
      }

      return false;
    }
  }

  [HarmonyPatch(typeof(WorkplaceAssigner), "ReassignWorkersToHigherPriorityWorkplaces")]
  public static class Patch_WorkplaceAssigner_ReassignWorkers
  {
    public static bool Prefix(WorkplaceAssigner __instance, WorkplacePriority understaffedWorkplace)
    {
      var workplaces = __instance._priorityOrderedWorkplaces._workplaces;
      if (workplaces == null || workplaces.Count == 0) return false;

      WorkplaceWorkerType workplaceWorkerType = understaffedWorkplace.Workplace.GetComponent<WorkplaceWorkerType>();
      bool isBotWorkplace = workplaceWorkerType != null && workplaceWorkerType.WorkerType == "Bot";

      string understaffedFaction = FactionAssignmentHelper.GetFactionID(understaffedWorkplace.Workplace);
      WorkplacePriority lowestPriorityStaffedWorkplace = null;

      for (int num = workplaces.Count - 1; num >= 0; num--)
      {
        WorkplacePriority wp = workplaces.Values[num];
        if (wp.Workplace.NumberOfAssignedWorkers > 0)
        {
          // Beavers do not have faction variants, so filtering only applies to Bots
          if (!isBotWorkplace || FactionAssignmentHelper.CanWorkAt(FactionAssignmentHelper.GetFactionID(wp.Workplace), understaffedFaction))
          {
            lowestPriorityStaffedWorkplace = wp;
            break;
          }
        }
      }

      if (lowestPriorityStaffedWorkplace != null && understaffedWorkplace.Priority > lowestPriorityStaffedWorkplace.Priority)
      {
        WorkplaceAssigner.ReassignWorkers(lowestPriorityStaffedWorkplace.Workplace, understaffedWorkplace.Workplace);
      }

      return false;
    }
  }
}