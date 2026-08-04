using System.Linq;
using HarmonyLib;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using Timberborn.Planting;
using Timberborn.PlantingUI;
using Timberborn.TemplateSystem;

namespace Calloatti.OmniFactionDevTool
{
  // Patch GetPlanterBuildingName to safely use FirstOrDefault instead of Single
  [HarmonyPatch(typeof(PlantingToolButtonFactory), "GetPlanterBuildingName")]
  public static class Patch_PlantingToolButtonFactory_GetPlanterBuildingName
  {
    public static bool Prefix(PlantableSpec plantableSpec, TemplateService ____templateService, ILoc ____loc, ref string __result)
    {
      PlanterBuildingSpec planterBuildingSpec = ____templateService.GetAll<PlanterBuildingSpec>()
          .FirstOrDefault((PlanterBuildingSpec building) => building.PlantableResourceGroup == plantableSpec.ResourceGroup);

      if (planterBuildingSpec != null)
      {
        string displayNameLocKey = planterBuildingSpec.GetSpec<LabeledEntitySpec>().DisplayNameLocKey;
        __result = ____loc.T(displayNameLocKey);
        return false; // Skip original method execution
      }

      return true; // Fall back to original method if no match is found
    }
  }
}