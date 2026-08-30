using System.Collections.Concurrent;
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
	private MatchCoordinator? _coordinator;
	private VBoxContainer _layout = null!;
	private VBoxContainer _startPanel = null!;
	private VBoxContainer _board = null!;
	private Button _startButton = null!;
	private Label _status = null!;
	private Label _phase = null!;
	private Label _focus = null!;
	private Label _decision = null!;
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
	private HSlider _speed = null!;
	private CheckButton _reducedMotion = null!;
	private Button _skipAnimations = null!;
	private bool _smokeTest;
	private bool _animationRunning;
	private bool _skipAnimation;
	private int _framesWhileAnimating;
	private long _renderedSequence;
	private int _submissionInFlight;
	private int _smokeStarted;

	public override void _Ready()
	{
		_smokeTest = OS.GetCmdlineUserArgs().Contains("--smoke-test", StringComparer.Ordinal);
		BuildShell();
		if (_smokeTest)
		{
			BeginMatch();
		}
	}

	public override void _Process(double delta)
	{
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
		lower.AddChild(detailsPanel);
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

		MatchCoordinatorOptions options = _smokeTest
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
		_coordinator.SnapshotPublished += QueueSnapshot;
		_startButton.Disabled = true;
		_startButton.Text = "Starting Core match…";
		_status.Text = "Loading checked-in M2 scenario through MatchCoordinator…";
		_ = AwaitStartAsync();
		if (_smokeTest && Interlocked.Exchange(ref _smokeStarted, 1) == 0)
		{
			_ = RunSmokeAsync();
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
		try
		{
			MatchSnapshot started = await _coordinator!.StartAsync();
			setup = started.Events.Any(value => value.Kind == EngineEventKinds.RunInitiated)
				&& started.View.Run is not null;
			humanDecision = started.AwaitingHumanDecision
				&& started.View.LegalActions.Count > 0;
			LegalAction? pass = started.Observation.LegalActions
				.FirstOrDefault(value => value.Kind == ActionKind.PassPriority);
			if (pass is not null && started.Observation.Decision is { } decision)
			{
				MatchSubmissionResult result = await _coordinator.SubmitActionAsync(
					new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id));
				submitted = result.Accepted;
				if (submitted)
				{
					// SnapshotPublished is queued from the coordinator worker. Wait
					// for the corresponding render before checking geometry, then
					// allow a few frames for a concurrent thinking publication to be
					// drained. This is a deterministic containment check rather than
					// a pixel-perfect screenshot assertion.
					long submittedSequence = result.Snapshot.Sequence;
					for (int frame = 0; frame < 30; frame++)
					{
						await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
						if (_renderedSequence >= submittedSequence
							&& IsFullyInsideViewport(_actionPanel))
						{
							actionPanelInViewport = true;
							break;
						}
					}
				}
			}

			// All Godot-side observations happen after a process-frame await,
			// so this check proves that delayed agent work did not stop frame
			// processing or inspection.
			for (int frame = 0; frame < 120; frame++)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (_coordinator.CurrentSnapshot?.IsOpponentThinking == true)
				{
					delayedThinking = true;
					break;
				}
			}

			bool responsive = _framesWhileAnimating > 0;
			bool passed = setup
				&& humanDecision
				&& submitted
				&& actionPanelInViewport
				&& delayedThinking
				&& responsive;
			GD.Print(
				$"M3_SMOKE {(passed ? "PASS" : "FAIL")} "
				+ $"setup={setup} human_decision={humanDecision} submitted={submitted} "
				+ $"action_panel_in_viewport={actionPanelInViewport} "
				+ $"delayed_agent={delayedThinking} responsive_frames={_framesWhileAnimating}");
			await _coordinator.DisposeAsync();
			GetTree().Quit(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print(
				$"M3_SMOKE FAIL setup={setup} human_decision={humanDecision} "
				+ $"submitted={submitted} action_panel_in_viewport={actionPanelInViewport} "
				+ $"error={exception.Message}");
			if (_coordinator is not null)
			{
				await _coordinator.DisposeAsync();
			}

			GetTree().Quit(1);
		}
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
		if (_startPanel.GetParent() is CanvasItem startPanel)
		{
			startPanel.Visible = false;
		}
		_board.Visible = true;
		MatchViewModel view = snapshot.View;
		_status.Text = snapshot.IsOpponentThinking
			? "Corp policy is thinking asynchronously · inspect cards and menus remain responsive"
			: view.Outcome.IsTerminal
				? $"Match complete · {view.Outcome.Winner} · {view.Outcome.Reason}"
				: "Core state published · human Runner input is available";
		_phase.Text = view.Decision is { } decision
			? $"{decision.OwnerSide.ToString().ToUpperInvariant()} · {decision.Kind.ToString().ToUpperInvariant()}"
			: view.Outcome.IsTerminal ? "GAME OVER" : "RESOLVING";
		_focus.Text = view.Run is { } run
			? $"{run.FocusLabel} · attacked {run.AttackedServerId} · {run.Step}"
			: "RUN FOCUS · no active run";
		_decision.Text = snapshot.IsOpponentThinking
			? "CORP THINKING · asynchronous agent delay\nThe render loop is free to process input and card inspection."
			: view.Decision is { } prompt
				? $"{prompt.OwnerSide} decision · {prompt.Prompt}\nRevision {prompt.Revision} · {view.LegalActions.Count} legal action(s)"
				: view.Outcome.IsTerminal ? "Outcome reached." : "No pending decision.";

		_corpSummary.Text = FormatPlayer(view.Corp, view.CorpHand.Count, view.CorpDeck.Count);
		_runnerSummary.Text = FormatPlayer(view.Runner, view.RunnerHand.Count, view.RunnerDeck.Count);
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
		if (snapshot.IsOpponentThinking)
		{
			_actions.AddChild(MakeLabel("Opponent thinking…", 13, Warning));
			return;
		}

		DecisionViewModel? decision = snapshot.View.Decision;
		if (decision is null || decision.OwnerSide != PlayerSide.Runner)
		{
			_actions.AddChild(MakeLabel("Waiting for the next issued Runner decision.", 12, Muted));
			return;
		}

		foreach (LegalActionViewModel action in snapshot.View.LegalActions)
		{
			Button button = new()
			{
				Text = $"{action.Kind}: {action.Description}",
				Alignment = HorizontalAlignment.Left,
				Disabled = Volatile.Read(ref _submissionInFlight) != 0,
			};
			button.Pressed += () => SubmitAction(decision, action);
			_actions.AddChild(button);
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
		control.CardInspected += cardId => InspectCard(cardId, card);
		return control;
	}

	private void InspectCard(string cardId, CardViewModel? card)
	{
		if (card is null)
		{
			return;
		}

		_details.Text = $"{card.Title}\n{card.Type} · {card.DefinitionId}\n"
			+ $"{card.RulesText}\n\nClick feedback is local and immediate; engine actions remain ID-only.";
		_details.AddThemeColorOverride("font_color", Colors.White);
	}

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
