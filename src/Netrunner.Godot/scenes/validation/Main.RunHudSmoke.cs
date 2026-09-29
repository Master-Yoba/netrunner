using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// U8 run HUD smoke: in the icebreaker lab encounter, the HUD replaces the
/// breaker panel and the dock's run controls, shows matching and incompatible
/// breakers, and Break all boosts and breaks every subroutine through issued
/// actions for the credits it quoted, then leaves Continue in the footer.
/// </summary>
public partial class Main
{
	/// <summary>Two ICE on HQ: the outer one rezzed and passed, the inner one being encountered.</summary>
	private async Task CheckPassedIceConnection(Vector2I size)
	{
		MatchViewModel view = _currentSnapshot!.View;
		CardViewModel outer = UiPreviewSamples.Known("lab-passed-ice", "ICE", "Practice Wall") with
		{ Strength = 2, Subtypes = ["Barrier"], Rezzed = true, RulesText = "[subroutine] End the run." };
		PublishLabView(view with
		{
			Servers = view.Servers.Select(server => server.Kind == ServerKind.Headquarters
				? server with { Ice = [outer, .. server.Ice] } : server).ToArray(),
		});
		await TableFrames(4);
		RequireInteraction(_gameTable.RunConnectionVisible, "The board does not draw the run's connection.");
		if (OS.GetCmdlineUserArgs().Contains("--run-hud-screenshots")) await SaveInteractionEvidence($"run-hud-passed-ice-{size.X}");
	}

	/// <summary>At the Corp seat the HUD offers the rez of the approached ICE and leaving it unrezzed.</summary>
	private async Task CheckCorpRunHud(Vector2I size)
	{
		MatchViewModel runner = _currentSnapshot!.View;
		CardViewModel ice = runner.Run!.CurrentIce! with { Rezzed = false };
		MatchViewModel corp = runner with
		{
			Viewer = PlayerSide.Corp,
			Corp = runner.Corp with { Credits = 9 },
			Servers = runner.Servers.Select(server => server.Kind == ServerKind.Headquarters ? server with { Ice = [ice] } : server).ToArray(),
			Run = runner.Run with
			{
				Step = RunTimingStep.ApproachPaidWindow,
				Encounter = null,
				CurrentIce = ice,
				Priority = runner.Run.Priority! with { Window = RunPriorityWindowKind.ApproachPaid, Holder = PlayerSide.Corp, CorpMayRezApproachedIce = true }
			},
			Decision = InteractionLabSamples.Decision(PlayerSide.Corp, _labRevision, DecisionKind.RunTiming, "Approach paid window"),
			LegalActions =
			[
				InteractionLabSamples.Action("rez-approached", ActionKind.RezIce, new RunIceParameters(new(ice.Id)), _labRevision),
				InteractionLabSamples.Action("corp-pass", ActionKind.PassPriority, new NoActionParameters(), _labRevision),
			],
		};
		PublishLabView(corp);
		await TableFrames(4);
		RequireInteraction(_runHud?.Visible == true && RunHudActive, "The run HUD did not open at the Corp seat.");
		Button? rez = _runHud!.FindChild("Rez", true, false) as Button;
		Button? leave = FindDescendants<Button>(_runHud.GetNode("Content/Footer")).FirstOrDefault(button => button.Text == "Leave unrezzed");
		RequireInteraction(rez is { Disabled: false } && rez.Text.StartsWith("REZ", StringComparison.Ordinal) && leave is not null,
			$"The Corp HUD lacks Rez ('{rez?.Text}') or Leave unrezzed.");
		RequireInteraction(!_tableActionButtons.Keys.Any(id => corp.LegalActions.Any(action => action.Id == id)),
			"The Corp's run controls remain in the dock beside the HUD.");
		if (OS.GetCmdlineUserArgs().Contains("--run-hud-screenshots")) await SaveInteractionEvidence($"run-hud-corp-{size.X}");
		int submissions = _labSubmissionCount;
		await ClickTableButton(rez!);
		RequireInteraction(_labSubmissionCount == submissions + 1, "Rez from the Corp HUD did not submit exactly once.");
	}

