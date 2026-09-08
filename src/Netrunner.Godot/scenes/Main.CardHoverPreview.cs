using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Owns the nonmodal, viewer-safe card preview used by the card table. The
/// preview is a sibling of the table inside its clipped viewport, so it never
/// changes a source card's layout or intercepts pointer input.
/// </summary>
public partial class Main
{
	private PanelContainer? _cardHoverPreview;
	private TableCardControl? _cardHoverPreviewFace;
	private Label? _cardHoverPreviewTitle;
	private Label? _cardHoverPreviewType;
	private Label? _cardHoverPreviewStats;
	private Label? _cardHoverPreviewStatus;
	private Label? _cardHoverPreviewCounters;
	private Label? _cardHoverPreviewRules;
	private TableCardControl? _hoveredTableCard;
	private long _hoverPreviewSnapshotSequence = -1;

	/// <summary>Whether a known-card hover preview is currently visible.</summary>
	public bool CardHoverPreviewVisible => _cardHoverPreview?.Visible == true;

	/// <summary>The viewer-safe card identity currently shown in the preview.</summary>
	public string? CardHoverPreviewCardId => CardHoverPreviewVisible
		? _cardHoverPreviewFace?.CardId
		: null;

	/// <summary>The preview's viewport-clamped global rectangle.</summary>
	public Rect2 CardHoverPreviewGlobalRect => _cardHoverPreview?.GetGlobalRect() ?? new Rect2();

	/// <summary>The snapshot sequence that last invalidated the preview.</summary>
	public long CardHoverPreviewSnapshotSequence => _hoverPreviewSnapshotSequence;

