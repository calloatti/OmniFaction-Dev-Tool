using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.NeedSpecs;
using Timberborn.NeedSystem;
using Timberborn.Wellbeing;
using Timberborn.WellbeingUI;

namespace Calloatti.OmniFaction
{
  [HarmonyPatch(typeof(WellbeingService), "AppliedNeeds")]
  public static class Patch_WellbeingService_AppliedNeeds
  {
    private static readonly Dictionary<string, int> EligibleNeedCounts = new Dictionary<string, int>();

    public static void Postfix(IEnumerable<NeedManager> needManagers)
    {
      EligibleNeedCounts.Clear();

      foreach (NeedManager needManager in needManagers)
      {
        if (!needManager.HasComponent<WellbeingTrackerRegistrar>()) continue;
        foreach (NeedSpec needSpec in needManager.NeedSpecs)
        {
          EligibleNeedCounts.TryGetValue(needSpec.Id, out int count);
          EligibleNeedCounts[needSpec.Id] = count + 1;
        }
      }

      Patch_PopulationWellbeingBox_UpdateCounters.EligibleCounts = EligibleNeedCounts;
    }
  }

  [HarmonyPatch(typeof(PopulationWellbeingBox), "UpdateCounters")]
  public static class Patch_PopulationWellbeingBox_UpdateCounters
  {
    internal static Dictionary<string, int> EligibleCounts = new Dictionary<string, int>();

    public static bool Prefix(PopulationWellbeingBox __instance)
    {
      foreach (PopulationWellbeingCounter counter in __instance._counters)
      {
        int appliedCount = __instance._appliedCount.GetValueOrDefault(counter.NeedId, 0);
        int eligibleCount = EligibleCounts.GetValueOrDefault(counter.NeedId, 0);
        counter.UpdateValues(appliedCount, eligibleCount);
      }
      return false;
    }
  }
}