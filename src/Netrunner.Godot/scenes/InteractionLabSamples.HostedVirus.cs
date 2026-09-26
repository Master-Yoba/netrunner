using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public static partial class InteractionLabSamples
{
	public const int HostedVirusScenario = 32;
	private const string HostedVirusId = "lab-hosted-virus";

	public static MatchViewModel HostedVirusEncounter(long revision)
	{
		MatchViewModel view = IcebreakerEncounter(revision);
		CardViewModel virus = UiPreviewSamples.Known(HostedVirusId, "Program", "Practice Botulus") with
		{
			DefinitionId = "30004",
			Cost = 2,
			Faction = "Anarch",
			Subtypes = ["Virus", "Trojan"],
			HostCardId = view.Run!.Encounter!.Ice!.Id,
			TypedCounters = [new("virus", CardCounterPurpose.Virus, 2, null)],
			RulesText = "Hosted on this ICE. Spend 1 hosted virus counter: Break 1 subroutine on the host ICE. No strength match is needed.",
		};
		return RefreshHostedVirusActions(view with
		{
			RunnerRig = [virus],
			Runner = view.Runner with { UsedMemory = 1 },
			Run = view.Run with { Encounter = view.Run.Encounter with { Breakers = [] } },
		}, revision);
	}

	public static MatchViewModel RefreshHostedVirusActions(MatchViewModel view, long revision)
	{
		var actions = new List<LegalActionViewModel>();
		CardViewModel virus = view.RunnerRig.Single(card => card.Id == HostedVirusId);
		EncounterViewModel encounter = view.Run!.Encounter!;
		if (virus.HostCardId == encounter.Ice!.Id && virus.TypedCounters.Any(counter => counter.Purpose == CardCounterPurpose.Virus && counter.Count > 0))
			foreach (SubroutineViewModel subroutine in encounter.Subroutines.Where(subroutine => !subroutine.Broken && !subroutine.Resolved))
				actions.Add(Action("virus-break-" + subroutine.Ordinal, ActionKind.BreakSubroutines,
					new RunBreakParameters(new(virus.Id), 1, subroutineId: new("lab-sub-" + subroutine.Ordinal), subroutineOrdinals: [subroutine.Ordinal]), revision) with
				{ Description = $"Spend 1 hosted virus counter to break host subroutine {subroutine.Ordinal + 1}" });
		actions.Add(Action("pass", ActionKind.PassPriority, new NoActionParameters(), revision));
		return view with { Decision = Decision(PlayerSide.Runner, revision, DecisionKind.RunTiming, "Spend hosted virus counters"), LegalActions = actions };
	}
}

public partial class Main
{
	private void SubmitHostedVirusLab(MatchViewModel view, LegalActionViewModel action)
	{
		if (action.Kind == ActionKind.PassPriority)
		{
			PublishLabView(view with { Run = null, Decision = null, LegalActions = [] });
			_tableLastAction.Text = "Encounter practice complete · reset to repeat";
			return;
		}
		if (action.Parameters is not RunBreakParameters broken) return;
		EncounterViewModel encounter = view.Run!.Encounter!;
		IReadOnlyList<int> targets = BoardIcebreakerPresenter.BrokenOrdinals(encounter, action);
		view = view with
		{
			RunnerRig = view.RunnerRig.Select(card => card.Id != broken.BreakerId.Value ? card : card with
			{
				TypedCounters = card.TypedCounters.Select(counter => counter.Purpose == CardCounterPurpose.Virus
					? counter with { Count = counter.Count - 1 } : counter).Where(counter => counter.Count > 0).ToArray(),
			}).ToArray(),
			Run = view.Run with
			{
				Encounter = encounter with
				{
					Subroutines = encounter.Subroutines.Select(subroutine =>
				targets.Contains(subroutine.Ordinal) ? subroutine with { Broken = true } : subroutine).ToArray()
				}
			},
		};
		PublishLabView(InteractionLabSamples.RefreshHostedVirusActions(view, _labRevision));
		_tableLastAction.Text = view.RunnerRig.Any(card => card.TypedCounters.Any(counter => counter.Purpose == CardCounterPurpose.Virus))
			? "Spent 1 virus counter · click Practice Botulus to break another subroutine"
			: "No virus counters remain · pass priority or reset to practice again";
	}
}
