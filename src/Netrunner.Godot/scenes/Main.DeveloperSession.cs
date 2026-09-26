using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private DeveloperSessionDialog? _developerDialog;
	private FileDialog? _developerFiles;
	private CancellationTokenSource? _developerCancellation;
	private int _developerRevision;
	private bool _developerExportRaw;
	private bool _developerBusy;
	private bool DeveloperDialogVisible => _developerDialog?.Visible == true;
	private static bool DeveloperToolsEnabled
	{
		get
		{
#if DEBUG
			return OS.GetCmdlineUserArgs().Contains("--developer", StringComparer.Ordinal);
#else
			return false;
#endif
		}
	}

	private void BindDeveloperTools()
	{
		if (!DeveloperToolsEnabled) return;
		var open = new Button { Name = "DeveloperTools", Text = "Developer tools…" };
		_settingsMenu!.GetNode<VBoxContainer>("Panel/Content").AddChild(open);
		open.Pressed += ShowDeveloperDialog;
		_developerDialog = GD.Load<PackedScene>("res://scenes/components/DeveloperSessionDialog.tscn").Instantiate<DeveloperSessionDialog>();
		GetModalLayer("DeveloperLayer", 15).AddChild(_developerDialog);
		_developerDialog.Hide();
		_developerDialog.CloseRequested += CloseDeveloperDialog;
		_developerDialog.PauseRequested += () => _ = PauseDeveloperSessionAsync();
		_developerDialog.AdvanceRequested += request => _ = AdvanceDeveloperSessionAsync(request);
		_developerDialog.RevealChanged += enabled => { if (enabled) _ = RefreshDeveloperInspectionAsync(); };
		_developerDialog.ImportRequested += () => ChooseDeveloperFile(export: false, raw: false);
		_developerDialog.ExportRequested += raw => ChooseDeveloperFile(export: true, raw);
		_developerFiles = new FileDialog
		{
			Name = "DeveloperFiles",
			Access = FileDialog.AccessEnum.Filesystem,
			Filters = ["*.json ; Engine or developer snapshot"],
			MinSize = new(1000, 640),
		};
		AddChild(_developerFiles);
		_developerFiles.FileSelected += path =>
		{
			if (_developerFiles.FileMode == FileDialog.FileModeEnum.OpenFile) _ = ImportDeveloperSnapshotAsync(path);
			else _ = ExportDeveloperSnapshotAsync(path, _developerExportRaw);
		};
	}

	private void ShowDeveloperDialog()
	{
		if (!DeveloperToolsEnabled || _developerDialog is null) return;
		CloseSaveDialog();
		SetSettingsVisible(false);
		_boardDrag?.Cancel("developer-dialog", false);
		ClearTableSelection();
		HideTablePile();
		HideCardZoom();
		HideCardHoverPreview();
		_developerRevision++;
		_developerCancellation?.Cancel();
		_developerCancellation?.Dispose();
		_developerCancellation = new();
		_developerBusy = false;
		_developerDialog.Show();
		_developerDialog.SetSession(_interactionLab ? null : _coordinator?.CurrentSnapshot);
		_developerDialog.SetBusy(false, _interactionLab ? "Import a real engine snapshot to leave the practice lab." : "Open a match or import a snapshot. Imports always start paused.");
		ApplyAccessibilityFocusOrder(_developerDialog);
		_developerDialog.GetNode<Button>("%Close").GrabFocus();
		if (!_interactionLab && _coordinator is not null) _ = PauseDeveloperSessionAsync();
	}

	private void CloseDeveloperDialog()
	{
		_developerRevision++;
		_developerCancellation?.Cancel();
		_developerDialog?.Hide();
		_developerFiles?.Hide();
		_settingsButton?.GrabFocus();
		if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
	}

	private bool CurrentDeveloperOperation(MatchCoordinator coordinator, long generation, int revision) =>
		DeveloperDialogVisible && IsCurrentSession(coordinator, generation) && revision == _developerRevision;

	private async Task PauseDeveloperSessionAsync()
	{
		if (_coordinator is not { } coordinator || _interactionLab) return;
		long generation = _sessionGeneration;
		int revision = _developerRevision;
		try
		{
			MatchSnapshot snapshot = await coordinator.PauseAsync();
			if (!CurrentDeveloperOperation(coordinator, generation, revision)) return;
			SettleSavedBoard();
			_developerDialog!.SetSession(snapshot);
			_developerDialog.SetBusy(_developerBusy, $"Paused at action {snapshot.ActionCount}. Close this panel to inspect the board; Continue resumes normal play.");
			await RefreshDeveloperInspectionAsync();
		}
		catch (Exception) { if (CurrentDeveloperOperation(coordinator, generation, revision)) _developerDialog!.SetBusy(false, "The session could not be paused."); }
	}

	private Task AdvanceDeveloperSessionAsync(DeveloperAdvanceRequest request) => RunDeveloperOperationAsync(async (coordinator, token) =>
	{
		DeveloperAdvanceResult result = await coordinator.AdvanceDeveloperAsync(request, token);
		return $"{result.Reason} · accepted {result.AcceptedActions} action(s) · action {result.Snapshot.ActionCount}"
			+ (result.Snapshot.Feedback?.IsError == true ? $" · {result.Snapshot.Feedback.Message}" : "");
	});

	private Task ExportDeveloperSnapshotAsync(string path, bool raw) => RunDeveloperOperationAsync(async (coordinator, token) =>
	{
		DeveloperSnapshotDocument document = await coordinator.ExportDeveloperSnapshotAsync(token);
		await Task.Run(() => DeveloperSnapshotStore.Write(path, document, GatewayBehaviorCatalog.Create(), _runtimeCardCatalog!, raw), token);
		return $"Exported {(raw ? "raw scenario snapshot" : "session with replay and policy")} · action {document.Snapshot.ActionSequence}.";
	});

	private async Task RunDeveloperOperationAsync(Func<MatchCoordinator, CancellationToken, Task<string>> operation)
	{
		if (!DeveloperToolsEnabled || _developerBusy || _coordinator is not { } coordinator || _interactionLab) return;
		long generation = _sessionGeneration;
		int revision = _developerRevision;
		_developerBusy = true;
		_developerDialog!.SetBusy(true, "Working… Pause stops at the next committed boundary.");
		try
		{
			string result = await operation(coordinator, _developerCancellation!.Token);
			if (!CurrentDeveloperOperation(coordinator, generation, revision)) return;
			SettleSavedBoard();
			_developerDialog.SetSession(coordinator.CurrentSnapshot);
			_developerDialog.SetBusy(false, result);
			await RefreshDeveloperInspectionAsync();
		}
		catch (OperationCanceledException) { }
		catch (Exception exception)
		{
			if (CurrentDeveloperOperation(coordinator, generation, revision))
				_developerDialog.SetBusy(false, $"Developer operation failed: {exception.Message}");
		}
		finally { if (revision == _developerRevision) _developerBusy = false; }
	}

	private async void ChooseDeveloperFile(bool export, bool raw)
	{
		int revision = _developerRevision;
		try
		{
			string directory = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "NetrunnerSolo", "developer");
			await Task.Run(() => Directory.CreateDirectory(directory));
			if (_sessionExited || revision != _developerRevision || !DeveloperDialogVisible) return;
			_developerExportRaw = raw;
			_developerFiles!.FileMode = export ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile;
			_developerFiles.Title = export ? "Export private developer snapshot" : "Import engine snapshot";
			_developerFiles.CurrentDir = directory;
			_developerFiles.CurrentFile = export ? raw ? "scenario.snapshot.json" : "session.developer.json" : "";
			_developerFiles.PopupCenteredRatio(0.65f);
		}
		catch (Exception) { if (!_sessionExited && revision == _developerRevision) _developerDialog!.SetBusy(false, "The developer folder could not be opened."); }
	}

	private async Task RefreshDeveloperInspectionAsync()
	{
		if (_developerDialog?.RevealEnabled != true || _coordinator is not { } coordinator || _interactionLab) return;
		int revision = _developerRevision;
		long generation = _sessionGeneration;
		try
		{
			DeveloperInspection inspection = await coordinator.InspectDeveloperStateAsync();
			if (CurrentDeveloperOperation(coordinator, generation, revision) && _developerDialog.RevealEnabled)
				_developerDialog.SetInspection(inspection);
		}
		catch (Exception) { /* An inspector cannot interrupt an engine session. */ }
	}

	private async Task ImportDeveloperSnapshotAsync(string path)
	{
		if (!DeveloperToolsEnabled || _developerBusy || !DeveloperDialogVisible) return;
		int revision = _developerRevision;
		long generation = _sessionGeneration;
		CancellationToken token = _developerCancellation!.Token;
		PlayerSide rawSide = _humanSide.Selected == 0 ? PlayerSide.Runner : PlayerSide.Corp;
		_developerBusy = true;
		_developerDialog!.SetBusy(true, "Validating snapshot, catalogs and replay…");
		MatchCoordinator? candidate = null;
		try
		{
			BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
			DeveloperSnapshotDocument document = await Task.Run(() => DeveloperSnapshotStore.Read(path, rawSide, behavior, _runtimeCardCatalog!), token);
			candidate = new(document, new MatchCoordinatorOptions
			{
				EnableDeveloperTools = true,
				HumanSide = document.HumanSide,
				BehaviorCatalog = behavior,
				RuntimeCardCatalog = _runtimeCardCatalog,
				CardMetadataResolver = _metadataResolver,
				AutoPassPriority = true,
				AgentDelay = TimeSpan.FromMilliseconds(45),
			});
			await candidate.StartAsync(token);
			if (_sessionExited || revision != _developerRevision || generation != _sessionGeneration || token.IsCancellationRequested) return;
			MatchCoordinator? previous = _coordinator;
			DetachCoordinatorPublications();
			_soloSession?.SetActiveSession(null);
			CloseFullMatchDiagnostics("developer-import");
			_coordinator = candidate;
			candidate = null;
			_interactionLab = false;
			_layout.GetNode<Control>("Header/InteractionLabToolbar").Hide();
			_labInstructions?.Hide();
			AttachCoordinatorPublications();
			ResetSoloPresentation();
			_fullMatchResumePaused = true;
			_fullMatchSessionGlobalDirectory = null;
			if (_sessionLogsButton is not null) _sessionLogsButton.Disabled = true;
			_startButton.Disabled = true;
			_humanSide.Select(document.HumanSide == PlayerSide.Runner ? 0 : 1);
			_matchHistory.ReportHeader = $"Developer scenario · {document.Origin}";
			QueueSnapshot(_coordinator.CurrentSnapshot!);
			SettleSavedBoard();
			_developerDialog.SetSession(_coordinator.CurrentSnapshot);
			_developerDialog.SetBusy(false, $"Imported {document.Origin} · paused at action {document.Snapshot.ActionSequence}. Use developer export to retain this scenario.");
			await RefreshDeveloperInspectionAsync();
			if (previous is not null) await previous.DisposeAsync();
		}
		catch (OperationCanceledException) { }
		catch (Exception)
		{
			if (!_sessionExited && revision == _developerRevision)
				_developerDialog.SetBusy(false, "Import failed. The snapshot may be corrupt or require different catalogs or policies. The current session is retained.");
		}
		finally
		{
			if (candidate is not null) await candidate.DisposeAsync();
			if (revision == _developerRevision) _developerBusy = false;
		}
	}
}
