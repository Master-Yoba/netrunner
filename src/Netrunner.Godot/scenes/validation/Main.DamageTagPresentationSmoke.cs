using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Detached Slice 4.6 UI proof. It feeds typed damage, tags, calculation,
/// and unless-choice values through the same Match presenters used by the
/// live board. No authoritative Core state is created or mutated here.
/// </summary>
public partial class Main
{
	private async Task RunSlice46UiSmokeAsync()
	{
		bool damagePresentation = false;
		bool tagChip = false;
		bool tagFeed = false;
		bool unlessChoices = false;
		bool conditionalValue = false;
		bool noEarlyJackOut = false;
		bool jackOutAfterDamage = false;
		bool screenshotRequested = false;
		bool screenshotSaved = false;
		try
		{
			_board.Visible = true;
			Slice46EnginePublication publication = BuildSlice46EnginePublication();
			BehaviorCatalog presentationCatalog = GatewayBehaviorCatalog.Create();
			BehaviorChoiceParameters payParameters = new(
				"public-trail-tag-or-pay",
				["pay-eight"],
				finalizeSelection: true);
			BehaviorChoiceParameters takeTagParameters = new(
				"public-trail-tag-or-pay",
				["take-tag"],
				finalizeSelection: true);
			LegalActionViewModel payCredits = new(
				new ActionId("action:slice46-public-trail-pay"),
				ActionKind.ChooseBehavior,
				"description is not a presentation source",
				payParameters)
			{
				BehaviorChoice = EncounterChoicePresenter.ResolveChoiceMetadata(
					payParameters,
					presentationCatalog),
			};
			LegalActionViewModel takeTag = new(
				new ActionId("action:slice46-public-trail-take-tag"),
				ActionKind.ChooseBehavior,
				"description is not a fallback source",
				takeTagParameters)
			{
				BehaviorChoice = EncounterChoicePresenter.ResolveChoiceMetadata(
					takeTagParameters,
					presentationCatalog),
			};
			DamagePresentationViewModel damage =
				DamagePresentationViewModel.FromEvent(publication.Damage);
			// The engine publication is an InterruptChoice while damage is
			// imminent. This detached decision overlay keeps the smoke focused on
			// the reusable unless renderer; the damage, tags, and calculation
			// values still come from the engine observation above.
			MatchViewModel beforeView = publication.BeforeView with
			{
				Decision = new DecisionViewModel(
					"decision:slice46-unless",
					publication.BeforeObservation.Decision?.Revision ?? 1,
					new PlayerId("player:runner"),
					PlayerSide.Runner,
					DecisionKind.BehaviorChoice,
					"Choose whether to pay 8 credits or take 1 tag."),
				PendingDamage = [damage],
				LegalActions = [payCredits, takeTag],
			};
			MatchSnapshot beforeDamage = CreateSlice46Snapshot(
				beforeView,
				publication.BeforeObservation,
				[]);
			RenderSnapshot(beforeDamage);
			AppendEventFeed(EventFeedRegionPresenter.Describe(new EngineEvent(
				1,
				EngineEventKinds.TagsAdjusted,
				new TagsAdjustedEventPayload(
					new PlayerId("player:runner"),
					1,
					2))));
			foreach (EngineEvent @event in publication.Events
				.Where(static value => value.Payload is DamageDealtEventPayload))
			{
				AppendEventFeed(EventFeedRegionPresenter.Describe(@event));
			}
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			screenshotRequested = OS.GetCmdlineUserArgs().Contains(
				"--m4-slice46-ui-screenshot",
				StringComparer.Ordinal);

			damagePresentation = _damageStatus.Text.Contains("DAMAGE · NET · 2", StringComparison.Ordinal)
				&& _damageStatus.Text.Contains("SOURCE card:1", StringComparison.Ordinal)
				&& _damageStatus.Text.Contains("NO FLATLINE", StringComparison.Ordinal);
			tagChip = _runnerSummary.Text.Contains("TAGS · 2 · TAGGED", StringComparison.Ordinal);
			tagFeed = _eventFeed.Text.Contains("TAG GAIN", StringComparison.Ordinal)
				&& _eventFeed.Text.Contains("DAMAGE · NET · 2", StringComparison.Ordinal);
			unlessChoices = SceneTreeContainsText(_actions, "UNLESS PAY")
				&& SceneTreeContainsText(_actions, "UNLESS FALLBACK");
			conditionalValue = SceneTreeContainsText(
				_focusPresentation,
				"CARD:2 · DAMAGE · 2 → 3")
				&& SceneTreeContainsText(
					_focusPresentation,
					"hosted advancement counters");
			noEarlyJackOut = !SceneTreeContainsText(
				_focusPresentation,
				"JACK-OUT CHOICE ISSUED");

			MatchViewModel afterDamageView = publication.View;
			MatchSnapshot afterDamage = CreateSlice46Snapshot(
				afterDamageView,
				publication.Observation,
				[
					publication.DamageEvent,
				]);
			RenderSnapshot(afterDamage);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			jackOutAfterDamage = SceneTreeContainsText(
				_focusPresentation,
				"KARUNĀ DAMAGE RESOLVED · JACK-OUT CHOICE ISSUED");
			if (screenshotRequested)
			{
				await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
				screenshotSaved = CaptureSlice46Screenshot();
			}
			else
			{
				screenshotSaved = true;
			}

			bool passed = damagePresentation
				&& tagChip
				&& tagFeed
				&& unlessChoices
				&& conditionalValue
				&& noEarlyJackOut
				&& jackOutAfterDamage
				&& screenshotSaved;
			GD.Print(
				$"M4_SLICE46_UI {(passed ? "PASS" : "FAIL")} "
				+ $"damage={FormatSlice46Bool(damagePresentation)} "
				+ $"tag_chip={FormatSlice46Bool(tagChip)} "
				+ $"tag_feed={FormatSlice46Bool(tagFeed)} "
				+ $"unless={FormatSlice46Bool(unlessChoices)} "
				+ $"conditional_value={FormatSlice46Bool(conditionalValue)} "
				+ $"no_early_jack_out={FormatSlice46Bool(noEarlyJackOut)} "
				+ $"jack_out_after_damage={FormatSlice46Bool(jackOutAfterDamage)}");
			GD.Print(
				$"M4_SLICE46_UI_SCREENSHOT requested={FormatSlice46Bool(screenshotRequested)} "
				+ $"saved={FormatSlice46Bool(screenshotSaved)} "
				+ "path=docs/evidence/slice46/slice46-ui-smoke.png");
			QuitCleanly(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			string error = exception.Message.Replace('\n', ' ').Replace('\r', ' ');
			GD.Print(
				$"M4_SLICE46_UI FAIL damage={FormatSlice46Bool(damagePresentation)} "
				+ $"tag_chip={FormatSlice46Bool(tagChip)} "
				+ $"tag_feed={FormatSlice46Bool(tagFeed)} "
				+ $"unless={FormatSlice46Bool(unlessChoices)} "
				+ $"conditional_value={FormatSlice46Bool(conditionalValue)} "
				+ $"no_early_jack_out={FormatSlice46Bool(noEarlyJackOut)} "
				+ $"jack_out_after_damage={FormatSlice46Bool(jackOutAfterDamage)} "
				+ $"screenshot={FormatSlice46Bool(screenshotSaved)} "
				+ $"error={error}");
			QuitCleanly(1);
		}
	}

	private void RenderSlice46DamageStatus(MatchSnapshot snapshot)
	{
		DamageDealtEventPayload[] damageEvents = snapshot.Events
			.Select(static @event => @event.Payload as DamageDealtEventPayload)
			.Where(static value => value is not null)
			.Select(static value => value!)
			.ToArray();
		if (damageEvents.Length > 0)
		{
			_damageStatus.Text = string.Join(
				'\n',
				damageEvents.Select(UnlessChoicePresenter.DescribeDamage));
			_damageStatus.AddThemeColorOverride(
				"font_color",
				damageEvents.Any(static value => value.Flatlined) ? Warning : Muted);
			return;
		}

		IReadOnlyList<DamagePresentationViewModel> damage = snapshot.View.PendingDamage;
		if (damage.Count == 0)
		{
			_damageStatus.Text = "DAMAGE · none observed";
			_damageStatus.AddThemeColorOverride("font_color", Muted);
			return;
		}

		_damageStatus.Text = string.Join('\n', damage.Select(static value => value.Summary));
		_damageStatus.AddThemeColorOverride(
			"font_color",
			damage.Any(static value => value.IsFlatlineRisk) ? Warning : Muted);
	}

	private static Slice46EnginePublication BuildSlice46EnginePublication()
	{
		BehaviorCatalog composition = GatewayBehaviorCatalog.Create();
		M2ScenarioDocument draft = CreateSlice46Scenario();
		BehaviorCatalog scenarioCatalog = draft.CreateBehaviorCatalog(composition);
		M2ScenarioDocument document = CreateSlice46Scenario(scenarioCatalog.Fingerprint);
		GameState state = M2ScenarioSetup.Build(document, composition);
		state = WithSlice46Rules(state, composition);
		ServerId server = state.Servers.Single(value => value.Kind == ServerKind.Remote).Id;
		GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
			state,
			server,
			composition);
		var events = new List<EngineEvent>(started.Events);
		GameState current = started.State;

		for (int step = 0; step < 32 && current.PendingDecision?.Kind != DecisionKind.InterruptChoice; step++)
		{
			PendingDecision pending = current.PendingDecision
				?? throw new InvalidOperationException("Slice 4.6 engine smoke stalled without a pending decision.");
			LegalAction pass = pending.LegalActions.FirstOrDefault(
				static action => action.Kind == ActionKind.PassPriority)
				?? throw new InvalidOperationException(
					$"Slice 4.6 engine smoke cannot pass decision '{pending.Id}'.");
			ApplyResult applied = GameEngine.Apply(
				current,
				new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pass.Id),
				composition);
			if (!applied.Accepted)
			{
				throw new InvalidOperationException(
					$"Slice 4.6 engine smoke rejected pass: {applied.Rejection?.Message}");
			}

			events.AddRange(applied.Events);
			current = applied.State;
		}