	/// <summary>
	/// Playtest, 29 September: a jack-out decision that arrived while the table was
	/// still settling left the HUD with no buttons. It must build them disabled and
	/// enable them once board actions are usable.
	/// </summary>
	private async Task CheckJackOutWhileSettling()
	{
		MatchViewModel view = _currentSnapshot!.View;
		MatchViewModel decision = view with
		{
			// As in the saved game: after the ICE, the Runner-only decision has no priority window.
			Run = view.Run! with { Step = RunTimingStep.JackOutDecision, Encounter = null, Priority = null },
			Decision = InteractionLabSamples.Decision(PlayerSide.Runner, _labRevision, DecisionKind.RunTiming, "Jack out or continue"),
			LegalActions =
			[
				InteractionLabSamples.Action("jack-out", ActionKind.JackOut, new NoActionParameters(), _labRevision),
				InteractionLabSamples.Action("continue-run", ActionKind.ContinueRun, new NoActionParameters(), _labRevision),
			],
		};
		// Any moment board actions are briefly unusable (settling, a submission in flight) stands in for the motion.
		Volatile.Write(ref _submissionInFlight, 1);
		PublishLabView(decision);
		await TableFrames(3);
		Button[] footer = FindDescendants<Button>(_runHud!.GetNode("Content/Footer"));
		RequireInteraction(footer.Length == 2 && footer.All(button => button.Disabled),
			$"A jack-out decision during settling built {footer.Length} footer buttons ({string.Join(", ", footer.Select(button => $"{button.Text}:{button.Disabled}"))}), or enabled them early; usable={CanUseBoardActions(_currentSnapshot!)}.");
		Volatile.Write(ref _submissionInFlight, 0);
		await TableFrames(3);
		footer = FindDescendants<Button>(_runHud.GetNode("Content/Footer"));
		RequireInteraction(footer.Length == 2 && footer.All(button => !button.Disabled),
			"Jack out and Continue stayed disabled after the table settled.");
	}

	/// <summary>Playtest, 29 September: breaching HQ took a server click and the dock; the HUD offers the choices.</summary>
	private async Task CheckBreachChoicesInHud()
	{
		MatchViewModel view = _currentSnapshot!.View;
		string root = view.Servers.SelectMany(server => server.Root).OfType<CardViewModel>().First(card => card.IsKnown).Id;
		MatchViewModel breach = view with
		{
			Run = view.Run! with
			{
				Step = RunTimingStep.Success,
				Encounter = null,
				CurrentIceIndex = -1,
				CurrentIce = null,
				Priority = null,
				Access = view.Run.Access with { State = RunAccessState.InProgress }
			},
			Decision = InteractionLabSamples.Decision(PlayerSide.Runner, _labRevision, DecisionKind.RunTiming, "Choose a breach candidate"),
			LegalActions =
			[
				InteractionLabSamples.Action("breach-root", ActionKind.ChooseBreachCandidate, new BreachCandidateParameters(new(root), null), _labRevision),
				InteractionLabSamples.Action("breach-hq", ActionKind.ChooseBreachCandidate, new BreachCandidateParameters(null, BreachHiddenCandidateSource.CorpHand), _labRevision),
			],
		};
		PublishLabView(breach);
		await TableFrames(4);
		Button[] choices = FindDescendants<Button>(_runHud!.GetNode("Content/Body"));
		RequireInteraction(choices.Length == 2 && choices.Any(button => button.Text == "Access a random card from HQ")
			&& choices.All(button => !button.Disabled), $"The HUD's breach choices are wrong: {string.Join(", ", choices.Select(button => button.Text))}.");
		RequireInteraction(!_tableActionButtons.Keys.Any(id => breach.LegalActions.Any(action => action.Id == id)),
			"Breach choices remain in the dock beside the HUD.");
		float height = _runHud.Size.Y;
		await TableFrames(6);
		RequireInteraction(Mathf.IsEqualApprox(height, _runHud.Size.Y), $"The HUD's height changed from {height} to {_runHud.Size.Y} with nothing new.");
		int submissions = _labSubmissionCount;
		await ClickTableButton(choices.First(button => button.Text == "Access a random card from HQ"));
		RequireInteraction(_labSubmissionCount == submissions + 1, "A breach choice from the HUD did not submit exactly once.");
	}

