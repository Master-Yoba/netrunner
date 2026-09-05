using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

public static class FullMatchFactory
{
    public static MatchCoordinator CreateDeveloper(PlayerSide humanSide, ulong seed = 42, TimeSpan? agentDelay = null)
    {
        var opponent = new FullGameContractAgent(new FullGameContractPolicy(
            humanSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp));
        return new MatchCoordinator(FullMatchDefinition.Developer(seed), opponent,
            new MatchCoordinatorOptions { HumanSide = humanSide, AgentDelay = agentDelay ?? TimeSpan.Zero });
    }

    public static Func<Observation, CancellationToken, ValueTask<ActionId>> CreateContractPolicy(PlayerSide side)
    {
        var agent = new FullGameContractAgent(new FullGameContractPolicy(side));
        return agent.ChooseActionAsync;
    }
}
