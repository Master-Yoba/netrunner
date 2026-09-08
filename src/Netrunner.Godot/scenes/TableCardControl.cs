using System.Text;
using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// A bounded, table-friendly card face. The control owns presentation state
/// only: a table can replace <see cref="Card"/> without giving the control any
/// access to authoritative game state.
/// </summary>
[Tool]
public partial class TableCardControl : Control
{
	public const string CardViewScenePath = "res://scenes/components/CardView.tscn";
	public const string DefaultAppearancePath = "res://themes/CardAppearance.tres";

	public static readonly Vector2 PortraitCardSize = new(150, 210);
	public static readonly Vector2 IceCardSize = new(240, 108);

	private CardViewModel? _card;
	private Texture2D? _artwork;
	private PlayerSide _backSide = PlayerSide.Corp;
	private Texture2D? _backTexture;
	private static readonly Dictionary<PlayerSide, Texture2D?> BackTextures = new();
	private CardAppearance? _appearance;
	private bool _appearanceConnected;
	public PlayerSide BackSide => _backSide;
	public bool HasCardBackTexture => _backTexture is not null;

	/// <summary>
	/// Shared card visual values. The resource is shared intentionally; state
	/// such as selection and actionability remains local to this control.
	/// </summary>
	[ExportGroup("Card appearance")]
	[Export]
	public CardAppearance Appearance
	{
		get => _appearance ??= LoadDefaultAppearance();
		set => ReplaceAppearance(value);
	}

	private CardAppearance CurrentAppearance => Appearance;

	/// <summary>
	/// Creates the authored card scene. Runtime callers should use this factory
	/// so the fixed label tree comes from CardView.tscn rather than from a C#
	/// layout builder.
	/// </summary>
	public static TableCardControl Create()
	{
		PackedScene? scene = GD.Load<PackedScene>(CardViewScenePath);
		if (scene is null)
		{
			string message = $"Production card scene is missing: {CardViewScenePath}";
			GD.PushError(message);
			throw new InvalidOperationException(message);
		}

		return scene.Instantiate<TableCardControl>();
	}

	private void ReplaceAppearance(CardAppearance? value)
	{
		value ??= LoadDefaultAppearance();
		if (ReferenceEquals(_appearance, value))
		{
			return;
		}

		DisconnectAppearance();
		_appearance = value;
		if (IsInsideTree())
		{
			ConnectAppearance();
		}
		RefreshAppearance();
	}

	private void ConnectAppearance()
	{
		if (_appearanceConnected || _appearance is null || !GodotObject.IsInstanceValid(_appearance))
		{
			return;
		}

		_appearance.Changed += HandleAppearanceChanged;
		_appearanceConnected = true;
	}

	private void DisconnectAppearance()
	{
		if (!_appearanceConnected || _appearance is null || !GodotObject.IsInstanceValid(_appearance))
		{
			_appearanceConnected = false;
			return;
		}

		_appearance.Changed -= HandleAppearanceChanged;
		_appearanceConnected = false;
	}

	private void HandleAppearanceChanged()
	{
		if (!GodotObject.IsInstanceValid(this))
		{
			return;
		}

		RefreshAppearance();
	}

	private static CardAppearance LoadDefaultAppearance()
	{
		CardAppearance? appearance = GD.Load<CardAppearance>(DefaultAppearancePath);
		if (appearance is not null)
		{
			return appearance;
		}

		GD.PushError($"Shared card appearance resource is missing: {DefaultAppearancePath}");
		return new CardAppearance();
	}

	private void RefreshAppearance()
	{
		if (!_hasExplicitDisplaySize)
		{
			_displaySize = _isIce ? CurrentAppearance.IceSize : CurrentAppearance.PortraitSize;
		}

		ApplySize();
		if (_title is not null)
		{
			RefreshPresentation();
		}
		else
		{
			QueueRedraw();
		}
	}

