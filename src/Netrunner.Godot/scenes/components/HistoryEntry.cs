using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Binding for one authored history entry. Variable detail lines and known
/// cards are instantiated from their own reusable scene templates.
/// </summary>
[Tool]
public partial class HistoryEntry : VBoxContainer
{
	private Label _turnLabel = null!;
	private PanelContainer _row = null!;
	private Label _sideLabel = null!;
	private Button _summaryButton = null!;
	private PanelContainer _detailsPanel = null!;
	private VBoxContainer _detailRows = null!;
	private Label _cardsHeading = null!;
	private VBoxContainer _cardRows = null!;
	private string _entryKey = string.Empty;
	private Action<string>? _toggle;
	private Action<CardViewModel>? _inspect;
	private PackedScene? _detailScene;
	private bool _bound;

	public string EntryKey => _entryKey;

	public override void _EnterTree()
	{
		BindNodes();
		WireSignals();
	}

	public override void _Ready()
	{
		BindNodes();
		WireSignals();
	}

	public override void _ExitTree()
	{
		if (_bound && IsInstanceValid(_summaryButton))
		{
			_summaryButton.Pressed -= HandleSummaryPressed;
		}
		_bound = false;
	}

	private void WireSignals()
	{
		if (_bound || _summaryButton is null || !IsInstanceValid(_summaryButton))
		{
			return;
		}

		_summaryButton.Pressed += HandleSummaryPressed;
		_bound = true;
	}

	public void Bind(
		MatchHistoryEntry entry,
		bool showTurn,
		bool detailsExpanded,
		Action<string> toggle,
		Action<CardViewModel>? inspect,
		float rowSpacing,
		int headerFontSize,
		int bodyFontSize,
		int smallFontSize,
		Color accentColor,
		Color mutedColor,
		Color corpCueColor,
		Color runnerCueColor,
		StyleBox? surfaceStyle,
		StyleBox? raisedStyle,
		PackedScene? detailScene)
	{
		ArgumentNullException.ThrowIfNull(entry);
		ArgumentNullException.ThrowIfNull(toggle);
		BindNodes();
		_entryKey = entry.Key;
		_toggle = toggle;
		_inspect = inspect;
		_detailScene = detailScene ?? GD.Load<PackedScene>(MatchHistoryPanel.DetailScenePath);
		_turnLabel.Visible = showTurn;
		_turnLabel.Text = $"TURN {entry.TurnNumber}";
		_sideLabel.Text = FormatSide(entry.Side);
		_sideLabel.AddThemeColorOverride(
			"font_color",
			MatchHistoryPanel.SideColour(entry.Side, corpCueColor, runnerCueColor));
		_summaryButton.Text = (detailsExpanded ? "▾ " : "▸ ") + entry.Summary;
		_summaryButton.TooltipText = MatchHistoryPanel.FormatEntryTooltip(entry);
		_summaryButton.AddThemeFontSizeOverride("font_size", bodyFontSize);
		_summaryButton.AddThemeColorOverride("font_hover_color", accentColor);
		_sideLabel.AddThemeFontSizeOverride("font_size", smallFontSize);
		_turnLabel.AddThemeFontSizeOverride("font_size", Math.Max(smallFontSize, headerFontSize - 4));
		_turnLabel.AddThemeColorOverride("font_color", accentColor);
		_detailsPanel.Visible = detailsExpanded;
		ApplyStyle(_row, surfaceStyle);
		ApplyStyle(_detailsPanel, raisedStyle);
		_detailRows.AddThemeConstantOverride("separation", Mathf.RoundToInt(rowSpacing));
		_cardRows.AddThemeConstantOverride("separation", Mathf.RoundToInt(rowSpacing));
		RebuildDetails(entry, bodyFontSize, smallFontSize, mutedColor, accentColor);
	}