	private void BuildCardHoverPreview(Control viewport)
	{
		GetWindow().FocusExited += HideCardHoverPreview;
		_cardHoverPreview = new PanelContainer
		{
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			ZIndex = 100,
			ClipContents = true,
			CustomMinimumSize = new Vector2(620, 380),
		};
		_cardHoverPreview.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.018f, 0.035f, 0.055f, 0.98f),
			BorderColor = new Color(0.92f, 0.97f, 1.0f, 0.88f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 9,
			CornerRadiusTopRight = 9,
			CornerRadiusBottomLeft = 9,
			CornerRadiusBottomRight = 9,
			ShadowColor = new Color(0.75f, 0.90f, 1.0f, 0.34f),
			ShadowSize = 8,
			ContentMarginLeft = 10,
			ContentMarginTop = 10,
			ContentMarginRight = 10,
			ContentMarginBottom = 10,
		});
		viewport.AddChild(_cardHoverPreview);

		HBoxContainer row = new()
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		row.AddThemeConstantOverride("separation", 12);
		_cardHoverPreview.AddChild(row);

		_cardHoverPreviewFace = new TableCardControl
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(240, 336),
		};
		row.AddChild(_cardHoverPreviewFace);
		// TableCardControl applies its regular interactive defaults in _Ready;
		// restore the preview's passive behavior after it enters the tree.
		_cardHoverPreviewFace.MouseFilter = Control.MouseFilterEnum.Ignore;
		_cardHoverPreviewFace.FocusMode = Control.FocusModeEnum.None;
		_cardHoverPreviewFace.SetDisplaySize(new Vector2(240, 336));

		VBoxContainer details = new()
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		details.AddThemeConstantOverride("separation", 3);
		ScrollContainer detailsScroll = new()
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			FocusMode = Control.FocusModeEnum.None,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		row.AddChild(detailsScroll);
		detailsScroll.AddChild(details);

		_cardHoverPreviewTitle = MakeWrappedLabel(string.Empty, 17, Colors.White);
		_cardHoverPreviewTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		details.AddChild(_cardHoverPreviewTitle);
		_cardHoverPreviewType = MakeWrappedLabel(string.Empty, 11, Accent);
		details.AddChild(_cardHoverPreviewType);
		_cardHoverPreviewStats = MakeWrappedLabel(string.Empty, 11, Colors.White);
		details.AddChild(_cardHoverPreviewStats);
		_cardHoverPreviewStatus = MakeWrappedLabel(string.Empty, 10, Muted);
		details.AddChild(_cardHoverPreviewStatus);
		_cardHoverPreviewCounters = MakeWrappedLabel(string.Empty, 10, Warning);
		details.AddChild(_cardHoverPreviewCounters);
		_cardHoverPreviewRules = MakeWrappedLabel(string.Empty, 12, Colors.White);
		_cardHoverPreviewRules.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_cardHoverPreviewRules.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		details.AddChild(_cardHoverPreviewRules);

		// BuildCardTable runs before BuildCardZoomOverlay. Defer the connection so
		// a card preview is dismissed as soon as the existing modal zoom opens.
		Callable.From(AttachHoverPreviewModalHooks).CallDeferred();
	}

	private void AttachHoverPreviewModalHooks()
	{
		if (IsInstanceValid(_zoomOverlay))
		{
			_zoomOverlay.VisibilityChanged += HandleHoverPreviewModalVisibilityChanged;
		}
	}

	private void HandleHoverPreviewModalVisibilityChanged()
	{
		if (_zoomOverlay.Visible)
		{
			HideCardHoverPreview();
		}
	}

	private void BeginCardHoverPreviewSnapshot(MatchSnapshot snapshot)
	{
		_hoverPreviewSnapshotSequence = snapshot.Sequence;
		HideCardHoverPreview();
	}

	private void HandleTableCardHoverChanged(TableCardControl card, bool active)
	{
		if (!active)
		{
			if (ReferenceEquals(_hoveredTableCard, card))
			{
				HideCardHoverPreview();
			}

			return;
		}

		_hoveredTableCard = card;
		ShowCardHoverPreview(card);
	}

	private void ShowCardHoverPreview(TableCardControl card)
	{
		if (!CanShowCardHoverPreview(card)
			|| _cardHoverPreview is null
			|| _cardHoverPreviewFace is null
			|| _cardHoverPreviewTitle is null
			|| _cardHoverPreviewType is null
			|| _cardHoverPreviewStats is null
			|| _cardHoverPreviewStatus is null
			|| _cardHoverPreviewCounters is null
			|| _cardHoverPreviewRules is null)
		{
			HideCardHoverPreview();
			return;
		}

		CardViewModel visible = card.Card!;
		_cardHoverPreviewFace.SetIce(card.IsIce);
		_cardHoverPreviewFace.SetDisplaySize(card.IsIce ? new Vector2(320, 144) : new Vector2(240, 336));
		_cardHoverPreviewFace.SetCard(visible);
		_cardHoverPreviewFace.MouseFilter = Control.MouseFilterEnum.Ignore;
		_cardHoverPreviewFace.FocusMode = Control.FocusModeEnum.None;
		_cardHoverPreviewTitle.Text = visible.Title;
		_cardHoverPreviewType.Text = FormatPreviewType(visible);
		_cardHoverPreviewStats.Text = FormatPreviewStats(visible);
		_cardHoverPreviewStatus.Text = visible.StatusLabel;
		_cardHoverPreviewCounters.Text = visible.CounterChips.Count == 0
			? string.Empty
			: string.Join(" · ", visible.CounterChips);
		_cardHoverPreviewCounters.Visible = _cardHoverPreviewCounters.Text.Length > 0;
		_cardHoverPreviewRules.Text = FormatCardRules(visible.RulesText);
		_cardHoverPreviewRules.Visible = _cardHoverPreviewRules.Text.Length > 0;

		_cardHoverPreview.Visible = true;
		_cardHoverPreview.MoveToFront();
		PositionCardHoverPreview(card);
	}

	private bool CanShowCardHoverPreview(TableCardControl card)
	{
		if (!_useCardTable
			|| _cardHoverPreview is null
			|| _tableViewport is null
			|| !_tableViewport.IsVisibleInTree()
			|| !_board.IsVisibleInTree()
			|| _currentSnapshot is null
			|| _startPanel.IsVisibleInTree()
			|| _zoomOverlay?.Visible == true
			|| _galleryOverlay?.Visible == true
			|| _tablePileOverlay?.Visible == true)
		{
			return false;
		}

		if (card.Card is not { IsKnown: true, Id.Length: > 0 }
			|| !card.IsInteractionActive
			|| !card.IsVisibleInTree()
			|| (!_gameTable.VisibleCards.Values.Contains(card)
				&& !(ReferenceEquals(card, _tableAccessCard) && _tableAccessPanel.IsVisibleInTree())))
		{
			return false;
		}

		return true;
	}

	private void PositionCardHoverPreview(TableCardControl source)
	{
		if (_cardHoverPreview is null || _tableViewport is null)
		{
			return;
		}

		Rect2 viewportRect = _tableViewport.GetGlobalRect();
		if (viewportRect.Size.X <= 0 || viewportRect.Size.Y <= 0)
		{
			return;
		}

		Vector2 desired = new(
			Mathf.Min(640, Mathf.Max(0, viewportRect.Size.X - 16)),
			Mathf.Min(400, Mathf.Max(0, viewportRect.Size.Y - 16)));
		if (desired.X < 1 || desired.Y < 1)
		{
			HideCardHoverPreview();
			return;
		}

		_cardHoverPreview.Size = desired;
		Rect2 sourceRect = source.GetGlobalRect();
		Vector2 sourceLocal = sourceRect.Position - viewportRect.Position;
		Vector2 position = new(sourceLocal.X + sourceRect.Size.X + 12, sourceLocal.Y);
		if (position.X + desired.X > viewportRect.Size.X - 8)
		{
			position.X = sourceLocal.X - desired.X - 12;
		}

		position.X = Mathf.Clamp(position.X, 8, Mathf.Max(8, viewportRect.Size.X - desired.X - 8));
		position.Y = Mathf.Clamp(position.Y, 8, Mathf.Max(8, viewportRect.Size.Y - desired.Y - 8));
		_cardHoverPreview.Position = position;
	}

	private void PollCardHoverPreview()
	{
		if (!CardHoverPreviewVisible) return;
		if (_hoveredTableCard is not { } source || !IsInstanceValid(source) || !CanShowCardHoverPreview(source))
			HideCardHoverPreview();
		else
			PositionCardHoverPreview(source);
	}

	private void HandleCardHoverPreviewViewportResized()
	{
		if (_cardHoverPreview?.Visible == true
			&& _hoveredTableCard is { } source
			&& CanShowCardHoverPreview(source))
		{
			PositionCardHoverPreview(source);
		}
	}

	private void HideCardHoverPreview()
	{
		_hoveredTableCard = null;
		if (_cardHoverPreview is not null)
		{
			_cardHoverPreview.Visible = false;
		}

		// Clear every viewer-known face when the preview is hidden. A later
		// concealed projection therefore cannot reveal a cached artwork or text.
		_cardHoverPreviewFace?.SetCard(null);
		if (_cardHoverPreviewTitle is not null) _cardHoverPreviewTitle.Text = string.Empty;
		if (_cardHoverPreviewType is not null) _cardHoverPreviewType.Text = string.Empty;
		if (_cardHoverPreviewStats is not null) _cardHoverPreviewStats.Text = string.Empty;
		if (_cardHoverPreviewStatus is not null) _cardHoverPreviewStatus.Text = string.Empty;
		if (_cardHoverPreviewCounters is not null) _cardHoverPreviewCounters.Text = string.Empty;
		if (_cardHoverPreviewRules is not null) _cardHoverPreviewRules.Text = string.Empty;
	}

	private static string FormatPreviewType(CardViewModel card)
	{
		string faction = card.Faction.Trim();
		string type = card.Type.Trim();
		return faction.Length == 0 ? type : $"{faction} · {type}";
	}

	private static string FormatPreviewStats(CardViewModel card)
	{
		var values = new List<string>();
		if (card.Cost is { } cost) values.Add($"Cost {cost}");
		if (card.Strength is { } strength) values.Add($"Strength {strength}");
		if (card.AgendaPoints is { } points) values.Add($"Agenda {points}");
		if (card.MemoryCost is { } memory) values.Add($"Memory {memory}");
		if (card.TrashCost is { } trash) values.Add($"Trash {trash}");
		return values.Count == 0 ? "Public card" : string.Join(" · ", values);
	}
}
