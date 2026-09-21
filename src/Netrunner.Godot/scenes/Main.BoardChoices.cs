using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Board-first controls for a card play or install resolution.
///
/// This partial deliberately keeps the board interaction separate from the
/// ordinary action dock.  It consumes only the current viewer-safe snapshot,
/// and every submission still goes through Main.SubmitAction with an issued
/// action ID.  It does not know, or attempt to reconstruct, engine state.
/// </summary>
public partial class Main
{
	private const string BoardChoicePanelPath = "res://scenes/components/BoardChoicePanel.tscn";

	private PanelContainer? _boardChoicePanel;
	private Label? _boardChoiceTitle;
	private Label? _boardChoicePrompt;
	private VBoxContainer? _boardChoiceOptions;
	private Button? _boardChoiceDismiss;
	private DecisionViewModel? _boardChoiceDecision;
	private DecisionViewModel? _boardChoiceDismissedDecision;
	private MatchSnapshot? _boardChoiceSnapshot;
	private LegalActionViewModel[] _boardChoiceActions = Array.Empty<LegalActionViewModel>();
	private bool _boardChoiceVisible;
	private bool? _boardChoiceInputEnabled;
	private bool _boardChoiceDisambiguating;
	private readonly List<PanelContainer> _boardChoiceServerCues = new();
	private bool _boardChoiceIsInitialStage;
	private bool _boardChoiceRunSelection;
	private bool _boardPlayResolutionTracking;
	private string? _boardPlaySourceDecisionId;
	private long _boardPlaySourceDecisionRevision;
	private long _boardPlaySourceActionCount = -1;

	/// <summary>
	/// Selects a currently highlighted card choice.  The card ID is a
	/// presentation handle supplied by GameTableControl; no card is looked up
	/// outside the current MatchSnapshot.
	/// </summary>
	public bool TrySelectBoardChoiceCard(string id)
	{
		if (!_boardChoiceVisible || string.IsNullOrWhiteSpace(id)
			|| !TryGetCurrentBoardChoiceActions(out DecisionViewModel decision, out IReadOnlyList<LegalActionViewModel> actions))
		{
			return false;
		}

		LegalActionViewModel[] matches = actions
			.Where(action => BoardCardTargetIds(action, actions).Contains(id, StringComparer.Ordinal))
			.ToArray();
		if (matches.Length == 0)
		{
			return false;
		}

		if (matches.Length == 1 && BoardCardTargetIds(matches[0], actions).Count <= 1)
		{
			SubmitBoardChoiceAction(decision, matches[0]);
		}
		else
		{
			_boardChoiceDisambiguating = true;
			// Multiple issued actions may contain the same card (for example, a
			// replacement set can have more than one legal continuation).  Keep
			// every action ID visible and require an explicit choice.
			ShowBoardChoicePanel(
				decision,
				matches,
				"Choose an action",
				$"{BoardCardDisplayName(id)} has more than one legal choice.");
		}

		return true;
	}

	/// <summary>
	/// Selects a currently highlighted server choice.  Server IDs are matched
	/// only against typed install payloads (ordinary run-event plays are
	/// intentionally outside this surface).
	/// </summary>
	public bool TrySelectBoardChoiceServer(string id)
	{
		if (!_boardChoiceVisible || string.IsNullOrWhiteSpace(id)
			|| !TryGetCurrentBoardChoiceActions(out DecisionViewModel decision, out IReadOnlyList<LegalActionViewModel> actions))
		{
			return false;
		}

		LegalActionViewModel[] matches = actions
			.Where(action => string.Equals(BoardServerTargetId(action), id, StringComparison.Ordinal))
			.ToArray();
		if (matches.Length == 0)
		{
			return false;
		}

		if (matches.Length == 1)
		{
			SubmitBoardChoiceAction(decision, matches[0]);
		}
		else
		{
			_boardChoiceDisambiguating = true;
			ShowBoardChoicePanel(
				decision,
				matches,
				"Choose an action",
				$"{_gameTable.GetServerDisplayName(id)} has more than one legal choice.");
		}

		return true;
	}

