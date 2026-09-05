using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private OptionButton _humanSide = null!;

	private void BeginFullMatch()
	{
		try
		{
			PlayerSide humanSide = (PlayerSide)_humanSide.GetSelectedId();
			string? corpPresetId = SelectedPreset(_corpPreset, _corpPresetIds);
			string? runnerPresetId = SelectedPreset(_runnerPreset, _runnerPresetIds);
			bool useOfficial = !_fullMatchSmoke
				&& _embeddedCardData is not null
				&& _officialPresetCatalog is not null
				&& corpPresetId is not null
				&& runnerPresetId is not null;
			_coordinator = useOfficial
				? FullMatchFactory.CreateOfficial(
					corpPresetId!,
					runnerPresetId!,
					_embeddedCardData!,
					humanSide,
					seed: 42,
					agentDelay: TimeSpan.FromMilliseconds(45),
					officialPresets: _officialPresetCatalog)
				: FullMatchFactory.CreateDeveloper(humanSide, agentDelay: TimeSpan.FromMilliseconds(45));
			_coordinator.SnapshotPublished += QueueSnapshot;
			_startButton.Disabled = true;
			_status.Text = useOfficial
				? "Starting the selected official match through Core setup…"
				: "Starting a developer match through Core setup…";
			_ = AwaitStartAsync();
		}
		catch (Exception exception)
		{
			_status.Text = $"Match setup failed: {exception.Message}";
			GD.PushError(exception.ToString());
		}
	}

	private async Task RunFullMatchSmokeAsync(PlayerSide side)
	{
		bool setup = false;
		bool mulligan = false;
		bool turnPair = false;
		bool identities = false;
		try
		{
			_reducedMotion.ButtonPressed = true;
			var policy = FullMatchFactory.CreateContractPolicy(side);
			for (int step = 0; step < 600; step++)
			{
				Observation observation = await _coordinator!.GetObservationAsync(side);
				MatchSnapshot snapshot = _coordinator.CurrentSnapshot!;
				if (!await AwaitRenderedSequenceAsync(snapshot.Sequence, 240))
				{
					throw new TimeoutException("Full-match publication did not render.");
				}
				if (snapshot.Feedback?.IsError == true)
				{
					throw new InvalidOperationException(snapshot.Feedback.Message);
				}
				identities |= snapshot.View.CorpIdentity is not null && snapshot.View.RunnerIdentity is not null
					&& _corpIdentityDetails.Text.Contains(snapshot.View.CorpIdentity.Title, StringComparison.Ordinal)
					&& _runnerIdentityDetails.Text.Contains(snapshot.View.RunnerIdentity.Title, StringComparison.Ordinal);
				turnPair |= snapshot.View.Turn.TurnNumber > 1;
				if (snapshot.IsTerminal)
				{
					if (!setup || !mulligan || !turnPair || !identities)
					{
						throw new InvalidOperationException("Full-match smoke missed a required setup/turn/identity presentation boundary.");
					}
					GD.Print($"FULL_MATCH_SMOKE PASS side={side} setup=true mulligan=true turn_pair=true identities=true terminal=true");
					GetTree().Quit();
					return;
				}
				if (!snapshot.AwaitingHumanDecision || observation.Decision is null)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
					continue;
				}
				setup |= observation.Decision.Kind == DecisionKind.Setup;
				LegalAction? redraw = !mulligan && observation.Decision.Kind == DecisionKind.Setup
					? observation.LegalActions.FirstOrDefault(action => action.Parameters is NumberChoiceParameters { Value: 1 }) : null;
				ActionId actionId = redraw?.Id ?? await policy(observation, CancellationToken.None);
				ActionsRegionState actions = ActionsRegionPresenter.Present(snapshot);
				LegalActionViewModel action = actions.DisplayActions.Single(candidate => candidate.Id == actionId);
				if (redraw is not null)
				{
					SaveFullMatchScreenshot();
				}
				mulligan |= redraw is not null;
				// Use the same submission handler as the rendered action buttons.
				SubmitAction(snapshot.View.Decision!, action);
				await WaitForPublishedSnapshotAsync(snapshot.Sequence, 240);
			}
			throw new TimeoutException("Full-match smoke exceeded its action cap.");
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print($"FULL_MATCH_SMOKE FAIL side={side} setup={setup} mulligan={mulligan} turn_pair={turnPair} identities={identities}");
			GetTree().Quit(1);
		}
	}

	private void SaveFullMatchScreenshot()
	{
		string[] args = OS.GetCmdlineUserArgs();
		int index = Array.IndexOf(args, "--full-match-screenshot");
		if (index >= 0 && index + 1 < args.Length)
		{
			Error error = GetViewport().GetTexture().GetImage().SavePng(args[index + 1]);
			if (error != Error.Ok)
			{
				throw new IOException($"Screenshot failed: {error}");
			}
		}
	}
}
