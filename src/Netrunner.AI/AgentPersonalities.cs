using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>A deliberate play style on top of a difficulty (AI plan A6). Each belongs to one side.</summary>
public enum AgentPersonality
{
    Balanced = 0,
    /// <summary>Corp: taxing ICE, few remotes, scores only behind a full server.</summary>
    Glacier = 1,
    /// <summary>Corp: scores early behind cheap ICE, before the Runner sets up.</summary>
    Rush = 2,
    /// <summary>Corp: many remotes with economy and assets.</summary>
    AssetSpam = 3,
    /// <summary>Runner: runs early and often.</summary>
    Aggressive = 4,
    /// <summary>Runner: builds its rig and economy before it presses.</summary>
    RigBuilder = 5,
}

/// <summary>
/// A6 personalities. A personality isn't a separate bot: it leads the plan-level traits the heuristic reads
/// (the archetype profile, with the deck's own archetype second) and shifts a few named terms of the fitted
/// evaluation the planner scores rollouts with (score-race urgency, protection margin, run pressure, economy),
/// so the planner plays the style instead of planning it away.
/// </summary>
public static class AgentPersonalities
{
    public const string ParameterKey = "personality";

    /// <summary>How much the deck's own archetype still counts against the personality's.</summary>
    public const double DeckWeight = 0.3;

    public static IReadOnlyDictionary<string, AgentPersonality> Keys { get; } = new Dictionary<string, AgentPersonality>(StringComparer.Ordinal)
    {
        ["balanced"] = AgentPersonality.Balanced,
        ["glacier"] = AgentPersonality.Glacier,
        ["rush"] = AgentPersonality.Rush,
        ["asset-spam"] = AgentPersonality.AssetSpam,
        ["aggressive"] = AgentPersonality.Aggressive,
        ["rig-builder"] = AgentPersonality.RigBuilder,
    };

    public static string Key(AgentPersonality personality) => Keys.First(pair => pair.Value == personality).Key;

    /// <summary>The side a personality plays; Balanced plays either.</summary>
    public static PlayerSide? Side(AgentPersonality personality) => personality switch
    {
        AgentPersonality.Glacier or AgentPersonality.Rush or AgentPersonality.AssetSpam => PlayerSide.Corp,
        AgentPersonality.Aggressive or AgentPersonality.RigBuilder => PlayerSide.Runner,
        _ => null,
    };

    /// <summary>The personalities a side can play, Balanced first.</summary>
    public static IReadOnlyList<AgentPersonality> For(PlayerSide side) =>
        [AgentPersonality.Balanced, .. Enum.GetValues<AgentPersonality>().Where(personality => Side(personality) == side)];

    public static bool IsValid(PlayerSide side, int value) =>
        Enum.IsDefined((AgentPersonality)value) && (Side((AgentPersonality)value) is not { } owner || owner == side);

    public static AgentPersonality Of(OfflineAgentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return (AgentPersonality)configuration.Parameters.GetValueOrDefault(ParameterKey);
    }

    /// <summary>The archetype whose traits a Corp personality leads with.</summary>
    public static CorpArchetype? CorpArchetypeOf(AgentPersonality personality) => personality switch
    {
        AgentPersonality.Glacier => CorpArchetype.Glacier,
        AgentPersonality.Rush => CorpArchetype.Rush,
        AgentPersonality.AssetSpam => CorpArchetype.Horizontal,
        _ => null,
    };

    /// <summary>
    /// The traits a Corp personality leads with: its archetype's, except that asset spam values assets and remotes
    /// more than a horizontal deck does (with Horizontal's own traits it installed 3.3 assets a game to Standard's 3.2).
    /// </summary>
    public static CorpArchetypeProfile? CorpProfileOf(AgentPersonality personality) => CorpArchetypeOf(personality) switch
    {
        CorpArchetype.Horizontal => CorpArchetypes.Profile(CorpArchetype.Horizontal) with { AssetBonus = 2.5, MaxRemotes = 5 },
        { } archetype => CorpArchetypes.Profile(archetype),
        null => null,
    };

    /// <summary>The archetype whose traits a Runner personality leads with.</summary>
    public static RunnerArchetype? RunnerArchetypeOf(AgentPersonality personality) => personality switch
    {
        AgentPersonality.Aggressive => RunnerArchetype.Aggro,
        AgentPersonality.RigBuilder => RunnerArchetype.BigRig,
        _ => null,
    };

    /// <summary>
    /// The personality's shift of a rollout's fitted score, in thousandths of log-odds, from the evaluation's
    /// features (feature set 2 or 3). A unit of each named term is worth this much more to the personality.
    /// </summary>
    public static long Shift(AgentPersonality personality, IReadOnlyList<double> features, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(names);
        (string Term, double PerUnit)[] terms = personality switch
        {
            // Protection margin: ICE, rezzed ICE and what a run on the best remote costs.
            AgentPersonality.Glacier => [("ice-installed", 120), ("ice-rezzed", 80), ("remote-cost", 40), ("occupied-remotes", -150)],
            // Score-race urgency: points and progress now are worth more than protection.
            AgentPersonality.Rush => [("agenda-self", 300), ("own-agenda-progress", 300), ("ice-installed", -60)],
            // Economy and board width.
            AgentPersonality.AssetSpam => [("occupied-remotes", 400), ("money-self", 200)],
            // Run pressure: points taken and open servers.
            AgentPersonality.Aggressive => [("agenda-self", 300), ("hq-open", 100), ("rd-open", 100), ("rig-value", -150)],
            // Setup: breaker coverage, rig and money.
            AgentPersonality.RigBuilder => [("breaker-types", 250), ("rig-value", 400), ("money-self", 150)],
            _ => [],
        };
        double shift = 0;
        foreach ((string term, double perUnit) in terms)
        {
            int index = IndexOf(names, term);
            if (index >= 0) shift += features[index] * perUnit;
        }
        return (long)Math.Round(shift);
    }

    private static int IndexOf(IReadOnlyList<string> names, string term)
    {
        for (int index = 0; index < names.Count; index++)
            if (names[index] == term) return index;
        return -1;
    }
}
