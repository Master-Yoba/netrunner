using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class RunnerHeuristicPolicyTests
{
    [Fact]
    public async Task BudgetFallbackIsMarkedAndMalformedIssuedActionsStillFail()
    {
        OfflineAgentConfiguration standard = RunnerPolicyProfiles.Create("standard", 54);
        var constrained = OfflineAgentConfiguration.Create(PlayerSide.Runner, RunnerHeuristicPolicy.PolicyVersion, "standard", 54,
            workBudget: 1, parameters: standard.Parameters);
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()));
        var agent = new OfflineAgent(constrained, new RunnerHeuristicPolicy(), new EmptyCatalog(), captureDiagnostics: true);
        agent.Observe(observation, 0);
        Assert.Equal(new ActionId("action:credit"), await agent.ChooseActionAsync(observation));
        Assert.True(agent.LastEvaluation!.BudgetExhausted);
        Assert.True(Assert.Single(agent.LastEvaluation.Candidates).Utility.UsedFallback);

        Observation malformed = Decision(new LegalAction(new("action:bad"), ActionKind.InstallCard, "ignored", new NoActionParameters()));
        var rejected = new OfflineAgent(constrained, new RunnerHeuristicPolicy(), new EmptyCatalog(), captureDiagnostics: true);
        rejected.Observe(malformed, 0);
        await Assert.ThrowsAsync<ArgumentException>(async () => await rejected.ChooseActionAsync(malformed));
        Assert.Equal(0, rejected.Checkpoint.CommittedDecisionCount);
    }

    [Fact]
    public void ProfilesPinSideVersionAndAllowedParameters()
    {
        OfflineAgentConfiguration standard = RunnerPolicyProfiles.Create("standard", 54);
        OfflineAgentConfiguration easy = RunnerPolicyProfiles.Create("easy", 54);
        RunnerPolicyProfiles.Validate(standard);
        RunnerPolicyProfiles.Validate(easy);
        Assert.NotEqual(standard.Digest, easy.Digest);
        Assert.True(easy.WorkBudget < standard.WorkBudget);
        RunnerPolicyProfiles.Validate(RunnerPolicyProfiles.Create("hard", 54));
        Assert.Throws<ArgumentException>(() => RunnerPolicyProfiles.Create("nightmare", 54));
        Assert.Throws<ArgumentException>(() => RunnerPolicyProfiles.Validate(OfflineAgentConfiguration.Create(PlayerSide.Corp,
            RunnerHeuristicPolicy.PolicyVersion, "standard", 54, parameters: standard.Parameters)));
        Assert.Throws<ArgumentException>(() => RunnerPolicyProfiles.Validate(OfflineAgentConfiguration.Create(PlayerSide.Runner,
            RunnerHeuristicPolicy.PolicyVersion, "standard", 54, parameters: [new("unknown", 100)])));
    }

    [Theory]
    [InlineData(9, "action:credit")]
    [InlineData(10, "action:run")]
    public async Task ConditionalEndRunUsesCreditBalanceAfterEarlierLoss(int credits, string expected)
    {
        CardDefinitionId iceId = new("test:conditional-ice");
        CardDefinitionId agendaId = new("test:winning-agenda");
        var catalog = new ScenarioCatalog(
            new(new(iceId, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 2), null,
                new(iceId, 0, IceType.CodeGate, 2,
                    [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3), new EndRunIfCreditsAtMostSubroutineSpec(PlayerSide.Runner, 6)], []),
                null, null, null, null, [], false),
            new(new(agendaId, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3), null,
                null, null, null, null, new(agendaId, 2), [], false));
        Observation observation = Decision(
            new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()),
            new LegalAction(new("action:run"), ActionKind.StartRun, "ignored", new RunServerParameters(new("server:hq"))));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with
        {
            Servers = [new("server:hq", ServerKind.Headquarters,
                [new AgentCard { Id = "card:ice", DefinitionId = iceId.Value, Rezzed = true }],
                [new AgentCard { Id = "card:agenda", DefinitionId = agendaId.Value }])],
            Players = baseline.Players with { Runner = baseline.Players.Runner with { Credits = credits, AgendaPoints = 5 } },
        };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        var agent = new OfflineAgent(RunnerPolicyProfiles.Create("standard", 54), new RunnerHeuristicPolicy(), catalog);
        agent.Observe(observation, 0);

        Assert.Equal(new ActionId(expected), await agent.ChooseActionAsync(observation));
    }

    /// <summary>
    /// Runner v28: a face-down card in a remote is worth the agenda it may be, likelier behind ICE (the Corp protects its
    /// agendas and leaves assets and ambushes naked). v27 values both at the flat 190 and runs neither for it.
    /// </summary>
    [Fact]
    public async Task V28ValuesAProtectedFaceDownRemoteCardAsTheAgendaItMayBe()
    {
        CardDefinitionId iceId = new("test:taxing-ice"), agendaId = new("test:agenda"), assetId = new("test:asset");
        var catalog = new ScenarioCatalog(
            new(new(iceId, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 1), null,
                new(iceId, 0, IceType.CodeGate, 1, [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)], []), null, null, null, null, [], false),
            new(new(agendaId, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3), null,
                null, null, null, null, new(agendaId, 2), [], false),
            new(new(assetId, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 1, trashCost: 2), null, null, null, null, null, null, [], false));
        Observation observation = Decision(
            new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()),
            new LegalAction(new("action:protected"), ActionKind.StartRun, "ignored", new RunServerParameters(new("server:protected"))),
            new LegalAction(new("action:naked"), ActionKind.StartRun, "ignored", new RunServerParameters(new("server:naked"))));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with
        {
            Servers = [
                new("server:protected", ServerKind.Remote, [new AgentCard { Id = "card:ice", DefinitionId = iceId.Value, Rezzed = true }], [null]),
                new("server:naked", ServerKind.Remote, [], [null])],
            Players = baseline.Players with { Runner = baseline.Players.Runner with { Credits = 5 } },
        };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        async Task<(int Protected, int Naked)> Scores(string version)
        {
            var agent = new OfflineAgent(RunnerPolicyProfiles.Create("standard", 54, version), new RunnerHeuristicPolicy(version), catalog,
                captureDiagnostics: true);
            agent.Observe(observation, 0);
            await agent.ChooseActionAsync(observation);
            int Score(string action) => agent.LastEvaluation!.Candidates.Single(candidate => candidate.ActionId.Value == action).Utility.Score;
            return (Score("action:protected"), Score("action:naked"));
        }

        (int protectedBefore, int nakedBefore) = await Scores(RunnerHeuristicPolicy.V27);
        (int protectedAfter, int nakedAfter) = await Scores(RunnerHeuristicPolicy.V28);

        // Half the pool's root cards are agendas; behind ICE that is three in four, in a naked remote about one in eleven.
        Assert.InRange(protectedAfter - protectedBefore, 400, 600);
        Assert.InRange(nakedAfter - nakedBefore, 0, 100);
    }

    private static Observation Decision(params LegalAction[] actions)
    {
        GameState state = GameState.CreateNew(54);
        var decision = new ObservationDecision(new("decision:runner-policy"), 1, state.Players.Single(player => player.Side == PlayerSide.Runner).Id,
            PlayerSide.Runner, DecisionKind.TurnAction, "ignored");
        return ObservationBuilder.Build(state, PlayerSide.Runner) with { DecisionId = decision.Id.Value, Decision = decision, LegalActions = actions };
    }

    private sealed class EmptyCatalog : ICardEvaluationCatalog
    {
        public string Version => "test-v1";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => [];
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) { descriptor = null!; return false; }
    }

    private sealed class ScenarioCatalog(params CardEvaluationDescriptor[] descriptors) : ICardEvaluationCatalog
    {
        private readonly Dictionary<CardDefinitionId, CardEvaluationDescriptor> _definitions = descriptors
            .ToDictionary(descriptor => descriptor.Printed.DefinitionId);
        public string Version => "test-v1";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => _definitions.Keys;
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) =>
            _definitions.TryGetValue(definitionId, out descriptor!);
    }
}
