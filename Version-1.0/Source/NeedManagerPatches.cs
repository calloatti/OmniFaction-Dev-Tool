using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.BlueprintSystem;
using Timberborn.Beavers;
using Timberborn.Bots;
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
    // Maps FactionId -> HashSet of valid Need Ids (for quick membership)
    public static Dictionary<string, HashSet<string>> FactionAllowedNeeds { get; } = new Dictionary<string, HashSet<string>>();

    // Maps FactionId -> List<NeedSpec> for beavers (pre‑computed, order preserved)
    public static Dictionary<string, List<NeedSpec>> FactionBeaverNeedSpecs { get; } = new Dictionary<string, List<NeedSpec>>();

    // Maps FactionId -> List<NeedSpec> for bots (pre‑computed, order preserved)
    public static Dictionary<string, List<NeedSpec>> FactionBotNeedSpecs { get; } = new Dictionary<string, List<NeedSpec>>();
  }

  [HarmonyPatch(typeof(NeedVerifier), nameof(NeedVerifier.Load))]
  public static class Patch_NeedVerifier_Load
  {
    public static void Postfix(NeedVerifier __instance)
    {
      FactionSpecService factionSpecService = __instance._factionSpecService;
      ISpecService specService = __instance._specService;
      if (factionSpecService == null || specService == null) return;

      var needCollections = specService.GetSpecs<NeedCollectionSpec>().ToList();
      // Build a dictionary for quick NeedSpec lookup by id, plus a loader-order index used as the
      // stable tie-break when two needs share an Order value (mirrors vanilla's LINQ OrderBy over
      // GetSpecs<NeedSpec>(), which is stable).
      var allNeedSpecs = specService.GetSpecs<NeedSpec>().ToList();
      var needSpecsById = allNeedSpecs.ToDictionary(spec => spec.Id);
      var specIndexById = new Dictionary<string, int>();
      for (int i = 0; i < allNeedSpecs.Count; i++) specIndexById[allNeedSpecs[i].Id] = i;

      FactionNeedCache.FactionAllowedNeeds.Clear();
      FactionNeedCache.FactionBeaverNeedSpecs.Clear();
      FactionNeedCache.FactionBotNeedSpecs.Clear();

      foreach (FactionSpec faction in factionSpecService.Factions)
      {
        HashSet<string> allowedIds = new HashSet<string>();
        List<NeedSpec> beaverSpecs = new List<NeedSpec>();
        List<NeedSpec> botSpecs = new List<NeedSpec>();

        // Helper to add needs from a collection, preserving order and deduplicating
        void AddNeedsFromCollection(NeedCollectionSpec collection)
        {
          if (collection == null) return;
          foreach (string needId in collection.Needs)
          {
            if (allowedIds.Contains(needId)) continue; // already added from earlier collection
            allowedIds.Add(needId);
            if (needSpecsById.TryGetValue(needId, out NeedSpec spec))
            {
              // Bucket by CharacterType exactly like vanilla FactionNeedService.GetBeaverNeeds
              // ("Beaver") / GetBotNeeds ("Bot"); specs with neither are in no list (matches
              // vanilla, where they sit in _needs but in neither filtered view).
              string charType = spec.CharacterType ?? "";
              if (charType == "Bot")
                botSpecs.Add(spec);
              else if (charType == "Beaver")
                beaverSpecs.Add(spec);
            }
            // If spec missing, we still track it in allowedIds but won't add to lists (shouldn't happen)
          }
        }

        // 1. Add "Common" collection FIRST (survival needs: hunger, thirst, sleep, injury)
        NeedCollectionSpec commonCollection = needCollections.FirstOrDefault(c => c.CollectionId == "Common");
        AddNeedsFromCollection(commonCollection);

        // 2. Then add faction-specific collections in the order they appear in faction.NeedCollectionIds
        foreach (string collectionId in faction.NeedCollectionIds)
        {
          // Skip "Common" because we already processed it
          if (collectionId == "Common") continue;
          NeedCollectionSpec collection = needCollections.FirstOrDefault(c => c.CollectionId == collectionId);
          AddNeedsFromCollection(collection);
        }

        // Preserve vanilla need order: FactionNeedService.Load sorts _needs by NeedSpec.Order
        // (stable LINQ OrderBy over GetSpecs<NeedSpec>()), and GetBeaverNeeds/GetBotNeeds filter
        // that sequence. GetSpecs alone returns loader (alphabetical) order, which is what made
        // beaver needs appear sorted alphabetically in the UI. Sort by Order, tie-break by loader
        // index to reproduce the vanilla stable sort exactly.
        beaverSpecs = beaverSpecs.OrderBy(spec => spec.Order).ThenBy(spec => specIndexById[spec.Id]).ToList();
        botSpecs = botSpecs.OrderBy(spec => spec.Order).ThenBy(spec => specIndexById[spec.Id]).ToList();

        FactionNeedCache.FactionAllowedNeeds[faction.Id] = allowedIds;
        FactionNeedCache.FactionBeaverNeedSpecs[faction.Id] = beaverSpecs;
        FactionNeedCache.FactionBotNeedSpecs[faction.Id] = botSpecs;
      }
    }
  }

  [HarmonyPatch(typeof(NeedManager), "GetNeeds")]
  public static class Patch_NeedManager_GetNeeds
  {
    public static void Postfix(NeedManager __instance, ref IEnumerable<NeedSpec> __result)
    {
      // Try cached faction (O(1))
      string faction = OmniFactionService.GetCachedFaction(__instance.GameObject);
      if (string.IsNullOrEmpty(faction) || faction == "Common")
      {
        // Fallback: scan entity name (only if not cached, e.g., initial load)
        string entityName = __instance.GameObject.name;
        foreach (string knownFaction in FactionNeedCache.FactionAllowedNeeds.Keys)
        {
          if (entityName.IndexOf(knownFaction, StringComparison.OrdinalIgnoreCase) >= 0)
          {
            faction = knownFaction;
            break;
          }
        }
        if (string.IsNullOrEmpty(faction) || faction == "Common") return;
      }

      // Determine if the entity is a beaver or a bot
      bool isBeaver = __instance.HasComponent<Beaver>();
      bool isBot = __instance.HasComponent<Bot>();

      List<NeedSpec> filteredSpecs = null;
      if (isBeaver)
        FactionNeedCache.FactionBeaverNeedSpecs.TryGetValue(faction, out filteredSpecs);
      else if (isBot)
        FactionNeedCache.FactionBotNeedSpecs.TryGetValue(faction, out filteredSpecs);

      if (filteredSpecs != null)
        __result = filteredSpecs;
    }
  }
}