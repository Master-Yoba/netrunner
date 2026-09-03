using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused C2b-3a/3b coverage for production run occurrences.</summary>
public sealed class RunSuccessfulTriggerTests
{
    [Fact]
    public void RunEndedOutcomeRequirementAndOccurrenceOutcomeAreTypedAndCanonical()
    {
        PlayerId runner = new("player:success-contract-runner");
        ServerId server = new("server:success-contract");
        BehaviorInstruction instruction = new(BehaviorInstructionKind.GainCredits, amount: 1);
        BehaviorTrigger successful = new(
            "ended-success",
            OccurrenceKind.RunEnded,
            TriggerMode.Optional,
            BehaviorTriggerScope.Any,
            subjectCardFilter: null,
            [instruction],
            runOutcomeRequirement: RunOutcomeRequirement.Successful);
        BehaviorTrigger unsuccessful = new(
            "ended-success",
            OccurrenceKind.RunEnded,
            TriggerMode.Optional,
            BehaviorTriggerScope.Any,
            subjectCardFilter: null,
            [instruction],
            runOutcomeRequirement: RunOutcomeRequirement.Unsuccessful);

        Assert.Equal(RunOutcomeRequirement.Successful, successful.RunOutcomeRequirement);
        Assert.NotEqual(successful, unsuccessful);
        string triggerJson = EngineJson.Serialize(successful);
        Assert.Contains("\"runOutcomeRequirement\":\"successful\"", triggerJson, StringComparison.Ordinal);
        BehaviorTrigger restoredTrigger = EngineJson.Deserialize<BehaviorTrigger>(triggerJson);
        Assert.Equal(successful, restoredTrigger);
        Assert.Equal(triggerJson, EngineJson.Serialize(restoredTrigger));

        BehaviorCatalog successfulCatalog = new BehaviorCatalogBuilder("test:success-outcome-catalog")
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId("test:success-outcome-definition"),
                PlayerSide.Runner,
                triggers: [successful]))
            .Build();
        BehaviorCatalog unsuccessfulCatalog = new BehaviorCatalogBuilder("test:success-outcome-catalog")
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId("test:success-outcome-definition"),
                PlayerSide.Runner,
                triggers: [unsuccessful]))
            .Build();
        Assert.NotEqual(successfulCatalog.Fingerprint, unsuccessfulCatalog.Fingerprint);

        GameplayOccurrenceEffect ended = new(
            OccurrenceKind.RunEnded,
            runner,
            serverId: server,
            successful: false);
        string occurrenceJson = EngineJson.Serialize(ended);
        Assert.Contains("\"successful\":false", occurrenceJson, StringComparison.Ordinal);
        GameplayOccurrenceEffect restoredOccurrence = EngineJson.Deserialize<GameplayOccurrenceEffect>(occurrenceJson);
        Assert.Equal(ended, restoredOccurrence);
        Assert.Equal(occurrenceJson, EngineJson.Serialize(restoredOccurrence));

        Assert.Throws<ArgumentException>(() => new BehaviorTrigger(
            "invalid-outcome-kind",
            OccurrenceKind.RunSuccessful,
            TriggerMode.Optional,
            BehaviorTriggerScope.Any,
            null,
            [instruction],
            RunOutcomeRequirement.Successful));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorTrigger(
            "invalid-outcome-value",
            OccurrenceKind.RunEnded,
            TriggerMode.Optional,
            BehaviorTriggerScope.Any,
            null,
            [instruction],
            (RunOutcomeRequirement)999));
        Assert.Throws<ArgumentException>(() => new GameplayOccurrenceEffect(
            OccurrenceKind.RunEnded,
            runner,
            serverId: server));
        Assert.Throws<ArgumentException>(() => new GameplayOccurrenceEffect(
            OccurrenceKind.CardInstalled,
            runner,
            successful: true));
    }

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

    [Fact]
    public void SuccessfulRunEndedTriggerRunsAfterRunCleanup()
    {
        CardDefinitionId definitionId = new("test:ended-success");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            triggers:
            [
                EndedTrigger(
                    "successful",
                    TriggerMode.Mandatory,
                    BehaviorTriggerScope.Any,
                    amount: 2,
                    outcome: RunOutcomeRequirement.Successful),
            ]));
        GameState state = CreateState(
            catalog,
            new CardPlacement(new("card:ended-success"), definitionId, ZoneKind.RunnerRig));
        ServerId server = RunTimingTests.RemoteId(state);

        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(state, server, catalog);
        ApplyResult runnerPass = ApplyPass(start.State, catalog);
        ApplyResult completed = ApplyPass(runnerPass.State, catalog);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.RunState);
        Assert.Empty(completed.State.ResolutionStack);
        Assert.Equal(7, completed.State.GetPlayer(RunnerId(state)).Credits);
        IReadOnlyList<EngineEvent> stream = start.Events
            .Concat(runnerPass.Events)
            .Concat(completed.Events)
            .ToArray();
        int runEnded = stream.ToList().FindIndex(@event => @event.Kind == EngineEventKinds.RunEnded);
        int triggerCredit = stream.ToList().FindIndex(
            @event => @event.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(@event.Payload).Delta == 2);
        Assert.True(runEnded >= 0);
        Assert.True(triggerCredit > runEnded);
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.RunEnded));
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.RunSuccessful));
        Assert.Empty(RunTimingTraceValidator.Validate(stream).Problems);
        Assert.Empty(StateInvariantValidator.Collect(completed.State, catalog));
    }

    [Fact]
    public void JackoutRunEndedOutcomeFilterRejectsSuccessOnlyAndAcceptsUnsuccessfulOnly()
    {
        CardDefinitionId successfulDefinition = new("test:ended-success-only");
        CardDefinitionId unsuccessfulDefinition = new("test:ended-unsuccessful-only");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                successfulDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    EndedTrigger(
                        "success-only",
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Any,
                        amount: 2,
                        outcome: RunOutcomeRequirement.Successful),
                ]),
            new CardBehaviorDefinition(
                unsuccessfulDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    EndedTrigger(
                        "unsuccessful-only",
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Any,
                        amount: 3,
                        outcome: RunOutcomeRequirement.Unsuccessful),
                ]));
        GameState state = CreateStateWithRunSetup(
            catalog,
            iceCount: 1,
            rezzed: false,
            new CardPlacement(new("card:ended-success-only"), successfulDefinition, ZoneKind.RunnerRig),
            new CardPlacement(new("card:ended-unsuccessful-only"), unsuccessfulDefinition, ZoneKind.RunnerRig));
        ServerId server = RunTimingTests.RemoteId(state);

        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(state, server, catalog);
        var stream = new List<EngineEvent>(start.Events);
        ApplyResult current = ApplyPass(start.State, catalog);
        stream.AddRange(current.Events);
        current = ApplyPass(current.State, catalog);
        stream.AddRange(current.Events);
        for (int step = 0; step < 16 && current.State.RunState?.Step != RunTimingStep.JackOutDecision; step++)
        {
            RunTimingStep runStep = Assert.IsType<RunState>(current.State.RunState).Step;
            Assert.True(
                runStep is RunTimingStep.ApproachPaidWindow
                    or RunTimingStep.EncounterPaidWindow
                    or RunTimingStep.MovementPaidWindow
                    or RunTimingStep.AfterMovePaidWindow,
                $"Unexpected yielded run step '{runStep}'.");
            current = ApplyPass(current.State, catalog);
            stream.AddRange(current.Events);
            current = ApplyPass(current.State, catalog);
            stream.AddRange(current.Events);
        }

        Assert.Equal(RunTimingStep.JackOutDecision, current.State.RunState?.Step);

        PendingDecision decision = Assert.IsType<PendingDecision>(current.State.PendingDecision);
        LegalAction jackOut = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.JackOut);
        ApplyResult ended = GameEngine.Apply(
            current.State,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, jackOut.Id),
            catalog);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        stream.AddRange(ended.Events);

        Assert.Null(ended.State.RunState);
        Assert.Equal(8, ended.State.GetPlayer(RunnerId(state)).Credits);
        Assert.DoesNotContain(
            ended.Events,
            @event => @event.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(@event.Payload).Delta == 2);
        Assert.Contains(
            ended.Events,
            @event => @event.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(@event.Payload).Delta == 3);
        RunEndedEventPayload runEnded = Assert.IsType<RunEndedEventPayload>(
            stream.Single(@event => @event.Kind == EngineEventKinds.RunEnded).Payload);
        Assert.False(runEnded.Successful);
        Assert.Empty(RunTimingTraceValidator.Validate(stream).Problems);
        Assert.Empty(StateInvariantValidator.Collect(ended.State, catalog));
    }

    [Fact]
    public void OptionalRunEndedTriggerRoundTripsAfterRealTurnRunCleanup()
    {
        CardDefinitionId definitionId = new("test:ended-real-turn-optional");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            triggers:
            [
                EndedTrigger(
                    "optional",
                    TriggerMode.Optional,
                    BehaviorTriggerScope.Any,
                    amount: 2,
                    outcome: RunOutcomeRequirement.Successful),
            ]));
        GameState state = CreateState(
            catalog,
            new CardPlacement(new("card:ended-real-turn-optional"), definitionId, ZoneKind.RunnerRig));
        GameState actionState = OpenRunnerAction(state, catalog);
        ServerId server = RunTimingTests.RemoteId(actionState);
        PendingDecision actionDecision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
        LegalAction startRun = Assert.Single(
            actionDecision.LegalActions,
            action => action.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(action.Parameters).ServerId == server);
        ApplyResult started = GameEngine.Apply(
            actionState,
            new ActionSubmission(actionDecision.Owner, actionDecision.Id, actionDecision.Revision, startRun.Id),
            catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);
        ApplyResult runnerPass = ApplyPass(started.State, catalog);
        ApplyResult opened = ApplyPass(runnerPass.State, catalog);

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Null(opened.State.RunState);
        Assert.Equal(DecisionKind.TriggerChoice, opened.State.PendingDecision?.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(opened.State.ResolutionStack[0]).Stage);
        Assert.Contains(opened.Events, @event => @event.Kind == EngineEventKinds.RunEnded);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        ApplyResult declined = ApplyDecline(restored, catalog);
        Assert.True(declined.Accepted, declined.Rejection?.Message);
        Assert.Null(declined.State.RunState);
        Assert.Single(declined.State.ResolutionStack);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(declined.State.ResolutionStack[0]).Stage);
        Assert.Equal(DecisionKind.TurnTiming, declined.State.PendingDecision?.Kind);
        Assert.DoesNotContain(declined.Events, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.Empty(StateInvariantValidator.Collect(declined.State, catalog));
    }

    private static BehaviorCatalog Catalog(params CardBehaviorDefinition[] definitions)
    {
        var builder = new BehaviorCatalogBuilder("test:success-catalog");
        builder.AddIce(new IceBehaviorSpec(
            FixtureIceBehaviorSpecs.Plain,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: [],
            strengthModifiers: []));
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

    private static BehaviorTrigger EndedTrigger(
        string key,
        TriggerMode mode,
        BehaviorTriggerScope scope,
        int amount,
        RunOutcomeRequirement? outcome = null,
        BehaviorCardFilter? subjectCardFilter = null) => new(
        key,
        OccurrenceKind.RunEnded,
        mode,
        scope,
        subjectCardFilter,
        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount)],
        outcome);

    private static GameState CreateState(
        BehaviorCatalog catalog,
        params CardPlacement[] placements)
    {
        return CreateStateWithRunSetup(catalog, 0, false, placements);
    }

    private static GameState CreateStateWithRunSetup(
        BehaviorCatalog catalog,
        int iceCount,
        bool rezzed,
        params CardPlacement[] placements)
    {
        GameState baseline = RunTimingTests.CreateRunState(iceCount, rezzed);
        CardInstanceState[] cards = baseline.Cards
            .Concat(placements.Select(placement => new CardInstanceState(
                placement.Id,
                placement.DefinitionId,
                baseline.GetZone(placement.Zone).Id,
                placement.Owner,
                faceUp: placement.Zone is ZoneKind.RunnerRig or ZoneKind.RunnerPlayArea)))
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
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
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

    private static GameState OpenRunnerAction(GameState state, IBehaviorCatalog catalog)
    {
        state = GameEngine.BeginTurn(state, PlayerSide.Runner, catalog);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
                catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
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
