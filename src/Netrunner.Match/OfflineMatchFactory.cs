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

    /// <summary>
    /// A new opponent for a player's game: the frozen tutorial bot on the teaching decks, the latest bot otherwise.
    /// The tutorial bot has no Hard tier or personalities (A6), so it plays Standard for Hard and ignores a personality.
    /// </summary>
    public static OfflineAgent CreatePlayerOpponent(FullMatchDefinition definition, PlayerSide humanSide, string difficulty = "standard",
        AgentPersonality personality = AgentPersonality.Balanced) =>
        IsTutorial(definition)
            ? CreateOpponent(definition, humanSide, difficulty == "hard" ? "standard" : difficulty,
                humanSide == PlayerSide.Corp ? TutorialRunnerVersion : TutorialCorpVersion)
            : CreateOpponent(definition, humanSide, difficulty, null, personality);

    /// <summary>The difficulties a player can choose (A6), weakest first.</summary>
    public static IReadOnlyList<string> Difficulties { get; } = ["easy", "standard", "hard"];

    public static OfflineAgent CreateOpponent(FullMatchDefinition definition, PlayerSide humanSide, string difficulty = "standard",
        string? policyVersion = null, AgentPersonality personality = AgentPersonality.Balanced)
    {
        PlayerSide side = humanSide switch
        {
            PlayerSide.Corp => PlayerSide.Runner,
            PlayerSide.Runner => PlayerSide.Corp,
            _ => throw new ArgumentOutOfRangeException(nameof(humanSide)),
        };
        ulong seed = AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, side);
        OfflineAgentConfiguration configuration = OfflinePolicies.Configure(side, difficulty, seed, policyVersion);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        if (side == PlayerSide.Corp && CorpPolicyProfiles.SupportsArchetypes(configuration.PolicyVersion))
        {
            (CorpArchetype primary, CorpArchetype secondary) = CorpDeckArchetypes(definition.Request.CorpDeck, catalog);
            configuration = CorpPolicyProfiles.WithArchetypes(configuration, primary, secondary);
        }
        if (side == PlayerSide.Runner && RunnerPolicyProfiles.SupportsArchetypes(configuration.PolicyVersion))
        {
            (RunnerArchetype primary, RunnerArchetype secondary) = RunnerDeckArchetypes(definition.Request.RunnerDeck, catalog);
            configuration = RunnerPolicyProfiles.WithArchetypes(configuration, primary, secondary);
        }
        if (personality != AgentPersonality.Balanced)
            configuration = side == PlayerSide.Corp
                ? CorpPolicyProfiles.WithPersonality(configuration, personality)
                : RunnerPolicyProfiles.WithPersonality(configuration, personality);
        return new(configuration, OfflinePolicies.Create(configuration), catalog);
    }

    /// <summary>The Runner deck's archetypes (A8): declared by its preset, otherwise inferred from its cards.</summary>
    public static (RunnerArchetype Primary, RunnerArchetype Secondary) RunnerDeckArchetypes(DeckRequest deck, ICardEvaluationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(catalog);
        if (deck.PresetId is { } id && OfficialPresetCatalog.Embedded().TryGet(id, out OfficialPreset preset) && preset.Archetypes.Count > 0)
            return (RunnerArchetypes.Keys[preset.Archetypes[0]],
                preset.Archetypes.Count > 1 ? RunnerArchetypes.Keys[preset.Archetypes[1]] : RunnerArchetype.Generic);
        return RunnerArchetypes.Infer(deck.Cards
            .Select(entry => (Found: catalog.TryGet(new CardDefinitionId(entry.Code), out CardEvaluationDescriptor card), Card: card, entry.Quantity))
            .Where(entry => entry.Found).Select(entry => (entry.Card, entry.Quantity)));
    }

    /// <summary>
    /// The Corp deck's archetypes (A8): its preset's declaration when it has one,
    /// otherwise inferred from its cards. The bot knows its own deck.
    /// </summary>
    public static (CorpArchetype Primary, CorpArchetype Secondary) CorpDeckArchetypes(DeckRequest deck, ICardEvaluationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(catalog);
        if (deck.PresetId is { } id && OfficialPresetCatalog.Embedded().TryGet(id, out OfficialPreset preset) && preset.Archetypes.Count > 0)
            return (CorpArchetypes.Keys[preset.Archetypes[0]],
                preset.Archetypes.Count > 1 ? CorpArchetypes.Keys[preset.Archetypes[1]] : CorpArchetype.Generic);
        return CorpArchetypes.Infer(deck.Cards
            .Select(entry => (Found: catalog.TryGet(new CardDefinitionId(entry.Code), out CardEvaluationDescriptor card), Card: card, entry.Quantity))
            .Where(entry => entry.Found).Select(entry => (entry.Card, entry.Quantity)));
    }
}