	public void SetBackSide(PlayerSide side)
	{
		_backSide = side;
		if (!BackTextures.TryGetValue(side, out _backTexture))
		{
			string filename = side == PlayerSide.Corp ? "corp.png" : "runner.png";
			string? directory = System.Environment.GetEnvironmentVariable("NETRUNNER_CARD_BACK_DIRECTORY");
			string[] candidates = string.IsNullOrWhiteSpace(directory)
				? new[] { $"user://card-backs/{filename}", $"res://card-backs/{filename}" }
				: new[] { System.IO.Path.Combine(directory, filename), $"user://card-backs/{filename}", $"res://card-backs/{filename}" };
			foreach (string path in candidates)
			{
				if (path.StartsWith("res://", StringComparison.Ordinal))
				{
					_backTexture = GD.Load<Texture2D>(path);
					if (_backTexture is not null)
					{
						break;
					}

					continue;
				}

				if (Godot.FileAccess.FileExists(path))
				{
					using Image image = Image.LoadFromFile(path);
					if (!image.IsEmpty())
					{
						_backTexture = ImageTexture.CreateFromImage(image);
						break;
					}
				}
			}
			BackTextures[side] = _backTexture;
		}
		RefreshDynamicPresentation();
		QueueRedraw();
	}
	private Label? _title;
	private Label? _type;
	private Label? _stats;
	private Label? _rulesCue;
	private Label? _status;
	private Label? _counters;
	private Vector2 _displaySize = PortraitCardSize;
	private bool _hasExplicitDisplaySize;
	private bool _isIce;
	private bool _selected;
	private bool _targetCandidate;
	private bool _actionableSource;
	private bool _hovered;
	private bool _interactionActive;

	/// <summary>Raised after a visible card is activated by mouse or keyboard.</summary>
	public event Action<string>? CardSelected;

	/// <summary>Raised when a card is hovered, focused, or activated for preview.</summary>
	public event Action<string>? CardInspected;

	/// <summary>
	/// Raised when this control becomes or ceases to be the active hover/focus
	/// source. The table forwards this without changing the card's layout.
	/// </summary>
	public event Action<TableCardControl, bool>? CardHoverChanged;

	public CardViewModel? Card => _card;

	public string CardId => _card?.Id ?? string.Empty;

	public bool IsIce => _isIce;

	public bool IsSelected => _selected;

	public bool IsTargetCandidate => _targetCandidate;

	public bool IsActionableSource => _actionableSource;

	public bool IsHovered => _hovered;

	public bool IsInteractionActive => _interactionActive;

	/// <summary>
	/// The rendered footprint. The table may choose a compact footprint while
	/// keeping the card's drawing and text layout legible at that size.
	/// </summary>
	public Vector2 DisplaySize => _displaySize;

	private bool IsCompactPortrait => !_isIce
		&& (_displaySize.X < CurrentAppearance.PortraitSize.X - 0.5f
			|| _displaySize.Y < CurrentAppearance.PortraitSize.Y - 0.5f);

	public override void _EnterTree()
	{
		base._EnterTree();
		ConnectAppearance();
	}

	public override void _Ready()
	{
		bool legacyConstructed = string.IsNullOrWhiteSpace(SceneFilePath);
		EnsureAuthoredLayout();
		ConnectAppearance();
		if (legacyConstructed)
		{
			// These values are authored on CardView.tscn. They are copied here
			// only for controls created by legacy `new TableCardControl()` calls.
			MouseFilter = MouseFilterEnum.Stop;
			FocusMode = FocusModeEnum.All;
			ClipContents = false;
			TooltipText = "Inspect card details; press Enter or Space to select.";
		}

		SetBackSide(_backSide);
		MouseEntered += HandleMouseEntered;
		MouseExited += HandleMouseExited;
		FocusEntered += HandleFocusEntered;
		FocusExited += HandleFocusExited;

		ApplySize();
		RefreshPresentation();
	}

	public override void _ExitTree()
	{
		DisconnectAppearance();
		base._ExitTree();
	}

