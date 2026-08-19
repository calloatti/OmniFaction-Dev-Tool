using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Timberborn.BlueprintSystem;
using Timberborn.Bots;
using Timberborn.TemplateInstantiation;
using Timberborn.TemplateSystem;
using Timberborn.Workshops;
using UnityEngine;

namespace Calloatti.OmniFaction
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

      // Clear before the branch so the fallback path (missing specs) also drops any stale
      // cross-session template list left over from a previous game.
      AllBotTemplates.Clear();

      if (botSpecs.Count > 0)
      {
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

  // Patch BotFactory.Create so spawned bots dynamically match the building that created them,
  // the DC-spawn faction when one is pending (OmniFactionService starting population), or the
  // faction of the nearest District Center to the spawn position (dev-tool spawns), falling back
  // to round-robin. TargetMethod resolves the Create overload per game version: (Vector3,
  // Quaternion, object) in 1.1, (Vector3, Quaternion) in 1.0, (Vector3) as a last resort.
  [HarmonyPatch(typeof(BotFactory))]
  public static class Patch_BotFactory_Create
  {
    private static int _botCounter;

    public static MethodBase TargetMethod()
    {
      return AccessTools.DeclaredMethod(typeof(BotFactory), nameof(BotFactory.Create), new[] { typeof(Vector3), typeof(Quaternion), typeof(object) })
          ?? AccessTools.DeclaredMethod(typeof(BotFactory), nameof(BotFactory.Create), new[] { typeof(Vector3), typeof(Quaternion) })
          ?? AccessTools.DeclaredMethod(typeof(BotFactory), nameof(BotFactory.Create), new[] { typeof(Vector3) });
    }

    public static bool Prefix(BotFactory __instance, Vector3 position)
    {
      var templates = Patch_BotFactory_Load.AllBotTemplates;
      if (templates.Count > 0)
      {
        // 1. Try the DC-spawn faction when one is pending (OmniFactionService starting population)
        string factionId = StartingBeaverSpawn.PendingFaction;

        // 2. Try to spawn based on the building that is currently finishing production
        if (string.IsNullOrEmpty(factionId))
        {
          var activeManufactory = Patch_Manufactory_IncreaseProductionProgress.ActiveManufactory;
          if (activeManufactory != null)
          {
            factionId = FactionAssignmentHelper.GetFactionID(activeManufactory);
          }
        }

        // 3. Try the nearest District Center's faction (dev-tool spawn at cursor)
        if (string.IsNullOrEmpty(factionId))
        {
          factionId = OmniFactionService.FindNearestDistrictFaction(position);
        }

        // 4. If a faction id was resolved, spawn the matching factioned template
        if (!string.IsNullOrEmpty(factionId))
        {
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

        // 5. Fallback to round-robin (e.g., if no DC/building faction could be resolved)
        __instance._botTemplate = templates[_botCounter++ % templates.Count];
      }
      return true;
    }
  }
}