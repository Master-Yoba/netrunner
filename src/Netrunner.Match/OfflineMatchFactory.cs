using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Player-facing offline opponent selection; contract proofs retain their explicit factory.</summary>
public static class OfflineMatchFactory
{
    public static IGameAgent CreateTraceOpponent(FullMatchDefinition definition, PlayerSide humanSide, string traceDirectory,
        long? throughActionSequence = null)
    {
        SessionRecoveryResult recovered = SessionTrace.Recover(traceDirectory, definition.Behavior, definition.Runtime, throughActionSequence);
        if (recovered.Policy is { } policy) return policy.Restore(definition.Behavior, definition.Runtime);
        return new FullGameContractAgent(humanSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp, definition.Runtime);
    }

    /// <summary>Creates the opponent; <paramref name="policyVersion"/> defaults to the latest version for that seat.</summary>
    /// <summary>
    /// The tutorial bot is frozen (Wave 4): with the Catalyst and Syndicate teaching
    /// decks the opponent plays C1's versions, which are playable and beatable, and
    /// bot tuning targets the legal decks instead.
    /// </summary>
    public const string TutorialCorpVersion = CorpHeuristicPolicy.V7;

    /// <inheritdoc cref="TutorialCorpVersion"/>
    public const string TutorialRunnerVersion = RunnerHeuristicPolicy.V6;

    /// <summary>Whether both decks are the teaching decks (basic or expanded), so the tutorial bot plays.</summary>
    public static bool IsTutorial(FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        OfficialPresetCatalog catalog = OfficialPresetCatalog.Embedded();
        return new[] { definition.Request.CorpDeck.PresetId, definition.Request.RunnerDeck.PresetId }
            .All(id => id is not null && catalog.Teaching.Any(preset => preset.PresetId == id));
    }

    /// <summary>A new opponent for a player's game: the frozen tutorial bot on the teaching decks, the latest bot otherwise.</summary>
    public static OfflineAgent CreatePlayerOpponent(FullMatchDefinition definition, PlayerSide humanSide, string difficulty = "standard") =>
        CreateOpponent(definition, humanSide, difficulty, IsTutorial(definition)
            ? humanSide == PlayerSide.Corp ? TutorialRunnerVersion : TutorialCorpVersion
            : null);

    public static OfflineAgent CreateOpponent(FullMatchDefinition definition, PlayerSide humanSide, string difficulty = "standard",
        string? policyVersion = null)
    {
        PlayerSide side = humanSide switch
        {
            PlayerSide.Corp => PlayerSide.Runner,
            PlayerSide.Runner => PlayerSide.Corp,
            _ => throw new ArgumentOutOfRangeException(nameof(humanSide)),
        };
        ulong seed = AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, side);
        OfflineAgentConfiguration configuration = OfflinePolicies.Configure(side, difficulty, seed, policyVersion);
        return new(configuration, OfflinePolicies.Create(configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
    }
}