		if (current.PendingDecision?.Kind != DecisionKind.InterruptChoice)
		{
			throw new InvalidOperationException(
				"Slice 4.6 engine smoke did not reach the imminent damage observation.");
		}

		Observation beforeObservation = ObservationBuilder.Build(
			current,
			PlayerSide.Runner,
			composition);
		MatchViewModel beforeView = ObservationViewMapper.Build(
			beforeObservation,
			CardMetadataResolvers.Default,
			composition);
		PendingDecision interrupt = current.PendingDecision;
		LegalAction decline = interrupt.LegalActions.FirstOrDefault(
			static action => action.Kind == ActionKind.DeclinePrevention)
			?? throw new InvalidOperationException(
				"Slice 4.6 engine smoke did not issue a decline-prevention action.");
		ApplyResult declined = GameEngine.Apply(
			current,
			new ActionSubmission(interrupt.Owner, interrupt.Id, interrupt.Revision, decline.Id),
			composition);
		if (!declined.Accepted)
		{
			throw new InvalidOperationException(
				$"Slice 4.6 engine smoke rejected damage decline: {declined.Rejection?.Message}");
		}

		events.AddRange(declined.Events);
		current = declined.State;
		for (int step = 0; step < 32 && current.PendingDecision?.Kind != DecisionKind.IceSubroutineChoice; step++)
		{
			PendingDecision pending = current.PendingDecision
				?? throw new InvalidOperationException("Slice 4.6 engine smoke stalled after damage.");
			LegalAction pass = pending.LegalActions.FirstOrDefault(
				static action => action.Kind == ActionKind.PassPriority)
				?? throw new InvalidOperationException(
					$"Slice 4.6 engine smoke cannot finish decision '{pending.Id}'.");
			ApplyResult applied = GameEngine.Apply(
				current,
				new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pass.Id),
				composition);
			if (!applied.Accepted)
			{
				throw new InvalidOperationException(
					$"Slice 4.6 engine smoke rejected post-damage pass: {applied.Rejection?.Message}");
			}

