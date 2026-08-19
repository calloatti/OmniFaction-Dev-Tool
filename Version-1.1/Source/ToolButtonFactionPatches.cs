using HarmonyLib;
using System.Collections.Generic;
using Timberborn.BlockObjectToolsUI;
using Timberborn.BlockSystem;
using Timberborn.Planting;
using Timberborn.PlantingUI;
using Timberborn.TemplateSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using UnityEngine;

namespace Calloatti.OmniFaction
{
  // ---- Static queue to hold pending background applications ----
  public static class FactionBackgroundQueue
  {
    private static readonly List<(ToolButton ToolButton, string TemplateName)> _pending = new();

    public static void AddPending(ToolButton toolButton, string templateName)
    {
      lock (_pending)
      {
        _pending.Add((toolButton, templateName));
      }
    }

    public static void ProcessPending()
    {
      lock (_pending)
      {
        if (_pending.Count == 0)
        {
          return;
        }

        var service = OmniFactionService.Instance;
        if (service == null)
        {
          return;
        }

        foreach (var (toolButton, templateName) in _pending)
        {
          service.TryApplyFactionBackground(toolButton, templateName);
        }
        _pending.Clear();
      }
    }
  }

  [HarmonyPatch(typeof(BlockObjectToolButtonFactory), nameof(BlockObjectToolButtonFactory.Create),
      new[] { typeof(PlaceableBlockObjectSpec), typeof(UnityEngine.UIElements.VisualElement) })]
  public static class Patch_BlockObjectToolButtonFactory_Create
  {
    public static void Postfix(ToolButton __result, PlaceableBlockObjectSpec template)
    {
      TemplateSpec templateSpec = template?.GetSpec<TemplateSpec>();
      if (templateSpec != null)
      {
        var service = OmniFactionService.Instance;
        if (service == null)
        {
          // Service not ready – queue it.
          FactionBackgroundQueue.AddPending(__result, templateSpec.TemplateName);
          return;
        }
        service.TryApplyFactionBackground(__result, templateSpec.TemplateName);
      }
    }
  }

  [HarmonyPatch(typeof(PlantingToolButtonFactory), nameof(PlantingToolButtonFactory.CreatePlantingTool))]
  public static class Patch_PlantingToolButtonFactory_CreatePlantingTool
  {
    public static void Postfix(ToolButton __result, PlantableSpec plantableSpec)
    {
      TemplateSpec templateSpec = plantableSpec?.GetSpec<TemplateSpec>();
      if (templateSpec != null)
      {
        var service = OmniFactionService.Instance;
        if (service == null)
        {
          FactionBackgroundQueue.AddPending(__result, templateSpec.TemplateName);
          return;
        }
        service.TryApplyFactionBackground(__result, templateSpec.TemplateName);
      }
    }
  }

  [HarmonyPatch(typeof(ToolButton), nameof(ToolButton.ToolEnabled), MethodType.Getter)]
  public static class Patch_ToolButton_ToolEnabled
  {
    public static void Postfix(ToolButton __instance, ref bool __result)
    {
      if (__result && FactionToolFilter.Singleton != null)
      {
        __result = FactionToolFilter.Singleton.ToolMatches(__instance.Tool);
      }
    }
  }
}