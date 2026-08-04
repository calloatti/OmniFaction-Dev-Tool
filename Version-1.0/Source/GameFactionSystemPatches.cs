using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HarmonyLib;
using Timberborn.FactionSystem;
using Timberborn.GameFactionSystem;
using Timberborn.NeedSpecs;

namespace Calloatti.OmniFactionDevTool
{
  // 1. Patch building template collections across all factions
  [HarmonyPatch(typeof(FactionTemplateCollectionIdProvider), nameof(FactionTemplateCollectionIdProvider.GetTemplateCollectionIds))]
  public static class Patch_FactionTemplateCollectionIdProvider_GetTemplateCollectionIds
  {
    public static void Postfix(FactionService ____factionService, ref IEnumerable<string> __result)
    {
      __result = FactionCollectionIdsAggregator.CombineWithAllFactions(____factionService, __result, (FactionSpec faction) => faction.TemplateCollectionIds);
    }
  }

  // 2. Patch good collections across all factions
  [HarmonyPatch(typeof(FactionGoodCollectionIdsProvider), nameof(FactionGoodCollectionIdsProvider.GetGoodCollectionIds))]
  public static class Patch_FactionGoodCollectionIdsProvider_GetGoodCollectionIds
  {
    public static void Postfix(FactionService ____factionService, ref IEnumerable<string> __result)
    {
      __result = FactionCollectionIdsAggregator.CombineWithAllFactions(____factionService, __result, (FactionSpec faction) => faction.GoodCollectionIds);
    }
  }

  // 3. Patch material collections across all factions
  [HarmonyPatch(typeof(FactionMaterialCollectionIdsProvider), nameof(FactionMaterialCollectionIdsProvider.GetMaterialCollectionIds))]
  public static class Patch_FactionMaterialCollectionIdsProvider_GetMaterialCollectionIds
  {
    public static void Postfix(FactionService ____factionService, ref IEnumerable<string> __result)
    {
      __result = FactionCollectionIdsAggregator.CombineWithAllFactions(____factionService, __result, (FactionSpec faction) => faction.MaterialCollectionIds);
    }
  }

  // 4. Aggregate need collections across all factions
  [HarmonyPatch(typeof(FactionNeedCollectionIdsProvider), nameof(FactionNeedCollectionIdsProvider.GetNeedCollectionIds))]
  public static class Patch_FactionNeedCollectionIdsProvider_GetNeedCollectionIds
  {
    public static void Postfix(FactionService ____factionService, ref IEnumerable<string> __result)
    {
      __result = FactionCollectionIdsAggregator.CombineWithAllFactions(____factionService, __result, (FactionSpec faction) => faction.NeedCollectionIds);
    }
  }

  // 5. Safe lookup for Beaver/Bot needs
  [HarmonyPatch(typeof(FactionNeedService), nameof(FactionNeedService.GetBeaverOrBotNeedById))]
  public static class Patch_FactionNeedService_GetBeaverOrBotNeedById
  {
    public static bool Prefix(FactionNeedService __instance, string id, ref NeedSpec __result)
    {
      NeedSpec needSpec = __instance.GetBeaverNeeds().FirstOrDefault((NeedSpec need) => need.Id == id)
                       ?? __instance.GetBotNeeds().FirstOrDefault((NeedSpec need) => need.Id == id);

      if (needSpec != null)
      {
        __result = needSpec;
        return false; // Skip original method to bypass SingleOrDefault throw
      }

      return true; // Fall back to original method if no match is found
    }
  }

  // Shared aggregation helper for the four Faction*CollectionIdsProvider postfixes.
  //
  // IMPORTANT (current-faction-wins dedup): the vanilla result (the CURRENT faction's collection
  // IDs) is seeded FIRST, then the other factions' IDs are appended. Because TemplateCollectionSystem
  // keeps the FIRST loaded blueprint per TemplateName, the current faction's templates are
  // enumerated first and therefore win any duplicate-name clash. Do not reorder this aggregation,
  // or which faction's building wins the dedup will silently flip.
  internal static class FactionCollectionIdsAggregator
  {
    internal static IEnumerable<string> CombineWithAllFactions(
        FactionService factionService,
        IEnumerable<string> result,
        Func<FactionSpec, ImmutableArray<string>> collectionIdSelector)
    {
      if (factionService == null)
      {
        return result;
      }

      FactionSpecService factionSpecService = factionService._factionSpecService;
      if (factionSpecService == null)
      {
        return result;
      }

      HashSet<string> combinedCollectionIds = new HashSet<string>(result);

      foreach (FactionSpec factionSpec in factionSpecService.Factions)
      {
        foreach (string collectionId in collectionIdSelector(factionSpec))
        {
          combinedCollectionIds.Add(collectionId);
        }
      }

      return combinedCollectionIds;
    }
  }
}