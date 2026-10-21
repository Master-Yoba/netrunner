using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public static partial class InteractionLabSamples
{
	public static MatchViewModel IcebreakerEncounter(long revision)
	{
		MatchViewModel view = UiPreviewSamples.Board(PlayerSide.Runner, UiPreviewSamples.Density.Normal);
		CardViewModel ice = UiPreviewSamples.Known("lab-encounter-ice", "ICE", "Practice Barrier") with
		{ Strength = 3, Subtypes = ["Barrier"], RulesText = "[subroutine] Lose 1[credit].\n[subroutine] End the run.\n[subroutine] End the run." };
		CardViewModel fracter = UiPreviewSamples.Known("lab-fracter", "Program", "Practice Fracter") with
		{ Strength = 1, Subtypes = ["Icebreaker", "Fracter"], RulesText = "1[credit]: +2 strength.\n1[credit]: Break up to 2 barrier subroutines." };
		CardViewModel decoder = UiPreviewSamples.Known("lab-decoder", "Program", "Practice Decoder") with
		{ Strength = 2, Subtypes = ["Icebreaker", "Decoder"], RulesText = "1[credit]: +1 strength.\n1[credit]: Break 1 code gate subroutine." };
		EncounterViewModel encounter = new("lab-encounter", ice, RunEncounterStatus.Initialized,
			[new(0, false, false), new(1, false, false), new(2, false, false)])
		{
			EffectiveStrength = 3,
			Breakers = [new(fracter.Id, fracter.Title, 1, 1) { CanInterfaceEncounter = true, PumpCost = 1, PumpStrength = 2, BreakCost = 1, BreakLimit = 2 },
				new(decoder.Id, decoder.Title, 2, 2) { PumpCost = 1, PumpStrength = 1, BreakCost = 1, BreakLimit = 1 }],
		};
		view = view with
		{
			RunnerRig = [fracter, decoder],
			RunnerHand = new(ZoneKind.RunnerGrip, 0, []),
			Runner = view.Runner with { Credits = 6 },
			Servers = view.Servers.Select(server => server.Kind == ServerKind.Headquarters
				? server with { Ice = [ice] } : server with { Ice = [] }).ToArray(),
			Run = new("lab-run", "preview-Headquarters", PlayerSide.Runner, RunCrPhase.Encounter,
				RunTimingStep.EncounterPaidWindow, 0, ice, false, false, null,
				new(RunPriorityWindowKind.EncounterPaid, PlayerSide.Runner, 0, false, false), encounter,
				new([], 0, null, [], [], RunAccessState.NotStarted), 2, null),
		};
		return RefreshIcebreakerActions(view, revision);
	}

	public static MatchViewModel RefreshIcebreakerActions(MatchViewModel view, long revision)
	{
		var actions = new List<LegalActionViewModel>();
		EncounterViewModel encounter = view.Run!.Encounter!;
		foreach (EncounterBreakerViewModel breaker in encounter.Breakers)
		{
			for (int hosted = 0; hosted <= Math.Min(1, view.Run.RunCredits); hosted++)
			{
				if (view.Runner.Credits < 1 - hosted) continue;
				string payment = $"{hosted} hosted + {1 - hosted} personal credit";
				actions.Add(Action($"pump-{breaker.CardId}-{hosted}", ActionKind.PumpBreaker,
					new RunBreakerParameters(new(breaker.CardId), hosted), revision) with
				{ Description = $"Pump {breaker.CardId} ({payment})" });
				if (breaker.CardId != "lab-fracter" || breaker.EffectiveStrength < encounter.EffectiveStrength) continue;
				foreach (IReadOnlyList<int> targets in SubroutineTargeting.Choices(encounter.Subroutines.Where(sub => !sub.Broken && !sub.Resolved).Select(sub => sub.Ordinal), 2))
					actions.Add(Action($"break-{breaker.CardId}-{string.Join("-", targets)}-{hosted}", ActionKind.BreakSubroutines,
						new RunBreakParameters(new(breaker.CardId), targets.Count, hosted, subroutineOrdinals: targets), revision) with
					{ Description = $"Break subroutines {string.Join(", ", targets.Select(ordinal => ordinal + 1))} with {breaker.CardId} ({payment})" });
			}
		}
		actions.Add(Action("pass", ActionKind.PassPriority, new NoActionParameters(), revision));
		return view with { Decision = Decision(PlayerSide.Runner, revision, DecisionKind.RunTiming, "Encounter paid abilities"), LegalActions = actions };
	}
}

public partial class Main
{
	private void SubmitIcebreakerLab(MatchViewModel view, LegalActionViewModel action)
	{
		if (action.Kind == ActionKind.PassPriority)
		{
			PublishLabView(view with { Run = null, Decision = null, LegalActions = [] });
			_tableLastAction.Text = "Encounter complete · reset to repeat";
			return;
		}
		EncounterViewModel encounter = view.Run!.Encounter!;
		int hosted;
		if (action.Parameters is RunBreakerParameters pump)
		{
			hosted = pump.HostedCredits;
			encounter = encounter with
			{
				Breakers = encounter.Breakers.Select(breaker => breaker.CardId == pump.BreakerId.Value
					? breaker with { EffectiveStrength = breaker.EffectiveStrength + (breaker.CardId == "lab-fracter" ? 2 : 1) } : breaker).ToArray(),
			};
		}
		else if (action.Parameters is RunBreakParameters broken)
		{
			hosted = broken.HostedCredits;
			IReadOnlyList<int> affected = BoardIcebreakerPresenter.BrokenOrdinals(encounter, action);
			encounter = encounter with { Subroutines = encounter.Subroutines.Select(sub => affected.Contains(sub.Ordinal) ? sub with { Broken = true } : sub).ToArray() };
		}
		else return;
		view = view with
		{
			Runner = view.Runner with { Credits = view.Runner.Credits - (1 - hosted) },
			Run = view.Run with { Encounter = encounter, RunCredits = view.Run.RunCredits - hosted }
		};
		PublishLabView(InteractionLabSamples.RefreshIcebreakerActions(view, _labRevision));
		_tableLastAction.Text = "Lab accepted: " + TableActionLabel(action);
	}
}
