using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Timberborn.Beavers;
using Timberborn.Characters;
using Timberborn.FactionSystem;
using Timberborn.TemplateInstantiation;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Calloatti.OmniFactionDevTool
{
  // Patch BeaverFactory.Load to handle multiple AdultSpec and ChildSpec blueprints safely
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.Load))]
  public static class Patch_BeaverFactory_Load
  {
    public static bool Prefix(BeaverFactory __instance, TemplateService ____templateService, TemplateInstantiator ____templateInstantiator)
    {
      var adultSpec = ____templateService.GetAll<AdultSpec>().FirstOrDefault();
      var childSpec = ____templateService.GetAll<ChildSpec>().FirstOrDefault();

      if (adultSpec != null && childSpec != null)
      {
        __instance._adultTemplate = adultSpec.Blueprint;
        __instance._childTemplate = childSpec.Blueprint;

        ____templateInstantiator.CacheInstance(adultSpec.Blueprint);
        ____templateInstantiator.CacheInstance(childSpec.Blueprint);

        return false; // Skip original method to bypass GetSingle<T>() exception
      }

      return true; // Fall back to original method if specs are missing
    }
  }

  // Patch BeaverTextureSetter.Start so beavers round-robin the DEFAULT fur texture of
  // each faction, letting Folktails (brown) and IronTeeth (gray) beavers coexist on the
  // dev map. Vanilla only ever uses _factionService.Current's textures, so a combined
  // all-factions map would show a single fur color. Uses the first texture of each
  // faction's set; the 1-5 variants are applied later per worker role.
  [HarmonyPatch(typeof(BeaverTextureSetter), nameof(BeaverTextureSetter.Start))]
  public static class Patch_BeaverTextureSetter_Start
  {
    private static int _factionCounter;

    public static bool Prefix(BeaverTextureSetter __instance)
    {
      CharacterMaterialModifier materialModifier = __instance.GetComponent<CharacterMaterialModifier>();
      bool isChild = __instance.GetComponent<Child>() != null;

      List<FactionSpec> factionsWithTextures = new List<FactionSpec>();
      FactionSpecService factionSpecService = __instance._factionService._factionSpecService;
      if (factionSpecService != null)
      {
        foreach (FactionSpec factionSpec in factionSpecService.Factions)
        {
          var textures = isChild ? factionSpec.ChildTextures : factionSpec.Textures;
          if (textures.Length > 0)
          {
            factionsWithTextures.Add(factionSpec);
          }
        }
      }

      if (factionsWithTextures.Count > 0)
      {
        FactionSpec faction = factionsWithTextures[_factionCounter++ % factionsWithTextures.Count];
        var textures = isChild ? faction.ChildTextures : faction.Textures;
        materialModifier.SetTexture(Shader.PropertyToID("_BaseMap"), textures[0].Asset);
        return false; // Skip original method
      }

      return true; // Fall back to original method (current faction's textures) if no faction textures exist
    }
  }
}