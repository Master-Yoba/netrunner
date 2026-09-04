using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// ENG-415 contract and production-path coverage. The synthetic run event is
/// a real Runner grip card, so snapshotting is exercised at the same boundary
/// that will bind Tread Lightly rather than by constructing a RunState by hand.
/// </summary>
public sealed class RunRezModifierTests
{
    private static readonly CardDefinitionId RunEventDefinitionId =
        new("test:run-rez-modifier-event");
    private static readonly CardInstanceId RunEventCardId =
        new("card:run-rez-modifier-event");

    [Fact]
    public void RunRezDeclarationValidatesFreezesAndRoundTrips()
    {
        BehaviorRunRezCostModifier declaration = new(-1, "run-rez-discount");
        List<BehaviorRunRezCostModifier> declarations = [declaration];
        RunEventBehaviorSpec spec = new(
            RunEventDefinitionId,
            cost: 0,
            hostedCredits: 0,
            rezCostModifiers: declarations);
        declarations[0] = new BehaviorRunRezCostModifier(-2, "different-reason");

        BehaviorRunRezCostModifier stored = Assert.Single(spec.RezCostModifiers!);
        Assert.Equal(-1, stored.Delta);
        Assert.Equal("run-rez-discount", stored.ReasonKey);
        string json = EngineJson.Serialize(spec);
        RunEventBehaviorSpec restored = EngineJson.Deserialize<RunEventBehaviorSpec>(json);

        Assert.Contains("rezCostModifiers", json, StringComparison.Ordinal);
        Assert.Equal(spec.RezCostModifiers, restored.RezCostModifiers);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BehaviorRunRezCostModifier(0, "run-rez-discount"));
        Assert.Throws<ArgumentException>(() =>
            new BehaviorRunRezCostModifier(-1, "not a stable reason"));
        Assert.Throws<ArgumentException>(() => new RunEventBehaviorSpec(
            RunEventDefinitionId,
            cost: 0,
            hostedCredits: 0,
            rezCostModifiers:
            [
                new BehaviorRunRezCostModifier(-1, "duplicate"),
                new BehaviorRunRezCostModifier(-1, "duplicate"),
            ]));

