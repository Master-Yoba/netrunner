using System.Text.RegularExpressions;
using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Card-table composition and issued-action routing; no authoritative state.</summary>
public partial class Main
{
	private bool _useCardTable;
	private GameTableControl _gameTable = null!;
	private MatchHistoryPanel _matchHistory = null!;
	private HFlowContainer _tableActions = null!;
	private Label _tablePrompt = null!;
	private Label _tableLastAction = null!;
	private Button _tableInspect = null!;
	private string? _tableSelectedCard;
	private string? _tableSelectedServer;
	private bool _tableAllActions;
	private readonly Dictionary<ActionId, Button> _tableActionButtons = new();
	private double _tablePlaybackWait;
	private bool _tableAwaitingSettlement;
	private readonly MatchHistoryStore _tableHistory = new();
	private readonly Queue<MatchSnapshot> _tablePublications = new();

	private void BuildCardTable()
	{
		_board = new VBoxContainer { Visible = false, SizeFlagsVertical = SizeFlags.ExpandFill };
		_board.AddThemeConstantOverride("separation", 6);
		_layout.AddChild(_board);
		HBoxContainer toolbar = new();
		_board.AddChild(toolbar);
		Button restart = new() { Text = "New game" };
		restart.Pressed += () => GetTree().ReloadCurrentScene();
		toolbar.AddChild(restart);
		_reducedMotion = new CheckButton { Text = "Reduced motion" };
		toolbar.AddChild(_reducedMotion);
		_reducedMotion.Toggled += reduced => { if (reduced) { _tablePlaybackWait = 0; _gameTable.StopMotion(); } };
		_speed = new HSlider
		{
			MinValue = 0.5,
			MaxValue = 3,
			Step = 0.5,
			Value = 1,
			CustomMinimumSize = new Vector2(90, 24),
			TooltipText = "Playback speed"
		};
		toolbar.AddChild(_speed);
		OptionButton scale = new() { TooltipText = "UI scale" };
		foreach (string label in new[] { "100%", "125%", "150%", "200%" }) scale.AddItem(label);
		scale.ItemSelected += index =>
		{
			float factor = new[] { 1f, 1.25f, 1.5f, 2f }[(int)index];
			Vector2I physical = GetWindow().Size;
			GetWindow().ContentScaleSize = new Vector2I(Math.Max(1280, (int)(physical.X / factor)), Math.Max(720, (int)(physical.Y / factor)));
		};
		toolbar.AddChild(scale);
		_skipAnimations = new Button { Text = "Skip animation" };
		_skipAnimations.Pressed += () => { _tablePlaybackWait = 0; _skipAnimation = true; _gameTable.StopMotion(); };
		toolbar.AddChild(_skipAnimations);
		_tableLastAction = MakeWrappedLabel("Choose your opening hand.", 14, Accent);
		_tableLastAction.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		toolbar.AddChild(_tableLastAction);
		Control tableViewport = new() { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
		_board.AddChild(tableViewport);
		_gameTable = new GameTableControl();
		SetFullRect(_gameTable);
		_gameTable.OffsetRight = -66;
		tableViewport.AddChild(_gameTable);
		_gameTable.CardSelected += SelectTableCard;
		_gameTable.CardInspected += id =>
		{
			if (_gameTable.VisibleCards.TryGetValue(id, out TableCardControl? control) && control.Card is { IsKnown: true } card)
			{
				_inspectedCard = card;
				_tableInspect.Text = $"Inspect {card.Title} ↗";
				_tableInspect.Disabled = false;
			}
			else
			{
				_inspectedCard = null;
				_tableInspect.Text = "Face-down card";
				_tableInspect.Disabled = true;
			}
		};
		_gameTable.ServerSelected += id =>
		{
			if (ShowTablePile(id)) return;
			_tableSelectedServer = id;
			_gameTable.SetSelectedServer(id);
			_tableSelectedCard = null;
			_tableAllActions = false;
			if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
		};
		_matchHistory = new MatchHistoryPanel();
		tableViewport.AddChild(_matchHistory);
		_matchHistory.InspectCard += card => { _inspectedCard = card; ShowInspectedCardZoom(); };
		tableViewport.Resized += PositionHistory;
		_matchHistory.ExpandedChanged += _ => PositionHistory();
		_matchHistory.PinnedChanged += _ => PositionHistory();
		void PositionHistory()
		{
			float width = _matchHistory.Expanded ? 320 : 60;
			_gameTable.OffsetRight = _matchHistory.Expanded && _matchHistory.Pinned && tableViewport.Size.X >= 1800 ? -326 : -66;
			_matchHistory.Position = new Vector2(Math.Max(0, tableViewport.Size.X - width), 0);
			_matchHistory.Size = new Vector2(width, tableViewport.Size.Y);
		}
		PanelContainer dock = MakePanel();
		_board.AddChild(dock);
		VBoxContainer dockContent = new();
		dock.AddChild(dockContent);
		HBoxContainer promptRow = new();
		dockContent.AddChild(promptRow);
		_tablePrompt = MakeWrappedLabel("", 15, Colors.White);
		_tablePrompt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		promptRow.AddChild(_tablePrompt);
		_tableInspect = new Button { Text = "Inspect a card", Disabled = true };
		_tableInspect.Pressed += ShowInspectedCardZoom;
		promptRow.AddChild(_tableInspect);
		Control actionViewport = new() { CustomMinimumSize = new Vector2(0, 84) };
		dockContent.AddChild(actionViewport);
		ScrollContainer scroll = new()
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			FollowFocus = true
		};
		SetFullRect(scroll);
		actionViewport.AddChild(scroll);
		_tableActions = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		scroll.AddChild(_tableActions);
		_focusPresentation = new VBoxContainer();
		dockContent.AddChild(_focusPresentation);
	}

