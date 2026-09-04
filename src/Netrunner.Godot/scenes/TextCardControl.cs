using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Reusable text-only card control.  It deliberately uses labels, borders,
/// and counter badges in addition to colour so card state remains legible in
/// reduced-colour modes and in a headless/UI inspection run.
/// </summary>
public partial class TextCardControl : PanelContainer
{
	private static readonly Color NormalBorder = new(0.24f, 0.34f, 0.42f);
	private static readonly Color HoverBorder = new(0.35f, 0.92f, 0.72f);
	private static readonly Color SelectedBorder = new(0.98f, 0.74f, 0.28f);

	private Label _title = null!;
	private Label _type = null!;
	private Label _stats = null!;
	private Label _status = null!;
	private Label _abilityCue = null!;
	private Label _firstXCue = null!;
	private Label _counters = null!;
	private CardViewModel? _card;
	private StrengthBreakdownViewModel? _strengthBreakdown;
	private bool _selected;
	private bool _targetCandidate;
	private bool _hovered;

	[Signal]
	public delegate void CardClickedEventHandler(string cardId);

	[Signal]
	public delegate void CardInspectedEventHandler(string cardId);

	public string CardId => _card?.Id ?? string.Empty;

	/// <summary>
	/// Exposes the retained interaction state for presentation smoke checks and
	/// accessibility-focused UI diagnostics. The engine never consumes this
	/// state; it is local to the card control.
	/// </summary>
	public bool IsSelected => _selected;

	public bool IsHovered => _hovered;

	public bool IsTargetCandidate => _targetCandidate;

	/// <summary>Text-only ability affordance retained for smoke/accessibility checks.</summary>
	public string DisplayedAbilityCue => _abilityCue?.Text ?? string.Empty;

	/// <summary>Text-only current-turn first-X cue retained for smoke/accessibility checks.</summary>
	public string DisplayedFirstXUseCue => _firstXCue?.Text ?? string.Empty;

	/// <summary>Rendered stats, including a typed encounter strength explanation.</summary>
	public string DisplayedStats => _stats?.Text ?? string.Empty;

	/// <summary>Text-only typed counter chips retained for smoke/accessibility checks.</summary>
	public string DisplayedCounterChips => _counters?.Text ?? string.Empty;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		CustomMinimumSize = new Vector2(116, 92);