        RunEventBehaviorSpec absent = new(RunEventDefinitionId, cost: 0, hostedCredits: 0);
        RunEventBehaviorSpec empty = new(
            RunEventDefinitionId,
            cost: 0,
            hostedCredits: 0,
            rezCostModifiers: []);
        Assert.Equal(EngineJson.Serialize(absent), EngineJson.Serialize(empty));
    }

    [Fact]
    public void RunRezDeclarationChangesCatalogEqualityFingerprintAndJsonOnlyWhenPresent()
    {
        RunEventBehaviorSpec absent = new(RunEventDefinitionId, cost: 0, hostedCredits: 0);
        RunEventBehaviorSpec empty = new(
            RunEventDefinitionId,
            cost: 0,
            hostedCredits: 0,
            rezCostModifiers: []);
        RunEventBehaviorSpec modified = new(
            RunEventDefinitionId,
            cost: 0,
            hostedCredits: 0,
            rezCostModifiers: [new BehaviorRunRezCostModifier(-1, "run-rez-discount")]);

        BehaviorCatalog unmodifiedCatalog = new BehaviorCatalogBuilder(
                "test:run-rez-declaration-catalog",
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(absent)
            .Build();
        BehaviorCatalog emptyCatalog = new BehaviorCatalogBuilder(
                "test:run-rez-declaration-catalog",
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(empty)
            .Build();
        BehaviorCatalog modifiedCatalog = new BehaviorCatalogBuilder(
                "test:run-rez-declaration-catalog",
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(modified)
            .Build();

        Assert.Equal(unmodifiedCatalog.Fingerprint, emptyCatalog.Fingerprint);
        Assert.NotEqual(unmodifiedCatalog.Fingerprint, modifiedCatalog.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => new BehaviorCatalogBuilder(
                "test:run-rez-declaration-conflict",
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(absent)
            .AddRunEvent(modified));
    }

    [Fact]
    public void RunEventSnapshotsOrderedRunRezQuoteAcrossGripPlayAreaAndHeapLifecycle()
    {
        RunRezFixture fixture = CreateFixture(
            [
                new BehaviorRunRezCostModifier(2, "z-increase"),
                new BehaviorRunRezCostModifier(-1, "a-discount"),
            ],
            corpCredits: 10,
            iceCount: 2);
        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            fixture.State,
            fixture.ServerId,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog,
            initiatingSource: fixture.EventCardId);

        RunState run = Assert.IsType<RunState>(started.State.RunState);
        Assert.Equal(fixture.EventCardId, run.ActiveRunEventId);
        Assert.Equal(2, run.RezCostModifiers!.Count);
        Assert.All(
            run.RezCostModifiers,
            snapshot =>
            {
                Assert.Equal(fixture.EventCardId, snapshot.SourceCardId);
                Assert.Equal(RunEventDefinitionId, snapshot.SourceDefinitionId);
            });
        Assert.Equal(
            ZoneKind.RunnerPlayArea,
            RunRezFixture.ZoneKindOf(started.State, fixture.EventCardId));

        GameState corpApproach = ReachCorpApproach(fixture, started.State);
        RezCardParameters current = Assert.IsType<RezCardParameters>(
            Assert.Single(
                corpApproach.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.RezCard
                    && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.OuterIceId)
                .Parameters);
        Assert.Equal(2, current.CostQuote!.BaseCost);
        Assert.Equal(3, current.CostQuote.EffectiveCost);
        Assert.Equal(
            ["a-discount", "z-increase"],
            current.CostQuote.Adjustments.Select(adjustment => adjustment.ReasonKey));
        Assert.All(
            current.CostQuote.Adjustments,
            adjustment =>
            {
                Assert.Equal(BehaviorCostModifierKind.IceRezCreditCost, adjustment.Kind);
                Assert.Equal(RunEventDefinitionId, adjustment.SourceDefinitionId);
                Assert.Equal(fixture.EventCardId, adjustment.SourceCardId);
            });

        string promptJson = EngineJson.Serialize(corpApproach);
        GameState restored = EngineJson.DeserializeState(
            promptJson,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(promptJson, EngineJson.Serialize(restored));
        Assert.Equal(
            DeterministicHash.ForState(corpApproach),
            DeterministicHash.ForState(restored));
        Assert.Equal(current, Assert.IsType<RezCardParameters>(
            Assert.Single(
                restored.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.RezCard
                    && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.OuterIceId)
                .Parameters));

        ApplyResult rezzed = Apply(fixture, corpApproach, current);
        Assert.True(rezzed.Accepted, rezzed.Rejection?.Message);
        Assert.Equal(7, rezzed.State.GetPlayer(fixture.CorpId).Credits);
        CardRezzedEventPayload rezzedPayload = Assert.IsType<CardRezzedEventPayload>(
            Assert.Single(rezzed.Events, value => value.Kind == EngineEventKinds.CardRezzed).Payload);
        Assert.Equal(3, rezzedPayload.CreditCost);
        Assert.Equal(current.CostQuote, Assert.IsType<RezCardParameters>(
            Assert.Single(
                corpApproach.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.RezCard
                    && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.OuterIceId)
                .Parameters).CostQuote);

        GameState innerCorpApproach = ReachNextCorpApproach(fixture, rezzed.State);
        RezCardParameters inner = Assert.IsType<RezCardParameters>(
            Assert.Single(
                innerCorpApproach.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.RezCard
                    && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.InnerIceId)
                .Parameters);
        Assert.Equal(2, inner.CostQuote!.BaseCost);
        Assert.Equal(3, inner.CostQuote.EffectiveCost);
        ApplyResult innerRezzed = Apply(fixture, innerCorpApproach, inner);
        Assert.True(innerRezzed.Accepted, innerRezzed.Rejection?.Message);
        Assert.Equal(4, innerRezzed.State.GetPlayer(fixture.CorpId).Credits);

        GameState ended = JackOutWhenPossible(fixture, innerRezzed.State);
        Assert.Null(ended.RunState);
        Assert.Equal(ZoneKind.RunnerHeap, RunRezFixture.ZoneKindOf(ended, fixture.EventCardId));
        Assert.Empty(ended.ResolutionStack);
        Assert.Null(ended.PendingDecision);
        Assert.Empty(GameEngine.MaterializeRezCardParameters(
            ended,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    public void ForgedRunRezQuoteIsRejectedAndInvariantUsesSameProjection()
    {
        RunRezFixture fixture = CreateFixture(
            [new BehaviorRunRezCostModifier(-1, "run-rez-discount")],
            corpCredits: 5,
            iceCount: 1);
        GameState corpApproach = ReachCorpApproach(
            fixture,
            GameEngine.BeginRun(
                fixture.State,
                fixture.ServerId,
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog,
                initiatingSource: fixture.EventCardId));
        LegalAction issued = Assert.Single(
            corpApproach.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.OuterIceId);
        RezCardParameters issuedParameters = Assert.IsType<RezCardParameters>(issued.Parameters);
        CostQuote forgedQuote = new(
            issuedParameters.CostQuote!.BaseCost,
            [new CostAdjustment(
                BehaviorCostModifierKind.IceRezCreditCost,
                -2,
                "forged-reason",
                RunEventDefinitionId,
                fixture.EventCardId)]);
        LegalAction forgedAction = new(
            issued.Id,
            issued.Kind,
            issued.Description,
            new RezCardParameters(fixture.OuterIceId, forgedQuote.EffectiveCost, forgedQuote));
        PendingDecision decision = corpApproach.PendingDecision;
        GameState forged = ReplacePending(
            corpApproach,
            new PendingDecision(
                decision.Id,
                decision.Revision,
                decision.Owner,
                decision.Kind,
                decision.Prompt,
                decision.LegalActions.Select(action => action.Id == issued.Id ? forgedAction : action).ToArray()));

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Contains(violations, violation => violation.Code == "run.rez-cost-mismatch");
        string before = EngineJson.Serialize(forged);
        ApplyResult rejected = GameEngine.Apply(
            forged,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, issued.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(forged, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    [Fact]
    public void LegacyApproachedRezIcePathCarriesQuoteAndPaysEffectiveCost()
    {
        RunRezFixture fixture = CreateFixture(
            [new BehaviorRunRezCostModifier(-1, "run-rez-discount")],
            corpCredits: 5,
            iceCount: 1);
        GameState started = GameEngine.BeginRun(
            fixture.State,
            fixture.ServerId,
            fixture.BehaviorCatalog,
            initiatingSource: fixture.EventCardId);
        GameState corpApproach = ReachCorpApproachLegacy(fixture, started);
        LegalAction issued = Assert.Single(
            corpApproach.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezIce
                && Assert.IsType<RunIceParameters>(action.Parameters).IceId == fixture.OuterIceId);
        RunIceParameters parameters = Assert.IsType<RunIceParameters>(issued.Parameters);
        Assert.Equal(2, parameters.CostQuote!.BaseCost);
        Assert.Equal(1, parameters.CostQuote.EffectiveCost);
        Assert.Equal("run-rez-discount", parameters.CostQuote.Adjustments.Single().ReasonKey);

        ApplyResult rezzed = ApplyLegacy(fixture, corpApproach, issued);
        Assert.True(rezzed.Accepted, rezzed.Rejection?.Message);
        Assert.Equal(4, rezzed.State.GetPlayer(fixture.CorpId).Credits);
        Assert.True(rezzed.State.GetCard(fixture.OuterIceId).Rezzed);
        Assert.Contains(
            rezzed.Events,
            value => value.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == -1);

        CostQuote forgedQuote = new(
            parameters.CostQuote.BaseCost,
            [new CostAdjustment(
                BehaviorCostModifierKind.IceRezCreditCost,
                -2,
                "forged-reason",
                RunEventDefinitionId,
                fixture.EventCardId)]);
        LegalAction forgedAction = new(
            issued.Id,
            issued.Kind,
            issued.Description,
            new RunIceParameters(fixture.OuterIceId, forgedQuote));
        GameState forged = ReplacePending(
            corpApproach,
            new PendingDecision(
                corpApproach.PendingDecision.Id,
                corpApproach.PendingDecision.Revision,
                corpApproach.PendingDecision.Owner,
                corpApproach.PendingDecision.Kind,
                corpApproach.PendingDecision.Prompt,
                corpApproach.PendingDecision.LegalActions.Select(action =>
                    action.Id == issued.Id ? forgedAction : action).ToArray()));
        Assert.Contains(
            StateInvariantValidator.Collect(forged, fixture.BehaviorCatalog),
            violation => violation.Code == "run.rez-cost-quote-mismatch");
        ApplyResult rejected = GameEngine.Apply(
            forged,
            new ActionSubmission(
                forged.PendingDecision!.Owner,
                forged.PendingDecision.Id,
                forged.PendingDecision.Revision,
                issued.Id),
            fixture.BehaviorCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(forged, rejected.State);
    }

    [Fact]
    public void ObservationCarriesRawRunRezTermsAndHidesThemFromRunner()
    {
        RunRezFixture fixture = CreateFixture(
            [new BehaviorRunRezCostModifier(-1, "run-rez-discount")],
            corpCredits: 5,
            iceCount: 1);
        GameState corpApproach = ReachCorpApproach(
            fixture,
            GameEngine.BeginRun(
                fixture.State,
                fixture.ServerId,
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog,
                initiatingSource: fixture.EventCardId));

        Observation corpObservation = ObservationBuilder.Build(
            corpApproach,
            PlayerSide.Corp,
            fixture.BehaviorCatalog);
        RezCardParameters observed = Assert.IsType<RezCardParameters>(
            Assert.Single(
                corpObservation.LegalActions,
                action => action.Kind == ActionKind.RezCard
                    && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.OuterIceId)
                .Parameters);
        Assert.Equal("run-rez-discount", observed.CostQuote!.Adjustments.Single().ReasonKey);
        string observationJson = EngineJson.Serialize(corpObservation);
        Assert.Contains("run-rez-discount", observationJson, StringComparison.Ordinal);
        Assert.Contains(fixture.EventCardId.Value, observationJson, StringComparison.Ordinal);

        Observation runnerObservation = ObservationBuilder.Build(
            corpApproach,
            PlayerSide.Runner,
            fixture.BehaviorCatalog);
        Assert.DoesNotContain(
            runnerObservation.LegalActions,
            action => action.Kind == ActionKind.RezCard);
        Assert.DoesNotContain("run-rez-discount", EngineJson.Serialize(runnerObservation), StringComparison.Ordinal);
    }

    private static RunRezFixture CreateFixture(
        IReadOnlyList<BehaviorRunRezCostModifier> declarations,
        int corpCredits,
        int iceCount)
    {
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                EngineCompatibility.BehaviorRegistryVersion,
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(new RunEventBehaviorSpec(
                RunEventDefinitionId,
                cost: 0,
                hostedCredits: 0,
                rezCostModifiers: declarations))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder(
                "runtime:run-rez-modifier")
            .Add(new CardRuntimeSpec(
                RunEventDefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Event,
                playCost: 0))
            .Add(new CardRuntimeSpec(
                FixtureIceBehaviorSpecs.RezCost,
                PlayerSide.Corp,
                RuntimeCardClass.Ice,
                rezCost: 2))
            .Build();
        GameState baseline = RunTimingTests.CreateRunState(
            iceCount,
            rezzed: false,
            FixtureIceBehaviorSpecs.RezCost,
            corpCredits);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState eventCard = new(
            RunEventCardId,
            RunEventDefinitionId,
            grip.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(RunEventCardId).ToArray())
                : zone)
            .ToArray();
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            baseline.Cards.Append(eventCard).ToArray(),
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            baseline.RulesState,
            behaviorCatalog.Version,
            behaviorCatalog.Fingerprint,
            runtimeCatalog.Version,
            runtimeCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);

        CardInstanceId[] iceIds = state
            .GetZone(state.GetServer(RunTimingTests.RemoteId(state)).IceZoneId)
            .CardIds
            .ToArray();
        return new RunRezFixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            RunEventCardId,
            RunTimingTests.RemoteId(state),
            state.Players.Single(player => player.Side == PlayerSide.Corp).Id,
            state.Players.Single(player => player.Side == PlayerSide.Runner).Id,
            iceIds[0],
            iceIds[^1]);
    }

    private static GameState ReachCorpApproach(RunRezFixture fixture, GameState started)
    {
        GameState state = started;
        while (true)
        {
            Assert.NotNull(state.PendingDecision);
            if (state.RunState!.Step == RunTimingStep.ApproachPaidWindow
                && SideOf(state, state.PendingDecision!.Owner) == PlayerSide.Corp)
            {
                return state;
            }

            if (state.RunState.Step == RunTimingStep.JackOutDecision)
            {
                throw new Xunit.Sdk.XunitException("The synthetic run reached jack-out before the Corp approach window.");
            }

            state = ApplyPass(fixture, state).State;
        }
    }

    private static GameState ReachNextCorpApproach(RunRezFixture fixture, GameState afterRez)
    {
        GameState state = afterRez;
        while (true)
        {
            Assert.NotNull(state.PendingDecision);
            RunState run = state.RunState!;
            if (run.Step == RunTimingStep.ApproachPaidWindow
                && run.CurrentIceId == fixture.InnerIceId
                && SideOf(state, state.PendingDecision!.Owner) == PlayerSide.Corp)
            {
                return state;
            }

            if (run.Step == RunTimingStep.JackOutDecision)
            {
                state = ApplyKind(fixture, state, ActionKind.ContinueRun).State;
            }
            else
            {
                state = ApplyPass(fixture, state).State;
            }
        }
    }

    private static GameState ReachCorpApproachLegacy(RunRezFixture fixture, GameState started)
    {
        GameState state = started;
        while (true)
        {
            Assert.NotNull(state.PendingDecision);
            if (state.RunState!.Step == RunTimingStep.ApproachPaidWindow
                && SideOf(state, state.PendingDecision!.Owner) == PlayerSide.Corp)
            {
                return state;
            }

            if (state.RunState.Step == RunTimingStep.JackOutDecision)
            {
                throw new Xunit.Sdk.XunitException("The synthetic legacy run reached jack-out before the Corp approach window.");
            }

            state = ApplyPassLegacy(fixture, state).State;
        }
    }

    private static GameState JackOutWhenPossible(RunRezFixture fixture, GameState source)
    {
        GameState state = source;
        while (state.RunState is not null)
        {
            if (state.PendingDecision is null)
            {
                throw new Xunit.Sdk.XunitException("A live synthetic run must yield a decision.");
            }

            state = state.RunState.Step == RunTimingStep.JackOutDecision
                ? ApplyKind(fixture, state, ActionKind.JackOut).State
                : ApplyPass(fixture, state).State;
        }

        return state;
    }

    private static ApplyResult Apply(
        RunRezFixture fixture,
        GameState state,
        RezCardParameters parameters)
    {
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == parameters.CardId);
        return GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static ApplyResult ApplyPass(RunRezFixture fixture, GameState state) =>
        ApplyKind(fixture, state, ActionKind.PassPriority);

    private static ApplyResult ApplyPassLegacy(RunRezFixture fixture, GameState state) =>
        ApplyKindLegacy(fixture, state, ActionKind.PassPriority);

    private static ApplyResult ApplyKind(
        RunRezFixture fixture,
        GameState state,
        ActionKind kind)
    {
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == kind);
        return GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static ApplyResult ApplyLegacy(
        RunRezFixture fixture,
        GameState state,
        LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog);

    private static ApplyResult ApplyKindLegacy(
        RunRezFixture fixture,
        GameState state,
        ActionKind kind)
    {
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == kind);
        return ApplyLegacy(fixture, state, action);
    }

    private static GameState ReplacePending(GameState source, PendingDecision pending) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        source.Players,
        source.Cards,
        source.Zones,
        source.Servers,
        source.Turn,
        source.Outcome,
        source.RandomState,
        source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        source.ResolutionStack,
        pending,
        source.ActionSequence,
        source.InitialSeed,
        source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private static PlayerSide SideOf(GameState state, PlayerId playerId) => state.GetPlayer(playerId).Side;

    private sealed record RunRezFixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        CardInstanceId EventCardId,
        ServerId ServerId,
        PlayerId CorpId,
        PlayerId RunnerId,
        CardInstanceId InnerIceId,
        CardInstanceId OuterIceId)
    {
        public static ZoneKind ZoneKindOf(GameState state, CardInstanceId cardId) =>
            state.GetZone(state.GetCard(cardId).ZoneId).Kind;
    }
}
