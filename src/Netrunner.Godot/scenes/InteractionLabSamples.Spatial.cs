using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public static partial class InteractionLabSamples
{
	public static string[] SpatialNames => ["Corp: card actions", "Runner: targeted abilities and costs", "Run: forced trash target",
		"Breach: hidden root and HQ", "Access: direct trash controls", "Encounter: bioroid click ability", "Corp: ICE install choice",
		"Runner: incremental multi-card selection", "Runner: search browser", "Corp: tagged resource trash", "Runner: pile ability", "Runner: ordered targets"];
	public static string[] SpatialInstructions => [
		"Click the agenda to advance or score, or ICE to rez. The named action confirms its cost.",
		"Click your identity, choose an ability, select its effect target and a separate card cost, then confirm. Esc cancels.",
		"A forced choice during a run highlights eligible programs. Select one and confirm; clicking alone spends nothing.",
		"Select a face-down installed card or the HQ server, then confirm access. Hidden identities stay hidden.",
		"Use Trash or Leave card beside the artwork. The price includes the exact payment split; expiring run credits are spent first. Click the artwork to inspect.",
		"Click the encountered ICE, select an unbroken subroutine, and confirm the click cost.",
		"Select the card to install, then its server root, ICE position, or new remote, and confirm.",
		"Select any number of highlighted programs, then confirm once. The lab advances through fresh issued choices.",
		"Select a search candidate in the card browser and confirm. Only authorized candidate faces are shown.",
		"Click the eligible resource, then confirm its trash action and cost.",
		"Open Heap and use the card's Actions control, or use the nearby pile action shortcut. Ordinary browsing still inspects.",
		"Select the first and second cards in order, then confirm the swap. Esc clears unsubmitted choices."];

	public static MatchViewModel Spatial(int scenario, long revision)
	{
		MatchViewModel view = IcebreakerEncounter(revision);
		PlayerSide side = scenario is 20 or 26 or 29 ? PlayerSide.Corp : PlayerSide.Runner;
		view = view with { Viewer = side, LegalActions = [], Run = scenario is 20 or 21 or 27 or 28 or 29 or 30 or 31 ? null : view.Run };
		CardViewModel a = UiPreviewSamples.Known("lab-target-a", "Program", "Practice target A");
		CardViewModel b = UiPreviewSamples.Known("lab-target-b", "Program", "Practice target B");
		CardViewModel cost = UiPreviewSamples.Known("lab-card-cost", "Event", "Practice payment card");
		view = view with { RunnerRig = [a, b], RunnerHand = new(ZoneKind.RunnerGrip, 1, [cost]), RunnerHeap = [], CorpDiscard = [] };
		LegalActionViewModel Act(string id, ActionKind kind, ActionParameters p) => Action(id, kind, p, revision);
		LegalActionViewModel Choice(string id, params string[] cards) => Act(id, ActionKind.ChooseBehavior,
			new BehaviorChoiceParameters("lab-selection", cardIds: cards.Select(card => new CardInstanceId(card)).ToArray()));
		var actions = new List<LegalActionViewModel>();
		DecisionKind decision = DecisionKind.TurnAction;
		switch (scenario)
		{
			case 20:
				CardViewModel agenda = UiPreviewSamples.Known("lab-agenda", "Agenda", "Practice agenda") with { AdvancementCounters = 3, AgendaPoints = 2 };
				view = view with { Servers = view.Servers.Select(server => server.Kind == ServerKind.Headquarters ? server with { Root = [agenda] } : server).ToArray() };
				actions.Add(Act("advance", ActionKind.AdvanceCard, new AdvanceCardParameters(new(agenda.Id), 1)));
				actions.Add(Act("score", ActionKind.ScoreAgenda, new ScoreAgendaParameters(new(agenda.Id), 2, 3)));
				actions.Add(Act("rez", ActionKind.RezCard, new RezCardParameters(new("lab-encounter-ice"), 2)));
				break;
			case 21:
				actions.Add(SpatialAbility(view.RunnerIdentity!.Id, a.Id, cost.Id, "target-a", revision));
				actions.Add(SpatialAbility(view.RunnerIdentity.Id, b.Id, cost.Id, "target-b", revision));
				actions.Add(SpatialAbility(view.RunnerIdentity.Id, null, null, "draw", revision, "draw"));
				break;
			case 22:
				decision = DecisionKind.IceSubroutineChoice;
				actions.Add(Act("trash-a", ActionKind.ResolveIceSubroutine, new IceSubroutineChoiceParameters("lab-frame", IceSubroutineChoiceKind.TrashInstalledRunnerCard, new(a.Id))));
				actions.Add(Act("trash-b", ActionKind.ResolveIceSubroutine, new IceSubroutineChoiceParameters("lab-frame", IceSubroutineChoiceKind.TrashInstalledRunnerCard, new(b.Id))));
				break;
			case 23:
				decision = DecisionKind.RunTiming;
				view = view with
				{
					Servers = view.Servers.Select(server => server.Kind == ServerKind.Headquarters ? server with { Root = [null] } : server).ToArray(),
					CardPositions = [new("lab-hidden-root", "preview-Headquarters", ZoneKind.ServerRoot, 0)],
				};
				actions.Add(Act("root", ActionKind.ChooseBreachCandidate, new BreachCandidateParameters(new("lab-hidden-root"), null)));
				actions.Add(Act("hq", ActionKind.ChooseBreachCandidate, new BreachCandidateParameters(null, BreachHiddenCandidateSource.CorpHand)));
				break;
			case 24:
				decision = DecisionKind.RunTiming;
				CardViewModel accessed = UiPreviewSamples.Known("lab-accessed", "Asset", "Practice accessed asset") with { TrashCost = 3 };
				view = view with
				{
					Run = view.Run! with
					{
						Phase = RunCrPhase.Success,
						Step = RunTimingStep.BreachAccessAbility,
						Encounter = null,
						CurrentIce = null,
						Priority = null,
						Access = view.Run.Access with { State = RunAccessState.InProgress, Accessed = [accessed] }
					}
				};
				foreach (int hosted in new[] { 0, 1, 2 }) actions.Add(Act("trash-" + hosted, ActionKind.TrashAccessedCard, new RunCardPaymentParameters(new(accessed.Id), hosted))
					with
				{ Description = $"Trash {accessed.Id} for 3 credits ({hosted} hosted + {3 - hosted} personal)" });
				actions.Add(Act("continue", ActionKind.NoAccessAction, new NoActionParameters()));
				break;
			case 25:
				decision = DecisionKind.RunTiming;
				foreach (int ordinal in new[] { 0, 1, 2 }) actions.Add(Act("bioroid-" + ordinal, ActionKind.UseBioroidAbility,
					new RunBioroidAbilityParameters(new("lab-encounter-ice"), new("lab-sub-" + ordinal), "click-break", ordinal)));
				break;
			case 26:
				decision = DecisionKind.IceSubroutineChoice;
				view = view with { CorpHand = new(ZoneKind.CorpHand, 1, [cost with { Type = "ICE" }]) };
				actions.Add(Act("install", ActionKind.ResolveIceSubroutine, new IceSubroutineChoiceParameters("lab-frame", IceSubroutineChoiceKind.InstallCard,
					new(cost.Id), InstallDestinationKind.ServerIce, new("preview-Headquarters"))));
				actions.Add(Act("install-new", ActionKind.ResolveIceSubroutine, new IceSubroutineChoiceParameters("lab-frame", IceSubroutineChoiceKind.InstallCard,
					new(cost.Id), InstallDestinationKind.ServerIce, createNewRemote: true)));
				break;
			case 27:
				view = view with { CardSelection = new("lab-frame:selection", "lab-selection", BehaviorChoiceMode.Mandatory, BehaviorChoiceCardinalityKind.AnyNumber, null, [a.Id, b.Id], []) };
				return RefreshSpatialIncremental(view, revision);
			case 28:
				decision = DecisionKind.BehaviorChoice;
				view = view with { ChoiceCards = [UiPreviewSamples.Known("lab-search-card", "Program", "Practice search candidate")] };
				actions.Add(Choice("search", "lab-search-card"));
				break;
			case 29:
				view = view with { RunnerRig = [a with { Type = "Resource", Title = "Practice resource" }] };
				actions.Add(Act("tag-trash", ActionKind.TrashTaggedResource, new TrashTaggedResourceParameters(new(a.Id), 1, 2)));
				break;
			case 30:
				view = view with { RunnerHeap = [a], RunnerRig = [b] };
				actions.Add(SpatialAbility(a.Id, b.Id, null, "heap-ability", revision));
				break;
			case 31:
				decision = DecisionKind.BehaviorChoice;
				view = view with { CardSelection = new("lab-frame:order", "lab-selection", BehaviorChoiceMode.OrderSelection, BehaviorChoiceCardinalityKind.Exactly, 2, [a.Id, b.Id], []) };
				actions.Add(Choice("a-b", a.Id, b.Id)); actions.Add(Choice("b-a", b.Id, a.Id));
				break;
		}
		return view with { Decision = Decision(side, revision, decision, SpatialNames[scenario - 20]), LegalActions = actions };
	}

	private static LegalActionViewModel SpatialAbility(string source, string? target, string? cost, string id, long revision, string key = "target") =>
		Action(id, ActionKind.UseAbility, new AbilityActionParameters(new(source), new("lab-definition"), key,
			BehaviorAbilityTiming.AnyPaidWindow, BehaviorAbilityActivation.PaidAbility,
			cost is null ? [new(BehaviorCostKind.Credits, 1)] : [new(BehaviorCostKind.Credits, 1), new(BehaviorCostKind.Additional, 1, additionalKey: "trash-card", additionalKind: BehaviorAdditionalCostKind.TrashCards, additionalTarget: BehaviorTarget.CardsInZone(BehaviorZone.Grip, new BehaviorCardFilter(ownerSide: PlayerSide.Runner)))],
			target is null ? [] : [new(target)], new(BehaviorAbilityUsageLimit.Unlimited, 0), new string('a', 64), new string('b', 64), new("lab-zone"), ZoneKind.RunnerIdentity,
			cost is null ? null : [new(1, "trash-card", [new(cost)])]), revision) with
		{ EffectLabel = key == "draw" ? "Draw a card" : "Choose a program" };

	public static MatchViewModel RefreshSpatialIncremental(MatchViewModel view, long revision)
	{
		ObservationCardSelection context = view.CardSelection!;
		LegalActionViewModel[] actions = [.. context.Candidates.Except(context.Selected).Select(id => Action("add-" + id, ActionKind.ChooseBehavior,
			new BehaviorChoiceParameters(context.ChoiceKey, cardIds: [new(id)], finalizeSelection: false), revision)),
			Action("finish", ActionKind.ChooseBehavior, new BehaviorChoiceParameters(context.ChoiceKey, cardIds: context.Selected.Select(id => new CardInstanceId(id)).ToArray()), revision)];
		return view with { Decision = Decision(view.Viewer, revision, DecisionKind.BehaviorChoice, "Select programs"), LegalActions = actions };
	}
}

public partial class Main
{
	private void SubmitSpatialLab(MatchViewModel view, LegalActionViewModel action)
	{
		if (action.Parameters is BehaviorChoiceParameters { FinalizeSelection: false } choice && view.CardSelection is { } context)
		{
			PublishLabView(InteractionLabSamples.RefreshSpatialIncremental(view with { CardSelection = context with { Selected = [.. context.Selected, .. choice.CardIds.Select(id => id.Value)] } }, _labRevision));
			return;
		}
		if (action.Parameters is RunCardPaymentParameters payment)
			view = view with { Runner = view.Runner with { Credits = view.Runner.Credits - (3 - payment.HostedCredits) }, Run = view.Run! with { RunCredits = view.Run.RunCredits - payment.HostedCredits } };
		PublishLabView(view with { Decision = null, LegalActions = [], CardSelection = null });
		_tableLastAction.Text = "Lab accepted: " + action.Kind;
	}
}
