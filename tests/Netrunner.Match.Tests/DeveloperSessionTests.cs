using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class DeveloperSessionTests
{
    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public async Task ImportStepsExactlyOnceAndExportsReplayWithExactPolicy(PlayerSide human)
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(58);
        DeveloperSnapshotDocument root = Root(definition, human);
        await using var coordinator = new MatchCoordinator(root, Options(definition, human));
        MatchSnapshot initial = await coordinator.StartAsync();
        Assert.True(initial.IsPaused);
        Assert.Equal(0, initial.ActionCount);
        Assert.False(coordinator.SupportsPlayerSaves);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CaptureSaveAsync("Not a player save"));
        DeveloperAdvanceResult result = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1));
        Assert.Equal(1, result.AcceptedActions);
        Assert.Equal(1, result.Snapshot.ActionCount);
        Assert.True(result.Snapshot.IsPaused);
        DeveloperSnapshotDocument exported = await coordinator.ExportDeveloperSnapshotAsync();
        exported.Validate(definition.Behavior, definition.Runtime);
        await using var restored = new MatchCoordinator(EngineJson.Deserialize<DeveloperSnapshotDocument>(EngineJson.Serialize(exported)), Options(definition, human));
        await restored.StartAsync();
        for (int index = 0; index < 12; index++)
        {
            await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1));
            await restored.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1));
            DeveloperSnapshotDocument left = await coordinator.ExportDeveloperSnapshotAsync();
            DeveloperSnapshotDocument right = await restored.ExportDeveloperSnapshotAsync();
            Assert.Equal(left.IntegrityHash, right.IntegrityHash);
        }
        // Deliberately importing a raw later snapshot creates a new scenario root, with new policy knowledge.
        DeveloperSnapshotDocument later = await coordinator.ExportDeveloperSnapshotAsync();
        DeveloperSnapshotDocument branch = DeveloperSnapshotDocument.Import(later.Snapshot, human, definition.Behavior, definition.Runtime);
        Assert.Empty(branch.Replay.Actions);
        Assert.Equal(later.Snapshot.ActionSequence, branch.Replay.InitialSnapshot.ActionSequence);
        Assert.Equal(0, branch.Opponent.Checkpoint!.CommittedDecisionCount);
        await using var branched = new MatchCoordinator(branch, Options(definition, human));
        await branched.StartAsync();
        Assert.Equal(1, (await branched.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1))).AcceptedActions);
        (await branched.ExportDeveloperSnapshotAsync()).Validate(definition.Behavior, definition.Runtime);
    }

    [Fact]
    public async Task RevealIsReadOnlyAndCannotChangeObservationsPolicyOrNextAction()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(45);
        DeveloperSnapshotDocument root = Root(definition, PlayerSide.Runner);
        await using var inspected = new MatchCoordinator(root, Options(definition, PlayerSide.Runner));
        await using var control = new MatchCoordinator(root, Options(definition, PlayerSide.Runner));
        await inspected.StartAsync();
        await control.StartAsync();
        string before = EngineJson.Serialize(await inspected.GetObservationAsync(PlayerSide.Runner));
        DeveloperInspection privateView = await inspected.InspectDeveloperStateAsync();
        Assert.Equal(root.Snapshot.Cards.Count, privateView.Cards.Count);
        Assert.Equal(before, EngineJson.Serialize(await inspected.GetObservationAsync(PlayerSide.Runner)));
        Assert.Equal(root.IntegrityHash, (await inspected.ExportDeveloperSnapshotAsync()).IntegrityHash);
        await inspected.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 10));
        await control.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 10));
        Assert.Equal((await control.ExportDeveloperSnapshotAsync()).IntegrityHash,
            (await inspected.ExportDeveloperSnapshotAsync()).IntegrityHash);
    }

    [Fact]
    public async Task AdvanceHonorsHumanTurnOutcomeAndActionLimits()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(62);
        await using var coordinator = new MatchCoordinator(Root(definition, PlayerSide.Runner), Options(definition, PlayerSide.Runner));
        await coordinator.StartAsync();
        DeveloperAdvanceResult human = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.HumanDecision));
        Assert.Equal(DeveloperStopReason.HumanDecision, human.Reason);
        Assert.Equal(PlayerSide.Runner, human.Snapshot.Observation.Decision!.OwnerSide);
        DeveloperAdvanceResult noOp = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.HumanDecision));
        Assert.Equal(0, noOp.AcceptedActions);
        ActionId issued = human.Snapshot.Observation.LegalActions[0].Id;
        Assert.Equal(1, (await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1, HumanAction: issued))).AcceptedActions);
        DeveloperAdvanceResult turn = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.Turn, 200, 1));
        Assert.Equal(DeveloperStopReason.Turn, turn.Reason);
        Assert.Equal(1, turn.Snapshot.View.Turn.TurnNumber);
        DeveloperAdvanceResult cap = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.Outcome, 1));
        Assert.Equal(DeveloperStopReason.ActionLimit, cap.Reason);
        DeveloperAdvanceResult outcome = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.Outcome, 2000));
        Assert.Equal(DeveloperStopReason.Outcome, outcome.Reason);
        Assert.True(outcome.Snapshot.IsTerminal);
        (await coordinator.ExportDeveloperSnapshotAsync()).Validate(definition.Behavior, definition.Runtime);
    }

    [Fact]
    public async Task CapabilityAndInvalidRequestsFailWithoutAdvancingOrStoppingWorker()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        await using var normal = new MatchCoordinator(definition, OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp),
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
        await normal.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => normal.ExportDeveloperSnapshotAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => normal.InspectDeveloperStateAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => normal.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1)));
        await using var developer = new MatchCoordinator(Root(definition, PlayerSide.Corp), Options(definition, PlayerSide.Corp));
        await developer.StartAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => developer.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => developer.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.Turn, 100)));
        await Assert.ThrowsAsync<ArgumentException>(() => developer.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1, HumanAction: new("action:invalid"))));
        Assert.Equal(0, developer.CurrentSnapshot!.ActionCount);
        Assert.Equal(1, (await developer.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1))).AcceptedActions);
    }

    [Fact]
    public async Task PauseAndCancellationStopLongAdvanceAtCommittedBoundary()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(78);
        await using var coordinator = new MatchCoordinator(Root(definition, PlayerSide.Runner), Options(definition, PlayerSide.Runner));
        await coordinator.StartAsync();
        Task<DeveloperAdvanceResult> advance = coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.Outcome, 10000));
        await coordinator.PauseAsync();
        Assert.Equal(DeveloperStopReason.Paused, (await advance.WaitAsync(TimeSpan.FromSeconds(5))).Reason);
        using var cancellation = new CancellationTokenSource();
        Task<DeveloperAdvanceResult> cancelled = coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.Outcome, 10000), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        // Barrier and explicit pause ensure the cancelled work cannot publish any later commit.
        await coordinator.PauseAsync();
        DeveloperSnapshotDocument saved = await coordinator.ExportDeveloperSnapshotAsync();
        Assert.True(coordinator.CurrentSnapshot!.IsPaused);
        saved.Validate(definition.Behavior, definition.Runtime);
        Assert.Equal(1, (await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1))).AcceptedActions);
    }

    [OfficialGatewayTheory]
    [MemberData(nameof(TacticalFixtureTests.ImplementedCases), MemberType = typeof(TacticalFixtureTests))]
    public async Task EveryTacticalBoundaryCanImportStepExportAndReplayWithoutSetup(string id)
    {
        CardDataSet data = CardDataResource.LoadEmbedded();
        var catalog = new GatewayTacticalCatalog(data);
        TacticalFixture fixture = catalog.Build(id);
        PlayerSide human = fixture.Metadata.Side;
        DeveloperSnapshotDocument root = DeveloperSnapshotDocument.Import(
            fixture.Initial.CreateSnapshot(fixture.Board.Behavior, fixture.Board.Runtime), human,
            fixture.Board.Behavior, fixture.Board.Runtime);
        await using var coordinator = new MatchCoordinator(root, new MatchCoordinatorOptions
        {
            EnableDeveloperTools = true,
            HumanSide = human,
            BehaviorCatalog = fixture.Board.Behavior,
            RuntimeCardCatalog = fixture.Board.Runtime,
            CardMetadataResolver = new CardCatalogMetadataResolver(data),
        });
        MatchSnapshot initial = await coordinator.StartAsync();
        Assert.Equal(DeterministicHash.ForState(fixture.Initial), DeterministicHash.ForState(root.Snapshot.ToState()));
        Assert.Equal(EngineJson.Serialize(ObservationBuilder.Build(fixture.Initial, human, fixture.Board.Behavior)), EngineJson.Serialize(initial.Observation));
        DeveloperAdvanceResult advanced = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1));
        Assert.True(advanced.AcceptedActions == 1, $"{id}: {advanced.Snapshot.Feedback?.Message}");
        (await coordinator.ExportDeveloperSnapshotAsync()).Validate(fixture.Board.Behavior, fixture.Board.Runtime);
        if (advanced.Snapshot.Observation.Decision is null)
        {
            DeveloperSnapshotDocument exported = await coordinator.ExportDeveloperSnapshotAsync();
            if (exported.Snapshot.PendingDecision is null && exported.Snapshot.Outcome.Kind == GameOutcomeKind.Ongoing)
            {
                DeveloperAdvanceResult settled = await coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.Outcome, 100));
                Assert.Equal(DeveloperStopReason.NoDecision, settled.Reason);
                Assert.Equal(0, settled.AcceptedActions);
                Assert.Equal(exported.IntegrityHash, (await coordinator.ExportDeveloperSnapshotAsync()).IntegrityHash);
            }
        }
    }

    private static DeveloperSnapshotDocument Root(FullMatchDefinition definition, PlayerSide human) =>
        DeveloperSnapshotDocument.Import(definition.Start().State.CreateSnapshot(definition.Behavior, definition.Runtime),
            human, definition.Behavior, definition.Runtime);

    private static MatchCoordinatorOptions Options(FullMatchDefinition definition, PlayerSide human) => new()
    {
        EnableDeveloperTools = true,
        HumanSide = human,
        BehaviorCatalog = definition.Behavior,
        RuntimeCardCatalog = definition.Runtime,
        CardMetadataResolver = definition.Metadata,
    };

    [Fact]
    public async Task PausingDuringEvaluationDiscardsLateResultWithoutConsumingPolicyRandomness()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(19);
        DeveloperSnapshotDocument root = Root(definition, PlayerSide.Runner);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new MatchCoordinator(root, new MatchCoordinatorOptions
        {
            EnableDeveloperTools = true,
            HumanSide = PlayerSide.Runner,
            BehaviorCatalog = definition.Behavior,
            RuntimeCardCatalog = definition.Runtime,
            Delay = async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { released.TrySetResult(); }
            },
        });
        await coordinator.StartAsync();
        Task<DeveloperAdvanceResult> advancing = coordinator.AdvanceDeveloperAsync(new(DeveloperAdvanceTarget.ActionLimit, 1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.PauseAsync();
        Assert.Equal(DeveloperStopReason.Paused, (await advancing).Reason);
        await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(root.IntegrityHash, (await coordinator.ExportDeveloperSnapshotAsync()).IntegrityHash);
        Assert.Equal(0, coordinator.CurrentSnapshot!.ActionCount);
    }
}
