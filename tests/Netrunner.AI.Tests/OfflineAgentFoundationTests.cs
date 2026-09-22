using System.Collections.Immutable;
using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class OfflineAgentFoundationTests
{
    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public void NativeAndJsonObservationsProduceIdenticalDetachedFeatures(PlayerSide side)
    {
        Observation native = Decision(side);
        Observation json = EngineJson.Deserialize<Observation>(EngineJson.Serialize(native)) with { Decision = native.Decision };
        AgentFeatures features = ObservationReader.Read(native);
        Assert.Equal(ObservationReader.Digest(features), ObservationReader.Digest(ObservationReader.Read(json)));
        Assert.Null(features.Self.MaximumHandSize);
        Assert.Null(features.Self.CoreDamage);
        Assert.Null(features.Board.Players.Runner.MemoryLimit);
        Assert.Equal(5, features.Self.Credits);
        Assert.Throws<ArgumentException>(() => ObservationReader.Read(json with { Decision = null }));
        Assert.Throws<ArgumentException>(() => ObservationReader.Read(json with { Viewer = Opposite(side) }));
        var turn = Assert.IsType<Dictionary<string, object?>>(native.PublicState["turn"]);
        turn["turnNumber"] = 55;
        Assert.Equal(1, features.Board.Turn.TurnNumber);
    }

    [Fact]
    public async Task PromptsLabelsAndLegalActionOrderingCannotChangeScoresOrSeededChoice()
    {
        Observation first = Decision(PlayerSide.Runner);
        Observation changed = first with
        {
            Decision = first.Decision! with { Prompt = "misleading prompt" },
            LegalActions = first.LegalActions.Reverse().Select(action => new LegalAction(action.Id, action.Kind,
                "misleading description", action.Parameters)).ToArray(),
        };
        OfflineAgent a = Agent(PlayerSide.Runner);
        OfflineAgent b = Agent(PlayerSide.Runner);
        a.Observe(first, 0);
        b.Observe(changed, 0);
        Assert.Equal(await a.ChooseActionAsync(first), await b.ChooseActionAsync(changed));
        Assert.Equal(EngineJson.Serialize(a.LastEvaluation), EngineJson.Serialize(b.LastEvaluation));
        Assert.Equal(EngineJson.Serialize(a.Checkpoint), EngineJson.Serialize(b.Checkpoint));
    }

    [Fact]
    public async Task AllDecisionFamiliesReturnIssuedIdsWithoutChangingCommittedState()
    {
        foreach (DecisionKind kind in Enum.GetValues<DecisionKind>())
        {
            Observation observation = Decision(PlayerSide.Runner, kind);
            OfflineAgent agent = Agent(PlayerSide.Runner);
            agent.Observe(observation, 0);
            string before = EngineJson.Serialize(agent.Checkpoint);
            Assert.Contains(await agent.ChooseActionAsync(observation), observation.LegalActions.Select(action => action.Id));
            Assert.Equal(before, EngineJson.Serialize(agent.Checkpoint));
        }
    }

    [Fact]
    public async Task RetryDiscardCancelAndRestorePreserveExactlyTheNextRandomChoice()
    {
        Observation observation = Decision(PlayerSide.Corp);
        OfflineAgent agent = Agent(PlayerSide.Corp);
        agent.Observe(observation, 0);
        string initial = EngineJson.Serialize(agent.Checkpoint);
        ActionId selected = await agent.ChooseActionAsync(observation);
        Assert.Equal(selected, await agent.ChooseActionAsync(observation));
        agent.DiscardDecision();
        Assert.Equal(selected, await agent.ChooseActionAsync(observation));
        Assert.Throws<InvalidOperationException>(() => agent.CommitDecision(observation.Decision!.Id, 999, selected, 1));
        Assert.Equal(initial, EngineJson.Serialize(agent.Checkpoint));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await agent.ChooseActionAsync(observation, cancelled.Token));
        Assert.Throws<InvalidOperationException>(() => agent.CommitDecision(observation.Decision!.Id, 1, selected, 1));
        Assert.Equal(initial, EngineJson.Serialize(agent.Checkpoint));
        OfflineAgent restored = Agent(PlayerSide.Corp, EngineJson.Deserialize<OfflineAgentCheckpoint>(initial));
        restored.Observe(observation, 0); // duplicate restored delivery is idempotent
        Assert.Equal(selected, await restored.ChooseActionAsync(observation));
        Assert.Equal(selected, await agent.ChooseActionAsync(observation));
        Commit(agent, observation, selected, 1);
        Commit(restored, observation, selected, 1);
        Commit(agent, observation, selected, 1); // an accepted retry cannot consume RNG twice
        Assert.Equal(EngineJson.Serialize(agent.Checkpoint), EngineJson.Serialize(restored.Checkpoint));
        Assert.Equal(1, agent.Checkpoint.CommittedDecisionCount);
        Assert.NotEqual(agent.Configuration.PolicySeed, agent.Checkpoint.RandomState);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await agent.ChooseActionAsync(observation));
        Observation next = Decision(PlayerSide.Corp, revision: 2);
        agent.Observe(next, 1);
        restored.Observe(next, 1);
        Assert.Equal(await agent.ChooseActionAsync(next), await restored.ChooseActionAsync(next));
        Assert.Throws<InvalidOperationException>(() => agent.Observe(next, 3));
        Assert.Throws<InvalidOperationException>(() => agent.Observe(observation, 1));
        Assert.Throws<ArgumentException>(() => Agent(PlayerSide.Corp, agent.Checkpoint with { ConfigurationDigest = "other" }));
    }

    [Fact]
    public async Task CancellationDuringScoringAndBudgetExhaustionAreDeterministic()
    {
        Observation observation = Decision(PlayerSide.Runner);
        using var cancel = new CancellationTokenSource();
        var cancellingPolicy = new ConstantPolicy(() => cancel.Cancel());
        OfflineAgent cancelledAgent = Agent(PlayerSide.Runner, policy: cancellingPolicy);
        cancelledAgent.Observe(observation, 0);
        string before = EngineJson.Serialize(cancelledAgent.Checkpoint);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelledAgent.ChooseActionAsync(observation, cancel.Token));
        Assert.Equal(before, EngineJson.Serialize(cancelledAgent.Checkpoint));
        OfflineAgent bounded = Agent(PlayerSide.Runner, budget: 1);
        bounded.Observe(observation, 0);
        ActionId choice = await bounded.ChooseActionAsync(observation);
        Assert.Equal(1, bounded.LastEvaluation!.WorkUsed);
        Assert.True(bounded.LastEvaluation.BudgetExhausted);
        bounded.DiscardDecision();
        Assert.Equal(choice, await bounded.ChooseActionAsync(observation));
    }

    [Fact]
    public void CheckpointsForgetHiddenLocationsAndBoundHistoryWithoutLosingKnownDefinitions()
    {
        Observation visible = Decision(PlayerSide.Runner);
        var board = new Dictionary<string, object?>(visible.PublicState)
        {
            ["runnerRig"] = new[] { new { id = "card:seen", definitionId = "test:seen", rezzed = false, advancementCounters = 0 } },
        };
        OfflineAgent agent = Agent(PlayerSide.Runner);
        agent.Observe(visible with { PublicState = board }, 0);
        Assert.Single(agent.Checkpoint.Knowledge.VisibleCards);
        agent.Observe(visible, 1);
        Assert.Empty(agent.Checkpoint.Knowledge.VisibleCards);
        Assert.Equal("test:seen", Assert.Single(agent.Checkpoint.Knowledge.Reveals).DefinitionId);
        for (int i = 2; i < 280; i++) agent.Observe(visible with { PublicState = i % 2 == 0 ? board : visible.PublicState }, i);
        Assert.Equal(128, agent.Checkpoint.Knowledge.History.Length);
        Assert.Equal(EngineJson.Serialize(agent.Checkpoint), EngineJson.Serialize(
            EngineJson.Deserialize<OfflineAgentCheckpoint>(EngineJson.Serialize(agent.Checkpoint))));
    }

    [Fact]
    public async Task CommittedObjectiveReachesPolicyAndExpiresWhenItsVisibleTargetDisappears()
    {
        Observation original = Decision(PlayerSide.Runner);
        var board = new Dictionary<string, object?>(original.PublicState)
        {
            ["runnerRig"] = new[] { new { id = "card:objective-target", definitionId = "test:target" } },
        };
        Observation observation = original with { PublicState = board };
        var agent = new OfflineAgent(OfflineAgentConfiguration.Create(PlayerSide.Runner, "objective-policy-v1", "test", 42),
            new ObjectivePolicy(), new EmptyCatalog(), captureDiagnostics: true);
        agent.Observe(observation, 0);
        ActionId action = await agent.ChooseActionAsync(observation);
        Assert.Null(agent.Checkpoint.Objective);
        Commit(agent, observation, action, 1);
        Observation next = observation with { Decision = observation.Decision! with { Revision = 2 } };
        agent.Observe(next, 1);
        Assert.Equal("test-objective", agent.Checkpoint.Objective!.Key);
        await agent.ChooseActionAsync(next);
        Assert.All(agent.LastEvaluation!.Candidates, candidate => Assert.Equal("test-objective", candidate.Utility.Reason));
        agent.Observe(original, 2);
        Assert.Null(agent.Checkpoint.Objective);
    }

    [Fact]
    public async Task NearTieVariationNeverCrossesThePriorityBoundaryEvenAtIntegerExtremes()
    {
        Observation observation = Decision(PlayerSide.Runner);
        var agent = new OfflineAgent(OfflineAgentConfiguration.Create(PlayerSide.Runner, "priority-policy-v1", "test", 42,
            nearTieTolerance: int.MaxValue), new PriorityPolicy(), new EmptyCatalog());
        agent.Observe(observation, 0);
        ActionId selected = await agent.ChooseActionAsync(observation);
        Assert.Equal(new ActionId("action:1"), selected);
        Commit(agent, observation, selected, 1);
        Assert.Equal(42ul, agent.Checkpoint.RandomState); // no random draw for a unique acceptable choice
        Assert.Null(agent.LastEvaluation);
    }

    [Theory]
    [InlineData(PlayerSide.Runner, ZoneKind.CorpHand)]
    [InlineData(PlayerSide.Runner, ZoneKind.CorpDeck)]
    [InlineData(PlayerSide.Runner, ZoneKind.ServerIce)]
    [InlineData(PlayerSide.Runner, ZoneKind.CorpDiscard)]
    [InlineData(PlayerSide.Corp, ZoneKind.RunnerGrip)]
    [InlineData(PlayerSide.Corp, ZoneKind.RunnerStack)]
    public async Task HiddenStateTwinsHaveIdenticalFeaturesScoresChoicesAndNextCheckpoint(PlayerSide viewer, ZoneKind hiddenZone)
    {
        Observation a = ObserveTwin(viewer, hiddenZone, false);
        Observation b = ObserveTwin(viewer, hiddenZone, true);
        Assert.Equal(ObservationReader.Digest(ObservationReader.Read(a)), ObservationReader.Digest(ObservationReader.Read(b)));
        OfflineAgent first = Agent(viewer);
        OfflineAgent second = Agent(viewer);
        first.Observe(a, 0);
        second.Observe(b, 0);
        ActionId chosen = await first.ChooseActionAsync(a);
        Assert.Equal(chosen, await second.ChooseActionAsync(b));
        Assert.Equal(EngineJson.Serialize(first.LastEvaluation), EngineJson.Serialize(second.LastEvaluation));
        Commit(first, a, chosen, 1);
        Commit(second, b, chosen, 1);
        Assert.Equal(EngineJson.Serialize(first.Checkpoint), EngineJson.Serialize(second.Checkpoint));
    }

    [Fact]
    public void ConfigurationFreezesParametersAndRejectsTampering()
    {
        var parameters = new Dictionary<string, int> { ["reserve"] = 4 };
        OfflineAgentConfiguration config = OfflineAgentConfiguration.Create(PlayerSide.Corp, "test-policy-v1", "test", 42, parameters: parameters);
        parameters["reserve"] = 999;
        Assert.Equal(4, config.Parameters["reserve"]);
        OfflineAgentConfiguration restored = EngineJson.Deserialize<OfflineAgentConfiguration>(EngineJson.Serialize(config));
        restored.Validate();
        Assert.Equal(config.Digest, restored.Digest);
        Assert.Throws<ArgumentException>(() => (restored with { Parameters = restored.Parameters.SetItem("reserve", 5) }).Validate());
        Assert.Throws<ArgumentException>(() => (restored with { FeatureVersion = "future" }).Validate());
    }

    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public async Task CoreIssuedPrivateSearchTwinsDoNotExposeIneligibleCardsOrDeckOrder(PlayerSide side)
    {
        (GameState state, BehaviorCatalog behavior, RuntimeCardCatalog runtime) = AgentBoundaryScenario.Search(side, false);
        (GameState twin, _, _) = AgentBoundaryScenario.Search(side, true);
        Observation first = ObservationBuilder.Build(state, side, behavior);
        Observation second = ObservationBuilder.Build(twin, side, behavior);
        Assert.Equal(DecisionKind.BehaviorChoice, first.Decision!.Kind);
        AgentCard eligible = Assert.Single(ObservationReader.Read(first).PrivateCards.ChoiceCards);
        Assert.Equal("test:eligible", eligible.DefinitionId);
        Assert.DoesNotContain("test:hidden-b", EngineJson.Serialize(first), StringComparison.Ordinal);
        Assert.DoesNotContain("test:hidden-c", EngineJson.Serialize(first), StringComparison.Ordinal);
        await AssertSamePolicy(first, second);
        Observation observer = ObservationBuilder.Build(state, Opposite(side), behavior);
        Observation twinObserver = ObservationBuilder.Build(twin, Opposite(side), behavior);
        Assert.Null(observer.Decision);
        Assert.Empty(ObservationReader.Read(observer).PrivateCards.ChoiceCards);
        Assert.Equal(ObservationReader.Digest(ObservationReader.Read(observer)), ObservationReader.Digest(ObservationReader.Read(twinObserver)));
        Assert.Empty(StateInvariantValidator.Collect(state, behavior, runtime));
    }

    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public async Task CoreIssuedAccessTwinsRespectBothSeatsInformation(PlayerSide viewer)
    {
        GameState state = AgentBoundaryScenario.Access(viewer, false);
        GameState twin = AgentBoundaryScenario.Access(viewer, true);
        Observation a = ObservationBuilder.Build(state, viewer);
        Observation b = ObservationBuilder.Build(twin, viewer);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, ObservationReader.Read(a).Board.Run!.Step);
        Assert.Equal(ObservationReader.Digest(ObservationReader.Read(a)), ObservationReader.Digest(ObservationReader.Read(b)));
        if (a.Decision is not null) await AssertSamePolicy(a, b);
        else
        {
            OfflineAgent first = Agent(viewer);
            OfflineAgent second = Agent(viewer);
            first.Observe(a, 0);
            second.Observe(b, 0);
            Assert.Equal(EngineJson.Serialize(first.Checkpoint), EngineJson.Serialize(second.Checkpoint));
        }
    }

    private static async Task AssertSamePolicy(Observation first, Observation second)
    {
        OfflineAgent a = Agent(first.Viewer);
        OfflineAgent b = Agent(second.Viewer);
        a.Observe(first, 0);
        b.Observe(second, 0);
        ActionId choice = await a.ChooseActionAsync(first);
        Assert.Equal(choice, await b.ChooseActionAsync(second));
        Assert.Equal(EngineJson.Serialize(a.LastEvaluation), EngineJson.Serialize(b.LastEvaluation));
        Commit(a, first, choice, 1);
        Commit(b, second, choice, 1);
        Assert.Equal(EngineJson.Serialize(a.Checkpoint), EngineJson.Serialize(b.Checkpoint));
    }

    private static Observation ObserveTwin(PlayerSide viewer, ZoneKind kind, bool changed)
    {
        GameState baseline = GameState.CreateNew(42);
        ZoneState zone = baseline.Zones.First(zone => zone.Kind == kind);
        PlayerSide owner = Opposite(viewer);
        var one = new CardInstanceId("card:hidden-one");
        var two = new CardInstanceId("card:hidden-two");
        CardInstanceState[] cards =
        [
            new(one, new(changed ? "test:changed" : "test:hidden"), zone.Id, owner),
            new(two, new(changed ? "test:hidden" : "test:changed"), zone.Id, owner),
        ];
        GameState state = new(baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion, baseline.Players,
            cards, baseline.Zones.Select(value => value.Id == zone.Id ? new ZoneState(zone.Id, kind, owner, zone.ServerId,
                changed ? [two, one] : [one, two]) : value), baseline.Servers, baseline.Turn, baseline.Outcome,
            baseline.RandomState, baseline.Allocator, schemaVersion: baseline.SchemaVersion, initialSeed: baseline.InitialSeed);
        Observation observation = ObservationBuilder.Build(state, viewer);
        Observation envelope = Decision(viewer);
        return observation with { DecisionId = envelope.DecisionId, Decision = envelope.Decision, LegalActions = envelope.LegalActions };
    }

    private static PlayerSide Opposite(PlayerSide side) => side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
    private static void Commit(OfflineAgent agent, Observation observation, ActionId action, long sequence) =>
        agent.CommitDecision(observation.Decision!.Id, observation.Decision.Revision, action, sequence);

    private static OfflineAgent Agent(PlayerSide side, OfflineAgentCheckpoint? checkpoint = null, ConstantPolicy? policy = null, int budget = 100) =>
        new(OfflineAgentConfiguration.Create(side, "test-policy-v1", "test", 42, workBudget: budget), policy ?? new(), new EmptyCatalog(), checkpoint, captureDiagnostics: true);

    private static Observation Decision(PlayerSide side, DecisionKind kind = DecisionKind.TurnAction, long revision = 1)
    {
        GameState state = GameState.CreateNew(42);
        Observation observation = ObservationBuilder.Build(state, side);
        var decision = new ObservationDecision(new("decision:test"), revision, state.Players.Single(player => player.Side == side).Id, side, kind, "ignored");
        return observation with
        {
            DecisionId = decision.Id.Value,
            Decision = decision,
            LegalActions = Enumerable.Range(1, 4).Select(i => new LegalAction(new ActionId($"action:{i}"),
                ActionKind.ChooseNumber, "ignored", new NumberChoiceParameters(i))).ToArray(),
        };
    }

    private sealed class ConstantPolicy(Action? callback = null) : IOfflinePolicy
    {
        public string Version => "test-policy-v1";
        public AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
            OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget)
        {
            callback?.Invoke();
            return new(1, 0, "equal-test-utility");
        }
        public AgentUtility Fallback(AgentFeatures features, AgentAction action) => new(0, 0, "budget-fallback", true);
    }

    private sealed class EmptyCatalog : ICardEvaluationCatalog
    {
        public string Version => "test";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => ImmutableArray<CardDefinitionId>.Empty;
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) { descriptor = null!; return false; }
    }

    private sealed class ObjectivePolicy : IOfflinePolicy
    {
        public string Version => "objective-policy-v1";
        public AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
            OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget) =>
            new(0, 0, objective?.Key ?? "new", Objective: new("test-objective", null, null, "card:objective-target"));
        public AgentUtility Fallback(AgentFeatures features, AgentAction action) => new(0, 0, "fallback");
    }

    private sealed class PriorityPolicy : IOfflinePolicy
    {
        public string Version => "priority-policy-v1";
        public AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
            OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget) =>
            action.Parameters is NumberChoiceParameters { Value: 1 }
                ? new(1, int.MinValue, "required-choice") : new(0, int.MaxValue, "lower-priority");
        public AgentUtility Fallback(AgentFeatures features, AgentAction action) => new(0, 0, "fallback");
    }
}
