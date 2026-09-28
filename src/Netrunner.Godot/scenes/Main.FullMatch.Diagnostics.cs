using System.Globalization;
using System.Reflection;
using System.Text;
using Godot;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Owns the client side of a recoverable full match session.  The Match
/// coordinator receives the authoritative trace directory through its typed
/// options; this file records only what the player saw and did on the UI
/// thread.  In particular, it never serializes a MatchViewModel or an
/// observation wholesale because either could make a presentation log look
/// like an authoritative replay artifact.
/// </summary>
public partial class Main
{
	private const string SessionDiagnosticsRoot = "user://diagnostics";
	private static bool _resumeArgumentsConsumed;

	private Button? _sessionLogsButton;
	private string? _fullMatchResumeSessionDirectory;
	private string? _fullMatchResumeError;
	private string? _fullMatchSessionDirectory;
	private string? _fullMatchSessionGlobalDirectory;
	private string? _fullMatchDiagnosticsParentDirectory;
	private StreamWriter? _fullMatchUiTrace;
	private FileStream? _fullMatchUiTraceStream;
	private bool _fullMatchAuthoritativeTraceConfigured;
	private bool _sessionRecoverySmoke;
	private long? _resumeThroughActionSequence;
	private bool _fullMatchResumePaused;
	private bool _fullMatchResumeContinueInFlight;

	/// <summary>
	/// Parses the client-only recovery arguments before Main._Ready builds the
	/// normal table.  A deferred callback then starts the same official
	/// composition path as the ordinary Start button.
	/// </summary>
	public override void _EnterTree()
	{
		// Scene reload means New game; command-line recovery applies only once.
		IReadOnlyList<string> arguments = _resumeArgumentsConsumed ? Array.Empty<string>() : OS.GetCmdlineUserArgs();
		_resumeArgumentsConsumed |= arguments.Contains("--resume-session", StringComparer.Ordinal);
		_sessionRecoverySmoke = arguments.Contains("--session-recovery-smoke", StringComparer.Ordinal);
		if (!TryReadOption(arguments, "--resume-session", out string? resumeDirectory, out string? error))
		{
			_fullMatchResumeError = error;
		}
		else if (resumeDirectory is not null)
		{
			_fullMatchResumeSessionDirectory = resumeDirectory;
		}

		if (!TryReadOption(arguments, "--resume-at-action", out string? actionValue, out string? actionError))
			_fullMatchResumeError = actionError;
		else if (actionValue is not null)
		{
			if (_fullMatchResumeSessionDirectory is null
				|| !long.TryParse(actionValue, NumberStyles.None, CultureInfo.InvariantCulture, out long actionSequence))
				_fullMatchResumeError = "--resume-at-action requires a nonnegative sequence and --resume-session.";
			else _resumeThroughActionSequence = actionSequence;
		}
		CallDeferred(nameof(InitializeSessionDiagnostics));
	}

	private void InitializeSessionDiagnostics()
	{
		if (_layout is null || _layout.GetChildCount() == 0)
		{
			return;
		}

		if (_layout.GetChild(0) is HBoxContainer header && _sessionLogsButton is null)
		{
			_sessionLogsButton = new Button
			{
				Text = "Open logs",
				Disabled = true,
				TooltipText = "Open this match's local diagnostics folder.",
			};
			_sessionLogsButton.Pressed += OpenSessionLogs;
			header.AddChild(_sessionLogsButton);
			ApplyAccessibilityFocusOrder(header);
		}

		if (_fullMatchResumeError is not null)
		{
			_status.Text = $"Resume setup failed: {_fullMatchResumeError}";
			GD.PrintErr($"SESSION_RESUME FAIL error={_fullMatchResumeError}");
			if (_sessionRecoverySmoke) QuitCleanly(1);
			return;
		}

		if (_fullMatchResumeSessionDirectory is not null
			&& !_smokeTest
			&& !_gallerySmoke
			&& !_slice43UiSmoke
			&& !_slice44UiSmoke
			&& !_slice45UiSmoke
			&& !_slice46UiSmoke
			&& !_slice47UiSmoke
			&& !_slice48UxSmoke
			&& !_fullMatchSmoke
			&& !_m4FullMatchSmoke
			&& _coordinator is null)
		{
			BeginMatch();
		}
	}

