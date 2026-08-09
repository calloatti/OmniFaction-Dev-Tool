using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Timberborn.BlueprintSystem;
using Timberborn.Bots;
using Timberborn.TemplateInstantiation;
using Timberborn.TemplateSystem;
using Timberborn.Workshops;
using UnityEngine;

namespace Calloatti.OmniFactionDevTool
{
  // We track the active Manufactory during the production tick to provide 
  // faction context when BotFactory.Create is subsequently called.
  [HarmonyPatch(typeof(Manufactory), nameof(Manufactory.IncreaseProductionProgress))]
  public static class Patch_Manufactory_IncreaseProductionProgress
  {
    public static Manufactory ActiveManufactory { get; private set; }

    public static void Prefix(Manufactory __instance)
    {
      ActiveManufactory = __instance;
    }

    public static void Postfix()
    {
      ActiveManufactory = null;
    }
  }

  // Patch BotFactory.Load to handle multiple BotSpec blueprints safely.
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

  // Patch BotFactory.Create so spawned bots dynamically match the building that created them.
  // Game 1.1: the (Vector3, Quaternion) overload became (Vector3, Quaternion, object initComponent);
  // the 1-arg Create(Vector3) delegates to it, so patching this overload covers both the
  // BotManufactory production path and the dev BotGeneratorTool spawn path.
  [HarmonyPatch(typeof(BotFactory), nameof(BotFactory.Create), new[] { typeof(Vector3), typeof(Quaternion), typeof(object) })]
  public static class Patch_BotFactory_Create
  {
    private static int _botCounter;

    public static bool Prefix(BotFactory __instance)
    {
      var templates = Patch_BotFactory_Load.AllBotTemplates;
      if (templates.Count > 0)
      {
        // 1. Try to spawn based on the building that is currently finishing production
        var activeManufactory = Patch_Manufactory_IncreaseProductionProgress.ActiveManufactory;
        if (activeManufactory != null)
        {
          // We use the FactionAssignmentHelper you added in WorkSystemPatches.cs
          string factionId = FactionAssignmentHelper.GetFactionID(activeManufactory);

          foreach (Blueprint template in templates)
          {
            TemplateSpec tSpec = template.GetSpec<TemplateSpec>();
            if (tSpec != null && tSpec.TemplateName.IndexOf(factionId, StringComparison.OrdinalIgnoreCase) >= 0)
            {
              __instance._botTemplate = template;
              return true; // Run original method with matched template
            }
          }
        }

        // 2. Fallback to round-robin (e.g., if you spawn a bot using Dev Tools out of thin air)
        __instance._botTemplate = templates[_botCounter++ % templates.Count];
      }
      return true;
    }
  }
}