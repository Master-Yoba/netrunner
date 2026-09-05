using System.Text;
using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// A bounded, table-friendly card face. The control owns presentation state
/// only: a table can replace <see cref="Card"/> without giving the control any
/// access to authoritative game state.
/// </summary>
public partial class TableCardControl : Control
{
	public static readonly Vector2 PortraitCardSize = new(100, 140);
	public static readonly Vector2 IceCardSize = new(160, 72);

	private static readonly Color Frame = new(0.39f, 0.47f, 0.55f);
	private static readonly Color FocusFrame = new(0.46f, 0.95f, 0.82f);
	private static readonly Color SelectedFrame = new(1.0f, 0.76f, 0.28f);
	private static readonly Color TargetFrame = new(0.39f, 0.78f, 1.0f);
	private static readonly Color HiddenFrame = new(0.31f, 0.39f, 0.48f);
	private static readonly Color HiddenSurface = new(0.075f, 0.10f, 0.15f);

	private CardViewModel? _card;
	private Texture2D? _artwork;
	private Label _title = null!;
	private Label _type = null!;
	private Label _stats = null!;
	private Label _rulesCue = null!;
	private Label _status = null!;
	private Label _counters = null!;
	private Vector2 _displaySize = PortraitCardSize;
	private bool _isIce;
	private bool _selected;
	private bool _targetCandidate;
	private bool _hovered;

	/// <summary>Raised after a visible card is activated by mouse or keyboard.</summary>
	public event Action<string>? CardSelected;

	/// <summary>Raised when a card is hovered, focused, or activated for preview.</summary>
	public event Action<string>? CardInspected;

	public CardViewModel? Card => _card;

	public string CardId => _card?.Id ?? string.Empty;

	public bool IsIce => _isIce;

	public bool IsSelected => _selected;

	public bool IsTargetCandidate => _targetCandidate;

	public bool IsHovered => _hovered;

	/// <summary>
	/// The rendered footprint. The table may choose a compact footprint while
	/// keeping the card's drawing and text layout legible at that size.
	/// </summary>
	public Vector2 DisplaySize => _displaySize;

	private bool IsCompactPortrait => !_isIce
		&& (_displaySize.X < PortraitCardSize.X - 0.5f || _displaySize.Y < PortraitCardSize.Y - 0.5f);

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		FocusMode = FocusModeEnum.All;
		ClipContents = true;
		TooltipText = "Inspect card details; press Enter or Space to select.";

		BuildLabels();
		MouseEntered += HandleMouseEntered;
		MouseExited += HandleMouseExited;
		FocusEntered += HandleFocusEntered;
		FocusExited += HandleFocusExited;

