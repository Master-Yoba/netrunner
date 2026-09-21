using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckBoardRefinements()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (PlayerSide side in new[] { PlayerSide.Runner, PlayerSide.Corp })
			{
				_labScenario = side == PlayerSide.Runner ? 15 : 16;
				ResetInteractionLab();
				await TableFrames(6);
				RequireInteraction(_gameTable.Hand.GlobalPosition.Y >= _gameTable.PlayArea.GetGlobalRect().End.Y,
					"Hand overlaps the main play area at rest.");
				bool corp = side == PlayerSide.Corp;
				BoardActionButton credits = _gameTable.GetNode<BoardActionButton>(corp
					? "Surface/Margins/Layout/CorpStrip/Content/Credits" : "Surface/Margins/Layout/RunnerField/Content/Summary/Content/Credits");
				BoardActionButton deck = corp ? FindDescendants<BoardActionButton>(_gameTable).Single(button => button.Name == "DrawPile")
					: _gameTable.GetNode<BoardActionButton>("Surface/Margins/Layout/RunnerField/Content/Deck");
				RequireInteraction(credits.ActionAvailable && credits.Icon is not null && deck.ActionAvailable, "Local credit/deck glow is missing.");
				RequireInteraction(!_tableActionButtons.Keys.Any(id => _currentSnapshot!.View.LegalActions
					.Any(action => action.Id == id && action.Kind is ActionKind.GainCredit or ActionKind.DrawCard)), "Basic actions remain in the default dock.");
				int before = int.Parse(credits.Text, System.Globalization.CultureInfo.InvariantCulture);
				await ClickTableButton(credits);
				RequireInteraction(_labSubmissionCount == 1 && int.Parse(credits.Text, System.Globalization.CultureInfo.InvariantCulture) == before + 1,
					"Credit click did not submit exactly one current action.");
				await ClickTableButton(deck);
				RequireInteraction(_labSubmissionCount == 2 && _gameTable.Hand.GetNode<Label>("Count").Text == "6", "Deck click did not draw into the hand.");
				BoardActionButton archives = FindDescendants<BoardActionButton>(_gameTable).Single(button => button.Name == "ArchivesPile");
				await LabPointer(archives.GetGlobalRect().GetCenter());
				RequireInteraction(CardHoverPreviewCardId == _currentSnapshot!.View.CorpDiscard[^1].Id, "Known Archives face lost its hover preview.");
				await ClickTableButton(archives);
				RequireInteraction(_tablePileOverlay?.Visible == true && _tablePileTitle.Text == "CORP ARCHIVES", "Archives does not open from its pile.");
				RequireInteraction(!credits.ActionAvailable && !deck.ActionAvailable, "Board actions stayed lit behind a pile browser.");
				HideTablePile();
				await TableFrames(2);
				MatchSnapshot current = _currentSnapshot!;
				RenderSnapshot(current with { IsOpponentThinking = true });
				await TableFrames(2);
				RequireInteraction(!credits.ActionAvailable && !deck.ActionAvailable, "Opponent thinking left local actions active.");
				SubmitBasicBoardAction(side, ActionKind.GainCredit);
				RequireInteraction(_labSubmissionCount == 2, "An unavailable board action submitted.");
				RenderSnapshot(current);
				await TableFrames(3);
				if (!corp)
				{
					Button runButton = _tableActions.GetChildren().OfType<Button>().Single(button => button.Text == "Run");
					await ClickTableButton(runButton);
					RequireInteraction(_boardChoiceRunSelection && _boardChoiceVisible && _labSubmissionCount == 2,
						"Run did not stage server selection without spending a click.");
					RequireInteraction(_boardChoiceServerCues.Count == 4 && !TrySelectBoardChoiceServer("preview-remote-1"), "Run offers an invalid server.");
					RequireInteraction(_boardChoicePrompt!.Text.Contains("start the run immediately", StringComparison.Ordinal)
						&& _boardChoiceServerCues.All(cue => cue.GetNode<Label>("%Caption").Text.Contains("1 click", StringComparison.Ordinal)),
						"Server selection does not explain immediate commitment and the click cost.");
					foreach (PanelContainer cue in _boardChoiceServerCues)
					{
						Label caption = cue.GetNode<Label>("%Caption");
						RequireInteraction(caption.IsVisibleInTree() && caption.GetVisibleLineCount() == caption.GetLineCount()
							&& caption.GetVisibleLineCount() > 0 && cue.GetGlobalRect().Encloses(caption.GetGlobalRect()),
							$"The server action and price are clipped: {caption.Text}, size={caption.Size}, lines={caption.GetVisibleLineCount()}/{caption.GetLineCount()}.");
					}
					if (OS.GetCmdlineUserArgs().Contains("--interaction-guidance-screenshots"))
						await SaveInteractionEvidence($"guidance-run-selection-{size.X}");
					if (OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots") && size.X == 1920)
						await SaveRefinementEvidence("run-selection-1920");
					await ClickTableButton(_boardChoiceDismiss!);
					RequireInteraction(!_boardChoiceVisible && _labSubmissionCount == 2, "Cancel run submitted an action.");
					await ClickTableButton(runButton);
					GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Pressed = true, Keycode = Key.Escape });
					await TableFrames(2);
					RequireInteraction(!_boardChoiceVisible && _labSubmissionCount == 2, "Escape did not cancel Run.");
					await ClickTableButton(runButton);
					MatchViewModel changed = current.View with { Decision = current.View.Decision! with { Revision = current.View.Decision!.Revision + 1 } };
					PublishLabView(changed);
					RequireInteraction(!_boardChoiceVisible && !TrySelectBoardChoiceServer("preview-Archives"), "A stale run selection survived a decision revision.");
					await TableFrames(2);
					StartBoardRunSelection();
					await TableFrames(2);
					PanelContainer target = _boardChoiceServerCues.First(cue => cue.Visible);
					Vector2 point = target.GetGlobalRect().GetCenter();
					await LabPointer(point); LabButton(point, true); LabButton(point, false);
					await TableFrames(3);
					RequireInteraction(_labSubmissionCount == 3 && _currentSnapshot!.View.Decision is null, "Selecting a run server did not submit its issued action.");
				}
				ResetInteractionLab();
				await TableFrames(4);
				_reducedMotion.ButtonPressed = false;
				await LabPointer(new Vector2(20, 20));
				TableCardControl hand = _gameTable.VisibleCards["lab-fan-0"];
				Vector2 resting = hand.Position;
				Vector2 hover = hand.GetGlobalTransform() * new Vector2(12, 45);
				await LabPointer(hover);
				await HandAnimationTime(0.3);
				RequireInteraction(_gameTable.Hand.HoveredCardId == hand.CardId && hand.Position.Y < resting.Y - 20
					&& hand.Scale.X > 1.04f && Mathf.Abs(hand.Rotation) < 0.01f, "Hand hover did not lift and straighten, or oscillated under a stationary pointer.");
				if (OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots")) await SaveRefinementEvidence($"hand-{side.ToString().ToLowerInvariant()}-{size.X}");
				await LabPointer(new Vector2(20, 20));
				await HandAnimationTime(0.4);
				RequireInteraction(hand.Position.DistanceTo(resting) < 1 && _gameTable.Hand.HoveredCardId is null, "Hand hover did not settle back into the fan.");
				if (OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots") && size.X == 1920 && !corp)
					await SaveRefinementEvidence("hand-resting-1920");
				_reducedMotion.ButtonPressed = true;
				await TableFrames(2);
				hand.GrabFocus();
				await TableFrames(2);
				RequireInteraction(hand.Position.Y <= resting.Y - _gameTable.Hand.HoverLift + 0.1f, "Reduced-motion keyboard hover did not update immediately.");
				hand.ReleaseFocus();
				await TableFrames(2);
				MatchViewModel empty = _currentSnapshot!.View with { CorpDiscard = [], CorpDiscardCount = 0 };
				if (corp)
				{
					CardViewModel recovered = _currentSnapshot.View.CorpDiscard[^1];
					empty = empty with
					{
						CorpHand = empty.CorpHand with
						{
							Count = empty.CorpHand.Count + 1,
							VisibleCards = [.. empty.CorpHand.VisibleCards, recovered]
						}
					};
				}
				PublishLabView(empty);
				await TableFrames(3);
				if (corp)
				{
					TableCardControl recovered = _gameTable.VisibleCards[empty.CorpHand.VisibleCards[^1].Id];
					RequireInteraction(recovered.MouseFilter == MouseFilterEnum.Stop && recovered.FocusMode == FocusModeEnum.All,
						"A card returning from Archives to hand lost pointer or keyboard input.");
				}
				await ClickTableButton(archives);
				RequireInteraction(_tablePileOverlay?.Visible == true && !_tablePileCards.GetChildren().OfType<TableCardControl>().Any() && SceneTreeContainsText(_tablePileCards, "This pile is empty."), "Empty Archives is not browsable.");
				HideTablePile();
				PublishLabView(empty with { Servers = empty.Servers.Select(server => server with { Id = "relocated-" + server.Id }).ToArray() });
				await TableFrames(3);
				RequireInteraction(archives.IsVisibleInTree() && deck.IsVisibleInTree(), "Central piles stayed in lanes from a previous board.");
			}
		}
		GD.Print("M412_BOARD_ACTIONS PASS seats=2 resolutions=2 credit=true draw=true archives=true run_cancel=true stale=true hand_hover=true reduced_motion=true");
	}

	private async Task HandAnimationTime(double duration)
	{
		for (double elapsed = 0; elapsed < duration; elapsed += GetProcessDeltaTime()) await TableFrames(1);
	}

	private async Task SaveRefinementEvidence(string name)
	{
		if (DisplayServer.GetName() == "headless") return;
		await TableFrames(2);
		RenderingServer.ForceDraw();
		string directory = InteractionEvidenceDirectory("m4-12");
		DirAccess.MakeDirRecursiveAbsolute(directory);
		using Image screenshot = GetViewport().GetTexture().GetImage();
		screenshot.SavePng(System.IO.Path.Combine(directory, name + ".png"));
	}
}
