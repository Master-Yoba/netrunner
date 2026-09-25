using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Detached UX-401 presentation gate. The fixture is intentionally built from
/// Match view models and passed through the same Main render regions as a live
/// match; it never creates or mutates authoritative Core state.
/// </summary>
public partial class Main
{
	private async Task RunSlice48UxSmokeAsync()
	{
		bool setup = false;
		bool fiveCardHand = false;
		bool denseServerAndRig = false;
		bool nestedChoice = false;
		bool discardPrompt = false;
		bool runFocus = false;
		bool outcome = false;
		bool focusOrder = false;
		bool reducedMotion = false;
		bool nonColorCues = false;
		bool scrollContainment = false;
		bool metadata = false;
		bool metadataMarkupProbe = false;
		bool officialContent = false;
		bool requireOfficialContent = false;
		bool zoomFocus = false;
		bool layout1920x1080 = false;
		bool layout2560x1440 = false;
		bool focus1920x1080 = false;
		bool focus2560x1440 = false;
		bool scroll1920x1080 = false;
		bool scroll2560x1440 = false;
		bool screenshotRequested = false;
		bool screenshotSaved = false;
		try
		{
			setup = HasSlice48SetupSurface();
			focusOrder = await CheckSlice48FocusOrderAsync(_startPanel);

			_board.Visible = true;
			_reducedMotion.ButtonPressed = true;
			_speed.Value = 1.0;

			(CardViewModel[] hand, IReadOnlyList<ServerViewModel> servers, CardViewModel[] rig) =
				CreateSlice48Board();
			requireOfficialContent = OS.GetCmdlineUserArgs().Contains(
				"--require-official-content",
				StringComparer.Ordinal);
			DecisionViewModel nestedDecision = new(
				"decision:slice48:nested",
				1,
				new PlayerId("player:slice48-runner"),
				PlayerSide.Runner,
				DecisionKind.BehaviorChoice,
				"NESTED CHOICE · choose a server, then choose a card.");
			LegalActionViewModel chooseServer = new(
				new ActionId("action:slice48-server"),
				ActionKind.ChooseBehavior,
				"CHOOSE SERVER · Remote Alpha",
				new BehaviorChoiceParameters("slice48-nested", ["choose-server"], finalizeSelection: false));
			LegalActionViewModel chooseCard = new(
				new ActionId("action:slice48-card"),
				ActionKind.ChooseBehavior,
				"CHOOSE CARD · Hosted program",
				new BehaviorChoiceParameters("slice48-nested", "choose-card"));
			MatchViewModel nestedView = CreateSlice48View(
				hand,
				servers,
				rig,
				nestedDecision,
				[chooseServer, chooseCard],
				CreateSlice48Run(null),
				new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null));
			officialContent = HasSlice48OfficialFixtureContent(hand, servers, rig, nestedView);
			MatchSnapshot nestedSnapshot = CreateSlice48Snapshot(nestedView, nestedDecision);
			RenderSnapshot(nestedSnapshot);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			fiveCardHand = FindDescendants<TextCardControl>(_runnerHand)
				.Count(control => control.IsInsideTree()) == 5;
			denseServerAndRig = servers.Count >= 3
				&& servers.All(server =>
				server.OrderedIce.Count == 3
					&& server.OrderedIce.All(card => card is not null)
					&& server.OrderedRoot.Count == 1
					&& server.OrderedRoot.All(card => card is not null))
				&& FindDescendants<TextCardControl>(_runnerRig)
					.Count(control => control.IsInsideTree()) >= 8;
			string[] nestedActionLabels = FindDescendants<Button>(_actions)
				.Select(button => button.Text)
				.ToArray();
			nestedChoice = _decision.Text.Contains("NESTED CHOICE", StringComparison.Ordinal)
				&& nestedActionLabels.Length >= 2
				&& nestedActionLabels.Any(label =>
					label.Contains("OPTION", StringComparison.OrdinalIgnoreCase)
					&& label.Contains("choose-server", StringComparison.OrdinalIgnoreCase))
				&& nestedActionLabels.Any(label =>
					label.Contains("OPTION", StringComparison.OrdinalIgnoreCase)
					&& label.Contains("choose-card", StringComparison.OrdinalIgnoreCase));

			TextCardControl metadataCard = FindDescendants<TextCardControl>(_runnerHand)
				.FirstOrDefault(control => control.CardId == hand[0].Id)
				?? throw new InvalidOperationException("UX-401 fixture did not render the first hand card.");
			InspectCard(metadataCard.CardId, hand[0]);
			metadata = _details.Text.Contains("[credit]", StringComparison.Ordinal)
				&& !_details.Text.Contains("<symbol:", StringComparison.Ordinal)
				&& !_details.Text.Contains("<strong>", StringComparison.Ordinal)
				&& !_details.Text.Contains("<ul>", StringComparison.Ordinal)
				&& !_details.Text.Contains("<li>", StringComparison.Ordinal);
			string markupProbe = FormatCardRules(
				"<strong>Markup probe</strong><br><ul><li>Gain 1<symbol:credit>.</li></ul>");
			metadataMarkupProbe = markupProbe.Contains("[credit]", StringComparison.Ordinal)
				&& markupProbe.Contains('•')
				&& !markupProbe.Contains("<symbol:", StringComparison.Ordinal)
				&& !markupProbe.Contains("<strong>", StringComparison.Ordinal)
				&& !markupProbe.Contains("<ul>", StringComparison.Ordinal)
				&& !markupProbe.Contains("<li>", StringComparison.Ordinal);
			metadata &= metadataMarkupProbe;

			zoomFocus = await CheckSlice48ZoomFocusAsync();

			metadataCard.SetTargetCandidate(true);
			metadataCard.SetSelected(true);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			nonColorCues = SceneTreeContainsText(metadataCard, "TARGET SELECTED")
				&& SceneTreeContainsText(_actionPanel, "LEGAL ACTIONS")
				&& SceneTreeContainsText(_startPanel, "OFFICIAL PRESETS");

			// Run the actual event-feed queue with reduced motion enabled. This
			// exercises the production animation branch instead of only inspecting
			// the CheckButton's value.
			EngineEvent cueEvent = CreateSlice48CardMovedEvent();
			_animationBatches.Enqueue([cueEvent]);
			await AnimateEventsAsync();
			reducedMotion = _reducedMotion.ButtonPressed
				&& !_animationRunning
				&& _eventFeed.Text.Contains("moved", StringComparison.OrdinalIgnoreCase);

			scrollContainment = CheckSlice48ScrollContainment()
				&& await CheckSlice48ScrollReachabilityAsync();

			screenshotRequested = OS.GetCmdlineUserArgs().Contains(
				"--m4-slice48-ui-screenshot",
				StringComparer.Ordinal);
			if (screenshotRequested)
			{
				// FramePostDraw is not emitted by every headless renderer. Two
				// process frames are sufficient for the native window and keep the
				// optional artifact bounded in CI as well as on desktop.
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				screenshotSaved = CaptureSlice48Screenshot();
			}
			else
			{
				screenshotSaved = true;
			}

			DecisionViewModel discardDecision = new(
				"decision:slice48:discard",
				2,
				new PlayerId("player:slice48-runner"),
				PlayerSide.Runner,
				DecisionKind.TurnDiscard,
				"Choose 1 card to discard down to your maximum hand size.");
			LegalActionViewModel discardAction = new(
				new ActionId("action:slice48-discard"),
				ActionKind.ChooseCard,
				"Discard · Teaching Card",
				new CardChoiceParameters(new CardInstanceId(hand[0].Id)));
			MatchViewModel discardView = CreateSlice48View(
				hand,
				servers,
				rig,
				discardDecision,
				[discardAction],
				CreateSlice48Run(null),
				new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null)) with
			{
				ForcedDiscard = new ForcedDiscardViewModel(
					PlayerSide.Runner,
					discardDecision.Prompt,
					new HandSizeViewModel(PlayerSide.Runner, hand.Length, 5, null)),
			};
			RenderSnapshot(CreateSlice48Snapshot(discardView, discardDecision));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			discardPrompt = SceneTreeContainsText(_actions, "FORCED DISCARD")
				&& FindDescendants<Button>(_actions).Any(button =>
					button.Text.Contains("DISCARD", StringComparison.OrdinalIgnoreCase));

