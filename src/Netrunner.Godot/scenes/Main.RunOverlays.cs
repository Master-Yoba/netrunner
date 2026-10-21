using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Run overlays (UI plan U4): the encountered ICE's subroutines listed beside
/// it, marked broken, fired or still to fire, and the accessed card shown at
/// the centre of the board with how many accesses have happened.
/// </summary>
public partial class Main
{
	private PanelContainer? _encounterOverlay;
	private VBoxContainer? _encounterRows;
	private string? _encounterIceId;

	private void BuildEncounterOverlay()
	{
		_encounterOverlay = new PanelContainer { Name = "EncounterOverlay", Visible = false, ZIndex = 35, MouseFilter = Control.MouseFilterEnum.Ignore };
		_encounterOverlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.04f, 0.05f, 0.08f, 0.94f),
			BorderColor = new Color(0.98f, 0.76f, 0.37f, 0.85f),
			BorderWidthLeft = 2,
			CornerRadiusTopLeft = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		});
		_encounterRows = new VBoxContainer { Name = "Subroutines", MouseFilter = Control.MouseFilterEnum.Ignore };
		_encounterRows.AddThemeConstantOverride("separation", 2);
		_encounterOverlay.AddChild(_encounterRows);
		_tableViewport.AddChild(_encounterOverlay);
	}

	private void RefreshRunOverlays(MatchSnapshot snapshot)
	{
		if (_encounterOverlay is null || _encounterRows is null) return;
		ClearChildren(_encounterRows);
		_encounterIceId = null;
		if (snapshot.View.Run is not { EndReason: null, Encounter: { Status: RunEncounterStatus.Initialized, Ice: { IsKnown: true } ice } encounter })
		{
			_encounterOverlay.Visible = false;
			return;
		}
		_encounterIceId = ice.Id;
		string[] printed = ice.RulesText.Split("[subroutine]", StringSplitOptions.None).Skip(1).Select(text => text.Trim()).ToArray();
		AddRow($"{ice.Title} · strength {encounter.EffectiveStrength ?? ice.Strength}", 13, Colors.White);
		foreach (SubroutineViewModel subroutine in encounter.Subroutines.OrderBy(value => value.Ordinal))
		{
			string text = subroutine.Ordinal < printed.Length ? printed[subroutine.Ordinal] : $"Subroutine {subroutine.Ordinal + 1}";
			(string mark, Color color) = subroutine.Broken ? ("✗ broken", new Color(0.55f, 0.6f, 0.62f))
				: subroutine.Resolved ? ("✓ fired", new Color(0.7f, 0.72f, 0.75f))
				: ("● will fire unless broken", new Color(0.98f, 0.76f, 0.37f));
			AddRow($"{subroutine.Ordinal + 1}. {text} — {mark}", 12, color);
		}
		_encounterOverlay.Visible = true;

		void AddRow(string text, int size, Color color)
		{
			var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			label.CustomMinimumSize = new Vector2(260, 0);
			label.AddThemeFontSizeOverride("font_size", size);
			label.AddThemeColorOverride("font_color", color);
			_encounterRows.AddChild(label);
		}
	}

	/// <summary>Keeps the overlays beside the encountered ICE and at the board's centre, after layout.</summary>
	private void PlaceRunOverlays()
	{
		Rect2 bounds = _tableViewport.GetGlobalRect();
		if (_encounterOverlay is { Visible: true } overlay && _encounterIceId is { } ice
			&& _gameTable.VisibleCards.TryGetValue(ice, out TableCardControl? card) && card.IsVisibleInTree())
		{
			overlay.ResetSize();
			Rect2 anchor = card.GetGlobalRect();
			Vector2 position = new(anchor.End.X + 10, anchor.Position.Y);
			if (position.X + overlay.Size.X > bounds.End.X) position.X = anchor.Position.X - overlay.Size.X - 10;
			position.X = Math.Clamp(position.X, bounds.Position.X, Math.Max(bounds.Position.X, bounds.End.X - overlay.Size.X));
			position.Y = Math.Clamp(position.Y, bounds.Position.Y, Math.Max(bounds.Position.Y, bounds.End.Y - overlay.Size.Y));
			overlay.GlobalPosition = position;
		}
		if (_tableAccessPanel is { Visible: true } access)
		{
			access.ResetSize();
			access.GlobalPosition = new Vector2(bounds.Position.X + (bounds.Size.X - access.Size.X) / 2,
				bounds.Position.Y + bounds.Size.Y * 0.18f);
		}
	}

	/// <summary>The accessed card's title, with how many accesses have happened this run when Core says.</summary>
	private static string AccessTitle(CardViewModel? accessed, MatchViewModel view)
	{
		if (accessed is null) return "";
		if (view.Run?.Access is not { AccessOccurrenceCount: { } count } access) return accessed.Title;
		string limit = access.RandomAccessLimit > 0 ? $" of {access.RandomAccessLimit}" : "";
		return $"{accessed.Title} · access {Math.Max(1, count)}{limit}";
	}
}
