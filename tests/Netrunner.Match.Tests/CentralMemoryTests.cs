using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class CentralMemoryTests
{
    [Fact]
    public async Task TheRunnerRemembersItsCentralAccessesFromPublicCounts()
    {
        // The synthetic developer decks rarely make a successful central run; the official lane covers it.
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        bool rd = false, hq = false;
        foreach (ulong seed in new ulong[] { 3, 4, 5, 6 })
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(RulesFuzzRunner.OfficialContentIds[0], seed);
            OfflineAgent corp = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", CorpHeuristicPolicy.V8);
            OfflineAgent runner = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", RunnerHeuristicPolicy.V10);
            var memory = new List<AgentCentralMemory?>();
            await FullMatchRunner.RunAsync(definition, 3000, corp, new Watching(runner, memory));
            // Every recorded size is one the Runner could see, and memory never goes backwards in the deck.
            Assert.All(memory.OfType<AgentCentralMemory>(), value =>
            {
                Assert.True(value.DeckAfterRdAccess is null or >= 0);
                Assert.True(value.HandAfterHqAccess is null or >= 0);
            });
            rd |= memory.Any(value => value?.DeckAfterRdAccess is not null);
            hq |= memory.Any(value => value?.HandAfterHqAccess is not null);
            if (rd && hq) break;
        }
        Assert.True(rd && hq, $"No remembered access: R&D {rd}, HQ {hq}.");
    }

    [Fact]
    public void OlderCheckpointsLoadWithoutCentralMemory()
    {
        AgentKnowledge knowledge = EngineJson.Deserialize<AgentKnowledge>(EngineJson.Serialize(AgentKnowledge.Empty));
        Assert.Null(knowledge.Centrals);
        string json = EngineJson.Serialize(AgentKnowledge.Empty with { Centrals = new AgentCentralMemory(30, null, null) });
        Assert.Equal(30, EngineJson.Deserialize<AgentKnowledge>(json).Centrals!.DeckAfterRdAccess);
    }

    private sealed class Watching(OfflineAgent agent, List<AgentCentralMemory?> memory) : IAgentLifecycle
    {
        public PlayerSide ControlledSide => agent.ControlledSide;
        public void Observe(Observation observation, long actionSequence)
        {
            agent.Observe(observation, actionSequence);
            memory.Add(agent.Checkpoint.Knowledge.Centrals);
        }
        public void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence) =>
            agent.CommitDecision(decisionId, revision, actionId, actionSequence);
        public void DiscardDecision() => agent.DiscardDecision();
        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default) =>
            agent.ChooseActionAsync(observation, cancellationToken);
    }
}