	private void SelectTableCard(string id)
	{
		_tableSelectedCard = id;
		_tableSelectedServer = null;
		_tableAllActions = false;
		_gameTable.SetSelection(id);
		_gameTable.SetSelectedServer(null);
		if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
	}

	private void RenderCardTable(MatchSnapshot snapshot, bool decisionChanged)
	{
		_board.Visible = true;
		_status.Text = snapshot.IsTerminal ? $"{snapshot.View.Outcome.Winner} wins · {snapshot.View.Outcome.Reason}"
			: snapshot.IsOpponentThinking ? "Opponent is thinking" : $"Playing {snapshot.View.Viewer}";
		_phase.Text = $"Turn {snapshot.View.Turn.TurnNumber} · {snapshot.View.Turn.ActiveSide}";
		if (decisionChanged)
		{
			_tableAllActions = false;
			_tableSelectedCard = null;
			_tableSelectedServer = null;
		}
		_gameTable.MotionEnabled = !_reducedMotion.ButtonPressed && !_skipAnimation;
		_gameTable.MotionDuration = 0.32 / _speed.Value;
		_gameTable.Render(snapshot.View);
		_gameTable.SetSelectedServer(_tableSelectedServer);
		_gameTable.SetSelection(_tableSelectedCard);
		if (snapshot.History.Count > 0) _tableLastAction.Text = snapshot.History[^1].Summary;
		if (snapshot.Feedback is { IsError: true } feedback) _tableLastAction.Text = feedback.Message;
		ClearChildren(_focusPresentation);
		if (snapshot.IsTerminal)
		{
			Button again = new() { Text = "New match" };
			again.Pressed += () => GetTree().ReloadCurrentScene();
			_focusPresentation.AddChild(again);
		}
		RenderCardTableActions(snapshot);
	}

