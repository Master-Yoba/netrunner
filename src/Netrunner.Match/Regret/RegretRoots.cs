using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match.Regret;

/// <summary>How roots are drawn from a game (bot plan S1.1).</summary>
/// <param name="TurnRoots">Turn-action roots kept per game, drawn from those whose two best candidate groups are close.</param>
/// <param name="RunRoots">Run-choice roots kept per game (continue or jack out, trash or not, rez or not).</param>
/// <param name="Labelled">Candidate groups the teacher labels at a root, the heuristic's best first.</param>
/// <param name="CloseShare">Two groups are close when their priorities are equal and their scores within this share of the best.</param>
/// <param name="Seed">The draw's seed.</param>
public sealed record RegretSampling(int TurnRoots = 7, int RunRoots = 2, int Labelled = 6, double CloseShare = 0.2, ulong Seed = 1);

/// <summary>Draws a game's decision roots, judging candidates by the latest Standard heuristic of the acting side.</summary>
public static partial class RegretRoots
{
    public const string TurnKind = "turn";
    public const string RunKind = "run";

    public static ImmutableArray<RegretRoot> Sample(RegretGame game, RegretSampling sampling)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(sampling);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var agents = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = Judge(definition, PlayerSide.Corp),
            [PlayerSide.Runner] = Judge(definition, PlayerSide.Runner),
        };
        var found = new List<RegretRoot>();
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, position.State, definition.Behavior);
            if (Root(game, definition, position, agents, sampling) is { } root) found.Add(root);
        });
        var random = new DeterministicRandom(sampling.Seed ^ StableHash(game.Id));
        return [.. Draw(found.Where(root => root.Kind == TurnKind).ToList(), sampling.TurnRoots, random)
            .Concat(Draw(found.Where(root => root.Kind == RunKind).ToList(), sampling.RunRoots, random))
            .OrderBy(root => root.Sequence)];
    }

    /// <summary>The kind of root a decision is, or null when it is neither a turn action nor a searched run choice.</summary>
    public static string? KindOf(GameState state, PendingDecision pending, PlayerSide side) =>
        pending.Kind == DecisionKind.TurnAction && state.RunState is null ? TurnKind
        : state.RunState is not null && pending.LegalActions.Any(action => side == PlayerSide.Runner
            ? action.Kind is ActionKind.JackOut or ActionKind.TrashAccessedCard
            : action.Kind is ActionKind.RezCard or ActionKind.RezIce) ? RunKind
        : null;

    /// <summary>
    /// The decision's legal actions grouped into distinct choices. Two actions are one choice when their kind and their
    /// parameters agree once every card instance is replaced by its definition: copies of one card installed in one place
    /// are one choice, different cards or places are not.
    /// </summary>
    public static IReadOnlyList<RegretCandidate> Group(GameState state, PendingDecision pending)
    {
        var definitions = state.Cards.ToDictionary(card => card.Id.Value, card => card.DefinitionId.Value, StringComparer.Ordinal);
        string Choice(LegalAction action) => CardInstance().Replace(EngineJson.Serialize(action.Parameters),
            match => definitions.TryGetValue(match.Value, out string? definition) ? "def:" + definition : match.Value);
        return [.. pending.LegalActions
            .GroupBy(action => (action.Kind, Choice: Choice(action)))
            .Select(group => new RegretCandidate($"{group.Key.Kind}|{Short(group.Key.Choice)}", group.Key.Kind,
                $"{group.First().Description} {Truncate(group.Key.Choice, 200)}", [.. group.Select(action => action.Id.Value).Order(StringComparer.Ordinal)]))
            .OrderBy(candidate => candidate.Group, StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"card:\d+", RegexOptions.CultureInvariant)]
    private static partial Regex CardInstance();

    private static string Short(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    private static RegretRoot? Root(RegretGame game, FullMatchDefinition definition, RegretPosition position,
        Dictionary<PlayerSide, OfflineAgent> agents, RegretSampling sampling)
    {
        GameState state = position.State;
        PendingDecision pending = state.PendingDecision!;
        PlayerSide side = state.GetPlayer(pending.Owner).Side;
        if (KindOf(state, pending, side) is not { } kind) return null;
        IReadOnlyList<RegretCandidate> groups = Group(state, pending);
        if (groups.Count < 2) return null;
        OfflineAgent agent = agents[side];
        agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime)).AsTask().GetAwaiter().GetResult();
        ImmutableArray<AgentCandidate> evaluated = agent.LastEvaluation!.Candidates;
        agent.DiscardDecision();
        var utilities = evaluated.ToDictionary(candidate => candidate.ActionId.Value, candidate => candidate.Utility, StringComparer.Ordinal);
        RegretCandidate[] ranked = [.. groups
            .Select(group => (Group: group, Utility: group.ActionIds.Select(id => utilities[id])
                .OrderByDescending(utility => utility.Priority).ThenByDescending(utility => utility.Score).First()))
            .OrderByDescending(entry => entry.Utility.Priority).ThenByDescending(entry => entry.Utility.Score)
            .ThenBy(entry => entry.Group.Group, StringComparer.Ordinal)
            .Select(entry => entry.Group)];
        if (kind == TurnKind)
        {
            AgentUtility first = Best(ranked[0], utilities), second = Best(ranked[1], utilities);
            bool close = first.Priority == second.Priority
                && (long)first.Score - second.Score <= Math.Max(1, Math.Abs((long)first.Score) * sampling.CloseShare);
            if (!close) return null;
        }
        return new RegretRoot($"{game.Id}#{position.Sequence}", game.Id, position.Sequence, side, kind, game.Split,
            [kind, side == PlayerSide.Corp ? "corp" : "runner"], [.. ranked.Take(sampling.Labelled)]);
    }

    private static AgentUtility Best(RegretCandidate group, Dictionary<string, AgentUtility> utilities) => group.ActionIds
        .Select(id => utilities[id]).OrderByDescending(utility => utility.Priority).ThenByDescending(utility => utility.Score).First();

    private static IEnumerable<RegretRoot> Draw(List<RegretRoot> roots, int count, DeterministicRandom random)
    {
        for (int index = roots.Count - 1; index > 0; index--)
        {
            int other = random.NextInt(index + 1);
            (roots[index], roots[other]) = (roots[other], roots[index]);
        }
        return roots.Take(count);
    }

    /// <summary>The latest Standard bot of a side, keeping its evaluation of every candidate.</summary>
    private static OfflineAgent Judge(FullMatchDefinition definition, PlayerSide side)
    {
        OfflineAgent standard = OfflineMatchFactory.CreateOpponent(definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp);
        return new OfflineAgent(standard.Configuration, OfflinePolicies.Create(standard.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), null, captureDiagnostics: true);
    }

    /// <summary>A hash of a string that is the same on every machine and run (FNV-1a).</summary>
    internal static ulong StableHash(string value)
    {
        ulong hash = 14695981039346656037;
        foreach (char character in value) hash = (hash ^ character) * 1099511628211;
        return hash;
    }
}
