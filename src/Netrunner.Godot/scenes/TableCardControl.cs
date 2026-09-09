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
		get
		{
			if (_appearance is null || !GodotObject.IsInstanceValid(_appearance))
			{
				ReplaceAppearance(LoadDefaultAppearance());
			}

			return _appearance!;
		}
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
		AppearanceReplaced?.Invoke(this);
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
		if (!HasAuthoredLabels())
		{
			TryBindAuthoredLabels();
		}

		if (HasAuthoredLabels())
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
					// Release presets omit development-only card backs.
					if (!ResourceLoader.Exists(path)) continue;
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
	private StyleBox? _statusBadgeStyle;
	private bool _statusBadgeStyleConnected;
	private CardLabelLayout? _statusBadgeLayout;
	private bool _statusBadgeLayoutConnected;
	private bool _statusRectConnected;
	private Control? _layoutHost;
	private Control? _portraitLayout;
	private Control? _compactLayout;
	private Control? _iceLayout;
	private Control? _activeLayout;
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

	/// <summary>Notifies layout owners when an Inspector resource assignment changes.</summary>
	public event Action<TableCardControl>? AppearanceReplaced;

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
		// A detached control can miss a resource change while the editor moves
		// it between owners. Rebind authored children and apply the current
		// shared values before it becomes visible again.
		_ = Appearance;
		ConnectAppearance();
		TryBindAuthoredLabels();
		RefreshAppearance();
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
		DisconnectStatusRect();
		DisconnectStatusBadgeLayout();
		DisconnectStatusBadgeStyle();
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

	private bool HasAuthoredLayouts() => _layoutHost is not null
		&& IsInstanceValid(_layoutHost)
		&& _portraitLayout is not null
		&& IsInstanceValid(_portraitLayout)
		&& _compactLayout is not null
		&& IsInstanceValid(_compactLayout)
		&& _iceLayout is not null
		&& IsInstanceValid(_iceLayout);

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
		_layoutHost = GetNodeOrNull<Control>("LayoutHost");
		_portraitLayout = GetNodeOrNull<Control>("LayoutHost/PortraitLayout");
		_compactLayout = GetNodeOrNull<Control>("LayoutHost/CompactLayout");
		_iceLayout = GetNodeOrNull<Control>("LayoutHost/IceLayout");
		if (!HasAuthoredLayouts())
		{
			return false;
		}

		if (_activeLayout is not null && !IsInstanceValid(_activeLayout))
		{
			_activeLayout = null;
		}

		BindLabelLayout(_activeLayout ?? _portraitLayout!);
		return HasAuthoredLabels();
	}

	private void BindLabelLayout(Control layout)
	{
		DisconnectStatusRect();
		_title = layout.GetNodeOrNull<Label>("Title");
		_type = layout.GetNodeOrNull<Label>("Type");
		_stats = layout.GetNodeOrNull<Label>("Stats");
		_rulesCue = layout.GetNodeOrNull<Label>("RulesCue");
		_status = layout.GetNodeOrNull<Label>("Status");
		_counters = layout.GetNodeOrNull<Label>("Counters");
		BindStatusBadgeStyle(layout);
		ConnectStatusRect();
	}

	private void BindStatusBadgeStyle(Control layout)
	{
		CardLabelLayout? authoredLayout = layout as CardLabelLayout;
		if (!ReferenceEquals(_statusBadgeLayout, authoredLayout))
		{
			DisconnectStatusBadgeLayout();
			_statusBadgeLayout = authoredLayout;
		}
		ConnectStatusBadgeLayout();

		StyleBox? style = authoredLayout?.StatusBadgeStyle;
		if (!ReferenceEquals(_statusBadgeStyle, style))
		{
			DisconnectStatusBadgeStyle();
			_statusBadgeStyle = style;
		}

		ConnectStatusBadgeStyle();
	}

	private void ConnectStatusBadgeLayout()
	{
		if (_statusBadgeLayoutConnected
			|| !IsInsideTree()
			|| _statusBadgeLayout is null
			|| !GodotObject.IsInstanceValid(_statusBadgeLayout))
		{
			return;
		}

		_statusBadgeLayout.StatusBadgeStyleChanged += HandleStatusBadgeStyleReplaced;
		_statusBadgeLayoutConnected = true;
	}

	private void DisconnectStatusBadgeLayout()
	{
		if (!_statusBadgeLayoutConnected
			|| _statusBadgeLayout is null
			|| !GodotObject.IsInstanceValid(_statusBadgeLayout))
		{
			_statusBadgeLayoutConnected = false;
			return;
		}

		_statusBadgeLayout.StatusBadgeStyleChanged -= HandleStatusBadgeStyleReplaced;
		_statusBadgeLayoutConnected = false;
	}

	private void HandleStatusBadgeStyleReplaced()
	{
		if (_activeLayout is not null && IsInstanceValid(_activeLayout))
		{
			BindStatusBadgeStyle(_activeLayout);
		}

		QueueRedraw();
	}

	private void ConnectStatusBadgeStyle()
	{
		if (_statusBadgeStyleConnected
			|| !IsInsideTree()
			|| _statusBadgeStyle is null
			|| !GodotObject.IsInstanceValid(_statusBadgeStyle))
		{
			return;
		}

		_statusBadgeStyle.Changed += HandleStatusBadgeStyleChanged;
		_statusBadgeStyleConnected = true;
	}

	private void DisconnectStatusBadgeStyle()
	{
		if (!_statusBadgeStyleConnected
			|| _statusBadgeStyle is null
			|| !GodotObject.IsInstanceValid(_statusBadgeStyle))
		{
			_statusBadgeStyleConnected = false;
			return;
		}

		_statusBadgeStyle.Changed -= HandleStatusBadgeStyleChanged;
		_statusBadgeStyleConnected = false;
	}

	private void HandleStatusBadgeStyleChanged()
	{
		if (GodotObject.IsInstanceValid(this))
		{
			QueueRedraw();
		}
	}

	private void ConnectStatusRect()
	{
		if (_statusRectConnected || !IsInsideTree() || _status is null || !IsInstanceValid(_status))
		{
			return;
		}

		_status.ItemRectChanged += HandleStatusRectChanged;
		_statusRectConnected = true;
	}

	private void DisconnectStatusRect()
	{
		if (!_statusRectConnected || _status is null || !IsInstanceValid(_status))
		{
			_statusRectConnected = false;
			return;
		}

		_status.ItemRectChanged -= HandleStatusRectChanged;
		_statusRectConnected = false;
	}

	private void HandleStatusRectChanged()
	{
		QueueRedraw();
	}

	private void SelectLabelLayout()
	{
		Control? desired = _isIce
			? _iceLayout
			: IsCompactPortrait ? _compactLayout : _portraitLayout;
		if (desired is null || !IsInstanceValid(desired))
		{
			return;
		}
		// A card can receive its compact/ICE role before entering the tree.
		// In that case the authored portrait is initially visible even though
		// no active layout has been bound yet.
		_portraitLayout!.Visible = ReferenceEquals(desired, _portraitLayout);
		_compactLayout!.Visible = ReferenceEquals(desired, _compactLayout);
		_iceLayout!.Visible = ReferenceEquals(desired, _iceLayout);

		if (!ReferenceEquals(_activeLayout, desired))
		{
			if (_activeLayout is not null && IsInstanceValid(_activeLayout))
			{
				_activeLayout.Visible = false;
			}

			_activeLayout = desired;
			_activeLayout.Visible = true;
			BindLabelLayout(_activeLayout);
		}
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

		int frameWidth = ToPixel(_selected || _targetCandidate ? appearance.EmphasisFrameWidth : appearance.FrameWidth);
		Rect2 frameRect = new(Vector2.One, cardSize - new Vector2(2, 2));
		using var frame = new StyleBoxFlat
		{
			BgColor = surface,
			BorderColor = border,
			BorderWidthLeft = frameWidth,
			BorderWidthTop = frameWidth,
			BorderWidthRight = frameWidth,
			BorderWidthBottom = frameWidth,
			CornerRadiusTopLeft = ToPixel(appearance.CornerRadius),
			CornerRadiusTopRight = ToPixel(appearance.CornerRadius),
			CornerRadiusBottomLeft = ToPixel(appearance.CornerRadius),
			CornerRadiusBottomRight = ToPixel(appearance.CornerRadius),
			ShadowColor = _actionableSource
				? WithAlpha(border, appearance.GlowOpacity)
				: new Color(0, 0, 0, appearance.FrameShadowOpacity),
			ShadowSize = _actionableSource ? ToPixel(appearance.GlowOuterExtent) : 4,
		};
		DrawStyleBox(frame, frameRect);

		if (_card is not { IsKnown: true })
		{
			DrawNeutralBack(cardRect, border);
			return;
		}

		// Keep the header inside the frame's actual border. Its top corners
		// follow the frame's inner radius so the header cannot square off or
		// cover the rounded outer border, including selected/target frames.
		float frameInset = Mathf.Max(1.0f, frameWidth);
		Rect2 contentRect = InsetRect(frameRect, frameInset);
		float headerHeight = _isIce
			? Mathf.Min(appearance.IceHeaderHeight, cardSize.Y * 0.34f)
			: IsCompactPortrait
				? Mathf.Min(appearance.CompactHeaderHeight, cardSize.Y * 0.34f)
				: Mathf.Min(appearance.PortraitHeaderHeight, cardSize.Y * 0.30f);
		headerHeight = Mathf.Min(headerHeight, contentRect.Size.Y);
		Rect2 headerRect = new(contentRect.Position, new Vector2(contentRect.Size.X, headerHeight));
		int headerRadius = ToPixel(Mathf.Max(0.0f, appearance.CornerRadius - frameInset));
		using var header = new StyleBoxFlat
		{
			BgColor = accent.Darkened(appearance.HeaderDarken),
			CornerRadiusTopLeft = headerRadius,
			CornerRadiusTopRight = headerRadius,
		};
		DrawStyleBox(header, headerRect);

		float underlineHeight = Mathf.Min(appearance.HeaderUnderlineHeight, headerRect.Size.Y);
		if (underlineHeight > 0.0f)
		{
			DrawRect(
				new Rect2(
					headerRect.Position + new Vector2(0, headerRect.Size.Y - underlineHeight),
					new Vector2(headerRect.Size.X, underlineHeight)),
				accent.Lightened(appearance.HeaderLighten));
		}
		DrawAuthoredStatusBadge();
		DrawStatusBadge(accent, cardSize);

		// The glow is part of the card surface. Draw it before motif/artwork,
		// non-status badges, and child labels. The bottom status background
		// deliberately sits beneath the glow while its text stays above it.
		if (_actionableSource)
		{
			DrawActionGlow(
				border,
				contentRect,
				Mathf.Max(0.0f, appearance.CornerRadius - frameInset));
		}

		Rect2 motifRect = ResolveMotifRect(cardSize, appearance);
		if (_artwork is not null)
		{
			DrawTextureRect(_artwork, motifRect, false, new Color(1, 1, 1, appearance.ArtworkOpacity));
		}

		DrawMotif(
			motifRect,
			accent,
			_artwork is not null ? appearance.MotifOpacityWithArtwork : appearance.MotifOpacityWithoutArtwork);
		DrawStateBadge(accent, cardSize);
	}

	private Rect2 ResolveMotifRect(Vector2 cardSize, CardAppearance appearance)
	{
		Rect2 normalized = _isIce
			? appearance.IceMotifRect
			: IsCompactPortrait ? appearance.CompactMotifRect : appearance.PortraitMotifRect;
		return new Rect2(
			cardSize.X * normalized.Position.X,
			cardSize.Y * normalized.Position.Y,
			Mathf.Max(1.0f, cardSize.X * normalized.Size.X),
			Mathf.Max(1.0f, cardSize.Y * normalized.Size.Y));
	}

	private void ApplySize()
	{
		Vector2 desired = _displaySize;
		CustomMinimumSize = desired;
		// A CardView can be authored as a full-stretch child of a host. In that
		// case the parent owns its footprint; assigning Size during EnterTree or
		// Ready makes Godot warn and overrides the saved layout after ready. Keep
		// the minimum-size hint for containers, while leaving stretch offsets and
		// the editor-authored child geometry intact.
		bool ownsSize = Mathf.IsEqualApprox(AnchorLeft, AnchorRight)
			&& Mathf.IsEqualApprox(AnchorTop, AnchorBottom);
		if (!ownsSize)
		{
			return;
		}

		Size = desired;
		SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		SizeFlagsVertical = SizeFlags.ShrinkCenter;
	}

	private void RefreshPresentation()
	{
		if (!HasAuthoredLabels())
		{
			TryBindAuthoredLabels();
		}

		if (!HasAuthoredLabels())
		{
			QueueRedraw();
			return;
		}

		SelectLabelLayout();
		ApplyLabelAppearance();

		RefreshDynamicPresentation();
		QueueRedraw();
	}

	private void ApplyLabelAppearance()
	{
		if (!HasAuthoredLabels())
		{
			return;
		}

		CardAppearance appearance = CurrentAppearance;
		bool compact = IsCompactPortrait;
		int typeFontSize = compact ? appearance.CompactTypeFontSize : appearance.TypeFontSize;
		int statsFontSize = compact ? appearance.CompactStatsFontSize : appearance.StatsFontSize;
		SetLabelAppearance(_title!, appearance.TitleFontSize, appearance.TitleColor);
		SetLabelAppearance(_type!, typeFontSize, appearance.TypeColor);
		SetLabelAppearance(_stats!, statsFontSize, appearance.StatsColor);
		SetLabelAppearance(_rulesCue!, appearance.RulesFontSize, appearance.RulesColor);
		SetLabelAppearance(_status!, appearance.StatusFontSize, appearance.StatusColor);
		SetLabelAppearance(_counters!, appearance.CounterFontSize, appearance.CounterColor);
	}

	private void RefreshDynamicPresentation()
	{
		if (!HasAuthoredLabels())
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

	private static void SetLabelAppearance(Label? label, int fontSize, Color colour)
	{
		if (label is null)
		{
			return;
		}

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

	private void DrawActionGlow(Color color, Rect2 contentRect, float contentRadius)
	{
		CardAppearance appearance = CurrentAppearance;
		float glowWidth = appearance.GlowInnerExtent;
		if (glowWidth <= 0.0f || appearance.GlowOpacity <= 0.0f)
		{
			return;
		}

		// Keep the filled gradient inside the frame and leave enough room for a
		// non-degenerate inner rectangle on both portrait and ICE footprints.
		float maxWidth = Mathf.Max(0.0f, Mathf.Min(contentRect.Size.X, contentRect.Size.Y) * 0.5f - 0.5f);
		glowWidth = Mathf.Min(glowWidth, maxWidth);
		if (glowWidth <= 0.0f)
		{
			return;
		}

		// A small number of filled bands preserves the exported falloff without
		// drawing separate antialiased outline StyleBoxes. Adjacent bands share
		// exact vertices and colors, so the result is a continuous surface fade.
		int bandCount = Mathf.Clamp(Mathf.CeilToInt(glowWidth / 2.0f), 1, 8);
		int cornerSegments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(contentRadius, 1.0f)), 8, 16);
		int pointsPerRing = (cornerSegments + 1) * 4;
		Vector2[] vertices = new Vector2[(bandCount + 1) * pointsPerRing];
		Color[] colors = new Color[vertices.Length];
		int[] indices = new int[bandCount * pointsPerRing * 6];
		for (int ring = 0; ring <= bandCount; ring++)
		{
			float distance = ring / (float)bandCount;
			float inset = glowWidth * distance;
			Vector2[] path = RoundedRectPath(
				InsetRect(contentRect, inset),
				Mathf.Max(0.0f, contentRadius - inset),
				cornerSegments);
			int offset = ring * pointsPerRing;
			path.CopyTo(vertices, offset);
			Color ringColor = WithAlpha(color, appearance.GlowOpacity * Mathf.Pow(1.0f - distance, appearance.GlowFalloff));
			Array.Fill(colors, ringColor, offset, pointsPerRing);
			if (ring == bandCount) continue;
			for (int point = 0; point < pointsPerRing; point++)
			{
				int next = (point + 1) % pointsPerRing;
				int index = (offset + point) * 6;
				indices[index] = offset + point;
				indices[index + 1] = offset + next;
				indices[index + 2] = offset + pointsPerRing + next;
				indices[index + 3] = offset + point;
				indices[index + 4] = offset + pointsPerRing + next;
				indices[index + 5] = offset + pointsPerRing + point;
			}
		}
		// One cached canvas command, rather than a draw call for every quad.
		RenderingServer.CanvasItemAddTriangleArray(
			GetCanvasItem(), indices, vertices, colors,
			Array.Empty<Vector2>(), Array.Empty<int>(), Array.Empty<float>(), default, -1);
	}

	private static Vector2[] RoundedRectPath(Rect2 rect, float radius, int cornerSegments)
	{
		float maxRadius = Mathf.Min(rect.Size.X, rect.Size.Y) * 0.5f;
		radius = Mathf.Clamp(radius, 0.0f, maxRadius);
		Vector2 endPosition = rect.Position + rect.Size;
		List<Vector2> points = new((cornerSegments + 1) * 4);
		AppendRoundedCorner(
			points,
			new Vector2(rect.Position.X + radius, rect.Position.Y + radius),
			radius,
			Mathf.Pi,
			Mathf.Pi * 1.5f,
			cornerSegments);
		AppendRoundedCorner(
			points,
			new Vector2(endPosition.X - radius, rect.Position.Y + radius),
			radius,
			Mathf.Pi * 1.5f,
			Mathf.Tau,
			cornerSegments);
		AppendRoundedCorner(
			points,
			new Vector2(endPosition.X - radius, endPosition.Y - radius),
			radius,
			0.0f,
			Mathf.Pi * 0.5f,
			cornerSegments);
		AppendRoundedCorner(
			points,
			new Vector2(rect.Position.X + radius, endPosition.Y - radius),
			radius,
			Mathf.Pi * 0.5f,
			Mathf.Pi,
			cornerSegments);
		return points.ToArray();
	}

	private static void AppendRoundedCorner(
		List<Vector2> points,
		Vector2 center,
		float radius,
		float startAngle,
		float endAngle,
		int segments)
	{
		for (int index = 0; index <= segments; index++)
		{
			float fraction = index / (float)segments;
			float angle = Mathf.Lerp(startAngle, endAngle, fraction);
			points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
		}
	}

	private static Rect2 InsetRect(Rect2 rect, float inset)
	{
		Vector2 delta = Vector2.One * inset;
		return new Rect2(rect.Position + delta, rect.Size - delta * 2.0f);
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

	}

	private void DrawAuthoredStatusBadge()
	{
		if (_status is null
			|| !IsInstanceValid(_status)
			|| !_status.Visible
			|| _statusBadgeStyle is null
			|| !GodotObject.IsInstanceValid(_statusBadgeStyle)
			|| _status.Size.X <= 0.0f
			|| _status.Size.Y <= 0.0f)
		{
			return;
		}

		// The visible badge belongs to the authored layout resource, but is
		// painted by the card surface before the glow. The label keeps only a
		// transparent padded style, so its text remains the final layer.
		DrawSetTransformMatrix(GetGlobalTransform().AffineInverse() * _status.GetGlobalTransform());
		DrawStyleBox(_statusBadgeStyle, new Rect2(Vector2.Zero, _status.Size));
		DrawSetTransformMatrix(Transform2D.Identity);
	}

	private void DrawStatusBadge(Color accent, Vector2 cardSize)
	{
		if (_card is null
			|| !_card.IsKnown
			|| (!_card.Rezzed && !_card.IsAbilityUsed && !_card.IsFirstXUsedThisTurn))
		{
			return;
		}

		CardAppearance appearance = CurrentAppearance;
		Rect2 status = _isIce
			? new Rect2(cardSize.X - 69, 21, 64, 13)
			: IsCompactPortrait
				? new Rect2(4, Mathf.Max(0, cardSize.Y - 15), Mathf.Max(1, cardSize.X - 8), 11)
				: new Rect2(5, cardSize.Y - 14, Mathf.Max(1, cardSize.X - 10), 10);
		DrawRect(status, WithAlpha(accent, appearance.StatusBadgeOpacity));
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
