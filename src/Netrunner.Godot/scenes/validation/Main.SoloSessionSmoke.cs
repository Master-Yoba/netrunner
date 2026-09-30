using Godot;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private static bool _soloSmokeReloadPending;
	private static SoloSessionService? _soloSmokeExpectedService;

	private async Task RunSoloClientSmokeAsync(IReadOnlyList<string> arguments)
	{
		try
		{
			RequireInteraction(!string.IsNullOrWhiteSpace(SoloSaveDirectory()), "Client persistence smoke requires an isolated save directory.");
			RequireInteraction(HasAuthenticatedOfficialContent(), "Client persistence smoke requires installed official content.");
			if (arguments.Contains("--offline-proof", StringComparer.Ordinal))
			{
				RequireInteraction(!OS.HasFeature("editor") && !DeveloperToolsEnabled, "Offline proof requires an exported release.");
				RequireInteraction(GD.Load<Texture2D>("res://card-backs/corp.png") is not null
					&& GD.Load<Texture2D>("res://card-backs/runner.png") is not null, "Generic card backs are missing from the package.");
				TryReadOption(arguments, "--offline-art", out string? artMode, out _);
				RequireInteraction(artMode is "missing" or "restored", "Offline proof requires an explicit artwork mode.");
				foreach (string code in Netrunner.Cards.CardArtworkCache.GatewayCodes)
				{
					LocalCardArtworkResolver.Artwork? artwork = LocalCardArtworkResolver.Shared.TryLoad(code);
					RequireInteraction(artMode == "missing" ? artwork is null : artwork?.FullFace == true,
						$"Unexpected offline artwork availability: {artMode} {code}.");
				}
				GD.Print($"M5_OFFLINE_ASSETS PASS artwork={artMode} cards=77 packaged_backs=true release=true");
			}
			TryReadOption(arguments, "--phase", out string? phase, out _);
			TryReadOption(arguments, "--side", out string? sideText, out _);
			TryReadOption(arguments, "--preset", out string? preset, out _);
			TryReadOption(arguments, "--difficulty", out string? difficulty, out _);
			PlayerSide side = sideText == "corp" ? PlayerSide.Corp : PlayerSide.Runner;
			preset ??= "expanded";
			difficulty ??= "standard";
			if (_soloSmokeReloadPending)
			{
				_soloSmokeReloadPending = false;
				RequireInteraction(_coordinator is null && _startPanel.Visible && _difficulty?.Selected == 0,
					"New game did not restore clean default setup.");
				RequireInteraction(ReferenceEquals(_soloSmokeExpectedService, GetSoloSessionService()), "New game replaced the app's serialized save writer.");
				GD.Print($"M5_CLIENT PASS phase=resume side={side} preset={preset} difficulty={difficulty} restart=true paused=true terminal=true history=true new_game=true");
				QuitCleanly();
				return;
			}
			GetWindow().Size = side == PlayerSide.Corp ? new(2560, 1440) : new(1920, 1080);
			GetWindow().ContentScaleSize = GetWindow().Size;
			await TableFrames(3);
			RequireInteraction(_difficulty?.Selected == 0 && _saveDialog is not null, "Standard must be the default and save browser must exist.");
			await CaptureSoloUiEvidenceAsync("setup");
			if (phase == "resume")
			{
				OpenInteractionLab();
				ShowSaveBrowser();
				SoloSaveBrowserEntry saved = (await GetSoloSessionService().BrowseAsync()).Single(entry => entry.Preview?.Name == "M5 process restart");
				RequireInteraction(saved.Preview!.HumanSide == side && saved.Preview.Difficulty == difficulty, "Restart preview lost seat/profile.");
				RequireInteraction(!saved.Preview.IsTerminal, "The restart fixture must exercise an unfinished game.");
				await LoadSavedMatchAsync(saved.Preview.SaveId, false);
				RequireInteraction(_coordinator?.CurrentSnapshot?.IsPaused == true && !SaveDialogVisible && !_interactionLab,
					"A process restart must restore paused and leave practice mode.");
				long count = _coordinator!.CurrentSnapshot!.ActionCount;
				await TableFrames(20);
				RequireInteraction(_coordinator.CurrentSnapshot.ActionCount == count && _gameTable.ActiveMotionCount == 0, "Loading advanced or replayed animation.");
				Button resume = _tableActions.GetChildren().OfType<Button>().Single(button => button.Text == "Continue match");
				RequireInteraction(!resume.Disabled, "Continue is unavailable after load.");
				resume.EmitSignal(BaseButton.SignalName.Pressed);
				_reducedMotion.ButtonPressed = true;
				await DriveSoloSmokeAsync(side, 2000, untilTerminal: true);
				RequireInteraction(_currentSnapshot!.IsTerminal, "Resumed game did not finish.");
				ShowSaveGame(false);
				await SaveCurrentMatchAsync("M5 terminal");
				string history = _matchHistory.ExportText();
				ShowSaveBrowser();
				SoloSaveBrowserEntry terminal = (await GetSoloSessionService().BrowseAsync()).Single(entry => entry.Preview?.Name == "M5 terminal");
				await LoadSavedMatchAsync(terminal.Preview!.SaveId, false);
				await TableFrames(3);
				RequireInteraction(_currentSnapshot!.IsTerminal && !_tableActions.GetChildren().OfType<Button>().Any(button => button.Text == "Continue match"), "Terminal load offered further play.");
				RequireInteraction(history == _matchHistory.ExportText(), "Terminal history differs after load.");
				_soloSmokeReloadPending = true;
				_soloSmokeExpectedService = GetSoloSessionService();
				FindDescendants<Button>(_focusPresentation).Single(button => button.Name == "NewMatch").EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}

			Control setup = _layout.GetNode<Control>("MatchSetup");
			setup.GetNode<Button>("%GenerateSeed").EmitSignal(BaseButton.SignalName.Pressed);
			RequireInteraction(ulong.TryParse(_seedInput.Text, out ulong generated) && generated != 42, "Generate seed did not expose its value.");
			_seedInput.Text = "56142";
			_humanSide.Select(side == PlayerSide.Runner ? 0 : 1);
			_difficulty!.Select(difficulty == "easy" ? 1 : 0);
			SelectPresetById(_corpPreset, _corpPresetIds, $"system-gateway-{preset}-corp");
			SelectPresetById(_runnerPreset, _runnerPresetIds, $"system-gateway-{preset}-runner");
			_startButton.EmitSignal(BaseButton.SignalName.Pressed);
			_reducedMotion.ButtonPressed = true;
			await DriveSoloSmokeAsync(side, 2, untilTerminal: false);
			RequireInteraction(_currentSnapshot is { IsTerminal: false }, "Save fixture finished too soon.");
			_reducedMotion.ButtonPressed = false;
			await DriveSoloSmokeAsync(side, 1, untilTerminal: false);
			ShowSaveGame(false);
			SoloSaveReceipt boundary = await GetSoloSessionService().SaveAsync(_coordinator!, "M5 animation boundary");
			SettleSavedBoard();
			RequireInteraction(_currentSnapshot!.ActionCount == boundary.ActionCount && _gameTable.ActiveMotionCount == 0
				&& _tablePublications.Count == 0 && !_tableAwaitingSettlement, "Manual save confirmation did not settle at its captured boundary.");
			CloseSaveDialog();
			ContinueRecoveredFullMatch();
			_reducedMotion.ButtonPressed = true;
			await DriveSoloSmokeAsync(side, 1, untilTerminal: false);
			SetSettingsVisible(true);
			await CaptureSoloUiEvidenceAsync("settings");
			_settingsMenu!.GetNode<Button>("%SaveGame").EmitSignal(BaseButton.SignalName.Pressed);
			RequireInteraction(SaveDialogVisible && !SettingsVisible && !CanUseBoardActions(_currentSnapshot!), "Save dialog did not block board actions.");
			Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Escape });
			await TableFrames(2);
			RequireInteraction(!SaveDialogVisible && _settingsButton!.HasFocus(), "Escape from the save name did not restore focus.");
			ShowSaveGame(false);
			_saveDialog!.NameInput.Text = "M5 test checkpoint";
			_saveDialog.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
			await WaitSoloSmokeAsync(() => !_manualSaveInFlight && !SaveDialogVisible, "Manual save did not complete.");
			SoloSaveBrowserEntry checkpoint = (await GetSoloSessionService().BrowseAsync()).Single(entry => entry.Preview?.Name == "M5 test checkpoint");
			RequireInteraction(checkpoint.Preview!.Difficulty == difficulty && checkpoint.Preview.HumanSide == side, "Chosen opponent was not persisted.");
			await CheckSoloWriteFailureAsync();
			await CheckSoloBackupBrowserAsync();
			string brokenPath = Path.Combine(GetSoloSessionService().DirectoryPath, Guid.NewGuid().ToString("N") + ".solo.json");
			await File.WriteAllTextAsync(brokenPath, "{broken");
			ShowSaveBrowser();
			await WaitSoloSmokeAsync(() => _saveDialog.Message.StartsWith("Choose a game", StringComparison.Ordinal), "Save browsing failed.");
			RequireInteraction(FindDescendants<Label>(_saveDialog).Any(label => label.Text.Contains("corrupt or incompatible", StringComparison.Ordinal)), "Corrupt save did not show an isolated error.");
			RequireInteraction(GetViewport().GetVisibleRect().Encloses(_saveDialog.GetNode<Control>("Panel").GetGlobalRect()), "Save browser escaped the viewport.");
			await CaptureSoloUiEvidenceAsync("browser");
			MatchCoordinator previous = _coordinator!;
			Task abandoned = LoadSavedMatchAsync(checkpoint.Preview.SaveId, false);
			CloseSaveDialog();
			await abandoned;
			RequireInteraction(ReferenceEquals(previous, _coordinator), "Closing the browser allowed a late load to replace the match.");
			Action<MatchSnapshot> obsoletePublication = _coordinatorPublication!;
			long obsoleteGeneration = _sessionGeneration;
			MatchSnapshot obsoleteSnapshot = previous.CurrentSnapshot!;
			TableCardControl? hovered = _gameTable.VisibleCards.Values.FirstOrDefault(_gameTable.Hand.ContainsCard);
			if (hovered is not null) _gameTable.Hand.Hover(hovered, true);
			ShowSaveBrowser();
			await LoadSavedMatchAsync(checkpoint.Preview.SaveId, false);
			RequireInteraction(!ReferenceEquals(previous, _coordinator) && _coordinator!.CurrentSnapshot!.IsPaused, "Load did not replace the session paused.");
			string loadedUiLog = Path.Combine(_fullMatchSessionGlobalDirectory!, "ui-diagnostics.jsonl");
			RequireInteraction(Path.GetFileName(_fullMatchSessionGlobalDirectory!).StartsWith("saved-", StringComparison.Ordinal)
				&& File.Exists(loadedUiLog) && File.ReadAllText(loadedUiLog).Contains("\"startup\"", StringComparison.Ordinal),
				"A loaded save's log folder has no UI diagnostics.");
			obsoletePublication(obsoleteSnapshot with { Sequence = long.MaxValue });
			_pendingSnapshots.Enqueue(new(obsoleteGeneration, obsoleteSnapshot with { Sequence = long.MaxValue }));
			await TableFrames(3);
			RequireInteraction(_renderedSequence != long.MaxValue && _tableSelectedCard is null && _tableSelectedServer is null && _gameTable.Hand.HoveredCardId is null
				&& !_zoomOverlay.Visible && !SpatialVisible && _gameTable.ActiveMotionCount == 0, "Stale publication or transient UI survived load.");
			await CaptureSoloUiEvidenceAsync("paused");
			ShowSaveGame(false);
			await SaveCurrentMatchAsync("M5 paused save");
			RequireInteraction(_tableActions.GetChildren().OfType<Button>().Any(button => button.Text == "Continue match" && !button.Disabled),
				"Saving an already paused game left Continue disabled.");
			ContinueRecoveredFullMatch();
			await DriveSoloSmokeAsync(side, 1, untilTerminal: false);
			SetSettingsVisible(true);
			_settingsMenu.GetNode<Button>("%SaveAndQuit").EmitSignal(BaseButton.SignalName.Pressed);
			_saveDialog.NameInput.Text = "M5 process restart";
			GD.Print($"M5_CLIENT SAVE_READY side={side} preset={preset} difficulty={difficulty} save=true animation_boundary=true write_failure=true backup=true errors=true escape=true stale=true");
			_saveDialog.ConfirmButton.EmitSignal(BaseButton.SignalName.Pressed);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"M5_CLIENT FAIL {exception}");
			QuitCleanly(1);
		}
	}

	private async Task CheckSoloWriteFailureAsync()
	{
		MatchCoordinator coordinator = _coordinator!;
		await coordinator.PauseAsync();
		await GetSoloSessionService().RequestAutoSaveAsync(coordinator);
		SettleSavedBoard();
		string directory = GetSoloSessionService().DirectoryPath;
		string parked = directory + ".parked-" + Guid.NewGuid().ToString("N");
		await Task.Run(() => { Directory.Move(directory, parked); File.WriteAllText(directory, "test-only unavailable save directory"); });
		try
		{
			ShowSaveGame(true);
			await SaveCurrentMatchAsync("M5 failed save");
			RequireInteraction(ReferenceEquals(coordinator, _coordinator) && SaveDialogVisible
				&& _saveDialog!.Message.Contains("could not be saved", StringComparison.Ordinal), "Failed Save and quit did not preserve the running match and show recovery guidance.");
		}
		finally
		{
			await Task.Run(() => { File.Delete(directory); Directory.Move(parked, directory); });
			CloseSaveDialog();
		}
	}

	private async Task CheckSoloBackupBrowserAsync()
	{
		string path = Path.Combine(GetSoloSessionService().DirectoryPath, SoloSessionService.AutoSaveId + ".solo.json");
		byte[] primary = await File.ReadAllBytesAsync(path);
		await File.WriteAllTextAsync(path, "{broken primary");
		try
		{
			ShowSaveBrowser();
			await WaitSoloSmokeAsync(() => _saveDialog!.Message.StartsWith("Choose a game", StringComparison.Ordinal), "Backup browsing failed.");
			Button backup = FindDescendants<Button>(_saveDialog!).Single(button => button.Text == "Load verified backup");
			MatchCoordinator old = _coordinator!;
			backup.EmitSignal(BaseButton.SignalName.Pressed);
			await WaitSoloSmokeAsync(() => !ReferenceEquals(old, _coordinator) && !SaveDialogVisible, "Verified backup was not loadable.");
			RequireInteraction(_coordinator!.CurrentSnapshot!.IsPaused, "Backup did not load paused.");
		}
		finally { await File.WriteAllBytesAsync(path, primary); }
	}

	private async Task DriveSoloSmokeAsync(PlayerSide side, int limit, bool untilTerminal)
	{
		var agent = new FullGameContractAgent(side, _runtimeCardCatalog);
		for (int step = 0; step < limit; step++)
		{
			if (step > 0 && step % 100 == 0)
				GD.Print($"M5_CLIENT_PROGRESS side={side} human_choices={step} actions={_currentSnapshot?.ActionCount} turn={_currentSnapshot?.View.Turn.TurnNumber}");
			MatchSnapshot ready = await _coordinator!.WaitForHumanDecisionAsync().WaitAsync(WallTimeLimits.Seconds(30));
			await WaitSoloSmokeAsync(() => _currentSnapshot?.Sequence >= ready.Sequence && !_tableAwaitingSettlement
				&& Volatile.Read(ref _submissionInFlight) == 0, "Board did not settle at the next decision.");
			if (_currentSnapshot!.IsTerminal) return;
			RequireInteraction(!_currentSnapshot.IsPaused, "Smoke driver encountered an unexpected pause.");
			ActionId id = await agent.ChooseActionAsync(ready.Observation);
			_tableAllActions = true;
			RenderCardTableActions(_currentSnapshot);
			RequireInteraction(_tableActionButtons.TryGetValue(id, out Button? button) && !button.Disabled, "Issued action missing from the fallback action list.");
			button!.EmitSignal(BaseButton.SignalName.Pressed);
			if (_stagedAction is not null)
				_tableActions.GetChildren().OfType<Button>().Single(candidate => candidate.Text == "Confirm").EmitSignal(BaseButton.SignalName.Pressed);
		}
		if (untilTerminal) throw new InvalidOperationException("Client full game exceeded its human-action cap.");
	}

	private async Task WaitSoloSmokeAsync(Func<bool> predicate, string message)
	{
		ulong deadline = Time.GetTicksMsec() + (ulong)WallTimeLimits.Seconds(30).TotalMilliseconds;
		while (!predicate())
		{
			if (Time.GetTicksMsec() > deadline) throw new TimeoutException(message);
			await TableFrames(1);
		}
	}

	private async Task CaptureSoloUiEvidenceAsync(string name)
	{
		string directory = OS.GetEnvironment("NETRUNNER_M5_SCREENSHOT_DIRECTORY");
		if (string.IsNullOrWhiteSpace(directory)) return;
		await TableFrames(3);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Directory.CreateDirectory(directory);
		using Image screenshot = GetViewport().GetTexture().GetImage();
		Error saved = screenshot.SavePng(Path.Combine(directory, $"{name}-{GetWindow().Size.X}.png"));
		RequireInteraction(saved == Error.Ok, "Could not save no-art client evidence.");
	}
}
