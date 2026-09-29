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
	private BeatPresenter _beats = null!;

	private void BuildCardTable()
	{
		_board = _layout.GetNode<VBoxContainer>("MatchTable");
		_tableLastAction = _board.GetNode<Label>("StatusBar/LastAction");
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
		// Above card frames, highlights and pulses (at most 30), below card flights (70).
		_tableAccessPanel.ZIndex = 60;
		_tableAccessCard = _tableAccessPanel.GetNode<TableCardControl>("%Card");
		_tableAccessCard.UseHoverPreview = true;
		_tableAccessTitle = _tableAccessPanel.GetNode<Label>("%Title");
		Button inspectAccess = _tableAccessPanel.GetNode<Button>("%Inspect");
		// Playtest, 29 September: clicking the accessed card already opens it, so the button only took room.
		inspectAccess.Visible = false;
		inspectAccess.Pressed += () =>
		{
			if (_tableAccessCard.Card is { IsKnown: true } card)
			{
				_inspectedCard = card;
				ShowInspectedCardZoom();
			}
		};
		_tableAccessCard.CardSelected += id =>
		{
			if (_spatialSelection is not null && TrySelectSpatialCard(id)) return;
			inspectAccess.EmitSignal(BaseButton.SignalName.Pressed);
		};
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
		_beats = new BeatPresenter { Name = "BeatPresenter", Board = _gameTable };
		_tableViewport.AddChild(_beats);
		BuildBoardDrag();
		BuildSettingsMenu();
		BuildOnboarding();
		BuildSound();
		BuildTilt();
		BuildAutoBenefits();
		BuildActionChips();
		BuildEncounterOverlay();
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
		if (_currentSnapshot is not { } snapshot) return;
		RenderCardTableActions(snapshot);
		// A hand card with several install destinations lights them on the board, as
		// a drag does, instead of listing them over the hand (playtest, 27 September).
		// Events with several targets keep their labelled buttons in the dock.
		if (snapshot.View.Decision is { } decision && decision.OwnerSide == snapshot.View.Viewer && InViewerHand(snapshot.View, id))
		{
			LegalActionViewModel[] candidates = snapshot.View.LegalActions
				.Where(action => TableActionCardId(action.Parameters) == id).ToArray();
			if (candidates.Length > 1 && candidates.All(action => action.Kind == ActionKind.InstallCard && IsBoardPlayInstallCandidate(action)))
				StageBoardPlayChoices(decision, candidates);
		}
	}

	private static bool InViewerHand(MatchViewModel view, string? cardId) => cardId is not null
		&& (view.Viewer == PlayerSide.Corp ? view.CorpHand : view.RunnerHand).VisibleCards.Any(card => card.Id == cardId);

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
		_gameTable.MotionDuration = (snapshot.History.Any(entry => entry.Side != snapshot.View.Viewer)
			? 0.50 : GameTableControl.DefaultMotionDuration) / _speed.Value;
		_gameTable.Render(snapshot.View);
		CardViewModel? accessed = AccessActionsPresenter.Card(snapshot.View);
		_tableAccessCard.SetCard(accessed);
		_tableAccessPanel.Visible = accessed is { IsKnown: true } && _accessAllRunId is null;
		_tableAccessTitle.Text = AccessTitle(accessed, snapshot.View);
		RefreshRunOverlays(snapshot);
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
			outcome.GetNode<Button>("Resume").Pressed += ShowSaveBrowser;
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
			ClearActionChips();
			_tableActionButtons.Clear();
			RefreshAccessActions(snapshot);
			DecisionViewModel? decision = snapshot.View.Decision;
			_tablePrompt.Text = snapshot.IsOpponentThinking ? "Opponent is thinking. You can inspect cards and read history."
				: snapshot.IsTerminal ? "Match complete."
				: _fullMatchResumePaused ? $"Paused at action {snapshot.ActionCount}. Continue when ready."
				: decision is null ? "Waiting for the next decision…"
				: $"{decision.OwnerSide}: {TablePrompt(decision, snapshot.View)}";
			_tablePrompt.TooltipText = decision is null ? string.Empty : $"Engine prompt: {decision.Prompt}";
			if (snapshot.View.Run is { } run)
				_tablePrompt.Text = DecisionPromptPresenter.RunContext(run, _gameTable.GetServerDisplayName) + " · " + _tablePrompt.Text;
			if (RenderAccessAllProgress()) return;
			_gameTable.SetTargets(new HashSet<string>(StringComparer.Ordinal));
			_gameTable.SetActionableSources(new HashSet<string>(StringComparer.Ordinal));
			if (_fullMatchResumePaused && !snapshot.IsTerminal)
			{
				Button continueButton = CreateTableActionButton("Continue match");
				continueButton.Disabled = _fullMatchResumeContinueInFlight || SessionModalVisible || _manualSaveInFlight;
				continueButton.Pressed += ContinueRecoveredFullMatch;
				_tableActions.AddChild(continueButton);
				return;
			}
			if (SessionModalVisible || snapshot.IsPaused || decision is null || snapshot.IsOpponentThinking || snapshot.IsTerminal)
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
			// A selected card's or server's own actions go beside it (U4).
			bool contextual = false;
			bool encounter = decision.Kind == DecisionKind.RunTiming && snapshot.View.Viewer == PlayerSide.Runner
				&& snapshot.View.Run is { Step: RunTimingStep.EncounterPaidWindow, Encounter: not null };
			IEnumerable<LegalActionViewModel> displayed = all;
			if (encounter && !_tableAllActions)
			{
				displayed = all.Where(action => BoardIcebreakerPresenter.Source(action) is null);
				_tablePrompt.Text += all.Any(action => action.Parameters is RunBreakParameters { SubroutineId: not null })
					? " · Click an available icebreaker or hosted card"
					: " · Click a matching icebreaker to boost or break";
				AddButton("All legal actions…", () => { _tableAllActions = true; RecordFullMatchSelection("ui-control", "all-legal-actions"); RenderCardTableActions(snapshot); });
			}
			else if (decision.Kind == DecisionKind.TurnDiscard && !_tableAllActions)
			{
				displayed = [];
				AddButton("All legal actions…", () => { _tableAllActions = true; RecordFullMatchSelection("ui-control", "all-legal-actions"); RenderCardTableActions(snapshot); });
			}
			else if (ordinary && !_tableAllActions)
			{
				if (snapshot.View.Viewer == PlayerSide.Runner && all.Any(action => action.Kind == ActionKind.StartRun))
					AddButton("Run", StartBoardRunSelection);
				displayed = all.Where(a => _tableSelectedCard is not null
					? TableActionCardId(a.Parameters) == _tableSelectedCard
					: _tableSelectedServer is not null
						? TableActionServerId(a.Parameters) == _tableSelectedServer
						: a.Kind is ActionKind.PassPriority or ActionKind.ScoreAgenda
							or ActionKind.EndTurn or ActionKind.RemoveTag or ActionKind.PurgeVirusCounters);
				// Hand cards keep their actions in the dock, so nothing covers the hand.
				contextual = _tableSelectedCard is not null && !InViewerHand(snapshot.View, _tableSelectedCard)
					|| _tableSelectedServer is not null;
				if (_tableSelectedCard is not null || _tableSelectedServer is not null)
					_tablePrompt.Text += " · Esc clears selection";
				AddButton("All legal actions…", () => { _tableAllActions = true; RecordFullMatchSelection("ui-control", "all-legal-actions"); RenderCardTableActions(snapshot); });
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
					AddButton("All legal actions…", () => { _tableAllActions = true; RecordFullMatchSelection("ui-control", "all-legal-actions"); RenderCardTableActions(snapshot); });
				HashSet<ActionId> spatialIds = spatial.Select(action => action.Action.Id).ToHashSet();
				displayed = displayed.Where(action => !spatialIds.Contains(action.Id) && !_accessActionButtons.ContainsKey(action.Id));
			}
			AddAccessAllButton(snapshot, AddButton);
			Container actionTarget = ActionContainerFor(contextual && displayed.Any());
			// A score leads, and continuing past one asks twice (a misclicked pass lost an agenda).
			bool scoreAvailable = all.Any(action => action.Kind == ActionKind.ScoreAgenda);
			foreach (LegalActionViewModel action in displayed.OrderByDescending(action => action.Kind == ActionKind.ScoreAgenda))
			{
				bool guarded = scoreAvailable && action.Kind == ActionKind.PassPriority;
				string label = guarded
					? SameDecision(_skipScoreDecision, decision) ? "Confirm: continue without scoring" : "Continue without scoring"
					: TableActionLabel(action);
				Button button = AddButtonTo(actionTarget, label, () =>
				{
					if (guarded && !SameDecision(_skipScoreDecision, decision))
					{
						_skipScoreDecision = decision;
						RenderCardTableActions(snapshot);
						return;
					}
					_skipScoreDecision = null;
					if (ActionsRegionPresenter.IsTargetedAction(action)) StageTargetAction(decision, action);
					else SubmitAction(decision, action);
				});
				button.TooltipText = action.CostSummary ?? TableActionLabel(action);
				button.Disabled = _tableAwaitingSettlement || Volatile.Read(ref _submissionInFlight) != 0;
				_tableActionButtons[action.Id] = button;
			}
			Button AddButton(string text, Action callback) => AddButtonTo(_tableActions, text, callback);
			Button AddButtonTo(Container container, string text, Action callback)
			{
				Button button = CreateTableActionButton(text);
				button.Disabled = _tableAwaitingSettlement;
				button.Pressed += callback;
				QuietFallback(button);
				container.AddChild(button);
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

	private DecisionViewModel? _skipScoreDecision;

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

	// Core's prompt text is diagnostic (rules citations, step names); players
	// see Match's wording, and the raw text stays in the prompt tooltip.
	private string TablePrompt(DecisionViewModel decision, MatchViewModel view) => decision.Kind == DecisionKind.TurnDiscard
		? HandDiscardPrompt(decision)
		: DecisionPromptPresenter.Prompt(decision, view);

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
		// Rules citations belong in the developer tooltip, not the play screen.
		text = Regex.Replace(text, @"\bCR [\d.]+[a-z]?:\s*", "");
		if (text.Length > 0) text = char.ToUpperInvariant(text[0]) + text[1..];
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
		while (_pendingSnapshots.TryDequeue(out SessionPublication? publication))
		{
			if (publication.Generation != _sessionGeneration) continue;
			_tablePublications.Enqueue(publication.Snapshot);
		}
		bool animate = !_skipAnimation && !_reducedMotion.ButtonPressed;
		if (_skipAnimation) _tablePlaybackWait = 0;
		if (_tableAwaitingSettlement && _tablePlaybackWait == 0 && _gameTable.ActiveMotionCount == 0 && _tablePublications.Count == 0)
		{
			_tableAwaitingSettlement = false;
			if (_currentSnapshot is { } settled) RenderCardTableActions(settled);
		}
		if (animate && (_tablePlaybackWait > 0 || _gameTable.ActiveMotionCount > 0 || _gameTable.DiscardMotionCount > 0)) return;
		while (_tablePublications.TryDequeue(out MatchSnapshot? next))
		{
			if (next.Sequence <= _renderedSequence) continue;
			_renderedSequence = next.Sequence;
			_tableHistory.Append(next.History);
			_matchHistory.Append(next.History);
			_tableAwaitingSettlement = animate || _tablePublications.Count > 0;
			MatchViewModel? before = _currentSnapshot?.View;
			RenderSnapshot(next);
			_snapshotRendered?.Invoke(next);
			bool meaningful = next.History.Any(entry => !entry.Summary.Contains("passes priority", StringComparison.Ordinal));
			if (meaningful && animate)
			{
				_tablePlaybackWait = (next.History.Any(entry => entry.Side != next.View.Viewer) ? 0.9 : 0.4) / _speed.Value;
				foreach (Button actionButton in _tableActions.GetChildren().OfType<Button>()) actionButton.Disabled = true;
			}
			PlayBeats(before, next, animate);
			// Give deferred layout/motion a frame to start, even for a status-only
			// publication. Never let a fast worker force the viewer to skip a turn.
			if (animate) break;
		}
		if (_tablePublications.Count == 0) _skipAnimation = false;
	}

	/// <summary>
	/// Plays the publication's beats (U3) and paces playback by them. A change the
	/// events don't explain is a snap mismatch: the board shows it by settling,
	/// and the session diagnostics record it.
	/// </summary>
	private void PlayBeats(MatchViewModel? before, MatchSnapshot next, bool animate)
	{
		if (before is null || next.PresentationEvents.Count == 0) return;
		_beats.Pacing = new BeatPacing(_speed.Value, _fastOpponent.ButtonPressed, _reducedMotion.ButtonPressed);
		_beats.Viewer = next.View.Viewer;
		IReadOnlyList<Beat> beats = BeatPlanner.Plan(next.PresentationEvents, before, next.View, _gameTable.GetServerDisplayName);
		double seconds = _beats.Play(beats);
		if (animate) _tablePlaybackWait = Math.Max(_tablePlaybackWait, seconds);
		BoardDisplayState shown = next.PresentationEvents.Aggregate(BoardDisplayState.From(before), (state, value) => state.Apply(value));
		IReadOnlyList<string> differences = shown.Differences(BoardDisplayState.From(next.View));
		if (differences.Count > 0)
		{
			_snapMismatches += differences.Count;
			WriteFullMatchDiagnostic("snap-mismatch", new { actionCount = next.ActionCount, differences });
		}
	}

	private int _snapMismatches;
}
