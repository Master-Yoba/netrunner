using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Godot;
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
	private static readonly Color Background = new(0.025f, 0.04f, 0.055f);
	private static readonly Color PanelBackground = new(0.055f, 0.08f, 0.105f);
	private static readonly Color PanelBorder = new(0.17f, 0.28f, 0.34f);
	private static readonly Color Accent = new(0.36f, 0.9f, 0.74f);
	private static readonly Color Muted = new(0.65f, 0.74f, 0.8f);
	private static readonly Color Warning = new(0.98f, 0.74f, 0.28f);

	private readonly Queue<IReadOnlyList<EngineEvent>> _animationBatches = new();
	private readonly Queue<string> _eventFeedLines = new();
	private readonly ConcurrentQueue<MatchSnapshot> _pendingSnapshots = new();
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
	private Label _focus = null!;
	private Label _decision = null!;
	private VBoxContainer _focusPresentation = null!;
	private Label _eventTitle = null!;
	private RichTextLabel _eventFeed = null!;
	private Label _details = null!;
	private Label _corpSummary = null!;
	private Label _runnerSummary = null!;
	private VBoxContainer _servers = null!;
	private VBoxContainer _runnerRig = null!;
	private VBoxContainer _runnerHand = null!;
	private VBoxContainer _scores = null!;
	private VBoxContainer _actions = null!;
	private PanelContainer _actionPanel = null!;
	private Button _zoomButton = null!;
	private PanelContainer _zoomOverlay = null!;
	private Label _zoomTitle = null!;
	private Label _zoomText = null!;
	private HSlider _speed = null!;
	private CheckButton _reducedMotion = null!;
	private Button _skipAnimations = null!;
	private bool _smokeTest;
	private bool _responsivenessSmoke;
	private bool _animationRunning;
	private bool _skipAnimation;
	private int _framesWhileAnimating;
	private long _renderedSequence;
	private long _processFrameCount;
	private int _submissionInFlight;
	private int _smokeStarted;
	private bool _smokeEncounterFocusObserved;
	private bool _smokeAccessFocusObserved;
	private bool _smokeRunEndedObserved;
	private bool _smokeOutcomePresentationObserved;
	private bool _smokeTargetCueObserved;
	private bool _smokeTargetControlsObserved;

	public override void _Ready()
	{
		_smokeTest = OS.GetCmdlineUserArgs().Contains("--smoke-test", StringComparer.Ordinal)
			|| OS.GetCmdlineUserArgs().Contains("--m3-responsiveness-smoke", StringComparer.Ordinal);
		_responsivenessSmoke = OS.GetCmdlineUserArgs().Contains("--m3-responsiveness-smoke", StringComparer.Ordinal);
		BuildShell();
		if (_smokeTest)
		{
			BeginMatch();
		}
	}

	private void RenderFocusPresentation(MatchSnapshot snapshot)
	{
		ClearChildren(_focusPresentation);
		MatchViewModel view = snapshot.View;
		if (snapshot.RunOutcome is { } completed && view.Run is null)
		{
			_focusPresentation.AddChild(MakeLabel("REPRESENTATIVE SLICE COMPLETE", 17, Accent));
			_focusPresentation.AddChild(MakeLabel(
				$"{completed.Summary}\nThe live run has been cleaned up by Core. This screen is a presentation-safe outcome summary.",
				13,
				Colors.White));
			_smokeOutcomePresentationObserved |= _smokeTest && _focusPresentation.GetChildCount() > 0;
			return;
		}

		if (view.Run is not { } run)
		{
			_focusPresentation.AddChild(MakeLabel(
				"RUN FOCUS · select a legal action to continue",
				13,
				Muted));
			return;
		}

		if (run.Encounter is { } encounter)
		{
			_smokeEncounterFocusObserved |= _smokeTest;
			RenderEncounterFocus(snapshot, run, encounter);
			return;
		}

		if (run.Access.State != RunAccessState.NotStarted)
		{
			_smokeAccessFocusObserved |= _smokeTest;
			RenderAccessFocus(snapshot, run);
			return;
		}

		if (run.Priority is { } priority)
		{
			RenderPaidWindowFocus(priority);
			return;
		}

		_focusPresentation.AddChild(MakeLabel(
			$"RUN STATUS · {run.Phase} · {run.Step} · credits {run.RunCredits}",
			15,
			Warning));
		_focusPresentation.AddChild(MakeLabel(
			"The current timing step is owned by Core. Legal actions below are the only actions that can mutate the run.",
			12,
			Muted));
	}

	private void RenderPaidWindowFocus(RunPriorityViewModel priority)
	{
		_focusPresentation.AddChild(MakeLabel(
			$"PAID WINDOW · {priority.Window} · PRIORITY {priority.Holder.ToString().ToUpperInvariant()}",
			17,
			Accent));
		_focusPresentation.AddChild(MakeLabel(
			$"Consecutive passes: {priority.ConsecutivePasses} · "
			+ $"Corp may rez approached ICE: {(priority.CorpMayRezApproachedIce ? "YES" : "NO")} · "
			+ $"non-ICE rez: {(priority.CorpMayRezNonIce ? "YES" : "NO")}",
			12,
			Colors.White));
		_focusPresentation.AddChild(MakeLabel(
			"PASS / CONTINUE remain explicit below. The priority holder is shown as text, not colour alone.",
			12,
			Muted));
	}

	private void RenderEncounterFocus(
		MatchSnapshot snapshot,
		RunFocusViewModel run,
		EncounterViewModel encounter)
	{
		string iceTitle = encounter.Ice?.Title ?? run.CurrentIce?.Title ?? "Unknown ICE";
		_focusPresentation.AddChild(MakeLabel(
			$"ENCOUNTER FOCUS · {iceTitle} · {encounter.Status.ToString().ToUpperInvariant()}",
			17,
			Warning));

		HBoxContainer encounterRow = new();
		encounterRow.AddThemeConstantOverride("separation", 10);
		if (encounter.Ice is { } ice)
		{
			TextCardControl iceCard = MakeCard(ice);
			iceCard.CustomMinimumSize = new Vector2(172, 122);
			encounterRow.AddChild(iceCard);
		}

		VBoxContainer state = new();
		state.AddThemeConstantOverride("separation", 3);
		state.AddChild(MakeLabel(
			$"ICE STRENGTH · {(encounter.EffectiveStrength is { } strength ? strength.ToString(CultureInfo.InvariantCulture) : "UNKNOWN")}",
			14,
			Colors.White));
		state.AddChild(MakeLabel(
			$"SUBROUTINES · {encounter.Subroutines.Count} total · {encounter.RemainingSubroutines} active",
			12,
			Accent));
		foreach (SubroutineViewModel subroutine in encounter.Subroutines)
		{
			string status = subroutine.Broken
				? "[BROKEN]"
				: subroutine.Resolved ? "[RESOLVED]" : "[ACTIVE]";
			state.AddChild(MakeLabel(
				$"{status} Subroutine {subroutine.Ordinal}",
				11,
				subroutine.Broken || subroutine.Resolved ? Muted : Colors.White));
		}

		encounterRow.AddChild(state);
		_focusPresentation.AddChild(encounterRow);
		string breakerCue = string.Join(
			" · ",
			snapshot.View.LegalActions
				.Where(action => action.Kind is ActionKind.PumpBreaker or ActionKind.BreakSubroutines)
				.Select(action => $"{action.Kind}: {action.Description}"));
		_focusPresentation.AddChild(MakeLabel(
			string.IsNullOrWhiteSpace(breakerCue)
				? "BREAKER OPTIONS · no breaker action is currently issued"
				: $"BREAKER OPTIONS · {breakerCue}",
			12,
			Colors.White));
		if (run.Priority is { } priority)
		{
			AddPriorityCue(priority);
		}
	}

	private void RenderAccessFocus(MatchSnapshot snapshot, RunFocusViewModel run)
	{
		AccessViewModel access = run.Access;
		_focusPresentation.AddChild(MakeLabel(
			$"ACCESS FOCUS · {run.AttackedServerId} · {access.State.ToString().ToUpperInvariant()}",
			17,
			Accent));
		_focusPresentation.AddChild(MakeLabel(
			$"Candidates: {access.Candidates.Count} · chosen: {access.Chosen.Count} · accessed: {access.Accessed.Count}"
				+ (access.RandomAccessLimit > 0 ? $" · random access limit {access.RandomAccessLimit}" : string.Empty),
			12,
			Colors.White));

		if (access.Candidates.Count > 0)
		{
			_focusPresentation.AddChild(MakeLabel("CANDIDATE TARGETS · click a card or choose a legal action below", 11, Warning));
			HBoxContainer candidates = new();
			candidates.AddThemeConstantOverride("separation", 7);
			foreach (CardViewModel? candidate in access.Candidates)
			{
				TextCardControl card = MakeCard(candidate);
				card.CustomMinimumSize = new Vector2(140, 100);
				candidates.AddChild(card);
			}
			_focusPresentation.AddChild(candidates);
		}
		else if (access.HiddenCandidateSource is { } source)
		{
			_focusPresentation.AddChild(MakeLabel(
				$"HIDDEN CANDIDATE · Core will draw from {source} after the Runner confirms the legal choice.",
				12,
				Warning));
		}

		if (access.Accessed.Count > 0)
		{
			_focusPresentation.AddChild(MakeLabel("ACCESSED CARD · costs and choices are shown below", 11, Warning));
			HBoxContainer accessed = new();
			foreach (CardViewModel? cardModel in access.Accessed)
			{
				TextCardControl card = MakeCard(cardModel);
				card.CustomMinimumSize = new Vector2(140, 100);
				accessed.AddChild(card);
			}
			_focusPresentation.AddChild(accessed);
		}

		IEnumerable<LegalActionViewModel> accessActions = snapshot.View.LegalActions.Where(IsAccessAction);
		string actionCue = string.Join(
			" · ",
			accessActions.Select(action => $"{action.Kind.ToString().ToUpperInvariant()}: {action.Description}"));
		_focusPresentation.AddChild(MakeLabel(
			string.IsNullOrWhiteSpace(actionCue)
				? "ACCESS ACTIONS · select a candidate to continue"
				: $"ACCESS ACTIONS · {actionCue}",
			12,
			Colors.White));
	}

	private void AddPriorityCue(RunPriorityViewModel priority)
	{
		_focusPresentation.AddChild(MakeLabel(
			$"PAID WINDOW · {priority.Window} · priority {priority.Holder} · passes {priority.ConsecutivePasses} · PASS / CONTINUE below",
			11,
			Muted));
	}

	public override void _Process(double delta)
	{
		_processFrameCount++;
		PollCoordinator();
		if (_animationRunning)
		{
			_framesWhileAnimating++;
		}
	}

	public override void _ExitTree()
	{
		if (_coordinator is null)
		{
			return;
		}

		_coordinator.SnapshotPublished -= QueueSnapshot;
		_coordinator.Dispose();
	}

	private void BuildShell()
	{
		ColorRect backdrop = new()
		{
			Color = Background,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		SetFullRect(backdrop);
		AddChild(backdrop);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 28);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_right", 28);
		margin.AddThemeConstantOverride("margin_bottom", 22);
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
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_zoomOverlay.Visible
			|| @event is not InputEventKey key
			|| !key.Pressed
			|| key.Keycode != Key.Escape)
		{
			return;
		}

		HideCardZoom();
		GetViewport().SetInputAsHandled();
	}

	private void BuildHeader()
	{
		var header = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(0, 62),
		};
		header.AddThemeConstantOverride("separation", 18);
		_layout.AddChild(header);

		var title = new Label
		{
			Text = "NETRUNNER SOLO",
			VerticalAlignment = VerticalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 30);
		title.AddThemeColorOverride("font_color", Accent);
		header.AddChild(title);

		_status = MakeLabel("M3 playable shell · waiting for setup", 15, Muted);
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

		_startPanel.AddChild(MakeLabel("M3 MICRO-DECKS · REPRESENTATIVE RUN", 18, Accent));
		_startPanel.AddChild(MakeLabel(
			"Human Runner · asynchronous Corp policy · text cards · Core-owned timing\n"
			+ "The checked-in Overclock run opens on the remote. Select legal choices below; animations never advance rules.",
			14,
			Muted));
		_startButton = new Button
		{
			Text = "Start representative run",
			CustomMinimumSize = new Vector2(240, 42),
		};
		_startButton.AddThemeFontSizeOverride("font_size", 16);
		_startButton.Pressed += BeginMatch;
		_startPanel.AddChild(_startButton);
	}

	private void BuildBoard()
	{
		_board = new VBoxContainer
		{
			Visible = false,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_board.AddThemeConstantOverride("separation", 10);
		_layout.AddChild(_board);

		HBoxContainer toolbar = new();
		toolbar.AddThemeConstantOverride("separation", 12);
		_board.AddChild(toolbar);
		_focus = MakeLabel("RUN FOCUS · awaiting engine publication", 16, Warning);
		_focus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		toolbar.AddChild(_focus);
		_reducedMotion = new CheckButton { Text = "Reduced motion" };
		_reducedMotion.AddThemeColorOverride("font_color", Muted);
		toolbar.AddChild(_reducedMotion);
		toolbar.AddChild(MakeLabel("Speed", 12, Muted));
		_speed = new HSlider
		{
			MinValue = 0.25,
			MaxValue = 2.0,
			Step = 0.25,
			Value = 1.0,
			CustomMinimumSize = new Vector2(110, 24),
		};
		toolbar.AddChild(_speed);
		_skipAnimations = new Button { Text = "Skip queued animation" };
		_skipAnimations.Pressed += SkipAnimations;
		toolbar.AddChild(_skipAnimations);

		// A plain Control deliberately owns the scroll viewport. Containers
		// propagate their children's minimum size to their parent; that would
		// allow the dynamically rendered server/card columns to push the action
		// row below the viewport as the match progresses.
		Control bodyViewport = new()
		{
			ClipContents = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_board.AddChild(bodyViewport);

		ScrollContainer bodyScroll = new()
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
		};
		SetFullRect(bodyScroll);
		bodyViewport.AddChild(bodyScroll);

		HBoxContainer body = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		body.AddThemeConstantOverride("separation", 10);
		bodyScroll.AddChild(body);

		PanelContainer corpPanel = MakePanel();
		corpPanel.CustomMinimumSize = new Vector2(205, 0);
		VBoxContainer corpContent = new();
		corpContent.AddThemeConstantOverride("separation", 7);
		corpPanel.AddChild(corpContent);
		corpContent.AddChild(MakeLabel("CORP · REMOTE NETWORK", 14, new Color(0.93f, 0.48f, 0.48f)));
		_corpSummary = MakeLabel("Credits · Clicks · Score", 13, Muted);
		corpContent.AddChild(_corpSummary);
		corpContent.AddChild(MakeLabel("HQ / R&D / Archives", 12, Muted));
		body.AddChild(corpPanel);

		PanelContainer centerPanel = MakePanel();
		centerPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		centerPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		VBoxContainer center = new();
		center.AddThemeConstantOverride("separation", 7);
		centerPanel.AddChild(center);
		_decision = MakeLabel("No decision", 15, Colors.White);
		_decision.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		center.AddChild(_decision);
		PanelContainer focusPanel = MakePanel();
		focusPanel.CustomMinimumSize = new Vector2(0, 112);
		_focusPresentation = new VBoxContainer();
		_focusPresentation.AddThemeConstantOverride("separation", 5);
		focusPanel.AddChild(_focusPresentation);
		_focusPresentation.AddChild(MakeLabel(
			"RUN FOCUS · select a legal action to continue",
			13,
			Muted));
		center.AddChild(focusPanel);
		_servers = new VBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_servers.AddThemeConstantOverride("separation", 6);
		center.AddChild(_servers);
		_eventTitle = MakeLabel("Immutable Engine Event", 13, Accent);
		center.AddChild(_eventTitle);
		_eventFeed = new RichTextLabel
		{
			Text = "Event queue is empty.",
			FitContent = false,
			ScrollActive = true,
			ScrollFollowing = true,
			CustomMinimumSize = new Vector2(0, 76),
		};
		_eventFeed.AddThemeFontSizeOverride("normal_font_size", 11);
		_eventFeed.AddThemeColorOverride("default_color", Muted);
		center.AddChild(_eventFeed);
		body.AddChild(centerPanel);

		PanelContainer runnerPanel = MakePanel();
		runnerPanel.CustomMinimumSize = new Vector2(260, 0);
		VBoxContainer runnerContent = new();
		runnerContent.AddThemeConstantOverride("separation", 6);
		runnerPanel.AddChild(runnerContent);
		runnerContent.AddChild(MakeLabel("RUNNER · YOUR RIG", 14, Accent));
		_runnerSummary = MakeLabel("Credits · Clicks · Score", 13, Muted);
		runnerContent.AddChild(_runnerSummary);
		runnerContent.AddChild(MakeLabel("Rig", 12, Warning));
		_runnerRig = new VBoxContainer();
		_runnerRig.AddThemeConstantOverride("separation", 4);
		runnerContent.AddChild(_runnerRig);
		runnerContent.AddChild(MakeLabel("Grip (cards you own)", 12, Warning));
		_runnerHand = new VBoxContainer();
		_runnerHand.AddThemeConstantOverride("separation", 4);
		runnerContent.AddChild(_runnerHand);
		runnerContent.AddChild(MakeLabel("Scoring", 12, Warning));
		_scores = new VBoxContainer();
		_scores.AddThemeConstantOverride("separation", 4);
		runnerContent.AddChild(_scores);
		body.AddChild(runnerPanel);

		HBoxContainer lower = new();
		lower.AddThemeConstantOverride("separation", 10);
		lower.CustomMinimumSize = new Vector2(0, 150);
		_board.AddChild(lower);

		_actionPanel = MakePanel();
		PanelContainer actionPanel = _actionPanel;
		actionPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		VBoxContainer actionContent = new();
		actionContent.AddThemeConstantOverride("separation", 5);
		actionPanel.AddChild(actionContent);
		actionContent.AddChild(MakeLabel("LEGAL ACTIONS · CLICK TO COMMIT", 13, Accent));

		// Keep the action row's height independent of the number or length of
		// legal actions. The inner ScrollContainer can grow its child, but this
		// wrapper reports only its fixed viewport minimum to actionContent.
		Control actionViewport = new()
		{
			ClipContents = true,
			CustomMinimumSize = new Vector2(0, 100),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		ScrollContainer actionScroll = new()
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
		};
		SetFullRect(actionScroll);
		_actions = new VBoxContainer();
		_actions.AddThemeConstantOverride("separation", 5);
		_actions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionScroll.AddChild(_actions);
		actionViewport.AddChild(actionScroll);
		actionContent.AddChild(actionViewport);
		lower.AddChild(actionPanel);

		PanelContainer detailsPanel = MakePanel();
		detailsPanel.CustomMinimumSize = new Vector2(420, 0);
		VBoxContainer detailsContent = new();
		detailsContent.AddThemeConstantOverride("separation", 5);
		detailsPanel.AddChild(detailsContent);
		detailsContent.AddChild(MakeLabel("INSPECTED CARD / TIMING", 13, Warning));
		Control detailsViewport = new()
		{
			ClipContents = true,
			CustomMinimumSize = new Vector2(0, 100),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		ScrollContainer detailsScroll = new()
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
		};
		SetFullRect(detailsScroll);
		_details = MakeLabel("Hover or click a text card for details.\nImportant cues use labels and borders as well as colour.", 12, Muted);
		_details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		detailsScroll.AddChild(_details);
		detailsViewport.AddChild(detailsScroll);
		detailsContent.AddChild(detailsViewport);
		_zoomButton = new Button
		{
			Text = "Zoom inspected card",
			Visible = false,
			CustomMinimumSize = new Vector2(0, 32),
		};
		_zoomButton.Pressed += ShowInspectedCardZoom;
		detailsContent.AddChild(_zoomButton);
		lower.AddChild(detailsPanel);
	}

	private void BuildCardZoomOverlay()
	{
		_zoomOverlay = new PanelContainer
		{
			Visible = false,
			MouseFilter = MouseFilterEnum.Stop,
		};
		_zoomOverlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.015f, 0.025f, 0.04f, 0.97f),
			BorderColor = Accent,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			ContentMarginLeft = 36,
			ContentMarginTop = 30,
			ContentMarginRight = 36,
			ContentMarginBottom = 30,
		});
		SetFullRect(_zoomOverlay);
		AddChild(_zoomOverlay);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 72);
		margin.AddThemeConstantOverride("margin_top", 56);
		margin.AddThemeConstantOverride("margin_right", 72);
		margin.AddThemeConstantOverride("margin_bottom", 56);
		_zoomOverlay.AddChild(margin);
		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 14);
		margin.AddChild(content);
		_zoomTitle = MakeLabel("CARD ZOOM", 24, Accent);
		_zoomTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_zoomTitle);
		_zoomText = MakeLabel(string.Empty, 18, Colors.White);
		_zoomText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_zoomText.SizeFlagsVertical = SizeFlags.ExpandFill;
		content.AddChild(_zoomText);
		Label cue = MakeLabel(
			"Full text view · hidden information remains concealed · press Escape or Close",
			12,
			Muted);
		content.AddChild(cue);
		Button close = new()
		{
			Text = "Close card zoom",
			CustomMinimumSize = new Vector2(180, 42),
		};
		close.Pressed += HideCardZoom;
		content.AddChild(close);
	}

	private void BuildFooter()
	{
		Label footer = MakeLabel(
			"M3 vertical slice · observation boundary · event animation queue · 1280×720 → 2560×1440 stretch",
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

		MatchCoordinatorOptions options = _responsivenessSmoke
			? new MatchCoordinatorOptions
			{
				// This mode intentionally uses the real ten-second production delay;
				// its frame/input assertions run while the worker is actually waiting.
				AgentDelay = TimeSpan.FromSeconds(10),
			}
			: _smokeTest
			? new MatchCoordinatorOptions
			{
				// Keep the production-facing ten-second setting visible in
				// the smoke path while injecting a short deterministic delay.
				AgentDelay = TimeSpan.FromSeconds(10),
				Delay = static (_, cancellationToken) =>
					new ValueTask(Task.Delay(TimeSpan.FromMilliseconds(45), cancellationToken)),
			}
			: new MatchCoordinatorOptions();
		_coordinator = M2MatchFactory.CreateRepresentative(options);
		_runnerPlaybackPolicy = _smokeTest
			? M2MatchFactory.CreateRepresentativeRunnerPolicy()
			: null;
		_coordinator.SnapshotPublished += QueueSnapshot;
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
		try
		{
			await _coordinator!.StartAsync();
		}
		catch (Exception exception)
		{
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

				LegalActionViewModel? target = snapshot.View.LegalActions.FirstOrDefault(IsTargetedAction);
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
				return true;
			}

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		return _renderedSequence >= sequence;
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
		bool layoutAt1280x720 = false;
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
			layoutAt1280x720 = await CheckViewportLayoutAsync(new Vector2I(1280, 720));
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
				&& layoutAt1280x720
				&& layoutAt2560x1440;
			GD.Print(
				$"M3_LATENCY_SMOKE {(passed ? "PASS" : "FAIL")} "
				+ $"setup={setup} submitted={submitted} thinking={thinking} "
				+ $"held_ms={holdDuration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} "
				+ $"thinking_frames={thinkingFrames} avg_fps={averageFps.ToString("F1", CultureInfo.InvariantCulture)} "
				+ $"inspection_one_frame={inspectionWithinOneFrame} control_one_frame={controlWithinOneFrame} "
				+ $"hover_selection_one_frame={hoverSelectionWithinOneFrame} "
				+ $"zoom_in_viewport={zoomVisible} layout_1280x720={layoutAt1280x720} "
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
				+ $"zoom_in_viewport={zoomVisible} layout_1280x720={layoutAt1280x720} "
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
			&& IsFullyInsideViewport(_focusPresentation)
			&& IsFullyInsideViewport(_zoomButton)
			&& IsFullyInsideViewport(_zoomOverlay);
		DisplayServer.WindowSetSize(previousSize);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		return inside;
	}

	private void PollCoordinator()
	{
		while (_pendingSnapshots.TryDequeue(out MatchSnapshot? snapshot))
		{
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

	private void QueueSnapshot(MatchSnapshot snapshot) => _pendingSnapshots.Enqueue(snapshot);

	private void RenderSnapshot(MatchSnapshot snapshot)
	{
		MatchSnapshot? previous = _currentSnapshot;
		bool decisionChanged = previous is null
			|| !SameDecision(previous.View.Decision, snapshot.View.Decision);
		_currentSnapshot = snapshot;
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
		_board.Visible = true;
		MatchViewModel view = snapshot.View;
		_status.Text = snapshot.RunOutcome is { } completedRun && snapshot.View.Run is null
			? $"Representative slice complete · {completedRun.Summary}"
			: snapshot.IsOpponentThinking
			? "Corp policy is thinking asynchronously · inspect cards and menus remain responsive"
			: view.Outcome.IsTerminal
				? $"Match complete · {view.Outcome.Winner} · {view.Outcome.Reason}"
			: "Core state published · human Runner input is available";
		_phase.Text = view.Decision is { } decision
			? $"{decision.OwnerSide.ToString().ToUpperInvariant()} · {decision.Kind.ToString().ToUpperInvariant()}"
			: snapshot.RunOutcome is not null && view.Run is null ? "SLICE COMPLETE"
			: view.Outcome.IsTerminal ? "GAME OVER" : "RESOLVING";
		_focus.Text = view.Run is { } run
			? $"{run.FocusLabel} · attacked {run.AttackedServerId} · {run.Step}"
			: snapshot.RunOutcome is { } ended
				? $"REPRESENTATIVE SLICE · COMPLETE · {ended.Reason}"
			: "RUN FOCUS · no active run";
		_decision.Text = snapshot.IsOpponentThinking
			? "CORP THINKING · asynchronous agent delay\nThe render loop is free to process input and card inspection."
			: view.Decision is { } prompt
				? $"{prompt.OwnerSide} decision · {prompt.Prompt}\nRevision {prompt.Revision} · {view.LegalActions.Count} legal action(s)"
			: snapshot.RunOutcome is { } finished && view.Run is null
				? $"REPRESENTATIVE RUN COMPLETE\n{finished.Summary}\nNo further Core action is pending."
			: view.Outcome.IsTerminal ? "Outcome reached." : "No pending decision.";

		_corpSummary.Text = FormatPlayer(view.Corp, view.CorpHand.Count, view.CorpDeck.Count);
		_runnerSummary.Text = FormatPlayer(view.Runner, view.RunnerHand.Count, view.RunnerDeck.Count);
		_cardControls.Clear();
		_cardModels.Clear();
		RenderFocusPresentation(snapshot);
		RenderServers(view.Servers);
		RenderRunnerCards(view);
		RenderActions(snapshot);
		if (snapshot.Feedback is { } feedback)
		{
			_details.Text = $"{feedback.Kind.ToString().ToUpperInvariant()}\n{feedback.Message}";
			_details.AddThemeColorOverride("font_color", feedback.IsError ? Warning : Muted);
		}
	}

	private void RenderServers(IReadOnlyList<ServerViewModel> servers)
	{
		ClearChildren(_servers);
		foreach (ServerViewModel server in servers)
		{
			PanelContainer panel = MakePanel();
			VBoxContainer content = new();
			content.AddThemeConstantOverride("separation", 4);
			panel.AddChild(content);
			content.AddChild(MakeLabel(
				$"{server.Kind.ToString().ToUpperInvariant()}  ·  {server.Id}",
				12,
				server.Kind == ServerKind.Remote ? Warning : Muted));
			HBoxContainer ice = new();
			ice.AddChild(MakeLabel("ICE (outer → inner)", 10, Muted));
			foreach (CardViewModel? card in server.OrderedIce)
			{
				TextCardControl control = MakeCard(card);
				ice.AddChild(control);
			}

			content.AddChild(ice);
			HBoxContainer root = new();
			root.AddChild(MakeLabel("ROOT", 10, Muted));
			foreach (CardViewModel? card in server.OrderedRoot)
			{
				root.AddChild(MakeCard(card));
			}

			content.AddChild(root);
			_servers.AddChild(panel);
		}
	}

	private void RenderRunnerCards(MatchViewModel view)
	{
		ClearChildren(_runnerRig);
		foreach (CardViewModel card in view.RunnerRig)
		{
			_runnerRig.AddChild(MakeCard(card));
		}

		ClearChildren(_runnerHand);
		_runnerHand.AddChild(MakeLabel(
			$"{view.RunnerHand.Count} card(s) · deck {view.RunnerDeck.Count}",
			11,
			Muted));
		foreach (CardViewModel card in view.RunnerHand.VisibleCards)
		{
			_runnerHand.AddChild(MakeCard(card));
		}

		ClearChildren(_scores);
		_scores.AddChild(MakeLabel(
			$"Runner score: {view.Runner.AgendaPoints} · Corp score: {view.Corp.AgendaPoints}",
			11,
			Warning));
		foreach (CardViewModel card in view.RunnerScore)
		{
			_scores.AddChild(MakeLabel($"Runner · {card.Title} ({card.AgendaPoints} AP)", 10, Muted));
		}

		foreach (CardViewModel card in view.CorpScore)
		{
			_scores.AddChild(MakeLabel($"Corp · {card.Title} ({card.AgendaPoints} AP)", 10, Muted));
		}
	}

	private void RenderActions(MatchSnapshot snapshot)
	{
		ClearChildren(_actions);
		UpdateTargetHighlights(snapshot.View.LegalActions);
		if (snapshot.IsOpponentThinking)
		{
			_actions.AddChild(MakeLabel("Opponent thinking…", 13, Warning));
			return;
		}

		DecisionViewModel? decision = snapshot.View.Decision;
		if (decision is null || decision.OwnerSide != PlayerSide.Runner)
		{
			_actions.AddChild(MakeLabel(
				snapshot.RunOutcome is not null && snapshot.View.Run is null
					? "REPRESENTATIVE RUN COMPLETE · no further actions"
					: "Waiting for the next issued Runner decision.",
				12,
				Muted));
			return;
		}

		if (_stagedAction is { } staged && _stagedDecision is { } stagedDecision)
		{
			_smokeTargetControlsObserved |= _smokeTest;
			PanelContainer selected = MakePanel();
			VBoxContainer selectedContent = new();
			selectedContent.AddThemeConstantOverride("separation", 3);
			selected.AddChild(selectedContent);
			selectedContent.AddChild(MakeLabel("TARGET SELECTED · confirm or cancel", 12, Warning));
			selectedContent.AddChild(MakeLabel(
				$"{staged.Kind.ToString().ToUpperInvariant()} · {staged.Description}\n"
				+ "No engine action has been submitted yet.",
				11,
				Colors.White));
			HBoxContainer selectedButtons = new();
			Button confirm = new()
			{
				Text = "Confirm target",
				CustomMinimumSize = new Vector2(150, 34),
			};
			confirm.Pressed += () => ConfirmStagedAction(stagedDecision, staged);
			selectedButtons.AddChild(confirm);
			Button cancel = new()
			{
				Text = "Cancel / Back",
				CustomMinimumSize = new Vector2(150, 34),
			};
			cancel.Pressed += CancelStagedAction;
			selectedButtons.AddChild(cancel);
			selectedContent.AddChild(selectedButtons);
			_actions.AddChild(selected);
		}

		foreach (LegalActionViewModel action in snapshot.View.LegalActions)
		{
			bool targeted = GetTargetCardId(action.Parameters) is not null
				|| action.Parameters is BreachCandidateParameters;
			Button button = new()
			{
				Text = targeted
					? $"SELECT TARGET · {action.Kind}: {action.Description}"
					: $"{action.Kind}: {action.Description}",
				Alignment = HorizontalAlignment.Left,
				Disabled = Volatile.Read(ref _submissionInFlight) != 0,
			};
			if (targeted)
			{
				button.Pressed += () => StageTargetAction(decision, action);
			}
			else
			{
				button.Pressed += () => SubmitAction(decision, action);
			}
			_actions.AddChild(button);
		}
	}

	private void StageTargetAction(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (!SameDecision(decision, _currentSnapshot?.View.Decision)
			|| _currentSnapshot is null
			|| _currentSnapshot.IsOpponentThinking)
		{
			return;
		}

		_stagedDecision = decision;
		_stagedAction = action;
		UpdateTargetHighlights(_currentSnapshot.View.LegalActions);
		RenderActions(_currentSnapshot);
	}

	private void ConfirmStagedAction(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (!SameDecision(decision, _currentSnapshot?.View.Decision))
		{
			CancelStagedAction();
			return;
		}

		SubmitAction(decision, action);
	}

	private void CancelStagedAction()
	{
		_stagedAction = null;
		_stagedDecision = null;
		if (_currentSnapshot is not null)
		{
			RenderActions(_currentSnapshot);
		}
	}

	private void UpdateTargetHighlights(IReadOnlyList<LegalActionViewModel> actions)
	{
		foreach (List<TextCardControl> controls in _cardControls.Values)
		{
			foreach (TextCardControl control in controls)
			{
				control.SetTargetCandidate(false);
			}
		}

		HashSet<string> candidateIds = actions
			.Select(action => GetTargetCardId(action.Parameters))
			.Where(value => value is not null)
			.Select(value => value!)
			.ToHashSet(StringComparer.Ordinal);
		foreach (string candidateId in candidateIds)
		{
			if (!_cardControls.TryGetValue(candidateId, out List<TextCardControl>? controls))
			{
				continue;
			}

			foreach (TextCardControl control in controls)
			{
				control.SetTargetCandidate(true);
				_smokeTargetCueObserved |= _smokeTest;
			}
		}

		if (_stagedAction is { } staged
			&& GetTargetCardId(staged.Parameters) is { } selectedId
			&& _cardControls.TryGetValue(selectedId, out List<TextCardControl>? selectedControls))
		{
			foreach (TextCardControl control in selectedControls)
			{
				control.SetSelected(true);
			}
		}
	}

	private void SubmitAction(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (_coordinator is null || Interlocked.Exchange(ref _submissionInFlight, 1) != 0)
		{
			return;
		}

		_ = SubmitActionAsync(new ActionSubmission(
			decision.Owner,
			new DecisionId(decision.Id),
			decision.Revision,
			action.Id));
	}

	private async Task SubmitActionAsync(ActionSubmission submission)
	{
		try
		{
			await _coordinator!.SubmitActionAsync(submission);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
		}
		finally
		{
			Interlocked.Exchange(ref _submissionInFlight, 0);
		}
	}

	private async Task AnimateEventsAsync()
	{
		_animationRunning = true;
		while (_animationBatches.Count > 0)
		{
			IReadOnlyList<EngineEvent> batch = _animationBatches.Dequeue();
			foreach (EngineEvent @event in batch)
			{
				_eventTitle.Text = $"{@event.Kind} · sequence {@event.Sequence}";
				AppendEventFeed(DescribeEvent(@event));
				double seconds = _reducedMotion.ButtonPressed || _skipAnimation
					? 0.01
					: 0.18 / Math.Max(0.25, _speed.Value);
				await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
			}

			_skipAnimation = false;
		}

		_animationRunning = false;
	}

	private void SkipAnimations()
	{
		_skipAnimation = true;
		_animationBatches.Clear();
	}

	private void AppendEventFeed(string line)
	{
		const int MaxVisibleEventLines = 8;
		_eventFeedLines.Enqueue(line);
		while (_eventFeedLines.Count > MaxVisibleEventLines)
		{
			_eventFeedLines.Dequeue();
		}

		_eventFeed.Text = string.Join('\n', _eventFeedLines.Reverse());
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

		LegalActionViewModel? action = _currentSnapshot.View.LegalActions
			.FirstOrDefault(candidate => GetTargetCardId(candidate.Parameters) == cardId);
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
		_details.Text = $"{card.Title}\n{card.Type} · {card.DefinitionId}\n"
			+ $"{card.RulesText}\n\nClick feedback is local and immediate; engine actions remain ID-only.\n"
			+ "Use Zoom inspected card for an enlarged, complete text view.";
		_details.AddThemeColorOverride("font_color", Colors.White);
		_zoomButton.Visible = true;
	}

	private void ShowInspectedCardZoom()
	{
		if (_inspectedCard is not { } card)
		{
			return;
		}

		_zoomTitle.Text = $"CARD ZOOM · {card.Title}";
		_zoomText.Text = FormatCardZoom(card);
		_zoomOverlay.Visible = true;
		_zoomOverlay.MoveToFront();
	}

	private void HideCardZoom()
	{
		_zoomOverlay.Visible = false;
	}

	private static string FormatCardZoom(CardViewModel card)
	{
		List<string> lines =
		[
			$"{card.Type}{(card.Subtypes.Count == 0 ? string.Empty : " · " + string.Join(" · ", card.Subtypes))}",
		];
		if (card.Cost is { } cost)
		{
			lines.Add($"Cost · {cost}");
		}

		if (card.Strength is { } strength)
		{
			lines.Add($"Strength · {strength}");
		}

		if (card.AgendaPoints is { } agendaPoints)
		{
			lines.Add($"Agenda points · {agendaPoints}");
		}

		if (card.TrashCost is { } trashCost)
		{
			lines.Add($"Trash cost · {trashCost}");
		}

		lines.Add($"Status · {(card.Rezzed ? "REZZED" : card.IsKnown ? "KNOWN" : "FACE-DOWN")}");
		if (card.Counters.Count > 0)
		{
			lines.Add(string.Join(" · ", card.Counters));
		}

		lines.Add(string.Empty);
		lines.Add(card.RulesText);
		return string.Join('\n', lines);
	}

	private static bool SameDecision(DecisionViewModel? left, DecisionViewModel? right) =>
		left is null && right is null
		|| left is not null
			&& right is not null
			&& left.Id == right.Id
			&& left.Revision == right.Revision
			&& left.Owner == right.Owner;

	private static string? GetTargetCardId(ActionParameters parameters) => parameters switch
	{
		CardChoiceParameters value => value.CardId.Value,
		RunIceParameters value => value.IceId.Value,
		RunBreakerParameters value => value.BreakerId.Value,
		RunBreakParameters value => value.BreakerId.Value,
		RunCardPaymentParameters value => value.CardId.Value,
		BreachCandidateParameters { CardId: { } cardId } => cardId.Value,
		_ => null,
	};

	private static bool IsAccessAction(LegalActionViewModel action) => action.Kind is
		ActionKind.StealAgenda
		or ActionKind.TrashAccessedCard
		or ActionKind.NoAccessAction
		or ActionKind.ChooseBreachCandidate;

	private static bool IsTargetedAction(LegalActionViewModel action) =>
		GetTargetCardId(action.Parameters) is not null
		|| action.Parameters is BreachCandidateParameters;

	private static string FormatPlayer(PlayerViewModel player, int hand, int deck) =>
		$"Credits {player.Credits} · Clicks {player.Clicks} · Score {player.AgendaPoints}\n"
		+ $"Hand {hand} · Deck {deck}";

	private static string DescribeEvent(EngineEvent @event) => @event.Payload switch
	{
		RunInitiatedEventPayload payload => $"Run initiated against {payload.ServerId.Value}.",
		RunEventPlayedEventPayload payload => $"Played {payload.DefinitionId.Value}; hosted {payload.HostedCredits} run credits.",
		IceApproachedEventPayload payload => $"Approached ICE {payload.IceId.Value}.",
		IceRezzedEventPayload payload => $"Corp rezzed ICE {payload.IceId.Value}.",
		EncounterInitializedEventPayload payload => $"Encountered {payload.IceId.Value} ({payload.SubroutineCount} subroutine(s)).",
		BreakerPumpedEventPayload payload => $"Pumped breaker {payload.BreakerId.Value}.",
		SubroutinesBrokenEventPayload payload => $"Broke {payload.BrokenCount} subroutine(s) with {payload.BreakerId.Value}.",
		RunSuccessfulEventPayload => "Run successful · breach boundary.",
		BreachStartedEventPayload payload => $"Breach started · {payload.CandidateCount} access candidate(s).",
		BreachCandidateChosenEventPayload payload => $"Access candidate chosen: {payload.CardId.Value}.",
		CardAccessedEventPayload payload => $"Accessed {payload.CardId.Value}.",
		AgendaStolenEventPayload payload => $"Agenda stolen · +{payload.AgendaPoints} points.",
		AccessedCardTrashedEventPayload payload => $"Trashed accessed card for {payload.TrashCost} credits.",
		RunEndedEventPayload payload => $"Run ended · {payload.Reason}.",
		_ => @event.Kind,
	};

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

	private static void ClearChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			child.QueueFree();
		}
	}
}
