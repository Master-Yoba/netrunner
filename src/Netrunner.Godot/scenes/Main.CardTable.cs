using System.Text.RegularExpressions;
using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Card-table composition and issued-action routing; no authoritative state.</summary>
public partial class Main
{
	// Keep the managed scene wrapper alive across button rebuilds and scene
	// reloads; repeated Load calls can race Godot's weak resource-handle cleanup.
	private static PackedScene? _tableActionButtonScene;
	private bool _useCardTable;
	private GameTableControl _gameTable = null!;
	private Control _tableViewport = null!;
	private MatchHistoryPanel _matchHistory = null!;
	private HFlowContainer _tableActions = null!;
	private Label _tablePrompt = null!;
	private Label _tableLastAction = null!;
	private Button _tableInspect = null!;
	private PanelContainer _tableAccessPanel = null!;
	private TableCardControl _tableAccessCard = null!;
	private Label _tableAccessTitle = null!;
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
		_board = _layout.GetNode<VBoxContainer>("MatchTable");
		Control toolbar = _board.GetNode<Control>("Toolbar");
		toolbar.GetNode<Button>("NewGame").Pressed += () => GetTree().ReloadCurrentScene();
		_reducedMotion = toolbar.GetNode<CheckButton>("ReducedMotion");
		_reducedMotion.Toggled += reduced => { if (reduced) { _tablePlaybackWait = 0; _gameTable.StopMotion(); } };
		_speed = toolbar.GetNode<HSlider>("Speed");
		OptionButton scale = toolbar.GetNode<OptionButton>("Scale");
		scale.ItemSelected += index =>
		{
			float factor = new[] { 1f, 1.25f, 1.5f, 2f }[(int)index];
			Vector2I physical = GetWindow().Size;
			GetWindow().ContentScaleSize = new Vector2I(Math.Max(1920, (int)(physical.X / factor)), Math.Max(1080, (int)(physical.Y / factor)));
		};
		_skipAnimations = toolbar.GetNode<Button>("SkipAnimation");
		_skipAnimations.Pressed += () => { _tablePlaybackWait = 0; _skipAnimation = true; _gameTable.StopMotion(); };
		_tableLastAction = toolbar.GetNode<Label>("LastAction");
		_tableViewport = _board.GetNode<Control>("TableViewport");
		_tableViewport.Resized += HandleCardHoverPreviewViewportResized;
		_gameTable = _tableViewport.GetNode<GameTableControl>("GameBoard");
		_gameTable.UseCardHoverPreview = true;
		_gameTable.CardSelected += SelectTableCard;
		_gameTable.BasicActionRequested += SubmitBasicBoardAction;
		_gameTable.PileRequested += id => ShowTablePile(id);
		_gameTable.CardHoverChanged += HandleTableCardHoverChanged;
		_gameTable.CardInspected += id =>
		{
			RecordFullMatchSelection("card-inspect", id);
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
			if (TrySelectSpatialServer(id)) return;
			CloseBoardIcebreaker();
			if (TrySelectBoardChoiceServer(id)) return;
			if (_currentSnapshot?.View.Servers.Any(server => server.Id == id && server.Kind == ServerKind.Archives) == true
				&& ShowTablePile("zone:corp-archives")) return;
			RecordFullMatchSelection("server", id);
			if (ShowTablePile(id)) return;
			_tableSelectedServer = id;
			_gameTable.SetSelectedServer(id);
			_tableSelectedCard = null;
			_tableAllActions = false;
			if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
		};
		_tableAccessPanel = _tableViewport.GetNode<PanelContainer>("AccessPanel");
		_tableAccessCard = _tableAccessPanel.GetNode<TableCardControl>("%Card");
		_tableAccessCard.UseHoverPreview = true;
		_tableAccessTitle = _tableAccessPanel.GetNode<Label>("%Title");
		Button inspectAccess = _tableAccessPanel.GetNode<Button>("%Inspect");
		inspectAccess.Pressed += () =>
		{
			if (_tableAccessCard.Card is { IsKnown: true } card)
			{
				_inspectedCard = card;
				ShowInspectedCardZoom();
			}
		};
		_tableAccessCard.CardSelected += id => { if (!TrySelectSpatialCard(id)) inspectAccess.EmitSignal(BaseButton.SignalName.Pressed); };
		_tableAccessCard.CardHoverChanged += HandleTableCardHoverChanged;
		BuildCardHoverPreview(_tableViewport);
		_matchHistory = _tableViewport.GetNode<MatchHistoryPanel>("History");
		_matchHistory.InspectCard += card => { _inspectedCard = card; ShowInspectedCardZoom(); };
		Control dock = _board.GetNode<Control>("TableViewport/DecisionDock");
		_tablePrompt = dock.GetNode<Label>("%Prompt");
		_tableInspect = dock.GetNode<Button>("%Inspect");
		_tableInspect.Pressed += ShowInspectedCardZoom;
		_tableActions = dock.GetNode<HFlowContainer>("%Actions");
		_focusPresentation = dock.GetNode<VBoxContainer>("%FocusPresentation");
		BuildBoardDrag();
	}

	private void SelectTableCard(string id)
	{
		if (_boardChoiceVisible) { TrySelectBoardChoiceCard(id); return; }
		if (TrySelectSpatialCard(id)) return;
		if (_boardChoosingSubroutines && id == _currentSnapshot?.View.Run?.Encounter?.Ice?.Id) return;
		if (TrySelectBoardIcebreaker(id)) return;
		CloseBoardIcebreaker();
		RecordFullMatchSelection("card", id);
		_tableSelectedCard = id;
		_tableSelectedServer = null;
		_tableAllActions = false;
		_gameTable.SetSelection(id);
		_gameTable.SetSelectedServer(null);
		if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
	}

	private void ClearTableSelection()
	{
		CloseSpatialActions();
		CloseBoardIcebreaker();
		_tableSelectedCard = null;
		_tableSelectedServer = null;
		_tableAllActions = false;
		_gameTable.SetSelection(null);
		_gameTable.SetSelectedServer(null);
		CancelStagedAction();
		HideCardHoverPreview();
	}

	private void RenderCardTable(MatchSnapshot snapshot, bool decisionChanged)
	{
		BeginCardHoverPreviewSnapshot(snapshot);
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
		_gameTable.Hand.ReducedMotion = _reducedMotion.ButtonPressed;
		_gameTable.MotionEnabled = !_reducedMotion.ButtonPressed && !_skipAnimation;
		_gameTable.MotionDuration = GameTableControl.DefaultMotionDuration / _speed.Value;
		_gameTable.Render(snapshot.View);
		CardViewModel? accessed = snapshot.View.Run is { Step: RunTimingStep.BreachAccessAbility, Access.Accessed.Count: > 0 } accessRun
			? accessRun.Access.Accessed[^1] : null;
		_tableAccessCard.SetCard(accessed);
		_tableAccessPanel.Visible = accessed is { IsKnown: true };
		_tableAccessTitle.Text = accessed?.Title ?? "";
		_gameTable.SetSelectedServer(_tableSelectedServer);
		_gameTable.SetSelection(_tableSelectedCard);
		if (snapshot.History.Count > 0) _tableLastAction.Text = snapshot.History[^1].Summary;
		if (snapshot.Feedback is { IsError: true } feedback) _tableLastAction.Text = feedback.Message;
		ClearChildren(_focusPresentation);
		if (snapshot.IsTerminal)
		{
			Control outcome = GD.Load<PackedScene>("res://scenes/components/OutcomePanel.tscn").Instantiate<Control>();
			outcome.GetNode<Label>("Summary").Text = $"{snapshot.View.Outcome.Winner} wins · {snapshot.View.Outcome.Reason}";
			outcome.GetNode<Button>("NewMatch").Pressed += () => GetTree().ReloadCurrentScene();
			_focusPresentation.AddChild(outcome);
		}
		RenderCardTableActions(snapshot);
	}

	private void RenderCardTableActions(MatchSnapshot snapshot)
	{
		RefreshDragPlay(snapshot);
		try
		{
			ClearChildren(_tableActions);
			_tableActionButtons.Clear();
			DecisionViewModel? decision = snapshot.View.Decision;
			_tablePrompt.Text = snapshot.IsOpponentThinking ? "Opponent is thinking. You can inspect cards and read history."
				: snapshot.IsTerminal ? "Match complete."
				: _fullMatchResumePaused ? $"Recovered at action {snapshot.ActionCount}. Continue when ready."
				: decision is null ? "Waiting for the next decision…"
				: $"{decision.OwnerSide}: {TablePrompt(decision)}";
			if (snapshot.View.Run is { } run)
				_tablePrompt.Text = $"Run on {_gameTable.GetServerDisplayName(run.AttackedServerId)} · {run.Step} · " + _tablePrompt.Text;
			_gameTable.SetTargets(new HashSet<string>(StringComparer.Ordinal));
			_gameTable.SetActionableSources(new HashSet<string>(StringComparer.Ordinal));
			if (_fullMatchResumePaused && !snapshot.IsTerminal)
			{
				Button continueButton = CreateTableActionButton("Continue recovered match");
				continueButton.Disabled = _fullMatchResumeContinueInFlight;
				continueButton.Pressed += ContinueRecoveredFullMatch;
				_tableActions.AddChild(continueButton);
				return;
			}
			if (decision is null || snapshot.IsOpponentThinking || snapshot.IsTerminal)
			{
				return;
			}
			IReadOnlyList<LegalActionViewModel> all = ActionsRegionPresenter.Present(snapshot).Actions;
			if (decision.OwnerSide == snapshot.View.Viewer
				&& decision.Kind != DecisionKind.Setup
				&& !_tableAwaitingSettlement
				&& Volatile.Read(ref _submissionInFlight) == 0)
			{
				_gameTable.SetActionableSources(all
					.SelectMany(TableActionSourceCardIds)
					.Concat(BoardSpatialActions.Project(snapshot.View).Select(action => action.SourceId).OfType<string>())
					.Concat(decision.Kind == DecisionKind.TurnDiscard
						? BoardInteractionProjector.Project(snapshot.View).Sources.Select(source => source.CardId) : [])
					.ToHashSet(StringComparer.Ordinal));
				_gameTable.SetTargets(all
					.Where(_ => decision.Kind != DecisionKind.TurnDiscard)
					.SelectMany(TableActionTargetCardIds)
					.ToHashSet(StringComparer.Ordinal));
			}
			if (_stagedAction is { } staged && _stagedDecision is { } stagedDecision)
			{
				_tablePrompt.Text = $"Confirm: {TableActionLabel(staged)}";
				AddButton("Confirm", () => ConfirmStagedAction(stagedDecision, staged));
				AddButton("Back", CancelStagedAction);
				return;
			}
			bool ordinary = decision.Kind is DecisionKind.TurnAction or DecisionKind.TurnTiming;
			bool encounter = decision.Kind == DecisionKind.RunTiming && snapshot.View.Viewer == PlayerSide.Runner
				&& snapshot.View.Run is { Step: RunTimingStep.EncounterPaidWindow, Encounter: not null };
			IEnumerable<LegalActionViewModel> displayed = all;
			if (encounter && !_tableAllActions)
			{
				displayed = all.Where(action => BoardIcebreakerPresenter.Source(action) is null);
				_tablePrompt.Text += " · Click a matching icebreaker to boost or break";
				AddButton("All legal actions…", () => { _tableAllActions = true; RenderCardTableActions(snapshot); });
			}
			else if (decision.Kind == DecisionKind.TurnDiscard && !_tableAllActions)
			{
				displayed = [];
				AddButton("All legal actions…", () => { _tableAllActions = true; RenderCardTableActions(snapshot); });
			}
			else if (ordinary && !_tableAllActions)
			{
				if (snapshot.View.Viewer == PlayerSide.Runner && all.Any(action => action.Kind == ActionKind.StartRun))
					AddButton("Run", StartBoardRunSelection);
				displayed = all.Where(a => _tableSelectedCard is not null
					? TableActionCardId(a.Parameters) == _tableSelectedCard
					: _tableSelectedServer is not null
						? TableActionServerId(a.Parameters) == _tableSelectedServer
						: a.Kind is ActionKind.PassPriority
							or ActionKind.EndTurn or ActionKind.RemoveTag or ActionKind.PurgeVirusCounters);
				if (_tableSelectedCard is not null || _tableSelectedServer is not null)
					_tablePrompt.Text += " · Esc clears selection";
				AddButton("All legal actions…", () => { _tableAllActions = true; RenderCardTableActions(snapshot); });
			}
			else if (_tableAllActions)
				AddButton("Back to table", () => { _tableAllActions = false; RenderCardTableActions(snapshot); });
			if (!_tableAllActions)
			{
				IReadOnlyList<BoardSpatialAction> spatial = BoardSpatialActions.Project(snapshot.View);
				foreach (string source in spatial.Select(action => action.SourceId).OfType<string>().Distinct(StringComparer.Ordinal))
					if (_gameTable.GetDropRect("HostCard", source) is null && _tableAccessCard.CardId != source)
						AddButton(ActionCardName(source) + " · card actions…", () => TrySelectSpatialCard(source));
				if (spatial.Count > 0 && !ordinary && !encounter && decision.Kind != DecisionKind.TurnDiscard)
					AddButton("All legal actions…", () => { _tableAllActions = true; RenderCardTableActions(snapshot); });
				HashSet<ActionId> spatialIds = spatial.Select(action => action.Action.Id).ToHashSet();
				displayed = displayed.Where(action => !spatialIds.Contains(action.Id));
			}
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
				Button button = CreateTableActionButton(text);
				button.Disabled = _tableAwaitingSettlement;
				button.Pressed += callback;
				_tableActions.AddChild(button);
				return button;
			}
		}
		finally
		{
			RefreshBoardChoices(snapshot);
			RefreshSpatialActions(snapshot);
			RefreshBasicBoardActions(snapshot);
			RefreshBoardIcebreaker(snapshot);
		}
	}

	private static Button CreateTableActionButton(string text)
	{
		_tableActionButtonScene ??= GD.Load<PackedScene>("res://scenes/components/ActionButton.tscn");
		Button button = _tableActionButtonScene.Instantiate<Button>();
		button.Text = text;
		return button;
	}

	private static string? TableActionServerId(ActionParameters parameters) => parameters switch
	{
		RunServerParameters run => run.ServerId.Value,
		InstallCardParameters install => install.ServerId?.Value,
		PlayCardParameters play => play.TargetServerId?.Value,
		_ => ActionsRegionPresenter.GetTargetServerId(parameters),
	};

	private static string? TableActionCardId(ActionParameters parameters) => parameters switch
	{
		PlayCardParameters play => play.CardId.Value,
		AbilityActionParameters ability => ability.SourceCardId.Value,
		AdvanceCardParameters advance => advance.CardId.Value,
		ScoreAgendaParameters score => score.CardId.Value,
		_ => ActionsRegionPresenter.GetTargetCardId(parameters),
	};

	private static IEnumerable<string> TableActionSourceCardIds(LegalActionViewModel action)
	{
		ArgumentNullException.ThrowIfNull(action);
		return action.Parameters switch
		{
			PlayCardParameters play => [play.CardId.Value],
			InstallCardParameters install => [install.CardId.Value],
			AbilityActionParameters ability => [ability.SourceCardId.Value],
			RezCardParameters rez => [rez.CardId.Value],
			AdvanceCardParameters advance => [advance.CardId.Value],
			ScoreAgendaParameters score => [score.CardId.Value],
			RunIceParameters runIce => [runIce.IceId.Value],
			RunBreakerParameters breaker => [breaker.BreakerId.Value],
			RunBreakParameters breakAction => [breakAction.BreakerId.Value],
			RunBioroidAbilityParameters bioroid => [bioroid.IceId.Value],
			IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.InstallCard, CardId: { } cardId }
				=> [cardId.Value],
			_ => action.Source is { } source ? [source.CardId] : [],
		};
	}

	private static IEnumerable<string> TableActionTargetCardIds(LegalActionViewModel action)
	{
		ArgumentNullException.ThrowIfNull(action);
		return action.Parameters switch
		{
			CardChoiceParameters choice => [choice.CardId.Value],
			TrashTaggedResourceParameters tagged => [tagged.CardId.Value],
			RunCardPaymentParameters payment => [payment.CardId.Value],
			BreachCandidateParameters { CardId: { } cardId } => [cardId.Value],
			BehaviorChoiceParameters behavior => behavior.CardIds.Select(card => card.Value),
			AbilityActionParameters ability => ability.TargetCardIds.Select(card => card.Value),
			InstallCardParameters install => install.CandidateReplacementCardIds.Select(card => card.Value),
			IceSubroutineChoiceParameters
			{
				Choice: IceSubroutineChoiceKind.TrashInstalledRunnerCard,
				CardId: { } cardId,
			}
				=> [cardId.Value],
			_ => [],
		};
	}

	private string TablePrompt(DecisionViewModel decision) => decision.Kind switch
	{
		DecisionKind.TurnTiming => decision.OwnerSide == PlayerSide.Corp
			? "Rez, score or use a paid ability — or pass priority."
			: "Use a paid ability — or pass priority.",
		DecisionKind.TurnAction => "Choose a card, a server or a basic action.",
		DecisionKind.Setup => "Keep this hand or take one mulligan.",
		DecisionKind.TurnDiscard => HandDiscardPrompt(decision),
		DecisionKind.MemoryCleanup => "Choose programs to trash to free memory.",
		_ => FriendlyText(decision.Prompt),
	};

	private string HandDiscardPrompt(DecisionViewModel decision)
	{
		string pile = decision.OwnerSide == PlayerSide.Corp ? "Archives" : "Heap";
		if (_currentSnapshot?.View is not { } view) return $"Drag a highlighted hand card to {pile}.";
		int selected = BoardInteractionProjector.SelectedDiscards(view).Count;
		string progress = selected > 0 ? $"{selected} selected. " : string.Empty;
		if (view.ForcedDiscard?.Hand is not { MaximumHandSize: { } maximum } hand)
			return $"{progress}Drag a highlighted hand card to {pile}. {decision.Prompt}";
		int remaining = Math.Max(0, hand.Count - maximum - selected);
		return $"{progress}Drag {remaining}{(selected > 0 ? " more" : "")} {(remaining == 1 ? "card" : "cards")} from hand to {pile}.";
	}

	private string FriendlyText(string text)
	{
		if (_currentSnapshot?.View is { } view)
			foreach (CardViewModel card in BoardChoiceCards(view).OrderByDescending(card => card.Id.Length))
				text = text.Replace(card.Id, ActionCardName(card.Id), StringComparison.Ordinal);
		if (_tableAccessCard.Card is { IsKnown: true } accessed)
			text = text.Replace(accessed.Id, accessed.Title, StringComparison.Ordinal);
		foreach ((string id, TableCardControl control) in _gameTable.VisibleCards.OrderByDescending(pair => pair.Key.Length))
			text = text.Replace(id, control.Card?.Title ?? "face-down card", StringComparison.Ordinal);
		text = Regex.Replace(text, @"server:\d+", match => _gameTable.GetServerDisplayName(match.Value));
		text = Regex.Replace(text, @"card:\d+", "face-down card");
		return text.Replace("Core", "the game", StringComparison.Ordinal);
	}

	private void PollCardTable(double delta)
	{
		PollCardHoverPreview();
		_gameTable.Hand.ReducedMotion = _reducedMotion.ButtonPressed;
		if (_currentSnapshot is { } current)
		{
			RefreshBasicBoardActions(current);
			RefreshBoardIcebreaker(current);
		}
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
		if (!_skipAnimation && (_tablePlaybackWait > 0 || _gameTable.DrawMotionCount > 0 || _gameTable.DiscardMotionCount > 0)) return;
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
			if (!_skipAnimation && _gameTable.DiscardMotionCount > 0) break;
		}
		if (_tablePublications.Count == 0) _skipAnimation = false;
	}
}
