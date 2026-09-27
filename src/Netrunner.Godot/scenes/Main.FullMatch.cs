using System.Diagnostics;
using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private static bool TryReadFullMatchSmokeOptions(
		IReadOnlyList<string> arguments,
		out string preset,
		out ulong seed,
		out string? error)
	{
		preset = "basic";
		seed = 47042;
		error = null;
		if (!TryReadOption(arguments, "--preset", out string? presetValue, out error))
		{
			return false;
		}

		if (presetValue is not null)
		{
			if (presetValue is not ("basic" or "expanded"))
			{
				error = $"--preset requires 'basic' or 'expanded', found '{presetValue}'.";
				return false;
			}

			preset = presetValue;
		}

		if (!TryReadOption(arguments, "--seed", out string? seedValue, out error))
		{
			return false;
		}

		if (seedValue is not null && !ulong.TryParse(seedValue, out seed))
		{
			error = $"--seed requires an unsigned integer, found '{seedValue}'.";
			return false;
		}

		return true;
	}

	private static bool TryReadOption(
		IReadOnlyList<string> arguments,
		string option,
		out string? value,
		out string? error)
	{
		value = null;
		error = null;
		int index = -1;
		for (int candidate = 0; candidate < arguments.Count; candidate++)
		{
			if (!string.Equals(arguments[candidate], option, StringComparison.Ordinal))
			{
				continue;
			}

			if (index >= 0)
			{
				error = $"{option} may be specified only once.";
				return false;
			}

			index = candidate;
		}

		if (index < 0)
		{
			return true;
		}

		if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
		{
			error = $"{option} requires a value.";
			return false;
		}

		value = arguments[index + 1];
		return true;
	}

	private OptionButton _humanSide = null!;

	private bool ConfigureFullMatchSmokePresets()
	{
		if (!_m4FullMatchSmoke || !HasAuthenticatedOfficialContent())
		{
			return true; // BeginFullMatch owns the unavailable-content diagnostic.
		}
		foreach (var (button, ids, side) in new[]
		{
			(_corpPreset, _corpPresetIds, "corp"),
			(_runnerPreset, _runnerPresetIds, "runner"),
		})
		{
			string requested = $"system-gateway-{_fullMatchSmokePreset}-{side}";
			int[] matches = ids.Where(pair => pair.Value == requested).Select(pair => pair.Key).ToArray();
			if (matches.Length != 1 || button.IsItemDisabled(matches[0]))
			{
				GD.Print($"M4_FULL_SMOKE FAIL preset_selector={requested} unavailable=true");
				GetTree().Quit(1);
				return false;
			}
			button.Select(matches[0]);
		}
		return true;
	}

	private bool HasAuthenticatedOfficialContent() =>
		_embeddedCardData is not null
		&& _officialPresetCatalog is not null
		&& _runtimeCardCatalog is not null
		&& string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal);

	private static string OfficialContentUnavailableMessage() =>
		"Authenticated System Gateway content is unavailable. Provision the local 77-card snapshot and rebuild "
		+ "with NetrunnerContentEnabled=true and NetrunnerCardDataMode=official.";

	private void BeginFullMatch()
	{
		try
		{
			ResetFullMatchSetupTrace();
			if (_fullMatchResumeSessionDirectory is not null)
			{
				BeginResumedFullMatch();
				return;
			}

			PlayerSide humanSide = (PlayerSide)_humanSide.GetSelectedId();
			if (_fullMatchSmoke && !_m4FullMatchSmoke)
			{
				_coordinator = FullMatchFactory.CreateDeveloper(
					humanSide,
					seed: 42,
					agentDelay: TimeSpan.FromMilliseconds(45));
				AttachCoordinatorPublications();
				_startButton.Disabled = true;
				_status.Text = "Starting the developer full-match proof through Core setup…";
				_ = AwaitStartAsync();
				return;
			}

			if (!HasAuthenticatedOfficialContent())
			{
				throw new InvalidOperationException(OfficialContentUnavailableMessage());
			}

			if (!ulong.TryParse(_seedInput.Text.Trim(), out ulong seed))
			{
				_seedInput.GrabFocus();
				throw new InvalidOperationException("The deterministic seed must be an unsigned integer.");
			}

			string corpPresetId = SelectedPreset(_corpPreset, _corpPresetIds)
				?? throw new InvalidOperationException("Choose a playable official Corp preset before starting.");
			string runnerPresetId = SelectedPreset(_runnerPreset, _runnerPresetIds)
				?? throw new InvalidOperationException("Choose a playable official Runner preset before starting.");

			if (_useCardTable)
				_matchHistory.ReportHeader = $"Netrunner · seed {seed} · {humanSide} · {corpPresetId} / {runnerPresetId} · {System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Main).Assembly)?.InformationalVersion ?? "unknown build"}";
			_coordinator = CreateOfficialFullMatchCoordinator(
				corpPresetId,
				runnerPresetId,
				_embeddedCardData!,
				humanSide,
				seed: seed,
				officialPresets: _officialPresetCatalog!);
			AttachCoordinatorPublications();
			_startButton.Disabled = true;
			_status.Text = "Shuffling decks and dealing opening hands…";
			_ = AwaitStartAsync();
		}
		catch (Exception exception)
		{
			RecordFullMatchException(exception, "begin");
			_status.Text = $"Match setup failed: {exception.Message}";
			GD.PushError(exception.ToString());
			if (_m4FullMatchSmoke)
			{
				GD.Print($"M4_FULL_SMOKE FAIL side={_fullMatchSmokeSide} preset={_fullMatchSmokePreset} seed={_fullMatchSmokeSeed} error={exception.Message}");
				GetTree().Quit(1);
			}
		}
	}

	private void ResetFullMatchSetupTrace()
	{
		while (_fullMatchSetupOrder.TryDequeue(out _))
		{
		}

		lock (_fullMatchSetupGate)
		{
			_lastFullMatchSetupOwner = null;
		}
	}

	private void CaptureFullMatchSetupSnapshot(MatchSnapshot snapshot)
	{
		if (!_m4FullMatchSmoke || snapshot.SetupDecisionOwnerSide is not { } owner)
		{
			return;
		}

		lock (_fullMatchSetupGate)
		{
			if (_lastFullMatchSetupOwner == owner)
			{
				return;
			}

			_fullMatchSetupOrder.Enqueue(owner);
			_lastFullMatchSetupOwner = owner;
		}
	}

	private List<PlayerSide> ReadFullMatchSetupOrder() => _fullMatchSetupOrder.ToArray().ToList();

	private async Task RunFullMatchReloadSmokeAsync()
	{
		try
		{
			bool controlsReady = HasAuthenticatedOfficialContent()
				&& !_startButton.Disabled
				&& _startPanel.Visible
				&& _humanSide.ItemCount >= 2
				&& _corpPreset.ItemCount > 0
				&& _runnerPreset.ItemCount > 0;
			if (!controlsReady)
			{
				throw new InvalidOperationException("New match reload did not restore the official setup controls.");
			}

			BeginMatch();
			MatchSnapshot snapshot = await WaitForSelectableSetupSnapshotAsync();
			if (!await AwaitRenderedSequenceAsync(snapshot.Sequence, 240))
			{
				throw new TimeoutException("New match reload setup publication did not render.");
			}

			GD.Print($"M4_FULL_SMOKE PASS side={_fullMatchSmokeSide} preset={_fullMatchSmokePreset} seed={_fullMatchSmokeSeed} setup_order=corp,runner setup=true mulligan=true turn_pair=true identities=true terminal=true reload=true fresh_setup=true");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			RecordFullMatchException(exception, "full-match-smoke");
			GD.PushError(exception.ToString());
			GD.Print($"M4_FULL_SMOKE FAIL side={_fullMatchSmokeSide} preset={_fullMatchSmokePreset} seed={_fullMatchSmokeSeed} reload=true error={exception.Message}");
			GetTree().Quit(1);
		}
	}

	private async Task<MatchSnapshot> WaitForSelectableSetupSnapshotAsync()
	{
		for (int frame = 0; frame < 240; frame++)
		{
			if (_coordinator?.CurrentSnapshot is { } snapshot
				&& snapshot.SetupDecisionOwnerSide == _fullMatchSmokeSide
				&& snapshot.Observation.Decision?.Kind == DecisionKind.Setup)
			{
				return snapshot;
			}

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		throw new TimeoutException("New match reload did not publish a selectable Core setup decision.");
	}

	private async Task RunFullMatchSmokeAsync(PlayerSide side)
	{
		if (_coordinator is null)
		{
			// BeginFullMatch already reported the actionable setup failure and
			// requested shutdown; do not mask it with a second null-coordinator
			// error from the asynchronous smoke task.
			return;
		}

		bool setup = false;
		bool mulligan = false;
		bool turnPair = false;
		bool identities = false;
		const long actionCap = 4000;
		Stopwatch wallClock = Stopwatch.StartNew();
		int pollingSteps = 0;
		MatchSnapshot? diagnosticSnapshot = null;
		try
		{
			_reducedMotion.ButtonPressed = true;
			var policy = FullMatchFactory.CreateContractPolicy(
				side,
				_m4FullMatchSmoke ? _runtimeCardCatalog : null);
			while (true)
			{
				pollingSteps++;
				if (wallClock.Elapsed > WallTimeLimits.Seconds(180))
				{
					throw new TimeoutException(
						$"Full-match smoke exceeded its {WallTimeLimits.Seconds(180).TotalSeconds:F0}-second wall-time cap at action_count={diagnosticSnapshot?.ActionCount ?? 0}.");
				}

				if (!_m4FullMatchSmoke && pollingSteps > 600)
				{
					throw new TimeoutException("Full-match smoke exceeded its developer polling cap.");
				}

				Observation observation = await _coordinator!.GetReadyObservationAsync(side)
					.WaitAsync(WallTimeLimits.Seconds(15));
				MatchSnapshot snapshot = _coordinator.CurrentSnapshot!;
				diagnosticSnapshot = snapshot;
				CaptureFullMatchSetupSnapshot(snapshot);
				if (!await AwaitRenderedSequenceAsync(snapshot.Sequence, 240))
				{
					throw new TimeoutException("Full-match publication did not render.");
				}
				if (snapshot.Feedback?.IsError == true)
				{
					throw new InvalidOperationException(snapshot.Feedback.Message);
				}
				if (snapshot.ActionCount > actionCap)
				{
					throw new TimeoutException(
						$"Full-match smoke exceeded its {actionCap}-action cap at action_count={snapshot.ActionCount}.");
				}
				if (_m4FullMatchSmoke && snapshot.ActionCount >= actionCap && !snapshot.IsTerminal)
				{
					throw new TimeoutException(
						$"Full-match smoke reached its {actionCap}-action cap before terminal outcome at action_count={snapshot.ActionCount}.");
				}
				identities |= snapshot.View.CorpIdentity is not null && snapshot.View.RunnerIdentity is not null
					&& (_useCardTable
						? _gameTable.VisibleCards.Values.Any(card => card.IsVisibleInTree() && card.Card?.Id == snapshot.View.CorpIdentity.Id)
							&& _gameTable.VisibleCards.Values.Any(card => card.IsVisibleInTree() && card.Card?.Id == snapshot.View.RunnerIdentity.Id)
						: _corpIdentityDetails.Text.Contains(snapshot.View.CorpIdentity.Title, StringComparison.Ordinal)
							&& _runnerIdentityDetails.Text.Contains(snapshot.View.RunnerIdentity.Title, StringComparison.Ordinal));
				turnPair |= snapshot.View.Turn.TurnNumber > 1;
				if (snapshot.IsTerminal)
				{
					List<PlayerSide> setupOrder = ReadFullMatchSetupOrder();
					bool setupBoundary = !_m4FullMatchSmoke
						|| setupOrder.SequenceEqual([PlayerSide.Corp, PlayerSide.Runner]);
					if (!setup || !mulligan || !turnPair || !identities || !setupBoundary)
					{
						throw new InvalidOperationException("Full-match smoke missed a required setup/turn/identity presentation boundary.");
					}
					if (_m4FullMatchSmoke)
					{
						Button? newMatch = FindDescendants<Button>(_focusPresentation)
							.FirstOrDefault(button => string.Equals(button.Text, "New match", StringComparison.Ordinal));
						if (newMatch is null)
						{
							throw new InvalidOperationException("Full-match smoke reached an outcome without the rendered New match control.");
						}

						_m4FullSmokeReloadPending = true;
						GD.Print($"M4_FULL_SMOKE RELOAD side={side} preset={_fullMatchSmokePreset} seed={_fullMatchSmokeSeed} setup_order=corp,runner action_count={snapshot.ActionCount}");
						newMatch.EmitSignal(BaseButton.SignalName.Pressed);
						return;
					}

					string marker = "FULL_MATCH_SMOKE";
					GD.Print($"{marker} PASS side={side} setup=true mulligan=true turn_pair=true identities=true terminal=true action_count={snapshot.ActionCount}");
					GetTree().Quit();
					return;
				}
				if (!snapshot.AwaitingHumanDecision || observation.Decision is null)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
					continue;
				}
				if (observation.Decision.Kind == DecisionKind.Setup)
				{
					setup = true;
				}
				LegalAction? redraw = !mulligan && observation.Decision.Kind == DecisionKind.Setup
					? observation.LegalActions.FirstOrDefault(action => action.Parameters is NumberChoiceParameters { Value: 1 }) : null;
				ActionsRegionState actions = ActionsRegionPresenter.Present(snapshot);
				// The smoke policy plays the human seat through the actual presented choices.
				// This includes matching breakers and the single hosted-first payment.
				HashSet<ActionId> visibleIds = actions.DisplayActions.Select(action => action.Id).ToHashSet();
				Observation presented = observation with { LegalActions = observation.LegalActions.Where(action => visibleIds.Contains(action.Id)).ToArray() };
				ActionId actionId = redraw?.Id ?? await policy(presented, CancellationToken.None);
				LegalActionViewModel action = actions.DisplayActions.Single(candidate => candidate.Id == actionId);
				if (redraw is not null)
				{
					SaveFullMatchScreenshot();
				}
				mulligan |= redraw is not null;
				// Use the same submission handler as the rendered action buttons.
				if (_useCardTable && _turnWindow is not null && action.Kind == ActionKind.PassPriority)
				{
					// An invisible window has no pass button: a player with clicks takes
					// a turn action instead. Pass through the same handler.
					SubmitAction(snapshot.View.Decision!, action);
				}
				else if (_useCardTable)
				{
					// Press the control the player sees; an invisible window shows the
					// previewed turn decision, with the window's rez and score.
					MatchSnapshot displayed = _currentSnapshot ?? snapshot;
					_tableAllActions = true;
					RenderCardTableActions(displayed);
					if (!_tableActionButtons.TryGetValue(action.Id, out Button? actionButton))
						throw new InvalidOperationException("Issued action is unreachable in the table controls.");
					actionButton.EmitSignal(BaseButton.SignalName.Pressed);
					if (_stagedAction is not null)
					{
						Button confirm = FindDescendants<Button>(_tableActions).Single(button => button.Text == "Confirm");
						confirm.EmitSignal(BaseButton.SignalName.Pressed);
					}
				}
				else SubmitAction(snapshot.View.Decision!, action);
				await WaitForPublishedSnapshotAsync(snapshot.Sequence, 240);
			}
		}
		catch (Exception exception)
		{
			List<PlayerSide> setupOrder = ReadFullMatchSetupOrder();
			diagnosticSnapshot = _coordinator?.CurrentSnapshot ?? diagnosticSnapshot;
			if (_m4FullMatchSmoke && diagnosticSnapshot is not null)
			{
				// This is the already viewer-filtered publication, never GameState.
				GD.Print("M4_FULL_SMOKE_DIAGNOSTIC " + EngineJson.Serialize(new
				{
					seed = _fullMatchSmokeSeed,
					preset = _fullMatchSmokePreset,
					side,
					diagnosticSnapshot.ActionCount,
					diagnosticSnapshot.Sequence,
					diagnosticSnapshot.View,
					diagnosticSnapshot.Feedback,
				}));
			}
			RecordFullMatchException(exception, "full-match-smoke");
			GD.PushError(exception.ToString());
			string marker = _m4FullMatchSmoke ? "M4_FULL_SMOKE" : "FULL_MATCH_SMOKE";
			string details = _m4FullMatchSmoke
				? $" preset={_fullMatchSmokePreset} seed={_fullMatchSmokeSeed} setup_order={string.Join(',', setupOrder)} action_count={diagnosticSnapshot?.ActionCount ?? 0}"
				: string.Empty;
			GD.Print($"{marker} FAIL side={side}{details} setup={setup} mulligan={mulligan} turn_pair={turnPair} identities={identities} error={exception.Message}");
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
