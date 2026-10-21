using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckActionLabels()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			_labScenario = 1;
			ResetInteractionLab();
			MatchViewModel view = _currentSnapshot!.View;
			PublishLabView(view with
			{
				RunnerHand = view.RunnerHand with { VisibleCards = [view.RunnerHand.VisibleCards[0] with { Title = "Jailbreak" }] },
			});
			SelectTableCard("lab-hand");
			await TableFrames(3);
			Button[] runs = _tableActionButtons.Values.ToArray();
			RequireInteraction(runs.Length == view.Servers.Count && runs.Select(button => button.Text).Distinct().Count() == runs.Length
				&& runs.All(button => button.Text.Contains("Jailbreak → Run", StringComparison.Ordinal)),
				"Run-event actions omit or duplicate their server destinations.");
			if (OS.GetCmdlineUserArgs().Contains("--action-label-screenshots")) await SaveInteractionEvidence($"run-labels-{size.X}");
			await ClickTableButton(runs[0]);
			RequireInteraction(_tablePrompt.Text.Contains("Jailbreak → Run", StringComparison.Ordinal) && _labSubmissionCount == 0,
				"Confirmation lost the run-event destination or submitted prematurely.");
			CancelStagedAction();
			_labScenario = 17;
			ResetInteractionLab();
			view = _currentSnapshot!.View;
			PublishLabView(view with
			{
				RunnerHand = view.RunnerHand with
				{
					VisibleCards = view.RunnerHand.VisibleCards.Select(card => card with { Title = "Same-name card" }).ToArray(),
				}
			});
			_tableAllActions = true;
			RenderCardTableActions(_currentSnapshot!);
			await TableFrames(3);
			Button[] discards = _tableActionButtons.Values.ToArray();
			RequireInteraction(discards.Select(button => button.Text).Distinct().Count() == discards.Length
				&& discards.All(button => button.Text.StartsWith("Discard Same-name card (Grip", StringComparison.Ordinal)),
				"Identical card titles make discard choices indistinguishable.");
			if (OS.GetCmdlineUserArgs().Contains("--action-label-screenshots")) await SaveInteractionEvidence($"discard-labels-{size.X}");
			CardViewModel knownPile = view.RunnerHand.VisibleCards[0] with { Id = "card:999", Title = "Visible heap target" };
			PublishLabView(_currentSnapshot!.View with { RunnerHeap = [knownPile] });
			RequireInteraction(TableActionLabel(InteractionLabSamples.Action("heap-label", ActionKind.ChooseCard,
				new CardChoiceParameters(new("card:999")), _labRevision)).Contains("Visible heap target", StringComparison.Ordinal),
				"An off-board known pile target was mislabeled as face-down.");
			PublishLabView(_currentSnapshot!.View with { RunnerHeap = [knownPile with { IsKnown = false, Title = "SECRET" }] });
			RequireInteraction(!TableActionLabel(InteractionLabSamples.Action("hidden-label", ActionKind.ChooseCard,
				new CardChoiceParameters(new("card:999")), _labRevision)).Contains("SECRET", StringComparison.Ordinal),
				"A label exposed a concealed target's title.");
			RequireInteraction(_labSubmissionCount == 0, "Label inspection submitted a game action.");
			await CheckReviewedActionLabels(size);
		}
		GD.Print("ACTION_LABELS PASS resolutions=2 run_targets=true confirmation=true discard_copies=true pile_names=true hidden_safe=true trigger_copies=true tag_cost=true");
	}

	private async Task CheckReviewedActionLabels(Vector2I size)
	{
		_labScenario = 27;
		ResetInteractionLab();
		MatchViewModel view = _currentSnapshot!.View;
		CardViewModel[] copies = view.RunnerRig.Select(card => card with { Title = "Practice payout card", Type = "Resource" }).ToArray();
		_labRevision++;
		view = view with
		{
			RunnerRig = copies,
			CardSelection = null,
			Decision = InteractionLabSamples.Decision(PlayerSide.Runner, _labRevision, DecisionKind.TriggerOrdering, "Choose the next payout to resolve"),
			LegalActions = copies.Select((card, index) => InteractionLabSamples.Action("order-" + index, ActionKind.OrderTriggers,
				new TriggerOrderParameters(new("trigger:" + index)), _labRevision) with
			{ Source = new(card.Id, card.Title, "payout", "Gain 3 credits") }).ToArray(),
		};
		PublishLabView(view);
		_labScenarios!.SetItemText(_labScenarios.GetItemIndex(27), "Review feedback · label regression fixture");
		_labInstructions!.Text = "Copies with identical titles remain distinguishable by their position on the board.";
		await TableFrames(4);
		Button[] triggers = _tableActionButtons.Values.ToArray();
		RequireInteraction(triggers.Length == 2 && triggers.Select(button => button.Text).Distinct().Count() == 2
			&& triggers.Any(button => button.Text.Contains("(Rig 1)", StringComparison.Ordinal))
			&& triggers.Any(button => button.Text.Contains("(Rig 2)", StringComparison.Ordinal)), "Same-title trigger sources lost their board positions.");
		if (OS.GetCmdlineUserArgs().Contains("--review-feedback-screenshots")) await SaveInteractionEvidence($"review-trigger-copies-{size.X}");
		_labRevision++;
		var ability = new AbilityActionParameters(new(copies[0].Id), new("test:tag-cost"), "spend-tags",
			BehaviorAbilityTiming.AnyPaidWindow, BehaviorAbilityActivation.PaidAbility, [new(BehaviorCostKind.Tags, 2)], [],
			new(BehaviorAbilityUsageLimit.Unlimited, 0), new string('a', 64), new string('b', 64), new("lab-rig"), ZoneKind.RunnerRig);
		PublishLabView(view with
		{
			Runner = view.Runner with { Tags = 2 },
			Decision = InteractionLabSamples.Decision(PlayerSide.Runner, _labRevision, DecisionKind.TurnAction, "Practice a paid tag cost"),
			LegalActions = [InteractionLabSamples.Action("tag-cost", ActionKind.UseAbility, ability, _labRevision) with { EffectLabel = "Gain 2 credits" }],
		});
		_labInstructions.Text = "This ability removes two existing tags as its cost.";
		await TableFrames(3);
		await ClickSpatialCard(copies[0].Id);
		RequireInteraction(_spatialOptions!.GetChildren().OfType<Button>().Single().Text.Contains("Cost: Remove 2 tags", StringComparison.Ordinal)
			&& _labSubmissionCount == 0, "The card ability still describes paying tags as taking tags.");
		if (OS.GetCmdlineUserArgs().Contains("--review-feedback-screenshots")) await SaveInteractionEvidence($"review-tag-cost-{size.X}");
	}
}
