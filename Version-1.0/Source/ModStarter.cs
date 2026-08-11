using HarmonyLib;
using Timberborn.Modding;
using Timberborn.ModManagerScene;

namespace Calloatti.OmniFaction
{
  // Native Timberborn Mod Starter Entry Point
  public class UnlockAllFactionBuildingsStarter : IModStarter
  {
    public void StartMod(IModEnvironment modEnvironment)
    {
      Harmony harmony = new Harmony("com.mod.unlockallfactionbuildings");
      harmony.PatchAll(GetType().Assembly);
    }
  }
}
