using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Plays the Corp through the real coordinator until an own-turn window offers a
/// rez, then proves the window is invisible: no pass in the dock, the next turn
/// actions offered, and taking one (gain a credit) passes the window and applies.
/// </summary>
public partial class Main
{
	private async Task RunInvisibleWindowSmokeAsync()
	{
		try
		{
			if (!_metadataSnapshot.IsOfficial)
			{
				GD.Print("INVISIBLE_WINDOW SKIP official=false");
				GetTree().Quit();
				return;
			}
			GetWindow().Size = new Vector2I(1920, 1080);
			GetWindow().ContentScaleSize = new Vector2I(1920, 1080);
			await TableFrames(3);
			_seedInput.Text = "47042";
			_humanSide.Select(1);
			_startButton.EmitSignal(BaseButton.SignalName.Pressed);
			_reducedMotion.ButtonPressed = true;
			for (int step = 0; step < 400; step++)
			{
				await SettledSideObservationAsync(PlayerSide.Corp);
				MatchSnapshot snapshot = _currentSnapshot!;
				if (snapshot.IsTerminal) throw new InvalidOperationException("The match ended before an own-turn rez window.");
				DecisionViewModel decision = snapshot.View.Decision!;
				if (snapshot.TurnPreview is not null && _turnWindow is not null)
				{
					if (decision.Kind != DecisionKind.TurnAction)
						throw new InvalidOperationException("An invisible window doesn't show the turn decision.");
					if (!BoardInteractionProjector.Project(snapshot.View).Sources.Any())
						throw new InvalidOperationException("Hand cards can't be dragged during an invisible window.");
					LegalActionViewModel? install = snapshot.View.LegalActions.FirstOrDefault(action => action.Parameters is InstallCardParameters);
					if (install is not null && OS.GetCmdlineUserArgs().Contains("--invisible-window-screenshots"))
					{
						SelectTableCard(((InstallCardParameters)install.Parameters).CardId.Value);
						await TableFrames(4);
						await SaveInteractionEvidence("invisible-window-selected");
						DismissBoardChoices();
						ClearTableSelection();
						await TableFrames(4);
					}
					if (snapshot.View.LegalActions.Any(action => action.Kind == ActionKind.PassPriority)
						|| FindDescendants<Button>(_tableActions).Any(button => button.Text.Contains("without", StringComparison.Ordinal)
							|| button.Text.Contains("Pass", StringComparison.Ordinal)))
						throw new InvalidOperationException("An invisible window still offers its pass.");
					if (!snapshot.View.LegalActions.Any(action => action.Kind == ActionKind.RezCard))
						throw new InvalidOperationException("The window's rez is missing from the merged view.");
					if (!_tablePrompt.Text.EndsWith(TablePrompt(snapshot.TurnPreview.Decision, snapshot.View), StringComparison.Ordinal))
						throw new InvalidOperationException($"The dock prompt is not a turn prompt: {_tablePrompt.Text}");
					LegalActionViewModel gain = snapshot.View.LegalActions.First(action => action.Kind == ActionKind.GainCredit);
					int credits = snapshot.View.Corp.Credits;
					int clicks = snapshot.View.Corp.Clicks;
					if (OS.GetCmdlineUserArgs().Contains("--invisible-window-screenshots")) await SaveInteractionEvidence("invisible-window");
					SubmitAction(decision, gain);
					for (int frame = 0; frame < 600; frame++)
					{
						await TableFrames(1);
						if (_currentSnapshot!.View.Corp.Credits == credits + 1 && _currentSnapshot.View.Corp.Clicks == clicks - 1)
						{
							GD.Print($"INVISIBLE_WINDOW PASS step={step} credits={credits + 1} clicks={clicks - 1}");
							GetTree().Quit();
							return;
						}
					}
					throw new InvalidOperationException("Taking a turn action from the window did not apply.");
				}
				if (decision.Kind == DecisionKind.TurnAction && !_turnSawNormal && OS.GetCmdlineUserArgs().Contains("--invisible-window-screenshots")
					&& snapshot.View.LegalActions.FirstOrDefault(action => action.Parameters is InstallCardParameters) is { } normalInstall)
				{
					_turnSawNormal = true;
					SelectTableCard(((InstallCardParameters)normalInstall.Parameters).CardId.Value);
					await TableFrames(4);
					await SaveInteractionEvidence("turn-action-selected");
					DismissBoardChoices();
					ClearTableSelection();
					await TableFrames(4);
				}
				LegalActionViewModel next = snapshot.View.LegalActions.FirstOrDefault(action => action.Parameters is InstallCardParameters install
						&& install.ServerId is null
						&& snapshot.View.CorpHand.VisibleCards.Any(card => card.Id == install.CardId.Value && card.Type is "Asset" or "Upgrade"))
					?? snapshot.View.LegalActions.FirstOrDefault(action => action.Kind is ActionKind.PassPriority or ActionKind.GainCredit)
					?? snapshot.View.LegalActions.FirstOrDefault(action => action.Parameters is NumberChoiceParameters { Value: 0 })
					?? snapshot.View.LegalActions[0];
				SubmitAction(decision, next);
				await TableFrames(2);
			}
			throw new InvalidOperationException("No own-turn rez window appeared.");
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("INVISIBLE_WINDOW FAIL " + exception.Message);
			GetTree().Quit(1);
		}
	}

	private bool _turnSawNormal;

	private async Task SettledSideObservationAsync(PlayerSide side)
	{
		Observation observation = await _coordinator!.GetReadyObservationAsync(side).WaitAsync(WallTimeLimits.Seconds(20));
		for (int frame = 0; frame < 600; frame++)
		{
			await TableFrames(1);
			if ((_turnWindow ?? _currentSnapshot?.View.Decision)?.Id == observation.Decision?.Id.Value
				&& _tablePublications.Count == 0 && Volatile.Read(ref _submissionInFlight) == 0 && _turnIntent is null)
				return;
		}
		throw new InvalidOperationException("The displayed decision did not settle.");
	}
}
