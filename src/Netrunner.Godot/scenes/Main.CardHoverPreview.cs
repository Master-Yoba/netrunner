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
	private CardPreviewLayout? _cardHoverPreview;
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
		_cardHoverPreview = viewport.GetNode<CardPreviewLayout>("CardHoverPreview");
		_cardHoverPreviewFace = _cardHoverPreview.GetNode<TableCardControl>("%Card");
		_cardHoverPreviewFace.MouseFilter = MouseFilterEnum.Ignore;
		_cardHoverPreviewFace.FocusMode = FocusModeEnum.None;
		_cardHoverPreviewTitle = _cardHoverPreview.GetNode<Label>("%Title");
		_cardHoverPreviewType = _cardHoverPreview.GetNode<Label>("%CardType");
		_cardHoverPreviewStats = _cardHoverPreview.GetNode<Label>("%Stats");
		_cardHoverPreviewStatus = _cardHoverPreview.GetNode<Label>("%Status");
		_cardHoverPreviewCounters = _cardHoverPreview.GetNode<Label>("%Counters");
		_cardHoverPreviewRules = _cardHoverPreview.GetNode<Label>("%Rules");
		Callable.From(AttachHoverPreviewModalHooks).CallDeferred();
	}

	private void AttachHoverPreviewModalHooks()
	{
		if (IsInstanceValid(_zoomOverlay))
		{
			_zoomOverlay.VisibilityChanged += HandleHoverPreviewModalVisibilityChanged;
		}
	}

	private void DisconnectCardHoverPreview()
	{
		if (!_useCardTable) return;
		GetWindow().FocusExited -= HideCardHoverPreview;
		if (IsInstanceValid(_tableViewport))
			_tableViewport.Resized -= HandleCardHoverPreviewViewportResized;
		if (IsInstanceValid(_zoomOverlay))
			_zoomOverlay.VisibilityChanged -= HandleHoverPreviewModalVisibilityChanged;
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
		_cardHoverPreview.RefreshCardSize();
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
			Mathf.Min(_cardHoverPreview.PreferredSize.X, Mathf.Max(0, viewportRect.Size.X - 2 * _cardHoverPreview.ViewportMargin)),
			Mathf.Min(_cardHoverPreview.PreferredSize.Y, Mathf.Max(0, viewportRect.Size.Y - 2 * _cardHoverPreview.ViewportMargin)));
		if (desired.X < 1 || desired.Y < 1)
		{
			HideCardHoverPreview();
			return;
		}

		_cardHoverPreview.Size = desired;
		Rect2 sourceRect = source.GetGlobalRect();
		Vector2 sourceLocal = sourceRect.Position - viewportRect.Position;
		Vector2 position = new(sourceLocal.X + sourceRect.Size.X + _cardHoverPreview.SourceGap, sourceLocal.Y);
		if (position.X + desired.X > viewportRect.Size.X - _cardHoverPreview.ViewportMargin)
		{
			position.X = sourceLocal.X - desired.X - _cardHoverPreview.SourceGap;
		}

		position.X = Mathf.Clamp(position.X, _cardHoverPreview.ViewportMargin, Mathf.Max(_cardHoverPreview.ViewportMargin, viewportRect.Size.X - desired.X - _cardHoverPreview.ViewportMargin));
		position.Y = Mathf.Clamp(position.Y, _cardHoverPreview.ViewportMargin, Mathf.Max(_cardHoverPreview.ViewportMargin, viewportRect.Size.Y - desired.Y - _cardHoverPreview.ViewportMargin));
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
