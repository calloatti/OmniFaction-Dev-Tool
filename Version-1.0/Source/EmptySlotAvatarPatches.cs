using System;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.CharactersUI;
using Timberborn.DwellingSystem;
using Timberborn.DwellingSystemUI;
using Timberborn.FactionSystem;
using Timberborn.GameFactionSystem;
using Timberborn.WorkSystemUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Calloatti.OmniFactionDevTool
{
  // A building's vacant character slots (dwelling lodges, workplace worker rows) render their
  // placeholder portrait from FactionService.Current, so in the multi-faction dev map every empty
  // slot shows the current faction's face. These patches push the BUILDING's faction into
  // EntityFactionAvatarHelper.PendingEmptySlotFaction while a building panel is being drawn, and
  // CharacterButton's Show*Empty postfixes use it to show that building's faction avatar instead.
  // Unfactioned/"Common" buildings and any render outside a building panel keep vanilla behavior.

  [HarmonyPatch(typeof(WorkerView), "ShowEmpty", new[] { typeof(bool) })]
  public static class Patch_WorkerView_ShowEmpty
  {
    public static void Prefix(WorkerView __instance)
    {
      if (__instance._workplaceWorkerType != null)
      {
        EntityFactionAvatarHelper.PendingEmptySlotFaction = FactionAssignmentHelper.GetFactionID(__instance._workplaceWorkerType);
      }
    }

    public static void Postfix(WorkerView __instance)
    {
      EntityFactionAvatarHelper.PendingEmptySlotFaction = null;
    }
  }

  [HarmonyPatch(typeof(DwellingUserFragment), nameof(DwellingUserFragment.ShowFragment))]
  public static class Patch_DwellingUserFragment_ShowFragment
  {
    public static void Prefix(DwellingUserFragment __instance, BaseComponent entity)
    {
      Dwelling dwelling = entity.GetComponent<Dwelling>();
      EntityFactionAvatarHelper.PendingEmptySlotFaction = dwelling != null
          ? FactionAssignmentHelper.GetFactionID(dwelling)
          : null;
    }

    public static void Postfix(DwellingUserFragment __instance)
    {
      EntityFactionAvatarHelper.PendingEmptySlotFaction = null;
    }
  }

  [HarmonyPatch(typeof(DwellingUserFragment), nameof(DwellingUserFragment.UpdateFragment))]
  public static class Patch_DwellingUserFragment_UpdateFragment
  {
    public static void Prefix(DwellingUserFragment __instance)
    {
      EntityFactionAvatarHelper.PendingEmptySlotFaction = __instance._dwelling != null
          ? FactionAssignmentHelper.GetFactionID(__instance._dwelling)
          : null;
    }

    public static void Postfix(DwellingUserFragment __instance)
    {
      EntityFactionAvatarHelper.PendingEmptySlotFaction = null;
    }
  }

  [HarmonyPatch(typeof(CharacterButton), nameof(CharacterButton.ShowAdultEmpty))]
  public static class Patch_CharacterButton_ShowAdultEmpty
  {
    public static void Postfix(CharacterButton __instance)
    {
      FactionSpec faction = EmptySlotFactionResolver.ResolvePendingFaction(__instance);
      if (faction == null)
      {
        return;
      }
      __instance._button.style.backgroundImage = new StyleBackground(faction.Avatar.Asset);
    }
  }

  [HarmonyPatch(typeof(CharacterButton), nameof(CharacterButton.ShowChildEmpty))]
  public static class Patch_CharacterButton_ShowChildEmpty
  {
    public static void Postfix(CharacterButton __instance)
    {
      FactionSpec faction = EmptySlotFactionResolver.ResolvePendingFaction(__instance);
      if (faction == null)
      {
        return;
      }
      __instance._button.style.backgroundImage = new StyleBackground(faction.ChildAvatar.Asset);
    }
  }

  [HarmonyPatch(typeof(CharacterButton), nameof(CharacterButton.ShowBotEmpty))]
  public static class Patch_CharacterButton_ShowBotEmpty
  {
    public static void Postfix(CharacterButton __instance)
    {
      FactionSpec faction = EmptySlotFactionResolver.ResolvePendingFaction(__instance);
      if (faction == null)
      {
        return;
      }
      __instance._button.style.backgroundImage = new StyleBackground(faction.BotAvatar.Asset);
    }
  }

  internal static class EmptySlotFactionResolver
  {
    public static FactionSpec ResolvePendingFaction(CharacterButton characterButton)
    {
      string factionId = EntityFactionAvatarHelper.PendingEmptySlotFaction;
      if (string.IsNullOrEmpty(factionId) || factionId == "Common")
      {
        return null;
      }
      FactionSpecService factionSpecService = characterButton._factionService?._factionSpecService;
      if (factionSpecService == null)
      {
        return null;
      }
      foreach (FactionSpec faction in factionSpecService.Factions)
      {
        if (faction.Id == factionId)
        {
          return faction;
        }
      }
      return null;
    }
  }
}
