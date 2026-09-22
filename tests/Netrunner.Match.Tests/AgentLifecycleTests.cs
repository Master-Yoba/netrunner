using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class AgentLifecycleTests
{
    [Fact]
    public async Task RestoringAndRetryingTheStatefulDriverAtEveryDecisionPreservesFullReplayAndCheckpoints()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        var corp = new RestoringAgent(PlayerSide.Corp, catalog, false);
        var runner = new RestoringAgent(PlayerSide.Runner, catalog, false);
        FullMatchResult uninterrupted = await FullMatchRunner.RunAsync(definition, corpAgent: corp, runnerAgent: runner);
        var restoredCorp = new RestoringAgent(PlayerSide.Corp, catalog, true);
        var restoredRunner = new RestoringAgent(PlayerSide.Runner, catalog, true);
        FullMatchResult restored = await FullMatchRunner.RunAsync(definition, corpAgent: restoredCorp, runnerAgent: restoredRunner);
        Assert.Equal(EngineJson.Serialize(uninterrupted.Replay), EngineJson.Serialize(restored.Replay));
        Assert.Equal(EngineJson.Serialize(corp.Checkpoint), EngineJson.Serialize(restoredCorp.Checkpoint));
        Assert.Equal(EngineJson.Serialize(runner.Checkpoint), EngineJson.Serialize(restoredRunner.Checkpoint));
        Assert.Equal(uninterrupted.ActionCount, corp.Checkpoint.ObservationCursor);
        Assert.Equal(uninterrupted.ActionCount, corp.Checkpoint.CommittedDecisionCount + runner.Checkpoint.CommittedDecisionCount);
    }

    [Fact]
    public async Task BothAgentsReceiveEveryAcceptedBoundaryWithoutOpponentDecisions()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var corp = new AuditedAgent(PlayerSide.Corp, definition.Runtime);
        var runner = new AuditedAgent(PlayerSide.Runner, definition.Runtime);
        FullMatchResult result = await FullMatchRunner.RunAsync(definition, corpAgent: corp, runnerAgent: runner);
        Assert.Equal(result.ActionCount + 1, corp.Observations.Count);
        Assert.Equal(result.ActionCount + 1, runner.Observations.Count);
        Assert.Equal(result.ActionCount, corp.Commits + runner.Commits);
        Assert.Contains(corp.Observations, observation => observation.Decision is null);
        Assert.Contains(runner.Observations, observation => observation.Decision is null);
        Assert.Equal(GameOutcomeKind.Won, ObservationReader.Read(corp.Observations[^1]).Board.Outcome.Kind);
        FullMatchResult legacy = await FullMatchRunner.RunAsync(definition);
        Assert.Equal(EngineJson.Serialize(legacy.Replay), EngineJson.Serialize(result.Replay));
    }

    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public async Task CoordinatorObservesHumanAndOpponentActionsInBothSeats(PlayerSide human)
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var opponent = new AuditedAgent(human == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp, definition.Runtime);
        var humanAgent = new FullGameContractAgent(human);
        await using var coordinator = new MatchCoordinator(definition, opponent, new MatchCoordinatorOptions { HumanSide = human });
        await coordinator.StartAsync();
        for (int i = 0; i < 100; i++)
        {
            Observation observation = await coordinator.GetObservationAsync(human);
            if (coordinator.CurrentSnapshot!.IsTerminal) break;
            Assert.False(coordinator.CurrentSnapshot.Feedback?.IsError == true, coordinator.CurrentSnapshot.Feedback?.Message);
            Assert.True((await coordinator.SubmitHumanActionAsync(await humanAgent.ChooseActionAsync(observation))).Accepted);
        }
        Assert.True(opponent.Commits > 0);
        Assert.True(opponent.Observations.Count > opponent.Commits + 1);
    }

    [Fact]
    public async Task RejectedAndCancelledAgentChoicesAreDiscardedWithoutCommit()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var illegal = new AuditedAgent(PlayerSide.Corp, definition.Runtime) { Illegal = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => FullMatchRunner.RunAsync(definition, corpAgent: illegal));
        Assert.Equal(0, illegal.Commits);
        Assert.Equal(1, illegal.Discards);
        using var source = new CancellationTokenSource();
        var cancelled = new AuditedAgent(PlayerSide.Corp, definition.Runtime) { Cancel = source };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FullMatchRunner.RunAsync(definition, corpAgent: cancelled, cancellationToken: source.Token));
        Assert.Equal(0, cancelled.Commits);
        Assert.Equal(1, cancelled.Discards);
    }

    [Fact]
    public void PolicySeedsAreStableDomainSeparatedAndLegacyRecoveryCannotInventMemory()
    {
        ulong corp = AgentLifecycleHost.DerivePolicySeed(42, PlayerSide.Corp);
        ulong runner = AgentLifecycleHost.DerivePolicySeed(42, PlayerSide.Runner);
        Assert.NotEqual(corp, runner);
        Assert.NotEqual(42ul, corp);
        Assert.Equal(corp, AgentLifecycleHost.DerivePolicySeed(42, PlayerSide.Corp));
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        Assert.Throws<ArgumentException>(() => new MatchCoordinator(definition,
            new AuditedAgent(PlayerSide.Corp, definition.Runtime), new MatchCoordinatorOptions { ResumeTraceDirectory = "legacy" }));
    }

    private sealed class RestoringAgent : IAgentLifecycle
    {
        private readonly ICardEvaluationCatalog _catalog;
        private readonly bool _restore;
        private OfflineAgent _agent;
        public RestoringAgent(PlayerSide side, ICardEvaluationCatalog catalog, bool restore)
        {
            _catalog = catalog;
            _restore = restore;
            _agent = new(OfflineAgentConfiguration.Create(side, "fixture-policy-v1", "test", AgentLifecycleHost.DerivePolicySeed(42, side)),
                new FixturePolicy(), catalog);
        }
        public PlayerSide ControlledSide => _agent.ControlledSide;
        public OfflineAgentCheckpoint Checkpoint => _agent.Checkpoint;
        public void Observe(Observation observation, long actionSequence) => _agent.Observe(observation, actionSequence);
        public void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence) =>
            _agent.CommitDecision(decisionId, revision, actionId, actionSequence);
        public void DiscardDecision() => _agent.DiscardDecision();
        public async ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
        {
            ActionId choice = await _agent.ChooseActionAsync(observation, cancellationToken);
            Assert.Null(_agent.LastEvaluation); // diagnostics are opt-in
            if (_restore)
            {
                _agent.DiscardDecision();
                _agent = new(_agent.Configuration, new FixturePolicy(), _catalog,
                    EngineJson.Deserialize<OfflineAgentCheckpoint>(EngineJson.Serialize(_agent.Checkpoint)));
                _agent.Observe(observation, _agent.Checkpoint.ObservationCursor);
                Assert.Equal(choice, await _agent.ChooseActionAsync(observation, cancellationToken));
            }
            return choice;
        }
    }

    private sealed class FixturePolicy : IOfflinePolicy
    {
        public string Version => "fixture-policy-v1";
        public AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
            OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget) =>
            new(0, action.Kind switch
            {
                ActionKind.EndTurn => 50,
                ActionKind.PassPriority => 40,
                ActionKind.ChooseNumber => 30,
                ActionKind.ChooseCard or ActionKind.GainCredit => 20,
                _ => 0,
            }, "fixture-turn-progression");
        public AgentUtility Fallback(AgentFeatures features, AgentAction action) => new(0, 0, "fixture-fallback");
    }

    private sealed class AuditedAgent(PlayerSide side, IRuntimeCardCatalog runtime) : IAgentLifecycle
    {
        private readonly FullGameContractAgent _agent = new(new FullGameContractPolicy(side), runtime);
        private Observation? _chosen;
        public PlayerSide ControlledSide => side;
        public List<Observation> Observations { get; } = [];
        public int Commits { get; private set; }
        public int Discards { get; private set; }
        public bool Illegal { get; init; }
        public CancellationTokenSource? Cancel { get; init; }
        public void Observe(Observation observation, long actionSequence)
        {
            Assert.Equal(Observations.Count, actionSequence);
            Assert.Equal(side, observation.Viewer);
            if (observation.Decision is null) Assert.Empty(observation.LegalActions);
            AgentFeatures typed = ObservationReader.Read(observation);
            Observation json = EngineJson.Deserialize<Observation>(EngineJson.Serialize(observation)) with { Decision = observation.Decision };
            Assert.Equal(ObservationReader.Digest(typed), ObservationReader.Digest(ObservationReader.Read(json)));
            Observations.Add(observation);
        }
        public async ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
        {
            _chosen = observation;
            Cancel?.Cancel();
            return Illegal ? new("action:unissued") : await _agent.ChooseActionAsync(observation, cancellationToken);
        }
        public void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence)
        {
            Assert.Equal(_chosen!.Decision!.Id, decisionId);
            Assert.Equal(_chosen.Decision.Revision, revision);
            Assert.Contains(actionId, _chosen.LegalActions.Select(action => action.Id));
            Assert.Equal(Observations.Count, actionSequence);
            Commits++;
            _chosen = null;
        }
        public void DiscardDecision() { Discards++; _chosen = null; }
    }
}
