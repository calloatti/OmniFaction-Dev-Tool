using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.Beavers;
using Timberborn.BlueprintSystem;
using Timberborn.Characters;
using Timberborn.FactionSystem;
using Timberborn.Reproduction;
using Timberborn.TemplateInstantiation;
using Timberborn.TemplateSystem;
using UnityEngine;

namespace Calloatti.OmniFactionDevTool
{
  // Patch BeaverFactory.Load to handle multiple AdultSpec and ChildSpec blueprints safely,
  // caching every beaver template so Create* calls can round-robin between the factions.
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.Load))]
  public static class Patch_BeaverFactory_Load
  {
    public static List<Blueprint> AllAdultTemplates { get; } = new List<Blueprint>();
    public static List<Blueprint> AllChildTemplates { get; } = new List<Blueprint>();

    private static int _adultCounter;
    private static int _childCounter;

    public static bool Prefix(BeaverFactory __instance, TemplateService ____templateService, TemplateInstantiator ____templateInstantiator)
    {
      var adultSpecs = ____templateService.GetAll<AdultSpec>().ToList();
      var childSpecs = ____templateService.GetAll<ChildSpec>().ToList();

      if (adultSpecs.Count > 0 && childSpecs.Count > 0)
      {
        AllAdultTemplates.Clear();
        AllChildTemplates.Clear();

        foreach (var adultSpec in adultSpecs)
        {
          ____templateInstantiator.CacheInstance(adultSpec.Blueprint);
          AllAdultTemplates.Add(adultSpec.Blueprint);
        }
        foreach (var childSpec in childSpecs)
        {
          ____templateInstantiator.CacheInstance(childSpec.Blueprint);
          AllChildTemplates.Add(childSpec.Blueprint);
        }

        __instance._adultTemplate = AllAdultTemplates[0];
        __instance._childTemplate = AllChildTemplates[0];

        return false; // Skip original method to bypass GetSingle<T>() exception
      }

      return true; // Fall back to original method if specs are missing
    }

    // Round-robin a factioned template so beavers of both factions coexist on the dev map.
    public static Blueprint PickAdultTemplate()
    {
      return PickTemplate(AllAdultTemplates, ref _adultCounter);
    }

    public static Blueprint PickChildTemplate()
    {
      return PickTemplate(AllChildTemplates, ref _childCounter);
    }

    // Pick the factioned template matching a faction id (e.g. the spawning building's or child's faction).
    // Returns null when the faction is unknown/empty or no matching template exists so the caller can
    // fall back to round-robin.
    public static Blueprint PickAdultTemplateForFaction(string factionId)
    {
      return PickTemplateForFaction(AllAdultTemplates, factionId);
    }

    public static Blueprint PickChildTemplateForFaction(string factionId)
    {
      return PickTemplateForFaction(AllChildTemplates, factionId);
    }

    private static Blueprint PickTemplateForFaction(List<Blueprint> templates, string factionId)
    {
      if (string.IsNullOrEmpty(factionId) || templates.Count == 0)
      {
        return null;
      }

      foreach (Blueprint blueprint in templates)
      {
        string templateName = blueprint.GetSpec<TemplateSpec>()?.TemplateName;
        if (!string.IsNullOrEmpty(templateName)
            && templateName.IndexOf(factionId, StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return blueprint;
        }
      }

      return null;
    }

    private static Blueprint PickTemplate(List<Blueprint> templates, ref int counter)
    {
      if (templates.Count == 0)
      {
        return null;
      }

      List<Blueprint> factioned = templates.Where(IsFactioned).ToList();
      List<Blueprint> pool = factioned.Count > 0 ? factioned : templates;
      return pool[counter++ % pool.Count];
    }

    // A factioned beaver template embeds the faction id in its TemplateName (e.g. "BeaverAdult.Folktails").
    private static bool IsFactioned(Blueprint blueprint)
    {
      string templateName = blueprint.GetSpec<TemplateSpec>()?.TemplateName;
      if (string.IsNullOrEmpty(templateName))
      {
        return false;
      }
      return FactionNeedCache.FactionAllowedNeeds.Keys.Any(factionId => templateName.IndexOf(factionId, StringComparison.OrdinalIgnoreCase) >= 0);
    }
  }

  // Thread the spawning building's faction into the next Create* call so newborns (BreedingPods,
  // ProcreationHouses, dwelling UI spawns) match their building's faction. Modded buildings that
  // don't resolve to a faction leave PendingFaction null and the caller falls back to round-robin.
  [HarmonyPatch(typeof(NewbornSpawner), nameof(NewbornSpawner.SpawnAdult))]
  [HarmonyPatch(typeof(NewbornSpawner), nameof(NewbornSpawner.SpawnChild))]
  public static class Patch_NewbornSpawner
  {
    public static string PendingFaction;

    public static void Prefix(BaseComponent spawner)
    {
      PendingFaction = FactionAssignmentHelper.GetFactionID(spawner);
      if (string.Equals(PendingFaction, "Common", StringComparison.OrdinalIgnoreCase))
      {
        PendingFaction = null;
      }
    }

    public static void Postfix()
    {
      PendingFaction = null;
    }
  }

  // Set _adultTemplate to a factioned template (round-robin) before the original Create runs.
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.CreateAdult))]
  public static class Patch_BeaverFactory_CreateAdult
  {
    public static bool Prefix(BeaverFactory __instance)
    {
      var template = Patch_BeaverFactory_Load.PickAdultTemplate();
      if (template != null)
      {
        __instance._adultTemplate = template;
      }
      return true; // Run original method
    }
  }

  // Set _childTemplate to the spawning building's factioned template when available (newborns from
  // BreedingPods/ProcreationHouses/dwelling UI), falling back to round-robin. Also covers
  // CreateNewbornChild, which delegates to CreateChild.
  // Game 1.1: BeaverFactory gained a private CreateChild(EntitySetup.Builder, Vector3, float)
  // overload, so the patch target must be disambiguated by argument types.
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.CreateChild), new[] { typeof(Vector3), typeof(float) })]
  public static class Patch_BeaverFactory_CreateChild
  {
    public static bool Prefix(BeaverFactory __instance)
    {
      var template = Patch_BeaverFactory_Load.PickChildTemplateForFaction(Patch_NewbornSpawner.PendingFaction)
                     ?? Patch_BeaverFactory_Load.PickChildTemplate();
      if (template != null)
      {
        __instance._childTemplate = template;
      }
      return true; // Run original method
    }
  }

  // Set _adultTemplate to the spawning building's factioned template when available (BreedingPods),
  // falling back to round-robin.
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.CreateNewbornAdult))]
  public static class Patch_BeaverFactory_CreateNewbornAdult
  {
    public static bool Prefix(BeaverFactory __instance)
    {
      var template = Patch_BeaverFactory_Load.PickAdultTemplateForFaction(Patch_NewbornSpawner.PendingFaction)
                     ?? Patch_BeaverFactory_Load.PickAdultTemplate();
      if (template != null)
      {
        __instance._adultTemplate = template;
      }
      return true; // Run original method
    }
  }

  // Set _adultTemplate to the child's own factioned template so a Folktails child grows into a
  // Folktails adult, falling back to round-robin.
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.CreateAdultFromChild))]
  public static class Patch_BeaverFactory_CreateAdultFromChild
  {
    public static bool Prefix(BeaverFactory __instance, Child child)
    {
      var template = Patch_BeaverFactory_Load.PickAdultTemplateForFaction(FactionAssignmentHelper.GetFactionID(child))
                     ?? Patch_BeaverFactory_Load.PickAdultTemplate();
      if (template != null)
      {
        __instance._adultTemplate = template;
      }
      return true; // Run original method
    }
  }

  // Patch BeaverTextureSetter.InitializeEntity (renamed from Start in game 1.1) so each beaver
  // wears the fur texture of ITS OWN faction, matched from the entity name (e.g.
  // "BeaverAdult.Folktails") so fur stays aligned with the beaver's faction-scoped needs.
  // Shared/unsuffixed beavers fall back to round-robin.
  // Uses the first texture of each faction's set; the 1-5 variants are applied later per role.
  [HarmonyPatch(typeof(BeaverTextureSetter), nameof(BeaverTextureSetter.InitializeEntity))]
  public static class Patch_BeaverTextureSetter_Start
  {
    private static int _factionCounter;

    public static bool Prefix(BeaverTextureSetter __instance)
    {
      CharacterMaterialModifier materialModifier = __instance.GetComponent<CharacterMaterialModifier>();
      bool isChild = __instance.GetComponent<Child>() != null;

      FactionSpec faction = FindFactionForEntity(__instance);
      if (faction == null)
      {
        // No faction matched in the entity name — round-robin across factions with textures
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

        if (factionsWithTextures.Count == 0)
        {
          return true; // Fall back to original method (current faction's textures)
        }

        faction = factionsWithTextures[_factionCounter++ % factionsWithTextures.Count];
      }

      var factionTextures = isChild ? faction.ChildTextures : faction.Textures;
      if (factionTextures.Length > 0)
      {
        materialModifier.SetTexture(Shader.PropertyToID("_BaseMap"), factionTextures[0].Asset);
        return false; // Skip original method
      }

      return true; // Fall back to original method if the matched faction has no textures
    }

    private static FactionSpec FindFactionForEntity(BeaverTextureSetter __instance)
    {
      FactionSpecService factionSpecService = __instance._factionService._factionSpecService;
      if (factionSpecService == null)
      {
        return null;
      }

      string entityName = __instance.GameObject.name;
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
}