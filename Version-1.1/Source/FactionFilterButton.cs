using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.AssetSystem;
using Timberborn.BottomBarSystem;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Calloatti.OmniFaction
{
  // Bottom-bar button that cycles through all factions and "All".
  // Uses a grayscale background tinted by faction color, and a dynamic text icon.
  public class FactionFilterButton : IBottomBarElementsProvider
  {
    private readonly VisualElementLoader _visualElementLoader;
    private readonly IAssetLoader _assetLoader;

    private VisualElement _root;
    private VisualElement _backgroundElement;
    private VisualElement _toolImageContainer;
    private Label _iconLabel;
    private Button _button;
    private Label _tooltip;

    // List of faction IDs, with null meaning "All".
    private List<string> _factionStates;
    private int _stateIndex;

    // Grayscale background sprite (loaded once).
    private Sprite _bgSprite;

    // Fixed color for the faction letter (rgb(193,166,112))
    private static readonly Color LetterColor = new Color(193f / 255f, 166f / 255f, 112f / 255f);

    public FactionFilterButton(VisualElementLoader visualElementLoader, IAssetLoader assetLoader)
    {
      _visualElementLoader = visualElementLoader;
      _assetLoader = assetLoader;
    }

    public IEnumerable<BottomBarElement> GetElements()
    {
      _root = _visualElementLoader.LoadVisualElement("Common/BottomBar/GrouplessToolButton");
      _backgroundElement = _root.Q<VisualElement>("Background");
      _toolImageContainer = _root.Q<VisualElement>("ToolImage");
      _button = _root.Q<Button>("ToolButton");
      _tooltip = _root.Q<Label>("Tooltip");

      // Load the grayscale background sprite once.
      _bgSprite = _assetLoader.Load<Sprite>("Sprites/BottomBar/button-bg-filter");

      // Build the list of faction states (All + all distinct factions).
      _factionStates = BuildFactionStateList();

      // Create the dynamic icon label inside the ToolImage container.
      _iconLabel = new Label
      {
        style =
                {
                    fontSize = 30, // larger font
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = new StyleColor(LetterColor), // fixed color
                    width = Length.Percent(100),
                    height = Length.Percent(100),
                }
      };
      _toolImageContainer.Clear();
      _toolImageContainer.Add(_iconLabel);

      // Start with "All" (index 0).
      _stateIndex = 0;
      UpdateIconAndTooltip();

      _button.RegisterCallback<ClickEvent>(_ => CycleState());

      // Tooltip hover logic.
      _tooltip.text = "Building filter";
      _tooltip.ToggleDisplayStyle(false);
      _root.RegisterCallback<MouseOverEvent>(_ => _tooltip.ToggleDisplayStyle(true));
      _root.RegisterCallback<MouseOutEvent>(_ => _tooltip.ToggleDisplayStyle(false));

      yield return BottomBarElement.CreateSingleLevel(_root);
    }

    private List<string> BuildFactionStateList()
    {
      // Start with "All" (null).
      var states = new List<string> { null };

      // Add all distinct faction IDs from the blueprint cache.
      if (FactionBlueprintCache.TemplateToFactionId != null)
      {
        var factions = FactionBlueprintCache.TemplateToFactionId.Values.Distinct().OrderBy(id => id);
        states.AddRange(factions);
      }
      else
      {
      }

      return states;
    }

    private void CycleState()
    {
      _stateIndex = (_stateIndex + 1) % _factionStates.Count;
      UpdateIconAndTooltip();

      // Apply the filter.
      string factionId = _factionStates[_stateIndex];
      FactionToolFilter.Singleton?.SetFilter(factionId);
    }

    private void UpdateIconAndTooltip()
    {
      string factionId = _factionStates[_stateIndex];

      // Update tooltip.
      string displayName = GetFactionDisplayName(factionId);
      _tooltip.text = $"Building filter: {displayName}";

      // Update the icon label: show first letter (or "A" for All) with fixed color.
      if (_iconLabel != null)
      {
        string iconText = string.IsNullOrEmpty(factionId) ? "A" : factionId[0].ToString().ToUpperInvariant();
        _iconLabel.text = iconText;
        // Color is already set in the label style, no need to change each time.
      }

    }

    private string GetFactionDisplayName(string factionId)
    {
      if (string.IsNullOrEmpty(factionId))
        return "All";

      // You can add a mapping to a prettier display name here.
      // For now, just return the ID.
      return factionId;
    }

    // This method can be called by the service when it loads to refresh the button.
    public void Refresh()
    {
      // Rebuild faction list in case new factions were added.
      _factionStates = BuildFactionStateList();
      // Ensure the current index is still valid.
      if (_stateIndex >= _factionStates.Count)
        _stateIndex = 0;
      UpdateIconAndTooltip();
    }
  }
}