	/// <summary>
	/// Refreshes the board-first surface from the latest immutable publication.
	/// Install choices follow the current decision, including during runs.
	/// Other spatial choices use the shared spatial controller; abstract
	/// play follow-ups retain their compact decision controls.
	/// </summary>
	public void RefreshBoardChoices(MatchSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (!SameDecision(_boardChoiceDecision, snapshot.View.Decision)) _boardChoiceDisambiguating = false;
		_boardChoiceSnapshot = snapshot;

		if (snapshot.IsTerminal
			|| BoardSpatialActions.Project(snapshot.View).Any(action => action.Automatic))
		{
			ClearBoardChoiceState(clearTracking: true, restoreTargets: false);
			return;
		}

		DecisionViewModel? decision = snapshot.View.Decision;
		if (snapshot.IsOpponentThinking || decision is null || decision.OwnerSide != snapshot.View.Viewer)
		{
			// The coordinator intentionally withholds the opponent's decision. Keep
			// the tracking bit across that publication so the next local resolution
			// can restore board-first controls, but never leave stale cues visible.
			HideBoardChoicePanel();
			return;
		}

		if (_stagedAction is not null || _tableAllActions && !_boardChoiceIsInitialStage
			|| !_boardPlayResolutionTracking && !_boardChoiceIsInitialStage && SameDecision(_boardChoiceDismissedDecision, decision))
		{
			HideBoardChoicePanel();
			return;
		}

		if (snapshot.Feedback?.IsError == true
			&& IsSourceDecision(snapshot, decision))
		{
			ClearBoardChoiceState(clearTracking: true, restoreTargets: false);
			return;
		}

		if (_boardChoiceIsInitialStage)
		{
			if (!SameDecision(_boardChoiceDecision, decision))
			{
				ClearBoardChoiceState(clearTracking: false, restoreTargets: false);
				return;
			}

			LegalActionViewModel[] refreshed = _boardChoiceActions
				.Where(action => snapshot.View.LegalActions.Any(current => current.Id == action.Id))
				.ToArray();
			if (refreshed.Length == 0)
			{
				ClearBoardChoiceState(clearTracking: false, restoreTargets: false);
				return;
			}

			_boardChoiceActions = refreshed;
			ShowBoardChoicePanel(
				decision,
				refreshed,
				_boardChoiceRunSelection ? "Choose a server to run" : "Choose a play or install destination",
				_boardChoiceRunSelection ? "Click a highlighted server to start the run immediately · 1 click. Cancel or Esc returns to the board." : BoardChoiceInitialPrompt(refreshed));
			return;
		}

		if (!_boardPlayResolutionTracking && decision.Kind != DecisionKind.InstallChoice)
		{
			HideBoardChoicePanel();
			return;
		}

		// BeginBoardPlayResolution is normally called while the original
		// TurnAction publication is still current. Do not mistake that unchanged
		// publication for a follow-up choice before SubmitAction has published.
		if (IsSourceDecision(snapshot, decision))
		{
			HideBoardChoicePanel();
			return;
		}

		if (!IsBoardPlayFollowUpDecision(decision.Kind))
		{
			ClearBoardChoiceState(clearTracking: true, restoreTargets: false);
			return;
		}

		LegalActionViewModel[] actions = snapshot.View.LegalActions
			.Where(IsBoardPlayFollowUpAction)
			.ToArray();
		if (actions.Length == 0)
		{
			ClearBoardChoiceState(clearTracking: true, restoreTargets: false);
			return;
		}

		_boardChoiceDecision = decision;
		_boardChoiceActions = actions;
		_boardChoiceIsInitialStage = false;
		_boardChoiceVisible = true;
		ShowBoardChoicePanel(
			decision,
			actions,
			BoardChoiceFollowUpTitle(decision.Kind),
			BoardChoiceFollowUpPrompt(decision, actions));
	}

	/// <summary>
	/// Marks the start of the resolution opened by a board drag.  This is local
	/// presentation tracking only; the caller still submits the issued action
	/// through Main.SubmitAction.
	/// </summary>
	public void BeginBoardPlayResolution()
	{
		_boardPlayResolutionTracking = true;
		_boardPlaySourceDecisionId = _currentSnapshot?.View.Decision?.Id;
		_boardPlaySourceDecisionRevision = _currentSnapshot?.View.Decision?.Revision ?? -1;
		_boardPlaySourceActionCount = _currentSnapshot?.ActionCount ?? -1;
		_boardChoiceIsInitialStage = false;
		HideBoardChoicePanel();
	}

