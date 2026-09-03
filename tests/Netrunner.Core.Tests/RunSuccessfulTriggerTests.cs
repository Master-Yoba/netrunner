using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused C2b-3a coverage for production RunSuccessful occurrences.</summary>
public sealed class RunSuccessfulTriggerTests
{
    [Fact]
    public void TriggerlessRunRetainsLegacyNoRulesState()
    {
        CardDefinitionId inactiveDefinition = new("test:success-triggerless-inactive");
        BehaviorCatalog catalog = Catalog(
            Definition(
                inactiveDefinition,
                Trigger("success", TriggerMode.Mandatory, BehaviorTriggerScope.Any, 1)));
        GameState state = CreateState(
            catalog,
            new CardPlacement(
                new("card:success-triggerless-inactive"),
                inactiveDefinition,
                ZoneKind.RunnerGrip));
        ServerId server = RunTimingTests.RemoteId(state);

        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(state, server, catalog);
        ApplyResult runnerPass = ApplyPass(start.State, catalog);
        ApplyResult completed = ApplyPass(runnerPass.State, catalog);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.RulesState);
        Assert.Empty(completed.State.ResolutionStack);
        Assert.Equal(1, completed.Events.Count(@event => @event.Kind == EngineEventKinds.RunSuccessful));
        Assert.Empty(StateInvariantValidator.Collect(completed.State, catalog));
    }

    [Fact]
    public void ActiveExternalRunnerTriggersResolveAfterSuccessBeforeBreach()
    {
        CardDefinitionId controllerDefinition = new("test:success-controller");
        CardDefinitionId anyDefinition = new("test:success-any");
        CardDefinitionId inactiveDefinition = new("test:success-inactive");
        BehaviorCatalog catalog = Catalog(
            Definition(
                controllerDefinition,
                Trigger("controller", TriggerMode.Mandatory, BehaviorTriggerScope.Controller, 2)),
            Definition(
                anyDefinition,
                Trigger("any", TriggerMode.Mandatory, BehaviorTriggerScope.Any, 3)),
            Definition(
                inactiveDefinition,
                Trigger("inactive", TriggerMode.Mandatory, BehaviorTriggerScope.Any, 100)));
        GameState state = CreateState(
            catalog,
            new CardPlacement(new("card:success-controller"), controllerDefinition, ZoneKind.RunnerRig),
            new CardPlacement(new("card:success-any"), anyDefinition, ZoneKind.RunnerRig),
            new CardPlacement(new("card:success-inactive"), inactiveDefinition, ZoneKind.RunnerGrip),
            new CardPlacement(new("card:success-unknown"), new("test:success-unknown"), ZoneKind.RunnerRig));
        ServerId server = RunTimingTests.RemoteId(state);

        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(state, server, catalog);
        ApplyResult runnerPass = ApplyPass(start.State, catalog);
        ApplyResult success = ApplyPass(runnerPass.State, catalog);
        var successSteps = new List<ApplyResult> { success };
        while (success.State.PendingDecision?.Kind == DecisionKind.TriggerOrdering)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(success.State.PendingDecision);
            LegalAction order = decision.LegalActions.First(action => action.Kind == ActionKind.OrderTriggers);
            success = GameEngine.Apply(
                success.State,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, order.Id),
                catalog);
            Assert.True(success.Accepted, success.Rejection?.Message);
            successSteps.Add(success);
        }

        Assert.True(success.Accepted, success.Rejection?.Message);
        Assert.Null(success.State.RunState);
        Assert.Empty(success.State.ResolutionStack);
        Assert.Equal(10, success.State.GetPlayer(RunnerId(state)).Credits);
        Assert.Equal(
            [
                EngineEventKinds.RunPriorityPassed,
                EngineEventKinds.RunSuccessful,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.BreachStarted,
                EngineEventKinds.RunEnded,
            ],
            successSteps.SelectMany(result => result.Events).Select(@event => @event.Kind));

        IReadOnlyList<EngineEvent> stream = start.Events
            .Concat(runnerPass.Events)
            .Concat(successSteps.SelectMany(result => result.Events))
            .ToArray();
        int runSuccessful = stream.ToList().FindIndex(@event => @event.Kind == EngineEventKinds.RunSuccessful);
        int breachStarted = stream.ToList().FindIndex(@event => @event.Kind == EngineEventKinds.BreachStarted);
        Assert.True(runSuccessful >= 0 && breachStarted > runSuccessful);
        Assert.All(
            stream.Skip(runSuccessful + 1).Take(breachStarted - runSuccessful - 1),
            @event => Assert.Equal(EngineEventKinds.CreditsAdjusted, @event.Kind));
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.RunSuccessful));
        Assert.Empty(RunTimingTraceValidator.Validate(stream).Problems);
        Assert.Empty(StateInvariantValidator.Collect(success.State, catalog));
    }

    [Fact]
    public void RunEventSourceSubjectTriggerRoundTripsAtBreachBoundaryWithoutRepeatingSuccess()
    {
        CardDefinitionId eventDefinition = new("test:success-run-event");
        CardDefinitionId observerDefinition = new("test:success-subject-observer");
        CardInstanceId eventId = new("card:success-run-event");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:success-source-catalog")
            .AddRunEvent(new RunEventBehaviorSpec(eventDefinition, cost: 0, hostedCredits: 0))
            .Add(new CardBehaviorDefinition(
                eventDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("source-self", TriggerMode.Mandatory, BehaviorTriggerScope.Self, 100),
                ]))
            .Add(new CardBehaviorDefinition(
                observerDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger(
                        "subject",
                        OccurrenceKind.RunSuccessful,
                        TriggerMode.Optional,
                        BehaviorTriggerScope.Any,
                        new BehaviorCardFilter(definitionId: eventDefinition, ownerSide: PlayerSide.Runner),
                        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 2)]),
                ]))
            .Build();
        GameState state = CreateState(
            catalog,
            new CardPlacement(eventId, eventDefinition, ZoneKind.RunnerGrip),
            new CardPlacement(new("card:success-subject-observer"), observerDefinition, ZoneKind.RunnerRig));
        ServerId server = RunTimingTests.RemoteId(state);

        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            state,
            server,
            catalog,
            initiatingSource: eventId);
        ApplyResult runnerPass = ApplyPass(start.State, catalog);
        ApplyResult opened = ApplyPass(runnerPass.State, catalog);

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.TriggerChoice, opened.State.PendingDecision?.Kind);
        Assert.Equal(RunTimingStep.BreachBoundary, opened.State.RunState?.Step);
        Assert.Equal(1, opened.Events.Count(@event => @event.Kind == EngineEventKinds.RunSuccessful));
        Assert.Equal(1, start.Events.Count(@event => @event.Kind == EngineEventKinds.RunEventPlayed));
        Assert.Equal(5, opened.State.GetPlayer(RunnerId(state)).Credits);

        RunTimingFrame runFrame = Assert.IsType<RunTimingFrame>(opened.State.ResolutionStack[0]);
        Assert.Equal(RunTimingStep.BreachBoundary, runFrame.Step);
        EffectResolutionFrame occurrenceFrame = Assert.IsType<EffectResolutionFrame>(opened.State.ResolutionStack[1]);
        GameplayOccurrenceEffect occurrence = Assert.IsType<GameplayOccurrenceEffect>(
            Assert.Single(occurrenceFrame.Effects.Instructions));
        Assert.Equal(OccurrenceKind.RunSuccessful, occurrence.Kind);
        Assert.Equal(RunnerId(state), occurrence.Controller);
        Assert.Equal(eventId, occurrence.SubjectCardId);
        Assert.Equal(server, occurrence.ServerId);
        Assert.Equal(occurrence.Controller, occurrenceFrame.Controller);
        StateInvariantValidator.Validate(opened.State, catalog);

        ServerId forgedServer = state.Servers
            .Select(candidate => candidate.Id)
            .First(candidate => candidate != server);
        EffectResolutionFrame forgedFrame = new(
            occurrenceFrame.Controller,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.RunSuccessful,
                occurrence.Controller,
                occurrence.SubjectCardId,
                forgedServer)));
        GameState forged = ReplaceResolutionStack(
            opened.State,
            [opened.State.ResolutionStack[0], forgedFrame]);
        Assert.Contains(
            StateInvariantValidator.Collect(forged, catalog),
            violation => violation.Code == "run.stack-topology");

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        ApplyResult completed = ApplyDecline(restored, catalog);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.RunState);
        Assert.Empty(completed.State.ResolutionStack);
        Assert.Equal(5, completed.State.GetPlayer(RunnerId(state)).Credits);
        Assert.DoesNotContain(completed.Events, @event => @event.Kind == EngineEventKinds.RunSuccessful);
        Assert.DoesNotContain(completed.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted &&
            Assert.IsType<CreditsAdjustedEventPayload>(@event.Payload).Delta == 100);
        Assert.Contains(eventId, completed.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(StateInvariantValidator.Collect(completed.State, catalog));
    }

    private static BehaviorCatalog Catalog(params CardBehaviorDefinition[] definitions)
    {
        var builder = new BehaviorCatalogBuilder("test:success-catalog");
        foreach (CardBehaviorDefinition definition in definitions)
        {
            builder.Add(definition);
        }

        return builder.Build();
    }

    private static CardBehaviorDefinition Definition(
        CardDefinitionId definitionId,
        BehaviorTrigger trigger) => new(
        definitionId,
        PlayerSide.Runner,
        triggers: [trigger]);

    private static BehaviorTrigger Trigger(
        string key,
        TriggerMode mode,
        BehaviorTriggerScope scope,
        int amount) => new(
        key,
        OccurrenceKind.RunSuccessful,
        mode,
        scope,
        subjectCardFilter: null,
        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount)]);

    private static GameState CreateState(
        BehaviorCatalog catalog,
        params CardPlacement[] placements)
    {
        GameState baseline = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        CardInstanceState[] cards = placements
            .Select(placement => new CardInstanceState(
                placement.Id,
                placement.DefinitionId,
                baseline.GetZone(placement.Zone).Id,
                placement.Owner,
                faceUp: placement.Zone is ZoneKind.RunnerRig or ZoneKind.RunnerPlayArea))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone =>
            {
                CardInstanceId[] additions = placements
                    .Where(placement => placement.Zone == zone.Kind)
                    .Select(placement => placement.Id)
                    .ToArray();
                return additions.Length == 0
                    ? zone
                    : new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        zone.CardIds.Concat(additions).ToArray());
            })
            .ToArray();
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
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
            catalog.Version,
            catalog.Fingerprint,
            baseline.RuntimeCardCatalogVersion,
            baseline.RuntimeCardCatalogFingerprint);
        StateInvariantValidator.Validate(state, catalog);
        return state;
    }

    private static ApplyResult ApplyPass(GameState state, IBehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
            catalog);
    }

    private static ApplyResult ApplyDecline(GameState state, IBehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction decline = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.DeclineTrigger);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decline.Id),
            catalog);
    }

    private static PlayerId RunnerId(GameState state) =>
        state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

    private static GameState ReplaceResolutionStack(
        GameState state,
        IReadOnlyList<ResolutionFrame> frames) => new(
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
        frames,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);

    private sealed record CardPlacement(
        CardInstanceId Id,
        CardDefinitionId DefinitionId,
        ZoneKind Zone,
        PlayerSide Owner = PlayerSide.Runner);
}
