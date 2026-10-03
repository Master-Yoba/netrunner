using System.Globalization;
using Godot;
using Netrunner.AI;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	// Survives New game/scene reload so an old writer cannot race a new rolling save.
	private static SoloSessionService? _applicationSoloSession;
	private OptionButton? _difficulty;
	private OptionButton? _personality;

	/// <summary>The setup form's difficulty: its items are Standard, Easy and Hard, in that order (A6 added Hard last).</summary>
	private string SelectedDifficulty => _difficulty?.Selected switch { 1 => "easy", 2 => "hard", _ => "standard" };

	private static int DifficultyIndex(string difficulty) => difficulty switch { "easy" => 1, "hard" => 2, _ => 0 };

	/// <summary>The setup form's personality for a bot on a side; one for the other side plays Balanced.</summary>
	private AgentPersonality SelectedPersonality(Netrunner.Core.PlayerSide botSide) =>
		_personality is { } option && option.Selected >= 0
		&& (AgentPersonality)option.GetItemId(option.Selected) is var personality && AgentPersonalities.Side(personality) == botSide
			? personality : AgentPersonality.Balanced;

	private void SelectPersonality(AgentPersonality personality)
	{
		if (_personality is { } option) option.Select(option.GetItemIndex((int)personality));
	}
	private SoloSaveDialog? _saveDialog;
	private SoloSessionService? _soloSession;
	private Control? _savePreviousFocus;
	private CancellationTokenSource? _browserCancellation;
	private bool _saveAndQuit;
	private bool _manualSaveInFlight;
	private bool _sessionExited;
	private int _saveDialogRevision;
	private long _sessionGeneration;
	private Action<MatchSnapshot>? _coordinatorPublication;
	private MatchCoordinator? _publicationCoordinator;
	private string? _lastReportedAutoSaveError;
	private string? _soloNotice;
	private double _soloNoticeRemaining;
	private bool SaveDialogVisible => _saveDialog?.Visible == true;
	private bool SessionModalVisible => SettingsVisible || SaveDialogVisible || DeveloperDialogVisible;
	private bool UseContractDiagnostic => _m4FullMatchSmoke || _fullMatchSmoke
		|| OS.GetCmdlineUserArgs().Contains("--m4-9-ui-smoke", StringComparer.Ordinal);

	public override void _Input(InputEvent @event)
	{
		// Native smoke events must not race the desktop's real pointer.
		if (_isolateSmokeInput && @event.Device != SmokeInputDevice) { GetViewport().SetInputAsHandled(); return; }
		if (DeveloperDialogVisible && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			if (_developerFiles?.Visible == true) _developerFiles.Hide();
			else CloseDeveloperDialog();
			GetViewport().SetInputAsHandled();
			return;
		}
		if (SaveDialogVisible && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			CloseSaveDialog();
			GetViewport().SetInputAsHandled();
		}
	}

	private void BindOfflineSetup(Control setup)
	{
		_difficulty = setup.GetNode<OptionButton>("%Difficulty");
		_personality = setup.GetNode<OptionButton>("%Personality");
		setup.GetNode<Button>("%GenerateSeed").Pressed += () =>
			_seedInput.Text = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))).ToString(CultureInfo.InvariantCulture);
		setup.GetNode<Button>("%Resume").Pressed += ShowSaveBrowser;
		_saveDialog = GD.Load<PackedScene>("res://scenes/components/SoloSaveDialog.tscn").Instantiate<SoloSaveDialog>();
		GetModalLayer("SaveLayer", 14).AddChild(_saveDialog);
		_saveDialog.Hide();
		_saveDialog.CloseRequested += CloseSaveDialog;
		_saveDialog.RefreshRequested += ShowSaveBrowser;
		_saveDialog.OpenFolderRequested += OpenSaveFolder;
		_saveDialog.LoadRequested += (id, backup) => _ = LoadSavedMatchAsync(id, backup);
		_saveDialog.SaveRequested += name => _ = SaveCurrentMatchAsync(name);
		BindDeveloperTools();
	}

	private SoloSessionService GetSoloSessionService()
	{
		if (_soloSession is not null) return _soloSession;
		if (!HasAuthenticatedOfficialContent()) throw new InvalidOperationException("Installed official game content is required to load saves.");
		// D-EL-8: each save loads with the content of the format it records.
		_soloSession = _applicationSoloSession ??= new(PlayFormats.SaveContent, SoloSaveDirectory());
		if (_coordinator is not null) _soloSession.SetActiveSession(_coordinator);
		return _soloSession;
	}

	private static string? SoloSaveDirectory()
	{
		// Smoke runs use an isolated application profile, never the player's saves.
		string value = OS.GetEnvironment("NETRUNNER_SAVE_DIRECTORY");
		return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
	}

	private void AttachCoordinatorPublications()
	{
		DetachCoordinatorPublications();
		long generation = Interlocked.Increment(ref _sessionGeneration);
		MatchCoordinator coordinator = _coordinator!;
		SoloSessionService? persistence = _useCardTable && !UseContractDiagnostic && coordinator.SupportsPlayerSaves ? GetSoloSessionService() : null;
		persistence?.SetActiveSession(coordinator);
		(int? Turn, Netrunner.Core.PlayerSide? Side)? lastAutoTurn = null;
		bool terminalSaved = false;
		_coordinatorPublication = snapshot =>
		{
			if (generation != Volatile.Read(ref _sessionGeneration)) return;
			_pendingSnapshots.Enqueue(new(generation, snapshot));
			CaptureFullMatchSetupSnapshot(snapshot);
			if (persistence is null) return;
			var turn = (snapshot.View.Turn.TurnNumber, snapshot.View.Turn.ActiveSide);
			if ((snapshot.View.Turn.Phase != Netrunner.Core.TurnPhase.Setup && lastAutoTurn != turn)
				|| (snapshot.IsTerminal && !terminalSaved))
			{
				lastAutoTurn = turn;
				terminalSaved |= snapshot.IsTerminal;
				_ = persistence.RequestAutoSaveAsync(coordinator);
			}
		};
		_publicationCoordinator = coordinator;
		coordinator.SnapshotPublished += _coordinatorPublication;
	}

	private void DetachCoordinatorPublications()
	{
		if (_publicationCoordinator is not null && _coordinatorPublication is not null)
			_publicationCoordinator.SnapshotPublished -= _coordinatorPublication;
		_publicationCoordinator = null;
		_coordinatorPublication = null;
	}

	private bool IsCurrentSession(MatchCoordinator coordinator, long generation) =>
		!_sessionExited && generation == _sessionGeneration && ReferenceEquals(_coordinator, coordinator);

	private void ReportAutomaticSaveStatus(double delta)
	{
		if (_currentSnapshot?.Feedback?.IsError == true) _soloNoticeRemaining = 0;
		_soloNoticeRemaining = Math.Max(0, _soloNoticeRemaining - delta);
		if (_soloNoticeRemaining > 0 && _soloNotice is not null && _useCardTable)
			_tableLastAction.Text = _soloNotice;
		string? error = _soloSession?.AutoSaveError;
		if (error == _lastReportedAutoSaveError) return;
		_lastReportedAutoSaveError = error;
		if (error is not null && _useCardTable) ShowSoloNotice(error);
	}

	private void ShowSoloNotice(string message)
	{
		_soloNotice = message;
		_soloNoticeRemaining = 4;
		_tableLastAction.Text = message;
	}

	private void PrepareSaveDialog()
	{
		if (!SaveDialogVisible) _savePreviousFocus = GetViewport().GuiGetFocusOwner();
		SetSettingsVisible(false);
		_boardDrag?.Cancel("save-dialog", false);
		ClearTableSelection();
		HideTablePile();
		HideCardZoom();
		HideCardHoverPreview();
		_saveDialogRevision++;
		_browserCancellation?.Cancel();
		_browserCancellation?.Dispose();
		_browserCancellation = new();
	}

	private void ShowSaveGame(bool quit)
	{
		if (_coordinator?.SupportsPlayerSaves != true || _interactionLab || _manualSaveInFlight || _saveDialog is null) return;
		PrepareSaveDialog();
		_saveAndQuit = quit;
		_saveDialog.ShowSave($"{_currentSnapshot?.View.Viewer} · Turn {_currentSnapshot?.View.Turn.TurnNumber} · {DateTime.Now:g}", quit);
		ApplyAccessibilityFocusOrder(_saveDialog);
		_saveDialog.NameInput.GrabFocus();
	}

	private void ShowSaveBrowser()
	{
		if (_saveDialog is null) return;
		PrepareSaveDialog();
		_saveDialog.ShowBrowser();
		ApplyAccessibilityFocusOrder(_saveDialog);
		_ = RefreshSavedGamesAsync(_saveDialogRevision, _browserCancellation!.Token);
	}

	private async Task RefreshSavedGamesAsync(int revision, CancellationToken token)
	{
		try
		{
			IReadOnlyList<SoloSaveBrowserEntry> entries = await GetSoloSessionService().BrowseAsync(token);
			if (_sessionExited || revision != _saveDialogRevision || token.IsCancellationRequested) return;
			_saveDialog!.SetEntries(entries);
			ApplyAccessibilityFocusOrder(_saveDialog);
		}
		catch (OperationCanceledException) { }
		catch (Exception)
		{
			if (!_sessionExited && revision == _saveDialogRevision)
				_saveDialog!.SetBusy(false, "Saved games could not be read. Check the save folder and installed game content, then refresh.");
		}
	}

	private void CloseSaveDialog()
	{
		_saveDialogRevision++;
		_browserCancellation?.Cancel();
		_saveDialog?.Hide();
		if (IsInstanceValid(_savePreviousFocus) && !_savePreviousFocus!.IsQueuedForDeletion() && _savePreviousFocus.IsVisibleInTree())
			_savePreviousFocus.GrabFocus();
		else _settingsButton?.GrabFocus();
		if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
	}

	private async Task SaveCurrentMatchAsync(string name)
	{
		if (_coordinator is not { } coordinator || _manualSaveInFlight) return;
		long generation = _sessionGeneration;
		int revision = _saveDialogRevision;
		bool quit = _saveAndQuit;
		bool wasPaused = coordinator.CurrentSnapshot?.IsPaused == true;
		bool exiting = false;
		_manualSaveInFlight = true;
		_saveDialog!.SetBusy(true, "Saving the current decision…");
		try
		{
			SoloSaveReceipt receipt = await GetSoloSessionService().SaveAsync(coordinator, name);
			if (!IsCurrentSession(coordinator, generation)) return;
			SettleSavedBoard();
			string message = $"Saved “{receipt.Preview.Name}” · Turn {receipt.Preview.Turn} · {receipt.Preview.Phase}";
			ShowSoloNotice(message);
			if (revision == _saveDialogRevision)
			{
				_saveDialog.SetBusy(false, message);
				if (quit) { exiting = true; QuitCleanly(); return; }
				CloseSaveDialog();
			}
		}
		catch (Exception)
		{
			if (IsCurrentSession(coordinator, generation) && revision == _saveDialogRevision)
				_saveDialog.SetBusy(false, "The game could not be saved. Check free space and save-folder permissions, then try again. Your match is still available.");
		}
		finally
		{
			if (IsCurrentSession(coordinator, generation))
			{
				_manualSaveInFlight = false;
				if (_currentSnapshot is { } current) RenderCardTableActions(current);
				if (!wasPaused && !exiting)
				{
					try { await coordinator.ContinueAsync(); }
					catch (Exception) { if (IsCurrentSession(coordinator, generation)) _tableLastAction.Text = "The match remains paused. Use Continue to resume."; }
				}
			}
		}
	}

	private void SettleSavedBoard()
	{
		ClearTableSelection();
		_gameTable.StopMotion();
		_tablePlaybackWait = 0;
		_skipAnimation = true;
		PollCardTable(0);
		_gameTable.StopMotion();
		_tableAwaitingSettlement = false;
		if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
	}

	private async Task LoadSavedMatchAsync(string id, bool backup)
	{
		_browserCancellation?.Cancel();
		_browserCancellation?.Dispose();
		_browserCancellation = new();
		int revision = ++_saveDialogRevision;
		long generation = _sessionGeneration;
		CancellationToken token = _browserCancellation!.Token;
		_saveDialog!.SetBusy(true, "Verifying the save and restoring its history…");
		LoadedSoloSession? candidate = null;
		try
		{
			string launchDiagnostics = OS.GetEnvironment("NETRUNNER_LAUNCH_DIAGNOSTICS");
			string traceRoot = string.IsNullOrWhiteSpace(launchDiagnostics)
				? ProjectSettings.GlobalizePath(SessionDiagnosticsRoot) : Path.GetFullPath(launchDiagnostics);
			string trace = Path.Combine(traceRoot, $"saved-{Guid.NewGuid():N}");
			candidate = await GetSoloSessionService().LoadAsync(id, backup, trace, DeveloperToolsEnabled, token);
			if (_sessionExited || revision != _saveDialogRevision || generation != _sessionGeneration || token.IsCancellationRequested) return;
			MatchCoordinator? previous = _coordinator;
			DetachCoordinatorPublications();
			CloseFullMatchDiagnostics("load-save");
			_coordinator = candidate.Coordinator;
			SoloSavePreview preview = candidate.Preview;
			candidate = null;
			_interactionLab = false;
			_layout.GetNode<Control>("Header/InteractionLabToolbar").Hide();
			_labInstructions?.Hide();
			AttachCoordinatorPublications();
			ResetSoloPresentation();
			_fullMatchResumePaused = true;
			_startButton.Disabled = true;
			_humanSide.Select(preview.HumanSide == Netrunner.Core.PlayerSide.Runner ? 0 : 1);
			_seedInput.Text = preview.Seed.ToString(CultureInfo.InvariantCulture);
			if (_corpPresetIds.ContainsValue(preview.CorpDeck)) SelectPresetById(_corpPreset, _corpPresetIds, preview.CorpDeck);
			if (_runnerPresetIds.ContainsValue(preview.RunnerDeck)) SelectPresetById(_runnerPreset, _runnerPresetIds, preview.RunnerDeck);
			_difficulty?.Select(DifficultyIndex(preview.Difficulty));
			SelectPersonality(preview.Personality);
			_matchHistory.ReportHeader = $"Netrunner · saved seed {preview.Seed} · {preview.HumanSide} · {preview.Difficulty}{(preview.Personality == AgentPersonality.Balanced ? "" : " " + AgentPersonalities.Key(preview.Personality))} · {preview.CorpDeck} / {preview.RunnerDeck}";
			_fullMatchSessionGlobalDirectory = trace;
			_fullMatchDiagnosticsParentDirectory = trace;
			OpenUiTrace(trace);
			WriteFullMatchDiagnostic("startup", new
			{
				appVersion = GetApplicationVersion(),
				engineVersion = GetEngineVersion(),
				os = OS.GetName(),
				osVersion = OS.GetVersion(),
				viewport = ViewportSize(),
				window = WindowSize(),
				seed = preview.Seed,
				corpPreset = preview.CorpDeck,
				runnerPreset = preview.RunnerDeck,
				humanSide = preview.HumanSide,
				resumedFrom = $"save:{id}",
				saveName = preview.Name,
				turn = preview.Turn,
			});
			QueueSnapshot(_coordinator.CurrentSnapshot!);
			SettleSavedBoard();
			CloseSaveDialog();
			ShowSoloNotice($"Loaded “{preview.Name}” · Turn {preview.Turn} · {preview.Phase}");
			_tableActions.GetChildren().OfType<Button>().FirstOrDefault()?.GrabFocus();
			if (previous is not null) await previous.DisposeAsync();
		}
		catch (OperationCanceledException) { }
		catch (Exception)
		{
			if (!_sessionExited && revision == _saveDialogRevision)
				_saveDialog.SetBusy(false, "This game could not be loaded. It may be corrupt or require different installed content or policies. Your current match has been kept.");
		}
		finally { if (candidate is not null) await candidate.Coordinator.DisposeAsync(); }
	}

	private void ResetSoloPresentation()
	{
		_soloNotice = null;
		_soloNoticeRemaining = 0;
		while (_pendingSnapshots.TryDequeue(out _)) { }
		_tablePublications.Clear();
		_beats?.Stop();
		_animationBatches.Clear();
		_tableHistory.Clear();
		_matchHistory.ClearHistory();
		ClearTableSelection();
		HideTablePile();
		HideCardZoom();
		_inspectedCard = null;
		_spatialSubmission = null;
		_stagedAction = null;
		_stagedDecision = null;
		_currentSnapshot = null;
		_renderedSequence = 0;
		_tablePlaybackWait = 0;
		_tableAwaitingSettlement = false;
		_fullMatchResumeContinueInFlight = false;
		_manualSaveInFlight = false;
		Interlocked.Exchange(ref _submissionInFlight, 0);
		_gameTable.ResetPresentation();
	}

	private async void OpenSaveFolder()
	{
		int revision = _saveDialogRevision;
		try
		{
			string directory = GetSoloSessionService().DirectoryPath;
			await Task.Run(() => Directory.CreateDirectory(directory));
			if (!_sessionExited && revision == _saveDialogRevision && OS.ShellOpen(directory) != Error.Ok)
				_saveDialog!.SetBusy(false, $"Your saves are in {directory}");
		}
		catch (Exception) { if (!_sessionExited && revision == _saveDialogRevision) _saveDialog!.SetBusy(false, "The save folder could not be opened."); }
	}

	private sealed record SessionPublication(long Generation, MatchSnapshot Snapshot);
}
