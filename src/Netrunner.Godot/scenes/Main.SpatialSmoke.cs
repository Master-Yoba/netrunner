using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckSpatialActions()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (int scenario in Enumerable.Range(20, InteractionLabSamples.SpatialNames.Length))
			{
				GD.Print($"SPATIAL_SCENARIO size={size.X} scenario={scenario}");
				_labScenario = scenario;
				ResetInteractionLab();
				await TableFrames(5);
				_gameTable.StopMotion();
				if (scenario == 24) { await CheckDirectAccessActions(size); continue; }
				IReadOnlyList<BoardSpatialAction> projected = BoardSpatialActions.Project(_currentSnapshot!.View);
				RequireInteraction(projected.Count > 0, "Spatial lab scenario has no projected actions.");
				BoardSpatialAction chosen = projected[0];
				DecisionViewModel original = _currentSnapshot.View.Decision!;
				if (chosen.SourceId is { } source)
				{
					if (scenario == 30)
					{
						RequireInteraction(ShowTablePile("zone:runner-heap"), "Heap ability fixture cannot open pile.");
						await TableFrames(3);
						await ClickTableButton(FindDescendants<Button>(_tablePileCards).Single(button => button.Text == "Card actions…"));
					}
					else await ClickSpatialCard(source);
					RequireInteraction(SpatialVisible && _labSubmissionCount == 0, "Source click did not open controls without paying.");
					if (chosen.Targets.Count > 0 && _spatialSelection is null)
						await ClickTableButton(_spatialOptions!.GetChildren().OfType<Button>().First(button => button.Text.Contains("choose targets", StringComparison.Ordinal)));
					if (scenario is 25 or 30)
						RequireInteraction(_spatialSelection is not null && !_spatialOptions!.GetChildren().OfType<Button>().Any(button => button.Text.Contains("choose targets", StringComparison.Ordinal)), "Single ability retained a redundant target submenu.");
				}
				RequireInteraction(SpatialVisible, "Mandatory target decision did not open controls.");
				if (scenario == 27)
				{
					await ClickSpatialCard("lab-target-a");
					await ClickSpatialCard("lab-target-b");
					RequireInteraction(_labSubmissionCount == 0 && _spatialIncrementalCards?.Count == 2, "Incremental card selection submitted early.");
					await PressModalEscapeAsync();
					RequireInteraction(_spatialIncrementalCards?.Count == 0 && _labSubmissionCount == 0, "Escape did not clear the local incremental selection.");
					await ClickSpatialCard("lab-target-b");
					await ClickSpatialCard("lab-target-a");
					await ClickTableButton(_spatialOptions!.GetChildren().OfType<Button>().Single(button => button.Text.StartsWith("Confirm selection", StringComparison.Ordinal)));
					await TableFrames(12);
					RequireInteraction(_labSubmissionCount == 3 && _currentSnapshot.View.Decision is null, "Incremental confirmation did not submit two fresh adds and one finalization.");
					continue;
				}
				foreach (BoardTarget target in chosen.Targets)
				{
					if (scenario == 28)
					{
						TableCardControl candidate = _spatialOptions!.FindChildren("*", "Control", true, false).OfType<TableCardControl>().Single();
						await LabPointer(candidate.GetGlobalRect().GetCenter());
						RequireInteraction(candidate.UseHoverPreview && candidate.TooltipText.Length == 0 && CanShowCardHoverPreview(candidate), "Search browser lost its artwork tooltip.");
						candidate.GrabFocus();
						GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Space, Pressed = true });
						GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Space, Pressed = false });
						await TableFrames(3);
						RequireInteraction(_spatialConfirmButtons.Values.Any(button => button.HasFocus()), "Browser selection did not move keyboard focus to confirmation.");
					}
					else if (target.Kind == BoardTargetKind.Card) await ClickSpatialCard(target.Id);
					else if (target.Kind == BoardTargetKind.Subroutine)
						await ClickTableButton(_spatialOptions!.GetChildren().OfType<Button>().First(button => button.GetMeta("subroutine_ordinal", -1).AsInt32() == 0));
					else await ClickTableButton(_spatialCues.Single(cue => cue.Target == target).Button);
					RequireInteraction(_labSubmissionCount == 0, "Target selection spent resources before confirmation.");
					RequireInteraction(_spatialSelection?.Selected.Contains(target) == true, $"Native target click missed {target}. Selected: {string.Join(",", _spatialSelection?.Selected ?? [])}");
				}
				RequireInteraction(_spatialConfirmButtons.ContainsKey(chosen.Action.Id), "Complete selection has no confirmation.");
				RequireInteraction(_tableViewport.GetGlobalRect().Encloses(_spatialPanel!.GetGlobalRect()), "Spatial panel escaped the viewport.");
				if (OS.GetCmdlineUserArgs().Contains("--spatial-screenshots") && scenario is 21 or 23 or 25 or 28)
					await SaveInteractionEvidence($"spatial-{scenario}-{size.X}");
				if (OS.GetCmdlineUserArgs().Contains("--access-screenshots") && scenario == 25)
					await SaveInteractionEvidence($"bioroid-direct-{size.X}");
				await ClickTableButton(_spatialConfirmButtons[chosen.Action.Id]);
				await TableFrames(3);
				RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot.View.Decision is null && !CurrentSpatialDecision(original), "Confirmation failed or retained a stale spatial decision.");
			}
			foreach (int guardedScenario in new[] { 22, 28 })
			{
				_labScenario = guardedScenario; ResetInteractionLab(); await TableFrames(4);
				foreach (bool submitting in new[] { false, true })
				{
					try
					{
						if (submitting) Interlocked.Exchange(ref _submissionInFlight, 1);
						else { _tableAwaitingSettlement = true; _tablePlaybackWait = 10; }
						await TableFrames(2);
						BoardTarget candidate = _spatialSelection!.Candidates[0];
						SelectSpatialTarget(candidate);
						RequireInteraction(_spatialSelection.Selected.Count == 0 && _labSubmissionCount == 0
							&& _spatialOptions!.GetChildren().OfType<Button>().All(button => button.Disabled)
							&& !_spatialCues.Any(cue => cue.Button.Visible), "Busy spatial controls accepted input.");
						if (guardedScenario == 22) RequireInteraction(!_gameTable.VisibleCards[candidate.Id].IsTargetCandidate, "Busy board still advertises a target.");
						else RequireInteraction(_spatialOptions!.FindChildren("*", "Control", true, false).OfType<TableCardControl>()
							.All(card => !card.IsTargetCandidate && card.Modulate.A < 1), "Busy search browser remained enabled.");
					}
					finally { Interlocked.Exchange(ref _submissionInFlight, 0); _tableAwaitingSettlement = false; _tablePlaybackWait = 0; }
					await TableFrames(2);
				}
			}
			// A mandatory decision remains available after Escape; it only resets local choices.
			_labScenario = 22; ResetInteractionLab(); await TableFrames(4);
			await ClickSpatialCard("lab-target-a");
			await PressModalEscapeAsync();
			RequireInteraction(SpatialVisible && _spatialSelection?.Selected.Count == 0 && _labSubmissionCount == 0, "Escape lost a mandatory decision or submitted a target.");
			// Keyboard selection uses the same staging path.
			_gameTable.VisibleCards["lab-target-a"].GrabFocus();
			GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Enter, Pressed = true });
			GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Enter, Pressed = false });
			await TableFrames(2);
			RequireInteraction(_spatialSelection?.Selected.Count == 1 && _labSubmissionCount == 0, "Keyboard target selection did not stage safely.");
			await PressModalEscapeAsync();
			// Modal input cannot confirm or select underlying targets.
			ShowTablePile("zone:runner-heap");
			SelectSpatialTarget(new(BoardTargetKind.Card, "lab-target-a", "Trash card"));
			RequireInteraction(_spatialSelection?.Selected.Count == 0, "A modal allowed board targeting behind it.");
			await PressModalEscapeAsync();
			await ClickTableButton(_spatialPanel!.GetNode<Button>("%Dismiss"));
			RequireInteraction(_tableAllActions && _tableActionButtons.Count == 2 && !SpatialVisible, "Fallback lost the issued target choices.");
			await ClickTableButton(_tableActionButtons.Values.First());
			RequireInteraction(_stagedAction is not null && _labSubmissionCount == 0, "Fallback target selection skipped confirmation.");
			CancelStagedAction();
		}
		GD.Print("BOARD_SPATIAL_ACTIONS PASS families=12 resolutions=2 targets=true costs=true hidden=true incremental=true escape=true modals=true fallback=true");
	}

	private async Task ClickSpatialCard(string id)
	{
		TableCardControl? browser = _spatialOptions?.FindChildren("*", "Control", true, false).OfType<TableCardControl>().FirstOrDefault(card => card.CardId == id && card.IsVisibleInTree());
		Rect2? area = browser?.GetGlobalRect() ?? (_tableAccessPanel.Visible && _tableAccessCard.CardId == id ? _tableAccessCard.GetGlobalRect() : _gameTable.GetDropRect("HostCard", id));
		RequireInteraction(area is not null, "Card target has no visible handle: " + id);
		Vector2 point = area!.Value.GetCenter();
		await LabPointer(point); LabButton(point, true); LabButton(point, false);
		await TableFrames(3);
	}
}
