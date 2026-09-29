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
			GD.Print("RUN_HUD PASS resolutions=2 replaces_panels=true breakers=true break_all=true quoted=true footer=true closes=true");
			QuitCleanly();
		}
		catch (Exception error)
		{
			GD.PrintErr($"RUN_HUD FAIL {error}");
			QuitCleanly(1);
		}
	}
}