	/// <summary>
	/// Stages ambiguous initial PlayCard/InstallCard candidates supplied by the
	/// drag controller. Candidates are retained verbatim; no action ID or typed
	/// destination is synthesized by the UI.
	/// </summary>
	public void StageBoardPlayChoices(DecisionViewModel decision, IReadOnlyList<LegalActionViewModel> candidates)
		=> StageBoardInitialChoices(decision, candidates, runSelection: false);

	private void StageBoardInitialChoices(DecisionViewModel decision, IReadOnlyList<LegalActionViewModel> candidates, bool runSelection)
	{
		ArgumentNullException.ThrowIfNull(decision);
		ArgumentNullException.ThrowIfNull(candidates);
		if (candidates.Count == 0
			|| candidates.Any(action => runSelection
				? action.Kind != ActionKind.StartRun || action.Parameters is not RunServerParameters
				: !IsBoardPlayInstallCandidate(action)))
		{
			return;
		}

		if (_currentSnapshot is { View.Decision: { } currentDecision }
			&& !SameDecision(currentDecision, decision))
		{
			return;
		}

		if (_currentSnapshot is { } current
			&& candidates.Any(candidate => !current.View.LegalActions.Any(action => action.Id == candidate.Id)))
		{
			return;
		}

		_boardChoiceDecision = decision;
		_boardChoiceSnapshot = _currentSnapshot;
		_boardChoiceActions = candidates.ToArray();
		_boardChoiceIsInitialStage = true;
		_boardChoiceRunSelection = runSelection;
		_boardChoiceVisible = true;
		ShowBoardChoicePanel(
			decision,
			_boardChoiceActions,
			runSelection ? "Choose a server to run" : "Choose a play or install destination",
			runSelection ? "Click a highlighted server to start the run immediately · 1 click. Cancel or Esc returns to the board." : BoardChoiceInitialPrompt(_boardChoiceActions));
	}

	/// <summary>Dismisses only the local contextual surface; it never rolls back Core.</summary>
	public void DismissBoardChoices()
	{
		_boardChoiceDismissedDecision = _currentSnapshot?.View.Decision;
		ClearBoardChoiceState(clearTracking: true, restoreTargets: true);
		if (_currentSnapshot is { } snapshot) RefreshBasicBoardActions(snapshot);
	}

	private void SubmitBoardChoiceAction(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (!TryGetCurrentBoardChoiceActions(out DecisionViewModel currentDecision, out IReadOnlyList<LegalActionViewModel> currentActions)
			|| !SameDecision(decision, currentDecision))
		{
			return;
		}

		LegalActionViewModel? currentAction = currentActions.FirstOrDefault(candidate => candidate.Id == action.Id);
		if (currentAction is null)
		{
			return;
		}

		bool startsResolution = _boardChoiceIsInitialStage
			&& IsImmediatePlayInstallAction(currentAction)
			&& currentAction.Parameters is not PlayCardParameters { TargetServerId: not null }
			&& _currentSnapshot?.View.Decision?.Kind == DecisionKind.TurnAction;
		if (startsResolution)
		{
			BeginBoardPlayResolution();
		}
		else
		{
			HideBoardChoicePanel();
		}

		SubmitAction(currentDecision, currentAction);
	}

	private bool TryGetCurrentBoardChoiceActions(
		out DecisionViewModel decision,
		out IReadOnlyList<LegalActionViewModel> actions)
	{
		if (_boardChoiceDecision is not { } selectedDecision)
		{
			decision = null!;
			actions = Array.Empty<LegalActionViewModel>();
			return false;
		}

		decision = selectedDecision;
		actions = Array.Empty<LegalActionViewModel>();
		if (!CanUseBoardChoiceInput())
		{
			return false;
		}

		MatchSnapshot current = (_currentSnapshot ?? _boardChoiceSnapshot)!;
		HashSet<ActionId> issued = current.View.LegalActions.Select(action => action.Id).ToHashSet();
		LegalActionViewModel[] retained = _boardChoiceActions
			.Where(action => issued.Contains(action.Id))
			.ToArray();
		if (retained.Length == 0)
		{
			return false;
		}

		actions = retained;
		return true;
	}

