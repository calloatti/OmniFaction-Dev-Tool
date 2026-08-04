using System.Collections.Generic;
using System.Collections.Immutable;
using HarmonyLib;
using Timberborn.BlueprintSystem;
using Timberborn.TemplateCollectionSystem;
using Timberborn.TemplateSystem;

namespace Calloatti.OmniFactionDevTool
{
  // Deduplicate blueprint instances in TemplateCollectionService by TemplateName
  [HarmonyPatch(typeof(TemplateCollectionService), nameof(TemplateCollectionService.Load))]
  public static class Patch_TemplateCollectionService_Load
  {
    public static void Postfix(TemplateCollectionService __instance)
    {
      if (__instance?.AllTemplates == null)
      {
        return;
      }

      List<Blueprint> filteredTemplates = new List<Blueprint>();
      HashSet<string> seenTemplateNames = new HashSet<string>();
      HashSet<Blueprint> seenBlueprints = new HashSet<Blueprint>();

      foreach (Blueprint blueprint in __instance.AllTemplates)
      {
        if (blueprint == null || !seenBlueprints.Add(blueprint))
        {
          continue; // Skip null or duplicate blueprint object references
        }

        TemplateSpec templateSpec = blueprint.GetSpec<TemplateSpec>();
        string templateName = templateSpec?.TemplateName;

        // Deduplicate by TemplateName (e.g., DevPowerGenerator).
        // "First wins" - the current faction's templates are enumerated first
        // (see FactionCollectionIdsAggregator), so they keep the name.
        if (!string.IsNullOrEmpty(templateName))
        {
          if (!seenTemplateNames.Add(templateName))
          {
            continue; // Skip duplicate template name (keeps the first loaded version)
          }
        }

        filteredTemplates.Add(blueprint);
      }

      __instance.AllTemplates = filteredTemplates.ToImmutableArray();
    }
  }
}