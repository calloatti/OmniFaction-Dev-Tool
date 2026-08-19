using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Timberborn.Goods;
using UnityEngine;

namespace Calloatti.OmniFaction
{
  // Deduplicate GoodGroupSpecs in GoodsGroupSpecService.Load
  [HarmonyPatch(typeof(GoodsGroupSpecService), nameof(GoodsGroupSpecService.Load))]
  public static class Patch_GoodsGroupSpecService_Load
  {
    public static void Postfix(GoodsGroupSpecService __instance)
    {
      var list = __instance._goodGroupSpecs;

      var seen = new HashSet<string>();
      var distinct = new List<GoodGroupSpec>();
      foreach (var spec in list)
      {
        if (seen.Add(spec.Id))
          distinct.Add(spec);
      }

      if (distinct.Count != list.Count)
      {
        list.Clear();
        list.AddRange(distinct);
      }
    }
  }

  // Prevent GoodsGroupSpecService.GetSpec from throwing when multiple specs exist with the same ID.
  [HarmonyPatch(typeof(GoodsGroupSpecService), nameof(GoodsGroupSpecService.GetSpec))]
  public static class Patch_GoodsGroupSpecService_GetSpec
  {
    public static bool Prefix(string goodGroupId, GoodsGroupSpecService __instance, ref GoodGroupSpec __result)
    {
      var matches = __instance.GoodGroupSpecs.Where(spec => spec.Id == goodGroupId).ToList();
      if (matches.Count == 0)
      {
        __result = null;
        return false; // skip original; will not throw
      }
      __result = matches[0];
      return false; // skip original method
    }
  }
}