	private bool CanUseBoardChoiceInput() => _boardChoiceVisible
		&& (_currentSnapshot ?? _boardChoiceSnapshot) is { IsOpponentThinking: false, IsTerminal: false } current
		&& current.View.Decision is { } decision
		&& decision.OwnerSide == current.View.Viewer && SameDecision(_boardChoiceDecision, decision)
		&& !_fullMatchResumePaused && !_tableAwaitingSettlement && Volatile.Read(ref _submissionInFlight) == 0
		&& _stagedAction is null && !SettingsVisible && _zoomOverlay?.Visible != true && _tablePileOverlay?.Visible != true;

	private void RefreshBoardChoiceAvailability()
	{
		if (!_boardChoiceVisible || _boardChoiceOptions is null) return;
		bool enabled = CanUseBoardChoiceInput();
		if (_boardChoiceInputEnabled == enabled) return;
		_boardChoiceInputEnabled = enabled;
		foreach (Button option in _boardChoiceOptions.GetChildren().OfType<Button>()) option.Disabled = !enabled;
		foreach (TableCardControl card in _boardChoiceOptions.FindChildren("*", "Control", true, false).OfType<TableCardControl>())
		{
			card.SetTargetCandidate(enabled);
			card.Modulate = new Color(1, 1, 1, enabled ? 1 : 0.5f);
		}
		_gameTable.SetTargets(enabled
			? _boardChoiceActions.SelectMany(action => BoardCardTargetIds(action, _boardChoiceActions)).ToHashSet(StringComparer.Ordinal) : []);
	}

	private void ShowBoardChoicePanel(
		DecisionViewModel decision,
		IReadOnlyList<LegalActionViewModel> actions,
		string title,
		string prompt)
	{
		_boardChoiceDecision = decision;
		_boardChoiceActions = actions.ToArray();
		_boardChoiceVisible = true;
		EnsureBoardChoicePanel();
		if (_boardChoicePanel is null
			|| _boardChoiceTitle is null
			|| _boardChoicePrompt is null
			|| _boardChoiceOptions is null)
		{
			return;
		}

		_boardChoiceTitle.Text = title;
		_boardChoiceDismiss!.Text = _boardChoiceRunSelection ? "Cancel run" : _boardChoiceIsInitialStage ? "Cancel selection" : "Use action list · Esc";
		_boardChoiceDismiss.TooltipText = _boardChoiceIsInitialStage ? "Cancel this selection without paying."
			: "Switch to the action list. This choice still needs to be resolved.";
		_boardChoicePrompt.Text = prompt;
		ClearChildren(_boardChoiceOptions);
		_boardChoicePanel.GetNode<ScrollContainer>("Content/OptionsScroll").CustomMinimumSize = new Vector2(0, 52);

		List<LegalActionViewModel> visibleButtons = BoardChoiceButtonActions(actions);
		if (visibleButtons.Count == 0)
		{
			Label hint = MakeWrappedLabel(
				_boardChoiceIsInitialStage ? "Cancel or Esc returns to the board without paying."
					: "This choice must still be resolved. Use action list or Esc switches controls.",
				11,
				Muted);
			_boardChoiceOptions.AddChild(hint);
		}
		else
		{
			foreach (LegalActionViewModel action in visibleButtons)
			{
				Button option = CreateTableActionButton(BoardChoiceActionLabel(action));
				option.Alignment = HorizontalAlignment.Left;
				option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				option.TooltipText = action.CostSummary ?? BoardChoiceActionLabel(action);
				option.Pressed += () => SubmitBoardChoiceAction(decision, action);
				_boardChoiceOptions.AddChild(option);
			}
		}

		RenderBoardChoiceCards(actions);
		ApplyBoardChoiceTargets(actions);
		_boardChoiceInputEnabled = null;
		RefreshBoardChoiceAvailability();
		_boardChoicePanel.Visible = true;
		// The dock keeps its issued actions for the fallback, but only one
		// surface should expose controls for this decision at a time.
		_tableActions.Hide();
		_boardChoicePanel.MoveToFront();
		_boardChoiceDismiss?.GrabFocus();
	}

