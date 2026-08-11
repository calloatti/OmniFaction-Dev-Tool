using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Timberborn.Goods;
using Timberborn.TopBarSystem;

namespace Calloatti.OmniFaction
{
  // Vanilla TopBarPanel.CreateCounter calls _goodService.Goods.Single(...) for every
  // SingleResourceGroup (e.g. Water, Badwater). With all factions loaded, a group can hold
  // duplicate IDs or several distinct goods, so .Single() throws
  // "Sequence contains more than one matching element" and TopBarPanel.PostLoad crashes.
  // Fix: pick one counter when the good is duplicated, and create a new top-bar item for
  // each unique good when a single-resource group holds more than one.
  [HarmonyPatch(typeof(TopBarPanel), nameof(TopBarPanel.CreateCounter))]
  public static class Patch_TopBarPanel_CreateCounter
  {
    public static bool Prefix(TopBarPanel __instance, GoodGroupSpec goodGroupSpec, ref ITopBarCounter __result)
    {
      if (!goodGroupSpec.SingleResourceGroup)
      {
        return true; // Extendable groups already show every good as a row.
      }

      // Deduplicate by good ID (vanilla .Single() throws on duplicates).
      List<string> uniqueGoods = __instance._goodService.Goods
          .Where(good => __instance.IsGroupGood(goodGroupSpec, good))
          .Distinct()
          .ToList();

      if (uniqueGoods.Count == 0)
      {
        return true; // Unreachable (GoodsGroupSpecService only keeps groups with goods).
      }

      // Create one simple counter per unique good — a new top-bar item each.
      for (int i = 0; i < uniqueGoods.Count; i++)
      {
        ITopBarCounter counter = __instance._topBarCounterFactory.CreateSimpleCounter(
            goodGroupSpec, uniqueGoods[i], __instance._root);
        if (i == 0)
        {
          __result = counter;
        }
        else
        {
          __instance._counters.Add(counter);
        }
      }

      return false; // Skip the original.
    }
  }
}
