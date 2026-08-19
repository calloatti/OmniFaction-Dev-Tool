using System;
using System.Collections.Generic;
using HarmonyLib;
using Timberborn.TemplateAttachmentSystem;
using UnityEngine;

namespace Calloatti.OmniFaction
{
  [HarmonyPatch(typeof(TemplateAttachments), nameof(TemplateAttachments.GetOrCreateAttachment))]
  public static class Patch_TemplateAttachments_GetOrCreateAttachment
  {
    private static readonly Dictionary<(string, string), string> _rewrittenIdCache = new Dictionary<(string, string), string>();
    private static readonly Dictionary<TemplateAttachmentsSpec, HashSet<string>> _specAttachmentIdCache = new Dictionary<TemplateAttachmentsSpec, HashSet<string>>();

    // Statics survive across game sessions, so both caches must be dropped when the spec service
    // reloads (TemplateCollectionService.Load). _specAttachmentIdCache in particular holds
    // TemplateAttachmentsSpec references from the previous session's spec graph.
    internal static void ClearCaches()
    {
      _rewrittenIdCache.Clear();
      _specAttachmentIdCache.Clear();
    }

    public static bool Prefix(TemplateAttachments __instance, ref string id, ref TemplateAttachment __result)
    {
      string entityFaction = FactionAssignmentHelper.GetFactionID(__instance);
      if (!string.IsNullOrEmpty(entityFaction) && entityFaction != "Common")
      {
        string originalId = id;
        var key = (originalId, entityFaction);
        if (!_rewrittenIdCache.TryGetValue(key, out string rewrittenId))
        {
          rewrittenId = originalId;
          foreach (string knownFaction in FactionNeedCache.FactionAllowedNeeds.Keys)
          {
            if (!knownFaction.Equals(entityFaction, StringComparison.OrdinalIgnoreCase) &&
                originalId.IndexOf(knownFaction, StringComparison.OrdinalIgnoreCase) >= 0)
            {
              rewrittenId = ReplaceIgnoreCase(originalId, knownFaction, entityFaction);
              break;
            }
          }
          _rewrittenIdCache[key] = rewrittenId;
        }
        id = rewrittenId;
      }

      if (__instance._attachmentCache.TryGetValue(id, out var cachedAttachment))
      {
        __result = cachedAttachment;
        return false;
      }

      bool exists = false;
      var spec = __instance._templateAttachmentsSpec;
      if (spec != null && spec.Attachments != null)
      {
        if (!_specAttachmentIdCache.TryGetValue(spec, out HashSet<string> idSet))
        {
          idSet = new HashSet<string>();
          foreach (var def in spec.Attachments)
            idSet.Add(def.AttachmentId);
          _specAttachmentIdCache[spec] = idSet;
        }
        exists = idSet.Contains(id);
      }

      if (!exists)
      {
        GameObject dummyObj = new GameObject("DummyMissingAttachment_" + id);
        dummyObj.transform.SetParent(__instance.GameObject.transform);
        dummyObj.SetActive(false);
        TemplateAttachment dummyAttachment = new TemplateAttachment(dummyObj);
        __instance._attachmentCache.Add(id, dummyAttachment);
        __result = dummyAttachment;
        return false;
      }

      return true;
    }

    private static string ReplaceIgnoreCase(string input, string search, string replacement)
    {
      int index = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);
      if (index < 0) return input;
      return input.Substring(0, index) + replacement + input.Substring(index + search.Length);
    }
  }
}