using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckInteractionGuidance()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			_labScenario = 27;
			ResetInteractionLab();
			MatchViewModel view = _currentSnapshot!.View;
			PublishLabView(InteractionLabSamples.RefreshSpatialIncremental(view with
			{
				CardSelection = view.CardSelection! with { Cardinality = BehaviorChoiceCardinalityKind.Exactly, Count = 2 },
			}, ++_labRevision));
			_labScenarios!.SetItemText(_labScenarios.GetItemIndex(27), "Select exactly two cards · regression fixture");
			_labInstructions!.Text = "Select two highlighted programs, then confirm. Click a selected card to undo; Esc resets the local selection.";
			await TableFrames(5);
			CheckPending("2 more cards");
			await ClickSpatialCard("lab-target-a");
			CheckPending("1 more card");
			RequireInteraction(!CardHoverPreviewVisible, "Selecting an incremental target left its preview over the next card.");
			await LabPointer(new Vector2(20, 20));
			await LabPointer(_gameTable.VisibleCards["lab-target-b"].GetGlobalRect().GetCenter());
			RequireInteraction(CardHoverPreviewCardId == "lab-target-b", "Incremental selection prevented previewing the next candidate.");
			await LabPointer(new Vector2(20, 20));
			RequireInteraction(_spatialPanel!.GetNode<Label>("%Prompt").Text.Contains("exactly 2 cards", StringComparison.Ordinal),
				"Exact selection did not explain its required count.");
			if (OS.GetCmdlineUserArgs().Contains("--interaction-guidance-screenshots"))
				await SaveInteractionEvidence($"guidance-exact-selection-{size.X}");
			await ClickSpatialCard("lab-target-b");
			Button confirm = _spatialOptions!.GetChildren().OfType<Button>().Single();
			RequireInteraction(!confirm.Disabled && confirm.Text == "Confirm selection · 2 cards" && _labSubmissionCount == 0,
				"A complete local selection did not expose confirmation without paying.");
			await ClickSpatialCard("lab-target-a");
			CheckPending("1 more card");
			await PressModalEscapeAsync();
			CheckPending("2 more cards");
			await ClickSpatialCard("lab-target-b");
			await ClickSpatialCard("lab-target-a");
			await ClickTableButton(_spatialOptions.GetChildren().OfType<Button>().Single());
			await TableFrames(12);
			RequireInteraction(_labSubmissionCount == 3 && _currentSnapshot!.View.Decision is null,
				"Exact selection did not submit two fresh additions and one final confirmation.");
			foreach (BehaviorChoiceCardinalityKind cardinality in new[] { BehaviorChoiceCardinalityKind.AnyNumber, BehaviorChoiceCardinalityKind.UpTo })
			{
				ResetInteractionLab();
				view = _currentSnapshot!.View;
				PublishLabView(InteractionLabSamples.RefreshSpatialIncremental(view with
				{
					CardSelection = view.CardSelection! with { Cardinality = cardinality, Count = cardinality == BehaviorChoiceCardinalityKind.UpTo ? 2 : null },
				}, ++_labRevision));
				await TableFrames(4);
				Button skip = _spatialOptions.GetChildren().OfType<Button>().Single();
				RequireInteraction(!skip.Disabled && skip.Text == "Continue without selecting cards", "An empty allowed selection is unclear or unavailable.");
				await ClickTableButton(skip);
				await TableFrames(5);
				RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot!.View.Decision is null, "Continue without cards did not finalize exactly once.");
			}
		}
		GD.Print("INTERACTION_GUIDANCE PASS resolutions=2 exact=true partial=true undo=true escape=true zero_selection=true hover_dismiss=true");

		void CheckPending(string remaining)
		{
			RequireInteraction(_spatialPendingConfirm is { Disabled: true } pending && pending.IsVisibleInTree()
				&& pending.Text.Contains(remaining, StringComparison.Ordinal) && _labSubmissionCount == 0,
				"Incomplete selection hid or enabled confirmation, or lost its remaining count.");
			RequireInteraction(_spatialPanel!.GetGlobalRect().Encloses(_spatialPendingConfirm!.GetGlobalRect()), "Pending confirmation is clipped.");
			_spatialPendingConfirm.EmitSignal(BaseButton.SignalName.Pressed);
			RequireInteraction(_labSubmissionCount == 0, "Pending confirmation submitted an incomplete selection.");
		}
	}
}
