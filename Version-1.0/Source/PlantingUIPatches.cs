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
  // and append the faction suffix if applicable.
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

        // Fetch the template name and append the faction suffix using our central cache
        TemplateSpec templateSpec = planterBuildingSpec.GetSpec<TemplateSpec>();
        if (templateSpec != null && !string.IsNullOrEmpty(templateSpec.TemplateName))
        {
          if (FactionBlueprintCache.TemplateToFactionLocKey.TryGetValue(templateSpec.TemplateName, out string factionLocKey) && !string.IsNullOrEmpty(factionLocKey))
          {
            string factionName = ____loc.T(factionLocKey);
            __result += $" ({factionName})";
          }
        }

        return false; // Skip original method execution
      }

      return true; // Fall back to original method if no match is found
    }
  }
}