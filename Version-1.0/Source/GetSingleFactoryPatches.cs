using System.Linq;
using HarmonyLib;
using Timberborn.BlockObstacles;
using Timberborn.BlockSystem;
using Timberborn.Common;
using Timberborn.RecoveredGoodSystem;
using Timberborn.TemplateSystem;
using Timberborn.WonderPlanes;

namespace Calloatti.OmniFaction
{
  // Patch BlockOccupationLayerFactory.Load to handle multiple BlockOccupierSpec blueprints safely
  [HarmonyPatch(typeof(BlockOccupationLayerFactory), nameof(BlockOccupationLayerFactory.Load))]
  public static class Patch_BlockOccupationLayerFactory_Load
  {
    public static bool Prefix(BlockOccupationLayerFactory __instance, TemplateService ____templateService)
    {
      var blockOccupierSpec = ____templateService.GetAll<BlockOccupierSpec>().FirstOrDefault();
      if (blockOccupierSpec != null)
      {
        __instance._blockOccupierTemplate = blockOccupierSpec.GetSpec<BlockObjectSpec>();
        return false; // Skip original method to bypass GetSingle<T>() exception
      }
      return true; // Fall back to original method if no spec is found
    }
  }

  // Patch RecoveredGoodStackFactory.Load to handle multiple RecoveredGoodStackSpec blueprints safely
  [HarmonyPatch(typeof(RecoveredGoodStackFactory), nameof(RecoveredGoodStackFactory.Load))]
  public static class Patch_RecoveredGoodStackFactory_Load
  {
    public static bool Prefix(RecoveredGoodStackFactory __instance, TemplateService ____templateService)
    {
      var recoveredGoodStackSpec = ____templateService.GetAll<RecoveredGoodStackSpec>().FirstOrDefault();
      if (recoveredGoodStackSpec != null)
      {
        __instance._recoveredGoodStackTemplate = recoveredGoodStackSpec.GetSpec<BlockObjectSpec>();
        __instance.GoodStackBlockSpec = __instance._recoveredGoodStackTemplate.Blocks.FirstOrDefault();
        return false; // Skip original method to bypass GetSingle<T>() and Blocks.Single() exceptions
      }
      return true; // Fall back to original method if no spec is found
    }
  }

  // Patch PlaneSpawner.Awake to handle multiple PlaneSpec blueprints safely
  [HarmonyPatch(typeof(PlaneSpawner), nameof(PlaneSpawner.Awake))]
  public static class Patch_PlaneSpawner_Awake
  {
    public static bool Prefix(PlaneSpawner __instance, TemplateService ____templateService)
    {
      var planeSpec = ____templateService.GetAll<PlaneSpec>().FirstOrDefault();
      if (planeSpec != null)
      {
        __instance._planeTemplate = planeSpec.Blueprint;
        string spawnPointName = __instance.GetComponent<PlaneSpawnerSpec>().SpawnPointName;
        __instance._spawnPoint = __instance.GameObject.FindChildTransform(spawnPointName);
        return false; // Skip original method to bypass GetSingle<T>() exception
      }
      return true; // Fall back to original method if no spec is found
    }
  }
}