	private void RenderCardTableActions(MatchSnapshot snapshot)
	{
		ClearChildren(_tableActions);
		_tableActionButtons.Clear();
		DecisionViewModel? decision = snapshot.View.Decision;
		_tablePrompt.Text = snapshot.IsOpponentThinking ? "Opponent is thinking. You can inspect cards and read history."
			: snapshot.IsTerminal ? "Match complete."
			: decision is null ? "Waiting for the next decision…"
			: $"{decision.OwnerSide}: {TablePrompt(decision)}";
		if (snapshot.View.Run is { } run)
			_tablePrompt.Text = $"Run on {_gameTable.GetServerDisplayName(run.AttackedServerId)} · {run.Step} · " + _tablePrompt.Text;
		_gameTable.SetTargets(new HashSet<string>(StringComparer.Ordinal));
		if (decision is null || snapshot.IsOpponentThinking || snapshot.IsTerminal) return;
		IReadOnlyList<LegalActionViewModel> all = ActionsRegionPresenter.Present(snapshot).DisplayActions;
		_gameTable.SetTargets(all.Select(a => TableActionCardId(a.Parameters))
			.OfType<string>().ToHashSet(StringComparer.Ordinal));
		if (_stagedAction is { } staged && _stagedDecision is { } stagedDecision)
		{
			_tablePrompt.Text = $"Confirm: {TableActionLabel(staged)}";
			AddButton("Confirm", () => ConfirmStagedAction(stagedDecision, staged));
			AddButton("Back", CancelStagedAction);
			return;
		}
		bool ordinary = decision.Kind is DecisionKind.TurnAction or DecisionKind.TurnTiming;
		IEnumerable<LegalActionViewModel> displayed = all;
		if (ordinary && !_tableAllActions)
		{
			displayed = all.Where(a => _tableSelectedCard is not null
				? TableActionCardId(a.Parameters) == _tableSelectedCard
				: _tableSelectedServer is not null
					? ActionsRegionPresenter.GetTargetServerId(a.Parameters) == _tableSelectedServer
					: a.Kind is ActionKind.GainCredit or ActionKind.DrawCard or ActionKind.PassPriority
						or ActionKind.EndTurn or ActionKind.RemoveTag or ActionKind.PurgeVirusCounters);
			AddButton(_tableSelectedCard is null && _tableSelectedServer is null ? "Choose a card / server" : "Clear selection", () =>
			{
				_tableSelectedCard = null; _tableSelectedServer = null;
				_gameTable.SetSelection(null);
				_gameTable.SetSelectedServer(null);
				RenderCardTableActions(snapshot);
			});
			AddButton("All legal actions…", () => { _tableAllActions = true; RenderCardTableActions(snapshot); });
		}
		else if (_tableAllActions)
			AddButton("Back to table", () => { _tableAllActions = false; RenderCardTableActions(snapshot); });
		foreach (LegalActionViewModel action in displayed)
		{
			Button button = AddButton(TableActionLabel(action), () =>
			{
				if (ActionsRegionPresenter.IsTargetedAction(action)) StageTargetAction(decision, action);
				else SubmitAction(decision, action);
			});
			button.TooltipText = action.CostSummary ?? TableActionLabel(action);
			button.Disabled = _tableAwaitingSettlement || Volatile.Read(ref _submissionInFlight) != 0;
			_tableActionButtons[action.Id] = button;
		}
		Button AddButton(string text, Action callback)
		{
			Button button = new() { Text = text, CustomMinimumSize = new Vector2(0, 32), Disabled = _tableAwaitingSettlement };
			button.Pressed += callback;
			_tableActions.AddChild(button);
			return button;
		}
	}

	private static string? TableActionCardId(ActionParameters parameters) => parameters switch
	{
		PlayCardParameters play => play.CardId.Value,
		AbilityActionParameters ability => ability.SourceCardId.Value,
		AdvanceCardParameters advance => advance.CardId.Value,
		ScoreAgendaParameters score => score.CardId.Value,
		_ => ActionsRegionPresenter.GetTargetCardId(parameters),
	};

	private string TablePrompt(DecisionViewModel decision) => decision.Kind switch
	{
		DecisionKind.TurnTiming => decision.OwnerSide == PlayerSide.Corp
			? "Rez, score or use a paid ability — or pass priority."
			: "Use a paid ability — or pass priority.",
		DecisionKind.TurnAction => "Choose a card, a server or a basic action.",
		DecisionKind.Setup => "Keep this hand or take one mulligan.",
		DecisionKind.TurnDiscard when _currentSnapshot?.View.ForcedDiscard is { Hand.MaximumHandSize: { } maximum } discard =>
			$"Discard {Math.Max(0, discard.Hand.Count - maximum)} more · hand {discard.Hand.Count}/{maximum}.",
		DecisionKind.TurnDiscard => "Choose cards to discard down to your hand limit.",
		DecisionKind.MemoryCleanup => "Choose programs to trash to free memory.",
		_ => FriendlyText(decision.Prompt),
	};

