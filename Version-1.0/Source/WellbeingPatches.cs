using System.Collections.Generic;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.NeedSpecs;
using Timberborn.NeedSystem;
using Timberborn.Wellbeing;
using Timberborn.WellbeingUI;

namespace Calloatti.OmniFactionDevTool
{
  // Rebuilds per-need "eligible" counts (how many beavers actually have each need) whenever the
  // wellbeing service counts applied needs. The population wellbeing panel divides each need by
  // the TOTAL beaver count, which dilutes faction-specific needs (e.g. Energy for IronTeeth) so
  // their bars can never reach 100% and max wellbeing looks unreachable. These eligible counts let
  // the panel use the true per-need population instead. Scope (global vs district) matches
  // automatically because the postfix receives the same needManagers enumeration the original just
  // counted.
  [HarmonyPatch(typeof(WellbeingService), "AppliedNeeds")]
  public static class Patch_WellbeingService_AppliedNeeds
  {
    public static readonly Dictionary<string, int> EligibleNeedCounts = new Dictionary<string, int>();

    public static void Postfix(IEnumerable<NeedManager> needManagers)
    {
      EligibleNeedCounts.Clear();

      foreach (NeedManager needManager in needManagers)
      {
        if (!needManager.HasComponent<WellbeingTrackerRegistrar>())
        {
          continue;
        }

        foreach (NeedSpec needSpec in needManager.NeedSpecs)
        {
          EligibleNeedCounts.TryGetValue(needSpec.Id, out int count);
          EligibleNeedCounts[needSpec.Id] = count + 1;
        }
      }
    }
  }

  // Replace the panel's per-need denominators (total beaver count) with the number of beavers that
  // actually have that need, so a need nobody carries shows "0 / 0" and a fully satisfied
  // faction-specific need can reach 100%.
  [HarmonyPatch(typeof(PopulationWellbeingBox), "UpdateCounters")]
  public static class Patch_PopulationWellbeingBox_UpdateCounters
  {
    public static bool Prefix(PopulationWellbeingBox __instance)
    {
      foreach (PopulationWellbeingCounter counter in __instance._counters)
      {
        int appliedCount = __instance._appliedCount.GetValueOrDefault(counter.NeedId, 0);
        int eligibleCount = Patch_WellbeingService_AppliedNeeds.EligibleNeedCounts.GetValueOrDefault(counter.NeedId, 0);
        counter.UpdateValues(appliedCount, eligibleCount);
      }

      return false; // Skip original method
    }
  }
}
