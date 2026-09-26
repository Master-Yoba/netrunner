using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task RunDeveloperSessionSmokeAsync(IReadOnlyList<string> arguments)
	{
		try
		{
			_isolateSmokeInput = true;
			bool disabled = arguments.Contains("--expect-disabled", StringComparer.Ordinal);
			if (disabled)
			{
				RequireInteraction(!DeveloperToolsEnabled && _developerDialog is null && _developerFiles is null,
					"Developer controls were created in a disabled build or launch.");
				RequireInteraction(!_galleryButton.Visible && !FindDescendants<Button>(_settingsMenu!).Any(button => button.Name == "DeveloperTools"),
					"A developer entry point remains visible.");
				GD.Print("M5_DEVELOPER PASS disabled=true controls_absent=true saved_setting_ignored=true");
				GetTree().Quit();
				return;
			}
			RequireInteraction(DeveloperToolsEnabled && _developerDialog is not null, "A developer build and --developer are required.");
			string directory = OS.GetEnvironment("NETRUNNER_M5_DEVELOPER_OUTPUT");
			RequireInteraction(!string.IsNullOrWhiteSpace(directory), "An isolated developer output directory is required.");
			Directory.CreateDirectory(directory);
			TryReadOption(arguments, "--side", out string? sideText, out _);
			PlayerSide side = sideText == "corp" ? PlayerSide.Corp : PlayerSide.Runner;
			GetWindow().Size = side == PlayerSide.Corp ? new(2560, 1440) : new(1920, 1080);
			GetWindow().ContentScaleSize = GetWindow().Size;
			_seedInput.Text = "57142";
			_humanSide.Select(side == PlayerSide.Runner ? 0 : 1);
			_startButton.EmitSignal(BaseButton.SignalName.Pressed);
			await DeveloperWaitAsync(() => _coordinator?.CurrentSnapshot is not null);
			await _coordinator!.WaitForHumanDecisionAsync();
			SetSettingsVisible(true);
			FindDescendants<Button>(_settingsMenu!).Single(button => button.Name == "DeveloperTools").EmitSignal(BaseButton.SignalName.Pressed);
			await PauseDeveloperSessionAsync();
			RequireInteraction(DeveloperDialogVisible && !SettingsVisible && _coordinator.CurrentSnapshot!.IsPaused, "Opening tools must pause cleanly.");
			DeveloperSnapshotDocument before = await _coordinator.ExportDeveloperSnapshotAsync();
			string publicBefore = EngineJson.Serialize(await _coordinator.GetObservationAsync(side));
			_developerDialog!.GetNode<CheckButton>("%Reveal").ButtonPressed = true;
			await RefreshDeveloperInspectionAsync();
			RequireInteraction(_developerDialog.GetNode<TextEdit>("%Inspector").Text.Contains("hidden", StringComparison.Ordinal), "Private inspector did not reveal hidden cards.");
			RequireInteraction(publicBefore == EngineJson.Serialize(await _coordinator.GetObservationAsync(side))
				&& before.IntegrityHash == (await _coordinator.ExportDeveloperSnapshotAsync()).IntegrityHash, "Reveal changed state or observations.");
			_developerDialog.GetNode<OptionButton>("%Actions").Select(1);
			_developerDialog.GetNode<Button>("%Step").EmitSignal(BaseButton.SignalName.Pressed);
			await DeveloperWaitAsync(() => !_developerBusy);
			RequireInteraction(_coordinator.CurrentSnapshot!.ActionCount == before.Snapshot.ActionSequence + 1
				&& _coordinator.CurrentSnapshot.IsPaused, "Step must accept exactly one action and pause.");
			_developerDialog.GetNode<CheckButton>("%Reveal").ButtonPressed = false;
			await CaptureSoloUiEvidenceAsync("developer");
			string portable = Path.Combine(directory, "session.developer.json");
			string raw = Path.Combine(directory, "scenario.snapshot.json");
			await ExportDeveloperSnapshotAsync(portable, raw: false);
			await ExportDeveloperSnapshotAsync(raw, raw: true);
			DeveloperSnapshotDocument exported = await _coordinator.ExportDeveloperSnapshotAsync();
			await ImportDeveloperSnapshotAsync(portable);
			RequireInteraction(exported.IntegrityHash == (await _coordinator.ExportDeveloperSnapshotAsync()).IntegrityHash,
				"Developer export/import changed replay or policy.");
			await ImportDeveloperSnapshotAsync(raw);
			DeveloperSnapshotDocument branch = await _coordinator.ExportDeveloperSnapshotAsync();
			RequireInteraction(branch.Origin == DeveloperSnapshotDocument.ImportedRoot && branch.Replay.Actions.Count == 0
				&& branch.EngineStateHash == exported.EngineStateHash && !_coordinator.SupportsPlayerSaves,
				"Raw import must retain state but start a distinct scenario replay.");
			_developerDialog.GetNode<SpinBox>("%Limit").Value = 3;
			_developerDialog.GetNode<Button>("%ToOutcome").EmitSignal(BaseButton.SignalName.Pressed);
			await DeveloperWaitAsync(() => !_developerBusy);
			RequireInteraction(_coordinator.CurrentSnapshot!.ActionCount == branch.Snapshot.ActionSequence + 3
				&& _coordinator.CurrentSnapshot.IsPaused, "Bounded fast-forward escaped its action cap.");
			MatchCoordinator kept = _coordinator;
			string bad = Path.Combine(directory, "corrupt.json");
			await File.WriteAllTextAsync(bad, "{}");
			await ImportDeveloperSnapshotAsync(bad);
			RequireInteraction(ReferenceEquals(kept, _coordinator) && _developerDialog.Status.StartsWith("Import failed", StringComparison.Ordinal), "Invalid import replaced the session.");
			_developerDialog.GetNode<Button>("%ToOutcome").EmitSignal(BaseButton.SignalName.Pressed);
			_developerDialog.GetNode<Button>("%Pause").EmitSignal(BaseButton.SignalName.Pressed);
			await DeveloperWaitAsync(() => !_developerBusy && _coordinator.CurrentSnapshot!.IsPaused);
			Input.ParseInputEvent(new InputEventKey { Device = SmokeInputDevice, Pressed = true, Keycode = Key.Escape });
			await TableFrames(2);
			RequireInteraction(!DeveloperDialogVisible && _settingsButton!.HasFocus(), "Escape must close developer tools and restore focus.");
			SetSettingsVisible(true);
			RequireInteraction(_settingsMenu!.GetNode<Button>("%SaveGame").Disabled, "Scenario roots must use developer exports.");
			SetSettingsVisible(false);
			RequireInteraction(!DeveloperDialogVisible && _currentSnapshot!.IsPaused, "Closing tools must retain paused board.");
			GD.Print($"M5_DEVELOPER PASS side={side} step=true cap=true reveal_isolated=true roundtrip=true raw_root=true corrupt_preserved=true pause=true");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"M5_DEVELOPER FAIL {exception}");
			GetTree().Quit(1);
		}
	}

	private async Task DeveloperWaitAsync(Func<bool> predicate)
	{
		for (int frame = 0; frame < WallTimeLimits.Count(2000); frame++)
		{
			if (predicate()) return;
			await TableFrames(1);
		}
		throw new TimeoutException("Developer UI did not reach its expected boundary.");
	}
}
