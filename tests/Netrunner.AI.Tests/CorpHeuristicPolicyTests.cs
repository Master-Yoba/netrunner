using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class CorpHeuristicPolicyTests
{
    [Theory]
    [InlineData("standard")]
    [InlineData("easy")]
    public async Task WinningScoreOutranksEconomyAndSurvivesCheckpointRestore(string profile)
    {
        Observation observation = Decision(
            new(new("action:credit"), ActionKind.GainCredit, "score now", new NoActionParameters()),
            new(new("action:score"), ActionKind.ScoreAgenda, "ignore this", new ScoreAgendaParameters(new("card:agenda"), 7, 4)));
        var configuration = CorpPolicyProfiles.Create(profile, 53);
        OfflineAgent first = Agent(configuration);
        first.Observe(observation, 0);
        OfflineAgent restored = new(configuration, new CorpHeuristicPolicy(), new EmptyCatalog(),
            EngineJson.Deserialize<OfflineAgentCheckpoint>(EngineJson.Serialize(first.Checkpoint)), captureDiagnostics: true);
        Assert.Equal(new ActionId("action:score"), await first.ChooseActionAsync(observation));
        Assert.Equal(await first.ChooseActionAsync(observation), await restored.ChooseActionAsync(observation));
        Assert.Equal(EngineJson.Serialize(first.LastEvaluation), EngineJson.Serialize(restored.LastEvaluation));
        Assert.False(first.LastEvaluation!.BudgetExhausted);
    }

    [Fact]
    public async Task BudgetFallbackIsMarkedAndCannotConcealMalformedActions()
    {
        var configuration = OfflineAgentConfiguration.Create(PlayerSide.Corp, CorpHeuristicPolicy.PolicyVersion, "standard", 53,
            workBudget: 1, parameters: CorpPolicyProfiles.Create("standard", 53).Parameters);
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "", new NoActionParameters()));
        OfflineAgent agent = Agent(configuration);
        agent.Observe(observation, 0);
        Assert.Equal(new ActionId("action:credit"), await agent.ChooseActionAsync(observation));
        Assert.True(agent.LastEvaluation!.BudgetExhausted);
        Assert.True(Assert.Single(agent.LastEvaluation.Candidates).Utility.UsedFallback);
        Observation malformed = Decision(new LegalAction(new("action:wrong"), ActionKind.InstallCard, "", new NoActionParameters()));
        OfflineAgent other = Agent(configuration);
        other.Observe(malformed, 0);
        await Assert.ThrowsAsync<ArgumentException>(async () => await other.ChooseActionAsync(malformed));
        Assert.Equal(0, other.Checkpoint.CommittedDecisionCount);
    }

    [Fact]
    public void ProfilesBindSideVersionAndAllowedParameterSet()
    {
        var standard = CorpPolicyProfiles.Create("standard", 53);
        var easy = CorpPolicyProfiles.Create("easy", 53);
        CorpPolicyProfiles.Validate(standard);
        CorpPolicyProfiles.Validate(easy);
        Assert.NotEqual(standard.Digest, easy.Digest);
        Assert.True(easy.WorkBudget < standard.WorkBudget);
        CorpPolicyProfiles.Validate(CorpPolicyProfiles.Create("hard", 53));
        Assert.Throws<ArgumentException>(() => CorpPolicyProfiles.Create("nightmare", 53));
        Assert.Throws<ArgumentException>(() => CorpPolicyProfiles.Validate(OfflineAgentConfiguration.Create(PlayerSide.Runner,
            CorpHeuristicPolicy.PolicyVersion, "standard", 53, parameters: standard.Parameters)));
        Assert.Throws<ArgumentException>(() => CorpPolicyProfiles.Validate(OfflineAgentConfiguration.Create(PlayerSide.Corp,
            CorpHeuristicPolicy.PolicyVersion, "standard", 53, parameters: [new("unknown", 100)])));
    }

    private static OfflineAgent Agent(OfflineAgentConfiguration configuration) =>
        new(configuration, new CorpHeuristicPolicy(), new EmptyCatalog(), captureDiagnostics: true);

    private static Observation Decision(params LegalAction[] actions)
    {
        GameState state = GameState.CreateNew(53);
        var decision = new ObservationDecision(new("decision:corp-policy"), 1, state.Players.Single(player => player.Side == PlayerSide.Corp).Id,
            PlayerSide.Corp, DecisionKind.TurnAction, "ignored");
        return ObservationBuilder.Build(state, PlayerSide.Corp) with { DecisionId = decision.Id.Value, Decision = decision, LegalActions = actions };
    }

    private sealed class EmptyCatalog : ICardEvaluationCatalog
    {
        public string Version => "test-v1";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => [];
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) { descriptor = null!; return false; }
    }
}