	private void EnsureBoardChoicePanel()
	{
		if (_boardChoicePanel is not null && IsInstanceValid(_boardChoicePanel))
		{
			return;
		}

		if (_tableViewport is null || !IsInstanceValid(_tableViewport))
		{
			return;
		}

		PackedScene? scene = GD.Load<PackedScene>(BoardChoicePanelPath);
		if (scene is null)
		{
			GD.PushError($"Board choice scene is missing: {BoardChoicePanelPath}");
			return;
		}

		_boardChoicePanel = scene.Instantiate<PanelContainer>();
		_boardChoicePanel.Visible = false;
		_boardChoicePanel.ZIndex = 80;
		// The contextual shell must not intercept a click on a server/card that
		// sits behind its compact footprint. Its authored buttons still receive
		// input because child controls retain their own Stop filter.
		_boardChoicePanel.MouseFilter = MouseFilterEnum.Ignore;
		_tableViewport.AddChild(_boardChoicePanel);
		_boardChoiceTitle = _boardChoicePanel.GetNodeOrNull<Label>("%Title");
		_boardChoicePrompt = _boardChoicePanel.GetNodeOrNull<Label>("%Prompt");
		_boardChoiceOptions = _boardChoicePanel.GetNodeOrNull<VBoxContainer>("%Options");
		_boardChoiceDismiss = _boardChoicePanel.GetNodeOrNull<Button>("%Dismiss");
		if (_boardChoiceDismiss is not null)
		{
			_boardChoiceDismiss.Pressed += DismissBoardChoices;
		}
	}

	private void HideBoardChoicePanel()
	{
		_boardChoiceVisible = false;
		_boardChoiceInputEnabled = null;
		_tableActions?.Show();
		ClearBoardServerCues();
		if (_boardChoicePanel is not null && IsInstanceValid(_boardChoicePanel))
		{
			_boardChoicePanel.Visible = false;
		}
	}

	private void ClearBoardChoiceState(bool clearTracking, bool restoreTargets)
	{
		HideBoardChoicePanel();
		_boardChoiceDecision = null;
		_boardChoiceSnapshot = null;
		_boardChoiceActions = Array.Empty<LegalActionViewModel>();
		_boardChoiceIsInitialStage = false;
		_boardChoiceRunSelection = false;
		if (clearTracking)
		{
			_boardPlayResolutionTracking = false;
			_boardPlaySourceDecisionId = null;
			_boardPlaySourceDecisionRevision = -1;
			_boardPlaySourceActionCount = -1;
		}

		if (restoreTargets)
		{
			RestoreOrdinaryBoardTargets();
		}
	}

	private void RestoreOrdinaryBoardTargets()
	{
		if (_currentSnapshot is not { } snapshot
			|| _gameTable is null
			|| !IsInstanceValid(_gameTable))
		{
			return;
		}

		_gameTable.SetTargets(snapshot.View.LegalActions
			.SelectMany(TableActionTargetCardIds)
			.ToHashSet(StringComparer.Ordinal));
	}

	private void ApplyBoardChoiceTargets(IReadOnlyList<LegalActionViewModel> actions)
	{
		if (_gameTable is null || !IsInstanceValid(_gameTable))
		{
			return;
		}

		HashSet<string> cardTargets = actions
			.SelectMany(action => BoardCardTargetIds(action, actions))
			.ToHashSet(StringComparer.Ordinal);
		_gameTable.SetTargets(cardTargets);
		RenderBoardServerCues(actions);
	}

	private List<LegalActionViewModel> BoardChoiceButtonActions(
		IReadOnlyList<LegalActionViewModel> actions)
	{
		// Spatial choices are committed from board clicks. Keep only issued
		// actions that have no visible board handle as compact panel buttons. A
		// hidden target remains available through its issued action as a safe
		// fallback; its label is still projected through FriendlyText.
		if (_boardChoiceDisambiguating) return actions.ToList();
		var result = new List<LegalActionViewModel>();
		foreach (LegalActionViewModel action in actions)
		{
			IReadOnlyList<string> cards = BoardCardTargetIds(action, actions);
			string? server = BoardServerTargetId(action);
			bool visibleCardTarget = _gameTable is not null
				&& cards.Any(id => _gameTable.VisibleCards.ContainsKey(id));
			bool visibleServerTarget = server is not null
				&& _boardChoiceSnapshot?.View.Servers.Any(candidate => candidate.Id == server) == true;
			if (cards.Count == 0 && server is null || !visibleCardTarget && !visibleServerTarget)
			{
				result.Add(action);
			}
		}

		return result;
	}

