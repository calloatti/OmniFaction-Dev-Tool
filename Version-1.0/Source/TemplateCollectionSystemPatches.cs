using System;
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

namespace Calloatti.OmniFaction
{
  public static class FactionBlueprintCache
  {
    public static Dictionary<string, string> TemplateToFactionLocKey { get; } = new Dictionary<string, string>();
    public static Dictionary<string, string> TemplateToFactionId { get; } = new Dictionary<string, string>();
    public static Dictionary<string, HashSet<string>> TemplateToFactionIds { get; } = new Dictionary<string, HashSet<string>>();

    // Localized faction display names resolved once per locKey (populated lazily by the
    // DisplayName postfix, cleared on Load). Avoids calling ILoc.T on every UI read.
    public static Dictionary<string, string> LocKeyToFactionName { get; } = new Dictionary<string, string>();
  }

  [HarmonyPatch(typeof(TemplateCollectionService), nameof(TemplateCollectionService.Load))]
  public static class Patch_TemplateCollectionService_Load
  {
    public static void Postfix(TemplateCollectionService __instance)
    {
      if (__instance?.AllTemplates == null) return;

      FactionBlueprintCache.TemplateToFactionLocKey.Clear();
      FactionBlueprintCache.TemplateToFactionId.Clear();
      FactionBlueprintCache.TemplateToFactionIds.Clear();
      FactionBlueprintCache.LocKeyToFactionName.Clear();
      Patch_TemplateAttachments_GetOrCreateAttachment.ClearCaches();
      ISpecService specService = __instance._specService;
      if (specService != null)
      {
        var factionSpecs = specService.GetSpecs<FactionSpec>();
        var collectionSpecs = specService.GetSpecs<TemplateCollectionSpec>();

        foreach (FactionSpec faction in factionSpecs)
        {
          string locKey = faction.DisplayNameLocKey;
          string factionId = faction.Id;

          foreach (string colId in faction.TemplateCollectionIds)
          {
            // Iterate ALL collection specs matching this CollectionId. A mod that appends to an
            // existing collection (e.g. via "Blueprints#append") creates a *separate*
            // TemplateCollectionSpec with the same CollectionId (different asset name = no JSON
            // merge at load time). Using FirstOrDefault here would only pick up the first spec
            // (vanilla's) and skip the mod's appended blueprints — so we must scan every match,
            // mirroring the game's own TemplateCollectionService.Load pattern of SelectMany over
            // GetSpecs<TemplateCollectionSpec>().
            var matchingCollectionSpecs = collectionSpecs.Where(c => c.CollectionId == colId);
            foreach (TemplateCollectionSpec colSpec in matchingCollectionSpecs)
            {
              foreach (var bpAsset in colSpec.Blueprints)
              {
                Blueprint bp = specService.GetBlueprint(bpAsset.Path);
                TemplateSpec tSpec = bp?.GetSpec<TemplateSpec>();
                if (tSpec != null && !string.IsNullOrEmpty(tSpec.TemplateName))
                {
                  if (!FactionBlueprintCache.TemplateToFactionLocKey.ContainsKey(tSpec.TemplateName))
                  {
                    FactionBlueprintCache.TemplateToFactionLocKey[tSpec.TemplateName] = locKey;
                    FactionBlueprintCache.TemplateToFactionId[tSpec.TemplateName] = factionId;
                  }
                  if (!FactionBlueprintCache.TemplateToFactionIds.TryGetValue(tSpec.TemplateName, out HashSet<string> factionIds))
                  {
                    factionIds = new HashSet<string>();
                    FactionBlueprintCache.TemplateToFactionIds[tSpec.TemplateName] = factionIds;
                  }
                  factionIds.Add(factionId);
                }
              }
            }
          }
        }
      }

      // Deduplicate templates (unchanged)
      List<Blueprint> filteredTemplates = new List<Blueprint>();
      HashSet<string> seenTemplateNames = new HashSet<string>();
      HashSet<Blueprint> seenBlueprints = new HashSet<Blueprint>();

      foreach (Blueprint blueprint in __instance.AllTemplates)
      {
        if (blueprint == null || !seenBlueprints.Add(blueprint)) continue;
        TemplateSpec templateSpec = blueprint.GetSpec<TemplateSpec>();
        string templateName = templateSpec?.TemplateName;
        if (!string.IsNullOrEmpty(templateName) && !seenTemplateNames.Add(templateName)) continue;
        filteredTemplates.Add(blueprint);
      }

      __instance.AllTemplates = filteredTemplates.ToImmutableArray();
    }
  }

  [HarmonyPatch(typeof(LabeledEntity), "get_DisplayName")]
  public static class Patch_LabeledEntity_get_DisplayName
  {
    public static void Postfix(LabeledEntity __instance, ref string __result, ref string ____displayName, ILoc ____loc)
    {
      TemplateSpec templateSpec = __instance.GetComponent<TemplateSpec>();
      if (templateSpec == null || string.IsNullOrEmpty(templateSpec.TemplateName)) return;

      if (!FactionBlueprintCache.TemplateToFactionLocKey.TryGetValue(templateSpec.TemplateName, out string locKey)
          || string.IsNullOrEmpty(locKey)) return;

      // Resolve the localized faction name once per locKey; only cache successful resolutions
      // (untranslated keys fall through to the original name and retry on next read).
      if (!FactionBlueprintCache.LocKeyToFactionName.TryGetValue(locKey, out string factionName))
      {
        factionName = ____loc.T(locKey);
        if (string.IsNullOrEmpty(factionName) || factionName == locKey) return;
        FactionBlueprintCache.LocKeyToFactionName[locKey] = factionName;
      }

      string suffix = $" ({factionName})";
      if (!string.IsNullOrEmpty(__result) && !__result.EndsWith(suffix))
      {
        __result += suffix;
        ____displayName = __result;
      }
    }
  }
}