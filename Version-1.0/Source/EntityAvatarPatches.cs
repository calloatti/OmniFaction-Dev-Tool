using System;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.BeaversUI;
using Timberborn.BotsUI;
using Timberborn.FactionSystem;
using Timberborn.GameFactionSystem;
using UnityEngine;

namespace Calloatti.OmniFactionDevTool
{
  // The game renders a beaver's/bot's portrait from FactionService.Current's avatars, so in the
  // multi-faction dev map every character shows the current faction's face. These postfixes
  // reroute the portrait to the entity's OWN faction's avatar, matched from the GameObject name
  // exactly like Patch_BeaverTextureSetter_Start. Unsuffixed/shared entities match no faction and
  // keep the original current-faction fallback.
  public static class EntityFactionAvatarHelper
  {
    // Set by the empty-slot renderers (EmptySlotAvatarPatches.cs) to the faction of the building
    // whose panel is being drawn; CharacterButton's Show*Empty postfixes read it so a building's
    // vacant character slots show that building's faction avatar instead of the current faction's.
    public static string PendingEmptySlotFaction;

    public static FactionSpec ResolveFaction(BaseComponent component, FactionService factionService)
    {
      FactionSpecService factionSpecService = factionService?._factionSpecService;
      if (factionSpecService == null)
      {
        return null;
      }
      string entityName = component.GameObject.name;
      foreach (FactionSpec factionSpec in factionSpecService.Factions)
      {
        if (entityName.IndexOf(factionSpec.Id, StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return factionSpec;
        }
      }
      return null;
    }
  }

  [HarmonyPatch(typeof(BeaverEntityBadge), nameof(BeaverEntityBadge.GetEntityAvatar))]
  public static class Patch_BeaverEntityBadge_GetEntityAvatar
  {
    public static void Postfix(BeaverEntityBadge __instance, ref Sprite __result)
    {
      FactionSpec faction = EntityFactionAvatarHelper.ResolveFaction(__instance, __instance._factionService);
      if (faction == null)
      {
        return; // Keep the original current-faction portrait
      }
      bool isChild = __instance._child != null;
      bool contaminated = __instance._contaminable != null && __instance._contaminable.IsContaminated;
      __result = contaminated
          ? (isChild ? faction.ContaminatedChildAvatar.Asset : faction.ContaminatedAdultAvatar.Asset)
          : (isChild ? faction.ChildAvatar.Asset : faction.Avatar.Asset);
    }
  }

  [HarmonyPatch(typeof(BotEntityBadge), nameof(BotEntityBadge.GetEntityAvatar))]
  public static class Patch_BotEntityBadge_GetEntityAvatar
  {
    public static void Postfix(BotEntityBadge __instance, ref Sprite __result)
    {
      FactionSpec faction = EntityFactionAvatarHelper.ResolveFaction(__instance, __instance._factionService);
      if (faction == null)
      {
        return;
      }
      __result = faction.BotAvatar.Asset;
    }
  }
}
