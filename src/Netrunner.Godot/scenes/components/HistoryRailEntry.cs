using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Binding for one authored marker in the collapsed history rail.</summary>
[Tool]
public partial class HistoryRailEntry : Button
{
	private string _entryKey = string.Empty;
	private Action<string>? _open;
	private bool _bound;

	public override void _EnterTree()
	{
		WireSignals();
	}

	public override void _Ready()
	{
		WireSignals();
	}

	public override void _ExitTree()
	{
		if (_bound && IsInstanceValid(this))
		{
			Pressed -= HandlePressed;
		}
		_bound = false;
	}

	private void WireSignals()
	{
		if (_bound)
		{
			return;
		}

		Pressed += HandlePressed;
		_bound = true;
	}

	public void Bind(
		MatchHistoryEntry entry,
		Action<string> open,
		int fontSize,
		Color accentColor,
		Color mutedColor,
		Color corpCueColor,
		Color runnerCueColor)
	{
		ArgumentNullException.ThrowIfNull(entry);
		ArgumentNullException.ThrowIfNull(open);
		_entryKey = entry.Key;
		_open = open;
		Text = entry.Side == PlayerSide.Corp ? "C" : "R";
		TooltipText = MatchHistoryPanel.FormatEntryTooltip(entry);
		AddThemeFontSizeOverride("font_size", fontSize);
		AddThemeColorOverride("font_color", MatchHistoryPanel.SideColour(entry.Side, corpCueColor, runnerCueColor));
		AddThemeColorOverride("font_hover_color", accentColor);
		AddThemeColorOverride("font_focus_color", accentColor);
		AddThemeColorOverride("font_disabled_color", mutedColor);
	}

	public void ApplyAppearance(
		int fontSize,
		Color accentColor,
		Color mutedColor,
		Color corpCueColor,
		Color runnerCueColor)
	{
		AddThemeFontSizeOverride("font_size", fontSize);
		AddThemeColorOverride("font_hover_color", accentColor);
		AddThemeColorOverride("font_focus_color", accentColor);
		AddThemeColorOverride("font_disabled_color", mutedColor);
		if (Text is "C" or "R")
		{
			AddThemeColorOverride(
				"font_color",
				Text == "C" ? corpCueColor : runnerCueColor);
		}
	}

	private void HandlePressed()
	{
		_open?.Invoke(_entryKey);
	}
}
