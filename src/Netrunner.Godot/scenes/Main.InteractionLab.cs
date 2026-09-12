using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private bool _interactionLab;
	private int _labScenario;
	private int _labStep;
	private long _labRevision;
	private InstallCardParameters? _labPendingInstall;
	private OptionButton? _labScenarios;
	private Label? _labInstructions;
	private int _labSubmissionCount;

	private void OpenInteractionLab()
	{
		// Only reachable before starting a match, or through the explicit lab CLI mode.
		if (_coordinator is not null) return;
		_interactionLab = true;
		Control toolbar = _board.GetNode<Control>("InteractionLabToolbar");
		toolbar.Show();
		if (_labScenarios is null)
		{
			_labScenarios = toolbar.GetNode<OptionButton>("Scenarios");
			foreach (string name in InteractionLabSamples.Names) _labScenarios.AddItem(name);
			_labScenarios.ItemSelected += index => { _labScenario = (int)index; ResetInteractionLab(); };
			toolbar.GetNode<Button>("Reset").Pressed += ResetInteractionLab;
			toolbar.GetNode<Button>("Exit").Pressed += () => GetTree().ReloadCurrentScene();
			_labInstructions = toolbar.GetNode<Label>("Instructions");
		}
		ResetInteractionLab();
	}

	private void ResetInteractionLab()
	{
		_boardDrag?.Cancel("lab-reset", false);
		DismissBoardChoices();
		HideTablePile();
		_labStep = 0;
		_labPendingInstall = null;
		_labSubmissionCount = 0;
		_labRevision++;
		_matchHistory.ClearHistory();
		_tableHistory.Clear();
		PublishLabView(InteractionLabSamples.Create(_labScenario, _labRevision));
		_labInstructions!.Text = InteractionLabSamples.Instructions[_labScenario];
		_tableLastAction.Text = _labScenario is 17 or 18 ? "Discard two cards by dragging them to the pile"
			: _labScenario >= 15 ? "Practice the hand and board actions" : "Drag the practice card from hand";
	}

	private void PublishLabView(MatchViewModel view)
	{
		Observation observation = new(view.Viewer, view.Decision?.Id ?? "", [], new Dictionary<string, object?>(), new Dictionary<string, object?>());
		RenderSnapshot(new MatchSnapshot(_labRevision, observation, view, [], null, false));
		_status.Text = "INTERACTION LAB · synthetic UI fixtures · no live match";
		_phase.Text = "Reset or switch scenarios at any time";
	}

	private void SubmitBoardActionLab(MatchViewModel view, LegalActionViewModel action)
	{
		bool corp = view.Viewer == PlayerSide.Corp;
		PlayerViewModel player = corp ? view.Corp : view.Runner;
		player = player with { Clicks = Math.Max(0, player.Clicks - 1), Credits = player.Credits + (action.Kind == ActionKind.GainCredit ? 1 : 0) };
		view = corp ? view with { Corp = player } : view with { Runner = player };
		if (action.Kind == ActionKind.DrawCard)
		{
			ZoneViewModel hand = corp ? view.CorpHand : view.RunnerHand;
			CardViewModel drawn = UiPreviewSamples.Known($"lab-drawn-{_labRevision}", corp ? "Operation" : "Event", "Practice drawn card");
			hand = hand with { Count = hand.Count + 1, VisibleCards = [.. hand.VisibleCards, drawn] };
			view = corp ? view with { CorpHand = hand, CorpDeck = view.CorpDeck with { Count = Math.Max(0, view.CorpDeck.Count - 1) } }
				: view with { RunnerHand = hand, RunnerDeck = view.RunnerDeck with { Count = Math.Max(0, view.RunnerDeck.Count - 1) } };
		}
		bool complete = action.Kind == ActionKind.StartRun || player.Clicks == 0;
		view = view with
		{
			Decision = complete ? null : InteractionLabSamples.Decision(view.Viewer, _labRevision, DecisionKind.TurnAction, "Practice board actions"),
			LegalActions = complete ? [] : view.LegalActions.Select(current => InteractionLabSamples.Action(current.Kind + current.Id.Value, current.Kind, current.Parameters, _labRevision)).ToArray(),
		};
		PublishLabView(view);
		_tableLastAction.Text = action.Parameters is RunServerParameters run
			? $"Lab selected run on {_gameTable.GetServerDisplayName(run.ServerId.Value)} · reset to repeat" : $"Lab accepted: {action.Kind}";
	}

	private bool SubmitInteractionLab(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (!_interactionLab) return false;
		if (_currentSnapshot is not { } snapshot || !SameDecision(decision, snapshot.View.Decision)
			|| !snapshot.View.LegalActions.Any(a => a.Id == action.Id)) return true;
		_labSubmissionCount++;
		_labRevision++;
		MatchViewModel view = snapshot.View;
		if (_labScenario is 17 or 18)
		{
			SubmitDiscardLab(view, action);
			return true;
		}
		if (_labScenario >= 15)
		{
			SubmitBoardActionLab(view, action);
			return true;
		}
		LegalActionViewModel Next(string id, ActionKind kind, ActionParameters parameters) => InteractionLabSamples.Action(id, kind, parameters, _labRevision);
		MatchViewModel Choice(DecisionKind kind, string prompt, params LegalActionViewModel[] actions) => view with
		{
			Decision = InteractionLabSamples.Decision(view.Viewer, _labRevision, kind, prompt),
			LegalActions = actions,
		};
		if (_labScenario == 8 && _labStep < 2)
		{
			_labPendingInstall ??= (InstallCardParameters)action.Parameters;
			_labStep++;
			bool finish = _labStep == 2;
			var install = new InstallCardParameters(new("lab-hand"), InstallDestinationKind.ServerRoot,
				new("preview-remote-0"), false, 3, [new("preview-root-0")], [new("preview-root-0")], [new("preview-root-0")], finish);
			PublishLabView(Choice(DecisionKind.InstallChoice, finish ? "Finish installing with selected replacement" : "Choose the asset to replace",
				Next(finish ? "finish-install" : "select-replacement", ActionKind.InstallCard, install)));
			return true;
		}
		if (_labStep == 0 && _labScenario is 9 or 10 or 11 or 13 or 14)
		{
			_labStep++;
			_labPendingInstall = action.Parameters as InstallCardParameters;
			if (_labScenario == 14)
				PublishLabView(Choice(DecisionKind.BehaviorChoice, "Choose two program targets",
					Next("pair-one", ActionKind.ChooseBehavior, new BehaviorChoiceParameters("lab-pair", new[] { new CardInstanceId("preview-rig-0"), new CardInstanceId("preview-rig-1") })),
					Next("pair-two", ActionKind.ChooseBehavior, new BehaviorChoiceParameters("lab-pair", new[] { new CardInstanceId("preview-rig-0"), new CardInstanceId("preview-rig-2") }))));
			else if (_labScenario == 11)
				PublishLabView(Choice(DecisionKind.BehaviorChoice, "Choose the event's effect",
					Next("gain-credits", ActionKind.ChooseBehavior, new BehaviorChoiceParameters("lab-choice", "gain-credits")),
					Next("draw-cards", ActionKind.ChooseBehavior, new BehaviorChoiceParameters("lab-choice", "draw-cards"))));
			else
				PublishLabView(Choice(_labScenario == 9 ? DecisionKind.MemoryCleanup : DecisionKind.ChooseCard,
					_labScenario == 9 ? "Choose a program to trash for memory" : "Choose a card target",
					(_labScenario == 13 ? view.RunnerHeap : view.RunnerRig).Where(c => c.HostCardId is null).Select(c => Next("choose-" + c.Id, ActionKind.ChooseCard, new CardChoiceParameters(new(c.Id)))).ToArray()));
			return true;
		}
		InstallCardParameters? completedInstall = action.Parameters as InstallCardParameters ?? _labPendingInstall;
		CardViewModel? handCard = (view.Viewer == PlayerSide.Corp ? view.CorpHand : view.RunnerHand).VisibleCards.FirstOrDefault(c => c.Id == "lab-hand");
		if (handCard is not null)
		{
			view = view.Viewer == PlayerSide.Corp ? view with { CorpHand = new(ZoneKind.CorpHand, 0, []) }
				: view with { RunnerHand = new(ZoneKind.RunnerGrip, Math.Max(0, view.RunnerHand.Count - 1), view.RunnerHand.VisibleCards.Where(c => c.Id != handCard.Id).ToArray()) };
			if (completedInstall is { } install)
			{
				if (install.DestinationKind == InstallDestinationKind.RunnerRig)
				{
					var rig = view.RunnerRig.Where(c => _labScenario != 9 || action.Parameters is not CardChoiceParameters choice || c.Id != choice.CardId.Value).ToList();
					rig.Add(handCard with { HostCardId = install.HostCardId?.Value });
					view = view with { RunnerRig = rig };
				}
				else
				{
					string serverId = install.ServerId?.Value ?? "lab-new-remote";
					var servers = view.Servers.ToList();
					if (install.CreateNewRemote) servers.Add(new(serverId, ServerKind.Remote, [], []));
					servers = servers.Select(s => s.Id != serverId ? s : install.DestinationKind == InstallDestinationKind.ServerIce
						? s with { Ice = new CardViewModel?[] { handCard }.Concat(s.Ice).ToArray() }
						: s with { Root = s.Root.Where(c => _labScenario != 8 || c?.Id != "preview-root-0").Append(handCard).ToArray() }).ToList();
					view = view with { Servers = servers };
				}
			}
		}
		view = view with { Decision = null, LegalActions = [] };
		PublishLabView(view);
		_labInstructions!.Text = $"Completed in {_labSubmissionCount} action(s). Reset to repeat or choose another scenario.";
		_tableLastAction.Text = $"Lab accepted: {action.Description}";
		return true;
	}

	private void SubmitDiscardLab(MatchViewModel view, LegalActionViewModel action)
	{
		if (action.Parameters is not CardChoiceParameters chosen) return;
		bool corp = view.Viewer == PlayerSide.Corp;
		ZoneViewModel hand = corp ? view.CorpHand : view.RunnerHand;
		string[] selected = [.. BoardInteractionProjector.SelectedDiscards(view).Select(card => card.Id), chosen.CardId.Value];
		if (selected.Length < 2)
		{
			PublishLabView(view with
			{
				Decision = InteractionLabSamples.Decision(view.Viewer, _labRevision, DecisionKind.TurnDiscard, "Choose 1 more card to discard"),
				LegalActions = view.LegalActions.Where(candidate => candidate.Id != action.Id)
					.Select(candidate => InteractionLabSamples.Action("discard-" + ((CardChoiceParameters)candidate.Parameters).CardId.Value,
						ActionKind.ChooseCard, candidate.Parameters, _labRevision)).ToArray(),
			});
		}
		else
		{
			CardViewModel[] discarded = hand.VisibleCards.Where(card => selected.Contains(card.Id, StringComparer.Ordinal)).ToArray();
			hand = hand with { Count = hand.Count - discarded.Length, VisibleCards = hand.VisibleCards.Except(discarded).ToArray() };
			view = corp ? view with { CorpHand = hand, CorpDiscard = [.. view.CorpDiscard, .. discarded], CorpDiscardCount = view.CorpDiscardCount + discarded.Length }
				: view with { RunnerHand = hand, RunnerHeap = [.. view.RunnerHeap, .. discarded] };
			PublishLabView(view with { Decision = null, LegalActions = [], ForcedDiscard = null });
		}
		_tableLastAction.Text = selected.Length < 2 ? "One selected · drag one more card" : "Discard complete · reset to repeat";
	}
}