	private string TableActionLabel(LegalActionViewModel action) => action.Kind switch
	{
		ActionKind.GainCredit => "Gain 1 credit · 1 click",
		ActionKind.DrawCard => "Draw a card · 1 click",
		ActionKind.PassPriority => "Pass priority",
		ActionKind.EndTurn => "End turn",
		ActionKind.StartRun when action.Parameters is RunServerParameters run => $"Run {_gameTable.GetServerDisplayName(run.ServerId.Value)} · 1 click",
		ActionKind.RezCard when action.Parameters is RezCardParameters rez => $"Rez {FriendlyText(rez.CardId.Value)} · {rez.CreditCost} credits",
		ActionKind.PlayCard when action.Parameters is PlayCardParameters play => $"Play {FriendlyText(play.CardId.Value)} · {play.CreditCost} credits",
		ActionKind.InstallCard when action.Parameters is InstallCardParameters install =>
			$"{(install.FinalizeSelection ? "Install" : "Choose install")} {FriendlyText(install.CardId.Value)} → "
			+ (install.CreateNewRemote ? "New remote" : install.ServerId is { } server ? _gameTable.GetServerDisplayName(server.Value) : "Rig")
			+ $" · {install.CreditCost} credits",
		ActionKind.ChooseNumber when action.Parameters is NumberChoiceParameters number
			&& _currentSnapshot?.View.Decision?.Kind == DecisionKind.Setup => number.Value == 0 ? "Keep hand" : "Mulligan",
		_ => FriendlyText(Slice46PresentationProjector.Label(action)),
	};

	private string FriendlyText(string text)
	{
		foreach ((string id, TableCardControl control) in _gameTable.VisibleCards.OrderByDescending(pair => pair.Key.Length))
			text = text.Replace(id, control.Card?.Title ?? "face-down card", StringComparison.Ordinal);
		text = Regex.Replace(text, @"server:\d+", match => _gameTable.GetServerDisplayName(match.Value));
		text = Regex.Replace(text, @"card:\d+", "face-down card");
		return text.Replace("Core", "the game", StringComparison.Ordinal);
	}

	private void PollCardTable(double delta)
	{
		_tablePlaybackWait = Math.Max(0, _tablePlaybackWait - delta);
		if (_tableAwaitingSettlement && _tablePlaybackWait == 0 && _gameTable.ActiveMotionCount == 0 && _tablePublications.Count == 0)
		{
			_tableAwaitingSettlement = false;
			if (_currentSnapshot is { } settled) RenderCardTableActions(settled);
		}
		while (_pendingSnapshots.TryDequeue(out MatchSnapshot? snapshot))
		{
			_tableHistory.Append(snapshot.History);
			_matchHistory.Append(snapshot.History);
			_tablePublications.Enqueue(snapshot);
		}
		if (_tablePublications.Count > 24) _skipAnimation = true;
		if (_tablePlaybackWait > 0 && !_skipAnimation) return;
		while (_tablePublications.TryDequeue(out MatchSnapshot? next))
		{
			if (next.Sequence <= _renderedSequence) continue;
			_renderedSequence = next.Sequence;
			RenderSnapshot(next);
			bool meaningful = next.History.Any(entry => !entry.Summary.Contains("passes priority", StringComparison.Ordinal));
			if (meaningful && !_skipAnimation && !_reducedMotion.ButtonPressed)
			{
				_tablePlaybackWait = 0.40 / _speed.Value;
				_tableAwaitingSettlement = true;
				foreach (Button actionButton in _tableActions.GetChildren().OfType<Button>()) actionButton.Disabled = true;
				break;
			}
		}
		if (_tablePublications.Count == 0) _skipAnimation = false;
	}
}
