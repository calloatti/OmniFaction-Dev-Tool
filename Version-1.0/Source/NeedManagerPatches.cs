using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.BlueprintSystem;
using Timberborn.FactionSystem;
using Timberborn.GameFactionSystem;
using Timberborn.NeedCollectionSystem;
using Timberborn.NeedSpecs;
using Timberborn.NeedSystem;
using Timberborn.SingletonSystem;

namespace Calloatti.OmniFactionDevTool
{
  public static class FactionNeedCache
  {
    // Maps FactionId (e.g., "Folktails", "IronTeeth", "CustomFaction") to a HashSet of valid Need Ids
    public static Dictionary<string, HashSet<string>> FactionAllowedNeeds { get; } = new Dictionary<string, HashSet<string>>();
  }

  // Intercept NeedVerifier.Load to natively build a cache of which needs belong to which faction.
  // NeedVerifier is guaranteed to have the fully loaded specs and services we need before entities spawn.
  [HarmonyPatch(typeof(NeedVerifier), nameof(NeedVerifier.Load))]
  public static class Patch_NeedVerifier_Load
  {
    public static void Postfix(NeedVerifier __instance)
    {
      FactionSpecService factionSpecService = Traverse.Create(__instance).Field("_factionSpecService").GetValue<FactionSpecService>();
      ISpecService specService = Traverse.Create(__instance).Field("_specService").GetValue<ISpecService>();

      if (factionSpecService == null || specService == null)
      {
        return;
      }

      var needCollections = specService.GetSpecs<NeedCollectionSpec>().ToList();

      FactionNeedCache.FactionAllowedNeeds.Clear();

      // Dynamically build the allowed needs matrix for every loaded faction
      foreach (FactionSpec faction in factionSpecService.Factions)
      {
        HashSet<string> allowedNeeds = new HashSet<string>();
        foreach (string collectionId in faction.NeedCollectionIds)
        {
          NeedCollectionSpec collection = needCollections.FirstOrDefault(c => c.CollectionId == collectionId);
          if (collection != null)
          {
            foreach (string needId in collection.Needs)
            {
              allowedNeeds.Add(needId);
            }
          }
        }
        FactionNeedCache.FactionAllowedNeeds[faction.Id] = allowedNeeds;
      }
    }
  }

  // Patch NeedManager to dynamically filter faction-specific needs for both Beavers and Bots.
  // Prevents crashes when entities try to fulfill cross-faction needs and lack the required animations.
  [HarmonyPatch(typeof(NeedManager), "GetNeeds")]
  public static class Patch_NeedManager_GetNeeds
  {
    public static void Postfix(NeedManager __instance, ref IEnumerable<NeedSpec> __result)
    {
      string entityName = __instance.GameObject.name;
      string matchedFactionId = null;

      // Find which faction this entity belongs to by checking its GameObject name 
      // (e.g., "Bot.Folktails Timberbot 3" or "Beaver.CustomFaction")
      foreach (string factionId in FactionNeedCache.FactionAllowedNeeds.Keys)
      {
        if (entityName.IndexOf(factionId, StringComparison.OrdinalIgnoreCase) >= 0)
        {
          matchedFactionId = factionId;
          break;
        }
      }

      // If we matched the entity to a faction, restrict its needs strictly to that faction's allowed needs.
      // This strips opposing faction needs before the Behavior Tree tries to use them.
      if (matchedFactionId != null && FactionNeedCache.FactionAllowedNeeds.TryGetValue(matchedFactionId, out HashSet<string> allowedNeeds))
      {
        __result = __result.Where(need => allowedNeeds.Contains(need.Id));
      }
    }
  }
}