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
		if (!_tablePileOverlay!.Visible)
		{
			_tablePilePreviousFocus = GetViewport().GuiGetFocusOwner();
		}

		_tablePileTitle.Text = title;
		_tablePileSummary.Text = concealed
			? $"{count} card(s) · count only · cards remain concealed"
			: $"{count} card(s) · public faces shown where available · select a known card to inspect";
		RenderTablePileCards(cards, count, concealed);
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
			_tablePilePreviousFocus.GrabFocus();
		}

		_tablePilePreviousFocus = null;
	}

	/// <summary>Closes the pile before Main's generic Escape handler can collapse history.</summary>
	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (_tablePileOverlay?.Visible != true
			|| _zoomOverlay.Visible
			|| @event is not InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			return;
		}

		HideTablePile();
		GetViewport().SetInputAsHandled();
	}

	private void BuildTablePileOverlay()
	{
		if (_tablePileOverlay is not null)
		{
			return;
		}

		_tablePileOverlay = new PanelContainer
		{
			Visible = false,
			MouseFilter = MouseFilterEnum.Stop,
		};
		_tablePileOverlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.018f, 0.035f, 0.05f, 0.98f),
			BorderColor = Accent,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			ContentMarginLeft = 18,
			ContentMarginTop = 16,
			ContentMarginRight = 18,
			ContentMarginBottom = 16,
		});
		SetFullRect(_tablePileOverlay);
		AddChild(_tablePileOverlay);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		_tablePileOverlay.AddChild(margin);

		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 8);
		margin.AddChild(content);

		_tablePileTitle = MakeLabel("PILE", 20, Accent);
		_tablePileTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_tablePileTitle);
		_tablePileSummary = MakeWrappedLabel(string.Empty, 12, Muted);
		content.AddChild(_tablePileSummary);

		_tablePileScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			FollowFocus = true,
			ClipContents = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			FocusMode = FocusModeEnum.None,
		};
		_tablePileCards = new GridContainer
		{
			Columns = 4,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_tablePileCards.AddThemeConstantOverride("h_separation", 10);
		_tablePileCards.AddThemeConstantOverride("v_separation", 10);
		_tablePileScroll.AddChild(_tablePileCards);
		content.AddChild(_tablePileScroll);

		_tablePileClose = new Button
		{
			Text = "Close pile",
			CustomMinimumSize = new Vector2(180, 40),
			FocusMode = FocusModeEnum.All,
			TooltipText = "Close the pile browser and return to the table",
		};
		_tablePileClose.Pressed += HideTablePile;
		content.AddChild(_tablePileClose);
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
		TableCardControl control = new()
		{
			FocusMode = card is not null ? FocusModeEnum.All : FocusModeEnum.None,
		};
		_tablePileCards.AddChild(control);
		control.SetDisplaySize(TableCardControl.PortraitCardSize);
		control.SetCard(card);
		if (card is null)
		{
			control.MouseFilter = MouseFilterEnum.Ignore;
			return control;
		}

		control.CardSelected += _ => InspectTablePileCard(card);
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
				count = view.CorpDiscard.Count;
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
