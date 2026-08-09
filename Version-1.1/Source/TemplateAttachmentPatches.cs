using System;
using HarmonyLib;
using Timberborn.TemplateAttachmentSystem;
using UnityEngine;

namespace Calloatti.OmniFactionDevTool
{
  // Intercept the creation of visual attachments (like hats or backpacks).
  // Dynamically rewrite the requested attachment ID for N factions so cross-faction 
  // workers equip their own faction's version of the outfit instead of crashing.
  [HarmonyPatch(typeof(TemplateAttachments), nameof(TemplateAttachments.GetOrCreateAttachment))]
  public static class Patch_TemplateAttachments_GetOrCreateAttachment
  {
    public static bool Prefix(TemplateAttachments __instance, ref string id, ref TemplateAttachment __result)
    {
      // 1. Find the actual faction of the entity (the worker/bot) receiving the attachment
      string entityFaction = FactionAssignmentHelper.GetFactionID(__instance);

      // 2. Dynamically check against all loaded factions (N factions)
      if (!string.IsNullOrEmpty(entityFaction) && entityFaction != "Common")
      {
        foreach (string knownFaction in FactionNeedCache.FactionAllowedNeeds.Keys)
        {
          if (!knownFaction.Equals(entityFaction, StringComparison.OrdinalIgnoreCase) &&
              id.IndexOf(knownFaction, StringComparison.OrdinalIgnoreCase) >= 0)
          {
            // Swap the other faction's name in the attachment ID with the entity's faction name
            id = ReplaceIgnoreCase(id, knownFaction, entityFaction);
            break;
          }
        }
      }

      // 3. If it's already generated and in the cache with the new ID, return it immediately
      if (__instance._attachmentCache.TryGetValue(id, out var cachedAttachment))
      {
        __result = cachedAttachment;
        return false; // Skip original method
      }

      // 4. Check if the requested attachment ID actually exists in this entity's spec
      bool existsInSpec = false;
      if (__instance._templateAttachmentsSpec != null && __instance._templateAttachmentsSpec.Attachments != null)
      {
        foreach (var attachmentDef in __instance._templateAttachmentsSpec.Attachments)
        {
          if (attachmentDef.AttachmentId == id)
          {
            existsInSpec = true;
            break;
          }
        }
      }

      // 5. FAILSAFE: If the attachment STILL does not exist in the spec, generate a dummy instead of crashing
      if (!existsInSpec)
      {
        GameObject dummyObj = new GameObject("DummyMissingAttachment_" + id);
        dummyObj.transform.SetParent(__instance.GameObject.transform);
        dummyObj.SetActive(false);

        TemplateAttachment dummyAttachment = new TemplateAttachment(dummyObj);

        __instance._attachmentCache.Add(id, dummyAttachment);
        __result = dummyAttachment;

        return false; // Skip original method to prevent KeyNotFoundException
      }

      // 6. If it does exist in the spec, let the vanilla game instantiate it normally
      return true;
    }

    private static string ReplaceIgnoreCase(string input, string search, string replacement)
    {
      int index = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);
      if (index < 0)
      {
        return input;
      }
      return input.Substring(0, index) + replacement + input.Substring(index + search.Length);
    }
  }
}