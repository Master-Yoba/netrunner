using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckDirectAccessActions(Vector2I size)
	{
		MatchSnapshot original = _currentSnapshot!;
		LegalActionViewModel trash = AccessActionsPresenter.Actions(original.View).Single(action => action.Kind == ActionKind.TrashAccessedCard);
		Button button = _accessActionButtons[trash.Id];
		RequireInteraction(_accessActionButtons.Count == 2 && !SpatialVisible && _labSubmissionCount == 0,
			"Access actions require a submenu or show duplicate payments.");
		RequireInteraction(trash.Parameters is RunCardPaymentParameters { HostedCredits: 2 }
			&& button.TooltipText == "Trash · 3 credits (2 run + 1 personal)"
			&& button.GetChildren().OfType<GameSymbolLabel>().Single().RenderedSymbolCount == 1,
			"Access action lost its exact price, preferred payment, or credit icon.");
		RequireInteraction(!_tableActions.GetChildren().OfType<Button>().Any(control => _accessActionButtons.ContainsValue(control)),
			"Direct access actions were duplicated in the dock.");
		RequireInteraction(_tableViewport.GetGlobalRect().Encloses(_tableAccessPanel.GetGlobalRect()), "Access controls escaped the viewport.");
		if (OS.GetCmdlineUserArgs().Contains("--access-screenshots")) await SaveInteractionEvidence($"access-actions-{size.X}");
		foreach (bool submitting in new[] { false, true })
		{
			try
			{
				if (submitting) Interlocked.Exchange(ref _submissionInFlight, 1);
				else { _tableAwaitingSettlement = true; _tablePlaybackWait = 10; }
				await TableFrames(2);
				button.EmitSignal(BaseButton.SignalName.Pressed);
				RequireInteraction(button.Disabled && _labSubmissionCount == 0, "Busy access controls accepted payment.");
			}
			finally { Interlocked.Exchange(ref _submissionInFlight, 0); _tableAwaitingSettlement = false; _tablePlaybackWait = 0; }
		}
		// Stale controls must not submit even if invoked before a UI refresh.
		_currentSnapshot = original with { View = original.View with { Decision = original.View.Decision! with { Revision = original.View.Decision.Revision + 1 } } };
		button.EmitSignal(BaseButton.SignalName.Pressed);
		RequireInteraction(_labSubmissionCount == 0, "An old access control submitted across decisions.");
		_currentSnapshot = original;
		await ClickSpatialCard("lab-accessed");
		RequireInteraction(_zoomOverlay.Visible && !SpatialVisible && _labSubmissionCount == 0, "Clicking access artwork no longer inspects the card.");
		button.EmitSignal(BaseButton.SignalName.Pressed);
		RequireInteraction(_labSubmissionCount == 0, "Access paid behind the inspection modal.");
		await PressModalEscapeAsync();
		await TableFrames(2);
		button = _accessActionButtons[trash.Id];
		button.GrabFocus();
		RequireInteraction(!button.Disabled && button.HasFocus() && CanUseAccessActions(_currentSnapshot!), "Access controls did not regain keyboard focus after inspection.");
		// Godot's default ui_accept binding targets device 0; the smoke isolates input on a virtual device.
		using var acceptBinding = new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Enter };
		InputMap.ActionAddEvent("ui_accept", acceptBinding);
		try
		{
			Input.ParseInputEvent(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Enter, Pressed = true });
			await TableFrames(2);
			Input.ParseInputEvent(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Enter, Pressed = false });
			await TableFrames(3);
		}
		finally { InputMap.ActionEraseEvent("ui_accept", acceptBinding); }
		RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot!.View.Decision is null && !SpatialVisible
			&& _currentSnapshot.View.Run!.RunCredits == 0 && _currentSnapshot.View.Runner.Credits == 5,
			$"Direct trash failed: submissions={_labSubmissionCount} run={_currentSnapshot!.View.Run!.RunCredits} personal={_currentSnapshot.View.Runner.Credits} decision={_currentSnapshot.View.Decision?.Id}.");
		ResetInteractionLab(); await TableFrames(4);
		LegalActionViewModel leave = _currentSnapshot!.View.LegalActions.Single(action => action.Kind == ActionKind.NoAccessAction);
		RequireInteraction(_accessActionButtons[leave.Id].TooltipText == "Leave card", "Non-agenda continuation does not explain leaving the card.");
		await ClickTableButton(_accessActionButtons[leave.Id]);
		RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot.View.Runner.Credits == 6 && _currentSnapshot.View.Run!.RunCredits == 2,
			"Leaving the card spent credits.");
		// Missing legal trash (including unaffordable/prohibited cases) must never get a fabricated button.
		ResetInteractionLab(); await TableFrames(3);
		PublishLabView(_currentSnapshot!.View with { LegalActions = _currentSnapshot.View.LegalActions.Where(action => action.Kind != ActionKind.TrashAccessedCard).ToArray() });
		await TableFrames(3);
		RequireInteraction(_accessActionButtons.Count == 1 && _accessActionButtons.Values.Single().TooltipText == "Leave card", "Access inferred a trash action from the printed cost.");
		GD.Print($"ACCESS_ACTIONS PASS resolution={size.X} direct=true payment=true inspection=true keyboard=true stale=true guards=true");
	}
}