			MatchViewModel runView = CreateSlice48View(
				hand,
				servers,
				rig,
				nestedDecision,
				[chooseServer],
				CreateSlice48Run(servers[0].OrderedIce[0]),
				new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null));
			RenderSnapshot(CreateSlice48Snapshot(runView, nestedDecision));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			runFocus = SceneTreeContainsText(_focusPresentation, "ENCOUNTER FOCUS")
				&& SceneTreeContainsText(_focusPresentation, "ICE STRENGTH")
				&& SceneTreeContainsText(_focusPresentation, "SUBROUTINES");

			MatchViewModel terminalView = runView with
			{
				Decision = null,
				Run = null,
				LegalActions = Array.Empty<LegalActionViewModel>(),
				Outcome = new OutcomeViewModel(
					GameOutcomeKind.Won,
					PlayerSide.Runner,
					GameWinReason.DeckOut),
			};
			RenderSnapshot(CreateSlice48Snapshot(terminalView, null));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			outcome = SceneTreeContainsText(_focusPresentation, "MATCH OUTCOME")
				&& SceneTreeContainsText(_focusPresentation, "New match");

			focusOrder = focusOrder && await CheckSlice48FocusOrderAsync(_board);
			Slice48Surface[] surfaces =
			[
				new Slice48Surface(
					"nested",
					nestedView,
					nestedDecision,
					["NESTED CHOICE", "CHOOSE SERVER", "CHOOSE CARD"]),
				new Slice48Surface(
					"discard",
					discardView,
					discardDecision,
					["FORCED DISCARD", "DISCARD"]),
				new Slice48Surface(
					"run",
					runView,
					nestedDecision,
					["ENCOUNTER FOCUS", "ICE STRENGTH", "SUBROUTINES"]),
				new Slice48Surface(
					"outcome",
					terminalView,
					null,
					["MATCH OUTCOME", "New match"]),
			];
			Slice48ResolutionEvidence layout1920Evidence = await CheckSlice48SurfaceLayoutsAsync(
				new Vector2I(1920, 1080),
				surfaces);
			layout1920x1080 = layout1920Evidence.Layout;
			focus1920x1080 = layout1920Evidence.FocusOrder;
			scroll1920x1080 = layout1920Evidence.ScrollReachability;
			Slice48ResolutionEvidence layout2560Evidence = await CheckSlice48SurfaceLayoutsAsync(
				new Vector2I(2560, 1440),
				surfaces);
			layout2560x1440 = layout2560Evidence.Layout;
			focus2560x1440 = layout2560Evidence.FocusOrder;
			scroll2560x1440 = layout2560Evidence.ScrollReachability;
			focusOrder &= focus1920x1080 && focus2560x1440;
			scrollContainment &= scroll1920x1080 && scroll2560x1440;

			bool passed = setup
				&& fiveCardHand
				&& denseServerAndRig
				&& nestedChoice
				&& discardPrompt
				&& runFocus
				&& outcome
				&& (!requireOfficialContent || officialContent)
				&& focusOrder
				&& reducedMotion
				&& nonColorCues
				&& scrollContainment
				&& metadata
				&& zoomFocus
				&& layout1920x1080
				&& layout2560x1440
				&& screenshotSaved;
			GD.Print(
				$"M4_SLICE48_UX {(passed ? "PASS" : "FAIL")} "
				+ $"setup={FormatSlice48Bool(setup)} "
				+ $"five_card_hand={FormatSlice48Bool(fiveCardHand)} "
				+ $"dense_server_rig={FormatSlice48Bool(denseServerAndRig)} "
				+ $"nested_choice={FormatSlice48Bool(nestedChoice)} "
				+ $"discard_prompt={FormatSlice48Bool(discardPrompt)} "
				+ $"run_focus={FormatSlice48Bool(runFocus)} "
				+ $"outcome={FormatSlice48Bool(outcome)} "
				+ $"official_content_required={FormatSlice48Bool(requireOfficialContent)} "
				+ $"official_content={FormatSlice48Bool(officialContent)} "
				+ $"metadata_mode={_metadataSnapshot.Mode} "
				+ $"metadata_cards={_metadataSnapshot.CardCount} "
				+ $"focus_order={FormatSlice48Bool(focusOrder)} "
				+ $"focus_1920x1080={FormatSlice48Bool(focus1920x1080)} "
				+ $"focus_2560x1440={FormatSlice48Bool(focus2560x1440)} "
				+ $"reduced_motion={FormatSlice48Bool(reducedMotion)} "
				+ $"non_color={FormatSlice48Bool(nonColorCues)} "
				+ $"scroll_containment={FormatSlice48Bool(scrollContainment)} "
				+ $"scroll_1920x1080={FormatSlice48Bool(scroll1920x1080)} "
				+ $"scroll_2560x1440={FormatSlice48Bool(scroll2560x1440)} "
				+ $"metadata={FormatSlice48Bool(metadata)} "
				+ $"metadata_markup_probe={FormatSlice48Bool(metadataMarkupProbe)} "
				+ $"zoom_focus={FormatSlice48Bool(zoomFocus)} "
				+ $"layout_1920x1080={FormatSlice48Bool(layout1920x1080)} "
				+ $"layout_2560x1440={FormatSlice48Bool(layout2560x1440)}");
			GD.Print(
				$"M4_SLICE48_UX_SCREENSHOT requested={FormatSlice48Bool(screenshotRequested)} "
				+ $"saved={FormatSlice48Bool(screenshotSaved)} "
				+ "path=docs/evidence/slice48/slice48-ui-smoke.png");
			GetTree().Quit(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print(
				$"M4_SLICE48_UX FAIL setup={FormatSlice48Bool(setup)} "
				+ $"five_card_hand={FormatSlice48Bool(fiveCardHand)} "
				+ $"dense_server_rig={FormatSlice48Bool(denseServerAndRig)} "
				+ $"nested_choice={FormatSlice48Bool(nestedChoice)} "
				+ $"discard_prompt={FormatSlice48Bool(discardPrompt)} "
				+ $"run_focus={FormatSlice48Bool(runFocus)} "
				+ $"outcome={FormatSlice48Bool(outcome)} "
				+ $"official_content_required={FormatSlice48Bool(requireOfficialContent)} "
				+ $"official_content={FormatSlice48Bool(officialContent)} "
				+ $"metadata_mode={_metadataSnapshot.Mode} "
				+ $"metadata_cards={_metadataSnapshot.CardCount} "
				+ $"focus_order={FormatSlice48Bool(focusOrder)} "
				+ $"focus_1920x1080={FormatSlice48Bool(focus1920x1080)} "
				+ $"focus_2560x1440={FormatSlice48Bool(focus2560x1440)} "
				+ $"reduced_motion={FormatSlice48Bool(reducedMotion)} "
				+ $"non_color={FormatSlice48Bool(nonColorCues)} "
				+ $"scroll_containment={FormatSlice48Bool(scrollContainment)} "
				+ $"scroll_1920x1080={FormatSlice48Bool(scroll1920x1080)} "
				+ $"scroll_2560x1440={FormatSlice48Bool(scroll2560x1440)} "
				+ $"metadata={FormatSlice48Bool(metadata)} "
				+ $"metadata_markup_probe={FormatSlice48Bool(metadataMarkupProbe)} "
				+ $"zoom_focus={FormatSlice48Bool(zoomFocus)} "
				+ $"layout_1920x1080={FormatSlice48Bool(layout1920x1080)} "
				+ $"layout_2560x1440={FormatSlice48Bool(layout2560x1440)} "
				+ $"screenshot={FormatSlice48Bool(screenshotSaved)} "
				+ $"error={exception.Message.Replace('\n', ' ').Replace('\r', ' ')}");
			GetTree().Quit(1);
		}
	}

	private bool HasSlice48SetupSurface()
	{
		bool presets = HasPresetKinds(_corpPreset) && HasPresetKinds(_runnerPreset)
			|| PresetUnavailable(_corpPreset) && PresetUnavailable(_runnerPreset);
		return SceneTreeContainsText(_startPanel, "MATCH SETUP")
			&& SceneTreeContainsText(_startPanel, "OFFICIAL PRESETS")
			&& presets
			&& _humanSide.ItemCount == 2
			&& _startButton.FocusMode != Control.FocusModeEnum.None;
	}

	private bool HasSlice48OfficialFixtureContent(
		CardViewModel[] hand,
		IReadOnlyList<ServerViewModel> servers,
		IReadOnlyList<CardViewModel> rig,
		MatchViewModel view)
	{
		if (!_metadataSnapshot.IsOfficial
			|| _metadataSnapshot.CardCount != 77
			|| _metadataResolver.Cards.Count != 77)
		{
			return false;
		}

		IEnumerable<CardViewModel> fixtureCards = hand
			.Concat(rig)
			.Concat(servers.SelectMany(server => server.OrderedIce
				.Concat(server.OrderedRoot)
				.OfType<CardViewModel>()));
		if (view.CorpIdentity is { } corpIdentity)
		{
			fixtureCards = fixtureCards.Append(corpIdentity);
		}
		if (view.RunnerIdentity is { } runnerIdentity)
		{
			fixtureCards = fixtureCards.Append(runnerIdentity);
		}

		CardViewModel[] cards = fixtureCards.ToArray();
		return cards.Length > 0
			&& cards.All(card =>
				card.DefinitionId is { Length: > 0 } definitionId
				&& !definitionId.StartsWith("fixture:", StringComparison.Ordinal)
				&& _metadataResolver.TryResolve(definitionId, out CardPresentationMetadata metadata)
				&& string.Equals(metadata.Title, card.Title, StringComparison.Ordinal)
				&& string.Equals(metadata.Type, card.Type, StringComparison.Ordinal)
				&& string.Equals(metadata.RulesText, card.RulesText, StringComparison.Ordinal));
	}

	private async Task<bool> CheckSlice48FocusOrderAsync(Node root)
	{
		List<Control> controls = FindDescendants<Control>(root)
			.Where(static control => control.Visible
				&& !control.IsQueuedForDeletion()
				&& control.FocusMode == Control.FocusModeEnum.All
				&& control is not ScrollContainer)
			.Where(static control => control is not BaseButton button || !button.Disabled)
			.ToList();
		if (controls.Count < 2)
		{
			return false;
		}
		for (int index = 0; index < controls.Count; index++)
		{
			Control control = controls[index];
			control.GrabFocus();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (GetViewport().GuiGetFocusOwner() != control
				|| !IsSlice48FocusControlVisible(control))
			{
				return false;
			}

			Control next = index + 1 < controls.Count ? controls[index + 1] : controls[0];
			if (control.FocusNext != next.GetPath())
			{
				return false;
			}

			InputEventKey tab = new()
			{
				Keycode = Key.Tab,
				PhysicalKeycode = Key.Tab,
				Pressed = true,
				Echo = false,
			};
			GetViewport().PushInput(tab);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (GetViewport().GuiGetFocusOwner() != next
				|| !IsSlice48FocusControlVisible(next))
			{
				return false;
			}
		}

		for (int index = controls.Count - 1; index >= 0; index--)
		{
			Control control = controls[index];
			control.GrabFocus();
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (GetViewport().GuiGetFocusOwner() != control
				|| !IsSlice48FocusControlVisible(control))
			{
				return false;
			}

			Control previous = index > 0 ? controls[index - 1] : controls[^1];
			if (control.FocusPrevious != previous.GetPath())
			{
				return false;
			}

			InputEventKey shiftTab = new()
			{
				Keycode = Key.Tab,
				PhysicalKeycode = Key.Tab,
				Pressed = true,
				Echo = false,
				ShiftPressed = true,
			};
			GetViewport().PushInput(shiftTab);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (GetViewport().GuiGetFocusOwner() != previous
				|| !IsSlice48FocusControlVisible(previous))
			{
				return false;
			}
		}

		return true;
	}

	private bool IsSlice48FocusControlVisible(Control control)
	{
		if (!control.IsVisibleInTree())
		{
			return false;
		}

		Rect2 controlRect = control.GetGlobalRect();
		if (!IsSlice48RectInside(controlRect, GetViewport().GetVisibleRect()))
		{
			return false;
		}

		for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent())
		{
			if (parent is ScrollContainer scroll
				&& !IsSlice48RectInside(controlRect, scroll.GetGlobalRect()))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsSlice48RectInside(Rect2 inner, Rect2 outer)
	{
		const float epsilon = 0.5f;
		return inner.Position.X >= outer.Position.X - epsilon
			&& inner.Position.Y >= outer.Position.Y - epsilon
			&& inner.End.X <= outer.End.X + epsilon
			&& inner.End.Y <= outer.End.Y + epsilon;
	}

	private async Task<bool> CheckSlice48ZoomFocusAsync()
	{
		if (!_zoomButton.IsVisibleInTree() || _zoomButton.Disabled)
		{
			return false;
		}

		_zoomButton.GrabFocus();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (GetViewport().GuiGetFocusOwner() != _zoomButton)
		{
			return false;
		}

		_zoomButton.EmitSignal(BaseButton.SignalName.Pressed);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!_zoomOverlay.Visible || GetViewport().GuiGetFocusOwner() != _zoomScroll)
		{
			return false;
		}

		InputEventKey tab = new()
		{
			Keycode = Key.Tab,
			PhysicalKeycode = Key.Tab,
			Pressed = true,
			Echo = false,
		};
		GetViewport().PushInput(tab);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool tabTrap = GetViewport().GuiGetFocusOwner() == _zoomCloseButton;

		InputEventKey shiftTab = new()
		{
			Keycode = Key.Tab,
			PhysicalKeycode = Key.Tab,
			Pressed = true,
			Echo = false,
			ShiftPressed = true,
		};
		GetViewport().PushInput(shiftTab);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool reverseTrap = GetViewport().GuiGetFocusOwner() == _zoomScroll;

		InputEventKey escape = new()
		{
			Keycode = Key.Escape,
			PhysicalKeycode = Key.Escape,
			Pressed = true,
			Echo = false,
		};
		GetViewport().PushInput(escape);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		return tabTrap
			&& reverseTrap
			&& !_zoomOverlay.Visible
			&& GetViewport().GuiGetFocusOwner() == _zoomButton;
	}

	private bool CheckSlice48ScrollContainment()
	{
		if (!IsInstanceValid(_bodyScroll) || !_bodyScroll.IsVisibleInTree()
			|| _bodyScroll.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled
			|| _bodyScroll.VerticalScrollMode != ScrollContainer.ScrollMode.Auto)
		{
			return false;
		}

		Rect2 bodyRect = _bodyScroll.GetGlobalRect();
		if (!IsHorizontallyInside(_corpPanel, bodyRect)
			|| !IsHorizontallyInside(_centerPanel, bodyRect)
			|| !IsHorizontallyInside(_runnerPanel, bodyRect))
		{
			return false;
		}

		ScrollContainer[] nestedScrolls = FindDescendants<ScrollContainer>(this)
			.Where(scroll => scroll.IsVisibleInTree())
			.ToArray();
		return nestedScrolls.Length >= 3
			&& nestedScrolls.All(scroll =>
				scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled
				&& scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Auto
				&& scroll.ClipContents
				&& IsHorizontallyInside(scroll, GetViewport().GetVisibleRect()));
	}

	private async Task<bool> CheckSlice48ScrollReachabilityAsync()
	{
		TextCardControl? lastRigCard = FindDescendants<TextCardControl>(_runnerRig)
			.LastOrDefault(control => control.IsInsideTree());
		if (lastRigCard is null)
		{
			return false;
		}

		int previousScroll = _bodyScroll.ScrollVertical;
		_bodyScroll.ScrollVertical = 0;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		int maximumScroll = (int)Math.Round(_bodyScroll.GetVScrollBar().MaxValue);
		_bodyScroll.EnsureControlVisible(lastRigCard);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Rect2 viewport = _bodyScroll.GetGlobalRect();
		Rect2 card = lastRigCard.GetGlobalRect();
		bool reachable = maximumScroll <= 0
			|| card.Position.Y >= viewport.Position.Y - 1
				&& card.End.Y <= viewport.End.Y + 1
				&& _bodyScroll.ScrollVertical > 0;
		_bodyScroll.ScrollVertical = previousScroll;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		return reachable;
	}

	private async Task<Slice48ResolutionEvidence> CheckSlice48SurfaceLayoutsAsync(
		Vector2I requestedSize,
		IReadOnlyList<Slice48Surface> surfaces)
	{
		Window rootWindow = GetViewport() as Window
			?? throw new InvalidOperationException("UX-401 root viewport is not a Window.");
		Vector2I previousSize = rootWindow.Size;
		Vector2I previousContentScale = rootWindow.ContentScaleSize;
		rootWindow.Size = requestedSize;
		rootWindow.ContentScaleSize = requestedSize;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Vector2 visibleSize = GetViewport().GetVisibleRect().Size;
		bool requestedViewport = Math.Abs(visibleSize.X - requestedSize.X) <= 2
			&& Math.Abs(visibleSize.Y - requestedSize.Y) <= 2;
		bool layout = requestedViewport;
		Control? setupPanel = _startPanel.GetParent() as Control;
		_board.Visible = false;
		if (setupPanel is not null)
		{
			setupPanel.Visible = true;
		}
		_status.Text = "Choose your side and decks to begin";
		_phase.Text = "SETUP";

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		layout &= setupPanel is not null
			&& IsFullyInsideViewport(setupPanel)
			&& SceneTreeContainsText(_startPanel, "MATCH SETUP")
			&& SceneTreeContainsText(_startPanel, "OFFICIAL PRESETS")
			&& AreVisibleLabelsLegible(_startPanel);
		bool setupFocusOrder = setupPanel is not null
			&& await CheckSlice48FocusOrderAsync(_startPanel);

		bool focusOrder = setupFocusOrder;
		bool scrollReachability = true;
		bool capture = OS.GetCmdlineUserArgs().Contains("--m4-slice48-ui-screenshot", StringComparer.Ordinal);
		if (capture)
		{
			// Capture the actual native framebuffer at each required size.
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			layout &= CaptureSlice48Screenshot($"setup-{requestedSize.X}x{requestedSize.Y}.png", requestedSize);
		}
		_board.Visible = true;
		if (setupPanel is not null)
		{
			setupPanel.Visible = false;
		}

		foreach (Slice48Surface surface in surfaces)
		{
			RenderSnapshot(CreateSlice48Snapshot(surface.View, surface.Decision));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			bool surfaceLayout = IsFullyInsideViewport(_board)
				&& IsFullyInsideViewport(_actionPanel)
				&& AreBodyColumnsInsideViewport()
				&& AreVisibleLabelsLegible(_layout)
				&& CheckSlice48ScrollContainment()
				&& surface.RequiredText.All(text => SceneTreeContainsText(_board, text));
			layout &= surfaceLayout;
			if (capture && surface.Name == "nested")
			{
				_bodyScroll.ScrollVertical = 0;
				await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
				layout &= CaptureSlice48Screenshot($"board-{requestedSize.X}x{requestedSize.Y}.png", requestedSize);
			}
			focusOrder &= await CheckSlice48FocusOrderAsync(_board);
			scrollReachability &= await CheckSlice48ScrollReachabilityAsync();
			GD.Print($"M4_SLICE48_SURFACE size={requestedSize.X}x{requestedSize.Y} surface={surface.Name} layout={FormatSlice48Bool(surfaceLayout)} focus={FormatSlice48Bool(focusOrder)} scroll={FormatSlice48Bool(scrollReachability)}");
		}

		rootWindow.Size = previousSize;
		rootWindow.ContentScaleSize = previousContentScale;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		return new Slice48ResolutionEvidence(
			requestedViewport && layout,
			focusOrder,
			scrollReachability);
	}

	private sealed record Slice48Surface(
		string Name,
		MatchViewModel View,
		DecisionViewModel? Decision,
		IReadOnlyList<string> RequiredText);

	private sealed record Slice48ResolutionEvidence(
		bool Layout,
		bool FocusOrder,
		bool ScrollReachability);

	private static MatchSnapshot CreateSlice48Snapshot(
		MatchViewModel view,
		DecisionViewModel? decision)
	{
		Observation observation = new(
			PlayerSide.Runner,
			decision?.Id ?? string.Empty,
			Array.Empty<LegalAction>(),
			new Dictionary<string, object?>(),
			new Dictionary<string, object?>())
		{
			Decision = decision is null
				? null
				: new ObservationDecision(
					new DecisionId(decision.Id),
					decision.Revision,
					decision.Owner,
					decision.OwnerSide,
					decision.Kind,
					decision.Prompt),
		};
		return new MatchSnapshot(1, observation, view, Array.Empty<EngineEvent>(), null, false);
	}

	private MatchViewModel CreateSlice48View(
		CardViewModel[] hand,
		IReadOnlyList<ServerViewModel> servers,
		IReadOnlyList<CardViewModel> rig,
		DecisionViewModel? decision,
		IReadOnlyList<LegalActionViewModel> actions,
		RunFocusViewModel? run,
		OutcomeViewModel outcome)
	{
		CardViewModel corpIdentity = CreateSlice48ContentCard(
			"card:slice48-corp-id",
			"Identity",
			"Synthetic Corp Identity",
			"<strong>Identity</strong> metadata is visible.",
			faction: "Weyland", side: "corp");
		CardViewModel runnerIdentity = CreateSlice48ContentCard(
			"card:slice48-runner-id",
			"Identity",
			"Synthetic Runner Identity",
			"<strong>Identity</strong> metadata is visible.",
			faction: "Anarch", side: "runner");
		return new MatchViewModel(
			PlayerSide.Runner,
			new PlayerViewModel("player:slice48-corp", PlayerSide.Corp, 12, 3, 2),
			new PlayerViewModel("player:slice48-runner", PlayerSide.Runner, 9, 4, 0)
			{
				Tags = 1,
			},
			servers,
			new ZoneViewModel(ZoneKind.CorpHand, 7, Array.Empty<CardViewModel>()),
			new ZoneViewModel(ZoneKind.CorpDeck, 21, Array.Empty<CardViewModel>()),
			new ZoneViewModel(ZoneKind.RunnerGrip, hand.Length, hand),
			new ZoneViewModel(ZoneKind.RunnerStack, 24, Array.Empty<CardViewModel>()),
			rig,
			Array.Empty<CardViewModel>(),
			Array.Empty<CardViewModel>(),
			decision,
			run,
			outcome,
			actions)
		{
			CorpIdentity = corpIdentity,
			RunnerIdentity = runnerIdentity,
			Turn = new TurnViewModel(
				true,
				PlayerSide.Runner,
				TurnPhase.Runner,
				TurnStep.RunnerAction,
				3,
				null),
			CorpHandSize = new HandSizeViewModel(PlayerSide.Corp, 7, 5, null),
			RunnerHandSize = new HandSizeViewModel(PlayerSide.Runner, hand.Length, 5, null),
		};
	}

	private (CardViewModel[] Hand, IReadOnlyList<ServerViewModel> Servers, CardViewModel[] Rig)
		CreateSlice48Board()
	{
		CardViewModel[] hand = Enumerable.Range(1, 5)
			.Select(index => index == 1
				? CreateSlice48RichContentCard(
					$"card:slice48-hand-{index}",
					"Teaching Card 1",
					"<strong>Run</strong> any server.<br><ul><li>Gain 1<symbol:credit>.</li></ul>")
				: CreateSlice48ContentCard(
					$"card:slice48-hand-{index}",
					"Event",
					$"Teaching Card {index}",
					"A readable five-card hand fixture."))
			.ToArray();
		IReadOnlyList<ServerViewModel> servers = Enumerable.Range(1, 3)
			.Select(serverIndex => new ServerViewModel(
				$"server:slice48-{serverIndex}",
				ServerKind.Remote,
				Enumerable.Range(1, 3)
					.Select(iceIndex => CreateSlice48ContentCard(
						$"card:slice48-ice-{serverIndex}-{iceIndex}",
						"ICE",
						$"Dense ICE {serverIndex}.{iceIndex}",
						"<symbol:subroutine> End the run.",
						strength: 4 + iceIndex,
						rezzed: true))
					.Cast<CardViewModel?>()
					.ToArray(),
				[
					CreateSlice48ContentCard(
						$"card:slice48-root-{serverIndex}",
						"Asset",
						$"Dense Asset {serverIndex}",
						"A visible remote root.",
						rezzed: true),
				]))
			.ToArray();
		CardViewModel[] rig = Enumerable.Range(1, 8)
			.Select(index => CreateSlice48ContentCard(
				$"card:slice48-rig-{index}",
				"Program",
				$"Rig Program {index}",
				"A visible installed program.",
				cost: index,
				memoryCost: 1,
				strength: index % 2 == 0 ? 3 : null))
			.ToArray();
		return (hand, servers, rig);
	}

	private CardViewModel CreateSlice48ContentCard(
		string id,
		string type,
		string fallbackTitle,
		string fallbackRules,
		string faction = "",
		int? cost = null,
		int? memoryCost = null,
		int? strength = null,
		bool rezzed = false,
		bool preferRichMarkup = false,
		string? side = null)
	{
		CardPresentationMetadata? metadata = _metadataResolver.Cards
			.Where(card => string.Equals(card.Type, type, StringComparison.OrdinalIgnoreCase))
			.Where(card => side is null || string.Equals(card.Side, side, StringComparison.OrdinalIgnoreCase))
			.Where(card => !preferRichMarkup || card.RulesText.Contains("<symbol:", StringComparison.Ordinal))
			.OrderByDescending(card => card.Title.Length + card.RulesText.Length)
			.FirstOrDefault();
		if (metadata is null)
		{
			return CreateSlice48Card(
				id,
				fallbackTitle,
				type,
				fallbackRules,
				faction,
				cost,
				memoryCost,
				strength,
				rezzed);
		}

		return CreateSlice48MetadataCard(
			id,
			metadata,
			rezzed,
			cost,
			memoryCost,
			strength,
			metadata.RulesText);
	}

	private CardViewModel CreateSlice48RichContentCard(
		string id,
		string fallbackTitle,
		string fallbackRules)
	{
		CardPresentationMetadata? metadata = _metadataResolver.Cards
			.Where(card => string.Equals(card.Type, "Event", StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(card => card.RulesText.Contains("<symbol:credit>", StringComparison.Ordinal))
			.ThenByDescending(card => card.Title.Length + card.RulesText.Length)
			.FirstOrDefault()
			?? _metadataResolver.Cards
				.OrderByDescending(card => card.Title.Length + card.RulesText.Length)
			.FirstOrDefault();
		return metadata is null
			? CreateSlice48Card(id, fallbackTitle, "Event", fallbackRules)
			: CreateSlice48MetadataCard(id, metadata, rezzed: false, null, null, null, metadata.RulesText);
	}

	private static CardViewModel CreateSlice48MetadataCard(
		string id,
		CardPresentationMetadata metadata,
		bool rezzed,
		int? fallbackCost,
		int? fallbackMemoryCost,
		int? fallbackStrength,
		string rules) => new(
			id,
			metadata.DefinitionId,
			metadata.Title,
			metadata.Type,
			true,
			rezzed || string.Equals(metadata.Type, "Identity", StringComparison.OrdinalIgnoreCase),
			0,
			metadata.Cost ?? fallbackCost,
			metadata.Strength ?? fallbackStrength,
			metadata.AgendaPoints,
			metadata.TrashCost,
			metadata.Subtypes,
			rules,
			metadata.Faction,
			metadata.MemoryCost ?? fallbackMemoryCost,
			metadata.AdvancementRequirement);

	private static CardViewModel CreateSlice48Card(
		string id,
		string title,
		string type,
		string rules,
		string faction = "",
		int? cost = null,
		int? memoryCost = null,
		int? strength = null,
		bool rezzed = false) => new(
			id,
			$"fixture:{id}",
			title,
			type,
			true,
			rezzed,
			0,
			cost,
			strength,
			null,
			null,
			Array.Empty<string>(),
			rules,
			faction,
			memoryCost,
			null);

	private static RunFocusViewModel CreateSlice48Run(CardViewModel? ice)
	{
		AccessViewModel access = new(
			Array.Empty<CardViewModel?>(),
			0,
			null,
			Array.Empty<CardViewModel?>(),
			Array.Empty<CardViewModel?>(),
			RunAccessState.NotStarted);
		EncounterViewModel? encounter = ice is null
			? null
			: new EncounterViewModel(
				"encounter:slice48",
				ice,
				RunEncounterStatus.Initialized,
				[
					new SubroutineViewModel(1, false, false),
					new SubroutineViewModel(2, true, false),
				])
			{
				EffectiveStrength = ice.Strength,
			};
		return new RunFocusViewModel(
			"run:slice48",
			"server:slice48-1",
			PlayerSide.Runner,
			RunCrPhase.Encounter,
			RunTimingStep.Encounter,
			0,
			ice,
			false,
			false,
			null,
			null,
			encounter,
			access,
			3,
			null);
	}

	private static EngineEvent CreateSlice48CardMovedEvent() => new(
		1,
		EngineEventKinds.CardMoved,
		new CardMovedEventPayload(
			new CardInstanceId("card:slice48-hand-1"),
			new ZoneId("zone:slice48-hand"),
			new ZoneId("zone:slice48-discard"),
			ZoneKind.RunnerGrip,
			ZoneKind.RunnerHeap));

	private static bool CaptureSlice48Screenshot(string filename = "slice48-ui-smoke.png", Vector2I? requiredSize = null)
	{
		try
		{
			Viewport? viewport = Engine.GetMainLoop() is SceneTree tree
				? tree.Root.GetViewport()
				: null;
			Image? image = viewport?.GetTexture()?.GetImage();
			if (image is null)
			{
				return false;
			}
			if (requiredSize is { } size && image.GetSize() != size)
			{
				GD.PrintErr($"UX-401 framebuffer expected={size} actual={image.GetSize()}");
				image.Dispose();
				return false;
			}

			string path = ProjectSettings.GlobalizePath(
				$"res://../../docs/evidence/slice48/{filename}");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			Error result = image.SavePng(path);
			image.Dispose();
			return result == Error.Ok;
		}
		catch (Exception exception)
		{
			GD.PushWarning($"Slice 4.8 screenshot capture failed: {exception.Message}");
			return false;
		}
	}

	private static string FormatSlice48Bool(bool value) => value ? "true" : "false";
}
