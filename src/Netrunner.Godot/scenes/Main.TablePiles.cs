using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Viewer-safe browsing for public piles. Decks expose their count only;
/// discard and score areas render the card projections already admitted to
/// the current MatchViewModel.
/// </summary>
public partial class Main
{
	private PanelContainer? _tablePileOverlay;
	private Label _tablePileTitle = null!;
	private Label _tablePileSummary = null!;
	private Button _tablePileClose = null!;
	private ScrollContainer _tablePileScroll = null!;
	private GridContainer _tablePileCards = null!;
	private Control? _tablePilePreviousFocus;
	private bool _tablePilePreviousFocusHidden;

	/// <summary>
	/// Opens a viewer-safe pile browser for a table zone handle. Returns false
	/// when the handle is not a supported pile or no published view is present.
	/// </summary>
	public bool ShowTablePile(string handle)
	{
		if (_currentSnapshot is not { View: { } view }
			|| !TryResolveTablePile(view, handle, out string title, out int count,
				out IReadOnlyList<CardViewModel> cards, out bool concealed))
		{
			return false;
		}

		BuildTablePileOverlay();
		CloseBoardIcebreaker();
		if (!_tablePileOverlay!.Visible)
		{
			_tablePilePreviousFocus = GetViewport().GuiGetFocusOwner();
			_tablePilePreviousFocusHidden = _tablePilePreviousFocus?.HasFocus(ignoreHiddenFocus: true) == false;
		}

		_tablePileTitle.Text = title;
		_tablePileSummary.Text = concealed
			? $"{count} card(s) · count only · cards remain concealed"
			: $"{count} card(s) · public faces shown where available · select a known card to inspect";
		RenderTablePileCards(cards, count, concealed);
		HideCardHoverPreview();
		_tablePileOverlay.Visible = true;
		_tablePileOverlay.MoveToFront();
		_tablePileClose.GrabFocus();
		return true;
	}

	/// <summary>Closes the pile browser and restores the control that opened it.</summary>
	private void HideTablePile()
	{
		if (_tablePileOverlay is null || !_tablePileOverlay.Visible)
		{
			return;
		}

		_tablePileOverlay.Visible = false;
		if (IsInstanceValid(_tablePilePreviousFocus)
			&& !_tablePilePreviousFocus!.IsQueuedForDeletion()
			&& _tablePilePreviousFocus.IsVisibleInTree())
		{
			_tablePilePreviousFocus.GrabFocus(hideFocus: _tablePilePreviousFocusHidden);
		}

		_tablePilePreviousFocus = null;
	}

	private void BuildTablePileOverlay()
	{
		if (_tablePileOverlay is not null) return;
		CanvasLayer layer = GetModalLayer("PileLayer", 10);
		_tablePileOverlay = layer.GetNodeOrNull<PanelContainer>("PileBrowser");
		if (_tablePileOverlay is null)
		{
			_tablePileOverlay = GD.Load<PackedScene>("res://scenes/components/PileBrowser.tscn").Instantiate<PanelContainer>();
			layer.AddChild(_tablePileOverlay);
		}
		_tablePileOverlay.Visible = false;
		_tablePileTitle = _tablePileOverlay.GetNode<Label>("%Title");
		_tablePileSummary = _tablePileOverlay.GetNode<Label>("%Summary");
		_tablePileScroll = _tablePileOverlay.GetNode<ScrollContainer>("%CardsScroll");
		_tablePileCards = _tablePileOverlay.GetNode<GridContainer>("%Cards");
		_tablePileClose = _tablePileOverlay.GetNode<Button>("%Close");
		_tablePileClose.Pressed += HideTablePile;
	}

