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
/// <param name="Labelled">At most this many candidate groups at a root, the heuristic's best first; 0 keeps every meaningful
/// group (S1.4): every group whose priority band is within one of the best group's.</param>
/// <param name="CloseShare">Two groups are close when their priorities are equal and their scores within this share of the best.</param>
/// <param name="Seed">The draw's seed.</param>
public sealed record RegretSampling(int TurnRoots = 7, int RunRoots = 2, int Labelled = 0, double CloseShare = 0.2, ulong Seed = 1);

/// <summary>Draws a game's decision roots, judging candidates by the latest Standard heuristic of the acting side.</summary>
public static partial class RegretRoots
{
    public const string TurnKind = "turn";
    public const string RunKind = "run";
    /// <summary>Any other decision with options (a finding's nested choice: what to break, discard or let fire).</summary>
    public const string ChoiceKind = "choice";

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
            if (Root(game, definition, position, agents, sampling, finding: false) is { } root) found.Add(root);
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
        var definitions = Definitions(state);
        string Choice(LegalAction action) => Parameters(definitions, action);
        return [.. pending.LegalActions
            .GroupBy(action => (action.Kind, Choice: Choice(action)))
            .Select(group => new RegretCandidate($"{group.Key.Kind}|{Short(group.Key.Choice)}", group.Key.Kind,
                $"{group.First().Description} {Truncate(group.Key.Choice, 200)}", [.. group.Select(action => action.Id.Value).Order(StringComparer.Ordinal)]))
            .OrderBy(candidate => candidate.Group, StringComparer.Ordinal)];
    }

    /// <summary>An action's parameters as JSON with every card instance read as its definition (<c>def:ID</c>).</summary>
    public static string Parameters(GameState state, LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        return Parameters(Definitions(state), action);
    }

    private static Dictionary<string, string> Definitions(GameState state) =>
        state.Cards.ToDictionary(card => card.Id.Value, card => card.DefinitionId.Value, StringComparer.Ordinal);

    private static string Parameters(Dictionary<string, string> definitions, LegalAction action) =>
        CardInstance().Replace(EngineJson.Serialize(action.Parameters),
            match => definitions.TryGetValue(match.Value, out string? definition) ? "def:" + definition : match.Value);

    [GeneratedRegex(@"card:\d+", RegexOptions.CultureInvariant)]
    private static partial Regex CardInstance();

    private static string Short(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    /// <summary>
    /// The roots of a game with every meaningful group (S1.4): each root keeps its groups, in their order, and gains those of
    /// the heuristic's meaningful groups it lacked, after them. Labels of the groups it had stay valid.
    /// </summary>
    public static ImmutableArray<RegretRoot> Extend(RegretGame game, IReadOnlyList<RegretRoot> roots, RegretSampling sampling)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(sampling);
        var bySequence = roots.ToDictionary(root => root.Sequence);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var agents = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = Judge(definition, PlayerSide.Corp),
            [PlayerSide.Runner] = Judge(definition, PlayerSide.Runner),
        };
        var extended = new Dictionary<long, RegretRoot>();
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, position.State, definition.Behavior);
            if (!bySequence.TryGetValue(position.Sequence, out RegretRoot? root)) return;
            RegretRoot? meaningful = Root(game, definition, position, agents, sampling, finding: true);
            HashSet<string> had = [.. root.Candidates.Select(candidate => candidate.Group)];
            extended[position.Sequence] = meaningful is null ? root
                : root with { Candidates = [.. root.Candidates, .. meaningful.Candidates.Where(candidate => !had.Contains(candidate.Group))] };
        });
        return [.. roots.Select(root => extended.GetValueOrDefault(root.Sequence, root))];
    }

    /// <summary>
    /// The roots at given decisions of a game, whatever their kind (the loops' and strategy work's findings): every
    /// decision with at least two distinct choices, its groups ranked and cut as a drawn root's are.
    /// </summary>
    public static ImmutableArray<RegretRoot> At(RegretGame game, IReadOnlySet<long> sequences, RegretSampling sampling,
        IReadOnlyList<string> categories)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(sampling);
        ArgumentNullException.ThrowIfNull(categories);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var agents = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = Judge(definition, PlayerSide.Corp),
            [PlayerSide.Runner] = Judge(definition, PlayerSide.Runner),
        };
        var found = ImmutableArray.CreateBuilder<RegretRoot>();
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, position.State, definition.Behavior);
            if (sequences.Contains(position.Sequence) && Root(game, definition, position, agents, sampling, finding: true) is { } root)
                found.Add(root with { Categories = [.. root.Categories, .. categories] });
        });
        return found.ToImmutable();
    }

    /// <summary>The category prefix that records a version's choice at a targeted root (S1.6): <c>diverge:VERSION=GROUP</c>.</summary>
    public const string DivergeCategory = "diverge";

    /// <summary>
    /// Bot plan S1.6, targeted roots: every turn-action and run-choice decision of a game where two versions of one side
    /// (same profile), both having watched the game, choose different groups. Each root has the latest Standard heuristic's
    /// meaningful groups and both chosen groups, and records the two choices in its categories
    /// (<see cref="DivergeCategory"/>), so labelling can keep them and the report can name them.
    /// </summary>
    /// <param name="agent">Makes the agent of a version for a side.</param>
    public static ImmutableArray<RegretRoot> Diverge(RegretGame game, string first, string second, PlayerSide side,
        Func<FullMatchDefinition, PlayerSide, string, OfflineAgent> agent, RegretSampling sampling)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(sampling);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var judges = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = Judge(definition, PlayerSide.Corp),
            [PlayerSide.Runner] = Judge(definition, PlayerSide.Runner),
        };
        OfflineAgent one = agent(definition, side, first), other = agent(definition, side, second);
        var found = ImmutableArray.CreateBuilder<RegretRoot>();
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent watcher in judges.Values.Append(one).Append(other))
                AgentLifecycleHost.Observe(watcher, position.State, definition.Behavior);
            GameState state = position.State;
            PendingDecision pending = state.PendingDecision!;
            if (state.GetPlayer(pending.Owner).Side != side || KindOf(state, pending, side) is null) return;
            IReadOnlyList<RegretCandidate> groups = Group(state, pending);
            if (groups.Count < 2) return;
            string Chosen(OfflineAgent chooser)
            {
                ActionId action = chooser.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime))
                    .AsTask().GetAwaiter().GetResult();
                chooser.DiscardDecision();
                return groups.First(group => group.ActionIds.Contains(action.Value)).Group;
            }
            string a = Chosen(one), b = Chosen(other);
            if (a == b || Root(game, definition, position, judges, sampling, finding: true) is not { } root) return;
            // Both choices are labelled, whatever the heuristic's band makes of them.
            RegretCandidate[] missing = [.. groups.Where(group => (group.Group == a || group.Group == b)
                && root.Candidates.All(candidate => candidate.Group != group.Group))];
            found.Add(root with
            {
                Candidates = [.. root.Candidates, .. missing],
                Categories = [.. root.Categories, DivergeCategory, $"{DivergeCategory}:{first}={a}", $"{DivergeCategory}:{second}={b}"],
            });
        });
        return found.ToImmutable();
    }

    /// <summary>The groups the versions chose at a targeted root (S1.6), from its categories; empty for a drawn root.</summary>
    public static IReadOnlySet<string> DivergeChoices(RegretRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.Categories.Where(category => category.StartsWith(DivergeCategory + ":", StringComparison.Ordinal))
            .Select(category => category[(category.IndexOf('=', StringComparison.Ordinal) + 1)..]).ToHashSet(StringComparer.Ordinal);
    }

    private static RegretRoot? Root(RegretGame game, FullMatchDefinition definition, RegretPosition position,
        Dictionary<PlayerSide, OfflineAgent> agents, RegretSampling sampling, bool finding)
    {
        GameState state = position.State;
        PendingDecision pending = state.PendingDecision!;
        PlayerSide side = state.GetPlayer(pending.Owner).Side;
        if ((KindOf(state, pending, side) ?? (finding ? ChoiceKind : null)) is not { } kind) return null;
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
        if (kind == TurnKind && !finding)
        {
            AgentUtility first = Best(ranked[0], utilities), second = Best(ranked[1], utilities);
            bool close = first.Priority == second.Priority
                && (long)first.Score - second.Score <= Math.Max(1, Math.Abs((long)first.Score) * sampling.CloseShare);
            if (!close) return null;
        }
        return new RegretRoot($"{game.Id}#{position.Sequence}", game.Id, position.Sequence, side, kind, game.Split,
            [kind, side == PlayerSide.Corp ? "corp" : "runner"], [.. Meaningful(ranked, utilities, sampling.Labelled)]);
    }

    /// <summary>
    /// The groups worth labelling, in the heuristic's order (S1.4): those whose priority band is within one of the best
    /// group's (a discouraged action next to ordinary ones, not a vetoed one), cut to <paramref name="cap"/> when it is set.
    /// </summary>
    internal static IEnumerable<RegretCandidate> Meaningful(IReadOnlyList<RegretCandidate> ranked, Dictionary<string, AgentUtility> utilities, int cap)
    {
        int best = PlannerPriorities.Band(Best(ranked[0], utilities).Priority);
        IEnumerable<RegretCandidate> kept = ranked.Where(group => PlannerPriorities.Band(Best(group, utilities).Priority) >= best - 1);
        return cap > 0 ? kept.Take(cap) : kept;
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