		var content = new VBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
		};
		AddChild(content);

		_title = MakeLabel(14, Colors.White);
		_title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_title);
		_type = MakeLabel(10, new Color(0.45f, 0.78f, 0.9f));
		content.AddChild(_type);
		_stats = MakeLabel(10, new Color(0.75f, 0.8f, 0.84f));
		content.AddChild(_stats);
		_status = MakeLabel(10, new Color(0.72f, 0.76f, 0.78f));
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_status);
		_abilityCue = MakeLabel(10, new Color(0.98f, 0.74f, 0.28f));
		_abilityCue.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_abilityCue);
		_firstXCue = MakeLabel(10, new Color(0.98f, 0.74f, 0.28f));
		_firstXCue.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_firstXCue);
		_counters = MakeLabel(10, new Color(0.98f, 0.74f, 0.28f));
		content.AddChild(_counters);

		MouseEntered += HandleMouseEntered;
		MouseExited += HandleMouseExited;
		// Card data may be assigned before the control enters the scene tree;
		// redraw the retained value now that the labels exist.
		SetCard(_card);
	}

	public void SetCard(CardViewModel? card)
	{
		_card = card;
		_selected = false;
		_targetCandidate = false;
		if (_title is null)
		{
			return;
		}

		if (card is null)
		{
			_title.Text = "UNKNOWN SLOT";
			_type.Text = "Hidden information";
			_stats.Text = "Identity concealed";
			_status.Text = "The card remains face-down.";
			_abilityCue.Text = string.Empty;
			_firstXCue.Text = string.Empty;
			_counters.Text = string.Empty;
		}
		else
		{
			_title.Text = card.Title;
			_type.Text = card.Type + (card.Subtypes.Count == 0 ? string.Empty : " · " + string.Join(" · ", card.Subtypes));
			_stats.Text = FormatStats(card, _strengthBreakdown);
			_status.Text = card.Rezzed ? "REZZED" : card.IsKnown ? "KNOWN" : "FACE-DOWN";
			_abilityCue.Text = card.AbilityCue ?? string.Empty;
			_firstXCue.Text = card.FirstXUseCue ?? string.Empty;
			_counters.Text = string.Join("  ", card.CounterChips);
		}

		UpdateStatusCue();
		UpdateBorder();
	}

	/// <summary>
	/// Shows the current encounter strength without mutating the card's printed
	/// strength. Passing null restores the ordinary printed-stat presentation.
	/// </summary>
	public void SetEncounterStrength(int? strength)
	{
		_strengthBreakdown = strength is { } value
			? new StrengthBreakdownViewModel(
				_card?.Strength ?? value,
				value,
				Array.Empty<ValueCalculationTermPresentationViewModel>())
			: null;
		if (_stats is not null && _card is not null)
		{
			_stats.Text = FormatStats(_card, _strengthBreakdown);
		}
	}

	/// <summary>
	/// Retains the authoritative Match-side strength breakdown for this card's
	/// current encounter; printed card stats remain unchanged.
	/// </summary>
	public void SetEncounterStrength(StrengthBreakdownViewModel? breakdown)
	{
		_strengthBreakdown = breakdown;
		if (_stats is not null && _card is not null)
		{
			_stats.Text = FormatStats(_card, _strengthBreakdown);
		}
	}

	public void SetSelected(bool selected)
	{
		_selected = selected;
		UpdateStatusCue();
		UpdateBorder();
	}

	/// <summary>
	/// Marks a visible card as a legal target. The explicit text cue and thicker
	/// border are intentional: target affordance remains understandable without
	/// relying on colour alone.
	/// </summary>
	public void SetTargetCandidate(bool candidate)
	{
		_targetCandidate = candidate;
		if (!candidate)
		{
			_selected = false;
		}

		UpdateStatusCue();
		UpdateBorder();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouse
			&& mouse.Pressed
			&& mouse.ButtonIndex == MouseButton.Left
			&& _card is not null)
		{
			_selected = !_selected;
			UpdateBorder();
			EmitSignal(SignalName.CardClicked, _card.Id);
			EmitSignal(SignalName.CardInspected, _card.Id);
			AcceptEvent();
		}
	}

	private void HandleMouseEntered()
	{
		_hovered = true;
		UpdateBorder();
	}

	private void HandleMouseExited()
	{
		_hovered = false;
		UpdateBorder();
	}

	private void UpdateBorder()
	{
		if (_card is null || !IsInsideTree())
		{
			return;
		}

		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.07f, 0.1f, 0.14f),
			BorderWidthLeft = _selected || _targetCandidate ? 2 : 1,
			BorderWidthTop = _selected || _targetCandidate ? 2 : 1,
			BorderWidthRight = _selected || _targetCandidate ? 2 : 1,
			BorderWidthBottom = _selected || _targetCandidate ? 2 : 1,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
			BorderColor = _selected
				? SelectedBorder
				: _targetCandidate
					? new Color(0.35f, 0.72f, 0.98f)
					: _hovered ? HoverBorder : NormalBorder,
			ContentMarginLeft = 8,
			ContentMarginTop = 7,
			ContentMarginRight = 8,
			ContentMarginBottom = 6,
		};
		AddThemeStyleboxOverride("panel", style);
	}

	private void UpdateStatusCue()
	{
		if (_status is null || _card is null)
		{
			return;
		}

		string visibility = _card.Rezzed ? "REZZED" : _card.IsKnown ? "KNOWN" : "FACE-DOWN";
		_status.Text = _selected
			? $"TARGET SELECTED · {visibility}"
			: _targetCandidate
				? $"TARGET CANDIDATE · {visibility}"
				: visibility;
	}

	private static Label MakeLabel(int size, Color colour)
	{
		var label = new Label
		{
			MouseFilter = MouseFilterEnum.Ignore,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", colour);
		return label;
	}

	private static string FormatStats(CardViewModel card, StrengthBreakdownViewModel? strengthBreakdown)
	{
		var values = new List<string>();
		if (card.Cost is { } cost)
		{
			values.Add($"Cost {cost}");
		}

		if (card.Strength is { } strength)
		{
			values.Add(strengthBreakdown is { } breakdown
				? breakdown.Summary
				: $"STR {strength}");
		}

		if (card.AgendaPoints is { } points)
		{
			values.Add($"{points} AP");
		}

		if (card.TrashCost is { } trash)
		{
			values.Add($"Trash {trash}");
		}

		return values.Count == 0 ? card.RulesText : string.Join(" · ", values);
	}
}
