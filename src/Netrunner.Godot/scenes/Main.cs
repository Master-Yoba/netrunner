using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Native M3 presentation shell for the checked-in representative run.
/// Rendering is driven by immutable coordinator publications; no control in
/// this class writes Core state or advances a resolution frame.
/// </summary>
public partial class Main : Control
{
	private static bool _m4FullSmokeReloadPending;

	private static readonly Color Background = new(0.025f, 0.04f, 0.055f);
	private static readonly Color PanelBackground = new(0.055f, 0.08f, 0.105f);
	private static readonly Color PanelBorder = new(0.17f, 0.28f, 0.34f);
	private static readonly Color Accent = new(0.36f, 0.9f, 0.74f);
	private static readonly Color Muted = new(0.65f, 0.74f, 0.8f);
	private static readonly Color Warning = new(0.98f, 0.74f, 0.28f);

	private readonly Queue<IReadOnlyList<EngineEvent>> _animationBatches = new();
	private readonly CardMetadataSnapshot _metadataSnapshot = CardMetadataResolvers.Embedded;
	private readonly ICardMetadataResolver _metadataResolver = CardMetadataResolvers.Default;
	private readonly CardDataSet? _embeddedCardData = TryLoadEmbeddedCardData();
	private readonly OfficialPresetCatalog? _officialPresetCatalog = TryLoadOfficialPresetCatalog();
	private readonly IRuntimeCardCatalog? _runtimeCardCatalog = CreateRuntimeCardCatalog();
	private readonly EventFeedRegionPresenter _eventFeedRegion = new();
	private readonly ConcurrentQueue<SessionPublication> _pendingSnapshots = new();
	private readonly ConcurrentQueue<PlayerSide> _fullMatchSetupOrder = new();
	private readonly object _fullMatchSetupGate = new();
	private readonly Dictionary<string, List<TextCardControl>> _cardControls = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CardViewModel> _cardModels = new(StringComparer.Ordinal);
	private MatchCoordinator? _coordinator;
	private Func<Observation, CancellationToken, ValueTask<ActionId>>? _runnerPlaybackPolicy;
	private MatchSnapshot? _currentSnapshot;
	private LegalActionViewModel? _stagedAction;
	private DecisionViewModel? _stagedDecision;
	private CardViewModel? _inspectedCard;
	private VBoxContainer _layout = null!;
	private VBoxContainer _startPanel = null!;
	private VBoxContainer _board = null!;
	private Button _startButton = null!;
	private Label _status = null!;
	private Label _phase = null!;
	private Label _turnSummary = null!;
	private Label _focus = null!;
	private Label _decision = null!;
	private VBoxContainer _focusPresentation = null!;
	private Label _eventTitle = null!;
	private RichTextLabel _eventFeed = null!;
	private Label _damageStatus = null!;
	private Label _details = null!;
	private Label _corpSummary = null!;
	private Label _runnerSummary = null!;
	private Label _corpIdentityDetails = null!;
	private Label _runnerIdentityDetails = null!;
	private OptionButton _corpPreset = null!;
	private OptionButton _runnerPreset = null!;
	private LineEdit _seedInput = null!;
	private readonly Dictionary<int, string> _corpPresetIds = new();
	private readonly Dictionary<int, string> _runnerPresetIds = new();
	private VBoxContainer _servers = null!;
	private VBoxContainer _runnerRig = null!;
	private VBoxContainer _runnerHand = null!;
	private VBoxContainer _scores = null!;
	private VBoxContainer _actions = null!;
	private ScrollContainer _bodyScroll = null!;
	private PanelContainer _corpPanel = null!;
	private PanelContainer _centerPanel = null!;
	private PanelContainer _runnerPanel = null!;
	private PanelContainer _actionPanel = null!;
	private Button _zoomButton = null!;
	private PanelContainer _zoomOverlay = null!;
	private Button _galleryButton = null!;
	private PanelContainer? _galleryOverlay;
	private Label _zoomTitle = null!;
	private GameSymbolLabel _zoomText = null!;
	private HSlider _speed = null!;
	private CheckButton _reducedMotion = null!;
	private CheckButton _fastOpponent = null!;
	private CheckButton _autoPass = null!;
	private Button _stopAtWindow = null!;
	private Button _skipAnimations = null!;
	private PlayerSide? _lastFullMatchSetupOwner;
	private bool _animationRunning;
	private bool _skipAnimation;
	private int _framesWhileAnimating;
	private long _renderedSequence;
	private long _processFrameCount;
	private int _submissionInFlight;

	private static RuntimeCardCatalog? CreateRuntimeCardCatalog()
	{
		if (!CardDataResource.TryLoadEmbedded(out CardDataSet? data, out _)
			|| data is null)
		{
			return null;
		}

		try
		{
			return GatewayRuntimeCardCatalog.Create(data);
		}
		catch (Exception exception) when (exception is CardDataValidationException or InvalidOperationException)
		{
			// The metadata shell remains usable in a clean/synthetic checkout;
			// runtime card actions retain their behavior-only compatibility path
			// until an authenticated generated payload is available.
			return null;
		}
	}

	private static CardDataSet? TryLoadEmbeddedCardData()
	{
		return CardDataResource.TryLoadEmbedded(out CardDataSet? data, out _)
			? data
			: null;
	}

	private static OfficialPresetCatalog? TryLoadOfficialPresetCatalog()
	{
		try
		{
			return OfficialPresetCatalog.Embedded();
		}
		catch (Exception exception) when (exception is PresetDataValidationException or InvalidOperationException)
		{
			return null;
		}
	}

