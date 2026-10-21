using System.Reflection;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MatchCoordinatorTests
{
    [Fact]
    public async Task RepresentativeStartPublishesRunOpeningAndRunnerDecision()
    {
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());

        MatchSnapshot snapshot = await coordinator.StartAsync();

        Assert.Contains(snapshot.Events, value => value.Kind == EngineEventKinds.RunEventPlayed);
        Assert.Contains(snapshot.Events, value => value.Kind == EngineEventKinds.RunInitiated);
        Assert.NotNull(snapshot.View.Decision);
        Assert.Equal(PlayerSide.Runner, snapshot.View.Decision!.OwnerSide);
        Assert.Contains(snapshot.View.LegalActions, value => value.Kind == ActionKind.PassPriority);
        Assert.NotNull(snapshot.View.Run);
        Assert.Equal("card:7", snapshot.View.Run!.InitiatingSource!.Id);
    }

    [Fact]
    public void PublicationUsesComposedCatalogForInstalledStaticHandModifier()
    {
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        GameState baseline = GameState.CreateNew(
            seed: 42,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);
        ZoneState runnerRig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState t400 = new(
            new CardInstanceId("card:t400"),
            GatewayBatch1CardCodes.T400MemoryDiamond,
            runnerRig.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == runnerRig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [t400.Id])
                : zone)
            .ToArray();
        RulesState rules = RulesState.CreateDefault(baseline.Players);
        GameState state = new(
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            [t400],
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            initialSeed: baseline.InitialSeed,
            rulesState: rules,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);
        Assert.Empty(StateInvariantValidator.Collect(state, behaviorCatalog));

        using MatchCoordinator coordinator = new(
            M2ScenarioProvider.LoadRepresentative(),
            new FirstLegalAgent(),
            new MatchCoordinatorOptions { BehaviorCatalog = behaviorCatalog });
        typeof(MatchCoordinator)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(coordinator, state);

        MatchSnapshot snapshot = (MatchSnapshot)typeof(MatchCoordinator)
            .GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [Array.Empty<EngineEvent>(), null, false])!;

        var players = Assert.IsType<Dictionary<string, object?>>(
            snapshot.Observation.PublicState["players"]);
        var runner = Assert.IsType<Dictionary<string, object?>>(players["runner"]);
        Assert.Equal(6, runner["maximumHandSize"]);
        Assert.Equal(6, snapshot.View.RunnerHandSize.MaximumHandSize);
    }

    [Fact]
    public void RunnerProjectionDoesNotExposeCorpHiddenCardsOrAnonymousIdentity()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        GameState state = M2ScenarioSetup.Build(document);
        Observation runnerObservation = ObservationBuilder.Build(state, PlayerSide.Runner);
        MatchViewModel view = ObservationViewMapper.Build(runnerObservation);

        Assert.All(view.Servers.SelectMany(value => value.Ice), card => Assert.Null(card));
        Assert.All(view.Servers.SelectMany(value => value.Root), card => Assert.Null(card));
        Assert.Empty(view.CorpHand.VisibleCards);
        Assert.Empty(view.CorpDeck.VisibleCards);
        Assert.Equal(1, view.RunnerHand.Count);
        Assert.Contains(view.RunnerHand.VisibleCards, value => value.DefinitionId == "30029");

        MatchViewModel corpView = ObservationViewMapper.Build(
            ObservationBuilder.Build(state, PlayerSide.Corp));
        ServerViewModel remote = Assert.Single(corpView.Servers, value => value.Kind == ServerKind.Remote);
        Assert.Equal(["card:2", "card:1"], remote.OrderedIce.Select(card => card!.Id));
    }

    [Fact]
    public async Task TenSecondAgentDelayPublishesThinkingWithoutBlockingRunnerSubmission()
    {
        var releaseAgent = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                AgentDelay = TimeSpan.FromSeconds(10),
                Delay = (_, cancellationToken) => new ValueTask(releaseAgent.Task.WaitAsync(cancellationToken)),
            },
            new FirstLegalAgent());

        MatchSnapshot initial = await coordinator.StartAsync();
        LegalAction pass = Assert.Single(initial.Observation.LegalActions, value => value.Kind == ActionKind.PassPriority);

        MatchSubmissionResult submitted = await coordinator.SubmitRunnerActionAsync(pass.Id);
        Assert.True(submitted.Accepted);
        await WaitForAsync(
            () => coordinator.CurrentSnapshot?.IsOpponentThinking == true,
            TimeSpan.FromSeconds(2));
        Assert.Null(coordinator.CurrentSnapshot!.Observation.Decision);
        Assert.Null(coordinator.CurrentSnapshot.View.Decision);

        releaseAgent.SetResult(null);
        await WaitForAsync(
            () => coordinator.CurrentSnapshot?.IsOpponentThinking == false,
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StaleSubmissionPublishesFeedbackAndPreservesObservation()
    {
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());

        MatchSnapshot initial = await coordinator.StartAsync();
        ObservationDecision decision = initial.Observation.Decision
            ?? throw new InvalidOperationException("The representative start should await Runner input.");
        LegalAction action = initial.Observation.LegalActions[0];
        string observationBefore = EngineJson.Serialize(initial.Observation);
        MatchSubmissionResult rejected = await coordinator.SubmitActionAsync(
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision + 1, action.Id));

        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.StaleRevision, rejected.Rejection?.Code);
        Assert.Equal(observationBefore, EngineJson.Serialize(rejected.Snapshot.Observation));
        Assert.Equal(MatchFeedbackKind.Rejected, rejected.Snapshot.Feedback?.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownScenarioActorIsRejectedAndNextSubmissionCanProceed(bool traceEnabled)
    {
        string? directory = traceEnabled
            ? Path.Combine(Path.GetTempPath(), $"netrunner-coordinator-{Guid.NewGuid():N}")
            : null;
        var agentGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
                new MatchCoordinatorOptions
                {
                    AgentDelay = TimeSpan.FromSeconds(10),
                    Delay = (_, cancellationToken) => new ValueTask(agentGate.Task.WaitAsync(cancellationToken)),
                    SessionTraceDirectory = directory,
                },
                new FirstLegalAgent());

            MatchSnapshot initial = await coordinator.StartAsync();
            ObservationDecision decision = initial.Observation.Decision
                ?? throw new Xunit.Sdk.XunitException("The scenario should await a Runner decision.");
            LegalAction action = initial.Observation.LegalActions[0];

            MatchSubmissionResult rejected = await coordinator.SubmitActionAsync(
                new ActionSubmission(
                    new PlayerId("player:unknown"),
                    decision.Id,
                    decision.Revision,
                    action.Id));

            Assert.False(rejected.Accepted);
            Assert.Equal(ApplyRejectionCode.WrongOwner, rejected.Rejection?.Code);
            Assert.Equal(initial.ActionCount, rejected.Snapshot.ActionCount);

            MatchSubmissionResult accepted = await coordinator.SubmitActionAsync(
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id));
            Assert.True(accepted.Accepted, accepted.Feedback);

            if (traceEnabled)
            {
                await WaitForAsync(
                    () => coordinator.CurrentSnapshot?.IsOpponentThinking == true,
                    TimeSpan.FromSeconds(2));
                M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
                BehaviorCatalog behavior = document.CreateBehaviorCatalog(GatewayBehaviorCatalog.Create());
                SessionRecoveryResult recovered = SessionTrace.Recover(directory!, behavior);
                Assert.False(recovered.HasInterruptedAction);
                Assert.Contains(
                    "\"rejection\"",
                    File.ReadAllText(Path.Combine(directory!, SessionTrace.JournalFileName)),
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ThrowingSnapshotSubscriberDoesNotBlockStartOrLaterCommands()
    {
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());
        int healthyPublicationCount = 0;
        coordinator.SnapshotPublished += static _ =>
            throw new InvalidOperationException("The test subscriber failed.");
        coordinator.SnapshotPublished += _ => Interlocked.Increment(ref healthyPublicationCount);

        MatchSnapshot started = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(healthyPublicationCount > 0);
        Assert.Same(started, coordinator.CurrentSnapshot);
        LegalAction pass = Assert.Single(
            started.Observation.LegalActions,
            value => value.Kind == ActionKind.PassPriority);

        MatchSubmissionResult submitted = await coordinator
            .SubmitRunnerActionAsync(pass.Id)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(submitted.Accepted, submitted.Feedback);

        Observation observation = await coordinator
            .GetObservationAsync(PlayerSide.Runner)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(observation);
        Assert.True(healthyPublicationCount > 1);
    }

    [Fact]
    public void EncounterProjectionUsesEffectivePalisadeStrengthWithoutChangingCardMetadata()
    {
        BehaviorCatalog behaviorCatalog = RepresentativeBehaviorCatalog();
        GameState state = ReachPalisadeEncounter();
        Observation liveObservation = ObservationBuilder.Build(state, PlayerSide.Runner, behaviorCatalog);
        MatchViewModel liveView = ObservationViewMapper.Build(liveObservation);

        RunFocusViewModel liveRun = Assert.IsType<RunFocusViewModel>(liveView.Run);
        EncounterViewModel liveEncounter = Assert.IsType<EncounterViewModel>(liveRun.Encounter);
        CardViewModel liveIce = Assert.IsType<CardViewModel>(liveEncounter.Ice);
        Assert.Equal("30072", liveIce.DefinitionId);
        Assert.Equal(2, liveIce.Strength);
        Assert.Equal(4, liveEncounter.EffectiveStrength);
        Assert.Equal(2, liveEncounter.StrengthQuote!.BaseValue);
        Assert.Equal(4, liveEncounter.StrengthQuote.EffectiveValue);
        Assert.Equal("ice-strength-protecting-remote", Assert.Single(liveEncounter.StrengthQuote.Terms).ReasonKey);
        Assert.DoesNotContain("STR", EngineJson.Serialize(liveEncounter.StrengthQuote), StringComparison.Ordinal);

        Observation restoredObservation = EngineJson.Deserialize<Observation>(
            EngineJson.Serialize(liveObservation));
        MatchViewModel restoredView = ObservationViewMapper.Build(restoredObservation);
        RunFocusViewModel restoredRun = Assert.IsType<RunFocusViewModel>(restoredView.Run);
        EncounterViewModel restoredEncounter = Assert.IsType<EncounterViewModel>(restoredRun.Encounter);
        CardViewModel restoredIce = Assert.IsType<CardViewModel>(restoredEncounter.Ice);
        Assert.Equal(2, restoredIce.Strength);
        Assert.Equal(4, restoredEncounter.EffectiveStrength);
        Assert.Equal(liveEncounter.StrengthQuote, restoredEncounter.StrengthQuote);
    }

    [Fact]
    public void EncounterProjectionUsesInjectedBehaviorCatalogSnapshot()
    {
        BehaviorCatalog representativeCatalog = RepresentativeBehaviorCatalog();
        GameState state = ReachPalisadeEncounter();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test-view-catalog")
            .AddIce(new IceBehaviorSpec(
                new CardDefinitionId("30072"),
                strength: 9,
                IceType.Barrier,
                rezCost: 3,
                subroutines: Array.Empty<IceSubroutineSpec>(),
                strengthModifiers: Array.Empty<IceStrengthModifierSpec>()))
            .AddIcebreaker(new IcebreakerBehaviorSpec(
                new CardDefinitionId("30006"),
                baseStrength: 11,
                IceType.Barrier,
                breakCost: 1,
                breakLimit: 2,
                pumpCost: 2,
                pumpStrength: 1))
            .Build();

        MatchViewModel view = ObservationViewMapper.Build(
            ObservationBuilder.Build(state, PlayerSide.Runner, representativeCatalog),
            CardMetadataResolvers.Default,
            behaviorCatalog);
        EncounterViewModel encounter = Assert.IsType<EncounterViewModel>(
            Assert.IsType<RunFocusViewModel>(view.Run).Encounter);

        Assert.Equal(9, encounter.EffectiveStrength);
        Assert.Equal(
            11,
            Assert.Single(encounter.Breakers, breaker => breaker.CardId == "card:5").EffectiveStrength);
    }

    [Fact]
    public void RepresentativePalisadeRequiresCleaverPumpBeforeBreak()
    {
        BehaviorCatalog behaviorCatalog = RepresentativeBehaviorCatalog();
        GameState encounter = ReachPalisadeEncounter();
        PendingDecision beforePump = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.DoesNotContain(
            beforePump.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines);

        MatchViewModel beforeView = ObservationViewMapper.Build(
            ObservationBuilder.Build(encounter, PlayerSide.Runner, behaviorCatalog));
        EncounterBreakerViewModel beforeCleaver = Assert.Single(
            Assert.IsType<RunFocusViewModel>(beforeView.Run).Encounter!.Breakers,
            breaker => breaker.CardId == "card:5");
        Assert.Equal(3, beforeCleaver.PrintedStrength);
        Assert.Equal(3, beforeCleaver.EffectiveStrength);
        Assert.Empty(beforeCleaver.StrengthQuote!.Terms);

        LegalAction pump = Assert.Single(beforePump.LegalActions, action =>
            action.Kind == ActionKind.PumpBreaker
            && action.Parameters is RunBreakerParameters
            {
                BreakerId.Value: "card:5",
                HostedCredits: 2,
            });
        ApplyResult pumped = GameEngine.Apply(
            encounter,
            new ActionSubmission(beforePump.Owner, beforePump.Id, beforePump.Revision, pump.Id),
            behaviorCatalog);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);

        PendingDecision afterPump = Assert.IsType<PendingDecision>(pumped.State.PendingDecision);
        Assert.Contains(afterPump.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && action.Parameters is RunBreakParameters { BreakerId.Value: "card:5" });
        MatchViewModel afterView = ObservationViewMapper.Build(
            ObservationBuilder.Build(pumped.State, PlayerSide.Runner, behaviorCatalog));
        EncounterBreakerViewModel afterCleaver = Assert.Single(
            Assert.IsType<RunFocusViewModel>(afterView.Run).Encounter!.Breakers,
            breaker => breaker.CardId == "card:5");
        Assert.Equal(3, afterCleaver.PrintedStrength);
        Assert.Equal(4, afterCleaver.EffectiveStrength);
        Assert.Equal("icebreaker-pump", Assert.Single(afterCleaver.StrengthQuote!.Terms).ReasonKey);
    }

    [Fact]
    public async Task HumanRunnerCanCompleteTheRepresentativeRunWithoutDroppedEventBatches()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        M2ScenarioRunSpec run = Assert.IsType<M2ScenarioRunSpec>(document.Run);
        var runner = new RunContractAgent(new RunPlayContract(
            run.Server,
            run.Source,
            Assert.IsType<string>(run.Breaker),
            run.MaxPumps));
        var publishedEvents = new System.Collections.Concurrent.ConcurrentQueue<EngineEvent>();
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            });
        coordinator.SnapshotPublished += snapshot =>
        {
            foreach (EngineEvent @event in snapshot.Events)
            {
                publishedEvents.Enqueue(@event);
            }
        };

        MatchSnapshot snapshot = await coordinator.StartAsync();
        for (int step = 0; step < 256 && snapshot.View.Run is not null; step++)
        {
            if (snapshot.AwaitingHumanDecision)
            {
                ActionId selected = await runner.ChooseActionAsync(snapshot.Observation);
                MatchSubmissionResult result = await coordinator.SubmitRunnerActionAsync(selected);
                Assert.True(result.Accepted, result.Feedback);
                snapshot = result.Snapshot;
                continue;
            }

            long sequence = snapshot.Sequence;
            await WaitForAsync(
                () => coordinator.CurrentSnapshot is { Sequence: > 0 } current
                    && current.Sequence > sequence,
                TimeSpan.FromSeconds(2));
            snapshot = coordinator.CurrentSnapshot!;
        }

        await WaitForAsync(
            () => coordinator.CurrentSnapshot?.View.Run is null,
            TimeSpan.FromSeconds(2));
        snapshot = coordinator.CurrentSnapshot!;

        Assert.Null(snapshot.View.Run);
        Assert.Equal(2, snapshot.View.Runner.AgendaPoints);
        Assert.Equal(12, snapshot.View.Runner.Credits);
        Assert.Contains(publishedEvents, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.NotNull(snapshot.RunOutcome);
        Assert.Equal(RunEndReason.BreachCompleted, snapshot.RunOutcome!.Reason);
        Assert.True(snapshot.RunOutcome.Successful);
        Assert.DoesNotContain("GameState", EngineJson.Serialize(snapshot.RunOutcome));
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The asynchronous coordinator did not reach the expected state.");
            }

            await Task.Delay(10);
        }
    }

    private static GameState ReachPalisadeEncounter()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        BehaviorCatalog behaviorCatalog = document.CreateBehaviorCatalog(GatewayBehaviorCatalog.Create());
        M2ScenarioRunSpec run = document.Run
            ?? throw new InvalidOperationException("The representative scenario has no run contract.");
        GameState state = M2ScenarioSetup.Build(document, behaviorCatalog);
        ServerId server = state.Servers.Single(candidate => candidate.Kind == ServerKind.Remote).Id;
        state = GameEngine.BeginRunWithEvents(
            state,
            server,
            behaviorCatalog,
            initiatingSource: new CardInstanceId(run.Source)).State;

        state = ApplyKind(state, ActionKind.PassPriority, behaviorCatalog); // initiation, Runner
        state = ApplyKind(state, ActionKind.PassPriority, behaviorCatalog); // initiation, Corp
        state = ApplyKind(state, ActionKind.PassPriority, behaviorCatalog); // approach, Runner
        state = ApplyKind(state, ActionKind.RezIce, behaviorCatalog);       // approach, Corp
        state = ApplyKind(state, ActionKind.PassPriority, behaviorCatalog); // approach, Corp
        state = ApplyKind(state, ActionKind.PassPriority, behaviorCatalog); // approach, Runner
        return state;
    }

    private static GameState ApplyKind(GameState state, ActionKind kind, IBehaviorCatalog behaviorCatalog)
    {
        PendingDecision decision = state.PendingDecision
            ?? throw new InvalidOperationException("The test state has no pending decision.");
        LegalAction action = decision.LegalActions.Single(candidate => candidate.Kind == kind);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            behaviorCatalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static BehaviorCatalog RepresentativeBehaviorCatalog()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        return document.CreateBehaviorCatalog(GatewayBehaviorCatalog.Create());
    }
}
