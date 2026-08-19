using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Timberborn.Goods;
using Timberborn.ToolSystem;

namespace Calloatti.OmniFaction
{
  // Deduplicate goods in GoodService after loading, so that each good ID appears only once.
  [HarmonyPatch(typeof(GoodService), nameof(GoodService.Load))]
  public static class Patch_GoodService_Load
  {
    private static readonly AccessTools.FieldRef<GoodService, List<string>> GoodsRef =
        AccessTools.FieldRefAccess<GoodService, List<string>>("_goods");
    private static readonly AccessTools.FieldRef<GoodService, Dictionary<string, GoodSpec>> GoodSpecsByIdRef =
        AccessTools.FieldRefAccess<GoodService, Dictionary<string, GoodSpec>>("_goodSpecsById");

    public static void Postfix(GoodService __instance)
    {
      // Access private field _goods
      var goods = GoodsRef(__instance);

      // Remove duplicate good IDs (keep first occurrence)
      var distinctGoods = goods.Distinct().ToList();
      if (distinctGoods.Count != goods.Count)
      {
        GoodsRef(__instance) = distinctGoods;

        // Also rebuild _goodSpecsById to only contain the first spec for each ID
        var specDict = GoodSpecsByIdRef(__instance);
        var newDict = new Dictionary<string, GoodSpec>();
        foreach (var goodId in distinctGoods)
        {
          if (specDict.TryGetValue(goodId, out var spec))
            newDict[goodId] = spec;
        }
        GoodSpecsByIdRef(__instance) = newDict;
      }
    }
  }
}