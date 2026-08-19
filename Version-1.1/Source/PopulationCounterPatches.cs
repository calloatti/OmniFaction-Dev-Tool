using HarmonyLib;
using Timberborn.AutomationBuildings;
using Timberborn.Population;

namespace Calloatti.OmniFaction
{
  // Hands a faction-filtered PopulationData to each Population Counter after the vanilla Sample()
  // has run, so a counter placed from a faction's blueprints (PopulationCounter.Folktails,
  // PopulationCounter.IronTeeth, ...) reports only its own faction across all 18 modes. The swap is
  // equivalent to the vanilla assignment the original Sample() makes (global data, or the counter's
  // instant-or-construction district data), and UpdateOutputState() is re-run so the output logic
  // reacts to the swapped data. "Common" counters (non-factioned blueprints) keep vanilla behavior.
  [HarmonyPatch(typeof(PopulationCounter), nameof(PopulationCounter.Sample))]
  public static class Patch_PopulationCounter_Sample
  {
    public static void Postfix(PopulationCounter __instance)
    {
      string faction = FactionAssignmentHelper.GetFactionID(__instance);
      if (faction == "Common") return;

      OmniFactionService service = OmniFactionService.Instance;
      if (service == null) return;

      PopulationData perFactionData = __instance.GlobalMode
          ? service.GetGlobal(faction)
          : service.GetDistrict(__instance._districtBuilding.GetInstantOrConstructionDistrict(), faction);

      __instance._sampledPopulationData = perFactionData;
      __instance.UpdateOutputState();
    }
  }
}
