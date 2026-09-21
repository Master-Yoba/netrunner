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
		}
		GD.Print("ACTION_LABELS PASS resolutions=2 run_targets=true confirmation=true discard_copies=true pile_names=true hidden_safe=true");
	}
}
