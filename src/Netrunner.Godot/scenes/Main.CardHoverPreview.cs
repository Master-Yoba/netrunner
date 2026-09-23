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
	private GameSymbolLabel? _cardHoverPreviewStats;
	private Label? _cardHoverPreviewStatus;
	private Label? _cardHoverPreviewCounters;
	private GameSymbolLabel? _cardHoverPreviewRules;
	private Label? _cardHoverPreviewHint;
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
		_cardHoverPreviewFace.UseHoverPreview = true;
		_cardHoverPreviewFace.MouseFilter = MouseFilterEnum.Ignore;
		_cardHoverPreviewFace.FocusMode = FocusModeEnum.None;
		_cardHoverPreviewTitle = _cardHoverPreview.GetNode<Label>("%Title");
		_cardHoverPreviewType = _cardHoverPreview.GetNode<Label>("%CardType");
		_cardHoverPreviewStats = _cardHoverPreview.GetNode<GameSymbolLabel>("%Stats");
		_cardHoverPreviewStatus = _cardHoverPreview.GetNode<Label>("%Status");
		_cardHoverPreviewCounters = _cardHoverPreview.GetNode<Label>("%Counters");
		_cardHoverPreviewRules = _cardHoverPreview.GetNode<GameSymbolLabel>("%Rules");
		_cardHoverPreviewHint = _cardHoverPreview.GetNode<Label>("%Hint");
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

		if (_boardDrag?.IsDragging == true) return;
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
			|| _cardHoverPreviewRules is null
			|| _cardHoverPreviewHint is null)
		{
			HideCardHoverPreview();
			return;
		}

		CardViewModel visible = card.Card!;
		_cardHoverPreviewFace.SetCard(visible);
		_cardHoverPreviewFace.SetIce(card.IsIce && !_cardHoverPreviewFace.HasFullCardArtwork);
		_cardHoverPreview.RefreshCardSize();
		_cardHoverPreviewFace.MouseFilter = Control.MouseFilterEnum.Ignore;
		_cardHoverPreviewFace.FocusMode = Control.FocusModeEnum.None;
		_cardHoverPreviewTitle.Text = visible.Title;
		_cardHoverPreviewType.Text = FormatPreviewType(visible);
		_cardHoverPreviewStats.SymbolText = FormatPreviewStats(visible);
		_cardHoverPreviewStatus.Text = FormatPreviewStatus(visible);
		_cardHoverPreviewCounters.Text = string.Join(" · ", visible.Counters.Concat(visible.CounterChips));
		_cardHoverPreviewCounters.Visible = _cardHoverPreviewCounters.Text.Length > 0;
		_cardHoverPreviewRules.SymbolText = FormatCardRules(visible.RulesText);
		_cardHoverPreviewRules.Visible = _cardHoverPreviewRules.SymbolText.Length > 0;
		_cardHoverPreviewHint.Text = "Press Enter or Space to select. Inspect card for full details.";
		if (card.GetParent() is BoardActionButton && _currentSnapshot is { } snapshot)
		{
			bool archives = snapshot.View.CorpDiscard.Any(candidate => candidate.Id == visible.Id);
			_cardHoverPreviewStatus.Text += archives
				? $"\nArchives · {snapshot.View.CorpDiscardCount} cards"
				: $"\nHeap · {snapshot.View.RunnerHeap.Count} public cards";
			_cardHoverPreviewHint.Text = "Click or press Enter to browse this pile. Inspect card for full details.";
		}
		_cardHoverPreview.GetNode<ScrollContainer>("%DetailsScroll").ScrollVertical = 0;

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
			|| (_icebreakerPanel?.Visible == true && (card.CardId == _boardBreakerId
				|| (_boardChoosingSubroutines && card.CardId == _currentSnapshot?.View.Run?.Encounter?.Ice?.Id)
				|| _icebreakerPanel.GetGlobalRect().HasPoint(GetViewport().GetMousePosition())))
			|| _startPanel.IsVisibleInTree()
			|| SessionModalVisible || _zoomOverlay?.Visible == true
			|| _galleryOverlay?.Visible == true
			|| _tablePileOverlay?.Visible == true)
		{
			return false;
		}

		if (card.Card is not { IsKnown: true, Id.Length: > 0 }
			|| !card.IsInteractionActive
			|| !card.IsVisibleInTree()
			|| (!_gameTable.VisibleCards.Values.Contains(card)
				&& !(ReferenceEquals(card, _tableAccessCard) && _tableAccessPanel.IsVisibleInTree())
				&& !(SpatialVisible && _spatialOptions?.IsAncestorOf(card) == true)))
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
		if (_cardHoverPreviewStats is not null) _cardHoverPreviewStats.SymbolText = string.Empty;
		if (_cardHoverPreviewStatus is not null) _cardHoverPreviewStatus.Text = string.Empty;
		if (_cardHoverPreviewCounters is not null) _cardHoverPreviewCounters.Text = string.Empty;
		if (_cardHoverPreviewRules is not null) _cardHoverPreviewRules.SymbolText = string.Empty;
		if (_cardHoverPreviewHint is not null) _cardHoverPreviewHint.Text = string.Empty;
	}

	private static string FormatPreviewType(CardViewModel card)
	{
		string faction = card.Faction.Trim();
		string type = card.Type.Trim();
		if (card.Subtypes.Count > 0) type += " · " + string.Join(" · ", card.Subtypes);
		return faction.Length == 0 ? type : $"{faction} · {type}";
	}

	private string FormatPreviewStatus(CardViewModel card)
	{
		var values = new List<string> { card.StatusLabel };
		if (card.AbilityCue is { Length: > 0 } ability) values.Add(ability);
		if (card.FirstXUseCue is { } firstUse) values.Add(firstUse);
		if (card.HandCondition is { } condition) values.Add("Condition met · " + condition.Summary);
		string status = string.Join(" · ", values);
		if (card.HostCardId is { } hostId && _currentSnapshot is { } snapshot)
		{
			CardViewModel? host = BoardChoiceCards(snapshot.View).FirstOrDefault(candidate => candidate.Id == hostId);
			status += $"\nHosted on {(host is { IsKnown: true } ? host.Title : "face-down card")}";
		}
		return status;
	}

	private static string FormatPreviewStats(CardViewModel card)
	{
		var values = new List<string>();
		if (card.Cost is { } cost) values.Add($"Cost {cost} [credit]");
		if (card.HandCondition?.InstallCost is { } install) values.Add($"Install now {install.EffectiveCost} [credit] (printed {install.BaseCost})");
		if (card.Strength is { } strength) values.Add($"Strength {strength}");
		if (card.AgendaPoints is { } points) values.Add($"Agenda {points} [agenda point]");
		if (card.MemoryCost is { } memory) values.Add($"Memory {memory} [MU]");
		if (card.TrashCost is { } trash) values.Add($"Trash {trash} [trash cost]");
		if (card.AdvancementRequirement is { } advancement) values.Add($"Advancement requirement {advancement}");
		return values.Count == 0 ? "Public card" : string.Join(" · ", values);
	}
}
