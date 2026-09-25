using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckTargetedAbilityPayment()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (string targetId in new[] { "lab-target-a", "lab-target-b" })
			{
				_labScenario = 21;
				ResetInteractionLab();
				await TableFrames(5);
				MatchSnapshot original = _currentSnapshot!;
				await ClickSpatialCard(original.View.RunnerIdentity!.Id);
				await ClickTableButton(_spatialOptions!.GetChildren().OfType<Button>().Single(button => button.Text.Contains("choose targets", StringComparison.Ordinal)));
				await ClickSpatialCard(targetId);
				await TableFrames(4);
				Button payment = _spatialOptions.GetChildren().OfType<Button>().Single(button => button.HasMeta("payment_card_id"));
				RequireInteraction(_spatialSelection!.Selected.Count == 1 && _spatialSelection.Selected[0].Id == targetId
					&& _spatialConfirmButtons.Count == 0 && _spatialPendingConfirm is { Disabled: true }
					&& _spatialPendingConfirm.Text.Contains("payment card", StringComparison.Ordinal)
					&& _spatialPanel!.GetNode<Label>("%Prompt").Text.Contains("card from your hand to trash as payment", StringComparison.Ordinal)
					&& payment.GetMeta("payment_card_id").AsString() == "lab-card-cost"
					&& payment.Text.Contains("from hand", StringComparison.Ordinal)
					&& _gameTable.VisibleCards["lab-card-cost"].IsTargetCandidate,
					"Selecting the ability target did not expose the remaining hand-card payment.");
				RequireInteraction(_spatialPanel!.GetGlobalRect().Encloses(payment.GetGlobalRect())
					&& _spatialPanel.GetGlobalRect().Encloses(_spatialPendingConfirm!.GetGlobalRect()),
					"The payment choice or pending confirmation was clipped.");
				_spatialPendingConfirm!.EmitSignal(BaseButton.SignalName.Pressed);
				RequireInteraction(_labSubmissionCount == 0 && ReferenceEquals(original, _currentSnapshot),
					"An incomplete selection paid or submitted the ability.");
				if (OS.GetCmdlineUserArgs().Contains("--ability-payment-screenshots") && targetId == "lab-target-a")
					await SaveInteractionEvidence($"ability-payment-pending-{size.X}");
				// The panel shortcut and the physical hand card stage the same issued payment.
				if (targetId == "lab-target-a") await ClickTableButton(payment);
				else await ClickSpatialCard("lab-card-cost");
				Button confirm = _spatialConfirmButtons.Values.Single();
				RequireInteraction(!confirm.Disabled && _spatialPendingConfirm is null
					&& _spatialSelection.CompleteActions.Single().Action.Parameters is AbilityActionParameters ability
					&& ability.TargetCardIds.Single().Value == targetId
					&& ability.AdditionalCostSelectionsOrEmpty.Single().CardIds.Single().Value == "lab-card-cost"
					&& confirm.Text.Contains("Trash Practice payment card", StringComparison.Ordinal)
					&& _labSubmissionCount == 0 && ReferenceEquals(original, _currentSnapshot),
					"Payment selection did not enable the exact action or paid before confirmation.");
				if (OS.GetCmdlineUserArgs().Contains("--ability-payment-screenshots") && targetId == "lab-target-a")
					await SaveInteractionEvidence($"ability-payment-ready-{size.X}");
				await ClickTableButton(confirm);
				RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot!.View.Decision is null,
					"The complete targeted ability did not submit exactly once.");
			}
		}
		GD.Print("ABILITY_PAYMENT PASS resolutions=2 targets=2 next_step=true pending_confirm=true shortcut=true hand=true no_early_payment=true");
	}
}
