using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Builds the board shell and its stable viewports. Dynamic snapshot content
/// is still rendered by Main; this region owns only board composition.
/// </summary>
public partial class Main
{
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
		Button restart = new() { Text = "Restart / choose side" };
		restart.Pressed += () => GetTree().ReloadCurrentScene();
		toolbar.AddChild(restart);
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

		_bodyScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
		};
		SetFullRect(_bodyScroll);
		bodyViewport.AddChild(_bodyScroll);

		HBoxContainer body = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		body.AddThemeConstantOverride("separation", 10);
		_bodyScroll.AddChild(body);

		_corpPanel = MakePanel();
		_corpPanel.CustomMinimumSize = new Vector2(205, 0);
		VBoxContainer corpContent = new();
		corpContent.AddThemeConstantOverride("separation", 7);
		_corpPanel.AddChild(corpContent);
		corpContent.AddChild(MakeLabel("CORP · REMOTE NETWORK", 14, new Color(0.93f, 0.48f, 0.48f)));
		_corpSummary = MakeLabel("Credits · Clicks · Score", 13, Muted);
		corpContent.AddChild(_corpSummary);
		corpContent.AddChild(MakeLabel("HQ / R&D / Archives", 12, Muted));
		body.AddChild(_corpPanel);

		_centerPanel = MakePanel();
		_centerPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_centerPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		VBoxContainer center = new();
		center.AddThemeConstantOverride("separation", 7);
		_centerPanel.AddChild(center);
		_turnSummary = MakeWrappedLabel(
			"TURN / TIMING · waiting for a valid observation projection",
			12,
			Warning);
		center.AddChild(_turnSummary);
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
		_damageStatus = MakeWrappedLabel("DAMAGE · none observed", 11, Muted);
		center.AddChild(_damageStatus);
		body.AddChild(_centerPanel);

		_runnerPanel = MakePanel();
		_runnerPanel.CustomMinimumSize = new Vector2(260, 0);
		VBoxContainer runnerContent = new();
		runnerContent.AddThemeConstantOverride("separation", 6);
		_runnerPanel.AddChild(runnerContent);
		runnerContent.AddChild(MakeLabel("RUNNER RIG · YOUR HAND", 14, Accent));
		_runnerSummary = MakeLabel("Credits · Clicks · Score", 13, Muted);
		runnerContent.AddChild(_runnerSummary);
		runnerContent.AddChild(MakeLabel("Rig", 12, Warning));
		_runnerRig = new VBoxContainer();
		_runnerRig.AddThemeConstantOverride("separation", 4);
		runnerContent.AddChild(_runnerRig);
		runnerContent.AddChild(MakeLabel("Your hand (HQ / Grip)", 12, Warning));
		_runnerHand = new VBoxContainer();
		_runnerHand.AddThemeConstantOverride("separation", 4);
		runnerContent.AddChild(_runnerHand);
		runnerContent.AddChild(MakeLabel("Scoring", 12, Warning));
		_scores = new VBoxContainer();
		_scores.AddThemeConstantOverride("separation", 4);
		runnerContent.AddChild(_scores);
		body.AddChild(_runnerPanel);

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

	private void RenderRunnerCards(BoardRegionState board)
	{
		ClearChildren(_runnerRig);
		foreach (CardViewModel card in board.RunnerRig)
		{
			TextCardControl control = MakeCard(card);
			EncounterBreakerViewModel? breaker = board.Run?.Encounter?.Breakers
				.FirstOrDefault(candidate => candidate.CardId == card.Id);
			control.SetEncounterStrength(breaker?.StrengthBreakdown);
			_runnerRig.AddChild(control);
		}

		ClearChildren(_runnerHand);
		ZoneViewModel hand = board.Viewer == PlayerSide.Corp ? board.CorpHand! : board.RunnerHand;
		ZoneViewModel deck = board.Viewer == PlayerSide.Corp ? board.CorpDeck! : board.RunnerDeck;
		_runnerHand.AddChild(MakeLabel(
			$"{board.Viewer} · {hand.Count} card(s) · deck {deck.Count}",
			11,
			Muted));
		foreach (CardViewModel card in hand.VisibleCards)
		{
			_runnerHand.AddChild(MakeCard(card));
		}

		ClearChildren(_scores);
		_scores.AddChild(MakeLabel(
			$"Runner score: {board.Runner.AgendaPoints} · Corp score: {board.Corp.AgendaPoints}",
			11,
			Warning));
		foreach (CardViewModel card in board.RunnerScore)
		{
			_scores.AddChild(MakeLabel($"Runner · {card.Title} ({card.AgendaPoints} AP)", 10, Muted));
		}

		foreach (CardViewModel card in board.CorpScore)
		{
			_scores.AddChild(MakeLabel($"Corp · {card.Title} ({card.AgendaPoints} AP)", 10, Muted));
		}
	}
}
