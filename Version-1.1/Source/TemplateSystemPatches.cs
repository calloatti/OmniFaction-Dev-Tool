using HarmonyLib;
using Timberborn.TemplateSystem;

namespace Calloatti.OmniFactionDevTool
{
  // Failsafe to bypass duplicate name exception during mapping
  [HarmonyPatch(typeof(TemplateNameMapper), "TryAddTemplate")]
  public static class Patch_TemplateNameMapper_TryAddTemplate
  {
    public static void Prefix(ref bool throwIfDuplicated)
    {
      throwIfDuplicated = false;
    }
  }
}