	/// <summary>
	/// Legacy callers may still construct the script with <c>new</c>. Move the
	/// authored children from a temporary CardView instance in that case so
	/// there is one layout definition and no generated duplicate tree.
	/// </summary>
	private void EnsureAuthoredLayout()
	{
		TryBindAuthoredLabels();
		if (HasAuthoredLabels())
		{
			return;
		}

		PackedScene? scene = GD.Load<PackedScene>(CardViewScenePath);
		if (scene is not null)
		{
			Node? template = scene.Instantiate();
			if (template is TableCardControl templateCard)
			{
				if (_appearance is null && templateCard._appearance is not null)
				{
					ReplaceAppearance(templateCard._appearance);
				}

				foreach (Node child in template.GetChildren().ToArray())
				{
					template.RemoveChild(child);
					AddChild(child);
				}

				template.Free();
			}
			else
			{
				template?.Free();
			}
		}

		TryBindAuthoredLabels();
	}

	private bool HasAuthoredLabels() => _title is not null
		&& IsInstanceValid(_title)
		&& _type is not null
		&& IsInstanceValid(_type)
		&& _stats is not null
		&& IsInstanceValid(_stats)
		&& _rulesCue is not null
		&& IsInstanceValid(_rulesCue)
		&& _status is not null
		&& IsInstanceValid(_status)
		&& _counters is not null
		&& IsInstanceValid(_counters);

	private bool TryBindAuthoredLabels()
	{
		_title = GetNodeOrNull<Label>("Title");
		_type = GetNodeOrNull<Label>("Type");
		_stats = GetNodeOrNull<Label>("Stats");
		_rulesCue = GetNodeOrNull<Label>("RulesCue");
		_status = GetNodeOrNull<Label>("Status");
		_counters = GetNodeOrNull<Label>("Counters");
		return HasAuthoredLabels();
	}

	/// <summary>
	/// Replaces the viewer-safe card projection. An unknown projection always
	/// receives the neutral back and never attempts an artwork or metadata lookup.
	/// </summary>
	public void SetCard(CardViewModel? card)
	{
		bool wasActive = _interactionActive;
		_card = card;
		_selected = false;
		_targetCandidate = false;
		_actionableSource = false;
		RefreshArtwork();
		RefreshPresentation();
		// A concealed or removed projection must dismiss any preview that was
		// associated with this control. A newly known projection waits for a new
		// hover/focus transition so a state revision cannot resurrect stale text.
		if (wasActive && _card?.IsKnown != true)
		{
			_interactionActive = false;
			CardHoverChanged?.Invoke(this, false);
		}
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
	/// Marks this card as the source of at least one currently issued action.
	/// This state is presentation-only and is never inferred from card text.
	/// </summary>
	public void SetActionableSource(bool actionable)
	{
		_actionableSource = actionable && _card?.IsKnown == true;
		RefreshDynamicPresentation();
		QueueRedraw();
	}

	/// <summary>
	/// Changes the physical orientation/shape used for an ICE lane. The card
	/// remains fixed at 240×108 in this mode and 150×210 otherwise.
	/// </summary>
	public void SetIce(bool ice)
	{
		if (_isIce == ice)
		{
			return;
		}

		_isIce = ice;
		_hasExplicitDisplaySize = false;
		_displaySize = ice ? CurrentAppearance.IceSize : CurrentAppearance.PortraitSize;
		ApplySize();
		RefreshPresentation();
		QueueRedraw();
	}

	/// <summary>
	/// Sets the actual rendered footprint used by this card. This is intended
	/// for table lanes that need a compact portrait such as 105×147; callers
	/// should keep <see cref="Scale"/> at one so text remains pixel-legible.
	/// </summary>
	public void SetDisplaySize(Vector2 size)
	{
		if (size.X <= 0 || size.Y <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size), "A card display size must be positive.");
		}