	public override void _Ready()
	{
		IReadOnlyList<string> arguments = OS.GetCmdlineUserArgs();
		bool fullSmokeReload = _m4FullSmokeReloadPending;
		_m4FullSmokeReloadPending = false;
		if (!ReadValidationArguments(arguments))
		{
			return;
		}
		if (arguments.Contains("--card-art-smoke", StringComparer.Ordinal)) PrepareCardArtworkSmoke();
		if (arguments.Contains("--m5-developer-smoke", StringComparer.Ordinal)) ProjectSettings.SetSetting("developer/enabled", true);
		BuildShell();
		StartLaunchMode(arguments, fullSmokeReload);
	}

	public override void _Process(double delta)
	{
		_processFrameCount++;
		if (_useCardTable)
		{
			PollCardTable(delta);
			UpdateBoardChoiceGeometry();
			PlaceActionChips();
			ContinueAccessAll();
			ContinueTurnIntent();
			PlaceRunOverlays();
			UpdateSpatialGeometry();
			UpdateAccessControls();
		}
		else PollCoordinator();
		ReportAutomaticSaveStatus(delta);
		if (_animationRunning)
		{
			_framesWhileAnimating++;
		}
	}

	public override void _ExitTree()
	{
		_sessionExited = true;
		Interlocked.Increment(ref _sessionGeneration);
		_browserCancellation?.Cancel();
		_developerCancellation?.Cancel();
		DetachCoordinatorPublications();
		_soloSession?.SetActiveSession(null);
		DisconnectCardHoverPreview();
		CloseFullMatchDiagnostics("shutdown");
		if (_coordinator is null)
		{
			return;
		}

		_coordinator.Dispose();
	}

