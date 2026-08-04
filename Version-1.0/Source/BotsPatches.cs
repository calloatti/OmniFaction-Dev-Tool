using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Timberborn.BlueprintSystem;
using Timberborn.Bots;
using Timberborn.TemplateInstantiation;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Calloatti.OmniFactionDevTool
{
  // Patch BotFactory.Load to handle multiple BotSpec blueprints safely.
  // Collects and caches every BotSpec blueprint so that all faction bot types
  // (e.g. Bot.IronTeeth and Bot.Folktails, plus any modded factions) are available.
  [HarmonyPatch(typeof(BotFactory), nameof(BotFactory.Load))]
  public static class Patch_BotFactory_Load
  {
    public static List<Blueprint> AllBotTemplates { get; } = new List<Blueprint>();

    public static bool Prefix(BotFactory __instance, TemplateService ____templateService, TemplateInstantiator ____templateInstantiator)
    {
      var botSpecs = ____templateService.GetAll<BotSpec>().ToList();
      if (botSpecs.Count > 0)
      {
        AllBotTemplates.Clear();
        foreach (var botSpec in botSpecs)
        {
          ____templateInstantiator.CacheInstance(botSpec.Blueprint);
          AllBotTemplates.Add(botSpec.Blueprint);
        }

        __instance._botTemplate = AllBotTemplates[0];
        return false; // Skip original method to prevent InvalidOperationException
      }
      return true; // Fallback to original method if no BotSpec is found
    }
  }

  // Patch BotFactory.Create so spawned bots round-robin across all faction bot
  // templates. Keeps the original body (just swaps which template it instantiates).
  [HarmonyPatch(typeof(BotFactory), nameof(BotFactory.Create), new[] { typeof(Vector3), typeof(Quaternion) })]
  public static class Patch_BotFactory_Create
  {
    private static int _botCounter;

    public static bool Prefix(BotFactory __instance)
    {
      var templates = Patch_BotFactory_Load.AllBotTemplates;
      if (templates.Count > 0)
      {
        __instance._botTemplate = templates[_botCounter++ % templates.Count];
      }
      return true; // Run original method
    }
  }
}