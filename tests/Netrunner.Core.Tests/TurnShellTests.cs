using System.Text.Json;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class TurnShellTests
{
    [Fact]
    public void BeginTurnAllotsCorpClicksAndOpensCorpPaidRezScoreWindow()
    {
        GameEngine.TurnStartResult start = GameEngine.BeginTurnWithEvents(CreateState(corpDeck: 2), PlayerSide.Corp);
        GameState state = start.State;

        Assert.Equal(TurnStep.CorpDraw, state.Turn.Step);
        Assert.Equal(3, state.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.BeforeTurnPriority, Assert.IsType<TurnFrame>(state.ResolutionStack.Single()).Stage);
        Assert.Equal(TurnPriorityWindowKind.PaidRezAndScore, Assert.IsType<TurnFrame>(state.ResolutionStack.Single()).PriorityWindow);
        Assert.Single(state.PendingDecision.LegalActions);
        Assert.Contains(state.PendingDecision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        Assert.NotNull(state.Turn.History?.CorpThisTurn);
        Assert.False(state.Turn.ThisTurnFacts!.TurnBegan);
        Assert.Contains(start.Events, @event => @event.Kind == EngineEventKinds.TurnClicksAllotted);
        Assert.Contains(start.Events, @event => @event.Kind == EngineEventKinds.TurnTimingWindowOpened);
        Assert.All(start.Events, @event => Assert.NotNull(@event.Payload));
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(RoundTrip(state)));
        Assert.Equal(EngineJson.Serialize(state), EngineJson.Serialize(RoundTrip(state)));
    }

    [Fact]
    public void CorpFormalBeginPerformsMandatoryDrawAndNeverOffersEndTurn()
    {
        GameState state = GameEngine.BeginTurn(CreateState(corpDeck: 2), PlayerSide.Corp);
        state = Pass(state);
        state = Pass(state);

        Assert.Equal(TurnStep.CorpAction, state.Turn.Step);
        Assert.True(state.Turn.ThisTurnFacts!.TurnBegan);
        Assert.True(state.Turn.ThisTurnFacts.MandatoryDrawPerformed);
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);
        Assert.Contains(state.PendingDecision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        Assert.DoesNotContain(state.PendingDecision.LegalActions, action => action.Kind == ActionKind.EndTurn);
        Assert.Single(state.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Single(state.GetZone(ZoneKind.CorpDeck).CardIds);

        state = Pass(state);
        state = Pass(state);
        Assert.Equal(DecisionKind.TurnAction, state.PendingDecision!.Kind);
        Assert.DoesNotContain(state.PendingDecision.LegalActions, action => action.Kind == ActionKind.EndTurn);

        PendingDecision pending = state.PendingDecision;
        LegalAction endTurn = new(
            new ActionId("action:forged-end-turn"),
            ActionKind.EndTurn,
            "End turn",
            new NoActionParameters());
        GameState tampered = ReplacePending(state, pending.LegalActions.Append(endTurn).ToArray());
        Assert.Contains(
            StateInvariantValidator.Collect(tampered),
            violation => violation.Code is "decision.illegal-end-turn" or "decision.extra-turn-action");
        ApplyResult rejected = GameEngine.Apply(
            tampered,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, endTurn.Id));
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
    }

    [Fact]
    public void RunnerGetsFourClicksWithoutAStartDrawAfterCorpBoundary()
    {
        GameState state = GameEngine.BeginTurn(CreateState(corpDeck: 2, runnerDeck: 2), PlayerSide.Corp);
        state = Pass(state);
        state = Pass(state); // Corp formal begin, mandatory draw, Corp action priority.
        for (int click = 0; click < 3; click++)
        {
            state = Pass(state);
            state = Pass(state); // Close action priority.
            state = ApplyKind(state, ActionKind.GainCredit);
        }

        state = Pass(state);
        state = Pass(state); // Action phase ends at zero clicks; discard is empty.
        state = Pass(state);
        state = Pass(state); // Formal Corp end; Runner allotment and pre-turn window.

        Assert.Equal(PlayerSide.Runner, state.Turn.ActiveSide);
        Assert.Equal(TurnStep.RunnerAction, state.Turn.Step);
        Assert.Equal(4, state.Players.Single(player => player.Side == PlayerSide.Runner).Clicks);
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);

        state = Pass(state);
        state = Pass(state); // Runner formal begin and Runner action priority.
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);
        Assert.True(state.Turn.ThisTurnFacts!.TurnBegan);
        Assert.False(state.Turn.ThisTurnFacts.MandatoryDrawPerformed);
        Assert.DoesNotContain(state.PendingDecision.LegalActions, action => action.Kind == ActionKind.EndTurn);
    }

    [Fact]
    public void ForcedActionBoundaryAllowsLosingClicksAboveBaselineAfterGrant()
    {
        GameState state = CreateState(corpHand: 6, corpDeck: 1, corpClicks: 5);
        var facts = new TurnFactsState(
            1,
            PlayerSide.Corp,
            1,
            clicksAllotted: 3,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: true,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        state = ReplaceTurn(
            state,
            new TurnState(
                PlayerSide.Corp,
                TurnPhase.Corp,
                1,
                TurnStep.CorpAction,
                new TurnHistoryState(1, facts, null, null, null)));

        state = GameEngine.BeginResolution(
            state,
            new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                PlayerSide.Corp,
                1,
                TurnFrameStage.ActionPhaseEnd,
                forcedActionEnd: true));

        Assert.Equal(DecisionKind.TurnDiscard, state.PendingDecision!.Kind);
        Assert.Equal(1, Assert.IsType<TurnDiscardFrame>(state.ResolutionStack[^1]).RequiredCount);
        state = ApplyKind(state, ActionKind.ChooseCard);
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);
        state = Pass(state);
        state = Pass(state);

        Assert.Equal(0, state.Players.Single(player => player.Side == PlayerSide.Corp).Clicks);
        Assert.Equal(5, state.Turn.History!.CorpThisTurn!.ClicksLost);
        Assert.Contains(state.GetZone(ZoneKind.CorpDiscard).CardIds, state.Cards.Select(card => card.Id).Contains);
        Assert.Empty(state.Turn.History.CorpThisTurn.TrashedCardOccurrenceIds);
    }

    [Fact]
    public void ActionPhaseCannotBeEndedWithClicksWithoutAForcedBoundary()
    {
        GameState state = CreateState(corpClicks: 1);
        var facts = new TurnFactsState(1, PlayerSide.Corp, 1, 3, 0, true, true, false, false);
        state = ReplaceTurn(
            state,
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, 1, TurnStep.CorpAction,
                new TurnHistoryState(1, facts, null, null, null)));

        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginResolution(
            state,
            new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                PlayerSide.Corp,
                1,
                TurnFrameStage.ActionPhaseEnd)));
    }

    [Fact]
    public void TurnObservationAddsFactsWithoutExposingOpponentHand()
    {
        GameState state = GameEngine.BeginTurn(CreateState(corpHand: 2, corpDeck: 2), PlayerSide.Corp);
        CardInstanceId hiddenCard = state.GetZone(ZoneKind.CorpHand).CardIds[0];
        string runnerJson = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner));

        Assert.Contains("turnFacts", runnerJson, StringComparison.Ordinal);
        Assert.Contains("corpDraw", runnerJson, StringComparison.Ordinal);
        Assert.DoesNotContain(hiddenCard.Value, runnerJson, StringComparison.Ordinal);
        Assert.DoesNotContain("resolutionStack", runnerJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Scn400FixturePinsCrTimingCitationsAndEng401Scope()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scenarios", "scn-400-turn.json");
        TurnScenarioArtifact fixture = EngineJson.Deserialize<TurnScenarioArtifact>(File.ReadAllText(path));
        Assert.Equal("SCN-400", fixture.ScenarioId);
        Assert.Empty(fixture.CollectViolations());
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("SCN-400", root.GetProperty("scenarioId").GetString());
        Assert.Equal("ENG-401", root.GetProperty("scope").GetString());
        Assert.Equal(3, root.GetProperty("engineStateSchemaVersion").GetInt32());
        Assert.Contains("CR 26.03 5.6.1", root.GetProperty("citations").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("turn-timing-window-opened", root.GetProperty("requiredEvents").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("Jinteki.net", root.GetProperty("crossCheckNote").GetString(), StringComparison.Ordinal);
        Assert.Contains(root.GetProperty("excluded").EnumerateArray().Select(value => value.GetString()), value => value!.Contains("ENG-402", StringComparison.Ordinal));
    }

    [Fact]
    public void Scn400TraceIsEngineDrivenAndEveryYieldRoundTrips()
    {
        List<EngineEvent> events = CollectScn400Events();

        TurnTimingTrace trace = TurnTimingTraceValidator.Validate(events);
        Assert.True(trace.IsValid, string.Join("; ", trace.Problems));
        Assert.Contains(trace.Entries, entry => entry.Citation.Contains("5.6.1d", StringComparison.Ordinal));
        Assert.Contains(trace.Entries, entry => entry.Citation.Contains("5.7.1g", StringComparison.Ordinal));
        Assert.Contains(events, @event => @event.Kind == EngineEventKinds.CardDrawn);
        Assert.Contains(events, @event =>
            @event.Payload is TurnStartedEventPayload { ActiveSide: PlayerSide.Runner });
        Assert.DoesNotContain(
            events,
            @event => @event.Payload is CardDrawnEventPayload { Side: PlayerSide.Runner }
                && events.Take(events.IndexOf(@event)).Any(previous =>
                    previous.Payload is TurnStartedEventPayload { ActiveSide: PlayerSide.Runner }));
    }

    [Fact]
    public void TurnTimingTraceRejectsMissingPhaseBoundary()
    {
        List<EngineEvent> events = CollectScn400Events();
        events.Remove(events.First(@event =>
            @event.Payload is TurnPhaseEndedEventPayload { Step: TurnStep.CorpDraw }));

        TurnTimingTrace trace = TurnTimingTraceValidator.Validate(events);
        Assert.False(trace.IsValid);
        Assert.Contains(trace.Problems, problem => problem.Contains("action timing window", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TurnTimingTraceRejectsRunnerMandatoryDraw()
    {
        List<EngineEvent> events = CollectScn400Events();
        int drawIndex = events.FindIndex(@event =>
            @event.Payload is CardDrawnEventPayload { Side: PlayerSide.Corp });
        CardDrawnEventPayload draw = Assert.IsType<CardDrawnEventPayload>(events[drawIndex].Payload);
        events[drawIndex] = new EngineEvent(
            events[drawIndex].Version,
            events[drawIndex].Sequence,
            events[drawIndex].Kind,
            new CardDrawnEventPayload(
                draw.Version,
                PlayerSide.Runner,
                draw.CardId,
                draw.FromZoneId,
                draw.ToZoneId));

        TurnTimingTrace trace = TurnTimingTraceValidator.Validate(events);
        Assert.False(trace.IsValid);
        Assert.Contains(trace.Problems, problem => problem.Contains("mandatory draw", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FormalTurnBeginUsesSerializableCheckpointOccurrence()
    {
        GameState seed = CreateState(corpDeck: 2);
        PlayerId corp = seed.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState state = WithRules(
            seed,
            [
                new TriggerRegistration(
                    new TriggerId("trigger:turn-begin"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.TurnBegin),
                    "fixture.turn-begin",
                    null,
                    new CostBundle(),
                    new EffectBundle()),
            ]);

        state = GameEngine.BeginTurn(state, PlayerSide.Corp);
        state = Pass(state);
        state = Pass(state);

        Assert.Equal(DecisionKind.TriggerChoice, state.PendingDecision!.Kind);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(state.ResolutionStack[^1]);
        Assert.Equal(OccurrenceKind.TurnBegin, checkpoint.Occurrence.Kind);
        GameState restored = RoundTrip(state);
        EffectCheckpointFrame restoredCheckpoint = Assert.IsType<EffectCheckpointFrame>(restored.ResolutionStack[^1]);
        Assert.Equal(OccurrenceKind.TurnBegin, restoredCheckpoint.Occurrence.Kind);
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void FormalTurnEndUsesSerializableCheckpointOccurrenceBeforeNextTurn()
    {
        GameState seed = CreateState(corpDeck: 2);
        PlayerId corp = seed.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState state = WithRules(
            seed,
            [
                new TriggerRegistration(
                    new TriggerId("trigger:turn-end"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.TurnEnd),
                    "fixture.turn-end",
                    null,
                    new CostBundle(),
                    new EffectBundle()),
            ]);

        state = GameEngine.BeginTurn(state, PlayerSide.Corp);
        state = Pass(state);
        state = Pass(state);
        for (int click = 0; click < 3; click++)
        {
            state = Pass(state);
            state = Pass(state);
            state = ApplyKind(state, ActionKind.GainCredit);
        }

        state = Pass(state);
        state = Pass(state);
        state = Pass(state);
        state = Pass(state);

        Assert.Equal(DecisionKind.TriggerChoice, state.PendingDecision!.Kind);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(state.ResolutionStack[^1]);
        Assert.Equal(OccurrenceKind.TurnEnd, checkpoint.Occurrence.Kind);
        Assert.Equal(PlayerSide.Runner, state.Turn.ActiveSide);
        Assert.Equal(TurnFrameStage.GainAllottedClicks, Assert.IsType<TurnFrame>(state.ResolutionStack[^3]).Stage);
    }

    [Fact]
    public void EffectWildcardTriggersDoNotMatchTurnBoundaryOccurrences()
    {
        GameState seed = CreateState(corpDeck: 2);
        PlayerId corp = seed.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState state = WithRules(
            seed,
            [
                new TriggerRegistration(
                    new TriggerId("trigger:effect-imminent"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.EffectImminent),
                    "fixture.effect-imminent",
                    null,
                    new CostBundle(),
                    new EffectBundle()),
                new TriggerRegistration(
                    new TriggerId("trigger:effect-resolved"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.EffectResolved),
                    "fixture.effect-resolved",
                    null,
                    new CostBundle(),
                    new EffectBundle()),
                new TriggerRegistration(
                    new TriggerId("trigger:damage"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.damage",
                    null,
                    new CostBundle(),
                    new EffectBundle()),
            ]);

        state = GameEngine.BeginTurn(state, PlayerSide.Corp);
        state = Pass(state);
        state = Pass(state);

        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(state.ResolutionStack[^1]).Stage);
    }

    [Fact]
    public void FreeStandingTurnBoundaryResolutionIsRejected()
    {
        GameState state = CreateState(corpDeck: 1);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        Assert.Throws<ResolutionFailureException>(() => GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new TurnBoundaryEffect(OccurrenceKind.TurnBegin, corp, 1))));
    }

    [Fact]
    public void TurnTimingTraceRejectsEndWithoutFormalBegin()
    {
        var events = new List<EngineEvent>
        {
            new(1, EngineEventKinds.TurnEnded, new TurnEndedEventPayload(PlayerSide.Runner, 1)),
        };

        TurnTimingTrace trace = TurnTimingTraceValidator.Validate(events);
        Assert.False(trace.IsValid);
        Assert.Contains(trace.Problems, problem => problem.Contains("formal turn end", StringComparison.Ordinal));
    }

    private static List<EngineEvent> CollectScn400Events()
    {
        GameEngine.TurnStartResult start = GameEngine.BeginTurnWithEvents(
            CreateState(corpDeck: 8, runnerDeck: 8),
            PlayerSide.Corp);
        GameState state = RoundTrip(start.State);
        Assert.Equal(DeterministicHash.ForState(start.State), DeterministicHash.ForState(state));
        var events = new List<EngineEvent>(start.Events);
        int runnerTurnsEnded = 0;

        while (runnerTurnsEnded == 0)
        {
            PendingDecision pending = state.PendingDecision
                ?? throw new InvalidOperationException("The SCN-400 engine trace must yield a decision until the Runner turn ends.");
            LegalAction action = pending.Kind switch
            {
                DecisionKind.TurnTiming => pending.LegalActions.Single(candidate => candidate.Kind == ActionKind.PassPriority),
                DecisionKind.TurnAction => pending.LegalActions.Single(candidate => candidate.Kind == ActionKind.GainCredit),
                DecisionKind.TurnDiscard => pending.LegalActions.First(candidate => candidate.Kind == ActionKind.ChooseCard),
                _ => throw new InvalidOperationException($"Unexpected SCN-400 turn decision '{pending.Kind}'."),
            };

            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id));
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            GameState next = RoundTrip(result.State);
            Assert.Equal(DeterministicHash.ForState(result.State), DeterministicHash.ForState(next));
            Assert.Equal(EngineJson.Serialize(result.State), EngineJson.Serialize(next));
            state = next;
            runnerTurnsEnded = events.Count(@event =>
                @event.Payload is TurnEndedEventPayload { Side: PlayerSide.Runner });
        }

        return events;
    }

    private static GameState CreateState(
        int corpHand = 0,
        int corpDeck = 2,
        int runnerDeck = 2,
        int corpClicks = 3)
    {
        GameState seed = GameState.CreateNew(400);
        var allocator = new StableIdAllocator(seed.Allocator);
        var cards = new List<CardInstanceState>();
        var corpHandIds = new List<CardInstanceId>();
        var corpDeckIds = new List<CardInstanceId>();
        var runnerStackIds = new List<CardInstanceId>();
        ZoneId corpHandZone = seed.GetZone(ZoneKind.CorpHand).Id;
        ZoneId corpDeckZone = seed.GetZone(ZoneKind.CorpDeck).Id;
        ZoneId runnerStackZone = seed.GetZone(ZoneKind.RunnerStack).Id;

        AddCards(corpHand, PlayerSide.Corp, corpHandZone, corpHandIds, "definition:turn-corp-hand");
        AddCards(corpDeck, PlayerSide.Corp, corpDeckZone, corpDeckIds, "definition:turn-corp-deck");
        AddCards(runnerDeck, PlayerSide.Runner, runnerStackZone, runnerStackIds, "definition:turn-runner-stack");

        var zones = seed.Zones.Select(zone => zone.Kind switch
        {
            ZoneKind.CorpHand => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, corpHandIds),
            ZoneKind.CorpDeck => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, corpDeckIds),
            ZoneKind.RunnerStack => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, runnerStackIds),
            _ => zone,
        }).ToArray();
        PlayerState corp = seed.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = seed.Players.Single(player => player.Side == PlayerSide.Runner);
        var players = new[]
        {
            new PlayerState(corp.Id, corp.Side, corp.Credits, corpClicks, corp.AgendaPoints),
            runner,
        };
        return new GameState(
            seed.SchemaVersion,
            seed.GameId,
            seed.RulesVersion,
            seed.CardDataVersion,
            players,
            cards,
            zones,
            seed.Servers,
            seed.Turn,
            seed.Outcome,
            seed.RandomState,
            allocator.State,
            seed.DecisionRevision,
            seed.EventSequence,
            [],
            null,
            seed.ActionSequence,
            seed.InitialSeed);

        void AddCards(
            int count,
            PlayerSide side,
            ZoneId zone,
            List<CardInstanceId> ids,
            string definition)
        {
            for (int index = 0; index < count; index++)
            {
                CardInstanceId id = allocator.NextCardInstanceId();
                ids.Add(id);
                cards.Add(new CardInstanceState(id, new CardDefinitionId(definition), zone, side));
            }
        }
    }

    private static GameState WithRules(
        GameState state,
        IReadOnlyList<TriggerRegistration> triggers)
    {
        RulesState defaults = RulesState.CreateDefault(state.Players);
        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            new RulesState(
                defaults.Version,
                defaults.Players,
                defaults.Programs,
                defaults.Counters,
                defaults.LingeringEffects,
                triggers,
                []));
    }

    private static GameState Pass(GameState state) => ApplyKind(state, ActionKind.PassPriority);

    private static GameState ApplyKind(GameState state, ActionKind kind)
    {
        PendingDecision decision = state.PendingDecision
            ?? throw new InvalidOperationException("Expected a pending turn decision.");
        LegalAction action = decision.LegalActions.First(candidate => candidate.Kind == kind);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id));
        Assert.True(result.Accepted, result.Rejection?.Message);
        return RoundTrip(result.State);
    }

    private static GameState ReplacePending(GameState state, IReadOnlyList<LegalAction> actions)
    {
        PendingDecision pending = state.PendingDecision!;
        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            new PendingDecision(pending.Id, pending.Revision, pending.Owner, pending.Kind, pending.Prompt, actions),
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            state.RulesState);
    }

    private static GameState ReplaceTurn(GameState state, TurnState turn) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards,
        state.Zones,
        state.Servers,
        turn,
        state.Outcome,
        state.RandomState,
        state.Allocator,
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState);

    private static GameState RoundTrip(GameState state) =>
        EngineJson.DeserializeState(EngineJson.Serialize(state));

}
