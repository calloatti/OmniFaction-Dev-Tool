using System.Collections.Generic;
using Timberborn.BlockObjectTools;
using Timberborn.Debugging;
using Timberborn.PlantingUI;
using Timberborn.SingletonSystem;
using Timberborn.TemplateSystem;
using Timberborn.ToolSystem;

namespace Calloatti.OmniFaction
{
  // Holds the bottom-bar building/planting filter state (All / Folktails / IronTeeth).
  // The filter is enforced through ToolButton.ToolEnabled (see
  // Patch_ToolButton_ToolEnabled); SetFilter reposts DevModeToggledEvent so every
  // ToolButton re-evaluates its visibility, the same refresh trick as the Tool Finder mod.
  // Faction classification is centralized in FactionBlueprintCache.TemplateToFactionIds
  // (template name -> faction set), shared with the background coloring in
  // FactionToolButtonBackground.
  public class FactionToolFilter : ILoadableSingleton
  {
    public static FactionToolFilter Singleton { get; private set; }

    private readonly EventBus _eventBus;

    public string CurrentFilter { get; private set; }

    public FactionToolFilter(EventBus eventBus)
    {
      _eventBus = eventBus;
    }

    public void Load()
    {
      Singleton = this;
    }

    public void SetFilter(string factionId)
    {
      CurrentFilter = factionId;
      _eventBus.Post(new DevModeToggledEvent(enabled: false));
    }

    internal bool ToolMatches(ITool tool)
    {
      if (string.IsNullOrEmpty(CurrentFilter)) return true;

      string templateName = null;
      if (tool is BlockObjectTool blockObjectTool)
      {
        templateName = blockObjectTool.Template?.GetSpec<TemplateSpec>()?.TemplateName;
      }
      else if (tool is PlantingTool plantingTool)
      {
        templateName = plantingTool.PlantableSpec?.GetSpec<TemplateSpec>()?.TemplateName;
      }

      if (string.IsNullOrEmpty(templateName)) return true;

      if (!FactionBlueprintCache.TemplateToFactionIds.TryGetValue(templateName, out HashSet<string> factions) || factions.Count == 0)
      {
        return true; // Common/unmapped tools are always visible
      }
      return factions.Contains(CurrentFilter);
    }
  }
}
