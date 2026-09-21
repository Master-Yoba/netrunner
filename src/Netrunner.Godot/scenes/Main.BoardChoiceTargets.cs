using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private void ClearBoardServerCues()
	{
		foreach (PanelContainer cue in _boardChoiceServerCues)
		{
			cue.GetParent()?.RemoveChild(cue);
			cue.QueueFree();
		}
		_boardChoiceServerCues.Clear();
	}

	private void RenderBoardServerCues(IReadOnlyList<LegalActionViewModel> actions)
	{
		ClearBoardServerCues();
		foreach (string server in actions.Select(BoardServerTargetId).OfType<string>().Distinct(StringComparer.Ordinal))
		{
			LegalActionViewModel[] choices = actions.Where(action => BoardServerTargetId(action) == server).ToArray();
			string caption = choices.Length == 1 ? BoardChoiceActionLabel(choices[0]) : "Choose an option for " + _gameTable.GetServerDisplayName(server);
			AddBoardChoiceCue("ServerTarget", server, caption, () => TrySelectBoardChoiceServer(server));
		}
		foreach (string card in actions.SelectMany(a => BoardCardTargetIds(a, actions)).Distinct(StringComparer.Ordinal))
			if (!_gameTable.VisibleCards.ContainsKey(card))
				AddBoardChoiceCue("HostCard", card, "Choose face-down ICE", () => TrySelectBoardChoiceCard(card));
	}

	private void AddBoardChoiceCue(string kind, string target, string caption, Action choose)
	{
		Rect2? rect = _gameTable.GetDropRect(kind, target);
		PanelContainer cue = GD.Load<PackedScene>("res://scenes/components/DropDestination.tscn").Instantiate<PanelContainer>();
		cue.MouseFilter = MouseFilterEnum.Stop;
		cue.ZIndex = 10;
		cue.SetMeta("target_id", target);
		cue.SetMeta("target_kind", kind);
		_tableViewport.AddChild(cue);
		cue.Visible = rect is not null;
		if (rect is { } area)
		{
			cue.GlobalPosition = area.Position;
			cue.Size = area.Size;
		}
		Label label = cue.GetNode<Label>("%Caption");
		label.Text = caption;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
		label.SizeFlagsVertical = SizeFlags.ShrinkEnd;
		label.AddThemeStyleboxOverride("normal", new StyleBoxFlat
		{
			BgColor = new Color(0.018f, 0.035f, 0.05f, 0.98f),
			ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
		});
		cue.GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				cue.AcceptEvent();
				choose();
			}
		};
		_boardChoiceServerCues.Add(cue);
	}

	private void UpdateBoardChoiceGeometry()
	{
		RefreshBoardChoiceAvailability();
		foreach (PanelContainer cue in _boardChoiceServerCues)
		{
			Rect2? rect = _gameTable.GetDropRect(cue.GetMeta("target_kind").AsString(), cue.GetMeta("target_id").AsString());
			cue.Visible = _boardChoiceInputEnabled == true && rect is { } area && area.HasArea();
			if (rect is not { } visible) continue;
			cue.GlobalPosition = visible.Position;
			cue.Size = visible.Size;
		}
	}

	private void RenderBoardChoiceCards(IReadOnlyList<LegalActionViewModel> actions)
	{
		if (_currentSnapshot?.View is not { } view || _boardChoiceOptions is null) return;
		HashSet<string> ids = actions.SelectMany(a => BoardCardTargetIds(a, actions)).ToHashSet(StringComparer.Ordinal);
		CardViewModel[] cards = BoardChoiceCards(view).Where(c => ids.Contains(c.Id)
			&& (!_gameTable.VisibleCards.TryGetValue(c.Id, out TableCardControl? tableCard)
				|| tableCard.MouseFilter == MouseFilterEnum.Ignore)).ToArray();
		if (cards.Length == 0) return;
		_boardChoicePanel!.GetNode<ScrollContainer>("Content/OptionsScroll").CustomMinimumSize = new Vector2(0, 220);
		HFlowContainer browser = GD.Load<PackedScene>("res://scenes/components/BoardChoiceCards.tscn").Instantiate<HFlowContainer>();
		_boardChoiceOptions.AddChild(browser);
		foreach (CardViewModel model in cards)
		{
			TableCardControl card = TableCardControl.Create();
			card.SetCard(model);
			card.SetDisplaySize(new Vector2(105, 147));
			card.SetTargetCandidate(true);
			card.CardSelected += id => TrySelectBoardChoiceCard(id);
			card.CardHoverChanged += HandleTableCardHoverChanged;
			browser.AddChild(card);
		}
	}
}