		ApplySize();
		RefreshPresentation();
	}

	/// <summary>
	/// Replaces the viewer-safe card projection. An unknown projection always
	/// receives the neutral back and never attempts an artwork or metadata lookup.
	/// </summary>
	public void SetCard(CardViewModel? card)
	{
		_card = card;
		_selected = false;
		_targetCandidate = false;
		RefreshArtwork();
		RefreshPresentation();
	}

	/// <summary>Marks this card as the currently selected local target.</summary>
	public void SetSelected(bool selected)
	{
		_selected = selected;
		RefreshDynamicPresentation();
		QueueRedraw();
	}

	/// <summary>Marks this card as a legal target in the current decision.</summary>
	public void SetTargetCandidate(bool candidate)
	{
		_targetCandidate = candidate;
		if (!candidate)
		{
			_selected = false;
		}

		RefreshDynamicPresentation();
		QueueRedraw();
	}

	/// <summary>
	/// Changes the physical orientation/shape used for an ICE lane. The card
	/// remains fixed at 160×72 in this mode and 100×140 otherwise.
	/// </summary>
	public void SetIce(bool ice)
	{
		if (_isIce == ice)
		{
			return;
		}

		_isIce = ice;
		_displaySize = ice ? IceCardSize : PortraitCardSize;
		ApplySize();
		RefreshPresentation();
		QueueRedraw();
	}

	/// <summary>
	/// Sets the actual rendered footprint used by this card. This is intended
	/// for table lanes that need a compact portrait such as 70×98; callers
	/// should keep <see cref="Scale"/> at one so text remains pixel-legible.
	/// </summary>
	public void SetDisplaySize(Vector2 size)
	{
		if (size.X <= 0 || size.Y <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size), "A card display size must be positive.");
		}

		_displaySize = size;
		ApplySize();
		RefreshPresentation();
		QueueRedraw();
	}

	public override void _GuiInput(InputEvent @event)
	{
		bool activate = @event is InputEventMouseButton mouse
			&& mouse.Pressed
			&& mouse.ButtonIndex == MouseButton.Left;
		if (@event is InputEventKey key
			&& key.Pressed
			&& !key.Echo
			&& key.Keycode is Key.Enter or Key.KpEnter or Key.Space)
		{
			activate = true;
		}

		if (!activate || _card is null || string.IsNullOrWhiteSpace(_card.Id))
		{
			return;
		}

		_selected = !_selected;
		RefreshDynamicPresentation();
		QueueRedraw();
		GrabFocus();
		CardSelected?.Invoke(_card.Id);
		AcceptEvent();
	}

	public override void _Draw()
	{
		Vector2 cardSize = _displaySize;
		Rect2 cardRect = new(Vector2.Zero, cardSize);
		Color accent = ResolveAccent();
		Color border = ResolveBorder();
		Color surface = _card is { IsKnown: true }
			? new Color(0.055f, 0.075f, 0.11f)
			: HiddenSurface;

		var frame = new StyleBoxFlat
		{
			BgColor = surface,
			BorderColor = border,
			BorderWidthLeft = _selected || _targetCandidate ? 2 : 1,
			BorderWidthTop = _selected || _targetCandidate ? 2 : 1,
			BorderWidthRight = _selected || _targetCandidate ? 2 : 1,
			BorderWidthBottom = _selected || _targetCandidate ? 2 : 1,
			CornerRadiusTopLeft = 7,
			CornerRadiusTopRight = 7,
			CornerRadiusBottomLeft = 7,
			CornerRadiusBottomRight = 7,
			ShadowColor = new Color(0, 0, 0, 0.33f),
			ShadowSize = 4,
		};
		DrawStyleBox(frame, new Rect2(Vector2.One, cardSize - new Vector2(2, 2)));

		if (_card is not { IsKnown: true })
		{
			DrawNeutralBack(cardRect, border);
			return;
		}

		float headerHeight = _isIce
			? Mathf.Min(22, cardSize.Y * 0.34f)
			: IsCompactPortrait ? 43 : Mathf.Min(29, cardSize.Y * 0.30f);
		DrawRect(new Rect2(2, 2, cardSize.X - 4, headerHeight), accent.Darkened(0.42f));
		DrawRect(
			new Rect2(2, 2 + headerHeight - 2, cardSize.X - 4, 2),
			accent.Lightened(0.12f));

		Rect2 motifRect = _isIce
			? new Rect2(
				cardSize.X * 0.56f,
				cardSize.Y * 0.11f,
				cardSize.X * 0.40f,
				cardSize.Y * 0.63f)
			: IsCompactPortrait
				? new Rect2(
					6,
					cardSize.Y * 0.47f,
					Mathf.Max(1, cardSize.X - 12),
					Mathf.Max(1, cardSize.Y * 0.13f))
				: new Rect2(8, 39, Mathf.Max(1, cardSize.X - 16), 55);
		if (_artwork is not null)
		{
			DrawTextureRect(_artwork, motifRect, false, new Color(1, 1, 1, 0.87f));
		}

		DrawMotif(motifRect, accent, _artwork is not null ? 0.26f : 0.72f);
		DrawStateBadge(accent, cardSize);
	}

	private void BuildLabels()
	{
		_title = MakeLabel(10, Colors.White);
		_type = MakeLabel(8, new Color(0.80f, 0.88f, 0.92f));
		_stats = MakeLabel(8, new Color(0.91f, 0.94f, 0.98f));
		_rulesCue = MakeLabel(7, new Color(0.66f, 0.73f, 0.79f));
		_status = MakeLabel(7, new Color(0.74f, 0.81f, 0.86f));
		_counters = MakeLabel(7, new Color(1.0f, 0.83f, 0.39f));

		AddChild(_title);
		AddChild(_type);
		AddChild(_stats);
		AddChild(_rulesCue);
		AddChild(_status);
		AddChild(_counters);
	}

	private void ApplySize()
	{
		Vector2 desired = _displaySize;
		CustomMinimumSize = desired;
		Size = desired;
		SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		SizeFlagsVertical = SizeFlags.ShrinkCenter;
	}

	private void RefreshPresentation()
	{
		if (_title is null)
		{
			QueueRedraw();
			return;
		}

		if (_isIce)
		{
			float leftWidth = Mathf.Max(1, _displaySize.X * 0.52f);
			float rightX = _displaySize.X * 0.56f;
			float rightWidth = Mathf.Max(1, _displaySize.X - rightX - 5);
			SetLabelRect(_title, new Rect2(5, 4, leftWidth, 14), 10, Colors.White);
			SetLabelRect(_type, new Rect2(5, 18, leftWidth, 12), 7, new Color(0.80f, 0.88f, 0.92f));
			SetLabelRect(_stats, new Rect2(5, _displaySize.Y - 20, leftWidth, 14), 8, new Color(0.91f, 0.94f, 0.98f));
			SetLabelRect(_rulesCue, new Rect2(5, 34, leftWidth, 15), 7, new Color(0.66f, 0.73f, 0.79f));
			SetLabelRect(_status, new Rect2(rightX, _displaySize.Y - 18, rightWidth, 13), 7, new Color(0.74f, 0.81f, 0.86f));
			SetLabelRect(_counters, new Rect2(rightX, 3, rightWidth, 16), 7, new Color(1.0f, 0.83f, 0.39f));
		}
		else if (IsCompactPortrait)
		{
			float width = Mathf.Max(1, _displaySize.X - 8);
			float statusY = Mathf.Max(0, _displaySize.Y - 14);
			float statsY = Mathf.Max(0, _displaySize.Y - 34);
			SetLabelRect(_title, new Rect2(4, 2, width, 31), 10, Colors.White);
			SetLabelRect(_type, new Rect2(4, 33, width, 10), 8, new Color(0.80f, 0.88f, 0.92f));
			SetLabelRect(_stats, new Rect2(4, statsY, width, 11), 9, new Color(0.91f, 0.94f, 0.98f));
			SetLabelRect(_rulesCue, new Rect2(4, 0, width, 1), 7, new Color(0.66f, 0.73f, 0.79f));
			SetLabelRect(_status, new Rect2(4, statusY, width, 10), 7, new Color(0.74f, 0.81f, 0.86f));
			SetLabelRect(_counters, new Rect2(4, Mathf.Max(0, _displaySize.Y - 24), width, 10), 7, new Color(1.0f, 0.83f, 0.39f));
		}
		else
		{
			SetLabelRect(_title, new Rect2(5, 4, 90, 20), 10, Colors.White);
			SetLabelRect(_type, new Rect2(5, 23, 90, 14), 7, new Color(0.80f, 0.88f, 0.92f));
			SetLabelRect(_stats, new Rect2(5, 96, 90, 14), 8, new Color(0.91f, 0.94f, 0.98f));
			SetLabelRect(_rulesCue, new Rect2(5, 110, 90, 14), 7, new Color(0.66f, 0.73f, 0.79f));
			SetLabelRect(_status, new Rect2(5, 124, 90, 12), 7, new Color(0.74f, 0.81f, 0.86f));
			SetLabelRect(_counters, new Rect2(5, 82, 90, 15), 7, new Color(1.0f, 0.83f, 0.39f));
		}

		RefreshDynamicPresentation();
		QueueRedraw();
	}

	private void RefreshDynamicPresentation()
	{
		if (_title is null)
		{
			return;
		}

		bool known = _card?.IsKnown == true;
		bool compact = IsCompactPortrait;
		_title.Text = known ? compact ? CompactTitle(_card!.Title) : _card!.Title : "FACE-DOWN";
		_title.AutowrapMode = TextServer.AutowrapMode.Off;
		_title.VerticalAlignment = VerticalAlignment.Top;
		_title.MaxLinesVisible = compact ? 2 : 1;
		_title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_type.Text = known ? FormatType(_card!, compact || _isIce) : "UNKNOWN";
		_stats.Text = known ? FormatStats(_card!, compact) : "HIDDEN";
		_rulesCue.Text = known
			? BuildRulesCue(_card!.RulesText, _isIce ? 38 : compact ? 20 : 30)
			: "NO PUBLIC DETAILS";
		_status.Text = known ? FormatStatus(_card!, compact) : "FACE-DOWN";
		_counters.Text = known ? FormatCounters(_card!, compact) : string.Empty;
		_counters.Visible = _counters.Text.Length > 0;
		_rulesCue.Visible = known && !_isIce && !compact;

		var statusStyle = new StyleBoxFlat
		{
			BgColor = known ? new Color(0.08f, 0.12f, 0.17f, 0.88f) : new Color(0.07f, 0.09f, 0.13f, 0.92f),
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3,
			ContentMarginLeft = 3,
			ContentMarginRight = 3,
			ContentMarginTop = 1,
			ContentMarginBottom = 1,
		};
		_status.AddThemeStyleboxOverride("normal", statusStyle);

		var counterStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.20f, 0.14f, 0.08f, 0.94f),
			BorderColor = new Color(0.80f, 0.57f, 0.20f, 0.75f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 3,
			ContentMarginRight = 3,
			ContentMarginTop = 1,
			ContentMarginBottom = 1,
		};
		_counters.AddThemeStyleboxOverride("normal", counterStyle);
		TooltipText = known
			? BuildTooltip(_card!)
			: "Face-down card; identity is concealed.";
	}

	private static string CompactTitle(string title)
	{
		string name = title.Split(':')[0];
		if (name.Length <= 11) return name;
		int split = name.LastIndexOf(' ', Math.Min(11, name.Length - 1));
		return split > 0 ? name[..split] + "\n" + name[(split + 1)..] : name;
	}

	private void RefreshArtwork()
	{
		_artwork = null;
		if (_card is not { IsKnown: true, DefinitionId: { } definitionId }
			|| string.IsNullOrWhiteSpace(definitionId))
		{
			return;
		}

		_artwork = LocalCardArtworkResolver.TryLoad(definitionId);
	}

	private static void SetLabelRect(Label label, Rect2 rect, int fontSize, Color colour)
	{
		label.Position = rect.Position;
		label.Size = rect.Size;
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", colour);
	}

	private static Label MakeLabel(int fontSize, Color colour)
	{
		var label = new Label
		{
			MouseFilter = MouseFilterEnum.Ignore,
			ClipText = true,
			AutowrapMode = TextServer.AutowrapMode.Off,
			VerticalAlignment = VerticalAlignment.Center,
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", colour);
		return label;
	}

	private void HandleMouseEntered()
	{
		_hovered = true;
		ZIndex = 10;
		QueueRedraw();
		EmitInspection();
	}

	private void HandleMouseExited()
	{
		_hovered = false;
		ZIndex = HasFocus() ? 10 : 0;
		QueueRedraw();
	}

	private void HandleFocusEntered()
	{
		ZIndex = 10;
		QueueRedraw();
		EmitInspection();
	}

	private void HandleFocusExited()
	{
		ZIndex = _hovered ? 10 : 0;
		QueueRedraw();
	}

	private void EmitInspection()
	{
		CardInspected?.Invoke(_card is { IsKnown: true } ? _card.Id : string.Empty);
	}

	private Color ResolveAccent()
	{
		if (_card is not { IsKnown: true })
		{
			return HiddenFrame;
		}

		string faction = _card.Faction.ToLowerInvariant();
		if (faction.Contains("anarch", StringComparison.Ordinal))
		{
			return new Color(0.82f, 0.30f, 0.42f);
		}

		if (faction.Contains("criminal", StringComparison.Ordinal))
		{
			return new Color(0.30f, 0.55f, 0.92f);
		}

		if (faction.Contains("shaper", StringComparison.Ordinal))
		{
			return new Color(0.35f, 0.78f, 0.63f);
		}

		if (faction.Contains("haas", StringComparison.Ordinal)
			|| faction.Contains("bioroid", StringComparison.Ordinal))
		{
			return new Color(0.74f, 0.42f, 0.84f);
		}

		if (faction.Contains("jinteki", StringComparison.Ordinal))
		{
			return new Color(0.93f, 0.38f, 0.42f);
		}

		if (faction.Contains("nbn", StringComparison.Ordinal))
		{
			return new Color(0.95f, 0.66f, 0.25f);
		}

		if (faction.Contains("weyland", StringComparison.Ordinal))
		{
			return new Color(0.38f, 0.68f, 0.47f);
		}

		if (_card.Type.Contains("ice", StringComparison.OrdinalIgnoreCase))
		{
			return new Color(0.31f, 0.67f, 0.80f);
		}

		return new Color(0.61f, 0.68f, 0.75f);
	}

	private Color ResolveBorder()
	{
		if (_selected)
		{
			return SelectedFrame;
		}

		if (_targetCandidate)
		{
			return TargetFrame;
		}

		if (_hovered || HasFocus())
		{
			return FocusFrame;
		}

		return _card is { IsKnown: true } ? Frame : HiddenFrame;
	}

	private void DrawNeutralBack(Rect2 cardRect, Color border)
	{
		Rect2 inset = new(cardRect.Position + new Vector2(8, 8), cardRect.Size - new Vector2(16, 16));
		DrawRect(inset, new Color(border.R, border.G, border.B, 0.22f), false, 1.0f);
		Vector2 center = cardRect.GetCenter();
		float radius = Mathf.Min(
			_isIce
				? Mathf.Min(19, cardRect.Size.Y * 0.22f)
				: IsCompactPortrait ? cardRect.Size.X * 0.26f : 28,
			Mathf.Min(cardRect.Size.X, cardRect.Size.Y) * 0.30f);
		DrawCircle(center, radius, new Color(0.11f, 0.17f, 0.25f, 0.75f));
		DrawArc(center, radius, 0, Mathf.Tau, 32, new Color(border.R, border.G, border.B, 0.72f), 1.3f);
		DrawLine(center + new Vector2(-radius * 0.55f, 0), center + new Vector2(radius * 0.55f, 0), border, 2);
		DrawLine(center + new Vector2(0, -radius * 0.55f), center + new Vector2(0, radius * 0.55f), border, 2);
	}

	private void DrawMotif(Rect2 motifRect, Color accent, float alpha)
	{
		Color line = new(accent.R, accent.G, accent.B, alpha);
		Vector2 center = motifRect.GetCenter();
		float radius = Mathf.Min(motifRect.Size.X, motifRect.Size.Y) * 0.31f;
		DrawCircle(center, radius, new Color(accent.R, accent.G, accent.B, alpha * 0.16f));
		DrawArc(center, radius, 0, Mathf.Tau, 28, line, 1.3f);
		DrawArc(center, radius * 0.63f, Mathf.Pi * 0.18f, Mathf.Pi * 1.35f, 18, line, 1.0f);

		if (_isIce)
		{
			for (int index = 0; index < 4; index++)
			{
				float y = motifRect.Position.Y + 8 + index * 9;
				DrawLine(
					new Vector2(motifRect.Position.X + 8, y),
					new Vector2(motifRect.End.X - 8, y),
					line,
					1.0f);
			}
		}
		else
		{
			for (int index = 0; index < 6; index++)
			{
				float angle = index * Mathf.Tau / 6;
				Vector2 ray = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * 1.7f;
				DrawLine(center, ray, line, 1.0f);
			}
		}
	}

	private void DrawStateBadge(Color accent, Vector2 cardSize)
	{
		if (_card is null || !_card.IsKnown)
		{
			return;
		}

		if (_targetCandidate)
		{
			Rect2 target = _isIce
				? new Rect2(cardSize.X - 48, cardSize.Y - 19, 43, 14)
				: new Rect2(cardSize.X - 58, cardSize.Y - 18, 53, 13);
			DrawRect(target, new Color(TargetFrame.R, TargetFrame.G, TargetFrame.B, 0.18f));
			DrawRect(target, TargetFrame, false, 1.0f);
		}

		if (_selected)
		{
			Rect2 selected = new(new Vector2(3, 3), cardSize - new Vector2(6, 6));
			DrawRect(selected, new Color(SelectedFrame.R, SelectedFrame.G, SelectedFrame.B, 0.20f), false, 1.0f);
		}

		if (_card.Rezzed || _card.IsAbilityUsed || _card.IsFirstXUsedThisTurn)
		{
			Rect2 status = _isIce
				? new Rect2(cardSize.X - 69, 21, 64, 13)
				: IsCompactPortrait
					? new Rect2(4, Mathf.Max(0, cardSize.Y - 15), Mathf.Max(1, cardSize.X - 8), 11)
					: new Rect2(5, cardSize.Y - 14, Mathf.Max(1, cardSize.X - 10), 10);
			DrawRect(status, new Color(accent.R, accent.G, accent.B, 0.16f));
		}
	}

	private static string FormatType(CardViewModel card, bool compact = false)
	{
		string type = card.Type.Trim().ToUpperInvariant();
		if (compact)
		{
			return type;
		}

		string faction = card.Faction.Trim().ToUpperInvariant();
		return faction.Length == 0 ? type : $"{faction} · {type}";
	}

	private static string FormatStats(CardViewModel card, bool compact = false)
	{
		var values = new List<string>();
		if (compact)
		{
			if (card.Cost is { } compactCost)
			{
				values.Add($"C{compactCost}");
			}

			if (card.Strength is { } compactStrength)
			{
				values.Add($"S{compactStrength}");
			}
			else if (card.AgendaPoints is { } compactPoints)
			{
				values.Add($"AP{compactPoints}");
			}
			else if (card.MemoryCost is { } compactMemory)
			{
				values.Add($"MU{compactMemory}");
			}
			else if (card.TrashCost is { } compactTrash)
			{
				values.Add($"T{compactTrash}");
			}

			return values.Count == 0 ? "PUBLIC" : string.Join(" · ", values);
		}

		if (card.Cost is { } cost)
		{
			values.Add($"COST {cost}");
		}

		if (card.Strength is { } strength)
		{
			values.Add($"STR {strength}");
		}

		if (card.AgendaPoints is { } points)
		{
			values.Add($"AP {points}");
		}

		if (card.MemoryCost is { } memory)
		{
			values.Add($"MU {memory}");
		}

		if (card.TrashCost is { } trash)
		{
			values.Add($"TRASH {trash}");
		}

		return values.Count == 0 ? "PUBLIC CARD" : string.Join("  ", values);
	}

	private static string FormatStatus(CardViewModel card, bool compact = false)
	{
		if (compact && (card.IsAbilityUsed || card.IsFirstXUsedThisTurn))
		{
			return "USED";
		}

		var values = new List<string> { card.StatusLabel };
		if (card.IsAbilityUsed || card.IsFirstXUsedThisTurn)
		{
			values.Add("USED");
		}

		return string.Join(" · ", values);
	}

	private static string FormatCounters(CardViewModel card, bool compact = false)
	{
		var values = new List<string>();
		if (card.AdvancementCounters > 0)
		{
			values.Add(compact ? $"ADV×{card.AdvancementCounters}" : $"ADV ×{card.AdvancementCounters}");
		}

		IEnumerable<string> chips = compact ? card.CounterChips.Take(1) : card.CounterChips;
		foreach (string chip in chips)
		{
			values.Add(compact ? CompactCounterChip(chip) : chip);
		}

		return values.Count == 0 ? string.Empty : string.Join("  ", values);
	}

	private static string CompactCounterChip(string chip) => chip
		.Replace("HOSTED CREDIT", "HOST", StringComparison.Ordinal)
		.Replace("VIRUS", "VIR", StringComparison.Ordinal)
		.Replace("GENERIC", "GEN", StringComparison.Ordinal)
		.Replace("UNKNOWN", "?", StringComparison.Ordinal)
		.Replace(" ×", "×", StringComparison.Ordinal);

	private static string BuildTooltip(CardViewModel card)
	{
		var lines = new List<string>
		{
			card.Title,
			FormatType(card),
			FormatStats(card),
			$"Status: {FormatStatus(card)}",
		};
		string counters = FormatCounters(card);
		if (counters.Length > 0)
		{
			lines.Add($"Counters: {counters}");
		}

		string rules = StripMarkup(card.RulesText);
		if (rules.Length > 0)
		{
			lines.Add($"Rules: {rules}");
		}

		lines.Add("Press Enter or Space to select.");
		return string.Join("\n", lines);
	}

	private static string BuildRulesCue(string rulesText, int limit)
	{
		string clean = StripMarkup(rulesText);
		if (clean.Length == 0)
		{
			return "PUBLIC CARD";
		}

		int sentenceEnd = clean.IndexOfAny(['.', ';', '\n']);
		if (sentenceEnd is > 0 and < 48)
		{
			clean = clean[..sentenceEnd];
		}

		return clean.Length <= limit ? clean : clean[..Math.Max(0, limit - 1)] + "…";
	}

	private static string StripMarkup(string text)
	{
		var builder = new StringBuilder(text.Length);
		bool inTag = false;
		foreach (char character in text)
		{
			if (character == '<')
			{
				inTag = true;
				continue;
			}

			if (character == '>')
			{
				inTag = false;
				continue;
			}

			if (!inTag)
			{
				builder.Append(character is '\r' or '\n' ? ' ' : character);
			}
		}

		return builder.ToString().Trim();
	}

	private static class LocalCardArtworkResolver
	{
		public static ImageTexture? TryLoad(string definitionId)
		{
			string code = ToSafeCode(definitionId);
			if (code.Length == 0)
			{
				return null;
			}

			string path = $"user://card-art/{code}.png";
			if (!Godot.FileAccess.FileExists(path))
			{
				return null;
			}

			Image image = Image.LoadFromFile(path);
			return image.GetWidth() > 0 && image.GetHeight() > 0
				? ImageTexture.CreateFromImage(image)
				: null;
		}

		private static string ToSafeCode(string definitionId)
		{
			var builder = new StringBuilder(definitionId.Length);
			foreach (char character in definitionId.Trim())
			{
				builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
					? character
					: '_');
			}

			return builder.ToString();
		}
	}
}
