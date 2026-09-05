using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

public static class FullMatchFactory
{
    public static MatchCoordinator CreateOfficial(
        string corpPresetId,
        string runnerPresetId,
        CardDataSet data,
        PlayerSide humanSide,
        ulong seed = 42,
        TimeSpan? agentDelay = null,
        OfficialPresetCatalog? officialPresets = null,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(corpPresetId);
        ArgumentNullException.ThrowIfNull(runnerPresetId);
        ArgumentNullException.ThrowIfNull(data);
        IBehaviorCatalog behavior = behaviorCatalog ?? GatewayBehaviorCatalog.Create();
        MatchRequest request = MatchRequestComposer.ComposeOfficial(
            corpPresetId,
            runnerPresetId,
            data,
            seed,
            officialPresets,
            behaviorCatalog: behavior);
        FullMatchDefinition definition = FullMatchDefinition.FromRequest(request, data, behavior);
        PlayerSide opponentSide = humanSide switch
        {
            PlayerSide.Corp => PlayerSide.Runner,
            PlayerSide.Runner => PlayerSide.Corp,
            _ => throw new ArgumentOutOfRangeException(nameof(humanSide)),
        };
        var opponent = new FullGameContractAgent(
            new FullGameContractPolicy(opponentSide),
            definition.Runtime);
        return new MatchCoordinator(
            definition,
            opponent,
            new MatchCoordinatorOptions
            {
                HumanSide = humanSide,
                AgentDelay = agentDelay ?? TimeSpan.Zero,
            });
    }

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