	private async Task RunRunHudSmokeAsync()
	{
		try
		{
			_isolateSmokeInput = true;
			OpenInteractionLab();
			_reducedMotion.ButtonPressed = true;
			foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
			{
				GetWindow().Size = size;
				GetWindow().ContentScaleSize = size;
				_labScenario = 19;
				ResetInteractionLab();
				await TableFrames(6);
				_gameTable.StopMotion();
				await CheckPassedIceConnection(size);
				await CheckCorpRunHud(size);
				ResetInteractionLab();
				await TableFrames(6);
				await CheckJackOutWhileSettling();
				ResetInteractionLab();
				await TableFrames(6);
				await CheckBreachChoicesInHud();
				ResetInteractionLab();
				await TableFrames(6);
				_gameTable.StopMotion();
				RequireInteraction(_runHud?.Visible == true && RunHudActive, "The run HUD did not open for the Runner's encounter.");
				RequireInteraction(_encounterOverlay?.Visible != true, "The old encounter overlay still shows beside the HUD.");
				RequireInteraction(!_tableActionButtons.Keys.Any(id => _currentSnapshot!.View.LegalActions
					.Any(action => action.Id == id && action.Kind is ActionKind.PassPriority or ActionKind.PumpBreaker or ActionKind.BreakSubroutines)),
					"Run controls remain in the dock beside the HUD.");
				RequireInteraction(_tableViewport.GetGlobalRect().Encloses(_runHud!.GetGlobalRect()),
					$"Run HUD {_runHud.GetGlobalRect()} escaped the table {_tableViewport.GetGlobalRect()}.");
				Control? matching = _runHud.FindChild("Breaker-lab-fracter", true, false) as Control;
				Control? incompatible = _runHud.FindChild("Breaker-lab-decoder", true, false) as Control;
				RequireInteraction(matching is not null && incompatible is not null, "The HUD does not list both breakers.");
				RequireInteraction(FindDescendants<Button>(incompatible!).Length == 0, "An incompatible breaker has controls in the HUD.");
				Button? breakAll = matching!.FindChild("BreakAll", true, false) as Button;
				RequireInteraction(breakAll is { Disabled: false } && breakAll.Text.Contains("3 ¢", StringComparison.Ordinal),
					$"Break all is missing or misquoted: '{breakAll?.Text}'.");
				int before = _currentSnapshot!.View.Runner.Credits + _currentSnapshot.View.Run!.RunCredits;
				int submissions = _labSubmissionCount;
				if (OS.GetCmdlineUserArgs().Contains("--run-hud-screenshots")) await SaveInteractionEvidence($"run-hud-encounter-{size.X}");
				await ClickTableButton(breakAll!);
				for (int frame = 0; frame < 60 && _currentSnapshot!.View.Run?.Encounter?.RemainingSubroutines > 0; frame++)
					await TableFrames(1);
				EncounterViewModel encounter = _currentSnapshot!.View.Run!.Encounter!;
				int after = _currentSnapshot.View.Runner.Credits + _currentSnapshot.View.Run.RunCredits;
				RequireInteraction(encounter.RemainingSubroutines == 0, "Break all left subroutines unbroken.");
				RequireInteraction(before - after == 3 && _labSubmissionCount - submissions == 3,
					$"Break all spent {before - after} credits over {_labSubmissionCount - submissions} submissions; it quoted 3 over 3.");
				await TableFrames(2);
				Button? proceed = FindDescendants<Button>(_runHud.GetNode("Content/Footer")).FirstOrDefault(button => button.Text == "Continue");
				RequireInteraction(proceed is { Disabled: false }, "Continue is missing from the HUD footer after breaking everything.");
				if (OS.GetCmdlineUserArgs().Contains("--run-hud-screenshots")) await SaveInteractionEvidence($"run-hud-broken-{size.X}");
				await ClickTableButton(proceed!);
				await TableFrames(3);
				RequireInteraction(_runHud.Visible == false && !RunHudActive, "The HUD stayed open after the run moved on.");
			}
			GD.Print("RUN_HUD PASS resolutions=2 replaces_panels=true breakers=true break_all=true quoted=true footer=true closes=true corp_rez=true passed_ice=true jack_out_after_settling=true breach_choices=true steady_height=true");
			QuitCleanly();
		}
		catch (Exception error)
		{
			GD.PrintErr($"RUN_HUD FAIL {error}");
			QuitCleanly(1);
		}
	}
}
