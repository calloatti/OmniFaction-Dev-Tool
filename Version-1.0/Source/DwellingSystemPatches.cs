using HarmonyLib;
using Timberborn.DwellingSystem;

namespace Calloatti.OmniFactionDevTool
{
  // Faction-restrict home assignment: a beaver may only be auto-assigned to its own faction's
  // dwelling. Unlike workplaces, dwellings have no priority ordering, so gating the single
  // CanAssignDweller decision point is sufficient — DwellerHomeAssigner (the stalest-dwelling
  // loop) simply skips rejected dwellings and moves on. "Common" dwellings accept anyone and
  // "Common" beavers (shared vanilla templates) may live anywhere.
  //
  // Not gated here (intentionally):
  //  - Dweller.AssignToDwellingAfterLoad (save restore) — re-establishes the saved home; we don't
  //    evict beavers that were already homed before this restriction.
  //  - CharacterBirth.PreInitializeEntity (newborns) — assigns the newborn to the spawning
  //    building's own dwelling, and Patch_NewbornSpawner already makes the newborn that building's
  //    faction, so this path is inherently same-faction.
  [HarmonyPatch(typeof(AutoAssignableDwelling), nameof(AutoAssignableDwelling.CanAssignDweller))]
  public static class Patch_AutoAssignableDwelling_CanAssignDweller
  {
    public static bool Prefix(AutoAssignableDwelling __instance, Dweller dweller, ref bool __result)
    {
      if (!FactionAssignmentHelper.CanResideAt(FactionAssignmentHelper.GetFactionID(dweller), FactionAssignmentHelper.GetFactionID(__instance)))
      {
        __result = false;
        return false; // Skip the original method — this dwelling is off-limits for this beaver
      }
      return true; // Factions are compatible — run the original method
    }
  }
}
