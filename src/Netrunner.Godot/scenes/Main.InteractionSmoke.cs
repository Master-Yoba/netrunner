using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private const int SmokeInputDevice = 47012;
	private bool _isolateSmokeInput;
	private bool _smokeFocusLoss;
	public override void _Input(InputEvent @event)
	{
		// Native smoke events must not race the desktop's real pointer.
		if (_isolateSmokeInput && @event.Device != SmokeInputDevice) GetViewport().SetInputAsHandled();
	}

	private async Task RunInteractionSmokeAsync()
	{
		try
		{
			_isolateSmokeInput = true;
			_boardDrag!.InputFilter = input => input.Device == SmokeInputDevice;
			_boardDrag.FocusLossFilter = () => _smokeFocusLoss;
			_boardDrag.GestureEnded += (outcome, card) => GD.Print($"M411_GESTURE scenario={_labScenario} card={card} outcome={outcome}");
			OpenInteractionLab();
			_reducedMotion.ButtonPressed = true;
			await CheckCompactBoardLayout();
			await CheckHandConditionFeedback();
			await CheckOpponentPlayback();
			await CheckLabScenarioPicker();
			await CheckInstallChoiceControls();
			await CheckActionLabels();
			await CheckBoardIcebreakers();
			await CheckHostedVirusCounters();
			await CheckTargetedAbilityPayment();
			await CheckInteractionGuidance();
			await CheckSpatialActions();
			foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
			{
				GetWindow().Size = size;
				GetWindow().ContentScaleSize = size;
				foreach (int scenario in Enumerable.Range(0, 15))
				{
					GD.Print($"M411_SCENARIO size={size.X} scenario={scenario}");
					_labScenario = scenario;
					ResetInteractionLab();
					await TableFrames(4);
					AssertBoardCardAlignment(_currentSnapshot!.View);
					var projection = BoardInteractionProjector.Project(_currentSnapshot!.View);
					RequireInteraction(projection.Sources.Count > 0, "Lab scenario has no draggable source.");
					BoardInteractionSource source = projection.Sources.Single(s => s.CardId == "lab-hand");
					TableCardControl card = _gameTable.VisibleCards[source.CardId];
					Vector2 start = card.GetGlobalTransform() * new Vector2(12, 45);
					// Cancel once before every valid drop, proving no submission or loss of source.
					await LabPointer(start);
					LabButton(start, true);
					await LabPointer(start + new Vector2(0, -40));
					RequireInteraction(_boardDrag!.IsDragging, $"Drag did not start for scenario {scenario}.");
					RequireInteraction(_boardDrag.GetNode<TableCardControl>("DragPreview").GetGlobalRect().GetCenter()
						.DistanceTo(start + new Vector2(0, -40)) < 0.1f, "Drag preview is not centered on the pointer.");
					GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Escape, Pressed = true });
					LabButton(start, false);
					await TableFrames(2);
					RequireInteraction(!_boardDrag.IsDragging && _labSubmissionCount == 0, "Cancellation submitted or retained a drag.");
					await LabPointer(start);
					LabButton(start, true);
					await LabPointer(start + new Vector2(0, -40));
					BoardInteractionDestination destination = source.Destinations[0];
					RequireInteraction(_boardDrag.IsDragging, $"Second drag did not start for scenario {scenario}.");
					Rect2? rect = _gameTable.GetDropRect(destination.Kind.ToString(), destination.TargetId);
					for (int scroll = 0; rect is null && scroll < 100; scroll++)
					{
						Rect2 board = _gameTable.GetGlobalRect();
						_gameTable.ScrollDragEdges(new Vector2(board.End.X - 12, board.Position.Y + 180), .05, 60, 1500);
						await TableFrames(1);
						rect = _gameTable.GetDropRect(destination.Kind.ToString(), destination.TargetId);
					}
					RequireInteraction(rect is { } area && area.HasArea(), $"No visible destination for scenario {scenario}.");
					Vector2 drop = destination.Kind == BoardInteractionDestinationKind.GeneralPlay
						? _gameTable.GetDropRect("RunnerRig", null)!.Value.GetCenter() : rect!.Value.GetCenter();
					await LabPointer(drop);
					if (OS.GetCmdlineUserArgs().Contains("--m4-11-screenshots") && scenario is 1 or 3 or 6 or 7)
						await SaveInteractionEvidence($"drag-{scenario}-{size.X}");
					LabButton(drop, false);
					await TableFrames(3);
					if (scenario == 1)
					{
						RequireInteraction(_boardChoiceServerCues.Count > 0, "General run-event drop did not stage server choices.");
						Vector2 target = _boardChoiceServerCues[0].GetGlobalRect().GetCenter();
						await LabPointer(target); LabButton(target, true); LabButton(target, false);
					}
					else if (scenario == 8)
					{
						RequireInteraction(TrySelectBoardChoiceCard("preview-root-0"), "Replacement card was not selectable.");
						await TableFrames(2);
						// Finalization must remain an explicit issued action, also available in backup controls.
						var finish = _currentSnapshot!.View.LegalActions.Single();
						RequireInteraction(_tableActionButtons.ContainsKey(finish.Id), "Backup finalize action missing.");
						await ClickTableButton(_boardChoiceOptions!.GetChildren().OfType<Button>().Single());
					}
					else if (scenario is 9 or 10 or 13 or 14)
					{
						BoardSpatialAction choice = BoardSpatialActions.Project(_currentSnapshot!.View)[0];
						foreach (BoardTarget target in choice.Targets) await ClickSpatialCard(target.Id);
						RequireInteraction(_labSubmissionCount == 1, "Target clicks submitted before confirmation.");
						await ClickTableButton(_spatialConfirmButtons[choice.Action.Id]);
					}
					else if (scenario == 11)
						await ClickTableButton(_boardChoiceOptions!.GetChildren().OfType<Button>().First());
					await TableFrames(2);
					RequireInteraction(_labSubmissionCount > 0 && _currentSnapshot!.View.Decision is null,
						$"Scenario {scenario} did not complete: submissions={_labSubmissionCount}, decision={_currentSnapshot?.View.Decision?.Kind}, choices={_boardChoiceVisible}, focus={GetViewport().GuiGetFocusOwner()?.Name}.");
				}
			}
			_labScenario = 0;
			ResetInteractionLab();
			await TableFrames(3);
			Vector2 initial = _gameTable.VisibleCards["lab-hand"].GetGlobalRect().GetCenter();
			await LabPointer(initial); LabButton(initial, true); await LabPointer(initial + new Vector2(0, -40));
			_labRevision++;
			PublishLabView(InteractionLabSamples.Create(0, _labRevision));
			LabButton(initial, false);
			RequireInteraction(!_boardDrag!.IsDragging && _labSubmissionCount == 0, "Stale gesture submitted after decision revision.");
			foreach (string cancellation in new[] { "right-click", "focus-loss", "invalid-drop" })
			{
				ResetInteractionLab();
				await TableFrames(3);
				initial = _gameTable.VisibleCards["lab-hand"].GetGlobalRect().GetCenter();
				await LabPointer(initial); LabButton(initial, true); await LabPointer(initial + new Vector2(0, -40));
				if (cancellation == "right-click")
					GetViewport().PushInput(new InputEventMouseButton { Device = SmokeInputDevice, Position = initial, ButtonIndex = MouseButton.Right, Pressed = true });
				else if (cancellation == "focus-loss")
				{
					_smokeFocusLoss = true;
					GetWindow().EmitSignal(Window.SignalName.FocusExited);
					_smokeFocusLoss = false;
				}
				else await LabPointer(initial);
				LabButton(initial, false);
				await TableFrames(2);
				RequireInteraction(!_boardDrag!.IsDragging && _labSubmissionCount == 0, $"{cancellation} submitted an action.");
			}
			_labScenario = 3;
			ResetInteractionLab();
			_labRevision++;
			MatchViewModel positional = InteractionLabSamples.Create(3, _labRevision) with
			{
				Servers = [new ServerViewModel("server:4", ServerKind.Headquarters, [null], [])
				{
					IcePositions = [new("card:hidden-host", "server:4", 0)],
				}],
				LegalActions = [InteractionLabSamples.Action("hidden-host", ActionKind.InstallCard,
					new InstallCardParameters(new("lab-hand"), InstallDestinationKind.RunnerRig, null, false, 3, hostCardId: new("card:hidden-host")), _labRevision)],
			};
			PublishLabView(positional);
			await TableFrames(4);
			RequireInteraction(_gameTable.GetDropRect("ServerRoot", "server:4") is not null, "Exact server ID lost its root drop region.");
			Vector2 hiddenHost = _gameTable.GetDropRect("HostCard", "card:hidden-host")!.Value.GetCenter();
			initial = _gameTable.VisibleCards["lab-hand"].GetGlobalRect().GetCenter();
			await LabPointer(initial); LabButton(initial, true); await LabPointer(hiddenHost);
			RequireInteraction(_boardDrag!.IsDragging, "Positional hidden host did not allow dragging.");
			LabButton(hiddenHost, false);
			await TableFrames(3);
			RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot!.View.RunnerRig.Any(c => c.HostCardId == "card:hidden-host"),
				"Drop onto an exposed face-down ICE position failed.");
			await CheckDragFollowing();
			await CheckBoardRefinements();
			await CheckDrawMotion();
			await CheckHandDiscard();
			await CheckDiscardMotion();
			await CheckReviewInteractionGuards();
			GD.Print("M411_INTERACTION PASS scenarios=15 resolutions=2 drag=true cancel=true stale=true choices=true menu_fallback=true hidden_host=true focus_loss=true invalid_drop=true centered=true smooth_follow=true");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			if (OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots")) await SaveRefinementEvidence("failure");
			GD.PrintErr($"M411_INTERACTION FAIL {error}");
			GetTree().Quit(1);
		}
	}

	private async Task CheckInstallChoiceControls()
	{
		// Same public choice as the Cleaver/Carmen playtest at action 76.
		// Keep the recorded game and official card data out of the fixture.
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			_labScenario = 2;
			ResetInteractionLab();
			MatchViewModel turn = _currentSnapshot!.View;
			turn = turn with
			{
				RunnerHand = turn.RunnerHand with { VisibleCards = [turn.RunnerHand.VisibleCards[0] with { Title = "Cleaver" }] },
				RunnerRig = [turn.RunnerRig[0] with { Title = "Carmen" }],
			};
			PublishLabView(turn);
			BeginBoardPlayResolution();
			_labRevision++;
			LegalActionViewModel replace = InteractionLabSamples.Action("replace-carmen", ActionKind.InstallCard,
				new InstallCardParameters(new("lab-hand"), InstallDestinationKind.RunnerRig, null, false, 3,
					[new("preview-rig-0")], [], [new("preview-rig-0")], false), _labRevision);
			LegalActionViewModel finish = InteractionLabSamples.Action("keep-carmen", ActionKind.InstallCard,
				new InstallCardParameters(new("lab-hand"), InstallDestinationKind.RunnerRig, null, false, 3,
					[new("preview-rig-0")], [], [], true), _labRevision);
			MatchViewModel choice = turn with
			{
				Decision = InteractionLabSamples.Decision(PlayerSide.Runner, _labRevision, DecisionKind.InstallChoice,
					"Choose cards to trash before installing"),
				LegalActions = [replace, finish],
			};
			PublishLabView(choice);
			await TableFrames(4);
			Button install = _boardChoiceOptions!.GetChildren().OfType<Button>().Single();
			RequireInteraction(install.IsVisibleInTree() && !_tableActionButtons[finish.Id].IsVisibleInTree(),
				"The same install action appears in both the contextual panel and action dock.");
			RequireInteraction(install.Text.Contains("Keep installed cards", StringComparison.Ordinal)
				&& _gameTable.VisibleCards["preview-rig-0"].IsTargetCandidate,
				"The install choice does not explain keeping the optional replacement.");
			RequireInteraction(_boardChoiceDismiss!.Text == "Use action list · Esc", "Mandatory install resolution suggests it can be cancelled.");
			if (OS.GetCmdlineUserArgs().Contains("--install-choice-screenshots"))
				await SaveInteractionEvidence($"install-choice-{size.X}");
			await PressModalEscapeAsync();
			RequireInteraction(!_boardChoiceVisible && _tableActionButtons[finish.Id].IsVisibleInTree()
				&& _tableActionButtons[replace.Id].IsVisibleInTree() && _labSubmissionCount == 0,
				"Escape did not restore the complete action dock without submitting a choice.");
			RequireInteraction(_tableActionButtons[replace.Id].Text.Contains("Select to trash: Carmen", StringComparison.Ordinal)
				&& !_tableActionButtons[finish.Id].Text.Contains("trash", StringComparison.OrdinalIgnoreCase),
				"Replacement selection looks like a second identical install action.");
			if (OS.GetCmdlineUserArgs().Contains("--install-choice-screenshots"))
				await SaveInteractionEvidence($"install-fallback-{size.X}");
			await ClickTableButton(_tableActionButtons[replace.Id]);
			RequireInteraction(_stagedAction?.Id == replace.Id && _tablePrompt.Text.Contains("Carmen", StringComparison.Ordinal)
				&& _labSubmissionCount == 0, "Replacement confirmation lost the selected trash target.");
			CancelStagedAction();
			_boardPlayResolutionTracking = true;
			RefreshBoardChoices(_currentSnapshot!);
			await TableFrames(2);
			await ClickTableButton(_boardChoiceOptions!.GetChildren().OfType<Button>().Single());
			RequireInteraction(_labSubmissionCount == 1 && !_boardChoiceVisible && _tableActions.IsVisibleInTree()
				&& _currentSnapshot!.View.RunnerRig.Any(card => card.Title == "Cleaver")
				&& _currentSnapshot.View.RunnerRig.Any(card => card.Title == "Carmen"),
				"Finalizing did not submit once, preserve Carmen, and restore ordinary controls.");
		}
		GD.Print("INSTALL_CHOICE_CONTROLS PASS resolutions=2 single_controls=true replacement_labels=true escape_fallback=true confirmation=true submit_once=true");
	}

	private async Task CheckDragFollowing()
	{
		_labScenario = 0;
		ResetInteractionLab();
		await TableFrames(3);
		Vector2 start = _gameTable.VisibleCards["lab-hand"].GetGlobalRect().GetCenter();
		Vector2 parked = start + new Vector2(0, -40);
		Vector2 target = parked + new Vector2(-180, -120);
		await LabPointer(start); LabButton(start, true); await LabPointer(parked);
		BoardDragController drag = _boardDrag!;
		TableCardControl preview = drag.GetNode<TableCardControl>("DragPreview");
		float savedSmoothing = drag.Appearance.FollowSmoothing;
		drag.SetProcess(false);
		try
		{
			Vector2? firstResult = null;
			foreach (int fps in new[] { 60, 120 })
			{
				drag.ReducedMotion = true;
				Move(parked); drag._Process(1.0 / fps);
				RequireInteraction(preview.GetGlobalRect().GetCenter().DistanceTo(parked) < 0.1f, "Reduced motion did not center immediately.");
				drag.ReducedMotion = false;
				Move(target); drag._Process(1.0 / fps);
				Vector2 center = preview.GetGlobalRect().GetCenter();
				RequireInteraction(center.DistanceTo(parked) > 1 && center.DistanceTo(target) > 1
					&& center.DistanceTo(target) < parked.DistanceTo(target), "Following jumped or moved away from the pointer.");
				for (int step = 1; step < fps / 10; step++) drag._Process(1.0 / fps);
				center = preview.GetGlobalRect().GetCenter();
				RequireInteraction(center.DistanceTo(target) < 25, "Following is too slow to catch the pointer.");
				if (firstResult is { } previous)
					RequireInteraction(center.DistanceTo(previous) < 0.1f, "Following depends on frame rate.");
				firstResult = center;
			}
			drag.Appearance.FollowSmoothing = 0;
			Move(parked); drag._Process(1.0 / 60);
			RequireInteraction(preview.GetGlobalRect().GetCenter().DistanceTo(parked) < 0.1f, "Zero smoothing did not track immediately.");
			drag.Appearance.FollowSmoothing = savedSmoothing;
			Move(target);
			// Release before the next preview frame: the pointer, not the lagging
			// card image, must determine where the action is dropped.
			LabButton(target, false);
			RequireInteraction(_labSubmissionCount == 1, "Smooth following changed drop hit testing.");
		}
		finally
		{
			drag.Appearance.FollowSmoothing = savedSmoothing;
			drag.ReducedMotion = _reducedMotion.ButtonPressed;
			drag.Cancel("smoke-cleanup", false);
			drag.SetProcess(true);
		}
		void Move(Vector2 point) => GetViewport().PushInput(new InputEventMouseMotion { Device = SmokeInputDevice, Position = point, GlobalPosition = point });
	}

	private async Task LabPointer(Vector2 point)
	{
		GetViewport().PushInput(new InputEventMouseMotion { Device = SmokeInputDevice, Position = point, GlobalPosition = point });
		await TableFrames(2);
	}
	private void LabButton(Vector2 point, bool pressed) => GetViewport().PushInput(new InputEventMouseButton
	{
		Device = SmokeInputDevice,
		Position = point,
		GlobalPosition = point,
		ButtonIndex = MouseButton.Left,
		Pressed = pressed,
	});
	private static void RequireInteraction(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task SaveInteractionEvidence(string name)
	{
		await TableFrames(2);
		RenderingServer.ForceDraw();
		string directory = InteractionEvidenceDirectory("m4-11");
		DirAccess.MakeDirRecursiveAbsolute(directory);
		using Image image = GetViewport().GetTexture().GetImage();
		image.SavePng(System.IO.Path.Combine(directory, name + ".png"));
	}

	private string InteractionEvidenceDirectory(string milestone)
	{
		bool artwork = _gameTable.VisibleCards.Values.Any(card => card.HasFullCardArtwork);
		return ProjectSettings.GlobalizePath($"res://../../{(artwork ? "artifacts" : "docs/evidence")}/{milestone}");
	}
}
