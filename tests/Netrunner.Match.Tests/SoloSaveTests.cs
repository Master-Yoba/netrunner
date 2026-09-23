using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class SoloSaveTests
{
    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public Task SaveWithoutTraceRestoresPausedAndMatchesSubsequentEnginePolicyAndHistory(PlayerSide human) =>
        AssertPortableContinuation(FullMatchDefinition.Developer(174), human);

    [OfficialGatewayTheory]
    [InlineData(PlayerSide.Corp, "basic")]
    [InlineData(PlayerSide.Runner, "basic")]
    [InlineData(PlayerSide.Corp, "expanded")]
    [InlineData(PlayerSide.Runner, "expanded")]
    public Task OfficialPresetsResumeWithTheSamePolicyAndAcceptedActions(PlayerSide human, string preset)
    {
        CardDataSet data = CardDataResource.LoadEmbedded();
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        MatchRequest request = MatchRequestComposer.ComposeOfficial($"system-gateway-{preset}-corp",
            $"system-gateway-{preset}-runner", data, 174, behaviorCatalog: behavior);
        return AssertPortableContinuation(FullMatchDefinition.FromRequest(request, data, behavior), human);
    }

    private static async Task AssertPortableContinuation(FullMatchDefinition definition, PlayerSide human)
    {
        await using var original = new MatchCoordinator(definition, Agent(definition, human),
            new MatchCoordinatorOptions { HumanSide = human });
        var history = new List<MatchHistoryEntry>();
        original.SnapshotPublished += snapshot => { lock (history) history.AddRange(snapshot.History); };
        await original.StartAsync();
        await AtHuman(original);
        SoloSaveDocument save = await original.CaptureSaveAsync("A portable match");
        SoloSaveDocument detached = EngineJson.Deserialize<SoloSaveDocument>(EngineJson.Serialize(save));
        detached.Validate(definition.Behavior, definition.Runtime);
        await using var resumed = new MatchCoordinator(definition, detached);
        MatchSnapshot restored = await resumed.StartAsync();
        Assert.True(restored.IsPaused);
        Assert.False(restored.IsOpponentThinking);
        Assert.Empty(restored.Events);
        Assert.Equal(EngineJson.Serialize(history), EngineJson.Serialize(restored.History));
        Assert.Equal(save.Payload.Snapshot.ActionSequence, restored.ActionCount);
        Assert.Equal(EngineJson.Serialize(original.CurrentSnapshot!.Observation), EngineJson.Serialize(restored.Observation));
        var humanAgent = new FullGameContractAgent(human, definition.Runtime);
        for (int step = 0; step < 20; step++)
        {
            await original.ContinueAsync();
            await resumed.ContinueAsync();
            MatchSnapshot left = await AtHuman(original);
            MatchSnapshot right = await AtHuman(resumed);
            Assert.Equal(EngineJson.Serialize(left.Observation), EngineJson.Serialize(right.Observation));
            if (left.IsTerminal) break;
            ActionId action = await humanAgent.ChooseActionAsync(left.Observation);
            Assert.True((await original.SubmitHumanActionAsync(action)).Accepted);
            Assert.True((await resumed.SubmitHumanActionAsync(action)).Accepted);
            await AtHuman(original);
            await AtHuman(resumed);
            SoloSaveDocument first = await original.CaptureSaveAsync("Original");
            SoloSaveDocument second = await resumed.CaptureSaveAsync("Restored");
            Assert.Equal(first.EngineStateHash, second.EngineStateHash);
            Assert.Equal(first.PolicyStateHash, second.PolicyStateHash);
            Assert.Equal(EngineJson.Serialize(first.Payload.Replay), EngineJson.Serialize(second.Payload.Replay));
        }
    }

    [Fact]
    public async Task ReadinessWaitCanImmediatelyFollowQueuedStart()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Runner));
        Task<MatchSnapshot> start = coordinator.StartAsync();
        MatchSnapshot ready = await coordinator.WaitForHumanDecisionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await start;
        Assert.True(ready.AwaitingHumanDecision);
        Assert.Equal(PlayerSide.Runner, ready.Observation.Decision!.OwnerSide);
    }

    [Fact]
    public async Task ReadinessWaitFlushesQueuedSubmissionBeforeChoosingItsBoundary()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Runner));
        await coordinator.StartAsync();
        MatchSnapshot first = await coordinator.WaitForHumanDecisionAsync();
        LegalAction keep = first.Observation.LegalActions.Single(action => action.Parameters is NumberChoiceParameters { Value: 0 });
        Task<MatchSubmissionResult> pending = coordinator.SubmitHumanActionAsync(keep.Id);
        Observation next = await coordinator.GetReadyObservationAsync(PlayerSide.Runner).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await pending).Accepted);
        Assert.NotNull(next.Decision);
        Assert.NotEqual(first.Observation.Decision!.Id, next.Decision.Id);
        Assert.Equal(PlayerSide.Runner, next.Decision.OwnerSide);
    }

    [Fact]
    public async Task PauseDiscardsLateNoncooperativeAgentResultBeforeContinuing()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var agent = new DelayedAgent(entered, release);
        await using var coordinator = new MatchCoordinator(definition, agent);
        await coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        MatchSnapshot paused = await coordinator.PauseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, paused.ActionCount);
        Assert.True(paused.IsPaused);
        await coordinator.ContinueAsync();
        release.TrySetResult();
        MatchSnapshot human = await AtHuman(coordinator);
        Assert.Equal(1, human.ActionCount);
        Assert.Equal(2, agent.Calls);
    }

    [Fact]
    public async Task ContractPolicyRoundTripsWithItsExactActionPreferences()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var agent = new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Runner,
            bioroidSubroutinePreference: BioroidSubroutinePreference.HighestOrdinalFirst), definition.Runtime);
        await using var coordinator = new MatchCoordinator(definition, agent, new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
        await coordinator.StartAsync();
        SoloSaveDocument save = await coordinator.CaptureSaveAsync("Contract");
        SoloSaveDocument restored = EngineJson.Deserialize<SoloSaveDocument>(EngineJson.Serialize(save));
        restored.Validate(definition.Behavior, definition.Runtime);
        Assert.Equal(BioroidSubroutinePreference.HighestOrdinalFirst, restored.Payload.Opponent.ContractPolicy!.BioroidSubroutinePreference);
    }

    [Fact]
    public async Task AutomaticCaptureLeavesThinkingInFlightAndDoesNotDiscardItsDecision()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Runner),
            new MatchCoordinatorOptions
            {
                Delay = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); },
            });
        await coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        SoloSaveDocument automatic = await coordinator.CaptureAutoSaveAsync("Automatic", Guid.NewGuid().ToString("N"));
        Assert.Equal(0, automatic.Payload.Snapshot.ActionSequence);
        Assert.False(coordinator.CurrentSnapshot!.IsPaused);
        Assert.True(coordinator.CurrentSnapshot.IsOpponentThinking);
        release.TrySetResult();
        await coordinator.WaitForHumanDecisionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        SoloSaveDocument after = await coordinator.CaptureSaveAsync("After thinking");
        Assert.Equal(2, after.Payload.Opponent.Checkpoint!.CommittedDecisionCount); // Mulligan, then keep.
        Assert.Equal(2, after.Payload.Snapshot.ActionSequence);
    }

    [Fact]
    public async Task CaptureDuringThinkingCancelsTentativeWorkAndLeavesExactlyOneBoundary()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Runner),
            new MatchCoordinatorOptions
            {
                HumanSide = PlayerSide.Runner,
                Delay = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); },
            });
        await coordinator.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        SoloSaveDocument save = await coordinator.CaptureSaveAsync("While thinking").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, save.Payload.Snapshot.ActionSequence);
        Assert.Equal(0, save.Payload.Opponent.Checkpoint!.ObservationCursor);
        Assert.Equal(0, save.Payload.Opponent.Checkpoint.CommittedDecisionCount);
        Assert.True(coordinator.CurrentSnapshot!.IsPaused);
        await coordinator.GetObservationAsync(PlayerSide.Runner).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, coordinator.CurrentSnapshot.ActionCount);
        await using var restored = new MatchCoordinator(definition, save);
        await restored.StartAsync();
        Assert.True(restored.CurrentSnapshot!.IsPaused);
        await restored.ContinueAsync();
        Assert.True((await AtHuman(restored)).ActionCount > 0);
    }

    [Fact]
    public async Task StorageKeepsVerifiedBackupAndIsolatesCorruptFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-save-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer();
            await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Corp),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
            await coordinator.StartAsync();
            SoloSaveDocument first = await coordinator.CaptureSaveAsync("First");
            using var store = new SoloSaveStore(definition.Behavior, definition.Runtime, directory);
            await store.WriteAsync(first);
            SoloSaveDocument second = await coordinator.CaptureSaveAsync("Second", first.Metadata.SaveId, first.Metadata.CreatedUtc);
            await store.WriteAsync(second);
            Assert.Equal("Second", (await store.LoadAsync(first.Metadata.SaveId)).Metadata.Name);
            Assert.Equal("First", (await store.LoadAsync(first.Metadata.SaveId, backup: true)).Metadata.Name);
            string path = Path.Combine(directory, first.Metadata.SaveId + ".solo.json");
            await File.WriteAllTextAsync(path, "{interrupted");
            await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".solo.json"), "null");
            IReadOnlyList<SoloSaveBrowserEntry> rows = await store.BrowseAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.False(row.IsCompatible));
            Assert.Equal("First", Assert.Single(rows, row => row.Backup is not null).Backup!.Name);
            await store.WriteAsync(second);
            Assert.Equal("First", (await store.LoadAsync(first.Metadata.SaveId, backup: true)).Metadata.Name);
            Assert.Equal(second.IntegrityHash, (await store.LoadAsync(first.Metadata.SaveId)).IntegrityHash);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ChecksumsPolicyCursorsAndCatalogPinsFailClosed()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Corp),
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
        await coordinator.StartAsync();
        SoloSaveDocument save = await coordinator.CaptureSaveAsync("Valid");
        Assert.Throws<InvalidOperationException>(() => (save with { Metadata = save.Metadata with { Name = "Tampered" } }).ValidateIntegrity());
        SoloSaveDocument wrongCursor = SoloSaveDocument.Create(save.Metadata, save.Payload with
        {
            Opponent = save.Payload.Opponent with { Checkpoint = save.Payload.Opponent.Checkpoint! with { ObservationCursor = 10 } },
        });
        Assert.Throws<InvalidOperationException>(() => wrongCursor.Validate(definition.Behavior, definition.Runtime));
        Assert.ThrowsAny<Exception>(() => save.Validate(GatewayBehaviorCatalog.Create(), definition.Runtime));
        Assert.Throws<InvalidOperationException>(() => (save with { SchemaVersion = 99 }).ValidateIntegrity());
    }

    [Fact]
    public async Task PolicyTraceRecoversCommittedCheckpointAndPortablePrefixAcrossTwoRecoveries()
    {
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-policy-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(73);
            string first = Path.Combine(directory, "first");
            SoloSaveDocument saved;
            await using (var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Runner),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Runner, SessionTraceDirectory = first }))
            {
                await coordinator.StartAsync();
                MatchSnapshot human = await AtHuman(coordinator);
                await coordinator.SubmitHumanActionAsync(human.Observation.LegalActions[0].Id);
                await AtHuman(coordinator);
                saved = await coordinator.CaptureSaveAsync("Before recovery");
            }
            Assert.Equal(SessionTrace.PolicySchemaVersion, SessionTrace.ReadManifest(first).SchemaVersion);
            // v2's atomic engine/policy envelope remains authoritative if a crash
            // interrupts publication of the derived legacy replay file.
            await File.WriteAllTextAsync(Path.Combine(first, SessionTrace.ReplayFileName), "{stale-derived-file");
            SessionRecoveryResult recovered = SessionTrace.Recover(first, definition.Behavior, definition.Runtime);
            Assert.Equal(saved.PolicyStateHash, BenchmarkHash.Value(recovered.Policy));
            Assert.Equal(saved.EngineStateHash, DeterministicHash.ForState(recovered.State));
            long sequence = saved.Payload.Replay.Actions[0].Sequence;
            SessionRecoveryResult earlier = SessionTrace.Recover(first, definition.Behavior, definition.Runtime, sequence);
            Assert.Equal(sequence, earlier.Policy!.Checkpoint!.ObservationCursor);
            string previous = first;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                string next = Path.Combine(directory, "resume-" + cycle);
                await using (var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Runner),
                    new MatchCoordinatorOptions { HumanSide = PlayerSide.Runner, ResumeTraceDirectory = previous, SessionTraceDirectory = next }))
                {
                    MatchSnapshot snapshot = await coordinator.StartAsync();
                    Assert.True(snapshot.IsPaused);
                    SoloSaveDocument portable = await coordinator.CaptureSaveAsync("Recovered");
                    portable.Validate(definition.Behavior, definition.Runtime);
                    Assert.Equal(saved.EngineStateHash, portable.EngineStateHash);
                    Assert.Equal(saved.PolicyStateHash, portable.PolicyStateHash);
                    Assert.Equal(EngineJson.Serialize(saved.Payload.Replay), EngineJson.Serialize(portable.Payload.Replay));
                }
                Directory.Delete(previous, true);
                previous = next;
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TerminalSaveRestoresOutcomeAndNeverStartsAnotherAgentDecision()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(421);
        await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Runner));
        await coordinator.StartAsync();
        var human = new FullGameContractAgent(PlayerSide.Runner, definition.Runtime);
        for (int action = 0; action < 300; action++)
        {
            MatchSnapshot snapshot = await coordinator.WaitForHumanDecisionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (snapshot.IsTerminal) break;
            Assert.True((await coordinator.SubmitHumanActionAsync(await human.ChooseActionAsync(snapshot.Observation))).Accepted);
        }
        Assert.True(coordinator.CurrentSnapshot!.IsTerminal);
        SoloSaveDocument save = await coordinator.CaptureSaveAsync("Finished");
        await using var restored = new MatchCoordinator(definition,
            EngineJson.Deserialize<SoloSaveDocument>(EngineJson.Serialize(save)));
        MatchSnapshot loaded = await restored.StartAsync();
        Assert.True(loaded.IsTerminal);
        Assert.Empty(loaded.Events);
        await restored.ContinueAsync();
        SoloSaveDocument again = await restored.CaptureSaveAsync("Still finished");
        Assert.Equal(save.EngineStateHash, again.EngineStateHash);
        Assert.Equal(save.PolicyStateHash, again.PolicyStateHash);
    }

    [Fact]
    public async Task FailedBackgroundWriteLeavesMatchUsable()
    {
        string path = Path.Combine(Path.GetTempPath(), "netrunner-blocked-store-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "A file prevents directory creation.");
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer();
            await using var coordinator = new MatchCoordinator(definition, Agent(definition, PlayerSide.Corp),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
            await coordinator.StartAsync();
            SoloSaveDocument save = await coordinator.CaptureSaveAsync("Failed write");
            using var store = new SoloSaveStore(definition.Behavior, definition.Runtime, path);
            await Assert.ThrowsAnyAsync<IOException>(() => store.WriteAsync(save));
            await coordinator.ContinueAsync();
            MatchSnapshot ready = await coordinator.WaitForHumanDecisionAsync();
            Assert.True((await coordinator.SubmitHumanActionAsync(ready.Observation.LegalActions[0].Id)).Accepted);
            Assert.True(coordinator.CurrentSnapshot!.ActionCount > 0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AtomicWriteValidationFailurePreservesCurrentAndBackup()
    {
        string path = Path.Combine(Path.GetTempPath(), "netrunner-atomic-" + Guid.NewGuid().ToString("N"));
        try
        {
            AtomicFile.Write(path, [1]);
            AtomicFile.Write(path, [2], backupPath: path + ".bak");
            Assert.Throws<IOException>(() => AtomicFile.Write(path, [3], _ => throw new IOException("Injected validation failure"), path + ".bak"));
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path + ".bak"));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); }
    }

    private static OfflineAgent Agent(FullMatchDefinition definition, PlayerSide human)
    {
        PlayerSide side = human == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        ulong seed = AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, side);
        return new(side == PlayerSide.Corp ? CorpPolicyProfiles.Create("standard", seed) : RunnerPolicyProfiles.Create("standard", seed),
            side == PlayerSide.Corp ? new CorpHeuristicPolicy() : new RunnerHeuristicPolicy(),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
    }

    private sealed class DelayedAgent(TaskCompletionSource entered, TaskCompletionSource release) : IGameAgent
    {
        public int Calls { get; private set; }
        public async ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
        {
            Calls++;
            entered.TrySetResult();
            await release.Task;
            return observation.LegalActions[0].Id;
        }
    }

    private static async Task<MatchSnapshot> AtHuman(MatchCoordinator coordinator)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            MatchSnapshot? snapshot = coordinator.CurrentSnapshot;
            if (snapshot?.Feedback?.IsError == true) throw new InvalidOperationException(snapshot.Feedback.Message);
            if (snapshot is { IsTerminal: true } or { AwaitingHumanDecision: true }) return snapshot;
            await Task.Delay(5, timeout.Token);
        }
    }
}