	public void ApplyAppearance(
		float rowSpacing,
		int headerFontSize,
		int bodyFontSize,
		int smallFontSize,
		Color accentColor,
		Color mutedColor,
		Color corpCueColor,
		Color runnerCueColor,
		StyleBox? surfaceStyle,
		StyleBox? raisedStyle)
	{
		if (!_bound || string.IsNullOrWhiteSpace(_entryKey))
		{
			return;
		}

		_sideLabel.AddThemeFontSizeOverride("font_size", smallFontSize);
		_sideLabel.AddThemeColorOverride(
			"font_color",
			MatchHistoryPanel.SideColour(
				_sideLabel.Text.StartsWith("CORP", StringComparison.Ordinal)
					? PlayerSide.Corp
					: PlayerSide.Runner,
				corpCueColor,
				runnerCueColor));
		_turnLabel.AddThemeFontSizeOverride("font_size", Math.Max(smallFontSize, headerFontSize - 4));
		_turnLabel.AddThemeColorOverride("font_color", accentColor);
		_summaryButton.AddThemeFontSizeOverride("font_size", bodyFontSize);
		_summaryButton.AddThemeColorOverride("font_hover_color", accentColor);
		ApplyStyle(_row, surfaceStyle);
		ApplyStyle(_detailsPanel, raisedStyle);
		_detailRows.AddThemeConstantOverride("separation", Mathf.RoundToInt(rowSpacing));
		_cardRows.AddThemeConstantOverride("separation", Mathf.RoundToInt(rowSpacing));
		_cardsHeading.AddThemeFontSizeOverride("font_size", smallFontSize);
		_cardsHeading.AddThemeColorOverride("font_color", mutedColor);
		foreach (Node child in _detailRows.GetChildren())
		{
			if (child is HistoryDetail detail)
			{
				detail.ApplyAppearance(bodyFontSize, accentColor, mutedColor);
			}
		}
		foreach (Node child in _cardRows.GetChildren())
		{
			if (child is HistoryDetail detail)
			{
				detail.ApplyAppearance(smallFontSize, accentColor, mutedColor);
			}
		}
	}

	public void GrabSummaryFocus()
	{
		if (_bound && IsInstanceValid(_summaryButton))
		{
			_summaryButton.GrabFocus();
		}
	}

	private void BindNodes()
	{
		if (_bound || GetNodeOrNull<Label>("TurnLabel") is null)
		{
			return;
		}

		_turnLabel = GetNode<Label>("TurnLabel");
		_row = GetNode<PanelContainer>("Row");
		_sideLabel = GetNode<Label>("Row/Content/Side");
		_summaryButton = GetNode<Button>("Row/Content/Summary");
		_detailsPanel = GetNode<PanelContainer>("DetailsPanel");
		_detailRows = GetNode<VBoxContainer>("DetailsPanel/Details/DetailRows");
		_cardsHeading = GetNode<Label>("DetailsPanel/Details/CardsHeading");
		_cardRows = GetNode<VBoxContainer>("DetailsPanel/Details/CardRows");
	}

	private void HandleSummaryPressed()
	{
		_toggle?.Invoke(_entryKey);
	}

	private void RebuildDetails(
		MatchHistoryEntry entry,
		int bodyFontSize,
		int smallFontSize,
		Color mutedColor,
		Color accentColor)
	{
		ClearContainer(_detailRows);
		ClearContainer(_cardRows);
		_cardsHeading.Visible = entry.Cards is { Count: > 0 };
		_cardsHeading.AddThemeFontSizeOverride("font_size", smallFontSize);
		_cardsHeading.AddThemeColorOverride("font_color", mutedColor);

		string[] details = (entry.Details ?? Array.Empty<string>())
			.Where(static detail => !string.IsNullOrWhiteSpace(detail))
			.Select(static detail => detail.Trim())
			.ToArray();
		if (details.Length == 0)
		{
			InstantiateDetail().BindText("No additional public details.", bodyFontSize, mutedColor);
		}
		else
		{
			foreach (string detail in details)
			{
				InstantiateDetail().BindText("• " + detail, bodyFontSize, Colors.White);
			}
		}

		if (entry.Cards is not { Count: > 0 })
		{
			return;
		}

		foreach (CardViewModel card in entry.Cards.Where(static card => card is not null))
		{
			InstantiateCardDetail().BindCard(card, smallFontSize, accentColor, _inspect);
		}
	}

	private HistoryDetail InstantiateDetail()
	{
		HistoryDetail detail = _detailScene?.Instantiate<HistoryDetail>()
			?? throw new InvalidOperationException($"History detail scene is missing: {MatchHistoryPanel.DetailScenePath}");
		_detailRows.AddChild(detail);
		return detail;
	}

	private HistoryDetail InstantiateCardDetail()
	{
		HistoryDetail detail = _detailScene?.Instantiate<HistoryDetail>()
			?? throw new InvalidOperationException($"History detail scene is missing: {MatchHistoryPanel.DetailScenePath}");
		_cardRows.AddChild(detail);
		return detail;
	}

	private static void ClearContainer(Container container)
	{
		foreach (Node child in container.GetChildren().ToArray())
		{
			container.RemoveChild(child);
			child.Free();
		}
	}

	private static void ApplyStyle(PanelContainer panel, StyleBox? style)
	{
		if (style is not null)
		{
			panel.AddThemeStyleboxOverride("panel", style);
		}
		else
		{
			panel.RemoveThemeStyleboxOverride("panel");
		}
	}

	private static string FormatSide(PlayerSide side)
		=> side == PlayerSide.Corp ? "CORP" : "RUNNER";
}
