using System;
using HarmonyLib;
using Timberborn.WorkerOutfitSystem;

namespace Calloatti.OmniFactionDevTool
{
  // WorkerOutfitService.Load only indexes the CURRENT faction's WorkerOutfitSpecs, so in the
  // multi-faction dev map a non-current faction's beaver/bot cannot resolve the outfit its
  // workplace specifies and silently wears none. This postfix unions every other faction's outfit
  // specs into the same dictionary (outfit ids embed the faction, so keys never collide; the
  // current faction's own rows were already added by the original method).
  [HarmonyPatch(typeof(WorkerOutfitService), nameof(WorkerOutfitService.Load))]
  public static class Patch_WorkerOutfitService_Load
  {
    public static void Postfix(WorkerOutfitService __instance)
    {
      string currentFactionId = __instance._factionService?.Current?.Id;
      foreach (WorkerOutfitSpec spec in __instance._specService.GetSpecs<WorkerOutfitSpec>())
      {
        if (string.IsNullOrEmpty(spec.FactionId) || spec.FactionId == currentFactionId)
        {
          continue;
        }
        int key = WorkerOutfitService.GetSpecKey(spec.Id, spec.WorkerType);
        if (!__instance._workerOutfitSpecs.ContainsKey(key))
        {
          __instance._workerOutfitSpecs[key] = spec;
        }
      }
    }
  }
}
