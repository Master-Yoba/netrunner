using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Detached Slice 4.7 client proof. It exercises the same Match projection and
/// Godot controls used by a live match; the smoke only submits no action and
/// therefore cannot mutate Core state.
/// </summary>
public partial class Main
{
	private async Task RunSlice47UiSmokeAsync()
	{
		bool setupChoices = false;
		bool identities = false;
		bool nbnChoices = false;
		bool accessCount = false;
		bool swapPreview = false;
		bool hostedTrojan = false;
		bool derezCue = false;
		bool outcomeFlow = false;
		bool screenshotRequested = false;
		bool screenshotSaved = false;
		try
		{
			setupChoices = _startPanel.GetChildCount() > 0
				&& SceneTreeContainsText(_startPanel, "OFFICIAL PRESETS")
				&& (HasPresetKinds(_corpPreset) && HasPresetKinds(_runnerPreset)
					|| PresetUnavailable(_corpPreset) && PresetUnavailable(_runnerPreset));
			if (!setupChoices)
			{
				GD.Print($"SLICE47_SETUP corp_disabled={_corpPreset.Disabled} runner_disabled={_runnerPreset.Disabled} "
					+ string.Join(" | ", Enumerable.Range(0, _corpPreset.ItemCount).Select(_corpPreset.GetItemText))
					+ " / " + string.Join(" | ", Enumerable.Range(0, _runnerPreset.ItemCount).Select(_runnerPreset.GetItemText)));
			}

			CardViewModel firstIce = new(
				"card:slice47-ice-a",
				"fixture:slice47-ice-a",
				"Host ICE",
				"ICE",
				true,
				false,
				0,
				null,
				4,
				null,
				null,
				["Barrier"],
					"A visible host ICE.");
			CardViewModel secondIce = new(
				"card:slice47-ice-b",
				"fixture:slice47-ice-b",
				"Second ICE",
				"ICE",
				true,
				true,
				0,
				null,
				5,
				null,
				null,
				["Code Gate"],
				"Another visible host ICE.");
			CardViewModel trojan = new(
				"card:slice47-trojan",
				"fixture:slice47-trojan",
				"Hosted Trojan",
				"Program",
				true,
				false,
				0,
				2,
				null,
				null,
				null,
				["Trojan"],
				"A hosted program.")
			{
				HostCardId = firstIce.Id,
			};
			CardViewModel corpIdentity = new(
				"card:slice47-corp-id",
				"fixture:slice47-corp-identity",
				"Haas-Bioroid Identity",
				"Identity",
				true,
				true,
				0,
				null,
				null,
				null,
					null,
					["Megacorp"],
				"Choose the controller of an agenda.",
				"Haas-Bioroid",
				null,
				null);
			CardViewModel runnerIdentity = new(
				"card:slice47-runner-id",
				"fixture:slice47-runner-identity",
				"Zahya Sadeghi",
				"Identity",
				true,
				true,
				0,
				null,
				null,
				null,
					null,
					["G-mod"],
				"Gain credits per access.",
				"Anarch",
				null,
				null);
			ServerViewModel server = new(
				"server:slice47",
				ServerKind.Remote,
				[firstIce, secondIce],
				Array.Empty<CardViewModel?>())
			{
				IcePositions =
				[
					new IcePositionViewModel(firstIce.Id, "server:slice47", 0),
					new IcePositionViewModel(secondIce.Id, "server:slice47", 1),
				],
			};
			AccessViewModel access = new(
				Array.Empty<CardViewModel?>(),
				0,
				null,
				Array.Empty<CardViewModel?>(),
				Array.Empty<CardViewModel?>(),
				RunAccessState.InProgress)
			{
				AccessOccurrenceCount = 3,
			};
			RunFocusViewModel run = new(
				"run:slice47",
				server.Id,
				PlayerSide.Runner,
				RunCrPhase.Success,
				RunTimingStep.BreachCandidateSelection,
				-1,
				null,
				true,
				false,
				null,
				null,
				null,
				access,
				0,
				null);
			BehaviorChoiceParameters gain = new("reality-plus-benefit", ["gain-two"]);
			BehaviorChoiceParameters draw = new("reality-plus-benefit", ["draw-two"]);
			BehaviorChoiceParameters swap = new(
				"tao-ice",
			null,
			[new CardInstanceId(firstIce.Id), new CardInstanceId(secondIce.Id)]);
			LegalActionViewModel gainAction = new(
				new ActionId("action:slice47-gain"),
				ActionKind.ChooseBehavior,
				"opaque gain description",
				gain);
			LegalActionViewModel drawAction = new(
				new ActionId("action:slice47-draw"),
				ActionKind.ChooseBehavior,
				"opaque draw description",
				draw);
			LegalActionViewModel swapAction = new(
				new ActionId("action:slice47-swap"),
				ActionKind.ChooseBehavior,
				"opaque swap description",
				swap);
			DecisionViewModel decision = new(
				"decision:slice47",
				1,
				new PlayerId("player:slice47-runner"),
				PlayerSide.Runner,
				DecisionKind.BehaviorChoice,
				"Engine-issued Slice 4.7 choices.");
			MatchViewModel view = new(
				PlayerSide.Runner,
				new PlayerViewModel("player:slice47-corp", PlayerSide.Corp, 5, 3, 0),
				new PlayerViewModel("player:slice47-runner", PlayerSide.Runner, 8, 4, 0),
				[server],
				new ZoneViewModel(ZoneKind.CorpHand, 5, Array.Empty<CardViewModel>()),
				new ZoneViewModel(ZoneKind.CorpDeck, 20, Array.Empty<CardViewModel>()),
				new ZoneViewModel(ZoneKind.RunnerGrip, 4, Array.Empty<CardViewModel>()),
				new ZoneViewModel(ZoneKind.RunnerStack, 20, Array.Empty<CardViewModel>()),
				[trojan],
				Array.Empty<CardViewModel>(),
				Array.Empty<CardViewModel>(),
				decision,
				run,
				new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null),
				[gainAction, drawAction, swapAction])
			{
				CorpIdentity = corpIdentity,
				RunnerIdentity = runnerIdentity,
			};
			Observation observation = new(
				PlayerSide.Runner,
				decision.Id,
				Array.Empty<LegalAction>(),
				new Dictionary<string, object?>(),
				new Dictionary<string, object?>())
			{
				Decision = new ObservationDecision(
					new DecisionId(decision.Id),
					decision.Revision,
					decision.Owner,
					decision.OwnerSide,
					decision.Kind,
					decision.Prompt),
			};
			EngineEvent[] events =
			[
				new EngineEvent(
					1,
					EngineEventKinds.IceSwapped,
					new IceSwappedEventPayload(
						new CardInstanceId(firstIce.Id),
						new ZoneId("zone:slice47-a"),
						0,
						new CardInstanceId(secondIce.Id),
						new ZoneId("zone:slice47-b"),
						1)),
				new EngineEvent(
					2,
					EngineEventKinds.CardDerezzed,
					new CardDerezzedEventPayload(new CardInstanceId(firstIce.Id))),
			];
			MatchSnapshot snapshot = new(1, observation, view, events, null, false);
			_board.Visible = true;
			RenderSnapshot(snapshot);
			foreach (EngineEvent @event in events)
			{
				AppendEventFeed(EventFeedRegionPresenter.Describe(@event));
			}
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			IdentityPanelState panels = IdentityPanelPresenter.Present(view);
			identities = panels.Corp.IsAvailable
				&& panels.Runner.IsAvailable
				&& _corpIdentityDetails.Text.Contains(corpIdentity.Title, StringComparison.Ordinal)
				&& _runnerIdentityDetails.Text.Contains(runnerIdentity.Title, StringComparison.Ordinal);
			Slice47DecisionViewModel projected = Slice47PresentationProjector.Present(snapshot);
			nbnChoices = projected.HasNbnChoice
				&& SceneTreeContainsText(_focusPresentation, "GAIN 2 CREDITS")
				&& SceneTreeContainsText(_focusPresentation, "DRAW 2 CARDS");
			accessCount = projected.ZahyaAccess.IsKnown
				&& SceneTreeContainsText(_focusPresentation, "ZAHYA ACCESS COUNT · 3");
			swapPreview = projected.SwapPreviews.Count >= 2
				&& SceneTreeContainsText(_focusPresentation, "ICE SWAP PREVIEW")
				&& projected.SwapPreviews.Any(preview => preview.BeforePositions.Count == 2
					&& preview.AfterPositions.Count == 2);
			hostedTrojan = SceneTreeContainsText(_servers, "HOSTED TROJANS")
				&& _cardControls.TryGetValue(trojan.Id, out List<TextCardControl>? childControls)
				&& childControls.Any(control => control.IsInsideTree());
			derezCue = _eventFeed.Text.Contains("DEREZZED", StringComparison.Ordinal);
			screenshotRequested = OS.GetCmdlineUserArgs().Contains(
				"--m4-slice47-ui-screenshot",
				StringComparer.Ordinal);
			if (screenshotRequested)
			{
				// Keep the decision state visible in the artifact.  The terminal
				// publication below proves the outcome/new-match transition but
				// should not replace the choice presentation in the screenshot.
				await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
				screenshotSaved = CaptureSlice47Screenshot();
			}
			else
			{
				screenshotSaved = true;
			}

			MatchViewModel terminalView = view with
			{
				Decision = null,
				Run = null,
				LegalActions = Array.Empty<LegalActionViewModel>(),
				Outcome = new OutcomeViewModel(
					GameOutcomeKind.Won,
					PlayerSide.Runner,
					GameWinReason.AgendaPoints),
			};
			RenderSnapshot(new MatchSnapshot(2, observation, terminalView, Array.Empty<EngineEvent>(), null, false));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			outcomeFlow = SceneTreeContainsText(_focusPresentation, "MATCH OUTCOME")
				&& SceneTreeContainsText(_focusPresentation, "New match");
			bool passed = setupChoices && identities && nbnChoices && accessCount
				&& swapPreview && hostedTrojan && derezCue && outcomeFlow && screenshotSaved;
			GD.Print(
				$"M4_SLICE47_UI {(passed ? "PASS" : "FAIL")} "
				+ $"setup_choices={FormatSlice47Bool(setupChoices)} "
				+ $"identities={FormatSlice47Bool(identities)} "
				+ $"nbn_choices={FormatSlice47Bool(nbnChoices)} "
				+ $"access_count={FormatSlice47Bool(accessCount)} "
				+ $"swap_preview={FormatSlice47Bool(swapPreview)} "
					+ $"hosted_trojan={FormatSlice47Bool(hostedTrojan)} "
					+ $"derez={FormatSlice47Bool(derezCue)} "
					+ $"outcome_new_match={FormatSlice47Bool(outcomeFlow)}");
			GD.Print(
				$"M4_SLICE47_UI_SCREENSHOT requested={FormatSlice47Bool(screenshotRequested)} "
				+ $"saved={FormatSlice47Bool(screenshotSaved)} "
				+ "path=docs/evidence/slice47/slice47-ui-smoke.png");
			GetTree().Quit(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print(
				$"M4_SLICE47_UI FAIL setup_choices={FormatSlice47Bool(setupChoices)} "
				+ $"identities={FormatSlice47Bool(identities)} "
				+ $"nbn_choices={FormatSlice47Bool(nbnChoices)} "
				+ $"access_count={FormatSlice47Bool(accessCount)} "
				+ $"swap_preview={FormatSlice47Bool(swapPreview)} "
					+ $"hosted_trojan={FormatSlice47Bool(hostedTrojan)} "
					+ $"derez={FormatSlice47Bool(derezCue)} "
					+ $"outcome_new_match={FormatSlice47Bool(outcomeFlow)} "
					+ $"screenshot={FormatSlice47Bool(screenshotSaved)} "
					+ $"error={exception.Message}");
			GetTree().Quit(1);
		}
	}

	private static bool HasPresetKinds(OptionButton button) =>
		HasPresetKind(button, "BASIC") && HasPresetKind(button, "EXPANDED");

	private static bool HasPresetKind(OptionButton button, string kind)
	{
		for (int index = 0; index < button.ItemCount; index++)
		{
			if (button.GetItemText(index).Contains(kind, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static bool PresetUnavailable(OptionButton button) =>
		button.Disabled
		&& Enumerable.Range(0, button.ItemCount)
			.Any(index => button.GetItemText(index).Contains("unavailable", StringComparison.OrdinalIgnoreCase));

	private bool CaptureSlice47Screenshot()
	{
		try
		{
			Image? image = GetViewport().GetTexture()?.GetImage();
			if (image is null)
			{
				return false;
			}

			string path = ProjectSettings.GlobalizePath(
				"res://../../docs/evidence/slice47/slice47-ui-smoke.png");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			Error result = image.SavePng(path);
			image.Dispose();
			return result == Error.Ok;
		}
		catch (Exception exception)
		{
			GD.PushWarning($"Slice 4.7 screenshot capture failed: {exception.Message}");
			return false;
		}
	}

	private static string FormatSlice47Bool(bool value) => value ? "true" : "false";
}