	/// <summary>Writes focus transitions even when a native crash skips managed cleanup.</summary>
	public override void _Notification(int what)
	{
		if (what == NotificationApplicationFocusIn)
		{
			WriteFullMatchDiagnostic("focus", new { state = "in" });
		}
		else if (what == NotificationApplicationFocusOut)
		{
			WriteFullMatchDiagnostic("focus", new { state = "out" });
		}
	}

	private MatchCoordinator CreateOfficialFullMatchCoordinator(
		string corpPresetId,
		string runnerPresetId,
		CardDataSet data,
		PlayerSide humanSide,
		ulong seed,
		OfficialPresetCatalog officialPresets)
	{
		IBehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
		MatchRequest request = MatchRequestComposer.ComposeOfficial(
			corpPresetId,
			runnerPresetId,
			data,
			seed,
			officialPresets,
			behaviorCatalog: behavior);
		return CreateCoordinatorForRequest(request, data, humanSide, behavior, resumeSource: null);
	}

	private MatchCoordinator CreateCoordinatorForRequest(
		MatchRequest request,
		CardDataSet data,
		PlayerSide humanSide,
		IBehaviorCatalog behavior,
		string? resumeSource)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(behavior);

		BeginFullMatchSessionTrace(request, humanSide, resumeSource);
		FullMatchDefinition definition = FullMatchDefinition.FromRequest(request, data, behavior);
		PlayerSide opponentSide = humanSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
		IGameAgent opponent = resumeSource is not null
			? OfflineMatchFactory.CreateTraceOpponent(definition, humanSide, resumeSource, _resumeThroughActionSequence)
			: UseContractDiagnostic ? new FullGameContractAgent(new FullGameContractPolicy(opponentSide), definition.Runtime)
			: OfflineMatchFactory.CreateOpponent(definition, humanSide, _difficulty?.Selected == 1 ? "easy" : "standard");
		var options = new MatchCoordinatorOptions
		{
			EnableDeveloperTools = DeveloperToolsEnabled,
			HumanSide = humanSide,
			AutoPassPriority = true,
			AgentDelay = TimeSpan.FromMilliseconds(45),
			SessionTraceDirectory = _fullMatchSessionGlobalDirectory,
			ResumeTraceDirectory = resumeSource,
			ResumeThroughActionSequence = _resumeThroughActionSequence,
		};
		_fullMatchAuthoritativeTraceConfigured = options.SessionTraceDirectory is not null
			&& (resumeSource is null || options.ResumeTraceDirectory is not null);
		if (!_fullMatchAuthoritativeTraceConfigured)
		{
			GD.PrintErr(
				"SESSION_TRACE WARN authoritative Match trace options are unavailable; "
				+ "the UI log remains presentation-only.");
		}

