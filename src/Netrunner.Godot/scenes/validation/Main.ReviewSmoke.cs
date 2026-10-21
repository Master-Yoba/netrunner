using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckReviewInteractionGuards()
	{
		_reducedMotion.ButtonPressed = true;
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			_labScenario = 0;
			ResetInteractionLab();
			MatchViewModel turn = _currentSnapshot!.View;
			CardViewModel installCard = UiPreviewSamples.Known("review-install", "Program", "Confirm this install");
			LegalActionViewModel install = InteractionLabSamples.Action("confirm-install", ActionKind.InstallCard,
				new InstallCardParameters(new(installCard.Id), InstallDestinationKind.RunnerRig, null, false, 3), _labRevision);
			// An empty rig: the first install's drop area must still be the whole rig box (playtest, 30 September).
			PublishLabView(turn with
			{
				RunnerHand = turn.RunnerHand with { Count = 2, VisibleCards = [.. turn.RunnerHand.VisibleCards, installCard] },
				RunnerRig = [],
				LegalActions = [.. turn.LegalActions, install],
			});
			await TableFrames(4);
			await ClickTableButton(_tableActions.GetChildren().OfType<Button>().Single(button => button.Text == "All legal actions…"));
			await ClickTableButton(_tableActionButtons[install.Id]);
			RequireInteraction(_stagedAction?.Id == install.Id, "The native menu did not stage the install confirmation.");
			Vector2 start = _gameTable.VisibleCards["lab-hand"].GetGlobalRect().GetCenter();
			Rect2 rig = _gameTable.GetDropRect("RunnerRig", null)!.Value;
			// Playtest, 30 September: with the rig empty the drop area shrank to a sliver.
			RequireInteraction(rig.Size.Y >= _gameTable.VisibleCards["lab-hand"].Size.Y * 0.8f,
				$"The rig's drop area {rig} is smaller than a card.");
			Vector2 drop = rig.GetCenter();
			await LabPointer(start); LabButton(start, true); await LabPointer(drop);
			LabButton(drop, false);
			await TableFrames(2);
			RequireInteraction(!_boardDrag!.IsDragging && _labSubmissionCount == 0 && _stagedAction?.Id == install.Id,
				"A hand drag bypassed the pending menu confirmation.");
			await ClickTableButton(_tableActions.GetChildren().OfType<Button>().Single(button => button.Text == "Back"));
			await LabPointer(start); LabButton(start, true); await LabPointer(drop);
			RequireInteraction(_boardDrag.IsDragging, "Cancelling confirmation did not restore dragging.");
			// If another input stages confirmation mid-gesture, release must also fail closed.
			StageTargetAction(_currentSnapshot!.View.Decision!, install);
			LabButton(drop, false);
			await TableFrames(2);
			RequireInteraction(!_boardDrag.IsDragging && _labSubmissionCount == 0, "A drag release ignored a newly staged confirmation.");
			CancelStagedAction();
			await LabPointer(start); LabButton(start, true); await LabPointer(drop); LabButton(drop, false);
			await TableFrames(3);
			RequireInteraction(_labSubmissionCount == 1, "The legal drag did not work after cancelling confirmation.");

			foreach (int scenario in new[] { 1, 11 })
			{
				_labScenario = scenario;
				ResetInteractionLab();
				MatchViewModel view = _currentSnapshot!.View;
				if (scenario == 1) StageBoardPlayChoices(view.Decision!, view.LegalActions);
				else
				{
					BeginBoardPlayResolution();
					SubmitInteractionLab(view.Decision!, view.LegalActions[0]);
				}
				await TableFrames(3);
				int before = _labSubmissionCount;
				foreach (bool submission in new[] { false, true })
				{
					try
					{
						if (submission) Interlocked.Exchange(ref _submissionInFlight, 1);
						else { _tableAwaitingSettlement = true; _tablePlaybackWait = 10; }
						await TableFrames(2);
						RequireInteraction(_boardChoiceInputEnabled == false && !_boardChoiceServerCues.Any(cue => cue.Visible)
							&& _boardChoiceOptions!.GetChildren().OfType<Button>().All(button => button.Disabled),
							"Contextual controls remained enabled while waiting for settlement/submission.");
						RequireInteraction(!TrySelectBoardChoiceServer(view.Servers[0].Id)
							&& !TrySelectBoardChoiceCard(scenario == 13 ? "preview-heap" : "preview-rig-0"),
							"A busy contextual card/server click was accepted.");
						if (scenario == 10) RequireInteraction(!_gameTable.VisibleCards["preview-rig-0"].IsTargetCandidate,
							"The busy board still advertises a selectable card target.");
						if (scenario == 13) RequireInteraction(_boardChoiceOptions!.FindChildren("*", "Control", true, false)
							.OfType<TableCardControl>().All(card => !card.IsTargetCandidate && card.Modulate.A < 1),
							"The busy pile-choice browser still advertises an enabled target.");
						if (scenario == 11)
						{
							Button option = _boardChoiceOptions!.GetChildren().OfType<Button>().First();
							await ClickTableButton(option);
							option.EmitSignal(BaseButton.SignalName.Pressed);
							if (!submission && size.X == 1920 && OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots"))
								await SaveRefinementEvidence("choice-settling-1920");
						}
						SubmitBoardChoiceAction(_boardChoiceDecision!, _boardChoiceActions[0]);
						RequireInteraction(_labSubmissionCount == before, "A contextual button bypassed the busy guard.");
					}
					finally
					{
						Interlocked.Exchange(ref _submissionInFlight, 0);
						_tableAwaitingSettlement = false;
						_tablePlaybackWait = 0;
					}
					await TableFrames(2);
					RequireInteraction(_boardChoiceInputEnabled == true
						&& _boardChoiceOptions!.GetChildren().OfType<Button>().All(button => !button.Disabled),
						"Contextual input did not recover when the board became ready.");
				}
				SubmitBoardChoiceAction(_boardChoiceDecision!, _boardChoiceActions[0]);
				RequireInteraction(_labSubmissionCount == before + 1, "The recovered contextual action did not submit exactly once.");
			}
		}
		GD.Print("M412_REVIEW_GUARDS PASS resolutions=2 confirmation=true mid_drag=true settling=true submission=true buttons=true cards=true servers=true recovery=true");
	}
}