	private bool IsSourceDecision(MatchSnapshot snapshot, DecisionViewModel decision) =>
		_boardPlaySourceDecisionId is not null
		&& string.Equals(_boardPlaySourceDecisionId, decision.Id, StringComparison.Ordinal)
		&& _boardPlaySourceDecisionRevision == decision.Revision
		&& (_boardPlaySourceActionCount < 0 || snapshot.ActionCount <= _boardPlaySourceActionCount);

	private static bool IsBoardPlayInstallCandidate(LegalActionViewModel action) =>
		action.Kind == ActionKind.InstallCard && action.Parameters is InstallCardParameters
		|| action.Kind == ActionKind.PlayCard && action.Parameters is PlayCardParameters;

	private static bool IsImmediatePlayInstallAction(LegalActionViewModel action) =>
		action.Kind == ActionKind.InstallCard && action.Parameters is InstallCardParameters
		|| action.Kind == ActionKind.PlayCard && action.Parameters is PlayCardParameters;

	private static bool IsBoardPlayFollowUpDecision(DecisionKind kind) => kind is
		DecisionKind.InstallChoice
		or DecisionKind.MemoryCleanup
		or DecisionKind.ChooseCard
		or DecisionKind.ChooseNumber
		or DecisionKind.BehaviorChoice
		or DecisionKind.TriggerChoice
		or DecisionKind.TriggerOrdering
		or DecisionKind.InterruptChoice;

	private static bool IsBoardPlayFollowUpAction(LegalActionViewModel action) => action.Parameters is
		InstallCardParameters
		or CardChoiceParameters
		or NumberChoiceParameters
		or BehaviorChoiceParameters
		or TriggerChoiceParameters
		or TriggerOrderParameters
		or PreventionChoiceParameters
		or ReplacementChoiceParameters
		or NoActionParameters;

	private string? BoardServerTargetId(LegalActionViewModel action) => !_boardChoiceIsInitialStage ? null : action.Parameters switch
	{
		RunServerParameters run => run.ServerId.Value,
		InstallCardParameters install when install.ServerId is { } server => server.Value,
		PlayCardParameters play when play.TargetServerId is { } server => server.Value,
		_ => null,
	};

	private IReadOnlyList<string> BoardCardTargetIds(
		LegalActionViewModel action,
		IReadOnlyList<LegalActionViewModel> peers)
	{
		if (action.Parameters is PlayCardParameters play
			&& _boardChoiceIsInitialStage
			&& play.TargetServerId is null)
		{
			return [play.CardId.Value];
		}

		if (action.Parameters is InstallCardParameters install)
		{
			if (_boardChoiceIsInitialStage)
			{
				return [install.CardId.Value];
			}

			var targets = new List<string>(2);
			if (!install.FinalizeSelection && install.SelectedReplacementCardIds.Count == 0
				&& install.HostCardId is { } host)
			{
				targets.Add(host.Value);
			}
			targets.AddRange(InstallReplacementDelta(install, peers));
			return targets;
		}

		return action.Parameters switch
		{
			CardChoiceParameters choice => [choice.CardId.Value],
			BehaviorChoiceParameters behavior => behavior.CardIds.Select(card => card.Value).ToArray(),
			_ => Array.Empty<string>(),
		};
	}

