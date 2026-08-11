using System.Linq;
using HarmonyLib;
using Timberborn.ModularShafts;
using Timberborn.RootProviders;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Calloatti.OmniFaction
{
  // Patch ShaftFrameFactory.Load to handle multiple ModularShaftPartsSpec blueprints safely
  [HarmonyPatch(typeof(ShaftFrameFactory), nameof(ShaftFrameFactory.Load))]
  public static class Patch_ShaftFrameFactory_Load
  {
    public static bool Prefix(ShaftFrameFactory __instance, TemplateService ____templateService, RootObjectProvider ____rootObjectProvider)
    {
      var single = ____templateService.GetAll<ModularShaftPartsSpec>().FirstOrDefault();
      if (single != null)
      {
        Transform root = ____rootObjectProvider.CreateRootObject("ShaftFrameFactory").transform;
        __instance._root = root;

        __instance._shaftBase = __instance.Instantiate(single.ShaftBase.Asset, root);
        __instance._shaftLowerFrame = __instance.Instantiate(single.ShaftLowerFrame.Asset, root);
        __instance._shaftSupport = __instance.Instantiate(single.ShaftSupport.Asset, root);
        __instance._shaftFrame = __instance.Instantiate(single.ShaftFrame.Asset, root);

        return false; // Skip original method
      }

      return true;
    }
  }

  // Patch ShaftModelFactory.Load to handle multiple ModularShaftPartsSpec blueprints safely
  [HarmonyPatch(typeof(ShaftModelFactory), nameof(ShaftModelFactory.Load))]
  public static class Patch_ShaftModelFactory_Load
  {
    public static bool Prefix(ShaftModelFactory __instance, TemplateService ____templateService)
    {
      var single = ____templateService.GetAll<ModularShaftPartsSpec>().FirstOrDefault();
      if (single != null)
      {
        __instance._modularShaftPartsSpec = single;
        return false; // Skip original method
      }

      return true;
    }
  }
}