		WriteFullMatchDiagnostic("coordinator-created", new
		{
			request.Seed,
			humanSide,
			resumedFrom = resumeSource,
			authoritativeTraceConfigured = _fullMatchAuthoritativeTraceConfigured,
		});
		return new MatchCoordinator(definition, opponent, options);
	}

	private void BeginFullMatchSessionTrace(
		MatchRequest request,
		PlayerSide humanSide,
		string? resumeSource)
	{
		CloseFullMatchDiagnostics("new-session");
		string launchRoot = OS.GetEnvironment("NETRUNNER_LAUNCH_DIAGNOSTICS");
		string root = string.IsNullOrWhiteSpace(launchRoot)
			? ProjectSettings.GlobalizePath(SessionDiagnosticsRoot)
			: Path.GetFullPath(launchRoot);
		Directory.CreateDirectory(root);
		string directoryName = $"session-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}";
		string directory = Path.Combine(root, directoryName);
		Directory.CreateDirectory(directory);
		_fullMatchSessionDirectory = $"{SessionDiagnosticsRoot}/{directoryName}";
		_fullMatchSessionGlobalDirectory = directory;
		_fullMatchDiagnosticsParentDirectory = root;

		_fullMatchUiTraceStream = new FileStream(
			Path.Combine(directory, "ui-diagnostics.jsonl"),
			System.IO.FileMode.Append,
			System.IO.FileAccess.Write,
			System.IO.FileShare.ReadWrite,
			bufferSize: 4096,
			options: System.IO.FileOptions.WriteThrough);
		_fullMatchUiTrace = new StreamWriter(
			_fullMatchUiTraceStream,
			new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			bufferSize: 4096,
			leaveOpen: true)
		{
			AutoFlush = true,
		};
		if (_sessionLogsButton is not null)
		{
			_sessionLogsButton.Disabled = false;
			_sessionLogsButton.TooltipText = $"Open logs: {directory}";
		}
		GD.Print($"SESSION_TRACE directory={directory} ui_log={Path.Combine(directory, "ui-diagnostics.jsonl")}");
		WriteFullMatchDiagnostic("startup", new
		{
			appVersion = GetApplicationVersion(),
			engineVersion = GetEngineVersion(),
			os = OS.GetName(),
			osVersion = OS.GetVersion(),
			viewport = ViewportSize(),
			window = WindowSize(),
			seed = request.Seed,
			corpPreset = request.CorpDeck.PresetId,
			runnerPreset = request.RunnerDeck.PresetId,
			humanSide,
			resumedFrom = resumeSource,
		});
	}

	private static string GetApplicationVersion() =>
		Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? typeof(Main).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? "unknown";

	private static string GetEngineVersion()
	{
		Godot.Collections.Dictionary version = Engine.GetVersionInfo();
		return version.ContainsKey("string") ? version["string"].ToString() : version.ToString();
	}

	private string ViewportSize()
	{
		Vector2 size = GetViewportRect().Size;
		return $"{size.X.ToString("F0", CultureInfo.InvariantCulture)}x{size.Y.ToString("F0", CultureInfo.InvariantCulture)}";
	}

	private static string WindowSize()
	{
		Vector2I size = DisplayServer.WindowGetSize();
		return $"{size.X.ToString(CultureInfo.InvariantCulture)}x{size.Y.ToString(CultureInfo.InvariantCulture)}";
	}

	private void WriteFullMatchDiagnostic(string kind, object payload)
	{
		if (_fullMatchUiTrace is null)
		{
			return;
		}

		try
		{
			_fullMatchUiTrace.WriteLine(EngineJson.Serialize(new
			{
				timestampUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
				kind,
				payload,
			}));
			_fullMatchUiTrace.Flush();
			_fullMatchUiTraceStream?.Flush(flushToDisk: true);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"SESSION_TRACE UI write failed kind={kind} error={exception.Message}");
		}
	}

	private void RecordFullMatchSelection(string selectionKind, string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		WriteFullMatchDiagnostic("selection", new { selectionKind, value });
	}

	private void RecordFullMatchSubmission(ActionSubmission submission, ActionKind? kind = null)
	{
		WriteFullMatchDiagnostic("submission", new
		{
			actor = submission.Actor.Value,
			decisionId = submission.DecisionId.Value,
			submission.Revision,
			actionId = submission.ActionId.Value,
			actionKind = kind?.ToString(),
		});
	}

	private void RecordFullMatchDisplayedSnapshot(MatchSnapshot snapshot)
	{
		if (_fullMatchSessionDirectory is null)
		{
			return;
		}

		WriteFullMatchDiagnostic("displayed-snapshot", new
		{
			snapshot.Sequence,
			actionCount = snapshot.ActionCount,
			viewer = snapshot.View.Viewer.ToString(),
			turn = snapshot.View.Turn.TurnNumber,
			activeSide = snapshot.View.Turn.ActiveSide.ToString(),
			decisionKind = snapshot.View.Decision?.Kind.ToString(),
			decisionOwner = snapshot.View.Decision?.OwnerSide.ToString(),
			snapshot.IsOpponentThinking,
			terminal = snapshot.IsTerminal,
			runActive = snapshot.View.Run is not null,
			feedbackKind = snapshot.Feedback?.Kind.ToString(),
			historyCount = snapshot.History.Count,
		});
	}

	private void RecordFullMatchException(Exception exception, string source)
	{
		WriteFullMatchDiagnostic("managed-exception", new
		{
			source,
			type = exception.GetType().FullName,
			message = exception.Message,
			detail = exception.ToString(),
		});
	}

	private void OpenSessionLogs()
	{
		if (_fullMatchSessionGlobalDirectory is null)
		{
			return;
		}

		string directory = _fullMatchDiagnosticsParentDirectory ?? _fullMatchSessionGlobalDirectory!;
		Error result = OS.ShellOpen(directory);
		if (result != Error.Ok)
		{
			_status.Text = $"Logs are at {directory}";
			GD.PrintErr($"SESSION_TRACE open failed directory={directory} error={result}");
		}
	}

	private void BeginResumedFullMatch()
	{
		try
		{
			if (!HasAuthenticatedOfficialContent())
			{
				throw new InvalidOperationException(OfficialContentUnavailableMessage());
			}

			(string source, MatchRequest request, PlayerSide humanSide) = LoadResumeSession(
				_fullMatchResumeSessionDirectory!);
			if (request.CorpDeck.PresetId is not { } corpPresetId
				|| request.RunnerDeck.PresetId is not { } runnerPresetId)
			{
				throw new InvalidOperationException("The recovered request does not contain official Corp and Runner presets.");
			}

			_humanSide.Select(humanSide == PlayerSide.Runner ? 0 : 1);
			_seedInput.Text = request.Seed.ToString(CultureInfo.InvariantCulture);
			SelectPresetById(_corpPreset, _corpPresetIds, corpPresetId);
			SelectPresetById(_runnerPreset, _runnerPresetIds, runnerPresetId);
			if (_useCardTable)
			{
				_matchHistory.ReportHeader = $"Netrunner · recovered seed {request.Seed} · {humanSide} · {corpPresetId} / {runnerPresetId}";
			}

			IBehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
			_coordinator = CreateCoordinatorForRequest(request, _embeddedCardData!, humanSide, behavior, source);
			_fullMatchResumePaused = true;
			_startButton.Disabled = true;
			_status.Text = "Recovered decision loaded · press Continue to resume this match.";
			AttachCoordinatorPublications();
			if (_sessionRecoverySmoke)
			{
				_ = RunSessionRecoverySmokeAsync();
			}
			else
			{
				_ = AwaitStartAsync();
			}
		}
		catch (Exception exception)
		{
			RecordFullMatchException(exception, "resume-setup");
			_status.Text = $"Resume setup failed: {exception.Message}";
			GD.PrintErr($"SESSION_RESUME FAIL error={exception.Message}");
			if (_sessionRecoverySmoke)
			{
				QuitCleanly(1);
			}
		}
	}

	private static void SelectPresetById(OptionButton button, Dictionary<int, string> ids, string presetId)
	{
		int index = ids.FirstOrDefault(pair => string.Equals(pair.Value, presetId, StringComparison.Ordinal)).Key;
		if (!ids.TryGetValue(index, out string? selectedPreset)
			|| !string.Equals(selectedPreset, presetId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException($"Recovered preset '{presetId}' is not available in this official catalog.");
		}

		button.Select(index);
	}

	private static (string Source, MatchRequest Request, PlayerSide HumanSide) LoadResumeSession(string rawDirectory)
	{
		string source = NormalizeDirectory(rawDirectory);
		if (!Directory.Exists(source))
		{
			throw new DirectoryNotFoundException($"Recovered session directory '{source}' does not exist.");
		}

		SessionTraceManifest manifest = SessionTrace.ReadManifest(source);
		return manifest.Request is { } request
			? (source, request, manifest.HumanSide)
			: throw new InvalidDataException("The recovered session manifest has no MatchRequest.");
	}

	private static string NormalizeDirectory(string rawDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rawDirectory);
		if (rawDirectory.StartsWith("user://", StringComparison.Ordinal))
			return ProjectSettings.GlobalizePath(rawDirectory);
		string launchDirectory = OS.GetEnvironment("NETRUNNER_LAUNCH_WORKING_DIRECTORY");
		return string.IsNullOrWhiteSpace(launchDirectory)
			? Path.GetFullPath(rawDirectory)
			: Path.GetFullPath(rawDirectory, launchDirectory);
	}

	private async Task RunSessionRecoverySmokeAsync()
	{
		bool passed = false;
		MatchSnapshot? snapshot = null;
		try
		{
			snapshot = await _coordinator!.StartAsync().WaitAsync(WallTimeLimits.Seconds(30));
			if (!await AwaitRenderedSequenceAsync(snapshot.Sequence, 240))
			{
				throw new TimeoutException("Recovered snapshot did not render.");
			}

			if (snapshot.View.Run is { Step: RunTimingStep.BreachAccessAbility, Access.Accessed.Count: > 0 } run
				&& run.Access.Accessed[^1] is { IsKnown: true } accessed
				&& (!_tableAccessPanel.Visible || _tableAccessCard.Card?.Id != accessed.Id))
				throw new InvalidOperationException("Recovered access card did not render.");
			passed = snapshot.Sequence > 0
				&& snapshot.ActionCount >= 0
				&& _fullMatchResumePaused
				&& _fullMatchAuthoritativeTraceConfigured;
			WriteFullMatchDiagnostic("recovery-smoke", new
			{
				snapshot.Sequence,
				actionCount = snapshot.ActionCount,
				paused = _fullMatchResumePaused,
				decisionKind = snapshot.View.Decision?.Kind.ToString(),
			});
			GD.Print(
				$"SESSION_RESUME {(passed ? "PASS" : "FAIL")} "
				+ $"directory={_fullMatchSessionGlobalDirectory} action_count={snapshot.ActionCount} "
				+ $"sequence={snapshot.Sequence} paused={_fullMatchResumePaused} "
				+ $"authoritative={_fullMatchAuthoritativeTraceConfigured}");
		}
		catch (Exception exception)
		{
			RecordFullMatchException(exception, "resume-smoke");
			GD.PrintErr($"SESSION_RESUME FAIL error={exception.Message}");
		}
		finally
		{
			if (_coordinator is not null)
			{
				await _coordinator.DisposeAsync();
				_coordinator = null;
			}

			CloseFullMatchDiagnostics("recovery-smoke-exit");
			QuitCleanly(passed ? 0 : 1);
		}
	}

	private void ContinueRecoveredFullMatch()
	{
		if (!_fullMatchResumePaused || _fullMatchResumeContinueInFlight || _coordinator is null)
		{
			return;
		}

		_fullMatchResumeContinueInFlight = true;
		WriteFullMatchDiagnostic("resume-continued", new
		{
			actionCount = _currentSnapshot?.ActionCount,
			sequence = _currentSnapshot?.Sequence,
		});
		_ = ContinueRecoveredFullMatchAsync();
	}

	private async Task ContinueRecoveredFullMatchAsync()
	{
		MatchCoordinator coordinator = _coordinator!;
		long generation = _sessionGeneration;
		try
		{
			await coordinator.ContinueAsync();
			if (IsCurrentSession(coordinator, generation)) _fullMatchResumePaused = false;
		}
		catch (Exception exception)
		{
			if (IsCurrentSession(coordinator, generation))
			{
				RecordFullMatchException(exception, "resume-continue");
				_status.Text = "The match could not continue. The saved game is still available.";
			}
		}
		finally
		{
			if (IsCurrentSession(coordinator, generation))
			{
				_fullMatchResumeContinueInFlight = false;
				if (_currentSnapshot is { } current && _useCardTable) RenderCardTableActions(current);
			}
		}
	}

	private void CloseFullMatchDiagnostics(string reason)
	{
		StreamWriter? writer = _fullMatchUiTrace;
		FileStream? stream = _fullMatchUiTraceStream;
		if (writer is null)
		{
			return;
		}

		try
		{
			WriteFullMatchDiagnostic("shutdown", new { reason });
			writer.Flush();
			stream?.Flush(flushToDisk: true);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"SESSION_TRACE UI close failed error={exception.Message}");
		}
		finally
		{
			_fullMatchUiTrace = null;
			_fullMatchUiTraceStream = null;
			try
			{
				writer.Dispose();
			}
			catch (Exception exception)
			{
				GD.PrintErr($"SESSION_TRACE UI writer dispose failed error={exception.Message}");
			}
			try
			{
				stream?.Dispose();
			}
			catch (Exception exception)
			{
				GD.PrintErr($"SESSION_TRACE UI stream dispose failed error={exception.Message}");
			}
			if (_sessionLogsButton is not null)
			{
				_sessionLogsButton.Disabled = true;
			}
		}
	}
}