			events.AddRange(applied.Events);
			current = applied.State;
		}

		if (current.PendingDecision?.Kind != DecisionKind.IceSubroutineChoice)
		{
			throw new InvalidOperationException(
				"Slice 4.6 engine smoke did not reach Karunā's post-damage choice.");
		}

		EngineEvent damageEvent = events.LastOrDefault(
			static value => value.Payload is DamageDealtEventPayload)
			?? throw new InvalidOperationException(
				"Slice 4.6 engine smoke did not publish a damage event.");
		DamageDealtEventPayload damage = (DamageDealtEventPayload)damageEvent.Payload;
		Observation observation = ObservationBuilder.Build(current, PlayerSide.Runner, composition);
		MatchViewModel view = ObservationViewMapper.Build(
			observation,
			CardMetadataResolvers.Default,
			composition);
		return new Slice46EnginePublication(
			beforeObservation,
			beforeView,
			observation,
			view,
			events,
			damage,
			damageEvent);
	}

	private static M2ScenarioDocument CreateSlice46Scenario(string? fingerprint = null) =>
		new(
			schemaVersion: EngineCompatibility.ArtifactSchemaVersion,
			scenarioId: "m4:slice46-ui-engine",
			seed: 4606,
			rulesVersion: EngineCompatibility.RulesVersion,
			cardDataVersion: EngineCompatibility.GatewayCardDataVersion,
			turn: new M2ScenarioTurnSpec(PlayerSide.Runner, TurnPhase.Runner, turnNumber: 2),
			remotes: 1,
			players:
			[
				new M2ScenarioPlayerSpec(PlayerSide.Corp, credits: 10, clicks: 3),
				new M2ScenarioPlayerSpec(PlayerSide.Runner, credits: 15, clicks: 4),
			],
			cards:
			[
				new M2ScenarioCardSpec(
					"card:1",
					GatewayBatch5CardCodes.Karuna.Value,
					ZoneKind.ServerIce,
					PlayerSide.Corp,
					server: "remote:0",
					faceUp: true,
					rezzed: true),
				new M2ScenarioCardSpec(
					"card:2",
					GatewayBatch5CardCodes.UrticaCipher.Value,
					ZoneKind.ServerRoot,
					PlayerSide.Corp,
					server: "remote:0",
					faceUp: true,
					rezzed: true,
					advancementCounters: 1),
				new M2ScenarioCardSpec(
					"card:3",
					GatewayBatch2CardCodes.Carmen.Value,
					ZoneKind.RunnerRig,
					PlayerSide.Runner,
					faceUp: true),
				new M2ScenarioCardSpec(
					"card:4",
					GatewayBatch2CardCodes.Carmen.Value,
					ZoneKind.RunnerGrip,
					PlayerSide.Runner),
				new M2ScenarioCardSpec(
					"card:5",
					GatewayBatch2CardCodes.Carmen.Value,
					ZoneKind.RunnerGrip,
					PlayerSide.Runner),
			],
			citations: ["CR 26.03 6.5, 9.8, and 9.2.8: damage resolves before Karunā's jack-out choice."],
			crossCheckNote: "The native smoke drives the real Karunā encounter through GameEngine.Apply.",
			behaviorRegistryVersion: EngineCompatibility.GatewayBehaviorRegistryVersion,
			behaviorCatalogFingerprint: fingerprint);

	private static GameState WithSlice46Rules(GameState state, BehaviorCatalog composition)
	{
		RulesState defaults = RulesState.CreateDefault(state.Players);
		PlayerId runnerId = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
		var damageObservationInterrupt = new InterruptRegistration(
			new InterruptId("fixture:slice46-observe-damage"),
			runnerId,
			InterruptKind.Prevention,
			mandatory: false,
			new TriggerCondition(OccurrenceKind.Damage),
			"fixture.slice46.observe-damage",
			sourceCardId: null,
			new CostBundle(),
			maximumPreventionAmount: 2,
			new EffectBundle());
		PlayerRulesState[] players = defaults.Players
			.Select(player => player.PlayerId == runnerId
				? new PlayerRulesState(
					player.PlayerId,
					player.BaseHandSize,
					coreDamage: 1,
					tags: 2,
					player.BaseMemory,
					player.UsedMemory)
				: player)
			.ToArray();
		RulesState rules = new(
			defaults.Version,
			players,
			defaults.Programs,
			defaults.Counters,
			defaults.LingeringEffects,
			defaults.Triggers,
			[damageObservationInterrupt]);
		return new GameState(
			state.GameId,
			state.RulesVersion,
			state.CardDataVersion,
			players: state.Players,
			cards: state.Cards,
			zones: state.Zones,
			servers: state.Servers,
			turn: state.Turn,
			outcome: state.Outcome,
			randomState: state.RandomState,
			allocator: state.Allocator,
			schemaVersion: state.SchemaVersion,
			decisionRevision: state.DecisionRevision,
			eventSequence: state.EventSequence,
			resolutionStack: state.ResolutionStack,
			pendingDecision: state.PendingDecision,
			actionSequence: state.ActionSequence,
			initialSeed: state.InitialSeed,
			runState: state.RunState,
			rulesState: rules,
			behaviorRegistryVersion: composition.Version,
			behaviorCatalogFingerprint: composition.Fingerprint,
			runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion,
			runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);
	}

	private sealed record Slice46EnginePublication(
		Observation BeforeObservation,
		MatchViewModel BeforeView,
		Observation Observation,
		MatchViewModel View,
		IReadOnlyList<EngineEvent> Events,
		DamageDealtEventPayload Damage,
		EngineEvent DamageEvent);

	private static MatchSnapshot CreateSlice46Snapshot(
		MatchViewModel view,
		Observation observation,
		IReadOnlyList<EngineEvent> events) => new(
		Sequence: 1,
		Observation: observation,
		View: view,
		Events: events,
		Feedback: null,
		IsOpponentThinking: false,
		RunOutcome: null);

	private bool CaptureSlice46Screenshot()
	{
		try
		{
			Image? image = GetViewport().GetTexture()?.GetImage();
			if (image is null)
			{
				return false;
			}

			string path = ProjectSettings.GlobalizePath(
				"res://../../docs/evidence/slice46/slice46-ui-smoke.png");
			Error result = image.SavePng(path);
			image.Dispose();
			return result == Error.Ok;
		}
		catch (Exception exception)
		{
			GD.PushWarning($"Slice 4.6 screenshot capture failed: {exception.Message}");
			return false;
		}
	}

	private static string FormatSlice46Bool(bool value) => value ? "true" : "false";
}
