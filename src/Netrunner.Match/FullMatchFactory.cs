using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

public static class FullMatchFactory
{
    public static MatchCoordinator CreateDeveloper(PlayerSide humanSide, ulong seed = 42, TimeSpan? agentDelay = null)
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(seed);
        var opponent = new FullGameContractAgent(new FullGameContractPolicy(
            humanSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp), definition.Runtime);
        return new MatchCoordinator(definition, opponent,
            new MatchCoordinatorOptions { HumanSide = humanSide, AgentDelay = agentDelay ?? TimeSpan.Zero });
    }

    public static Func<Observation, CancellationToken, ValueTask<ActionId>> CreateContractPolicy(PlayerSide side, IRuntimeCardCatalog? runtimeCardCatalog = null)
    {
        var agent = new FullGameContractAgent(new FullGameContractPolicy(side), runtimeCardCatalog);
        return agent.ChooseActionAsync;
    }
}
