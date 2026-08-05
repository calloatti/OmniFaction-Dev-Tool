using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HarmonyLib;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.FactionSystem;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.TemplateCollectionSystem;
using Timberborn.TemplateSystem;

namespace Calloatti.OmniFactionDevTool
{
  public static class FactionBlueprintCache
  {
    // Maps TemplateName -> Faction DisplayNameLocKey (e.g., "DevPowerGenerator" -> "Faction.Folktails.DisplayName")
    public static Dictionary<string, string> TemplateToFactionLocKey { get; } = new Dictionary<string, string>();
  }

  // Deduplicate blueprint instances in TemplateCollectionService by TemplateName
  // AND safely map which blueprint belongs to which faction.
  [HarmonyPatch(typeof(TemplateCollectionService), nameof(TemplateCollectionService.Load))]
  public static class Patch_TemplateCollectionService_Load
  {
    public static void Postfix(TemplateCollectionService __instance)
    {
      if (__instance?.AllTemplates == null)
      {
        return;
      }

      // --- 1. Build Faction Blueprint Cache ---
      FactionBlueprintCache.TemplateToFactionLocKey.Clear();
      ISpecService specService = __instance._specService;

      if (specService != null)
      {
        var factionSpecs = specService.GetSpecs<FactionSpec>();
        var collectionSpecs = specService.GetSpecs<TemplateCollectionSpec>();

        foreach (FactionSpec faction in factionSpecs)
        {
          // FactionSpec uses a private property for the LocKey.
          string locKey = faction.DisplayNameLocKey;

          foreach (string colId in faction.TemplateCollectionIds)
          {
            TemplateCollectionSpec colSpec = collectionSpecs.FirstOrDefault(c => c.CollectionId == colId);
            if (colSpec != null)
            {
              foreach (var bpAsset in colSpec.Blueprints)
              {
                Blueprint bp = specService.GetBlueprint(bpAsset.Path);
                TemplateSpec tSpec = bp?.GetSpec<TemplateSpec>();

                if (tSpec != null && !string.IsNullOrEmpty(tSpec.TemplateName))
                {
                  // Only map it if it hasn't been claimed yet. This ensures faction-specific 
                  // collections claim their buildings before shared/common collections evaluate.
                  if (!FactionBlueprintCache.TemplateToFactionLocKey.ContainsKey(tSpec.TemplateName))
                  {
                    FactionBlueprintCache.TemplateToFactionLocKey[tSpec.TemplateName] = locKey;
                  }
                }
              }
            }
          }
        }
      }

      // --- 2. Deduplicate Templates ---
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

  // Intercept LabeledEntity.DisplayName to append the faction suffix globally
  [HarmonyPatch(typeof(LabeledEntity), "get_DisplayName")]
  public static class Patch_LabeledEntity_get_DisplayName
  {
    public static void Postfix(LabeledEntity __instance, ref string __result, ref string ____displayName, ILoc ____loc)
    {
      TemplateSpec templateSpec = __instance.GetComponent<TemplateSpec>();
      if (templateSpec != null && !string.IsNullOrEmpty(templateSpec.TemplateName))
      {
        if (FactionBlueprintCache.TemplateToFactionLocKey.TryGetValue(templateSpec.TemplateName, out string factionLocKey) && !string.IsNullOrEmpty(factionLocKey))
        {
          string factionName = ____loc.T(factionLocKey);
          string suffix = $" ({factionName})";

          // Ensure we only append the suffix once, and only to valid localized strings
          if (!string.IsNullOrEmpty(__result) && !__result.EndsWith(suffix))
          {
            __result += suffix;
            ____displayName = __result; // Update the LabeledEntity's internal cache
          }
        }
      }
    }
  }
}