		_hasExplicitDisplaySize = true;
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
		CardAppearance appearance = CurrentAppearance;
		Vector2 cardSize = _displaySize;
		Rect2 cardRect = new(Vector2.Zero, cardSize);
		Color accent = ResolveAccent();
		Color border = ResolveBorder();
		Color surface = _card is { IsKnown: true }
			? appearance.SurfaceColor
			: appearance.HiddenSurfaceColor;

		using var frame = new StyleBoxFlat
		{
			BgColor = surface,
			BorderColor = border,
			BorderWidthLeft = ToPixel(_selected || _targetCandidate ? appearance.EmphasisFrameWidth : appearance.FrameWidth),
			BorderWidthTop = ToPixel(_selected || _targetCandidate ? appearance.EmphasisFrameWidth : appearance.FrameWidth),
			BorderWidthRight = ToPixel(_selected || _targetCandidate ? appearance.EmphasisFrameWidth : appearance.FrameWidth),
			BorderWidthBottom = ToPixel(_selected || _targetCandidate ? appearance.EmphasisFrameWidth : appearance.FrameWidth),
			CornerRadiusTopLeft = ToPixel(appearance.CornerRadius),
			CornerRadiusTopRight = ToPixel(appearance.CornerRadius),
			CornerRadiusBottomLeft = ToPixel(appearance.CornerRadius),
			CornerRadiusBottomRight = ToPixel(appearance.CornerRadius),
			ShadowColor = _actionableSource
				? WithAlpha(border, appearance.GlowOpacity)
				: new Color(0, 0, 0, appearance.FrameShadowOpacity),
			ShadowSize = _actionableSource ? ToPixel(appearance.GlowOuterExtent) : 4,
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
		DrawRect(new Rect2(2, 2, cardSize.X - 4, headerHeight), accent.Darkened(appearance.HeaderDarken));
		DrawRect(
			new Rect2(2, 2 + headerHeight - 2, cardSize.X - 4, 2),
			accent.Lightened(appearance.HeaderLighten));

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
				: new Rect2(12, 58, Mathf.Max(1, cardSize.X - 24), 82);
		if (_artwork is not null)
		{
			DrawTextureRect(_artwork, motifRect, false, new Color(1, 1, 1, appearance.ArtworkOpacity));
		}

		DrawMotif(
			motifRect,
			accent,
			_artwork is not null ? appearance.MotifOpacityWithArtwork : appearance.MotifOpacityWithoutArtwork);
		DrawStateBadge(accent, cardSize);
		if (_actionableSource) DrawActionGlow(cardSize, border);
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
			CardAppearance appearance = CurrentAppearance;
			float leftWidth = Mathf.Max(1, _displaySize.X * 0.52f);
			float rightX = _displaySize.X * 0.56f;
			float rightWidth = Mathf.Max(1, _displaySize.X - rightX - 5);
			SetLabelRect(_title!, new Rect2(5, 4, leftWidth, 14), appearance.TitleFontSize, appearance.TitleColor);
			SetLabelRect(_type!, new Rect2(5, 18, leftWidth, 12), appearance.TypeFontSize, appearance.TypeColor);
			SetLabelRect(_stats!, new Rect2(5, _displaySize.Y - 20, leftWidth, 14), appearance.StatsFontSize, appearance.StatsColor);
			SetLabelRect(_rulesCue!, new Rect2(5, 34, leftWidth, 15), appearance.RulesFontSize, appearance.RulesColor);
			SetLabelRect(_status!, new Rect2(rightX, _displaySize.Y - 18, rightWidth, 13), appearance.StatusFontSize, appearance.StatusColor);
			SetLabelRect(_counters!, new Rect2(rightX, 3, rightWidth, 16), appearance.CounterFontSize, appearance.CounterColor);
		}
		else if (IsCompactPortrait)
		{
			CardAppearance appearance = CurrentAppearance;
			float width = Mathf.Max(1, _displaySize.X - 8);
			float statusY = Mathf.Max(0, _displaySize.Y - 14);
			float statsY = Mathf.Max(0, _displaySize.Y - 34);
			SetLabelRect(_title!, new Rect2(4, 2, width, 31), appearance.TitleFontSize, appearance.TitleColor);
			SetLabelRect(_type!, new Rect2(4, 33, width, 10), appearance.TypeFontSize, appearance.TypeColor);
			SetLabelRect(_stats!, new Rect2(4, statsY, width, 11), appearance.CompactStatsFontSize, appearance.StatsColor);
			SetLabelRect(_rulesCue!, new Rect2(4, 0, width, 1), appearance.RulesFontSize, appearance.RulesColor);
			SetLabelRect(_status!, new Rect2(4, statusY, width, 10), appearance.StatusFontSize, appearance.StatusColor);
			SetLabelRect(_counters!, new Rect2(4, Mathf.Max(0, _displaySize.Y - 24), width, 10), appearance.CounterFontSize, appearance.CounterColor);
		}
		else
		{
			CardAppearance appearance = CurrentAppearance;
			float width = Mathf.Max(1, _displaySize.X - 14);
			SetLabelRect(_title!, new Rect2(7, 6, width, 30), appearance.TitleFontSize, appearance.TitleColor);
			SetLabelRect(_type!, new Rect2(7, 34, width, 21), appearance.TypeFontSize, appearance.TypeColor);
			SetLabelRect(_stats!, new Rect2(7, 144, width, 21), appearance.StatsFontSize, appearance.StatsColor);
			SetLabelRect(_rulesCue!, new Rect2(7, 165, width, 21), appearance.RulesFontSize, appearance.RulesColor);
			SetLabelRect(_status!, new Rect2(7, 186, width, 18), appearance.StatusFontSize, appearance.StatusColor);
			SetLabelRect(_counters!, new Rect2(7, 123, width, 23), appearance.CounterFontSize, appearance.CounterColor);
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
		bool showText = known || _backTexture is null;
		_title!.Visible = showText;
		_type!.Visible = showText;
		_stats!.Visible = showText;
		_status!.Visible = showText;
		bool compact = IsCompactPortrait;
		_title!.Text = known ? compact ? CompactTitle(_card!.Title) : _card!.Title : "FACE-DOWN";
		_title!.AutowrapMode = TextServer.AutowrapMode.Off;
		_title!.VerticalAlignment = VerticalAlignment.Top;
		_title!.MaxLinesVisible = compact ? 2 : 1;
		_title!.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_type!.Text = known ? FormatType(_card!, compact || _isIce) : "UNKNOWN";
		_stats!.Text = known ? FormatStats(_card!, compact) : "HIDDEN";
		_rulesCue!.Text = known
			? BuildRulesCue(_card!.RulesText, _isIce ? 38 : compact ? 20 : 30)
			: "NO PUBLIC DETAILS";
		_status!.Text = known ? FormatStatus(_card!, compact) : "FACE-DOWN";
		_counters!.Text = known ? FormatCounters(_card!, compact) : string.Empty;
		_counters!.Visible = _counters!.Text.Length > 0;
		_rulesCue!.Visible = known && !_isIce && !compact;
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

	private void HandleMouseEntered()
	{
		_hovered = true;
		ZIndex = 10;
		QueueRedraw();
		EmitInspection();
		UpdateInteractionState(force: true);
	}

	private void HandleMouseExited()
	{
		_hovered = false;
		ZIndex = HasFocus() ? 10 : 0;
		QueueRedraw();
		UpdateInteractionState();
	}

	private void HandleFocusEntered()
	{
		ZIndex = 10;
		QueueRedraw();
		EmitInspection();
		UpdateInteractionState(force: true);
	}

	private void HandleFocusExited()
	{
		ZIndex = _hovered ? 10 : 0;
		QueueRedraw();
		UpdateInteractionState();
	}

	private void EmitInspection()
	{
		CardInspected?.Invoke(_card is { IsKnown: true } ? _card.Id : string.Empty);
	}

	private void UpdateInteractionState(bool force = false)
	{
		bool active = (_hovered || HasFocus()) && _card?.IsKnown == true;
		if (!force && active == _interactionActive)
		{
			return;
		}

		_interactionActive = active;
		CardHoverChanged?.Invoke(this, active);
	}

	private Color ResolveAccent()
	{
		CardAppearance appearance = CurrentAppearance;
		if (_card is not { IsKnown: true })
		{
			return appearance.HiddenColor;
		}

		string faction = _card.Faction.ToLowerInvariant();
		if (faction.Contains("anarch", StringComparison.Ordinal))
		{
			return appearance.AnarchAccent;
		}

		if (faction.Contains("criminal", StringComparison.Ordinal))
		{
			return appearance.CriminalAccent;
		}

		if (faction.Contains("shaper", StringComparison.Ordinal))
		{
			return appearance.ShaperAccent;
		}

		if (faction.Contains("haas", StringComparison.Ordinal)
			|| faction.Contains("bioroid", StringComparison.Ordinal))
		{
			return appearance.HaasAccent;
		}

		if (faction.Contains("jinteki", StringComparison.Ordinal))
		{
			return appearance.JintekiAccent;
		}

		if (faction.Contains("nbn", StringComparison.Ordinal))
		{
			return appearance.NbnAccent;
		}

		if (faction.Contains("weyland", StringComparison.Ordinal))
		{
			return appearance.WeylandAccent;
		}

		if (_card.Type.Contains("ice", StringComparison.OrdinalIgnoreCase))
		{
			return appearance.IceAccent;
		}

		return appearance.DefaultAccent;
	}

	private Color ResolveBorder()
	{
		CardAppearance appearance = CurrentAppearance;
		if (_selected)
		{
			return appearance.SelectedColor;
		}

		if (_targetCandidate)
		{
			return appearance.TargetColor;
		}

		if (_actionableSource)
		{
			return appearance.ActionableColor;
		}

		if (_hovered || HasFocus())
		{
			return appearance.FocusColor;
		}

		return _card is { IsKnown: true } ? appearance.FrameColor : appearance.HiddenColor;
	}

	private void DrawActionGlow(Vector2 cardSize, Color color)
	{
		// A broad inner fade complements the outer shadow where hand cards overlap.
		CardAppearance appearance = CurrentAppearance;
		using var glow = new StyleBoxFlat
		{
			BgColor = Colors.Transparent,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = ToPixel(appearance.CornerRadius),
			CornerRadiusTopRight = ToPixel(appearance.CornerRadius),
			CornerRadiusBottomLeft = ToPixel(appearance.CornerRadius),
			CornerRadiusBottomRight = ToPixel(appearance.CornerRadius),
		};
		int glowWidth = ToPixel(appearance.GlowInnerExtent);
		if (glowWidth <= 0)
		{
			return;
		}

		for (int inset = 1; inset <= glowWidth; inset++)
		{
			float distance = glowWidth <= 1 ? 0.0f : (inset - 1f) / (glowWidth - 1f);
			float fade = Mathf.Pow(1f - distance, appearance.GlowFalloff);
			glow.BorderColor = WithAlpha(color, appearance.GlowOpacity * fade);
			DrawStyleBox(glow, new Rect2(Vector2.One * inset, cardSize - Vector2.One * inset * 2));
		}
	}

	private void DrawNeutralBack(Rect2 cardRect, Color border)
	{
		CardAppearance appearance = CurrentAppearance;
		if (_backTexture is not null)
		{
			Vector2 available = cardRect.Size - new Vector2(6, 6);
			if (_isIce) available = new Vector2(available.Y, available.X);
			Vector2 imageSize = _backTexture.GetSize();
			Vector2 size = imageSize * Mathf.Min(available.X / imageSize.X, available.Y / imageSize.Y);
			DrawSetTransform(cardRect.GetCenter(), _isIce ? Mathf.Pi / 2 : 0);
			DrawTextureRect(_backTexture, new Rect2(-size / 2, size), false);
			DrawSetTransform(Vector2.Zero);
			return;
		}
		Rect2 inset = new(cardRect.Position + new Vector2(8, 8), cardRect.Size - new Vector2(16, 16));
		DrawRect(inset, WithAlpha(border, appearance.BackInsetOpacity), false, appearance.BackLineWidth);
		Vector2 center = cardRect.GetCenter();
		float radius = Mathf.Min(
			_isIce
				? Mathf.Min(19, cardRect.Size.Y * 0.22f)
				: IsCompactPortrait ? cardRect.Size.X * 0.26f : 28,
			Mathf.Min(cardRect.Size.X, cardRect.Size.Y) * 0.30f);
		DrawCircle(center, radius, WithAlpha(appearance.HiddenSurfaceColor, appearance.BackCenterOpacity));
		DrawArc(center, radius, 0, Mathf.Tau, 32, WithAlpha(border, appearance.BackRingOpacity), appearance.BackLineWidth);
		DrawLine(center + new Vector2(-radius * 0.55f, 0), center + new Vector2(radius * 0.55f, 0), border, appearance.BackCrossWidth);
		DrawLine(center + new Vector2(0, -radius * 0.55f), center + new Vector2(0, radius * 0.55f), border, appearance.BackCrossWidth);
	}

	private void DrawMotif(Rect2 motifRect, Color accent, float alpha)
	{
		CardAppearance appearance = CurrentAppearance;
		Color line = new(accent.R, accent.G, accent.B, alpha);
		Vector2 center = motifRect.GetCenter();
		float radius = Mathf.Min(motifRect.Size.X, motifRect.Size.Y) * 0.31f;
		DrawCircle(center, radius, new Color(accent.R, accent.G, accent.B, alpha * appearance.MotifFillOpacity));
		DrawArc(center, radius, 0, Mathf.Tau, 28, line, appearance.MotifLineWidth);
		DrawArc(center, radius * 0.63f, Mathf.Pi * 0.18f, Mathf.Pi * 1.35f, 18, line, appearance.MotifLineWidth);

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

		CardAppearance appearance = CurrentAppearance;

		if (_targetCandidate)
		{
			Rect2 target = _isIce
				? new Rect2(cardSize.X - 48, cardSize.Y - 19, 43, 14)
				: new Rect2(cardSize.X - 58, cardSize.Y - 18, 53, 13);
			DrawRect(target, WithAlpha(appearance.TargetColor, appearance.TargetBadgeOpacity));
			DrawRect(target, appearance.TargetColor, false, appearance.FrameWidth);
		}

		if (_selected)
		{
			Rect2 selected = new(new Vector2(3, 3), cardSize - new Vector2(6, 6));
			DrawRect(selected, WithAlpha(appearance.SelectedColor, appearance.SelectedBadgeOpacity), false, appearance.FrameWidth);
		}

		if (_card.Rezzed || _card.IsAbilityUsed || _card.IsFirstXUsedThisTurn)
		{
			Rect2 status = _isIce
				? new Rect2(cardSize.X - 69, 21, 64, 13)
				: IsCompactPortrait
					? new Rect2(4, Mathf.Max(0, cardSize.Y - 15), Mathf.Max(1, cardSize.X - 8), 11)
					: new Rect2(5, cardSize.Y - 14, Mathf.Max(1, cardSize.X - 10), 10);
			DrawRect(status, WithAlpha(accent, appearance.StatusBadgeOpacity));
		}
	}

	private static int ToPixel(float value) => Mathf.Max(0, Mathf.RoundToInt(value));

	private static Color WithAlpha(Color color, float alpha) => new(
		color.R,
		color.G,
		color.B,
		Mathf.Clamp(alpha, 0.0f, 1.0f));

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
