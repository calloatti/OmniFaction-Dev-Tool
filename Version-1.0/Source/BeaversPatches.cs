using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Timberborn.BaseComponentSystem;
using Timberborn.Beavers;
using Timberborn.BlueprintSystem;
using Timberborn.Characters;
using Timberborn.FactionSystem;
using Timberborn.GameDistricts;
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

    // Before the game is playable (ShowPrimaryUIEvent), the game's own starting-settlement spawn
    // (StartingBeaversInitializer) routes through CreateAdult/CreateChild and must produce the
    // player's starting faction, not the round-robin dev-tool pool. Returns null once playable.
    public static Blueprint PickAdultTemplateForStartupFaction()
    {
      if (OmniFactionService.StartupComplete) return null;
      return PickAdultTemplateForFaction(OmniFactionService.CurrentFaction);
    }

    public static Blueprint PickChildTemplateForStartupFaction()
    {
      if (OmniFactionService.StartupComplete) return null;
      return PickChildTemplateForFaction(OmniFactionService.CurrentFaction);
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

  // Thread the spawning building's faction into the next Create* call so newborns
  // (BreedingPods, ProcreationHouses, dwelling UI spawns) match their building's faction.
  // Buildings that don't resolve to a faction fall back to the district center's faction,
  // then null so the caller round-robins.
  //
  // NOTE: Split into two separate Harmony patch classes (not a single class with two
  // [HarmonyPatch] attributes) because Harmony 2.4.1 silently fails to apply the Prefix
  // to one of the two target methods when stacked — verified: SpawnChild prefix fired
  // correctly but SpawnAdult prefix never did, so PendingFaction was always null for
  // advanced breeding pod adult spawns, causing round-robin (wrong faction) spawns.
  [HarmonyPatch(typeof(NewbornSpawner), nameof(NewbornSpawner.SpawnAdult))]
  public static class Patch_NewbornSpawner_SpawnAdult
  {
    public static void Prefix(BaseComponent spawner)
    {
      NewbornSpawnerFactionHelper.SetPendingFaction(spawner);
    }

    public static void Postfix()
    {
      Patch_NewbornSpawner.PendingFaction = null;
    }
  }

  [HarmonyPatch(typeof(NewbornSpawner), nameof(NewbornSpawner.SpawnChild))]
  public static class Patch_NewbornSpawner_SpawnChild
  {
    public static void Prefix(BaseComponent spawner)
    {
      NewbornSpawnerFactionHelper.SetPendingFaction(spawner);
    }

    public static void Postfix()
    {
      Patch_NewbornSpawner.PendingFaction = null;
    }
  }

  // Shared static holder for the pending faction, set by both SpawnAdult/SpawnChild prefixes.
  public static class Patch_NewbornSpawner
  {
    public static string PendingFaction;
  }

  // Shared: resolve the spawner's faction and set PendingFaction.
  // 1. Try the building's own template name (cache lookup or substring match).
  // 2. If "Common" (shared/unfactioned building), fall back to the district center's
  //    faction — so a shared breeding pod in a Folktails district still spawns Folktails.
  internal static class NewbornSpawnerFactionHelper
  {
    public static void SetPendingFaction(BaseComponent spawner)
    {
      string faction = FactionAssignmentHelper.GetFactionID(spawner);

      if (string.Equals(faction, "Common", StringComparison.OrdinalIgnoreCase))
      {
        DistrictBuilding districtBuilding = spawner?.GetComponent<DistrictBuilding>();
        if (districtBuilding != null)
        {
          DistrictCenter district = districtBuilding.District;
          if (district != null)
          {
            string dcFaction = FactionAssignmentHelper.GetFactionID(district);
            if (!string.Equals(dcFaction, "Common", StringComparison.OrdinalIgnoreCase))
            {
              faction = dcFaction;
            }
          }
        }
      }

      Patch_NewbornSpawner.PendingFaction = string.Equals(faction, "Common", StringComparison.OrdinalIgnoreCase) ? null : faction;
    }
  }

  // Faction threaded into the next CreateAdult/CreateChild call by OmniFactionService when it
  // spawns a faction's starting population at a freshly placed District Center. Only set around
  // those spawn calls, so it never interferes with newborn or dev-tool spawns.
  public static class StartingBeaverSpawn
  {
    public static string PendingFaction;
  }

  // Set _adultTemplate to the DC-spawn faction when one is pending (OmniFactionService starting
  // population), otherwise the startup faction before the game is playable, otherwise the faction
  // of the nearest District Center to the spawn position (dev-tool spawns), falling back to
  // round-robin, before the original Create runs.
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.CreateAdult))]
  public static class Patch_BeaverFactory_CreateAdult
  {
    public static bool Prefix(BeaverFactory __instance, Vector3 position)
    {
      var template = Patch_BeaverFactory_Load.PickAdultTemplateForFaction(StartingBeaverSpawn.PendingFaction)
                     ?? Patch_BeaverFactory_Load.PickAdultTemplateForStartupFaction()
                     ?? Patch_BeaverFactory_Load.PickAdultTemplateForFaction(OmniFactionService.FindNearestDistrictFaction(position))
                     ?? Patch_BeaverFactory_Load.PickAdultTemplate();
      if (template != null)
      {
        __instance._adultTemplate = template;
      }
      return true; // Run original method
    }
  }

  // Set _childTemplate to the spawning building's factioned template when available (newborns from
  // BreedingPods/ProcreationHouses/dwelling UI), or the DC-spawn faction when one is pending
  // (OmniFactionService starting population), or the startup faction before the game is playable,
  // or the faction of the nearest District Center to the spawn position (dev-tool spawns), falling
  // back to round-robin. Also covers CreateNewbornChild, which delegates to CreateChild.
  [HarmonyPatch(typeof(BeaverFactory), nameof(BeaverFactory.CreateChild))]
  public static class Patch_BeaverFactory_CreateChild
  {
    public static bool Prefix(BeaverFactory __instance, Vector3 position)
    {
      string faction = Patch_NewbornSpawner.PendingFaction ?? StartingBeaverSpawn.PendingFaction;
      var template = Patch_BeaverFactory_Load.PickChildTemplateForFaction(faction)
                     ?? Patch_BeaverFactory_Load.PickChildTemplateForStartupFaction()
                     ?? Patch_BeaverFactory_Load.PickChildTemplateForFaction(OmniFactionService.FindNearestDistrictFaction(position))
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

  // Shared faction-fur resolution: match an entity name against the loaded faction ids and apply
  // the matched faction's first texture to the material. Used by the BeaverTextureSetter.Start
  // prefix.
  public static class FactionFurHelper
  {
    public static FactionSpec FindFactionForName(FactionSpecService factionSpecService, string entityName)
    {
      if (factionSpecService == null || string.IsNullOrEmpty(entityName))
      {
        return null;
      }
      foreach (FactionSpec factionSpec in factionSpecService.Factions)
      {
        if (entityName.IndexOf(factionSpec.Id, StringComparison.OrdinalIgnoreCase) >= 0)
        {
          return factionSpec;
        }
      }
      return null;
    }

    public static bool TryApplyFactionFur(FactionSpec faction, CharacterMaterialModifier materialModifier, bool isChild)
    {
      if (faction == null || materialModifier == null)
      {
        return false;
      }
      var factionTextures = isChild ? faction.ChildTextures : faction.Textures;
      if (factionTextures.Length == 0)
      {
        return false;
      }
      materialModifier.SetTexture(Shader.PropertyToID("_BaseMap"), factionTextures[0].Asset);
      return true;
    }
  }

  // Patch BeaverTextureSetter.Start so each beaver wears the fur texture of ITS OWN faction,
  // matched from the entity name (e.g. "BeaverAdult.Folktails") so fur stays aligned with the
  // beaver's faction-scoped needs. Shared/unsuffixed beavers fall back to round-robin.
  // Uses the first texture of each faction's set; the 1-5 variants are applied later per role.
  [HarmonyPatch(typeof(BeaverTextureSetter), nameof(BeaverTextureSetter.Start))]
  [HarmonyPriority(Priority.First)]
  public static class Patch_BeaverTextureSetter_Start
  {
    private static int _factionCounter;

    public static bool Prefix(BeaverTextureSetter __instance)
    {
      CharacterMaterialModifier materialModifier = __instance.GetComponent<CharacterMaterialModifier>();
      bool isChild = __instance.GetComponent<Child>() != null;

      string entityName = __instance.GameObject.name;
      FactionSpec faction = FactionFurHelper.FindFactionForName(__instance._factionService._factionSpecService, entityName);
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
          Debug.Log($"[OmniFactionDevTool] BeaverTextureSetter.Start: '{entityName}' matched no faction and none available for round-robin; falling back to original");
          return true; // Fall back to original method (current faction's textures)
        }

        faction = factionsWithTextures[_factionCounter++ % factionsWithTextures.Count];
        Debug.Log($"[OmniFactionDevTool] BeaverTextureSetter.Start: '{entityName}' matched no faction; round-robin -> {faction.Id}");
      }
      else
      {
        Debug.Log($"[OmniFactionDevTool] BeaverTextureSetter.Start: '{entityName}' matched faction {faction.Id}");
      }

      if (FactionFurHelper.TryApplyFactionFur(faction, materialModifier, isChild))
      {
        var textures = isChild ? faction.ChildTextures : faction.Textures;
        Debug.Log($"[OmniFactionDevTool] BeaverTextureSetter.Start: '{entityName}' applied {faction.Id} texture '{textures[0].Asset.name}'");
        return false; // Skip original method
      }

      Debug.Log($"[OmniFactionDevTool] BeaverTextureSetter.Start: '{entityName}' matched faction {faction.Id} but it has no {(isChild ? "child " : "")}textures; falling back to original");
      return true; // Fall back to original method if the matched faction has no textures
    }
  }
}