	private void BuildShell()
	{
		if (_useCardTable)
		{
			BindProductionShell();
			return;
		}
		// Diagnostic modes retain their existing layout, outside the production scene.
		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.Free();
		}
		ApplyThemeFoundation();
		ColorRect backdrop = new()
		{
			Color = Background,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		SetFullRect(backdrop);
		AddChild(backdrop);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", _useCardTable ? 10 : 28);
		margin.AddThemeConstantOverride("margin_top", _useCardTable ? 8 : 22);
		margin.AddThemeConstantOverride("margin_right", _useCardTable ? 10 : 28);
		margin.AddThemeConstantOverride("margin_bottom", _useCardTable ? 8 : 22);
		SetFullRect(margin);
		AddChild(margin);

		_layout = new VBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_layout.AddThemeConstantOverride("separation", 12);
		margin.AddChild(_layout);

		BuildHeader();
		BuildStartPanel();
		BuildBoard();
		BuildFooter();
		BuildCardZoomOverlay();
		// Keep the initial setup surface keyboard navigable before any match
		// publication exists. Dynamic action controls refresh this same order
		// after each snapshot.
		ApplyAccessibilityFocusOrder(_startPanel);
		ApplyAccessibilityFocusOrder(_board);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		bool escape = @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape };
		if (SaveDialogVisible)
		{
			if (escape) { CloseSaveDialog(); GetViewport().SetInputAsHandled(); }
			return;
		}
		if (SettingsVisible)
		{
			if (escape) { SetSettingsVisible(false); GetViewport().SetInputAsHandled(); }
			return;
		}
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 } && ToggleAllLegalActions())
		{
			GetViewport().SetInputAsHandled();
			return;
		}
		if (_zoomOverlay.Visible)
		{
			if (escape) { HideCardZoom(); GetViewport().SetInputAsHandled(); }
			return;
		}
		if (_tablePileOverlay?.Visible == true)
		{
			if (escape) { HideTablePile(); GetViewport().SetInputAsHandled(); }
			return;
		}
		if (SpatialVisible && escape)
		{
			CancelSpatialSelection();
			GetViewport().SetInputAsHandled();
			return;
		}
		if (_boardChoiceVisible && (escape
			|| @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }))
		{
			DismissBoardChoices();
			GetViewport().SetInputAsHandled();
			return;
		}
		if (_useCardTable && escape)
		{
			if (_boardChoosingSubroutines)
			{
				CancelBoardBreakTargets();
				if (_currentSnapshot is { } snapshot) RefreshBoardIcebreaker(snapshot);
				GetViewport().SetInputAsHandled();
				return;
			}
			ClearTableSelection();
			_matchHistory.SetExpanded(false);
			GetViewport().SetInputAsHandled();
		}
	}

	private void BuildHeader()
	{
		var header = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(0, _useCardTable ? 36 : 62),
		};
		header.AddThemeConstantOverride("separation", 18);
		_layout.AddChild(header);

		var title = new Label
		{
			Text = "NETRUNNER SOLO",
			VerticalAlignment = VerticalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", _useCardTable ? 22 : 30);
		title.AddThemeColorOverride("font_color", Accent);
		header.AddChild(title);

		_status = MakeLabel("Match setup · waiting for a preset", 15, Muted);
		_status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_status.VerticalAlignment = VerticalAlignment.Center;
		header.AddChild(_status);

		_phase = MakeLabel("SETUP", 14, Warning);
		_phase.HorizontalAlignment = HorizontalAlignment.Right;
		_phase.VerticalAlignment = VerticalAlignment.Center;
		header.AddChild(_phase);
	}

	private void BuildStartPanel()
	{
		_startPanel = new VBoxContainer();
		_startPanel.AddThemeConstantOverride("separation", 10);
		PanelContainer panel = MakePanel();
		panel.CustomMinimumSize = new Vector2(0, 150);
		panel.AddChild(_startPanel);
		_layout.AddChild(panel);

		_startPanel.AddChild(MakeLabel("MATCH SETUP · SETUP TO OUTCOME", 18, Accent));
		_startPanel.AddChild(MakeWrappedLabel(
			"Choose your side and decks to play against the contract agent. Basic decks introduce the game; expanded decks add the booster cards.",
			14,
			Muted));
		BuildOfficialPresetSelectors();
		bool officialContentAvailable = HasAuthenticatedOfficialContent();
		_startButton = new Button
		{
			Text = officialContentAvailable
				? "Start selected official match"
				: "Official match unavailable",
			CustomMinimumSize = new Vector2(240, 42),
			Disabled = !officialContentAvailable,
		};
		_startButton.AddThemeFontSizeOverride("font_size", 16);
		_humanSide = new OptionButton();
		_humanSide.AddItem("Play Runner", (int)PlayerSide.Runner);
		_humanSide.AddItem("Play Corp", (int)PlayerSide.Corp);
		_startPanel.AddChild(_humanSide);
		_startPanel.AddChild(MakeLabel("Game seed", 11, Muted));
		_seedInput = new LineEdit
		{
			Text = "42",
			PlaceholderText = "42",
			CustomMinimumSize = new Vector2(240, 34),
			TooltipText = "Use the same seed and choices to reproduce a game.",
		};
		_seedInput.AddThemeFontSizeOverride("font_size", 14);
		_startPanel.AddChild(_seedInput);
		_startButton.Pressed += BeginMatch;
		_startPanel.AddChild(_startButton);

		_galleryButton = new Button
		{
			Text = "Open 77-card metadata gallery (dev)",
			CustomMinimumSize = new Vector2(300, 36),
		};
		_galleryButton.Pressed += ShowCardGallery;
		if (!_metadataSnapshot.IsOfficial)
		{
			_galleryButton.Disabled = true;
			_galleryButton.TooltipText = "Provision the authenticated 77-card snapshot for the dev gallery.";
			if (_gallerySmoke)
			{
				GD.PrintErr(_metadataSnapshot.Diagnostic);
			}
		}
		_startPanel.AddChild(_galleryButton);
	}

	private void BuildOfficialPresetSelectors()
	{
		_startPanel.AddChild(MakeLabel("OFFICIAL PRESETS", 12, Warning));
		_corpPreset = new OptionButton();
		_runnerPreset = new OptionButton();
		_corpPreset.TooltipText = "Choose the Corp basic or expanded official preset.";
		_runnerPreset.TooltipText = "Choose the Runner basic or expanded official preset.";
		PopulatePresetButton(_corpPreset, PlayerSide.Corp, _corpPresetIds);
		PopulatePresetButton(_runnerPreset, PlayerSide.Runner, _runnerPresetIds);
		_startPanel.AddChild(MakeLabel("Corp deck", 11, Muted));
		_startPanel.AddChild(_corpPreset);
		_startPanel.AddChild(MakeLabel("Runner deck", 11, Muted));
		_startPanel.AddChild(_runnerPreset);
	}

	private void PopulatePresetButton(
		OptionButton button,
		PlayerSide side,
		Dictionary<int, string> ids)
	{
		if (!HasAuthenticatedOfficialContent())
		{
			button.AddItem("Official presets unavailable · authenticated content required");
			button.Disabled = true;
			button.TooltipText = "Provision the authenticated 77-card System Gateway snapshot before starting an official match.";
			return;
		}

		IReadOnlyList<OfficialPreset> presets = _officialPresetCatalog!.For(side);
		int firstPlayable = -1;
		for (int index = 0; index < presets.Count; index++)
		{
			OfficialPreset preset = presets[index];
			bool enabled = preset.Playable;
			string state = enabled ? "READY" : "LOCKED · coverage pending";
			string kindLabel = preset.Kind == "starter" ? "BASIC (STARTER)" : preset.Kind.ToUpperInvariant();
			button.AddItem(
				$"{kindLabel} · {preset.PresetId} · {preset.CardCount} cards · {state}",
				index);
			ids[index] = preset.PresetId;
			button.SetItemDisabled(index, !enabled);
			if (enabled && firstPlayable < 0)
			{
				firstPlayable = index;
			}
		}

		if (firstPlayable >= 0)
		{
			button.Select(firstPlayable);
		}
		else
		{
			if (presets.Count == 0)
			{
				button.AddItem("No official preset for this side");
			}
			else
			{
				button.AddItem("Official presets locked · coverage pending");
			}
			button.Disabled = true;
		}
	}

	private string? SelectedPreset(OptionButton button, Dictionary<int, string> ids)
	{
		if (button.Disabled)
		{
			return null;
		}

		int selected = button.GetSelectedId();
		if (!ids.TryGetValue(selected, out string? presetId)
			|| _officialPresetCatalog is null
			|| !_officialPresetCatalog.TryGet(presetId, out OfficialPreset preset)
			|| !preset.Playable)
		{
			return null;
		}

		return presetId;
	}

	private void BuildFooter()
	{
		Label footer = MakeLabel(
			"Engine-issued actions · deterministic setup · 1920×1080 ↔ 2560×1440",
			11,
			new Color(0.42f, 0.5f, 0.56f));
		_layout.AddChild(footer);
	}

	private void BeginMatch()
	{
		if (_coordinator is not null)
		{
			return;
		}
		if (!_smokeTest)
		{
			BeginFullMatch();
			return;
		}

		MatchCoordinatorOptions options = _responsivenessSmoke
			? new MatchCoordinatorOptions
			{
				// This mode intentionally uses the real ten-second production delay;
				// its frame/input assertions run while the worker is actually waiting.
				// The checked-in M2 smoke is a behavior-only legacy fixture, so it
				// deliberately exercises the coordinator's compatibility adapter.
				AgentDelay = TimeSpan.FromSeconds(10),
				CardMetadataResolver = _metadataResolver,
			}
			: _smokeTest
			? new MatchCoordinatorOptions
			{
				// Keep the production-facing ten-second setting visible in
				// the smoke path while injecting a short deterministic delay. This
				// M2 fixture likewise remains on the behavior-only compatibility path.
				AgentDelay = TimeSpan.FromSeconds(10),
				CardMetadataResolver = _metadataResolver,
				Delay = static (_, cancellationToken) =>
					new ValueTask(Task.Delay(TimeSpan.FromMilliseconds(45), cancellationToken)),
			}
			: new MatchCoordinatorOptions
			{
				CardMetadataResolver = _metadataResolver,
				RuntimeCardCatalog = _runtimeCardCatalog,
			};
		_coordinator = M2MatchFactory.CreateRepresentative(options);
		_runnerPlaybackPolicy = _smokeTest
			? M2MatchFactory.CreateRepresentativeRunnerPolicy()
			: null;
		AttachCoordinatorPublications();
		_startButton.Disabled = true;
		_startButton.Text = "Starting Core match…";
		_status.Text = "Loading checked-in M2 scenario through MatchCoordinator…";
		_ = AwaitStartAsync();
		if (_smokeTest && Interlocked.Exchange(ref _smokeStarted, 1) == 0)
		{
			_ = _responsivenessSmoke ? RunResponsivenessSmokeAsync() : RunSmokeAsync();
		}
	}

	private async Task AwaitStartAsync()
	{
		MatchCoordinator coordinator = _coordinator!;
		long generation = _sessionGeneration;
		try
		{
			await coordinator.StartAsync();
		}
		catch (Exception exception)
		{
			if (!IsCurrentSession(coordinator, generation)) return;
			RecordFullMatchException(exception, "start");
			_status.Text = $"Match failed to start: {exception.Message}";
			if (_useCardTable && IsInstanceValid(_tableLastAction))
			{
				_tableLastAction.Text = exception.Message;
			}
			GD.PushError(exception.ToString());
			GD.Print($"M3_START FAIL {exception.Message}");
		}
	}

	private async Task RunSmokeAsync()
	{
		bool setup = false;
		bool humanDecision = false;
		bool delayedThinking = false;
		bool submitted = false;
		bool actionPanelInViewport = false;
		bool fullRun = false;
		bool targetStageCancelPreserved = false;
		bool targetSelectionExercised = false;
		bool targetCues = false;
		bool targetControls = false;
		bool completionNoPendingAction = false;
		try
		{
			MatchSnapshot started = await _coordinator!.StartAsync();
			setup = started.Events.Any(value => value.Kind == EngineEventKinds.RunInitiated)
				&& started.View.Run is not null;
			humanDecision = started.AwaitingHumanDecision
				&& started.View.LegalActions.Count > 0;
			if (!await AwaitRenderedSequenceAsync(started.Sequence, 60))
			{
				throw new TimeoutException("The fast M3 smoke did not render the setup publication.");
			}

			LegalAction? pass = started.Observation.LegalActions
				.FirstOrDefault(value => value.Kind == ActionKind.PassPriority);
			if (pass is null || started.Observation.Decision is not { } decision)
			{
				throw new InvalidOperationException("The fast M3 smoke did not receive the initial Runner pass decision.");
			}

			MatchSubmissionResult result = await _coordinator.SubmitActionAsync(
				new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id));
			submitted = result.Accepted;
			if (!submitted)
			{
				throw new InvalidOperationException(result.Feedback ?? "The initial Runner pass was rejected.");
			}

			if (!await AwaitRenderedSequenceAsync(result.Snapshot.Sequence, 60))
			{
				throw new TimeoutException("The fast M3 smoke did not render the first submitted action.");
			}
			actionPanelInViewport = IsFullyInsideViewport(_actionPanel);

			MatchSnapshot snapshot = result.Snapshot;
			for (int step = 0; step < 256; step++)
			{
				if (snapshot.View.Run is null)
				{
					fullRun = snapshot.RunOutcome is not null;
					break;
				}

				if (snapshot.IsOpponentThinking)
				{
					delayedThinking = true;
					snapshot = await WaitForPublishedSnapshotAsync(snapshot.Sequence, 180);
					if (!await AwaitRenderedSequenceAsync(snapshot.Sequence, 180))
					{
						throw new TimeoutException("The fast M3 smoke did not render a Corp publication.");
					}
					continue;
				}

				if (!snapshot.AwaitingHumanDecision)
				{
					snapshot = await WaitForPublishedSnapshotAsync(snapshot.Sequence, 180);
					if (!await AwaitRenderedSequenceAsync(snapshot.Sequence, 180))
					{
						throw new TimeoutException("The fast M3 smoke did not render a timing publication.");
					}
					continue;
				}

				LegalActionViewModel? target = snapshot.View.LegalActions
					.FirstOrDefault(ActionsRegionPresenter.IsTargetedAction);
				if (!targetSelectionExercised && target is not null)
				{
					targetSelectionExercised = true;
					string observationBeforeCancel = EngineJson.Serialize(snapshot.Observation);
					long sequenceBeforeCancel = snapshot.Sequence;
					StageTargetAction(snapshot.View.Decision!, target);
					bool staged = _stagedAction?.Id == target.Id
						&& _stagedDecision is not null;
					CancelStagedAction();
					MatchSnapshot afterCancel = _coordinator.CurrentSnapshot
						?? throw new InvalidOperationException("The coordinator lost its snapshot during target cancel smoke.");
					targetStageCancelPreserved = staged
						&& sequenceBeforeCancel == afterCancel.Sequence
						&& observationBeforeCancel == EngineJson.Serialize(afterCancel.Observation)
						&& _stagedAction is null;
					targetCues = _smokeTargetCueObserved;
					targetControls = _smokeTargetControlsObserved;
				}

				if (_runnerPlaybackPolicy is null)
				{
					throw new InvalidOperationException("The fast M3 smoke has no representative Runner policy.");
				}

				ActionId playbackAction = await _runnerPlaybackPolicy(
					snapshot.Observation,
					CancellationToken.None);
				MatchSubmissionResult playback = await _coordinator.SubmitRunnerActionAsync(playbackAction);
				if (!playback.Accepted)
				{
					throw new InvalidOperationException(playback.Feedback ?? "Deterministic Runner playback was rejected.");
				}

				snapshot = playback.Snapshot;
				if (!await AwaitRenderedSequenceAsync(snapshot.Sequence, 180))
				{
					throw new TimeoutException("The fast M3 smoke did not render a Runner playback publication.");
				}
			}

			MatchSnapshot finalSnapshot = _coordinator.CurrentSnapshot
				?? throw new InvalidOperationException("The fast M3 smoke ended without a final coordinator snapshot.");
			if (!fullRun && finalSnapshot.View.Run is null && finalSnapshot.RunOutcome is not null)
			{
				fullRun = true;
			}
			if (finalSnapshot.IsOpponentThinking)
			{
				delayedThinking = true;
			}
			completionNoPendingAction = fullRun
				&& finalSnapshot.View.Run is null
				&& finalSnapshot.View.Decision is null
				&& !finalSnapshot.IsOpponentThinking;
			bool renderedCompletion = await AwaitRenderedSequenceAsync(finalSnapshot.Sequence, 120);
			bool responsive = _framesWhileAnimating > 0;
			bool passed = setup
				&& humanDecision
				&& submitted
				&& actionPanelInViewport
				&& _smokeBodyColumnsInViewport
				&& _smokeLabelsLegible
				&& delayedThinking
				&& responsive
				&& fullRun
				&& _smokeEncounterFocusObserved
				&& _smokeAccessFocusObserved
				&& _smokeRunEndedObserved
				&& _smokeOutcomePresentationObserved
				&& renderedCompletion
				&& completionNoPendingAction
				&& targetSelectionExercised
				&& targetStageCancelPreserved
				&& targetCues
				&& targetControls;
			GD.Print(
				$"M3_SMOKE {(passed ? "PASS" : "FAIL")} "
				+ $"setup={setup} human_decision={humanDecision} submitted={submitted} "
				+ $"action_panel_in_viewport={actionPanelInViewport} "
				+ $"body_columns_in_viewport={_smokeBodyColumnsInViewport} "
				+ $"labels_legible={_smokeLabelsLegible} "
				+ $"delayed_agent={delayedThinking} responsive_frames={_framesWhileAnimating} "
				+ $"full_run={fullRun} encounter_focus={_smokeEncounterFocusObserved} "
				+ $"access_focus={_smokeAccessFocusObserved} run_ended={_smokeRunEndedObserved} "
				+ $"outcome_presentation={_smokeOutcomePresentationObserved} no_pending={completionNoPendingAction} "
				+ $"target_stage_cancel={targetStageCancelPreserved} target_cues={targetCues} "
				+ $"target_controls={targetControls}");
			await _coordinator.DisposeAsync();
			GetTree().Quit(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print(
				$"M3_SMOKE FAIL setup={setup} human_decision={humanDecision} "
				+ $"submitted={submitted} action_panel_in_viewport={actionPanelInViewport} "
				+ $"body_columns_in_viewport={_smokeBodyColumnsInViewport} "
				+ $"labels_legible={_smokeLabelsLegible} "
				+ $"full_run={fullRun} encounter_focus={_smokeEncounterFocusObserved} "
				+ $"access_focus={_smokeAccessFocusObserved} run_ended={_smokeRunEndedObserved} "
				+ $"target_stage_cancel={targetStageCancelPreserved} "
				+ $"error={exception.Message}");
			if (_coordinator is not null)
			{
				await _coordinator.DisposeAsync();
			}

			GetTree().Quit(1);
		}
	}

	private async Task<bool> AwaitRenderedSequenceAsync(long sequence, int maxFrames)
	{
		for (int frame = 0; frame < maxFrames; frame++)
		{
			if (_renderedSequence >= sequence)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				ObserveBodyColumnLayout();
				return true;
			}

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		bool rendered = _renderedSequence >= sequence;
		if (rendered)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			ObserveBodyColumnLayout();
		}

		return rendered;
	}

	private async Task<MatchSnapshot> WaitForPublishedSnapshotAsync(long previousSequence, int maxFrames)
	{
		for (int frame = 0; frame < maxFrames; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (_coordinator?.CurrentSnapshot is { } current && current.Sequence > previousSequence)
			{
				return current;
			}
		}

		throw new TimeoutException($"The coordinator did not publish after sequence {previousSequence}.");
	}

	/// <summary>
	/// Dedicated M3 gate for the real ten-second agent-latency requirement. It
	/// intentionally runs separately from the fast smoke so ordinary verify
	/// remains quick, then holds the worker delay for at least ten seconds while
	/// exercising local inspection, a control change, zoom, and layout checks.
	/// </summary>
	private async Task RunResponsivenessSmokeAsync()
	{
		bool setup = false;
		bool submitted = false;
		bool thinking = false;
		bool inspectionWithinOneFrame = false;
		bool controlWithinOneFrame = false;
		bool hoverSelectionWithinOneFrame = false;
		bool zoomVisible = false;
		bool layoutAt1920x1080 = false;
		bool layoutAt2560x1440 = false;
		bool heldTenSeconds = false;
		int thinkingFrames = 0;
		TimeSpan holdDuration = TimeSpan.Zero;
		double averageFps = 0;
		try
		{
			MatchSnapshot started = await _coordinator!.StartAsync();
			setup = started.Events.Any(value => value.Kind == EngineEventKinds.RunInitiated)
				&& started.View.Run is not null;
			for (int frame = 0; frame < 120 && _renderedSequence < started.Sequence; frame++)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			LegalAction? pass = started.Observation.LegalActions
				.FirstOrDefault(value => value.Kind == ActionKind.PassPriority);
			if (pass is null || started.Observation.Decision is not { } decision)
			{
				throw new InvalidOperationException("The responsiveness smoke did not receive the initial Runner pass decision.");
			}

			MatchSubmissionResult result = await _coordinator.SubmitActionAsync(
				new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id));
			submitted = result.Accepted;
			if (!submitted)
			{
				throw new InvalidOperationException(result.Feedback ?? "The responsiveness smoke pass action was rejected.");
			}

			for (int frame = 0; frame < 180; frame++)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (_coordinator.CurrentSnapshot?.IsOpponentThinking == true)
				{
					thinking = true;
					break;
				}
			}

			if (!thinking)
			{
				throw new TimeoutException("The responsiveness smoke did not observe the Corp thinking publication.");
			}

			CardViewModel card = _cardModels.Values.FirstOrDefault()
				?? throw new InvalidOperationException("The responsiveness smoke rendered no inspectable card.");
			TextCardControl cardControl = (_cardControls.TryGetValue(card.Id, out List<TextCardControl>? controls)
				? controls!.FirstOrDefault(value => value.IsInsideTree())
				: null)
				?? throw new InvalidOperationException("The responsiveness smoke rendered no live card control.");
			long localBaseline = _processFrameCount;
			InspectCard(card.Id, card);
			_speed.Value = 1.5;
			_reducedMotion.ButtonPressed = true;
			// Exercise the same retained states driven by real pointer signals. The
			// explicit properties make the one-frame assertion observable without
			// depending on a screenshot or colour-only styling.
			cardControl.EmitSignal(Control.SignalName.MouseEntered);
			cardControl.SetSelected(true);
			ShowInspectedCardZoom();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			long localFrames = _processFrameCount - localBaseline;
			inspectionWithinOneFrame = localFrames <= 1 && _details.Text.Contains(card.Title, StringComparison.Ordinal);
			controlWithinOneFrame = localFrames <= 1
				&& Math.Abs(_speed.Value - 1.5) < 0.01
				&& _reducedMotion.ButtonPressed;
			hoverSelectionWithinOneFrame = localFrames <= 1
				&& cardControl.IsHovered
				&& cardControl.IsSelected;
			cardControl.EmitSignal(Control.SignalName.MouseExited);
			cardControl.SetSelected(false);
			zoomVisible = _zoomOverlay.Visible && IsFullyInsideViewport(_zoomOverlay);

			Stopwatch hold = Stopwatch.StartNew();
			long holdStartFrames = _processFrameCount;
			while (hold.Elapsed < TimeSpan.FromSeconds(10.25))
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			hold.Stop();
			holdDuration = hold.Elapsed;
			thinkingFrames = (int)Math.Max(0, _processFrameCount - holdStartFrames);
			heldTenSeconds = holdDuration >= TimeSpan.FromSeconds(10);
			averageFps = thinkingFrames / Math.Max(holdDuration.TotalSeconds, 0.001);
			layoutAt1920x1080 = await CheckViewportLayoutAsync(new Vector2I(1920, 1080));
			layoutAt2560x1440 = await CheckViewportLayoutAsync(new Vector2I(2560, 1440));

			// The M3 gate is a 60-FPS interaction target. Allow a small timing
			// tolerance for scheduler jitter, but never report a materially slower
			// 30-FPS loop as satisfying that gate.
			bool responsiveFps = averageFps >= 55;
			bool passed = setup
				&& submitted
				&& thinking
				&& heldTenSeconds
				&& inspectionWithinOneFrame
				&& controlWithinOneFrame
				&& hoverSelectionWithinOneFrame
				&& zoomVisible
				&& responsiveFps
				&& layoutAt1920x1080
				&& layoutAt2560x1440;
			GD.Print(
				$"M3_LATENCY_SMOKE {(passed ? "PASS" : "FAIL")} "
				+ $"setup={setup} submitted={submitted} thinking={thinking} "
				+ $"held_ms={holdDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} "
				+ $"thinking_frames={thinkingFrames} avg_fps={averageFps.ToString("F1", CultureInfo.InvariantCulture)} "
				+ $"inspection_one_frame={inspectionWithinOneFrame} control_one_frame={controlWithinOneFrame} "
				+ $"hover_selection_one_frame={hoverSelectionWithinOneFrame} "
				+ $"zoom_in_viewport={zoomVisible} layout_1920x1080={layoutAt1920x1080} "
				+ $"layout_2560x1440={layoutAt2560x1440}");
			await _coordinator.DisposeAsync();
			GetTree().Quit(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print(
				$"M3_LATENCY_SMOKE FAIL setup={setup} submitted={submitted} thinking={thinking} "
				+ $"held_ms={holdDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} "
				+ $"inspection_one_frame={inspectionWithinOneFrame} control_one_frame={controlWithinOneFrame} "
				+ $"hover_selection_one_frame={hoverSelectionWithinOneFrame} "
				+ $"zoom_in_viewport={zoomVisible} layout_1920x1080={layoutAt1920x1080} "
				+ $"layout_2560x1440={layoutAt2560x1440} error={exception.Message}");
			if (_coordinator is not null)
			{
				await _coordinator.DisposeAsync();
			}

			GetTree().Quit(1);
		}
	}

	private async Task<bool> CheckViewportLayoutAsync(Vector2I size)
	{
		Vector2I previousSize = DisplayServer.WindowGetSize();
		DisplayServer.WindowSetSize(size);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool inside = IsFullyInsideViewport(_board)
			&& IsFullyInsideViewport(_actionPanel)
			&& AreBodyColumnsInsideViewport()
			&& AreVisibleLabelsLegible(_layout)
			&& IsFullyInsideViewport(_focusPresentation)
			&& IsFullyInsideViewport(_zoomButton)
			&& IsFullyInsideViewport(_zoomOverlay);
		DisplayServer.WindowSetSize(previousSize);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		return inside;
	}

	private void ObserveBodyColumnLayout()
	{
		if (_smokeTest && _board.IsVisibleInTree())
		{
			_smokeBodyColumnsInViewport &= AreBodyColumnsInsideViewport();
			_smokeLabelsLegible &= AreVisibleLabelsLegible(_layout);
		}
	}

	private bool AreBodyColumnsInsideViewport()
	{
		if (!IsInstanceValid(_bodyScroll) || !_bodyScroll.IsVisibleInTree())
		{
			return false;
		}

		Rect2 viewportRect = GetViewport().GetVisibleRect();
		Rect2 bodyViewportRect = _bodyScroll.GetGlobalRect();
		return IsHorizontallyInside(bodyViewportRect, viewportRect)
			&& IsHorizontallyInside(_corpPanel, bodyViewportRect)
			&& IsHorizontallyInside(_centerPanel, bodyViewportRect)
			&& IsHorizontallyInside(_runnerPanel, bodyViewportRect);
	}

	private void PollCoordinator()
	{
		while (_pendingSnapshots.TryDequeue(out SessionPublication? publication))
		{
			if (publication.Generation != _sessionGeneration) continue;
			MatchSnapshot snapshot = publication.Snapshot;
			if (snapshot.Sequence <= _renderedSequence)
			{
				continue;
			}

			_renderedSequence = snapshot.Sequence;
			RenderSnapshot(snapshot);
			if (snapshot.Events.Count > 0)
			{
				_animationBatches.Enqueue(snapshot.Events);
			}
		}

		if (_animationBatches.Count > 0 && !_animationRunning)
		{
			_ = AnimateEventsAsync();
		}
	}

	private void QueueSnapshot(MatchSnapshot snapshot) => _pendingSnapshots.Enqueue(new(_sessionGeneration, snapshot));

	private void RenderSnapshot(MatchSnapshot snapshot)
	{
		MatchSnapshot issued = snapshot;
		if (_useCardTable) snapshot = WithTurnPreview(snapshot);
		ApplyAutoPassSetting();
		RefreshInspectedCard(snapshot.View);
		MatchSnapshot? previous = _currentSnapshot;
		bool decisionChanged = previous is null
			|| !SameDecision(previous.View.Decision, snapshot.View.Decision);
		_currentSnapshot = snapshot;
		_fullMatchResumePaused = snapshot.IsPaused;
		RecordFullMatchDisplayedSnapshot(issued);
		_smokeRunEndedObserved |= _smokeTest
			&& snapshot.Events.Any(value => value.Kind == EngineEventKinds.RunEnded);
		if (decisionChanged)
		{
			_stagedAction = null;
			_stagedDecision = null;
		}

		if (_startPanel.GetParent() is CanvasItem startPanel)
		{
			startPanel.Visible = false;
		}
		if (_useCardTable)
		{
			RenderCardTable(snapshot, decisionChanged);
			return;
		}
		_board.Visible = true;
		MatchViewModel view = snapshot.View;
		TurnRegionState turnRegion = TurnRegionPresenter.Present(snapshot);
		_status.Text = view.CorpIdentity is null && snapshot.RunOutcome is { } completedRun && snapshot.View.Run is null
			? $"Representative slice complete · {completedRun.Summary}"
			: snapshot.IsOpponentThinking
			? "Opponent is thinking · card inspection remains available"
			: view.Outcome.IsTerminal
				? $"Match complete · {view.Outcome.Winner} · {view.Outcome.Reason}"
			: $"Your turn · {snapshot.View.Viewer} can choose an issued action";
		_phase.Text = turnRegion.IsAvailable
			? $"{turnRegion.Turn.PhaseLabel} · {turnRegion.Turn.TimingLabel}"
			: view.CorpIdentity is null && snapshot.RunOutcome is not null && view.Run is null ? "SLICE COMPLETE"
			: view.Outcome.IsTerminal ? "GAME OVER" : "TIMING UNAVAILABLE";
		string installedCounters = view.InstalledAdvancementCounters.Count == 0
			? "INSTALLED ADVANCEMENT · none visible"
			: "INSTALLED ADVANCEMENT · "
				+ string.Join(
					" · ",
					view.InstalledAdvancementCounters.Select(counter => counter.Summary));
		_turnSummary.Text = turnRegion.IsAvailable
			? $"{turnRegion.Summary}\n{installedCounters}"
			: $"{turnRegion.Summary}\n{turnRegion.Diagnostic}\n{installedCounters}";
		_turnSummary.AddThemeColorOverride(
			"font_color",
			turnRegion.IsAvailable ? Colors.White : Warning);
		_focus.Text = view.Run is { } run
			? $"{run.FocusLabel} · attacked {run.AttackedServerId} · {run.Step}"
			: snapshot.IsTerminal
				? $"MATCH OUTCOME · {view.Outcome.Winner} · {view.Outcome.Reason}"
			: snapshot.RunOutcome is { } ended
				? $"REPRESENTATIVE SLICE · COMPLETE · {ended.Reason}"
			: "RUN FOCUS · no active run";
		_decision.Text = snapshot.IsOpponentThinking
			? "OPPONENT THINKING · asynchronous agent delay\nThe render loop is free to process input and card inspection."
			: view.Decision is { } prompt
				? $"{prompt.OwnerSide} decision · {prompt.Prompt}\nRevision {prompt.Revision} · {ActionsRegionPresenter.Present(snapshot).DisplayActions.Count} displayed action(s)"
			: view.Outcome.IsTerminal
				? "Outcome reached."
			: snapshot.RunOutcome is { } finished && view.Run is null
				? $"REPRESENTATIVE RUN COMPLETE\n{finished.Summary}\nNo further Core action is pending."
			: "No pending decision.";

		BoardRegionState board = BoardRegionPresenter.Present(view);
		_corpSummary.Text = board.CorpSummary;
		_runnerSummary.Text = board.RunnerSummary;
		RenderIdentityPanels(view);
		RenderSlice46DamageStatus(snapshot);
		_cardControls.Clear();
		_cardModels.Clear();
		RenderFocusPresentationRegion(snapshot);
		RenderServers(board.Servers, board.RunnerRig);
		RenderRunnerCards(board);
		RenderActions(snapshot);
		if (snapshot.Feedback is { } feedback)
		{
			_details.Text = $"{feedback.Kind.ToString().ToUpperInvariant()}\n{feedback.Message}";
			_details.AddThemeColorOverride("font_color", feedback.IsError ? Warning : Muted);
		}
	}

	private TextCardControl MakeCard(CardViewModel? card)
	{
		var control = new TextCardControl();
		control.SetCard(card);
		if (card is not null)
		{
			_cardModels[card.Id] = card;
			if (!_cardControls.TryGetValue(card.Id, out List<TextCardControl>? controls))
			{
				controls = new List<TextCardControl>();
				_cardControls.Add(card.Id, controls);
			}

			controls.Add(control);
		}

		control.CardClicked += HandleCardClicked;
		control.CardInspected += cardId => InspectCard(cardId, card);
		return control;
	}

	private void HandleCardClicked(string cardId)
	{
		if (_currentSnapshot is null
			|| _currentSnapshot.IsOpponentThinking
			|| _currentSnapshot.View.Decision is not { } decision
			|| decision.OwnerSide != PlayerSide.Runner)
		{
			return;
		}

		LegalActionViewModel? action = ActionsRegionPresenter.Present(_currentSnapshot).DisplayActions
			.FirstOrDefault(candidate => ActionsRegionPresenter.GetTargetCardId(candidate.Parameters) == cardId);
		if (action is not null)
		{
			StageTargetAction(decision, action);
		}
	}

	private void InspectCard(string cardId, CardViewModel? card)
	{
		if (card is null)
		{
			return;
		}

		_inspectedCard = card;
		RecordFullMatchSelection("card-inspect", card.Id);
		_details.Text = $"{card.Title}\n{card.Type} · {card.DefinitionId}\n"
			+ (card.AbilityCue is { Length: > 0 } cue ? $"{cue}\n" : string.Empty)
			+ (card.FirstXUseCue is { Length: > 0 } firstXCue ? $"{firstXCue}\n" : string.Empty)
			+ (card.CounterChips.Count == 0 ? string.Empty : $"{string.Join("  ", card.CounterChips)}\n")
			+ $"{FormatCardRules(card.RulesText)}\n\nClick feedback is local and immediate; engine actions remain ID-only.\n"
			+ "Use Zoom inspected card for an enlarged, complete text view.";
		_details.AddThemeColorOverride("font_color", Colors.White);
		_zoomButton.Visible = true;
	}

	private static bool SameDecision(DecisionViewModel? left, DecisionViewModel? right) =>
		left is null && right is null
		|| left is not null
			&& right is not null
			&& left.Id == right.Id
			&& left.Revision == right.Revision
			&& left.Owner == right.Owner;

	private static Label MakeLabel(string text, int size, Color colour)
	{
		var label = new Label
		{
			Text = text,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", colour);
		return label;
	}

	private static Label MakeWrappedLabel(string text, int size, Color colour)
	{
		Label label = MakeLabel(text, size, colour);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		return label;
	}

	private static PanelContainer MakePanel()
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = PanelBackground,
			BorderColor = PanelBorder,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 7,
			CornerRadiusTopRight = 7,
			CornerRadiusBottomLeft = 7,
			CornerRadiusBottomRight = 7,
			ContentMarginLeft = 10,
			ContentMarginTop = 8,
			ContentMarginRight = 10,
			ContentMarginBottom = 8,
		});
		return panel;
	}

	private static void SetFullRect(Control control)
	{
		control.SetAnchorsPreset(LayoutPreset.FullRect);
		control.OffsetLeft = 0;
		control.OffsetTop = 0;
		control.OffsetRight = 0;
		control.OffsetBottom = 0;
	}

	private bool IsFullyInsideViewport(Control control)
	{
		if (!IsInstanceValid(control) || !control.IsVisibleInTree())
		{
			return false;
		}

		Rect2 controlRect = control.GetGlobalRect();
		Rect2 viewportRect = GetViewport().GetVisibleRect();
		const float epsilon = 0.5f;
		return controlRect.Position.X >= viewportRect.Position.X - epsilon
			&& controlRect.Position.Y >= viewportRect.Position.Y - epsilon
			&& controlRect.End.X <= viewportRect.End.X + epsilon
			&& controlRect.End.Y <= viewportRect.End.Y + epsilon;
	}

	private static bool IsHorizontallyInside(Control control, Rect2 outerRect)
	{
		if (!IsInstanceValid(control) || !control.IsVisibleInTree())
		{
			return false;
		}

		return IsHorizontallyInside(control.GetGlobalRect(), outerRect);
	}

	private static bool IsHorizontallyInside(Rect2 innerRect, Rect2 outerRect)
	{
		const float epsilon = 0.5f;
		return innerRect.Position.X >= outerRect.Position.X - epsilon
			&& innerRect.End.X <= outerRect.End.X + epsilon;
	}

	private static bool AreVisibleLabelsLegible(Node root)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is Label label
				&& label.IsVisibleInTree()
				&& label.Text.Replace("\n", string.Empty, StringComparison.Ordinal).Length > 1
				&& label.GetGlobalRect().Size.X < 16)
			{
				return false;
			}

			if (!AreVisibleLabelsLegible(child))
			{
				return false;
			}
		}

		return true;
	}

	private static void ClearChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			child.QueueFree();
		}
	}
}