	private static string[] InstallReplacementDelta(
		InstallCardParameters action,
		IReadOnlyList<LegalActionViewModel> peers)
	{
		if (action.FinalizeSelection || action.SelectedReplacementCardIds.Count == 0)
		{
			return Array.Empty<string>();
		}

		InstallCardParameters[] installPeers = peers
			.Select(peer => peer.Parameters as InstallCardParameters)
			.Where(candidate => candidate is not null && SameInstallSelectionFamily(candidate, action))
			.Select(candidate => candidate!)
			.ToArray();
		if (installPeers.Length == 0)
		{
			return Array.Empty<string>();
		}

		int minimumSelected = installPeers.Min(candidate => candidate.SelectedReplacementCardIds.Count);
		int baselineCount = installPeers.Any(candidate => candidate.FinalizeSelection)
			? minimumSelected
			: Math.Max(0, minimumSelected - 1);
		if (baselineCount > action.SelectedReplacementCardIds.Count)
		{
			return Array.Empty<string>();
		}

		IReadOnlyList<CardInstanceId> baseline = action.SelectedReplacementCardIds.Take(baselineCount).ToArray();
		if (installPeers.Any(candidate => !candidate.SelectedReplacementCardIds
			.Take(baselineCount)
			.SequenceEqual(baseline)))
		{
			return Array.Empty<string>();
		}

		CardInstanceId[] delta = action.SelectedReplacementCardIds
			.Skip(baselineCount)
			.Where(candidate => !baseline.Contains(candidate))
			.ToArray();
		return delta.Length == 1 ? [delta[0].Value] : Array.Empty<string>();
	}

	private static bool SameInstallSelectionFamily(
		InstallCardParameters left,
		InstallCardParameters right) =>
		left.CardId == right.CardId
		&& left.DestinationKind == right.DestinationKind
		&& left.ServerId == right.ServerId
		&& left.CreateNewRemote == right.CreateNewRemote
		&& left.HostCardId == right.HostCardId
		&& left.CandidateReplacementCardIds.SequenceEqual(right.CandidateReplacementCardIds)
		&& left.RequiredReplacementCardIds.SequenceEqual(right.RequiredReplacementCardIds);

	private string BoardChoiceActionLabel(LegalActionViewModel action)
	{
		return TableActionLabel(action);
	}

	private string BoardCardDisplayName(string id)
	{
		if (_gameTable is not null
			&& _gameTable.VisibleCards.TryGetValue(id, out TableCardControl? control)
			&& control.Card is { IsKnown: true } card)
		{
			return card.Title;
		}

		if (_boardChoiceSnapshot is { View: { } view })
		{
			CardViewModel? exposed = BoardChoiceCards(view)
				.FirstOrDefault(card => string.Equals(card.Id, id, StringComparison.Ordinal));
			if (exposed is not null)
			{
				return exposed.IsKnown ? exposed.Title : "face-down card";
			}
		}

		return FriendlyBoardText(id);
	}

	private string FriendlyBoardText(string text)
	{
		string result = FriendlyText(text);
		if (_boardChoiceSnapshot is not { View: { } view })
		{
			return result;
		}

		foreach (CardViewModel card in BoardChoiceCards(view)
			.Where(card => !string.IsNullOrWhiteSpace(card.Id))
			.OrderByDescending(card => card.Id.Length))
		{
			string display = card.IsKnown ? card.Title : "face-down card";
			result = result.Replace(card.Id, display, StringComparison.Ordinal);
		}

		return result;
	}

	private static CardViewModel[] BoardChoiceCards(MatchViewModel view) => BoardSpatialActions.Cards(view).ToArray();

	private string BoardChoiceInitialPrompt(IReadOnlyList<LegalActionViewModel> actions)
	{
		HashSet<string> servers = actions
			.Select(BoardServerTargetId)
			.Where(server => server is not null)
			.Select(server => server!)
			.ToHashSet(StringComparer.Ordinal);
		return servers.Count > 0
			? "Click a highlighted server to play or install at the displayed cost. If it has multiple options, choose one to pay."
			: "Choose how to play or install this card. Choosing an option pays its displayed cost.";
	}

	private static string BoardChoiceFollowUpTitle(DecisionKind kind) => kind switch
	{
		DecisionKind.InstallChoice => "Install resolution",
		DecisionKind.MemoryCleanup => "Memory cleanup",
		_ => "Card resolution",
	};

	private string BoardChoiceFollowUpPrompt(
		DecisionViewModel decision,
		IReadOnlyList<LegalActionViewModel> actions) =>
		FriendlyBoardText(decision.Prompt)
		+ (actions.Any(action => BoardCardTargetIds(action, actions).Count > 0)
			? " Click a highlighted card on the board."
			: " Choose an option below.");
}