	private void RenderTablePileCards(
		IReadOnlyList<CardViewModel> cards,
		int count,
		bool concealed)
	{
		foreach (Node child in _tablePileCards.GetChildren().ToArray())
		{
			_tablePileCards.RemoveChild(child);
			child.QueueFree();
		}

		if (concealed)
		{
			_tablePileCards.AddChild(MakeWrappedLabel(
				"This pile is face-down. Only its public count is available.",
				14,
				Muted));
			_tablePileClose.FocusNext = _tablePileClose.GetPath();
			_tablePileClose.FocusPrevious = _tablePileClose.GetPath();
			return;
		}

		List<Control> focusableCards = new();
		foreach (CardViewModel card in cards)
		{
			Control pileCard = AddTablePileCard(card.IsKnown ? card : null);
			if (card.IsKnown)
			{
				focusableCards.Add(pileCard);
			}
		}

		int hiddenCount = Math.Max(0, count - cards.Count);
		for (int index = 0; index < hiddenCount; index++)
		{
			AddTablePileCard(null);
		}

		if (focusableCards.Count == 0)
		{
			_tablePileCards.AddChild(MakeWrappedLabel(
				count == 0 ? "This pile is empty." : "No public card faces are available.",
				14,
				Muted));
			_tablePileClose.FocusNext = _tablePileClose.GetPath();
			_tablePileClose.FocusPrevious = _tablePileClose.GetPath();
			return;
		}

		_tablePileClose.FocusNext = focusableCards[0].GetPath();
		_tablePileClose.FocusPrevious = focusableCards[^1].GetPath();
		focusableCards[0].FocusPrevious = _tablePileClose.GetPath();
		for (int index = 0; index < focusableCards.Count - 1; index++)
		{
			focusableCards[index].FocusNext = focusableCards[index + 1].GetPath();
			focusableCards[index + 1].FocusPrevious = focusableCards[index].GetPath();
		}
		focusableCards[^1].FocusNext = _tablePileClose.GetPath();
	}

	private TableCardControl AddTablePileCard(CardViewModel? card)
	{
		TableCardControl control = TableCardControl.Create();
		control.FocusMode = card is not null ? FocusModeEnum.All : FocusModeEnum.None;
		_tablePileCards.AddChild(control);
		control.SetCard(card);
		if (card is null)
		{
			control.MouseFilter = MouseFilterEnum.Ignore;
			return control;
		}

		control.CardSelected += _ => InspectTablePileCard(card);
		if (_currentSnapshot is { } snapshot && BoardSpatialActions.Project(snapshot.View).Any(action => action.SourceId == card.Id))
		{
			Button actions = CreateTableActionButton("Card actions…");
			actions.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
			actions.OffsetTop = -34;
			control.AddChild(actions);
			actions.Pressed += () => { HideTablePile(); TrySelectSpatialCard(card.Id); };
		}
		return control;
	}

	private void InspectTablePileCard(CardViewModel card)
	{
		if (!card.IsKnown)
		{
			return;
		}

		_inspectedCard = card;
		ShowInspectedCardZoom();
	}

	private static bool TryResolveTablePile(
		MatchViewModel view,
		string handle,
		out string title,
		out int count,
		out IReadOnlyList<CardViewModel> cards,
		out bool concealed)
	{
		title = string.Empty;
		count = 0;
		cards = Array.Empty<CardViewModel>();
		concealed = false;

		switch (handle)
		{
			case "zone:corp-archives":
				title = "CORP ARCHIVES";
				count = view.CorpDiscardCount;
				cards = view.CorpDiscard;
				break;
			case "zone:runner-heap":
				title = "RUNNER HEAP";
				count = view.RunnerHeap.Count;
				cards = view.RunnerHeap;
				break;
			case "zone:corp-score":
				title = "CORP SCORE";
				count = view.CorpScore.Count;
				cards = view.CorpScore;
				break;
			case "zone:runner-score":
				title = "RUNNER SCORE";
				count = view.RunnerScore.Count;
				cards = view.RunnerScore;
				break;
			case "zone:corp-deck":
				title = "CORP R&D";
				count = view.CorpDeck.Count;
				concealed = true;
				break;
			case "zone:runner-deck":
				title = "RUNNER STACK";
				count = view.RunnerDeck.Count;
				concealed = true;
				break;
			default:
				return false;
		}

		return true;
	}
}
