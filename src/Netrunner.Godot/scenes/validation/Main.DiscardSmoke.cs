using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckHandDiscard()
	{
		_reducedMotion.ButtonPressed = true;
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (bool corp in new[] { false, true })
			{
				_labScenario = corp ? 18 : 17;
				ResetInteractionLab();
				await TableFrames(4);
				BoardActionButton pile = corp
					? FindDescendants<BoardActionButton>(_gameTable).Single(button => button.Name == "ArchivesPile")
					: _gameTable.GetNode<BoardActionButton>("Surface/Margins/Layout/RunnerField/Content/Discard");
				RequireInteraction(_tableActionButtons.Count == 0 && BoardInteractionProjector.Project(_currentSnapshot!.View).Sources.Count == 7,
					"Discard still requires menu buttons or has missing hand sources.");
				RequireInteraction(_gameTable.VisibleCards["lab-discard-0"].IsActionableSource, "Discard candidates have no white source glow.");
				await ClickTableButton(pile);
				RequireInteraction(_tablePileOverlay?.Visible == true, "Empty discard pile is not browsable.");
				HideTablePile();
				await TableFrames(2);
				await BeginDiscardDrag("lab-discard-0");
				Vector2 wrong = _gameTable.GetDropRect(corp ? "RunnerHeap" : "CorpArchives", null)!.Value.GetCenter();
				await LabPointer(wrong); LabButton(wrong, false);
				await TableFrames(3);
				RequireInteraction(_labSubmissionCount == 0 && !_boardDrag!.IsDragging && _gameTable.VisibleCards["lab-discard-0"].Visible,
					"Dropping on the opponent's pile submitted or lost the hand card.");

				for (int choice = 0; choice < 2; choice++)
				{
					await BeginDiscardDrag($"lab-discard-{choice}");
					Vector2 drop = _gameTable.GetDropRect(corp ? "CorpArchives" : "RunnerHeap", null)!.Value.GetCenter();
					await LabPointer(drop);
					PanelContainer cue = _boardDrag!.GetChildren().OfType<PanelContainer>().Single();
					RequireInteraction(cue.GetGlobalRect().Size.IsEqualApprox(pile.Size), "Discard cue extends outside the actual pile.");
					if (choice == 0 && size.X == 1920 && OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots"))
						await SaveRefinementEvidence(corp ? "discard-corp-1920" : "discard-runner-1920");
					LabButton(drop, false);
					await TableFrames(4);
					RequireInteraction(_labSubmissionCount == choice + 1 && !_boardChoiceVisible && _tablePileOverlay?.Visible != true,
						"Discard drop did not submit exactly once, or opened an unrelated popup.");
					if (choice == 0)
					{
						MatchViewModel view = _currentSnapshot!.View;
						RequireInteraction((corp ? view.CorpHand : view.RunnerHand).Count == 7
							&& BoardInteractionProjector.SelectedDiscards(view).Single().Id == "lab-discard-0"
							&& !_gameTable.VisibleCards.ContainsKey("lab-discard-0")
							&& _gameTable.VisibleCards.Values.Count(_gameTable.Hand.ContainsCard) == 6,
							"A pending discard changed authoritative hand size or stayed in the fan.");
						RequireInteraction(pile.GetNode<Label>("Pending").Visible && pile.GetNode<Label>("Pending").Text.Contains("1 selected", StringComparison.Ordinal)
							&& HandDiscardPrompt(view.Decision!).Contains("Drag 1 more", StringComparison.Ordinal),
							"Pending discard count or remaining-card instruction is missing.");
						if (!corp && size.X == 1920 && OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots"))
							await SaveRefinementEvidence("discard-pending-1920");
					}
				}
				MatchViewModel complete = _currentSnapshot!.View;
				RequireInteraction(complete.Decision is null && (corp ? complete.CorpHand : complete.RunnerHand).Count == 5
					&& (corp ? complete.CorpDiscard.Count : complete.RunnerHeap.Count) == 2 && !pile.GetNode<Label>("Pending").Visible,
					"Discard set did not finish at the hand limit or left a pending badge.");
				await LabPointer(new Vector2(20, 20));
				await LabPointer(pile.GetGlobalRect().GetCenter());
				RequireInteraction(CardHoverPreviewCardId == "lab-discard-1", "Discard pile top face has no hover preview.");
				await ClickTableButton(pile);
				RequireInteraction(_tablePileCards.GetChildren().OfType<TableCardControl>().Count() == 2, "Discarded cards are missing from the pile browser.");
				HideTablePile();
				ResetInteractionLab();
				await TableFrames(3);
				await ClickTableButton(_tableActions.GetChildren().OfType<Button>().Single(button => button.Text == "All legal actions…"));
				await ClickTableButton(_tableActionButtons.Values.First());
				await ClickTableButton(_tableActions.GetChildren().OfType<Button>().Single(button => button.Text == "Confirm"));
				RequireInteraction(_labSubmissionCount == 1 && BoardInteractionProjector.SelectedDiscards(_currentSnapshot!.View).Count == 1,
					"Discard recovery controls no longer submit the same choices.");
			}
		}
		GD.Print("M412_DISCARD PASS seats=2 resolutions=2 drag=true wrong_pile=true pending=true multi_discard=true browser=true menu_fallback=true");
	}

	private async Task BeginDiscardDrag(string id)
	{
		Vector2 start = _gameTable.VisibleCards[id].GetGlobalTransform() * new Vector2(12, 45);
		await LabPointer(start); LabButton(start, true); await LabPointer(start + new Vector2(0, -40));
		RequireInteraction(_boardDrag!.IsDragging, $"Discard drag did not start for {id}.");
	}
}
