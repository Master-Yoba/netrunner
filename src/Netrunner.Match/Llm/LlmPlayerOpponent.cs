using Netrunner.AI;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match.Review;

namespace Netrunner.Match.Llm;

/// <summary>
/// The LLM opponent a player meets in the client: the player's Standard bot with a local model making its strategic
/// choices, at the settings the latency and override sweeps chose (docs/LLM_OPPONENT.md). <c>ai-llm-play</c> starts
/// from the same settings.
/// </summary>
public static class LlmPlayerOpponent
{
    /// <summary>Reasoning effort low with a 400-token thinking budget: more thinking won no more games.</summary>
    public static LlmSampling Sampling { get; } = new(0.6, 0.95, 20, null, "low", ThinkingBudget: 400);

    /// <summary>No lookups (the rules summary instead), structured answers, deferring to the bot, and a 30 s deadline.</summary>
    public static ModelDeciderSettings Settings { get; } =
        new(MaxToolCalls: 0, CardText: false, StructuredAnswer: true, Deadline: TimeSpan.FromSeconds(30), Defer: true);

    /// <summary>The model chooses among the bot's best three options.</summary>
    public const int Shortlist = 3;

    /// <summary>How long one request to the server may take; past the deadline the bot's choice stands anyway.</summary>
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(45);

    /// <summary>A new opponent for a player's game: their Standard bot (with its deck archetypes) and the model.</summary>
    public static LlmOpponentAgent Create(FullMatchDefinition definition, PlayerSide humanSide, ILlmChatClient client, RulesIndex? rules = null) =>
        Restore(OfflineMatchFactory.CreatePlayerOpponent(definition, humanSide), definition, client, rules);

    /// <summary>The opponent around a bot restored from a save, which keeps only the bot.</summary>
    public static LlmOpponentAgent Restore(OfflineAgent bot, FullMatchDefinition definition, ILlmChatClient client, RulesIndex? rules = null) =>
        LlmOpponentAgent.Wrap(bot, definition, new ModelDecider(client, Sampling, rules, Settings), Shortlist);
}
